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
        // The sign-in, anti-forgery and TempData cookies are encrypted with these keys, so with more than one instance
        // every instance must share them: a cookie one instance wrote is then readable by whichever answers next. They're
        // kept in DataProtection:Path, a folder every instance mounts, so access to it should be limited to the app.
        var dp = builder.Services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);

        var keyPath = builder.Configuration.GetValue<string>("DataProtection:Path");
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            // Each instance would make keys of its own, so a form or sign-in could fail when the next request reaches
            // another instance. Only allowed for local development, which runs one instance.
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(ErrorMessages.DataProtectionPathUndefined);
            }

            return;
        }

        var keyDirectory = new DirectoryInfo(keyPath);
        if (!keyDirectory.Exists || keyDirectory.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            throw new ReadOnlyException(string.Format(ErrorMessages.DataProtectionPathNotWritable, keyPath));
        }

        dp.PersistKeysToFileSystem(keyDirectory);

        // Whoever can read the shared folder could otherwise read the keys and forge or decrypt the cookies, so they're
        // encrypted at rest with a Key Vault key. Only local development may keep them unencrypted.
        var keyVaultKey = builder.Configuration.GetValue<string>("DataProtection:KeyVaultKey");
        if (string.IsNullOrWhiteSpace(keyVaultKey))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(ErrorMessages.DataProtectionKeyVaultKeyUndefined);
            }

            return;
        }

        dp.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKey), new DefaultAzureCredential());
    }
}
