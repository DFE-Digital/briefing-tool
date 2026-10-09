using Dfe.Briefing.Tool.Application.Builders;
using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Domain.Constants;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Extensions;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class BriefingBuilderTests
{
    private static readonly AgentDefinition Establishment = new(AgentNames.Establishment, SystemPromptKey.Establishment);
    private static readonly AgentDefinition Trust = new(AgentNames.Trust, SystemPromptKey.Trust);

    private readonly BriefingBuilder _sut = new();

    private static AgentResult Result(string agentName, string output)
        => new() { AgentName = agentName, Output = output, TotalTokens = 10 };

    [Fact]
    public void BuildNoEvidenceResult_ForTrustAgent_ReturnsNoTrustInformationFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(Trust, new BriefingParameters("TR12345", "Test Trust"));

        Assert.Contains("No trust information found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_ForEstablishmentAgent_ReturnsNoSchoolOrAcademyFoundMessage()
    {
        var result = _sut.BuildNoEvidenceResult(Establishment, new BriefingParameters("123456", "Test Academy"));

        Assert.Contains("No school or academy found.", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, result.TotalTokens);
    }

    [Fact]
    public void BuildNoEvidenceResult_StartsWithAMarkdownHeadingOfTheSubjectName()
    {
        var result = _sut.BuildNoEvidenceResult(Establishment, new BriefingParameters("123456", "Smith & Jones Academy"));

        Assert.StartsWith("# Smith & Jones Academy", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileContext_JoinsEachResult_WithItsDisplayNameAndOutput()
    {
        var context = _sut.CompileContext([Result(AgentNames.Establishment, "establishment-output"), Result(AgentNames.Ofsted, "ofsted-output")]);

        Assert.Contains($"AGENT: {AgentNames.Establishment.ToDisplayName()}", context, StringComparison.Ordinal);
        Assert.Contains("establishment-output", context, StringComparison.Ordinal);
        Assert.Contains($"AGENT: {AgentNames.Ofsted.ToDisplayName()}", context, StringComparison.Ordinal);
        Assert.Contains("ofsted-output", context, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithNoCity_OmitsTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy"), city: null);

        Assert.DoesNotContain("located in or near", instruction, StringComparison.Ordinal);
        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWebSearchInstruction_WithACity_IncludesTheLocationHint()
    {
        var instruction = _sut.BuildWebSearchInstruction(new BriefingParameters("123456", "Test Academy"), city: "Coventry");

        Assert.Contains("located in or near Coventry", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSubjectInstruction_IncludesTheSchoolOrTrustNameAndUrnOrTrn()
    {
        var instruction = _sut.BuildSubjectInstruction(new BriefingParameters("123456", "Test Academy"));

        Assert.Contains("Test Academy", instruction, StringComparison.Ordinal);
        Assert.Contains("123456", instruction, StringComparison.Ordinal);
    }
}
