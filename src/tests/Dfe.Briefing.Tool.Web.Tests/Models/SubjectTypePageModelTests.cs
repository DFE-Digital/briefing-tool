using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Models;

public sealed class SubjectTypePageModelTests
{
    [Theory]
    [InlineData(SubjectTypes.Establishment, false, "school", "School name", "URN")]
    [InlineData(SubjectTypes.Trust, true, "trust", "Trust name", "TRN")]
    [InlineData("invalid", false, "school", "School name", "URN")]
    [InlineData(null, false, "school", "School name", "URN")]
    public void Type_DerivesLabelsAndValidity(string? type, bool isTrust, string subjectNoun, string nameLabel, string referenceLabel)
    {
        var page = CreatePage(type);

        Assert.Equal(isTrust, page.IsTrust);
        Assert.Equal(subjectNoun, page.SubjectNoun);
        Assert.Equal(nameLabel, page.NameLabel);
        Assert.Equal(referenceLabel, page.ReferenceLabel);
        Assert.Equal(type is SubjectTypes.Establishment or SubjectTypes.Trust, page.HasValidType);
    }

    [Fact]
    public void RedirectToStart_RedirectsToTypePage()
    {
        var result = CreatePage(SubjectTypes.Establishment).RedirectToStartForTest();

        Assert.Equal("Type", Assert.IsType<RedirectToPageResult>(result).PageName);
    }

    private static TestSubjectTypePageModel CreatePage(string? type)
        => new()
        {
            PageContext = new PageContext(),
            Type = type
        };

    private sealed class TestSubjectTypePageModel : SubjectTypePageModel
    {
        public IActionResult RedirectToStartForTest() => RedirectToStart();
    }
}
