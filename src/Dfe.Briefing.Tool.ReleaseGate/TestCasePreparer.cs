using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.ReleaseGate;

/// <summary>
/// Prepares the test cases for the release gate by adding the prompts and evidence that the agents would normally build in the app, so that the gate can run them without needing to run the app.
/// </summary>
internal static class TestCasePreparer
{ 
    /// <summary>
    /// Prepares test case files by processing source files, updating their content, and writing them to the specified
    /// prepared directory.
    /// </summary>
    /// <param name="services">The service provider used to resolve dependencies.</param>
    /// <param name="preparedDirectory">The directory where prepared test case files are written.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidDataException">Thrown when a test case file is empty or invalid.</exception>
    public static async Task PrepareAsync(IServiceProvider services, string preparedDirectory, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var briefingBuilder = scope.ServiceProvider.GetRequiredService<IBriefingBuilder>();
        var source = Path.Combine(AppContext.BaseDirectory, TestCaseFields.SourceDirectory);

        foreach (var file in Directory.Exists(source) ? Directory.GetFiles(source, TestCaseFields.FilePattern, SearchOption.AllDirectories) : [])
        {
            var relativePath = Path.GetRelativePath(source, file);
            var agentName = relativePath.Split(Path.DirectorySeparatorChar)[0];
            var testCase = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?.AsObject()
                ?? throw new InvalidDataException(TestCaseErrors.Empty(relativePath));

            Prepare(testCase, agentName, briefingBuilder, relativePath);

            var prepared = Path.Combine(preparedDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(prepared)!);
            await File.WriteAllTextAsync(prepared, testCase.ToJsonString(JsonSerializerHelper.JsonOptions), cancellationToken);
        }
    }

    private static void Prepare(JsonObject testCase, string agentName, IBriefingBuilder briefingBuilder, string relativePath)
    {
        if (testCase.ContainsKey(TestCaseFields.Prompt))
            throw new InvalidDataException($"{relativePath} has a \"{TestCaseFields.Prompt}\". Give its \"{TestCaseFields.Subject}\" instead: prompts are built by the app, so the gate tests the ones it sends.");

        var subject = testCase[TestCaseFields.Subject]?.AsObject()
            ?? throw new InvalidDataException($"{relativePath} needs a \"{TestCaseFields.Subject}\" with the school or trust's \"{TestCaseFields.SubjectName}\" and \"{TestCaseFields.SubjectUrnOrTrn}\".");
        var parameters = new BriefingParameters(
            subject[TestCaseFields.SubjectUrnOrTrn]?.GetValue<string>() ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectUrnOrTrn)),
            subject[TestCaseFields.SubjectName]?.GetValue<string>() ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectName)));
        testCase.Remove(TestCaseFields.Subject);

        if (agentName != AgentNames.Synthesis)
        {
            testCase[TestCaseFields.Prompt] = briefingBuilder.BuildSubjectInstruction(parameters);
            return;
        }

        // The Synthesis agent is given the other agents' outputs in its prompt, as in the app. They're also the case's
        // evidence, which is what the judge scores groundedness against.
        var outputs = testCase[TestCaseFields.SpecialistOutputs]?.AsArray()
            ?? throw new InvalidDataException($"{relativePath} needs \"{TestCaseFields.SpecialistOutputs}\": the other agents' answers to combine.");
        var specialistResults = outputs.Select(output => new AgentResult
        {
            AgentName = output?[TestCaseFields.SpecialistAgent]?.GetValue<string>()
                ?? throw new InvalidDataException($"{relativePath}: each specialist output needs an \"{TestCaseFields.SpecialistAgent}\"."),
            Output = output[TestCaseFields.SpecialistOutput]?.GetValue<string>() ?? string.Empty
        }).ToList();
        testCase.Remove(TestCaseFields.SpecialistOutputs);

        testCase[TestCaseFields.Prompt] = briefingBuilder.BuildSynthesisPrompt(parameters, specialistResults);
        testCase[TestCaseFields.Evidence] = briefingBuilder.CompileContext(specialistResults);
    }
}
