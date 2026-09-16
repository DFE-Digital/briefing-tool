namespace Dfe.Ai.Agents.Tools.Mcp.Interfaces;

/// <summary>
/// Discovers tools and prompts from a remote MCP server.
/// </summary>
public interface IMcpToolClient : IAgentToolProvider, IAsyncDisposable
{
    /// <summary>
    /// Gets a prompt from the MCP server.
    /// </summary>
    /// <param name="name">The prompt name.</param>
    /// <param name="promptType">The prompt type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The prompt text.</returns>
    Task<string> GetPromptAsync(string name, string promptType, CancellationToken cancellationToken = default);
}
