using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

internal partial class BriefingBuilder(IPromptProvider promptProvider) : IBriefingBuilder
{
    public AIResult BuildNoEvidenceResult(AgentDefinition establihsmentOrTrustAgent, BriefingParameters parameters)
    {
        var message = establihsmentOrTrustAgent.SystemPromptKey == SystemPromptKey.Trust
            ? ErrorMessages.NoTrustInformationFound
            : ErrorMessages.NoSchoolOrAcademyFound;

        return new AIResult($"# {parameters.SchoolOrTrustName}\n\n{message}", TotalTokens: 0);
    }

    public string CompileContext(IReadOnlyCollection<AgentResult> results)
        => string.Join(Environment.NewLine + Environment.NewLine, results.Select(
            result =>
                $"""
                ===================================
                AGENT: {result.AgentName.ToDisplayName()}
                ===================================

                {result.Output}
                """));
    public string BuildWebSearchInstruction(BriefingParameters parameters, string? city)
    {
        var subject = $"\"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn})";
        var locationHint = city is null ? string.Empty : $" It is located in or near {city}.";
        return $"""
            We already have the internal-record findings supplied as evidence below about {subject}.{locationHint}
            Search the web for current, genuinely new, publicly available information about this subject that is not already covered by those findings. Do not repeat or re-confirm anything already listed.
            Search local news first: local newspapers and broadcasters covering the area, and the local authority's own website and news releases. Take the local authority from the findings below; if none is given, use the place named above. Then search national sources.
            """;
    }
    public AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools) => new()
    {
        Name = AgentNames.WebSearch,
        Instructions = promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch),
        Tools = tools
    };
    public string BuildSubjectInstruction(BriefingParameters parameters)
        => $"Using only the evidence supplied, analyse \"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn}) and report your findings. Do not use any other knowledge.";

    // The web search agent's findings carry full links to each article, but when the Synthesis model rewrites them
    // it shortens the links to bare domains. So Synthesis only marks where the news goes, and the web search agent's
    // own bullets are put there unchanged.
    public string? InsertNewsFindings(string? briefing, AgentResult? webSearchResult)
    {
        if (briefing is null)
            return null;

        var markerLine = briefing.Split('\n').FirstOrDefault(line => line.Contains(NewsMarker, StringComparison.Ordinal));
        if (markerLine is null)
            return briefing;

        var indent = markerLine[..(markerLine.Length - markerLine.TrimStart().Length)];
        // Most recent first, as the model doesn't reliably order them; undated findings go last.
        var findings = (webSearchResult?.Output ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .OrderByDescending(PublishedDate)
            .ToList();

        var replacement = findings.Count > 0
            ? string.Join('\n', findings.Select(finding => indent + finding))
            : $"{indent}- {ErrorMessages.NoNewsCoverageFound}";

        return briefing.Replace(markerLine.TrimEnd('\r'), replacement, StringComparison.Ordinal);
    }

    public const string NewsMarker = "{{news}}";

    // The Synthesis model is told to leave out a section it has no facts for, but sometimes leaves its heading with
    // nothing under it - a "##" heading followed straight by another heading, or by the end, is removed here.
    public string? RemoveEmptySections(string? briefing)
        => briefing is null ? null : EmptySectionRegex().Replace(briefing, string.Empty);

    [System.Text.RegularExpressions.GeneratedRegex(@"^##[ \t]+[^\r\n]*\r?\n(?:[ \t]*\r?\n)*(?=^##?[ \t]|\z)", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex EmptySectionRegex();

    // A finding starts with its date, e.g. "- 20 August 2026: ...".
    private static DateOnly PublishedDate(string finding)
    {
        var colon = finding.IndexOf(':', StringComparison.Ordinal);
        return colon > 2 && DateOnly.TryParseExact(finding[2..colon].Trim(), "d MMMM yyyy", EnglishCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;
    }

    private static readonly System.Globalization.CultureInfo EnglishCulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
}
