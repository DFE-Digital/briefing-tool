using OpenAI.Responses;

namespace Dfe.Ai.Agents.Agents.Interfaces;

/// <summary>
/// Provides access to Foundry conversations, including creating conversations and sending input to agents.
/// </summary>
public interface IFoundryConversationClient
{
    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ID of the created conversation.</returns>
    Task<string> CreateConversationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends input to an agent within a conversation and returns its response.
    /// </summary>
    /// <param name="agentName">The name of the agent to send input to.</param>
    /// <param name="conversationId">The ID of the conversation.</param>
    /// <param name="inputItems">The input items to send.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response from the agent.</returns>
    Task<ResponseResult> CreateResponseAsync(string agentName, string conversationId, IReadOnlyList<ResponseItem> inputItems, CancellationToken cancellationToken = default);
}
