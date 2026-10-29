namespace Dfe.Briefing.Tool.Infrastructure.Constants;

/// <summary>The settings files every app reads.</summary>
public static class SettingsFiles
{
    /// <summary>The agents' settings, shared by the web app and the release gate. Each app's own file is appsettings.json.</summary>
    public const string Shared = "appsettings.shared.json";
}
