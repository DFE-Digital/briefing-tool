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
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceOutput_Fails(string? output)
    {
        foreach (var definition in BriefingAgents.All)
        {
            var failure = definition.Validate!(Result(definition.Name, output!));

            Assert.Equal("Output must not be empty.", failure);
        }
    }

    [Fact]
    public void Validate_HtmlOrPlainTextOutput_FailsForEveryAgent()
    {
        foreach (var definition in BriefingAgents.All)
        {
            var failure = definition.Validate!(Result(definition.Name, "<h1>Heading</h1><p>Some HTML text</p>"));

            Assert.Equal("Output must be valid Markdown, starting with a heading.", failure);
        }
    }

    [Fact]
    public void Validate_MarkdownOutput_PassesForEveryAgent()
    {
        foreach (var definition in BriefingAgents.All)
        {
            var failure = definition.Validate!(Result(definition.Name, "# Test Academy\n\nContent"));

            Assert.Null(failure);
        }
    }
}
