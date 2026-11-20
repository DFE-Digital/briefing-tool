using System.Globalization;
using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Keeps only the Rise and Recast concerns from the last <see cref="Years"/> years, so the agent never sees an older one,
/// and puts them most recent first, the order the briefing lists them in.
/// </summary>
/// <remarks>
/// The briefing reports concerns from the 3 years up to today. Told only in its prompt, the agent still listed older
/// ones, as it's also told never to leave a concern out; leaving them out of the evidence makes the rule hold every time,
/// and sends the model less. A concern dated after today is a recording error, so it's left out too; one whose date
/// can't be read is kept, as its age isn't known.
/// </remarks>
internal static partial class ConcernWindow
{
    public const int Years = 3;

    /// <summary>What a set of concerns says when every one was older than the window.</summary>
    public const string NoneRecent = "No concerns from the 3 years up to today were found.";

    /// <summary>
    /// The concerns in <paramref name="concerns"/> dated from <paramref name="today"/> less <see cref="Years"/> years up
    /// to <paramref name="today"/>, each kept with its own record number and source.
    /// </summary>
    public static ContextResult Recent(ContextResult concerns, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(concerns);
        if (!concerns.HasEvidence)
            return concerns;

        var (preamble, records) = EvidenceRecords.Split(concerns.Text);
        var from = today.AddYears(-Years);
        var kept = records.Where(record => ConcernDate(record.Text) is not { } date || (date >= from && date <= today)).ToList();

        if (kept.Count == records.Count)
            return concerns;

        return kept.Count == 0
            ? new ContextResult(NoneRecent, HasEvidence: false)
            : EvidenceRecords.Rebuild(concerns, preamble, records, kept);
    }

    /// <summary>
    /// <paramref name="concerns"/> most recent first, each with its own record number, so every citation still points at
    /// its record. A concern whose date can't be read goes last.
    /// </summary>
    /// <remarks>Told only in its prompt to list them most recent first, the agent didn't always.</remarks>
    public static ContextResult NewestFirst(ContextResult concerns)
    {
        ArgumentNullException.ThrowIfNull(concerns);
        if (!concerns.HasEvidence)
            return concerns;

        var (preamble, records) = EvidenceRecords.Split(concerns.Text);
        var ordered = records.OrderByDescending(record => ConcernDate(record.Text)).ToList();
        return ordered.SequenceEqual(records) ? concerns : EvidenceRecords.Rebuild(concerns, preamble, records, ordered);
    }

    private static DateOnly? ConcernDate(string record)
        => ConcernDateRegex().Match(record) is { Success: true } match
            && DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    [GeneratedRegex(@"^concern_date:[ \t]*(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConcernDateRegex();
}
