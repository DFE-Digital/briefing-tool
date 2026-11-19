using System.Globalization;
using System.Text.Json;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>The academies' names in the order each section lists them; a section no academy has a figure for is empty.</summary>
internal sealed record AcademyOrders(IReadOnlyList<string> Ofsted, IReadOnlyList<string> Ks2, IReadOnlyList<string> Ks4, IReadOnlyList<string> Absence);

/// <summary>
/// The order a trust's academies are listed in, section by section, worked out in code: the Ofsted summary most serious
/// first, the KS2 and KS4 summaries highest result first, and attendance highest overall absence first.
/// </summary>
/// <remarks>
/// Sorting was the agent's job, told in its prompt, and it sorted unreliably, especially across many academies. Now the
/// briefing's bullets are put in this order after it's written (see <see cref="AcademyListOrder"/>), so the prompt
/// needn't describe it.
/// </remarks>
internal static partial class AcademyCounter
{
    private const string Ks2Field = "ks2_performance_latest";
    private const string Ks4Field = "ks4_performance_latest";
    private const string SchoolAttendanceField = "school_attendance";
    private const string TrustAttendanceField = "academy_attendance";
    private const string Ks2ResultName = "reading_writing_maths_expected_standard";
    private const string Ks4ScoreName = "attainment_8_score";
    private const string AcademicYearName = "academic_year";
    private const string OverallAbsenceName = "overall_absence";
    private const string AcademyName = "academy";

    // A report card with an area graded Urgent improvement comes first, then one with Needs attention, then the rest.
    private static readonly string[] ReportCardConcernGrades = ["Urgent improvement", "Needs attention"];

    /// <summary>The order of the trust's academies in each section.</summary>
    /// <param name="academyRecords">The academies' records, as the search gives them.</param>
    /// <param name="trustRecord">The trust's own record, which can give each academy's attendance.</param>
    /// <returns>The orders, or <see langword="null"/> when the records hold no academy.</returns>
    public static AcademyOrders? Order(string? academyRecords, string? trustRecord)
    {
        var academies = ReadAcademies(academyRecords);
        if (academies.Count == 0)
            return null;

        return new AcademyOrders(
            [.. academies
                .OrderBy(academy => OverallRank(academy.LatestInspection.Outcome))
                .ThenBy(academy => ReportCardRank(academy.LatestInspection))
                .ThenBy(academy => academy.Name, StringComparer.OrdinalIgnoreCase)
                .Select(academy => academy.Name)],
            HighestFirst(academies.Select(academy => (academy.Name, Figure(academy.Ks2Result, Ks2ResultName)))),
            HighestFirst(academies.Select(academy => (academy.Name, Figure(academy.Ks4Score, Ks4ScoreName)))),
            HighestFirst(Absences(academies, trustRecord)));
    }

    // Academies with the same figure are in alphabetical order; an academy without the figure isn't listed.
    private static List<string> HighestFirst(IEnumerable<(string Name, double? Figure)> figures)
        => [.. figures
            .Where(figure => figure.Figure is not null)
            .OrderByDescending(figure => figure.Figure)
            .ThenBy(figure => figure.Name, StringComparer.OrdinalIgnoreCase)
            .Select(figure => figure.Name)];

    private static int ReportCardRank(LatestInspection inspection)
    {
        if (inspection.Outcome != ReportCard)
            return 0;

        var rank = Array.FindIndex(ReportCardConcernGrades,
            grade => inspection.ReportCardGrades.Contains(grade, StringComparer.OrdinalIgnoreCase));
        return rank >= 0 ? rank : ReportCardConcernGrades.Length;
    }

    /// <summary>
    /// Each academy's overall absence in its latest academic year: from its own record, or the trust record's
    /// <c>academy_attendance</c> where that gives a later year or the academy's record gives none.
    /// </summary>
    private static IEnumerable<(string Name, double? Figure)> Absences(List<Academy> academies, string? trustRecord)
    {
        var latest = new Dictionary<string, (string Name, string Year, double? Absence)>(StringComparer.OrdinalIgnoreCase);
        foreach (var academy in academies)
            Offer(latest, academy.Name, LatestYear(Entries(academy.Attendance)));

        foreach (var entry in TrustAttendance(trustRecord))
        {
            if (Text(entry, AcademyName) is { } name)
                Offer(latest, name, (Scalar(entry, AcademicYearName) ?? string.Empty, Number(Scalar(entry, OverallAbsenceName))));
        }

        return latest.Values.Select(value => (value.Name, value.Absence));
    }

    private static void Offer(Dictionary<string, (string Name, string Year, double? Absence)> latest, string name, (string Year, double? Absence)? entry)
    {
        if (entry is not { Absence: not null } offered)
            return;

        // The academy keeps the name it was first given, so the same academy is listed once however its records write it.
        var known = latest.TryGetValue(name, out var current);
        if (!known || string.CompareOrdinal(offered.Year, current.Year) > 0)
            latest[name] = (known ? current.Name : name, offered.Year, offered.Absence);
    }

    private static (string Year, double? Absence)? LatestYear(List<JsonElement> entries)
        => entries
            .Select(entry => (Year: Scalar(entry, AcademicYearName) ?? string.Empty, Absence: Number(Scalar(entry, OverallAbsenceName))))
            .Where(entry => entry.Absence is not null)
            .OrderByDescending(entry => entry.Year, StringComparer.Ordinal)
            .Select(entry => ((string Year, double? Absence)?)entry)
            .FirstOrDefault();

    private static IEnumerable<JsonElement> TrustAttendance(string? trustRecord)
    {
        if (string.IsNullOrWhiteSpace(trustRecord))
            return [];

        return RecordStartRegex().Split(trustRecord)
            .Select(ReadFields)
            .SelectMany(fields => Entries(FieldValue(fields, TrustAttendanceField)));
    }

    // A JSON array's objects, cloned so they outlive the document; a value that isn't one gives none.
    private static List<JsonElement> Entries(string? json)
    {
        if (json is null)
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? [.. document.RootElement.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object).Select(entry => entry.Clone())]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // A figure in a results object, e.g. {"reading_writing_maths_expected_standard": "58%"}; null when it isn't a number.
    private static double? Figure(string? json, string name)
    {
        if (json is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Number(Scalar(document.RootElement, name)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A property written as a string ("46.2", "202324") or a number (46.2).
    private static string? Scalar(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : Text(element, name);

    // "58%", "47.3" or "-0.12"; anything else, e.g. "SUPP", isn't a figure.
    private static double? Number(string? value)
        => value is not null && double.TryParse(value.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
