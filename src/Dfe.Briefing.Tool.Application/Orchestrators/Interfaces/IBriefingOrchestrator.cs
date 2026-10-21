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

    /// <summary>Whether a web search can be offered for this briefing: it found records, and has no news yet.</summary>
    bool CanAddWebSearchFindings(string? briefing);

    /// <summary>
    /// Searches the web for current news about the school or trust, once the person has asked for it, and adds the
    /// findings as a "More background information" section, under "News and media coverage", at the end of a
    /// briefing <see cref="GetBriefing"/> produced.
    /// Only the web search agent runs; the briefing is reused as it is.
    /// </summary>
    /// <param name="parameters">The school or trust the briefing is about.</param>
    /// <param name="briefing">The briefing's Markdown, as <see cref="GetBriefing"/> returned it.</param>
    /// <param name="city">The town or city <see cref="GetBriefing"/> returned, if any, to focus the search.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The briefing with the More background information section at the end.</returns>
    Task<AIResult> AddWebSearchFindings(BriefingParameters parameters, string briefing, string? city, CancellationToken cancellationToken = default);
}
