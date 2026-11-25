#Requires -Version 5.1
<#
.SYNOPSIS
  Uninstalls the per-user Mailflow installation created by Mailflow-Setup.exe.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Split-Path $PSScriptRoot -Parent),
    [switch]$Silent,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$fullDir = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\', '/')

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    $confirm = [System.Windows.Forms.MessageBox]::Show(
        "Voulez-vous désinstaller Mailflow de :`r`n$fullDir ?",
        'Désinstallation de Mailflow',
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }

    if (-not $RemoveData -and (Test-Path (Join-Path $fullDir 'data'))) {
        $delData = [System.Windows.Forms.MessageBox]::Show(
            "Supprimer aussi le dossier 'data' (base de données, journaux et documents archivés) ?`r`n`r`nChoisissez Non pour conserver vos données.",
            'Données Mailflow',
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button2
        )
        if ($delData -eq [System.Windows.Forms.DialogResult]::Yes) { $RemoveData = $true }
    }
}

# Stop running Mailflow.exe from this install directory
$targetExe = Join-Path $fullDir 'Mailflow.exe'
Get-Process -Name 'Mailflow' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -and ([IO.Path]::GetFullPath($_.Path) -ieq $targetExe)) {
            $null = $_.CloseMainWindow()
            if (-not $_.WaitForExit(3000)) { $_.Kill() }
        }
    } catch { }
}

# Remove shortcuts
$desktopLnk = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Mailflow.lnk'
$startMenuLnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Mailflow.lnk'
Remove-Item $desktopLnk, $startMenuLnk -Force -ErrorAction SilentlyContinue

# Remove startup & uninstall registry entries
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name 'Mailflow' -ErrorAction SilentlyContinue
}
$uninstKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Mailflow'
if (Test-Path $uninstKey) {
    Remove-Item -Path $uninstKey -Force -ErrorAction SilentlyContinue
}

# Remove program files while preserving data\ unless requested
if (Test-Path $fullDir) {
    Get-ChildItem -LiteralPath $fullDir -Force | Where-Object {
        $RemoveData -or ($_.Name -ine 'data')
    } | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($RemoveData -and (Test-Path $fullDir)) {
    Start-Process -FilePath 'cmd.exe' -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$fullDir`"" -WindowStyle Hidden
}

if (-not $Silent) {
    [System.Windows.Forms.MessageBox]::Show(
        "Mailflow a été désinstallé." + ($(if (-not $RemoveData) { "`r`nVos données ont été conservées dans :`r`n$fullDir\data" } else { '' })),
        'Désinstallation de Mailflow',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
