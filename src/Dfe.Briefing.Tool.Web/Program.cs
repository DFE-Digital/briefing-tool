using Dfe.Briefing.Tool.Infrastructure.Extensions;
using Dfe.Briefing.Tool.Web.Extensions;
using Dfe.Briefing.Tool.Web.Middleware;
using GovUk.Frontend.AspNetCore; 

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSharedSettings();

builder.AddInfrastructure();

var app = builder.Build();

// First, so every log of the request - the error page's too - carries its correlation ID.
app.UseMiddleware<CorrelationIdMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Errors");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/Errors", "?statusCode={0}");
app.UseGovUkFrontend();
app.AddSecurityLayer(); 

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

await app.RunAsync();
