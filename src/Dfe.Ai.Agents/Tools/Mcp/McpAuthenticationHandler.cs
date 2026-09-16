using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using System.Net.Http.Headers;

namespace Dfe.Ai.Agents.Tools.Mcp;
/// <summary>
/// Adds a fresh bearer token to each outgoing request using <see cref="ITokenService"/>.
/// </summary>
/// <remarks>
/// <see cref="ITokenService"/> handles token caching and refresh, supporting long-lived
/// connections without using an expired token.
/// </remarks>
public sealed class McpAuthenticationHandler(ITokenService tokenService) : DelegatingHandler
{
    /// <summary>
    /// Sends an HTTP request with a bearer token added to the Authorization header.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
