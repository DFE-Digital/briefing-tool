using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class TownFinderTests
{
    private static readonly BriefingParameters School = new("900001", "Exampleton Primary School", IsTrust: false);
    private static readonly BriefingParameters Trust = new("TR09001", "Example Learning Trust", IsTrust: true);
    private static readonly ContextResult None = new(string.Empty, HasEvidence: false);

    private static BriefingRecords SchoolRecords(string record)
        => new(new ContextResult($"[Evidence 1]\nestablishment_urn: 900001\n{record}", HasEvidence: true), None, None, None);

    private static BriefingRecords TrustRecords(string record, string academies = "")
        => new(new ContextResult($"[Evidence 1]\ntrust_reference_number: TR09001\n{record}", HasEvidence: true),
            academies.Length == 0 ? None : new ContextResult(academies, HasEvidence: true), None, None);

    [Theory]
    [InlineData("establishment_address: 2 School Lane, Exampleton, EX1 2BB", "Exampleton")]
    [InlineData("establishment_address: 2 School Lane,  Exampleton ,EX1 2BB", "Exampleton")]
    [InlineData("establishment_address: 2 School Lane, Exampleton, sw1a 2aa", "Exampleton")]
    [InlineData("establishment_address: 2 School Lane, Exampleton", "Exampleton")]
    [InlineData("establishment_address: 2 School Lane, Little Example, Exampleton, EX1 2BB", "Exampleton")]
    public void Find_ForASchool_TakesTheTownBeforeThePostcode(string record, string town)
        => Assert.Equal(town, TownFinder.Find(School, SchoolRecords(record)));

    [Theory]
    // No town before the postcode, only a street, a street with no town, or a number where the town would be.
    [InlineData("establishment_address: EX1 2BB\nlocal_authority_name: Exampleshire")]
    [InlineData("establishment_address: 2 School Lane\nlocal_authority_name: Exampleshire")]
    [InlineData("establishment_address: 2 School Lane, EX1 2BB\nlocal_authority_name: Exampleshire")]
    [InlineData("establishment_address: School Lane, Unit 4, EX1 2BB\nlocal_authority_name: Exampleshire")]
    [InlineData("establishment_address: \nlocal_authority_name: Exampleshire")]
    [InlineData("local_authority_name: Exampleshire")]
    public void Find_ForASchoolWithNoTownInItsAddress_TakesItsLocalAuthority(string record)
        => Assert.Equal("Exampleshire", TownFinder.Find(School, SchoolRecords(record)));

    [Theory]
    [InlineData("establishment_name: Exampleton Primary School")]
    [InlineData("establishment_address: EX1 2BB\nlocal_authority_name: ")]
    public void Find_ForASchoolWithNeitherATownNorALocalAuthority_GivesNone(string record)
        => Assert.Null(TownFinder.Find(School, SchoolRecords(record)));

    [Fact]
    public void Find_NeverTakesTheTownFromAnotherSchoolsRecord()
    {
        var records = new BriefingRecords(new ContextResult(
            "[Evidence 1]\nestablishment_urn: 900099\nestablishment_address: 1 Other Road, Othertown, OT1 1AA\n\n" +
            "[Evidence 2]\nestablishment_urn: 900001\nestablishment_address: 2 School Lane, Exampleton, EX1 2BB", HasEvidence: true), None, None, None);

        Assert.Equal("Exampleton", TownFinder.Find(School, records));
    }

    [Fact]
    public void Find_WithNoUrnInAnyRecord_ReadsEveryRecord_AsTheyCantBeToldApart()
    {
        var records = new BriefingRecords(new ContextResult("establishment_address: 2 School Lane, Exampleton, EX1 2BB", HasEvidence: true), None, None, None);

        Assert.Equal("Exampleton", TownFinder.Find(School, records));
    }

    [Fact]
    public void Find_ForATrust_TakesTheTownFromItsOwnAddress()
        => Assert.Equal("Exampleton", TownFinder.Find(Trust, TrustRecords("trust_address: 1 Example Road, Exampleton, EX1 1AA")));

    [Fact]
    public void Find_ForATrustWithNoTownInItsAddress_TakesTheLocalAuthorityAllItsAcademiesShare()
    {
        var academies = "[Evidence 1]\nestablishment_urn: 900001\nlocal_authority_name: Exampleshire\n\n" +
            "[Evidence 2]\nestablishment_urn: 900002\nlocal_authority_name: exampleshire ";

        Assert.Equal("Exampleshire", TownFinder.Find(Trust, TrustRecords("trust_address: EX1 1AA", academies)));
    }

    [Fact]
    public void Find_ForATrustWhoseAcademiesAreInSeveralLocalAuthorities_GivesNone()
    {
        var academies = "[Evidence 1]\nlocal_authority_name: Exampleshire\n\n[Evidence 2]\nlocal_authority_name: Otherhampton";

        Assert.Null(TownFinder.Find(Trust, TrustRecords("trust_name: Example Learning Trust", academies)));
    }

    [Fact]
    public void Find_ForATrust_NeverTakesTheLocalAuthorityFromItsOwnRecord_AsItGivesNone()
        => Assert.Null(TownFinder.Find(Trust, TrustRecords("local_authority_name: Exampleshire")));

    [Fact]
    public void Find_WithNoRecordForTheSubject_GivesNone()
        => Assert.Null(TownFinder.Find(School, new BriefingRecords(None, None, None, None)));
}
