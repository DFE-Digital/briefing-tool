using Azure.Identity; 
using Dfe.Briefing.Tool.Infrastructure.Configs;
using GovUK.Dfe.CoreLibs.AiAgents;
using GovUK.Dfe.CoreLibs.AiAgents.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        SetConfiguration<AzureFoundryConfig>(services, configuration, "AzureFoundry");

        services.AddFoundryAgents(
            endpoint: sp => new Uri(sp.GetRequiredService<AzureFoundryConfig>().Endpoint),
            credential: sp =>
            {
                var config = sp.GetRequiredService<AzureFoundryConfig>();
                return new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
            },
            optionsFactory: sp => new FoundryAgentFactoryOptions(sp.GetRequiredService<AzureFoundryConfig>().DeploymentModel));
        services.AddAgentVersionPinning(configuration);
        services.AddPinnedAgentVersionDriftDetection();

        services.AddAzureSearchContextRetriever(configuration);

        return services;
    }

    private static void SetConfiguration<T>(IServiceCollection services, IConfiguration configuration, string configurationName) where T : class
    {
        services.AddOptions<T>().Bind(configuration.GetSection(configurationName)).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
