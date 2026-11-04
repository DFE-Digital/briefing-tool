using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Infrastructure.Exceptions;
using Dfe.Briefing.Tool.Infrastructure.Extensions;
using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Services;
using GovUk.Frontend.AspNetCore;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class InfrastructureExtension
{
    private const string RequireTelemetryKey = "AiAgents:RequireTokenUsageTelemetry";

    /// <exception cref="AgentConfigurationException">Application Insights isn't set, and telemetry is required.</exception>
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        // Every page's errors are shown by the one error summary in the layout (_ErrorSummary), not one the tag helpers add.
        builder.Services.AddGovUkFrontend(options => options.ErrorSummaryGeneration = ErrorSummaryGenerationOptions.None);
        builder.Services.AddInfrastructure(builder.Configuration); 
        builder.AddSecurityServices();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAnalyticsConsentService, AnalyticsConsentService>();
        if (!builder.AddApplicationInsights() && builder.Configuration.GetValue(RequireTelemetryKey, defaultValue: true))
            throw new AgentConfigurationException(ErrorMessages.ApplicationInsightsMissing);
        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");

            options.Conventions.AllowAnonymousToFolder("/Public");
            options.Conventions.AllowAnonymousToFolder("/Errors");
        }); 

        return builder;
    } 
}
