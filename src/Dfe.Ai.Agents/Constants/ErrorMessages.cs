namespace Dfe.Ai.Agents.Constants;

internal static class ErrorMessages
{
    public const string NoAzureSearchInformationFound = "No {0} information found."; 
    public const string NoAzureSearchClientConfigured = "No Azure Search client configured for '{0}'.";
    public const string ToolCallsRequiredNoCallback = "Response {0} paused for tool calls but no resolveToolCalls callback was supplied.";
    public const string MissingToolCallOutput = "No tool output was resolved for call '{0}'.";
    public const string ToolCallRoundLimitExceeded = "Agent '{0}' exceeded the maximum of {1} tool-call rounds in a single run.";
    public const string ResponseDidNotComplete = "Agent '{0}' response {1} did not complete (status '{2}'){3}.";
    public const string McpToolsNotFoundOnServer = "MCP server '{0}' does not expose the following configured tool(s): {1}.";
}
