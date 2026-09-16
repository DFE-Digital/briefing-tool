using Azure.Core;

namespace Dfe.Briefing.Tool.Infrastructure.Authentication;

public sealed class ApiKeyCredential(string apiKey) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(apiKey, DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(GetToken(requestContext, cancellationToken));
}
