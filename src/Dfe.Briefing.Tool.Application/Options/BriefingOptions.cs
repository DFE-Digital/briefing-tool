namespace Dfe.Briefing.Tool.Application.Options;

public sealed class BriefingOptions
{
    public const string SectionName = "Briefing";

    /// <summary>
    /// The maximum number of evidence records to fetch when searching for information about a school or trust (Default 25).
    /// </summary>
    public int MaxEvidencePerSearch { get; init; } = 25;

    /// <summary>
    /// The maximum number of academies to fetch when searching for information about a trust (Default 100).
    /// </summary>
    public int MaxAcademiesPerTrust { get; init; } = 100;
}
