using Dfe.Briefing.Tool.Application.Builders;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class AcademyListOrderTests
{
    private static readonly AcademyOrders Orders = new(
        Ofsted: ["Academy C", "Academy A", "Academy B"],
        Ks2: ["Academy B", "Academy A"],
        Ks4: [],
        Absence: ["Academy A", "Academy B"]);

    [Fact]
    public void Apply_PutsEachSectionsAcademyBulletsInItsOrder_LeavingItsSentencesWhereTheyAre()
    {
        var briefing =
            "# Example Trust\n\n" +
            "## Ofsted summary\n\n" +
            "1 Inadequate, 1 Good, 1 Report card.\n\n" +
            "- Academy A: Good (inspected 1 May 2023) [Evidence 2]\n" +
            "- Academy B: Report card (inspected 2 December 2025): inclusion: Expected standard [Evidence 3]\n" +
            "- Academy C: Inadequate (inspected 3 March 2022) [Evidence 4]\n\n" +
            "Ofsted stopped giving schools a single overall effectiveness grade in September 2024.\n\n" +
            "## KS2 summary\n\n" +
            "- Academy A: 58% (2023/24) [Evidence 2]\n" +
            "- Academy B: 71% (2023/24) [Evidence 3]\n\n" +
            "## Attendance\n\n" +
            "- Whole trust: overall absence 6.0% [Evidence 1]\n" +
            "- Academy B: overall absence 5.0% (2023/24) [Evidence 3]\n" +
            "- Academy A: overall absence 8.0% (2023/24) [Evidence 2]";

        var ordered = AcademyListOrder.Apply(briefing, Orders);

        Assert.Equal(
            "# Example Trust\n\n" +
            "## Ofsted summary\n\n" +
            "1 Inadequate, 1 Good, 1 Report card.\n\n" +
            "- Academy C: Inadequate (inspected 3 March 2022) [Evidence 4]\n" +
            "- Academy A: Good (inspected 1 May 2023) [Evidence 2]\n" +
            "- Academy B: Report card (inspected 2 December 2025): inclusion: Expected standard [Evidence 3]\n\n" +
            "Ofsted stopped giving schools a single overall effectiveness grade in September 2024.\n\n" +
            "## KS2 summary\n\n" +
            "- Academy B: 71% (2023/24) [Evidence 3]\n" +
            "- Academy A: 58% (2023/24) [Evidence 2]\n\n" +
            "## Attendance\n\n" +
            "- Whole trust: overall absence 6.0% [Evidence 1]\n" +
            "- Academy A: overall absence 8.0% (2023/24) [Evidence 2]\n" +
            "- Academy B: overall absence 5.0% (2023/24) [Evidence 3]",
            ordered);
    }

    [Fact]
    public void Apply_MovesEachBulletWithTheLinesIndentedUnderIt_MatchingNamesWhateverTheirCaseOrBold()
    {
        var briefing =
            "## Ofsted summary\n\n" +
            "- Academy B: Report card (inspected 2 December 2025) [Evidence 3]\n" +
            "  - inclusion: Expected standard\n" +
            "- **academy c**: Inadequate (inspected 3 March 2022) [Evidence 4]\n";

        var ordered = AcademyListOrder.Apply(briefing, Orders);

        Assert.Equal(
            "## Ofsted summary\n\n" +
            "- **academy c**: Inadequate (inspected 3 March 2022) [Evidence 4]\n" +
            "- Academy B: Report card (inspected 2 December 2025) [Evidence 3]\n" +
            "  - inclusion: Expected standard\n",
            ordered);
    }

    [Fact]
    public void Apply_LeavesOtherSectionsAndSectionsWithNoOrderAsWritten()
    {
        var briefing =
            "## School summary\n\n- Academy B [Evidence 3]\n- Academy A [Evidence 2]\n\n" +
            "## KS4 summary\n\n- Academy B: Attainment 8 score 40 [Evidence 3]\n- Academy A: Attainment 8 score 50 [Evidence 2]\n\n" +
            "## Concerns and support\n\n### Key issues\n\n- Academy B: Inadequate [Evidence 3]\n- Academy C: Ongoing [Evidence 4]";

        Assert.Equal(briefing, AcademyListOrder.Apply(briefing, Orders));
    }

    [Fact]
    public void Apply_KeepsAnAcademyNotInTheOrderAheadOfThoseThatAre_SoNothingIsDropped()
    {
        var briefing = "## KS2 summary\n\n- Academy A: 58% [Evidence 2]\n- Unknown Academy: 60% [Evidence 5]\n- Academy B: 71% [Evidence 3]";

        Assert.Equal(
            "## KS2 summary\n\n- Unknown Academy: 60% [Evidence 5]\n- Academy B: 71% [Evidence 3]\n- Academy A: 58% [Evidence 2]",
            AcademyListOrder.Apply(briefing, Orders));
    }

    [Fact]
    public void Apply_HandlesWindowsLineEndings()
    {
        var briefing = "## KS2 summary\r\n\r\n- Academy A: 58% [Evidence 2]\r\n- Academy B: 71% [Evidence 3]\r\n";

        Assert.Equal("## KS2 summary\r\n\r\n- Academy B: 71% [Evidence 3]\r\n- Academy A: 58% [Evidence 2]\r\n",
            AcademyListOrder.Apply(briefing, Orders));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Apply_WithNoBriefing_ReturnsItAsItIs(string? briefing)
        => Assert.Equal(briefing, AcademyListOrder.Apply(briefing, Orders));

    [Fact]
    public void Apply_WithNoOrders_ReturnsTheBriefingAsItIs()
    {
        const string briefing = "## KS2 summary\n\n- Academy A: 58%\n- Academy B: 71%";

        Assert.Equal(briefing, AcademyListOrder.Apply(briefing, orders: null));
    }
}
