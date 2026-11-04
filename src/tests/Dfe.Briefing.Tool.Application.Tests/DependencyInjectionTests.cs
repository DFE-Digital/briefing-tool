using Dfe.Briefing.Tool.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests;

public sealed class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(string maxEvidencePerSearch)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Briefing:MaxEvidencePerSearch"] = maxEvidencePerSearch })
            .Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddApplication()
            .BuildServiceProvider();
    }

    [Fact]
    public void AddApplication_RegistersBriefingOptions_BoundFromConfiguration()
    {
        using var provider = BuildProvider("5");

        Assert.Equal(5, provider.GetRequiredService<BriefingOptions>().MaxEvidencePerSearch);
    }

    [Fact]
    public void AddApplication_RejectsAnOutOfRangeEvidenceSize()
    {
        using var provider = BuildProvider("0");

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<BriefingOptions>());
    }
}
