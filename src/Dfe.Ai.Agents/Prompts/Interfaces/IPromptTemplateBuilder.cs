namespace Dfe.Ai.Agents.Prompts.Interfaces;

/// <summary>
/// Builds prompt templates from a key and values.
/// </summary>
public interface IPromptTemplateBuilder
{
    /// <summary>
    /// Builds a prompt template using the specified key and values.
    /// </summary>
    /// <param name="key">The key for the prompt template.</param>
    /// <param name="values">The values to substitute into the prompt template.</param>
    /// <returns>The built prompt template.</returns>
    string Build(string key, IReadOnlyDictionary<string, string> values);
}
