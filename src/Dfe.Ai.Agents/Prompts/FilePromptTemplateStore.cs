using Dfe.Ai.Agents.Prompts.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.Ai.Agents.Prompts;

public sealed class FilePromptTemplateStore(IReadOnlyDictionary<string, string> paths, Func<string, string> readFile,
    Func<string, string> fallback, ILogger<FilePromptTemplateStore>? logger = null) : IPromptTemplateStore
{
    private readonly ILogger<FilePromptTemplateStore> _logger = logger ?? NullLogger<FilePromptTemplateStore>.Instance;

    public string GetTemplate(string key)
    {
        if (!paths.TryGetValue(key, out var path))
        {
            _logger.LogWarning("No prompt file configured for {Key}; using fallback.", key);
            return fallback(key);
        }

        try
        {
            return readFile(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prompt file for {Key}; using fallback.", key);
            return fallback(key);
        }
    }
}
