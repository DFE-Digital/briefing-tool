using GovUK.Dfe.AI.Agents.ValueObjects;
using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Application.Helpers; 

internal static partial class CityExtractorHelper
{
    public static (AgentResult Result, string? City) ExtractCity(AgentResult result)
    {
        if (string.IsNullOrEmpty(result.Output))
            return (result, null); 

        var match = CityMarkerRegex().Match(result.Output);

        if (!match.Success)
            return (result, null);

        var city = match.Groups[1].Value.Trim();
        var strippedOutput = CityMarkerRegex().Replace(result.Output, string.Empty).TrimEnd();

        return (result with { Output = strippedOutput }, city.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : city
        );
    }

    [GeneratedRegex(@"\{\{\s*city:\s*(.*?)\s*\}\}", RegexOptions.IgnoreCase, "en-GB")]
    private static partial Regex CityMarkerRegex();
}