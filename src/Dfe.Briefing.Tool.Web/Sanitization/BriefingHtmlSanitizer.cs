using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoLinks;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Converts Markdown into sanitized HTML for briefing content.
/// </summary>
/// <remarks>Supports pipe tables and automatic links for web addresses. The output permits a restricted set of
/// HTML elements and attributes, and only HTTP and HTTPS links.</remarks>
public static class BriefingHtmlSanitizer
{
    /// <summary>
    /// The Markdown pipeline used to parse pipe tables and automatically detect links, using HTTPS for www links.
    /// </summary>
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true })
        .Build();
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    /// <summary>
    /// Converts Markdown to sanitized HTML.
    /// </summary>
    /// <param name="markdown">The Markdown content to convert.</param>
    /// <param name="referenceIdPrefix">The prefix used for citation reference IDs.</param>
    /// <returns>The sanitized HTML, or <see langword="null"/> if <paramref name="markdown"/> is <see langword="null"/>.</returns>
    public static string? Sanitize(string? markdown, string referenceIdPrefix = CitationReferenceFormatter.DefaultReferenceIdPrefix)
        => markdown is null
            ? null
            : Sanitizer.Sanitize(Markdown.ToHtml(CitationReferenceFormatter.CollectReferences(markdown, referenceIdPrefix), MarkdownPipeline));

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
