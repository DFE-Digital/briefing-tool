using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Pages.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Pages.Errors;

public sealed class IndexModelTests
{
    [Theory]
    [InlineData(404, ErrorMessages.PageNotFound, true)]
    [InlineData(500, ErrorMessages.ProblemWithService, false)]
    [InlineData(501, ErrorMessages.NotImplemented, false)]
    [InlineData(418, "Error 418", false)]
    public void OnGet_SetsTheExpectedErrorMessage(int statusCode, string expectedMessage, bool isPageNotFound)
    {
        var page = CreatePage();

        page.OnGet(statusCode);

        Assert.Equal(expectedMessage, page.ErrorMessage);
        Assert.Equal(isPageNotFound, page.IsPageNotFound);
    }

    [Fact]
    public void OnPost_WithStatusCode_SetsTheErrorPage()
    {
        var page = CreatePage();

        page.OnPost(404);

        Assert.Equal(ErrorMessages.PageNotFound, page.ErrorMessage);
        Assert.True(page.IsPageNotFound);
    }

    [Fact]
    public void OnGet_WithNoStatusAndMissingPageException_Sets404()
    {
        var page = CreatePage();
        page.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature(new InvalidOperationException("no page named Test")));

        page.OnGet();

        Assert.Equal(ErrorMessages.PageNotFound, page.ErrorMessage);
        Assert.Equal(StatusCodes.Status404NotFound, page.HttpContext.Response.StatusCode);
    }

    [Fact]
    public void OnGet_WithNoStatusAndUnhandledException_UsesServiceMessage()
    {
        var page = CreatePage();
        page.HttpContext.Features.Set<IExceptionHandlerPathFeature>(
            new ExceptionHandlerFeature(new InvalidOperationException("boom")));

        page.OnGet();

        Assert.Equal(ErrorMessages.ProblemWithService, page.ErrorMessage);
    }

    [Fact]
    public void ErrorPageLogTemplates_ContainRequestAndErrorPlaceholders()
    {
        Assert.Contains("{RequestId}", ErrorMessages.ErrorPageLogTemplate);
        Assert.Contains("{ErrorMessage}", ErrorMessages.ErrorPageLogTemplate);
        Assert.Contains("{RequestId}", ErrorMessages.ErrorPageUnhandledLogTemplate);
        Assert.Contains("{Message}", ErrorMessages.ErrorPageUnhandledLogTemplate);
    }

    private static IndexModel CreatePage()
        => new(NullLogger<IndexModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private sealed class ExceptionHandlerFeature(Exception exception) : IExceptionHandlerPathFeature
    {
        public Exception Error { get; } = exception;
        public string Path => "/Errors";
    }
}
