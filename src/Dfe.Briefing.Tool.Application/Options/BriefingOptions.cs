namespace Dfe.Briefing.Tool.Application.Options;

public sealed class BriefingOptions
{
    public const string SectionName = "Briefing";

    public int MaxEvidencePerSearch { get; init; } = 3;
}
