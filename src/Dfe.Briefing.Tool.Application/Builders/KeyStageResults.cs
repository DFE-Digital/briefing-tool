using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Says, as a fact worked out in code, when KS2 and KS4 results don't apply to a school: a special school or
/// alternative provision (phase "Not applicable") whose record gives no KS2 or KS4 results.
/// </summary>
/// <remarks>
/// Told only in its prompt, the agent sometimes wrote "The latest KS2 results are not available" for such a school, as
/// that's the sentence for a mainstream school with no results. Reading the decision as a fact makes the rule hold.
/// Only the requested school's own records are read: the set can hold another school's, e.g. a similarly named one,
/// whose phase or results must not decide this school's fact.
/// </remarks>
internal static partial class KeyStageResults
{
    private const string NotApplicablePhase = "Not applicable";

    /// <summary>
    /// The fact for <paramref name="schoolName"/>, or <see langword="null"/> unless its own records - those giving
    /// <paramref name="urn"/>, or every record when none gives a URN - all give its phase as "Not applicable" and none
    /// gives a KS2 or KS4 result.
    /// </summary>
    public static string? NotApplicable(ContextResult schoolRecord, string urn, string schoolName)
    {
        ArgumentNullException.ThrowIfNull(schoolRecord);
        if (!schoolRecord.HasEvidence)
            return null;

        var records = RecordStartRegex().Split(schoolRecord.Text).Where(static record => !string.IsNullOrWhiteSpace(record)).ToList();
        var own = records.Any(static record => UrnRegex().IsMatch(record))
            ? records.Where(record => UrnRegex().Match(record) is { Success: true } match
                && string.Equals(match.Groups["urn"].Value.Trim(), urn.Trim(), StringComparison.OrdinalIgnoreCase)).ToList()
            : records;

        var phases = own.SelectMany(static record => PhaseRegex().Matches(record).Select(static match => match.Groups["phase"].Value.Trim())).ToList();
        if (phases.Count == 0 || !phases.TrueForAll(static phase => phase.Equals(NotApplicablePhase, StringComparison.OrdinalIgnoreCase))
            || own.Exists(static record => ResultsRegex().IsMatch(record)))
        {
            return null;
        }

        return $"{schoolName.Trim()} is a special school or alternative provision (phase \"Not applicable\") and its record "
            + "gives no KS2 or KS4 results: KS2 and KS4 measures don't apply to it.";
    }

    // A record starts with a line giving its number: "--- establishment_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence \d+ ---|\[Evidence \d+\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordStartRegex();

    [GeneratedRegex(@"^establishment_urn:[ \t]*(?<urn>\S[^\r\n]*?)[ \t]*\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrnRegex();

    [GeneratedRegex(@"^phase_of_education:[ \t]*(?<phase>\S[^\r\n]*?)[ \t]*\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhaseRegex();

    // A KS2 or KS4 field with a value, e.g. "ks4_performance_latest: {...}".
    [GeneratedRegex(@"^ks[24]_[a-z0-9_]*:[ \t]*\S", RegexOptions.Multiline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResultsRegex();
}
