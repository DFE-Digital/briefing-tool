namespace Dfe.Briefing.Tool.Web.Constants;

internal static class ErrorMessages
{
    public const string PageNotFound = "Page not found";
    public const string NotImplemented = "Not implemented";
    public const string ProblemWithService = "Sorry, there is a problem with the service";
    public const string ApplicationInsightsMissing =
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, logs and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";
}
