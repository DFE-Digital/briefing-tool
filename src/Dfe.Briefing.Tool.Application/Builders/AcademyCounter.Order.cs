using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private const string AttendanceLatestField = "attendance_latest";
    private const string AttendanceHistoricField = "attendance_historic";
    private const string TrustAttendanceField = "academy_attendance";
    private const string TrustEstablishmentAttendanceField = "establishment_attendance";
    private const string Ks2ResultName = "reading_writing_maths_expected_standard";
    private const string Ks4ScoreName = "attainment_8_score";
    private const string AcademicYearName = "academic_year";
    private const string OverallAbsenceName = "overall_absence";
    private const string PctOverallAbsenceName = "pct_overall_absence";
    private const string AcademyName = "academy";

    // Where a year's results give the school's figure: the percentage meeting the expected standard in reading, writing
    // and maths, and the Attainment 8 average across all subjects.
    private static readonly string[] Ks2ResultPath = ["key_stage_2_measure", "expected_standard", "reading_writing_maths", "all_pupils", "school"];
    private static readonly string[] Ks4ScorePath = ["key_stage_4_measure", "attainment8_average", "total_average", "school"];

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
            OfstedOrder(academies),
            HighestFirst(academies.Select(academy => (academy.Name, Figure(academy.Ks2Result, Ks2ResultName, Ks2ResultPath)))),
            HighestFirst(academies.Select(academy => (academy.Name, Figure(academy.Ks4Score, Ks4ScoreName, Ks4ScorePath)))),
            HighestFirst(Absences(academies, trustRecord)));
    }

    // None when any academy's inspections can't be read, so the agent's own order is kept rather than one that puts
    // that academy last as never inspected.
    private static List<string> OfstedOrder(List<Academy> academies)
        => academies.TrueForAll(static academy => academy.InspectionsReadable)
            ? [.. academies
                .OrderBy(academy => OverallRank(academy.LatestInspection.Outcome))
                .ThenBy(academy => ReportCardRank(academy.LatestInspection))
                .ThenBy(academy => academy.Name, StringComparer.OrdinalIgnoreCase)
                .Select(academy => academy.Name)]
            : [];

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
    /// <c>establishment_attendance</c> (or <c>academy_attendance</c>) where that gives a later year or the academy's
    /// record gives none.
    /// </summary>
    private static IEnumerable<(string Name, double? Figure)> Absences(List<Academy> academies, string? trustRecord)
    {
        var latest = new Dictionary<string, (string Name, string Year, double? Absence)>(StringComparer.OrdinalIgnoreCase);
        foreach (var academy in academies)
        {
            OfferAbsence(latest, academy.Name, LatestAbsence(JsonObject(academy.AttendanceLatest),
                Entries(academy.Attendance).Concat(ByYear(JsonObject(academy.AttendanceHistoric)))));
        }

        foreach (var (name, absence) in TrustEstablishmentAttendance(trustRecord, academies))
            OfferAbsence(latest, name, absence);

        foreach (var entry in TrustFields(trustRecord, TrustAttendanceField).SelectMany(Entries))
        {
            if (Text(entry, AcademyName) is { } name)
                OfferAbsence(latest, name, (Scalar(entry, AcademicYearName) ?? string.Empty, OverallAbsence(entry)));
        }

        return latest.Values.Select(value => (value.Name, value.Absence));
    }

    // attendance_latest gives the latest figures with no year, e.g. {"pct_overall_absence": "3.76%"}, so it's taken as
    // the year after the latest of the yearly figures: attendance_historic, keyed by year, e.g. {"202324":
    // {"pct_overall_absence": "5.96%"}}, or school_attendance, a list, e.g. [{"academic_year": "202324", "overall_absence": "5.0%"}].
    private static (string Year, double? Absence)? LatestAbsence(JsonElement? latest, IEnumerable<JsonElement> byYear)
    {
        var latestYear = LatestYear(byYear);
        if (latest is { } figures && OverallAbsence(figures) is { } absence)
            return (latestYear is { } previous ? NextAcademicYear(previous.Year) : string.Empty, absence);

        return latestYear;
    }

    // The trust record's establishment_attendance: each academy's figures keyed by its name and URN, e.g.
    // {"Example Academy (149322)": {"attendance_latest": {...}, "attendance_historic": {"202324": {...}}}}, and {} for
    // one with none. Each is given the name the academy's own records use, found by its URN.
    private static IEnumerable<(string Name, (string Year, double? Absence)? Absence)> TrustEstablishmentAttendance(string? trustRecord,
        List<Academy> academies)
        => TrustFields(trustRecord, TrustEstablishmentAttendanceField)
            .Select(JsonObject)
            .OfType<JsonElement>()
            .SelectMany(static byAcademy => byAcademy.EnumerateObject())
            .Where(static academy => academy.Value.ValueKind == JsonValueKind.Object)
            .Select(academy => (AcademyNamed(academy.Name, academies),
                LatestAbsence(ObjectProperty(academy.Value, AttendanceLatestField), ByYear(ObjectProperty(academy.Value, AttendanceHistoricField)))));

    // "Example Academy (149322)": the academy with that URN, or failing that the name it gives.
    private static string AcademyNamed(string nameAndUrn, List<Academy> academies)
    {
        var match = NameAndUrnRegex().Match(nameAndUrn);
        if (!match.Success)
            return nameAndUrn.Trim();

        var urn = match.Groups["urn"].Value;
        return academies.Find(academy => string.Equals(academy.Key, urn, StringComparison.OrdinalIgnoreCase))?.Name ?? match.Groups["name"].Value;
    }

    private static JsonElement? ObjectProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static IEnumerable<string> TrustFields(string? trustRecord, string field)
        => string.IsNullOrWhiteSpace(trustRecord)
            ? []
            : RecordStartRegex().Split(trustRecord).Select(ReadFields).Select(fields => FieldValue(fields, field)).OfType<string>();

    // An object keyed by academic year, as entries giving their year, e.g. {"academic_year": "202324", ...}.
    private static IEnumerable<JsonElement> ByYear(JsonElement? byYearOrNone)
        => byYearOrNone is { } byYear
            ? byYear.EnumerateObject()
                .Where(static year => IsAcademicYear(year.Name) && year.Value.ValueKind == JsonValueKind.Object)
                .Select(static year => WithYear(year.Name, year.Value))
            : [];

    private static JsonElement WithYear(string year, JsonElement figures)
    {
        var entry = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [AcademicYearName] = JsonSerializer.SerializeToElement(year) };
        foreach (var figure in figures.EnumerateObject())
            entry.TryAdd(figure.Name, figure.Value.Clone());
        return JsonSerializer.SerializeToElement(entry);
    }

    private static double? OverallAbsence(JsonElement entry)
        => Number(Scalar(entry, PctOverallAbsenceName) ?? Scalar(entry, OverallAbsenceName));

    // "202324" is 2023/24: six digits whose years follow on.
    private static bool IsAcademicYear(string text)
        => text.Length == 6 && text.All(char.IsAsciiDigit)
           && (int.Parse(text[..4], CultureInfo.InvariantCulture) + 1) % 100 == int.Parse(text[4..], CultureInfo.InvariantCulture);

    private static string NextAcademicYear(string year)
    {
        if (!IsAcademicYear(year))
            return string.Empty;

        var start = int.Parse(year[..4], CultureInfo.InvariantCulture) + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{start}{(start + 1) % 100:00}");
    }

    private static void OfferAbsence(Dictionary<string, (string Name, string Year, double? Absence)> latest, string name, (string Year, double? Absence)? entry)
    {
        if (entry is not { Absence: not null } offered)
            return;

        // The academy keeps the name it was first given, so the same academy is listed once however its records write it.
        var known = latest.TryGetValue(name, out var current);
        if (!known || string.CompareOrdinal(offered.Year, current.Year) > 0)
            latest[name] = (known ? current.Name : name, offered.Year, offered.Absence);
    }

    private static (string Year, double? Absence)? LatestYear(IEnumerable<JsonElement> entries)
        => entries
            .Select(entry => (Year: Scalar(entry, AcademicYearName) ?? string.Empty, Absence: OverallAbsence(entry)))
            .Where(entry => entry.Absence is not null)
            .OrderByDescending(entry => entry.Year, StringComparer.Ordinal)
            .Select(entry => ((string Year, double? Absence)?)entry)
            .FirstOrDefault();

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

    // A result: named in a flat object, e.g. {"reading_writing_maths_expected_standard": "58%"}; or, in an object keyed
    // by academic year, the school's figure at the path in the latest year, e.g. {"202425": {"key_stage_4_measure":
    // {"attainment8_average": {"total_average": {"school": "56", "national": "46.1"}}}}}. Null when it isn't a number,
    // e.g. "Data not applicable".
    private static double? Figure(string? json, string flatName, string[] path)
    {
        if (JsonObject(json) is not { } result)
            return null;

        if (result.TryGetProperty(flatName, out _))
            return Number(Scalar(result, flatName));

        return result.EnumerateObject()
            .Where(static year => IsAcademicYear(year.Name))
            .OrderByDescending(static year => year.Name, StringComparer.Ordinal)
            .Select(year => Number(At(year.Value, path)))
            .FirstOrDefault();
    }

    private static string? At(JsonElement element, string[] path)
    {
        var value = element;
        foreach (var name in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value))
                return null;
        }

        return ScalarValue(value);
    }

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*\((?<urn>\d+)\)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NameAndUrnRegex();

    // A JSON object, cloned so it outlives the document; null for a value that isn't one.
    private static JsonElement? JsonObject(string? json)
    {
        if (json is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Scalar(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? ScalarValue(value) : null;

    // A value written as a string ("46.2", "202324") or a number (46.2).
    private static string? ScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String when value.GetString()?.Trim() is { Length: > 0 } text => text,
        _ => null,
    };

    // "58%", "47.3" or "-0.12"; anything else, e.g. "SUPP", isn't a figure.
    private static double? Number(string? value)
        => value is not null && double.TryParse(value.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
