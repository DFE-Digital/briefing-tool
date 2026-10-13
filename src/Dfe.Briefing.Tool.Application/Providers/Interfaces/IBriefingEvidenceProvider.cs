using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers.Interfaces;

public interface IBriefingEvidenceProvider
{
    Task<ContextResult> GetEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken);
}
