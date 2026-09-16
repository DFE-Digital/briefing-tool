namespace Dfe.Ai.Agents.Tools.Mcp;

/// <summary>
/// Configures a connection to an MCP server.
/// </summary>
public sealed record McpServerConnectionOptions
{
    /// <summary>The unique label used for logging and diagnostics.</summary>
    public required string ServerLabel { get; init; }

    /// <summary>The MCP server endpoint.</summary>
    public required Uri ServerUri { get; init; }

    /// <summary>
    /// The allowed tool names. If null or empty, all tools are allowed.
    /// This is not an authorization boundary; the server must enforce access independently.
    /// </summary>
    public IReadOnlyList<string>? AllowedToolNames { get; init; }

    /// <summary>
    /// The tool list cache duration. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan ToolListCacheDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The MCP protocol version to use.</summary>
    public string ProtocolVersion { get; init; } = "2025-11-25";

    /// <summary>Whether tool calls require explicit approval.</summary>
    public bool RequireApproval { get; init; }

    /// <summary>The authentication configuration for the MCP server.</summary>
    public required McpServerAuthenticationConfig Authentication { get; init; }
}

/// <summary>
/// Configures Azure AD client-credentials authentication for an MCP server.
/// </summary>
public sealed record McpServerAuthenticationConfig
{
    /// <summary>The Azure AD tenant ID.</summary>
    public required string TenantId { get; init; }

    /// <summary>The Azure AD client ID.</summary>
    public required string ClientId { get; init; }

    /// <summary>The Azure AD client secret.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>The scope requested for the access token.</summary>
    public required string Scope { get; init; }
}