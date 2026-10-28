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
    string BuildSynthesisPrompt(BriefingParameters parameters, IReadOnlyCollection<AgentResult> specialistResults);
    AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools);
    string BuildWebSearchEvidence(string briefing);
    string BuildNewsFindings(AgentResult webSearchResult);
    bool HasNewsSection(string briefing);
    string? RemoveBackgroundSection(string? briefing);
    string? RemoveEmptySections(string? briefing);
}
