using Dfe.Ai.Agents.Models;

namespace Dfe.Ai.Agents.Factories;

/// <summary>
/// Creates or retrieves agents based on their specifications.
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Gets an existing agent matching the specification or creates a new one.
    /// </summary>
    /// <param name="spec">The specification for the agent to get or create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reference to the agent.</returns>
    Task<AgentReference> GetOrCreateAsync(AgentSpec spec, CancellationToken cancellationToken = default);
}
