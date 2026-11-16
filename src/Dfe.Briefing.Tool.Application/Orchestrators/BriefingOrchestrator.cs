using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Tools.WebSearch;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators;

/// <summary>
/// Writes a briefing in two steps: code finds the school's or trust's records (its own first, then the rest at the same
/// time), and one agent writes the whole briefing from them. The web search is a separate, later run, only if the person
/// asks for it.
/// </summary>
public class BriefingOrchestrator(IBriefingEvidenceProvider evidenceProvider, IAgentService agentService,
    IAgentRuntimeService agentRuntime, IBriefingBuilder briefingBuilder) : IBriefingOrchestrator
{
    public async Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var records = await evidenceProvider.GetRecordsAsync(parameters, progress, cancellationToken);
        if (records is null)
            return briefingBuilder.BuildNoEvidenceResult(parameters);

        progress?.Report("Writing the summary...");
        var result = await agentService.RunAsync(BriefingAgents.Briefing, briefingBuilder.BuildBriefingPrompt(parameters),
            briefingBuilder.BuildBriefingEvidence(parameters, records), cancellationToken);

        var (briefing, city) = briefingBuilder.PresentBriefing(result.Output);
        return new AIResult(briefing, result.TotalTokens, city);
    }

    public bool CanAddWebSearchFindings(string? briefing)
        => !string.IsNullOrWhiteSpace(briefing) && briefing.Contains("\n## ", StringComparison.Ordinal) && !briefingBuilder.HasNewsSection(briefing);

    public async Task<AIResult> GetWebSearchFindings(BriefingParameters parameters, string briefing, string? city, string? website,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(briefing);

        var location = city is null ? null : WebSearchLocation.ForCity(city);
        var tools = await new WebSearchToolProvider(location).GetToolsAsync(cancellationToken);
        var spec = briefingBuilder.BuildWebSearchAgentSpec(tools);

        // The briefing itself is the evidence, so the search looks for what it doesn't already cover.
        var webSearchResult = await agentRuntime.RunEphemeralAsync(spec, briefingBuilder.BuildWebSearchInstruction(parameters, city),
            evidence: briefingBuilder.BuildWebSearchEvidence(briefing), validateOutput: BriefingAgents.ValidateMarkdownOutput,
            cancellationToken: cancellationToken);

        return new AIResult(briefingBuilder.BuildNewsFindings(webSearchResult, parameters, website), webSearchResult.TotalTokens, city);
    }
}
