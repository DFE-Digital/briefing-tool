using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Dfe.Ai.Agents;
using Dfe.Ai.Agents.Context;
using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Factories;
using Dfe.Briefing.Tool.Application.FileRetrievers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Infrastructure.Authentication;
using Dfe.Briefing.Tool.Infrastructure.Configs;
using Dfe.Briefing.Tool.Infrastructure.Constants;
using Dfe.Briefing.Tool.Infrastructure.FileRetrievers;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReverseMarkdown;
using System.Net;

namespace Dfe.Briefing.Tool.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Adds the infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to which to add the infrastructure services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPromptFileReader, PromptFileReader>();

        SetConfiguration<AzureFoundryConfig>(services, configuration, "AzureFoundry");

        services.AddFoundryAgents(
            endpoint: sp => new Uri(sp.GetRequiredService<AzureFoundryConfig>().Endpoint),
            credential: sp => new InteractiveBrowserCredential(),
            optionsFactory: sp => new FoundryAgentFactoryOptions(sp.GetRequiredService<AzureFoundryConfig>().DeploymentModel));
         
        services.AddAzureSearchContextRetriever(configuration);

        return services;
    }

    private static void SetConfiguration<T>(IServiceCollection services, IConfiguration configuration, string configurationName) where T : class
    {
        services.AddOptions<T>().Bind(configuration.GetSection(configurationName)).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
