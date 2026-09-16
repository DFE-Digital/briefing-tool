namespace Dfe.Briefing.Tool.Application.Services.Interfaces;

public interface IPromptRetrieverService
{
    string GetSystemPrompt(string promptType);
    string GetUserPrompt(string promptType);
}
