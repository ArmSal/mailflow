# Parallel Development Plan (multi-agent)

How to build Mailflow with **3 to 10 agents working at the same time**.
Based on [Mailflow-dotnet-plan.md](Mailflow-dotnet-plan.md).

## Principles

1. **Contracts first.** Nothing runs in parallel until Wave 0 has frozen the entities, enums and interfaces. After that, agents code against interfaces, never against each other's code.
2. **One owner per folder.** Each agent edits only the folders listed for it. Two agents never touch the same file.
3. **One git worktree + branch per agent** (`agent/<id>-<topic>`), merged by the integrator after `dotnet build` and `dotnet test` pass.
4. **Shared files are changed by the integrator only:** `.sln`, `Directory.Packages.props`, `Program.cs` DI registration, `appsettings.json`.
5. **A change to a contract** (Domain entity or Application interface) is a request to the integrator, not a direct edit.

## Wave 0: Foundation (1 agent, sequential, about 1 session)

| Task | Output |
|---|---|
| Scaffold solution, 4 src + 3 test projects, references | `dotnet build` green |
| Central package management (`Directory.Packages.props`), `.gitignore`, `.editorconfig` | |
| Domain: all entities + enums from section 4 of the main plan | `Domain/Entities`, `Domain/Enums` |
| Application: interfaces `IMailClient`, `IPdfTextReader`, `IPrinterService`, `IDocumentStore`, `ISecretStore`, `IClock`, `IAppDbContext` + DTOs / `MatchContext` | `Application/Abstractions` |
| Port setting in `appsettings.json` (default `127.0.0.1:5088`) | |
| Empty `dotnet test` passes, CI build script | |

**Gate:** build + test green, contracts reviewed and tagged `contracts-v1`.

## Wave 1: Parallel build (up to 10 agents)

Every row is independent once Wave 0 is done.

| Agent | Scope | Owns (folders) | Depends on | Done when |
|---|---|---|---|---|
| **A1 Rule engine** | RuleEngine, ConditionEvaluator, accent-insensitive compare, regex timeout, "closest keyword wins", StopProcessing | `Domain/Rules`, `tests/Domain.Tests` | contracts | Every field x operator tested |
| **A2 Persistence** | `MailflowDbContext`, EF configurations, first migration, seed (default category "A trier", default rule) | `Infrastructure/Persistence` | contracts | Migration applies on empty SQLite |
| **A3 Mail** | MailKit IMAP client (search SINCE, BODY.PEEK, mark read, MOVE with COPY fallback, list folders), provider presets | `Infrastructure/Mail` | `IMailClient` | Passes against GreenMail |
| **A4 PDF + storage** | PdfPig text of first 2 pages, file naming pattern, SHA-256 dedup, folder creation | `Infrastructure/Pdf`, `Infrastructure/Storage` | `IPdfTextReader`, `IDocumentStore` | Fixture PDFs read, dedup tested |
| **A5 Printing + secrets** | Windows printer catalog, SumatraPDF silent printing, DPAPI secret store | `Infrastructure/Printing`, `Infrastructure/Security`, `tools/SumatraPDF` | `IPrinterService`, `ISecretStore` | Test page prints, secret round-trips |
| **A6 Web shell** | `Program.cs`, DI wiring, Serilog, cookie auth, first-run wizard, layout, Windows Service hosting | `Web/Program.cs`, `Web/Pages/Login`, `Web/Pages/Shared`, `Web/wwwroot` | contracts | Login works, app runs as console and service |
| **A7 CRUD pages** | Stores, Mailboxes (presets, Test connection), Printers (Detect, Test print), Categories | `Web/Pages/Stores`, `Mailboxes`, `Printers`, `Categories` | contracts + fakes | Pages work with in-memory fakes |
| **A8 Rules UI** | Rule editor (conditions/actions rows, priority drag and drop), Allowed senders, "Test this rule" | `Web/Pages/Rules` | contracts + A1 types | Rule saved and dry-run shown |
| **A9 Pipeline** | `MessagePipeline`, `MailboxProcessor`, `PrintPolicy`, failure behaviour (mark read only after save) | `Application/Processing`, `tests/Application.Tests` | interfaces (uses NSubstitute fakes) | All failure cases tested |
| **A10 Worker + deploy** | `MailflowWorker` (PeriodicTimer), RunNowSignal, Pause/Resume, `install.ps1`, `uninstall.ps1`, CI workflow | `Web/Worker`, `deploy/`, `.github/` | interfaces | Worker loop runs with fakes, scripts lint clean |

### Running with fewer agents

| Agents | Grouping |
|---|---|
| **3** | G1 = A1 + A9 + A10 (logic) / G2 = A2 + A3 + A4 + A5 (infrastructure) / G3 = A6 + A7 + A8 (web) |
| **5** | A1+A9 / A2+A4 / A3+A5 / A6+A10 / A7+A8 |
| **7** | A1 / A2+A4 / A3 / A5+A10 / A6 / A7 / A8+A9 |
| **10** | One per row above |

**Gate:** integrator merges all branches, wires DI in `Program.cs`, `dotnet build` and `dotnet test` green.

## Wave 2: Features on top (3 agents)

| Agent | Scope | Owns |
|---|---|---|
| **B1 History** | Search, Open PDF, Reprint, Change category (moves file) | `Web/Pages/History`, `Application/Queries`, `Application/Commands/Reprint`, `Reclassify` |
| **B2 Ops pages** | Accueil dashboard, Journal, Run now / Pause, RunLog display | `Web/Pages/Index`, `Web/Pages/Journal` |
| **B3 Settings + data** | Settings page, nightly SQLite backup (`VACUUM INTO`), Import `config.yaml` | `Web/Pages/Settings`, `Application/Commands/ImportYaml`, backup service |

## Wave 3: Hardening (3 agents)

| Agent | Scope |
|---|---|
| **C1 Integration tests** | GreenMail end-to-end: receive, classify, save, print (fake printer), mark read |
| **C2 Security review** | Auth, anti-forgery, path traversal in file naming, secrets never logged, run `/security-review` |
| **C3 Docs + release** | `dotnet publish` single file, release zip, user guide, screenshots |

Then: pilot week at the first store (`LeaveUnread`, then `MarkAsRead`).

## Agent brief template

Give each agent this prompt skeleton:

```
You are agent <ID> on Mailflow. Read Docs/Mailflow-dotnet-plan.md and
Docs/Parallel-Development-Plan.md.
Scope: <row from table>
You may edit ONLY: <owned folders>
Code against the interfaces in Application/Abstractions. Do not change contracts.
Write tests first for bugs; write the minimum code needed; change nothing else.
Work in your own git worktree/branch agent/<id>-<topic>.
Finish when: <done-when>. Run `dotnet build` and `dotnet test` and report the result.
```

## Merge order for the integrator

A2 -> A1 -> A4 -> A3 -> A5 -> A9 -> A10 -> A6 -> A7 -> A8, then Wave 2, then Wave 3.
Resolve any contract change requests before merging.
