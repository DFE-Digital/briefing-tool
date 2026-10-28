# briefing-tool

Generates school and trust briefings for ministers from DfE records, using AI agents in Azure AI Foundry.

| Project | What it is |
| --- | --- |
| `Dfe.Briefing.Tool.Web` | The web app |
| `Dfe.Briefing.Tool.Application` | Briefing logic: evidence, prompts, agents |
| `Dfe.Briefing.Tool.Infrastructure` | Registers the agents, shared by the web app and the console tool |
| `Dfe.Briefing.Tool.ReleaseGate` | Console tool: release gate, Copilot Studio export, agent clean-up |

## Set up

Settings are in `src/Dfe.Briefing.Tool.Web/appsettings.json`. Put secrets in user secrets locally (right-click
**Dfe.Briefing.Tool.Web**, **Manage User Secrets**), or environment variables elsewhere, e.g.
`AiAgents__Authentication__ClientSecret`. The console tool uses the same settings and secrets.

| Setting | Needed for |
| --- | --- |
| `AiAgents:Foundry:Endpoint`, `DefaultModel` | The Foundry project and model |
| `AiAgents:Authentication` (`TenantId`, `ClientId`, `ClientSecret`) | The app's identity, unless it's a managed identity |
| `AiAgents:AISearch:Endpoint` | The evidence search |
| `AiAgents:Guardrails:SubscriptionId`, `ResourceGroup` | The guardrail (off while empty) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry |

### Roles

| Role | Scope | Needed for |
|---|---|---|
| Foundry User | Foundry resource | Creating and running agents; publishing release gate results to Foundry |
| Search Index Data Reader | AI Search service | Searching for evidence |
| Reader | Foundry resource | Checking the guardrail at startup | 
| Cognitive Services Contributor | Foundry resource | Prompt Shields (`ScanPrompts`, `ScanEvidence`); Creating or updating the guardrail (`gate --apply-guardrails`) |
| Storage Blob Data Contributor | Storage container | Only if `GlobalConcurrency` is set |

## Console tool

In `src/Dfe.Briefing.Tool.ReleaseGate`, run `dotnet run` and pick from the menu, or give the command. In Visual Studio,
set it as the startup project and pick a profile beside the Start button.

| Command | Does |
| --- | --- |
| `dotnet run -- gate` | Runs the release gate: checks the guardrail, then every guardrail and evaluation test case (exit 1 if it fails) |
| `dotnet run -- export-copilot-studio copilot-studio` | Exports the test cases as Copilot Studio test sets |

With no console to ask in (e.g. in a pipeline), `dotnet run` runs the gate.

## Cleaning up agents

| What builds up | Cleaned up by |
| --- | --- |
| Orphaned temporary agents (e.g. a web search agent a crash left behind) | **Orphan agent sweep**, part of the daily agent sweep in the web app |
| Old versions of each agent (a new one on every prompt, model or guardrail change) | **Agent version sweep**, part of the daily agent sweep in the web app; and the library, whenever a new version is created |

The daily agent sweep runs at `Briefing:AgentSweepTime`: the orphan agent sweep first, then the agent version sweep.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Briefing:AgentSweepTime` | `01:00` | When the agent sweep runs, in UTC |
| `Briefing:OrphanAgentSweepEnabled` | `true` | Runs the orphan agent sweep |
| `Briefing:OrphanAgentMinimumAge` | `7200` | Seconds a temporary agent must be old before it's deleted. At least `AiAgents:RunTimeout` plus 90; less is refused and logged as an error |
| `AiAgents:RunTimeout` | `1800` | Seconds one agent run may take (library default 300). The orphan agent sweep needs it |
| `Briefing:AgentVersionSweepEnabled` | `true` | Runs the agent version sweep |
| `AiAgents:KeepLatestVersions` | `3` | Versions kept per agent |
| `AiAgents:ProtectedVersions` | none | Versions never deleted, e.g. `{ "synthesis-agent": [ "4" ] }` |

> **Note:**
>
> - **Never deleted:** pinned versions (`AiAgents:VersionPins`), protected versions, temporary agents a run could still be using, and other apps' agents.
> - **If a sweep fails:** it's logged as an error, the other sweep still runs, and both run again the next day.
> - **Both sweeps off:** the agent sweep isn't started at all.
> - **Several web app instances:** each runs the sweep once a day. Repeating it is harmless; to run it on one only, set both `Enabled` settings to `false` on the others.

## Guardrail

`AiAgents:Guardrails` puts a Foundry guardrail on every agent version. It's off until `SubscriptionId` and
`ResourceGroup` are set; then the app won't start if an agent's guardrail is missing or weakened.

| Protection | Blocks |
| --- | --- |
| The Foundry guardrail (`Targets: Agents`, as the model is behind a connection) | Harmful content, jailbreaks, protected material |
| `ScanPrompts` | Attacks in the prompt, e.g. in a typed school name |
| `ScanEvidence` | Instructions planted in the search results |
| `Foundry:GatewayGuardrailErrors` (`400 model_error`) | Counts the gateway's own content filter as a guardrail block, not an app error |

## Release gate

`gate` tests the agents before a release, with the web app's settings and prompts, using the AI agent library's
standard release check. It fails unless the guardrail is in place and every agent passes. Without the guardrail, no
test case runs.

### Test cases

One JSON file per case under `TestCases/<agent>/`. Prompts are never in a case: the gate builds them with the app's
`BriefingBuilder`, so it tests exactly what the app sends.

| Folder | Passes when |
| --- | --- |
| `guardrail/` (`"guardrailMustBlock": true`) | The attack is blocked |
| `evaluation/` | The answer has the right facts (`mustMention`, `mustNotMention`) and scores |

```json
{
  "subject": { "name": "Exampleton Primary School", "urnOrTrn": "900001" },
  "evidence": "[Evidence 1]\nestablishment_name: Exampleton Primary School\n...",
  "mustMention": [ "Requires improvement", "14 March 2023" ],
  "mustNotMention": [ "202324" ],
  "group": "primary"
}
```

Synthesis cases give `specialistOutputs` (the other agents' answers) instead of `evidence`.

| Tip | Why |
| --- | --- |
| Use made-up records | Real ones can hold concerns that mustn't be in the repository |
| Give each agent several scored cases | One case's score decides the result on its own, and the judge's scores vary |
| Check `mustNotMention` doesn't match inside a word in the evidence | Checks ignore case and match inside words ("Recast" is in "forecast") |
| Copy a passing `evaluation-reports/<agent>.json` into `Baselines/` | Later runs fail if a score falls by more than `Tolerance` |

### Settings

In `src/Dfe.Briefing.Tool.ReleaseGate/appsettings.json`, under `AiAgents:Evaluation:ReleaseGate`:

| Setting | Value | Meaning |
| --- | --- | --- |
| `MinimumScore` | 3.0 | Lowest average Groundedness and Relevance (1 to 5) |
| `Tolerance` | 0.2 | How far a score may fall below its baseline |
| `MaxGroupGap` | 1.0 | Largest score gap between case groups (fairness) |
| `Repeats` | 3 | Runs per case, averaged: steadier scores, but 3 times the model calls |
| `RefusalPhrases` | "No information available" | Answers checked by their wording, not scored |
| `PublishToFoundry` | true | Also shows each run on the Foundry project's **Evaluations** page |

### Results

| Where | What |
| --- | --- |
| Console | `PASS` or `FAIL` per agent, with each reason |
| `evaluation-reports/<agent>.json` | Every answer, score and failure |
| Foundry **Evaluations** | Foundry's own scores, per agent per run |
| Application Insights | Release metrics and logs (queries below) |

## Copilot Studio

`export-copilot-studio` writes the evaluation test cases as `copilot-studio/test-set.csv`, the question and expected
response file Copilot Studio's **Evaluation** section imports. Import it into the Copilot Studio agent, which produces
the whole briefing, and test with **Compare meaning** and **General quality**.

| Column | From |
| --- | --- |
| `Question` | "Give me a briefing on {name} ({URN or TRN})", one per school or trust, from every evaluation case about it |
| `Expected response` | "A briefing on {name} that includes: …", the facts those cases must mention |

> **Note*:*
>
> - Guardrail cases aren't exported.
> - A case's own `copilotStudio.question` or `copilotStudio.expectedResponse` is used instead, if it has one.
>
## Telemetry

Logs, traces and metrics go to Application Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. Every
metric is tagged with `dfe.ai_agents.application` (`AiAgents:ApplicationName`, "briefing-tool"), so apps sharing a
resource never mix. The console tool's telemetry has `cloud_RoleInstance` "release-gate". Metrics arrive a few minutes
after a run.

| Metric | Shows |
| --- | --- |
| `gen_ai.client.inference.usage.input_tokens` / `output_tokens`, `dfe.ai_agents.cost` | Tokens and cost per model call |
| `dfe.ai_agents.workflow.tokens` / `workflow.cost` | Tokens and cost per briefing |
| `gen_ai.invoke_agent.duration` | How long each agent run takes |
| `dfe.ai_agents.guardrail.checks`, `guardrail.blocks` | Guardrail checks, and what it blocked |
| `dfe.ai_agents.release_gate.agents` / `test_cases` / `score` | Release gate results |

**Release result per agent**, newest first:

```kusto
let app = "briefing-tool";
customMetrics
| where timestamp > ago(30d) and tostring(customDimensions["dfe.ai_agents.application"]) == app
| extend run = tostring(customDimensions["dfe.ai_agents.release_gate.run_id"])
| where isnotempty(run)
| extend agent = tostring(customDimensions["gen_ai.agent.name"]),
    outcome = tostring(coalesce(customDimensions["dfe.ai_agents.release_gate.outcome"], customDimensions["dfe.ai_agents.guardrail.outcome"])),
    testCase = tostring(customDimensions["dfe.ai_agents.release_gate.test_case"]),
    metric = tostring(customDimensions["gen_ai.evaluation.name"])
| summarize started = min(timestamp),
    guardrailPassed = countif(name == "dfe.ai_agents.guardrail.checks" and outcome == "passed"),
    guardrailChecks = countif(name == "dfe.ai_agents.guardrail.checks"),
    casesPassed = tolong(sumif(valueSum, name == "dfe.ai_agents.release_gate.test_cases" and outcome == "passed")),
    attacksBlocked = tolong(sumif(valueSum, name == "dfe.ai_agents.release_gate.test_cases" and outcome == "blocked_by_guardrail")),
    failedCases = make_set_if(testCase, name == "dfe.ai_agents.release_gate.test_cases" and outcome == "failed"),
    groundedness = round(avgif(valueSum / valueCount, name == "dfe.ai_agents.release_gate.score" and metric == "Groundedness"), 2),
    relevance = round(avgif(valueSum / valueCount, name == "dfe.ai_agents.release_gate.score" and metric == "Relevance"), 2),
    gate = take_anyif(outcome, name == "dfe.ai_agents.release_gate.agents")
    by run, agent
| extend guardrail = case(guardrailPassed > 0, "passed", guardrailChecks > 0, "FAILED", "not checked"),
    releaseGate = case(gate == "passed", "PASSED", gate == "failed", "FAILED", "not finished")
| project started, run, agent, guardrail, casesPassed, attacksBlocked, failedCases, groundedness, relevance, releaseGate
| order by started desc, agent asc
```

**Why a release failed** (set the run id from the query above, or the log line "Release run {id} started"):

```kusto
let runId = "<run id>";
traces
| where timestamp > ago(30d) and cloud_RoleName == "briefing-tool"
| where tostring(customDimensions.ReleaseRunId) == runId and isnotempty(customDimensions.ReleaseSection)
| project agent = tostring(customDimensions.AgentName), section = tostring(customDimensions.ReleaseSection),
    reason = tostring(customDimensions.ReleaseProblem)
| order by agent asc, section asc
```

**Tokens and cost per agent, per day:**

```kusto
customMetrics
| where timestamp > ago(30d) and tostring(customDimensions["dfe.ai_agents.application"]) == "briefing-tool"
| where name in ("gen_ai.client.inference.usage.input_tokens", "gen_ai.client.inference.usage.output_tokens", "dfe.ai_agents.cost")
| summarize inputTokens = tolong(sumif(valueSum, name endswith "input_tokens")),
    outputTokens = tolong(sumif(valueSum, name endswith "output_tokens")),
    cost = round(sumif(valueSum, name == "dfe.ai_agents.cost"), 4)
    by day = bin(timestamp, 1d), agent = tostring(customDimensions["gen_ai.agent.name"])
| order by day desc, cost desc
```

**Agent's execution time per sec:**

``` kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name == "gen_ai.invoke_agent.duration"
| extend agent = tostring(customDimensions["gen_ai.agent.name"])
| summarize runs = sum(valueCount), totalSec = sum(valueSum), maxSec = max(valueMax) by agent
| extend avgSec = round(totalSec / iff(runs == 0, 1, runs), 2)
| project agent, runs, avgSec, maxSec
| order by avgSec desc
```

**Concurrency pressure – Hourly average execution-slot wait time:** Sustained increases may require adjusting `MaxRunsPerInstance` or `GlobalConcurrency:MaxRunsAcrossInstances`

| Maximum Waiting Time | Severity | Recommended Action |
| --- | --- | --- |
| 0–2 seconds | Low | No action |
| >2–3 seconds | Moderate | Monitor |
| >3–5 seconds | High | Investigate capacity limits |
| >5 seconds | Critical | Consider increasing capacity |

```kusto
customMetrics
| where cloud_RoleName == "briefing-tool"
| where name == "dfe.ai_agents.run_slot.wait.duration"
| summarize
    calls = sum(valueCount),
    totalWaitSec = sum(valueSum),
    maxWaitSec = max(valueMax)
    by timestamp = bin(timestamp, 1h)
| extend avgWaitSec = round(totalWaitSec / iff(calls == 0, 1, calls), 3)
| extend severity = case(
    maxWaitSec <= 2, "Low",
    maxWaitSec <= 3, "Moderate",
    maxWaitSec <= 5, "High",
    "Critical"
)
| extend recommendedAction = case(
    severity == "Low", "No action",
    severity == "Moderate", "Monitor",
    severity == "High", "Investigate capacity limits",
    "Consider increasing capacity"
)
| project
    timestamp,
    calls,
    avgWaitSec,
    maxWaitSec = round(maxWaitSec, 3),
    totalWaitSec = round(totalWaitSec, 3),
    severity,
    recommendedAction
| order by timestamp desc
```

**Errors**, newest first. A briefing that says "Sorry, something went wrong" shows its cause here:

```kusto
union traces, exceptions
| where timestamp > ago(30d) and cloud_RoleName == "briefing-tool" and severityLevel >= 3
| project timestamp, error = coalesce(message, outerMessage), exception = innermostMessage, instance = cloud_RoleInstance
| order by timestamp desc
```

More queries, for speed, concurrency and safety, are in the AI agent library's
[telemetry guide](https://github.com/DFE-Digital/rsd-ai-libs/blob/main/docs/telemetry.md#queries).
