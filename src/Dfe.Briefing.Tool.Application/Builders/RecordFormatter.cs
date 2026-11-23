using System.Globalization;
using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Writes the records' values as the briefing gives them, so the agent copies them rather than converting them:
/// dates as "28 November 2022", academic years as "2023/24", and scores without a "%". A result whose key names it
/// only in code is given the name the briefing templates use for it.
/// </summary>
internal static partial class RecordFormatter
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");

    /// <summary><paramref name="records"/>, with every date, academic year and score written as the briefing gives it.</summary>
    public static ContextResult Format(ContextResult records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (!records.HasEvidence)
            return records;

        var text = DateRegex().Replace(records.Text, DateInWords);
        text = AcademicYearRegex().Replace(text, AcademicYear);
        text = AcademicYearKeyRegex().Replace(text, AcademicYear);
        text = UnclearKeyRegex().Replace(text, key => ClearKeyNames[key.Groups["name"].Value]);
        text = PercentScoreRegex().Replace(text, "${field}${score}");
        return text == records.Text ? records : records with { Text = text };
    }

    // "2022-11-28", and any time after it ("2022-11-28T09:30:00Z"), which the briefing doesn't give.
    private static string DateInWords(Match match)
        => DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMMM yyyy", EnglishCulture)
            : match.Value;

    // "202324" is 2023/24; six digits whose years don't follow on aren't an academic year, so they're left alone.
    private static string AcademicYear(Match match)
    {
        var start = int.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
        var end = int.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture);
        return (start + 1) % 100 == end
            ? $"{match.Groups["field"].Value}{match.Groups["start"].Value}/{match.Groups["end"].Value}"
            : match.Value;
    }

    // Not part of a longer number, a web address's path ("/2024-06-03/") or a range ("2024-06-03-2024-06-10").
    [GeneratedRegex(@"(?<![\d/-])(?<date>\d{4}-\d{2}-\d{2})(?:T[\d:.]+(?:Z|[+-]\d{2}:?\d{2})?)?(?![\d/-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DateRegex();

    // "academic_year": "202324" inside a result's JSON, or academic_year: 202324 on its own line.
    [GeneratedRegex(@"(?<field>""?[a-z0-9_]*academic_year""?[ \t]*:[ \t]*""?)(?<start>\d{4})(?<end>\d{2})(?!\d)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AcademicYearRegex();

    // A JSON key that is an academic year, as results by year give them: {"202425": {...}}.
    [GeneratedRegex(@"(?<field>"")(?<start>\d{4})(?<end>\d{2})(?=""[ \t]*:)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AcademicYearKeyRegex();

    // JSON keys the agent misread under their own names, given the names the briefing templates use. Only the key is
    // renamed. The KS4 results' "engmath_95_percent" (twice: {"engmath_95_percent": {"engmath_95_percent": {...}}})
    // was mistaken for the grade 5 to 9 percentages or left out; the socio-economic figures' "enfl_pct" was taken for
    // a social worker or deprivation figure.
    private static readonly Dictionary<string, string> ClearKeyNames = new(StringComparer.Ordinal)
    {
        ["engmath_95_percent"] = "\"percentage_achieving_grade_5_or_above_in_english_and_maths\"",
        ["enfl_pct"] = "\"percentage_with_english_not_as_first_language\"",
    };

    [GeneratedRegex(@"""(?<name>engmath_95_percent|enfl_pct)""(?=[ \t]*:)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnclearKeyRegex();

    // Attainment 8, Progress 8, the EBacc average point score and the Attainment 8 English and maths elements are
    // scores, not percentages, whatever the records label them.
    [GeneratedRegex(@"(?<field>""?[a-z0-9_]*(?:attainment_8|progress_8|ebacc_average_point_score|_element)[a-z0-9_]*""?[ \t]*:[ \t]*""?)(?<score>[-+]?\d+(?:\.\d+)?)[ \t]*%", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PercentScoreRegex();
}
