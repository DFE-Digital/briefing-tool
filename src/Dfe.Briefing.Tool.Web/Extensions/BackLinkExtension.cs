using Microsoft.AspNetCore.Mvc;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class BackLinkExtension
{
    /// <summary>
    /// Where a Back link goes: the page the person came from, as the browser's back button would, so a page linked to
    /// from several places (the AI summaries page, say) goes back to whichever it was. It's the page's own
    /// <paramref name="fallback"/> when the browser doesn't say, when it was another site, or when it was this same
    /// page - after a form on it shows an error, going back would only show it again before it was sent.
    /// </summary>
    /// <param name="url">The page's URL helper.</param>
    /// <param name="fallback">The page before this one in the journey.</param>
    /// <returns>A path within this site, never another site's address.</returns>
    public static string BackLink(this IUrlHelper url, string? fallback)
    {
        var request = url.ActionContext.HttpContext.Request;
        var home = url.Content("~/");

        if (!Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var previous)
            || !string.Equals(previous.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(previous.AbsolutePath, request.Path.Value, StringComparison.OrdinalIgnoreCase))
        {
            return fallback ?? home;
        }

        // Only the path and query: whatever the browser sends, the link stays on this site.
        return previous.PathAndQuery;
    }
}
