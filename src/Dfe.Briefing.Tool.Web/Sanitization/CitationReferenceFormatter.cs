using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Two kinds of source appear inline and repeat: AiAgents.AISearch renders each evidence citation
/// wrapped in square brackets (plain text or a real link), and agents like the web search agent write
/// their own Markdown links - either can cite the same source more than once in one document. This
/// collects each distinct source (a citation by its text, a link by its URL) into a numbered
/// "References" list once, replacing every inline occurrence with a small link to its entry, so the same
/// source is never spelled out twice. A reference with a web address is one link named after its source,
/// e.g. "The Star", or the site's name, e.g. "thestar.co.uk", when the source gives no better name.
/// </summary>
public static partial class CitationReferenceFormatter
{
    /// <summary>The id prefix of the briefing's reference anchors, e.g. "reference-1".</summary>
    public const string DefaultReferenceIdPrefix = "reference";

    /// <param name="markdown">The agent's Markdown.</param>
    /// <param name="referenceIdPrefix">
    /// The prefix of the reference anchors' ids. Two documents on one page, such as the briefing and the web search
    /// findings under it, each need their own, so a citation never jumps to the other's reference with the same number.
    /// </param>
    public static string CollectReferences(string markdown, string referenceIdPrefix = DefaultReferenceIdPrefix)
    {
        var references = new List<string>();
        var numberByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        var withMarkers = SourceRegex().Replace(JoinNamesToAddresses(markdown), match =>
        {
            var (name, url, isSearchCitation) = ReadSource(match);
            // A search citation is one record, whatever website the library attached; a link is one page.
            var key = isSearchCitation ? name : url ?? name;

            if (!numberByKey.TryGetValue(key, out var number))
            {
                references.Add(FormatReference(name, url, isSearchCitation));
                number = references.Count;
                numberByKey[key] = number;
            }

            return $"{MarkerStart}{number}{MarkerEnd}";
        });

        // Web addresses in the briefing's own text become full links; the References list is built separately, so a
        // source's name (e.g. "Education.gov.uk news") is never mistaken for an address.
        if (references.Count == 0)
            return WebAddresses.WithSchemes(markdown);

        var items = references.Select((reference, index) => $"""<li id="{referenceIdPrefix}-{index + 1}">{reference}</li>""");
        return $"{WebAddresses.WithSchemes(GroupCitations(withMarkers, referenceIdPrefix))}\n\n## References\n\n<ol>\n{string.Join('\n', items)}\n</ol>\n";
    }

    // Placeholders for a citation's reference number, so citations side by side can be found and grouped.
    private const char MarkerStart = '';
    private const char MarkerEnd = '';

    // Citations written next to each other - "[A] [B]", "[A], [B]" or "[A] ([B])" - become one bracketed group,
    // "(11, 12)", rather than "11 12" or "11 (12)"; a single citation becomes "(11)", and a repeat within a group is
    // shown once.
    private static string GroupCitations(string text, string referenceIdPrefix)
        => CitationGroupRegex().Replace(text, group =>
        {
            var numbers = MarkerRegex().Matches(group.Value).Select(marker => int.Parse(marker.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct();
            return "(" + string.Join(", ", numbers.Select(n => $"""<a href="#{referenceIdPrefix}-{n}">{n}</a>""")) + ")";
        });

    // One citation, or a bracketed run of them, then any more separated by spaces, commas or semicolons.
    [GeneratedRegex(@"(?:\(\s*\d+(?:[\s,;]+\d+)*\s*\)|\d+)(?:[ \t,;]*(?:\(\s*\d+(?:[\s,;]+\d+)*\s*\)|\d+))*")]
    private static partial Regex CitationGroupRegex();

    [GeneratedRegex(@"(?<n>\d+)")]
    private static partial Regex MarkerRegex();

    // Agents sometimes cite one source as a name followed by its address - "[Chorus Trust] [chorustrust.org/x]",
    // "[Chorus Trust][...]", "[Chorus Trust] (...)" or "[Chorus Trust]: ..." - which would otherwise become two
    // references: the name with no link, and the bare address. This joins them into one link, [name](address).
    // Two citations side by side, e.g. "[Evidence 1] [Evidence 2]", are left alone, as the second isn't an address.
    private static string JoinNamesToAddresses(string markdown)
        => NameThenAddressRegex().Replace(UnnestLinks(markdown), match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var address = match.Groups["address"].Value.Trim();

            return WebAddresses.IsAddress(address) && !WebAddresses.IsAddress(name) ? $"[{name}]({address})" : match.Value;
        });

    [GeneratedRegex(@"\[(?<name>[^\[\]]+)\](?:\s*\[(?<address>[^\[\]]+)\]|\s+\((?<address>[^()]+)\)|:\s*(?<address>\S+))")]
    private static partial Regex NameThenAddressRegex();

    // The web search agent sometimes wraps its link inside another, "[The Star]([thestar.co.uk](https://...))", and
    // occasionally mistypes the bracket, "[The Star)(...)" - either would show as two reference numbers and stray
    // brackets. This collapses them into the one link they mean, "[The Star](https://...)".
    private static string UnnestLinks(string markdown) => NestedLinkRegex().Replace(markdown, "[${name}](${url})");

    [GeneratedRegex(@"\[(?<name>[^\[\]()]+)[\])]\(\[[^\[\]]+\]\((?<url>[^()\s]+)\)\)")]
    private static partial Regex NestedLinkRegex();

    // A Markdown link, [name](url); a citation the library rendered as a link, [<a href="url">name</a>];
    // or a citation with no web address, [name].
    private static (string Name, string? Url, bool IsSearchCitation) ReadSource(Match match)
    {
        var text = match.Groups["text"].Value.Trim();

        if (match.Groups["url"].Success)
            return (text, match.Groups["url"].Value.Trim(), false);

        var anchor = AnchorRegex().Match(text);
        return anchor.Success ? (anchor.Groups["name"].Value.Trim(), anchor.Groups["url"].Value.Trim(), true) : (text, null, true);
    }

    private static string FormatReference(string name, string? url, bool isSearchCitation)
    {
        // A source cited only by its address: the reference list is raw HTML, which Markdown never auto-links.
        if (url is null && WebAddresses.IsAddress(name))
            url = name;

        // A school or trust record combines several sources, so it has no single page to link to - the library cites
        // it by the start of its text instead. Label it as the record it is; any link the library attached to it is
        // the school's or trust's own website, so the label links there.
        if (isSearchCitation && DescribeRecord(name) is { } record)
            return url is null ? record : Link(url, record);

        if (url is null)
            return name;

        // A name that says nothing about the source (an address, or "source"/"here") is replaced by the site's name.
        var text = name.Length == 0 || WebAddresses.IsAddress(name) || GenericLinkTextRegex().IsMatch(name) ? SiteName(url) : name;
        return Link(url, text);
    }

    // Each reference is one link, its text naming the source; the address is in the link, not shown beside it. An
    // address without a scheme would be treated as a page within this app, not the source's own site.
    private static string Link(string url, string text)
        => $"""<a href="{WebAddresses.WithScheme(url)}">{text}</a>""";

    // "https://www.thestar.co.uk/news/a-1" is "thestar.co.uk".
    private static string SiteName(string url)
        => Uri.TryCreate(WebAddresses.WithScheme(url), UriKind.Absolute, out var address) && address.Host.Length > 0
            ? (address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host)
            : url;

    // A record's citation text is its first field values, e.g. "Eckington School 149825 Academy converter…" or
    // "CHORUS EDUCATION TRUST TR01920 Multi-academy trust…" (see ContentFields in appsettings.json: the name comes
    // first, then the identifier). Older ordering, "149825 Eckington School…", is read too, without the name.
    private static string? DescribeRecord(string text)
    {
        var trust = TrustRecordRegex().Match(text);
        if (trust.Success)
            return $"{trust.Groups["name"].Value.Trim()} - trust record (TRN {trust.Groups["trn"].Value})";

        var school = SchoolRecordRegex().Match(text);
        if (school.Success)
            return $"{school.Groups["name"].Value.Trim()} - school record (URN {school.Groups["urn"].Value})";

        var urnFirst = UrnFirstRecordRegex().Match(text);
        return urnFirst.Success ? $"School record (URN {urnFirst.Groups["urn"].Value})" : null;
    }

    [GeneratedRegex(@"^(?<name>[^\d\[\]<>]{2,}?)\s+(?<trn>TR\d{5})\b")]
    private static partial Regex TrustRecordRegex();

    [GeneratedRegex(@"^(?<name>[^\[\]<>]{2,}?)\s+(?<urn>\d{6,7})\b")]
    private static partial Regex SchoolRecordRegex();

    [GeneratedRegex(@"^(?<urn>\d{6,7})\s+\S")]
    private static partial Regex UrnFirstRecordRegex();

    [GeneratedRegex(@"\[(?<text>[^\[\]]+)\](?:\((?<url>[^()]+)\))?")]
    private static partial Regex SourceRegex();

    // The library renders a citation with a web address as an HTML link. If a model copies such a rendered
    // citation, the library escapes its "<" (it treats any HTML a model writes as text), so the link arrives as
    // "&lt;a href="...">name&lt;/a>" - both forms are read here, so neither reaches the reader as raw tag text.
    [GeneratedRegex("""^(?:<|&lt;)a\s+href="(?<url>[^"]+)"[^>]*?(?:>|&gt;)(?<name>.*?)(?:<|&lt;)/a(?:>|&gt;)$""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    // Link text that says nothing about the source, so only the address is worth showing.
    [GeneratedRegex(@"^(?:link|source|here|website|read more|click here|more)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericLinkTextRegex();
}
