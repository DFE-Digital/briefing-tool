using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Services.Base;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;

/// <summary>
/// Background service that performs a daily sweep to delete orphaned agents.
/// </summary>
/// <param name="agentHousekeepingService">Service used to manage agent housekeeping operations.</param>
/// <param name="options">Configuration options for agent sweeping and scheduling.</param>
/// <param name="timeProvider">Provides time-related functionality for scheduling.</param>
/// <param name="logger">Logger instance for logging service operations.</param>
internal sealed class OrphanAgentSweepService(IAgentHousekeepingService agentHousekeepingService, BriefingOptions options,
    TimeProvider timeProvider, ILogger<OrphanAgentSweepService> logger) : BaseBackgroundDailyJob(timeProvider, logger)
{
    protected override string Name => "Orphan agent sweep";

    protected override TimeOnly RunAt => options.OrphanAgentSweepTime;

    internal override Task RunAsync(CancellationToken cancellationToken)
        => agentHousekeepingService.DeleteOrphanedAgentsAsync(cancellationToken);
}
