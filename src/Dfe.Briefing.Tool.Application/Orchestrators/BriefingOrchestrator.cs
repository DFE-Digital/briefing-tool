using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.Prompts.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Tools.WebSearch;
using GovUK.Dfe.AI.Agents.ValueObjects; 

namespace Dfe.Briefing.Tool.Application.Orchestrators;

public partial class BriefingOrchestrator(IReadOnlyCollection<AgentDefinition> definitions, IContextRetriever contextRetriever,
    IPromptTemplateBuilder promptBuilder, IAgentService agentService, IAgentRuntimeService agentRuntime,
    IPromptProvider promptProvider, IBriefingBuilder briefingBuilder) : IBriefingOrchestrator
{
    private static bool IsTrust(string schoolUrnOrTrustTrn) => schoolUrnOrTrustTrn.StartsWith("TR");

    public async Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var establihsmentOrTrustAgent = IsTrust(parameters.SchoolUrnOrTrustTrn)
            ? definitions.Single(x => x.SystemPromptKey == SystemPromptKey.Trust)
            : definitions.Single(x => x.SystemPromptKey == SystemPromptKey.Establishment);

        var specialistAgents = GetSpecialistAgents(definitions, establihsmentOrTrustAgent.Name);

        progress?.Report($"Looking up records for {parameters.SchoolOrTrustName}...");
        var establihsmentOrTrustEvidence = await ResolveEvidenceAsync(establihsmentOrTrustAgent, parameters, cancellationToken);
        if (!establihsmentOrTrustEvidence.HasEvidence)
            return briefingBuilder.BuildNoEvidenceResult(establihsmentOrTrustAgent, parameters);

        var context = new AgentContext();

        progress?.Report($"Calling {establihsmentOrTrustAgent.Name.ToDisplayName()} agent...");
        var establishmentOrTrustResults = await agentService.RunParallelAsync(
            [establihsmentOrTrustAgent],
            resolvePrompt: (_, _) => Task.FromResult(briefingBuilder.BuildSubjectInstruction(parameters)),
            context, shouldSuppress: _ => false, cancellationToken: cancellationToken,
            resolveEvidence: (_, _) => Task.FromResult<string?>(establihsmentOrTrustEvidence.Text));
        progress?.Report($"Received {establihsmentOrTrustAgent.Name.ToDisplayName()} information.");

        var (subjectResult, city) = CityExtractorHelper.ExtractCity(establishmentOrTrustResults.Single());
        establishmentOrTrustResults = [subjectResult];

        foreach (var definition in specialistAgents)
            progress?.Report($"Calling {definition.Name.ToDisplayName()} agent...");

        var specialistAgentResults = await agentService.RunParallelAsync(
            specialistAgents,
            resolvePrompt: (_, _) => Task.FromResult(briefingBuilder.BuildSubjectInstruction(parameters)),
            context, cancellationToken: cancellationToken,
            resolveEvidence: (definition, ct) => ResolveEvidenceTextAsync(definition, parameters, ct));
        progress?.Report("Received findings from all specialist agents.");

        var knownResults = establishmentOrTrustResults.Concat(specialistAgentResults).ToList();

        var specialistResults = knownResults;
        if (parameters.AllowWebSearch)
        {
            progress?.Report("Searching the web for additional information...");
            var webSearchResult = await RunWebSearchAsync(parameters, city, knownResults, cancellationToken);
            specialistResults = [.. knownResults, webSearchResult];
            progress?.Report("Received web search findings.");
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

        var spec = new AgentSpec
        {
            Name = AgentNames.WebSearch,
            Instructions = promptProvider.GetSystemPrompt(SystemPromptKey.WebSearch),
            Tools = tools
        };

        return await agentRuntime.RunEphemeralAsync(spec, briefingBuilder.BuildWebSearchInstruction(parameters, city),
            evidence: briefingBuilder.CompileContext(knownResults), validateOutput: BriefingAgents.ValidateHtmlOutput,
            cancellationToken: cancellationToken);
    }

    private async Task<string?> ResolveEvidenceTextAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var evidence = await ResolveEvidenceAsync(definition, parameters, cancellationToken);
        return evidence.Text;
    }
    private async Task<ContextResult> ResolveEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var (evidenceCategory, searchQueryTemplate) = AgentEvidenceQueries.For(definition.Name);

        var searchQuery = searchQueryTemplate
            .Replace("{{SchoolOrTrustName}}", parameters.SchoolOrTrustName, StringComparison.Ordinal)
            .Replace("{{SchoolUrnOrTrustTrn}}", parameters.SchoolUrnOrTrustTrn, StringComparison.Ordinal);

        return await contextRetriever.GetContextAsync(evidenceCategory, searchQuery, size: 10, cancellationToken: cancellationToken);
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
