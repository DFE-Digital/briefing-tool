namespace Dfe.Briefing.Tool.Web.Constants;

/// <summary>What a briefing can be about, as chosen on the Briefing/Type page and passed to Briefing/Find.</summary>
public static class SubjectTypes
{
    public const string Establishment = "establishment";
    public const string Trust = "trust";

    public static bool IsValid(string? type) => type is Establishment or Trust;
}
