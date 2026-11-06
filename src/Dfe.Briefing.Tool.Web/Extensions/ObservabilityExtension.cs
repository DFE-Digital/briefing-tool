using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Dfe.Briefing.Tool.Web.Extensions;

/// <summary>
/// What only the web app records, on top of the telemetry shared with the release gate (see
/// ApplicationInsightExtension, which also traces outgoing calls such as AI Search): every page request, and a health
/// endpoint for uptime.
/// </summary>
public static class ObservabilityExtension
{
    /// <summary>Answers 200 "Healthy" while the app can serve requests; for the Container Apps probes and availability tests.</summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Adds each page request to the telemetry already sent to Application Insights (the "requests" table and
    /// http.server.* metrics): latency, throughput and failures. Health checks aren't traced, so probes every few
    /// seconds don't fill the requests table.
    /// </summary>
    public static WebApplicationBuilder AddWebTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing
            .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments(HealthPath)));

        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics
            .AddAspNetCoreInstrumentation());

        return builder;
    }

    /// <summary>
    /// The health endpoint: a liveness check that the app is up and serving, open without signing in, so probes and
    /// availability tests can reach it. It calls no other service, so a slow dependency never restarts the app.
    /// </summary>
    public static WebApplication MapHealth(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath).AllowAnonymous().DisableHttpMetrics();
        return app;
    }
}
