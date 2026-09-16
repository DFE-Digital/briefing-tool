using Dfe.Briefing.Tool.Application.Configs;
using Dfe.Briefing.Tool.Application.Constants;
using Dfe.Briefing.Tool.Application.FileRetrievers;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Dfe.Briefing.Tool.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;
public class PromptRetrieverService(IPromptFileReader fileReader, PromptConfig promptConfiguration, ILogger<PromptRetrieverService> logger) : IPromptRetrieverService
{
    private const string StandardAgentResponseFormatKey = "StandardAgentResponseFormat";

    public string GetSystemPrompt(string promptType)
    {
        var prompt = GetPrompt(promptConfiguration.SystemPrompts, promptType, GetSystemEmbeddedFallback);

        if (promptType == SystemPromptType.Synthesis)
        {
            return prompt;
        }

        var responseFormat = GetPrompt(promptConfiguration.SystemPrompts, StandardAgentResponseFormatKey,
            _ => Messages.StandardAgentResponseFormat);
        return $"{prompt}{Environment.NewLine}{Environment.NewLine}{responseFormat}";
    }

    public string GetUserPrompt(string promptType) =>
        GetPrompt(promptConfiguration.UserPrompts, promptType, GetUserEmbeddedFallback);

    private string GetPrompt(IDictionary<string, string> prompts, string promptType, Func<string, string> fallback)
    {
        if (!prompts.TryGetValue(promptType, out var path))
        {
            logger.LogWarning(ErrorMessages.NoPromptConfigured, promptType);
            return fallback(promptType);
        }

        try
        {
            return fileReader.Read(path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ErrorMessages.FailedToLoadPromptFile, promptType);
            return fallback(promptType);
        }
    }

    private static string GetSystemEmbeddedFallback(string promptType) => promptType switch
    {
        SystemPromptType.Synthesis => "You are a tool that helps civil servants at the UK Department for Education write reports, briefs and submissions to regional directors",
        SystemPromptType.Establishment => "You are a tool that helps civil servants at the UK Department for Education analyse academy establishment data with structured reasoning",
        SystemPromptType.Ofsted => "You are an Ofsted specialist who analyses supplied evidence to identify the latest outcome, strengths, concerns, trends and overall implications",
        SystemPromptType.RiseConcerns => "You are a senior academy risk analyst who identifies significant Rise concerns from supplied evidence, with impact and recommended follow-up",
        SystemPromptType.RecastConcerns => "You are a senior academy risk analyst who identifies significant Recast concerns from supplied evidence, with impact and recommended follow-up",
        SystemPromptType.Trust => "You are a trust information analyst who produces a factual summary of an academy's trust information from supplied evidence",
        _ => Messages.GeneralPurposeSystemPrompt
    };

    private static string GetUserEmbeddedFallback(string promptType) => promptType switch
    {
        UserPromptType.Synthesis => "You are analysing Synthesis data with structured reasoning.",
        _ => Messages.GeneralPurposeUserPrompt
    };
}
