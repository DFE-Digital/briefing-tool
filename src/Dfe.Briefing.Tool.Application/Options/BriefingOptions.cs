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
    /// Gets how many seconds old a temporary agent must be before the orphan agent sweep deletes it (Default 7200). The AI
    /// agent library refuses less than AiAgents:RunTimeout plus 90 seconds, as a run could still be using the agent.
    /// </summary>
    public int OrphanAgentMinimumAge { get; init; } = 7200;

    /// <summary>
    /// Enables the agent version sweep, which deletes old versions of briefing agents in Foundry (Default true), keeping only the latest N versions.
    /// </summary>
    public bool AgentVersionSweepEnabled { get; init; } = true;

    /// <summary>
    /// Gets the day of the week (UTC) the agent version sweep runs on, at <see cref="AgentSweepTime"/>; when not set, it runs
    /// every day. A new version is only made when an agent's prompt, model, tools or guardrail changes - a deployment, so
    /// weekly is enough. The orphan agent sweep runs every day whatever this is.
    /// </summary>
    public DayOfWeek? AgentVersionSweepDay { get; init; }

    /// <summary>
    /// Gets the time of day (UTC) when the daily agent sweep runs the enabled sweeps.
    /// </summary>
    public TimeOnly AgentSweepTime { get; init; } = new(1, 0);
}
