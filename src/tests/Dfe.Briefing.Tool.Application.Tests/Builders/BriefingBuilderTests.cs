using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class BriefingBuilderTests
{
    private static readonly AgentDefinition Establishment = new(AgentNames.Establishment, SystemPromptKey.Establishment);
    private static readonly AgentDefinition Trust = new(AgentNames.Trust, SystemPromptKey.Trust);

    private readonly IPromptProvider _promptProvider = Substitute.For<IPromptProvider>();
    private readonly BriefingBuilder _sut;

    public BriefingBuilderTests() => _sut = new BriefingBuilder(_promptProvider);

    private static AgentResult Result(string agentName, string output)
        => new() { AgentName = agentName, Output = output, TotalTokens = 10 };

    [Fact]
    public void BuildNoEvidenceResult_ForTrustAgent_ReturnsNoTrustInformationFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(Trust, new BriefingParameters("TR12345", "Test Trust"));

        Assert.Contains("No trust information found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_ForEstablishmentAgent_ReturnsNoSchoolOrAcademyFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(Establishment, new BriefingParameters("123456", "Test Academy"));

        Assert.Contains("No school or academy found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_StartsWithAMarkdownHeadingOfTheSubjectName()
    {
        var result = _sut.BuildNoEvidenceResult(Establishment, new BriefingParameters("123456", "Smith & Jones Academy"));

        Assert.StartsWith("# Smith & Jones Academy", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileContext_JoinsEachResult_WithItsDisplayNameAndOutput()
    {
        var context = _sut.CompileContext([Result(AgentNames.Establishment, "establishment-output"), Result(AgentNames.Trust, "trust-output")]);

        Assert.Contains($"AGENT: {AgentNames.Establishment.ToDisplayName()}", context, StringComparison.Ordinal);
        Assert.Contains("establishment-output", context, StringComparison.Ordinal);
        Assert.Contains($"AGENT: {AgentNames.Trust.ToDisplayName()}", context, StringComparison.Ordinal);
        Assert.Contains("trust-output", context, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithNoCity_OmitsTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy"), city: null);

        Assert.DoesNotContain("located in or near", instruction, StringComparison.Ordinal);
        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithACity_IncludesTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy"), city: "Coventry");

        Assert.Contains("located in or near Coventry", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_AsksForLocalAuthorityAndLocalNewsFirst()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy"), city: "Coventry");

        Assert.Contains("local authority", instruction, StringComparison.Ordinal);
        Assert.Contains("local news", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWebSearchAgentSpec_UsesTheWebSearchSystemPrompt_AndTheGivenTools()
    {
        _promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch).Returns("web-search-system-prompt");
        IReadOnlyList<AgentTool> tools = [];

        var spec = _sut.BuildWebSearchAgentSpec(tools);

        Assert.Equal(AgentNames.WebSearch, spec.Name);
        Assert.Equal("web-search-system-prompt", spec.Instructions);
        Assert.Same(tools, spec.Tools);
    }

    [Fact]
    public void AppendNewsFindings_AddsTheWebSearchBulletsAsAMoreBackgroundInformationSection_KeepingTheirFullLinks()
    {
        const string briefing = "# Test Academy\n\n## Financial health\n\n- No financial health assessment is available.\n";
        var webSearch = Result(AgentNames.WebSearch,
            "## Web search findings\n\n- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n- 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)");

        var result = _sut.AppendNewsFindings(briefing, webSearch);

        Assert.StartsWith(briefing.TrimEnd(), result, StringComparison.Ordinal);
        Assert.EndsWith("\n\n## More background information\n\n### News and media coverage\n\n- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n- 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)\n", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Web search findings", result, StringComparison.Ordinal);
        Assert.True(_sut.HasNewsSection(result));
        Assert.False(_sut.HasNewsSection(briefing));
    }

    [Fact]
    public void RemoveEmptySections_RemovesHeadingsWithNothingUnderThem_AndKeepsTheRest()
    {
        const string briefing = "# Trust\n\n## Pupil characteristics\n\n- A: 10%\n\n## Attendance\n\n## Concerns and support\n\nNone found.\n\n## Empty at end\n";

        var result = _sut.RemoveEmptySections(briefing)!;

        Assert.DoesNotContain("## Attendance", result, StringComparison.Ordinal);
        Assert.DoesNotContain("## Empty at end", result, StringComparison.Ordinal);
        Assert.Contains("## Pupil characteristics\n\n- A: 10%", result, StringComparison.Ordinal);
        Assert.Contains("## Concerns and support\n\nNone found.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendNewsFindings_OrdersFindingsMostRecentFirst_WithUndatedOnesLast()
    {
        var webSearch = Result(AgentNames.WebSearch,
            "- Date not given: C. [C](https://c.org/c)\n- 10 June 2026: B. [B](https://b.org/b)\n- 24 September 2026: A. [A](https://a.org/a)");

        var result = _sut.AppendNewsFindings("# Test Academy", webSearch);

        Assert.True(result.IndexOf("24 September", StringComparison.Ordinal) < result.IndexOf("10 June", StringComparison.Ordinal));
        Assert.True(result.IndexOf("10 June", StringComparison.Ordinal) < result.IndexOf("Date not given", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendNewsFindings_WithNoFindings_SaysNoCoverageWasFound()
    {
        var webSearch = Result(AgentNames.WebSearch, "## Web search findings\n\nNo current web information was found for this school or trust.");

        var result = _sut.AppendNewsFindings("# Test Academy", webSearch);

        Assert.EndsWith($"## More background information\n\n### News and media coverage\n\n- {ErrorMessages.NoNewsCoverageFound}\n", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Trust\n\n## Attendance\n\n- A: 5%\n\n## More background information\n\n1. Website\n   - https://a.org/\n\n## Concerns and support\n\nNone found.\n")]
    [InlineData("# Trust\n\n## Attendance\n\n- A: 5%\n\n## More background information\n\n### News and media coverage\n\n- 1 May 2026: News. [A](https://a.org/x)\n")]
    public void RemoveBackgroundSection_RemovesTheWholeSection_AndKeepsTheRest(string briefing)
    {
        var result = _sut.RemoveBackgroundSection(briefing)!;

        Assert.DoesNotContain("More background information", result, StringComparison.Ordinal);
        Assert.DoesNotContain("a.org", result, StringComparison.Ordinal);
        Assert.Contains("## Attendance\n\n- A: 5%", result, StringComparison.Ordinal);
        Assert.Equal(briefing.Contains("## Concerns", StringComparison.Ordinal), result.Contains("## Concerns and support\n\nNone found.", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSubjectInstruction_IncludesTheSchoolOrTrustNameAndUrnOrTrn()
    {
        var instruction = _sut.BuildSubjectInstruction(new BriefingParameters("123456", "Test Academy"));

        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }
}
