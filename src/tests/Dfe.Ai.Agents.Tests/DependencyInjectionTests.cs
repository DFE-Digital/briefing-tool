using Azure.AI.Projects;
using Azure.Core;
using Dfe.Ai.Agents.Agents;
using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Context;
using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Factories;
using Dfe.Ai.Agents.Orchestration;
using Dfe.Ai.Agents.Tools.Mcp;
using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dfe.Ai.Agents.Tests;

/// <summary>
/// Builds a real <see cref="ServiceCollection"/>/<see cref="IConfiguration"/> and calls each
/// <c>DependencyInjection.Add*</c> method the way a real consumer would, then resolves the public
/// service types - proving the wiring actually works (correct types, config binds, resolution
/// doesn't throw) rather than just reading the registration code and assuming it does.
/// </summary>
public sealed class DependencyInjectionTests
{
    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    [Fact]
    public void AddFoundryAgents_WithEndpointAndCredential_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();

        services.AddFoundryAgents(
            endpoint: _ => new Uri("https://example.services.ai.azure.com/api/projects/test"),
            credential: _ => new FakeTokenCredential(),
            optionsFactory: _ => new FoundryAgentFactoryOptions("gpt-4o"));

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<AIProjectClient>());
        Assert.IsType<FoundryAgentFactory>(provider.GetRequiredService<IAgentFactory>());
        Assert.IsType<FoundryConversationClient>(provider.GetRequiredService<IFoundryConversationClient>());
        Assert.IsType<FoundryAgentRunner>(provider.GetRequiredService<IAgentRunner>());
        Assert.NotNull(provider.GetRequiredService<AgentOrchestrator>());
    }

    [Fact]
    public void AddAzureSearchContextRetriever_RegistersRealRetrieverAndFilter_FromValidConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureSearch:Endpoint"] = "https://example.search.windows.net",
                ["AzureSearch:TenantId"] = "tenant-1",
                ["AzureSearch:ClientId"] = "client-1",
                ["AzureSearch:ClientSecret"] = "secret-1",
                ["AzureSearch:Indexes:0"] = "establishment-index",
                ["AzureSearch:Indexes:1"] = "ofsted-index",
            })
            .Build();

        services.AddAzureSearchContextRetriever(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RelativeScoreRelevanceFilter>(provider.GetRequiredService<IRelevanceFilter>());
        Assert.IsType<AzureSearchContextRetriever>(provider.GetRequiredService<IContextRetriever>());
    }

    [Fact]
    public void AddAzureSearchContextRetriever_ThrowsOnResolve_WhenARequiredConfigValueIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Endpoint deliberately omitted - AzureSearchContextRetrieverOptions.Endpoint is `required`.
                ["AzureSearch:TenantId"] = "tenant-1",
                ["AzureSearch:ClientId"] = "client-1",
                ["AzureSearch:ClientSecret"] = "secret-1",
                ["AzureSearch:Indexes:0"] = "establishment-index",
            })
            .Build();

        services.AddAzureSearchContextRetriever(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IContextRetriever>());
    }

    [Fact]
    public async Task AddMcpClientServices_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddMcpClientServices(_ => new McpServerConnectionOptions
        {
            ServerLabel = "my-tools",
            ServerUri = new Uri("https://mcp.example.com"),
            Authentication = new McpServerAuthenticationConfig
            {
                TenantId = "tenant-1",
                ClientId = "client-1",
                ClientSecret = "secret-1",
                Scope = "api://mcp/.default",
            },
        });

        // McpToolClient is IAsyncDisposable-only, so the container needs disposing asynchronously.
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<TokenService>(provider.GetRequiredService<ITokenService>());
        Assert.IsType<McpToolClient>(provider.GetRequiredService<IMcpToolClient>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is McpToolStartupValidator);
    }
}
