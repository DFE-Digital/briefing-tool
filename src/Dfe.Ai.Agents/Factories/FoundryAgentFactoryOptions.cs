using Dfe.Ai.Agents.Models;

namespace Dfe.Ai.Agents.Factories;

/// <summary>
/// Options for configuring the Foundry agent factory.
/// </summary>
/// <param name="DefaultModel">
/// The default model deployment used when <see cref="AgentSpec.ModelOverride"/> is not set.
/// </param>
public sealed record FoundryAgentFactoryOptions(string DefaultModel);
