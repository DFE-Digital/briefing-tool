using Dfe.Briefing.Tool.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Extensions;

public sealed class BackLinkExtensionTests
{
    private const string Fallback = "/Briefing/Type";

    private static IUrlHelper UrlFor(string? referer, string host = "localhost:5234", string path = "/Briefing/Find")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.Path = path;
        if (referer is not null)
            httpContext.Request.Headers.Referer = referer;

        var url = Substitute.For<IUrlHelper>();
        url.ActionContext.Returns(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        url.Content("~/").Returns("/");
        return url;
    }

    [Fact]
    public void WhenThePreviousPageIsOnThisSite_GoesBackToItsPathAndQuery()
        => Assert.Equal("/Briefing/Type?type=trust", UrlFor("http://localhost:5234/Briefing/Type?type=trust").BackLink(Fallback));

    [Fact]
    public void ComparesTheHostIgnoringCase()
        => Assert.Equal("/Briefing/Type", UrlFor("http://LOCALHOST:5234/Briefing/Type").BackLink(Fallback));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/Briefing/Type")]
    public void WithoutAnAbsolutePreviousPage_UsesTheFallback(string? referer)
        => Assert.Equal(Fallback, UrlFor(referer).BackLink(Fallback));

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("http://localhost:9999/Briefing/Type")]
    public void WhenThePreviousPageIsOnAnotherSite_UsesTheFallback(string referer)
        => Assert.Equal(Fallback, UrlFor(referer).BackLink(Fallback));

    [Theory]
    [InlineData("http://localhost:5234/Briefing/Find")]
    [InlineData("http://localhost:5234/briefing/find?type=school")]
    public void WhenThePreviousPageIsThisPage_UsesTheFallback(string referer)
        // E.g. after a form on the page is posted back with an error: going "back" to it would go nowhere.
        => Assert.Equal(Fallback, UrlFor(referer).BackLink(Fallback));

    [Fact]
    public void WithoutAPreviousPageOrFallback_GoesHome()
        => Assert.Equal("/", UrlFor(null).BackLink(null));
}
