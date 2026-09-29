using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;

namespace Dfe.Briefing.Tool.Application.Providers;

internal static class AgentEvidenceQueries
{
    private static readonly Dictionary<string, (string EvidenceCategory, string SearchQueryTemplate)> Queries = new()
    {
        [AgentNames.Establishment] = (EvidenceCategory.Establishment, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}}"),
        [AgentNames.Ofsted] = (EvidenceCategory.Ofsted, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} Ofsted inspection"),
        [AgentNames.RiseConcerns] = (EvidenceCategory.RiseConcerns, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} RISE concerns"),
        [AgentNames.RecastConcerns] = (EvidenceCategory.RecastConcerns, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} RECAST concerns"),
        [AgentNames.Trust] = (EvidenceCategory.Trust, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}}'s Trust data"),
    };

    public static (string EvidenceCategory, string SearchQueryTemplate) For(string agentName) =>
        Queries.TryGetValue(agentName, out var query)
            ? query
            : throw new InvalidOperationException(string.Format(ErrorMessages.NoEvidenceQueryConfigured, agentName));
}
