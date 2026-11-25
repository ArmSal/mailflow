using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Installation de Mailflow")]
[assembly: AssemblyDescription("Installateur complet de Mailflow")]
[assembly: AssemblyProduct("Mailflow")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Mailflow.Setup
{
    internal sealed class InstallOptions
    {
        public string InstallDir;
        public bool CreateDesktopShortcut;
        public bool CreateStartMenuShortcut;
        public bool StartWithWindows;
        public bool LaunchAfterInstall;
        public bool RegisterUninstall;
        public bool Silent;

        public InstallOptions()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            InstallDir = Path.Combine(localApp, "Programs", "Mailflow");
            CreateDesktopShortcut = true;
            CreateStartMenuShortcut = true;
            StartWithWindows = true;
            LaunchAfterInstall = true;
            RegisterUninstall = true;
            Silent = false;
        }
    }

    internal static class SetupProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            InstallOptions opts = ParseArgs(args);
            if (opts.Silent)
            {
                try
                {
                    InstallerEngine.Install(opts, null);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (SetupForm form = new SetupForm(opts))
            {
                Application.Run(form);
                return form.Succeeded ? 0 : 1;
            }
        }

        private static InstallOptions ParseArgs(string[] args)
        {
            InstallOptions opts = new InstallOptions();
            if (args == null) return opts;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase))
                {
                    opts.Silent = true;
                }
                else if (a.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                {
                    string v = a.Substring(6).Trim().Trim('"');
                    if (v.Length > 0) opts.InstallDir = v;
                }
                else if (string.Equals(a, "--no-desktop", StringComparison.OrdinalIgnoreCase))
                {
                    opts.CreateDesktopShortcut = false;
                }
                else if (string.Equals(a, "--no-startmenu", StringComparison.OrdinalIgnoreCase))
                {
                    opts.CreateStartMenuShortcut = false;
                }
                else if (string.Equals(a, "--no-startup", StringComparison.OrdinalIgnoreCase))
                {
                    opts.StartWithWindows = false;
                }
                else if (string.Equals(a, "--no-launch", StringComparison.OrdinalIgnoreCase))
                {
                    opts.LaunchAfterInstall = false;
                }
                else if (string.Equals(a, "--no-reg", StringComparison.OrdinalIgnoreCase))
                {
                    opts.RegisterUninstall = false;
                }
            }
            return opts;
        }
    }

    internal static class InstallerEngine
    {
        private const string Version = "1.0.0";

        public static void Install(InstallOptions opts, Action<int, string> onProgress)
        {
            string fullRoot = Path.GetFullPath(opts.InstallDir.Trim());
            if (fullRoot.Length == 0) throw new InvalidOperationException("Le dossier d'installation est vide.");

            Report(onProgress, 2, "Arrêt d'une instance Mailflow en cours…");
            StopRunningInstance(fullRoot);

            Report(onProgress, 8, "Création du dossier d'installation…");
            Directory.CreateDirectory(fullRoot);

            string rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream("payload.zip"))
            {
                if (stream == null) throw new InvalidOperationException("Archive intégrée introuvable (payload.zip).");
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int done = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        done++;
                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        string dest = Path.GetFullPath(Path.Combine(fullRoot, relative));
                        if (!dest.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(dest, fullRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Chemin d'archive non autorisé : " + entry.FullName);
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(dest);
                        }
                        else
                        {
                            string parent = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                            using (Stream inStream = entry.Open())
                            using (FileStream outStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                inStream.CopyTo(outStream);
                            }
                        }

                        if (total > 0 && (done % 10 == 0 || done == total))
                        {
                            int pct = 10 + (int)((done * 75.0) / total);
                            Report(onProgress, pct, "Extraction des fichiers (" + done + "/" + total + ")…");
                        }
                    }
                }
            }

            string exePath = Path.Combine(fullRoot, "Mailflow.exe");
            if (!File.Exists(exePath)) throw new InvalidOperationException("Mailflow.exe introuvable après extraction.");

            if (opts.CreateDesktopShortcut)
            {
                Report(onProgress, 88, "Création du raccourci sur le Bureau…");
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktop, "Mailflow.lnk"), exePath, fullRoot);
            }

            if (opts.CreateStartMenuShortcut)
            {
                Report(onProgress, 91, "Création du raccourci dans le menu Démarrer…");
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                CreateShortcut(Path.Combine(programs, "Mailflow.lnk"), exePath, fullRoot);
            }

            if (opts.RegisterUninstall)
            {
                Report(onProgress, 94, "Configuration du démarrage et de la désinstallation…");
                using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (runKey != null)
                    {
                        if (opts.StartWithWindows)
                            runKey.SetValue("Mailflow", "\"" + exePath + "\" --minimized");
                        else
                            runKey.DeleteValue("Mailflow", false);
                    }
                }

                string uninstallScript = Path.Combine(fullRoot, "deploy", "uninstall-app.ps1");
                string uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + uninstallScript + "\" -InstallDir \"" + fullRoot + "\"";
                using (RegistryKey uninst = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mailflow"))
                {
                    if (uninst != null)
                    {
                        uninst.SetValue("DisplayName", "Mailflow");
                        uninst.SetValue("DisplayVersion", Version);
                        uninst.SetValue("Publisher", "Mailflow");
                        uninst.SetValue("InstallLocation", fullRoot);
                        uninst.SetValue("DisplayIcon", exePath);
                        uninst.SetValue("UninstallString", uninstallCmd);
                        uninst.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        uninst.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }

            if (opts.LaunchAfterInstall)
            {
                Report(onProgress, 98, "Démarrage de Mailflow…");
                ProcessStartInfo psi = new ProcessStartInfo(exePath);
                psi.WorkingDirectory = fullRoot;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }

            Report(onProgress, 100, "Installation terminée !");
        }

        private static void Report(Action<int, string> onProgress, int percent, string status)
        {
            if (onProgress != null) onProgress(percent, status);
        }

        private static void StopRunningInstance(string installDir)
        {
            string targetExe = Path.Combine(installDir, "Mailflow.exe");
            Process[] procs = Process.GetProcessesByName("Mailflow");
            foreach (Process p in procs)
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule != null ? p.MainModule.FileName : null; }
                    catch { }
                    if (path != null && string.Equals(Path.GetFullPath(path), targetExe, StringComparison.OrdinalIgnoreCase))
                    {
                        p.CloseMainWindow();
                        if (!p.WaitForExit(3000)) p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        private static void CreateShortcut(string lnkPath, string targetExe, string workingDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (shortcut == null) return;
                try
                {
                    Type scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe + ",0" });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Mailflow - Tri des e-mails et impression PDF" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly InstallOptions _opts;
        private readonly TextBox _dirBox;
        private readonly Button _browseBtn;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkStartMenu;
        private readonly CheckBox _chkStartup;
        private readonly CheckBox _chkLaunch;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Button _installBtn;
        private readonly Button _closeBtn;
        private bool _installing;

        public bool Succeeded { get; private set; }

        public SetupForm(InstallOptions opts)
        {
            _opts = opts;
            Text = "Installation de Mailflow 1.0.0";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 380);
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = "Installer Mailflow 1.0.0";
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.Location = new Point(20, 16);
            title.Size = new Size(480, 28);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Installe Mailflow, SumatraPDF (impression PDF silencieuse), l'interface web. Vos données existantes dans 'data' sont conservées.";
            subtitle.ForeColor = Color.FromArgb(75, 85, 99);
            subtitle.Location = new Point(20, 48);
            subtitle.Size = new Size(480, 38);
            Controls.Add(subtitle);

            Label dirLabel = new Label();
            dirLabel.Text = "Dossier d'installation :";
            dirLabel.Location = new Point(20, 96);
            dirLabel.Size = new Size(480, 20);
            Controls.Add(dirLabel);

            _dirBox = new TextBox();
            _dirBox.Text = opts.InstallDir;
            _dirBox.Location = new Point(20, 118);
            _dirBox.Size = new Size(382, 24);
            Controls.Add(_dirBox);

            _browseBtn = new Button();
            _browseBtn.Text = "Parcourir…";
            _browseBtn.Location = new Point(408, 117);
            _browseBtn.Size = new Size(92, 26);
            _browseBtn.Click += OnBrowse;
            Controls.Add(_browseBtn);

            _chkDesktop = new CheckBox();
            _chkDesktop.Text = "Créer un raccourci sur le Bureau";
            _chkDesktop.Checked = opts.CreateDesktopShortcut;
            _chkDesktop.Location = new Point(20, 158);
            _chkDesktop.Size = new Size(480, 22);
            Controls.Add(_chkDesktop);

            _chkStartMenu = new CheckBox();
            _chkStartMenu.Text = "Créer un raccourci dans le menu Démarrer";
            _chkStartMenu.Checked = opts.CreateStartMenuShortcut;
            _chkStartMenu.Location = new Point(20, 184);
            _chkStartMenu.Size = new Size(480, 22);
            Controls.Add(_chkStartMenu);

            _chkStartup = new CheckBox();
            _chkStartup.Text = "Démarrer Mailflow automatiquement avec Windows (en arrière-plan)";
            _chkStartup.Checked = opts.StartWithWindows;
            _chkStartup.Location = new Point(20, 210);
            _chkStartup.Size = new Size(480, 22);
            Controls.Add(_chkStartup);

            _chkLaunch = new CheckBox();
            _chkLaunch.Text = "Lancer Mailflow à la fin de l'installation";
            _chkLaunch.Checked = opts.LaunchAfterInstall;
            _chkLaunch.Location = new Point(20, 236);
            _chkLaunch.Size = new Size(480, 22);
            Controls.Add(_chkLaunch);

            _progress = new ProgressBar();
            _progress.Location = new Point(20, 274);
            _progress.Size = new Size(480, 20);
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            Controls.Add(_progress);

            _status = new Label();
            _status.Text = "Prêt à installer.";
            _status.ForeColor = Color.FromArgb(55, 65, 81);
            _status.Location = new Point(20, 300);
            _status.Size = new Size(480, 20);
            Controls.Add(_status);

            _installBtn = new Button();
            _installBtn.Text = "Installer";
            _installBtn.Location = new Point(304, 334);
            _installBtn.Size = new Size(96, 30);
            _installBtn.Click += OnInstallClick;
            Controls.Add(_installBtn);

            _closeBtn = new Button();
            _closeBtn.Text = "Fermer";
            _closeBtn.Location = new Point(406, 334);
            _closeBtn.Size = new Size(94, 30);
            _closeBtn.Click += delegate { Close(); };
            Controls.Add(_closeBtn);

            AcceptButton = _installBtn;
            CancelButton = _closeBtn;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choisissez le dossier où installer Mailflow";
                dlg.SelectedPath = _dirBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    _dirBox.Text = dlg.SelectedPath;
                }
            }
        }

        private void SetEnabled(bool enabled)
        {
            _dirBox.Enabled = enabled;
            _browseBtn.Enabled = enabled;
            _chkDesktop.Enabled = enabled;
            _chkStartMenu.Enabled = enabled;
            _chkStartup.Enabled = enabled;
            _chkLaunch.Enabled = enabled;
            _installBtn.Enabled = enabled;
            _closeBtn.Enabled = enabled;
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            if (_installing) return;
            _installing = true;
            _opts.InstallDir = _dirBox.Text;
            _opts.CreateDesktopShortcut = _chkDesktop.Checked;
            _opts.CreateStartMenuShortcut = _chkStartMenu.Checked;
            _opts.StartWithWindows = _chkStartup.Checked;
            _opts.LaunchAfterInstall = _chkLaunch.Checked;
            SetEnabled(false);

            Thread t = new Thread(delegate()
            {
                try
                {
                    InstallerEngine.Install(_opts, delegate(int pct, string msg)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _progress.Value = Math.Max(0, Math.Min(100, pct));
                            _status.Text = msg;
                        });
                    });
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _installing = false;
                        Succeeded = true;
                        _closeBtn.Enabled = true;
                        _installBtn.Text = "Terminé";
                        MessageBox.Show(this, "Mailflow a été installé avec succès.", "Installation de Mailflow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Close();
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _installing = false;
                        Succeeded = false;
                        SetEnabled(true);
                        _status.Text = "Échec : " + ex.Message;
                        MessageBox.Show(this, "Erreur pendant l'installation :\r\n" + ex.Message, "Installation de Mailflow", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }
}
