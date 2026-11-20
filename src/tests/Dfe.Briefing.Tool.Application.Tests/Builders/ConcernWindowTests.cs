using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class ConcernWindowTests
{
    // 3 years before 6 November 2026 is 6 November 2023.
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static string Concern(int evidence, string date, string summary)
        => $"--- rise_concerns_index Evidence {evidence} ---\nconcern_date: {date}\nsummary: {summary}";

    [Fact]
    public void Recent_LeavesOutConcernsBeforeTheWindow_WithTheirSources()
    {
        var concerns = new ContextResult(
            $"{Concern(1, "2019-05-01", "Old")}\n\n{Concern(2, "2025-06-10", "Recent")}\n\n{Concern(3, "2023-11-06", "First day")}", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Old"), new EvidenceSource(2, "Recent"), new EvidenceSource(3, "First day")]
        };

        var recent = ConcernWindow.Recent(concerns, Today);

        Assert.True(recent.HasEvidence);
        Assert.Equal($"{Concern(2, "2025-06-10", "Recent")}\n\n{Concern(3, "2023-11-06", "First day")}", recent.Text);
        Assert.Equal([new EvidenceSource(2, "Recent"), new EvidenceSource(3, "First day")], recent.Sources);
    }

    [Fact]
    public void Recent_LeavesOutAConcernDatedAfterToday_AndKeepsOneDatedToday()
    {
        var concerns = new ContextResult($"{Concern(1, "2026-11-07", "Tomorrow")}\n\n{Concern(2, "2026-11-06", "Today")}", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Tomorrow"), new EvidenceSource(2, "Today")]
        };

        var recent = ConcernWindow.Recent(concerns, Today);

        Assert.Equal(Concern(2, "2026-11-06", "Today"), recent.Text);
        Assert.Equal([new EvidenceSource(2, "Today")], recent.Sources);
    }

    [Fact]
    public void Recent_KeepsAConcernWhoseDateCannotBeRead_AsItsAgeIsNotKnown()
    {
        var concerns = new ContextResult("[Evidence 1]\nconcern_date: not recorded\nsummary: A\n\n[Evidence 2]\nsummary: B", HasEvidence: true);

        Assert.Same(concerns, ConcernWindow.Recent(concerns, Today));
    }

    [Fact]
    public void Recent_SaysNoneWereFound_WhenEveryConcernIsOlderThanTheWindow()
    {
        var concerns = new ContextResult(Concern(1, "2020-01-10", "Old"), HasEvidence: true) { Sources = [new EvidenceSource(1, "Old")] };

        var recent = ConcernWindow.Recent(concerns, Today);

        Assert.False(recent.HasEvidence);
        Assert.Equal(ConcernWindow.NoneRecent, recent.Text);
        Assert.Empty(recent.Sources);
    }

    [Theory]
    [InlineData("No match.", false)]
    [InlineData("A note with no records.", true)]
    public void Recent_LeavesAResultWithNoRecordsAsItIs(string text, bool hasEvidence)
    {
        var concerns = new ContextResult(text, hasEvidence);

        Assert.Same(concerns, ConcernWindow.Recent(concerns, Today));
    }

    [Fact]
    public void NewestFirst_PutsTheMostRecentConcernFirst_KeepingEachOnesNumber_WithUndatedOnesLast()
    {
        var concerns = new ContextResult(
            $"A note.\n\n{Concern(1, "2024-01-15", "Oldest")}\n\n[Evidence 2]\nconcern_date: not recorded\nsummary: Undated\n\n{Concern(3, "2025-06-10", "Newest")}",
            HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Oldest"), new EvidenceSource(2, "Undated"), new EvidenceSource(3, "Newest")]
        };

        var ordered = ConcernWindow.NewestFirst(concerns);

        Assert.Equal(
            $"A note.\n\n{Concern(3, "2025-06-10", "Newest")}\n\n{Concern(1, "2024-01-15", "Oldest")}\n\n[Evidence 2]\nconcern_date: not recorded\nsummary: Undated",
            ordered.Text);
        Assert.Equal(concerns.Sources, ordered.Sources);
    }

    [Fact]
    public void NewestFirst_LeavesConcernsAlreadyInOrder_OrASetWithNoEvidence_AsTheyAre()
    {
        var inOrder = new ContextResult($"{Concern(1, "2025-06-10", "Newest")}\n\n{Concern(2, "2024-01-15", "Oldest")}", HasEvidence: true);
        var none = new ContextResult(ConcernWindow.NoneRecent, HasEvidence: false);

        Assert.Same(inOrder, ConcernWindow.NewestFirst(inOrder));
        Assert.Same(none, ConcernWindow.NewestFirst(none));
    }
}
