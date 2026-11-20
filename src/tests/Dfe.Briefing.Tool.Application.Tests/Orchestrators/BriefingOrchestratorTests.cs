using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Orchestrators;

public sealed class BriefingOrchestratorTests
{
    private static readonly BriefingParameters School = new("123456", "Test Academy", IsTrust: false);
    private static readonly BriefingParameters Trust = new("TR12345", "Test Trust", IsTrust: true);

    private static readonly BriefingRecords Records = new(
        new ContextResult("[Evidence 1]\nestablishment_name: Test Academy", HasEvidence: true),
        new ContextResult("This school is not part of a trust.", HasEvidence: false),
        new ContextResult("[Evidence 1]\nsummary: Rise concern", HasEvidence: true),
        new ContextResult(string.Empty, HasEvidence: false));

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IBriefingEvidenceProvider _evidenceProvider = Substitute.For<IBriefingEvidenceProvider>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();
    private readonly IAgentRuntimeService _agentRuntime = Substitute.For<IAgentRuntimeService>();
    private readonly IPromptProvider _promptProvider = Substitute.For<IPromptProvider>();
    private readonly IBriefingBuilder _briefingBuilder;
    private readonly BriefingOrchestrator _sut;

    public BriefingOrchestratorTests()
    {
        _briefingBuilder = new BriefingBuilder(_promptProvider, _promptBuilder);
        _evidenceProvider.GetRecordsAsync(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(Records);
        _promptBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("briefing-prompt");
        _promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch).Returns("web-search-system-prompt");
        BriefingAgentReturns("# Test Academy\n\n## About the school\n\nRated Good. [Evidence 1]", 10);

        _sut = new BriefingOrchestrator(_evidenceProvider, _agentService, _agentRuntime, _briefingBuilder);
    }

    private void BriefingAgentReturns(string output, long totalTokens)
        => _agentService.RunAsync(Arg.Any<AgentDefinition>(), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result(AgentNames.Briefing, output, totalTokens)));

    private void SetUpWebSearchResult(AgentResult result)
        => _agentRuntime.RunEphemeralAsync(Arg.Any<AgentSpec>(), Arg.Any<string>(), Arg.Any<ToolCallResolver?>(),
                Arg.Any<string?>(), Arg.Any<Func<AgentResult, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private static AgentResult Result(string agentName, string output, long totalTokens)
        => new() { AgentName = agentName, Output = output, TotalTokens = totalTokens };

    [Theory]
    [InlineData(false, "No school or academy found.")]
    [InlineData(true, "No trust information found.")]
    public async Task GetBriefing_WithNoRecordForTheSubject_SaysSo_WithoutRunningTheAgent(bool isTrust, string message)
    {
        _evidenceProvider.GetRecordsAsync(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns((BriefingRecords?)null);

        var result = await _sut.GetBriefing(isTrust ? Trust : School, cancellationToken: cancellationToken);

        Assert.Contains(message, result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
        await _agentService.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task GetBriefing_RunsTheBriefingAgentOnce_WithItsPrompt_AndItsRecordsAsEvidence_ButNoConcerns()
    {
        await _sut.GetBriefing(School, cancellationToken: cancellationToken);

        await _agentService.Received(1).RunAsync(BriefingAgents.Briefing, "briefing-prompt",
            Arg.Is<AgentEvidence?>(evidence => evidence != null
                && evidence.Text.Contains("=== School record ===", StringComparison.Ordinal)
                && evidence.Text.Contains("establishment_name: Test Academy", StringComparison.Ordinal)
                && evidence.Text.Contains("This school is not part of a trust.", StringComparison.Ordinal)
                && !evidence.Text.Contains("Rise concern", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_PassesItsProgressToTheSearches_ThenReportsWritingTheSummary()
    {
        var steps = new List<string>();
        var progress = new SynchronousProgress(steps.Add);

        await _sut.GetBriefing(School, progress, cancellationToken);

        await _evidenceProvider.Received(1).GetRecordsAsync(School, progress, Arg.Any<CancellationToken>());
        Assert.Equal(["Writing the summary..."], steps);
    }

    [Fact]
    public async Task GetBriefing_ReturnsTheTownFromTheRecords_ForALaterWebSearch_NotOneTheAgentWrites()
    {
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456\nestablishment_address: 1 High Street, Coventry, CV1 1AA", HasEvidence: true);
        _evidenceProvider.GetRecordsAsync(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Records with { Subject = school });
        BriefingAgentReturns("# Test Academy\n\n## About the school\n\nRated Good.\n{{city: Elsewhere}}", 10);

        var result = await _sut.GetBriefing(School, cancellationToken: cancellationToken);

        Assert.Equal("Coventry", result.City);
        Assert.DoesNotContain("city:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBriefing_ForATrust_ListsItsAcademiesInTheOrderWorkedOutFromTheRecords_NotTheAgents()
    {
        var academies = new ContextResult(
            "[Evidence 1]\nestablishment_name: Academy A\nestablishment_urn: 900001\nofsted_all_inspections: [{\"inspection_date\": \"2023-01-01\", \"overall_effectiveness\": \"Good\"}]\n\n" +
            "[Evidence 2]\nestablishment_name: Academy B\nestablishment_urn: 900002\nofsted_all_inspections: [{\"inspection_date\": \"2023-01-01\", \"overall_effectiveness\": \"Inadequate\"}]",
            HasEvidence: true);
        _evidenceProvider.GetRecordsAsync(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Records with { Related = academies });
        BriefingAgentReturns("# Test Trust\n\n## Ofsted summary\n\n- Academy A: Good [Evidence 2]\n- Academy B: Inadequate [Evidence 3]", 10);

        var result = await _sut.GetBriefing(Trust, cancellationToken: cancellationToken);

        Assert.Equal("# Test Trust\n\n## Ofsted summary\n\n- Academy B: Inadequate [Evidence 3]\n- Academy A: Good [Evidence 2]", result.Output);
    }

    [Fact]
    public async Task GetBriefing_GivesNoTown_WhenTheRecordsGiveNoAddressOrLocalAuthority()
    {
        var result = await _sut.GetBriefing(School, cancellationToken: cancellationToken);

        Assert.Null(result.City);
    }

    [Fact]
    public async Task GetBriefing_RemovesAMoreBackgroundInformationSection_AndEmptySections_AndGivesTheAgentsTokens()
    {
        BriefingAgentReturns("# Test Academy\n\n## About the school\n\nRated Good.\n\n## Attendance\n\n## More background information\n\n1. Website\n", 25);

        var result = await _sut.GetBriefing(School, cancellationToken: cancellationToken);

        Assert.Equal("# Test Academy\n\n## About the school\n\nRated Good.\n\n", result.Output);
        Assert.Equal(25, result.TotalTokens);
        await _agentRuntime.DidNotReceiveWithAnyArgs().RunEphemeralAsync(default!, default!, cancellationToken: cancellationToken);
        Assert.True(_sut.CanAddWebSearchFindings(result.Output));
    }

    [Fact]
    public async Task GetBriefing_PropagatesAFailedRun()
    {
        _agentService.RunAsync(Arg.Any<AgentDefinition>(), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>())
            .Returns<Task<AgentResult>>(_ => throw new InvalidOperationException("briefing-agent failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetBriefing(School, cancellationToken: cancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# Test Academy\n\nNo school or academy found.")]
    [InlineData("# Test Academy\n\n## School summary\n\nRated Good.\n\n## More background information\n\n### News and media coverage\n\n- 1 May 2026: News. [A](https://a.org/x)")]
    public void CanAddWebSearchFindings_IsFalse_WithNoRecordsOrWhenNewsIsAlreadyThere(string? briefing)
        => Assert.False(_sut.CanAddWebSearchFindings(briefing));

    [Fact]
    public async Task GetWebSearchFindings_RunsTheWebSearchAgent_WithTheBriefingWithoutItsCitationsAsEvidence_AndTheCityInItsPrompt()
    {
        SetUpWebSearchResult(Result(AgentNames.WebSearch, "- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)", 10));
        const string briefing = "# Test Academy\n\n## School summary\n\nRated Good. [Test Academy 123456 Academy converter]";

        await _sut.GetWebSearchFindings(School, briefing, "Coventry", website: null, cancellationToken);

        await _agentRuntime.Received(1).RunEphemeralAsync(
            Arg.Any<AgentSpec>(), Arg.Is<string>(prompt => prompt.Contains("Coventry", StringComparison.Ordinal)), Arg.Any<ToolCallResolver?>(),
            Arg.Is<string?>(evidence => evidence != null && evidence.Contains("Rated Good.", StringComparison.Ordinal) && !evidence.Contains('[')),
            Arg.Any<Func<AgentResult, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWebSearchFindings_ReturnsOnlyTheMoreBackgroundInformationSection_WithoutRerunningTheBriefingAgent()
    {
        SetUpWebSearchResult(Result(AgentNames.WebSearch, "## Web search findings\n\n- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)", 15));
        const string briefing = "# Test Academy\n\n## School summary\n\nRated Good.\n\n## Financial health\n\n- Not available.\n";

        var result = await _sut.GetWebSearchFindings(School, briefing, city: null, website: null, cancellationToken);

        Assert.Equal("## More background information\n\n### News and media coverage\n\n- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)\n", result.Output);
        Assert.Equal(15, result.TotalTokens);
        await _agentService.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, cancellationToken: cancellationToken);
    }

    // Progress<T> reports on the thread pool, so a test can't rely on its order; this reports as it's called.
    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
