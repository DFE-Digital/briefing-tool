namespace Dfe.Ai.Agents.Models;

/// <summary>
/// Represents an agent reference.
/// </summary>
/// <param name="Id">The unique agent identifier.</param>
/// <param name="Name">The agent name.</param>
public sealed record AgentReference(string Id, string Name);
