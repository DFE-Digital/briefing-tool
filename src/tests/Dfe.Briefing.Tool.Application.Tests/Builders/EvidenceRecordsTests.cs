using Dfe.Briefing.Tool.Application.Builders;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Builders;

public sealed class EvidenceRecordsTests
{
    private static ContextResult Records(params string[] records)
        => new(string.Join("\n\n", records), HasEvidence: true)
        {
            Sources = [.. records.Select((_, i) => new EvidenceSource(i + 1, $"Source {i + 1}"))]
        };

    [Fact]
    public void WithoutDuplicates_LeavesOutAnExactRepeat_AndItsSource()
    {
        var records = Records(
            "[Evidence 1]\nestablishment_urn: 900001\nnumber_on_roll: 431",
            "[Evidence 2]\nestablishment_urn: 900002\nnumber_on_roll: 200",
            "[Evidence 3]\nestablishment_urn: 900001\nnumber_on_roll: 431");

        var result = EvidenceRecords.WithoutDuplicates(records, "establishment_urn");

        Assert.Equal("[Evidence 1]\nestablishment_urn: 900001\nnumber_on_roll: 431\n\n[Evidence 2]\nestablishment_urn: 900002\nnumber_on_roll: 200", result.Text);
        Assert.Equal([1, 2], result.Sources.Select(source => source.Number));
    }

    [Fact]
    public void WithoutDuplicates_LeavesOutALessCompleteCopy_AndKeepsTheFullerRecord_WhereverItIs()
    {
        var records = Records(
            "[Evidence 1]\nestablishment_urn: 900001\nnumber_on_roll: 431",
            "[Evidence 2]\nestablishment_urn: 900001\nnumber_on_roll: 431\nschool_capacity: 420");

        var result = EvidenceRecords.WithoutDuplicates(records, "establishment_urn");

        Assert.Equal("[Evidence 2]\nestablishment_urn: 900001\nnumber_on_roll: 431\nschool_capacity: 420", result.Text);
    }

    [Theory]
    [InlineData("[Evidence 2]\nestablishment_urn: 900001\nnumber_on_roll: 440")]
    [InlineData("[Evidence 2]\nestablishment_urn: 900002\nnumber_on_roll: 431")]
    [InlineData("[Evidence 2]\nnumber_on_roll: 431")]
    public void WithoutDuplicates_KeepsRecordsThatDisagree_AreAboutAnotherSchool_OrHaveNoKey(string second)
    {
        var records = Records("[Evidence 1]\nestablishment_urn: 900001\nnumber_on_roll: 431", second);

        Assert.Same(records, EvidenceRecords.WithoutDuplicates(records, "establishment_urn"));
    }

    [Fact]
    public void WithoutDuplicates_WithNoKey_ComparesEveryRecord()
    {
        var records = Records(
            "--- rise_concerns_index Evidence 1 ---\nconcern_date: 2025-02-10\nconcern_type: Behaviour",
            "--- rise_concerns_index Evidence 2 ---\nconcern_date: 2025-02-10\nconcern_type: Behaviour");

        var result = EvidenceRecords.WithoutDuplicates(records, keyField: null);

        Assert.Equal("--- rise_concerns_index Evidence 1 ---\nconcern_date: 2025-02-10\nconcern_type: Behaviour", result.Text);
        Assert.Equal([1], result.Sources.Select(source => source.Number));
    }

    [Fact]
    public void WithoutDuplicates_LeavesASetWithNoEvidenceAsItIs()
    {
        var records = new ContextResult("This school is not part of a trust.", HasEvidence: false);

        Assert.Same(records, EvidenceRecords.WithoutDuplicates(records, "trust_reference_number"));
    }
}
