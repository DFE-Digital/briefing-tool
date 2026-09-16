using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ClientModel.Primitives;

namespace Dfe.Ai.Agents;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Foundry Agent Service using an existing <see cref="AIProjectClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsFactory">Creates the agent factory options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddFoundryAgents(this IServiceCollection services,
        Func<IServiceProvider, FoundryAgentFactoryOptions> optionsFactory)
    {
        services.AddSingleton(optionsFactory);
        services.AddSingleton(sp => sp.GetRequiredService<AIProjectClient>().AgentAdministrationClient);
        services.AddSingleton(sp => sp.GetRequiredService<AIProjectClient>().ProjectOpenAIClient);
        services.AddSingleton<IAgentFactory, FoundryAgentFactory>();
        services.AddSingleton<IFoundryConversationClient, FoundryConversationClient>();
        services.AddSingleton<IAgentRunner, FoundryAgentRunner>();
        services.AddSingleton<AgentOrchestrator>();

        return services;
    }

    /// <summary>
    /// Registers the Foundry Agent Service and configures its AI project client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="endpoint">Creates the Foundry Agent Service endpoint.</param>
    /// <param name="credential">Creates the authentication credential.</param>
    /// <param name="optionsFactory">Creates the agent factory options.</param>
    /// <param name="maxRetries">The maximum number of client retries.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddFoundryAgents(this IServiceCollection services,
        Func<IServiceProvider, Uri> endpoint,
        Func<IServiceProvider, TokenCredential> credential,
        Func<IServiceProvider, FoundryAgentFactoryOptions> optionsFactory,
        int maxRetries = 3)
    {
        services.AddSingleton(sp =>
        {
            var options = new AIProjectClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries),
            };

            return new AIProjectClient(endpoint(sp), credential(sp), options);
        });

        return services.AddFoundryAgents(optionsFactory);
    }

    /// <summary>
    /// Registers <see cref="AzureSearchContextRetriever"/> and its dependencies with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="options">The Azure Search configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddAzureSearchContextRetriever(this IServiceCollection services,IConfiguration configuration)
    {
        SetConfiguration<AzureSearchContextRetrieverOptions>(services, configuration, "AzureSearch");
        services.AddSingleton<IRelevanceFilter>(sp =>
        {
            var options = sp.GetRequiredService<AzureSearchContextRetrieverOptions>(); 
            return new RelativeScoreRelevanceFilter(options.MinimumRelevanceFilter);
        });

        services.AddSingleton<IContextRetriever>(sp =>
        {
            var config = sp.GetRequiredService<AzureSearchContextRetrieverOptions>();
            var credential = new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);

            var clientOptions = new SearchClientOptions
            {
                Retry =
                {
                    MaxRetries = config.MaxRetryAttemps,
                    Mode = RetryMode.Exponential
                }
            };
            var clients = config.Indexes.ToDictionary(indexName => indexName, indexName => new SearchClient(
                new Uri(config.Endpoint), indexName, credential, clientOptions));

            return new AzureSearchContextRetriever(clients, sp.GetRequiredService<IRelevanceFilter>(),
                sp.GetRequiredService<ILogger<AzureSearchContextRetriever>>());
        });

        return services;
    }

    /// <summary>
    /// Registers services for discovering and using tools and prompts from a remote MCP server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsFactory">Creates the MCP server connection options.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddMcpClientServices(this IServiceCollection services,
        Func<IServiceProvider, McpServerConnectionOptions> optionsFactory)
    {
        services.AddSingleton(optionsFactory);
        services.AddHttpClient<ITokenService, TokenService>();

        services.AddTransient<McpAuthenticationHandler>();
        services.AddHttpClient(McpToolClient.HttpClientName)
            .AddHttpMessageHandler<McpAuthenticationHandler>();

        services.AddSingleton<IMcpToolClient, McpToolClient>();
        services.AddHostedService<McpToolStartupValidator>();

        return services;
    }
    private static void SetConfiguration<T>(IServiceCollection services, IConfiguration configuration, string configurationName) where T : class
    {
        services.AddOptions<T>().Bind(configuration.GetSection(configurationName)).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
