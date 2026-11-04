using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers.Interfaces;

public interface IBriefingEvidenceProvider
{
    /// <summary>
    /// Finds the records a briefing is written from: the school's or trust's own record first, then, at the same time,
    /// its trust or academies (found by the exact values in that record, not by name) and its Rise and Recast concerns.
    /// </summary>
    /// <param name="parameters">The school or trust.</param>
    /// <param name="progress">Told each set of records being collected, and what each search found.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records, or null if the school or trust itself has no record.</returns>
    Task<BriefingRecords?> GetRecordsAsync(BriefingParameters parameters, IProgress<string>? progress, CancellationToken cancellationToken);
}
