using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class WebsiteAddressTests
{
    [Theory]
    [InlineData("establishment_website: www.example-valley.example.org", "establishment_website: https://www.example-valley.example.org")]
    [InlineData("trust_website: example-trust.org.uk", "trust_website: https://example-trust.org.uk")]
    [InlineData("website: www.example.org/about-us", "website: https://www.example.org/about-us")]
    [InlineData("establishment_website: WWW.EXAMPLE.ORG  ", "establishment_website: https://WWW.EXAMPLE.ORG  ")]
    public void WithScheme_AddsHttpsToABareAddress(string line, string expected)
    {
        var records = new ContextResult($"[Evidence 1]\nestablishment_name: Example\n{line}\nnumber_on_roll: 10", HasEvidence: true);

        var result = WebsiteAddress.WithScheme(records);

        Assert.Equal($"[Evidence 1]\nestablishment_name: Example\n{expected}\nnumber_on_roll: 10", result.Text);
    }

    [Theory]
    [InlineData("establishment_website: https://www.example.org")]
    [InlineData("establishment_website: http://www.example.org")]
    [InlineData("establishment_website: Not recorded")]
    [InlineData("establishment_website: ")]
    [InlineData("summary: see www.example.org for details")]
    public void WithScheme_LeavesAFullAddressOrAValueThatIsNotAnAddressAsItIs(string line)
    {
        var records = new ContextResult($"[Evidence 1]\n{line}", HasEvidence: true);

        Assert.Same(records, WebsiteAddress.WithScheme(records));
    }

    [Fact]
    public void WithScheme_AddsHttpsToEveryRecordsWebsite_AndKeepsTheSources()
    {
        var records = new ContextResult("[Evidence 1]\nestablishment_website: www.one.example.org\n\n[Evidence 2]\nestablishment_website: two.example.org\r\nphase_of_education: Primary", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "One"), new EvidenceSource(2, "Two")]
        };

        var result = WebsiteAddress.WithScheme(records);

        Assert.Equal("[Evidence 1]\nestablishment_website: https://www.one.example.org\n\n[Evidence 2]\nestablishment_website: https://two.example.org\r\nphase_of_education: Primary", result.Text);
        Assert.Equal(records.Sources, result.Sources);
    }

    [Fact]
    public void WithScheme_LeavesASetWithNoEvidenceAsItIs()
    {
        var records = new ContextResult("This school is not part of a trust.", HasEvidence: false);

        Assert.Same(records, WebsiteAddress.WithScheme(records));
    }
}
