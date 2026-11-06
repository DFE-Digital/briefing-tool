using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Gives each website in the records a full address, starting "https://", before the agent sees it.
/// </summary>
/// <remarks>
/// Records often hold a bare address, such as "www.example.org". The briefing shows the full address, so screen readers
/// read it as a link and the page can link it. Told only in its prompt to add "https://", the agent sometimes copied
/// the bare address; adding it here makes the rule hold every time. An address that already has http:// or https://, and
/// a value that isn't an address (such as "Not recorded"), are left as they are.
/// </remarks>
internal static partial class WebsiteAddress
{
    public const string Scheme = "https://";

    /// <summary><paramref name="records"/>, with <see cref="Scheme"/> added to each website field that has none.</summary>
    public static ContextResult WithScheme(ContextResult records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (!records.HasEvidence)
            return records;

        var text = BareWebsiteRegex().Replace(records.Text, match => match.Groups["field"].Value + Scheme + match.Groups["address"].Value);
        return text == records.Text ? records : records with { Text = text };
    }

    // A website field line ("establishment_website: www.example.org") whose value is a bare domain, with an optional path.
    [GeneratedRegex(@"^(?<field>(?:establishment_|trust_)?website:[ \t]*)(?<address>(?!https?://)[a-z0-9-]+(?:\.[a-z0-9-]+)+(?:/\S*)?)(?=[ \t]*\r?$)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BareWebsiteRegex();
}
