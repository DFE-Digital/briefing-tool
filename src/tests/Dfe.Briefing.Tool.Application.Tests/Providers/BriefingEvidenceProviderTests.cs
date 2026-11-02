using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Filters;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Providers;

public sealed class BriefingEvidenceProviderTests
{
    private const int EvidencePerSearch = 4;
    private const int AcademiesPerTrust = 98;

    private static readonly BriefingParameters School = new("123456", "Test Academy", IsTrust: false);
    private static readonly BriefingParameters TrustSubject = new("TR01234", "Test Trust", IsTrust: true);

    private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;
    private readonly IContextRetriever _contextRetriever = Substitute.For<IContextRetriever>();
    private readonly BriefingEvidenceProvider _sut;

    public BriefingEvidenceProviderTests()
    {
        _contextRetriever.GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("evidence", HasEvidence: true)));
        _sut = new BriefingEvidenceProvider(_contextRetriever, new BriefingOptions { MaxEvidencePerSearch = EvidencePerSearch, MaxAcademiesPerTrust = AcademiesPerTrust });
    }

    private void IndexReturns(string index, string text, bool hasEvidence = true)
        => _contextRetriever.GetContextAsync(index, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult(text, hasEvidence)));

    private Task ReceivedFilteredSearch(string index, string filter, int size = EvidencePerSearch)
        => _contextRetriever.Received(1).GetContextAsync(index, "*", size,
            Arg.Is<ODataFilter>(f => f.ToString() == filter), Arg.Any<CancellationToken>());

    [Fact]
    public async Task School_IsFoundByUrn_ThenItsTrustByTheTrustNameInItsOwnRecord()
    {
        IndexReturns(EvidenceCategory.Establishment, "establishment_urn: 123456\nassociated_trust_name: O'NEILL ACADEMY TRUST\nmp_name: Jane Smith");

        var records = await _sut.GetRecordsAsync(School, progress: null, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Establishment, "establishment_urn eq '123456'");
        await ReceivedFilteredSearch(EvidenceCategory.Trust, "trust_name eq 'O''NEILL ACADEMY TRUST'");
        Assert.NotNull(records);
        Assert.Contains("O'NEILL", records.Subject.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task School_NotInATrust_HasNoTrustRecord_WithoutSearchingForOne()
    {
        IndexReturns(EvidenceCategory.Establishment, "establishment_urn: 123456\nestablishment_type: Community school");

        var records = await _sut.GetRecordsAsync(School, progress: null, _cancellationToken);

        Assert.False(records!.Related.HasEvidence);
        await _contextRetriever.DidNotReceive().GetContextAsync(EvidenceCategory.Trust, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("TR01234")]
    [InlineData(" tr01234 ")]
    public async Task Trust_IsFoundByTrn_InTheIndexsCase_ThenItsAcademiesByItsName(string typedTrn)
    {
        IndexReturns(EvidenceCategory.Trust, "trust_name: EXAMPLE TRUST\ntrust_reference_number: TR01234");

        await _sut.GetRecordsAsync(new BriefingParameters(typedTrn, "Test Trust", IsTrust: true), progress: null, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Trust, "trust_reference_number eq 'TR01234'");
        await ReceivedFilteredSearch(EvidenceCategory.Establishment, "associated_trust_name eq 'EXAMPLE TRUST'", AcademiesPerTrust);
    }

    [Theory]
    [InlineData(EvidenceCategory.RiseConcerns)]
    [InlineData(EvidenceCategory.RecastConcerns)]
    public async Task Concerns_AreFoundByTheWholeNameOrTheReference_SinceTheirIndexesHaveNoFilterableFields(string index)
    {
        await _sut.GetRecordsAsync(School, progress: null, _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(index, "\"Test Academy\" 123456", EvidencePerSearch,
            Arg.Is<ODataFilter>(f => f.IsEmpty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Concerns_Query_TakesQuotesAndBackslashesOutOfTheName_SoItStaysOnePhrase()
    {
        await _sut.GetRecordsAsync(new BriefingParameters("123456", "The \"Best\" School\\", IsTrust: false), progress: null, _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(EvidenceCategory.RiseConcerns, "\"The  Best  School\" 123456", EvidencePerSearch,
            Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachSearch_GivesItsOwnSetOfRecords()
    {
        IndexReturns(EvidenceCategory.Trust, "trust_name: EXAMPLE TRUST");
        IndexReturns(EvidenceCategory.Establishment, "academies");
        IndexReturns(EvidenceCategory.RiseConcerns, "rise");
        IndexReturns(EvidenceCategory.RecastConcerns, "recast");

        var records = await _sut.GetRecordsAsync(TrustSubject, progress: null, _cancellationToken);

        Assert.Equal(("trust_name: EXAMPLE TRUST", "academies", "rise", "recast"),
            (records!.Subject.Text, records.Related.Text, records.RiseConcerns.Text, records.RecastConcerns.Text));
    }

    [Theory]
    [InlineData(false, EvidenceCategory.Establishment)]
    [InlineData(true, EvidenceCategory.Trust)]
    public async Task NoRecordForTheSubject_GivesNoRecords_WithoutSearchingForAnythingElse(bool isTrust, string index)
    {
        IndexReturns(index, "No match.", hasEvidence: false);

        var records = await _sut.GetRecordsAsync(isTrust ? TrustSubject : School, progress: null, _cancellationToken);

        Assert.Null(records);
        await _contextRetriever.Received(1).GetContextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Progress_SaysWhatsBeingCollected_ThenWhatEachSearchFound()
    {
        _contextRetriever.GetContextAsync(EvidenceCategory.Establishment, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("establishment_urn: 123456\nassociated_trust_name: EXAMPLE TRUST", HasEvidence: true)
            {
                Sources = [new EvidenceSource(1, "Test Academy")]
            }));
        _contextRetriever.GetContextAsync(EvidenceCategory.RiseConcerns, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ODataFilter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContextResult("rise", HasEvidence: true) { Sources = [new EvidenceSource(1, "A"), new EvidenceSource(2, "B")] }));
        IndexReturns(EvidenceCategory.RecastConcerns, "No match.", hasEvidence: false);
        var steps = new List<string>();

        await _sut.GetRecordsAsync(School, new SynchronousProgress(steps.Add), _cancellationToken);

        // The last three searches run at the same time, so each reports when it finishes, in whatever order that is.
        Assert.Equal(["Collecting the school's record...", "Collected the school's record (1 record)",
            "Collecting its trust's record, Rise concerns and Recast concerns..."], steps[..3]);
        string[] found = ["Collected its trust's record", "Collected Rise concerns (2 records)", "No Recast concerns found"];
        Assert.Equal(found.Order(StringComparer.Ordinal), steps[3..].Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Progress_ForATrust_NamesItsAcademies_AndSaysWhenNoneWereFound()
    {
        IndexReturns(EvidenceCategory.Trust, "trust_reference_number: TR01234");
        var steps = new List<string>();

        await _sut.GetRecordsAsync(TrustSubject, new SynchronousProgress(steps.Add), _cancellationToken);

        Assert.Equal("Collecting its academies' records, Rise concerns and Recast concerns...", steps[2]);
        Assert.Contains("No academy records found", steps);
    }

    // Progress<T> reports on the thread pool, so a test can't rely on it; this reports as it's called.
    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value)
        {
            lock (this)
                report(value);
        }
    }
}
