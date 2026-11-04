namespace Dfe.Briefing.Tool.Domain.ValueObjects;

/// <summary>The school or trust a briefing is about, as its record shows it, for the person to confirm.</summary>
/// <param name="UrnOrTrn">The school's URN or the trust's TRN, as the record holds it.</param>
/// <param name="Name">The school or trust name.</param>
/// <param name="IsTrust">Whether it's a trust.</param>
/// <param name="Region">The region it's in, if the record has one.</param>
/// <param name="Website">Its own website, if the record has one.</param>
/// <param name="RecordDate">When the record was last updated at its source, Get information about schools, if it says.</param>
public record BriefingSubject(string UrnOrTrn, string Name, bool IsTrust, string? Region, string? Website = null, DateOnly? RecordDate = null)
{
    public BriefingParameters ToParameters() => new(UrnOrTrn, Name, IsTrust);
}
