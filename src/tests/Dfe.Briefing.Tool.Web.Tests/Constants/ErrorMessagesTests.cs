using Dfe.Briefing.Tool.Web.Constants;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Constants;

public sealed class ErrorMessagesTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void BriefingTimedOut_WithNoTimeoutUsesTheDefault(int timeoutSeconds)
    {
        Assert.Equal("The summary could not be generated within 6 minutes. Please try again.",
            ErrorMessages.BriefingTimedOut(timeoutSeconds));
    }

    [Theory]
    [InlineData(60, "The summary could not be generated within 1 minute. Please try again.")]
    [InlineData(61, "The summary could not be generated within 2 minutes. Please try again.")]
    [InlineData(360, "The summary could not be generated within 6 minutes. Please try again.")]
    [InlineData(420, "The summary could not be generated within 7 minutes. Please try again.")]
    [InlineData(540, "The summary could not be generated within 9 minutes. Please try again.")]
    public void BriefingTimedOut_FormatsTheConfiguredMinutes(int timeoutSeconds, string expected)
    {
        Assert.Equal(expected, ErrorMessages.BriefingTimedOut(timeoutSeconds));
    }

    [Theory]
    [InlineData("school", "This school can't be found. Go back and search for it again.")]
    [InlineData("trust", "This trust can't be found. Go back and search for it again.")]
    public void SubjectNotFound_UsesTheSubjectNoun(string subjectNoun, string expected)
    {
        Assert.Equal(expected, ErrorMessages.SubjectNotFound(subjectNoun));
    }

    [Fact]
    public void FixedMessages_AreDefinedForUserAndLogOutput()
    {
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.PageNotFound));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.NotImplemented));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.ProblemWithService));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.BriefingNotSearchable));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.BriefingFailed));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.BriefingBusy));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.WebSearchFailed));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.WebSearchBusy));
        Assert.False(string.IsNullOrWhiteSpace(ErrorMessages.ApplicationInsightsMissing));
        Assert.Contains("{RequestId}", ErrorMessages.ErrorPageLogTemplate);
        Assert.Contains("{ErrorMessage}", ErrorMessages.ErrorPageLogTemplate);
        Assert.Contains("{RequestId}", ErrorMessages.ErrorPageUnhandledLogTemplate);
        Assert.Contains("{Message}", ErrorMessages.ErrorPageUnhandledLogTemplate);
        Assert.Contains("{RunTimeoutSeconds}", ErrorMessages.BriefingTimedOutLog);
        Assert.Contains("{SchoolOrTrustName}", ErrorMessages.BriefingTimedOutLog);
        Assert.Contains("{SchoolOrTrustName}", ErrorMessages.BriefingFailedLog);
    }
}
