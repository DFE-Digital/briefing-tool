using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Dfe.Briefing.Tool.ReleaseGate.Tests;

/// <summary>
/// The release gate's real test case files, prepared as the gate prepares them: each case's records go through the
/// app's builder, as the app's own searches do.
/// </summary>
public sealed class TestCasePreparerTests : IDisposable
{
    private readonly IBriefingBuilder _builder = Substitute.For<IBriefingBuilder>();
    private readonly List<(BriefingParameters Parameters, BriefingRecords Records)> _built = [];
    private readonly DirectoryInfo _prepared = Directory.CreateTempSubdirectory("briefing-prepared-cases-");

    public TestCasePreparerTests()
    {
        _builder.BuildBriefingPrompt(Arg.Any<BriefingParameters>()).Returns("briefing-prompt");
        _builder.BuildBriefingEvidence(Arg.Any<BriefingParameters>(), Arg.Any<BriefingRecords>())
            .Returns(call =>
            {
                _built.Add((call.Arg<BriefingParameters>(), call.Arg<BriefingRecords>()));
                return new AgentEvidence("briefing-evidence");
            });
    }

    public void Dispose() => _prepared.Delete(recursive: true);

    private Task PrepareAsync()
        => TestCasePreparer.PrepareAsync(new ServiceCollection().AddScoped(_ => _builder).BuildServiceProvider(), _prepared.FullName,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Every_case_gets_the_apps_prompt_and_evidence_in_place_of_its_subject_and_records()
    {
        await PrepareAsync();

        var files = _prepared.GetFiles("*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var testCase = JsonNode.Parse(await File.ReadAllTextAsync(file.FullName, TestContext.Current.CancellationToken))!.AsObject();
            Assert.Equal("briefing-prompt", testCase["prompt"]?.GetValue<string>());
            Assert.Equal("briefing-evidence", testCase["evidence"]?.GetValue<string>());
            Assert.False(testCase.ContainsKey("subject"), file.Name);
            Assert.False(testCase.ContainsKey("records"), file.Name);
        }
        Assert.Equal(files.Length, _built.Count);
    }

    [Fact]
    public async Task A_trust_case_gives_its_own_record_and_its_academies_records()
    {
        await PrepareAsync();

        var (parameters, records) = Assert.Single(_built, built => built.Parameters.SchoolOrTrustName == "Example Learning Trust"
            && built.Records.Related.HasEvidence);
        Assert.True(parameters.IsTrust);
        Assert.Contains("trust_reference_number: TR09001", records.Subject.Text, StringComparison.Ordinal);
        Assert.Contains("establishment_urn: 900002", records.Related.Text, StringComparison.Ordinal);
    }
}
