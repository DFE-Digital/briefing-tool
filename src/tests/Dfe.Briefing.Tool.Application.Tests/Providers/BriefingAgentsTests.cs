using Dfe.Briefing.Tool.Application.Providers;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Providers;

public sealed class BriefingAgentsTests
{
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
            var failure = definition.Validate!(new AgentResult(definition.Name, output!, 10));

            Assert.Equal("Output must not be empty.", failure);
        }
    }

    [Fact]
    public void Validate_MarkdownOrPlainTextOutput_FailsForEveryAgent()
    {
        foreach (var definition in BriefingAgents.All)
        {
            var failure = definition.Validate!(new AgentResult(definition.Name, "# Heading\nSome markdown text", 10));

            Assert.Equal("Output must be valid HTML, not Markdown or plain text.", failure);
        }
    }

    [Fact]
    public void Validate_HtmlOutput_PassesForEveryAgent()
    {
        foreach (var definition in BriefingAgents.All)
        {
            var failure = definition.Validate!(new AgentResult(definition.Name, "<h1>Test Academy</h1><p>Content</p>", 10));

            Assert.Null(failure);
        }
    }
}
