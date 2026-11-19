namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Puts the academy bullets in a trust's briefing in the order worked out in code (<see cref="AcademyOrders"/>), after
/// the agent has written it: the Ofsted summary, KS2 summary, KS4 summary and Attendance sections.
/// </summary>
/// <remarks>
/// Only whole bullets move, each with the lines indented under it; a section's sentences stay where they are. A bullet
/// that doesn't start with an academy in the order (for example "Whole trust") stays ahead of the academies, in the
/// order the agent wrote it, so nothing is ever dropped.
/// </remarks>
internal static class AcademyListOrder
{
    private const string SectionPrefix = "## ";
    private const string TitlePrefix = "# ";
    private const string BulletPrefix = "- ";
    private const char NameEnd = ':';
    private const int NotInOrder = -1;

    /// <summary><paramref name="briefing"/> with each section's academy bullets in <paramref name="orders"/>' order.</summary>
    public static string? Apply(string? briefing, AcademyOrders? orders)
    {
        if (string.IsNullOrEmpty(briefing) || orders is null)
            return briefing;

        var sections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ofsted summary"] = orders.Ofsted,
            ["KS2 summary"] = orders.Ks2,
            ["KS4 summary"] = orders.Ks4,
            ["Attendance"] = orders.Absence,
        };

        var lines = briefing.Split('\n');
        var result = new List<string>(lines.Length);
        IReadOnlyList<string>? order = null;
        var run = new List<List<string>>();

        foreach (var line in lines)
        {
            if (IsBullet(line) || (run.Count > 0 && IsIndented(line)))
            {
                if (IsBullet(line))
                    run.Add([line]);
                else
                    run[^1].Add(line);
                continue;
            }

            Flush(result, run, order);
            if (line.StartsWith(SectionPrefix, StringComparison.Ordinal) || line.StartsWith(TitlePrefix, StringComparison.Ordinal))
                order = sections.GetValueOrDefault(line.TrimStart('#').Trim());
            result.Add(line);
        }

        Flush(result, run, order);
        return string.Join('\n', result);
    }

    // A run of bullets, in the section's order when it has one; otherwise as written.
    private static void Flush(List<string> result, List<List<string>> run, IReadOnlyList<string>? order)
    {
        var bullets = order is null || order.Count == 0 ? run : [.. run.OrderBy(bullet => Rank(bullet[0], order))];
        result.AddRange(bullets.SelectMany(static bullet => bullet));
        run.Clear();
    }

    // The academy's place in the order, from the name its bullet starts with, before the first colon.
    private static int Rank(string bullet, IReadOnlyList<string> order)
    {
        var text = bullet[BulletPrefix.Length..];
        var end = text.IndexOf(NameEnd, StringComparison.Ordinal);
        var name = (end < 0 ? text : text[..end]).Replace("**", string.Empty, StringComparison.Ordinal).Trim();
        for (var rank = 0; rank < order.Count; rank++)
        {
            if (string.Equals(order[rank].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return rank;
        }

        return NotInOrder;
    }

    private static bool IsBullet(string line) => line.StartsWith(BulletPrefix, StringComparison.Ordinal);

    private static bool IsIndented(string line) => line.Length > 0 && char.IsWhiteSpace(line[0]) && line.Trim().Length > 0;
}
