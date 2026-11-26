// Installer for the Mailflow Print Agent. Plain C# 5 (built with the .NET Framework csc.exe that ships with Windows).
// Built twice by deploy\build-release.ps1: with /define:UNINSTALL (Uninstall.exe, no payload) and without (Setup.exe, payload.zip embedded).
// Per-user install, no administrator rights. Options: /S or /silent, /dir=PATH, /server=URL, /password=TEXT, /nostartup, /noRun, /lang=fr|en
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Mailflow.AgentSetup
{
    internal static class L
    {
        public static string Lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        public static string T(string fr, string en) { return Lang == "fr" ? fr : en; }
    }

    internal sealed class Options
    {
        public bool Silent, Startup = true, Launch = true, Stage2, DirGiven;
        public string InstallDir, Server, Password, BadArg;

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            o.InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MailflowPrintAgent");
            foreach (string raw in args)
            {
                string a = raw.TrimStart('/', '-');
                string key = a, val = null;
                int eq = a.IndexOf('=');
                if (eq >= 0) { key = a.Substring(0, eq); val = a.Substring(eq + 1); }
                switch (key.ToLowerInvariant())
                {
                    case "s": case "silent": o.Silent = true; break;
                    case "dir": if (!string.IsNullOrEmpty(val)) { o.InstallDir = val; o.DirGiven = true; } break;
                    case "server": o.Server = val; break;
                    case "password": o.Password = val; break;
                    case "nostartup": o.Startup = false; break;
                    case "norun": o.Launch = false; break;
                    case "lang": if (val == "fr" || val == "en") L.Lang = val; break;
                    case "_stage2": o.Stage2 = true; break;
                    default: o.BadArg = raw; break;
                }
            }
            return o;
        }
    }

    internal static class Sys
    {
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MailflowPrintAgent";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "MailflowPrintAgent";
        public const string ExeName = "MailflowPrintAgent.exe";

        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

        public static void AttachParentConsole() { try { AttachConsole(-1); } catch (Exception) { } }

        public static string Full(string p) { return Path.GetFullPath(p.Trim().TrimEnd('\\', '/')); }

        public static bool Same(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stops only the agent running from this install folder.</summary>
        public static void StopAgent(string dir)
        {
            string exe = Path.Combine(dir, ExeName);
            foreach (Process p in Process.GetProcessesByName("MailflowPrintAgent"))
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule.FileName; } catch (Exception) { }
                    if (path == null || !Same(path, exe)) continue;
                    p.Kill();
                    p.WaitForExit(8000);
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }

        /// <summary>Checks the address and password. Returns null when Mailflow accepted them, otherwise the reason.</summary>
        public static string TestConnection(string server, string password)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(server.TrimEnd('/') + "/api/agent/ping");
                req.Timeout = 8000;
                req.Headers["Authorization"] = "Bearer " + password;
                using (req.GetResponse()) { }
                return null;
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.Unauthorized) return L.T("Mailflow a refusé le mot de passe.", "Mailflow refused the password.");
                return ex.Message;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static string JsonString(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class Remover
    {
        /// <summary>Removes the agent. Returns what could not be deleted. Uninstall.exe itself is left to the caller.</summary>
        public static List<string> Run(string dir)
        {
            List<string> failed = new List<string>();
            Sys.StopAgent(dir);
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(Sys.RunKey, true))
                {
                    string v = run == null ? null : run.GetValue(Sys.RunValue) as string;
                    if (v != null && v.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) run.DeleteValue(Sys.RunValue, false);
                }
                Registry.CurrentUser.DeleteSubKeyTree(Sys.UninstallKey, false);
            }
            catch (Exception) { }
            string self = Path.Combine(dir, "Uninstall.exe");
            foreach (string f in Directory.GetFiles(dir))
            {
                if (Sys.Same(f, self)) continue;
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception) { failed.Add(f); }
            }
            foreach (string d in Directory.GetDirectories(dir))
                try { Directory.Delete(d, true); } catch (Exception) { failed.Add(d); }
            return failed;
        }
    }

#if !UNINSTALL
    internal static class Installer
    {
        /// <summary>Copies the program, writes the configuration and the Windows entries. Throws with a readable message on failure.</summary>
        public static void Install(Options o)
        {
            string dir = Sys.Full(o.InstallDir);
            Directory.CreateDirectory(dir);
            Sys.StopAgent(dir);

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream payload = asm.GetManifestResourceStream("payload.zip"))
            {
                if (payload == null) throw new InvalidOperationException("payload.zip missing");
                using (ZipArchive zip = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    string prefix = dir + Path.DirectorySeparatorChar;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        if (e.FullName.EndsWith("/")) continue;
                        string target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bad path in payload");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                }
            }

            string exe = Path.Combine(dir, Sys.ExeName);
            if (!File.Exists(exe)) throw new FileNotFoundException(Sys.ExeName);
            string uninst = Path.Combine(dir, "Uninstall.exe");
            if (!File.Exists(uninst)) throw new FileNotFoundException("Uninstall.exe");

            string config = Path.Combine(dir, "agent.json");
            if (!string.IsNullOrEmpty(o.Server) && !string.IsNullOrEmpty(o.Password))
            {
                string protectedPw = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(o.Password), null, DataProtectionScope.CurrentUser));
                File.WriteAllText(config,
                    "{\r\n  \"ServerUrl\": " + Sys.JsonString(o.Server.Trim()) + ",\r\n  \"PasswordProtected\": " + Sys.JsonString(protectedPw)
                    + ",\r\n  \"SumatraPath\": \"SumatraPDF.exe\",\r\n  \"PollSeconds\": 5\r\n}\r\n", new UTF8Encoding(false));
            }

            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(Sys.RunKey))
            {
                if (o.Startup) run.SetValue(Sys.RunValue, "\"" + exe + "\" --run");
                else run.DeleteValue(Sys.RunValue, false);
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Sys.UninstallKey))
            {
                k.SetValue("DisplayName", "Mailflow Print Agent");
                k.SetValue("DisplayVersion", BuildInfo.Version);
                k.SetValue("Publisher", "Mailflow");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + uninst + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /S");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            if (o.Launch && File.Exists(config))
                Process.Start(new ProcessStartInfo(exe, "--run") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true });
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly Options _o;
        private readonly TextBox _dir = new TextBox(), _server = new TextBox(), _password = new TextBox();
        private readonly CheckBox _startup = new CheckBox(), _launch = new CheckBox();
        private readonly Button _install = new Button();

        public SetupForm(Options o)
        {
            _o = o;
            Text = "Mailflow Print Agent - " + BuildInfo.Version;
            ClientSize = new Size(540, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;

            Label intro = new Label { Left = 16, Top = 14, Width = 508, Height = 56,
                Text = L.T("Cet agent imprime sur cet ordinateur les PDF envoyés par Mailflow. Saisissez l'adresse de Mailflow et le mot de passe de l'agent (le même que dans Mailflow, sous Imprimantes > Agents d'impression).",
                           "This agent prints on this computer the PDFs sent by Mailflow. Enter Mailflow's address and the agent's password (the same one as in Mailflow, under Printers > Print agents).") };
            Controls.Add(intro);

            AddField(L.T("Adresse de Mailflow (par exemple http://100.64.0.5:5089)", "Mailflow address (for example http://100.64.0.5:5089)"), _server, 82);
            AddField(L.T("Mot de passe de l'agent", "Agent password"), _password, 140);
            _password.UseSystemPasswordChar = true;
            AddField(L.T("Dossier d'installation", "Install folder"), _dir, 198);
            _dir.Width = 410;
            _dir.Text = _o.InstallDir;
            Button browse = new Button { Left = 434, Top = 217, Width = 90, Height = _dir.Height + 2, Text = L.T("Parcourir...", "Browse...") };
            browse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                    if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = Path.Combine(d.SelectedPath, "MailflowPrintAgent");
            };
            Controls.Add(browse);

            _startup.Text = L.T("Démarrer l'agent avec Windows", "Start the agent with Windows");
            _startup.Left = 16; _startup.Top = 262; _startup.Width = 500; _startup.Checked = _o.Startup;
            _launch.Text = L.T("Démarrer l'agent à la fin de l'installation", "Start the agent when the installation finishes");
            _launch.Left = 16; _launch.Top = 290; _launch.Width = 500; _launch.Checked = _o.Launch;
            Controls.Add(_startup); Controls.Add(_launch);

            _install.Text = L.T("Installer", "Install");
            _install.Left = 330; _install.Top = 350; _install.Width = 90; _install.Height = 30;
            _install.Click += OnInstall;
            Button cancel = new Button { Text = L.T("Annuler", "Cancel"), Left = 434, Top = 350, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            Controls.Add(_install); Controls.Add(cancel);
            AcceptButton = _install; CancelButton = cancel;

            if (File.Exists(Path.Combine(_o.InstallDir, "agent.json")))
                _server.Text = ""; // an existing configuration is kept when both fields stay empty
        }

        private void AddField(string label, TextBox box, int top)
        {
            Controls.Add(new Label { Left = 16, Top = top, Width = 508, Text = label });
            box.Left = 16; box.Top = top + 20; box.Width = 508;
            Controls.Add(box);
        }

        private void OnInstall(object sender, EventArgs e)
        {
            string server = _server.Text.Trim(), password = _password.Text;
            Uri uri;
            bool keepExisting = server.Length == 0 && password.Length == 0 && File.Exists(Path.Combine(Sys.Full(_dir.Text), "agent.json"));
            if (!keepExisting)
            {
                if (!Uri.TryCreate(server, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    Warn(L.T("Saisissez l'adresse complète de Mailflow, en commençant par http:// ou https://.", "Enter Mailflow's full address, starting with http:// or https://."));
                    return;
                }
                if (password.Length == 0) { Warn(L.T("Saisissez le mot de passe de l'agent.", "Enter the agent password.")); return; }
                Cursor = Cursors.WaitCursor;
                string problem = Sys.TestConnection(server, password);
                Cursor = Cursors.Default;
                if (problem != null && MessageBox.Show(this,
                        L.T("Connexion à Mailflow impossible :\r\n", "Cannot connect to Mailflow:\r\n") + problem + "\r\n\r\n" + L.T("Installer quand même ?", "Install anyway?"),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            _o.InstallDir = _dir.Text; _o.Server = server; _o.Password = password; _o.Startup = _startup.Checked; _o.Launch = _launch.Checked;
            try
            {
                _install.Enabled = false; Cursor = Cursors.WaitCursor;
                Installer.Install(_o);
                Cursor = Cursors.Default;
                MessageBox.Show(this, L.T("Installation terminée.", "Installation complete."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default; _install.Enabled = true;
                MessageBox.Show(this, L.T("L'installation a échoué : ", "Installation failed: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Warn(string m) { MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options o = Options.Parse(args);
            if (o.Silent) Sys.AttachParentConsole();
            if (o.BadArg != null)
            {
                string m = L.T("Option inconnue : ", "Unknown option: ") + o.BadArg + "\r\n/S, /dir=CHEMIN, /server=URL, /password=TEXTE, /nostartup, /noRun, /lang=fr|en";
                if (o.Silent) Console.Error.WriteLine(m); else MessageBox.Show(m, "Mailflow Print Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            if (o.Silent)
            {
                try { Installer.Install(o); Console.WriteLine(L.T("Installation terminée.", "Installation complete.")); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            using (SetupForm f = new SetupForm(o)) f.ShowDialog();
            return 0;
        }
    }
#else
    // ---------------------------------------------------------------- Uninstall.exe
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options o = Options.Parse(args);
            if (o.Silent) Sys.AttachParentConsole();
            string self = Application.ExecutablePath;
            string dir = Sys.Full(o.DirGiven ? o.InstallDir : Path.GetDirectoryName(self));

            if (!o.Stage2)
            {
                // Run from a temp copy so that Uninstall.exe itself can be deleted afterwards.
                string tmp = Path.Combine(Path.GetTempPath(), "mailflow-agent-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(self, tmp, true);
                StringBuilder sb = new StringBuilder();
                foreach (string a in args) sb.Append('"').Append(a.Replace("\"", "")).Append("\" ");
                sb.Append("\"/_stage2\" \"/dir=").Append(dir).Append("\"");
                int code;
                using (Process p = Process.Start(new ProcessStartInfo(tmp, sb.ToString()) { UseShellExecute = false }))
                {
                    p.WaitForExit(); code = p.ExitCode;
                }
                bool removed = code == 0 || code == 1;
                string cmd = "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + tmp + "\""
                    + (removed ? " \"" + Path.Combine(dir, "Uninstall.exe") + "\" & rmdir \"" + dir + "\"" : "");
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                return code == 3 ? 0 : code;
            }

            try
            {
                string title = L.T("Désinstallation de Mailflow Print Agent", "Uninstall Mailflow Print Agent");
                if (!o.Silent)
                {
                    Application.EnableVisualStyles();
                    if (MessageBox.Show(L.T("Supprimer Mailflow Print Agent et sa configuration de ", "Remove Mailflow Print Agent and its configuration from ") + dir + " ?",
                        title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 3;
                }
                List<string> failed = Remover.Run(dir);
                string msg = L.T("Mailflow Print Agent a été désinstallé.", "Mailflow Print Agent has been uninstalled.");
                if (failed.Count > 0) msg += "\r\n\r\n" + L.T("Non supprimés :\r\n", "Not removed:\r\n") + string.Join("\r\n", failed.ToArray());
                if (o.Silent) Console.WriteLine(msg);
                else MessageBox.Show(msg, title, MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                return failed.Count > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                if (o.Silent) Console.Error.WriteLine(ex.Message); else MessageBox.Show(ex.Message, "Mailflow Print Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 4;
            }
        }
    }
#endif
}
