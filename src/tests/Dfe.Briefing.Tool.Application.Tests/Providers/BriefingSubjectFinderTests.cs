using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Filters;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Providers;

public sealed class BriefingSubjectFinderTests
{
    private const string SchoolRecord = """
        [Evidence 1]
        establishment_name: Exampleton Primary School
        establishment_urn: 900001
        establishment_type: Academy converter
        gor_name: South East
        establishment_website: www.exampleton-primary.example.org
        last_changed_date: 2026-10-15
        """;

    private const string TrustRecord = """
        [Evidence 1]
        trust_name: Example Coast Trust
        trust_reference_number: TR09002
        trust_type: Multi-academy trust
        region: South West
        """;

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly BriefingSubjectFinder _sut;

    public BriefingSubjectFinderTests() => _sut = new BriefingSubjectFinder(_contextRetriever);

    private void IndexReturns(string text, bool hasEvidence = true)
        => _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult(text, hasEvidence)));

    [Fact]
    public async Task School_IsFoundByUrn_InTheEstablishmentIndex()
    {
        IndexReturns(SchoolRecord);

        var school = await _sut.FindAsync(" 900001 ", _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Establishment, "*", 1,
            Arg.Is<ODataFilter>(f => f.ToString() == "establishment_urn eq '900001'"), Arg.Any<CancellationToken>());
        Assert.Equal(new BriefingSubject("900001", "Exampleton Primary School", IsTrust: false, Region: "South East",
            Website: "www.exampleton-primary.example.org", RecordDate: new DateOnly(2026, 10, 15)), school);
        Assert.Equal(new BriefingParameters("900001", "Exampleton Primary School"), school!.ToParameters());
    }

    [Fact]
    public async Task Trust_IsFoundByTrn_InTheIndexsCase()
    {
        IndexReturns(TrustRecord);

        var trust = await _sut.FindAsync("tr09002", _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Trust, "*", 1,
            Arg.Is<ODataFilter>(f => f.ToString() == "trust_reference_number eq 'TR09002'"), Arg.Any<CancellationToken>());
        Assert.Equal(new BriefingSubject("TR09002", "Example Coast Trust", IsTrust: true, Region: "South West"), trust);
    }

    [Theory]
    [InlineData("last_changed_date: 15-10-2026")]
    [InlineData("last_updated: 2026-10-15T06:00:00Z")]
    public async Task RecordDate_IsReadInEitherFormat(string dateLine)
    {
        IndexReturns($"[Evidence 1]\nestablishment_name: Exampleton School\n{dateLine}");

        var school = await _sut.FindAsync("900001", _cancellationToken);

        Assert.Equal(new DateOnly(2026, 10, 15), school!.RecordDate);
    }

    [Fact]
    public async Task UnreadableRecordDate_IsLeftOut()
    {
        IndexReturns("[Evidence 1]\nestablishment_name: Exampleton School\nlast_changed_date: unknown");

        Assert.Null((await _sut.FindAsync("900001", _cancellationToken))!.RecordDate);
    }

    [Fact]
    public async Task Search_FindsByTheStartOfEachWord_GivingEachRecordFound()
    {
        const string records = """
            --- trust_index Evidence 1 ---
            trust_name: Aspirations Academies Trust
            trust_reference_number: TR00103
            region: South East

            --- trust_index Evidence 2 ---
            trust_name: Aspire Learning Trust
            trust_reference_number: tr02786
            """;
        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult(records, HasEvidence: true)
            {
                Sources = [new EvidenceSource(1, "Aspirations", new Uri("https://www.aspirationsacademies.org/")), new EvidenceSource(2, "Aspire", null)]
            }));

        var trusts = await _sut.SearchAsync(" aspir* (acad ", isTrust: true, _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.Trust, "aspir* acad*", BriefingSubjectFinder.MaxSearchResults,
            Arg.Is<ODataFilter>(f => f.IsEmpty), Arg.Any<CancellationToken>());
        Assert.Equal(
            [
                new BriefingSubject("TR00103", "Aspirations Academies Trust", IsTrust: true, Region: "South East", Website: "https://www.aspirationsacademies.org/"),
                new BriefingSubject("TR02786", "Aspire Learning Trust", IsTrust: true, Region: null)
            ],
            trusts);
    }

    [Fact]
    public async Task Search_ForAWholeUrn_FindsThatSchool()
    {
        IndexReturns(SchoolRecord);

        var schools = await _sut.SearchAsync("900001", isTrust: false, _cancellationToken);

        Assert.Equal("Exampleton Primary School", Assert.Single(schools).Name);
    }

    [Fact]
    public async Task Search_ForATrnOnTheSchoolSearch_FindsTheTrust()
    {
        IndexReturns(TrustRecord);

        var found = await _sut.SearchAsync("tr 09002", isTrust: false, _cancellationToken);

        Assert.True(Assert.Single(found).IsTrust);
    }

    [Theory]
    [InlineData("1234", false, "establishment_index", "establishment_urn ge '123400' and establishment_urn le '123499'")]
    [InlineData("900", true, "establishment_index", "establishment_urn ge '900000' and establishment_urn le '900999'")]
    [InlineData("tr001", false, "trust_index", "trust_reference_number ge 'TR00100' and trust_reference_number le 'TR00199'")]
    public async Task Search_ForTheStartOfAReference_FindsEveryReferenceStartingWithIt(string typed, bool isTrust, string index, string filter)
    {
        IndexReturns(string.Empty, hasEvidence: false);

        await _sut.SearchAsync(typed, isTrust, _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(index, "*", BriefingSubjectFinder.MaxSearchResults,
            Arg.Is<ODataFilter>(f => f.ToString() == filter), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_ForTheStartOfAUrn_GivesTheSchoolsFound()
    {
        IndexReturns("--- establishment_index Evidence 1 ---\nestablishment_name: Exampleton School\nestablishment_urn: 900001");

        var schools = await _sut.SearchAsync("9000", isTrust: false, _cancellationToken);

        Assert.Equal("900001", Assert.Single(schools).UrnOrTrn);
    }

    [Fact]
    public async Task Search_WithNoLettersOrDigits_DoesNotSearch()
    {
        Assert.Empty(await _sut.SearchAsync(" *() ", isTrust: true, _cancellationToken));

        await _contextRetriever.DidNotReceiveWithAnyArgs().GetContextAsync(default!, default!, default, default, _cancellationToken);
    }

    [Fact]
    public async Task NoRecord_IsNotFound()
    {
        IndexReturns(string.Empty, hasEvidence: false);

        Assert.Null(await _sut.FindAsync("900001", _cancellationToken));
    }

    [Fact]
    public async Task RecordWithoutAName_IsNotFound()
    {
        IndexReturns("[Evidence 1]\nestablishment_urn: 900001");

        Assert.Null(await _sut.FindAsync("900001", _cancellationToken));
    }

    [Theory]
    [InlineData("[Evidence 1]\r\nestablishment_name: Exampleton School\r\nregion: \r\n")]
    [InlineData("[Evidence 1]\nestablishment_name: Exampleton School\ngor_name: Not applicable")]
    [InlineData("[Evidence 1]\nestablishment_name: Exampleton School")]
    public async Task BlankNotApplicableOrMissingRegion_IsLeftOut(string record)
    {
        IndexReturns(record);

        var school = await _sut.FindAsync("900001", _cancellationToken);

        Assert.NotNull(school);
        Assert.Equal("Exampleton School", school.Name);
        Assert.Null(school.Region);
    }
}
