using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Helpers;

internal static partial class CityExtractorHelper
{
    public static (AgentResult Result, string? City) ExtractCity(AgentResult result)
    {
        var (output, city) = ExtractCity(result.Output);
        return (output == result.Output ? result : result with { Output = output! }, city);
    }

    /// <summary>The answer without its <c>{{city: ...}}</c> marker, and the town or city it gave; null for "none".</summary>
    public static (string? Output, string? City) ExtractCity(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return (output, null);

        var match = CityMarkerRegex().Match(output);
        if (!match.Success)
            return (output, null);

        var city = match.Groups[1].Value.Trim();
        var strippedOutput = CityMarkerRegex().Replace(output, string.Empty).TrimEnd();

        return (strippedOutput, city.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : city);
    }

    [GeneratedRegex(@"\{\{\s*city:\s*(.*?)\s*\}\}", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex CityMarkerRegex();
}
