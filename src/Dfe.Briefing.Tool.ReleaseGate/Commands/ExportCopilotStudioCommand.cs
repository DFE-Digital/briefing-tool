using System.Text;
using System.Text.Json.Nodes;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using Microsoft.Extensions.Configuration;

namespace Dfe.Briefing.Tool.ReleaseGate.Commands;

/// <summary>
/// Exports the evaluation test cases as one Copilot Studio test set: a question and its expected response per school or
/// trust, for the Copilot Studio agent's Evaluation section. Guardrail cases aren't exported.
/// </summary>
internal static class ExportCopilotStudioCommand
{
    private sealed record Case(string Question, string Subject, string? ExpectedResponse, IReadOnlyList<string> Facts);

    public static async Task<int> RunAsync(string folder, CancellationToken cancellationToken)
    {
        var source = Path.Combine(AppContext.BaseDirectory, TestCaseFields.SourceDirectory);
        var refusals = RefusalPhrases();

        var cases = new List<Case>();
        foreach (var file in Directory.Exists(source) ? Directory.GetFiles(source, TestCaseFields.FilePattern, SearchOption.AllDirectories) : [])
        {
            var relativePath = Path.GetRelativePath(source, file);
            if (relativePath.Split(Path.DirectorySeparatorChar).Contains(TestCaseFields.GuardrailTestSet))
                continue;

            var testCase = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?.AsObject()
                ?? throw new InvalidDataException(TestCaseErrors.Empty(relativePath));
            cases.Add(ToCase(testCase, relativePath, refusals));
        }

        // Every evaluation case about the same school or trust becomes one question, as Copilot Studio answers it whole.
        var questions = cases
            .GroupBy(item => item.Question)
            .Select(group => (Question: group.Key,
                ExpectedResponse: group.Select(item => item.ExpectedResponse).FirstOrDefault(response => response is not null)
                    ?? DefaultExpectedResponse(group.First().Subject, [.. group.SelectMany(item => item.Facts).Distinct(StringComparer.OrdinalIgnoreCase)])))
            .Where(question => question.Question.Length <= CopilotStudioTestSet.MaxQuestionLength)
            .ToList();

        if (questions.Count == 0)
        {
            await Console.Error.WriteLineAsync($"No evaluation test cases were found under {source}.");
            return 1;
        }

        Directory.CreateDirectory(folder);
        var exported = questions.Take(CopilotStudioTestSet.MaxQuestions).ToList();
        var testSet = Path.Combine(folder, CopilotStudioTestSet.TestSetFile);
        await WriteCsvAsync(testSet, [CopilotStudioTestSet.QuestionHeading, CopilotStudioTestSet.ExpectedResponseHeading],
            exported.Select(question => new[] { question.Question, question.ExpectedResponse }), cancellationToken);

        Console.WriteLine($"Wrote {exported.Count} questions to {Path.GetFullPath(testSet)}");
        if (questions.Count > CopilotStudioTestSet.MaxQuestions)
            Console.WriteLine($"Only the first {CopilotStudioTestSet.MaxQuestions} were written - Copilot Studio's limit for one test set.");

        return 0;
    }

    // A case's own "copilotStudio" object, if it has one, sets its question and expected response; otherwise both come
    // from its subject and the facts it must mention.
    private static Case ToCase(JsonObject testCase, string relativePath, IReadOnlyList<string> refusals)
    {
        var copilotStudio = testCase[TestCaseFields.CopilotStudio]?.AsObject();
        var subject = SubjectOf(testCase, relativePath);

        // Markdown headings check the briefing agents' layout, and refusals a specialist's empty answer: neither is a
        // fact a whole briefing gives.
        var facts = (ReadList(testCase, TestCaseFields.MustMention) ?? [])
            .Where(fact => !fact.StartsWith(CopilotStudioTestSet.MarkdownHeadingPrefix, StringComparison.Ordinal))
            .Where(fact => !refusals.Contains(fact, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new Case(ReadText(copilotStudio, TestCaseFields.CopilotStudioQuestion) ?? $"Give me a briefing on {subject}.", subject,
            ReadText(copilotStudio, TestCaseFields.CopilotStudioExpectedResponse), facts);
    }

    private static string DefaultExpectedResponse(string subject, IReadOnlyList<string> facts)
        => facts.Count == 0
            ? $"A briefing on {subject}."
            : $"A briefing on {subject} that includes: {string.Join(CopilotStudioTestSet.ListSeparator, facts)}.";

    // "Exampleton Primary School (URN 900001)".
    private static string SubjectOf(JsonObject testCase, string relativePath)
    {
        var subject = testCase[TestCaseFields.Subject]?.AsObject()
            ?? throw new InvalidDataException($"{relativePath} needs a \"{TestCaseFields.Subject}\".");
        var name = ReadText(subject, TestCaseFields.SubjectName) ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectName));
        var urnOrTrn = ReadText(subject, TestCaseFields.SubjectUrnOrTrn) ?? throw new InvalidDataException(TestCaseErrors.SubjectNeeds(relativePath, TestCaseFields.SubjectUrnOrTrn));

        var identifier = BriefingParameters.FromReference(urnOrTrn, name).IsTrust ? "TRN" : "URN";
        return $"{name} ({identifier} {urnOrTrn})";
    }

    // The release gate's refusal phrases, e.g. "No information available": answers checked by their wording, not facts.
    private static string[] RefusalPhrases()
        => new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(Settings.AppSettingsFile, optional: true).Build()
            .GetSection(Settings.RefusalPhrasesKey).Get<string[]>() ?? [];

    private static string? ReadText(JsonObject? node, string property)
        => node?[property]?.GetValue<string>() is { Length: > 0 } value ? value : null;

    private static List<string>? ReadList(JsonObject? node, string property)
        => node?[property]?.AsArray().Select(item => item?.GetValue<string>()).OfType<string>().ToList() is { Count: > 0 } values ? values : null;

    private static async Task WriteCsvAsync(string path, string[] headings, IEnumerable<string[]> rows, CancellationToken cancellationToken)
    {
        var csv = new StringBuilder().AppendLine(string.Join(',', headings.Select(Quote)));
        foreach (var row in rows)
            csv.AppendLine(string.Join(',', row.Select(Quote)));

        await File.WriteAllTextAsync(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
