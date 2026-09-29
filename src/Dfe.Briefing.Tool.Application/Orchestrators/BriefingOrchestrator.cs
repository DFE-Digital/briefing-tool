using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Context;
using GovUK.Dfe.CoreLibs.AiAgents.Context.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Extensions;
using GovUK.Dfe.CoreLibs.AiAgents.Prompts.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using System.Net;

namespace Dfe.Briefing.Tool.Application.Orchestrators;

public class BriefingOrchestrator(IReadOnlyCollection<AgentDefinition> definitions, IContextRetriever contextRetriever,
    IPromptTemplateBuilder promptBuilder, IAgentService agentService) : IBriefingOrchestrator
{
    private static bool IsTrust(string schoolUrnOrTrustTrn) => schoolUrnOrTrustTrn.StartsWith("TR");

    public async Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var establihsmentOrTrustAgent = IsTrust(parameters.SchoolUrnOrTrustTrn)
            ? definitions.Single(x => x.SystemPromptType == SystemPromptType.Trust)
            : definitions.Single(x => x.SystemPromptType == SystemPromptType.Establishment);

        var specialistAgents = GetSpecialistAgents(definitions, establihsmentOrTrustAgent.Name);

        progress?.Report($"Looking up records for {parameters.SchoolOrTrustName}...");
        var establihsmentOrTrustEvidence = await ResolveEvidenceAsync(establihsmentOrTrustAgent, parameters, cancellationToken);
        if (!establihsmentOrTrustEvidence.HasEvidence)
            return BuildNoEvidenceResult(establihsmentOrTrustAgent, parameters);

        var context = new AgentContext();

        progress?.Report($"Calling {establihsmentOrTrustAgent.Name.ToDisplayName()} agent...");
        var establishmentOrTrustResults = await agentService.RunParallelAsync(
            [establihsmentOrTrustAgent],
            resolvePrompt: (_, _) => Task.FromResult(BuildSubjectInstruction(parameters)),
            context, shouldSuppress: _ => false, cancellationToken: cancellationToken,
            resolveEvidence: (_, _) => Task.FromResult<string?>(establihsmentOrTrustEvidence.Text));
        progress?.Report($"Received {establihsmentOrTrustAgent.Name.ToDisplayName()} information.");

        foreach (var definition in specialistAgents)
            progress?.Report($"Calling {definition.Name.ToDisplayName()} agent...");

        var specialistAgentResults = await agentService.RunParallelAsync(
            specialistAgents,
            resolvePrompt: (_, _) => Task.FromResult(BuildSubjectInstruction(parameters)),
            context, cancellationToken: cancellationToken,
            resolveEvidence: (definition, ct) => ResolveEvidenceTextAsync(definition, parameters, ct));
        progress?.Report("Received findings from all specialist agents.");

        var knownResults = establishmentOrTrustResults.Concat(specialistAgentResults).ToList();

        var specialistResults = knownResults;
        if (parameters.AllowWebSearch)
        {
            progress?.Report("Searching the web for additional information...");
            var webSearchAgent = definitions.Single(x => x.Name == AgentNames.WebSearch);
            var webSearchResults = await agentService.RunParallelAsync(
                [webSearchAgent],
                resolvePrompt: (_, _) => Task.FromResult(BuildWebSearchInstruction(parameters)),
                context, cancellationToken: cancellationToken,
                resolveEvidence: (_, _) => Task.FromResult<string?>(CompileContext(knownResults)));
            specialistResults = [.. knownResults, .. webSearchResults];
            progress?.Report("Received web search findings.");
        }

        progress?.Report("Compiling the briefing...");
        var synthesisAgent = definitions.Single(x => x.Name == AgentNames.Synthesis);
        var synthesisResult = await RunSynthesisAsync(synthesisAgent, parameters, specialistResults, cancellationToken);
        var totalTokens = specialistResults.Sum(x => x.TotalTokens) + synthesisResult.TotalTokens;

        return new AIResult(synthesisResult.Output, totalTokens);
    }

    private static string BuildSubjectInstruction(BriefingParameters parameters)
        => $"Analyse the available evidence about \"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn}) and report your findings.";

    private static string BuildWebSearchInstruction(BriefingParameters parameters)
    {
        var subject = $"\"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn})";
        return $"""
            We already have the internal-record findings supplied as evidence below about {subject}.
            Search the web for current, genuinely new, publicly available information about this subject that is not already covered by those findings. Do not repeat or re-confirm anything already listed.
            """;
    }

    private async Task<string?> ResolveEvidenceTextAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var evidence = await ResolveEvidenceAsync(definition, parameters, cancellationToken);
        return evidence.Text;
    }

    private static string CompileContext(IReadOnlyCollection<AgentResult> results)
        => string.Join(Environment.NewLine + Environment.NewLine, results.Select(
            result =>
                $"""
                ===================================
                AGENT: {result.AgentName.ToDisplayName()}
                ===================================

                {result.Output}
                """));

    private async Task<ContextResult> ResolveEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var (evidenceCategory, searchQueryTemplate) = AgentEvidenceQueries.For(definition.Name);

        var searchQuery = searchQueryTemplate
            .Replace("{{SchoolOrTrustName}}", parameters.SchoolOrTrustName, StringComparison.Ordinal)
            .Replace("{{SchoolUrnOrTrustTrn}}", parameters.SchoolUrnOrTrustTrn, StringComparison.Ordinal);

        return await contextRetriever.GetContextAsync(evidenceCategory, searchQuery, size: 10, cancellationToken: cancellationToken);
    }

    private static AIResult BuildNoEvidenceResult(AgentDefinition establihsmentOrTrustAgent, BriefingParameters parameters)
    {
        var message = establihsmentOrTrustAgent.SystemPromptType == SystemPromptType.Trust
            ? ErrorMessages.NoTrustInformationFound
            : ErrorMessages.NoSchoolOrAcademyFound;

        var name = WebUtility.HtmlEncode(parameters.SchoolOrTrustName);
        return new AIResult($"<h1>{name}</h1><p>{message}</p>", TotalTokens: 0);
    }

    private async Task<AgentResult> RunSynthesisAsync(AgentDefinition synthesisAgent, BriefingParameters parameters,
        IReadOnlyCollection<AgentResult> specialistResults, CancellationToken cancellationToken)
    {
        var prompt = promptBuilder.Build(UserPromptType.Synthesis, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["Context"] = CompileContext(specialistResults),
            ["IncludedSections"] = string.Join(", ", specialistResults.Select(result => result.AgentName.ToDisplayName()))
        });

        return await agentService.RunAsync(synthesisAgent, prompt, cancellationToken: cancellationToken);
    }

    private static List<AgentDefinition> GetSpecialistAgents(IReadOnlyCollection<AgentDefinition> definitions, string mandatoryAgentName)
        => [.. definitions.Where(definition => definition.Name != mandatoryAgentName
            && definition.Name != AgentNames.WebSearch && definition.Name != AgentNames.Synthesis)];
}
