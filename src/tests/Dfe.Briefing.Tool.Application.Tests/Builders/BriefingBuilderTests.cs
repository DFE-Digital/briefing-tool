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

    [Theory]
    [InlineData("TR12345", "Test Trust", true, "No trust information found.")]
    [InlineData("123456", "Smith & Jones Academy", false, "No school or academy found.")]
    public void BuildNoEvidenceResult_GivesAHeadingOfTheSubjectName_AndSaysNothingWasFound_ForItsType(
        string reference, string name, bool isTrust, string message)
    {
        var result = _sut.BuildNoEvidenceResult(new BriefingParameters(reference, name, isTrust));

        Assert.Equal($"# {name}\n\n{message}", result.Output);
        Assert.Equal(0, result.TotalTokens);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Coventry", "located in or near Coventry")]
    public void BuildWebSearchInstruction_NamesTheSubject_WithALocationHintOnlyWhenThereIsACity(string? city, string? hint)
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy", IsTrust: false), city);

        Assert.Contains("\"Test Academy\" (URN/TRN: 123456)", instruction, StringComparison.Ordinal);
        if (hint is null)
            Assert.DoesNotContain("located in or near", instruction, StringComparison.Ordinal);
        else
            Assert.Contains(hint, instruction, StringComparison.Ordinal);
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
        Assert.DoesNotContain("=== Academy counts ===", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBriefingEvidence_ForATrust_PutsItsAcademyCountsStraightAfterItsOwnRecord_AsTheyreNeverCut()
    {
        var none = new ContextResult(string.Empty, HasEvidence: false);
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);
        var academies = new ContextResult(
            "[Evidence 1]\nestablishment_urn: 900001\nphase_of_education: Primary\nlocal_authority_name: Exampleshire\n" +
            "ofsted_all_inspections: [{\"inspection_date\": \"2023-03-14\", \"overall_effectiveness\": \"Good\"}]\n\n" +
            "[Evidence 2]\nestablishment_urn: 900002\nphase_of_education: Secondary\nlocal_authority_name: Exampleshire", HasEvidence: true);

        var evidence = _sut.BuildBriefingEvidence(new BriefingParameters("TR12345", "Test Trust", IsTrust: true), new BriefingRecords(trust, academies, none, none));

        Assert.StartsWith(
            "=== Trust record ===\n\n[Evidence 1]\ntrust_name: Test Trust\n\n" +
            "=== Academy counts ===\n\nacademies_counted: 2\nby_phase: Primary 1, Secondary 1\n" +
            "by_latest_ofsted_inspection: Good 1, Not available 1\nby_local_authority: Exampleshire 2\n\n" +
            "=== Rise concerns ===",
            evidence.Text, StringComparison.Ordinal);
        // The counts have no record numbers, so the academies still number on from the trust's record.
        Assert.Contains("=== Its academies' records ===\n\n[Evidence 2]\nestablishment_urn: 900001", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBriefingEvidence_SendsOnlyTheConcernsFromTheLast3Years_EachDatedInWords()
    {
        // Today is 3 November 2026, so the window starts on 3 November 2023 (see ConcernWindowTests for the rules).
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456", HasEvidence: true);
        var rise = new ContextResult("[Evidence 1]\nconcern_date: 2019-05-01\nsummary: Old\n\n[Evidence 2]\nconcern_date: 2025-02-11\nsummary: Recent", HasEvidence: true);
        var recast = new ContextResult("[Evidence 1]\nconcern_date: 2021-02-02\nsummary: Also old\n\n[Evidence 2]\nconcern_date: 2025-03-03T09:30:00Z\nconcern: Falling roll", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, none, rise, recast));

        Assert.Contains("=== Rise concerns ===\n\n[Evidence 3]\nconcern_date: 11 February 2025\nsummary: Recent\n\n", evidence.Text, StringComparison.Ordinal);
        Assert.Contains("concern_date: 3 March 2025\nconcern: Falling roll", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("2019-05-01", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("2021-02-02", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBriefingEvidence_ForASpecialSchoolWithNoResults_SaysKeyStageResultsDoNotApply_StraightAfterItsRecord()
    {
        // When the fact is given at all is covered by KeyStageResultsTests.
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456\nphase_of_education: Not applicable", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, none, none, none));

        Assert.Contains($"phase_of_education: Not applicable\n\n=== {EvidenceLabels.KeyStageResults} ===\n\nTest Academy is a special school or alternative provision",
            evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBriefingEvidence_GivesEachWebsiteItsHttps_SoTheAgentCannotCopyABareAddress()
    {
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456\nestablishment_website: www.example-valley.example.org", HasEvidence: true);
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Example Learning Trust\ntrust_website: example-trust.example.org", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, trust, none, none));

        Assert.Contains("establishment_website: https://www.example-valley.example.org\n", evidence.Text, StringComparison.Ordinal);
        Assert.Contains("trust_website: https://example-trust.example.org", evidence.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBriefingEvidence_ForASchool_GivesNoAcademyCounts()
    {
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456\nphase_of_education: Primary", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, none, none, none));

        Assert.DoesNotContain("=== Academy counts ===", evidence.Text, StringComparison.Ordinal);
    }
}
