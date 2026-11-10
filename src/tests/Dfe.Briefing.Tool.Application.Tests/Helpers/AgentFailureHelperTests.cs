using System.ClientModel;
using System.ClientModel.Primitives;
using Dfe.Briefing.Tool.Application.Helpers;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Helpers;

public sealed class AgentFailureHelperTests
{
    // The AI agent library wraps the client's failure: InvalidOperationException("…rate limit…", ClientResultException).
    [Theory]
    [InlineData(429, true)]
    [InlineData(500, false)]
    [InlineData(400, false)]
    public void IsRateLimited_FindsTheGatewaysStatus_InsideTheLibrarysException(int status, bool rateLimited)
    {
        var failure = new InvalidOperationException("Agent 'briefing-agent' failed.", new ClientResultException(new FakeResponse(status)));

        Assert.Equal(rateLimited, AgentFailureHelper.IsRateLimited(failure));
    }

    [Fact]
    public void IsRateLimited_IsFalse_ForAFailureWithNoStatus()
    {
        Assert.False(AgentFailureHelper.IsRateLimited(new TimeoutException("Agent 'briefing-agent' did not finish.")));
        Assert.False(AgentFailureHelper.IsRateLimited(null));
    }

    private sealed class FakeResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(BinaryData.Empty);
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
