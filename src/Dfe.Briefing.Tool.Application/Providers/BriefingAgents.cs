using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    /// <summary>
    /// Writes the whole briefing in one run from the school's or trust's records, citing each fact's record. The web
    /// search agent is built per run (see BriefingBuilder.BuildWebSearchAgentSpec), so it isn't listed here.
    /// </summary>
    public static readonly AgentDefinition Briefing = new(AgentNames.Briefing, SystemPromptKey.Briefing) { Validate = ValidateMarkdownOutput };

    public static readonly AgentDefinition[] All = [Briefing];

    public static string? ValidateMarkdownOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('#') ? null : "Output must be valid Markdown, starting with a heading.";
    }
}
