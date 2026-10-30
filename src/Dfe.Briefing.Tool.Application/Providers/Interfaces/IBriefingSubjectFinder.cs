using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers.Interfaces;

public interface IBriefingSubjectFinder
{
    /// <summary>
    /// Finds the school (by URN) or trust (by TRN) in the same records the briefing uses, so the person can confirm it's
    /// the one they want before a briefing is generated.
    /// </summary>
    /// <param name="urnOrTrn">The school's URN, or the trust's TRN.</param>
    /// <param name="isTrust">Whether the person chose a trust, so it's looked for among trusts, or a school.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The school or trust, or null if there's no record for it.</returns>
    Task<BriefingSubject?> FindAsync(string urnOrTrn, bool isTrust, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for schools or trusts by name, URN or TRN, as the person types it. In a name, each word matches the
    /// start of a word, e.g. "aspir acad" finds Aspirations Academies Trust. A school's URN or a trust's TRN, whole or
    /// its start (e.g. "1234" or "TR001"), finds the schools or trusts it could be.
    /// </summary>
    /// <param name="text">What the person has typed.</param>
    /// <param name="isTrust">Whether the person chose to search for a trust, or a school or other establishment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best matches, most relevant first; none for text with no letters or digits in it.</returns>
    Task<IReadOnlyList<BriefingSubject>> SearchAsync(string text, bool isTrust, CancellationToken cancellationToken);
}
