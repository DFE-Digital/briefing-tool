using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Context;
using GovUK.Dfe.CoreLibs.AiAgents.Context.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Prompts.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Orchestrators;

public sealed class BriefingOrchestratorTests
{
    private const string EstablishmentName = AgentNames.Establishment;
    private const string OfstedName = AgentNames.Ofsted;
    private const string TrustName = AgentNames.Trust;
    private const string RiseConcernsName = AgentNames.RiseConcerns;
    private const string WebSearchName = AgentNames.WebSearch;
    private const string SynthesisName = AgentNames.Synthesis;

    private static readonly AgentDefinition Establishment = new(EstablishmentName, SystemPromptKey.Establishment);
    private static readonly AgentDefinition Ofsted = new(OfstedName, SystemPromptKey.Ofsted);
    private static readonly AgentDefinition Trust = new(TrustName, SystemPromptKey.Trust);
    private static readonly AgentDefinition RiseConcerns = new(RiseConcernsName, SystemPromptKey.RiseConcerns, IsManagedAgent: false);
    private static readonly AgentDefinition WebSearch = new(WebSearchName, SystemPromptKey.WebSearch, IsManagedAgent: false);
    private static readonly AgentDefinition Synthesis = new(SynthesisName, SystemPromptKey.Synthesis, IsManagedAgent: false);

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();

    private BriefingOrchestrator CreateSut(IReadOnlyCollection<AgentDefinition>? definitions = null)
    {
        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("evidence", HasEvidence: true)));

        _promptBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("synthesis-prompt");

        _agentService.RunAsync(Arg.Any<AgentDefinition>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentResult(SynthesisName, "final-briefing", 10)));

        return new BriefingOrchestrator(definitions ?? [Establishment, Ofsted, Trust, WebSearch, Synthesis], _contextRetriever, _promptBuilder, _agentService);
    }

    private static bool DefinitionsAre(IReadOnlyCollection<AgentDefinition> actual, params string[] expectedNames)
        => actual.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal)
            .SequenceEqual(expectedNames.OrderBy(n => n, StringComparer.Ordinal));

    private void SetUpParallelResult(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, params AgentResult[] results)
        => _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string?>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentResult>>(results));

    private void SetUpParallelFailure(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, Exception exception)
        => _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string?>>?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<AgentResult>>>(_ => throw exception);

    private void SetUpDefaultSpecialistResults()
    {
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), new AgentResult(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, OfstedName, TrustName), new AgentResult(OfstedName, "ofsted-output", 10), new AgentResult(TrustName, "trust-output", 10));
    }

    private async Task AssertParallelCallReceived(Predicate<IReadOnlyCollection<AgentDefinition>> matcher, int times = 1)
        => await _agentService.Received(times).RunParallelAsync(
            Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => matcher(d)),
            Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
            Arg.Any<AgentContext>(),
            Arg.Any<Func<Exception, bool>?>(),
            Arg.Any<Func<AgentDefinition, CancellationToken, Task<string?>>?>(),
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task GetBriefing_NoEstablishmentEvidence_ReturnsNoSchoolOrAcademyFoundResult_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
        _contextRetriever.GetContextAsync(EvidenceCategory.Trust, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
    public async Task GetBriefing_ResolvesEvidence_UsingEachSpecialistsEvidenceCategoryAndSearchQuery()
    {
        var sut = CreateSut([Establishment, Ofsted, Trust, WebSearch, Synthesis]);
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), new AgentResult(EstablishmentName, "establishment-output", 10));

        Func<AgentDefinition, CancellationToken, Task<string?>>? capturedResolveEvidence = null;
        _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => DefinitionsAre(d, OfstedName, TrustName)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Do<Func<AgentDefinition, CancellationToken, Task<string?>>?>(f => capturedResolveEvidence = f),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentResult>>([new AgentResult(OfstedName, "ofsted-output", 10), new AgentResult(TrustName, "trust-output", 10)]));

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Establishment,
            Arg.Is<string>(q => q.Contains("Test Academy", StringComparison.Ordinal) && q.Contains("123456", StringComparison.Ordinal)),
            10, Arg.Any<string?>(), Arg.Any<CancellationToken>());

        Assert.NotNull(capturedResolveEvidence);
        await capturedResolveEvidence!(Ofsted, cancellationToken);
        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Ofsted,
            Arg.Is<string>(q => q.Contains("Ofsted inspection", StringComparison.Ordinal)), 10, Arg.Any<string?>(), Arg.Any<CancellationToken>());

        await capturedResolveEvidence!(Trust, cancellationToken);
        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Trust,
            Arg.Is<string>(q => q.Contains("Trust data", StringComparison.Ordinal)), 10, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_ExcludesEstablishmentWebSearchAndSynthesis_FromTheSpecialistAgentsRun()
    {
        var sut = CreateSut([Establishment, Ofsted, WebSearch, Synthesis]);
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), new AgentResult(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, OfstedName), new AgentResult(OfstedName, "ofsted-output", 10));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        await AssertParallelCallReceived(d => DefinitionsAre(d, OfstedName));
    }

    [Fact]
    public async Task GetBriefing_DoesNotRunTheWebSearchAgent_WhenNotAllowed()
    {
        var sut = CreateSut();
        SetUpDefaultSpecialistResults();

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: false);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await AssertParallelCallReceived(d => DefinitionsAre(d, WebSearchName), times: 0);
    }

    [Fact]
    public async Task GetBriefing_RunsTheWebSearchAgent_WhenAllowed_WithEvidenceCompiledFromKnownResults()
    {
        var sut = CreateSut();
        SetUpDefaultSpecialistResults();

        Func<AgentDefinition, CancellationToken, Task<string?>>? capturedResolveEvidence = null;
        _agentService.RunParallelAsync(
                Arg.Is<IReadOnlyCollection<AgentDefinition>>(d => DefinitionsAre(d, WebSearchName)),
                Arg.Any<Func<AgentDefinition, CancellationToken, Task<string>>>(),
                Arg.Any<AgentContext>(),
                Arg.Any<Func<Exception, bool>?>(),
                Arg.Do<Func<AgentDefinition, CancellationToken, Task<string?>>?>(f => capturedResolveEvidence = f),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgentResult>>([new AgentResult(WebSearchName, "web search findings", 10)]));

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.NotNull(capturedResolveEvidence);
        var evidence = await capturedResolveEvidence!(WebSearch, cancellationToken);
        Assert.Contains("establishment-output", evidence, StringComparison.Ordinal);
        Assert.Contains("ofsted-output", evidence, StringComparison.Ordinal);
        Assert.Contains("AGENT: Establishment", evidence, StringComparison.Ordinal);
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
                d["Context"].Contains("AGENT: Ofsted", StringComparison.Ordinal) &&
                d["Context"].Contains("AGENT: Trust", StringComparison.Ordinal) &&
                d["IncludedSections"].Contains("Establishment", StringComparison.Ordinal) &&
                !d["IncludedSections"].Contains("-agent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_SumsTokensAcrossSpecialistsWebSearchAndSynthesis()
    {
        var sut = CreateSut();
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), new AgentResult(EstablishmentName, "establishment-output", 100));
        SetUpParallelResult(d => DefinitionsAre(d, OfstedName, TrustName), new AgentResult(OfstedName, "ofsted-output", 50), new AgentResult(TrustName, "trust-output", 20));
        SetUpParallelResult(d => DefinitionsAre(d, WebSearchName), new AgentResult(WebSearchName, "web search findings", 15));
        _agentService.RunAsync(Arg.Is<AgentDefinition>(d => d.Name == SynthesisName), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentResult(SynthesisName, "final-briefing", 25)));

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal(210, result.TotalTokens);
    }

    [Fact]
    public async Task GetBriefing_RunsNonManagedSpecialists_LikeAnyOtherSpecialist()
    {
        var sut = CreateSut([Establishment, Ofsted, Trust, RiseConcerns, WebSearch, Synthesis]);
        SetUpParallelResult(d => DefinitionsAre(d, EstablishmentName), new AgentResult(EstablishmentName, "establishment-output", 10));
        SetUpParallelResult(d => DefinitionsAre(d, OfstedName, TrustName, RiseConcernsName),
            new AgentResult(OfstedName, "ofsted-output", 10),
            new AgentResult(TrustName, "trust-output", 10),
            new AgentResult(RiseConcernsName, "rise-output", 10));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Context"].Contains("rise-output", StringComparison.Ordinal)));
    }
}
