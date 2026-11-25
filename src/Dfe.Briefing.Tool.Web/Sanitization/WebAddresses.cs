using System.Text.RegularExpressions;

namespace Dfe.Briefing.Tool.Web.Sanitization;

/// <summary>
/// Provides utilities for recognizing and normalizing web addresses.
/// </summary>
/// <remarks>Recognizes addresses with supported UK and generic domains. Bare addresses can be normalized by
/// adding "https://"; backticks around addresses can be removed.</remarks>
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
