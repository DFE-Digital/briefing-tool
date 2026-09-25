namespace Dfe.Briefing.Tool.Domain.ValueObjects;

public record BriefingParameters(string SchoolUrnOrTrustTrn, string SchoolOrTrustName, bool AllowWebSearch = false);
