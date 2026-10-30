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

    private static readonly AgentDefinition Establishment = new(AgentNames.Establishment, SystemPromptKey.Establishment);
    private static readonly AgentDefinition Trust = new(AgentNames.Trust, SystemPromptKey.Trust);
    private static readonly AgentDefinition RiseConcerns = new(AgentNames.RiseConcerns, SystemPromptKey.RiseConcerns);
    private static readonly AgentDefinition RecastConcerns = new(AgentNames.RecastConcerns, SystemPromptKey.RecastConcerns);

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

    private static ContextResult Record(string text) => new(text, HasEvidence: true);

    private Task ReceivedFilteredSearch(string index, string filter, int size = EvidencePerSearch)
        => _contextRetriever.Received(1).GetContextAsync(index, "*", size,
            Arg.Is<ODataFilter>(f => f.ToString() == filter), Arg.Any<CancellationToken>());

    [Fact]
    public async Task School_IsFoundByUrn()
    {
        await _sut.GetEvidenceAsync(Establishment, School, subjectEvidence: null, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Establishment, "establishment_urn eq '123456'");
    }

    [Theory]
    [InlineData("TR01234")]
    [InlineData(" tr01234 ")]
    public async Task Trust_IsFoundByTrn_InTheIndexsCase(string typedTrn)
    {
        await _sut.GetEvidenceAsync(Trust, new BriefingParameters(typedTrn, "Test Trust", IsTrust: true), subjectEvidence: null, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Trust, "trust_reference_number eq 'TR01234'");
    }

    [Fact]
    public async Task SchoolsTrust_IsFoundByTheTrustNameInTheSchoolsOwnRecord()
    {
        var school = Record("establishment_urn: 123456\nassociated_trust_name: O'NEILL ACADEMY TRUST\nmp_name: Jane Smith");

        await _sut.GetEvidenceAsync(Trust, School, school, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Trust, "trust_name eq 'O''NEILL ACADEMY TRUST'");
    }

    [Fact]
    public async Task SchoolsTrust_WhenTheSchoolIsNotInATrust_ReturnsNoEvidenceWithoutSearching()
    {
        var school = Record("establishment_urn: 123456\nestablishment_type: Community school");

        var result = await _sut.GetEvidenceAsync(Trust, School, school, _cancellationToken);

        Assert.False(result.HasEvidence);
        await _contextRetriever.DidNotReceiveWithAnyArgs().GetContextAsync(default!, default!, default, default, _cancellationToken);
    }

    [Fact]
    public async Task TrustsAcademies_AreFoundByTheTrustNameInTheTrustsOwnRecord()
    {
        var trust = Record("trust_name: EXAMPLE TRUST\ntrust_reference_number: TR01234");

        await _sut.GetEvidenceAsync(Establishment, TrustSubject, trust, _cancellationToken);

        await ReceivedFilteredSearch(EvidenceCategory.Establishment, "associated_trust_name eq 'EXAMPLE TRUST'", AcademiesPerTrust);
    }

    [Fact]
    public async Task TrustsAcademies_WhenTheTrustRecordWasNotFound_ReturnsNoEvidenceWithoutSearching()
    {
        var result = await _sut.GetEvidenceAsync(Establishment, TrustSubject, new ContextResult("No trust found.", HasEvidence: false), _cancellationToken);

        Assert.False(result.HasEvidence);
        await _contextRetriever.DidNotReceiveWithAnyArgs().GetContextAsync(default!, default!, default, default, _cancellationToken);
    }

    [Theory]
    [InlineData(AgentNames.RiseConcerns, EvidenceCategory.RiseConcerns)]
    [InlineData(AgentNames.RecastConcerns, EvidenceCategory.RecastConcerns)]
    public async Task Concerns_AreFoundByText_SinceTheirIndexesHaveNoFilterableFields(string agentName, string index)
    {
        var agent = agentName == AgentNames.RiseConcerns ? RiseConcerns : RecastConcerns;

        await _sut.GetEvidenceAsync(agent, School, subjectEvidence: null, _cancellationToken);

        await _contextRetriever.Received(1).GetContextAsync(index, "Test Academy 123456", EvidencePerSearch,
            Arg.Is<ODataFilter>(f => f.IsEmpty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownAgent_Throws()
    {
        var unknown = new AgentDefinition("unknown-agent", "Unknown");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetEvidenceAsync(unknown, School, subjectEvidence: null, _cancellationToken));
    }
}
