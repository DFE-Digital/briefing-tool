using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Two kinds of source appear inline and repeat: AiAgents.AISearch renders each evidence citation
/// wrapped in square brackets (plain text or a real link), and agents like the web search agent write
/// their own Markdown links - either can cite the same source more than once in one document. This
/// collects each distinct source (a citation by its text, a link by its URL) into a numbered
/// "References" list once, replacing every inline occurrence with a small link to its entry, so the same
/// source is never spelled out twice. A reference with a web address shows the full address, so a reader
/// can see exactly which page the information came from before following it.
/// </summary>
public static partial class CitationReferenceFormatter
{
    public static string CollectReferences(string markdown)
    {
        var references = new List<string>();
        var numberByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        var withMarkers = SourceRegex().Replace(JoinNamesToAddresses(markdown), match =>
        {
            var (name, url) = ReadSource(match);
            var key = url ?? name;

            if (!numberByKey.TryGetValue(key, out var number))
            {
                references.Add(FormatReference(name, url));
                number = references.Count;
                numberByKey[key] = number;
            }

            return $"""<a href="#reference-{number}">{number}</a>""";
        });

        if (references.Count == 0)
            return markdown;

        var items = references.Select((reference, index) => $"""<li id="reference-{index + 1}">{reference}</li>""");
        return $"{withMarkers}\n\n## References\n\n<ol>\n{string.Join('\n', items)}\n</ol>\n";
    }

    // Agents sometimes cite one source as a name followed by its address - "[Chorus Trust] [chorustrust.org/x]",
    // "[Chorus Trust][...]", "[Chorus Trust] (...)" or "[Chorus Trust]: ..." - which would otherwise become two
    // references: the name with no link, and the bare address. This joins them into one link, [name](address).
    // Two citations side by side, e.g. "[Evidence 1] [Evidence 2]", are left alone, as the second isn't an address.
    private static string JoinNamesToAddresses(string markdown)
        => NameThenAddressRegex().Replace(markdown, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            var address = match.Groups["address"].Value.Trim();

            return WebAddresses.IsAddress(address) && !WebAddresses.IsAddress(name) ? $"[{name}]({address})" : match.Value;
        });

    [GeneratedRegex(@"\[(?<name>[^\[\]]+)\](?:\s*\[(?<address>[^\[\]]+)\]|\s+\((?<address>[^()]+)\)|:\s*(?<address>\S+))")]
    private static partial Regex NameThenAddressRegex();

    // A Markdown link, [name](url); a citation the library rendered as a link, [<a href="url">name</a>];
    // or a citation with no web address, [name].
    private static (string Name, string? Url) ReadSource(Match match)
    {
        var text = match.Groups["text"].Value.Trim();

        if (match.Groups["url"].Success)
            return (text, match.Groups["url"].Value.Trim());

        var anchor = AnchorRegex().Match(text);
        return anchor.Success ? (anchor.Groups["name"].Value.Trim(), anchor.Groups["url"].Value.Trim()) : (text, null);
    }

    private static string FormatReference(string name, string? url)
    {
        // A source cited only by its address: the reference list is raw HTML, which Markdown never auto-links.
        if (url is null && WebAddresses.IsAddress(name))
            url = name;

        if (url is null)
            return name;

        // An address without a scheme would be treated as a page within this app, not the source's own site.
        url = WebAddresses.WithScheme(url);

        var link = $"""<a href="{url}">{url}</a>""";
        return name.Length == 0 || WebAddresses.IsAddress(name) ? link : $"{name}: {link}";
    }

    [GeneratedRegex(@"\[(?<text>[^\[\]]+)\](?:\((?<url>[^()]+)\))?")]
    private static partial Regex SourceRegex();

    [GeneratedRegex("""^<a\s+href="(?<url>[^"]+)"[^>]*>(?<name>.*?)</a>$""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();
}
