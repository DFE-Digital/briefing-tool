namespace Dfe.Briefing.Tool.Application.Constants;

internal static class ErrorMessages
{
    public const string FailedToLoadPromptFile = "Failed to load prompt file for {PromptType}. Using fallback.";
    public const string NoPromptConfigured = "No prompt configured for type: {PromptType}";
    public const string NoEvidenceCategoryConfigured = "Agent '{0}' has no evidence category configured.";
    public const string NoSearchQueryConfigured = "Agent '{0}' has no search query configured.";
    public const string NoEstablishmentDataFound = "No evidence was found for '{0}' - a briefing cannot be generated.";
}
