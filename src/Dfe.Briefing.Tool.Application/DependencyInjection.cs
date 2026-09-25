using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.CoreLibs.AiAgents;
using GovUK.Dfe.CoreLibs.AiAgents.Tools;
using GovUK.Dfe.CoreLibs.AiAgents.Tools.WebSearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Register the file prompts service - every prompt type must have a configured, readable
        // file; a missing/unconfigured one throws instead of silently falling back.
        services.AddFilePrompts(configuration,
            responseFormatKey: PromptKeys.StandardAgentResponseFormatKey,
            responseFormatExemptPromptTypes: new HashSet<string> { SystemPromptType.Synthesis });

        // Register the agent definition provider and briefing orchestrator
        services.AddSingleton<IAgentDefinitionProvider, AgentDefinitionProvider>();
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();

        // Register the web search tool provider for the web search agent
        services.AddSingleton(new AgentToolBinding(AgentNames.WebSearch, new WebSearchToolProvider()));

        return services;
    }
}
