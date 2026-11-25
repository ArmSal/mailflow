# SumatraPDF (portable)

Printing uses the portable SumatraPDF executable. It is not committed. SumatraPDF is a separate program under the GNU GPL v3 (source: https://github.com/sumatrapdfreader/sumatrapdf); Mailflow starts it as an external process and does not modify it. If you distribute an installer that contains it, keep its licence and credit with it.

1. Download the 64-bit portable build from https://www.sumatrapdfreader.org/download-free-pdf-viewer
2. Place `SumatraPDF.exe` in this folder (`tools/SumatraPDF/SumatraPDF.exe`), copied next to the app on publish.
3. Or set `SumatraOptions.ExePath` to another location.
