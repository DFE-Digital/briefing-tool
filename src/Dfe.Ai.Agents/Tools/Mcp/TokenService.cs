using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Dfe.Ai.Agents.Tools.Mcp;

public class TokenService(McpServerConnectionOptions mcpServerConnectionOptions, HttpClient httpClient) : ITokenService
{
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-1))
            return _cachedToken;

        var tokenEndpoint = $"https://login.microsoftonline.com/{mcpServerConnectionOptions.Authentication.TenantId}/oauth2/v2.0/token";

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = mcpServerConnectionOptions.Authentication.ClientId,
            ["client_secret"] = mcpServerConnectionOptions.Authentication.ClientSecret,
            ["scope"] = mcpServerConnectionOptions.Authentication.Scope
        });

        var response = await httpClient.PostAsync(tokenEndpoint, body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize token response");

        _cachedToken = result.AccessToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(result.ExpiresIn);

        return _cachedToken;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn
    );
}
