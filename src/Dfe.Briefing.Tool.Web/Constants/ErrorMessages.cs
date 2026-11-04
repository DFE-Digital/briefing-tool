namespace Dfe.Briefing.Tool.Web.Constants;

internal static class ErrorMessages
{
    public const string DataProtectionPathUndefined =
            "DataProtection:Path is undefined or empty. Set it to a folder every instance of the app shares, so they all " +
            "use the same keys for sign-in and form cookies.";
    public const string DataProtectionKeyVaultKeyUndefined =
            "DataProtection:KeyVaultKey is undefined or empty. Set it to the URI of the Key Vault key that encrypts the " +
            "data protection keys kept in DataProtection:Path, so they aren't stored unencrypted.";
    public const string DataProtectionPathNotWritable = "DataProtection path '{0}' cannot be written to";
    public const string PageNotFound = "Page not found";
    public const string NotImplemented = "Not implemented";
    public const string ProblemWithService = "Sorry, there is a problem with the service";
    public const string ApplicationInsightsMissing =
            "APPLICATIONINSIGHTS_CONNECTION_STRING isn't set, so token usage, logs and release-gate results can't be sent to " +
            "Application Insights. Set it to the Application Insights connection string, or, for local development " +
            "only, set AiAgents:RequireTokenUsageTelemetry to false.";
}
