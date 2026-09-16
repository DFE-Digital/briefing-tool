namespace Dfe.Ai.Agents.Models;

/// <summary>
/// Defines an agent for orchestration by <see cref="AgentOrchestrator"/>.
/// </summary>
/// <param name="Name">The agent name.</param>
/// <param name="SystemPromptType">The system prompt type.</param>
/// <param name="EvidenceCategory">The evidence category for specialist agents.</param>
/// <param name="SearchQueryTemplate">The search query template for specialist agents.</param>
public sealed record AgentDefinition(string Name, string SystemPromptType, string? EvidenceCategory = null, string? SearchQueryTemplate = null);
