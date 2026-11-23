using System.Text.Json;
using Azure.Core;
using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Infrastructure.Extensions;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Checks;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Interfaces;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;
using GovUK.Dfe.AI.Agents.Exceptions;
using GovUK.Dfe.AI.Agents.Extensibility.Interfaces;
using GovUK.Dfe.AI.Agents.Quality;
using GovUK.Dfe.AI.Agents.Quality.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests.QualityGate;

/// <summary>
/// The briefing tool's release decision, end to end, as the gate command makes it: the app's own settings, policy,
/// test cases and briefing builder, with only Foundry faked. The agent answers each case with exactly the facts it
/// must mention, so these tests show that the test cases alone decide the release: no reviews need recording, and a
/// leak of personal data blocks it however well the briefings score.
/// </summary>
public sealed class BriefingQualityGateTests : IDisposable
{
    // "Rise concern with pupil personal data" in privacy-and-security.json.
    private const string PrivacyCase = "evaluation/privacy-and-security/PRIV-001";

    private readonly string _root = Directory.CreateTempSubdirectory("briefing-quality-gate-").FullName;
    private readonly IAgentService _agents = Substitute.For<IAgentService>();
    private IReadOnlyList<AgentTestCase> _cases = [];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permits_release_when_every_briefing_is_right_with_no_reviews_recorded()
    {
        var report = await DecideAsync();

        Assert.True(report.ReleasePermitted, string.Join(Environment.NewLine, report.Agents.SelectMany(a => a.Reasons)));
        Assert.Equal((ReleaseDecision.Pass, 0), (report.Decision, report.ExitCode));
        Assert.All(report.Agents[0].Principles, principle => Assert.Equal(ReleaseDecision.Pass, principle.Decision));
        Assert.DoesNotContain(report.Agents[0].Findings, f => f.Result.CheckId == QualityCheckIds.Attestation && f.Result.NotMet);
    }

    [Fact]
    public async Task Blocks_release_when_a_briefing_repeats_a_pupils_personal_data_however_well_it_scores()
    {
        var report = await DecideAsync(leakPersonalData: true);

        Assert.Equal(ReleaseDecision.Fail, report.Decision);
        var leak = Assert.Single(report.Agents[0].Findings, f => f.Result.CheckId == QualityCheckIds.SensitiveDataLeakage && f.Result.NotMet);
        Assert.Equal((CheckSeverity.Critical, FindingDisposition.Blocking, PrivacyCase), (leak.Result.Severity, leak.Disposition, leak.Result.Subject));
        // The case planted the UPN in the evidence ("sensitiveData"), so the gate knows it mustn't appear.
        Assert.Contains("Planted value: A9***********", leak.Result.Evidence);
    }

    [Fact]
    public async Task Blocks_release_when_any_briefing_has_text_the_settings_ban_from_every_briefing()
    {
        // "As an AI" is in ReleaseGate:MustNotMention, not in any case file.
        var report = await DecideAsync(addToEveryBriefing: "As an AI, I can confirm this [Evidence 1].");

        Assert.Equal(ReleaseDecision.Fail, report.Decision);
        var banned = report.Agents[0].Findings.Where(f => f.Result.CheckId == QualityCheckIds.Scenario && f.Result.NotMet).ToList();
        Assert.Equal(_cases.Count(c => !c.GuardrailMustBlock), banned.Count);
        Assert.All(banned, f => Assert.Contains("Mentions \"As an AI\"", f.Result.Summary, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Never_gives_the_agent_a_cases_Rise_or_Recast_concerns_as_no_briefing_reports_them()
    {
        await DecideAsync();

        // FAIR-001's Recast concern ("Pupil places") and PRIV-001's pupil in a Rise concern are in their cases' records.
        Assert.DoesNotContain("Pupil places", EvidenceOf("FAIR-001"), StringComparison.Ordinal);
        Assert.DoesNotContain("Casey Madeup", EvidenceOf("PRIV-001"), StringComparison.Ordinal);
        Assert.All(_cases, c => Assert.DoesNotContain("concerns ===", c.Evidence ?? string.Empty, StringComparison.Ordinal));

        string EvidenceOf(string id) => _cases.Single(c => c.Name.EndsWith(id, StringComparison.Ordinal)).Evidence ?? string.Empty;
    }

    /// <summary>The gate command's host, with Foundry faked: agents, judge, guardrail check and clean-up.</summary>
    private async Task<QualityGateReport> DecideAsync(bool leakPersonalData = false, string? addToEveryBriefing = null)
    {
        var prepared = Path.Combine(_root, "prepared");
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory, DisableDefaults = true });
        builder.Configuration
            .AddSharedSettings()
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiAgents:Foundry:Endpoint"] = "https://example.services.ai.azure.com/api/projects/briefings",
                ["AiAgents:Foundry:DefaultModel"] = "myconnection/gpt-5.1",
                ["AiAgents:AISearch:Endpoint"] = "https://example.search.windows.net",
                // The shared settings leave the search's own identity empty for user secrets; never used here.
                ["AiAgents:AISearch:Authentication:TenantId"] = "00000000-0000-0000-0000-000000000000",
                ["AiAgents:AISearch:Authentication:ClientId"] = "00000000-0000-0000-0000-000000000000",
                ["AiAgents:AISearch:Authentication:ClientSecret"] = "not-a-secret",
                ["AiAgents:Evaluation:ReleaseGate:TestCasesDirectory"] = prepared,
                ["AiAgents:Evaluation:ReleaseGate:ReportsDirectory"] = Path.Combine(_root, "reports"),
                ["AiAgents:Evaluation:ReleaseGate:PublishToFoundry"] = "false",
                ["AiAgents:Evaluation:ReleaseGate:Repeats"] = "1",
            });

        // As Program.cs does, with a stand-in credential.
        builder.Services.AddInfrastructure(builder.Configuration, agents => agents
            .UseCredential(Substitute.For<TokenCredential>())
            .AddResponsibleAiQualityGate());

        var judge = Substitute.For<IAgentRunEvaluator>();
        judge.EvaluateAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new Dictionary<string, double> { ["Groundedness"] = 4.5, ["Relevance"] = 4.5 });
        var guardrail = Substitute.For<IGuardrailCheck>();
        guardrail.Name.Returns("Foundry guardrail briefing-tool-guardrail");
        guardrail.CheckAsync(Arg.Any<CancellationToken>()).Returns([]);
        builder.Services.AddSingleton(_agents).AddSingleton(Substitute.For<IAgentRuntimeService>())
            .AddSingleton(judge).AddSingleton(guardrail);

        using var host = builder.Build();
        await TestCasePreparer.PrepareAsync(host.Services, prepared, Token);
        _cases = await AgentTestCase.LoadAsync(Path.Combine(prepared, "briefing-agent"), Token);
        AnswerEveryCase(_cases, leakPersonalData, addToEveryBriefing);

        return await host.Services.GetRequiredService<IResponsibleAiQualityGate>().DecideReleaseAsync(
            Application.Providers.BriefingAgents.All, Token);
    }

    /// <summary>
    /// A stand-in for the briefing agent: blocked on every attack, as the guardrail would, and otherwise a briefing of
    /// exactly the facts the case must mention, each cited, in plain sentences.
    /// </summary>
    private void AnswerEveryCase(IReadOnlyList<AgentTestCase> cases, bool leakPersonalData, string? addToEveryBriefing)
    {
        var byEvidence = cases.GroupBy(c => c.Evidence ?? string.Empty).ToDictionary(g => g.Key, g => g.First());
        _agents.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(call =>
        {
            var definition = call.Arg<AgentDefinition>();
            var testCase = byEvidence[call.Arg<AgentEvidence?>()?.Text ?? string.Empty];
            if (testCase.GuardrailMustBlock)
            {
                throw new AgentGuardrailException(definition.Name, AgentGuardrailException.PromptStage);
            }

            // A heading has a fact under it, or the app takes it out as an empty section, as it would for a user.
            var lines = testCase.MustMention.Select(fact => fact.StartsWith('#') ? $"{fact}\n\nThe records give this [Evidence 1].\n" : $"It says {fact} [Evidence 1].").ToList();
            if (addToEveryBriefing is not null)
            {
                lines.Add(addToEveryBriefing);
            }

            if (leakPersonalData && testCase.Name == PrivacyCase)
            {
                lines.Add("It was raised for the pupil with UPN A900001250017 [Evidence 1].");
            }
            lines.Add("{{city: Exampleton}}");
            return new AgentResult { AgentName = definition.Name, Output = string.Join("\n", lines) };
        });
    }
}
