using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;

public interface IBriefingOrchestrator
{
    /// <summary>
    /// Generates a briefing based on the provided parameters.
    /// </summary>
    /// <param name="parameters">The parameters for generating the briefing.</param>
    /// <param name="progress">Optional sink for human-readable step updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated briefing.</returns>
    Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
