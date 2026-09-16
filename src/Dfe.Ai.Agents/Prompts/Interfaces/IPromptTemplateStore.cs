namespace Dfe.Ai.Agents.Prompts.Interfaces;

/// <summary>
/// Provides access to prompt templates by key.
/// </summary>
public interface IPromptTemplateStore
{
    /// <summary>
    /// Gets the prompt template for the specified key.
    /// </summary>
    /// <param name="key">The prompt template key.</param>
    /// <returns>The prompt template.</returns>
    string GetTemplate(string key);
}
