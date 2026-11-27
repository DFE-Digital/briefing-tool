using System.Text;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Pages.Briefing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Pages.Briefing;

public sealed class SummaryModelTests
{
    private const string Urn = "123456";
    private const string OtherUrn = "654321";
    // Free text of the kind Front Door's firewall scores as an attack: quotes, SQL words and markup.
    private const string Briefing = "# Test Academy\n\nRated Good. It's in the 'select' union of schools <b>[Evidence 1]</b>";
    private const string City = "Exampleton";

    private static readonly BriefingSubject School = new(Urn, "Test Academy", IsTrust: false, Region: "North", Website: "https://test.example");

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IBriefingOrchestrator _orchestrator = Substitute.For<IBriefingOrchestrator>();
    private readonly IBriefingSubjectFinder _subjectFinder = Substitute.For<IBriefingSubjectFinder>();

    // Shared by every page in a test, as one app instance's keys.
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public SummaryModelTests()
    {
        _subjectFinder.FindAsync(Urn, false, Arg.Any<CancellationToken>()).Returns(School);
        _subjectFinder.FindAsync(OtherUrn, false, Arg.Any<CancellationToken>())
            .Returns(School with { UrnOrTrn = OtherUrn, Name = "Other Academy" });
        _orchestrator.CanAddWebSearchFindings(Arg.Any<string?>()).Returns(true);
        _orchestrator.GetBriefing(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new AIResult(Briefing, 10, City));
        WebSearchReturns(new AIResult("## More background information\n\nIn the news.", 5));
    }

    private void WebSearchReturns(AIResult result)
        => _orchestrator.GetWebSearchFindings(Arg.Any<BriefingParameters>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private void WebSearchFails()
        => _orchestrator.GetWebSearchFindings(Arg.Any<BriefingParameters>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

    private Task<AIResult> WebSearchRanWithTheGeneratedBriefing()
        => _orchestrator.Received(1).GetWebSearchFindings(
            Arg.Is<BriefingParameters>(p => p.SchoolUrnOrTrustTrn == Urn), Briefing, City, School.Website, Arg.Any<CancellationToken>());

    private Task<AIResult> WebSearchDidNotRun()
        => _orchestrator.DidNotReceive().GetWebSearchFindings(Arg.Any<BriefingParameters>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());

    private SummaryModel CreatePage(string urnOrTrn = Urn, IDataProtectionProvider? dataProtection = null,
        int runTimeoutSeconds = 360)
        => new(_orchestrator, _subjectFinder, dataProtection ?? _dataProtection,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiAgents:RunTimeout"] = runTimeoutSeconds.ToString()
            }).Build(), NullLogger<SummaryModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            Type = SubjectTypes.Establishment,
            UrnOrTrn = urnOrTrn,
        };

    // The token the page gives the browser with a generated briefing.
    private async Task<string> GenerateTokenAsync(string urnOrTrn = Urn)
    {
        var page = CreatePage(urnOrTrn);
        page.AcceptDisclaimer = true;
        await page.OnPostAsync(cancellationToken);
        return page.WebSearchToken!;
    }

    private static string? Html(IActionResult result)
    {
        var value = Assert.IsType<JsonResult>(result).Value!;
        return (string?)value.GetType().GetProperty("html")!.GetValue(value);
    }

    private static void AssertNotSearchable(SummaryModel page)
        => Assert.Equal(ErrorMessages.BriefingNotSearchable, Assert.Single(page.ModelState[string.Empty]!.Errors).ErrorMessage);

    [Fact]
    public async Task OnGet_WhenTheSchoolIsnotFound_SendsThePersonBackToTheStart()
    {
        var result = await CreatePage("999999").OnGetAsync(cancellationToken);

        Assert.Equal("Type", Assert.IsType<RedirectToPageResult>(result).PageName);
    }

    [Fact]
    public async Task OnGetStream_WhenTheSubjectIsNotFound_SendsTheNotFoundMessage()
    {
        var page = CreatePage("999999");
        var body = new MemoryStream();
        page.HttpContext.Response.Body = body;

        await page.OnGetStreamAsync(cancellationToken);

        var stream = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("event: error-message", stream);
        Assert.Contains(ErrorMessages.SubjectNotFound(page.SubjectNoun), stream);
    }

    [Theory]
    [InlineData("school", "This school can't be found. Go back and search for it again.")]
    [InlineData("trust", "This trust can't be found. Go back and search for it again.")]
    public void SubjectNotFound_UsesTheSubjectNoun(string subjectNoun, string expected)
    {
        Assert.Equal(expected, ErrorMessages.SubjectNotFound(subjectNoun));
    }

    [Fact]
    public async Task OnPost_WithoutTheDisclaimer_ShowsAnErrorAndGeneratesNothing()
    {
        var page = CreatePage();

        var result = await page.OnPostAsync(cancellationToken);

        Assert.IsType<PageResult>(result);
        Assert.Equal(SummaryModel.DisclaimerNotAccepted,
            Assert.Single(page.ModelState[nameof(SummaryModel.AcceptDisclaimer)]!.Errors).ErrorMessage);
        await _orchestrator.DidNotReceive().GetBriefing(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnPost_OffersTheWebSearchWithATokenNotTheBriefingsText()
    {
        var page = CreatePage();
        page.AcceptDisclaimer = true;

        await page.OnPostAsync(cancellationToken);

        Assert.Contains("Rated Good.", page.BriefingOutput);
        Assert.True(page.CanSearchWeb);
        Assert.NotNull(page.WebSearchToken);
        Assert.Matches("^[A-Za-z0-9_-]+$", page.WebSearchToken);
        Assert.DoesNotContain("Rated", page.WebSearchToken);
        Assert.DoesNotContain(City, page.WebSearchToken);
    }

    [Fact]
    public async Task OnPost_WhenTheBriefingCantBeSearched_DoesnotOfferTheWebSearch()
    {
        _orchestrator.CanAddWebSearchFindings(Arg.Any<string?>()).Returns(false);
        var page = CreatePage();
        page.AcceptDisclaimer = true;

        await page.OnPostAsync(cancellationToken);

        Assert.False(page.CanSearchWeb);
        Assert.Null(page.WebSearchToken);
    }

    [Fact]
    public async Task OnPost_WhenTheBriefingFails_ShowsAnUnlinkedError()
    {
        _orchestrator.GetBriefing(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var page = CreatePage();
        page.AcceptDisclaimer = true;

        await page.OnPostAsync(cancellationToken);

        Assert.Null(page.BriefingOutput);
        Assert.Single(page.ModelState[string.Empty]!.Errors);
        Assert.False(page.CanSearchWeb);
    }

    [Fact]
    public async Task OnPost_WhenTheBriefingTimesOut_ShowsConfiguredMinutes()
    {
        _orchestrator.GetBriefing(Arg.Any<BriefingParameters>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Agent 'briefing-agent' did not finish within 540 seconds."));
        var page = CreatePage(runTimeoutSeconds: 540);
        page.AcceptDisclaimer = true;

        await page.OnPostAsync(cancellationToken);

        Assert.Equal("The summary could not be generated within 9 minutes. Please try again.",
            Assert.Single(page.ModelState[string.Empty]!.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData(360, "6")]
    [InlineData(420, "7")]
    [InlineData(540, "9")]
    [InlineData(61, "2")]
    [InlineData(60, "1")]
    [InlineData(0, "6")]
    public void BriefingTimedOut_UsesConfiguredWholeMinutes(int timeoutSeconds, string expectedMinutes)
    {
        var minuteLabel = expectedMinutes == "1" ? "minute" : "minutes";
        Assert.Equal($"The summary could not be generated within {expectedMinutes} {minuteLabel}. Please try again.",
            ErrorMessages.BriefingTimedOut(timeoutSeconds));
    }

    [Fact]
    public async Task OnGetStream_SendsAWebSearchOfferWithATokenNotTheBriefingsText()
    {
        var page = CreatePage();
        var body = new MemoryStream();
        page.HttpContext.Response.Body = body;

        await page.OnGetStreamAsync(cancellationToken);

        var stream = Encoding.UTF8.GetString(body.ToArray());
        var offer = stream.Split("\n\n").Single(e => e.StartsWith("event: web-search-offer\n", StringComparison.Ordinal));
        Assert.Contains("\"token\":", offer);
        Assert.DoesNotContain("Rated", offer);
        Assert.DoesNotContain(City, offer);
        Assert.Contains("event: complete\n", stream);
    }

    [Fact]
    public async Task OnPostMoreInformation_SearchesTheWebWithTheBriefingAndCityFromTheToken()
    {
        var page = CreatePage();
        page.WebSearchToken = await GenerateTokenAsync();

        await page.OnPostMoreInformationAsync(cancellationToken);

        Assert.True(page.ModelState.IsValid);
        Assert.Contains("Rated Good.", page.BriefingOutput);
        Assert.Contains("In the news.", page.WebSearchOutput);
        await WebSearchRanWithTheGeneratedBriefing();
    }

    [Fact]
    public async Task OnPostMoreInformation_WhenTheWebSearchFails_OffersItAgainWithAWorkingToken()
    {
        WebSearchFails();
        var page = CreatePage();
        page.WebSearchToken = await GenerateTokenAsync();

        await page.OnPostMoreInformationAsync(cancellationToken);

        Assert.Single(page.ModelState[string.Empty]!.Errors);
        Assert.Contains("Rated Good.", page.BriefingOutput);
        Assert.True(page.CanSearchWeb);

        WebSearchReturns(new AIResult("Found it.", 1));
        var retry = CreatePage();
        retry.WebSearchToken = page.WebSearchToken;
        await retry.OnPostMoreInformationAsync(cancellationToken);
        Assert.True(retry.ModelState.IsValid);
        Assert.Contains("Found it.", retry.WebSearchOutput);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("!!!")]
    public async Task OnPostMoreInformation_WithAnUnusableToken_ShowsAnErrorAndDoesnotSearch(string? token)
    {
        var page = CreatePage();
        page.WebSearchToken = token;

        await page.OnPostMoreInformationAsync(cancellationToken);

        AssertNotSearchable(page);
        Assert.Null(page.WebSearchOutput);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostMoreInformation_WithAnAlteredToken_ShowsAnErrorAndDoesnotSearch()
    {
        var token = await GenerateTokenAsync();
        var page = CreatePage();
        page.WebSearchToken = token[..^4] + (token[^1] == 'A' ? "BBBB" : "AAAA");

        await page.OnPostMoreInformationAsync(cancellationToken);

        AssertNotSearchable(page);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostMoreInformation_WithAnotherSchoolsToken_ShowsAnErrorAndDoesnotSearch()
    {
        var page = CreatePage();
        page.WebSearchToken = await GenerateTokenAsync(OtherUrn);

        await page.OnPostMoreInformationAsync(cancellationToken);

        AssertNotSearchable(page);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostMoreInformation_WithATokenMadeWithOtherKeys_ShowsAnErrorAndDoesnotSearch()
    {
        // As after a restart or deploy, or on another instance.
        var page = CreatePage(dataProtection: new EphemeralDataProtectionProvider());
        page.WebSearchToken = await GenerateTokenAsync();

        await page.OnPostMoreInformationAsync(cancellationToken);

        AssertNotSearchable(page);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostWebSearch_SearchesTheWebWithTheBriefingAndCityFromTheToken()
    {
        var request = new WebSearchRequest(Urn, School.Name, await GenerateTokenAsync());

        var result = await CreatePage().OnPostWebSearchAsync(request, cancellationToken);

        Assert.Contains("In the news.", Html(result));
        await WebSearchRanWithTheGeneratedBriefing();
    }

    [Fact]
    public async Task OnPostWebSearch_WithoutARequest_IsABadRequest()
    {
        var result = await CreatePage().OnPostWebSearchAsync(null, cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        await WebSearchDidNotRun();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("!!!")]
    public async Task OnPostWebSearch_WithAnUnusableToken_IsABadRequest(string? token)
    {
        var result = await CreatePage().OnPostWebSearchAsync(new WebSearchRequest(Urn, School.Name, token!), cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostWebSearch_WithAnotherSchoolsToken_IsABadRequest()
    {
        var request = new WebSearchRequest(Urn, School.Name, await GenerateTokenAsync(OtherUrn));

        var result = await CreatePage().OnPostWebSearchAsync(request, cancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        await WebSearchDidNotRun();
    }

    [Fact]
    public async Task OnPostWebSearch_WhenTheWebSearchFails_IsAServerError()
    {
        WebSearchFails();
        var request = new WebSearchRequest(Urn, School.Name, await GenerateTokenAsync());

        var result = await CreatePage().OnPostWebSearchAsync(request, cancellationToken);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
