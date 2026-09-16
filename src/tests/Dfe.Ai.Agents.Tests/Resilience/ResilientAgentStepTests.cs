using Dfe.Ai.Agents.Resilience;
using Dfe.Ai.Agents.Tests.Constants;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Resilience;

public sealed class ResilientAgentStepTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsStepResult_WhenStepSucceeds()
    {
        var result = await ResilientAgentStep.ExecuteAsync(
            step: () => Task.FromResult("ok"),
            fallback: _ => "fallback");

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFallback_WhenStepThrowsAndSuppressionAllows()
    {
        var result = await ResilientAgentStep.ExecuteAsync<string>(
            step: () => throw new InvalidOperationException(TestErrorMessages.StepFailure),
            fallback: ex => $"fallback: {ex.Message}",
            shouldSuppress: _ => true);

        Assert.Equal($"fallback: {TestErrorMessages.StepFailure}", result);
    }

    [Fact]
    public async Task ExecuteAsync_Propagates_WhenShouldSuppressReturnsFalse()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ResilientAgentStep.ExecuteAsync<string>(
            step: () => throw new InvalidOperationException(TestErrorMessages.StepFailure),
            fallback: _ => "fallback",
            shouldSuppress: _ => false));
    }

    [Fact]
    public async Task ExecuteAsync_Propagates_WhenCancelled()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => ResilientAgentStep.ExecuteAsync<string>(
            step: () => throw new OperationCanceledException(),
            fallback: _ => "fallback"));
    }
}
