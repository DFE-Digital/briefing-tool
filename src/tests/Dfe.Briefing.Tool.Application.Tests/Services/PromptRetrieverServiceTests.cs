using Dfe.Briefing.Tool.Application.Configs;
using Dfe.Briefing.Tool.Application.FileRetrievers;
using Dfe.Briefing.Tool.Application.Services;
using Dfe.Briefing.Tool.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Services;

public sealed class PromptRetrieverServiceTests
{
    private readonly IPromptFileReader _fileReader = Substitute.For<IPromptFileReader>();

    private PromptRetrieverService CreateSut(PromptConfig config)
        => new(_fileReader, config, NullLogger<PromptRetrieverService>.Instance);

    [Fact]
    public void GetSystemPrompt_ReturnsFileContent_WhenConfiguredAndReadable()
    {
        var config = new PromptConfig
        {
            SystemPrompts = { [SystemPromptType.Ofsted] = "Prompts/Systems/Ofsted.md" }
        };
        _fileReader.Read("Prompts/Systems/Ofsted.md").Returns("Real Ofsted instructions.");

        var sut = CreateSut(config);

        var result = sut.GetSystemPrompt(SystemPromptType.Ofsted);

        Assert.StartsWith("Real Ofsted instructions.", result);
    }

    [Fact]
    public void GetSystemPrompt_FallsBackToEmbeddedDescription_WhenNotConfigured()
    {
        var sut = CreateSut(new PromptConfig());

        var result = sut.GetSystemPrompt(SystemPromptType.Ofsted);

        Assert.Contains("Ofsted", result);
        _fileReader.DidNotReceive().Read(Arg.Any<string>());
    }

    [Fact]
    public void GetSystemPrompt_FallsBackToEmbeddedDescription_WhenFileReadThrows()
    {
        var config = new PromptConfig
        {
            SystemPrompts = { [SystemPromptType.Ofsted] = "Prompts/Systems/Ofsted.md" }
        };
        _fileReader.Read(Arg.Any<string>()).Throws(new FileNotFoundException());

        var sut = CreateSut(config);

        var result = sut.GetSystemPrompt(SystemPromptType.Ofsted);

        Assert.Contains("Ofsted", result);
    }

    [Fact]
    public void GetSystemPrompt_AppendsStandardResponseFormat_ForNonSynthesisTypes()
    {
        var sut = CreateSut(new PromptConfig());

        var result = sut.GetSystemPrompt(SystemPromptType.Ofsted);

        Assert.Contains("standardised structured output", result);
    }

    [Fact]
    public void GetSystemPrompt_DoesNotAppendStandardResponseFormat_ForSynthesis()
    {
        var sut = CreateSut(new PromptConfig());

        var result = sut.GetSystemPrompt(SystemPromptType.Synthesis);

        Assert.DoesNotContain("standardised structured output", result);
    }

    [Fact]
    public void GetSystemPrompt_AppendsStandardResponseFormatFromFile_WhenConfigured()
    {
        var config = new PromptConfig
        {
            SystemPrompts =
            {
                [SystemPromptType.Ofsted] = "Prompts/Systems/Ofsted.md",
                ["StandardAgentResponseFormat"] = "Prompts/StandardAgentResponseFormat.md"
            }
        };
        _fileReader.Read("Prompts/Systems/Ofsted.md").Returns("Real Ofsted instructions.");
        _fileReader.Read("Prompts/StandardAgentResponseFormat.md").Returns("File-based response format.");

        var sut = CreateSut(config);

        var result = sut.GetSystemPrompt(SystemPromptType.Ofsted);

        Assert.EndsWith("File-based response format.", result);
        Assert.DoesNotContain("standardised structured output", result);
    }
}
