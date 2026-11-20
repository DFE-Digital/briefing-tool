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
    public void Order_ReadsInspectionsKeyedByPublicationDate_AndGivesNoOfstedOrder_WhenAnAcademysInspectionsCantBeRead()
    {
        static string Published(string date, string details) => $"ofsted_all_inspections: {{\"{date}\":{{\"inspection_details\":{{{details}}}}}}}";
        var readable =
            Academy(1, "Good Academy", Published("2023-12-18", "\"inspection_start_date\":\"2023-11-21\",\"overall_effectiveness\":\"Good\"")) +
            Academy(2, "Card Academy", Published("2026-03-25", "\"inspection_start_date\":\"2026-01-27\",\"ofsted_system\":\"report_cards\",\"achievement\":\"Needs attention\"")) +
            Academy(3, "Inadequate Academy", Published("2022-02-01", "\"inspection_start_date\":\"2022-01-11\",\"overall_effectiveness\":\"Inadequate\""));

        Assert.Equal(["Inadequate Academy", "Good Academy", "Card Academy"], AcademyCounter.Order(readable, trustRecord: null)!.Ofsted);
        Assert.Empty(AcademyCounter.Order(readable + Academy(4, "Unreadable Academy", "ofsted_all_inspections: not json"), trustRecord: null)!.Ofsted);
    }

    [Fact]
    public void Order_PutsKs2AndKs4HighestFirst_FromTheLatestYearsSchoolFigure_WhenResultsAreKeyedByYear()
    {
        static string Ks4(string latest, string previous) =>
            "ks4_performance_latest: {" +
            $"\"202324\":{{\"key_stage_4_measure\":{{\"attainment8_average\":{{\"total_average\":{{\"national\":\"45.9\",\"school\":\"{previous}\"}}}}}}}}," +
            $"\"202425\":{{\"key_stage_4_measure\":{{\"5_to_9_percent\":{{\"maths_5_to_9_pct\":{{\"school\":\"99%\"}}}}," +
            $"\"attainment8_average\":{{\"eng_average\":{{\"school\":\"99\"}},\"total_average\":{{\"local_authority\":\"48.5\",\"national\":\"46.1\",\"school\":\"{latest}\"}}}}}}}}}}";
        // Disadvantaged pupils, the other subjects and the progress scores aren't the result.
        static string Ks2(string expected) =>
            "ks2_performance_latest: {\"202425\":{\"key_stage_2_measure\":{\"expected_standard\":{" +
            "\"maths\":{\"all_pupils\":{\"school\":\"99%\"}}," +
            $"\"reading_writing_maths\":{{\"all_pupils\":{{\"local_authority\":\"64.74%\",\"national\":\"62.62%\",\"school\":\"{expected}\"}},\"disadvantaged_pupils\":{{\"school\":\"99%\"}}}}}}," +
            "\"progress_measure_score\":{\"maths\":{\"all_pupils\":{\"school\":\"Data not applicable\"}}}}}}";

        var records =
            Academy(1, "Secondary A", Ks4(latest: "48.5", previous: "60.0")) +
            Academy(2, "Secondary B", Ks4(latest: "56", previous: "40.0")) +
            Academy(3, "Primary A", Ks2("58%")) +
            Academy(4, "Primary B", Ks2("73%")) +
            Academy(5, "Primary Not Applicable", Ks2("Data not applicable"));

        var orders = AcademyCounter.Order(records, trustRecord: null)!;

        Assert.Equal(["Secondary B", "Secondary A"], orders.Ks4);
        Assert.Equal(["Primary B", "Primary A"], orders.Ks2);
    }

    [Fact]
    public void Order_TakesAttendanceFromTheTrustRecordsEstablishmentAttendance_MatchingEachAcademyByItsUrn()
    {
        static string Academy(string urn, string name) => $"[Evidence {urn}]\nestablishment_name: {name}\nestablishment_urn: {urn}\n\n";
        var records = Academy("149322", "Bishopswood School") + Academy("139079", "Kingfisher School") + Academy("151488", "Woodeaton Manor School");
        // The trust record writes one academy's name differently, gives one with no figures ({}), and one not in the
        // academies' records.
        var trust = "[Evidence 1]\ntrust_name: Example Trust\nestablishment_attendance: {" +
            "\"Bishopswood Sch (149322)\":{\"attendance_historic\":{\"202324\":{\"pct_overall_absence\":\"12.38%\"}},\"attendance_latest\":{\"pct_overall_absence\":\"9.36%\"}}," +
            "\"Kingfisher School (139079)\":{\"attendance_historic\":{\"202324\":{\"pct_overall_absence\":\"8.41%\"}},\"attendance_latest\":{\"pct_overall_absence\":\"8.83%\"}}," +
            "\"Woodeaton Manor School (151488)\":{}," +
            "\"Hawthorn Academy (151771)\":{\"attendance_latest\":{\"pct_overall_absence\":\"10.5%\"}}}";

        Assert.Equal(["Hawthorn Academy", "Bishopswood School", "Kingfisher School"], AcademyCounter.Order(records, trust)!.Absence);
    }

    [Fact]
    public void Order_PutsAttendanceHighestOverallAbsenceFirst_FromTheLatestAndHistoricFigures()
    {
        var records =
            // The latest figures, taken as the year after the latest historic one (2024/25), over the historic ones.
            Academy(1, "Academy A", "attendance_latest: {\"pct_overall_absence\":\"3.76%\",\"pct_persistent_absence\":\"4.82%\"}\n" +
                "attendance_historic: {\"202223\":{\"pct_overall_absence\":\"9.5%\"},\"202324\":{\"pct_overall_absence\":\"5.96%\"}}") +
            Academy(2, "Academy B", "attendance_historic: {\"202223\":{\"pct_overall_absence\":\"9.0%\"},\"202324\":{\"pct_overall_absence\":\"7.0%\"}}") +
            // The latest figures with no year: a year the trust record gives is later.
            Academy(3, "Academy C", "attendance_latest: {\"pct_overall_absence\":\"9.0%\"}");
        var trust = "[Evidence 1]\ntrust_name: Example Trust\nacademy_attendance: [" +
            "{\"academy\": \"Academy C\", \"academic_year\": \"202324\", \"overall_absence\": \"4.0%\"}, " +
            "{\"academy\": \"Academy A\", \"academic_year\": \"202324\", \"overall_absence\": \"20.0%\"}]";

        Assert.Equal(["Academy B", "Academy C", "Academy A"], AcademyCounter.Order(records, trust)!.Absence);
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
