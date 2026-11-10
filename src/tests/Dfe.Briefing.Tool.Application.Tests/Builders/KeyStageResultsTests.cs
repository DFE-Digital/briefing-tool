using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class KeyStageResultsTests
{
    // The requested school is URN 900003; a row can add more records after its own, e.g. another school's.
    private const string Urn = "900003";

    private static ContextResult Records(string text) => new($"[Evidence 1]\nestablishment_name: Example Brook Special School\n{text}", HasEvidence: true);

    private const string AnotherSchool = "\n\n[Evidence 2]\nestablishment_name: Example Brook Academy\nestablishment_urn: 900099\n";

    [Theory]
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable\nestablishment_age_range: 3 to 19")]
    [InlineData("establishment_urn: 900003\nphase_of_education: not applicable  ")]
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable\nks2_performance_latest: ")]
    // Another school's phase and results don't count: only the requested school's own records do.
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable" + AnotherSchool + "phase_of_education: Secondary\nks4_performance_latest: {\"attainment_8_score\": \"46.2\"}")]
    // With no URN in any record, they can't be told apart, so every record is the school's.
    [InlineData("phase_of_education: Not applicable")]
    public void NotApplicable_ForASpecialSchoolWithNoResults_SaysTheMeasuresDoNotApply(string records)
    {
        var fact = KeyStageResults.NotApplicable(Records(records), Urn, " Example Brook Special School ");

        Assert.Equal("Example Brook Special School is a special school or alternative provision (phase \"Not applicable\") and its record "
            + "gives no KS2 or KS4 results: KS2 and KS4 measures don't apply to it.", fact);
    }

    [Theory]
    [InlineData("establishment_urn: 900003\nphase_of_education: Secondary")]
    [InlineData("establishment_urn: 900003\nphase_of_education: Primary")]
    [InlineData("establishment_urn: 900003\nestablishment_age_range: 3 to 19")]
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable\nks2_performance_latest: {\"reading_writing_maths_expected_standard\": \"4%\"}")]
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable\nks4_performance_latest: {\"attainment_8_score\": \"12.1\"}")]
    // Another school being special doesn't make the requested one special.
    [InlineData("establishment_urn: 900003\nphase_of_education: Secondary" + AnotherSchool + "phase_of_education: Not applicable")]
    // The school's own records disagree on its phase, so it isn't shown to be special.
    [InlineData("establishment_urn: 900003\nphase_of_education: Not applicable\n\n[Evidence 2]\nestablishment_urn: 900003\nphase_of_education: Secondary")]
    // Only another school's record is given.
    [InlineData("establishment_urn: 900099\nphase_of_education: Not applicable")]
    public void NotApplicable_GivesNothing_WhenTheSchoolIsNotSpecialOrHasResults(string records)
    {
        Assert.Null(KeyStageResults.NotApplicable(Records(records), Urn, "Example Brook Special School"));
    }

    [Fact]
    public void NotApplicable_GivesNothing_WhenThereIsNoRecord()
    {
        Assert.Null(KeyStageResults.NotApplicable(new ContextResult("No records were found.", HasEvidence: false), Urn, "Example Brook Special School"));
    }
}
