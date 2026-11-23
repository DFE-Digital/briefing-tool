using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Filters;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

/// <summary>
/// Finds the records a briefing is written from, by searching each index with the exact values from the school's or
/// trust's own record wherever it can.
/// </summary>
/// <param name="contextRetriever">The context retriever.</param>
/// <param name="options">The briefing options.</param>
internal partial class BriefingEvidenceProvider(IContextRetriever contextRetriever, BriefingOptions options) : IBriefingEvidenceProvider
{
    private const string MatchAll = "*";
    private const string SchoolRecordName = "the school's record";
    private const string TrustRecordName = "the trust's record";
    private const string SchoolsTrustRecordName = "its trust's record";
    private const string AcademyRecordsName = "its academies' records";
    private static readonly ContextResult NoConcerns = new(string.Empty, HasEvidence: false);

    public async Task<BriefingRecords?> GetRecordsAsync(BriefingParameters parameters, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var reference = parameters.SchoolUrnOrTrustTrn.Trim().ToUpperInvariant();
        var subjectRecord = parameters.IsTrust ? TrustRecordName : SchoolRecordName;
        progress?.Report($"Collecting {subjectRecord}...");
        var subject = parameters.IsTrust
            ? await SearchAsync(EvidenceCategory.Trust, MatchAll, $"trust_reference_number eq {reference}", cancellationToken)
            : await SearchAsync(EvidenceCategory.Establishment, MatchAll, $"establishment_urn eq {reference}", cancellationToken);
        if (!subject.HasEvidence)
            return null;
        progress?.Report(Collected(subjectRecord, subject));

        var relatedRecords = parameters.IsTrust ? AcademyRecordsName : SchoolsTrustRecordName;
        progress?.Report($"Collecting {relatedRecords}...");
        var related = parameters.IsTrust
            ? await ReportedAsync(relatedRecords, "No academy records found", SearchAcademiesOfTrustAsync(subject, cancellationToken), progress)
            : await ReportedAsync(relatedRecords, "No trust record found", SearchTrustOfSchoolAsync(subject, cancellationToken), progress);

        // Rise and Recast concerns aren't searched: no briefing reports them (see BriefingBuilder.BuildBriefingEvidence).
        return new BriefingRecords(subject, related, NoConcerns, NoConcerns);
    }

    private static async Task<ContextResult> ReportedAsync(string name, string noneFound, Task<ContextResult> search, IProgress<string>? progress)
    {
        var result = await search;
        progress?.Report(result.HasEvidence ? Collected(name, result) : noneFound);
        return result;
    }

    // With the number of records, when the search says where each came from.
    private static string Collected(string name, ContextResult result) => result.Sources.Count switch
    {
        0 => $"Collected {name}",
        1 => $"Collected {name} (1 record)",
        var count => $"Collected {name} ({count} records)"
    };

    /// <summary>
    /// Searches for the trust that a school belongs to, using the school's evidence to find the trust name.
    /// </summary>
    /// <param name="schoolEvidence">The evidence for the school.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The evidence for the trust.</returns>
    private Task<ContextResult> SearchTrustOfSchoolAsync(ContextResult schoolEvidence, CancellationToken cancellationToken)
        => ReadField(schoolEvidence, AssociatedTrustNameRegex()) is { } trustName
            ? SearchAsync(EvidenceCategory.Trust, MatchAll, $"trust_name eq {trustName}", cancellationToken)
            : NoEvidence("This school is not part of a trust.");

    private Task<ContextResult> SearchAcademiesOfTrustAsync(ContextResult trustEvidence, CancellationToken cancellationToken)
        => ReadField(trustEvidence, TrustNameRegex()) is { } trustName
            ? contextRetriever.GetContextAsync(EvidenceCategory.Establishment, MatchAll, options.MaxAcademiesPerTrust,
                $"associated_trust_name eq {trustName}", cancellationToken)
            : NoEvidence("No academies were found for this trust.");

    private Task<ContextResult> SearchAsync(string index, string query, ODataFilter filter, CancellationToken cancellationToken)
        => contextRetriever.GetContextAsync(index, query, options.MaxEvidencePerSearch, filter, cancellationToken);

    private static Task<ContextResult> NoEvidence(string message) => Task.FromResult(new ContextResult(message, HasEvidence: false));

    /// <summary>
    /// Reads a field value from evidence text using the first capture group in a regular expression.
    /// </summary>
    /// <param name="evidence">The evidence to read from.</param>
    /// <param name="fieldLine">The regular expression used to extract the field value.</param>
    /// <returns>The trimmed value captured by the first group in <paramref name="fieldLine"/>, or <see langword="null"/> when
    /// evidence is missing, has no evidence, or the captured value is empty.</returns>
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
