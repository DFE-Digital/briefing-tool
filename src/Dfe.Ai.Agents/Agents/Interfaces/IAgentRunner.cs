using Dfe.Ai.Agents.Models;

namespace Dfe.Ai.Agents.Agents.Interfaces;

/// <summary>
/// Runs agents and returns their results.
/// </summary>
public interface IAgentRunner
{
    /// <summary>
    /// Runs an agent with the specified configuration and prompt.
    /// </summary>
    /// <param name="spec">The agent configuration.</param>
    /// <param name="prompt">The prompt to run.</param>
    /// <param name="conversationId">An existing conversation to continue; null creates a new conversation.</param>
    /// <param name="resolveToolCalls">Optional callback for resolving paused tool calls.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent execution result.</returns>
    Task<AgentResult> RunAsync(AgentSpec spec, string prompt, string? conversationId = null,
        Func<IReadOnlyList<ToolCallRequest>, CancellationToken, Task<IEnumerable<ToolCallOutput>>>? resolveToolCalls = null,
        CancellationToken cancellationToken = default);
}
