using System.Net;
using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Collects distinct sources cited in a Markdown document into a numbered "References" list, replacing each inline occurrence with a link to its entry.
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

        // The link is named after the page itself as well as its source: agents often give every page on one site the
        // same name, such as "The Star", so that name alone can't tell the references apart. The whole text is the
        // link. A name that says nothing about the source (an address, or "source"/"here") is left out.
        var page = PageName(url);
        return name.Length == 0 || WebAddresses.IsAddress(name) || GenericLinkTextRegex().IsMatch(name)
               || string.Equals(name, page, StringComparison.OrdinalIgnoreCase)
            ? Link(url, page)
            : Link(url, $"{name} - {page}");
    }

    // An address without a scheme would be treated as a page within this app, not the source's own site.
    private static string Link(string url, string text)
        => $"""<a href="{WebAddresses.WithScheme(url)}">{text}</a>""";

    // The page's own name, from the last part of its address - news sites name each article there:
    // "https://www.gazette-news.co.uk/news/24681357.stanway-school-colchester-welcome-new-headteacher/?utm_source=openai" is
    // "stanway-school-colchester-welcome-new-headteacher". A last part that names nothing, a number ("/news/12345") or
    // "index.html", takes the part before it too, and a site's home page is its site name. Tracking parameters
    // ("utm_...") are left out of the text, though not the link; any other query stays, as it can pick out the page.
    private static string PageName(string url)
    {
        if (!Uri.TryCreate(WebAddresses.WithScheme(url), UriKind.Absolute, out var address) || address.Host.Length == 0)
            return WebUtility.HtmlEncode(url.Trim());

        var query = string.Join('&', address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !parameter.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)));
        var segments = Uri.UnescapeDataString(address.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);

        string name;
        if (segments.Length == 0)
        {
            name = address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host;
        }
        else
        {
            var last = ArticleNumberRegex().Replace(PageExtensionRegex().Replace(segments[^1], string.Empty), string.Empty);
            var isIndex = IndexPageRegex().IsMatch(last);
            if (segments.Length > 1 && (isIndex || last.Length < 4 || last.All(char.IsDigit)))
                name = isIndex ? segments[^2] : $"{segments[^2]}/{last}";
            else
                name = last;
        }

        return WebUtility.HtmlEncode(query.Length > 0 ? $"{name}?{query}" : name);
    }

    // Some news sites put the article's number before its name: "24681357.stanway-school-...". Only removed when a
    // name with letters follows it.
    [GeneratedRegex(@"^\d+[.\-_](?=.*[a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleNumberRegex();

    [GeneratedRegex(@"\.(?:html?|aspx?|php|jsp)$", RegexOptions.IgnoreCase)]
    private static partial Regex PageExtensionRegex();

    [GeneratedRegex(@"^(?:index|default|home)$", RegexOptions.IgnoreCase)]
    private static partial Regex IndexPageRegex();

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

    [GeneratedRegex(@"^(?<name>[^\d\[\]<>]{2,}?)\s+(?<trn>(?:TR|UT)\d{5})\b")]
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

    // Link text that says nothing about the source, so only the page's name is worth showing.
    [GeneratedRegex(@"^(?:link|source|here|website|read more|click here|more)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericLinkTextRegex();
}
