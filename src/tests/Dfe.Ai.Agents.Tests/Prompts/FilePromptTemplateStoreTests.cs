using Dfe.Ai.Agents.Prompts;
using Dfe.Ai.Agents.Tests.Constants;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Prompts;

public sealed class FilePromptTemplateStoreTests
{
    [Fact]
    public void GetTemplate_ReturnsFileContent_WhenConfiguredAndReadable()
    {
        var sut = new FilePromptTemplateStore(
            paths: new Dictionary<string, string> { ["greeting"] = "greeting.md" },
            readFile: _ => "Hello from file.",
            fallback: _ => "fallback");

        var result = sut.GetTemplate("greeting");

        Assert.Equal("Hello from file.", result);
    }

    [Fact]
    public void GetTemplate_UsesFallback_WhenKeyNotConfigured()
    {
        var sut = new FilePromptTemplateStore(
            paths: new Dictionary<string, string>(),
            readFile: _ => throw new InvalidOperationException(TestErrorMessages.ShouldNotBeCalled),
            fallback: key => $"fallback for {key}");

        var result = sut.GetTemplate("greeting");

        Assert.Equal("fallback for greeting", result);
    }

    [Fact]
    public void GetTemplate_UsesFallback_WhenFileReadThrows()
    {
        var sut = new FilePromptTemplateStore(
            paths: new Dictionary<string, string> { ["greeting"] = "greeting.md" },
            readFile: _ => throw new FileNotFoundException(),
            fallback: _ => "fallback");

        var result = sut.GetTemplate("greeting");

        Assert.Equal("fallback", result);
    }
}
