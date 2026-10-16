using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoLinks;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Agents return Markdown built from their own evidence (including content pulled from external Azure
/// Search documents and, for the web search agent, arbitrary web pages), so it's untrusted for rendering
/// purposes even though it isn't user-submitted - a compromised or adversarial source document could
/// otherwise inject a script into a civil servant's browser. This collects repeated citations into a
/// References list (see CitationReferenceFormatter), renders the result to HTML, then restricts it to the
/// tags the agents' formatting requirements actually use (headings, paragraphs, lists, tables, citation
/// links) before it's ever rendered with Html.Raw.
/// </summary>
public static class BriefingHtmlSanitizer
{
    // Pipe tables: agents use tables to present comparable facts (e.g. Ofsted judgements across
    // inspections), which plain CommonMark can't parse. Auto-links: agents write web addresses as plain text
    // (a Markdown link would become a numbered reference), so bare https:// and www. addresses become links here.
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true })
        .Build();
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? Sanitize(string? markdown)
        => markdown is null ? null : Sanitizer.Sanitize(Markdown.ToHtml(WebAddresses.WithSchemes(CitationReferenceFormatter.CollectReferences(markdown)), MarkdownPipeline));

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "h1", "h2", "h3", "h4", "p", "ul", "ol", "li", "a", "br", "table", "thead", "tbody", "tr", "th", "td" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("id"); // CitationReferenceFormatter's reference list anchors

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        return sanitizer;
    }
}
