using Dfe.Briefing.Tool.Infrastructure.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Dfe.Briefing.Tool.Infrastructure.Extensions;

/// <summary>The settings shared by the web app and the release gate.</summary>
public static class SharedSettingsExtension
{
    /// <summary>
    /// Loads <see cref="SettingsFiles.Shared"/> from the app's output folder (it's copied there from this project) as the
    /// first source, so the app's own appsettings.json, user secrets and environment variables all override it.
    /// </summary>
    public static IConfigurationBuilder AddSharedSettings(this IConfigurationBuilder configuration)
    {
        configuration.Sources.Insert(0, new JsonConfigurationSource
        {
            Path = SettingsFiles.Shared,
            Optional = false,
            FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory)
        });

        return configuration;
    }
}
