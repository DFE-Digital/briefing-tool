namespace Dfe.Ai.Agents.Context;

public sealed class AzureSearchContextRetrieverOptions
{
    public required string Endpoint { get; init; }

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public required IReadOnlyList<string> Indexes { get; init; }
    public double MinimumRelevanceFilter { get; init; } = 0.5;
    public int MaxRetryAttemps { get; init; } = 3;
}
