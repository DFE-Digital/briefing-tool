using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders.Interfaces;

public interface IBriefingBuilder
{
    AIResult BuildNoEvidenceResult(AgentDefinition establihsmentOrTrustAgent, BriefingParameters parameters);
    string CompileContext(IReadOnlyCollection<AgentResult> results);
    string BuildWebSearchInstruction(BriefingParameters parameters, string? city);
    string BuildSubjectInstruction(BriefingParameters parameters);
    AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools);
    string? InsertNewsFindings(string? briefing, AgentResult? webSearchResult);
    string? RemoveEmptySections(string? briefing);
}
