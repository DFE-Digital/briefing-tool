using System.Globalization;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services;
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

    // A Sunday, and the Monday after it, in UTC.
    private static readonly DateTimeOffset Sunday = new(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = Sunday.AddDays(1);

    private AgentSweepService CreateSweep(bool orphanSweepEnabled = true, bool versionSweepEnabled = true,
        int? keepLatestVersions = 3, int orphanMinimumAgeSeconds = 7200, DayOfWeek? versionSweepDay = null, DateTimeOffset? now = null)
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
            AgentVersionSweepEnabled = versionSweepEnabled,
            AgentVersionSweepDay = versionSweepDay
        };

        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(now ?? Sunday);

        return new AgentSweepService(_agentRuntime, _agentFactory, configuration, options, timeProvider,
            NullLogger<AgentSweepService>.Instance);
    }

    [Theory]
    [InlineData(false, true)]   // the version sweep day: both sweeps, keeping the configured number of versions
    [InlineData(true, false)]   // another day: only orphans
    public async Task AgentSweep_DeletesOrphansEveryDay_AndPrunesVersionsOnlyOnTheVersionSweepDay(bool onMonday, bool prunes)
    {
        await CreateSweep(versionSweepDay: DayOfWeek.Sunday, now: onMonday ? Monday : Sunday).RunAsync(cancellationToken);

        await _agentRuntime.Received(1).DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromHours(2), cancellationToken);
        foreach (var agent in BriefingAgents.All)
            await _agentFactory.Received(prunes ? 1 : 0).PruneVersionsAsync(agent.Name, 3, cancellationToken);
    }

    [Theory]
    [MemberData(nameof(EveryDay))]
    public async Task AgentSweep_WithoutAVersionSweepDay_PrunesVersionsEveryDay(DateTimeOffset now)
    {
        await CreateSweep(versionSweepDay: null, now: now).RunAsync(cancellationToken);

        await _agentFactory.ReceivedWithAnyArgs(BriefingAgents.All.Length).PruneVersionsAsync(default!, default, cancellationToken);
    }

    public static TheoryData<DateTimeOffset> EveryDay => [.. Enumerable.Range(0, 7).Select(days => Sunday.AddDays(days))];

    [Fact]
    public async Task AgentSweep_UsesTheUtcDay_NotTheLocalOne()
    {
        // 23:30 on Saturday in UTC is already Sunday an hour ahead, but the sweep runs on UTC's day.
        var saturdayNightUtc = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(1));

        await CreateSweep(versionSweepDay: DayOfWeek.Sunday, now: saturdayNightUtc).RunAsync(cancellationToken);

        await _agentFactory.DidNotReceiveWithAnyArgs().PruneVersionsAsync(default!, default, cancellationToken);
    }

    [Fact]
    public async Task AgentSweep_DeletesOrphansOlderThanTheConfiguredMinimumAge()
    {
        await CreateSweep(orphanMinimumAgeSeconds: 2700).RunAsync(cancellationToken);

        await _agentRuntime.Received(1).DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromMinutes(45), cancellationToken);
    }

    [Theory]
    [InlineData(false, true)]   // orphan sweep off: only versions are pruned
    [InlineData(true, false)]   // version sweep off: only orphans are deleted
    public async Task AgentSweep_RunsOnlyTheSweepThatIsOn(bool orphanSweepEnabled, bool versionSweepEnabled)
    {
        await CreateSweep(orphanSweepEnabled, versionSweepEnabled).RunAsync(cancellationToken);

        await _agentRuntime.ReceivedWithAnyArgs(orphanSweepEnabled ? 1 : 0).DeleteOrphanedEphemeralAgentsAsync(default, cancellationToken);
        await _agentFactory.ReceivedWithAnyArgs(versionSweepEnabled ? BriefingAgents.All.Length : 0).PruneVersionsAsync(default!, default, cancellationToken);
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
}
