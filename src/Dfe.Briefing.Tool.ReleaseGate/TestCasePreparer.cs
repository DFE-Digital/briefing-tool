using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.ReleaseGate;

/// <summary>
/// Prepares the test cases for the release gate by adding the prompt and evidence the app would build from each case's
/// records, so that the gate can run them without needing to run the app.
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
            var testCase = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?.AsObject()
                ?? throw new InvalidDataException(TestCaseErrors.Empty(relativePath));

            Prepare(testCase, briefingBuilder, relativePath);

            var prepared = Path.Combine(preparedDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(prepared)!);
            await File.WriteAllTextAsync(prepared, testCase.ToJsonString(JsonSerializerHelper.JsonOptions), cancellationToken);
        }
    }

    // A case gives the records the app would find, labelled by set; the app's own builder turns them into the prompt and
    // evidence, so the gate tests exactly what the app sends.
    private static void Prepare(JsonObject testCase, IBriefingBuilder briefingBuilder, string relativePath)
    {
        if (testCase.ContainsKey(TestCaseFields.Prompt) || testCase.ContainsKey(TestCaseFields.Evidence))
            throw new InvalidDataException($"{relativePath} has a \"{TestCaseFields.Prompt}\" or \"{TestCaseFields.Evidence}\". Give its \"{TestCaseFields.Subject}\" and \"{TestCaseFields.Records}\" instead: the gate builds them as the app does.");

        var subject = testCase[TestCaseFields.Subject]?.AsObject()
            ?? throw new InvalidDataException($"{relativePath} needs a \"{TestCaseFields.Subject}\" with the school or trust's \"{TestCaseFields.SubjectName}\" and \"{TestCaseFields.SubjectUrnOrTrn}\".");
        var parameters = BriefingParameters.FromReference(
            subject[TestCaseFields.SubjectUrnOrTrn]?.GetValue<string>() ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectUrnOrTrn)),
            subject[TestCaseFields.SubjectName]?.GetValue<string>() ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectName)));

        var records = ReadRecords(testCase[TestCaseFields.Records]?.AsObject()
            ?? throw new InvalidDataException(TestCaseErrors.RecordsNeeded(relativePath)), parameters.IsTrust, relativePath);
        testCase.Remove(TestCaseFields.Subject);
        testCase.Remove(TestCaseFields.Records);

        testCase[TestCaseFields.Prompt] = briefingBuilder.BuildBriefingPrompt(parameters);
        testCase[TestCaseFields.Evidence] = briefingBuilder.BuildBriefingEvidence(parameters, records).Text;
    }

    // A school's case gives "school" and, if it's in a trust, "trust"; a trust's gives "trust" and "academies". Either
    // can give "riseConcerns" and "recastConcerns". A set it leaves out has no records, as when a search finds none.
    private static BriefingRecords ReadRecords(JsonObject records, bool isTrust, string relativePath)
    {
        string[] allowed = isTrust
            ? [TestCaseFields.TrustRecords, TestCaseFields.AcademyRecords, TestCaseFields.RiseConcerns, TestCaseFields.RecastConcerns]
            : [TestCaseFields.SchoolRecords, TestCaseFields.TrustRecords, TestCaseFields.RiseConcerns, TestCaseFields.RecastConcerns];
        if (records.Select(record => record.Key).FirstOrDefault(key => !allowed.Contains(key)) is { } unknown)
            throw new InvalidDataException(TestCaseErrors.UnknownRecords(relativePath, unknown, allowed));

        ContextResult Set(string key) => records[key]?.GetValue<string>() is { Length: > 0 } text
            ? new ContextResult(text, HasEvidence: true)
            : new ContextResult(string.Empty, HasEvidence: false);

        return isTrust
            ? new BriefingRecords(Set(TestCaseFields.TrustRecords), Set(TestCaseFields.AcademyRecords), Set(TestCaseFields.RiseConcerns), Set(TestCaseFields.RecastConcerns))
            : new BriefingRecords(Set(TestCaseFields.SchoolRecords), Set(TestCaseFields.TrustRecords), Set(TestCaseFields.RiseConcerns), Set(TestCaseFields.RecastConcerns));
    }
}
