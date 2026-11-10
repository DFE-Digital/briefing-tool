using Dfe.Briefing.Tool.Application.Providers;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Providers;

public sealed class BriefingAgentsTests
{
    private static AgentResult Result(string agentName, string output)
        => new() { AgentName = agentName, Output = output, TotalTokens = 10 };

    [Fact]
    public void All_EveryAgent_HasAValidateFunction()
        => Assert.All(BriefingAgents.All, definition => Assert.NotNull(definition.Validate));

    [Theory]
    [InlineData(null, "Output must not be empty.")]
    [InlineData("", "Output must not be empty.")]
    [InlineData("   ", "Output must not be empty.")]
    [InlineData("<h1>Heading</h1><p>Some HTML text</p>", "Output must be valid Markdown, starting with a heading.")]
    [InlineData("Plain text with no heading", "Output must be valid Markdown, starting with a heading.")]
    [InlineData("# Test Academy\n\nContent", null)]
    public void Validate_PassesOnlyMarkdownStartingWithAHeading_ForEveryAgent(string? output, string? failure)
    {
        foreach (var definition in BriefingAgents.All)
            Assert.Equal(failure, definition.Validate!(Result(definition.Name, output!)));
    }
}
