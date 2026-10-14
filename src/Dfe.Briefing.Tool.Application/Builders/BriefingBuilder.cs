using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

internal class BriefingBuilder(IPromptProvider promptProvider) : IBriefingBuilder
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

}
