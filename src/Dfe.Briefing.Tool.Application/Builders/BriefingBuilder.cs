using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
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
    private const string EstablishmentUrnField = "establishment_urn";
    private const string TrustReferenceNumberField = "trust_reference_number";
    public AIResult BuildNoEvidenceResult(BriefingParameters parameters)
    {
        var message = parameters.IsTrust ? ErrorMessages.NoTrustInformationFound : ErrorMessages.NoSchoolOrAcademyFound;
        return new AIResult($"# {parameters.SchoolOrTrustName}\n\n{message}", TotalTokens: 0);
    }

    public string? PresentBriefing(string? output) => BriefingPresenter.Present(output);

    public string? FindTown(BriefingParameters parameters, BriefingRecords records) => TownFinder.Find(parameters, records);

    public string? OrderAcademies(string? briefing, BriefingParameters parameters, BriefingRecords records)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(records);

        return parameters.IsTrust && records.Related.HasEvidence
            ? AcademyListOrder.Apply(briefing, AcademyCounter.Order(records.Related.Text, records.Subject.Text))
            : briefing;
    }

    public string BuildBriefingPrompt(BriefingParameters parameters)
        => promptBuilder.Build(UserPromptType.Briefing, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn, 
            ["SubjectType"] = parameters.IsTrust ? "trust" : "school"
        });

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Builds briefing evidence text and source mappings from subject, concerns, and related records.
    /// </summary>
    /// <param name="parameters">Defines briefing context, including subject type and identifiers used to label and shape evidence sections.</param>
    /// <param name="records">Provides subject, concern, and related record sets used to compose evidence content and sources.</param>
    /// <returns>An AgentEvidence instance containing merged, labeled evidence text and renumbered sources with consistent
    /// offsets.</returns>
    public AgentEvidence BuildBriefingEvidence(BriefingParameters parameters, BriefingRecords records)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(records);
         
        var academyCounts = parameters.IsTrust && records.Related.HasEvidence ? AcademyCounter.Count(records.Related.Text) : null;
        var keyStageResults = parameters.IsTrust ? null : KeyStageResults.NotApplicable(records.Subject, parameters.SchoolUrnOrTrustTrn, parameters.SchoolOrTrustName);
        (string Label, ContextResult Result)[] sets =
        [
            (parameters.IsTrust ? EvidenceLabels.TrustRecord : EvidenceLabels.SchoolRecord,
                Records(records.Subject, parameters.IsTrust ? TrustReferenceNumberField : EstablishmentUrnField)),
            .. academyCounts is null ? [] : new[] { (EvidenceLabels.AcademyCounts, new ContextResult(academyCounts, HasEvidence: true)) },
            .. keyStageResults is null ? [] : new[] { (EvidenceLabels.KeyStageResults, new ContextResult(keyStageResults, HasEvidence: true)) },
            (EvidenceLabels.RiseConcerns, Concerns(records.RiseConcerns)),
            (EvidenceLabels.RecastConcerns, Concerns(records.RecastConcerns)),
            (parameters.IsTrust ? EvidenceLabels.AcademyRecords : EvidenceLabels.SchoolsTrustRecord,
                Records(records.Related, parameters.IsTrust ? EstablishmentUrnField : TrustReferenceNumberField))
        ];

        var text = new StringBuilder();
        var sources = new List<EvidenceSource>();
        var offset = 0;
        foreach (var (label, result) in sets)
        {
            text.Append($"=== {label} ===\n\n");
            // A search can find nothing, or report evidence with no text: either way the set says no records were found.
            if (!result.HasEvidence || string.IsNullOrWhiteSpace(result.Text))
            {
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

    /// <summary>
    /// Removes duplicate evidence records by key field, formats the records, and ensures website addresses include a URI scheme.
    /// </summary>
    /// <param name="records">The records to process.</param>
    /// <param name="keyField">The field name used to identify duplicate records.</param>
    /// <returns>A processed result with duplicates removed, records formatted, and website addresses normalized with a scheme.</returns>
    private static ContextResult Records(ContextResult records, string keyField)
        => WebsiteAddress.WithScheme(RecordFormatter.Format(EvidenceRecords.WithoutDuplicates(records, keyField)));

    /// <summary>
    /// Builds a formatted concerns result from recent entries by applying redaction, duplicate removal, and
    /// newest-first ordering.
    /// </summary>
    /// <param name="concerns">Source concerns context used to select recent entries.</param>
    /// <returns>A formatted concerns result containing redacted, deduplicated, and newest-first recent entries.</returns>
    private ContextResult Concerns(ContextResult concerns)
        => RecordFormatter.Format(ConcernWindow.NewestFirst(EvidenceRecords.WithoutDuplicates(
            ConcernRedactor.Redact(ConcernWindow.Recent(concerns, Today())), keyField: null)));

    private static int Number(string digits) => int.Parse(digits, CultureInfo.InvariantCulture);

    /// <summary>
    /// Matches the numeric evidence identifier in evidence header lines formatted as either markdown-style (`--- ...
    /// Evidence {number} ---`) or bracket-style (`[Evidence {number}]`).
    /// </summary>  
    [GeneratedRegex(@"(?<=^---[^\r\n]*\bEvidence |^\[Evidence )\d+(?=(?: ---|\])\r?$)", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EvidenceNumberRegex();

    // With no town, the search is biased to its default location (London) but the agent isn't told the school is there.
    private const string UnknownTownHint = " Its town isn't known, so give priority to national sources.";

    public string BuildWebSearchInstruction(BriefingParameters parameters, string? city)
    {
        var subject = $"\"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn})";
        var locationHint = city is null ? UnknownTownHint : $" It is located in or near {city}.";
        return $"""
            We already have the internal-record findings supplied as evidence below about {subject}.{locationHint}
            Search the web for current, genuinely new, publicly available information about this subject that is not already covered by those findings. Do not repeat or re-confirm anything already listed.
            Search local news first: local newspapers and broadcasters covering the area, and the local authority's own website and news releases. Take the local authority from the findings below; if none is given, use the place named above. Then search national sources.
            End every finding with a link in the form [publisher](full address of the page) - never a publisher's name alone. Start every finding with the date the page was published, as d mmmm yyyy, or "Date not given:" - never "today", "3 days ago" or any other relative date.
            """;
    }
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
