using System.Globalization;
using Dfe.Briefing.Tool.Application.Providers;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.Evaluation.QualityGates.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.ReleaseGate.Commands;

internal static class GateCommand
{
    /// <summary>
    /// Runs the agent release gate process asynchronously and outputs the results to the console.
    /// </summary>
    /// <param name="services">The service provider used to resolve dependencies.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The result is 0 if the release gate passes; otherwise, 1.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var releaseGate = services.GetRequiredService<IAgentReleaseGate>();
        using var run = releaseGate.BeginRun();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, ReleaseLog.RunStarted, run.Id));

        var results = await releaseGate.CheckReleaseAsync(BriefingAgents.All, cancellationToken);
        foreach (var result in results)
        {
            Console.WriteLine($"{(result.Passed ? Outcomes.Pass : Outcomes.Fail)}  {result.AgentName}");
            foreach (var line in result.ProblemLines())
                Console.WriteLine(ReleaseLog.ConsoleProblemIndent + line);
        }

        if (results.All(result => result.Passed))
        {
            Console.WriteLine("Release gate passed. Reports are in evaluation-reports; copy a report into Baselines to raise the bar.");
            return 0;
        }

        Console.WriteLine("Release gate failed - don't release. Each line above says why; the reports in evaluation-reports have every answer.");
        return 1;
    }
}
