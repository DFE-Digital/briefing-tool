using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class RecordFormatterTests
{
    private static string Format(string line)
        => RecordFormatter.Format(new ContextResult($"[Evidence 1]\n{line}", HasEvidence: true)).Text[("[Evidence 1]\n".Length)..];

    [Theory]
    [InlineData("date_established: 2019-04-01", "date_established: 1 April 2019")]
    [InlineData("concern_date: 2025-03-03T09:30:00Z", "concern_date: 3 March 2025")]
    [InlineData("concern_date: 2024-06-03 \r", "concern_date: 3 June 2024 \r")]
    [InlineData("""ofsted_all_inspections: [{"inspection_date": "2022-11-28", "overall_effectiveness": "Good"}]""",
        """ofsted_all_inspections: [{"inspection_date": "28 November 2022", "overall_effectiveness": "Good"}]""")]
    [InlineData("summary: Raised on 2024-06-03.", "summary: Raised on 3 June 2024.")]
    public void Format_WritesEveryDateInWords(string line, string expected)
        => Assert.Equal(expected, Format(line));

    [Theory]
    [InlineData("concern_date: 2024-13-45")]
    [InlineData("establishment_website: https://www.example.org/news/2024-06-03/")]
    [InlineData("reference: 12024-06-03")]
    public void Format_LeavesADateItCannotRead_OrThatIsPartOfSomethingElse_AsItIs(string line)
        => Assert.Equal(line, Format(line));

    [Theory]
    [InlineData("""ks2_performance_latest: {"academic_year": "202324", "reading_writing_maths_expected_standard": "58%"}""",
        """ks2_performance_latest: {"academic_year": "2023/24", "reading_writing_maths_expected_standard": "58%"}""")]
    [InlineData("academic_year: 201920", "academic_year: 2019/20")]
    [InlineData("ks4_academic_year: 199900", "ks4_academic_year: 1999/00")]
    [InlineData("academic_year: 202399", "academic_year: 202399")]
    [InlineData("pupil_count: 202324", "pupil_count: 202324")]
    public void Format_WritesAcademicYearsWithASlash_OnlyWhereTheYearsFollowOn(string line, string expected)
        => Assert.Equal(expected, Format(line));

    [Theory]
    [InlineData("""{"attainment_8_score": "47.3%"}""", """{"attainment_8_score": "47.3"}""")]
    [InlineData("""{"national_attainment_8_score": "46.1 %"}""", """{"national_attainment_8_score": "46.1"}""")]
    [InlineData("""{"progress_8_score": "-0.12%"}""", """{"progress_8_score": "-0.12"}""")]
    [InlineData("""{"ebacc_average_point_score": "4.05%"}""", """{"ebacc_average_point_score": "4.05"}""")]
    [InlineData("attainment_8_english_element: 10.2%", "attainment_8_english_element: 10.2")]
    [InlineData("""{"grade_5_or_above_english_and_maths": "45%"}""", """{"grade_5_or_above_english_and_maths": "45%"}""")]
    public void Format_TakesThePercentSignOffScores_ButNotOffPercentages(string line, string expected)
        => Assert.Equal(expected, Format(line));

    [Fact]
    public void Format_KeepsTheSources_AndLeavesASetWithNoEvidenceAsItIs()
    {
        var records = new ContextResult("[Evidence 1]\ndate_established: 2019-04-01", HasEvidence: true) { Sources = [new EvidenceSource(1, "Trust")] };
        var none = new ContextResult("No concerns.", HasEvidence: false);

        Assert.Equal(records.Sources, RecordFormatter.Format(records).Sources);
        Assert.Same(none, RecordFormatter.Format(none));
    }
}
