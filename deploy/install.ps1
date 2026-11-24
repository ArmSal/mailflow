#Requires -Version 5.1
<#
.SYNOPSIS
  Installs Mailflow as a Windows service (run in an elevated PowerShell). Safe to run again.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Mailflow',
    [string]$DocumentsDir = 'C:\MailflowDocuments',
    [string]$Url = 'http://localhost:5088'
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'Mailflow'
$Account = 'svc-mailflow'
$DataDir = Join-Path $InstallDir 'data'
$Exe = Join-Path $InstallDir 'Mailflow.exe'

function Step($msg) { Write-Host "[install] $msg" }

# 1. Admin check
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator (right-click PowerShell > Run as administrator).'
}
if (-not (Test-Path $Exe)) {
    throw "Cannot find $Exe. Unzip the release into $InstallDir first."
}

# 2. Service account with a fresh random password (shown once, never written to disk or logs)
Add-Type -AssemblyName System.Web
$password = [System.Web.Security.Membership]::GeneratePassword(24, 4)
$secure = ConvertTo-SecureString $password -AsPlainText -Force
if (Get-LocalUser -Name $Account -ErrorAction SilentlyContinue) {
    Step "Local user $Account exists: resetting its password."
    Set-LocalUser -Name $Account -Password $secure
} else {
    Step "Creating local user $Account."
    New-LocalUser -Name $Account -Password $secure -PasswordNeverExpires -UserMayNotChangePassword `
        -Description 'Mailflow service account' | Out-Null
}

# 3. Grant "Log on as a service" (SeServiceLogonRight)
$sid = (Get-LocalUser -Name $Account).SID.Value
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("mailflow-secpol-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $cfg = Join-Path $tmp 'secpol.inf'
    $db = Join-Path $tmp 'secpol.sdb'
    secedit.exe /export /cfg $cfg /areas USER_RIGHTS | Out-Null
    $lines = Get-Content $cfg
    $idx = [array]::FindIndex([string[]]$lines, [Predicate[string]]{ param($l) $l -like 'SeServiceLogonRight*' })
    if ($idx -ge 0 -and $lines[$idx] -match [regex]::Escape("*$sid")) {
        Step 'Log on as a service already granted.'
    } else {
        Step 'Granting "Log on as a service".'
        if ($idx -ge 0) { $lines[$idx] = $lines[$idx].TrimEnd() + ",*$sid" }
        elseif ($lines -contains '[Privilege Rights]') {
            $h = [array]::IndexOf([string[]]$lines, '[Privilege Rights]')
            $lines = @($lines[0..$h]) + "SeServiceLogonRight = *$sid" + @($lines[($h + 1)..($lines.Count - 1)])
        }
        else { $lines += '[Privilege Rights]'; $lines += "SeServiceLogonRight = *$sid" }
        Set-Content -Path $cfg -Value $lines -Encoding Unicode
        secedit.exe /import /cfg $cfg /db $db /areas USER_RIGHTS | Out-Null
        secedit.exe /configure /db $db /areas USER_RIGHTS | Out-Null
    }
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

# 4. Folders with restricted ACLs (service account + Administrators + SYSTEM)
foreach ($dir in @($DocumentsDir, $DataDir)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null; Step "Created $dir" }
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)   # no inheritance
    foreach ($id in @("$env:COMPUTERNAME\$Account", 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -Path $dir -AclObject $acl
    Step "ACL set on $dir"
}
# Tell the app to store documents in $DocumentsDir (read at startup; can be changed later in the dashboard).
@{ Storage = @{ DocumentsRoot = $DocumentsDir } } | ConvertTo-Json | Set-Content -Path (Join-Path $InstallDir 'appsettings.Production.json') -Encoding UTF8

# Binaries: no inheritance (C:\ lets Authenticated Users write, which would allow replacing the exe).
# Administrators + SYSTEM full control; the service account may only read and execute.
& icacls.exe $InstallDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${Account}:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls failed on $InstallDir (exit $LASTEXITCODE)." }

# 5. Service (delayed automatic start) + recovery
$binPath = "`"$Exe`""
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Step "Service $ServiceName exists: stopping and updating it."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe config $ServiceName binPath= $binPath start= delayed-auto obj= ".\$Account" password= $password | Out-Null
} else {
    Step "Creating service $ServiceName."
    & sc.exe create $ServiceName binPath= $binPath start= delayed-auto obj= ".\$Account" password= $password `
        DisplayName= 'Mailflow' | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw "sc.exe failed with exit code $LASTEXITCODE" }
& sc.exe description $ServiceName 'Fetches supplier emails, files and prints PDF documents.' | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# 6. Start
Start-Service -Name $ServiceName
Step "Service $ServiceName started."

Write-Host ''
Write-Host "Dashboard: $Url   (first visit: choose the admin password)"
Write-Host "Service account: .\$Account"
Write-Host "Generated password (shown ONCE, store it in your password manager): $password"
Write-Host 'Reminder: install the store printer for ALL users, otherwise the service cannot see it.'
