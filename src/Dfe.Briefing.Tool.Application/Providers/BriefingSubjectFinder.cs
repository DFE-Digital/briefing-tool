using System.Globalization;
using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

/// <summary>
/// Finds the school or trust a briefing is about in its own index, the one the establishment or trust agent reads.
/// </summary>
/// <param name="contextRetriever">The context retriever.</param>
internal sealed partial class BriefingSubjectFinder(IContextRetriever contextRetriever) : IBriefingSubjectFinder
{
    private const string MatchAll = "*";
    private const string NotApplicable = "Not applicable";

    private const string UrnField = "establishment_urn";
    private const string TrnField = "trust_reference_number";
    private const int UrnLength = 6;
    private const int TrnLength = 7;

    /// <summary>How many matches a name search gives: enough to choose from as the person types.</summary>
    public const int MaxSearchResults = 10;

    // The fields each detail may be in, the first one present winning. GIAS calls the region the government office
    // region, and the date a record last changed its last changed date.
    private static readonly string[] RegionFields = ["region", "region_name", "government_office_region", "gor_name"];
    private static readonly string[] WebsiteFields = ["establishment_website", "trust_website", "website"];
    private static readonly string[] RecordDateFields = ["last_changed_date", "date_last_updated", "last_updated", "data_extract_date", "extract_date"];

    // The evidence writes dates as yyyy-mm-dd; GIAS's own exports use dd-mm-yyyy.
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];

    public async Task<BriefingSubject?> FindAsync(string urnOrTrn, bool isTrust, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urnOrTrn);

        var reference = urnOrTrn.Trim().ToUpperInvariant();
        var record = isTrust
            ? await contextRetriever.GetContextAsync(EvidenceCategory.Trust, MatchAll, 1, $"trust_reference_number eq {reference}", cancellationToken)
            : await contextRetriever.GetContextAsync(EvidenceCategory.Establishment, MatchAll, 1, $"establishment_urn eq {reference}", cancellationToken);

        return record.HasEvidence ? ToSubject(ReadFields(record.Text), isTrust, reference, LinkOf(record, 0)) : null;
    }

    public async Task<IReadOnlyList<BriefingSubject>> SearchAsync(string text, bool isTrust, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Only the chosen kind is searched: a trust's TRN, whole or its start, or a school's URN. Anything else is a name.
        var reference = WhitespaceRegex().Replace(text, string.Empty).ToUpperInvariant();
        if ((isTrust ? TrnRegex() : UrnRegex()).IsMatch(reference))
            return await FindAsync(reference, isTrust, cancellationToken) is { } subject ? [subject] : [];

        if ((isTrust ? PartialTrnRegex() : PartialUrnRegex()).IsMatch(reference))
            return await SearchByReferenceStartAsync(reference, isTrust, cancellationToken);

        var query = PrefixQuery(text);
        if (query is null)
            return [];

        var records = await contextRetriever.GetContextAsync(isTrust ? EvidenceCategory.Trust : EvidenceCategory.Establishment, query,
            MaxSearchResults, default, cancellationToken);
        return ReadRecords(records, isTrust);
    }

   /// <summary>
   /// Searches for trust reference numbers or establishment URNs that start with the specified value.
   /// </summary>
   /// <param name="start">Leading characters of the reference number to match.</param>
   /// <param name="isTrust">Indicates whether to search trust reference numbers; otherwise searches establishment URNs.</param>
   /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
   /// <returns>A task that represents the asynchronous operation and contains a read-only list of matching briefing subjects.</returns>
    private async Task<IReadOnlyList<BriefingSubject>> SearchByReferenceStartAsync(string start, bool isTrust, CancellationToken cancellationToken)
    {
        var length = isTrust ? TrnLength : UrnLength;
        var lowest = start.PadRight(length, '0');
        var highest = start.PadRight(length, '9');

        var records = isTrust
            ? await contextRetriever.GetContextAsync(EvidenceCategory.Trust, MatchAll, MaxSearchResults,
                $"trust_reference_number ge {lowest} and trust_reference_number le {highest}", cancellationToken)
            : await contextRetriever.GetContextAsync(EvidenceCategory.Establishment, MatchAll, MaxSearchResults,
                $"establishment_urn ge {lowest} and establishment_urn le {highest}", cancellationToken);
        return ReadRecords(records, isTrust);
    }

    private static IReadOnlyList<BriefingSubject> ReadRecords(ContextResult records, bool isTrust)
    {
        if (!records.HasEvidence)
            return [];

        var referenceField = isTrust ? TrnField : UrnField;
        return [.. RecordBlockRegex().Split(records.Text)
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select((block, index) => (Fields: ReadFields(block), Index: index))
            .Where(record => record.Fields.ContainsKey(referenceField))
            .Select(record => ToSubject(record.Fields, isTrust, record.Fields[referenceField].ToUpperInvariant(), LinkOf(records, record.Index)))
            .OfType<BriefingSubject>()];
    }

    private static BriefingSubject? ToSubject(Dictionary<string, string> fields, bool isTrust, string reference, string? link)
    {
        var nameField = isTrust ? "trust_name" : "establishment_name";
        if (!fields.TryGetValue(nameField, out var name))
            return null;

        return new BriefingSubject(reference, name, isTrust,
            Region: FirstOf(fields, RegionFields),
            Website: FirstOf(fields, WebsiteFields) ?? link,
            RecordDate: ReadDate(FirstOf(fields, RecordDateFields)));
    }

    // The search takes a record's first full web address, its website, out of its text as its link (see AISearch).
    private static string? LinkOf(ContextResult result, int index)
        => index < result.Sources.Count ? result.Sources[index].Link?.ToString() : null;

    // Each word the person typed, as the start of a word in the name: "aspir acad" is "aspir* acad*". Only letters and
    // digits are kept, so nothing typed is read as search syntax.
    private static string? PrefixQuery(string text)
    {
        var words = WordRegex().Matches(text).Select(word => word.Value + MatchAll).ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }

    private static string? FirstOf(Dictionary<string, string> fields, string[] names)
        => names.Select(field => fields.GetValueOrDefault(field)).FirstOrDefault(value => value is not null);

    // Only the date matters: a value may also have a time, e.g. "2026-10-15T06:00:00Z". Every format is 10 characters.
    private const int DateLength = 10;

    private static DateOnly? ReadDate(string? value)
        => value is { Length: >= DateLength } && DateOnly.TryParseExact(value[..DateLength], DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;

    // Search evidence labels each value with its field, one per line, e.g. "trust_name: EXAMPLE TRUST". Only the first
    // record's value counts, and a blank or "Not applicable" value isn't worth showing.
    private static Dictionary<string, string> ReadFields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = FieldLineRegex().Matches(text)
            .Select(line => (Field: line.Groups[1].Value, Value: line.Groups[2].Value.Trim()))
            .Where(line => line.Value.Length > 0 && !line.Value.Equals(NotApplicable, StringComparison.OrdinalIgnoreCase));
        foreach (var (field, value) in lines)
            fields.TryAdd(field, value);

        return fields;
    }

    [GeneratedRegex(@"^([a-z0-9_]+):[ \t]*(.*?)\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldLineRegex();

    // Where each record starts in the search's text, "--- trust_index Evidence 2 ---" (or "[Evidence 2]").
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence \d+ ---|\[Evidence \d+\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordBlockRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex UrnRegex();

    [GeneratedRegex(@"^TR\d{5}$")]
    private static partial Regex TrnRegex();

    // The start of a URN or TRN, at least 4 characters as the Find page asks for (e.g. "1234" or "TR00"): enough to
    // narrow it down, but not the whole.
    [GeneratedRegex(@"^\d{4,5}$")]
    private static partial Regex PartialUrnRegex();

    [GeneratedRegex(@"^TR\d{2,4}$")]
    private static partial Regex PartialTrnRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
