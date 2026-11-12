using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Web;
using System.Diagnostics.CodeAnalysis;

namespace Dfe.Briefing.Tool.Web.Extensions;

[ExcludeFromCodeCoverage]
public static class SecurityServicesExtension
{
    // Every instance must name the app the same to read each other's keys; by default the name is the content root path,
    // which can differ between instances.
    private const string DataProtectionApplicationName = "Dfe.Briefing.Tool";

    public static void AddSecurityServices(this WebApplicationBuilder builder)
    {
        AddHsts(builder);
        AddIdentityServices(builder);
        AddAntiForgeryCookies(builder);
        AddDataProtectionServices(builder);
    }

    private static void AddHsts(WebApplicationBuilder builder)
    {
        // Enforce HTTPS in ASP.NET Core
        // @link https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?
        builder.Services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });
    }

    private static void AddIdentityServices(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization(options =>
        {
            var policyBuilder = new AuthorizationPolicyBuilder();
            policyBuilder.RequireAuthenticatedUser();
            options.DefaultPolicy = policyBuilder.Build();
            options.FallbackPolicy = options.DefaultPolicy;

        });

        builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration);

        if (!builder.Environment.IsDevelopment())
        {
            // Override the redirect URI scheme
            builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {


                options.Events.OnRedirectToIdentityProvider = context =>
                {

                    var path = context.HttpContext.Request.Path;

                    // Skip API routes
                    if (path.StartsWithSegments("/api"))
                    {
                        return Task.CompletedTask;
                    }

                    if (context.ProtocolMessage.RedirectUri.StartsWith("http://"))
                    {
                        context.ProtocolMessage.RedirectUri =
                            context.ProtocolMessage.RedirectUri.Replace("http://", "https://");
                    }

                    return Task.CompletedTask;
                };
            });
        }

        builder.Services.Configure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.Cookie.Name = "BriefingName";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
    }

    private static void AddAntiForgeryCookies(WebApplicationBuilder builder)
    {
        builder.Services.AddAntiforgery(opts => { opts.Cookie.Name = "BriefingAntiForge"; });
    }

    private static void AddDataProtectionServices(WebApplicationBuilder builder)
    {
        // The sign-in, anti-forgery and TempData cookies are encrypted with these keys. Each instance keeps its own in
        // its container, and ASP.NET Core logs a warning that they won't outlive it. So run one replica: with more, a
        // form or sign-in can fail when the next request reaches another one, and every restart or deploy signs
        // everyone out.
        builder.Services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
    }
}
