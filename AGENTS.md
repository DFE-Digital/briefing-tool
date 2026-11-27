# Repository Instructions — Briefing-tool

## 1. Project Context

- Project: Briefing-tool
- Purpose: Generates school and trust briefings for ministers from DfE records using AI agents in Azure AI Foundry, with an optional web search for missing information.
- Primary technology: .NET / C# / ASP.NET Core Razor Pages with GOV.UK Frontend
- Target framework: .NET 10
- C# version: C# 14.0
- Evidence source: Azure AI Search (no database in use)
- AI: DfE agent libraries (`GovUK.Dfe.AI.Agents`, `.Guardrails`, `.AISearch`, `.Evaluation`) from the DfE GitHub NuGet feed
- Architecture: Clean Architecture
- Test framework: xUnit v3 with NSubstitute

These instructions define engineering standards for this repository. Personal security, approval and Git/GitHub restrictions are defined by the developer's global instructions and must also be followed.

## 2. Architecture — Clean Architecture

Respect the architecture already established in the repository. Do not restructure the application merely to make it conform to a theoretical architecture.

| Project | Layer | Responsibility |
| --- | --- | --- |
| `Dfe.Briefing.Tool.Domain` | Domain | Constants and value objects |
| `Dfe.Briefing.Tool.Application` | Application | Briefing logic: orchestrator, builders, evidence providers, agents, options, agent sweep |
| `Dfe.Briefing.Tool.Infrastructure` | Infrastructure | Agent registration, shared settings, prompts, telemetry |
| `Dfe.Briefing.Tool.Web` | Presentation | Razor Pages web app |
| `Dfe.Briefing.Tool.ReleaseGate` | Presentation | Console tool for the Responsible AI quality gate |

### Domain Layer

Rules:

- Must not depend on Application, Infrastructure or Presentation.
- Must not contain HTTP or framework-specific implementation details.

### Application Layer

Rules:
- Depends on Domain.
- Does not depend on Infrastructure implementations.
- Follow the existing folders (`Builders`, `Providers`, `Orchestrators`, `Services`, each with `Interfaces`).
- Register services in `DependencyInjection.cs`.

### Infrastructure Layer

Rules:
- Implements abstractions defined by inner layers.
- Must not own business rules.
- Agent prompts live in `Prompts/`; shared settings in `appsettings.shared.json`.

### Presentation Layer

Rules:
- Delegates briefing workflows to Application.
- Keep Razor `PageModel`s thin.

### Dependency Direction

- Dependencies point inward.
- Web and ReleaseGate are the composition roots.

## 3. C# and .NET Coding Standards

- Follow the repository's target framework and language version.
- Follow established naming, formatting and project conventions.
- Use nullable reference types appropriately.
- Keep classes and methods focused on a single responsibility.
- Avoid unnecessary abstractions, interfaces and design patterns.
- Use dependency injection and respect lifetimes; avoid captive dependencies.
- Use the options pattern with validation (`ValidateOnStart`) for settings.
- Use async/await; avoid `.Result`, `.Wait()` and unnecessary `Task.Run`.
- Pass CancellationToken through asynchronous workflows.
- Use `TimeProvider` where time matters.
- Handle exceptions intentionally; do not swallow exceptions.
- Maximum seven parameters per method or constructor, in accordance with SonarQube S107.

## 4. ASP.NET Core Standards

- Keep Razor Pages thin; put workflows in the Application layer.
- Use GOV.UK Frontend components and patterns.
- Validate external input.
- Sanitise AI-generated content before rendering (`Sanitization/`).
- Keep the app stateless between requests.
- Apply authentication and authorization consistently (Microsoft Entra ID).
- Use structured logging.
- Never log credentials, tokens, personal secrets or sensitive data.

## 5. AI Agents and Guardrails

- Isolate external integrations behind appropriate abstractions.
- Handle timeouts, transient failures and cancellation.
- Do not weaken guardrails or quality gate thresholds to make something pass.
- Release gate test cases use made-up records only; never reuse or renumber an `id`.
- Running the release gate costs money; run it only with approval.
- Do not hardcode credentials or secrets; use user secrets or environment variables.
- Do not introduce new dependencies without approval.

## 6. Testing Standards

- Follow the existing test project structure (`src/tests/`), mirroring source folders.
- Use xUnit v3, NSubstitute and the Microsoft.Testing.Platform runner.
- Test business behaviour, validation, failure paths and edge cases.
- Keep tests deterministic and independent.
- Do not weaken or remove tests to make a build pass.
- Obtain approval before creating or modifying test files.

## 7. SonarQube and Quality Standards

- Aim for zero new SonarQube issues.
- Respect S107: no more than seven parameters per method or constructor.
- Do not use `NOSONAR` or suppressions to bypass quality rules.
- Never access secrets to obtain analysis credentials.

## 8. Build and Verification

- `dotnet restore src/Dfe.Briefing.Tool.slnx`
- `dotnet build src/Dfe.Briefing.Tool.slnx`
- `dotnet test --solution src/Dfe.Briefing.Tool.slnx`

Obtain approval before executing commands with potential side effects.

Never claim a successful build or test without verified results.

## 9. Repository Change Boundaries

- Understand the existing implementation before proposing changes.
- Make the smallest appropriate change.
- Do not refactor unrelated code.
- Do not change public contracts, prompts or quality gate settings without approval.
- Preserve existing user changes.
- Obtain explicit approval before every file modification.
- Global Git/GitHub restrictions remain mandatory.

## 10. Completion Report

At the end of each task, provide:

1. Summary of the work.
2. Files inspected and files changed.
3. Technical decisions.
4. Build and test results.
5. SonarQube results, if applicable.
6. Read-only Git status.
7. Outstanding issues and manual actions required.

Never claim changes were made when they were only proposed.
