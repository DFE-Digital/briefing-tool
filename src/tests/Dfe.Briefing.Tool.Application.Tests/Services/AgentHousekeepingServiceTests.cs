using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services;
using GovUK.Dfe.AI.Agents.Factories.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Services;

public sealed class AgentHousekeepingServiceTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentRuntimeService _agentRuntime = Substitute.For<IAgentRuntimeService>();
    private readonly IAgentFactory _agentFactory = Substitute.For<IAgentFactory>();
    private readonly AgentHousekeepingService _sut;

    public AgentHousekeepingServiceTests()
        => _sut = new AgentHousekeepingService(_agentRuntime, _agentFactory, NullLogger<AgentHousekeepingService>.Instance);

    [Fact]
    public async Task PruneOldVersionsAsync_PrunesEveryBriefingAgent_KeepingTheLatestVersions()
    {
        await _sut.PruneOldVersionsAsync(3, cancellationToken);

        foreach (var agent in BriefingAgents.All)
            await _agentFactory.Received(1).PruneVersionsAsync(agent.Name, 3, cancellationToken);
    }

    [Fact]
    public async Task PruneOldVersionsAsync_WhenOneAgentFails_StillPrunesTheOthers()
    {
        var failing = BriefingAgents.All[0].Name;
        _agentFactory.PruneVersionsAsync(failing, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Foundry unavailable"));

        var exception = await Record.ExceptionAsync(() => _sut.PruneOldVersionsAsync(2, cancellationToken));

        Assert.Null(exception);
        foreach (var agent in BriefingAgents.All.Skip(1))
            await _agentFactory.Received(1).PruneVersionsAsync(agent.Name, 2, cancellationToken);
    }

    [Fact]
    public async Task PruneOldVersionsAsync_KeepingFewerThanOne_IsRefused()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.PruneOldVersionsAsync(0, cancellationToken));

    [Fact]
    public async Task DeleteOrphanedAgentsAsync_UsesTheLibrarysMinimumAge_AndReturnsWhatWasDeleted()
    {
        _agentRuntime.DeleteOrphanedEphemeralAgentsAsync(null, cancellationToken).Returns(["web-search-agent-1a2b"]);

        var deleted = await _sut.DeleteOrphanedAgentsAsync(cancellationToken);

        Assert.Equal(["web-search-agent-1a2b"], deleted);
    }
}
