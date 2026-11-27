// Mailflow installer / uninstaller (single source, C# 5, built with the .NET Framework csc.exe).
// Built twice by deploy\build-release.ps1: with /define:UNINSTALL (Uninstall.exe, no payload) and without (Setup.exe, payload.zip embedded).
// BuildInfo.Version and the assembly attributes come from a generated file (version single-sourced from -Version).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Mailflow.Setup
{
    // ---------------------------------------------------------------- texts (fr / en)
    internal static class L
    {
        public static string Lang = "en";
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>();
        private static void A(string k, string fr, string en) { Map[k] = new string[] { fr, en }; }

        static L()
        {
            A("setupTitle", "Installation de Mailflow", "Mailflow Setup");
            A("uninstTitle", "Désinstallation de Mailflow", "Mailflow Uninstall");
            A("back", "< Précédent", "< Back");
            A("next", "Suivant >", "Next >");
            A("install", "Installer", "Install");
            A("upgrade", "Mettre à jour", "Update");
            A("cancel", "Annuler", "Cancel");
            A("finish", "Terminer", "Finish");
            A("browse", "Parcourir…", "Browse…");
            A("welcomeH", "Bienvenue", "Welcome");
            A("welcomeS", "Cet assistant installe Mailflow {0} sur cet ordinateur.", "This wizard installs Mailflow {0} on this computer.");
            A("welcomeBody",
              "Mailflow lit vos boîtes e-mail, classe les PDF reçus (factures, bons de livraison…) et les imprime.\r\n\r\nAucun droit administrateur n'est nécessaire : l'installation se fait pour votre compte Windows uniquement.\r\n\r\nCliquez sur Suivant pour continuer.",
              "Mailflow reads your mailboxes, sorts the PDFs it receives (invoices, delivery notes…) and prints them.\r\n\r\nNo administrator rights are needed: the installation is for your Windows account only.\r\n\r\nClick Next to continue.");
            A("welcomeUpd", "Une installation existante a été trouvée ({0}). Elle sera mise à jour ; toutes vos données sont conservées.",
              "An existing installation was found ({0}). It will be updated; all your data is kept.");
            A("licH", "Contrat de licence", "License agreement");
            A("licS", "Lisez la licence avant de continuer.", "Please read the license before continuing.");
            A("licAccept", "J'accepte les termes de la licence", "I accept the terms of the license");
            A("dirH", "Dossier d'installation", "Install folder");
            A("dirS", "Choisissez où installer Mailflow.", "Choose where to install Mailflow.");
            A("dirLabel", "Mailflow sera installé dans ce dossier :", "Mailflow will be installed in this folder:");
            A("dirHint", "Le dossier par défaut ne demande aucun droit administrateur. Les données (base, journaux, sauvegardes) sont stockées dans le sous-dossier « data ».",
              "The default folder needs no administrator rights. Your data (database, logs, backups) is stored in the \"data\" subfolder.");
            A("browseDesc", "Choisissez le dossier d'installation de Mailflow", "Choose the Mailflow install folder");
            A("optH", "Options", "Options");
            A("optS", "Raccourcis et démarrage.", "Shortcuts and startup.");
            A("optDesktop", "Créer un raccourci sur le Bureau", "Create a desktop shortcut");
            A("optStart", "Créer un raccourci dans le menu Démarrer", "Create a Start menu shortcut");
            A("optStartup", "Démarrer Mailflow avec Windows (en arrière-plan)", "Start Mailflow with Windows (in the background)");
            A("optLaunch", "Lancer Mailflow à la fin de l'installation", "Launch Mailflow when setup finishes");
            A("optWv2", "Télécharger et installer Microsoft WebView2 (connexion Internet nécessaire)",
              "Download and install Microsoft WebView2 (Internet connection required)");
            A("wv2Missing", "Le composant Microsoft Edge WebView2 n'est pas installé sur cet ordinateur. Sans lui, Mailflow ne peut pas afficher son tableau de bord.",
              "The Microsoft Edge WebView2 runtime is not installed on this computer. Without it Mailflow cannot display its dashboard.");
            A("progH", "Installation", "Installing");
            A("progS", "Veuillez patienter…", "Please wait…");
            A("doneH", "Installation terminée", "Setup complete");
            A("doneS", "Mailflow est installé.", "Mailflow is installed.");
            A("doneBody", "Mailflow {0} est installé dans :\r\n{1}", "Mailflow {0} is installed in:\r\n{1}");
            A("failH", "Échec de l'installation", "Setup failed");
            A("failS", "L'installation n'a pas pu se terminer.", "Setup could not be completed.");
            A("wv2Manual", "WebView2 n'a pas pu être installé automatiquement. Téléchargez « Evergreen Bootstrapper » sur https://developer.microsoft.com/microsoft-edge/webview2/ puis lancez Mailflow.",
              "WebView2 could not be installed automatically. Download the Evergreen Bootstrapper from https://developer.microsoft.com/microsoft-edge/webview2/ and then start Mailflow.");
            A("wv2Warn", "WebView2 est absent : installez-le (https://developer.microsoft.com/microsoft-edge/webview2/) avant de lancer Mailflow.",
              "WebView2 is missing: install it (https://developer.microsoft.com/microsoft-edge/webview2/) before starting Mailflow.");
            A("cancelQ", "Quitter l'installation ?", "Exit setup?");
            A("pStop", "Arrêt de Mailflow en cours d'exécution…", "Stopping the running Mailflow…");
            A("pClean", "Préparation du dossier…", "Preparing the folder…");
            A("pCopy", "Copie des fichiers ({0}/{1})…", "Copying files ({0}/{1})…");
            A("pShort", "Création des raccourcis…", "Creating shortcuts…");
            A("pReg", "Enregistrement dans Windows…", "Registering with Windows…");
            A("pWv2", "Installation de WebView2…", "Installing WebView2…");
            A("pLaunch", "Démarrage de Mailflow…", "Starting Mailflow…");
            A("pDone", "Terminé.", "Done.");
            A("lnkDesc", "Mailflow - tri des e-mails et impression des PDF", "Mailflow - email sorting and PDF printing");
            A("lnkUninst", "Désinstaller Mailflow", "Uninstall Mailflow");
            A("eNoPayload", "Le programme d'installation est incomplet (archive intégrée introuvable). Téléchargez-le de nouveau.",
              "The installer is incomplete (embedded archive not found). Please download it again.");
            A("eDirEmpty", "Le dossier d'installation est vide.", "The install folder is empty.");
            A("eDirBad", "Dossier d'installation invalide : {0}", "Invalid install folder: {0}");
            A("eDirRoot", "Choisissez un sous-dossier, pas la racine d'un disque : {0}", "Choose a subfolder, not a drive root: {0}");
            A("eDirNotEmpty", "Le dossier {0} n'est pas vide et ne contient pas Mailflow. Choisissez un dossier vide ou un nouveau dossier.",
              "The folder {0} is not empty and does not contain Mailflow. Choose an empty or new folder.");
            A("eAccess", "Écriture impossible dans {0}. Choisissez un dossier de votre profil (par défaut) ou lancez l'installation en administrateur. Détail : {1}",
              "Cannot write to {0}. Choose a folder in your profile (the default) or run setup as administrator. Detail: {1}");
            A("eLocked", "Un fichier de {0} est utilisé par un autre programme. Fermez Mailflow puis recommencez. Détail : {1}",
              "A file in {0} is in use by another program. Close Mailflow and try again. Detail: {1}");
            A("eGeneric", "Erreur : {0}", "Error: {0}");
            A("eBadPath", "Chemin d'archive refusé : {0}", "Archive path rejected: {0}");
            A("eNoExe", "Mailflow.exe est introuvable après la copie.", "Mailflow.exe was not found after copying.");
            A("eUsage", "Option inconnue : {0}\r\n\r\nOptions : /S ou /silent, /dir=CHEMIN, /nostartup, /nodesktop, /nostartmenu, /noRun, /minimized, /nowebview2, /lang=fr|en",
              "Unknown option: {0}\r\n\r\nOptions: /S or /silent, /dir=PATH, /nostartup, /nodesktop, /nostartmenu, /noRun, /minimized, /nowebview2, /lang=fr|en");
            A("uQ", "Voulez-vous désinstaller Mailflow ?\r\n\r\n{0}", "Do you want to uninstall Mailflow?\r\n\r\n{0}");
            A("uData", "Voulez-vous aussi supprimer vos données (base de données, réglages des documents, journaux, sauvegardes) ?\r\n\r\nChoisissez Non pour les conserver (recommandé) : une réinstallation les retrouvera.",
              "Do you also want to delete your data (database, document settings, logs, backups)?\r\n\r\nChoose No to keep them (recommended): a reinstall will find them again.");
            A("uDone", "Mailflow a été désinstallé.", "Mailflow has been uninstalled.");
            A("uKept", "Vos données ont été conservées dans :\r\n{0}", "Your data was kept in:\r\n{0}");
            A("uNotFound", "Aucune installation de Mailflow trouvée dans {0}.", "No Mailflow installation found in {0}.");
            A("uPartial", "Certains éléments n'ont pas pu être supprimés (utilisés par un autre programme) :\r\n{0}", "Some items could not be removed (in use by another program):\r\n{0}");
        }

        public static string T(string key, params object[] args)
        {
            string[] v;
            if (!Map.TryGetValue(key, out v)) return key;
            string s = v[Lang == "fr" ? 0 : 1];
            return args.Length == 0 ? s : string.Format(s, args);
        }

        public static string Detect()
        {
            try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en"; }
            catch (Exception) { return "en"; }
        }
    }

    internal class SetupException : Exception { public SetupException(string m) : base(m) { } }

    // ---------------------------------------------------------------- options
    internal sealed class Options
    {
        public string InstallDir;
        public bool DirGiven;
        public bool Desktop = true;
        public bool StartMenu = true;
        public bool Startup = true;
        public bool Launch = true;
        public bool InstallWebView2 = true;
        public bool Silent;
        public bool Minimized;
        public bool RemoveData;
        public bool Stage2;
        public bool LangGiven;
        public string BadArg;

        public Options()
        {
            InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Mailflow");
        }

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            L.Lang = L.Detect();
            foreach (string raw in args)
            {
                string a = (raw ?? "").Trim();
                if (a.Length == 0) continue;
                if (a[0] == '-') a = a.TrimStart('-'); else if (a[0] == '/') a = a.Substring(1);
                string name = a, val = null;
                int eq = a.IndexOf('=');
                if (eq >= 0) { name = a.Substring(0, eq); val = a.Substring(eq + 1).Trim().Trim('"'); }
                switch (name.ToLowerInvariant())
                {
                    case "s": case "silent": o.Silent = true; break;
                    case "dir": if (!string.IsNullOrEmpty(val)) { o.InstallDir = val; o.DirGiven = true; } break;
                    case "nostartup": o.Startup = false; break;
                    case "nodesktop": o.Desktop = false; break;
                    case "nostartmenu": o.StartMenu = false; break;
                    case "norun": o.Launch = false; break;
                    case "minimized": o.Minimized = true; break;
                    case "nowebview2": o.InstallWebView2 = false; break;
                    case "removedata": o.RemoveData = true; break;
                    case "_stage2": o.Stage2 = true; break;
                    case "lang": if (val == "fr" || val == "en") { L.Lang = val; o.LangGiven = true; } else o.BadArg = raw; break;
                    default: o.BadArg = raw; break;
                }
            }
            return o;
        }
    }

    // ---------------------------------------------------------------- Windows helpers
    internal static class Sys
    {
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mailflow";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Wv2Guid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

        public static void AttachParentConsole() { try { AttachConsole(-1); } catch (Exception) { } }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "Mailflow-Setup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n", Encoding.UTF8);
            }
            catch (Exception) { }
        }

        public static string Full(string dir) { return Path.GetFullPath(dir.Trim()).TrimEnd('\\', '/'); }

        public static bool SameFile(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        public static string ExistingInstallDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    string d = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "Mailflow.exe"))) return d;
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool WebView2Present()
        {
            string path = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + Wv2Guid;
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey k = hklm.OpenSubKey(path))
                        if (VersionOk(k)) return true;
                }
                catch (Exception) { }
            }
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\" + Wv2Guid)) if (VersionOk(k)) return true; }
            catch (Exception) { }
            return false;
        }

        private static bool VersionOk(RegistryKey k)
        {
            string pv = k == null ? null : k.GetValue("pv") as string;
            return !string.IsNullOrEmpty(pv) && pv != "0.0.0.0";
        }

        /// <summary>Downloads the Evergreen bootstrapper and runs it silently. Returns true when WebView2 is present afterwards.</summary>
        public static bool InstallWebView2()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string tmp = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
                using (WebClient wc = new WebClient()) wc.DownloadFile("https://go.microsoft.com/fwlink/p/?LinkId=2124703", tmp);
                using (Process p = Process.Start(new ProcessStartInfo(tmp, "/silent /install") { UseShellExecute = true }))
                {
                    p.WaitForExit(5 * 60 * 1000);
                }
                try { File.Delete(tmp); } catch (Exception) { }
            }
            catch (Exception ex) { Log("WebView2 install failed: " + ex.Message); }
            return WebView2Present();
        }

        /// <summary>Closes (gracefully, then kills) only the Mailflow.exe running from this install folder.</summary>
        public static void StopInstance(string dir)
        {
            string exe = Path.Combine(dir, "Mailflow.exe");
            foreach (Process p in Process.GetProcessesByName("Mailflow"))
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule.FileName; } catch (Exception) { }
                    if (path == null || !SameFile(path, exe)) continue;
                    try { p.CloseMainWindow(); } catch (Exception) { }
                    if (!p.WaitForExit(4000))
                    {
                        p.Kill();
                        p.WaitForExit(8000);
                    }
                }
                catch (Exception ex) { Log("StopInstance: " + ex.Message); }
                finally { p.Dispose(); }
            }
        }

        public static void ComSet(object o, string prop, object v) { o.GetType().InvokeMember(prop, BindingFlags.SetProperty, null, o, new object[] { v }); }

        public static void CreateShortcut(string lnk, string target, string args, string workDir, string icon, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new SetupException("WScript.Shell unavailable");
            object shell = Activator.CreateInstance(t);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnk));
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                try
                {
                    ComSet(sc, "TargetPath", target);
                    ComSet(sc, "Arguments", args ?? "");
                    ComSet(sc, "WorkingDirectory", workDir);
                    ComSet(sc, "IconLocation", icon);
                    ComSet(sc, "Description", desc);
                    sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                }
                finally { Marshal.FinalReleaseComObject(sc); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        public static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                try
                {
                    object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    try { return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string; }
                    finally { Marshal.FinalReleaseComObject(sc); }
                }
                finally { Marshal.FinalReleaseComObject(shell); }
            }
            catch (Exception) { return null; }
        }

        public static string StartMenuDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Mailflow"); } }
        public static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Mailflow.lnk"); } }
    }

    // ---------------------------------------------------------------- removal engine
    internal static class Remover
    {
        /// <summary>Removes the installation. Returns the items that could not be deleted. Uninstall.exe itself is left to the caller.</summary>
        public static List<string> Run(string dir, bool removeData)
        {
            List<string> failed = new List<string>();
            Sys.StopInstance(dir);

            // Shortcuts (only those pointing into this install folder).
            RemoveShortcutsIn(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), dir);
            RemoveShortcutsIn(Sys.StartMenuDir, dir);
            try { if (Directory.Exists(Sys.StartMenuDir) && Directory.GetFileSystemEntries(Sys.StartMenuDir).Length == 0) Directory.Delete(Sys.StartMenuDir); }
            catch (Exception) { }

            // Start-with-Windows entry and Add/Remove entry.
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(Sys.RunKey, true))
                {
                    string v = run == null ? null : run.GetValue("Mailflow") as string;
                    if (v != null && v.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) run.DeleteValue("Mailflow", false);
                }
                string loc = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Sys.UninstallKey)) loc = k == null ? null : k.GetValue("InstallLocation") as string;
                if (loc == null || Sys.SameFile(loc, dir)) Registry.CurrentUser.DeleteSubKeyTree(Sys.UninstallKey, false);
            }
            catch (Exception ex) { Sys.Log("registry cleanup: " + ex.Message); }

            // Program files: everything except data\ and the running Uninstall.exe.
            string dataDir = Path.Combine(dir, "data");
            string self = Path.Combine(dir, "Uninstall.exe");
            foreach (string f in Directory.GetFiles(dir)) { if (!Sys.SameFile(f, self)) DeleteFile(f, failed); }
            foreach (string d in Directory.GetDirectories(dir)) { if (!Sys.SameFile(d, dataDir)) DeleteDir(d, failed); }

            if (removeData)
            {
                DeleteDir(dataDir, failed);
                DeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailflow"), failed);
            }
            return failed;
        }

        internal static void RemoveShortcutsIn(string folder, string dir)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string lnk in Directory.GetFiles(folder, "*.lnk"))
                {
                    string t = Sys.ShortcutTarget(lnk);
                    if (t != null && t.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(lnk); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        private static void DeleteFile(string f, List<string> failed)
        {
            try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            catch (Exception) { failed.Add(f); }
        }

        private static void DeleteDir(string d, List<string> failed)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception) { failed.Add(d); }
        }
    }

#if !UNINSTALL
    // ---------------------------------------------------------------- install engine
    internal static class Installer
    {
        public static Stream Payload()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
            if (s == null) throw new SetupException(L.T("eNoPayload"));
            return s;
        }

        public static string ValidateDir(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Trim().Length == 0) throw new SetupException(L.T("eDirEmpty"));
            string dir;
            try { dir = Sys.Full(input); } catch (Exception) { throw new SetupException(L.T("eDirBad", input)); }
            if (!Path.IsPathRooted(dir)) throw new SetupException(L.T("eDirBad", input));
            if (Path.GetPathRoot(dir).TrimEnd('\\') == dir) throw new SetupException(L.T("eDirRoot", dir));
            if (Directory.Exists(dir) && !File.Exists(Path.Combine(dir, "Mailflow.exe")))
                foreach (string entry in Directory.GetFileSystemEntries(dir))
                    // a data folder left behind by an uninstall that kept the user data is fine
                    if (!string.Equals(Path.GetFileName(entry), "data", StringComparison.OrdinalIgnoreCase))
                        throw new SetupException(L.T("eDirNotEmpty", dir));
            return dir;
        }

        private static void DeleteTree(string d)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception) { }
        }

        private static void MoveEntry(string from, string to)
        {
            if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
        }

        /// <summary>Moves the current program files (not data\) to <paramref name="old"/> and the new ones in; puts the old ones back on failure.</summary>
        private static void Swap(string dir, string stage, string old)
        {
            string dataDir = Path.Combine(dir, "data");
            Directory.CreateDirectory(old);
            bool newFilesMoving = false;
            try
            {
                foreach (string e in Directory.GetFileSystemEntries(dir))
                    if (!Sys.SameFile(e, dataDir)) MoveEntry(e, Path.Combine(old, Path.GetFileName(e)));
                newFilesMoving = true; // from here dir holds only data\ and new files
                foreach (string e in Directory.GetFileSystemEntries(stage))
                    MoveEntry(e, Path.Combine(dir, Path.GetFileName(e)));
            }
            catch (Exception)
            {
                if (newFilesMoving)
                    foreach (string e in Directory.GetFileSystemEntries(dir))
                        if (!Sys.SameFile(e, dataDir)) { try { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } catch (Exception) { } }
                foreach (string e in Directory.GetFileSystemEntries(old))
                    { try { MoveEntry(e, Path.Combine(dir, Path.GetFileName(e))); } catch (Exception) { } }
                DeleteTree(stage); DeleteTree(old);
                throw;
            }
            DeleteTree(stage); DeleteTree(old);
        }

        public static void Install(Options o, Action<int, string> report, out bool wv2Missing)
        {
            wv2Missing = false;
            string dir = ValidateDir(o.InstallDir);
            try { DoInstall(o, dir, report, out wv2Missing); }
            catch (UnauthorizedAccessException ex) { throw new SetupException(L.T("eAccess", dir, ex.Message)); }
            catch (IOException ex) { throw new SetupException(L.T("eLocked", dir, ex.Message)); }
        }

        private static void DoInstall(Options o, string dir, Action<int, string> report, out bool wv2Missing)
        {
            // The new program is unpacked next to the install folder first, so a failure leaves the current install untouched.
            string stage = dir + ".new", old = dir + ".old";
            report(6, L.T("pClean"));
            Directory.CreateDirectory(dir);
            DeleteTree(stage); DeleteTree(old);

            string prefix = stage + "\\";
            using (ZipArchive zip = new ZipArchive(Payload(), ZipArchiveMode.Read))
            {
                int total = zip.Entries.Count, done = 0;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    done++;
                    string rel = e.FullName.Replace('/', '\\');
                    string dest = Path.GetFullPath(Path.Combine(stage, rel));
                    if (!dest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new SetupException(L.T("eBadPath", e.FullName));
                    if (e.Name.Length == 0) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (Stream src = e.Open())
                    using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst);
                    if (done % 5 == 0 || done == total) report(10 + (int)(done * 60L / total), L.T("pCopy", done, total));
                }
            }
            if (!File.Exists(Path.Combine(stage, "Mailflow.exe"))) { DeleteTree(stage); throw new SetupException(L.T("eNoExe")); }

            report(72, L.T("pStop"));
            Sys.StopInstance(dir);
            Swap(dir, stage, old);
            string exe = Path.Combine(dir, "Mailflow.exe");
            string uninst = Path.Combine(dir, "Uninstall.exe");
            if (!File.Exists(exe)) throw new SetupException(L.T("eNoExe"));

            report(82, L.T("pShort"));
            string desc = L.T("lnkDesc");
            if (o.Desktop) Sys.CreateShortcut(Sys.DesktopLnk, exe, "", dir, exe + ",0", desc);
            Remover.RemoveShortcutsIn(Sys.StartMenuDir, dir); // stale entries of a previous install (e.g. other language)
            if (o.StartMenu)
            {
                Sys.CreateShortcut(Path.Combine(Sys.StartMenuDir, "Mailflow.lnk"), exe, "", dir, exe + ",0", desc);
                if (File.Exists(uninst))
                    Sys.CreateShortcut(Path.Combine(Sys.StartMenuDir, L.T("lnkUninst") + ".lnk"), uninst, "", dir, exe + ",0", L.T("lnkUninst"));
            }

            report(88, L.T("pReg"));
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(Sys.RunKey))
            {
                if (o.Startup) run.SetValue("Mailflow", "\"" + exe + "\" --minimized");
                else run.DeleteValue("Mailflow", false);
            }
            long bytes = 0;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Sys.UninstallKey))
            {
                k.SetValue("DisplayName", "Mailflow");
                k.SetValue("DisplayVersion", BuildInfo.Version);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("UninstallString", "\"" + uninst + "\"");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /S");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("Language", L.Lang);
            }

            wv2Missing = !Sys.WebView2Present();
            if (wv2Missing && o.InstallWebView2)
            {
                report(92, L.T("pWv2"));
                wv2Missing = !Sys.InstallWebView2();
            }

            if (o.Launch && !wv2Missing)
            {
                report(98, L.T("pLaunch"));
                Process.Start(new ProcessStartInfo(exe, o.Minimized ? "--minimized" : "") { WorkingDirectory = dir, UseShellExecute = true });
            }
            report(100, L.T("pDone"));
        }
    }

    // ---------------------------------------------------------------- wizard
    internal sealed class SetupForm : Form
    {
        private readonly Options _o;
        private readonly string _existing;
        private readonly bool _wv2Missing;
        private int _page;
        private bool _busy, _ok, _failed, _wv2StillMissing;
        private string _error;

        private readonly Label _title = new Label(), _sub = new Label();
        private readonly LinkLabel _fr = new LinkLabel(), _en = new LinkLabel();
        private readonly Panel[] _pages = new Panel[6];
        private readonly Button _back = new Button(), _next = new Button(), _cancel = new Button();
        private readonly Label _welcome = new Label(), _welcomeUpd = new Label(), _dirLabel = new Label(), _dirHint = new Label(), _wv2Note = new Label(), _status = new Label(), _doneLbl = new Label();
        private readonly TextBox _license = new TextBox(), _dir = new TextBox();
        private readonly CheckBox _accept = new CheckBox(), _cDesktop = new CheckBox(), _cStart = new CheckBox(), _cStartup = new CheckBox(), _cLaunch = new CheckBox(), _cWv2 = new CheckBox();
        private readonly Button _browse = new Button();
        private readonly ProgressBar _bar = new ProgressBar();

        public bool Succeeded { get { return _ok; } }

        public SetupForm(Options o)
        {
            _o = o;
            _existing = Sys.ExistingInstallDir();
            if (_existing != null && !o.DirGiven) _o.InstallDir = _existing;
            _wv2Missing = !Sys.WebView2Present();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 420);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.White };
            _title.Font = new Font("Segoe UI", 12f, FontStyle.Bold); _title.Location = new Point(16, 8); _title.Size = new Size(450, 24);
            _sub.Location = new Point(16, 34); _sub.Size = new Size(470, 22); _sub.ForeColor = Color.FromArgb(75, 85, 99);
            _fr.Text = "FR"; _en.Text = "EN";
            _fr.Location = new Point(500, 10); _en.Location = new Point(534, 10); _fr.AutoSize = true; _en.AutoSize = true;
            _fr.LinkClicked += delegate { SetLang("fr"); };
            _en.LinkClicked += delegate { SetLang("en"); };
            header.Controls.AddRange(new Control[] { _title, _sub, _fr, _en });
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(243, 244, 246) };
            _back.Size = new Size(96, 28); _next.Size = new Size(104, 28); _cancel.Size = new Size(96, 28);
            _back.Location = new Point(250, 12); _next.Location = new Point(350, 12); _cancel.Location = new Point(474, 12);
            _back.Click += delegate { Go(_page - 1); };
            _next.Click += OnNext;
            _cancel.Click += delegate { Close(); };
            bottom.Controls.AddRange(new Control[] { _back, _next, _cancel });
            Controls.Add(bottom); Controls.Add(header);
            AcceptButton = _next;

            BuildPages();
            _accept.CheckedChanged += delegate { UpdateButtons(); };
            _dir.Text = _o.InstallDir;
            _cDesktop.Checked = _o.Desktop; _cStart.Checked = _o.StartMenu; _cStartup.Checked = _o.Startup; _cLaunch.Checked = _o.Launch;
            _cWv2.Checked = _o.InstallWebView2;
            Go(0);
        }

        private Panel NewPage(int i)
        {
            Panel p = new Panel { Location = new Point(0, 65), Size = new Size(580, 303), Visible = false };
            _pages[i] = p; Controls.Add(p); return p;
        }

        private void BuildPages()
        {
            Panel p0 = NewPage(0);
            _welcome.Location = new Point(24, 20); _welcome.Size = new Size(530, 150);
            _welcomeUpd.Location = new Point(24, 190); _welcomeUpd.Size = new Size(530, 50); _welcomeUpd.ForeColor = Color.FromArgb(3, 105, 161);
            p0.Controls.AddRange(new Control[] { _welcome, _welcomeUpd });

            Panel p1 = NewPage(1);
            _license.Multiline = true; _license.ReadOnly = true; _license.ScrollBars = ScrollBars.Vertical; _license.BackColor = Color.White;
            _license.Location = new Point(24, 12); _license.Size = new Size(532, 232); _license.Font = new Font("Consolas", 9f);
            try
            {
                using (ZipArchive z = new ZipArchive(Installer.Payload(), ZipArchiveMode.Read))
                using (StreamReader r = new StreamReader(z.GetEntry("LICENSE").Open(), Encoding.UTF8))
                    _license.Text = r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
            catch (Exception) { _license.Text = "MIT License"; }
            _accept.Location = new Point(24, 256); _accept.Size = new Size(530, 24);
            p1.Controls.AddRange(new Control[] { _license, _accept });

            Panel p2 = NewPage(2);
            _dirLabel.Location = new Point(24, 24); _dirLabel.Size = new Size(530, 20);
            _dir.Location = new Point(24, 48); _dir.Size = new Size(430, 24);
            _browse.Location = new Point(462, 46); _browse.Size = new Size(94, 28);
            _browse.Click += OnBrowse;
            _dirHint.Location = new Point(24, 90); _dirHint.Size = new Size(530, 60); _dirHint.ForeColor = Color.FromArgb(75, 85, 99);
            p2.Controls.AddRange(new Control[] { _dirLabel, _dir, _browse, _dirHint });

            Panel p3 = NewPage(3);
            int y = 20;
            foreach (CheckBox c in new CheckBox[] { _cDesktop, _cStart, _cStartup, _cLaunch })
            { c.Location = new Point(24, y); c.Size = new Size(530, 24); p3.Controls.Add(c); y += 30; }
            if (_wv2Missing)
            {
                _wv2Note.Location = new Point(24, y + 12); _wv2Note.Size = new Size(530, 40); _wv2Note.ForeColor = Color.FromArgb(180, 83, 9);
                _cWv2.Location = new Point(24, y + 56); _cWv2.Size = new Size(530, 24);
                p3.Controls.AddRange(new Control[] { _wv2Note, _cWv2 });
            }

            Panel p4 = NewPage(4);
            _bar.Location = new Point(24, 60); _bar.Size = new Size(530, 22); _bar.Maximum = 100;
            _status.Location = new Point(24, 92); _status.Size = new Size(530, 24);
            p4.Controls.AddRange(new Control[] { _bar, _status });

            Panel p5 = NewPage(5);
            _doneLbl.Location = new Point(24, 20); _doneLbl.Size = new Size(530, 250);
            p5.Controls.Add(_doneLbl);
        }

        private void SetLang(string lang)
        {
            if (_busy) return;
            L.Lang = lang; ApplyText();
        }

        private void ApplyText()
        {
            string v = BuildInfo.Version;
            Text = L.T("setupTitle") + " " + v;
            _fr.Font = new Font(Font, L.Lang == "fr" ? FontStyle.Bold : FontStyle.Regular);
            _en.Font = new Font(Font, L.Lang == "en" ? FontStyle.Bold : FontStyle.Regular);
            _welcome.Text = L.T("welcomeBody");
            _welcomeUpd.Text = _existing == null ? "" : L.T("welcomeUpd", _existing);
            _accept.Text = L.T("licAccept");
            _dirLabel.Text = L.T("dirLabel"); _dirHint.Text = L.T("dirHint"); _browse.Text = L.T("browse");
            _cDesktop.Text = L.T("optDesktop"); _cStart.Text = L.T("optStart"); _cStartup.Text = L.T("optStartup"); _cLaunch.Text = L.T("optLaunch");
            _wv2Note.Text = L.T("wv2Missing"); _cWv2.Text = L.T("optWv2");
            _back.Text = L.T("back"); _cancel.Text = L.T("cancel");
            if (_page == 5) ShowDone();
            string[] h = { "welcomeH", "licH", "dirH", "optH", "progH", "doneH" };
            string[] s = { "welcomeS", "licS", "dirS", "optS", "progS", "doneS" };
            bool fail = _page == 5 && _failed;
            _title.Text = fail ? L.T("failH") : L.T(h[_page]);
            _sub.Text = fail ? L.T("failS") : (_page == 0 ? L.T("welcomeS", v) : L.T(s[_page]));
            UpdateButtons();
        }

        private void ShowDone()
        {
            if (_failed) { _doneLbl.Text = _error; _doneLbl.ForeColor = Color.FromArgb(185, 28, 28); return; }
            _doneLbl.ForeColor = SystemColors.ControlText;
            string t = L.T("doneBody", BuildInfo.Version, Sys.Full(_o.InstallDir));
            if (_wv2StillMissing) t += "\r\n\r\n" + L.T(_o.InstallWebView2 ? "wv2Manual" : "wv2Warn");
            _doneLbl.Text = t;
        }

        private void Go(int page)
        {
            _page = Math.Max(0, Math.Min(5, page));
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _page;
            ApplyText();
        }

        private void UpdateButtons()
        {
            _back.Visible = _page > 0 && _page < 4;
            _cancel.Visible = _page < 5;
            _cancel.Enabled = !_busy;
            _next.Enabled = !_busy && (_page != 1 || _accept.Checked);
            _next.Text = _page == 3 ? (_existing != null ? L.T("upgrade") : L.T("install")) : (_page == 5 ? L.T("finish") : L.T("next"));
            _next.Visible = _page != 4;
        }

        private void OnBrowse(object s, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("browseDesc"); d.SelectedPath = _dir.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = d.SelectedPath;
            }
        }

        private void OnNext(object s, EventArgs e)
        {
            if (_page == 5) { Close(); return; }
            if (_page == 2)
            {
                try { _o.InstallDir = Installer.ValidateDir(_dir.Text); }
                catch (SetupException ex) { MessageBox.Show(this, ex.Message, L.T("setupTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            if (_page == 3) { StartInstall(); return; }
            Go(_page + 1);
        }

        private void StartInstall()
        {
            _o.Desktop = _cDesktop.Checked; _o.StartMenu = _cStart.Checked; _o.Startup = _cStartup.Checked;
            _o.Launch = _cLaunch.Checked; _o.InstallWebView2 = _cWv2.Checked;
            _busy = true;
            Go(4);
            Thread t = new Thread(delegate()
            {
                string err = null; bool wv2 = false;
                try
                {
                    Installer.Install(_o, delegate(int pct, string msg)
                    {
                        BeginInvoke((MethodInvoker)delegate { _bar.Value = Math.Max(0, Math.Min(100, pct)); _status.Text = msg; });
                    }, out wv2);
                }
                catch (SetupException ex) { err = ex.Message; }
                catch (Exception ex) { err = L.T("eGeneric", ex.Message); }
                BeginInvoke((MethodInvoker)delegate
                {
                    _busy = false;
                    _failed = err != null; _ok = !_failed; _error = err;
                    _wv2StillMissing = wv2;
                    if (err != null) Sys.Log(err);
                    Go(5);
                });
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) { e.Cancel = true; return; }
            if (_page < 5 && e.CloseReason == CloseReason.UserClosing &&
                MessageBox.Show(this, L.T("cancelQ"), L.T("setupTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options o = Options.Parse(args);
            if (o.BadArg != null)
            {
                string m = L.T("eUsage", o.BadArg);
                Sys.Log(m);
                if (o.Silent) { Sys.AttachParentConsole(); Console.Error.WriteLine(m); }
                else { Application.EnableVisualStyles(); MessageBox.Show(m, L.T("setupTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                return 2;
            }
            if (o.Silent)
            {
                Sys.AttachParentConsole();
                try
                {
                    string existing = Sys.ExistingInstallDir();
                    if (existing != null && !o.DirGiven) o.InstallDir = existing;
                    bool wv2;
                    Installer.Install(o, delegate(int p, string msg) { Sys.Log(msg); }, out wv2);
                    if (wv2) { Console.Error.WriteLine(L.T("wv2Warn")); Sys.Log(L.T("wv2Warn")); }
                    Sys.Log("Installed " + BuildInfo.Version + " to " + o.InstallDir);
                    return 0;
                }
                catch (Exception ex)
                {
                    string m = ex is SetupException ? ex.Message : L.T("eGeneric", ex.Message);
                    Console.Error.WriteLine(m); Sys.Log(m);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (SetupForm f = new SetupForm(o)) { Application.Run(f); return f.Succeeded ? 0 : 1; }
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
            if (o.BadArg != null) { ShowError(o, L.T("eUsage", o.BadArg)); return 2; }
            string self = Application.ExecutablePath;
            string dir = Sys.Full(o.DirGiven ? o.InstallDir : Path.GetDirectoryName(self));

            if (!o.Stage2)
            {
                if (!File.Exists(Path.Combine(dir, "Mailflow.exe")) && !File.Exists(Path.Combine(dir, "Uninstall.exe")))
                {
                    ShowError(o, L.T("uNotFound", dir)); return 1;
                }
                // Run from a temp copy so that Uninstall.exe itself can be deleted afterwards.
                string tmp = Path.Combine(Path.GetTempPath(), "mailflow-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(self, tmp, true);
                List<string> pass = new List<string>(args);
                pass.Add("/_stage2");
                if (!o.DirGiven) pass.Add("/dir=" + dir);
                StringBuilder sb = new StringBuilder();
                foreach (string a in pass) sb.Append('"').Append(a.Replace("\"", "")).Append("\" ");
                int code;
                using (Process p = Process.Start(new ProcessStartInfo(tmp, sb.ToString()) { UseShellExecute = false }))
                {
                    p.WaitForExit(); code = p.ExitCode;
                }
                // Uninstalled (0) or partly (1): also remove Uninstall.exe and the folder if empty (rmdir keeps a non-empty data folder).
                bool removed = code == 0 || code == 1;
                string cmd = "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + tmp + "\""
                    + (removed ? " \"" + Path.Combine(dir, "Uninstall.exe") + "\" & rmdir \"" + dir + "\"" : "");
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                return code == 3 ? 0 : code;
            }

            // Stage 2 (running from the temp copy).
            try
            {
                if (!o.LangGiven)
                {
                    string lang = null;
                    try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Sys.UninstallKey)) lang = k == null ? null : k.GetValue("Language") as string; } catch (Exception) { }
                    if (lang == "fr" || lang == "en") L.Lang = lang;
                }
                bool removeData = o.RemoveData;
                string dataDir = Path.Combine(dir, "data");
                if (!o.Silent)
                {
                    Application.EnableVisualStyles();
                    if (MessageBox.Show(L.T("uQ", dir), L.T("uninstTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 3;
                    if (Directory.Exists(dataDir))
                        removeData = MessageBox.Show(L.T("uData"), L.T("uninstTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                }
                List<string> failed = Remover.Run(dir, removeData);
                string msg = L.T("uDone");
                if (!removeData && Directory.Exists(dataDir)) msg += "\r\n\r\n" + L.T("uKept", dataDir);
                if (failed.Count > 0) msg += "\r\n\r\n" + L.T("uPartial", string.Join("\r\n", failed.ToArray()));
                Sys.Log("Uninstalled " + dir + (removeData ? " (data removed)" : " (data kept)"));
                if (o.Silent) Console.WriteLine(msg);
                else MessageBox.Show(msg, L.T("uninstTitle"), MessageBoxButtons.OK, failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                return failed.Count > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                ShowError(o, L.T("eGeneric", ex.Message)); return 4;
            }
        }

        private static void ShowError(Options o, string m)
        {
            Sys.Log(m);
            if (o.Silent) Console.Error.WriteLine(m);
            else { Application.EnableVisualStyles(); MessageBox.Show(m, L.T("uninstTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
#endif
}
