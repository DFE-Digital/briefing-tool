using Dfe.Briefing.Tool.Web.Pages.Public;
using Dfe.Briefing.Tool.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Pages.Public;

public sealed class CookiesTests
{
    [Fact]
    public void OnGet_WithConsent_SetsThePreferencesTempDataKey()
    {
        var consentService = Substitute.For<IAnalyticsConsentService>();
        consentService.ConsentValue().Returns((bool?)null);
        var page = CreatePage(consentService);

        var result = page.OnGet(true, "/");

        Assert.IsType<RedirectResult>(result);
        Assert.True(page.TempData.ContainsKey(Cookies.PreferencesSetTempDataKey));
        Assert.True((bool?)page.TempData[Cookies.PreferencesSetTempDataKey]);
        consentService.Received(1).AllowConsent();
    }

    [Fact]
    public void OnPost_WithRubyCookieConsent_SetsTheIsRubyRequestTempDataKey()
    {
        var consentService = Substitute.For<IAnalyticsConsentService>();
        consentService.ConsentValue().Returns((bool?)null);
        var page = CreatePage(consentService);

        var result = page.OnPost(null, "/", true);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/cookies?consent=True&returnUrl=/", redirect.Url);
        Assert.False((bool?)page.TempData[Cookies.IsRubyRequestTempDataKey]);
    }

    [Fact]
    public void OnGet_WithNoConsentAndNoReturnUrl_ShowsThePage()
    {
        var consentService = Substitute.For<IAnalyticsConsentService>();
        consentService.ConsentValue().Returns((bool?)null);
        var page = CreatePage(consentService);

        var result = page.OnGet(null, "/");

        Assert.IsType<PageResult>(result);
        Assert.False(page.PreferencesSet);
        consentService.DidNotReceive().AllowConsent();
        consentService.DidNotReceive().DenyConsent();
    }

    [Fact]
    public void OnGet_WhenConsentIsRejected_CallsDenyConsent()
    {
        var consentService = Substitute.For<IAnalyticsConsentService>();
        var page = CreatePage(consentService);

        page.OnGet(false, "/");

        consentService.Received(1).DenyConsent();
        consentService.DidNotReceive().AllowConsent();
    }

    [Fact]
    public void OnPost_WithExistingPreferences_ClearsThem()
    {
        var consentService = Substitute.For<IAnalyticsConsentService>();
        consentService.ConsentValue().Returns((bool?)null);
        var page = CreatePage(consentService);
        page.TempData[Cookies.PreferencesSetTempDataKey] = true;
        page.TempData[Cookies.IsRubyRequestTempDataKey] = false;

        var result = page.OnPost(true, "/", null);

        Assert.IsType<PageResult>(result);
        Assert.Null(page.TempData[Cookies.PreferencesSetTempDataKey]);
        Assert.Null(page.TempData[Cookies.IsRubyRequestTempDataKey]);
    }

    private static Cookies CreatePage(IAnalyticsConsentService consentService)
    {
        var httpContext = new DefaultHttpContext();
        var tempDataProvider = Substitute.For<ITempDataProvider>();
        var urlHelper = Substitute.For<IUrlHelper>();
        urlHelper.IsLocalUrl(Arg.Any<string?>()).Returns(call => call.Arg<string?>()?.StartsWith('/') == true);
        return new Cookies(consentService)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, tempDataProvider),
            Url = urlHelper
        };
    }
}
