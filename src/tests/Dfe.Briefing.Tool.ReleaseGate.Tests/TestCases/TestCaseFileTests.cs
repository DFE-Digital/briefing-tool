using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Application.Providers;
using Xunit;
using GovUK.Dfe.AI.Agents.Options;
using GovUK.Dfe.AI.Agents.Quality;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests.TestCases;

/// <summary>The release gate's real test case files, checked against the conventions every app's cases follow.</summary>
public sealed class TestCaseFileTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestCases");

    public static TheoryData<string> Agents => [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order()];

    [Fact]
    public void Ships_test_cases_only_for_briefing_agents() =>
        Assert.Subset(BriefingAgents.All.Select(agent => agent.Name).ToHashSet(),
            Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().ToHashSet());

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task Every_case_loads_and_follows_the_release_conventions(string agent)
    {
        var cases = await LoadAsync(agent);

        // The release gate fails a case that doesn't; checked here too, so a wrong file fails the build, not a release.
        Assert.NotEmpty(cases);
        Assert.Empty(cases.SelectMany(ReleaseSections.Problems));
    }

    /// <summary>
    /// The agent's cases as the library loads them. The app builds each prompt from the case's "subject" before the gate
    /// runs (TestCasePreparer), so a stand-in prompt is given here: the conventions don't depend on it.
    /// </summary>
    private static async Task<IReadOnlyList<AgentTestCase>> LoadAsync(string agent)
    {
        var source = Path.Combine(Root, agent);
        var copy = Directory.CreateTempSubdirectory("briefing-test-cases-");
        try
        {
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var testCase = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!.AsObject();
                testCase["prompt"] = "Built by the app from the case's subject.";
                var target = Path.Combine(copy.FullName, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, testCase.ToJsonString(), TestContext.Current.CancellationToken);
            }

            return await AgentTestCase.LoadAsync(copy.FullName, TestContext.Current.CancellationToken);
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }
}
