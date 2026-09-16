using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Constants;
using Dfe.Ai.Agents.Factories;
using Dfe.Ai.Agents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Responses;

namespace Dfe.Ai.Agents.Agents;

public sealed class FoundryAgentRunner(IAgentFactory agentFactory, IFoundryConversationClient conversationClient,
    ILogger<FoundryAgentRunner>? logger = null) : IAgentRunner
{
    /// <summary>
    /// The maximum number of tool-call rounds allowed per agent run.
    /// </summary>
    private const int MaxToolCallRounds = 10;

    private readonly ILogger<FoundryAgentRunner> _logger = logger ?? NullLogger<FoundryAgentRunner>.Instance;

    public async Task<AgentResult> RunAsync(AgentSpec spec, string prompt, string? conversationId = null,
        Func<IReadOnlyList<ToolCallRequest>, CancellationToken, Task<IEnumerable<ToolCallOutput>>>? resolveToolCalls = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        cancellationToken.ThrowIfCancellationRequested();

        var agent = await agentFactory.GetOrCreateAsync(spec, cancellationToken).ConfigureAwait(false);
        var resolvedConversationId = conversationId
            ?? await conversationClient.CreateConversationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyList<ResponseItem> inputItems = [ResponseItem.CreateUserMessageItem(prompt)];
            ResponseResult response;
            var round = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++round > MaxToolCallRounds)
                {
                    throw new InvalidOperationException(
                        string.Format(ErrorMessages.ToolCallRoundLimitExceeded, agent.Name, MaxToolCallRounds));
                }

                response = await conversationClient.CreateResponseAsync(agent.Name, resolvedConversationId, inputItems, cancellationToken)
                    .ConfigureAwait(false);

                var toolCalls = response.OutputItems.OfType<FunctionCallResponseItem>().ToList();
                if (toolCalls.Count == 0)
                {
                    ThrowIfNotCompleted(agent.Name, response);
                    break;
                }

                inputItems = await ResolveToolCallsAsync(response, toolCalls, resolveToolCalls, cancellationToken).ConfigureAwait(false);
            }

            var output = response.GetOutputText();
            var totalTokens = response.Usage?.TotalTokenCount ?? 0;

            return new AgentResult(agent.Name, output, totalTokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Agent {AgentName} failed", agent.Name);
            throw;
        }
    }

    // A response with no function calls is normally the model's final answer, but a response that
    // paused on something else this runner doesn't resolve (an MCP approval request, a
    // content-filtered or truncated result, etc.) also has no function calls - without this check
    // that would be silently treated as a successful, if empty or wrong, answer.
    private static void ThrowIfNotCompleted(string agentName, ResponseResult response)
    {
        if (response.Status is null || response.Status == ResponseStatus.Completed)
        {
            return;
        }

        var detail = response.Error is not null
            ? $": {response.Error.Message}"
            : response.IncompleteStatusDetails?.Reason is { } reason
                ? $": {reason}"
                : string.Empty;

        throw new InvalidOperationException(
            string.Format(ErrorMessages.ResponseDidNotComplete, agentName, response.Id, response.Status, detail));
    }

    /// <summary>
    /// Resolves the tool calls made by the model in the response, using the provided callback to get the outputs for each tool call.
    /// </summary>
    /// <param name="response">The response containing the tool calls.</param>
    /// <param name="toolCalls">The list of tool calls to resolve.</param>
    /// <param name="resolveToolCalls">The callback to resolve the tool calls.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of resolved tool call outputs.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static async Task<IReadOnlyList<ResponseItem>> ResolveToolCallsAsync(ResponseResult response,
        IReadOnlyList<FunctionCallResponseItem> toolCalls,
        Func<IReadOnlyList<ToolCallRequest>, CancellationToken, Task<IEnumerable<ToolCallOutput>>>? resolveToolCalls,
        CancellationToken cancellationToken)
    {
        if (resolveToolCalls is null)
        {
            throw new InvalidOperationException(string.Format(ErrorMessages.ToolCallsRequiredNoCallback, response.Id));
        }

        var requests = toolCalls
            .Select(call => new ToolCallRequest(call.CallId, call.FunctionName, call.FunctionArguments.ToString()))
            .ToList();
        var outputs = await resolveToolCalls(requests, cancellationToken).ConfigureAwait(false);
        var outputByCallId = outputs.ToDictionary(o => o.CallId, o => o.Output);

        var nextInputItems = new List<ResponseItem>(response.OutputItems);
        foreach (var toolCall in toolCalls)
        {
            if (!outputByCallId.TryGetValue(toolCall.CallId, out var output))
            {
                throw new InvalidOperationException(string.Format(ErrorMessages.MissingToolCallOutput, toolCall.CallId));
            }

            nextInputItems.Add(ResponseItem.CreateFunctionCallOutputItem(toolCall.CallId, output));
        }

        return nextInputItems;
    }
}
