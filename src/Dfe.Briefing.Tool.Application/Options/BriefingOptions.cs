namespace Dfe.Briefing.Tool.Application.Options;

public sealed class BriefingOptions
{
    public const string SectionName = "Briefing";

    /// <summary>
    /// Gets the maximum number of evidence items allowed per search.
    /// </summary>
    public int MaxEvidencePerSearch { get; init; } = 25;

    /// <summary>
    /// Gets the maximum number of academies allowed per trust.
    /// </summary>
    public int MaxAcademiesPerTrust { get; init; } = 100;

    /// <summary>
    /// Enables the orphan agent sweep, which deletes orphaned temporary agents (Default true).
    /// </summary>
    public bool OrphanAgentSweepEnabled { get; init; } = true;

    /// <summary>
    /// Gets the time of day when the orphan agent sweep operation is scheduled to run.
    /// </summary>
    public TimeOnly OrphanAgentSweepTime { get; init; } = new(1, 0);

    /// <summary>
    /// Enables the agent version sweep, which deletes old versions of briefing agents in Foundry (Default true), keeping only the latest N versions.
    /// </summary>
    public bool AgentVersionSweepEnabled { get; init; } = true;

    /// <summary>
    /// Gets the time of day when the agent version sweep occurs.
    /// </summary>
    public TimeOnly AgentVersionSweepTime { get; init; } = new(2, 0);
}
