namespace Dfe.Briefing.Tool.Domain.ValueObjects;

public record BriefingParameters(string SchoolUrnOrTrustTrn, string SchoolOrTrustName)
{
    public bool IsTrust => SchoolUrnOrTrustTrn.Trim().StartsWith("TR", StringComparison.OrdinalIgnoreCase);
}
