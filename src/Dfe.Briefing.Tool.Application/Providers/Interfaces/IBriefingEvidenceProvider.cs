using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers.Interfaces;

public interface IBriefingEvidenceProvider
{
    /// <summary>
    /// Searches the agent's index for the school or trust being briefed on. Pass the subject's own evidence
    /// (the school's or trust's record) when searching for a specialist, so related records can be found by the
    /// exact values in it rather than by name.
    /// </summary>
    Task<ContextResult> GetEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, ContextResult? subjectEvidence,
        CancellationToken cancellationToken);
}
