using Dfe.Briefing.Tool.Web.Extensions;
using GovUk.Frontend.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.AddInfrastructure();

var app = builder.Build(); 

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
} 
app.UseGovUkFrontend();
app.AddSecurityLayer(); 

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
