using System.Text.Json.Nodes;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;

namespace Dfe.Briefing.Tool.ReleaseGate.Constants;

/// <summary>The release gate's commands.</summary>
internal static class CommandNames
{
    public const string Gate = "gate";
}

/// <summary>Configuration keys and the files the gate reads.</summary>
internal static class Settings
{
    public const string AppSettingsFile = "appsettings.json";
    public const string TestCasesDirectoryKey = "AiAgents:Evaluation:ReleaseGate:TestCasesDirectory";

    // The web app's run slots shared across instances, which the gate doesn't use (see Program.cs).
    public const string GlobalConcurrencyRunsKey = "AiAgents:GlobalConcurrency:MaxRunsAcrossInstances";
    public const string GlobalConcurrencyContainerKey = "AiAgents:GlobalConcurrency:BlobContainerUri";
    public const string PreparedCasesFolder = "briefing-release-gate";
}

/// <summary>
/// The test case files: one folder per agent under <see cref="SourceDirectory"/>, one JSON file per Responsible AI
/// principle, its cases split into <see cref="EvaluationSection"/> and <see cref="GuardrailSection"/>.
/// </summary>
internal static class TestCaseFields
{
    public const string SourceDirectory = "TestCases";
    public const string FilePattern = "*.json";

    // The AI agent library's sections: every case in "guardrail" is an attack the guardrail must block.
    public const string EvaluationSection = "evaluation";
    public const string GuardrailSection = "guardrail";

    // The library's single list of cases, which these files don't use.
    public const string Tests = "tests";
    public const string Id = "id";

    public const string Subject = "subject";
    public const string SubjectName = "name";
    public const string SubjectUrnOrTrn = "urnOrTrn";
    public const string Prompt = "prompt";
    public const string Evidence = "evidence";
    public const string Records = "records";
    public const string SchoolRecords = "school";
    public const string TrustRecords = "trust";
    public const string AcademyRecords = "academies";
    public const string RiseConcerns = "riseConcerns";
    public const string RecastConcerns = "recastConcerns";
    public const string MustMention = "mustMention";
    public const string MustNotMention = "mustNotMention";
}

/// <summary>
/// Outcome labels for the console. Logs and metrics use the AI agent library's words (<c>AgentOutcomes</c>: passed,
/// failed, blocked_by_guardrail), so its standard queries read them.
/// </summary>
internal static class Outcomes
{
    public const string Pass = "PASS";
    public const string Warning = "WARNING";
    public const string Fail = "FAIL";
    public const string NotEvaluated = "not evaluated";

    public static string Of(ReleaseDecision decision) => decision switch
    {
        ReleaseDecision.Pass => Pass,
        ReleaseDecision.Warning => Warning,
        _ => Fail,
    };
}

/// <summary>
/// Telemetry settings. The release metrics are the AI agent library's own, the same in every app: tagged with the app
/// (dfe.ai_agents.application, from AiAgents:ApplicationName), the agent and the release run. cloud_RoleName is
/// AiAgents:ApplicationName, with the gate's own cloud_RoleInstance.
/// </summary>
internal static class Telemetry
{
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

    // Each phase as it starts, so a log (or a pipeline that's still running) shows where the gate is.
    public const string PreparingTestCases = "[1/3] Preparing the test cases";
    public const string RunningTestCases = "[2/3] Checking the guardrail, running every test case and scoring it with the judge (this takes a while)";
    public const string Deciding = "[3/3] Deciding the release under the Responsible AI policy";
    public const string StoppedTemplate = "Release gate stopped: {Reason}";

    public const string ItemSeparator = ", ";
    public const string ConsoleProblemIndent = "    ";
    public const string Blocked = "  (release blocked)";
    public const string TestCaseFileWrong = "A test case file is wrong: ";
}

/// <summary>Errors for a test case file that's wrong, the same whichever command reads it.</summary>
internal static class TestCaseErrors
{
    public static string Empty(string relativePath) => $"{relativePath} is empty.";

    public static string UseSections(string relativePath)
        => $"{relativePath} has \"{TestCaseFields.Tests}\": put each case in \"{TestCaseFields.EvaluationSection}\", or in \"{TestCaseFields.GuardrailSection}\" if it's an attack.";

    /// <summary>Where a case is, for messages: "fairness.json FAIR-001", or its position when it has no id.</summary>
    public static string CaseAt(string relativePath, JsonObject testCase, int index)
        => $"{relativePath} {(testCase[TestCaseFields.Id]?.GetValue<string>() is { Length: > 0 } id ? id : $"test {index + 1}")}";

    public static string SubjectNeeds(string relativePath, string field) => $"{relativePath}: subject needs \"{field}\".";

    public static string RecordsNeeded(string relativePath)
        => $"{relativePath} needs \"{TestCaseFields.Records}\": the records the briefing is written from, by set.";

    public static string SubjectRecordNeeded(string relativePath, string set)
        => $"{relativePath} needs its subject's own record in \"{TestCaseFields.Records}\": \"{set}\". The app writes no briefing without it.";

    public static string UnknownRecords(string relativePath, string set, IEnumerable<string> allowed)
        => $"{relativePath}: \"{set}\" isn't a set of records for this subject. Use {string.Join(", ", allowed.Select(name => $"\"{name}\""))}.";
}
