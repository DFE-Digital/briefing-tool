using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Web.Services;
using GovUk.Frontend.AspNetCore;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class InfrastructureExtension
{
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddGovUkFrontend();
        builder.Services.AddInfrastructure(builder.Configuration); 
        builder.AddSecurityServices();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAnalyticsConsentService, AnalyticsConsentService>();
        builder.AddApplicationInsights();
        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");

            options.Conventions.AllowAnonymousToFolder("/Public");
            options.Conventions.AllowAnonymousToFolder("/Errors");
        }); 

        return builder;
    } 
}
