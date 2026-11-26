using Mailflow.Application.Processing;
using Mailflow.Web;
using Mailflow.Web.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Mailflow.Desktop;

/// <summary>Owns the tray icon, the window and (unless attached to another process) the web host.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Uri _url;
    private readonly LaunchOptions _options;
    private readonly MainForm _form;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _runNow = new("Lancer le traitement maintenant");
    private readonly ToolStripMenuItem _pause = new("Mettre en pause");
    private WebApplication? _host;   // null when attached to a server running in another process
    private bool _balloonShown;
    private bool _exiting;

    public TrayApp(Uri url, LaunchOptions options, EventWaitHandle showEvent)
    {
        _url = url;
        _options = options;
        _form = new MainForm(url);
        _form.FormClosing += OnFormClosing;

        var open = new ToolStripMenuItem("Ouvrir Mailflow");
        open.Click += (_, _) => ShowWindow();
        _runNow.Click += (_, _) => _host?.Services.GetRequiredService<RunNowSignal>().Request();
        _pause.Click += (_, _) =>
        {
            var state = _host?.Services.GetRequiredService<WorkerState>();
            if (state is null) return;
            if (state.IsPaused) state.Resume(); else state.Pause();
        };
        var quit = new ToolStripMenuItem("Quitter");
        quit.Click += async (_, _) => await ExitAsync();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([open, new ToolStripSeparator(), _runNow, _pause, new ToolStripSeparator(), quit]);
        menu.Opening += (_, _) =>
        {
            // Attached to another process: it owns the worker, so these stay disabled.
            _runNow.Enabled = _pause.Enabled = _host is not null;
            _pause.Text = _host?.Services.GetRequiredService<WorkerState>().IsPaused == true ? "Reprendre" : "Mettre en pause";
        };

        _tray = new NotifyIcon { Icon = Desktop.MainForm.AppIcon, Text = "Mailflow", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();

        // Another launch asks us to show the window.
        new Thread(() =>
        {
            while (showEvent.WaitOne())
            {
                if (_exiting) return;
                try { _form.BeginInvoke(ShowWindow); } catch (Exception) { return; }
            }
        }) { IsBackground = true }.Start();

        _form.HandleCreated += async (_, _) => await StartAsync();
        _form.CreateControl();   // creates the handle without showing the window
        if (!options.Minimized) ShowWindow();
    }

    public int ExitCode { get; private set; }

    private async Task StartAsync()
    {
        var outcome = await StartupFlow.RunAsync(_url.Port, ct => AttachOrStart.HttpProbeAsync(_url, ct),
            () => PortOwner.GetListenerImagePath(_url.Port), Environment.ProcessPath,
            async () => _host = await Task.Run(() => MailflowHost.StartAsync(_options.HostArgs, CancellationToken.None,
                services => services.AddSingleton<IFailureNotifier>(new ToastNotifier(ShowToast)))),
            Dialogs.ShowError);
        ExitCode = StartupFlow.ExitCode(outcome);
        if (ExitCode != 0)
        {
            _form.Hide();
            await ExitAsync();
            return;
        }
        await _form.InitializeAsync();
    }

    /// <summary>Called from worker threads: the balloon is raised on the UI thread.</summary>
    private void ShowToast(string title, string message)
    {
        try { _form.BeginInvoke(() => _tray.ShowBalloonTip(10000, title, message, ToolTipIcon.Error)); }
        catch (Exception) { /* window already gone */ }
    }

    private void ShowWindow()
    {
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason is CloseReason.WindowsShutDown) return;
        e.Cancel = true;
        _form.Hide();
        if (_balloonShown) return;
        _balloonShown = true;
        _tray.ShowBalloonTip(3000, "Mailflow", "Mailflow continue de fonctionner en arrière-plan", ToolTipIcon.Info);
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Visible = false;
        _tray.Dispose();
        _form.SaveState();
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                await _host.DisposeAsync();
            }
        }
        catch (Exception) { /* exiting anyway */ }
        _form.Dispose();
        ExitThread();
    }
}
