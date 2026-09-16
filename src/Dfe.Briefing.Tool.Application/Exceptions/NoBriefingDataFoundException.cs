namespace Dfe.Briefing.Tool.Application.Exceptions;

/// <summary>
/// Thrown when the mandatory Establishment agent has no evidence to work from, so a briefing
/// cannot be generated. Callers should treat this distinctly from a generic failure - it's a
/// data-availability outcome, not an error.
/// </summary>
public sealed class NoBriefingDataFoundException(string message) : Exception(message);
