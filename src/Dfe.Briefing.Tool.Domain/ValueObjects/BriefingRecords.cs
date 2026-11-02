using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Domain.ValueObjects;

/// <summary>The records a briefing is written from, each from its own search.</summary>
/// <param name="Subject">The school's or trust's own record.</param>
/// <param name="Related">For a school, its trust's record; for a trust, its academies' records.</param>
/// <param name="RiseConcerns">Rise concerns and support about the school or trust.</param>
/// <param name="RecastConcerns">Recast concerns and support about the school or trust.</param>
public sealed record BriefingRecords(ContextResult Subject, ContextResult Related, ContextResult RiseConcerns, ContextResult RecastConcerns);
