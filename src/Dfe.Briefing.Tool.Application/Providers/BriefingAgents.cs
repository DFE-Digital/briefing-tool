using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    public static readonly AgentDefinition[] All =
    [
        new(AgentNames.Establishment, SystemPromptType.Establishment) { Validate = ValidateHtmlOutput },
        new(AgentNames.Ofsted, SystemPromptType.Ofsted) { Validate = ValidateHtmlOutput },
        new(AgentNames.Trust, SystemPromptType.Trust) { Validate = ValidateHtmlOutput },
        new(AgentNames.RiseConcerns, SystemPromptType.RiseConcerns) { Validate = ValidateHtmlOutput },
        new(AgentNames.RecastConcerns, SystemPromptType.RecastConcerns) { Validate = ValidateHtmlOutput },
        new(AgentNames.WebSearch, SystemPromptType.WebSearch, IsManagedAgent: false) { Validate = ValidateHtmlOutput },
        new(AgentNames.Synthesis, SystemPromptType.Synthesis) { Validate = ValidateHtmlOutput }
    ];

    // Every agent's prompt requires valid HTML output; catches a model response that broke that rule.
    private static string? ValidateHtmlOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('<') ? null : "Output must be valid HTML, not Markdown or plain text.";
    }
}
