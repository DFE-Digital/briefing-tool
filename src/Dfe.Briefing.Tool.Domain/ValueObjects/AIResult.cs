namespace Dfe.Briefing.Tool.Domain.ValueObjects;

/// <param name="City">The town or city the school or trust is in, when known - used to focus a later web search.</param>
public record AIResult(string? Output, long TotalTokens, string? City = null);
