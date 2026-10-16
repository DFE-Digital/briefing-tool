using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Tools.WebSearch;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators;

public partial class BriefingOrchestrator(IReadOnlyCollection<AgentDefinition> definitions, IBriefingEvidenceProvider evidenceProvider,
    IPromptTemplateBuilder promptBuilder, IAgentService agentService, IAgentRuntimeService agentRuntime,
    IBriefingBuilder briefingBuilder) : IBriefingOrchestrator
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

        // Each progress message is one step, shown as in progress until the next one arrives - so agents that
        // run in parallel are announced together, as a single step.
        progress?.Report($"Calling {string.Join(", ", specialistAgents.Select(definition => definition.Name.ToDisplayName()))} agents...");

        var specialistAgentResults = await agentService.RunParallelAsync(
            specialistAgents,
            resolvePrompt: (_, _) => Task.FromResult(briefingBuilder.BuildSubjectInstruction(parameters)),
            context, cancellationToken: cancellationToken,
            resolveEvidence: async (definition, ct) => await evidenceProvider.GetEvidenceAsync(definition, parameters, establihsmentOrTrustEvidence, ct));

        // The Establishment and Trust prompts both emit a city marker, and either can run as a specialist.
        var strippedSpecialistResults = specialistAgentResults.Select(result => CityExtractorHelper.ExtractCity(result).Result);
        var knownResults = establishmentOrTrustResults.Concat(strippedSpecialistResults).ToList();

        var specialistResults = knownResults;
        if (parameters.AllowWebSearch)
        {
            progress?.Report("Searching the web for additional information...");
            var webSearchResult = await RunWebSearchAsync(parameters, city, knownResults, cancellationToken);
            specialistResults = [.. knownResults, webSearchResult];
        }

        progress?.Report("Compiling the briefing...");
        var synthesisAgent = definitions.Single(x => x.Name == AgentNames.Synthesis);
        var synthesisResult = await RunSynthesisAsync(synthesisAgent, parameters, specialistResults, cancellationToken);
        var totalTokens = specialistResults.Sum(x => x.TotalTokens) + synthesisResult.TotalTokens;

        return new AIResult(synthesisResult.Output, totalTokens);
    } 

    private async Task<AgentResult> RunWebSearchAsync(BriefingParameters parameters, string? city,
        IReadOnlyCollection<AgentResult> knownResults, CancellationToken cancellationToken)
    {
        var location = city is null ? null : WebSearchLocation.ForCity(city);
        var tools = await new WebSearchToolProvider(location).GetToolsAsync(cancellationToken);

        var spec = briefingBuilder.BuildWebSearchAgentSpec(tools);

        return await agentRuntime.RunEphemeralAsync(spec, briefingBuilder.BuildWebSearchInstruction(parameters, city),
            evidence: briefingBuilder.CompileContext(knownResults), validateOutput: BriefingAgents.ValidateMarkdownOutput,
            cancellationToken: cancellationToken);
    }

    private async Task<AgentResult> RunSynthesisAsync(AgentDefinition synthesisAgent, BriefingParameters parameters,
        IReadOnlyCollection<AgentResult> specialistResults, CancellationToken cancellationToken)
    {
        var prompt = promptBuilder.Build(UserPromptType.Synthesis, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["Context"] = briefingBuilder.CompileContext(specialistResults),
            ["IncludedSections"] = string.Join(", ", specialistResults.Select(result => result.AgentName.ToDisplayName()))
        });

        return await agentService.RunAsync(synthesisAgent, prompt, cancellationToken: cancellationToken);
    }

    private static List<AgentDefinition> GetSpecialistAgents(IReadOnlyCollection<AgentDefinition> definitions, string mandatoryAgentName)
        => [.. definitions.Where(definition => definition.Name != mandatoryAgentName && definition.Name != AgentNames.Synthesis)];
}
