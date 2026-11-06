using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class KeyStageResultsTests
{
    private static ContextResult Record(string fields) => new($"[Evidence 1]\nestablishment_name: Example Brook Special School\n{fields}", HasEvidence: true);

    [Theory]
    [InlineData("phase_of_education: Not applicable\nestablishment_age_range: 3 to 19")]
    [InlineData("phase_of_education: not applicable  ")]
    [InlineData("phase_of_education: Not applicable\nks2_performance_latest: ")]
    public void NotApplicable_ForASpecialSchoolWithNoResults_SaysTheMeasuresDoNotApply(string fields)
    {
        var fact = KeyStageResults.NotApplicable(Record(fields), " Example Brook Special School ");

        Assert.Equal("Example Brook Special School is a special school or alternative provision (phase \"Not applicable\") and its record "
            + "gives no KS2 or KS4 results: KS2 and KS4 measures don't apply to it.", fact);
    }

    [Theory]
    [InlineData("phase_of_education: Secondary")]
    [InlineData("phase_of_education: Primary")]
    [InlineData("establishment_age_range: 3 to 19")]
    [InlineData("phase_of_education: Not applicable\nks2_performance_latest: {\"reading_writing_maths_expected_standard\": \"4%\"}")]
    [InlineData("phase_of_education: Not applicable\nks4_performance_latest: {\"attainment_8_score\": \"12.1\"}")]
    public void NotApplicable_GivesNothing_WhenTheSchoolIsNotSpecialOrHasResults(string fields)
    {
        Assert.Null(KeyStageResults.NotApplicable(Record(fields), "Example Brook Special School"));
    }

    [Fact]
    public void NotApplicable_GivesNothing_WhenThereIsNoRecord()
    {
        Assert.Null(KeyStageResults.NotApplicable(new ContextResult("No records were found.", HasEvidence: false), "Example Brook Special School"));
    }
}
