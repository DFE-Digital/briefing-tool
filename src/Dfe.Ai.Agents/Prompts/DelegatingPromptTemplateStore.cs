using Dfe.Ai.Agents.Prompts.Interfaces;

namespace Dfe.Ai.Agents.Prompts;

public sealed class DelegatingPromptTemplateStore(Func<string, string> getTemplate) : IPromptTemplateStore
{
    public string GetTemplate(string key) => getTemplate(key);
}
