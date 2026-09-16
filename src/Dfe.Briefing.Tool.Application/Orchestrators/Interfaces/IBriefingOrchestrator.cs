using Dfe.Briefing.Tool.Domain;
using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;

public interface IBriefingOrchestrator
{
    Task<AIResult> GetBriefing(BriefingParameters parameters, CancellationToken cancellationToken = default);
}
