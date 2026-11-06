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
/// </remarks>
internal static partial class KeyStageResults
{
    /// <summary>
    /// The fact for <paramref name="schoolName"/>, or <see langword="null"/> when the school's record doesn't show both
    /// that its phase is "Not applicable" and that it has no KS2 or KS4 results.
    /// </summary>
    public static string? NotApplicable(ContextResult schoolRecord, string schoolName)
    {
        ArgumentNullException.ThrowIfNull(schoolRecord);
        if (!schoolRecord.HasEvidence || !NotApplicablePhaseRegex().IsMatch(schoolRecord.Text) || ResultsRegex().IsMatch(schoolRecord.Text))
            return null;

        return $"{schoolName.Trim()} is a special school or alternative provision (phase \"Not applicable\") and its record "
            + "gives no KS2 or KS4 results: KS2 and KS4 measures don't apply to it.";
    }

    [GeneratedRegex(@"^phase_of_education:[ \t]*Not applicable[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotApplicablePhaseRegex();

    // A KS2 or KS4 field with a value, e.g. "ks4_performance_latest: {...}".
    [GeneratedRegex(@"^ks[24]_[a-z0-9_]*:[ \t]*\S", RegexOptions.Multiline | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResultsRegex();
}
