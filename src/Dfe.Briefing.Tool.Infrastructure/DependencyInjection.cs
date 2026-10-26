using Dfe.Briefing.Tool.Application;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers;
using GovUK.Dfe.AI.Agents.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.Infrastructure;

public static class DependencyInjection
{
    private const string GuardrailsSubscriptionIdKey = "AiAgents:Guardrails:SubscriptionId";

    /// <summary>
    /// Registers the briefing agents. The web app and the release gate share this, so both run the agents with the
    /// same settings and guardrail; each adds what only it needs through <paramref name="configureAgents"/>.
    /// Cleaning up old agents and versions isn't done here: the daily "sweep" job does it (see README).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration,
        Action<AgentsBuilder>? configureAgents = null)
    {
        services.AddApplication();
         
        var briefingOptions = configuration.GetSection(BriefingOptions.SectionName).Get<BriefingOptions>() ?? new BriefingOptions();
        services.AddAgentSweeps(briefingOptions); 

        services.AddAgents(configuration, agents =>
        {
            agents.AddAgents(BriefingAgents.All).AddAISearch();

            // Guardrails are only added if the subscription ID is set in configuration,
            // so that the release gate can run without guardrails.
            if (!string.IsNullOrWhiteSpace(configuration[GuardrailsSubscriptionIdKey]))
                agents.AddGuardrails();

            configureAgents?.Invoke(agents);
        });

        return services;
    }
}
