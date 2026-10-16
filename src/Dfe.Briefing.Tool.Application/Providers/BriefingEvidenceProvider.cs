using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Filters;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

/// <summary>
/// Provides evidence for briefing agents by retrieving context from various sources based on the agent definition and briefing parameters.
/// </summary>
/// <param name="contextRetriever">The context retriever.</param>
/// <param name="options">The briefing options.</param>
internal partial class BriefingEvidenceProvider(IContextRetriever contextRetriever, BriefingOptions options) : IBriefingEvidenceProvider
{
    private const string MatchAll = "*";

    public Task<ContextResult> GetEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, ContextResult? subjectEvidence,
        CancellationToken cancellationToken)
    {
        var urnOrTrn = parameters.SchoolUrnOrTrustTrn.Trim();

        return definition.Name switch
        {
            AgentNames.Establishment when parameters.IsTrust => SearchAcademiesOfTrustAsync(subjectEvidence, cancellationToken),
            AgentNames.Establishment => SearchAsync(EvidenceCategory.Establishment, MatchAll, $"establishment_urn eq {urnOrTrn}", cancellationToken),

            AgentNames.Trust when parameters.IsTrust => SearchAsync(EvidenceCategory.Trust, MatchAll,
                $"trust_reference_number eq {urnOrTrn.ToUpperInvariant()}", cancellationToken),
            AgentNames.Trust => SearchTrustOfSchoolAsync(subjectEvidence, cancellationToken),

            // Nothing in the concerns indexes is filterable, so these can only be found by their text.
            AgentNames.RiseConcerns => SearchAsync(EvidenceCategory.RiseConcerns, $"{parameters.SchoolOrTrustName} {urnOrTrn}", default, cancellationToken),
            AgentNames.RecastConcerns => SearchAsync(EvidenceCategory.RecastConcerns, $"{parameters.SchoolOrTrustName} {urnOrTrn}", default, cancellationToken),

            _ => throw new InvalidOperationException(string.Format(ErrorMessages.NoEvidenceQueryConfigured, definition.Name))
        };
    }
    /// <summary>
    /// Searches for the trust that a school belongs to, using the school's evidence to find the trust name.
    /// </summary>
    /// <param name="schoolEvidence">The evidence for the school.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The evidence for the trust.</returns>
    private Task<ContextResult> SearchTrustOfSchoolAsync(ContextResult? schoolEvidence, CancellationToken cancellationToken)
        => ReadField(schoolEvidence, AssociatedTrustNameRegex()) is { } trustName
            ? SearchAsync(EvidenceCategory.Trust, MatchAll, $"trust_name eq {trustName}", cancellationToken)
            : NoEvidence("This school is not part of a trust.");

    private Task<ContextResult> SearchAcademiesOfTrustAsync(ContextResult? trustEvidence, CancellationToken cancellationToken)
        => ReadField(trustEvidence, TrustNameRegex()) is { } trustName
            ? contextRetriever.GetContextAsync(EvidenceCategory.Establishment, MatchAll, options.MaxAcademiesPerTrust,
                $"associated_trust_name eq {trustName}", cancellationToken)
            : NoEvidence("No academies were found for this trust.");

    private Task<ContextResult> SearchAsync(string index, string query, ODataFilter filter, CancellationToken cancellationToken)
        => contextRetriever.GetContextAsync(index, query, options.MaxEvidencePerSearch, filter, cancellationToken);

    private static Task<ContextResult> NoEvidence(string message) => Task.FromResult(new ContextResult(message, HasEvidence: false));

    // Search evidence labels each value with its field, one per line, e.g. "trust_name: EXAMPLE TRUST".
    private static string? ReadField(ContextResult? evidence, Regex fieldLine)
    {
        if (evidence is not { HasEvidence: true })
            return null;

        var value = fieldLine.Match(evidence.Text).Groups[1].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    [GeneratedRegex(@"^associated_trust_name:[ \t]*(.*)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AssociatedTrustNameRegex();

    [GeneratedRegex(@"^trust_name:[ \t]*(.*)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrustNameRegex();
}
