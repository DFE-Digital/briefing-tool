using Dfe.Briefing.Tool.Web.Pages;
using Xunit;

namespace Dfe.Briefing.Tool.Web.Tests.Pages;

public sealed class IndexModelTests
{
    [Fact]
    public void IndexModel_CanBeCreated()
    {
        var model = new IndexModel();

        Assert.NotNull(model);
    }
}
