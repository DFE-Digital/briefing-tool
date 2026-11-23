using System.Text.RegularExpressions;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Takes the personal details that can be recognised by their form out of the Rise and Recast concerns before the agent
/// sees them: unique pupil numbers (UPNs), dates of birth, email addresses and phone numbers.
/// </summary> 
internal static partial class ConcernRedactor
{
    /// <summary>What a personal detail is replaced with.</summary>
    public const string Removed = "(removed)";

    /// <summary><paramref name="concerns"/>, with every UPN, date of birth, email address and phone number replaced.</summary>
    public static ContextResult Redact(ContextResult concerns)
    {
        ArgumentNullException.ThrowIfNull(concerns);
        if (!concerns.HasEvidence)
            return concerns;

        var text = UpnRegex().Replace(concerns.Text, Removed);
        text = DateOfBirthRegex().Replace(text, "${label}" + Removed);
        text = EmailRegex().Replace(text, Removed);
        text = PhoneRegex().Replace(text, Removed);
        return text == concerns.Text ? concerns : concerns with { Text = text };
    }

    // A letter and 12 characters: 11 digits, then a digit or, for a temporary UPN, a letter (e.g. "A900001250017").
    [GeneratedRegex(@"\b[A-Z]\d{11}[0-9A-Z]\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UpnRegex();

    // A date straight after "DOB", "D.O.B.", "date of birth" or "born (on)", written as 2012-03-04, 04/03/2012 or
    // 4 March 2012; only the date is replaced.
    [GeneratedRegex(@"(?<label>\b(?:d\.?o\.?b\.?|date\s+of\s+birth|born(?:\s+on)?)(?![a-z])[ \t]*[:\-]?[ \t]*)(?<date>\d{4}-\d{2}-\d{2}|\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}|\d{1,2}(?:st|nd|rd|th)?\s+[a-z]+\s+\d{4})",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DateOfBirthRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailRegex();

    // A UK number: 0 or +44 (with an optional "(0)"), then 9 or 10 more digits, which may be spaced or hyphenated.
    [GeneratedRegex(@"(?<![\w+])(?:\+44[ \t]?(?:\(0\)[ \t]?)?|0)\d(?:[ \t-]?\d){8,9}(?!\w)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhoneRegex();
}
