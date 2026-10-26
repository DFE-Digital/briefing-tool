using Microsoft.AspNetCore.HttpOverrides;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class SecurityConfigurationExtension
{
    // dotnet watch's and Visual Studio's hot-reload websocket, allowed in Development only.
    private const string LocalWebSocket = "ws://localhost:*";
    private const string LocalSecureWebSocket = "wss://localhost:*";

    /// <summary>
    /// Add API key based authentication
    /// </summary>
    /// <param name="app"></param>
    /// <returns>An instance of <see cref="IApplicationBuilder"/></returns>
    public static IApplicationBuilder AddSecurityLayer(this IApplicationBuilder app)
    {
        app.AddForwardedHeaders();
        app.AddSecurityHeaders();
        app.UseHttpsRedirection(); 
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
    private static void AddForwardedHeaders(this IApplicationBuilder app)
    {
        var forwardOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.All,
            RequireHeaderSymmetry = false
        };
        forwardOptions.KnownIPNetworks.Clear();
        forwardOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardOptions);
    }

    private static void AddSecurityHeaders(this IApplicationBuilder app)
    {
        var isDevelopment = app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        app.UseSecurityHeaders(options =>
        {
            options.AddFrameOptionsDeny()
                .AddXssProtectionDisabled()
                .AddContentTypeOptionsNoSniff()
                .RemoveServerHeader()
                .AddContentSecurityPolicy(builder =>
                {
                    builder.AddDefaultSrc().Self();
                    builder.AddStyleSrc().Self().WithNonce();
                    builder.AddScriptSrc().Self().WithNonce();

                    var connectSrc = builder.AddConnectSrc().Self();
                    if (isDevelopment)
                    {
                        // dotnet watch's browser-refresh script (and Visual Studio's own live-reload
                        // equivalent) opens a websocket back to the dev server for hot reload
                        // notifications - CSP has no "same-origin websocket" shorthand, so this has
                        // to be spelled out explicitly, and only for Development; never allow
                        // arbitrary ws/wss origins once this is actually deployed.
                        connectSrc.From(LocalWebSocket).From(LocalSecureWebSocket);
                    }
                })
                .AddPermissionsPolicy(builder =>
                {
                    builder.AddAccelerometer().None();
                    builder.AddAutoplay().None();
                    builder.AddCamera().None();
                    builder.AddEncryptedMedia().None();
                    builder.AddFullscreen().None();
                    builder.AddGeolocation().None();
                    builder.AddGyroscope().None();
                    builder.AddMagnetometer().None();
                    builder.AddMicrophone().None();
                    builder.AddMidi().None();
                    builder.AddPayment().None();
                    builder.AddPictureInPicture().None();
                    builder.AddSyncXHR().None();
                    builder.AddUsb().None();
                });
        });
    }
}
