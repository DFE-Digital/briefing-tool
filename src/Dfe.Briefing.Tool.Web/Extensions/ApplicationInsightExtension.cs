using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Briefing.Tool.Infrastructure.Exceptions;
using Dfe.Briefing.Tool.Web.Constants;
using GovUK.Dfe.AI.Agents.Diagnostics;
using OpenTelemetry.Resources;
using Serilog;
using System.Diagnostics.CodeAnalysis;

namespace Dfe.Briefing.Tool.Web.Extensions;

[ExcludeFromCodeCoverage]
public static class ApplicationInsightExtension
{
    private const string ApplicationNameKey = "AiAgents:ApplicationName";
    private const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    private const string RequireTelemetryKey = "AiAgents:RequireTokenUsageTelemetry";

    /// <summary>
    /// Adds Application Insights telemetry to the application.
    /// </summary>
    /// <param name="builder"></param>
    /// <exception cref="AgentConfigurationException"></exception>
    public static void AddApplicationInsights(this WebApplicationBuilder builder)
    {
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddSerilog(
            serilog => serilog.ReadFrom.Configuration(builder.Configuration),
            preserveStaticLogger: true,
            writeToProviders: true);

        if (string.IsNullOrWhiteSpace(builder.Configuration[ConnectionStringKey]))
        {
            if (builder.Configuration.GetValue(RequireTelemetryKey, defaultValue: true))
            {
                throw new AgentConfigurationException(ErrorMessages.ApplicationInsightsMissing);
            }

            return;
        }

        var applicationName = builder.Configuration[ApplicationNameKey] ?? AppDomain.CurrentDomain.FriendlyName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(applicationName))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName))
            .WithLogging(configureBuilder: null, configureOptions: logs => logs.IncludeFormattedMessage = true)
            .UseAzureMonitorExporter();
    }
}
