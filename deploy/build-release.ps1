#Requires -Version 5.1
<#
.SYNOPSIS
  Builds the release zip (dist\Mailflow-<version>-win-x64.zip). Unzip it into C:\Mailflow, add
  tools\SumatraPDF\SumatraPDF.exe (see tools\SumatraPDF\README.md), then run deploy\install.ps1 as administrator.
#>
[CmdletBinding()]
param([string]$Version = '1.0.0', [string]$Out = 'dist', [switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root "$Out\stage"
$zip = Join-Path $root "$Out\Mailflow-$Version-win-x64.zip"

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$publishArgs = @(
    'publish', (Join-Path $root 'src\Mailflow.Desktop'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', '-m:1', '/nr:false',
    '-o', $stage
)
if ($NoRestore) { $publishArgs += '--no-restore' }
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# IIS leftovers, debug symbols and development settings do not belong in a release.
Remove-Item (Join-Path $stage '*.pdb'), (Join-Path $stage 'web.config'), (Join-Path $stage 'aspnetcorev2_inprocess.dll'),
    (Join-Path $stage 'appsettings.Development.json'), (Join-Path $stage '*.xml'), (Join-Path $stage 'Mailflow.Web.runtimeconfig.json') -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path (Join-Path $stage 'deploy') -Force | Out-Null
Copy-Item (Join-Path $root 'deploy\install.ps1'), (Join-Path $root 'deploy\uninstall.ps1'), (Join-Path $root 'deploy\uninstall-app.ps1') -Destination (Join-Path $stage 'deploy')
New-Item -ItemType Directory -Path (Join-Path $stage 'tools\SumatraPDF') -Force | Out-Null
Copy-Item (Join-Path $root 'tools\SumatraPDF\README.md') -Destination (Join-Path $stage 'tools\SumatraPDF')
$sumatraExe = Join-Path $root 'tools\SumatraPDF\SumatraPDF.exe'
if (Test-Path $sumatraExe) {
    Copy-Item $sumatraExe -Destination (Join-Path $stage 'tools\SumatraPDF\SumatraPDF.exe')
} else {
    Write-Warning 'tools\SumatraPDF\SumatraPDF.exe not found; printing will require placing it in tools\SumatraPDF\.'
}
New-Item -ItemType Directory -Path (Join-Path $stage 'Docs') -Force | Out-Null
Copy-Item (Join-Path $root 'LICENSE') -Destination $stage

Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "Release written to $zip"

$setupExe = Join-Path $root "$Out\Mailflow-Setup-$Version-win-x64.exe"
$setupSrc = Join-Path $root 'deploy\installer\SetupProgram.cs'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }
Remove-Item $setupExe -ErrorAction SilentlyContinue
& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    "/win32icon:$(Join-Path $root 'src\Mailflow.Desktop\Assets\mailflow.ico')" `
    "/resource:$zip,payload.zip" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    "/out:$setupExe" `
    $setupSrc
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Host "Installer written to $setupExe"
