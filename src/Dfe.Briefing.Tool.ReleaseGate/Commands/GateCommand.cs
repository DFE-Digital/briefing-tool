using System.Globalization;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Interfaces;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.ReleaseGate.Commands;

internal static class GateCommand
{
    /// <summary>
    /// Runs the AI agent library's Responsible AI quality gate inside one release run: the standard release check
    /// (guardrail, every test case, judge scores), then every quality check, decided by the policy in appsettings.json.
    /// Writes the decision, by principle, to the console.
    /// </summary>
    /// <param name="services">The service provider used to resolve dependencies.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>0 when the release is permitted (a pass, or a warning whose risks are accepted); otherwise, 1.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var releaseGate = services.GetRequiredService<IAgentReleaseGate>();
        var qualityGate = services.GetRequiredService<IResponsibleAiQualityGate>();
        using var run = releaseGate.BeginRun();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, ReleaseLog.RunStarted, run.Id));

        Console.WriteLine(ReleaseLog.RunningTestCases);
        var report = await qualityGate.DecideReleaseAsync(BriefingAgents.All, cancellationToken);

        Console.WriteLine(ReleaseLog.Deciding);
        Write(report);
        return report.ExitCode;
    }

    private static void Write(QualityGateReport report)
    {
        foreach (var agent in report.Agents)
        {
            Console.WriteLine($"{Outcomes.Of(agent.Decision)}  {agent.AgentName}{(agent.ReleasePermitted ? string.Empty : ReleaseLog.Blocked)}");
            foreach (var principle in agent.Principles)
                Console.WriteLine($"{ReleaseLog.ConsoleProblemIndent}{principle.Principle,-22}{(principle.Decision is { } decision ? Outcomes.Of(decision) : Outcomes.NotEvaluated)}");

            // The first reason is the decision's own; the rest are each blocking failure, warning and accepted risk.
            foreach (var reason in agent.Reasons.Skip(1))
                Console.WriteLine(ReleaseLog.ConsoleProblemIndent + reason);
        }

        Console.WriteLine(report.ReleasePermitted
            ? "Release permitted. Copy a passing evaluation-reports/<agent>.json into Baselines to raise the bar."
            : "Release blocked - don't release. Each line above says why; fix it, or record an authorised risk acceptance where the policy allows.");
        Console.WriteLine($"Report: {report.MarkdownPath} (machine-readable: {report.JsonPath})");
    }
}
