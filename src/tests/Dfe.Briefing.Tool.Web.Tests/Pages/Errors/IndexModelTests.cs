using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Pages.Errors;
using Microsoft.AspNetCore.Http;
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
    public void OnGet_SetsTheExpectedErrorMessage(int statusCode, string expectedMessage, bool isPageNotFound)
    {
        var page = CreatePage();

        page.OnGet(statusCode);

        Assert.Equal(expectedMessage, page.ErrorMessage);
        Assert.Equal(isPageNotFound, page.IsPageNotFound);
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
}
