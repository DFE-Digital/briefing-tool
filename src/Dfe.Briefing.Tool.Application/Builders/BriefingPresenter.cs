using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Helpers;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Turns the briefing agent's answer into the briefing a user sees: with every heading written as Markdown ("## Name"),
/// and without a location marker the agent writes anyway, the background section (added only after a web search) or a
/// section left empty. The app shows this, and the release
/// gate checks and scores it (<c>AgentDefinition.PresentOutput</c>), so both see the same briefing.
/// </summary>
internal static partial class BriefingPresenter
{
    /// <summary>The briefing as shown.</summary>
    public static string? Present(string? output)
        => RemoveEmptySections(RemoveBackgroundSection(SpaceHeadings(CityExtractorHelper.RemoveCityMarker(output))));

    // "##Ofsted Summary" isn't a Markdown heading, so it would show as text and no step that finds a section by its
    // heading would find it: the space after the "#"s is put back first.
    public static string? SpaceHeadings(string? briefing)
        => briefing is null ? null : UnspacedHeadingRegex().Replace(briefing, "${hashes} ");

    /// <summary>The briefing as shown, for <c>AgentDefinition.PresentOutput</c>.</summary>
    public static string AsShown(string output) => Present(output) ?? string.Empty;

    public static string? RemoveBackgroundSection(string? briefing)
        => briefing is null ? null : BackgroundSectionRegex().Replace(briefing, string.Empty);

    // The briefing agent is told to leave out a section it has no facts for, but sometimes leaves its heading with
    // nothing under it - a "##" heading followed straight by another heading, or by the end, is removed here.
    public static string? RemoveEmptySections(string? briefing)
        => briefing is null ? null : EmptySectionRegex().Replace(briefing, string.Empty);

    // The section runs to the next "##" heading ("###" doesn't end it) or the end of the briefing.
    [GeneratedRegex(@"^##[ \t]+More background information[^\r\n]*(?:\r?\n|\z)[\s\S]*?(?=^##[ \t]|\z)", RegexOptions.Multiline)]
    private static partial Regex BackgroundSectionRegex();

    [GeneratedRegex(@"^##[ \t]+[^\r\n]*\r?\n(?:[ \t]*\r?\n)*(?=^##?[ \t]|\z)", RegexOptions.Multiline)]
    private static partial Regex EmptySectionRegex();

    // One to three "#"s starting a line, straight before a letter.
    [GeneratedRegex(@"^(?<hashes>#{1,3})(?=\p{L})", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnspacedHeadingRegex();
}
