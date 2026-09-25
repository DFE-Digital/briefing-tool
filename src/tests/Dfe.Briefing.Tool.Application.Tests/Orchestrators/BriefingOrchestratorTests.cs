using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.AiAgents.Agents;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Context.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Factories.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Orchestration;
using GovUK.Dfe.CoreLibs.AiAgents.Prompts.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Orchestrators;

public sealed class BriefingOrchestratorTests
{
    private static readonly AgentDefinition Establishment = new("establishment-agent", SystemPromptType.Establishment, EvidenceCategory.Establishment, "{{SchoolOrTrustName}}");
    private static readonly AgentDefinition Ofsted = new("ofsted-agent", SystemPromptType.Ofsted, EvidenceCategory.Ofsted, "{{SchoolOrTrustName}} Ofsted");
    private static readonly AgentDefinition Trust = new("trust-agent", SystemPromptType.Trust, EvidenceCategory.Trust, "{{SchoolOrTrustName}} Trust");
    private static readonly AgentDefinition RiseConcerns = new("rise-concerns-agent", SystemPromptType.RiseConcerns,
        EvidenceCategory.RiseConcerns, "{{SchoolOrTrustName}} RISE concerns", IsManagedAgent: false);
    private const string SynthesisAgentName = "briefing-synthesis-agent";
    private const string WebSearchAgentName = "web-search-agent";

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentDefinitionProvider _definitionProvider = Substitute.For<IAgentDefinitionProvider>();
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly IPromptProvider _promptProvider = Substitute.For<IPromptProvider>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly IAgentFactory _agentFactory = Substitute.For<IAgentFactory>();
    private readonly IAgentRunner _agentRunner = Substitute.For<IAgentRunner>();
    private readonly IAgentRuntime _agentRuntime = Substitute.For<IAgentRuntime>();

    private BriefingOrchestrator CreateSut(IReadOnlyCollection<AgentDefinition>? definitions = null)
    {
        _definitionProvider.GetAgentsDefinitions().Returns(definitions ?? [Establishment, Ofsted, Trust]);

        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("evidence", HasEvidence: true)));
         
        _agentFactory.GetOrCreateAsync(Arg.Any<AgentSpec>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new AgentReference($"{callInfo.Arg<AgentSpec>().Name}-id", callInfo.Arg<AgentSpec>().Name));
        _agentRuntime.ResolveAsync(Arg.Any<AgentReference>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<AgentReference>());
         
        _agentRuntime.Orchestrator.Returns(new AgentOrchestrator(_agentRunner));

        _promptProvider.GetSystemPrompt(Arg.Any<string>()).Returns("system-instructions");
        _promptBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("synthesis-prompt");
         
        var specialistAgentRunner = new SpecialistAgentRunner(_agentFactory, _agentRunner, _agentRuntime, _promptProvider);

        return new BriefingOrchestrator(_definitionProvider, _contextRetriever, _promptProvider, _promptBuilder, _agentRuntime,
            specialistAgentRunner);
    }

    private void SetUpAgentResult(string agentName, string output, long totalTokens = 10)
        => _agentRunner.RunAsync(Arg.Is<AgentReference>(agent => agent.Name == agentName), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentResult(agentName, output, totalTokens)));

    private void SetUpAgentFailure(string agentName)
        => _agentRunner.RunAsync(Arg.Is<AgentReference>(agent => agent.Name == agentName), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException($"{agentName} failed"));
 
    private AgentSpec? _capturedSynthesisSpec;
    private string? _capturedWebSearchInstructions;
    private string? _capturedWebSearchPrompt;

    private void SetUpSynthesisAgentResult(string output, long totalTokens = 10)
        => _agentRuntime.RunEphemeralAsync(Arg.Is<AgentSpec>(spec => spec.Name == SynthesisAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _capturedSynthesisSpec = callInfo.Arg<AgentSpec>();
                return Task.FromResult(new AgentResult(SynthesisAgentName, output, totalTokens));
            });

    private void SetUpWebSearchAgentResult(string output, long totalTokens = 10)
        => _agentRuntime.RunEphemeralAsync(Arg.Is<AgentSpec>(spec => spec.Name == WebSearchAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                _capturedWebSearchInstructions = callInfo.Arg<AgentSpec>().Instructions;
                _capturedWebSearchPrompt = callInfo.ArgAt<string>(1);
                return Task.FromResult(new AgentResult(WebSearchAgentName, output, totalTokens));
            });

    private void SetUpEphemeralSpecialistResult(string agentName, string output, long totalTokens = 10)
        => _agentRuntime.RunEphemeralAsync(Arg.Is<AgentSpec>(spec => spec.Name == agentName), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentResult(agentName, output, totalTokens)));

    private void SetUpWebSearchAgentFailure()
        => _agentRuntime.RunEphemeralAsync(Arg.Is<AgentSpec>(spec => spec.Name == WebSearchAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("web search failed"));

    [Fact]
    public async Task GetBriefing_AlwaysRunsEstablishmentAndEverySpecialist()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentReference>(agent => agent.Name == Establishment.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentReference>(agent => agent.Name == Ofsted.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentReference>(agent => agent.Name == Trust.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_CreatesOrResolvesEverySpecialistAgent_WithInstructionsFromItsOwnPromptType()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentFactory.Received(1).GetOrCreateAsync(Arg.Is<AgentSpec>(spec => spec.Name == Establishment.Name), Arg.Any<CancellationToken>());
        await _agentFactory.Received(1).GetOrCreateAsync(Arg.Is<AgentSpec>(spec => spec.Name == Ofsted.Name), Arg.Any<CancellationToken>());
        await _agentFactory.Received(1).GetOrCreateAsync(Arg.Is<AgentSpec>(spec => spec.Name == Trust.Name), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_FailedSpecialistAgent_DoesNotFailWholeBriefing()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentFailure(Ofsted.Name);
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Context"].Contains("could not be generated", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetBriefing_FailedEstablishmentAgent_PropagatesFailure()
    {
        var sut = CreateSut();
        SetUpAgentFailure(Establishment.Name);
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetBriefing(parameters, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task GetBriefing_UnresolvableSpecialistAgent_DoesNotFailWholeBriefing()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        _agentFactory.GetOrCreateAsync(Arg.Is<AgentSpec>(spec => spec.Name == Ofsted.Name), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AgentReference>(new InvalidOperationException("Agent 'ofsted-agent' was not found.")));
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        await _agentRunner.DidNotReceive().RunAsync(Arg.Is<AgentReference>(agent => agent.Name == Ofsted.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_NoEstablishmentEvidence_ReturnsANoSchoolOrAcademyFoundResult_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("No Establishment information found.", HasEvidence: false)));

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.NotNull(result.Output);
        Assert.Contains("No school or academy found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
        await _agentRunner.DidNotReceive().RunAsync(Arg.Any<AgentReference>(), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        await _agentRuntime.DidNotReceiveWithAnyArgs().RunEphemeralAsync(default!, default!, cancellationToken);
    }

    [Fact]
    public async Task GetBriefing_NoTrustEvidence_ReturnsANoTrustInformationFoundResult_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Trust, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("No Trust information found.", HasEvidence: false)));
         
        var parameters = new BriefingParameters("TR12345", "Test Trust");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.NotNull(result.Output);
        Assert.Contains("No trust information found.", result.Output, StringComparison.Ordinal);
        await _agentRunner.DidNotReceive().RunAsync(Arg.Any<AgentReference>(), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_SumsTokensAcrossAllAgents()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output", totalTokens: 100);
        SetUpAgentResult(Ofsted.Name, "ofsted-output", totalTokens: 50);
        SetUpAgentResult(Trust.Name, "trust-output", totalTokens: 20);
        SetUpSynthesisAgentResult("final-briefing", totalTokens: 25);

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal(195, result.TotalTokens);
    }

    [Fact]
    public async Task GetBriefing_UsesHumanReadableDisplayNames_ForAgentSectionsInSynthesis()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["Context"].Contains("AGENT: Establishment", StringComparison.Ordinal) &&
                d["Context"].Contains("AGENT: Ofsted", StringComparison.Ordinal) &&
                d["Context"].Contains("AGENT: Trust", StringComparison.Ordinal) &&
                d["IncludedSections"].Contains("Establishment", StringComparison.Ordinal) &&
                d["IncludedSections"].Contains("Ofsted", StringComparison.Ordinal) &&
                d["IncludedSections"].Contains("Trust", StringComparison.Ordinal) &&
                !d["IncludedSections"].Contains("-agent", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_RunsSynthesisAsAnEphemeralAgent_RatherThanResolvingAProvisionedOne()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentRuntime.Received(1).RunEphemeralAsync(
            Arg.Is<AgentSpec>(spec => spec.Name == SynthesisAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_UsesSynthesisInstructionsFromThePromptService()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        _promptProvider.GetSystemPrompt(SystemPromptType.Synthesis).Returns("synthesis-instructions");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("synthesis-instructions", _capturedSynthesisSpec?.Instructions);
    }

    [Fact]
    public async Task GetBriefing_DoesNotRunTheWebSearchAgent_WhenNotAllowed()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: false);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentRuntime.DidNotReceive().RunEphemeralAsync(
            Arg.Is<AgentSpec>(spec => spec.Name == WebSearchAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_RunsTheWebSearchAgent_WhenAllowed_AndIncludesItInSynthesis()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");
        SetUpWebSearchAgentResult("web search findings");

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await _agentRuntime.Received(1).RunEphemeralAsync(
            Arg.Is<AgentSpec>(spec => spec.Name == WebSearchAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => 
                d["IncludedSections"].Contains("Web Search", StringComparison.Ordinal) &&
                d["Context"].Contains("web search findings", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_UsesWebSearchInstructionsFromThePromptService()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");
        _promptProvider.GetSystemPrompt(SystemPromptType.WebSearch).Returns("web-search-instructions");
        SetUpWebSearchAgentResult("web search findings");

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("web-search-instructions", _capturedWebSearchInstructions);
    }

    [Fact]
    public async Task GetBriefing_WebSearchPrompt_IncludesTheAlreadyCompiledSpecialistFindings()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment findings unique to this test");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");
        SetUpWebSearchAgentResult("web search findings");

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        await sut.GetBriefing(parameters, cancellationToken: cancellationToken);
        Assert.NotNull(_capturedWebSearchPrompt);
        Assert.Contains("establishment findings unique to this test", _capturedWebSearchPrompt, StringComparison.Ordinal);
        Assert.Contains("ofsted-output", _capturedWebSearchPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetBriefing_WaitsForSpecialistsToFinish_BeforeRunningWebSearch()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");

        var ofstedGate = new TaskCompletionSource<AgentResult>();
        _agentRunner.RunAsync(Arg.Is<AgentReference>(agent => agent.Name == Ofsted.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ => ofstedGate.Task);
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");
        SetUpWebSearchAgentResult("web search findings");

        var webSearchCalled = false;
        _agentRuntime.When(x => x.RunEphemeralAsync(
                Arg.Is<AgentSpec>(spec => spec.Name == WebSearchAgentName), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => webSearchCalled = true);

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);
        var briefingTask = sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        await Task.Delay(50, cancellationToken);
        Assert.False(webSearchCalled);

        ofstedGate.SetResult(new AgentResult(Ofsted.Name, "ofsted-output", 10));
        await briefingTask;

        Assert.True(webSearchCalled);
    }

    [Fact]
    public async Task GetBriefing_RunsNonManagedSpecialists_AsEphemeralAgents()
    {
        var sut = CreateSut([Establishment, Ofsted, Trust, RiseConcerns]);
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpEphemeralSpecialistResult(RiseConcerns.Name, "rise-output");
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
        await _agentRuntime.Received(1).RunEphemeralAsync(
            Arg.Is<AgentSpec>(spec => spec.Name == RiseConcerns.Name), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _promptBuilder.Received(1).Build(Arg.Any<string>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["Context"].Contains("rise-output", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetBriefing_EphemeralSpecialistFailure_DoesNotFailWholeBriefing()
    {
        var sut = CreateSut([Establishment, Ofsted, Trust, RiseConcerns]);
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        _agentRuntime.RunEphemeralAsync(Arg.Is<AgentSpec>(spec => spec.Name == RiseConcerns.Name), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("rise-concerns-agent failed"));
        SetUpSynthesisAgentResult("final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
    }

    [Fact]
    public async Task GetBriefing_WebSearchAgentFailure_DoesNotFailTheBriefing()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpSynthesisAgentResult("final-briefing");
        SetUpWebSearchAgentFailure();

        var parameters = new BriefingParameters("123456", "Test Academy", AllowWebSearch: true);

        var result = await sut.GetBriefing(parameters, cancellationToken: cancellationToken);

        Assert.Equal("final-briefing", result.Output);
    }
}
