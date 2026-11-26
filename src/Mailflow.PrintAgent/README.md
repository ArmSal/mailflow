# Mailflow Print Agent

A small program for the PC that owns a printer. Mailflow runs somewhere else (for example at the shop where the mail arrives); the agent connects to it, downloads the PDFs waiting for this printer and prints them with SumatraPDF. Mailflow never connects to the agent: the agent only makes outgoing requests, so nothing has to be opened on the printer PC's network.

Each agent has a **name** and a **password**. You choose both when you create the agent in Mailflow (the password is 12 characters or more), and type the same name and password when you install the agent. Mailflow keeps only a hash of the password; the agent keeps it encrypted for the Windows user who ran the setup. The name is how Mailflow knows which agent (which site, which printers) is calling, so give each PC its own name, for example `Boutique Centre`.

## 1. In Mailflow (the PC that reads the mail)

1. **Imprimantes > Agents d'impression** (Printers > Print agents): create an agent, give it a name and a password (leave the password empty to have one generated and shown once).
2. **Imprimantes > Ajouter / Modifier**: set the type to **PrintNode**, pick the agent, and put in the printer's **Windows name on the agent PC** (for example `HP LaserJet 400`). "Détecter les imprimantes" lists the printers that agent reported, so you can click one instead of typing it. Use "Test d'impression" to queue a test page.
3. Rules that print to this printer now queue the PDF for the agent instead of printing locally. The History shows the document as printed once the agent reports success, or shows the agent's error.

### Letting the agent reach Mailflow

Tailscale is used as the example below, but it is only one option: any private network, VPN or trusted LAN works the same way. Use that network's address of the Mailflow PC wherever the Tailscale address appears.

Mailflow's web interface only listens on this PC (`127.0.0.1:5088`). To let agents in, add a **second listener that serves nothing but the agent API**, on the PC's Tailscale address, in `appsettings.json` next to `Mailflow.exe`. Use an `https://` address so the connection is encrypted and the agent can check it is talking to the real Mailflow:

```json
{
  "Agent": { "Port": 5089 },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://127.0.0.1:5088" },
      "Agent": { "Url": "https://100.64.0.5:5089" }
    }
  },
  "AllowedHosts": "localhost;127.0.0.1;[::1];100.64.0.5"
}
```

Replace `100.64.0.5` with the Tailscale address of the Mailflow PC and restart Mailflow. On port 5089 every page other than `/api/agent/*` answers 404, so the login page and the web interface are not reachable from the network.

**How the HTTPS works.** The first time Mailflow starts with an `https://` agent address it creates its own certificate (kept in `data\agent-tls.dat`, encrypted for the Windows user running Mailflow, valid 10 years) and shows its **SHA-256 fingerprint** on the Print agents page. When you install an agent, it connects, shows the fingerprint it received and asks you to confirm that it is the same one Mailflow shows. From then on the agent accepts **only that certificate**: a fake Mailflow, or someone in the middle, is refused even though the certificate is self-signed. The password is then sent over an encrypted, verified connection.

- To skip the confirmation, paste the fingerprint into the installer's fingerprint field (or `/certsha256=...`, or `ServerCertSha256` in `agent.json`).
- If the fingerprint changes (the `data` folder was deleted, or Mailflow now runs under another Windows account) every agent has to be set up again and will show the new fingerprint for confirmation.
- **Your own certificate** (for example from `tailscale cert`): add `"Certificate": { "Path": "mailflow.crt", "KeyPath": "mailflow.key" }` to the `Agent` endpoint and use the certificate's name in the address. Windows then trusts it and the agent needs no fingerprint.
- An `http://` address still works and is accepted on a Tailscale-only network, where Tailscale encrypts the traffic, but Mailflow warns about it on the Print agents page. Never use plain `http://` outside such a network.

If Windows Firewall blocks the port, allow it from Tailscale only (PowerShell as administrator):

```powershell
New-NetFirewallRule -DisplayName "Mailflow agent" -Direction Inbound -Protocol TCP -LocalPort 5089 -RemoteAddress 100.64.0.0/10 -Action Allow
```

## 2. On the PC with the printer

**With the installer (recommended):** run `MailflowPrintAgent-Setup-<version>-win-x64.exe` (French or English, no administrator rights). Enter the Mailflow address (`https://100.64.0.5:5089`), the agent's name and its password. For an `https://` address the wizard shows the certificate fingerprint and asks you to confirm it matches the one in Mailflow; the wizard tests the connection, installs the agent for the current Windows user, can start it with Windows and starts it now. It adds an entry in "Installed apps" for uninstalling. Silent install: `Setup.exe /S /server=https://100.64.0.5:5089 /certsha256=AB:CD:... /name="Boutique Centre" /password=... [/dir=C:\MailflowPrintAgent] [/nostartup] [/noRun] [/lang=en]` (the password is visible in the command line, so prefer the wizard or `--setup` on shared machines).

**Without the installer:**

1. Unzip `MailflowPrintAgent-<version>-win-x64.zip` anywhere (for example `C:\MailflowPrintAgent`). `SumatraPDF.exe` is included.
2. Run `MailflowPrintAgent.exe --setup`. Enter the Mailflow address, the agent's name and its password. The setup tests the connection, saves `agent.json`, and can start the agent with Windows.
3. Run `MailflowPrintAgent.exe` to start it now. With "start with Windows" it runs hidden at each logon of that Windows user.

The printer must be installed on that PC and its Windows name must match the one entered in Mailflow. `Get-Printer | Select Name` in PowerShell lists the names.

Files next to the program: `agent.json` (settings), `agent.log` (what the agent did, kept to about 1 MB).

| `agent.json` | Meaning |
|---|---|
| `ServerUrl` | Address of Mailflow's agent listener |
| `AgentName` | This agent's name in Mailflow |
| `ServerCertSha256` | Fingerprint of Mailflow's certificate for `https://` (written after you confirm it); only that certificate is accepted |
| `PasswordProtected` | The password, encrypted for the current Windows user (written by `--setup`) |
| `SumatraPath` | Path to SumatraPDF.exe (default: next to the agent) |
| `PollSeconds` | Seconds between checks when there is nothing to print (1 to 300, default 5) |

Other switches: `--run` (hidden, used by the autostart entry), `--uninstall` (removes the autostart entry).

## Behaviour to know

- **Instant delivery.** The agent keeps a request open to Mailflow, which holds it until something needs printing and then answers at once, so a print reaches the agent in about a second (measured: 0.3 s). The agent authenticates with its name and password each time it connects, and prints only what Mailflow hands over after that. `PollSeconds` is only the pause used if Mailflow answers straight away (an older Mailflow without waiting).
- If Mailflow cannot be reached the agent keeps trying (every 5 seconds, then up to once a minute) and prints what is waiting as soon as it is back.
- If the agent takes a job but never reports (it crashed or lost the network), Mailflow offers the job again after 5 minutes. In that rare case a page can print twice.
- **Changing the password:** in Mailflow, on the Print agents page, type a new password next to the agent and press "Changer le mot de passe" (empty = generate one). The old password stops working at once, so then run `MailflowPrintAgent.exe --setup` on the printer PC (or run the installer again) and enter the new one. Printers and queued jobs are kept. The Mailflow administrator password is changed separately, under Settings > Sécurité.
- **Printers:** every 10 minutes (and at start) the agent sends Mailflow the list of printers installed on its PC, including network printers added to Windows there. It cannot discover printers that are not installed on that PC.
- SumatraPDF is a separate program under the GNU GPL v3, see `THIRD-PARTY-NOTICES.md`.

## Security notes

What protects the link:

- **No open port on the printer PC.** The agent only makes outgoing connections, so nothing on that PC listens for the network.
- **HTTPS with a pinned certificate.** The agent accepts only the certificate whose fingerprint you confirmed, so it cannot be tricked into sending its password or printing PDFs for a fake Mailflow, and nobody on the path can read or change the traffic.
- **Tailscale** also encrypts and authenticates the traffic between the two PCs (WireGuard). Its access rules can limit which devices may reach Mailflow's agent port.
- **Name and password on every connection.** Mailflow checks one salted PBKDF2 hash per request, refuses an address after 10 wrong tries a minute, and answers an unknown name as slowly as a wrong password.
- **The agent API is the only thing on that port.** An agent can only fetch and report its own jobs; the file path of a PDF comes from Mailflow's database, never from the request.

Known limits you should understand:

1. **HTTP is still possible.** If you configure an `http://` agent address, only Tailscale protects the link, and the agent cannot tell a fake Mailflow from the real one. Mailflow shows a warning on the Print agents page. Use `https://`.
2. **The password is the whole identity of an agent.** Whoever has an agent's name and password can read that agent's queued PDFs and mark jobs done. Choose a long password, change it (Print agents page) if it leaks, and prefer the wizard or `--setup` to `/password=` on the command line, which other programs on the PC can see.
3. **Guessing is slowed, not impossible.** The lockout is per address, so many different devices can still try. Limit who can reach the port with Tailscale access rules.
4. **PDFs are opened by SumatraPDF.** A hostile PDF sent by a compromised Mailflow could try to exploit a flaw in it. Keep SumatraPDF up to date.
5. **A Windows user who is logged in on the printer PC can read the agent's saved password** (it is encrypted for that user, not hidden from them), and anything running as that user can too.
6. **The programs are not code-signed**, so Windows SmartScreen may warn when you run them, and you cannot verify the publisher. Check the file comes from your own release page.
7. **A page can print twice** if the agent takes a job and loses the network before reporting (Mailflow offers it again after 5 minutes).
