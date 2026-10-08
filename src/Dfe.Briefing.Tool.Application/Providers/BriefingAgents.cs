using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

public static class BriefingAgents
{
    public static readonly AgentDefinition[] All =
    [
        new(AgentNames.Establishment, SystemPromptKey.Establishment) { Validate = ValidateHtmlOutput },
        new(AgentNames.Ofsted, SystemPromptKey.Ofsted) { Validate = ValidateHtmlOutput },
        new(AgentNames.Trust, SystemPromptKey.Trust) { Validate = ValidateHtmlOutput },
        new(AgentNames.RiseConcerns, SystemPromptKey.RiseConcerns) { Validate = ValidateHtmlOutput },
        new(AgentNames.RecastConcerns, SystemPromptKey.RecastConcerns) { Validate = ValidateHtmlOutput },
        new(AgentNames.Synthesis, SystemPromptKey.Synthesis) { Validate = ValidateHtmlOutput }
    ];

    public static string? ValidateHtmlOutput(AgentResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Output))
            return "Output must not be empty.";

        return result.Output.TrimStart().StartsWith('<') ? null : "Output must be valid HTML, not Markdown or plain text.";
    }
}
