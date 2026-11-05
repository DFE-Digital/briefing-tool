using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// The one place the agents' prompts and evidence are built - the app and the release gate both use it, so the gate
/// tests what the app sends.
/// </summary>
internal partial class BriefingBuilder(IPromptProvider promptProvider, IPromptTemplateBuilder promptBuilder, TimeProvider timeProvider)
    : IBriefingBuilder
{
    public AIResult BuildNoEvidenceResult(BriefingParameters parameters)
    {
        var message = parameters.IsTrust ? ErrorMessages.NoTrustInformationFound : ErrorMessages.NoSchoolOrAcademyFound;
        return new AIResult($"# {parameters.SchoolOrTrustName}\n\n{message}", TotalTokens: 0);
    }

    public (string? Briefing, string? City) PresentBriefing(string? output) => BriefingPresenter.Present(output);

    public string BuildBriefingPrompt(BriefingParameters parameters)
        => promptBuilder.Build(UserPromptType.Briefing, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["SubjectType"] = parameters.IsTrust ? "trust" : "school",
            // The agent can't know the date itself, and needs it for "the last 3 years" of concerns.
            ["Today"] = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).ToString("d MMMM yyyy", EnglishCulture)
        });

    // Each search numbers its own records from 1, so they're renumbered to run on across the sets, each set under its
    // own label: every [Evidence n] the agent cites then points at one record, and at its source.
    public AgentEvidence BuildBriefingEvidence(BriefingParameters parameters, BriefingRecords records)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(records);

        // Evidence over AiAgents:MaxEvidenceCharacters is cut from the end, so the related records - a large trust's
        // academies can be hundreds of records - go last, after the subject's own record and its concerns. A trust's
        // academy counts, made in code from all of those records, go straight after its own record, so they're never cut.
        var academyCounts = parameters.IsTrust && records.Related.HasEvidence ? AcademyCounter.Count(records.Related.Text) : null;
        (string Label, ContextResult Result)[] sets =
        [
            (parameters.IsTrust ? EvidenceLabels.TrustRecord : EvidenceLabels.SchoolRecord, records.Subject),
            .. academyCounts is null ? [] : new[] { (EvidenceLabels.AcademyCounts, new ContextResult(academyCounts, HasEvidence: true)) },
            (EvidenceLabels.RiseConcerns, records.RiseConcerns),
            (EvidenceLabels.RecastConcerns, records.RecastConcerns),
            (parameters.IsTrust ? EvidenceLabels.AcademyRecords : EvidenceLabels.SchoolsTrustRecord, records.Related)
        ];

        var text = new StringBuilder();
        var sources = new List<EvidenceSource>();
        var offset = 0;
        foreach (var (label, result) in sets)
        {
            text.Append($"=== {label} ===\n\n");
            if (!result.HasEvidence)
            {
                // Says why there's nothing, e.g. "This school is not part of a trust.", so it isn't read as missing data.
                text.Append(string.IsNullOrWhiteSpace(result.Text) ? EvidenceLabels.NoRecords : result.Text.Trim()).Append("\n\n");
                continue;
            }

            var setOffset = offset;
            text.Append(EvidenceNumberRegex().Replace(result.Text.Trim(), match => (Number(match.Value) + setOffset).ToString(CultureInfo.InvariantCulture))).Append("\n\n");
            sources.AddRange(result.Sources.Select(source => source with { Number = source.Number + setOffset }));

            offset += Math.Max(
                EvidenceNumberRegex().Matches(result.Text).Select(match => Number(match.Value)).DefaultIfEmpty().Max(),
                result.Sources.Select(source => source.Number).DefaultIfEmpty().Max());
        }

        return new AgentEvidence(text.ToString().TrimEnd(), sources);
    }

    private static int Number(string digits) => int.Parse(digits, CultureInfo.InvariantCulture);

    // A record's own number, in the line that starts it: "--- trust_index Evidence 2 ---", or "[Evidence 2]".
    [GeneratedRegex(@"(?<=^---[^\r\n]*\bEvidence |^\[Evidence )\d+(?=(?: ---|\])\r?$)", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EvidenceNumberRegex();

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

    [GeneratedRegex(@"[ \t]*\[[^\[\]\r\n]+\](?:\([^()\s]+\))?")]
    private static partial Regex CitationRegex();

    public AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools) => new()
    {
        Name = AgentNames.WebSearch,
        Instructions = promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch),
        Tools = tools
    };

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

    // More background information is only for web search findings. The briefing agent is told not to write it, but
    // one it writes anyway is removed here, so the section appears only once the person asks for a web search.
    public string? RemoveBackgroundSection(string? briefing) => BriefingPresenter.RemoveBackgroundSection(briefing);

    public const string BackgroundSectionHeading = "## More background information";

    public const string WebsiteSectionHeading = "### Website";

    public const string NewsSectionHeading = "### News and media coverage";

    // A section the agent left with nothing under it is removed (see BriefingPresenter).
    public string? RemoveEmptySections(string? briefing) => BriefingPresenter.RemoveEmptySections(briefing);

    // A finding starts with its date, e.g. "- 20 August 2026: ...".
    private static DateOnly PublishedDate(string finding)
    {
        var colon = finding.IndexOf(':', StringComparison.Ordinal);
        return colon > 2 && DateOnly.TryParseExact(finding[2..colon].Trim(), "d MMMM yyyy", EnglishCulture,
            DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;
    }

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");
}
