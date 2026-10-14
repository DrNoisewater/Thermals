using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Thermals.Core;
using Thermals.Native;

namespace Thermals.UI;

/// <summary>
/// Application root: owns settings, the sensor hub, the overlay, the tray icon and the main window.
/// Lives on the UI thread; the hub's background thread hands snapshots over via <see cref="OnHubUpdated"/>.
/// </summary>
internal sealed class AppHost : ApplicationContext
{
    public const string ShowMessageName = "Thermals.ShowMainWindow";
    private const int HotkeyToggleOverlay = 1;

    public AppSettings Settings { get; private set; }
    public SensorHub Hub { get; }
    public CsvLogger Logger { get; } = new();
    public bool IsElevated { get; } = Platform.IsElevated();
    public bool PawnIoInstalled { get; } = Platform.IsPawnIoInstalled();
    public bool HotkeyRegistered { get; private set; }

    private readonly Control _marshal = new();
    private readonly OverlayForm _overlay;
    private readonly NotifyIcon _tray;
    private readonly MessageWindow _messages;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _trayOverlayItem, _trayLogItem;
    private MainForm? _main;
    private int _uiPostPending;
    private bool _exiting, _trayHintShown;
    private string _trayText = "";
    private Icon? _trayValueIcon;

    public AppHost(bool launchedMinimized)
    {
        _ = _marshal.Handle;   // create the handle on the UI thread so BeginInvoke can marshal to it

        Settings = AppSettings.Load();
        SensorFormat.Fahrenheit = Settings.Fahrenheit;
        _appIcon = LoadAppIcon();

        Hub = new SensorHub { IntervalMs = Settings.PollIntervalMs, ThrottleDrives = Settings.ThrottleDrives };
        Hub.ApplyHardwareGroups(Settings);
        Hub.SetFocusSensors(Settings.Overlay.Items.Select(i => i.SensorId));
        Hub.Updated += OnHubUpdated;
        Logger.Failed += msg => _marshal.BeginInvoke(new Action(() =>
        {
            UpdateHubMode();
            _tray.ShowBalloonTip(5000, "Logging stopped", msg, ToolTipIcon.Warning);
            _main?.RefreshMenus();
        }));

        _overlay = new OverlayForm(this);

        _trayOverlayItem = new ToolStripMenuItem("Show overlay", null, (_, _) => ToggleOverlay());
        _trayLogItem = new ToolStripMenuItem("Log to CSV", null, (_, _) => ToggleLogging());
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Thermals", null, (_, _) => ShowMain());
        menu.Items.Add(open);
        menu.Items.Add(_trayOverlayItem);
        menu.Items.Add(_trayLogItem);
        menu.Items.Add("Reset min / max", null, (_, _) => Hub.ResetStats());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings(null));
        menu.Items.Add("About Thermals", null, (_, _) => Dialogs.About(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        Theme.StyleMenu(menu);
        menu.Opening += (_, _) =>
        {
            open.Font = Theme.Font(menu.DeviceDpi, 9f, FontStyle.Regular, Theme.SemiboldFamily);
            _trayOverlayItem.Checked = Settings.Overlay.Enabled;
            _trayLogItem.Checked = Logger.IsRunning;
        };

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Thermals",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        _messages = new MessageWindow();
        _messages.ShowRequested += ShowMain;
        _messages.HotkeyPressed += id => { if (id == HotkeyToggleOverlay) ToggleOverlay(); };
        HotkeyRegistered = _messages.RegisterHotkey(HotkeyToggleOverlay,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Keys.O);

        Hub.Start();
        if (Settings.Overlay.Enabled) _overlay.ShowOverlay();

        if (!launchedMinimized && !Settings.StartMinimized) ShowMain();
        UpdateHubMode();
    }

    // ------------------------------------------------------------------ data flow

    /// <summary>Poll thread. Logs, then posts at most one pending UI refresh at a time.</summary>
    private void OnHubUpdated(SensorSnapshot snapshot)
    {
        Logger.Write(snapshot);
        if (Interlocked.Exchange(ref _uiPostPending, 1) == 1) return;
        try
        {
            _marshal.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _uiPostPending, 0);
                if (!_exiting && Hub.Latest is { } latest) Dispatch(latest);
            }));
        }
        catch (Exception)
        {
            // Handle gone during shutdown.
            Interlocked.Exchange(ref _uiPostPending, 0);
        }
    }

    private void Dispatch(SensorSnapshot s)
    {
        if (!Settings.Overlay.ItemsInitialized && s.All.Count > 0)
        {
            Settings.Overlay.Items = DefaultOverlay.Pick(s);
            Settings.Overlay.ItemsInitialized = true;
            Settings.Save();
            Hub.SetFocusSensors(Settings.Overlay.Items.Select(i => i.SensorId));
        }

        if (_main is { IsDisposed: false, Visible: true } && _main.WindowState != FormWindowState.Minimized)
            _main.ApplySnapshot(s);

        if (_overlay.Visible) _overlay.ApplySnapshot(s);
        UpdateTray(s);
    }

    /// <summary>
    /// Full polling only while the sensor list is on screen or logging is active;
    /// otherwise just the hardware the overlay/tray needs.
    /// </summary>
    public void UpdateHubMode()
    {
        bool mainVisible = _main is { IsDisposed: false, Visible: true } && _main.WindowState != FormWindowState.Minimized;
        bool full = mainVisible || Logger.IsRunning;
        if (full && !Hub.FullUpdate)
        {
            Hub.FullUpdate = true;
            Hub.PollNow();
        }
        Hub.FullUpdate = full;
    }

    // ------------------------------------------------------------------ actions

    public void ShowMain()
    {
        if (_main == null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            _main.VisibleChanged += (_, _) => UpdateHubMode();
            _main.Resize += (_, _) => UpdateHubMode();
        }
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        UpdateHubMode();
        if (Hub.Latest is { } s) _main.ApplySnapshot(s);
    }

    /// <summary>Called by the main window when the user closes it.</summary>
    public void MainWindowHidden()
    {
        UpdateHubMode();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "Thermals is still running",
                "The overlay and logging keep working. Right-click the tray icon to exit.", ToolTipIcon.None);
        }
    }

    public void ToggleOverlay()
    {
        Settings.Overlay.Enabled = !Settings.Overlay.Enabled;
        if (Settings.Overlay.Enabled)
        {
            _overlay.ShowOverlay();
            if (Hub.Latest is { } s) _overlay.ApplySnapshot(s);
        }
        else
        {
            _overlay.Hide();
        }
        Settings.Save();
        _main?.RefreshMenus();
    }

    public void ToggleLogging()
    {
        if (Logger.IsRunning) Logger.Stop();
        else Logger.Start(string.IsNullOrWhiteSpace(Settings.LogDirectory) ? AppPaths.DefaultLogDir : Settings.LogDirectory);
        UpdateHubMode();
        _main?.RefreshMenus();
    }

    public bool IsOverlayItem(string sensorId) => Settings.Overlay.Items.Any(i => i.SensorId == sensorId);

    public void SetOverlayItem(SensorReading r, bool pinned)
    {
        var items = Settings.Overlay.Items;
        if (pinned && !IsOverlayItem(r.Id))
            items.Add(new OverlayItem { SensorId = r.Id, Label = DefaultOverlay.AutoLabel(r) });
        else if (!pinned)
            items.RemoveAll(i => i.SensorId == r.Id);
        OverlayItemsChanged();
    }

    public void OverlayItemsChanged()
    {
        Settings.Overlay.ItemsInitialized = true;
        Settings.Save();
        Hub.SetFocusSensors(Settings.Overlay.Items.Select(i => i.SensorId));
        _overlay.ResetLayout();
        if (Hub.Latest is { } s) Dispatch(s);
    }

    public void ShowSettings(IWin32Window? owner)
    {
        using var dlg = new SettingsForm(this, Settings.Clone());
        if ((owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == DialogResult.OK)
            ApplySettings(dlg.Result);
    }

    public void ApplySettings(AppSettings s)
    {
        Settings = s;
        SensorFormat.Fahrenheit = s.Fahrenheit;
        Hub.IntervalMs = s.PollIntervalMs;
        Hub.ThrottleDrives = s.ThrottleDrives;
        Hub.ApplyHardwareGroups(s);
        Hub.SetFocusSensors(s.Overlay.Items.Select(i => i.SensorId));
        s.Save();

        if (s.Overlay.Enabled) _overlay.ShowOverlay(); else _overlay.Hide();
        _overlay.ResetLayout();
        _lastTrayValue = null;
        if (!s.TrayShowsValue) SetTrayIcon(null);
        _main?.SettingsChanged();
        if (Hub.Latest is { } snap) Dispatch(snap);
    }

    /// <summary>Shows unsaved overlay settings live while the settings dialog is open; null restores.</summary>
    public void PreviewOverlay(OverlaySettings? preview)
    {
        _overlay.Preview = preview;
        bool show = (preview ?? Settings.Overlay).Enabled;
        if (show) _overlay.ShowOverlay(); else _overlay.Hide();
        _overlay.ResetLayout();
        if (show && Hub.Latest is { } s) _overlay.ApplySnapshot(s);
    }

    public void SaveWindowBounds(Rectangle r)
    {
        Settings.WindowX = r.X;
        Settings.WindowY = r.Y;
        Settings.WindowW = r.Width;
        Settings.WindowH = r.Height;
        Settings.Save();
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            if (_main is { IsDisposed: false }) _main.CloseForExit();
            _overlay.Close();
            _tray.Visible = false;
            _messages.Dispose();
            Logger.Stop();
            Hub.Dispose();
            Settings.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Shutdown error", ex);
        }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _overlay.Dispose();
            _marshal.Dispose();
            _trayValueIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ tray

    private string? _lastTrayValue;

    private void UpdateTray(SensorSnapshot s)
    {
        // Tooltip: first few overlay values.
        var parts = new List<string>();
        foreach (var item in Settings.Overlay.Items.Take(4))
            if (s.ById.TryGetValue(item.SensorId, out var r))
                parts.Add($"{item.Label} {SensorFormat.FormatCompact(r.Type, r.Value)}");
        string text = parts.Count > 0 ? "Thermals\n" + string.Join("\n", parts) : "Thermals";
        if (text.Length > 63) text = text[..63];
        if (text != _trayText)
        {
            _trayText = text;
            _tray.Text = text;
        }

        if (!Settings.TrayShowsValue) return;

        // Icon: the first overlay item, as a bare number.
        string? value = null;
        var first = Settings.Overlay.Items.FirstOrDefault();
        if (first != null && s.ById.TryGetValue(first.SensorId, out var fr) && fr.Value is float v && !float.IsNaN(v))
        {
            double d = SensorFormat.ToDisplay(fr.Type, v);
            value = Math.Round(d).ToString("0");
            if (value.Length > 3) value = null;
        }
        if (value == _lastTrayValue) return;
        _lastTrayValue = value;
        SetTrayIcon(value);
    }

    private void SetTrayIcon(string? value)
    {
        var old = _trayValueIcon;
        if (value == null)
        {
            _tray.Icon = _appIcon;
            _trayValueIcon = null;
        }
        else
        {
            _trayValueIcon = RenderTrayIcon(value);
            _tray.Icon = _trayValueIcon;
        }
        if (old != null)
        {
            NativeMethods.DestroyIcon(old.Handle);
            old.Dispose();
        }
    }

    private static Icon RenderTrayIcon(string text)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var bg = new SolidBrush(Color.FromArgb(235, 24, 26, 32)))
            using (var path = RoundedRect(new RectangleF(0, 0, size, size), size * 0.2f))
                g.FillPath(bg, path);

            float em = text.Length switch { 1 => 0.78f, 2 => 0.70f, _ => 0.50f } * size;
            using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var brush = new SolidBrush(Color.White);
            g.DrawString(text, font, brush, new RectangleF(-1, 0, size + 2, size + 1), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Icon LoadAppIcon()
    {
        try
        {
            using var stream = typeof(AppHost).Assembly.GetManifestResourceStream("thermals.ico");
            if (stream != null) return new Icon(stream);
        }
        catch (Exception ex)
        {
            Log.Error("Icon load failed", ex);
        }
        return SystemIcons.Application;
    }

    // ------------------------------------------------------------------ message window

    /// <summary>Hidden top-level window: receives global hotkeys and "show" requests from a second instance.</summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly int _showMessage = NativeMethods.RegisterWindowMessage(ShowMessageName);
        private readonly List<int> _hotkeys = new();

        public event Action? ShowRequested;
        public event Action<int>? HotkeyPressed;

        public MessageWindow() => CreateHandle(new CreateParams { Caption = "Thermals.MessageWindow" });

        public bool RegisterHotkey(int id, uint modifiers, Keys key)
        {
            if (!NativeMethods.RegisterHotKey(Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key)) return false;
            _hotkeys.Add(id);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32());
            else if (m.Msg == _showMessage && _showMessage != 0) ShowRequested?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in _hotkeys) NativeMethods.UnregisterHotKey(Handle, id);
            _hotkeys.Clear();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
