using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Helpers;

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
    private static readonly string[] ReportCardGradeNames = ["Urgent improvement", "Needs attention", "Expected standard", "Strong standard", "Exceptional"];

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
        public NewestValue Phase { get; set; }
        public NewestValue LocalAuthority { get; set; }
        public List<Inspection> Inspections { get; } = [];
    }

    /// <summary>
    /// A field's value from the newest record that provides it, along with the record's last changed date.
    /// </summary>
    /// <param name="Value">The value of the field from the newest record that provides it.</param>
    /// <param name="Date">The last changed date of the record that provides the value.</param>
    private readonly record struct NewestValue(string? Value, DateOnly Date)
    {
        public NewestValue Offer(string? value, DateOnly date)
            => value is not null && (Value is null || date > Date) ? new NewestValue(value, date) : this;
    }

    private sealed record Inspection(DateOnly Date, string? OverallEffectiveness, IReadOnlyList<string> ReportCardGrades);

    /// <summary>
    /// Reads academy records and returns a list of Academy objects, taking each field from the newest record that provides it and collecting all inspections.
    /// </summary>
    /// <param name="academyRecords"></param>
    /// <returns></returns>
    private static List<Academy> ReadAcademies(string? academyRecords)
    {
        if (string.IsNullOrWhiteSpace(academyRecords))
            return [];

        var records = RecordStartRegex().Split(academyRecords).Select(ReadFields).ToList();
        var urnsByName = records
            .SelectMany(fields => FieldValue(fields, "establishment_name") is { } name && FieldValue(fields, "establishment_urn") is { } urn
                ? new[] { (Name: name, Urn: urn) }
                : [])
            .GroupBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Urns: group.Select(record => record.Urn).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(name => name.Urns.Count == 1)
            .ToDictionary(name => name.Name, name => name.Urns[0], StringComparer.OrdinalIgnoreCase);

        var academies = new Dictionary<string, AcademyFields>(StringComparer.OrdinalIgnoreCase);
        foreach (var fields in records)
        {
            var name = FieldValue(fields, "establishment_name");
            var key = FieldValue(fields, "establishment_urn") ?? (name is not null && urnsByName.TryGetValue(name, out var urn) ? urn : name);
            if (key is null)
                continue;

            if (!academies.TryGetValue(key, out var academy))
                academies[key] = academy = new AcademyFields();

            var recordDate = RecordDateHelper.Read(fields) ?? DateOnly.MinValue;
            academy.Phase = academy.Phase.Offer(FieldValue(fields, "phase_of_education"), recordDate);
            academy.LocalAuthority = academy.LocalAuthority.Offer(FieldValue(fields, "local_authority_name"), recordDate);
            academy.Inspections.AddRange(ReadInspections(FieldValue(fields, "ofsted_all_inspections")));
        }

        return [.. academies.Values
            .Select(academy => new Academy(academy.Phase.Value ?? NotAvailable, Latest(academy.Inspections), academy.LocalAuthority.Value ?? NotAvailable))];
    }

    private static Dictionary<string, string> ReadFields(string record)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var groups in FieldRegex().Matches(record).Select(match => match.Groups))
            fields.TryAdd(groups[1].Value, groups[2].Value.Trim());
        return fields;
    }

    private static string? FieldValue(Dictionary<string, string> fields, string name)
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

            return [.. document.RootElement.EnumerateArray()
                .Where(inspection => inspection.ValueKind == JsonValueKind.Object)
                .Select(inspection => new Inspection(InspectionDate(inspection), Text(inspection, "overall_effectiveness"), FindReportCardGrades(inspection)))];
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
        => ReportCardGradeNames.Contains(text, StringComparer.OrdinalIgnoreCase);

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
        => counts.OrderBy(count => IndexOf(ReportCardGradeNames, count.Value));

    private static int IndexOf(string[] values, string value)
        => Array.FindIndex(values, known => string.Equals(known, value, StringComparison.OrdinalIgnoreCase));

    // A record starts with a line giving its number: "--- establishment_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence \d+ ---|\[Evidence \d+\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordStartRegex();

    [GeneratedRegex(@"^([a-z0-9_]+):[ \t]*(.*?)\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldRegex();
}
