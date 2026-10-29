using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Application.Extensions;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// The one place the briefing agents' prompts are built - the app and the release gate both use it, so the gate tests
/// the prompts the app sends.
/// </summary>
internal partial class BriefingBuilder(IPromptProvider promptProvider, IPromptTemplateBuilder promptBuilder) : IBriefingBuilder
{
    public AIResult BuildNoEvidenceResult(AgentDefinition establihsmentOrTrustAgent, BriefingParameters parameters)
    {
        var message = establihsmentOrTrustAgent.SystemPromptKey == SystemPromptKey.Trust
            ? ErrorMessages.NoTrustInformationFound
            : ErrorMessages.NoSchoolOrAcademyFound;

        return new AIResult($"# {parameters.SchoolOrTrustName}\n\n{message}", TotalTokens: 0);
    }

    public string CompileContext(IReadOnlyCollection<AgentResult> results)
        => string.Join(Environment.NewLine + Environment.NewLine, results.Select(
            result =>
                $"""
                ===================================
                AGENT: {result.AgentName.ToDisplayName()}
                ===================================

                {result.Output}
                """));
    public string BuildWebSearchInstruction(BriefingParameters parameters, string? city)
    {
        var subject = $"\"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn})";
        var locationHint = city is null ? string.Empty : $" It is located in or near {city}.";
        return $"""
            We already have the internal-record findings supplied as evidence below about {subject}.{locationHint}
            Search the web for current, genuinely new, publicly available information about this subject that is not already covered by those findings. Do not repeat or re-confirm anything already listed.
            Search local news first: local newspapers and broadcasters covering the area, and the local authority's own website and news releases. Take the local authority from the findings below; if none is given, use the place named above. Then search national sources.
            End every finding with a link in the form [publisher](full address of the page) - never a publisher's name alone. Start every finding with the date the page was published, as d mmmm yyyy, or "Date not given:" - never "today", "3 days ago" or any other relative date.
            """;
    }
    // The briefing's own citations are bracketed record names with no address, e.g. "[Silverdale School 139167 …]";
    // left in, the web search agent copies that style and cites news without a link. Only the facts are needed.
    public string BuildWebSearchEvidence(string briefing) => CitationRegex().Replace(briefing, string.Empty);

    [System.Text.RegularExpressions.GeneratedRegex(@"[ \t]*\[[^\[\]\r\n]+\](?:\([^()\s]+\))?")]
    private static partial System.Text.RegularExpressions.Regex CitationRegex();

    public AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools) => new()
    {
        Name = AgentNames.WebSearch,
        Instructions = promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch),
        Tools = tools
    };
    public string BuildSubjectInstruction(BriefingParameters parameters)
        => $"Using only the evidence supplied, analyse \"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn}) and report your findings. Do not use any other knowledge.";

    public string BuildSynthesisPrompt(BriefingParameters parameters, IReadOnlyCollection<AgentResult> specialistResults)
        => promptBuilder.Build(UserPromptType.Synthesis, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["Context"] = CompileContext(specialistResults),
            ["IncludedSections"] = string.Join(", ", specialistResults.Select(result => result.AgentName.ToDisplayName()))
        });

    // The web search only runs if the person asks for it once they've read the briefing, so its findings are a
    // More background information section of their own, shown apart from the briefing: the school's or trust's own
    // website, from its record, then the news. The news is the web search agent's own bullets, unchanged: a model
    // rewriting them shortens each link to a bare domain.
    public string BuildNewsFindings(AgentResult webSearchResult, BriefingParameters parameters, string? website)
    {
        // Most recent first, as the model doesn't reliably order them; undated findings go last.
        var findings = (webSearchResult.Output ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .OrderByDescending(PublishedDate)
            .ToList();

        var body = findings.Count > 0 ? string.Join('\n', findings) : $"- {ErrorMessages.NoNewsCoverageFound}";

        // The address is plain text, so it's shown as a link but isn't collected into the References like a citation.
        var websiteSection = string.IsNullOrWhiteSpace(website)
            ? string.Empty
            : $"{WebsiteSectionHeading}\n\nThe website for {parameters.SchoolOrTrustName} is: {website.Trim()}\n\n";

        return $"{BackgroundSectionHeading}\n\n{websiteSection}{NewsSectionHeading}\n\n{body}\n";
    }

    public bool HasNewsSection(string briefing) => briefing.Contains(NewsSectionHeading, StringComparison.Ordinal);

    // More background information is only for web search findings. The Synthesis model is told not to write it, but
    // one it writes anyway is removed here, so the section appears only once the person asks for a web search.
    public string? RemoveBackgroundSection(string? briefing)
        => briefing is null ? null : BackgroundSectionRegex().Replace(briefing, string.Empty);

    public const string BackgroundSectionHeading = "## More background information";

    public const string WebsiteSectionHeading = "### Website";

    public const string NewsSectionHeading = "### News and media coverage";

    // The section runs to the next "##" heading ("###" doesn't end it) or the end of the briefing.
    [System.Text.RegularExpressions.GeneratedRegex(@"^##[ \t]+More background information[^\r\n]*(?:\r?\n|\z)[\s\S]*?(?=^##[ \t]|\z)", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex BackgroundSectionRegex();

    // The Synthesis model is told to leave out a section it has no facts for, but sometimes leaves its heading with
    // nothing under it - a "##" heading followed straight by another heading, or by the end, is removed here.
    public string? RemoveEmptySections(string? briefing)
        => briefing is null ? null : EmptySectionRegex().Replace(briefing, string.Empty);

    [System.Text.RegularExpressions.GeneratedRegex(@"^##[ \t]+[^\r\n]*\r?\n(?:[ \t]*\r?\n)*(?=^##?[ \t]|\z)", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex EmptySectionRegex();

    // A finding starts with its date, e.g. "- 20 August 2026: ...".
    private static DateOnly PublishedDate(string finding)
    {
        var colon = finding.IndexOf(':', StringComparison.Ordinal);
        return colon > 2 && DateOnly.TryParseExact(finding[2..colon].Trim(), "d MMMM yyyy", EnglishCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;
    }

    private static readonly System.Globalization.CultureInfo EnglishCulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
}
