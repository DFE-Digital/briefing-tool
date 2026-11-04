using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.ReleaseGate.Commands;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Interfaces;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Traceability;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests.Commands;

/// <summary>
/// The gate command runs the library's Responsible AI quality gate, which is tested in the library, inside one release
/// run; these tests check that the command's exit code follows the release decision, so a pipeline blocks on it.
/// </summary>
public sealed class GateCommandTests
{
    private readonly IAgentReleaseGate _releaseGate = Substitute.For<IAgentReleaseGate>();
    private readonly IResponsibleAiQualityGate _qualityGate = Substitute.For<IResponsibleAiQualityGate>();
    private readonly IReleaseRun _run = Substitute.For<IReleaseRun>();

    public GateCommandTests()
    {
        _run.Id.Returns("run-1");
        _releaseGate.BeginRun().Returns(_run);
        Decides(ReleaseDecision.Pass, permitted: true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Permits_release_when_the_quality_gate_passes_and_decides_every_agent_inside_one_release_run()
    {
        var exitCode = await GateCommand.RunAsync(Services(), Token);

        Assert.Equal(0, exitCode);
        Received.InOrder(() =>
        {
            _releaseGate.BeginRun();
            _qualityGate.DecideReleaseAsync(BriefingAgents.All, Token);
            _run.Dispose();
        });
    }

    [Fact]
    public async Task Permits_release_on_a_warning_whose_risks_are_accepted()
    {
        Decides(ReleaseDecision.Warning, permitted: true);

        Assert.Equal(0, await GateCommand.RunAsync(Services(), Token));
    }

    [Theory]
    [InlineData(ReleaseDecision.Warning)]
    [InlineData(ReleaseDecision.Fail)]
    public async Task Blocks_release_when_the_quality_gate_does_not_permit_it(ReleaseDecision decision)
    {
        Decides(decision, permitted: false);

        Assert.Equal(1, await GateCommand.RunAsync(Services(), Token));
    }

    private void Decides(ReleaseDecision decision, bool permitted)
    {
        var agents = BriefingAgents.All.Select(agent => new AgentReleaseDecision
        {
            AgentName = agent.Name,
            Decision = decision,
            ReleasePermitted = permitted,
            Reasons = [$"{decision}"],
        }).ToList();

        _qualityGate.DecideReleaseAsync(Arg.Any<IReadOnlyCollection<AgentDefinition>>(), Arg.Any<CancellationToken>()).Returns(new QualityGateReport
        {
            Decision = decision,
            ReleasePermitted = permitted,
            Reasons = [],
            Agents = agents,
            Manifest = new ReleaseManifest
            {
                RunId = "run-1",
                ApplicationName = "briefing-tool",
                GeneratedAt = DateTimeOffset.UnixEpoch,
                EvaluationFrameworkVersion = "test",
                PolicyFingerprint = "sha256:test",
            },
        });
    }

    private ServiceProvider Services() => new ServiceCollection()
        .AddSingleton(_releaseGate)
        .AddSingleton(_qualityGate)
        .BuildServiceProvider();
}
