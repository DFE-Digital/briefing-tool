using Dfe.Ai.Agents.Agents.Interfaces;
using Dfe.Ai.Agents.Context;
using Dfe.Ai.Agents.Models;
using Dfe.Ai.Agents.Orchestration;
using Dfe.Ai.Agents.Tests.Constants;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Orchestration;

public sealed class AgentOrchestratorTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly IAgentRunner _agentRunner = Substitute.For<IAgentRunner>();

    private AgentOrchestrator CreateSut() => new(_agentRunner);

    [Fact]
    public async Task RunSequentialAsync_FeedsEachAgentsOutputToTheNext()
    {
        var researcher = new AgentSpec { Name = "researcher", Instructions = "Research." };
        var writer = new AgentSpec { Name = "writer", Instructions = "Write." };

        _agentRunner.RunAsync(researcher, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("researcher", "research notes", 10));
        _agentRunner.RunAsync(writer, Arg.Is<string>(p => p.Contains("research notes")), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("writer", "final draft", 20));

        var sut = CreateSut();
        var result = await sut.RunSequentialAsync([researcher, writer], "Explain MCP.", new AgentContext(), cancellationToken: cancellationToken);

        Assert.Equal("final draft", result.FinalOutput);
        Assert.Equal(30, result.TotalTokens);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, r => Assert.True(r.Succeeded));
    }

    [Fact]
    public async Task RunSequentialAsync_RecordsFailure_AndContinues_ByDefault()
    {
        var first = new AgentSpec { Name = "first", Instructions = "First." };
        var second = new AgentSpec { Name = "second", Instructions = "Second." };

        _agentRunner.RunAsync(first, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        _agentRunner.RunAsync(second, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("second", "ok", 5));

        var sut = CreateSut();
        var result = await sut.RunSequentialAsync([first, second], "input", new AgentContext(), cancellationToken: cancellationToken);

        Assert.False(result.Results[0].Succeeded);
        Assert.True(result.Results[1].Succeeded);
        Assert.Equal("ok", result.FinalOutput);
    }

    [Fact]
    public async Task RunSequentialAsync_Propagates_WhenShouldSuppressReturnsFalse()
    {
        var spec = new AgentSpec { Name = "first", Instructions = "First." };
        _agentRunner.RunAsync(spec, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunSequentialAsync([spec], "input", new AgentContext(), shouldSuppress: _ => false, cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RunParallelAsync_RunsEveryAgent_AndAggregatesOutputs()
    {
        var a = new AgentSpec { Name = "a", Instructions = "A." };
        var b = new AgentSpec { Name = "b", Instructions = "B." };

        _agentRunner.RunAsync(a, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("a", "output a", 10));
        _agentRunner.RunAsync(b, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("b", "output b", 15));

        var sut = CreateSut();
        var result = await sut.RunParallelAsync([a, b], "input", new AgentContext(), cancellationToken: cancellationToken);

        Assert.Contains("output a", result.FinalOutput);
        Assert.Contains("output b", result.FinalOutput);
        Assert.Equal(25, result.TotalTokens);
    }

    [Fact]
    public async Task RunParallelAsync_OneFailure_DoesNotAbortTheOthers()
    {
        var a = new AgentSpec { Name = "a", Instructions = "A." };
        var b = new AgentSpec { Name = "b", Instructions = "B." };

        _agentRunner.RunAsync(a, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        _agentRunner.RunAsync(b, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("b", "output b", 15));

        var sut = CreateSut();
        var result = await sut.RunParallelAsync([a, b], "input", new AgentContext(), cancellationToken: cancellationToken);

        Assert.Equal(2, result.Results.Count);
        Assert.Contains(result.Results, r => !r.Succeeded && r.AgentName == "a");
        Assert.Contains(result.Results, r => r.Succeeded && r.AgentName == "b");
    }

    [Fact]
    public async Task RunParallelAsync_Steps_ResolvesEachAgentsPromptIndependently()
    {
        // Simulates two agents pulling context from different sources (e.g. different MCP
        // servers / Azure AI Search indexes) - each gets its own resolver, not a shared input.
        var fromSearch = new AgentSpec { Name = "from-search", Instructions = "Search agent." };
        var fromMcp = new AgentSpec { Name = "from-mcp", Instructions = "MCP agent." };

        _agentRunner.RunAsync(fromSearch, "search evidence", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("from-search", "search result", 10));
        _agentRunner.RunAsync(fromMcp, "mcp evidence", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("from-mcp", "mcp result", 12));

        var steps = new List<AgentOrchestrationStep>
        {
            new(fromSearch, _ => Task.FromResult("search evidence")),
            new(fromMcp, _ => Task.FromResult("mcp evidence")),
        };

        var sut = CreateSut();
        var result = await sut.RunParallelAsync(steps, new AgentContext(), cancellationToken: cancellationToken);

        Assert.Contains("search result", result.FinalOutput);
        Assert.Contains("mcp result", result.FinalOutput);
    }

    [Fact]
    public async Task RunParallelAsync_Steps_RecordsFailure_WhenPromptResolutionFails()
    {
        var ok = new AgentSpec { Name = "ok", Instructions = "Fine." };
        var broken = new AgentSpec { Name = "broken", Instructions = "Broken source." };

        _agentRunner.RunAsync(ok, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new AgentResult("ok", "output", 5));

        var steps = new List<AgentOrchestrationStep>
        {
            new(ok, _ => Task.FromResult("input")),
            new(broken, _ => throw new InvalidOperationException(TestErrorMessages.McpServerUnreachable)),
        };

        var sut = CreateSut();
        var result = await sut.RunParallelAsync(steps, new AgentContext(), cancellationToken: cancellationToken);

        Assert.Contains(result.Results, r => r.Succeeded && r.AgentName == "ok");
        Assert.Contains(result.Results, r => !r.Succeeded && r.AgentName == "broken");
        await _agentRunner.DidNotReceive().RunAsync(broken, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunParallelAsync_RespectsMaxConcurrency()
    {
        const int maxConcurrency = 2;
        var currentlyRunning = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        var specs = Enumerable.Range(0, 5).Select(i => new AgentSpec { Name = $"agent-{i}", Instructions = "Go." }).ToList();

        foreach (var spec in specs)
        {
            _agentRunner.RunAsync(spec, Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
                .Returns(async _ =>
                {
                    lock (gate)
                    {
                        currentlyRunning++;
                        maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentlyRunning);
                    }

                    await Task.Delay(30, cancellationToken);

                    lock (gate)
                    {
                        currentlyRunning--;
                    }

                    return new AgentResult(spec.Name, "ok", 1);
                });
        }

        var sut = CreateSut();
        await sut.RunParallelAsync(specs, "input", new AgentContext(), maxConcurrency: maxConcurrency, cancellationToken: cancellationToken);

        Assert.True(maxObservedConcurrency <= maxConcurrency);
    }
}
