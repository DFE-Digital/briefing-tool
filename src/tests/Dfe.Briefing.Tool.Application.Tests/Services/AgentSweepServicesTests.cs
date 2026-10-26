using System.Globalization;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Services;
using Dfe.Briefing.Tool.Application.Services.Base;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Services;

public sealed class AgentSweepServicesTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentHousekeepingService _housekeeping = Substitute.For<IAgentHousekeepingService>();

    private AgentVersionSweepService CreateVersionSweep(int? keepLatestVersions)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AgentVersionSweepService.KeepLatestVersionsKey] = keepLatestVersions?.ToString(CultureInfo.InvariantCulture)
            })
            .Build();

        return new AgentVersionSweepService(_housekeeping, configuration, new BriefingOptions(), TimeProvider.System,
            NullLogger<AgentVersionSweepService>.Instance);
    }

    [Fact]
    public async Task OrphanAgentSweep_DeletesOrphanedAgents()
    {
        var sut = new OrphanAgentSweepService(_housekeeping, new BriefingOptions(), TimeProvider.System,
            NullLogger<OrphanAgentSweepService>.Instance);

        await sut.RunAsync(cancellationToken);

        await _housekeeping.Received(1).DeleteOrphanedAgentsAsync(cancellationToken);
        await _housekeeping.DidNotReceiveWithAnyArgs().PruneOldVersionsAsync(default, cancellationToken);
    }

    [Fact]
    public async Task AgentVersionSweep_PrunesOldVersions_KeepingTheConfiguredNumber()
    {
        await CreateVersionSweep(keepLatestVersions: 3).RunAsync(cancellationToken);

        await _housekeeping.Received(1).PruneOldVersionsAsync(3, cancellationToken);
        await _housekeeping.DidNotReceiveWithAnyArgs().DeleteOrphanedAgentsAsync(cancellationToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task AgentVersionSweep_WithoutKeepLatestVersions_PrunesNothing(int? keepLatestVersions)
    {
        await CreateVersionSweep(keepLatestVersions).RunAsync(cancellationToken);

        await _housekeeping.DidNotReceiveWithAnyArgs().PruneOldVersionsAsync(default, cancellationToken);
    }

    [Theory]
    [InlineData("2026-10-26T00:30:00Z", "01:00", 0.5)]   // later today
    [InlineData("2026-10-26T02:00:00Z", "02:00", 24)]    // exactly now: tomorrow's
    [InlineData("2026-10-26T03:30:00Z", "02:00", 22.5)]  // passed today: tomorrow's
    public void UntilNext_GivesTheTimeToTheNextRun(string now, string at, double hours)
        => Assert.Equal(TimeSpan.FromHours(hours),
            BaseBackgroundDailyJob.UntilNext(TimeOnly.Parse(at, CultureInfo.InvariantCulture), DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));
}
