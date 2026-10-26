using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Services.Base;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;

/// <summary>
/// Prunes old versions of this app's agents in Foundry, keeping only the latest N versions, daily at
/// Briefing:AgentVersionSweepTime (UTC).
/// </summary>
/// <param name="housekeeping">Service for agent housekeeping operations.</param>
/// <param name="configuration">Application configuration provider.</param>
/// <param name="options">Options for briefing and scheduling.</param>
/// <param name="timeProvider">Provides time-related functionality.</param>
/// <param name="logger">Logger for diagnostic and operational messages.</param>
internal sealed partial class AgentVersionSweepService(IAgentHousekeepingService housekeeping, IConfiguration configuration,
    BriefingOptions options, TimeProvider timeProvider, ILogger<AgentVersionSweepService> logger) : BaseBackgroundDailyJob(timeProvider, logger)
{
    public const string KeepLatestVersionsKey = "AiAgents:KeepLatestVersions";

    protected override string Name => "Agent version sweep";

    protected override TimeOnly RunAt => options.AgentVersionSweepTime;

    internal override async Task RunAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<int?>(KeepLatestVersionsKey) is not ({ } keep and > 0))
        {
            LogNotConfigured(logger, KeepLatestVersionsKey);
            return;
        }

        await housekeeping.PruneOldVersionsAsync(keep, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old agent versions weren't pruned: {Setting} isn't set")]
    private static partial void LogNotConfigured(ILogger logger, string setting);
}
