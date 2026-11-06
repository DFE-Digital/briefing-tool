using Dfe.Briefing.Tool.Application.Builders;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class AcademyCounterTests
{
    private static string Academy(int evidence, string urn, string phase, string localAuthority, string inspections)
        => $"--- establishment_index Evidence {evidence} ---\nestablishment_name: Academy {urn}\nestablishment_urn: {urn}\n" +
           $"phase_of_education: {phase}\nlocal_authority_name: {localAuthority}\nofsted_all_inspections: {inspections}\n\n";

    private static string Judged(string date, string judgement) => $"[{{\"inspection_date\": \"{date}\", \"overall_effectiveness\": \"{judgement}\"}}]";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("No academies were found for this trust.")]
    public void Count_WithNoAcademyRecords_ReturnsNull(string? records)
        => Assert.Null(AcademyCounter.Count(records));

    [Fact]
    public void Count_CountsARecordWithOnlyAName_AsTheAcademyAnotherRecordGivesThatNameWithItsUrn()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", Judged("2021-05-01", "Good")) +
            "--- establishment_index Evidence 2 ---\nestablishment_name: academy 900001\nofsted_all_inspections: " + Judged("2024-02-20", "Inadequate") + "\n\n" +
            Academy(3, "900002", "Secondary", "Exampleshire", Judged("2022-11-28", "Good"));

        Assert.Equal(
            "academies_counted: 2\n" +
            "by_phase: Primary 1, Secondary 1\n" +
            "by_latest_ofsted_inspection: Inadequate 1, Good 1\n" +
            "by_local_authority: Exampleshire 2",
            AcademyCounter.Count(records));
    }

    [Fact]
    public void Count_KeepsANameOnlyRecordApart_WhenTwoAcademiesShareThatName()
    {
        var records =
            "--- establishment_index Evidence 1 ---\nestablishment_name: St Mary's\nestablishment_urn: 900001\nphase_of_education: Primary\n\n" +
            "--- establishment_index Evidence 2 ---\nestablishment_name: St Mary's\nestablishment_urn: 900002\nphase_of_education: Secondary\n\n" +
            "--- establishment_index Evidence 3 ---\nestablishment_name: St Mary's\nphase_of_education: Primary\n\n";

        Assert.StartsWith("academies_counted: 3\n", AcademyCounter.Count(records), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Good", "Requires improvement", "Requires improvement")]   // different grades: the most serious
    [InlineData("Requires improvement", "Good", "Requires improvement")]   // whichever record comes first
    [InlineData(null, "Good", "Good")]                                     // a record without a grade doesn't hide one
    public void Count_ReconcilesRecordsSharingTheLatestInspectionDate(string? first, string? second, string counted)
    {
        static string Inspection(string? grade) => grade is null
            ? "[{\"inspection_date\": \"2024-02-20\"}]"
            : Judged("2024-02-20", grade);
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", Inspection(first)) +
            Academy(2, "900001", "Primary", "Exampleshire", Inspection(second));

        Assert.Contains($"by_latest_ofsted_inspection: {counted} 1\n", AcademyCounter.Count(records), StringComparison.Ordinal);
    }

    [Fact]
    public void Count_GivesAReportCard_WithEveryRecordsAreaGrades_WhenRecordsShareTheLatestInspectionDate()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", "[{\"inspection_date\": \"2025-12-02\", \"overall_effectiveness\": \"Good\"}]") +
            Academy(2, "900001", "Primary", "Exampleshire", "[{\"inspection_date\": \"2025-12-02\", \"achievement\": \"Needs attention\"}]") +
            Academy(3, "900001", "Primary", "Exampleshire", "[{\"inspection_date\": \"2025-12-02\", \"inclusion\": \"Strong standard\"}]");

        var counts = AcademyCounter.Count(records);

        Assert.Contains("by_latest_ofsted_inspection: Report card 1\n", counts, StringComparison.Ordinal);
        Assert.Contains("report_card_academies_with_an_area_graded: Needs attention 1, Strong standard 1\n", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_CountsByPhaseJudgementAndLocalAuthority()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", Judged("2023-03-14", "Good")) +
            Academy(2, "900002", "Secondary", "Otherton", Judged("2022-11-28", "Inadequate")) +
            Academy(3, "900003", "Primary", "Exampleshire", Judged("2021-05-01", "Requires improvement")) +
            Academy(4, "900004", "Primary", "Exampleshire", Judged("2019-01-10", "Outstanding"));

        Assert.Equal(
            "academies_counted: 4\n" +
            "by_phase: Primary 3, Secondary 1\n" +
            "by_latest_ofsted_inspection: Inadequate 1, Requires improvement 1, Good 1, Outstanding 1\n" +
            "by_local_authority: Exampleshire 3, Otherton 1",
            AcademyCounter.Count(records));
    }

    [Fact]
    public void Count_CountsAnAcademyWithSeveralRecordsOnce_TakingItsLatestInspectionFromAllOfThem()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", Judged("2018-02-01", "Requires improvement")) +
            "[Evidence 2]\nestablishment_urn: 900001\nofsted_all_inspections: " + Judged("2023-03-14", "Good") + "\n\n" +
            Academy(3, "900002", "Secondary", "Exampleshire", Judged("2022-11-28", "Good"));

        var counts = AcademyCounter.Count(records);

        Assert.StartsWith("academies_counted: 2\nby_phase: Primary 1, Secondary 1\n", counts, StringComparison.Ordinal);
        Assert.Contains("by_latest_ofsted_inspection: Good 2\n", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_GivesNotJudged_WhenTheLatestInspectionHasNoOverallJudgement_AndNotAvailable_WhenThereIsNoInspection()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire",
                "[{\"inspection_date\": \"2025-01-20\"}, {\"inspection_date\": \"2022-01-01\", \"overall_effectiveness\": \"Good\"}]") +
            Academy(2, "900002", "Primary", "Exampleshire", "[]") +
            Academy(3, "900003", "Primary", "Exampleshire", "not json") +
            Academy(4, "900004", "Primary", "Exampleshire", Judged("2023-01-01", "Good"));

        Assert.Contains("by_latest_ofsted_inspection: Good 1, Not judged 1, Not available 2\n",
            AcademyCounter.Count(records), StringComparison.Ordinal);
    }

    [Fact]
    public void Count_GivesNotAvailable_ForAMissingPhaseOrLocalAuthority_AndPutsItLast()
    {
        var records =
            "[Evidence 1]\nestablishment_urn: 900001\n\n" +
            Academy(2, "900002", "Secondary", "Otherton", Judged("2023-01-01", "Good"));

        var counts = AcademyCounter.Count(records);

        Assert.Contains("by_phase: Secondary 1, Not available 1\n", counts, StringComparison.Ordinal);
        Assert.EndsWith("by_local_authority: Otherton 1, Not available 1", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_PutsJudgementWordingItDoesntKnowAfterTheJudgements_AndBeforeNotJudged()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", "[{\"inspection_date\": \"2025-01-20\"}]") +
            Academy(2, "900002", "Primary", "Exampleshire", Judged("2023-01-01", "Serious weaknesses")) +
            Academy(3, "900003", "Primary", "Exampleshire", Judged("2023-01-01", "Good"));

        Assert.Contains("by_latest_ofsted_inspection: Good 1, Serious weaknesses 1, Not judged 1\n",
            AcademyCounter.Count(records), StringComparison.Ordinal);
    }

    private static string ReportCard(string date, params (string Area, string Grade)[] areas)
        => $"[{{\"inspection_date\": \"{date}\", " + string.Join(", ", areas.Select(area => $"\"{area.Area}\": \"{area.Grade}\"")) + "}]";

    [Fact]
    public void Count_KeepsGradedInspectionsReportCardsAndUngradedInspectionsApart()
    {
        var records =
            Academy(1, "900001", "Primary", "Exampleshire", Judged("2023-03-14", "Requires improvement")) +
            Academy(2, "900002", "Primary", "Exampleshire",
                ReportCard("2025-12-02", ("inclusion", "Expected standard"), ("achievement", "Needs attention"), ("leadership_and_governance", "Expected standard"))) +
            Academy(3, "900003", "Primary", "Exampleshire", "[{\"inspection_date\": \"2025-03-10\", \"leadership_and_management\": \"Good\"}]") +
            Academy(4, "900004", "Secondary", "Exampleshire",
                ReportCard("2026-01-20", ("inclusion", "Urgent improvement"), ("curriculum_and_teaching", "Needs attention")));

        var counts = AcademyCounter.Count(records);

        Assert.Contains("by_latest_ofsted_inspection: Requires improvement 1, Report card 2, Not judged 1\n", counts, StringComparison.Ordinal);
        // Each academy once under each grade any of its areas has.
        Assert.Contains("report_card_academies_with_an_area_graded: Urgent improvement 1, Needs attention 2, Expected standard 1\n", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_UsesTheLatestInspection_WhicheverSystemItWasUnder()
    {
        var records =
            "[Evidence 1]\nestablishment_urn: 900001\nofsted_all_inspections: [" +
            "{\"inspection_date\": \"2026-02-03\", \"inclusion\": \"Strong standard\"}, " +
            "{\"inspection_date\": \"2021-06-01\", \"overall_effectiveness\": \"Inadequate\"}]\n\n" +
            "[Evidence 2]\nestablishment_urn: 900002\nofsted_all_inspections: [" +
            "{\"inspection_date\": \"2023-05-01\", \"overall_effectiveness\": \"Outstanding\"}]\n\n" +
            "[Evidence 3]\nestablishment_urn: 900002\nofsted_all_inspections: [" +
            "{\"inspection_date\": \"2019-05-01\", \"achievement\": \"Exceptional\"}]";

        var counts = AcademyCounter.Count(records);

        Assert.Contains("by_latest_ofsted_inspection: Outstanding 1, Report card 1\n", counts, StringComparison.Ordinal);
        Assert.Contains("report_card_academies_with_an_area_graded: Strong standard 1\n", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_FindsReportCardGradesWhateverTheirFieldsAreCalled_NestedOrInAnyCase()
    {
        var records = Academy(1, "900001", "Primary", "Exampleshire",
            "[{\"inspection_date\": \"2026-02-03\", \"report_card\": {\"areas\": [{\"area\": \"Inclusion\", \"grade\": \"Strong Standard\"}]}, " +
            "\"overall_effectiveness\": \"Exceptional\"}]");

        var counts = AcademyCounter.Count(records);

        // A report card grade, even where an overall grade would be, makes it a report card - never an overall grade.
        Assert.Contains("by_latest_ofsted_inspection: Report card 1\n", counts, StringComparison.Ordinal);
        Assert.Contains("report_card_academies_with_an_area_graded: Strong Standard 1, Exceptional 1\n", counts, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_GivesNoReportCardLine_WhenNoAcademysLatestInspectionIsAReportCard()
        => Assert.DoesNotContain("report_card",
            AcademyCounter.Count(Academy(1, "900001", "Primary", "Exampleshire", Judged("2023-01-01", "Good"))), StringComparison.Ordinal);
}
