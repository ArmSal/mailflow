#Requires -Version 5.1
<#
.SYNOPSIS
  Removes the Mailflow service and service account. Documents and data are kept unless -RemoveData is given.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Mailflow',
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'Mailflow'
$Account = 'svc-mailflow'

function Step($msg) { Write-Host "[uninstall] $msg" }

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator (right-click PowerShell > Run as administrator).'
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed with exit code $LASTEXITCODE" }
    Step "Service $ServiceName removed."
} else {
    Step "Service $ServiceName not found, nothing to do."
}

if (Get-LocalUser -Name $Account -ErrorAction SilentlyContinue) {
    Remove-LocalUser -Name $Account
    Step "Local user $Account removed."
} else {
    Step "Local user $Account not found, nothing to do."
}

if ($RemoveData) {
    $dataDir = Join-Path $InstallDir 'data'
    if (Test-Path $dataDir) { Remove-Item -Recurse -Force $dataDir; Step "Removed $dataDir" }
} else {
    Step "Data kept in $InstallDir\data and the documents folder (use -RemoveData to delete the data folder)."
}
