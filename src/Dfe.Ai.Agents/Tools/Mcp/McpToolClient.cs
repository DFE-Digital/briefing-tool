using Dfe.Ai.Agents.Constants;
using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Responses;
using System.Text;

namespace Dfe.Ai.Agents.Tools.Mcp;

public sealed class McpToolClient : IMcpToolClient
{
    internal const string HttpClientName = "Dfe.Ai.Agents.McpToolClient";

    private readonly McpServerConnectionOptions _options;
    private readonly ITokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpToolClient> _logger;
    private readonly Lazy<Task<McpClient>> _client;
    private readonly SemaphoreSlim _toolNamesLock = new(1, 1);
    private HashSet<string>? _cachedToolNames;
    private DateTimeOffset _toolNamesCachedAt;

    public McpToolClient(McpServerConnectionOptions options, ITokenService tokenService, IHttpClientFactory httpClientFactory,
        ILogger<McpToolClient> logger)
    {
        _options = options;
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _client = new Lazy<Task<McpClient>>(ConnectAsync);
    }

    public async Task<IReadOnlyList<ResponseTool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var client = await _client.Value.ConfigureAwait(false);
        var availableNames = await GetAvailableToolNamesAsync(client, cancellationToken).ConfigureAwait(false);

        McpToolFilter? allowedTools = null;
        if (_options.AllowedToolNames is { Count: > 0 } names)
        {
            var missing = names.Where(name => !availableNames.Contains(name)).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Format(ErrorMessages.McpToolsNotFoundOnServer, _options.ServerLabel, string.Join(", ", missing)));
            }

            allowedTools = new McpToolFilter();
            foreach (var name in names)
            {
                allowedTools.ToolNames.Add(name);
            }
        }

        var approvalPolicy = new McpToolCallApprovalPolicy(
            new GlobalMcpToolCallApprovalPolicy(_options.RequireApproval ? "always" : "never"));
         
        var authorizationToken = await _tokenService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        var tool = ResponseTool.CreateMcpTool(
            serverLabel: _options.ServerLabel,
            serverUri: _options.ServerUri,
            serverDescription: null,
            authorizationToken: authorizationToken,
            allowedTools: allowedTools,
            toolCallApprovalPolicy: approvalPolicy);

        return [tool];
    }

    public async Task<string> GetPromptAsync(string name, string promptType,
        CancellationToken cancellationToken = default)
    {
        var client = await _client.Value.ConfigureAwait(false);
        var result = await client.GetPromptAsync(name, new Dictionary<string, object?>
            {
                { "promptType", promptType }
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = new StringBuilder();
        foreach (var message in result.Messages)
        {
            if (message.Content is TextContentBlock textContent)
            {
                text.AppendLine(textContent.Text);
            }
        }

        return text.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        _toolNamesLock.Dispose();

        if (!_client.IsValueCreated)
        {
            return;
        }

        var client = await _client.Value.ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<HashSet<string>> GetAvailableToolNamesAsync(McpClient client, CancellationToken cancellationToken)
    {
        if (_cachedToolNames is not null && DateTimeOffset.UtcNow - _toolNamesCachedAt < _options.ToolListCacheDuration)
        {
            return _cachedToolNames;
        }

        await _toolNamesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToolNames is not null && DateTimeOffset.UtcNow - _toolNamesCachedAt < _options.ToolListCacheDuration)
            {
                return _cachedToolNames;
            }

            var serverTools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _cachedToolNames = [.. serverTools.Select(t => t.Name)];
            _toolNamesCachedAt = DateTimeOffset.UtcNow;
            return _cachedToolNames;
        }
        finally
        {
            _toolNamesLock.Release();
        }
    }

    private async Task<McpClient> ConnectAsync()
    { 
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = _options.ServerUri,
            Name = _options.ServerLabel,
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        var transport = new HttpClientTransport(transportOptions, httpClient);

        try
        {
            var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = _options.ProtocolVersion })
                .ConfigureAwait(false);
            
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server '{ServerLabel}' at {ServerUri}", _options.ServerLabel, _options.ServerUri);
            throw;
        }
    }
}
