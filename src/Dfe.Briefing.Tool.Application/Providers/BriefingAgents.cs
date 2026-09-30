using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    // RequireCitations defaults to true and checks for [Evidence n] markers, but every prompt here
    // cites sources as <a href="..."> links instead - opted out to avoid fighting that format.
    public static readonly AgentDefinition[] All =
    [
        new(AgentNames.Establishment, SystemPromptKey.Establishment) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.Ofsted, SystemPromptKey.Ofsted) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.Trust, SystemPromptKey.Trust) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.RiseConcerns, SystemPromptKey.RiseConcerns) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.RecastConcerns, SystemPromptKey.RecastConcerns) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.WebSearch, SystemPromptKey.WebSearch, IsManagedAgent: false) { Validate = ValidateHtmlOutput, RequireCitations = false },
        new(AgentNames.Synthesis, SystemPromptKey.Synthesis) { Validate = ValidateHtmlOutput, RequireCitations = false }
    ];

    // Every agent's prompt requires valid HTML output; catches a model response that broke that rule.
    private static string? ValidateHtmlOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('<') ? null : "Output must be valid HTML, not Markdown or plain text.";
    }
}
