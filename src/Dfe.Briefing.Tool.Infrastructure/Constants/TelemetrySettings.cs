namespace Dfe.Briefing.Tool.Infrastructure.Constants;

/// <summary>The settings telemetry is read from, the same in the web app and the release gate.</summary>
public static class TelemetrySettings
{
    public const string ConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>cloud_RoleName, shared by every app reporting on these agents.</summary>
    public const string ApplicationNameKey = "AiAgents:ApplicationName";

    public const string LoggingSection = "Logging";
}
