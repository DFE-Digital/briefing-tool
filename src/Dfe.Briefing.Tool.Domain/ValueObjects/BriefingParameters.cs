namespace Dfe.Briefing.Tool.Domain.ValueObjects;

/// <summary>What a briefing is about.</summary>
/// <param name="SchoolUrnOrTrustTrn">The school's URN or the trust's TRN.</param>
/// <param name="SchoolOrTrustName">The school or trust name.</param>
/// <param name="IsTrust">Whether it's a trust, as the person chose when they started, or a school.</param>
public record BriefingParameters(string SchoolUrnOrTrustTrn, string SchoolOrTrustName, bool IsTrust)
{
    /// <summary>
    /// For a briefing named only by its reference, such as a release gate test case: a TRN starts "TR", and a URN is
    /// all digits. The app's own briefings are given the type the person chose.
    /// </summary>
    public static BriefingParameters FromReference(string schoolUrnOrTrustTrn, string schoolOrTrustName)
        => new(schoolUrnOrTrustTrn, schoolOrTrustName,
            IsTrust: schoolUrnOrTrustTrn.Trim().StartsWith("TR", StringComparison.OrdinalIgnoreCase));
}
