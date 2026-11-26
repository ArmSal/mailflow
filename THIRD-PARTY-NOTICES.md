# Third-party notices

Mailflow itself is licensed under the [MIT License](LICENSE). It uses the open-source components below, each under its own licence. Licences are listed as published by each project; the project's own licence text is the authoritative source.

## Bundled with the installer

| Component | Licence | Source |
|---|---|---|
| **SumatraPDF** (portable `SumatraPDF.exe`, used only for silent printing) | GNU GPL v3 (with some BSD-licensed parts) | https://github.com/sumatrapdfreader/sumatrapdf |
| .NET runtime and ASP.NET Core (self-contained build) | MIT | https://github.com/dotnet |
| Microsoft Edge WebView2 loader (`WebView2Loader.dll`) | Microsoft WebView2 SDK licence | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| SQLite (`e_sqlite3.dll`) | Public domain | https://www.sqlite.org/copyright.html |

**About SumatraPDF.** It is a separate program, unmodified, started by Mailflow as an external process to print PDF files. It is not part of Mailflow and is **not** covered by Mailflow's MIT licence: it stays under the GPL v3. Its source code and licence text are available at the link above. The repository ships the official portable `SumatraPDF.exe` (version 3.5.2, 64-bit) in `tools/SumatraPDF/`, next to the GPL v3 licence text (`LICENSE-GPL-3.0.txt`), and it is bundled into the installers. Its complete source code is available from https://github.com/sumatrapdfreader/sumatrapdf (the official releases are listed at https://www.sumatrapdfreader.org/download-free-pdf-viewer). Anyone who wants a different build can replace the file (see `tools/SumatraPDF/README.md`).

## NuGet packages

| Package | Licence |
|---|---|
| MailKit, MimeKit | MIT |
| BouncyCastle.Cryptography | MIT |
| Microsoft.EntityFrameworkCore (+ Sqlite, Relational), Microsoft.Data.Sqlite, Microsoft.Extensions.* | MIT |
| System.Drawing.Common, System.Security.Cryptography.ProtectedData | MIT |
| PdfPig (UglyToad) | Apache-2.0 |
| Serilog and its sinks / extensions | Apache-2.0 |
| SQLitePCLRaw | Apache-2.0 |
| Microsoft.Web.WebView2 | Microsoft WebView2 SDK licence |

## Front-end libraries (served from `wwwroot/lib`)

| Library | Version | Licence |
|---|---|---|
| Bootstrap | 5.3.3 | MIT (see `src/Mailflow.Web/wwwroot/lib/bootstrap/LICENSE`) |
| htmx | 2.0.4 | Zero-Clause BSD |

To list the exact NuGet versions in use, run `dotnet list package --include-transitive`.
