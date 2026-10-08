using Dfe.Briefing.Tool.Application;
using Dfe.Briefing.Tool.Application.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplication();
        services.AddAgents(configuration, agents => agents
            .AddAgents(BriefingAgents.All)
            .AddAISearch());

        return services;
    }
}
