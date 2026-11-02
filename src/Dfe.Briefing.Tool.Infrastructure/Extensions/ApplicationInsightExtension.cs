using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Briefing.Tool.Infrastructure.Constants;
using GovUK.Dfe.AI.Agents.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using System.Diagnostics.CodeAnalysis;

namespace Dfe.Briefing.Tool.Infrastructure.Extensions;

/// <summary>Logging and Application Insights, set up the same way in the web app and the release gate.</summary>
[ExcludeFromCodeCoverage]
public static class ApplicationInsightExtension
{
    /// <summary>
    /// Logs one line per message to the console, at the levels in the "Logging" section. When
    /// APPLICATIONINSIGHTS_CONNECTION_STRING is set, also sends the logs and the AI agent library's traces and metrics
    /// to Application Insights; returns whether it's set.
    /// </summary>
    /// <param name="roleInstance">The cloud_RoleInstance that tells this app's telemetry apart; null: one per process.</param>
    public static bool AddApplicationInsights(this IHostApplicationBuilder builder, string? roleInstance = null)
    {
        // Read here as well, for a host built without its defaults (the release gate).
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection(TelemetrySettings.LoggingSection));
        // Scopes carry a request's correlation ID onto each of its log lines.
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.IncludeScopes = true;
        });

        var connectionString = builder.Configuration[TelemetrySettings.ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var applicationName = builder.Configuration[TelemetrySettings.ApplicationNameKey] ?? AppDomain.CurrentDomain.FriendlyName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(applicationName, serviceInstanceId: roleInstance))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName))
            .WithLogging(configureBuilder: null, configureOptions: logs =>
            {
                logs.IncludeFormattedMessage = true;
                logs.IncludeScopes = true;
            })
            .UseAzureMonitorExporter(options => options.ConnectionString = connectionString);

        return true;
    }
}
