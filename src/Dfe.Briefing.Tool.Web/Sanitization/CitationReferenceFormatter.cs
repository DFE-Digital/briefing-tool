using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Two kinds of source appear inline and repeat: AiAgents.AISearch renders each evidence citation
/// wrapped in square brackets (plain text or a real link), and agents like the web search agent write
/// their own Markdown links - either can cite the same source more than once in one document. This
/// collects each distinct source (a citation by its text, a link by its URL) into a numbered
/// "References" list once, replacing every inline occurrence with a small link to its entry, so the same
/// source is never spelled out twice.
/// </summary>
public static partial class CitationReferenceFormatter
{
    public static string CollectReferences(string markdown)
    {
        var references = new List<string>();
        var numberByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        var withMarkers = SourceRegex().Replace(markdown, match =>
        {
            var text = match.Groups["text"].Value;
            var url = match.Groups["url"].Success ? match.Groups["url"].Value : null;
            var key = url ?? text;

            if (!numberByKey.TryGetValue(key, out var number))
            {
                references.Add(url is null ? text : $"""<a href="{url}">{text}</a>""");
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

    [GeneratedRegex(@"\[(?<text>[^\[\]]+)\](?:\((?<url>[^()]+)\))?")]
    private static partial Regex SourceRegex();
}
