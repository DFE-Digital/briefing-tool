using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Dfe.Briefing.Tool.ReleaseGate.Extensions;

/// <summary>
/// Sends the gate's results to Application Insights, passed or failed: the AI agent library's release gate, evaluation
/// and guardrail metrics, all on the library's meter, and the gate's logs. Set up by the shared AddApplicationInsights.
/// </summary>
internal static class TelemetryExtension
{
    /// <summary>
    /// Resolves the OpenTelemetry meter and tracer providers from the service provider to initialize telemetry
    /// pipelines.
    /// </summary>
    /// <param name="services">Application service provider used to resolve telemetry providers.</param>
    public static void StartTelemetry(this IServiceProvider services)
    {
        services.GetService<MeterProvider>();
        services.GetService<TracerProvider>();
    }

    /// <summary>
    /// Forces pending metrics and traces to be exported by flushing the registered <see cref="MeterProvider"/> and <see
    /// cref="TracerProvider"/>.
    /// </summary>
    /// <param name="services">Service provider used to resolve telemetry providers to flush.</param>
    public static void FlushTelemetry(this IServiceProvider services)
    {
        services.GetService<MeterProvider>()?.ForceFlush();
        services.GetService<TracerProvider>()?.ForceFlush();
    }
}
