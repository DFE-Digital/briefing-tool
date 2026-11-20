using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Finds, in code, the town a later web search is focused on, from the school's or trust's own records: the town in its
/// address, else its local authority (for a trust, the one its academies all share).
/// </summary>
/// <remarks>
/// The briefing agent used to give the town in a marker on its answer's last line; working it out here means the agent
/// isn't asked to, and the town can't come from another school's record. When neither is found it's null, and the web
/// search uses its default location (London) and is told the town isn't known.
/// </remarks>
internal static partial class TownFinder
{
    private const string UrnField = "establishment_urn";
    private const string TrnField = "trust_reference_number";
    private const string SchoolAddressField = "establishment_address";
    private const string TrustAddressField = "trust_address";
    private const string LocalAuthorityField = "local_authority_name";
    private const char AddressSeparator = ',';

    /// <summary>The town for <paramref name="parameters"/>'s subject, or null when its records give neither a town nor a local authority.</summary>
    public static string? Find(BriefingParameters parameters, BriefingRecords records)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(records);

        var own = OwnRecords(records.Subject, parameters.IsTrust ? TrnField : UrnField, parameters.SchoolUrnOrTrustTrn);
        var addressField = parameters.IsTrust ? TrustAddressField : SchoolAddressField;

        return own.Select(fields => TownIn(fields.GetValueOrDefault(addressField))).FirstOrDefault(static town => town is not null)
            ?? (parameters.IsTrust ? SharedLocalAuthority(records.Related) : FirstLocalAuthority(own));
    }

    /// <summary>
    /// The fields of the subject's own records: those giving its URN or TRN, or every record when none gives one (a
    /// set can hold another school's record, whose address must never decide this one's town).
    /// </summary>
    private static List<Dictionary<string, string>> OwnRecords(ContextResult subject, string keyField, string reference)
    {
        var all = FieldsOfEach(subject);
        return all.Exists(fields => fields.ContainsKey(keyField))
            ? all.FindAll(fields => string.Equals(fields.GetValueOrDefault(keyField)?.Trim(), reference.Trim(), StringComparison.OrdinalIgnoreCase))
            : all;
    }

    private static List<Dictionary<string, string>> FieldsOfEach(ContextResult set)
    {
        if (!set.HasEvidence || string.IsNullOrWhiteSpace(set.Text))
            return [];

        var (_, records) = EvidenceRecords.Split(set.Text);
        return records.Count == 0
            ? [EvidenceRecords.Fields(set.Text)]
            : records.Select(static record => EvidenceRecords.Fields(record.Text)).ToList();
    }

    /// <summary>
    /// The town in an address written "street, town, postcode": the last part before the postcode. Null when the address
    /// has no street before it, or that part holds a number, so it isn't a town.
    /// </summary>
    private static string? TownIn(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var parts = address.Split(AddressSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && PostcodeRegex().IsMatch(parts[^1]))
            parts.RemoveAt(parts.Count - 1);

        return parts.Count >= 2 && !parts[^1].Any(char.IsDigit) ? parts[^1] : null;
    }

    private static string? FirstLocalAuthority(List<Dictionary<string, string>> records)
        => records.Select(static fields => fields.GetValueOrDefault(LocalAuthorityField)).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    /// <summary>The local authority every academy in the trust's records gives, or null when they give none or several.</summary>
    private static string? SharedLocalAuthority(ContextResult academies)
    {
        var authorities = FieldsOfEach(academies)
            .Select(static fields => fields.GetValueOrDefault(LocalAuthorityField)?.Trim())
            .OfType<string>()
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return authorities.Count == 1 ? authorities[0] : null;
    }

    // A UK postcode, e.g. "EX1 1AA" or "SW1A 2AA".
    [GeneratedRegex(@"^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PostcodeRegex();
}
