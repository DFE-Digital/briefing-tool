using Dfe.Briefing.Tool.Application.Builders;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class AcademyCounterOrderTests
{
    private static string Academy(int evidence, string name, string fields)
        => $"[Evidence {evidence}]\nestablishment_name: {name}\nestablishment_urn: 90000{evidence}\n{fields}\n\n";

    private static string Inspection(string json) => $"ofsted_all_inspections: [{json}]";

    private static string Judged(string judgement) => Inspection($"{{\"inspection_date\": \"2023-01-01\", \"overall_effectiveness\": \"{judgement}\"}}");

    private static string ReportCard(params string[] grades)
        => Inspection("{\"inspection_date\": \"2026-01-01\", " + string.Join(", ", grades.Select((grade, i) => $"\"area_{i}\": \"{grade}\"")) + "}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No academies were found for this trust.")]
    public void Order_WithNoAcademyRecords_ReturnsNull(string? records)
        => Assert.Null(AcademyCounter.Order(records, trustRecord: null));

    [Fact]
    public void Order_PutsTheOfstedSummaryMostSeriousFirst_ThenReportCardsByTheirWorstArea_ThenNotJudged_ThenNotAvailable()
    {
        var records =
            Academy(1, "Outstanding Academy", Judged("Outstanding")) +
            Academy(2, "Card Fine", ReportCard("Expected standard", "Strong standard")) +
            Academy(3, "No Inspection Academy", "ofsted_all_inspections: []") +
            Academy(4, "Good B", Judged("Good")) +
            Academy(5, "Card Urgent", ReportCard("Needs attention", "Urgent improvement")) +
            Academy(6, "Inadequate Academy", Judged("Inadequate")) +
            Academy(7, "Not Judged Academy", Inspection("{\"inspection_date\": \"2025-03-01\"}")) +
            Academy(8, "Good A", Judged("Good")) +
            Academy(9, "Card Attention", ReportCard("Needs attention")) +
            Academy(10, "Requires Improvement Academy", Judged("Requires improvement"));

        var orders = AcademyCounter.Order(records, trustRecord: null)!;

        Assert.Equal(
            ["Inadequate Academy", "Requires Improvement Academy", "Good A", "Good B", "Outstanding Academy",
             "Card Urgent", "Card Attention", "Card Fine", "Not Judged Academy", "No Inspection Academy"],
            orders.Ofsted);
    }

    [Fact]
    public void Order_PutsKs2AndKs4HighestFirst_AlphabeticalOnATie_LeavingOutAnAcademyWithoutTheResult()
    {
        var records =
            Academy(1, "Primary B", "ks2_performance_latest: {\"academic_year\": \"202324\", \"reading_writing_maths_expected_standard\": \"58%\"}") +
            Academy(2, "Primary A", "ks2_performance_latest: {\"reading_writing_maths_expected_standard\": \"58%\"}") +
            Academy(3, "Primary C", "ks2_performance_latest: {\"reading_writing_maths_expected_standard\": 71}") +
            Academy(4, "Primary Suppressed", "ks2_performance_latest: {\"reading_writing_maths_expected_standard\": \"SUPP\"}") +
            Academy(5, "Secondary A", "ks4_performance_latest: {\"attainment_8_score\": \"46.2\"}") +
            Academy(6, "Secondary B", "ks4_performance_latest: {\"attainment_8_score\": \"51.0\"}") +
            Academy(7, "Secondary Broken", "ks4_performance_latest: not json");

        var orders = AcademyCounter.Order(records, trustRecord: null)!;

        Assert.Equal(["Primary C", "Primary A", "Primary B"], orders.Ks2);
        Assert.Equal(["Secondary B", "Secondary A"], orders.Ks4);
    }

    [Fact]
    public void Order_PutsAttendanceHighestOverallAbsenceFirst_FromEachAcademysLatestYear_OrTheTrustRecord()
    {
        var records =
            Academy(1, "Academy A", "school_attendance: [{\"academic_year\": \"202223\", \"overall_absence\": \"12.0%\"}, {\"academic_year\": \"202324\", \"overall_absence\": \"5.0%\"}]") +
            Academy(2, "Academy B", "school_attendance: [{\"academic_year\": \"202324\", \"overall_absence\": \"8.4%\"}]") +
            Academy(3, "Academy C", "phase_of_education: Primary") +
            Academy(4, "Academy D", "school_attendance: [{\"academic_year\": \"202223\", \"overall_absence\": \"9.0%\"}]");
        // The trust record gives Academy C, which its own record doesn't, and a later year for Academy D.
        var trust = "[Evidence 1]\ntrust_name: Example Trust\nacademy_attendance: [" +
            "{\"academy\": \"Academy C\", \"academic_year\": \"202324\", \"overall_absence\": \"6.1%\"}, " +
            "{\"academy\": \"academy d\", \"academic_year\": \"202324\", \"overall_absence\": \"4.0%\"}, " +
            "{\"academy\": \"Academy B\", \"academic_year\": \"202223\", \"overall_absence\": \"20.0%\"}]";

        var orders = AcademyCounter.Order(records, trust)!;

        Assert.Equal(["Academy B", "Academy C", "Academy A", "Academy D"], orders.Absence);
    }

    [Fact]
    public void Order_ListsAnAcademyWithSeveralRecordsOnce()
    {
        var records =
            "[Evidence 1]\nestablishment_name: Academy A\nestablishment_urn: 900001\nks2_performance_latest: {\"reading_writing_maths_expected_standard\": \"60%\"}\n\n" +
            "[Evidence 2]\nestablishment_name: Academy A\nestablishment_urn: 900001\n" + Judged("Good") + "\n\n";

        var orders = AcademyCounter.Order(records, trustRecord: null)!;

        Assert.Equal(["Academy A"], orders.Ofsted);
        Assert.Equal(["Academy A"], orders.Ks2);
        Assert.Empty(orders.Ks4);
        Assert.Empty(orders.Absence);
    }
}
