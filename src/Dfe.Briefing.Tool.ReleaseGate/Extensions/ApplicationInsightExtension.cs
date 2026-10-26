using Azure.Monitor.OpenTelemetry.Exporter;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Dfe.Briefing.Tool.ReleaseGate.Extensions;

/// <summary>
/// Sends the gate's results to Application Insights, passed or failed: the AI agent library's release gate, evaluation
/// and guardrail metrics, all on the library's meter, and the gate's logs.
/// </summary>
internal static class ApplicationInsightExtension
{
    /// <summary>
    /// Sets up logging, and sends it and the metrics to Application Insights if APPLICATIONINSIGHTS_CONNECTION_STRING
    /// is set; returns whether it is.
    /// </summary>
    public static bool AddApplicationInsights(this HostApplicationBuilder builder)
    {
        // Logging as in the web app: Serilog, configured by its "Serilog" section, also writing to Application Insights.
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddSerilog(
            serilog => serilog.ReadFrom.Configuration(builder.Configuration),
            preserveStaticLogger: true,
            writeToProviders: true);

        var connectionString = builder.Configuration[Settings.ApplicationInsightsConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var applicationName = builder.Configuration[Telemetry.ApplicationNameKey] ?? AppDomain.CurrentDomain.FriendlyName;

        builder.Services.AddOpenTelemetry()
            // Same cloud_RoleName as the web app; its own cloud_RoleInstance tells the gate's telemetry apart.
            .ConfigureResource(resource => resource.AddService(applicationName, serviceInstanceId: Telemetry.RoleInstance))
            .WithTracing(tracing => tracing.AddSource(AgentTelemetry.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(AgentTelemetry.SourceName))
            .WithLogging(configureBuilder: null, configureOptions: logs => logs.IncludeFormattedMessage = true)
            .UseAzureMonitorExporter(options => options.ConnectionString = connectionString);

        return true;
    }

    /// <summary>
    /// Starts the exporters: the gate doesn't run as a host, so nothing else would. Disposing the host then sends
    /// whatever is still waiting.
    /// </summary>
    public static void StartTelemetry(this IServiceProvider services)
    {
        services.GetService<MeterProvider>();
        services.GetService<TracerProvider>();
    }

    /// <summary>Sends what's recorded so far, so a run's results arrive even if the process is stopped straight after.</summary>
    public static void FlushTelemetry(this IServiceProvider services)
    {
        services.GetService<MeterProvider>()?.ForceFlush();
        services.GetService<TracerProvider>()?.ForceFlush();
    }
}
