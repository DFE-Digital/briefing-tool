using Dfe.Briefing.Tool.Web.Sanitization;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Sanitization;

public sealed class CitationReferenceFormatterTests
{
    private static string Collect(string markdown) => CitationReferenceFormatter.CollectReferences(markdown);

    private static string References(string output) => output[output.IndexOf("## References", StringComparison.Ordinal)..];

    [Fact]
    public void WithoutCitations_AddsNoReferencesButGivesAddressesTheirScheme()
        => Assert.Equal("Rated good. See https://www.example.org for more.", Collect("Rated good. See www.example.org for more."));

    [Fact]
    public void NumbersEachSourceOnce_InTheOrderFirstCited()
    {
        var output = Collect("Rated good [Evidence A]. Attendance is high [Evidence B]. Pupils are happy [Evidence A].");

        Assert.Equal(
            """
            Rated good (<a href="#reference-1">1</a>). Attendance is high (<a href="#reference-2">2</a>). Pupils are happy (<a href="#reference-1">1</a>).

            ## References

            <ol>
            <li id="reference-1">Evidence A</li>
            <li id="reference-2">Evidence B</li>
            </ol>

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void GroupsCitationsSideBySide()
        => Assert.StartsWith(
            """Rated good (<a href="#reference-1">1</a>, <a href="#reference-2">2</a>).""",
            Collect("Rated good [Evidence A] [Evidence B]."));

    [Fact]
    public void UsesTheGivenReferenceIdPrefix()
    {
        var output = CitationReferenceFormatter.CollectReferences("In the news [Evidence A].", "web-reference");

        Assert.Contains("""<a href="#web-reference-1">1</a>""", output);
        Assert.Contains("""<li id="web-reference-1">Evidence A</li>""", output);
    }

    [Fact]
    public void NamesALinkAfterItsSourceAndPage()
        => Assert.Contains(
            """<li id="reference-1"><a href="https://www.thestar.co.uk/news/stanway-school-wins">The Star - stanway-school-wins</a></li>""",
            Collect("Won an award [The Star](https://www.thestar.co.uk/news/stanway-school-wins)."));

    [Fact]
    public void GivesEachPageOfOneSiteItsOwnReference_AndEachPageOneReference()
    {
        var output = References(Collect(
            "A [The Star](https://www.thestar.co.uk/news/one-story). B [The Star](https://www.thestar.co.uk/news/two-story). C [The Star](https://www.thestar.co.uk/news/one-story)."));

        Assert.Contains(">The Star - one-story</a>", output);
        Assert.Contains(">The Star - two-story</a>", output);
        Assert.DoesNotContain("reference-3", output);
    }

    [Theory]
    [InlineData("here")]
    [InlineData("Source")]
    [InlineData("https://example.org/")]
    public void NamesALinkAfterItsPageAlone_WhenItsTextSaysNothingAboutTheSource(string text)
        => Assert.Contains("""<a href="https://example.org/">example.org</a>""", References(Collect($"See [{text}](https://example.org/).")));

    [Fact]
    public void LeavesTrackingParametersOutOfThePageName()
        => Assert.Contains(">News - story?id=5</a>", Collect("[News](https://example.org/story?utm_source=x&id=5)"));

    [Fact]
    public void LeavesTheArticleNumberAndExtensionOutOfThePageName()
        => Assert.Contains(">Gazette - stanway-school-wins</a>", Collect("[Gazette](https://gazette.co.uk/news/24681357.stanway-school-wins.html)"));

    [Theory]
    [InlineData("[Chorus Trust] [chorustrust.org/news]")]
    [InlineData("[Chorus Trust] (chorustrust.org/news)")]
    [InlineData("[Chorus Trust]: chorustrust.org/news")]
    [InlineData("[Chorus Trust]([chorustrust.org](https://chorustrust.org/news))")]
    [InlineData("[Chorus Trust)([chorustrust.org](https://chorustrust.org/news))")]
    public void JoinsASourcesNameToItsAddress_AsOneReference(string citation)
    {
        var output = Collect($"Joined the trust {citation} last year.");

        Assert.Contains("""<li id="reference-1"><a href="https://chorustrust.org/news">Chorus Trust - news</a></li>""", output);
        Assert.DoesNotContain("reference-2", output);
    }

    [Fact]
    public void LinksASourceCitedOnlyByItsAddress()
        => Assert.Contains("""<li id="reference-1"><a href="https://chorustrust.org/news">news</a></li>""", Collect("See [chorustrust.org/news]."));

    [Theory]
    [InlineData("Test Academy 123456 Establishment details", "Test Academy - school record (URN 123456)")]
    [InlineData("123456 Establishment details", "School record (URN 123456)")]
    [InlineData("Test Trust TR01234 Trust details", "Test Trust - trust record (TRN TR01234)")]
    public void LabelsASchoolOrTrustRecord(string citation, string label)
        => Assert.Contains($"""<li id="reference-1">{label}</li>""", Collect($"Rated good [{citation}]."));

    [Theory]
    [InlineData("""<a href="https://testacademy.org.uk">Test Academy 123456 Establishment details</a>""")]
    [InlineData("""&lt;a href="https://testacademy.org.uk"&gt;Test Academy 123456 Establishment details&lt;/a&gt;""")]
    public void LinksARecordToTheWebsiteTheLibraryAttached_WhetherOrNotItsAnchorWasEscaped(string citation)
    {
        var output = Collect($"Rated good [{citation}].");

        Assert.Contains("""<li id="reference-1"><a href="https://testacademy.org.uk">Test Academy - school record (URN 123456)</a></li>""", output);
        Assert.DoesNotContain("&lt;a", output);
    }

    [Fact]
    public void CountsARecordOnce_WhateverWebsiteIsAttached()
    {
        var output = References(Collect(
            """A [Test Academy 123456 details]. B [<a href="https://testacademy.org.uk">Test Academy 123456 details</a>]."""));

        Assert.DoesNotContain("reference-2", output);
    }

    [Fact]
    public void EncodesAPageNameThatIsntAnAddress()
        => Assert.Contains(">Notes - &lt;draft&gt;</a>", Collect("[Notes](<draft>)"));
}
