using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class ConcernRedactorTests
{
    private static string Redact(string summary)
        => ConcernRedactor.Redact(new ContextResult($"[Evidence 1]\nconcern_date: 2025-02-10\nsummary: {summary}", HasEvidence: true))
            .Text["[Evidence 1]\nconcern_date: 2025-02-10\nsummary: ".Length..];

    [Theory]
    [InlineData("Complaint about pupil (UPN A900001250017) and bullying.", "Complaint about pupil (UPN (removed)) and bullying.")]
    [InlineData("Temporary UPN B12345678901C given.", "Temporary UPN (removed) given.")]
    [InlineData("Pupil DOB 2014-03-04, year 5.", "Pupil DOB (removed), year 5.")]
    [InlineData("Pupil (D.O.B.: 04/03/2014).", "Pupil (D.O.B.: (removed)).")]
    [InlineData("Date of birth 4 March 2014.", "Date of birth (removed).")]
    [InlineData("A child born on 4th March 2014.", "A child born on (removed).")]
    [InlineData("Email the parent at parent.name@example.co.uk now.", "Email the parent at (removed) now.")]
    [InlineData("Call 01234 567890 or 07700-900123.", "Call (removed) or (removed).")]
    [InlineData("Call +44 (0)1234 567890.", "Call (removed).")]
    public void Redact_TakesOutEachPersonalDetailItCanRecognise(string summary, string expected)
        => Assert.Equal(expected, Redact(summary));

    [Theory]
    [InlineData("Exampleton Primary School (URN 900001) raised a concern on 2025-02-10.")]
    [InlineData("Example Learning Trust (TR09001) has 1,250 pupils.")]
    [InlineData("Roll fell from 431 to 420 in 2024.")]
    public void Redact_LeavesSchoolDetailsDatesAndFiguresAsTheyAre(string summary)
        => Assert.Equal(summary, Redact(summary));

    [Fact]
    public void Redact_KeepsTheSources_AndLeavesASetWithNoEvidenceAsItIs()
    {
        var concerns = new ContextResult("[Evidence 1]\nsummary: UPN A900001250017", HasEvidence: true) { Sources = [new EvidenceSource(1, "Rise")] };
        var none = new ContextResult("No concerns.", HasEvidence: false);

        Assert.Equal(concerns.Sources, ConcernRedactor.Redact(concerns).Sources);
        Assert.Same(none, ConcernRedactor.Redact(none));
    }
}
