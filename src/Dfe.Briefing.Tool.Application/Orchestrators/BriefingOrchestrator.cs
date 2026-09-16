using Azure.AI.Projects.Agents;
using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Context;
using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Models;
using Dfe.Ai.Agents.Orchestration;
using Dfe.Ai.Agents.Prompts.Interfaces;
using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.Exceptions;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Orchestrators;

public class BriefingOrchestrator(IAgentDefinitionProvider definitionProvider, IContextRetriever contextRetriever, IPromptRetrieverService promptRetrieverService,
    IPromptTemplateBuilder promptBuilder, IAgentRunner agentRunner, AgentOrchestrator orchestrator) : IBriefingOrchestrator
{
    private static bool IsSchoolUrn(string schoolUrnOrTrustTrn) => schoolUrnOrTrustTrn.Length == 6 && schoolUrnOrTrustTrn.All(char.IsDigit);
    public async Task<AIResult> GetBriefing(BriefingParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolUrnOrTrustTrn);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.SchoolOrTrustName);

        var definitions = definitionProvider.GetAgentsDefinitions();
        AgentDefinition establihsmentOrTrustAgent;
        if (IsSchoolUrn(parameters.SchoolUrnOrTrustTrn))
        {
            establihsmentOrTrustAgent = definitions.Single(x => x.SystemPromptType == SystemPromptType.Establishment);
        }
        else
        {
            establihsmentOrTrustAgent = definitions.Single(x => x.SystemPromptType == SystemPromptType.Trust);
        }
         
        var specialistAgents = GetSpecialistAgents(definitions, establihsmentOrTrustAgent.Name);

        var establihsmentOrTrustEvidence = await ResolveEvidenceAsync(establihsmentOrTrustAgent, parameters, cancellationToken);
        if (!establihsmentOrTrustEvidence.HasEvidence)
        {
            throw new NoBriefingDataFoundException(
                string.Format(ErrorMessages.NoEstablishmentDataFound, parameters.SchoolOrTrustName));
        }

        var context = new AgentContext();

        var establishmentOrTrustOutcome = await orchestrator.RunParallelAsync(
            [BuildStep(establihsmentOrTrustAgent, parameters, establihsmentOrTrustEvidence.Text)], context, shouldSuppress: _ => false, cancellationToken: cancellationToken);

        var specialistAgentsOutcome = await orchestrator.RunParallelAsync(
            [.. specialistAgents.Select(definition => BuildStep(definition, parameters))],
            context, cancellationToken: cancellationToken);

        var specialistResults = establishmentOrTrustOutcome.Results.Concat(specialistAgentsOutcome.Results)
            .Select(ToAgentResult)
            .ToList(); 
        var synthesisResult = await RunSynthesisAsync(parameters, specialistResults, cancellationToken);
        var totalTokens = specialistResults.Sum(x => x.TotalTokens) + synthesisResult.TotalTokens;

        return new AIResult(synthesisResult.Output, totalTokens);
    }

    private static AgentResult ToAgentResult(AgentStepResult step)
        => step.Result ?? new AgentResult(step.AgentName,
            "This section could not be generated due to an error retrieving or analysing evidence.",
            TotalTokens: 0);

    private AgentOrchestrationStep BuildStep(AgentDefinition definition, BriefingParameters parameters, string? evidence = null)
    {
        var agentSpec = new AgentSpec
        {
            Name = definition.Name,
            Instructions = promptRetrieverService.GetSystemPrompt(definition.SystemPromptType)
        };

        // The Establishment step's evidence is already resolved (GetBriefing needs it up front to
        // check for "no data"), so reuse it instead of querying the search index a second time.
        Func<CancellationToken, Task<string>> resolvePrompt = evidence is not null
            ? _ => Task.FromResult(BuildEvidencePrompt(parameters, evidence))
            : ct => ResolveEvidencePromptAsync(definition, parameters, ct);

        return new AgentOrchestrationStep(agentSpec, resolvePrompt);
    }

    private async Task<string> ResolveEvidencePromptAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var evidence = await ResolveEvidenceAsync(definition, parameters, cancellationToken);
        return BuildEvidencePrompt(parameters, evidence.Text);
    }

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

    // No per-agent prompt template needed here: the system instructions (cached on the Foundry
    // agent) already say how to analyse it. This just supplies what changes per call.
    private static string BuildEvidencePrompt(BriefingParameters parameters, string evidence)
        => $"Academy: {parameters.SchoolOrTrustName}{Environment.NewLine}{Environment.NewLine}Evidence:{Environment.NewLine}{evidence}";

    private async Task<AgentResult> RunSynthesisAsync(BriefingParameters parameters,
        IReadOnlyCollection<AgentResult> specialistResults, CancellationToken cancellationToken)
    {
        var definition = new AgentDefinition(AgentNames.Synthesis, SystemPromptType.Synthesis);
        var context = string.Join( Environment.NewLine + Environment.NewLine, specialistResults.Select(
            result =>
                $"""
                ===================================
                AGENT: {result.AgentName}
                ===================================

                {result.Output}
                """));

        // Synthesis needs which sections actually ran and every specialist's output, so it
        // keeps its own file-based template rather than the plain data envelope specialists use.
        var prompt = promptBuilder.Build(UserPromptType.Synthesis, new Dictionary<string, string>
        {
            ["SchoolOrTrustName"] = parameters.SchoolOrTrustName,
            ["SchoolUrnOrTrustTrn"] = parameters.SchoolUrnOrTrustTrn,
            ["Context"] = context,
            ["IncludedSections"] = string.Join(", ", specialistResults.Select(result => result.AgentName))
        });

        return await RunAgentAsync(definition.Name, definition.SystemPromptType, prompt, cancellationToken);
    }
    private async Task<AgentResult> RunAgentAsync(string agentName, string systemPromptType,
        string prompt, CancellationToken cancellationToken)
    {
        var systemInstructions = promptRetrieverService.GetSystemPrompt(systemPromptType);
        var spec = new AgentSpec { Name = agentName, Instructions = systemInstructions };

        return await agentRunner.RunAsync(spec, prompt, cancellationToken: cancellationToken);
    }
    private static List<AgentDefinition> GetSpecialistAgents(IReadOnlyCollection<AgentDefinition> definitions, string mandatoryAgentName)
        => [.. definitions.Where(definition => definition.Name != mandatoryAgentName)];
}
