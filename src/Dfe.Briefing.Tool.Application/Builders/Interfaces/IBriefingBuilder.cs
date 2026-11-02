using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders.Interfaces;

public interface IBriefingBuilder
{
    AIResult BuildNoEvidenceResult(BriefingParameters parameters);
    string BuildBriefingPrompt(BriefingParameters parameters);
    AgentEvidence BuildBriefingEvidence(BriefingParameters parameters, BriefingRecords records);
    string BuildWebSearchInstruction(BriefingParameters parameters, string? city);
    AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools);
    string BuildWebSearchEvidence(string briefing);
    string BuildNewsFindings(AgentResult webSearchResult, BriefingParameters parameters, string? website);
    bool HasNewsSection(string briefing);
    string? RemoveBackgroundSection(string? briefing);
    string? RemoveEmptySections(string? briefing);
}
