# Mailflow – Project plan (.NET)

**Email sorting, PDF archiving and automatic printing**
Version 1.0 – November 2026

---

## 1. Goal

A free, lightweight Windows application, installed on the always-on PC of a store, that:

1. **Reads** one or more mailboxes (OVH, iCloud, any IMAP).
2. **Sorts** incoming emails and their PDFs (factures, bons de livraison, avoirs, commandes…) using rules you configure.
3. **Saves** a renamed copy of each PDF into organised folders on the PC.
4. **Prints** the chosen documents on the store's local printer.
5. **Marks emails as read**, moves them, or leaves them untouched, depending on the rule.
6. Is **managed entirely from a web dashboard**: mailboxes, stores, printers, folders and rules can change without touching code.

### Non-goals for v1
- Recap / digest of what to read (v2)
- Cloud backup (v2 – will simply be a folder setting pointing to Google Drive / OneDrive)
- Printing in the other 4 stores (v3 – PrintNode or one install per store)
- OCR of scanned PDFs (v3)

### Constraints
| Constraint | How it is met |
|---|---|
| Free | .NET, all libraries and SumatraPDF are free / open source |
| Light | One Windows service, ~60–120 MB RAM, ~0 % CPU between checks |
| Configurable | Every setting lives in the database and is edited from the dashboard |
| Safe | Never deletes emails, prints only from approved senders, passwords encrypted |

---

## 2. Tech stack

| Area | Choice | Package |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | – |
| Host | Generic Host + Windows Service | `Microsoft.Extensions.Hosting.WindowsServices` |
| Dashboard | **ASP.NET Core Razor Pages** + **HTMX** + Bootstrap 5 | (htmx/bootstrap as static files, no CDN) |
| Database | **SQLite** + **EF Core** (code-first, migrations) | `Microsoft.EntityFrameworkCore.Sqlite` |
| IMAP / email | **MailKit / MimeKit** | `MailKit` |
| PDF text | **PdfPig** | `UglyToad.PdfPig` |
| Printer list | `PrinterSettings.InstalledPrinters` | `System.Drawing.Common` (Windows only) |
| PDF printing | **SumatraPDF** (silent CLI) | external exe, shipped next to the app |
| Secret storage | **Windows DPAPI** (encrypted in DB, readable only by the service account on this PC) | `System.Security.Cryptography.ProtectedData` |
| Auth (dashboard) | Cookie authentication, hashed password | `Microsoft.AspNetCore.Identity` `PasswordHasher` only |
| Logging | **Serilog** → rolling files + DB table for the Journal page | `Serilog.AspNetCore`, `Serilog.Sinks.File` |
| Scheduling | `BackgroundService` + `PeriodicTimer` (no Quartz needed) | – |
| Tests | **xUnit** + **NSubstitute**; optional **GreenMail** (Docker) for IMAP integration tests | `xunit`, `NSubstitute` |
| IDE | Visual Studio 2026 Community or Rider | – |

---

## 3. Architecture (Clean Architecture)

```
┌───────────────────────────── Mailflow.Web (host) ───────────────────────────┐
│  Razor Pages dashboard · Auth · BackgroundService (worker loop) · Windows Service │
└───────────────┬───────────────────────────────────────────────────┬──────────────┘
                │ uses                                              │ wires DI
┌───────────────▼───────────────┐                 ┌─────────────────▼───────────────┐
│     Mailflow.Application │  interfaces ◄── │   Mailflow.Infrastructure  │
│  Use cases: ProcessMailbox,   │                 │  EF Core (SQLite), MailKit IMAP,│
│  TestRule, TestConnection,    │                 │  PdfPig, SumatraPDF printer,    │
│  Reprint, ImportYaml…         │                 │  DPAPI secrets, file storage    │
└───────────────┬───────────────┘                 └─────────────────────────────────┘
                │
┌───────────────▼───────────────┐
│       Mailflow.Domain    │  Entities, RuleEngine (pure logic), value objects
└───────────────────────────────┘
```

**Rules:** Domain depends on nothing. Application depends on Domain and defines interfaces (`IMailClient`, `IPdfTextReader`, `IPrinterService`, `IDocumentStore`, `ISecretStore`, `IClock`). Infrastructure implements them. Web is the only executable.

### Solution layout
```
Mailflow/
├── Mailflow.sln
├── src/
│   ├── Mailflow.Domain/
│   │   ├── Entities/         Store, Mailbox, Printer, Category, Rule, RuleCondition,
│   │   │                     RuleAction, AllowedSender, ProcessedMessage, Document, RunLog, Setting
│   │   ├── Rules/            RuleEngine, ConditionEvaluator, MatchContext
│   │   └── Enums/            ConditionField, ConditionOperator, ActionType, AfterProcessing
│   ├── Mailflow.Application/
│   │   ├── Abstractions/     IMailClient, IPdfTextReader, IPrinterService, IDocumentStore,
│   │   │                     ISecretStore, IClock, IAppDbContext
│   │   ├── Processing/       MailboxProcessor, MessagePipeline, PrintPolicy
│   │   ├── Queries/          DashboardStats, DocumentSearch
│   │   └── Commands/         TestConnection, TestRule, TestPrint, Reprint, Reclassify, ImportYaml
│   ├── Mailflow.Infrastructure/
│   │   ├── Persistence/      MailflowDbContext, Configurations/, Migrations/, Seed
│   │   ├── Mail/             MailKitClient (IMAP), ImapPresets
│   │   ├── Pdf/              PdfPigTextReader
│   │   ├── Printing/         WindowsPrinterCatalog, SumatraPrinterService
│   │   ├── Storage/          FileSystemDocumentStore (naming, folders, dedup)
│   │   └── Security/         DpapiSecretStore
│   └── Mailflow.Web/
│       ├── Program.cs        host, DI, Windows service, Serilog, auth
│       ├── Worker/           MailflowWorker (BackgroundService), RunNowSignal
│       ├── Pages/            Index, Login, Mailboxes/, Stores/, Printers/, Categories/,
│       │                     Rules/, History/, Journal/, Settings/
│       ├── wwwroot/          bootstrap, htmx, css
│       └── appsettings.json  only: URL/port, DB path, log path
├── tests/
│   ├── Mailflow.Domain.Tests/        rule engine (most tests here)
│   ├── Mailflow.Application.Tests/   pipeline with fakes
│   └── Mailflow.Integration.Tests/   SQLite + GreenMail (optional)
├── tools/
│   └── SumatraPDF/        SumatraPDF.exe (portable) shipped with the release
└── deploy/
    ├── install.ps1        create service account, copy files, install & start service
    └── uninstall.ps1
```

---

## 4. Data model

| Entity | Key fields |
|---|---|
| **Store** | Id, Name, Address, IsActive |
| **Mailbox** | Id, StoreId, DisplayName, Email, Host, Port, UseSsl, Username, EncryptedPassword, InboxFolder, CheckIntervalMinutes, LookbackDays, DefaultAfterProcessing (`MarkRead` / `Move` / `Leave`), IsActive, LastCheckAt, LastError |
| **Printer** | Id, StoreId, DisplayName, WindowsName, Kind (`Local` / `PrintNode` later), DefaultCopies, Duplex, IsActive |
| **Category** | Id, Name (Factures, Bons de livraison…), LocalSubfolder, ImapFolder (nullable), Color, SortOrder |
| **Rule** | Id, Name, Priority, IsActive, MatchMode (`All` / `Any`), StopProcessing, MailboxId (nullable = all mailboxes) |
| **RuleCondition** | Id, RuleId, Field, Operator, Value, CaseSensitive |
| **RuleAction** | Id, RuleId, Type, CategoryId?, PrinterId?, Copies?, ImapFolder? |
| **AllowedSender** | Id, Pattern (address or domain), StoreId (nullable = all) |
| **ProcessedMessage** | Id, MailboxId, MessageId, ImapUid, ReceivedAt, From, Subject, MatchedRuleId, Status, ProcessedAt |
| **Document** | Id, ProcessedMessageId, Sha256 (unique), OriginalName, StoredPath, CategoryId, PrintedAt?, PrinterId?, PrintError? |
| **RunLog** | Id, StartedAt, Duration, MailboxId, MessagesHandled, DocumentsSaved, Printed, Error |
| **Setting** | Key, Value (DocumentsRoot, PrintMaxAgeHours, FileNamePattern, AdminPasswordHash…) |

**Enums**
- `ConditionField`: FromAddress, FromDomain, ToAddress, Subject, Body, AttachmentName, PdfText, HasPdf
- `ConditionOperator`: Contains, NotContains, Equals, StartsWith, EndsWith, Regex
- `ActionType`: SaveToCategory, Print, MarkAsRead, MoveToFolder, LeaveUnread, Skip

**File naming pattern** (configurable setting):
`{Category}/{yyyy}/{MM}/{yyyy-MM-dd}_{Supplier}_{OriginalName}.pdf`

---

## 5. Processing pipeline

Runs every *N* minutes per mailbox, plus on demand ("Run now" button).

```
for each active mailbox:
  1. Connect (MailKit, SSL), open inbox read-write
  2. Search: SINCE (today − LookbackDays)
  3. Skip messages already in ProcessedMessage (by Message-ID, fallback UID+date hash)
  4. For each new message:
     a. Parse with MimeKit (no flags changed – BODY.PEEK)
     b. Extract PDF attachments → PdfPig text of the first 2 pages
     c. Build MatchContext (from, domain, to, subject, body, attachment names, pdf text)
     d. RuleEngine.Evaluate → ordered list of matched rules → merged actions
        (no match → default rule: category "A trier", no print, leave unread)
     e. SaveToCategory: store each PDF (dedup by SHA-256), insert Document
     f. Print: only if PrintPolicy allows →
          rule says print  AND sender in AllowedSenders
          AND message age ≤ PrintMaxAgeHours  AND printer active
     g. Mailbox action ONLY after (e) succeeded:
          MarkAsRead → add \Seen flag
          MoveToFolder → create folder if missing, MOVE (fallback COPY + \Deleted + UID EXPUNGE)
          LeaveUnread → nothing
     h. Insert ProcessedMessage (single DB transaction per message)
  5. Write RunLog, update Mailbox.LastCheckAt / LastError
```

**Failure behaviour**
- Save fails → message not recorded, not marked read → retried next run.
- Print fails → document saved, `PrintError` stored, visible in History with a **Reprint** button.
- Mailbox unreachable → error on the dashboard, other mailboxes keep running.
- Never: delete an email, open or execute an attachment.

### Rule engine (Domain, pure and fully unit-tested)
- Rules evaluated by `Priority` ascending.
- `MatchMode.All` = every condition true; `Any` = at least one.
- Text comparison: case-insensitive and **accent-insensitive** by default ("Facture" = "facture" = "FACTURÉ").
- `PdfText` + `Contains`: for type detection, an option "keyword closest to the top wins" (a facture mentioning "bon de livraison n°…" lower down stays a facture).
- `StopProcessing` stops evaluation after that rule.
- Regex has a 100 ms timeout (`RegexOptions` + `matchTimeout`) to avoid hangs.

---

## 6. Dashboard

URL: `http://127.0.0.1:5088` (bound to 127.0.0.1 by default). Login required.

| Page | Features |
|---|---|
| **Accueil** | Worker status, last/next run, today's counters, last errors, **Run now**, **Pause/Resume**, 10 latest documents |
| **Magasins** | CRUD stores; each shows its mailboxes and printers |
| **Boîtes mail** | CRUD; provider presets (OVH MX Plan, OVH Email Pro, iCloud, Gmail, Outlook/Hotmail, Yahoo, Custom IMAP; app password required for iCloud/Gmail/Yahoo, Microsoft personal accounts need OAuth2 = v2); password field write-only; **Test connection** (shows message count + server folders); after-processing default; interval; active toggle |
| **Imprimantes** | **Detect printers** (lists Windows printers), add/assign to store, copies, duplex, **Test print**, active toggle |
| **Catégories / Dossiers** | CRUD; local subfolder with path check; IMAP folder picked from server list or created; colour |
| **Règles** | Ordered list (drag & drop priority), active toggles; editor with condition rows + action rows; **Test this rule** against the last 50 emails (read-only, shows matches); default rule; **Expéditeurs autorisés** (print whitelist) |
| **Historique** | Search by date / supplier / category / store; **Open PDF**, **Reprint**, **Change category** (moves file) |
| **Journal** | Run logs and errors |
| **Paramètres** | Documents root folder, file name pattern, print max age, admin password, DB backup now, **Import config.yaml** (from the Python v1) |

UI rules: French labels, Bootstrap, HTMX for inline actions (test buttons, toggles, run now) – no SPA, no build step.

---

## 7. Security

| Risk | Mitigation |
|---|---|
| Dashboard accessed by staff / network | Bound to `127.0.0.1`; login with hashed password; session timeout 30 min; anti-forgery tokens on all forms (built into Razor Pages) |
| Mailbox passwords | Encrypted with **DPAPI** (scope = service account), never displayed, write-only field |
| Spam printing on the store printer | Print only from **AllowedSenders**; max age 24 h |
| Malicious PDFs | Never opened/executed; only text read by PdfPig; SumatraPDF only prints |
| Staff browsing invoices | Service runs under dedicated local account `svc-mailflow`; documents folder ACL limited to that account + admin |
| Remote access (later) | **Tailscale** to reach the dashboard from your own PC – no port opened on the store router |
| Data loss | Nightly SQLite backup (`VACUUM INTO`) into the documents folder (→ cloud in v2) |

---

## 8. Deployment

**Build** (on the developer PC):
```
dotnet publish src/Mailflow.Web -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```
Result: `Mailflow.exe` + `tools/SumatraPDF.exe` + `deploy/install.ps1` → zipped release.

**Install** (on the store PC, PowerShell as admin):
1. Unzip to `C:\Mailflow`
2. `install.ps1`:
   - creates local user `svc-mailflow` (random password) and grants "Log on as a service"
   - creates `C:\MailflowDocuments` and `C:\Mailflow\data` with restricted ACLs
   - `sc.exe create Mailflow binPath= "C:\Mailflow\Mailflow.exe" start= delayed-auto obj= .\svc-mailflow`
   - sets recovery: restart on failure (1 min)
   - starts the service, opens `http://127.0.0.1:5088` → first-run wizard sets the admin password
3. Make sure the store printer is installed **for all users** (otherwise the service account cannot see it).

**Update:** stop service → replace exe → start service (EF migrations applied automatically at startup).

---

## 9. Testing strategy

| Level | What | Tool |
|---|---|---|
| Unit | Rule engine: every field × operator, accents, priority, stop processing, "closest keyword" | xUnit |
| Unit | PrintPolicy, file naming, dedup | xUnit |
| Application | MessagePipeline with fake mail client / printer / store: mark read only after save, retries, failures | xUnit + NSubstitute |
| Integration | Real IMAP flow against **GreenMail** (Docker) | xUnit |
| Manual (on the store PC) | Real mailbox in "test rule" mode, test print, 1 week pilot with `LeaveUnread` before switching to `MarkAsRead` | checklist |

Sample PDFs from real suppliers (anonymised if needed) go into `tests/fixtures/` to lock the classification behaviour.

---

## 10. Phases and deliverables

| # | Phase | Main tasks | Done when… |
|---|---|---|---|
| **0** | Setup | Solution + 4 projects + tests, EF Core + first migration, Serilog, CI build script | `dotnet test` green, app runs in console |
| **1** | Host & shell | Windows service hosting, login + first-run wizard, layout, Accueil (static), Settings page | Installed as a service on a test PC, dashboard opens |
| **2** | Stores & mailboxes | CRUD, presets, DPAPI secrets, **Test connection** (MailKit) | A real mailbox connects from the dashboard |
| **3** | Printers | Detect printers, CRUD, **Test print** via SumatraPDF from the service | Test page prints from the service account |
| **4** | Categories & rules | Categories CRUD + IMAP folder list, rule editor, priority, allowed senders, **Test this rule** | Rules for 3 main suppliers match correctly on real emails |
| **5** | Worker | Pipeline, dedup, print policy, mark read / move, RunLog, Run now / Pause | Runs unattended for 48 h with no missed or double-printed document |
| **6** | History & polish | Search, open, reprint, reclassify, Journal, nightly backup, import config.yaml | Pilot week completed |
| **v2** | Recap + cloud | Daily recap (email / push), documents root on Google Drive/OneDrive, "à vérifier" flags on factures | — |
| **v3** | Multi-store | PrintNode printers, per-store routing, OCR for scanned PDFs, remote access via Tailscale | All stores live |

Each phase ends with a working, installable build.

---

## 11. Resource targets

| Metric | Target |
|---|---|
| RAM (idle) | ≤ 120 MB |
| CPU between runs | ~0 % |
| One run, no new email | < 3 s |
| Disk (app) | ~80 MB |
| Disk (documents) | ~1–2 GB / year / store |
| Service priority | Below normal (till software always first) |

---

## 12. Open questions

1. Pilot mailbox address and OVH offer (MX Plan `ssl0.ovh.net` or Email Pro `pro1.mail.ovh.net`)?
2. Main suppliers (name + sender address) to pre-configure the first rules?
3. Printer model at the pilot store, and is it USB or network?
4. Default for unmatched emails: leave unread (recommended) or mark as read?
5. Dashboard reachable only on the store PC, or also from your PC via Tailscale?
6. Keep the Python v1 running during the pilot (read-only) for comparison?
