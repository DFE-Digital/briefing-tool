using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Models;
using Dfe.Ai.Agents.Orchestration;
using Dfe.Ai.Agents.Prompts.Interfaces;
using Dfe.Briefing.Tool.Application.Exceptions;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Orchestrators;

public sealed class BriefingOrchestratorTests
{
    private static readonly AgentDefinition Establishment = new("establishment-agent", SystemPromptType.Establishment, EvidenceCategory.Establishment, "{{SchoolOrTrustName}}");
    private static readonly AgentDefinition Ofsted = new("ofsted-agent", SystemPromptType.Ofsted, EvidenceCategory.Ofsted, "{{SchoolOrTrustName}} Ofsted");
    private static readonly AgentDefinition Trust = new("trust-agent", SystemPromptType.Trust, EvidenceCategory.Trust, "{{SchoolOrTrustName}} Trust");
    private static readonly AgentDefinition Synthesis = new("briefing-synthesis-agent", SystemPromptType.Synthesis);

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentDefinitionProvider _definitionProvider = Substitute.For<IAgentDefinitionProvider>();
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly IPromptRetrieverService _promptRetrieverService = Substitute.For<IPromptRetrieverService>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly IAgentRunner _agentRunner = Substitute.For<IAgentRunner>();

    private BriefingOrchestrator CreateSut()
    {
        _definitionProvider.GetAgentsDefinitions().Returns([Establishment, Ofsted, Trust]); 

        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("evidence", HasEvidence: true)));

        _promptRetrieverService.GetSystemPrompt(Arg.Any<string>()).Returns("system-instructions");
        _promptBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("synthesis-prompt");

        return new BriefingOrchestrator(_definitionProvider, _contextRetriever, _promptRetrieverService,
            _promptBuilder, _agentRunner, new AgentOrchestrator(_agentRunner));
    }

    private void SetUpAgentResult(string agentName, string output, long totalTokens = 10)
        => _agentRunner.RunAsync(Arg.Is<AgentSpec>(spec => spec.Name == agentName), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentResult(agentName, output, totalTokens)));

    private void SetUpAgentFailure(string agentName)
        => _agentRunner.RunAsync(Arg.Is<AgentSpec>(spec => spec.Name == agentName), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException($"{agentName} failed"));

    [Fact]
    public async Task GetBriefing_AlwaysRunsEstablishmentAndEverySpecialist()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentResult(Ofsted.Name, "ofsted-output");
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpAgentResult(Synthesis.Name, "final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        await sut.GetBriefing(parameters, cancellationToken);

        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentSpec>(spec => spec.Name == Establishment.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentSpec>(spec => spec.Name == Ofsted.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
        await _agentRunner.Received(1).RunAsync(Arg.Is<AgentSpec>(spec => spec.Name == Trust.Name), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_FailedSpecialistAgent_DoesNotFailWholeBriefing()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output");
        SetUpAgentFailure(Ofsted.Name);
        SetUpAgentResult(Trust.Name, "trust-output");
        SetUpAgentResult(Synthesis.Name, "final-briefing");

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken);

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetBriefing(parameters, cancellationToken));
    }

    [Fact]
    public async Task GetBriefing_NoEstablishmentEvidence_ThrowsNoBriefingDataFoundException_WithoutRunningAnyAgent()
    {
        var sut = CreateSut();
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("No Establishment information found.", HasEvidence: false)));

        var parameters = new BriefingParameters("123456", "Test Academy");

        await Assert.ThrowsAsync<NoBriefingDataFoundException>(() => sut.GetBriefing(parameters, cancellationToken));
        await _agentRunner.DidNotReceive().RunAsync(Arg.Any<AgentSpec>(), Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBriefing_SumsTokensAcrossAllAgents()
    {
        var sut = CreateSut();
        SetUpAgentResult(Establishment.Name, "establishment-output", totalTokens: 100);
        SetUpAgentResult(Ofsted.Name, "ofsted-output", totalTokens: 50);
        SetUpAgentResult(Trust.Name, "trust-output", totalTokens: 20);
        SetUpAgentResult(Synthesis.Name, "final-briefing", totalTokens: 25);

        var parameters = new BriefingParameters("123456", "Test Academy");

        var result = await sut.GetBriefing(parameters, cancellationToken);

        Assert.Equal(195, result.TotalTokens);
    }
}
