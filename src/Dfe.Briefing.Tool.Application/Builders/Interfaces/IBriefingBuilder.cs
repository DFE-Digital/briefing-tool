using Dfe.Briefing.Tool.Domain.ValueObjects;
using GovUK.Dfe.AI.Agents.Tools;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Briefing.Tool.Application.Builders.Interfaces;

/// <summary>
/// Builds and formats briefing prompts, evidence, and related web-search content for school or trust briefings.
/// </summary>
public interface IBriefingBuilder
{
    /// <summary>
    /// Builds an AI result that indicates no supporting evidence was found for the briefing request.
    /// </summary>
    /// <param name="parameters">Input values used to construct the no-evidence briefing result.</param>
    /// <returns>An <see cref="AIResult"/> representing a no-evidence outcome for the requested briefing.</returns>
    AIResult BuildNoEvidenceResult(BriefingParameters parameters);

    /// <summary>
    /// Builds the prompt text used to generate a briefing.
    /// </summary>
    /// <param name="parameters">Contains the inputs required to compose the briefing prompt.</param>
    /// <returns>A formatted prompt string for briefing generation.</returns>
    string BuildBriefingPrompt(BriefingParameters parameters);

    /// <summary>
    /// Builds briefing evidence from the supplied parameters and records.
    /// </summary>
    /// <param name="parameters">Configuration and options that define how briefing evidence is assembled.</param>
    /// <param name="records">Source briefing records used to produce the evidence.</param>
    /// <returns>An <see cref="AgentEvidence"/> instance containing the assembled briefing evidence.</returns>
    AgentEvidence BuildBriefingEvidence(BriefingParameters parameters, BriefingRecords records);

    /// <summary>
    /// Builds a web search instruction string for a briefing request.
    /// </summary>
    /// <param name="parameters">Contains the briefing options used to compose the search instruction.</param>
    /// <param name="city">Specifies the city to scope the search, or <see langword="null"/> to leave location unspecified.</param>
    /// <returns>A formatted instruction string for use in web search.</returns>
    string BuildWebSearchInstruction(BriefingParameters parameters, string? city);

    /// <summary>
    /// Builds an <c>AgentSpec</c> for a web search agent using the provided tools.
    /// </summary>
    /// <param name="tools">Tools to include in the agent specification.</param>
    /// <returns>An <c>AgentSpec</c> configured for web search.</returns>
    AgentSpec BuildWebSearchAgentSpec(IReadOnlyList<AgentTool> tools);

    /// <summary>
    /// Builds a web-search evidence payload from the provided briefing.
    /// </summary>
    /// <param name="briefing">Briefing text used to derive web-search evidence.</param>
    /// <returns>A string containing the generated web-search evidence.</returns>
    string BuildWebSearchEvidence(string briefing);

    /// <summary>
    /// Builds a news findings section from web search results and briefing options.
    /// </summary>
    /// <param name="webSearchResult">Web search result data used to derive news findings.</param>
    /// <param name="parameters">Briefing settings that control how findings are selected and formatted.</param>
    /// <param name="website">Optional website to scope or prioritize findings; null uses all available sources.</param>
    /// <returns>A formatted string containing the generated news findings.</returns>
    string BuildNewsFindings(AgentResult webSearchResult, BriefingParameters parameters, string? website);

    /// <summary>
    /// Determines whether a briefing contains a news section.
    /// </summary>
    /// <param name="briefing">The briefing text to evaluate for a news section.</param>
    /// <returns><see langword="true"/> if the briefing contains a news section; otherwise, <see langword="false"/>.</returns>
    bool HasNewsSection(string briefing);

    /// <summary>
    /// Removes the background section from a briefing.
    /// </summary>
    /// <param name="briefing">Briefing text that may include a background section.</param>
    /// <returns>Briefing text with the background section removed, or <see langword="null"/> when <paramref name="briefing"/> is
    /// <see langword="null"/>.</returns>
    string? RemoveBackgroundSection(string? briefing);

    /// <summary>
    /// Removes empty sections from briefing text.
    /// </summary>
    /// <param name="briefing">Briefing text to process.</param>
    /// <returns>Briefing text with empty sections removed, or <see langword="null"/> when <paramref name="briefing"/> is <see
    /// langword="null"/>.</returns>
    string? RemoveEmptySections(string? briefing);

    /// <summary>
    /// Presents briefing content and its associated city from the provided output text.
    /// </summary>
    /// <param name="output">Output text containing the briefing information to present.</param>
    /// <returns>A tuple where <c>Briefing</c> is the briefing text and <c>City</c> is the associated city; each value is <see
    /// langword="null" /> when not available.</returns>
    (string? Briefing, string? City) PresentBriefing(string? output);
}
