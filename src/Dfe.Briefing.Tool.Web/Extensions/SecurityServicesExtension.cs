using Azure.Identity;
using Dfe.Briefing.Tool.Web.Constants;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Web;
using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace Dfe.Briefing.Tool.Web.Extensions;

[ExcludeFromCodeCoverage]
public static class SecurityServicesExtension
{
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
        // Setup basic Data Protection and persist keys.xml to local file system
        var dp = builder.Services.AddDataProtection();

        // If a Key Vault Key URI is defined, encrypt the keys.xml with it before persisting to disk
        var kvProtectionKeyUri = builder.Configuration.GetValue<string>("DataProtection:KeyVaultKey");
        if (!string.IsNullOrWhiteSpace(kvProtectionKeyUri))
        {
            var kvProtectionPath = builder.Configuration.GetValue<string>("DataProtection:Path");

            if (string.IsNullOrWhiteSpace(kvProtectionPath))
            {
                throw new InvalidOperationException(ErrorMessages.DataProtectionPathUndefined);
            }

            var kvProtectionPathDir = new DirectoryInfo(kvProtectionPath);
            if (!kvProtectionPathDir.Exists || kvProtectionPathDir.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                throw new ReadOnlyException(string.Format(ErrorMessages.DataProtectionPathNotWritable, kvProtectionPath));
            }

            dp.PersistKeysToFileSystem(kvProtectionPathDir);
            dp.ProtectKeysWithAzureKeyVault(kvProtectionKeyUri, new DefaultAzureCredential());
        }
    }
}
