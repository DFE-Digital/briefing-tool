using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    public static readonly AgentDefinition[] All =
    [
        new(AgentNames.Establishment, SystemPromptKey.Establishment) { Validate = ValidateMarkdownOutput },
        new(AgentNames.Trust, SystemPromptKey.Trust) { Validate = ValidateMarkdownOutput },
        new(AgentNames.RiseConcerns, SystemPromptKey.RiseConcerns) { Validate = ValidateMarkdownOutput },
        new(AgentNames.RecastConcerns, SystemPromptKey.RecastConcerns) { Validate = ValidateMarkdownOutput },
        new(AgentNames.Synthesis, SystemPromptKey.Synthesis) { Validate = ValidateMarkdownOutput, RequiredCitations = false }
    ];

    public static string? ValidateMarkdownOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('#') ? null : "Output must be valid Markdown, starting with a heading.";
    }
}
