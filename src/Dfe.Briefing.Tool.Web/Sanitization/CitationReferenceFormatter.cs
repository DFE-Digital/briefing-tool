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

    /// <summary>
    /// Collects source citations from Markdown and appends a numbered References section.
    /// </summary>
    /// <param name="markdown">The Markdown content containing source citations and web addresses.</param>
    /// <param name="referenceIdPrefix">The prefix used for generated reference element identifiers.</param>
    /// <returns>The Markdown content with citations replaced by reference links and a References section appended.</returns>
    public static string CollectReferences(string markdown, string referenceIdPrefix = DefaultReferenceIdPrefix)
    {
        var references = new List<string>();
        var withMarkers = NumberSources(JoinNamesToAddresses(markdown), references);

        if (references.Count == 0)
            return WebAddresses.WithSchemes(markdown);

        var items = references.Select((reference, index) => $"""<li id="{referenceIdPrefix}-{index + 1}">{reference}</li>""");
        return $"{WebAddresses.WithSchemes(GroupCitations(withMarkers, referenceIdPrefix))}\n\n## References\n\n<ol>\n{string.Join('\n', items)}\n</ol>\n";
    }

    /// <summary>
    /// Replaces each cited source with a marker holding its reference number, adding each distinct source to
    /// <paramref name="references"/> the first time it's cited.
    /// </summary>
    private static string NumberSources(string markdown, List<string> references)
    {
        var numberByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        return SourceRegex().Replace(markdown, match =>
        {
            var (name, url, isSearchCitation) = ReadSource(match);
            var key = isSearchCitation ? name : url ?? name;

            if (!numberByKey.TryGetValue(key, out var number))
            {
                references.Add(FormatReference(name, url, isSearchCitation));
                number = references.Count;
                numberByKey[key] = number;
            }

            return $"{MarkerStart}{number}{MarkerEnd}";
        });
    }

    // Placeholders for a citation's reference number, so citations side by side can be found and grouped.
    private const char MarkerStart = '';
    private const char MarkerEnd = '';

    /// <summary>
    /// Groups citation markers and replaces them with linked references.
    /// </summary>
    /// <param name="text">The text containing citation markers.</param>
    /// <param name="referenceIdPrefix">The prefix used to construct citation reference IDs.</param>
    /// <returns>The text with grouped citations replaced by linked references.</returns>
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

    /// <summary>
    /// Converts names followed by web addresses into Markdown links.
    /// </summary>
    /// <param name="markdown">The Markdown text to process.</param>
    /// <returns>The processed Markdown text.</returns>
    private static string JoinNamesToAddresses(string markdown)
        => NameThenAddressRegex().Replace(UnnestLinks(markdown), match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var address = match.Groups["address"].Value.Trim();

            return WebAddresses.IsAddress(address) && !WebAddresses.IsAddress(name) ? $"[{name}]({address})" : match.Value;
        });

    [GeneratedRegex(@"\[(?<name>[^\[\]]+)\](?:\s*\[(?<address>[^\[\]]+)\]|\s+\((?<address>[^()]+)\)|:\s*(?<address>\S+))")]
    private static partial Regex NameThenAddressRegex();

    /// <summary>
    /// Converts nested Markdown links to standard link syntax.
    /// </summary>
    /// <param name="markdown">The Markdown text to process.</param>
    /// <returns>The Markdown text with nested links converted to standard link syntax.</returns>
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

    /// <summary>
    /// Gets a display name from a URL.
    /// </summary>
    /// <param name="url">The URL to derive the page name from.</param>
    /// <returns>The HTML-encoded page name.</returns>
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

    /// <summary>
    /// Creates a description for a recognized trust or school record.
    /// </summary>
    /// <param name="text">The text to inspect for a trust or school record.</param>
    /// <returns>A description of the recognized record, or null if no record is recognized.</returns>
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
