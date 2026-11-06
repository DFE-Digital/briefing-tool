using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Provides operations that aggregate academy records into trust-level counts by phase, latest Ofsted inspection
/// outcome, report card area grades, and local authority.
/// </summary>
/// <remarks>Academies are counted once per trust by grouping records by URN; a record with only a name joins the
/// academy another record gives that name with its URN. The latest inspection is taken per academy, reconciling
/// records that share its date; report card grades are handled separately from overall grades, and missing or invalid
/// data is normalized to standard fallback outcomes.</remarks>
internal static partial class AcademyCounter
{
    public const string NotAvailable = "Not available";
    public const string NotJudged = "Not judged";
    public const string ReportCard = "Report card";

    // Graded inspections (before September 2024), most serious first.
    private static readonly string[] OverallGrades = ["Inadequate", "Requires improvement", "Good", "Outstanding"];

    // Report card grades (from November 2025), given to each area, lowest first.
    private static readonly string[] ReportCardGrades = ["Urgent improvement", "Needs attention", "Expected standard", "Strong standard", "Exceptional"];

    // After the overall grades (and any other wording the records use for one) come the outcomes that aren't one.
    private static readonly string[] NotOverallGrades = [ReportCard, NotJudged, NotAvailable];

    /// <summary>
    /// Counts the academies in a trust's academies' records, each academy once however many records it has.
    /// </summary>
    /// <param name="academyRecords">The academies' records, as the search gives them.</param>
    /// <returns>The counts, one per line, or <see langword="null"/> when the records hold no academy.</returns>
    public static string? Count(string? academyRecords)
    {
        var academies = ReadAcademies(academyRecords);
        if (academies.Count == 0)
            return null;

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"academies_counted: {academies.Count}\n");
        text.Append("by_phase: ").Append(Tally(academies.Select(academy => academy.Phase), ByCountThenName)).Append('\n');
        text.Append("by_latest_ofsted_inspection: ")
            .Append(Tally(academies.Select(academy => academy.LatestInspection.Outcome), ByOverallSeverity)).Append('\n');

        // An academy is counted once under each grade its latest report card gives any of its areas.
        var reportCardGrades = academies.SelectMany(academy => academy.LatestInspection.ReportCardGrades.Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
        if (reportCardGrades.Count > 0)
            text.Append("report_card_academies_with_an_area_graded: ").Append(Tally(reportCardGrades, ByReportCardGrade)).Append('\n');

        text.Append("by_local_authority: ").Append(Tally(academies.Select(academy => academy.LocalAuthority), ByCountThenName));
        return text.ToString();
    }

    private sealed record Academy(string Phase, LatestInspection LatestInspection, string LocalAuthority);

    // Outcome is an overall grade, "Report card", "Not judged" or "Not available"; ReportCardGrades are a report
    // card's area grades.
    private sealed record LatestInspection(string Outcome, IReadOnlyList<string> ReportCardGrades);

    private sealed class AcademyFields
    {
        public string? Phase { get; set; }
        public string? LocalAuthority { get; set; }
        public List<Inspection> Inspections { get; } = [];
    }

    private sealed record Inspection(DateOnly Date, string? OverallEffectiveness, IReadOnlyList<string> ReportCardGrades);

    // The records often hold several about the same academy, so they're grouped by URN, taking each field from the first
    // record that gives it, and every inspection from all of them. A record with a name but no URN joins the academy
    // another record gives that name with its URN, so it isn't counted again; a name given with two different URNs
    // (two academies with one name) can't be told apart, so such a record is grouped by its name alone.
    private static List<Academy> ReadAcademies(string? academyRecords)
    {
        if (string.IsNullOrWhiteSpace(academyRecords))
            return [];

        var records = RecordStartRegex().Split(academyRecords).Select(ReadFields).ToList();
        var urnsByName = records
            .Where(fields => Value(fields, "establishment_urn") is not null && Value(fields, "establishment_name") is not null)
            .GroupBy(fields => Value(fields, "establishment_name")!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Urns: group.Select(fields => Value(fields, "establishment_urn")!).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(name => name.Urns.Count == 1)
            .ToDictionary(name => name.Name, name => name.Urns[0], StringComparer.OrdinalIgnoreCase);

        var academies = new Dictionary<string, AcademyFields>(StringComparer.OrdinalIgnoreCase);
        foreach (var fields in records)
        {
            var name = Value(fields, "establishment_name");
            var key = Value(fields, "establishment_urn") ?? (name is not null && urnsByName.TryGetValue(name, out var urn) ? urn : name);
            if (key is null)
                continue;

            if (!academies.TryGetValue(key, out var academy))
                academies[key] = academy = new AcademyFields();

            academy.Phase ??= Value(fields, "phase_of_education");
            academy.LocalAuthority ??= Value(fields, "local_authority_name");
            academy.Inspections.AddRange(ReadInspections(Value(fields, "ofsted_all_inspections")));
        }

        return academies.Values
            .Select(academy => new Academy(academy.Phase ?? NotAvailable, Latest(academy.Inspections), academy.LocalAuthority ?? NotAvailable))
            .ToList();
    }

    private static Dictionary<string, string> ReadFields(string record)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in FieldRegex().Matches(record))
            fields.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Trim());
        return fields;
    }

    private static string? Value(Dictionary<string, string> fields, string name)
        => fields.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    // A report card grade anywhere in the inspection makes it a report card, even beside an overall grade, so the two
    // systems' grades are never counted together.
    //
    // Several records can give the latest inspection (the same date), so they're reconciled rather than one picked by
    // the order they came in: a report card in any of them makes it a report card, with every area grade they give; a
    // record without an overall grade doesn't hide one another record gives; and where they give different overall
    // grades, the most serious is counted, so a concern is never hidden by a duplicate record.
    private static LatestInspection Latest(List<Inspection> inspections)
    {
        if (inspections.Count == 0)
            return new LatestInspection(NotAvailable, []);

        var latestDate = inspections.Max(inspection => inspection.Date);
        var latest = inspections.Where(inspection => inspection.Date == latestDate).ToList();

        var reportCardGrades = latest.SelectMany(inspection => inspection.ReportCardGrades).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (reportCardGrades.Count > 0)
            return new LatestInspection(ReportCard, reportCardGrades);

        var overall = latest.Select(inspection => inspection.OverallEffectiveness).OfType<string>()
            .OrderBy(OverallRank).ThenBy(grade => grade, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return new LatestInspection(overall ?? NotJudged, []);
    }

    // ofsted_all_inspections is a JSON array, e.g. [{"inspection_date": "2023-03-14", "overall_effectiveness": "Good"}].
    // A value that isn't one gives no inspections rather than failing the briefing.
    private static List<Inspection> ReadInspections(string? json)
    {
        if (json is null)
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return document.RootElement.EnumerateArray()
                .Where(inspection => inspection.ValueKind == JsonValueKind.Object)
                .Select(inspection => new Inspection(InspectionDate(inspection), Text(inspection, "overall_effectiveness"), FindReportCardGrades(inspection)))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // A report card's areas could be named anything, or nested, so every value in the inspection is checked for one
    // of its grades, in the records' own wording.
    private static List<string> FindReportCardGrades(JsonElement element)
    {
        var grades = new List<string>();
        Collect(element);
        return grades;

        void Collect(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                        Collect(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                        Collect(item);
                    break;
                case JsonValueKind.String when value.GetString()?.Trim() is { } text && IsReportCardGrade(text):
                    grades.Add(text);
                    break;
            }
        }
    }

    private static bool IsReportCardGrade(string text)
        => ReportCardGrades.Contains(text, StringComparer.OrdinalIgnoreCase);

    // An inspection with no date, or one that can't be read, counts as the oldest.
    private static DateOnly InspectionDate(JsonElement inspection)
        => DateOnly.TryParseExact(Text(inspection, "inspection_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;

    // "Primary 2, Secondary 1": each value in the records' own wording, grouped whatever its case.
    private static string Tally(IEnumerable<string> values, Func<IEnumerable<(string Value, int Count)>, IEnumerable<(string Value, int Count)>> order)
    {
        var counts = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Value: group.First(), Count: group.Count()));
        return string.Join(", ", order(counts).Select(count => string.Create(CultureInfo.InvariantCulture, $"{count.Value} {count.Count}")));
    }

    private static IEnumerable<(string Value, int Count)> ByCountThenName(IEnumerable<(string Value, int Count)> counts)
        => counts.OrderBy(count => count.Value == NotAvailable).ThenByDescending(count => count.Count).ThenBy(count => count.Value, StringComparer.OrdinalIgnoreCase);

    // Overall grades most serious first; then any other wording the records use for one; then a report card, "Not
    // judged" and "Not available", which aren't overall grades.
    private static IEnumerable<(string Value, int Count)> ByOverallSeverity(IEnumerable<(string Value, int Count)> counts)
        => counts.OrderBy(count => OverallRank(count.Value)).ThenBy(count => count.Value, StringComparer.OrdinalIgnoreCase);

    private static int OverallRank(string outcome)
    {
        var grade = IndexOf(OverallGrades, outcome);
        if (grade >= 0)
            return grade;

        var notGrade = IndexOf(NotOverallGrades, outcome);
        return notGrade >= 0 ? OverallGrades.Length + 1 + notGrade : OverallGrades.Length;
    }

    private static IEnumerable<(string Value, int Count)> ByReportCardGrade(IEnumerable<(string Value, int Count)> counts)
        => counts.OrderBy(count => IndexOf(ReportCardGrades, count.Value));

    private static int IndexOf(string[] values, string value)
        => Array.FindIndex(values, known => string.Equals(known, value, StringComparison.OrdinalIgnoreCase));

    // A record starts with a line giving its number: "--- establishment_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence \d+ ---|\[Evidence \d+\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordStartRegex();

    [GeneratedRegex(@"^([a-z0-9_]+):[ \t]*(.*?)\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldRegex();
}
