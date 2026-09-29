using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
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

public class BriefingOrchestrator(IAgentDefinitionProvider definitionProvider, IContextRetriever contextRetriever,
    IPromptProvider promptProvider, IPromptTemplateBuilder promptBuilder, IAgentRuntime agentRuntime,
    ISpecialistAgentRunner specialistAgentRunner) : IBriefingOrchestrator
{
    private static readonly AgentDefinition WebSearchAgentDefinition =
        new(AgentNames.WebSearch, SystemPromptType.WebSearch, IsManagedAgent: false);

    private static bool IsTrust(string schoolUrnOrTrustTrn) => schoolUrnOrTrustTrn.StartsWith("TR");

    public async Task<AIResult> GetBriefing(BriefingParameters parameters, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var definitions = definitionProvider.GetAgentsDefinitions();
        AgentDefinition establihsmentOrTrustAgent;
        if (IsTrust(parameters.SchoolUrnOrTrustTrn))
        {
            establihsmentOrTrustAgent = definitions.Single(x => x.SystemPromptType == SystemPromptType.Trust);
        }
        else
        {
            establihsmentOrTrustAgent = definitions.Single(x => x.SystemPromptType == SystemPromptType.Establishment);
        }

        var specialistAgents = GetSpecialistAgents(definitions, establihsmentOrTrustAgent.Name);

        progress?.Report($"Looking up records for {parameters.SchoolOrTrustName}...");
        var establihsmentOrTrustEvidence = await ResolveEvidenceAsync(establihsmentOrTrustAgent, parameters, cancellationToken);
        if (!establihsmentOrTrustEvidence.HasEvidence)
        {
            return BuildNoEvidenceResult(establihsmentOrTrustAgent, parameters);
        }

        var context = new AgentContext(); 

        progress?.Report($"Calling {establihsmentOrTrustAgent.Name.ToDisplayName()} agent...");
        var establishmentOrTrustResults = await specialistAgentRunner.RunParallelAsync(
            [establihsmentOrTrustAgent],
            (_, _) => Task.FromResult(BuildEvidence(parameters.SchoolOrTrustName, establihsmentOrTrustEvidence.Text)),
            context, shouldSuppress: _ => false, cancellationToken: cancellationToken);
        progress?.Report($"Received {establihsmentOrTrustAgent.Name.ToDisplayName()} information.");

        foreach (var definition in specialistAgents)
        {
            progress?.Report($"Calling {definition.Name.ToDisplayName()} agent...");
        }

        var specialistAgentResults = await specialistAgentRunner.RunParallelAsync(
            specialistAgents, (definition, ct) => ResolveEvidencePromptAsync(definition, parameters, ct),
            context, cancellationToken: cancellationToken);
        progress?.Report("Received findings from all specialist agents.");

        var knownResults = establishmentOrTrustResults.Concat(specialistAgentResults).ToList();

        var specialistResults = knownResults;
        if (parameters.AllowWebSearch)
        {
            progress?.Report("Searching the web for additional information...");
            var webSearchResults = await specialistAgentRunner.RunParallelAsync(
                [WebSearchAgentDefinition], (_, _) => Task.FromResult(BuildWebSearchPrompt(parameters, knownResults)),
                context, cancellationToken: cancellationToken);
            specialistResults = [.. knownResults, .. webSearchResults];
            progress?.Report("Received web search findings.");
        }

        progress?.Report("Compiling the briefing...");
        var synthesisResult = await RunSynthesisAsync(parameters, specialistResults, cancellationToken);
        var totalTokens = specialistResults.Sum(x => x.TotalTokens) + synthesisResult.TotalTokens;

        return new AIResult(synthesisResult.Output, totalTokens);
    }
    private static string BuildEvidence(string subjectName, string evidence)
        => $"Academy: {subjectName}{Environment.NewLine}{Environment.NewLine}Evidence:{Environment.NewLine}{evidence}";

    private static string BuildWebSearchPrompt(BriefingParameters parameters, IReadOnlyCollection<AgentResult> alreadyKnownResults)
    {
        var subject = $"\"{parameters.SchoolOrTrustName}\" (URN/TRN: {parameters.SchoolUrnOrTrustTrn})";
        return $"""
            We already have the following information about {subject} from internal records:

            {CompileContext(alreadyKnownResults)}

            Search the web for current, genuinely new, publicly available information about this subject that is not already covered above. Do not repeat or re-confirm anything already listed.
            """;
    }

    private async Task<string> ResolveEvidencePromptAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var evidence = await ResolveEvidenceAsync(definition, parameters, cancellationToken);
        return BuildEvidence(parameters.SchoolOrTrustName, evidence.Text);
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
        var evidenceCategory = definition.EvidenceCategory
            ?? throw new InvalidOperationException(string.Format(ErrorMessages.NoEvidenceCategoryConfigured, definition.Name));
        var searchQueryTemplate = definition.SearchQueryTemplate
            ?? throw new InvalidOperationException(string.Format(ErrorMessages.NoSearchQueryConfigured, definition.Name));

        var searchQuery = searchQueryTemplate
            .Replace("{{SchoolOrTrustName}}", parameters.SchoolOrTrustName, StringComparison.Ordinal)
            .Replace("{{SchoolUrnOrTrustTrn}}", parameters.SchoolUrnOrTrustTrn, StringComparison.Ordinal);

        return await contextRetriever.GetContextAsync(evidenceCategory, searchQuery, 10, cancellationToken);
    }

    private static AIResult BuildNoEvidenceResult(AgentDefinition establihsmentOrTrustAgent, BriefingParameters parameters)
    {
        var message = establihsmentOrTrustAgent.SystemPromptType == SystemPromptType.Trust
            ? ErrorMessages.NoTrustInformationFound
            : ErrorMessages.NoSchoolOrAcademyFound;

        var name = WebUtility.HtmlEncode(parameters.SchoolOrTrustName);
        return new AIResult($"<h1>{name}</h1><p>{message}</p>", TotalTokens: 0);
    }

    private async Task<AgentResult> RunSynthesisAsync(BriefingParameters parameters,
        IReadOnlyCollection<AgentResult> specialistResults, CancellationToken cancellationToken)
    {
        var prompt = promptBuilder.Build(UserPromptType.Synthesis, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["Context"] = CompileContext(specialistResults),
            ["IncludedSections"] = string.Join(", ", specialistResults.Select(result => result.AgentName.ToDisplayName()))
        });

        var spec = new AgentSpec
        {
            Name = AgentNames.Synthesis,
            Instructions = promptProvider.GetSystemPrompt(SystemPromptType.Synthesis),
        };

        return await agentRuntime.RunEphemeralAsync(spec, prompt, cancellationToken);
    }

    private static List<AgentDefinition> GetSpecialistAgents(IReadOnlyCollection<AgentDefinition> definitions, string mandatoryAgentName)
        => [.. definitions.Where(definition => definition.Name != mandatoryAgentName)];
}
