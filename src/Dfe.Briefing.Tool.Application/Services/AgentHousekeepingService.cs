using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Factories.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;

internal sealed partial class AgentHousekeepingService(IAgentRuntimeService agentRuntime, IAgentFactory agentFactory,
    ILogger<AgentHousekeepingService> logger) : IAgentHousekeepingService
{
    public async Task<IReadOnlyList<string>> DeleteOrphanedAgentsAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await agentRuntime.DeleteOrphanedEphemeralAgentsAsync(null, cancellationToken);
        return deleted;
    }

    public async Task PruneOldVersionsAsync(int keep, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't prune old versions of {AgentName}; the next sweep tries again")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception, string agentName);
}
