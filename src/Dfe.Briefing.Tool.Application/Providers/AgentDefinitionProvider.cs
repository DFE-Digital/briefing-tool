using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;

namespace Dfe.Briefing.Tool.Application.Providers
{
    public sealed class AgentDefinitionProvider : IAgentDefinitionProvider
    {
        private static readonly IReadOnlyCollection<AgentDefinition>
            Definitions =
            [
                new(AgentNames.Establishment, SystemPromptType.Establishment,
                    EvidenceCategory.Establishment, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}}"),
                new(AgentNames.Ofsted, SystemPromptType.Ofsted,
                    EvidenceCategory.Ofsted, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} Ofsted inspection"),
                new(AgentNames.RiseConcerns, SystemPromptType.RiseConcerns,
                    EvidenceCategory.RiseConcerns, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} RISE concerns",
                    IsManagedAgent: false),
                new(AgentNames.RecastConcerns, SystemPromptType.RecastConcerns,
                    EvidenceCategory.RecastConcerns, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}} RECAST concerns",
                    IsManagedAgent: false),
                new(AgentNames.Trust, SystemPromptType.Trust,
                    EvidenceCategory.Trust, "{{SchoolOrTrustName}} {{SchoolUrnOrTrustTrn}}'s Trust data")
            ]; 
        public IReadOnlyCollection<AgentDefinition> GetAgentsDefinitions() => Definitions;
    }
}
