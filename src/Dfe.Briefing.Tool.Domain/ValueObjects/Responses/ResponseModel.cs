namespace Dfe.Briefing.Tool.Domain.ValueObjects.Responses;

public record ResponseModel(string? Results = null, long? TotalCount = 0, string? Error = null);
