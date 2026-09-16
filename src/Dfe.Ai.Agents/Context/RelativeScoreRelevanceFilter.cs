using Dfe.Ai.Agents.Context.Interfaces;
using Dfe.Ai.Agents.Models;

namespace Dfe.Ai.Agents.Context;

public sealed class RelativeScoreRelevanceFilter(double minRelativeScore = 0.5) : IRelevanceFilter
{
    public IReadOnlyList<SearchResultItem> Filter(IReadOnlyList<SearchResultItem> results)
    {
        var scored = results.Where(x => x.Score is > 0).ToList();
        if (scored.Count == 0)
        {
            return results;
        }

        var topScore = scored.Max(x => x.Score!.Value);

        return [.. results.Where(x => x.Score is > 0 && x.Score.Value >= topScore * minRelativeScore)];
    }
}
