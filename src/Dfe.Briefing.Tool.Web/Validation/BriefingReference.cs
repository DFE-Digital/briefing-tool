using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Validation;

/// <summary>
/// The formats of a school's URN (6 digits) and a trust's TRN (TR and 5 digits). Every page that takes one from the
/// person or the URL checks it before it's looked up.
/// </summary>
public static partial class BriefingReference
{
    /// <summary>The reference as the records hold it: trimmed, and in capitals, so "tr01234" is TR01234.</summary>
    public static string Normalise(string? urnOrTrn) => urnOrTrn?.Trim().ToUpperInvariant() ?? string.Empty;

    public static bool IsUrn(string reference) => UrnRegex().IsMatch(reference);

    public static bool IsTrn(string reference) => TrnRegex().IsMatch(reference);

    public static bool IsValid(string reference) => IsUrn(reference) || IsTrn(reference);

    /// <summary>
    /// The URN or TRN at the end of a suggestion the person chose, e.g. 123456 in "Exampleton School (URN 123456)".
    /// </summary>
    /// <returns>The reference, or null if the text doesn't end with one.</returns>
    public static string? FromSuggestion(string text)
    {
        var match = SuggestionRegex().Match(text.Trim());
        return match.Success ? match.Groups["reference"].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex UrnRegex();

    [GeneratedRegex(@"^TR\d{5}$")]
    private static partial Regex TrnRegex();

    [GeneratedRegex(@"\((?:URN|TRN) (?<reference>\d{6}|TR\d{5})\)$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SuggestionRegex();
}
