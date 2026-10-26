using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Application.Services;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dfe.Briefing.Tool.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<BriefingOptions>()
            .BindConfiguration(BriefingOptions.SectionName)
            .Validate(options => options.MaxEvidencePerSearch is >= 1 and <= 50,
                $"{BriefingOptions.SectionName}:{nameof(BriefingOptions.MaxEvidencePerSearch)} must be between 1 and 50.")
            .Validate(options => options.MaxAcademiesPerTrust is >= 1 and <= 100,
                $"{BriefingOptions.SectionName}:{nameof(BriefingOptions.MaxAcademiesPerTrust)} must be between 1 and 100.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<BriefingOptions>>().Value);
        services.AddSingleton<IReadOnlyCollection<AgentDefinition>>(BriefingAgents.All);
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();
        services.AddScoped<IBriefingBuilder, BriefingBuilder>();
        services.AddScoped<IBriefingEvidenceProvider, BriefingEvidenceProvider>();
        services.AddSingleton<IAgentHousekeepingService, AgentHousekeepingService>();

        return services;
    }

    /// <summary>
    /// Adds the daily agent clean-ups that are enabled: the orphan agent sweep and the agent version sweep, each at its
    /// own time. They run only in a host that starts its background services, i.e. the web app; the console tool's
    /// "sweep" command runs both on demand.
    /// </summary>
    public static IServiceCollection AddAgentSweeps(this IServiceCollection services, BriefingOptions briefingOptions)
    {
        services.TryAddSingleton(TimeProvider.System);

        if (briefingOptions.OrphanAgentSweepEnabled)
            services.AddHostedService<OrphanAgentSweepService>();

        if (briefingOptions.AgentVersionSweepEnabled)
            services.AddHostedService<AgentVersionSweepService>();

        return services;
    }
}
