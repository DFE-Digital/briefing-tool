using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
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
    private readonly BriefingBuilder _sut;

    public BriefingBuilderTests()
    {
        _sut = new BriefingBuilder(_promptProvider, _promptBuilder);
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
    [InlineData(null, "Its town isn't known, so give priority to national sources.")]
    [InlineData("Coventry", "located in or near Coventry")]
    public void BuildWebSearchInstruction_NamesTheSubject_WithItsTown_OrNationalSourcesFirstWhenItsTownIsntKnown(string? city, string hint)
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy", IsTrust: false), city);

        Assert.Contains("\"Test Academy\" (URN/TRN: 123456)", instruction, StringComparison.Ordinal);
        Assert.Contains(hint, instruction, StringComparison.Ordinal);
        // With no town, the agent is never told where the school is: the search's own default location isn't its town.
        if (city is null)
            Assert.DoesNotContain("located in or near", instruction, StringComparison.Ordinal);
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
    [InlineData("123456", false, "school", UserPromptType.SchoolBriefing)]
    [InlineData("TR12345", true, "trust", UserPromptType.TrustBriefing)]
    public void BuildBriefingPrompt_FillsTheSchoolOrTrustTemplate_WithTheSubjectAndItsType(string urnOrTrn, bool isTrust, string subjectType, string promptType)
    {
        _promptBuilder.Build(promptType, Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("briefing-prompt");

        var prompt = _sut.BuildBriefingPrompt(new BriefingParameters(urnOrTrn, "Test", isTrust));

        Assert.Equal("briefing-prompt", prompt);
        _promptBuilder.DidNotReceive().Build(isTrust ? UserPromptType.SchoolBriefing : UserPromptType.TrustBriefing, Arg.Any<IReadOnlyDictionary<string, string>>());
        _promptBuilder.Received(1).Build(promptType, Arg.Is<IReadOnlyDictionary<string, string>>(values =>
            values["SchoolOrTrustName"] == "Test" && values["SchoolUrnOrTrustTrn"] == urnOrTrn && values["SubjectType"] == subjectType));
    }

    [Fact]
    public void BuildBriefingPrompt_GivesNoDate_AsTheEvidenceAlreadyHoldsOnlyTheLast3YearsConcerns()
    {
        _sut.BuildBriefingPrompt(Subject);

        _promptBuilder.Received(1).Build(UserPromptType.SchoolBriefing,
            Arg.Is<IReadOnlyDictionary<string, string>>(values => !values.ContainsKey("Today")));
    }

    [Theory]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]\n\n{{city: Exampleton}}")]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]\n\n{{ City: none }}")]
    [InlineData("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]")]
    public void PresentBriefing_TakesOutAnyLocationMarkerTheAgentStillWrites(string output)
    {
        var briefing = _sut.PresentBriefing(output);

        Assert.Equal("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]", briefing);
    }

    [Fact]
    public void PresentBriefing_PutsTheSpaceBackAfterAHeadingsHashes_SoEmptySectionsAreStillRemoved()
    {
        var briefing = _sut.PresentBriefing(
            "#Test Academy\n\n##About the school\n\nA fact. [Evidence 1]\n\n##KS4 Summary\n\n###Detail\n\n- #1 in the list\n## Ofsted Summary\n");

        Assert.Equal("# Test Academy\n\n## About the school\n\nA fact. [Evidence 1]\n\n## KS4 Summary\n\n### Detail\n\n- #1 in the list\n", briefing);
    }

    [Theory]
    [InlineData("- [Allestree Woodlands School]: Good [Evidence 2]", "- Allestree Woodlands School: Good [Evidence 2]")]
    [InlineData("- [Allestree Woodlands School] – Inspected 23 April 2024 [Evidence 2]", "- Allestree Woodlands School – Inspected 23 April 2024 [Evidence 2]")]
    [InlineData("  - [Previous inspection]: Good [Evidence 2]", "  - Previous inspection: Good [Evidence 2]")]
    [InlineData("1. [Frederick Gent School]: Report card", "1. Frederick Gent School: Report card")]
    [InlineData("### [Wilsthorpe School]", "### Wilsthorpe School")]
    [InlineData("[Selston High School] – Good", "Selston High School – Good")]
    public void PresentBriefing_TakesTheBracketsOffANameStartingALine_SoThePageDoesntTurnItIntoAReference(string line, string expected)
    {
        var briefing = _sut.PresentBriefing($"# Test Trust\n\n## Ofsted summary\n\n{line}\n");

        Assert.Equal($"# Test Trust\n\n## Ofsted summary\n\n{expected}\n", briefing);
    }

    [Theory]
    [InlineData("- Allestree Woodlands School: Good [Evidence 2]")]
    [InlineData("- Allestree Woodlands School: Good [Allestree Woodlands School - school record (URN 137911)]")]
    [InlineData("- [Evidence 2] Good")]
    [InlineData("- [Allestree Woodlands School](https://www.woodlands.ttct.co.uk): Good")]
    [InlineData("- [<a href=\"https://www.woodlands.ttct.co.uk\">Allestree Woodlands School</a>] Good")]
    public void PresentBriefing_LeavesCitationsAndLinksInTheirBrackets(string line)
    {
        var briefing = _sut.PresentBriefing($"# Test Trust\n\n## Ofsted summary\n\n{line}\n");

        Assert.Equal($"# Test Trust\n\n## Ofsted summary\n\n{line}\n", briefing);
    }

    [Fact]
    public void OrderAcademies_ForATrust_PutsItsAcademyBulletsInTheOrderItsRecordsGive()
    {
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);
        var academies = new ContextResult(
            "[Evidence 1]\nestablishment_name: Academy A\nestablishment_urn: 900001\nks4_performance_latest: {\"attainment_8_score\": \"44.0\"}\n\n" +
            "[Evidence 2]\nestablishment_name: Academy B\nestablishment_urn: 900002\nks4_performance_latest: {\"attainment_8_score\": \"52.5\"}", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);
        const string briefing = "## KS4 summary\n\n- Academy A: Attainment 8 score 44.0 [Evidence 2]\n- Academy B: Attainment 8 score 52.5 [Evidence 3]";

        var ordered = _sut.OrderAcademies(briefing, new BriefingParameters("TR12345", "Test Trust", IsTrust: true), new BriefingRecords(trust, academies, none, none));

        Assert.Equal("## KS4 summary\n\n- Academy B: Attainment 8 score 52.5 [Evidence 3]\n- Academy A: Attainment 8 score 44.0 [Evidence 2]", ordered);
    }

    [Fact]
    public void OrderAcademies_ForASchool_ReturnsTheBriefingAsItIs()
    {
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);
        const string briefing = "## Ofsted summary\n\n- Latest inspection: Good [Evidence 1]\n- Previous inspection: Outstanding [Evidence 1]";

        Assert.Equal(briefing, _sut.OrderAcademies(briefing, Subject, new BriefingRecords(school, none, none, none)));
    }

    [Fact]
    public void FindTown_TakesTheTownFromTheSubjectsOwnRecord()
    {
        var school = new ContextResult("[Evidence 1]\nestablishment_urn: 123456\nestablishment_address: 2 School Lane, Exampleton, EX1 2BB", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        Assert.Equal("Exampleton", _sut.FindTown(Subject, new BriefingRecords(school, none, none, none)));
    }


    [Fact]
    public void BuildBriefingEvidence_LabelsEachSet_AndNumbersTheRecordsAndTheirSourcesOnAcrossThem()
    {
        var school = new ContextResult("--- establishment_index Evidence 1 ---\nestablishment_name: Test Academy", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Test Academy", new Uri("https://test.example.org/"))]
        };
        var trust = new ContextResult("--- trust_index Evidence 1 ---\ntrust_name: Test Trust", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Test Trust")]
        };
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, trust, none, none));

        Assert.Equal(
            "=== School record ===\n\n--- establishment_index Evidence 1 ---\nestablishment_name: Test Academy\n\n" +
            "=== Its trust's record ===\n\n--- trust_index Evidence 2 ---\ntrust_name: Test Trust",
            evidence.Text);
        Assert.Equal(
            [new EvidenceSource(1, "Test Academy", new Uri("https://test.example.org/")), new EvidenceSource(2, "Test Trust")],
            evidence.Sources);
    }

    [Fact]
    public void BuildBriefingEvidence_ForATrust_PutsItsOwnRecordFirst_AndItsAcademiesLast_AsTheyreCutFirstWhenTooLong()
    {
        var none = new ContextResult(string.Empty, HasEvidence: false);
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);

        var evidence = _sut.BuildBriefingEvidence(new BriefingParameters("TR12345", "Test Trust", IsTrust: true), new BriefingRecords(trust, none, none, none));

        Assert.StartsWith("=== Trust record ===\n\n[Evidence 1]\ntrust_name: Test Trust\n\n=== Its academies' records ===", evidence.Text, StringComparison.Ordinal);
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
            "=== Its academies' records ===",
            evidence.Text, StringComparison.Ordinal);
        // The counts have no record numbers, so the academies still number on from the trust's record.
        Assert.Contains("=== Its academies' records ===\n\n[Evidence 2]\nestablishment_urn: 900001", evidence.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildBriefingEvidence_NeverGivesTheAgentRiseOrRecastConcerns_AsNoBriefingReportsThem(bool isTrust)
    {
        var own = new ContextResult("[Evidence 1]\nestablishment_urn: 123456", HasEvidence: true);
        var related = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);
        var rise = new ContextResult("[Evidence 1]\nconcern_date: 2025-02-10\nsummary: Pupil Casey Madeup bullied", HasEvidence: true)
        {
            Sources = [new EvidenceSource(1, "Rise concern")]
        };
        var recast = new ContextResult("[Evidence 1]\nconcern_date: 2025-03-03\nconcern: Falling roll", HasEvidence: true);
        var parameters = isTrust ? new BriefingParameters("TR12345", "Test Trust", IsTrust: true) : Subject;

        var evidence = _sut.BuildBriefingEvidence(parameters, new BriefingRecords(own, related, rise, recast));

        Assert.DoesNotContain("concerns ===", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Casey Madeup", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Falling roll", evidence.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(evidence.Sources, source => source.Name == "Rise concern");
        // The related records number straight on from the subject's, as no concern took a number.
        Assert.Contains("[Evidence 2]", evidence.Text, StringComparison.Ordinal);
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
    public void BuildBriefingEvidence_GivesTheAgentValuesReadyToCopy_AndNoRepeats()
    {
        var school = new ContextResult(
            "[Evidence 1]\nestablishment_urn: 123456\nofsted_all_inspections: [{\"inspection_date\": \"2022-11-28\"}]\n" +
            "ks4_performance_latest: {\"academic_year\": \"202324\", \"attainment_8_score\": \"47.3%\"}\n\n" +
            "[Evidence 2]\nestablishment_urn: 123456\nofsted_all_inspections: [{\"inspection_date\": \"2022-11-28\"}]", HasEvidence: true);
        var trust = new ContextResult("[Evidence 1]\ntrust_name: Test Trust", HasEvidence: true);
        var none = new ContextResult(string.Empty, HasEvidence: false);

        var evidence = _sut.BuildBriefingEvidence(Subject, new BriefingRecords(school, trust, none, none));

        Assert.Contains("\"inspection_date\": \"28 November 2022\"", evidence.Text, StringComparison.Ordinal);
        Assert.Contains("\"academic_year\": \"2023/24\", \"attainment_8_score\": \"47.3\"", evidence.Text, StringComparison.Ordinal);
        // The school's second record is a less complete copy of its first, so its trust's record numbers on from 1.
        Assert.DoesNotContain("[Evidence 2]\nestablishment_urn", evidence.Text, StringComparison.Ordinal);
        Assert.EndsWith("=== Its trust's record ===\n\n[Evidence 2]\ntrust_name: Test Trust", evidence.Text, StringComparison.Ordinal);
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
