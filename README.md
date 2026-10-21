# briefing-tool
Generates comprehensive school and trust briefings with detailed insights

## Required roles

The app's identity (the `AiAgents:Authentication` service principal, or a managed identity) needs:

| Role | Scope | Required | Why |
| --- | --- | --- | --- |
| **Foundry User** | Foundry project | Mandatory | Lets the app create and run agents |
| **Search Index Data Reader** | Azure AI Search service | Mandatory | The app uses `AiAgents:AISearch` for evidence retrieval |
| **Storage Blob Data Contributor** | Storage account or container | Only if `GlobalConcurrency` is configured | Currently commented out in `appsettings.json`, so not needed |

## Telemetry

Logs, traces and AI agent metrics (token usage, execution duration and tool calls) are sent to Application Insights via Azure Monitor OpenTelemetry (`InfrastructureExtension.AddInfrastructure`).

Set `APPLICATIONINSIGHTS_CONNECTION_STRING` to enable telemetry. Without it, the app runs but telemetry is not exported.

### Token usage metrics

Recorded in `customMetrics` using OpenTelemetry GenAI conventions.

| Metric                                                          | Description                                  |
| --------------------------------------------------------------- | -------------------------------------------- |
| `gen_ai.client.inference.usage.input_tokens` / `.output_tokens` | Input/output tokens per model call           |
| `gen_ai.invoke_agent.duration`                                  | Agent execution duration                     |
| `gen_ai.invoke_agent.inference_calls` / `.tool_calls`           | Model and tool calls per agent run           |
| `dfe.ai_agents.workflow.tokens`                                 | Total briefing tokens by type (input/output) |
| `dfe.ai_agents.run_slot.wait.duration`                          | Concurrency wait time                        |
| `dfe.ai_agents.cost` / `dfe.ai_agents.workflow.cost`            | Agent and briefing costs                     |
| `dfe.ai_agents.tokens.remaining`                                | Remaining deployment tokens per minute       |
| `dfe.ai_agents.tokens.low`                                      | Responses below the low-token threshold      |

> **Configurations**
> - `AiAgents:Pricing` – Model rates per 1,000 tokens in `appsettings.json`.
> - `AiAgents:LowRemainingTokensPercent` – Low-token threshold (default: 10%).
> - `AiAgents:ApplicationName` – The `cloud_RoleName` the telemetry is tagged with ("briefing-tool"). Every query
>   below filters on it, so other apps sharing the Application Insights resource aren't counted.

#### Daily token usage and cost by agent

```kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name in (
    "gen_ai.client.inference.usage.input_tokens",
    "gen_ai.client.inference.usage.output_tokens",
    "dfe.ai_agents.cost"
)
| summarize
    input_tokens = sumif(valueSum, name == "gen_ai.client.inference.usage.input_tokens"),
    output_tokens = sumif(valueSum, name == "gen_ai.client.inference.usage.output_tokens"),
    cost = sumif(valueSum, name == "dfe.ai_agents.cost")
    by agent = tostring(customDimensions["gen_ai.agent.name"]),
       bin(timestamp, 1d)
| extend total_tokens = input_tokens + output_tokens
| order by timestamp asc, agent asc
```

#### Briefing token usage and cost (per minute)

```kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name in ("dfe.ai_agents.workflow.tokens", "dfe.ai_agents.workflow.cost")
| summarize
    inputTokens = sumif(valueSum, name == "dfe.ai_agents.workflow.tokens" and tostring(customDimensions["gen_ai.token.type"]) == "input"),
    outputTokens = sumif(valueSum, name == "dfe.ai_agents.workflow.tokens" and tostring(customDimensions["gen_ai.token.type"]) == "output"),
    cost = sumif(valueSum, name == "dfe.ai_agents.workflow.cost")
    by bin(timestamp, 1m)
| extend totalTokens = inputTokens + outputTokens
| project timestamp, inputTokens, outputTokens, totalTokens, cost
| order by timestamp desc
```

#### Agent token usage and remaining token balance (per second)

```kusto
let remaining = customMetrics
| where cloud_RoleName == "briefing-tool"
| where name == "dfe.ai_agents.tokens.remaining"
| summarize remainingTokens = min(valueMin) by bin(timestamp, 1s);
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name in (
    "gen_ai.client.inference.usage.input_tokens",
    "gen_ai.client.inference.usage.output_tokens"
)
| summarize
    inputTokens = sumif(valueSum, name == "gen_ai.client.inference.usage.input_tokens"),
    outputTokens = sumif(valueSum, name == "gen_ai.client.inference.usage.output_tokens")
    by bin(timestamp, 1s), agent = tostring(customDimensions["gen_ai.agent.name"])
| extend totalTokens = inputTokens + outputTokens
| join kind=leftouter (remaining) on timestamp
| project timestamp, agent, inputTokens, outputTokens, totalTokens, remainingTokens
| order by timestamp desc
```

#### Concurrency pressure – Hourly average execution-slot wait time. Sustained increases may require adjusting `MaxConcurrency` or `GlobalConcurrency.MaxConcurrentRuns`

- Low (0–100ms): No action
- Moderate (100–1,000ms): Monitor
- High (1,000–3,000ms): Investigate limits
- Critical (>3,000ms): Consider increasing capacity

```kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name == "dfe.ai_agents.run_slot.wait.duration"
| summarize
    calls = sum(valueCount),
    totalWaitMs = sum(valueSum) * 1000,
    maxWaitMs = max(valueMax) * 1000
    by timestamp = bin(timestamp, 1h)
| extend avgWaitMs = round(totalWaitMs / iff(calls == 0, 1, calls), 3)
| project timestamp, calls, avgWaitMs, maxWaitMs, totalWaitMs
| order by timestamp desc
```

#### Hourly low-token response count

```kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name == "dfe.ai_agents.tokens.low"
| summarize times = sum(valueSum) by bin(timestamp, 1h)
| order by timestamp desc
```

### App logs

Recorded as `traces`, tagged with `cloud_RoleName` ("briefing-tool"):

```kusto
traces
| where cloud_RoleName == "briefing-tool"
| order by timestamp desc
| take 100
```
