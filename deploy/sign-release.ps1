#Requires -Version 7
<#
.SYNOPSIS
  Signs a published release so installed copies of Mailflow and the Print Agent accept it as an update.
  Run by the owner on their own machine; the private key never goes to GitHub.
.EXAMPLE
  ./deploy/sign-release.ps1 -NewKey                 # once: creates the key pair, prints the public key to paste in UpdateKeys.cs
  ./deploy/sign-release.ps1 -Tag v1.3.4             # after CI published the release: downloads the two Setup files, writes
                                                    # manifest.json + manifest.json.sig and uploads them to the release
  ./deploy/sign-release.ps1 -Tag v1.3.4 -NoUpload   # same, but only writes the files to -Dir
.NOTES
  Sign only after you checked that the release assets are the ones your CI built: the manifest vouches for whatever is there.
  Back the key file up: losing it means shipping a new public key in a manual update.
#>
[CmdletBinding()]
param(
    [switch]$NewKey,
    [string]$Tag,
    [string]$KeyPath = (Join-Path $HOME '.mailflow\release-signing.pem'),
    [string]$Dir,
    [switch]$NoUpload
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$keysFile = Join-Path $root 'src\Mailflow.Application\Updates\UpdateKeys.cs'

if ($NewKey) {
    if (Test-Path $KeyPath) { throw "$KeyPath already exists; refusing to overwrite." }
    New-Item -ItemType Directory -Path (Split-Path $KeyPath) -Force | Out-Null
    $ec = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
    $ec.ExportPkcs8PrivateKeyPem() | Set-Content -Path $KeyPath -Encoding ascii
    Write-Host "Private key written to $KeyPath (keep it secret, back it up)."
    Write-Host "Public key (paste in ${keysFile}):"
    Write-Host ([Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo()))
    return
}

if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Give the release tag, for example -Tag v1.3.4." }
$version = $Matches[1]
if (-not (Test-Path $KeyPath)) { throw "No key at $KeyPath. Run with -NewKey first." }
if (-not $Dir) { $Dir = Join-Path ([IO.Path]::GetTempPath()) "mailflow-sign-$version" }
Remove-Item $Dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $Dir | Out-Null

$ec = [Security.Cryptography.ECDsa]::Create()
$ec.ImportFromPem((Get-Content $KeyPath -Raw))
$pub = [Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo())
if ((Get-Content $keysFile -Raw) -notmatch [regex]::Escape("`"$pub`"")) {
    throw "This key does not match the public key in ${keysFile}: clients would reject the signature."
}

gh release download $Tag --pattern '*-Setup-*-win-x64.exe' --dir $Dir --repo ArmSal/mailflow
if ($LASTEXITCODE -ne 0) { throw 'Download failed.' }
$files = foreach ($name in "Mailflow-Setup-$version-win-x64.exe", "MailflowPrintAgent-Setup-$version-win-x64.exe") {
    $p = Join-Path $Dir $name
    if (-not (Test-Path $p)) { throw "$name is missing from the release." }
    [ordered]@{ name = $name; sha256 = (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item $p).Length }
}
$manifest = [ordered]@{ version = $version; published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); files = @($files) }
$mPath = Join-Path $Dir 'manifest.json'
[IO.File]::WriteAllBytes($mPath, [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 4)))
$sig = $ec.SignData([IO.File]::ReadAllBytes($mPath), [Security.Cryptography.HashAlgorithmName]::SHA256)
$sPath = Join-Path $Dir 'manifest.json.sig'
Set-Content -Path $sPath -Value ([Convert]::ToBase64String($sig)) -Encoding ascii -NoNewline
Write-Host "Wrote $mPath and $sPath"
if ($NoUpload) { return }
gh release upload $Tag $mPath $sPath --repo ArmSal/mailflow
if ($LASTEXITCODE -ne 0) { throw 'Upload failed.' }
Write-Host "Uploaded to release $Tag."
