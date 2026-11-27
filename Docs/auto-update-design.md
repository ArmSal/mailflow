# Auto-update design (draft, not implemented)

## What exists today
- `UpdateChecker` (Web/Updates) reads GitHub `releases/latest` at startup then daily and shows a banner linking to the release page. Nothing is downloaded.
- Releases (v1.3.3 is current) ship 4 assets: `Mailflow-Setup-<v>-win-x64.exe`, `MailflowPrintAgent-Setup-<v>-win-x64.exe` and the two zips. No checksums, no code signing.
- `Setup.exe /S` already upgrades in place: stops the running `Mailflow.exe` of that folder (close, then kill after 4 s), deletes old program files, keeps `data\`, rewrites shortcuts/registry, relaunches (unless `/noRun`). Per-user install, no admin rights.
- EF migrations run at app start; the app refuses to start if the schema is out of step. A downgrade after a migration is therefore not possible.

## Threat model
Running a downloaded installer = remote code execution as the user. Who can feed us a bad installer?
1. Network attacker: stopped by HTTPS (default .NET cert validation, no override).
2. Corrupt or truncated download: stopped by a hash.
3. Someone who controls the GitHub release (stolen account or token, malicious workflow change): a hash stored in the same release does NOT stop this. Only a signature made with a key that is not in GitHub does.
4. Rollback to an old vulnerable version: stopped by a signed version number that must be greater than the running one.

## Recommended design
**Signed manifest, embedded public key.**
- Each release gets `manifest.json`: `{ product, version, published (UTC), files: [{name, sha256, size}] }`, plus `manifest.json.sig`, an ECDSA P-256 signature (built into .NET, no new dependency).
- The public key is compiled into Mailflow and the Print Agent. The private key lives offline on the owner's machine (not a GitHub secret). A script `deploy/sign-release.ps1` signs the manifest after the CI build, and the owner uploads the two files to the release. A compromised GitHub account can then no longer ship an update that clients accept.
- Cost: one manual signing step per release. If that is too heavy, the fallback is to keep the key as a CI secret: this only defends against 1, 2 and 4, not 3. Authenticode (paid certificate) is the longer-term upgrade and is independent of this design.

**Client flow** (new `Updates/UpdateInstaller`):
1. `UpdateChecker` finds a newer tag (already built). It then downloads `manifest.json` + `.sig` from `https://github.com/ArmSal/mailflow/releases/download/<tag>/` (host and path allowlist, no redirects to other hosts).
2. Verify signature, `product`, `version == tag`, `version > running`. Any failure: show the banner link only, log at Debug.
3. Download the installer to `data\updates\<version>\` (current user only), check size and SHA-256 against the manifest.
4. Before launching: trigger the existing backup so there is a restore point for the migration; refuse if a print job is running (retry later).
5. Launch `Setup.exe /S` detached, from that private folder, then exit. Setup stops us, installs, relaunches. Setup needs a small new option so the relaunch uses `--minimized` (today the silent relaunch opens the window).
6. Guard against loops: record `attempted=<version>` in `data\updates\state.json`; do not retry the same version for 24 h, and surface "update failed" in the UI if the version after restart is still the old one.

**Modes** (Setting `UpdateMode`, replaces the on/off `UpdateCheck`): `Off`, `Notify` (today's behaviour), `Manual` (banner button "Mettre à jour maintenant" runs the flow), `Auto` (runs when idle). Suggested default: `Manual`, `Auto` opt-in. A store PC that prints invoices should not restart by surprise.

**Scope guard:** only when the running exe's folder equals the `InstallLocation` of the per-user Uninstall registry key (i.e. installed by Setup.exe). Windows-service installs and dev runs stay banner-only.

**Print Agent:** same manifest and verification code, a smaller updater inside the agent. Agent installer upgrades must keep the agent's name and (DPAPI-protected) password config. I have not verified that `AgentSetupProgram.cs` does in silent mode. Server first, agents after: an agent must tolerate an older server.

## Installer hardening needed
Setup deletes the old files before copying the new ones, so a failure halfway leaves a broken install. For auto-update this must change: extract to `<dir>.new`, then swap, keeping `<dir>.old` until the new version starts.

## Phases
1. Release pipeline: `manifest.json` generation in `build-release.ps1`, `sign-release.ps1`, key generation, public key embedded. Tests for sign/verify, tampering, downgrade, wrong product.
2. Mailflow server/desktop updater (Manual mode) + Setup `--minimized` relaunch + safe swap. Help FR+EN and README.
3. `Auto` mode + failure/loop handling.
4. Print Agent updater.

## Decisions (owner, 2026-11-27)
1. Signing: offline key, manual signing per release (`deploy/sign-release.ps1`). Key at `%USERPROFILE%\.mailflow\release-signing.pem`, unencrypted: back it up, keep it off the repo and off shared drives.
2. Default mode: `Notify` (today's banner only). `Manual` and `Auto` are opt-in in Settings.
3. The Print Agent is part of the first delivery (not phase 4).

## Progress
- Phase 1 done: `Application/Updates/UpdateManifest.cs` + `UpdateKeys.cs` (verifier, public key embedded), `deploy/sign-release.ps1` (tested on v1.3.3 with `-NoUpload`), 10 unit tests. The verifier is plain BCL so it can be linked into the Print Agent.
- Releases before the first signed one have no manifest: clients simply keep showing the banner.
- Release routine becomes: push tag, CI publishes, owner checks the assets, runs `./deploy/sign-release.ps1 -Tag vX.Y.Z`.

## Assumptions
- Distribution stays GitHub Releases on `ArmSal/mailflow`; the repo may become public, so the updater must also work anonymously (it does today).
- Installs are per-user via Setup.exe; no admin elevation is wanted.
- The Setup.exe source (C# 5, csc.exe) can be edited; no new dependencies.
