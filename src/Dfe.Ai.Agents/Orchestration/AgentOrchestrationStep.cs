using Dfe.Ai.Agents.Models;

namespace Dfe.Ai.Agents.Orchestration;

/// <summary>
/// Defines an agent orchestration step.
/// </summary>
/// <param name="Spec">The agent to run.</param>
/// <param name="ResolvePrompt">Resolves the prompt before execution.</param>
public sealed record AgentOrchestrationStep(AgentSpec Spec, Func<CancellationToken, Task<string>> ResolvePrompt);
