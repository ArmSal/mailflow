# SumatraPDF (portable)

Printing uses the portable SumatraPDF executable, committed here as `SumatraPDF.exe` (official 64-bit portable build, version 3.5.2). SumatraPDF is a separate program under the GNU GPL v3 (licence text: [LICENSE-GPL-3.0.txt](LICENSE-GPL-3.0.txt); source: https://github.com/sumatrapdfreader/sumatrapdf); Mailflow starts it as an external process and does not modify it. If you distribute an installer that contains it, keep its licence and credit with it.

To use another version:

1. Download the 64-bit portable build from https://www.sumatrapdfreader.org/download-free-pdf-viewer
2. Replace `SumatraPDF.exe` in this folder (`tools/SumatraPDF/SumatraPDF.exe`); it is copied next to the app on publish.
3. Or set `SumatraOptions.ExePath` to another location.

SumatraPDF writes `SumatraPDF-settings.txt` next to itself when it runs; that file is git-ignored.
