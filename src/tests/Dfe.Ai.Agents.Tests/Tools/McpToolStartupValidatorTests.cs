using Dfe.Ai.Agents.Tools.Mcp;
using Dfe.Ai.Agents.Tools.Mcp.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OpenAI.Responses;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Tools;

public sealed class McpToolStartupValidatorTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IMcpToolClient _client = Substitute.For<IMcpToolClient>();
    private readonly ILogger<McpToolStartupValidator> _logger = Substitute.For<ILogger<McpToolStartupValidator>>();

    private McpToolStartupValidator CreateSut() => new(_client, _logger);

    [Fact]
    public async Task StartAsync_CallsGetToolsAsync_ToValidateConfiguration()
    {
        _client.GetToolsAsync(cancellationToken).Returns(Task.FromResult<IReadOnlyList<ResponseTool>>([]));
        var sut = CreateSut();

        await sut.StartAsync(cancellationToken);

        await _client.Received(1).GetToolsAsync(cancellationToken);
    }

    [Fact]
    public async Task StartAsync_Propagates_WhenValidationFails()
    {
        var exception = new InvalidOperationException("MCP server 'foo' does not expose the following configured tool(s): bar.");
        _client.GetToolsAsync(cancellationToken).Throws(exception);
        var sut = CreateSut();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.StartAsync(cancellationToken));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task StopAsync_CompletesWithoutError()
    {
        var sut = CreateSut();

        var exception = await Record.ExceptionAsync(() => sut.StopAsync(cancellationToken));

        Assert.Null(exception);
    }
}
