using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class BriefingBuilderTests
{
    private static readonly BriefingParameters Subject = new("123456", "Test Academy", IsTrust: false);

    private readonly IPromptProvider _promptProvider = Substitute.For<IPromptProvider>();
    private readonly IPromptTemplateBuilder _promptBuilder = Substitute.For<IPromptTemplateBuilder>();
    private readonly TimeProvider _timeProvider = Substitute.For<TimeProvider>();
    private readonly BriefingBuilder _sut;

    public BriefingBuilderTests()
    {
        _timeProvider.GetUtcNow().Returns(new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero));
        _sut = new BriefingBuilder(_promptProvider, _promptBuilder, _timeProvider);
    }

    private static AgentResult Result(string agentName, string output)
        => new() { AgentName = agentName, Output = output, TotalTokens = 10 };

    [Fact]
    public void BuildNoEvidenceResult_ForATrust_ReturnsNoTrustInformationFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(new BriefingParameters("TR12345", "Test Trust", IsTrust: true));

        Assert.Contains("No trust information found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_ForASchool_ReturnsNoSchoolOrAcademyFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(new BriefingParameters("123456", "Test Academy", IsTrust: false));

        Assert.Contains("No school or academy found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_StartsWithAMarkdownHeadingOfTheSubjectName()
    {
        var result = _sut.BuildNoEvidenceResult(new BriefingParameters("123456", "Smith & Jones Academy", IsTrust: false));

        Assert.StartsWith("# Smith & Jones Academy", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithNoCity_OmitsTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy", IsTrust: false), city: null);

        Assert.DoesNotContain("located in or near", instruction, StringComparison.Ordinal);
        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithACity_IncludesTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy", IsTrust: false), city: "Coventry");

        Assert.Contains("located in or near Coventry", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_AsksForLocalAuthorityAndLocalNewsFirst()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy", IsTrust: false), city: "Coventry");

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
    public void BuildNewsFindings_GivesTheWebSearchBulletsAsAMoreBackgroundInformationSection_KeepingTheirFullLinks()
    {
        var webSearch = Result(AgentNames.WebSearch,
            "## Web search findings\n\n- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n- 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)");

        var result = _sut.BuildNewsFindings(webSearch, Subject, website: null);

        Assert.Equal("## More background information\n\n### News and media coverage\n\n- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)\n- 22 July 2026: New chief executive. [Chorus](https://www.chorustrust.org/news/ceo)\n", result);
        Assert.True(_sut.HasNewsSection(result));
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
    public void BuildNewsFindings_WithAWebsite_GivesItFirst_AsPlainText()
    {
        var webSearch = Result(AgentNames.WebSearch, "- 20 August 2026: Best GCSE results. [The Star](https://www.thestar.co.uk/news/a-1)");

        var result = _sut.BuildNewsFindings(webSearch, Subject, website: " www.test-academy.example.org ");

        Assert.StartsWith(
            "## More background information\n\n### Website\n\nThe website for Test Academy is: www.test-academy.example.org\n\n" +
            "### News and media coverage\n\n- 20 August 2026", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNewsFindings_OrdersFindingsMostRecentFirst_WithUndatedOnesLast()
    {
        var webSearch = Result(AgentNames.WebSearch,
            "- Date not given: C. [C](https://c.org/c)\n- 10 June 2026: B. [B](https://b.org/b)\n- 24 September 2026: A. [A](https://a.org/a)");

        var result = _sut.BuildNewsFindings(webSearch, Subject, website: null);

        Assert.True(result.IndexOf("24 September", StringComparison.Ordinal) < result.IndexOf("10 June", StringComparison.Ordinal));
        Assert.True(result.IndexOf("10 June", StringComparison.Ordinal) < result.IndexOf("Date not given", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildNewsFindings_WithNoFindings_SaysNoCoverageWasFound()
    {
        var webSearch = Result(AgentNames.WebSearch, "## Web search findings\n\nNo current web information was found for this school or trust.");

        var result = _sut.BuildNewsFindings(webSearch, Subject, website: null);

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

    [Theory]
    [InlineData("123456", false, "school")]
    [InlineData("TR12345", true, "trust")]
    public void BuildBriefingPrompt_FillsTheBriefingTemplate_WithTheSubjectAndItsType(string urnOrTrn, bool isTrust, string subjectType)
    {
        _promptBuilder.Build(UserPromptType.Briefing, Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("briefing-prompt");

        var prompt = _sut.BuildBriefingPrompt(new BriefingParameters(urnOrTrn, "Test", isTrust));

        Assert.Equal("briefing-prompt", prompt);
        _promptBuilder.Received(1).Build(UserPromptType.Briefing, Arg.Is<IReadOnlyDictionary<string, string>>(values =>
            values["SchoolOrTrustName"] == "Test" && values["SchoolUrnOrTrustTrn"] == urnOrTrn && values["SubjectType"] == subjectType));
    }

    [Fact]
    public void BuildBriefingPrompt_GivesTodaysDate_SoTheAgentKnowsWhichConcernsAreFromTheLast3Years()
    {
        _sut.BuildBriefingPrompt(Subject);

        _promptBuilder.Received(1).Build(UserPromptType.Briefing,
            Arg.Is<IReadOnlyDictionary<string, string>>(values => values["Today"] == "3 November 2026"));
    }

    [Theory]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]\n\n{{city: Exampleton}}", "Exampleton")]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]\n\n{{city: none}}", null)]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]", null)]
    public void PresentBriefing_TakesOutTheLocationMarker_AndGivesItsCity(string output, string? city)
    {
        var (briefing, found) = _sut.PresentBriefing(output);

        Assert.Equal(city, found);
        Assert.DoesNotContain("{{city", briefing, StringComparison.Ordinal);
        Assert.Contains("A fact. [Evidence 1]", briefing, StringComparison.Ordinal);
    }


    [Fact]
    public void BuildBriefingEvidence_LabelsEachSet_AndNumbersTheRecordsAndTheirSourcesOnAcrossThem()
    {
        var school = new ContextResult("--- establishment_index Evidence 1 ---\nestablishment_name: Test Academy", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Test Academy", new Uri("https://test.example.org/"))]
        };
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);
        var rise = new ContextResult("--- rise_concerns_index Evidence 1 ---\nsummary: A\n\n--- rise_concerns_index Evidence 2 ---\nsummary: B", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Concern A"), new EvidenceSource(2, "Concern B")]
        };
        var recast = new ContextResult("No match.", HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, trust, rise, recast));

        Assert.Equal(
            "=== School record ===\n\n--- establishment_index Evidence 1 ---\nestablishment_name: Test Academy\n\n" +
            "=== Rise concerns ===\n\n--- rise_concerns_index Evidence 2 ---\nsummary: A\n\n--- rise_concerns_index Evidence 3 ---\nsummary: B\n\n" +
            "=== Recast concerns ===\n\nNo match.\n\n" +
            "=== Its trust's record ===\n\n[Evidence 4]\ntrust_name: Test Trust",
            evidence.Text);
        Assert.Equal(
            [new EvidenceSource(1, "Test Academy", new Uri("https://test.example.org/")), new EvidenceSource(2, "Concern A"), new EvidenceSource(3, "Concern B")],
            evidence.Sources);
    }

    [Fact]
    public void BuildBriefingEvidence_ForATrust_PutsItsOwnRecordFirst_AndItsAcademiesLast_AsTheyreCutFirstWhenTooLong()
    {
        var none = new ContextResult(string.Empty, HasEvidence: false);
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);

        var evidence = _sut.BuildBriefingEvidence(new BriefingParameters("TR12345", "Test Trust", IsTrust: true), new BriefingRecords(trust, none, none, none));

        Assert.StartsWith("=== Trust record ===\n\n[Evidence 1]\ntrust_name: Test Trust\n\n=== Rise concerns ===", evidence.Text, StringComparison.Ordinal);
        Assert.EndsWith("=== Its academies' records ===\n\nNo records were found.", evidence.Text, StringComparison.Ordinal);
    }
}
