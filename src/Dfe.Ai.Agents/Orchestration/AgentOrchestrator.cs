using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Context;
using Dfe.Ai.Agents.Models;
using Dfe.Ai.Agents.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.Ai.Agents.Orchestration;

/// <summary>
/// Orchestrates sequential and parallel execution of multiple agents.
/// </summary>
/// <param name="agentRunner">The runner for executing individual agents.</param>
/// <param name="logger">Optional logger for orchestration errors.</param>
public sealed class AgentOrchestrator(IAgentRunner agentRunner, ILogger<AgentOrchestrator>? logger = null)
{
    private readonly ILogger<AgentOrchestrator> _logger = logger ?? NullLogger<AgentOrchestrator>.Instance;

    /// <summary>
    /// Runs agents sequentially, passing each successful output to the next.
    /// </summary>
    /// <param name="agents">The list of agent specifications to run.</param>
    /// <param name="initialInput">The initial input for the first agent.</param>
    /// <param name="context">The context for the agents.</param>
    /// <param name="shouldSuppress">Determines which exceptions to suppress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the orchestration.</returns>
    public async Task<OrchestrationResult> RunSequentialAsync(IReadOnlyList<AgentSpec> agents, string initialInput,
        AgentContext context, Func<Exception, bool>? shouldSuppress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<AgentStepResult>(agents.Count);
        var currentInput = initialInput;
        string? lastOutput = null;

        foreach (var agentSpec in agents)
        {
            var input = currentInput;
            var step = await RunStepAsync(agentSpec, _ => Task.FromResult(input), context, true, shouldSuppress, cancellationToken);
            results.Add(step);

            if (step.Succeeded)
            {
                lastOutput = step.Result!.Output;
                currentInput = lastOutput ?? currentInput;
            }
        }

        return new OrchestrationResult(lastOutput, results);
    }

    /// <summary> 
    /// Runs agents concurrently using the same input.
    /// </summary> 
    /// <param name="agents">The agents to run.</param>
    /// <param name="input">The input for each agent.</param> 
    /// <param name="context">The agent context.</param>
    /// <param name="maxConcurrency">The maximum number of concurrent agents.</param>
    /// <param name="shouldSuppress">Determines which exceptions to suppress.</param> 
    /// <param name="cancellationToken">The cancellation token.</param> 
    /// <returns>The orchestration result.</returns>
    public Task<OrchestrationResult> RunParallelAsync(IReadOnlyList<AgentSpec> agents, string input,
        AgentContext context, int? maxConcurrency = null, Func<Exception, bool>? shouldSuppress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var steps = agents.Select(spec => new AgentOrchestrationStep(spec, _ => Task.FromResult(input))).ToList();
        return RunParallelAsync(steps, context, maxConcurrency, shouldSuppress, cancellationToken);
    }

    /// <summary> 
    /// Runs orchestration steps concurrently, resolving each prompt independently.
    /// </summary> 
    /// <param name="steps">The orchestration steps to run.</param> 
    /// <param name="context">The agent context.</param>
    /// <param name="maxConcurrency">The maximum number of concurrent steps.</param>
    /// <param name="shouldSuppress">Determines which exceptions to suppress.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The orchestration result.</returns>
    public async Task<OrchestrationResult> RunParallelAsync(IReadOnlyList<AgentOrchestrationStep> steps,
        AgentContext context, int? maxConcurrency = null, Func<Exception, bool>? shouldSuppress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(context);

        using var semaphore = maxConcurrency is int max ? new SemaphoreSlim(max, max) : null;

        var tasks = steps.Select(step => RunBoundedStepAsync(step, context, semaphore, shouldSuppress, cancellationToken));
        var results = await Task.WhenAll(tasks);

        var finalOutput = string.Join(Environment.NewLine + Environment.NewLine,
            results.Where(r => r.Succeeded).Select(r => r.Result!.Output));

        return new OrchestrationResult(finalOutput, results);
    }

    /// <summary>
    /// Runs a single step with optional concurrency control.
    /// </summary>
    /// <param name="agentOrchestrationStep">The agent step to run.</param>
    /// <param name="context">The context for the agent step.</param>
    /// <param name="semaphore">The semaphore for concurrency control.</param>
    /// <param name="shouldSuppress">A function to determine whether to suppress exceptions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the agent step.</returns>
    private async Task<AgentStepResult> RunBoundedStepAsync(AgentOrchestrationStep agentOrchestrationStep, AgentContext context,
        SemaphoreSlim? semaphore, Func<Exception, bool>? shouldSuppress, CancellationToken cancellationToken)
    {
        if (semaphore is not null)
        {
            await semaphore.WaitAsync(cancellationToken);
        }

        try
        {
            return await RunStepAsync(agentOrchestrationStep.Spec, agentOrchestrationStep.ResolvePrompt,
                context, false, shouldSuppress, cancellationToken);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    /// <summary>
    /// Resolves the prompt and executes a single agent step.
    /// </summary>
    /// <param name="agentSpec">The specification for the agent step.</param>
    /// <param name="resolvePrompt">A function to resolve the prompt for the agent step.</param>
    /// <param name="context">The context for the agent step.</param>
    /// <param name="prependContext">A value indicating whether to prepend the context to the prompt.</param>
    /// <param name="shouldSuppress">A function to determine whether to suppress exceptions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    private async Task<AgentStepResult> RunStepAsync(AgentSpec agentSpec, Func<CancellationToken, Task<string>> resolvePrompt,
        AgentContext context, bool prependContext, Func<Exception, bool>? shouldSuppress, CancellationToken cancellationToken)
        => await ResilientAgentStep.ExecuteAsync(
            step: async () =>
            {
                var rawPrompt = await resolvePrompt(cancellationToken);
                var prompt = prependContext ? BuildPrompt(rawPrompt, context) : rawPrompt;
                var result = await agentRunner.RunAsync(agentSpec, prompt, cancellationToken: cancellationToken);
                 
                if (prependContext)
                {
                    context.AddHistory(new AgentContextEntry(agentSpec.Name, prompt, result.Output));
                }

                return new AgentStepResult(agentSpec.Name, result, Error: null);
            },
            fallback: ex =>
            {
                _logger.LogError(ex, "Agent step {AgentName} failed; recording failure and continuing", agentSpec.Name);
                return new AgentStepResult(agentSpec.Name, Result: null, ex);
            },
            shouldSuppress: shouldSuppress);

    private static string BuildPrompt(string input, AgentContext context)
    { 
        var contextPrompt = context.BuildContextPrompt();
        return string.IsNullOrEmpty(contextPrompt) ? input : $"{contextPrompt}{Environment.NewLine}{Environment.NewLine}{input}";
    }
}
