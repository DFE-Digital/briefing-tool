namespace Dfe.Ai.Agents.Models;

/// <summary>
/// Represents the result of a context lookup.
/// </summary>
/// <param name="Text">The formatted context text.</param>
/// <param name="HasEvidence">Indicates whether evidence was found.</param>
public sealed record ContextResult(string Text, bool HasEvidence);
