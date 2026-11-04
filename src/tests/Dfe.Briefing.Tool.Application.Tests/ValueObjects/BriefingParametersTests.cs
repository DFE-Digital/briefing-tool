using Dfe.Briefing.Tool.Domain.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.ValueObjects;

public sealed class BriefingParametersTests
{
    [Theory]
    [InlineData("TR01234", true)]
    [InlineData(" tr01234 ", true)]
    [InlineData("123456", false)]
    public void FromReference_TakesTheTypeFromTheReference(string urnOrTrn, bool isTrust)
        => Assert.Equal(isTrust, BriefingParameters.FromReference(urnOrTrn, "Example").IsTrust);

    [Fact]
    public void Subject_GivesItsOwnType_NotOneFromItsReference()
        => Assert.True(new BriefingSubject("123456", "Example Trust", IsTrust: true, Region: null).ToParameters().IsTrust);
}
