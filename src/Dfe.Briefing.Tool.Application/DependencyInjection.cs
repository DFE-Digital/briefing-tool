using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Orchestrators;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
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
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<BriefingOptions>>().Value);

        services.AddSingleton<IReadOnlyCollection<AgentDefinition>>(BriefingAgents.All);
        services.AddScoped<IBriefingOrchestrator, BriefingOrchestrator>();
        services.AddScoped<IBriefingBuilder, BriefingBuilder>();
        services.AddScoped<IBriefingEvidenceProvider, BriefingEvidenceProvider>();

        return services;
    }
}
