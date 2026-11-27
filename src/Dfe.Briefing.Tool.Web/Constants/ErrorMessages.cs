namespace Dfe.Briefing.Tool.Web.Constants;

public static class ErrorMessages
{
    public const string PageNotFound = "Page not found";
    public const string NotImplemented = "Not implemented";
    public const string ProblemWithService = "Sorry, there is a problem with the service";
    public const int DefaultRunTimeoutSeconds = 360;
    public const string BriefingNotSearchable =
        "This summary can't be searched for more information. Generate it again and try once more.";
    public const string BriefingFailed =
        "Sorry, something went wrong generating this summary. Please try again.";
    public const string BriefingBusy =
        "The service is busy. Please try again in a few minutes.";
    public const string WebSearchFailed =
        "Sorry, something went wrong searching the web. Your summary is unchanged - please try again.";
    public const string WebSearchBusy =
        "The service is busy. Your summary is unchanged - please try the web search again in a few minutes.";
    public const string BriefingTimedOutLog =
        "Briefing generation timed out after the configured RunTimeout of {RunTimeoutSeconds} seconds for {SchoolOrTrustName}";
    public const string BriefingFailedLog =
        "Failed to generate briefing for {SchoolOrTrustName}";
    public const string ErrorPageLogTemplate =
        "ErrorPage::Something went wrong - {RequestId}, {ErrorMessage}";
    public const string ErrorPageUnhandledLogTemplate =
        "ErrorPage::Something went wrong - {RequestId}, {Message}";
    public const string ApplicationInsightsMissing =
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, logs and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";

    public static string BriefingTimedOut(int runTimeoutSeconds)
    {
        var effectiveTimeoutSeconds = runTimeoutSeconds > 0
            ? runTimeoutSeconds
            : DefaultRunTimeoutSeconds;
        var minutes = (int)Math.Ceiling(effectiveTimeoutSeconds / 60d);
        var minuteLabel = minutes == 1 ? "minute" : "minutes";
        return $"The summary could not be generated within {minutes} {minuteLabel}. Please try again.";
    }

    public static string SubjectNotFound(string subjectNoun)
        => $"This {subjectNoun} can't be found. Go back and search for it again.";
}
