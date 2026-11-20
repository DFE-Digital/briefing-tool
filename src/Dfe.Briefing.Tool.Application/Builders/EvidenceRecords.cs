using System.Globalization;
using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Splits a search's results into its records, each starting with the line that gives its number, and puts them back
/// together with only the sources of the records kept.
/// </summary>
internal static partial class EvidenceRecords
{
    /// <summary>One record: its own number, and its text from the line that starts it.</summary>
    public sealed record Record(int Number, string Text);

    /// <summary>
    /// The text before the first record (e.g. a note from the search), and each record in order; no records when the
    /// text has none.
    /// </summary>
    public static (string Preamble, IReadOnlyList<Record> Records) Split(string text)
    {
        var starts = RecordStartRegex().Matches(text);
        if (starts.Count == 0)
            return (text, []);

        var records = new List<Record>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
            records.Add(new Record(int.Parse(starts[i].Groups["number"].Value, CultureInfo.InvariantCulture), text[starts[i].Index..end].Trim()));
        }

        return (text[..starts[0].Index].Trim(), records);
    }

    /// <summary>
    /// <paramref name="result"/> holding only <paramref name="kept"/>, in their order, after its preamble; a source
    /// stays unless its record was left out.
    /// </summary>
    public static ContextResult Rebuild(ContextResult result, string preamble, IReadOnlyList<Record> all, IReadOnlyList<Record> kept)
    {
        var dropped = all.Select(record => record.Number).Except(kept.Select(record => record.Number)).ToHashSet();
        var texts = kept.Select(record => record.Text);
        return result with
        {
            Text = string.Join("\n\n", preamble.Length > 0 ? texts.Prepend(preamble) : texts),
            Sources = [.. result.Sources.Where(source => !dropped.Contains(source.Number))]
        };
    }

    /// <summary>
    /// <paramref name="result"/> without any record whose every field is given, with the same value, by another of its
    /// records - a repeat, or a less complete copy, of a record it keeps. Nothing a record gives is lost: a record is
    /// only left out when another holds all of it.
    /// </summary>
    /// <param name="result">The records.</param>
    /// <param name="keyField">
    /// The field that names what a record is about, e.g. establishment_urn: only records with the same value are
    /// compared, and a record without one is always kept. When null, every record is compared with every other.
    /// </param>
    public static ContextResult WithoutDuplicates(ContextResult result, string? keyField)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasEvidence)
            return result;

        var (preamble, records) = Split(result.Text);
        if (records.Count < 2)
            return result;

        var fields = records.Select(record => Fields(record.Text)).ToList();
        var kept = records.Where((_, i) => !IsDuplicate(fields, i, keyField)).ToList();
        return kept.Count == records.Count ? result : Rebuild(result, preamble, records, kept);
    }

    // A record is a duplicate when another with the same key holds all of its fields: a fuller one, or (for an exact
    // repeat) the first of them, so one copy is always kept.
    private static bool IsDuplicate(List<Dictionary<string, string>> fields, int index, string? keyField)
    {
        var record = fields[index];
        if (record.Count == 0 || (keyField is not null && !record.ContainsKey(keyField)))
            return false;

        return fields.Where((other, i) => i != index
                && (keyField is null || string.Equals(other.GetValueOrDefault(keyField), record[keyField], StringComparison.OrdinalIgnoreCase))
                && (other.Count > record.Count || i < index))
            .Any(other => record.All(field => other.TryGetValue(field.Key, out var value) && string.Equals(value, field.Value, StringComparison.Ordinal)));
    }

    /// <summary>A record's fields, one per "name: value" line; the line that starts it has none.</summary>
    public static Dictionary<string, string> Fields(string record)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var groups in FieldRegex().Matches(record).Select(match => match.Groups))
            fields[groups["name"].Value] = groups["value"].Value;
        return fields;
    }

    // A record starts with a line giving its number: "--- rise_concerns_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"^(?:---[^\r\n]*\bEvidence (?<number>\d+) ---|\[Evidence (?<number>\d+)\])\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RecordStartRegex();

    [GeneratedRegex(@"^(?<name>[a-z0-9_]+):[ \t]*(?<value>.*?)[ \t]*\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldRegex();
}
