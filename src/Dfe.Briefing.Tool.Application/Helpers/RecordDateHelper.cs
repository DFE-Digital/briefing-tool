using System.Globalization;

namespace Dfe.Briefing.Tool.Application.Helpers;

/// <summary>The date a search record was last changed, read from whichever of its date fields it has.</summary>
internal static class RecordDateHelper
{
    // The fields the date may be in, the first one present winning. GIAS calls it the last changed date.
    private static readonly string[] Fields = ["last_changed_date", "date_last_updated", "last_updated", "data_extract_date", "extract_date"];

    // The evidence writes dates as yyyy-mm-dd; GIAS's own exports use dd-mm-yyyy.
    private static readonly string[] Formats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy"];

    // Only the date matters: a value may also have a time, e.g. "2026-10-15T06:00:00Z". Every format is 10 characters.
    private const int DateLength = 10;

    /// <summary>The record's date, or <see langword="null"/> when it has none that can be read.</summary>
    /// <param name="fields">The record's fields, by name.</param>
    public static DateOnly? Read(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var value = Fields.Select(field => fields.GetValueOrDefault(field)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        return value is { Length: >= DateLength }
            && DateOnly.TryParseExact(value[..DateLength], Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
    }
}
