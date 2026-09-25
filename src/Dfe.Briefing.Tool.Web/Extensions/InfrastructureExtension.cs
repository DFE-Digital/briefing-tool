using Dfe.Briefing.Tool.Application;
using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Web.Services;
using GovUk.Frontend.AspNetCore;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class InfrastructureExtension
{
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddGovUkFrontend();
        builder.Services.AddApplication(builder.Configuration);
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.AddSecurityServices(); 
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAnalyticsConsentService, AnalyticsConsentService>();

        return builder;
    }
}
