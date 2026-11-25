using Dfe.Briefing.Tool.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Services;

public sealed class AnalyticsConsentServiceTests
{
    private const string ConsentCookie = AnalyticsConsentService.ConsentCookieName;

    private readonly DefaultHttpContext _httpContext = new();

    private AnalyticsConsentService CreateService(string? analyticsDomain = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleAnalytics:Domain"] = analyticsDomain })
            .Build();

        return new AnalyticsConsentService(new HttpContextAccessor { HttpContext = _httpContext }, configuration,
            NullLogger<AnalyticsConsentService>.Instance);
    }

    private void RequestHasCookies(string cookies) => _httpContext.Request.Headers.Cookie = cookies;

    private string[] SetCookies() => _httpContext.Response.Headers.SetCookie.Select(c => c!).ToArray();

    private string[] SetCookiesFor(string name) => SetCookies().Where(c => c.StartsWith(name + "=", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void ConsentValue_WithNoChoiceMade_IsFalse()
    {
        var service = CreateService();

        Assert.False(service.ConsentValue());
        Assert.False(service.HasConsent());
    }

    [Fact]
    public void ConsentValue_WithoutARequest_IsFalse()
    {
        var service = new AnalyticsConsentService(new HttpContextAccessor(), new ConfigurationBuilder().Build(),
            NullLogger<AnalyticsConsentService>.Instance);

        Assert.False(service.ConsentValue());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ConsentValue_ReadsTheConsentCookie(string cookieValue, bool expected)
    {
        RequestHasCookies($"{ConsentCookie}={cookieValue}");

        var service = CreateService();

        Assert.Equal(expected, service.ConsentValue());
        Assert.Equal(expected, service.HasConsent());
    }

    [Fact]
    public void AllowConsent_SetsAYearLongSecureCookie_AndIsUsedOverTheRequestsCookie()
    {
        RequestHasCookies($"{ConsentCookie}=false");
        var service = CreateService();

        service.AllowConsent();

        Assert.True(service.HasConsent());
        var cookie = Assert.Single(SetCookiesFor(ConsentCookie));
        Assert.StartsWith($"{ConsentCookie}=true;", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"expires={DateTime.Today.AddYears(1):ddd, dd MMM yyyy}", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowConsent_LeavesAnalyticsCookiesAlone()
    {
        RequestHasCookies("_ga=1; _gid=2; ai_user=3");

        CreateService().AllowConsent();

        Assert.Equal(ConsentCookie, Assert.Single(SetCookies()).Split('=')[0]);
    }

    [Fact]
    public void DenyConsent_SetsTheCookieToFalse()
    {
        var service = CreateService();

        service.DenyConsent();

        Assert.False(service.ConsentValue());
        Assert.StartsWith($"{ConsentCookie}=false;", Assert.Single(SetCookiesFor(ConsentCookie)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("_ga")]
    [InlineData("_ga_ABC123")]
    [InlineData("_gid")]
    public void DenyConsent_DeletesGoogleAnalyticsCookies_OnThisHostAndTheAnalyticsDomain(string name)
    {
        RequestHasCookies($"{name}=1");

        CreateService().DenyConsent();

        var deleted = SetCookiesFor(name);
        Assert.Equal(2, deleted.Length);
        Assert.All(deleted, c => Assert.Contains("expires=Thu, 01 Jan 1970", c, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deleted, c => !c.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deleted, c => c.Contains("domain=.education.gov.uk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DenyConsent_UsesTheConfiguredAnalyticsDomain()
    {
        RequestHasCookies("_ga=1");

        CreateService(".example.gov.uk").DenyConsent();

        Assert.Contains(SetCookiesFor("_ga"), c => c.Contains("domain=.example.gov.uk", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ai_user")]
    [InlineData("ai_session")]
    public void DenyConsent_ExpiresAppInsightsCookies(string name)
    {
        RequestHasCookies($"{name}=1");

        CreateService().DenyConsent();

        var expired = Assert.Single(SetCookiesFor(name));
        Assert.StartsWith($"{name}=;", expired, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"expires={DateTime.Now.AddYears(-1):ddd, dd MMM yyyy}", expired, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("BriefingAntiForge")]
    [InlineData("other_ga")]
    [InlineData("gid")]
    public void DenyConsent_LeavesOtherCookiesAlone(string name)
    {
        RequestHasCookies($"{name}=1");

        CreateService().DenyConsent();

        Assert.Empty(SetCookiesFor(name));
    }
}
