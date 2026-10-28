using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services.Base;
using GovUK.Dfe.AI.Agents.Factories.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;

/// <summary>
/// Executes scheduled sweeps to remove orphaned agents and prune old agent versions based on configuration.
/// </summary>
/// <param name="agentRuntime">Deletes orphaned temporary agents.</param>
/// <param name="agentFactory">Prunes old agent versions.</param>
/// <param name="configuration">Application configuration for sweep settings.</param>
/// <param name="options">Options controlling sweep behavior and schedule.</param>
/// <param name="timeProvider">Supplies time information for scheduling.</param>
/// <param name="logger">Logger for diagnostic and error messages.</param>
internal sealed partial class AgentSweepService(IAgentRuntimeService agentRuntime, IAgentFactory agentFactory,
    IConfiguration configuration, BriefingOptions options, TimeProvider timeProvider, ILogger<AgentSweepService> logger)
    : BaseBackgroundDailyJob(timeProvider, logger)
{
    public const string KeepLatestVersionsKey = "AiAgents:KeepLatestVersions";

    private const string OrphanAgentSweep = "Orphan agent sweep";
    private const string AgentVersionSweep = "Agent version sweep";

    protected override string Name => "Agent sweep";

    protected override TimeOnly RunAt => options.AgentSweepTime;

    internal override async Task RunAsync(CancellationToken cancellationToken)
    {
        if (options.OrphanAgentSweepEnabled)
            await RunSweepAsync(OrphanAgentSweep, () => DeleteOrphanedAgentsAsync(cancellationToken), cancellationToken);

        if (options.AgentVersionSweepEnabled)
            await RunSweepAsync(AgentVersionSweep, () => PruneOldVersionsAsync(cancellationToken), cancellationToken);
    }

    // The library refuses an age shorter than AiAgents:RunTimeout plus 90 seconds, or no RunTimeout at all.
    private Task<IReadOnlyList<string>> DeleteOrphanedAgentsAsync(CancellationToken cancellationToken)
        => agentRuntime.DeleteOrphanedEphemeralAgentsAsync(TimeSpan.FromSeconds(options.OrphanAgentMinimumAge), cancellationToken);

    private async Task PruneOldVersionsAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<int?>(KeepLatestVersionsKey) is not ({ } keep and > 0))
        {
            LogNotConfigured(logger, KeepLatestVersionsKey);
            return;
        }

        foreach (var agentName in BriefingAgents.All.Select(agent => agent.Name))
        {
            try
            {
                await agentFactory.PruneVersionsAsync(agentName, keep, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogPruneFailed(logger, ex, agentName);
            }
        }
    }

    private async Task RunSweepAsync(string sweep, Func<Task> run, CancellationToken cancellationToken)
    {
        try
        {
            await run();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogSweepFailed(logger, ex, sweep);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old agent versions weren't pruned: {Setting} isn't set")]
    private static partial void LogNotConfigured(ILogger logger, string setting);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't prune old versions of {AgentName}; the next sweep tries again")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception, string agentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Sweep} failed; it runs again tomorrow")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception, string sweep);
}
