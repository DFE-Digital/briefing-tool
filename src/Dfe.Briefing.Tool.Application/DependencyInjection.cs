using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Application.Services;
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
            .Validate(options => options.OrphanAgentMinimumAge > 0,
                $"{BriefingOptions.SectionName}:{nameof(BriefingOptions.OrphanAgentMinimumAge)} must be more than zero.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<BriefingOptions>>().Value);
        services.AddSingleton<IReadOnlyCollection<AgentDefinition>>(BriefingAgents.All);
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();
        services.AddScoped<IBriefingBuilder, BriefingBuilder>();
        services.AddScoped<IBriefingEvidenceProvider, BriefingEvidenceProvider>();

        return services;
    }

    /// <summary>
    /// Adds the daily agent sweep if either of its sweeps (orphan agents, old agent versions) is enabled. It runs only
    /// in a host that starts its background services, i.e. the web app.
    /// </summary>
    public static IServiceCollection AddAgentSweeps(this IServiceCollection services, BriefingOptions briefingOptions)
    {
        if (!briefingOptions.OrphanAgentSweepEnabled && !briefingOptions.AgentVersionSweepEnabled)
            return services;

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<AgentSweepService>();

        return services;
    }
}
