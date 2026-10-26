using System.Globalization;

namespace Dfe.Briefing.Tool.Application.Extensions;

public static class AgentNameExtensions
{
    private const string AgentSuffix = "-agent";

    /// <summary>
    /// An agent's name as a reader sees it, e.g. "rise-concerns-agent" becomes "Rise Concerns". Used in progress
    /// messages and to label each agent's output in the Synthesis prompt.
    /// </summary>
    public static string ToDisplayName(this string agentName)
    {
        var name = agentName.EndsWith(AgentSuffix, StringComparison.OrdinalIgnoreCase)
            ? agentName[..^AgentSuffix.Length]
            : agentName;

        return string.Join(' ', name
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word)));
    }
}
