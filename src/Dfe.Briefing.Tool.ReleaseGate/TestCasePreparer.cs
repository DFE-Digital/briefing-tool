using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Builders.Interfaces;
using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Briefing.Tool.ReleaseGate;

/// <summary>
/// Prepares the test cases for the release gate by adding the prompt and evidence the app would build from each case's
/// records, so that the gate can run them without needing to run the app.
/// </summary>
internal static partial class TestCasePreparer
{ 
    /// <summary>
    /// Prepares test case files by processing source files, updating their content, and writing them to the specified
    /// prepared directory.
    /// </summary>
    /// <param name="services">The service provider used to resolve dependencies.</param>
    /// <param name="preparedDirectory">The directory where prepared test case files are written.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <param name="sourceDirectory">The test cases to prepare; the app's own <c>TestCases</c> folder when null.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a test case file is empty or invalid.
    /// </exception>
    public static async Task PrepareAsync(IServiceProvider services, string preparedDirectory, CancellationToken cancellationToken,
        string? sourceDirectory = null)
    {
        await using var scope = services.CreateAsyncScope();
        var briefingBuilder = BuilderFor(scope.ServiceProvider);
        var source = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, TestCaseFields.SourceDirectory);

        foreach (var file in Directory.Exists(source) ? Directory.GetFiles(source, TestCaseFields.FilePattern, SearchOption.AllDirectories) : [])
        {
            var relativePath = Path.GetRelativePath(source, file);
            var testsFile = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?.AsObject()
                ?? throw new InvalidDataException(TestCaseErrors.Empty(relativePath));
            if (testsFile.ContainsKey(TestCaseFields.Tests))
                throw new InvalidDataException(TestCaseErrors.UseSections(relativePath));

            foreach (var (section, isAttack) in new[] { (TestCaseFields.EvaluationSection, false), (TestCaseFields.GuardrailSection, true) })
            {
                var tests = testsFile[section]?.AsArray() ?? [];
                for (var i = 0; i < tests.Count; i++)
                {
                    var testCase = tests[i]?.AsObject() ?? throw new InvalidDataException(TestCaseErrors.Empty($"{relativePath} {section} test {i + 1}"));
                    Prepare(testCase, briefingBuilder, TestCaseErrors.CaseAt(relativePath, testCase, i), isAttack);
                }
            }

            var prepared = Path.Combine(preparedDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(prepared)!);
            await File.WriteAllTextAsync(prepared, testsFile.ToJsonString(JsonSerializerHelper.JsonOptions), cancellationToken);
        }
    }

    // A case gives the records the app would find, labelled by set; the app's own builder turns them into the prompt and
    // evidence, so the gate tests exactly what the app sends.
    // The exception is an attack on the question itself: a guardrail case with its own prompt and no subject, records or
    // evidence, as the quality gate needs to show the guardrail blocks a jailbreak that comes with no evidence.
    private static void Prepare(JsonObject testCase, IBriefingBuilder briefingBuilder, string relativePath, bool isAttack)
    {
        if (isAttack && IsQuestionAttack(testCase))
            return;

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
        if (!isAttack)
            MustNotShowRawValues(testCase, records);
    }

    // The briefing always rewrites the records' dates and academic years ("2023-03-14" as "14 March 2023", "202324" as
    // "2023/24"), so each case's own raw values must never appear in its briefing. Found from its records, so no case has
    // to list them; one the case expects itself is left alone.
    private static void MustNotShowRawValues(JsonObject testCase, BriefingRecords records)
    {
        var text = string.Join('\n', new[] { records.Subject, records.Related, records.RiseConcerns, records.RecastConcerns }.Select(static r => r.Text));
        var expected = testCase[TestCaseFields.MustMention]?.AsArray().Select(static node => node?.GetValue<string>() ?? string.Empty).ToList() ?? [];
        if (testCase[TestCaseFields.MustNotMention] is not JsonArray mustNotMention)
            testCase[TestCaseFields.MustNotMention] = mustNotMention = [];
        var listed = mustNotMention.Select(static node => node?.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rawValues = RawDateRegex().Matches(text).Concat(RawAcademicYearRegex().Matches(text))
            .Select(static m => m.Groups["value"].Value)
            .Where(value => !expected.Exists(e => e.Contains(value, StringComparison.OrdinalIgnoreCase)) && listed.Add(value));
        foreach (var value in rawValues)
            mustNotMention.Add(value);
    }

    // Not between word boundaries: a timestamp's date ("2025-03-03T09:30:00Z") is followed by a letter, and is raw too.
    [GeneratedRegex(@"(?<![\d-])(?<value>\d{4}-\d{2}-\d{2})(?![\d-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RawDateRegex();

    [GeneratedRegex(@"""academic_year"":\s*""(?<value>\d{6})""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RawAcademicYearRegex();

    // The app's builder, measuring the 3-year concerns window from TestCases:Today when it's set, so the test cases'
    // concerns never age out of it. Only the builder's date is fixed: the gate's own clock, which decides when a risk
    // acceptance expires, stays real.
    private static IBriefingBuilder BuilderFor(IServiceProvider services)
    {
        var builder = services.GetRequiredService<IBriefingBuilder>();
        var setting = services.GetService<IConfiguration>()?[Settings.TestCasesTodayKey];
        if (string.IsNullOrWhiteSpace(setting))
            return builder;

        if (!DateOnly.TryParseExact(setting.Trim(), Settings.TestCasesTodayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var today))
            throw new InvalidDataException($"{Settings.TestCasesTodayKey} must be a date written {Settings.TestCasesTodayFormat}, not \"{setting}\".");

        return (IBriefingBuilder)ActivatorUtilities.CreateInstance(services, builder.GetType(), new FixedDateTimeProvider(today));
    }

    /// <summary>A clock that always gives midday (UTC) on one day.</summary>
    private sealed class FixedDateTimeProvider(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    private static bool IsQuestionAttack(JsonObject testCase)
        => testCase[TestCaseFields.Prompt]?.GetValue<string>() is { Length: > 0 }
            && !testCase.ContainsKey(TestCaseFields.Evidence)
            && !testCase.ContainsKey(TestCaseFields.Subject)
            && !testCase.ContainsKey(TestCaseFields.Records);

    // A school's case gives "school" and, if it's in a trust, "trust"; a trust's gives "trust" and "academies". Either
    // can give "riseConcerns" and "recastConcerns". A set it leaves out has no records, as when a search finds none.
    private static BriefingRecords ReadRecords(JsonObject records, bool isTrust, string relativePath)
    {
        string[] allowed = isTrust
            ? [TestCaseFields.TrustRecords, TestCaseFields.AcademyRecords, TestCaseFields.RiseConcerns, TestCaseFields.RecastConcerns]
            : [TestCaseFields.SchoolRecords, TestCaseFields.TrustRecords, TestCaseFields.RiseConcerns, TestCaseFields.RecastConcerns];
        if (records.Select(record => record.Key).FirstOrDefault(key => !allowed.Contains(key)) is { } unknown)
            throw new InvalidDataException(TestCaseErrors.UnknownRecords(relativePath, unknown, allowed));

        var subjectRecord = isTrust ? TestCaseFields.TrustRecords : TestCaseFields.SchoolRecords;
        if (string.IsNullOrWhiteSpace(records[subjectRecord]?.GetValue<string>()))
            throw new InvalidDataException(TestCaseErrors.SubjectRecordNeeded(relativePath, subjectRecord));

        // A set given as null, "" or only spaces has no records, as when the Rise or Recast index finds none.
        ContextResult Set(string key) => records[key]?.GetValue<string>() is { } text && !string.IsNullOrWhiteSpace(text)
            ? new ContextResult(text, HasEvidence: true)
            : new ContextResult(string.Empty, HasEvidence: false);

        return isTrust
            ? new BriefingRecords(Set(TestCaseFields.TrustRecords), Set(TestCaseFields.AcademyRecords), Set(TestCaseFields.RiseConcerns), Set(TestCaseFields.RecastConcerns))
            : new BriefingRecords(Set(TestCaseFields.SchoolRecords), Set(TestCaseFields.TrustRecords), Set(TestCaseFields.RiseConcerns), Set(TestCaseFields.RecastConcerns));
    }
}
