using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IReadOnlyCollection<AgentDefinition>>(BriefingAgents.All);
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();

        return services;
    }
}
