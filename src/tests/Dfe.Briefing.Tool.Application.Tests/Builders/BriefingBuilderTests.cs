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
    public void InsertNewsFindings_PutsTheWebSearchBulletsInPlaceOfTheMarker_KeepingTheirFullLinks()
    {
        const string briefing = "## More background information\n\n2. News and media coverage\n   {{news}}\n3. Financial health assessment";
        var webSearch = Result(AgentNames.WebSearch,
            "## Web search findings\n\n- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n- 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)");

        var result = _sut.InsertNewsFindings(briefing, webSearch);

        Assert.Contains("   - 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n   - 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)\n3. Financial", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{news}}", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Web search findings", result, StringComparison.Ordinal);
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
    public void InsertNewsFindings_OrdersFindingsMostRecentFirst_WithUndatedOnesLast()
    {
        var webSearch = Result(AgentNames.WebSearch,
            "- Date not given: C. [C](https://c.org/c)\n- 10 June 2026: B. [B](https://b.org/b)\n- 24 September 2026: A. [A](https://a.org/a)");

        var result = _sut.InsertNewsFindings("   {{news}}", webSearch)!;

        Assert.True(result.IndexOf("24 September", StringComparison.Ordinal) < result.IndexOf("10 June", StringComparison.Ordinal));
        Assert.True(result.IndexOf("10 June", StringComparison.Ordinal) < result.IndexOf("Date not given", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("## Web search findings\n\nNo current web information was found for this school or trust.")]
    public void InsertNewsFindings_WithNoFindings_SaysNoCoverageWasFound(string? webSearchOutput)
    {
        var webSearch = webSearchOutput is null ? null : Result(AgentNames.WebSearch, webSearchOutput);

        var result = _sut.InsertNewsFindings("2. News and media coverage\r\n   {{news}}\r\n3. Financial", webSearch);

        Assert.Contains($"   - {ErrorMessages.NoNewsCoverageFound}", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{news}}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertNewsFindings_WithNoMarker_LeavesTheBriefingUnchanged()
    {
        const string briefing = "# Test Academy\n\nNo marker here.";

        Assert.Equal(briefing, _sut.InsertNewsFindings(briefing, Result(AgentNames.WebSearch, "- 1 May 2026: News. [A](https://a.org/x)")));
    }

    [Fact]
    public void BuildSubjectInstruction_IncludesTheSchoolOrTrustNameAndUrnOrTrn()
    {
        var instruction = _sut.BuildSubjectInstruction(new BriefingParameters("123456", "Test Academy"));

        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }
}
