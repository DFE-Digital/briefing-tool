using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Application.Helpers;

/// <summary>
/// Removes a <c>{{city: ...}}</c> location marker from the briefing agent's answer. The agent is no longer asked for one -
/// the town comes from the records in code (see <c>TownFinder</c>) - but one it writes anyway is never shown.
/// </summary>
internal static partial class CityExtractorHelper
{
    /// <summary>The answer without any <c>{{city: ...}}</c> marker.</summary>
    public static string? RemoveCityMarker(string? output)
        => string.IsNullOrEmpty(output) || !CityMarkerRegex().IsMatch(output)
            ? output
            : CityMarkerRegex().Replace(output, string.Empty).TrimEnd();

    [GeneratedRegex(@"\{\{\s*city:\s*(.*?)\s*\}\}", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex CityMarkerRegex();
}
