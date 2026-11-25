using Microsoft.AspNetCore.Mvc;

namespace Dfe.Briefing.Tool.Web.Extensions;

public static class BackLinkExtension
{
    /// <summary>
    /// Gets a link to the previous page when the referrer is on the current host and has a different path; otherwise,
    /// uses the fallback or the home page.
    /// </summary>
    /// <param name="url">The URL helper for the current request.</param>
    /// <param name="fallback">The URL to use when the referrer is missing, is on another host, or has the current path.</param>
    /// <returns>The previous page's path and query, or the fallback URL or home page.</returns>
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
