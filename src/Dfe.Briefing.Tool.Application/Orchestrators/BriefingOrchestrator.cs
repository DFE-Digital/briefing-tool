using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context;
using Dfe.Briefing.Tool.Application.Extensions;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Tools.WebSearch;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators;

public partial class BriefingOrchestrator(IReadOnlyCollection<AgentDefinition> definitions, IBriefingEvidenceProvider evidenceProvider,
    IAgentService agentService, IAgentRuntimeService agentRuntime, IBriefingBuilder briefingBuilder) : IBriefingOrchestrator
{
    public async Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var establihsmentOrTrustAgent = parameters.IsTrust
            ? definitions.Single(x => x.SystemPromptKey == SystemPromptKey.Trust)
            : definitions.Single(x => x.SystemPromptKey == SystemPromptKey.Establishment);

        var specialistAgents = GetSpecialistAgents(definitions, establihsmentOrTrustAgent.Name);

        progress?.Report($"Looking up records for {parameters.SchoolOrTrustName}...");
        var establihsmentOrTrustEvidence = await evidenceProvider.GetEvidenceAsync(establihsmentOrTrustAgent, parameters, subjectEvidence: null, cancellationToken);
        if (!establihsmentOrTrustEvidence.HasEvidence)
            return briefingBuilder.BuildNoEvidenceResult(establihsmentOrTrustAgent, parameters);

        var context = new AgentContext();

        progress?.Report($"Calling {establihsmentOrTrustAgent.Name.ToDisplayName()} agent...");
        var establishmentOrTrustResults = await agentService.RunParallelAsync(
            [establihsmentOrTrustAgent],
            resolvePrompt: (_, _) => Task.FromResult(briefingBuilder.BuildSubjectInstruction(parameters)),
            context, shouldSuppress: _ => false, cancellationToken: cancellationToken,
            resolveEvidence: (_, _) => Task.FromResult<AgentEvidence?>(establihsmentOrTrustEvidence));

        var (subjectResult, city) = CityExtractorHelper.ExtractCity(establishmentOrTrustResults.Single());
        establishmentOrTrustResults = [subjectResult];

        progress?.Report($"Calling {string.Join(", ", specialistAgents.Select(definition => definition.Name.ToDisplayName()))} agents...");

        var specialistAgentResults = await agentService.RunParallelAsync(
            specialistAgents,
            resolvePrompt: (_, _) => Task.FromResult(briefingBuilder.BuildSubjectInstruction(parameters)),
            context, cancellationToken: cancellationToken,
            resolveEvidence: async (definition, ct) => await evidenceProvider.GetEvidenceAsync(definition, parameters, establihsmentOrTrustEvidence, ct));

        var strippedSpecialistResults = specialistAgentResults.Select(result => CityExtractorHelper.ExtractCity(result).Result);
        var knownResults = establishmentOrTrustResults.Concat(strippedSpecialistResults).ToList();

        progress?.Report("Compiling the briefing...");
        var synthesisAgent = definitions.Single(x => x.Name == AgentNames.Synthesis);
        var synthesisResult = await RunSynthesisAsync(synthesisAgent, parameters, knownResults, cancellationToken);
        var totalTokens = knownResults.Sum(x => x.TotalTokens) + synthesisResult.TotalTokens;

        // The web search runs only if the person asks for it once they've read the briefing (AddWebSearchFindings).
        var briefing = briefingBuilder.RemoveEmptySections(briefingBuilder.RemoveBackgroundSection(synthesisResult.Output));
        return new AIResult(briefing, totalTokens, city);
    }

    public bool CanAddWebSearchFindings(string? briefing)
        => !string.IsNullOrWhiteSpace(briefing) && briefing.Contains("\n## ", StringComparison.Ordinal) && !briefingBuilder.HasNewsSection(briefing);

    public async Task<AIResult> AddWebSearchFindings(BriefingParameters parameters, string briefing, string? city,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(briefing);

        if (briefingBuilder.HasNewsSection(briefing))
            return new AIResult(briefing, TotalTokens: 0, city);

        var location = city is null ? null : WebSearchLocation.ForCity(city);
        var tools = await new WebSearchToolProvider(location).GetToolsAsync(cancellationToken);
        var spec = briefingBuilder.BuildWebSearchAgentSpec(tools);

        // The briefing itself is the evidence, so the search looks for what it doesn't already cover.
        var webSearchResult = await agentRuntime.RunEphemeralAsync(spec, briefingBuilder.BuildWebSearchInstruction(parameters, city),
            evidence: briefingBuilder.BuildWebSearchEvidence(briefing), validateOutput: BriefingAgents.ValidateMarkdownOutput,
            cancellationToken: cancellationToken);

        return new AIResult(briefingBuilder.AppendNewsFindings(briefing, webSearchResult), webSearchResult.TotalTokens, city);
    }

    private async Task<AgentResult> RunSynthesisAsync(AgentDefinition synthesisAgent, BriefingParameters parameters,
        IReadOnlyCollection<AgentResult> specialistResults, CancellationToken cancellationToken)
    {
        var prompt = briefingBuilder.BuildSynthesisPrompt(parameters, specialistResults);
        return await agentService.RunAsync(synthesisAgent, prompt, cancellationToken: cancellationToken);
    }

    private static List<AgentDefinition> GetSpecialistAgents(IReadOnlyCollection<AgentDefinition> definitions, string mandatoryAgentName)
        => [.. definitions.Where(definition => definition.Name != mandatoryAgentName && definition.Name != AgentNames.Synthesis)];
}
