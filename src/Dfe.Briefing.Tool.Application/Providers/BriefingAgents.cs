using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    /// <summary>
    /// Writes the whole briefing in one run from the school's or trust's records, citing each fact's record. The web
    /// search agent is built per run (see BriefingBuilder.BuildWebSearchAgentSpec), so it isn't listed here.
    /// </summary>
    /// <remarks>
    /// <see cref="AgentDefinition.PresentOutput"/> is the step the app takes before showing the briefing, so the release
    /// gate checks and scores the briefing users read, not the location marker or sections the app removes.
    /// </remarks>
    public static readonly AgentDefinition Briefing = new(AgentNames.Briefing, SystemPromptKey.Briefing)
    {
        Validate = ValidateMarkdownOutput,
        PresentOutput = BriefingPresenter.AsShown,
    };

    public static readonly AgentDefinition[] All = [Briefing];

    public static string? ValidateMarkdownOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('#') ? null : "Output must be valid Markdown, starting with a heading.";
    }
}
