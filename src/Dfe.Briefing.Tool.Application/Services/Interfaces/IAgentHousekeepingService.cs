namespace Dfe.Briefing.Tool.Application.Services.Interfaces;

/// <summary>
/// Provides housekeeping operations for managing temporary and versioned agents in Foundry.
/// </summary>
public interface IAgentHousekeepingService
{
    /// <summary>
    /// Deletes orphaned agents and returns the identifiers of the deleted agents.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A read-only list of identifiers for the agents that were deleted.</returns>
    Task<IReadOnlyList<string>> DeleteOrphanedAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes agent versions, retaining only the specified number of most recent versions.
    /// </summary>
    /// <param name="keep">The number of most recent agent versions to retain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <remarks>An agent that can't be pruned is logged, and the others are still pruned.</remarks>
    Task PruneOldVersionsAsync(int keep, CancellationToken cancellationToken = default);
}
