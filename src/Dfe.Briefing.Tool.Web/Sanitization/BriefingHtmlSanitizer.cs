using Ganss.Xss;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// The synthesis agent returns HTML built from its own evidence (including content pulled from
/// external Azure Search documents), so it's untrusted for rendering purposes even though it isn't
/// user-submitted - a compromised or adversarial source document could otherwise inject a script
/// into a civil servant's browser. This restricts output to the tags the agents' formatting
/// requirements actually use (headings, paragraphs, lists, citation links) before it's ever
/// rendered with Html.Raw.
/// </summary>
public static class BriefingHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? Sanitize(string? html) => html is null ? null : Sanitizer.Sanitize(html);

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "h1", "h2", "h3", "h4", "p", "ul", "ol", "li", "a", "br" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        return sanitizer;
    }
}
