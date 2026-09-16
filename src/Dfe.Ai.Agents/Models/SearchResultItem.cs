namespace Dfe.Ai.Agents.Models;

/// <summary>
/// Represents a search result item.
/// </summary>
/// <param name="Content">The result content.</param>
/// <param name="Score">The optional relevance score.</param>
public sealed record SearchResultItem(string Content, double? Score = null);
