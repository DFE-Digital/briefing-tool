using System.Text.RegularExpressions;
using Dfe.Briefing.Tool.Application.Helpers;

namespace Dfe.Briefing.Tool.Application.Builders;

/// <summary>
/// Turns the briefing agent's answer into the briefing a user sees: with every heading written as Markdown ("## Name"),
/// no academy name in square brackets at the start of a line, and without a location marker the agent writes anyway, the background section (added only after a web search) or a
/// section left empty. The app shows this, and the release
/// gate checks and scores it (<c>AgentDefinition.PresentOutput</c>), so both see the same briefing.
/// </summary>
internal static partial class BriefingPresenter
{
    /// <summary>The briefing as shown.</summary>
    public static string? Present(string? output)
        => RemoveEmptySections(RemoveBackgroundSection(UnbracketLeadingNames(SpaceHeadings(CityExtractorHelper.RemoveCityMarker(output)))));

    // A citation ends a sentence or bullet, never starts one, so "- [Allestree Woodlands School]: Good" is a name the agent
    // bracketed against its instructions: shown as written, the page would take it for a citation, turn it into a
    // reference number and the reader would lose the name. The brackets are taken off; a link ("[name](address)") or a
    // citation rendered as a link ("[<a href=...>name</a>]") is left alone.
    public static string? UnbracketLeadingNames(string? briefing)
        => briefing is null ? null : LeadingBracketedNameRegex().Replace(briefing, "${lead}${name}");

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

    // At the start of a line, after any bullet, number or heading marker: "[text]" not followed by "(", and not an
    // "[Evidence n]" citation left as written.
    [GeneratedRegex(@"^(?<lead>[ \t]*(?:(?:[-*+]|\d+\.)[ \t]+|#{1,6}[ \t]+)?)\[(?!Evidence \d+\])(?<name>[^\[\]<>\r\n]+)\](?!\()",RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingBracketedNameRegex();
}
