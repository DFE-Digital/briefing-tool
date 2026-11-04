using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Middleware;

/// <summary>
/// Gives every request one correlation ID, so all its logs - the pages', the error page's and the agents' - can be found
/// together. A caller's own X-Correlation-ID is kept when it's safe to log; otherwise the request's trace ID is used. The
/// ID is sent back in the response's X-Correlation-ID header, and is the request's TraceIdentifier.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogProperty = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString() is var incoming && SafeIdRegex().IsMatch(incoming)
            ? incoming
            : Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag(LogProperty, correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // A template, not a dictionary: the console prints it as "CorrelationId:…", Application Insights as a property.
        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
            await next(context);
    }

    // Anything else - too long, or with spaces or line breaks - could forge or flood log lines, so a new ID is made.
    [GeneratedRegex(@"\A[A-Za-z0-9._-]{1,64}\z", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeIdRegex();
}
