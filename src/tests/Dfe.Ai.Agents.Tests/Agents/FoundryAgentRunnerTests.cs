using Dfe.Ai.Agents.Agents;
using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Factories;
using Dfe.Ai.Agents.Models;
using NSubstitute;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Agents;

public sealed class FoundryAgentRunnerTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentFactory _agentFactory = Substitute.For<IAgentFactory>();
    private readonly IFoundryConversationClient _conversationClient = Substitute.For<IFoundryConversationClient>();

    private FoundryAgentRunner CreateSut() => new(_agentFactory, _conversationClient);
     
    private static ResponseResult ResponseWithOutputItems(string id, IEnumerable<ResponseItem> items, int? totalTokens = null, string status = "completed")
    {
        var itemsJson = string.Join(',', items.Select(item => ModelReaderWriter.Write(item).ToString()));
        var usageJson = totalTokens is null
            ? ""
            : $",\"usage\":{{\"input_tokens\":10,\"output_tokens\":{totalTokens - 10},\"total_tokens\":{totalTokens}}}";
        var json = $"{{\"id\":\"{id}\",\"object\":\"response\",\"created_at\":0,\"status\":\"{status}\",\"model\":\"gpt-4o\",\"output\":[{itemsJson}]{usageJson}}}";
        return ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(json))!;
    }

    private static ResponseResult CompletedResponse(string id, string text, int totalTokens = 30)
        => ResponseWithOutputItems(id, [ResponseItem.CreateAssistantMessageItem(text, (IEnumerable<ResponseMessageAnnotation>?)null)], totalTokens);

    private static ResponseResult ResponseWithFunctionCall(string id, string callId, string functionName)
        => ResponseWithOutputItems(id,
            [ResponseItem.CreateFunctionCallItem(callId: callId, functionName: functionName, functionArguments: BinaryData.FromString("{}"))]);

    [Fact]
    public async Task RunAsync_ThrowsArgumentNullException_WhenSpecIsNull()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunAsync(null!, "prompt", cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RunAsync_ThrowsArgumentException_WhenPromptIsEmpty()
    {
        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };

        await Assert.ThrowsAsync<ArgumentException>(() => sut.RunAsync(spec, "  ", cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RunAsync_CreatesNewConversation_WhenNoConversationIdGiven_AndReturnsAgentReply()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");
        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(CompletedResponse("resp-1", "Here's the answer."));

        var sut = CreateSut();

        var result = await sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken);

        Assert.Equal("my-agent", result.AgentName);
        Assert.Equal("Here's the answer.", result.Output);
        Assert.Equal(30, result.TotalTokens);
        await _conversationClient.Received(1).CreateConversationAsync(cancellationToken);
    }

    [Fact]
    public async Task RunAsync_ReusesGivenConversation_WithoutCreatingANewOne()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateResponseAsync("my-agent", "existing-conversation", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(CompletedResponse("resp-1", "Reply."));

        var sut = CreateSut();

        await sut.RunAsync(spec, "prompt", conversationId: "existing-conversation", cancellationToken: cancellationToken);

        await _conversationClient.DidNotReceiveWithAnyArgs().CreateConversationAsync(cancellationToken);
    }

    [Fact]
    public async Task RunAsync_ResolvesFunctionToolCalls_ThenCompletes()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");

        var pendingResponse = ResponseWithFunctionCall("resp-1", "call-1", "lookup");
        var resolvedResponse = CompletedResponse("resp-2", "Resolved.");

        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(pendingResponse, resolvedResponse);

        var resolved = false;
        var sut = CreateSut();

        var result = await sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken, resolveToolCalls: (calls, ct) =>
        {
            resolved = true;
            var call = Assert.Single(calls);
            Assert.Equal("call-1", call.CallId);
            Assert.Equal("lookup", call.FunctionName);
            return Task.FromResult<IEnumerable<ToolCallOutput>>([new ToolCallOutput("call-1", "42")]);
        });

        Assert.True(resolved);
        Assert.Equal("Resolved.", result.Output);
    }

    [Fact]
    public async Task RunAsync_Throws_WhenToolCallsHaveNoResolver()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");
        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(ResponseWithFunctionCall("resp-1", "call-1", "lookup"));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RunAsync_Throws_WhenResponseDoesNotComplete_AndHasNoToolCalls()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");
        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(ResponseWithOutputItems("resp-1", [], status: "failed"));

        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken));
        Assert.Contains("resp-1", ex.Message);
        Assert.Contains("Failed", ex.Message);
    }

    [Fact]
    public async Task RunAsync_Throws_WhenToolCallRoundsExceedLimit()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");
        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(_ => ResponseWithFunctionCall("resp-loop", "call-1", "lookup"));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken,
            resolveToolCalls: (calls, ct) => Task.FromResult<IEnumerable<ToolCallOutput>>(
                calls.Select(c => new ToolCallOutput(c.CallId, "42")))));

        await _conversationClient.Received(10).CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken);
    }

    [Fact]
    public async Task RunAsync_LogsAndRethrows_WhenConversationClientThrows()
    {
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };
        _agentFactory.GetOrCreateAsync(spec, cancellationToken).Returns(new AgentReference("agent-id", "my-agent"));
        _conversationClient.CreateConversationAsync(cancellationToken).Returns("conversation-1");
        _conversationClient.CreateResponseAsync("my-agent", "conversation-1", Arg.Any<IReadOnlyList<ResponseItem>>(), cancellationToken)
            .Returns(Task.FromException<ResponseResult>(new InvalidOperationException("boom")));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(spec, "prompt", cancellationToken: cancellationToken));
    }
}
