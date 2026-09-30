using Azure.Monitor.OpenTelemetry.AspNetCore;
using Dfe.Briefing.Tool.Application;
using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Web.Services;
using GovUK.Dfe.CoreLibs.AiAgents.Diagnostics;
using GovUk.Frontend.AspNetCore;
using OpenTelemetry.Resources;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class InfrastructureExtension
{
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddGovUkFrontend();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.AddSecurityServices();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAnalyticsConsentService, AnalyticsConsentService>();

        // Records agent token usage and sends all logs, traces and metrics to Application Insights.
        // Reads the connection string from APPLICATIONINSIGHTS_CONNECTION_STRING.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("briefing-tool"))
            .UseAzureMonitor()
            .WithTracing(t => t.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(m => m.AddMeter(AgentTelemetry.SourceName));

        return builder;
    }
}
