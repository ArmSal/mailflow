using System.Diagnostics;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Mailflow.Desktop;

internal sealed class MainForm : Form
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailflow");
    private static readonly string StatePath = Path.Combine(DataDir, "window.json");

    private readonly Uri _origin;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _loading = new()
    {
        Text = "Chargement de Mailflow…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 14f), ForeColor = Color.FromArgb(75, 85, 99),
    };

    public static Icon AppIcon { get; } = LoadIcon();

    public MainForm(Uri origin)
    {
        _origin = origin;
        Text = "Mailflow";
        Icon = AppIcon;
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        Size = new Size(1280, 800);
        Controls.Add(_web);
        Controls.Add(_loading);

        var saved = Desktop.WindowState.Load(StatePath);
        if (saved is not null)
        {
            var bounds = new Rectangle(saved.X, saved.Y, Math.Max(saved.Width, MinimumSize.Width), Math.Max(saved.Height, MinimumSize.Height));
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            if (saved.Maximized) WindowState = FormWindowState.Maximized;
        }
    }

    private static Icon LoadIcon()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("mailflow.ico")!;
        return new Icon(s);
    }

    public void SaveState()
    {
        var max = WindowState == FormWindowState.Maximized;
        var b = max || WindowState == FormWindowState.Minimized ? RestoreBounds : Bounds;
        new Desktop.WindowState(b.X, b.Y, b.Width, b.Height, max).Save(StatePath);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveState();
        base.OnFormClosing(e);
    }

    public async Task InitializeAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception)
        {
            var answer = MessageBox.Show(
                "Le composant Microsoft Edge WebView2 est introuvable sur cet ordinateur, la fenêtre ne peut pas s'afficher.\n\n" +
                "Voulez-vous ouvrir le tableau de bord dans votre navigateur par défaut ?",
                "Mailflow", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) OpenExternal(_origin.ToString());
            return;
        }

        var core = _web.CoreWebView2;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // no print preview, view-source or reload shortcuts
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.NavigationStarting += (_, e) =>
        {
            if (NavigationPolicy.IsAllowed(e.Uri, _origin)) return;
            if (!e.IsUserInitiated && e.Uri.StartsWith("data:text/html", StringComparison.Ordinal)) return; // our error page
            e.Cancel = true;
            if (NavigationPolicy.IsExternalWeb(e.Uri, _origin)) OpenExternal(e.Uri);
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (NavigationPolicy.IsAllowed(e.Uri, _origin)) core.Navigate(e.Uri);
            else if (NavigationPolicy.IsExternalWeb(e.Uri, _origin)) OpenExternal(e.Uri);
        };
        core.WebMessageReceived += (_, e) =>
        {
            if (e.TryGetWebMessageAsString() == "retry") core.Navigate(_origin.ToString());
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                // The server answered with an error page (404, 500...): say so, instead of claiming it is unreachable.
                core.NavigateToString(e.HttpStatusCode >= 400 ? HttpErrorPage(e.HttpStatusCode) : ErrorPage);
                return;
            }
            _web.Visible = true;
            _loading.Visible = false;
        };
        core.Navigate(_origin.ToString());
    }

    private static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser available */ }
    }

    private static string HttpErrorPage(int status) => $$"""
        <!doctype html><html lang="fr"><meta charset="utf-8"><body style="font-family:Segoe UI,sans-serif;text-align:center;margin-top:20vh;color:#374151">
        <h2>{{(status == 404 ? "Page introuvable" : "Le serveur a renvoyé une erreur")}}</h2>
        <p>Code {{status}}.</p>
        <button onclick="window.chrome.webview.postMessage('retry')" style="font-size:16px;padding:8px 20px;cursor:pointer">Retour à l'accueil</button>
        </body></html>
        """;

    private const string ErrorPage = """
        <!doctype html><html lang="fr"><meta charset="utf-8"><body style="font-family:Segoe UI,sans-serif;text-align:center;margin-top:20vh;color:#374151">
        <h2>Mailflow n'est pas joignable</h2>
        <p>Le serveur local ne répond pas pour le moment.</p>
        <button onclick="window.chrome.webview.postMessage('retry')" style="font-size:16px;padding:8px 20px;cursor:pointer">Réessayer</button>
        </body></html>
        """;
}
