using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.ReleaseGate.Commands;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests.Commands;

/// <summary>
/// The gate command runs the library's standard release check, which is tested in the library, inside one release
/// run; these tests check what the command decides from it.
/// </summary>
public sealed class GateCommandTests
{
    private readonly IAgentReleaseGate _releaseGate = Substitute.For<IAgentReleaseGate>();
    private readonly IReleaseRun _run = Substitute.For<IReleaseRun>();

    public GateCommandTests()
    {
        _run.Id.Returns("run-1");
        _releaseGate.BeginRun().Returns(_run);
        Returns([.. BriefingAgents.All.Select(agent => new AgentGateResult(agent.Name, [], []))]);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passes_when_every_agent_passes_and_checks_them_all_inside_one_release_run()
    {
        var exitCode = await GateCommand.RunAsync(Services(), Token);

        Assert.Equal(0, exitCode);
        Received.InOrder(() =>
        {
            _releaseGate.BeginRun();
            _releaseGate.CheckReleaseAsync(BriefingAgents.All, Token);
            _run.Dispose();
        });
    }

    [Fact]
    public async Task Fails_when_any_agent_fails()
    {
        Returns(new AgentGateResult(BriefingAgents.All[0].Name, ["the guardrail doesn't exist"], []) { Evaluated = false });

        Assert.Equal(1, await GateCommand.RunAsync(Services(), Token));
    }

    private void Returns(params AgentGateResult[] results) =>
        _releaseGate.CheckReleaseAsync(Arg.Any<IReadOnlyCollection<AgentDefinition>>(), Arg.Any<CancellationToken>()).Returns(results);

    private ServiceProvider Services() => new ServiceCollection()
        .AddSingleton(_releaseGate)
        .BuildServiceProvider();
}
