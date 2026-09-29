using Dfe.Briefing.Tool.Web.Extensions;
using GovUk.Frontend.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");

    options.Conventions.AllowAnonymousToFolder("/Public");
    options.Conventions.AllowAnonymousToFolder("/Errors");
});
builder.AddInfrastructure();

var app = builder.Build(); 

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Errors");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Re-executes the pipeline against /Errors?statusCode={0} for any response that reaches a status
// code (e.g. 404 for a URL that matched no page) without already producing an error page itself -
// UseExceptionHandler above only catches thrown exceptions, not this.
app.UseStatusCodePagesWithReExecute("/Errors", "?statusCode={0}");
app.UseGovUkFrontend();
app.AddSecurityLayer(); 

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
