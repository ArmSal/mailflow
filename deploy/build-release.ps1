#Requires -Version 5.1
<#
.SYNOPSIS
  Builds the Windows installer: <Out>\Mailflow-Setup-<Version>-win-x64.exe (also keeps the plain zip next to it).
  Needs only the .NET SDK and the .NET Framework C# compiler (csc.exe, part of Windows).
.PARAMETER Version
  Single source of the version: used by the app build, the wizard, Add/Remove Programs and the file names.
.PARAMETER Out
  Output folder (relative to the repository root unless absolute). Default: dist
.PARAMETER NoRestore
  Skip NuGet restore (use only when restore already ran).
.NOTES
  tools\SumatraPDF\SumatraPDF.exe is git-ignored (licensing): it is bundled when present, otherwise a warning is printed
  and the installer ships without it.
#>
[CmdletBinding()]
param([string]$Version = '1.0.0', [string]$Out = 'dist', [switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
$root = Split-Path $PSScriptRoot -Parent
$outDir = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
$stage = Join-Path $outDir 'stage'
$build = Join-Path $outDir 'obj'
$zip = Join-Path $outDir "Mailflow-$Version-win-x64.zip"
$setupExe = Join-Path $outDir "Mailflow-Setup-$Version-win-x64.exe"
$icon = Join-Path $root 'src\Mailflow.Desktop\Assets\mailflow.ico'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

Remove-Item $stage, $build -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage, $build -Force | Out-Null

# 1. Application (self-contained, single file).
$publishArgs = @(
    'publish', (Join-Path $root 'src\Mailflow.Desktop'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', "-p:Version=$Version", '-m:1', '/nr:false',
    '-o', $stage
)
if ($NoRestore) { $publishArgs += '--no-restore' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# IIS leftovers, debug symbols and development settings do not belong in a release.
Remove-Item (Join-Path $stage '*.pdb'), (Join-Path $stage 'web.config'), (Join-Path $stage 'aspnetcorev2_inprocess.dll'),
    (Join-Path $stage 'appsettings.Development.json'), (Join-Path $stage '*.xml'), (Join-Path $stage 'Mailflow.Web.runtimeconfig.json') -ErrorAction SilentlyContinue

# 2. Extra payload: licence, icon, SumatraPDF.
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage
Copy-Item $icon -Destination $stage
$sumatraDir = Join-Path $stage 'tools\SumatraPDF'
New-Item -ItemType Directory -Path $sumatraDir -Force | Out-Null
Copy-Item (Join-Path $root 'tools\SumatraPDF\README.md') -Destination $sumatraDir
$sumatraExe = Join-Path $root 'tools\SumatraPDF\SumatraPDF.exe'
if (Test-Path $sumatraExe) {
    Copy-Item $sumatraExe -Destination $sumatraDir
} else {
    Write-Warning "tools\SumatraPDF\SumatraPDF.exe not found: this installer will NOT contain it and printing will not work until it is placed in <install>\tools\SumatraPDF\. See tools\SumatraPDF\README.md."
}

# 3. Generated build info (version single-sourced from -Version).
$buildInfo = Join-Path $build 'BuildInfo.g.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("Mailflow")]
[assembly: AssemblyProduct("Mailflow")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
namespace Mailflow.Setup { internal static class BuildInfo { public const string Version = "$Version"; } }
"@ | Set-Content -Path $buildInfo -Encoding UTF8

$src = Join-Path $root 'deploy\installer\SetupProgram.cs'
$refs = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
          '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')

# 4. Uninstaller (same source, no payload), shipped inside the install folder.
& $csc /nologo /target:winexe /platform:x64 /optimize+ /define:UNINSTALL "/win32icon:$icon" @refs `
    "/out:$(Join-Path $stage 'Uninstall.exe')" $src $buildInfo
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }

# 5. Zip (forward slashes), then the installer with the zip embedded.
Remove-Item $zip, $setupExe -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Zip written to $zip"

& $csc /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$icon" @refs `
    "/resource:$zip,payload.zip" "/out:$setupExe" $src $buildInfo
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

Remove-Item $stage, $build -Recurse -Force
Write-Host "Installer written to $setupExe"
