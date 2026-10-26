using Dfe.Briefing.Tool.Application.Extensions;
using Dfe.Briefing.Tool.Domain.Constants;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Extensions;

public sealed class AgentNameExtensionsTests
{
    [Theory]
    [InlineData(AgentNames.Establishment, "Establishment")]
    [InlineData(AgentNames.Trust, "Trust")]
    [InlineData(AgentNames.RiseConcerns, "Rise Concerns")]
    [InlineData(AgentNames.RecastConcerns, "Recast Concerns")]
    [InlineData(AgentNames.Synthesis, "Synthesis")]
    [InlineData(AgentNames.WebSearch, "Web Search")]
    [InlineData("no-suffix", "No Suffix")]
    public void ToDisplayName_DropsTheAgentSuffix_AndCapitalisesEachWord(string agentName, string expected)
        => Assert.Equal(expected, agentName.ToDisplayName());
}
