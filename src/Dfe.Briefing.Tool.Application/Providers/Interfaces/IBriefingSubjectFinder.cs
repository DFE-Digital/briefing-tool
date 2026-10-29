using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers.Interfaces;

public interface IBriefingSubjectFinder
{
    /// <summary>
    /// Finds the school (by URN) or trust (by TRN, starting "TR") in the same records the briefing uses, so the person
    /// can confirm it's the one they want before a briefing is generated.
    /// </summary>
    /// <returns>The school or trust, or null if there's no record for it.</returns>
    Task<BriefingSubject?> FindAsync(string urnOrTrn, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for schools or trusts by name, URN or TRN, as the person types it. In a name, each word matches the
    /// start of a word, e.g. "aspir acad" finds Aspirations Academies Trust. A URN or TRN, whole or its start (e.g.
    /// "1234" or "TR001"), finds the schools or trusts it could be - a URN schools and a TRN trusts, whichever
    /// <paramref name="isTrust"/> says.
    /// </summary>
    /// <param name="text">What the person has typed.</param>
    /// <param name="isTrust">Whether a name is searched for among trusts, or among schools and other establishments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best matches, most relevant first; none for text with no letters or digits in it.</returns>
    Task<IReadOnlyList<BriefingSubject>> SearchAsync(string text, bool isTrust, CancellationToken cancellationToken);
}
