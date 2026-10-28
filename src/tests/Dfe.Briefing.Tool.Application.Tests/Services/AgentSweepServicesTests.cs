using System.Globalization;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services;
using Dfe.Briefing.Tool.Application.Services.Base;
using GovUK.Dfe.AI.Agents.Factories.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Services;

public sealed class AgentSweepServicesTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentRuntimeService _agentRuntime = Substitute.For<IAgentRuntimeService>();
    private readonly IAgentFactory _agentFactory = Substitute.For<IAgentFactory>();

    private AgentSweepService CreateSweep(bool orphanSweepEnabled = true, bool versionSweepEnabled = true,
        int? keepLatestVersions = 3, int orphanMinimumAgeSeconds = 7200)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AgentSweepService.KeepLatestVersionsKey] = keepLatestVersions?.ToString(CultureInfo.InvariantCulture)
            })
            .Build();
        var options = new BriefingOptions
        {
            OrphanAgentSweepEnabled = orphanSweepEnabled,
            OrphanAgentMinimumAge = orphanMinimumAgeSeconds,
            AgentVersionSweepEnabled = versionSweepEnabled
        };

        return new AgentSweepService(_agentRuntime, _agentFactory, configuration, options, TimeProvider.System,
            NullLogger<AgentSweepService>.Instance);
    }

    [Fact]
    public async Task AgentSweep_RunsBothSweeps_KeepingTheConfiguredNumberOfVersions()
    {
        await CreateSweep().RunAsync(cancellationToken);

        await _agentRuntime.Received(1).DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromHours(2), cancellationToken);
        foreach (var agent in BriefingAgents.All)
            await _agentFactory.Received(1).PruneVersionsAsync(agent.Name, 3, cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_DeletesOrphansOlderThanTheConfiguredMinimumAge()
    {
        await CreateSweep(orphanMinimumAgeSeconds: 2700).RunAsync(cancellationToken);

        await _agentRuntime.Received(1).DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromMinutes(45), cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_WithOrphanSweepOff_OnlyPrunesVersions()
    {
        await CreateSweep(orphanSweepEnabled: false).RunAsync(cancellationToken);

        await _agentRuntime.DidNotReceiveWithAnyArgs().DeleteOrphanedEphemeralAgentsAsync(default, cancellationToken);
        await _agentFactory.ReceivedWithAnyArgs(BriefingAgents.All.Count()).PruneVersionsAsync(default!, default, cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_WithVersionSweepOff_OnlyDeletesOrphans()
    {
        await CreateSweep(versionSweepEnabled: false).RunAsync(cancellationToken);

        await _agentRuntime.Received(1).DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromHours(2), cancellationToken);
        await _agentFactory.DidNotReceiveWithAnyArgs().PruneVersionsAsync(default!, default, cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_WhenOrphanSweepFails_StillPrunesVersions()
    {
        _agentRuntime.DeleteOrphanedEphemeralAgentsAsync(Arg.Any<TimeSpan>(), cancellationToken)
            .ThrowsAsync(new InvalidOperationException("Foundry is down"));

        await CreateSweep().RunAsync(cancellationToken);

        await _agentFactory.ReceivedWithAnyArgs(BriefingAgents.All.Count()).PruneVersionsAsync(default!, default, cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_WhenOneAgentFailsToPrune_StillPrunesTheOthers()
    {
        var failing = BriefingAgents.All[0].Name;
        _agentFactory.PruneVersionsAsync(failing, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Foundry unavailable"));

        await CreateSweep(keepLatestVersions: 2).RunAsync(cancellationToken);

        foreach (var agent in BriefingAgents.All.Skip(1))
            await _agentFactory.Received(1).PruneVersionsAsync(agent.Name, 2, cancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task AgentSweep_WithoutKeepLatestVersions_PrunesNothing(int? keepLatestVersions)
    {
        await CreateSweep(keepLatestVersions: keepLatestVersions).RunAsync(cancellationToken);

        await _agentFactory.DidNotReceiveWithAnyArgs().PruneVersionsAsync(default!, default, cancellationToken);
    }

    [Theory]
    [InlineData("2026-10-26T00:30:00Z", "01:00", 0.5)]   // later today
    [InlineData("2026-10-26T02:00:00Z", "02:00", 24)]    // exactly now: tomorrow's
    [InlineData("2026-10-26T03:30:00Z", "02:00", 22.5)]  // passed today: tomorrow's
    public void UntilNext_GivesTheTimeToTheNextRun(string now, string at, double hours)
        => Assert.Equal(TimeSpan.FromHours(hours),
            BaseBackgroundDailyJob.UntilNext(TimeOnly.Parse(at, CultureInfo.InvariantCulture), DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));
}
