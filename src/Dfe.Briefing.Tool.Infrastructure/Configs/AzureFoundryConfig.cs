namespace Dfe.Briefing.Tool.Infrastructure.Configs;

public sealed class AzureFoundryConfig
{
    public required string Endpoint { get; init; }

    public required string ApiKey { get; init; }

    public required string DeploymentModel { get; init; }
}
