using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Models;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Models;

public sealed class SubjectPageModelTests
{
    private static readonly BriefingSubject School =
        new("123456", "Test School", IsTrust: false, Region: "North");

    private static readonly BriefingSubject Trust =
        new("TR12345", "Test Trust", IsTrust: true, Region: "North");

    [Fact]
    public async Task LoadSubjectAsync_ForValidSchool_NormalisesAndLoadsSubject()
    {
        var finder = Substitute.For<IBriefingSubjectFinder>();
        finder.FindAsync("123456", false, Arg.Any<CancellationToken>())
            .Returns(School);
        var page = CreatePage(finder, SubjectTypes.Establishment, " 123456 ");

        var found = await page.LoadSubjectAsyncForTest(TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Same(School, page.Subject);
        await finder.Received(1).FindAsync("123456", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadSubjectAsync_ForValidTrust_NormalisesAndLoadsSubject()
    {
        var finder = Substitute.For<IBriefingSubjectFinder>();
        finder.FindAsync("TR12345", true, Arg.Any<CancellationToken>())
            .Returns(Trust);
        var page = CreatePage(finder, SubjectTypes.Trust, " tr12345 ");

        var found = await page.LoadSubjectAsyncForTest(TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Same(Trust, page.Subject);
        await finder.Received(1).FindAsync("TR12345", true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubjectTypes.Establishment, "TR12345")]
    [InlineData(SubjectTypes.Trust, "123456")]
    [InlineData("invalid", "123456")]
    [InlineData(null, "123456")]
    [InlineData(SubjectTypes.Establishment, null)]
    public async Task LoadSubjectAsync_WithWrongReferenceOrType_DoesNotCallFinder(
        string? type,
        string? urnOrTrn)
    {
        var finder = Substitute.For<IBriefingSubjectFinder>();
        var page = CreatePage(finder, type, urnOrTrn);

        var found = await page.LoadSubjectAsyncForTest(TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.Null(page.Subject);
        await finder.DidNotReceiveWithAnyArgs().FindAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadSubjectAsync_WhenRecordIsMissing_ReturnsFalse()
    {
        var finder = Substitute.For<IBriefingSubjectFinder>();
        finder.FindAsync("123456", false, Arg.Any<CancellationToken>())
            .Returns((BriefingSubject?)null);
        var page = CreatePage(finder, SubjectTypes.Establishment, "123456");

        var found = await page.LoadSubjectAsyncForTest(TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.Null(page.Subject);
    }

    private static TestSubjectPageModel CreatePage(IBriefingSubjectFinder finder, string? type, string? urnOrTrn)
        => new(finder)
        {
            Type = type,
            UrnOrTrn = urnOrTrn
        };

    private sealed class TestSubjectPageModel(IBriefingSubjectFinder subjectFinder) : SubjectPageModel(subjectFinder)
    {
        public Task<bool> LoadSubjectAsyncForTest(CancellationToken cancellationToken) => LoadSubjectAsync(cancellationToken);
    }
}
