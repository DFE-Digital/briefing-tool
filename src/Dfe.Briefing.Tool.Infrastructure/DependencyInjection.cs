using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Providers;
using GovUK.Dfe.CoreLibs.AiAgents;
using GovUK.Dfe.CoreLibs.AiAgents.Tools.WebSearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAiAgents(configuration, agents => agents
            .AddAgents(BriefingAgents.All)
            .AddTools(AgentNames.WebSearch, new WebSearchToolProvider()));

        return services;
    }
}
