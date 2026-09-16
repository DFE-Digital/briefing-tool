using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfe.Ai.Agents.Tools.Mcp;

/// <summary>
/// Validates the MCP server connection and configured tools at application startup.
/// </summary>
/// <remarks>
/// Startup fails if the MCP server is unavailable or the configured tools are invalid.
/// </remarks>
public sealed class McpToolStartupValidator(IMcpToolClient client, ILogger<McpToolStartupValidator> logger) : IHostedService
{
    /// <summary>
    /// Validates the MCP server connection and configured tools at application startup.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.GetToolsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MCP tool configuration validation failed during startup.");
            throw;
        }
    }
    /// <summary>
    /// Stops the hosted service. This implementation does nothing and returns a completed task.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task representing the asynchronous operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
