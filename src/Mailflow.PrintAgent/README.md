# Mailflow Print Agent

A small program for the PC that owns a printer. Mailflow runs somewhere else (for example at the shop where the mail arrives); the agent connects to it, downloads the PDFs waiting for this printer and prints them with SumatraPDF. Mailflow never connects to the agent: the agent only makes outgoing requests, so nothing has to be opened on the printer PC's network.

Each agent has a **name** and a **password**. You choose both when you create the agent in Mailflow (the password is 12 characters or more), and type the same name and password when you install the agent. Mailflow keeps only a hash of the password; the agent keeps it encrypted for the Windows user who ran the setup. The name is how Mailflow knows which agent (which site, which printers) is calling, so give each PC its own name, for example `Boutique Centre`.

## 1. In Mailflow (the PC that reads the mail)

1. **Imprimantes > Agents d'impression** (Printers > Print agents): create an agent, give it a name and a password (leave the password empty to have one generated and shown once).
2. **Imprimantes > Ajouter / Modifier**: set the type to **PrintNode**, pick the agent, and put in the printer's **Windows name on the agent PC** (for example `HP LaserJet 400`). "Détecter les imprimantes" lists the printers that agent reported, so you can click one instead of typing it. Use "Test d'impression" to queue a test page.
3. Rules that print to this printer now queue the PDF for the agent instead of printing locally. The History shows the document as printed once the agent reports success, or shows the agent's error.

### Letting the agent reach Mailflow

Mailflow's web interface only listens on this PC (`127.0.0.1:5088`). To let agents in, add a **second listener that serves nothing but the agent API**. With Tailscale, bind it to the PC's Tailscale address (or its MagicDNS name) in `appsettings.json` next to `Mailflow.exe`:

```json
{
  "Agent": { "Port": 5089 },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://127.0.0.1:5088" },
      "Agent": { "Url": "http://100.64.0.5:5089" }
    }
  },
  "AllowedHosts": "localhost;127.0.0.1;[::1];100.64.0.5"
}
```

Replace `100.64.0.5` with the Tailscale address of the Mailflow PC. Restart Mailflow. On port 5089 every page other than `/api/agent/*` answers 404, so the login page and the web interface are not reachable from the network.

If Windows Firewall blocks it, allow the port from Tailscale only (PowerShell as administrator):

```powershell
New-NetFirewallRule -DisplayName "Mailflow agent" -Direction Inbound -Protocol TCP -LocalPort 5089 -RemoteAddress 100.64.0.0/10 -Action Allow
```

Tailscale encrypts the traffic between the two PCs. Do not expose the agent port to the open internet over plain `http://`: the password would travel in clear text. If you must, put it behind HTTPS (for example `tailscale serve` or a reverse proxy) and use an `https://` address in the agent.

## 2. On the PC with the printer

**With the installer (recommended):** run `MailflowPrintAgent-Setup-<version>-win-x64.exe` (French or English, no administrator rights). Enter the Mailflow address (`http://100.64.0.5:5089`), the agent's name and its password; the wizard tests the connection, installs the agent for the current Windows user, can start it with Windows and starts it now. It adds an entry in "Installed apps" for uninstalling. Silent install: `Setup.exe /S /server=http://100.64.0.5:5089 /name="Boutique Centre" /password=... [/dir=C:\MailflowPrintAgent] [/nostartup] [/noRun] [/lang=en]` (the password is visible in the command line, so prefer the wizard or `--setup` on shared machines).

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
| `PasswordProtected` | The password, encrypted for the current Windows user (written by `--setup`) |
| `SumatraPath` | Path to SumatraPDF.exe (default: next to the agent) |
| `PollSeconds` | Seconds between checks when there is nothing to print (1 to 300, default 5) |

Other switches: `--run` (hidden, used by the autostart entry), `--uninstall` (removes the autostart entry).

## Behaviour to know

- If Mailflow cannot be reached the agent keeps trying (every 5 seconds, then up to once a minute) and prints what is waiting as soon as it is back.
- If the agent takes a job but never reports (it crashed or lost the network), Mailflow offers the job again after 5 minutes. In that rare case a page can print twice.
- **Changing the password:** in Mailflow, on the Print agents page, type a new password next to the agent and press "Changer le mot de passe" (empty = generate one). The old password stops working at once, so then run `MailflowPrintAgent.exe --setup` on the printer PC (or run the installer again) and enter the new one. Printers and queued jobs are kept. The Mailflow administrator password is changed separately, under Settings > Sécurité.
- **Printers:** every 10 minutes (and at start) the agent sends Mailflow the list of printers installed on its PC, including network printers added to Windows there. It cannot discover printers that are not installed on that PC.
- SumatraPDF is a separate program under the GNU GPL v3, see `THIRD-PARTY-NOTICES.md`.
