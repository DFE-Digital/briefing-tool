namespace Dfe.Briefing.Tool.ReleaseGate.Constants;

/// <summary>The release gate's commands and their options.</summary>
internal static class CommandNames
{
    public const string Gate = "gate";
    public const string ExportCopilotStudio = "export-copilot-studio";
    public const string DefaultExportFolder = "copilot-studio";
}

/// <summary>Configuration keys and the files the gate reads.</summary>
internal static class Settings
{
    public const string BriefingAppSettingsFile = "briefing.appsettings.json";
    public const string AppSettingsFile = "appsettings.json";
    public const string TestCasesDirectoryKey = "AiAgents:Evaluation:ReleaseGate:TestCasesDirectory";
    public const string RefusalPhrasesKey = "AiAgents:Evaluation:ReleaseGate:RefusalPhrases";
    public const string ApplicationInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string PreparedCasesFolder = "briefing-release-gate";
}

/// <summary>The test case files: one folder per agent under <see cref="SourceDirectory"/>, one JSON file per case.</summary>
internal static class TestCaseFields
{
    public const string SourceDirectory = "TestCases";
    public const string FilePattern = "*.json";
    public const string GuardrailTestSet = "guardrail";

    public const string Subject = "subject";
    public const string SubjectName = "name";
    public const string SubjectUrnOrTrn = "urnOrTrn";
    public const string Prompt = "prompt";
    public const string Evidence = "evidence";
    public const string SpecialistOutputs = "specialistOutputs";
    public const string SpecialistAgent = "agent";
    public const string SpecialistOutput = "output";
    public const string MustMention = "mustMention";
    public const string MustNotMention = "mustNotMention";

    public const string CopilotStudio = "copilotStudio";
    public const string CopilotStudioQuestion = "question";
    public const string CopilotStudioExpectedResponse = "expectedResponse";
}

/// <summary>The Copilot Studio test set file, in the format its evaluation imports.</summary>
internal static class CopilotStudioTestSet
{
    // Copilot Studio's limits for an imported single-response test set.
    public const int MaxQuestions = 100;
    public const int MaxQuestionLength = 1000;

    public const string TestSetFile = "test-set.csv";

    // The import takes exactly these two headings, in this order.
    public const string QuestionHeading = "Question";
    public const string ExpectedResponseHeading = "Expected response";

    public const string ListSeparator = "; ";
    public const string MarkdownHeadingPrefix = "#";
}

/// <summary>
/// Outcome labels for the console. Logs and metrics use the AI agent library's words (<c>AgentOutcomes</c>: passed,
/// failed, blocked_by_guardrail), so its standard queries read them.
/// </summary>
internal static class Outcomes
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
}

/// <summary>
/// Telemetry settings. The release metrics are the AI agent library's own, the same in every app: tagged with the app
/// (dfe.ai_agents.application, from AiAgents:ApplicationName), the agent and the release run. cloud_RoleName is
/// AiAgents:ApplicationName, with the gate's own cloud_RoleInstance.
/// </summary>
internal static class Telemetry
{
    public const string ApplicationNameKey = "AiAgents:ApplicationName";
    public const string RoleInstance = "release-gate";

    // A folder name from a GUID: digits only, no hyphens.
    public const string RunIdFormat = "N";
}

/// <summary>
/// The gate's own messages. The AI agent library logs the release check itself, in the standard form every app's
/// queries read (see README).
/// </summary>
internal static class ReleaseLog
{
    public const string RunStarted = "Release gate run {0}";
    public const string StoppedTemplate = "Release gate stopped: {Reason}";

    public const string ItemSeparator = ", ";
    public const string ConsoleProblemIndent = "    ";
    public const string TestCaseFileWrong = "A test case file is wrong: ";
}

/// <summary>Errors for a test case file that's wrong, the same whichever command reads it.</summary>
internal static class TestCaseErrors
{
    public static string Empty(string relativePath) => $"{relativePath} is empty.";

    public static string SubjectNeeds(string relativePath, string field) => $"{relativePath}: subject needs \"{field}\".";
}
