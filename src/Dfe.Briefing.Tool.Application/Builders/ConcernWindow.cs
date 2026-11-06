using System.Globalization;
using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Keeps only the Rise and Recast concerns from the last <see cref="Years"/> years, so the agent never sees an older one.
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

        var starts = RecordStartRegex().Matches(concerns.Text);
        if (starts.Count == 0)
            return concerns;

        var from = today.AddYears(-Years);
        var kept = new List<string>();
        var dropped = new HashSet<int>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : concerns.Text.Length;
            var record = concerns.Text[starts[i].Index..end].Trim();
            // A date after today is a recording error, not a concern in the window, so it's left out too.
            if (ConcernDate(record) is { } date && (date < from || date > today))
                dropped.Add(int.Parse(starts[i].Groups["number"].Value, CultureInfo.InvariantCulture));
            else
                kept.Add(record);
        }

        if (dropped.Count == 0)
            return concerns;

        if (kept.Count == 0)
            return new ContextResult(NoneRecent, HasEvidence: false);

        // Anything before the first record, e.g. a note from the search, stays.
        var preamble = concerns.Text[..starts[0].Index].Trim();
        var text = string.Join("\n\n", preamble.Length > 0 ? [preamble, .. kept] : kept);
        return concerns with { Text = text, Sources = [.. concerns.Sources.Where(source => !dropped.Contains(source.Number))] };
    }

    /// <summary>
    /// <paramref name="concerns"/>, with each concern_date written as the briefing gives it, e.g. "11 February 2025".
    /// </summary>
    /// <remarks>
    /// Converting each date itself, the agent once gave a concern the date of another concern about the same academy.
    /// Written out here, each date is copied as it stands. A date that can't be read is left as it is.
    /// </remarks>
    public static ContextResult WithDatesInWords(ContextResult concerns)
    {
        ArgumentNullException.ThrowIfNull(concerns);
        if (!concerns.HasEvidence)
            return concerns;

        var text = ConcernDateValueRegex().Replace(concerns.Text, match =>
            DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? match.Groups["field"].Value + date.ToString("d MMMM yyyy", EnglishCulture)
                : match.Value);
        return text == concerns.Text ? concerns : concerns with { Text = text };
    }

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");

    private static DateOnly? ConcernDate(string record)
        => ConcernDateRegex().Match(record) is { Success: true } match
            && DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    // A record starts with a line giving its number: "--- rise_concerns_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence (?<number>\d+) ---|\[Evidence (?<number>\d+)\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordStartRegex();

    [GeneratedRegex(@"^concern_date:[ \t]*(?<date>\d{4}-\d{2}-\d{2})", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConcernDateRegex();

    // The whole value: the date, and any time after it ("2025-02-11T09:00:00Z"), which the briefing doesn't give.
    [GeneratedRegex(@"^(?<field>concern_date:[ \t]*)(?<date>\d{4}-\d{2}-\d{2})(?:T[^\s]*)?(?=[ \t]*\r?$)", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConcernDateValueRegex();
}
