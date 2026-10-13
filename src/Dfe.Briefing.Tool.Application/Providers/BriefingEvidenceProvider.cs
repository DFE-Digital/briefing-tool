using Dfe.Briefing.Tool.Application.Options;
using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Context.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Providers;

internal class BriefingEvidenceProvider(IContextRetriever contextRetriever, BriefingOptions options) : IBriefingEvidenceProvider
{
    public Task<ContextResult> GetEvidenceAsync(AgentDefinition definition, BriefingParameters parameters, CancellationToken cancellationToken)
    {
        var (evidenceCategory, searchQueryTemplate) = AgentEvidenceQueries.For(definition.Name);

        var searchQuery = searchQueryTemplate
            .Replace("{{SchoolOrTrustName}}", parameters.SchoolOrTrustName, StringComparison.Ordinal)
            .Replace("{{SchoolUrnOrTrustTrn}}", parameters.SchoolUrnOrTrustTrn, StringComparison.Ordinal);

        return contextRetriever.GetContextAsync(evidenceCategory, searchQuery, size: options.MaxEvidencePerSearch,
            cancellationToken: cancellationToken);
    }
}
