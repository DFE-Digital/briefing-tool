using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Application.Providers;
using Xunit;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Scenarios;
using GovUK.Dfe.AI.Agents.Options;
using GovUK.Dfe.AI.Agents.Quality;
using Microsoft.Extensions.Configuration;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests.TestCases;

/// <summary>The release gate's real test case files, checked against the conventions every app's cases follow.</summary>
public sealed class TestCaseFileTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestCases");

    public static TheoryData<string> Agents => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order()];

    [Fact]
    public void Ships_test_cases_only_for_briefing_agents() =>
        Assert.Subset(BriefingAgents.All.Select(agent => agent.Name).ToHashSet(),
            Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().ToHashSet());

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_loads_and_follows_the_release_conventions(string agent)
    {
        var cases = await LoadAsync(agent);

        // The release gate fails a case that doesn't; checked here too, so a wrong file fails the build, not a release.
        Assert.NotEmpty(cases);
        Assert.Empty(cases.SelectMany(ReleaseSections.Problems));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_file_splits_its_cases_into_evaluation_and_guardrail(string agent)
    {
        var source = Path.Combine(Root, agent);

        // One file per principle, straight in the agent's folder, its cases in the two sections only.
        Assert.Empty(Directory.GetDirectories(source));
        foreach (var file in Directory.GetFiles(source, "*.json"))
        {
            var testsFile = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!.AsObject();
            Assert.False(testsFile.ContainsKey("tests"), $"{Path.GetFileName(file)}: use \"evaluation\" and \"guardrail\", not \"tests\".");
            Assert.True(testsFile.ContainsKey("evaluation") || testsFile.ContainsKey("guardrail"), Path.GetFileName(file));

            // The section says whether it's an attack, so no case repeats it.
            Assert.All(new[] { "evaluation", "guardrail" }.SelectMany(section => testsFile[section]?.AsArray() ?? []),
                test => Assert.False(test!.AsObject().ContainsKey("guardrailMustBlock"), $"{Path.GetFileName(file)} {test["id"]}"));
        }

        // And the library names them by their section: guardrail/<type>/<id> or evaluation/<type>/<id>.
        Assert.All(await LoadAsync(agent), testCase =>
            Assert.Equal(testCase.GuardrailMustBlock ? "guardrail" : "evaluation", testCase.Name.Split('/')[0]));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_names_its_responsible_ai_principle_risk_and_pass_condition(string agent)
    {
        var cases = await LoadAsync(agent);

        // The quality gate's own convention check (RequireScenarioMetadata), so a vague case fails the build.
        Assert.Empty(cases.SelectMany(testCase => ScenarioMetadata.Problems(testCase, requireMetadata: true)));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_user_group_the_policy_requires_has_enough_cases_to_compare(string agent)
    {
        var policy = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build()
            .GetSection("AiAgents:Evaluation:QualityGate");
        var minimum = policy.GetValue<int>("MinimumCasesPerGroup");
        var cases = await LoadAsync(agent);

        Assert.All(policy.GetSection("RequiredGroups").Get<string[]>() ?? [], group =>
            Assert.True(cases.Count(c => c.Group == group) >= minimum, $"\"{group}\" needs at least {minimum} evaluation cases."));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Tests_every_principle_its_cases_can_evidence(string agent)
    {
        var principles = (await LoadAsync(agent)).Select(testCase => ScenarioMetadata.For(testCase).Principle).ToHashSet();

        // Accountability is evidenced by the gate itself: traceability, test case conventions and attestations.
        Assert.Subset(principles, Enum.GetValues<ResponsibleAiPrinciple>().Where(p => p != ResponsibleAiPrinciple.Accountability).ToHashSet());
    }

    /// <summary>
    /// The agent's cases as the library loads them. The app builds each prompt from the case's "subject" before the gate
    /// runs (TestCasePreparer), so a stand-in prompt is given here: the conventions don't depend on it.
    /// </summary>
    private static async Task<IReadOnlyList<AgentTestCase>> LoadAsync(string agent)
    {
        var source = Path.Combine(Root, agent);
        var copy = Directory.CreateTempSubdirectory("briefing-test-cases-");
        try
        {
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var testsFile = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!.AsObject();
                // A question attack keeps its own prompt; every other case's is built from its subject.
                foreach (var testCase in new[] { "evaluation", "guardrail" }.SelectMany(section => testsFile[section]?.AsArray() ?? []))
                    testCase!["prompt"] ??= "Built by the app from the case's subject.";
                var target = Path.Combine(copy.FullName, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, testsFile.ToJsonString(), TestContext.Current.CancellationToken);
            }

            return await AgentTestCase.LoadAsync(copy.FullName, TestContext.Current.CancellationToken);
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }
}
