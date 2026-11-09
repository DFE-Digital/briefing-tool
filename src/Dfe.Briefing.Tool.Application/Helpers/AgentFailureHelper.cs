using System.ClientModel;

namespace Dfe.Briefing.Tool.Application.Helpers;

/// <summary>Tells apart why an agent run failed, so the person can be told what to do.</summary>
public static class AgentFailureHelper
{
    private const int TooManyRequests = 429;

    /// <summary>
    /// Whether the run failed because the model's gateway was still rate limiting it (HTTP 429) after the AI agent
    /// library's retries: the app is busy, so trying again in a few minutes will usually work.
    /// </summary>
    public static bool IsRateLimited(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException { Status: TooManyRequests })
                return true;
        }

        return false;
    }
}
