using Azure.AI.Projects.Agents;
using Dfe.Ai.Agents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Net;

namespace Dfe.Ai.Agents.Factories;

public sealed class FoundryAgentFactory(AgentAdministrationClient administrationClient, FoundryAgentFactoryOptions options,
    ILogger<FoundryAgentFactory>? logger = null) : IAgentFactory
{
    private readonly ILogger<FoundryAgentFactory> _logger = logger ?? NullLogger<FoundryAgentFactory>.Instance;
    private readonly ConcurrentDictionary<string, Lazy<Task<AgentReference>>> _cache = [];

    public async Task<AgentReference> GetOrCreateAsync(AgentSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Instructions);

        var cacheKey = ComputeCacheKey(spec);

        var cached = _cache.GetOrAdd(cacheKey, _ => new Lazy<Task<AgentReference>>(() => GetOrCreateOnFoundryAsync(spec, cancellationToken)));

        try
        {
            return await cached.Value.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create Foundry agent for {AgentName}", spec.Name);
            _cache.TryRemove(new KeyValuePair<string, Lazy<Task<AgentReference>>>(cacheKey, cached));
            throw;
        }
    }

    private async Task<AgentReference> GetOrCreateOnFoundryAsync(AgentSpec spec, CancellationToken cancellationToken)
    {
        var existing = await FindMatchingVersionAsync(spec, cancellationToken);
        if (existing is not null)
        {
            return new AgentReference(existing.Id, spec.Name);
        }

        return await CreateAgentVersionAsync(spec, cancellationToken);
    }

    /// <summary>
    /// Finds the latest version of the agent named <c>spec.Name</c> on Foundry if it matches the
    /// given spec's model and instructions, or returns null if the agent doesn't exist yet or its
    /// latest version doesn't match.
    /// </summary>
    /// <param name="spec">The agent specification to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching agent version, or null if none matches.</returns>
    private async Task<ProjectsAgentVersion?> FindMatchingVersionAsync(AgentSpec spec, CancellationToken cancellationToken)
    {
        var model = spec.Model ?? options.DefaultModel;

        ProjectsAgentRecord record;
        try
        {
            record = (await administrationClient.GetAgentAsync(spec.Name, cancellationToken).ConfigureAwait(false)).Value;
        }
        catch (ClientResultException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }

        var latest = record.GetLatestVersion();
        if (latest.Definition is DeclarativeAgentDefinition definition
            && definition.Model == model
            && definition.Instructions == spec.Instructions)
        {
            return latest;
        }

        return null;
    }

    /// <summary>
    /// Creates a new version of the agent named <c>spec.Name</c> on Foundry based on the given spec.
    /// </summary>
    /// <param name="spec">The agent specification to use for creation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created agent reference.</returns>
    private async Task<AgentReference> CreateAgentVersionAsync(AgentSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            var definition = new DeclarativeAgentDefinition(model: spec.Model ?? options.DefaultModel)
            {
                Instructions = spec.Instructions,
            };
            foreach (var tool in spec.Tools)
            {
                definition.Tools.Add(tool);
            }

            var createOptions = new ProjectsAgentVersionCreationOptions(definition) { Description = spec.Description };
            var version = (await administrationClient.CreateAgentVersionAsync(spec.Name, createOptions, null, cancellationToken)
                .ConfigureAwait(false)).Value;

            return new AgentReference(version.Id, spec.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create Foundry agent for {AgentName}", spec.Name);
            throw;
        }
    }

    /// <summary>
    /// Computes a unique cache key for the given agent specification. The cache key is based on the agent's name, model, instructions, and tool signatures.
    /// </summary>
    /// <param name="spec">The agent specification.</param>
    /// <returns>The cache key.</returns>
    private static string ComputeCacheKey(AgentSpec spec)
    {
        var toolSignatures = spec.Tools.Select(tool => ModelReaderWriter.Write(tool).ToString());
        return string.Concat([spec.Name, spec.Model, spec.Instructions, .. toolSignatures]);
    }
}
