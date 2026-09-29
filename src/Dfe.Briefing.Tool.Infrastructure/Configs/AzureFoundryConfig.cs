namespace Dfe.Briefing.Tool.Infrastructure.Configs;

public sealed class AzureFoundryConfig
{
    public required string Endpoint { get; init; }

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public required string DeploymentModel { get; init; }

    /// <summary>Redacts <see cref="ClientSecret"/> so this never leaks via logging or exception messages.</summary>
    public override string ToString()
        => $"{{ Endpoint = {Endpoint}, TenantId = {TenantId}, ClientId = {ClientId}, ClientSecret = [REDACTED], DeploymentModel = {DeploymentModel} }}";
}
