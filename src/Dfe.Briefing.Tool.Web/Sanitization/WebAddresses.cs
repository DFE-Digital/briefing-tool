using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Agents often write web addresses without "https://" (e.g. "chorustrust.org/feeds/news.asp?nid=1"), or wrap
/// them in backticks - neither of which becomes a link. This finds every web address in a briefing and gives it
/// its scheme, so each one becomes a link that shows the complete address the reader will be taken to. Only
/// addresses ending in a known web domain are matched, so figures such as "56.7" or abbreviations such as "e.g."
/// are left alone.
/// </summary>
internal static partial class WebAddresses
{
    private const string Address =
        @"(?:https?://)?(?:www\.)?(?:[a-z0-9-]+\.)+(?:co\.uk|org\.uk|gov\.uk|sch\.uk|ac\.uk|nhs\.uk|police\.uk|uk|org|com|net|gov|edu|info|io)\b(?:/[^\s<>()\[\]`""]*[^\s<>()\[\]`"".,;:!?'])?/?";

    /// <summary>Adds "https://" to every bare address in the text, and removes any backticks around an address.</summary>
    public static string WithSchemes(string text)
    {
        var unquoted = QuotedAddressRegex().Replace(text, "$1");
        return BareAddressRegex().Replace(unquoted, "https://$1");
    }

    /// <summary>Whether the whole of the text is a single web address.</summary>
    public static bool IsAddress(string text) => WholeAddressRegex().IsMatch(text.Trim());

    /// <summary>The address with "https://" added if it has no scheme.</summary>
    public static string WithScheme(string address)
    {
        var trimmed = address.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : "https://" + trimmed;
    }

    // Case-sensitive: a typed web address is lower case ("silverdale.chorustrust.org"), whereas a capitalised one
    // ("Education.gov.uk", "GOV.UK") is a publisher's name in a sentence, and must stay as text.
    [GeneratedRegex(@"`(" + Address + ")`")]
    private static partial Regex QuotedAddressRegex();

    // Not preceded by "/" or ":" (already has a scheme, or is inside one), "@" (an email address) or a word character.
    [GeneratedRegex(@"(?<![/\w.@:-])((?:www\.)?(?:[a-z0-9-]+\.)+(?:co\.uk|org\.uk|gov\.uk|sch\.uk|ac\.uk|nhs\.uk|police\.uk|uk|org|com|net|gov|edu|info|io)\b(?:/[^\s<>()\[\]`""]*[^\s<>()\[\]`"".,;:!?'])?/?)")]
    private static partial Regex BareAddressRegex();

    [GeneratedRegex("^" + Address + "$", RegexOptions.IgnoreCase)]
    private static partial Regex WholeAddressRegex();
}
