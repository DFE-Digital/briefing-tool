using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Dfe.Ai.Agents.Constants;
using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.Ai.Agents.Context;

public sealed class AzureSearchContextRetriever(IReadOnlyDictionary<string, SearchClient> clients, IRelevanceFilter relevanceFilter,
    ILogger<AzureSearchContextRetriever>? logger = null) : IContextRetriever
{ 
    private readonly ILogger<AzureSearchContextRetriever> _logger = logger ?? NullLogger<AzureSearchContextRetriever>.Instance;
     
    public async Task<ContextResult> GetContextAsync(string scope, string query, int size = 10, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        if (!clients.TryGetValue(scope, out var client))
        {
            throw new InvalidOperationException(string.Format(ErrorMessages.NoAzureSearchClientConfigured, scope));
        }

        var results = await SearchAsync(client, scope, query, size, cancellationToken); 
        if (relevanceFilter.Filter(results).Count == 0)
        {
            return new ContextResult(string.Format(ErrorMessages.NoAzureSearchInformationFound, scope), HasEvidence: false);
        }
        return new ContextResult(string.Join(Environment.NewLine + Environment.NewLine, relevanceFilter.Filter(results).Select((item, i)
            => $"--- {scope} Evidence {i + 1} ---\n{item.Content}")), HasEvidence: true);
    }

    private async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchClient client, string scope, string query, int size, CancellationToken cancellationToken)
    {
        var searchOptions = new SearchOptions { Size = size };
        var results = new List<SearchResultItem>();

        try
        {
            SearchResults<SearchDocument> response = await client.SearchAsync<SearchDocument>(query, searchOptions, cancellationToken);

            await foreach (var result in response.GetResultsAsync().WithCancellation(cancellationToken))
            {
                var content = ExtractContent(result.Document);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    results.Add(new SearchResultItem(content, result.Score));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Azure Search query against {Scope} failed", scope);
            throw;
        }

        return results;
    }

    private static string? ExtractContent(SearchDocument document)
        => string.Join(" ", document
            .Where(kv => kv.Value is string && !string.IsNullOrWhiteSpace(kv.Value?.ToString()))
            .Select(kv => kv.Value.ToString()));
}
