using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Filters;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Orchestrators;

public sealed class BriefingOrchestratorTests
{
    private const string EstablishmentName = AgentNames.Establishment;
    private const string RecastConcernsName = AgentNames.RecastConcerns;
    private const string TrustName = AgentNames.Trust;
    private const string RiseConcernsName = AgentNames.RiseConcerns;
    private const string WebSearchName = AgentNames.WebSearch;
    private const string SynthesisName = AgentNames.Synthesis;

    // Deliberately not the default (3), so the tests prove the configured value is the one used.
    private const int ConfiguredEvidencePerSearch = 7;

    private static readonly AgentDefinition Establishment = new(EstablishmentName, SystemPromptKey.Establishment);
    private static readonly AgentDefinition RecastConcerns = new(RecastConcernsName, SystemPromptKey.RecastConcerns);
    private static readonly AgentDefinition Trust = new(TrustName, SystemPromptKey.Trust);
    private static readonly AgentDefinition RiseConcerns = new(RiseConcernsName, SystemPromptKey.RiseConcerns, IsManagedAgent: false);
    private static readonly AgentDefinition Synthesis = new(SynthesisName, SystemPromptKey.Synthesis, IsManagedAgent: false);

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();
    private readonly IAgentRuntimeService _agentRuntime = Substitute.For<IAgentRuntimeService>();
    private readonly IPromptProvider _promptProvider = Substitute.For<IPromptProvider>();

    private readonly IBriefingBuilder _briefingBuilder;

    public BriefingOrchestratorTests() => _briefingBuilder = new BriefingBuilder(_promptProvider, _promptBuilder);

    private BriefingOrchestrator CreateSut(IReadOnlyCollection<AgentDefinition>? definitions = null)
    {
        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("evidence", HasEvidence: true)));

        _promptBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("synthesis-prompt");
        _promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch).Returns("web-search-system-prompt");

        _agentService.RunAsync(Arg.Any<AgentDefinition>(), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result(SynthesisName, "final-briefing", 10)));

        var evidenceProvider = new BriefingEvidenceProvider(_contextRetriever, new BriefingOptions { MaxEvidencePerSearch = ConfiguredEvidencePerSearch });

        return new BriefingOrchestrator(definitions ?? [Establishment, RecastConcerns, Trust, Synthesis], evidenceProvider,
            _agentService, _agentRuntime, _briefingBuilder);
    }

    private void SetUpWebSearchResult(AgentResult result)
        => _agentRuntime.RunEphemeralAsync(Arg.Any<AgentSpec>(), Arg.Any<string>(), Arg.Any<ToolCallResolver?>(),
                Arg.Any<string?>(), Arg.Any<Func<AgentResult, string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private static bool DefinitionsAre(IReadOnlyCollection<AgentDefinition> actual, params string[] expectedNames)
        => actual.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal)
            .SequenceEqual(expectedNames.OrderBy(n => n, StringComparer.Ordinal));

    private void SetUpParallelResult(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, params AgentResult[] results)
        => _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<AgentEvidence?>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentResult>>(results));

    private void SetUpParallelFailure(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, Exception exception)
        => _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<AgentEvidence?>>?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<AgentResult>>>(_ => throw exception);

    private void SetUpDefaultSpecialistResults()
    {
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), Result(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName, TrustName), Result(RecastConcernsName, "recast-output", 10), Result(TrustName, "trust-output", 10));
    }

    private static AgentResult Result(string agentName, string output, long totalTokens)
        => new() { AgentName = agentName, Output = output, TotalTokens = totalTokens };

    private async Task AssertParallelCallReceived(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, int times = 1)
        => await _agentService.Received(times).RunParallelAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
            Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
            Arg.Any<AgentContext>(),
            Arg.Any<Func<Exception, bool>?>(),
            Arg.Any<Func<AgentDefinition, CancellationToken, Task<AgentEvidence?>>?>(),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task GetBriefing_NoEstablishmentEvidence_ReturnsNoSchoolOrAcademyFoundResult_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("No Establishment information found.", HasEvidence: false)));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Contains("No school or academy found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
        await _agentService.DidNotReceiveWithAnyArgs().RunParallelAsync(default!, default!, default!, cancellationToken: cancellationToken);
        await _agentService.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task GetBriefing_NoTrustEvidence_ReturnsNoTrustInformationFoundResult_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Trust, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("No Trust information found.", HasEvidence: false)));

        var parameters = new BriefingParameters("TR12345", "Test Trust");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Contains("No trust information found.", result.Output, StringComparison.Ordinal);
        await _agentService.DidNotReceiveWithAnyArgs().RunParallelAsync(default!, default!, default!, cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task GetBriefing_FailedEstablishmentRun_PropagatesFailure()
    {
        var sut = CreateSut();
        SetUpParallelFailure(d => DefinitionsAre(d, EstablishmentName), new InvalidOperationException("establishment-agent failed"));

        var parameters = new BriefingParameters("123456", "Test Academy");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetBriefing(parameters, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task GetBriefing_FindsTheSchoolByUrn_AndPassesItsRecordToTheSpecialistSearches()
    {
        var sut = CreateSut([Establishment, RecastConcerns, Trust, Synthesis]);
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("establishment_urn: 123456\nassociated_trust_name: EXAMPLE TRUST", HasEvidence: true)));
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), Result(EstablishmentName, "establishment-output", 10));

        Func<AgentDefinition, CancellationToken, Task<AgentEvidence?>>? capturedResolveEvidence = null;
        _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => DefinitionsAre(d, RecastConcernsName, TrustName)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Do<Func<AgentDefinition, CancellationToken, Task<AgentEvidence?>>?>(f => capturedResolveEvidence = f),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentResult>>([Result(RecastConcernsName, "recast-output", 10), Result(TrustName, "trust-output", 10)]));

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Establishment, "*", ConfiguredEvidencePerSearch,
            Arg.Is<ODataFilter>(f => f.ToString() == "establishment_urn eq '123456'"), Arg.Any<CancellationToken>());

        Assert.NotNull(capturedResolveEvidence);
        await capturedResolveEvidence!(RecastConcerns, cancellationToken);
        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.RecastConcerns,
            Arg.Is<string>(q => q.Contains("Test Academy", StringComparison.Ordinal) && q.Contains("123456", StringComparison.Ordinal)),
            ConfiguredEvidencePerSearch, Arg.Is<ODataFilter>(f => f.IsEmpty), Arg.Any<CancellationToken>());

        // The trust is found through the school's own record, not by the name typed into the form.
        await capturedResolveEvidence!(Trust, cancellationToken);
        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Trust, "*", ConfiguredEvidencePerSearch,
            Arg.Is<ODataFilter>(f => f.ToString() == "trust_name eq 'EXAMPLE TRUST'"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_ExcludesEstablishmentAndSynthesis_FromTheSpecialistAgentsRun()
    {
        var sut = CreateSut([Establishment, RecastConcerns, Synthesis]);
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), Result(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName), Result(RecastConcernsName, "recast-output", 10));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        await AssertParallelCallReceived(d => DefinitionsAre(d, RecastConcernsName));
    }

    [Fact]
    public async Task GetBriefing_DoesNotRunTheWebSearchAgent_OrShowMoreBackgroundInformation()
    {
        var sut = CreateSut();
        SetUpDefaultSpecialistResults();
        _agentService.RunAsync(Arg.Is<AgentDefinition>(d => d.Name == SynthesisName), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result(SynthesisName, "# Test Academy\n\n## School summary\n\nRated Good.\n\n## More background information\n\n1. Website\n", 10)));

        var result = await sut.GetBriefing(new BriefingParameters("123456", "Test Academy"), cancellationToken: cancellationToken);

        await _agentRuntime.DidNotReceiveWithAnyArgs().RunEphemeralAsync(default!, default!, cancellationToken: cancellationToken);
        Assert.DoesNotContain("More background information", result.Output, StringComparison.Ordinal);
        Assert.Contains("## School summary\n\nRated Good.", result.Output, StringComparison.Ordinal);
        Assert.True(sut.CanAddWebSearchFindings(result.Output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# Test Academy\n\nNo school or academy found.")]
    [InlineData("# Test Academy\n\n## School summary\n\nRated Good.\n\n## More background information\n\n### News and media coverage\n\n- 1 May 2026: News. [A](https://a.org/x)")]
    public void CanAddWebSearchFindings_IsFalse_WithNoRecordsOrWhenNewsIsAlreadyThere(string? briefing)
        => Assert.False(CreateSut().CanAddWebSearchFindings(briefing));

    [Fact]
    public async Task GetWebSearchFindings_RunsTheWebSearchAgent_WithTheBriefingWithoutItsCitationsAsEvidence_AndTheCityInItsPrompt()
    {
        var sut = CreateSut();
        SetUpWebSearchResult(Result(WebSearchName, "- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)", 10));
        const string briefing = "# Test Academy\n\n## School summary\n\nRated Good. [Test Academy 123456 Academy converter]";

        await sut.GetWebSearchFindings(new BriefingParameters("123456", "Test Academy"), briefing, "Coventry", website: null, cancellationToken);

        await _agentRuntime.Received(1).RunEphemeralAsync(
            Arg.Any<AgentSpec>(), Arg.Is<string>(prompt => prompt.Contains("Coventry", StringComparison.Ordinal)), Arg.Any<ToolCallResolver?>(),
            Arg.Is<string?>(evidence => evidence != null && evidence.Contains("Rated Good.", StringComparison.Ordinal) && !evidence.Contains('[')),
            Arg.Any<Func<AgentResult, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWebSearchFindings_ReturnsOnlyTheMoreBackgroundInformationSection_WithoutRerunningTheOtherAgents()
    {
        var sut = CreateSut();
        SetUpWebSearchResult(Result(WebSearchName, "## Web search findings\n\n- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)", 15));
        const string briefing = "# Test Academy\n\n## School summary\n\nRated Good.\n\n## Financial health\n\n- Not available.\n";

        var result = await sut.GetWebSearchFindings(new BriefingParameters("123456", "Test Academy"), briefing, city: null, website: null, cancellationToken);

        Assert.Equal("## More background information\n\n### News and media coverage\n\n- 20 August 2026: Record results. [The Star](https://www.thestar.co.uk/a-1)\n", result.Output);
        Assert.Equal(15, result.TotalTokens);
        await _agentService.DidNotReceiveWithAnyArgs().RunParallelAsync(default!, default!, default!, cancellationToken: cancellationToken);
        await _agentService.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, cancellationToken: cancellationToken);
    }

    [Fact]
    public async Task GetBriefing_StripsTheCityMarker_FromTheSubjectOutput_BeforeItReachesSynthesis()
    {
        var sut = CreateSut();
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName),
            Result(EstablishmentName, "# Test Academy\n\nSome findings.\n{{city: Coventry}}", 10));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName, TrustName), Result(RecastConcernsName, "recast-output", 10), Result(TrustName, "trust-output", 10));

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                !d["Context"].Contains("city:", StringComparison.Ordinal) &&
                d["Context"].Contains("Some findings.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_StripsTheCityMarker_FromSpecialistOutputs_WhenTheSubjectIsATrust()
    {
        var sut = CreateSut();
        SetUpParallelResult(d => DefinitionsAre(d, TrustName), Result(TrustName, "# Test Trust\n\nTrust findings.\n{{city: Leeds}}", 10));
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName, RecastConcernsName),
            Result(EstablishmentName, "## About the trust\n\nEstablishment findings.\n{{city: Coventry}}", 10),
            Result(RecastConcernsName, "recast-output", 10));

        var parameters = new BriefingParameters("TR12345", "Test Trust");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                !d["Context"].Contains("city:", StringComparison.Ordinal) &&
                d["Context"].Contains("Establishment findings.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_ReturnsTheExtractedCity_ForALaterWebSearch()
    {
        var sut = CreateSut();
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName),
            Result(EstablishmentName, "# Test Academy\n\nSome findings.\n{{city: Coventry}}", 10));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName, TrustName), Result(RecastConcernsName, "recast-output", 10), Result(TrustName, "trust-output", 10));

        var result = await sut.GetBriefing(new BriefingParameters("123456", "Test Academy"), cancellationToken: cancellationToken);

        Assert.Equal("Coventry", result.City);
    }

    [Fact]
    public async Task GetBriefing_RunsSynthesis_AsTheEphemeralDefinitionFromTheDefinitionProvider()
    {
        var sut = CreateSut();
        SetUpDefaultSpecialistResults();

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentService.Received(1).RunAsync(
            Arg.Is<AgentDefinition>(d => d.Name == SynthesisName && !d.IsManagedAgent),
            Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_BuildsSynthesisPrompt_WithHumanReadableDisplayNamesAndCompiledContext()
    {
        var sut = CreateSut();
        SetUpDefaultSpecialistResults();

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        _promptBuilder.Received(1).Build(UserPromptType.Synthesis,
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["SchoolOrTrustName"] == "Test Academy" &&
                d["SchoolUrnOrTrustTrn"] == "123456" &&
                d["Context"].Contains("AGENT: Establishment", StringComparison.Ordinal) &&
                d["Context"].Contains("AGENT: Recast", StringComparison.Ordinal) &&
                d["Context"].Contains("AGENT: Trust", StringComparison.Ordinal) &&
                d["IncludedSections"].Contains("Establishment", StringComparison.Ordinal) &&
                !d["IncludedSections"].Contains("-agent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_SumsTokensAcrossSpecialistsAndSynthesis()
    {
        var sut = CreateSut();
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), Result(EstablishmentName, "establishment-output", 100));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName, TrustName), Result(RecastConcernsName, "recast-output", 50), Result(TrustName, "trust-output", 20));
        _agentService.RunAsync(Arg.Is<AgentDefinition>(d => d.Name == SynthesisName), Arg.Any<string>(), Arg.Any<AgentEvidence?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result(SynthesisName, "final-briefing", 25)));

        var result = await sut.GetBriefing(new BriefingParameters("123456", "Test Academy"), cancellationToken: cancellationToken);

        Assert.Equal(195, result.TotalTokens);
    }

    [Fact]
    public async Task GetBriefing_RunsNonManagedSpecialists_LikeAnyOtherSpecialist()
    {
        var sut = CreateSut([Establishment, RecastConcerns, Trust, RiseConcerns, Synthesis]);
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), Result(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, RecastConcernsName, TrustName, RiseConcernsName),
            Result(RecastConcernsName, "recast-output", 10),
            Result(TrustName, "trust-output", 10),
            Result(RiseConcernsName, "rise-output", 10));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Context"].Contains("rise-output", StringComparison.Ordinal)));
    }
}
