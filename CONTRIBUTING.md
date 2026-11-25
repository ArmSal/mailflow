# Contributing to Mailflow

This guide explains where to change the code for the most common extensions. Read the architecture section of the [README](README.md) first: `Domain` <- `Application` <- `Infrastructure` <- `Web` / `Desktop`. Nothing depends upward, and the core talks to external services only through interfaces.

## Getting started

Requirements: Windows, the .NET 10 SDK, Git.

```powershell
dotnet build
dotnet test
```

Workflow: open an issue to discuss the change, create a branch (`feature/<topic>`), keep the layers clean, add tests, and open a pull request. `dotnet build` and `dotnet test` must pass (CI runs both on Windows).

Conventions:

- The rule engine and the processing pipeline are tested first. For a bug, write a failing test, then fix it.
- Keep changes focused: no unrelated refactoring or formatting in the same pull request.
- Never commit credentials, real invoices or customer data. Anonymise sample PDFs.
- Code and comments are in English. User-facing text is French in the source and translated to English (see below).

## Tests

| Project | Content |
|---|---|
| `tests/Mailflow.Domain.Tests` | Rule engine: fields, operators, accents, priority, stop |
| `tests/Mailflow.Application.Tests` | Pipeline with fake mail client, printer and storage; print policy; address parsing |
| `tests/Mailflow.Integration.Tests` | Persistence, mail, PDF, printing, security, settings, worker and Razor pages |
| `tests/Mailflow.Desktop.Tests` | Desktop shell logic (launch options, port owner, startup) |

Run one project with `dotnet test tests/Mailflow.Domain.Tests`.

## Database changes

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add MigrationName -p src/Mailflow.Infrastructure -s src/Mailflow.Infrastructure
```

Migrations are applied automatically at startup. Never edit a migration that has been released. Any change to a domain entity needs its migration: without it the application refuses to start (the integration tests detect this). For a new required column, give a default that suits existing rows. Enums are stored as text, so a new enum value needs no migration.

## Add a mail provider preset

Add one line to [`ImapPresets.cs`](src/Mailflow.Infrastructure/Mail/ImapPresets.cs):

```csharp
new("My provider", "imap.example.com", 993, true, "Note shown to the user.", ["example.com"]),
```

The arguments are name, host, port, SSL, a note, and optionally the e-mail domains used to auto-detect the provider from the address typed in the mailbox form. The provider list in the form reads this list. Any IMAP server already works through "IMAP personnalisé" and the advanced options. Add a case to the `ImapPresets` tests if you want to pin the host. The note is user-facing text: if you add one in French, add its English entry too.

## Add UI translations

French is the source language and the key. In Razor views (`T` is available through `using static Loc`) and in page code (`Loc.T`), wrap each visible French string:

```cshtml
<button class="btn btn-primary">@T("Enregistrer")</button>
```

With arguments: `T("{0} documents", count)`. Then add the English text to a JSON file in `src/Mailflow.Web/Localization/en/` (flat `"French": "English"` objects; all files in the folder are merged):

```json
{ "Enregistrer": "Save" }
```

A missing entry falls back to French, so nothing breaks. A test checks that every English entry is non-empty and keeps the same `{0}` placeholders as its French key. The language is chosen with the FR | EN switch (cookie `.Mailflow.Culture`, French by default). Help page text follows the same rule.

## Add a rule condition field

1. Add the value to `ConditionField` ([`Enums.cs`](src/Mailflow.Domain/Enums/Enums.cs)).
2. If the data does not exist yet, add it to [`MatchContext`](src/Mailflow.Domain/Rules/MatchContext.cs) and fill it in `MessagePipeline` and in `MailboxRuleTester` (the rule test).
3. Add the `case` in [`ConditionEvaluator`](src/Mailflow.Domain/Rules/ConditionEvaluator.cs).
4. Add the label in [`RuleSupport.cs`](src/Mailflow.Web/Pages/Rules/RuleSupport.cs), wrapped in `T()` with an English entry.
5. Add tests in `RuleEngineTests`.

## Add a rule action

1. Add the value to `ActionType`.
2. Handle it in [`MessagePipeline`](src/Mailflow.Application/Processing/MessagePipeline.cs), which interprets actions, including the "one action per family" rule.
3. Add the label in `RuleSupport.cs` and, if the action has parameters, validation in [`Rules/Edit.cshtml.cs`](src/Mailflow.Web/Pages/Rules/Edit.cshtml.cs). A new field on `RuleAction` needs a migration.
4. Add tests in `PipelineTests`, especially ordering (a mailbox action runs only after saving) and failure behaviour.

## Add a printing, storage or mail backend

Implement the matching interface from [`Abstractions.cs`](src/Mailflow.Application/Abstractions/Abstractions.cs) in `Infrastructure` and register it in the dependency-injection extension.

| Need | Interface | Existing example |
|---|---|---|
| Another printer backend | `IPrinterService` | `SumatraPrinterService` |
| Remote document storage | `IDocumentStore` | `FileSystemDocumentStore` |
| Another mail protocol | `IMailClient` | `MailKitClient` |
| Another text extraction (OCR) | `IPdfTextReader` | `PdfPigTextReader` |
| Another secret store | `ISecretStore` | `DpapiSecretStore` |

`IMailClient` is shaped around IMAP (folders, UIDs). A very different protocol may need the interface to change: open an issue first.

## UI conventions

See [Docs/design-system.md](Docs/design-system.md) for tokens, components, htmx conventions and wording.

## Security: do not break these

- Never delete an e-mail; never open or run an attachment.
- Open a PDF from the dashboard by document id, never with a path coming from the browser.
- Every POST form keeps its anti-forgery token; no `Html.Raw` on user data.
- Printing depends on the rules (including exact-sender conditions), the message age and the DKIM/DMARC check. Any change to `PrintPolicy` or address extraction needs dedicated tests.
- No unencrypted mail connection, except to `localhost` in tests.

Report vulnerabilities privately through GitHub Security Advisories, not in public issues.

## Known limits

Mailflow is built for one site: SQLite, a single sequential worker, no central multi-site dashboard, no plug-in system.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
