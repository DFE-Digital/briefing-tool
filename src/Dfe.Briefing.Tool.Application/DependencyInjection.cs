using Dfe.Ai.Agents.Prompts;
using Dfe.Ai.Agents.Prompts.Interfaces;
using Dfe.Briefing.Tool.Application.Configs;
using Dfe.Briefing.Tool.Application.FileRetrievers;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Services;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dfe.Briefing.Tool.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPromptRetrieverService, PromptRetrieverService>();
        services.AddScoped<IPromptTemplateStore>(sp =>
            new DelegatingPromptTemplateStore(sp.GetRequiredService<IPromptRetrieverService>().GetUserPrompt));
        services.AddScoped<IPromptTemplateBuilder, PromptTemplateBuilder>();
        services.AddSingleton<IAgentDefinitionProvider, AgentDefinitionProvider>();
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();

        services.AddOptions<PromptConfig>().Bind(configuration.GetSection("PromptFiles")).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PromptConfig>>().Value);

        return services;
    }
}
