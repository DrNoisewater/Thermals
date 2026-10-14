using Thermals.Core;

namespace Thermals.UI;

internal sealed class SettingsForm : ThemedForm
{
    private readonly AppHost _host;
    public AppSettings Result { get; }

    private readonly Strip _header = new() { Dock = DockStyle.Top, BottomLine = true };
    private readonly Strip _footer = new() { Dock = DockStyle.Bottom, TopLine = true, Fill = Theme.Chrome };
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
    private readonly TabPills _tabs = new();
    private readonly FormPage _general = new() { Dock = DockStyle.Fill };
    private readonly FormPage _hardware = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly FormPage _overlay = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly FormPage _items = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly FlatButton _save = new() { Text = "Save", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK };
    private readonly FlatButton _cancel = new() { Text = "Cancel", Kind = ButtonKind.Secondary, DialogResult = DialogResult.Cancel };

    // General
    private readonly NumberBox _interval = new() { Minimum = 250, Maximum = 10000, Increment = 250, Unit = "ms" };
    private readonly ChoiceBox _unit = new();
    private readonly ToggleSwitch _minToTray = new(), _trayValue = new(), _autostart = new(), _startHidden = new();
    private readonly FolderPicker _logDir = new();

    // Hardware
    private readonly ToggleSwitch[] _groupToggles;
    private readonly ToggleSwitch _throttle = new();

    // Overlay
    private readonly ToggleSwitch _overlayOn = new(), _thresholds = new();
    private readonly CornerPicker _corner = new();
    private readonly ChoiceBox _monitor = new(), _font = new();
    private readonly NumberBox _margin = new() { Minimum = 0, Maximum = 500, Increment = 2, Unit = "px" };
    private readonly NumberBox _fontSize = new() { Minimum = 6, Maximum = 48, Increment = 0.5m, DecimalPlaces = 1, Unit = "pt" };
    private readonly Slider _background = new() { Minimum = 0, Maximum = 255, Formatter = v => $"{v * 100 / 255}%" };
    private readonly SwatchPicker _color = new();
    private readonly ThresholdPair _limits = new();

    // Overlay items
    private readonly SensorGrid _itemList = new() { MultiSelect = false };
    private readonly Strip _itemsEditor = new();
    private readonly FlatButton _up = new() { Text = "Move up" }, _down = new() { Text = "Move down" },
        _rename = new() { Text = "Rename…" }, _remove = new() { Text = "Remove" }, _defaults = new() { Text = "Defaults" };

    private bool _loading, _thresholdsInF;

    private static readonly (string Name, Func<AppSettings, bool> Get, Action<AppSettings, bool> Set)[] Groups =
    {
        ("CPU", s => s.CpuEnabled, (s, v) => s.CpuEnabled = v),
        ("Graphics cards", s => s.GpuEnabled, (s, v) => s.GpuEnabled = v),
        ("Memory", s => s.MemoryEnabled, (s, v) => s.MemoryEnabled = v),
        ("Motherboard: fans, voltages, temperatures", s => s.MotherboardEnabled, (s, v) => s.MotherboardEnabled = v),
        ("Drives: temperatures and health", s => s.StorageEnabled, (s, v) => s.StorageEnabled = v),
        ("Network adapters", s => s.NetworkEnabled, (s, v) => s.NetworkEnabled = v),
        ("Battery", s => s.BatteryEnabled, (s, v) => s.BatteryEnabled = v),
        ("AIO and fan controllers (USB)", s => s.ControllerEnabled, (s, v) => s.ControllerEnabled = v),
        ("Digital power supply", s => s.PsuEnabled, (s, v) => s.PsuEnabled = v),
    };

    private sealed record MonitorChoice(string Device, string Label, bool Primary)
    {
        public override string ToString() => Label;
    }

    public SettingsForm(AppHost host, AppSettings working)
    {
        _host = host;
        Result = working;
        _groupToggles = Groups.Select(_ => new ToggleSwitch()).ToArray();

        Text = "Thermals settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(S(640), S(724));

        _tabs.Items.AddRange(new[] { "General", "Hardware", "Overlay", "Overlay items" });
        _tabs.SelectedIndexChanged += (_, _) => ShowPage(_tabs.SelectedIndex);
        _header.Height = S(60);
        _header.Controls.Add(_tabs);
        _header.Layout += (_, _) =>
        {
            var p = _tabs.GetPreferredSize(Size.Empty);
            _tabs.SetBounds(S(16), (_header.Height - p.Height) / 2, p.Width, p.Height);
        };

        _footer.Height = S(64);
        _footer.Controls.Add(_save);
        _footer.Controls.Add(_cancel);
        _footer.Layout += (_, _) =>
        {
            int x = _footer.Width - S(16);
            foreach (var b in new[] { _save, _cancel })
            {
                var p = b.GetPreferredSize(Size.Empty);
                int w = Math.Max(p.Width, S(96));
                x -= w;
                b.SetBounds(x, (_footer.Height - p.Height) / 2, w, p.Height);
                x -= S(8);
            }
        };
        AcceptButton = _save;
        CancelButton = _cancel;

        BuildGeneral();
        BuildHardware();
        BuildOverlay();
        BuildItems();
        _pages.Controls.AddRange(new Control[] { _general, _hardware, _overlay, _items });

        Controls.Add(_pages);
        Controls.Add(_header);
        Controls.Add(_footer);

        LoadValues();
        HookPreview();
        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK) SaveValues();
            _host.PreviewOverlay(null);
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Owner == null) CenterToScreen();
    }

    private void ShowPage(int index)
    {
        var pages = new[] { _general, _hardware, _overlay, _items };
        for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
    }

    // ------------------------------------------------------------------ pages

    private void BuildGeneral()
    {
        _unit.Items.AddRange(new object[] { "Fahrenheit (°F)", "Celsius (°C)" });
        _general.AddRow("Update interval", "How often sensors are read. 1000 ms is a good balance.", _interval, 140);
        _general.AddRow("Temperature unit", null, _unit, 200);
        _general.AddSection("Window");
        _general.AddRow("Keep running in the tray", "Closing the window leaves the overlay and logging running.", _minToTray);
        _general.AddRow("Live value in the tray icon", "Shows the first overlay item as a number.", _trayValue);
        _general.AddSection("Startup");
        _general.AddRow("Start with Windows", "Starts at sign-in without a UAC prompt.", _autostart);
        _general.AddRow("Start hidden", "Only the tray icon and overlay appear.", _startHidden);
        _general.AddSection("Logging");
        _general.AddRow("CSV log folder", null, _logDir, 380);

        _autostart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            bool ok = _autostart.Checked ? Autostart.Enable() : Autostart.Disable();
            if (!ok)
            {
                Dialogs.Message(this, "Thermals", "Couldn't change the startup task", "Details are in error.log in the data folder.");
                _loading = true;
                _autostart.Checked = Autostart.IsEnabled();
                _loading = false;
            }
        };
        _unit.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            int warnC = ToCelsius(_limits.Warn.Value), hotC = ToCelsius(_limits.Hot.Value);
            _thresholdsInF = _unit.SelectedIndex == 0;
            SetLimits(warnC, hotC);
        };
    }

    private void BuildHardware()
    {
        _hardware.AddNote("Choose which devices Thermals reads. Turning off what you don't need lowers the cost of every update.");
        for (int i = 0; i < Groups.Length; i++) _hardware.AddRow(Groups[i].Name, null, _groupToggles[i]);
        _hardware.AddSection("Performance");
        _hardware.AddRow("Read drives less often", "SSDs every 5 seconds, hard drives every minute. A sleeping hard drive is never woken.", _throttle);
    }

    private void BuildOverlay()
    {
        foreach (var f in new[] { "Segoe UI", "Segoe UI Semibold", "Bahnschrift", "Cascadia Mono", "Cascadia Code", "Consolas", "Calibri", "Verdana", "Tahoma", "Arial" })
            if (Theme.IsInstalled(f)) _font.Items.Add(f);

        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var sc = screens[i];
            _monitor.Items.Add(new MonitorChoice(sc.DeviceName,
                $"Display {i + 1}  ·  {sc.Bounds.Width} × {sc.Bounds.Height}{(sc.Primary ? "  ·  main" : "")}", sc.Primary));
        }

        _overlay.AddRow("Show overlay", "Toggle anywhere with Ctrl+Shift+O.", _overlayOn);
        _overlay.AddRow("Corner", null, _corner);
        _overlay.AddRow("Monitor", null, _monitor, 280);
        _overlay.AddRow("Distance from edge", null, _margin, 140);
        _overlay.AddRow("Font", null, _font, 220);
        _overlay.AddRow("Text size", null, _fontSize, 140);
        _overlay.AddRow("Background opacity", null, _background, 280);
        _overlay.AddRow("Text colour", null, _color);
        _overlay.AddRow("Warning colours", "Temperatures turn amber, then red, above these.", _thresholds);
        _overlay.AddRow("Amber and red above", null, _limits, 300);
    }

    private void BuildItems()
    {
        _items.AddNote("These readings appear in the overlay, top to bottom. Double-click any sensor in the main window (or select it and press Space) to add it. Double-click an item here to rename it.");

        _itemList.Columns.AddRange(new[]
        {
            new GridColumn { Header = "Label", Weight = 1f, Color = Theme.Text },
            new GridColumn { Header = "Sensor", Weight = 2.2f, Color = Theme.TextDim },
        });
        _itemList.EmptyText = "No items. Use Defaults or add sensors from the main window.";
        _itemList.RowsActivated += (_, _) => RenameSelected();

        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _rename.Click += (_, _) => RenameSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _defaults.Click += (_, _) => ResetDefaults();

        var buttons = new[] { _up, _down, _rename, _remove, _defaults };
        _itemsEditor.Controls.Add(_itemList);
        _itemsEditor.Controls.AddRange(buttons);
        _itemsEditor.Layout += (_, _) =>
        {
            int bw = S(112), gap = S(8);
            int y = 0;
            foreach (var b in buttons)
            {
                int bh = b.GetPreferredSize(Size.Empty).Height;
                if (b == _defaults) y += S(12);
                b.SetBounds(_itemsEditor.Width - bw, y, bw, bh);
                y += bh + gap;
            }
            _itemList.SetBounds(0, 0, _itemsEditor.Width - bw - S(16), _itemsEditor.Height);
        };
        _items.AddFill(_itemsEditor);
    }

    // ------------------------------------------------------------------ overlay items

    private OverlayItem? SelectedItem => _itemList.SelectedRows.FirstOrDefault()?.Tag as OverlayItem;

    private void RefreshItems(OverlayItem? select = null)
    {
        var snap = _host.Hub.Latest;
        var group = new GridGroup { Key = "items" };
        foreach (var item in Result.Overlay.Items)
        {
            string source = snap != null && snap.ById.TryGetValue(item.SensorId, out var r)
                ? $"{r.Hardware.Name}  ›  {r.Name}"
                : "Not found on this PC";
            group.Rows.Add(new GridRow { Tag = item, Cells = new[] { item.Label, source } });
        }
        _itemList.SetGroups(new[] { group });
        if (select != null) _itemList.SelectTag(select);
    }

    private void MoveSelected(int delta)
    {
        var item = SelectedItem;
        var list = Result.Overlay.Items;
        if (item == null) return;
        int i = list.IndexOf(item), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        RefreshItems(item);
        PushPreview();
    }

    private void RemoveSelected()
    {
        var item = SelectedItem;
        var list = Result.Overlay.Items;
        if (item == null) return;
        int i = list.IndexOf(item);
        list.Remove(item);
        RefreshItems(list.Count > 0 ? list[Math.Min(i, list.Count - 1)] : null);
        PushPreview();
    }

    private void RenameSelected()
    {
        var item = SelectedItem;
        if (item == null) return;
        var name = Dialogs.Prompt(this, "Rename overlay item", "Label", item.Label);
        if (name == null) return;
        item.Label = name.Trim();
        RefreshItems(item);
        PushPreview();
    }

    private void ResetDefaults()
    {
        if (_host.Hub.Latest is not { } snap) return;
        Result.Overlay.Items = DefaultOverlay.Pick(snap);
        RefreshItems();
        PushPreview();
    }

    // ------------------------------------------------------------------ live overlay preview

    private void HookPreview()
    {
        _overlayOn.CheckedChanged += (_, _) => PushPreview();
        _thresholds.CheckedChanged += (_, _) => PushPreview();
        _corner.ValueChanged += (_, _) => PushPreview();
        _monitor.SelectedIndexChanged += (_, _) => PushPreview();
        _font.SelectedIndexChanged += (_, _) => PushPreview();
        _margin.ValueChanged += (_, _) => PushPreview();
        _fontSize.ValueChanged += (_, _) => PushPreview();
        _background.ValueChanged += (_, _) => PushPreview();
        _color.ValueChanged += (_, _) => PushPreview();
        _limits.Warn.ValueChanged += (_, _) => PushPreview();
        _limits.Hot.ValueChanged += (_, _) => PushPreview();
    }

    private void PushPreview()
    {
        if (_loading) return;
        SaveOverlay(Result.Overlay);
        _host.PreviewOverlay(Result.Overlay);
    }

    // ------------------------------------------------------------------ load / save

    private decimal ToShown(int celsius) => _thresholdsInF ? Math.Round(celsius * 9m / 5m + 32m) : celsius;
    private int ToCelsius(decimal shown) => _thresholdsInF ? (int)Math.Round((shown - 32m) * 5m / 9m) : (int)shown;

    private void SetLimits(int warnC, int hotC)
    {
        bool was = _loading;
        _loading = true;
        string unit = _thresholdsInF ? "°F" : "°C";
        _limits.Warn.Unit = unit;
        _limits.Hot.Unit = unit;
        _limits.Warn.Value = ToShown(warnC);
        _limits.Hot.Value = ToShown(hotC);
        _loading = was;
    }

    private void LoadValues()
    {
        _loading = true;
        var s = Result;
        _interval.Value = s.PollIntervalMs;
        _unit.SelectedIndex = s.Fahrenheit ? 0 : 1;
        _minToTray.Checked = s.MinimizeToTray;
        _trayValue.Checked = s.TrayShowsValue;
        _autostart.Checked = Autostart.IsEnabled();
        _startHidden.Checked = s.StartMinimized;
        _logDir.Value = string.IsNullOrWhiteSpace(s.LogDirectory) ? AppPaths.DefaultLogDir : s.LogDirectory;

        for (int i = 0; i < Groups.Length; i++) _groupToggles[i].Checked = Groups[i].Get(s);
        _throttle.Checked = s.ThrottleDrives;

        var o = s.Overlay;
        _overlayOn.Checked = o.Enabled;
        _corner.Value = o.Corner;
        int mon = _monitor.Items.Cast<MonitorChoice>().ToList()
            .FindIndex(m => o.MonitorDevice == null ? m.Primary : m.Device == o.MonitorDevice);
        _monitor.SelectedIndex = Math.Max(0, mon);
        if (!_font.Items.Contains(o.FontFamily)) _font.Items.Insert(0, o.FontFamily);
        _font.SelectedIndex = _font.Items.IndexOf(o.FontFamily);
        _margin.Value = o.MarginPx;
        _fontSize.Value = (decimal)o.FontSizePt;
        _background.Value = o.BackgroundAlpha;
        _color.Value = Color.FromArgb(o.TextColorArgb);
        _thresholds.Checked = o.ColorThresholds;
        _thresholdsInF = s.Fahrenheit;
        SetLimits(o.WarnTempC, o.HotTempC);
        RefreshItems();
        _loading = false;
    }

    private void SaveOverlay(OverlaySettings o)
    {
        o.Enabled = _overlayOn.Checked;
        o.Corner = _corner.Value;
        if (_monitor.SelectedItem is MonitorChoice m) o.MonitorDevice = m.Primary ? null : m.Device;
        o.MarginPx = (int)_margin.Value;
        o.FontFamily = _font.SelectedItem as string ?? "Segoe UI";
        o.FontSizePt = (float)_fontSize.Value;
        o.BackgroundAlpha = _background.Value;
        o.TextColorArgb = Color.FromArgb(255, _color.Value).ToArgb();
        o.ColorThresholds = _thresholds.Checked;
        o.WarnTempC = ToCelsius(_limits.Warn.Value);
        o.HotTempC = Math.Max(ToCelsius(_limits.Hot.Value), o.WarnTempC);
        o.ItemsInitialized = true;
    }

    private void SaveValues()
    {
        var s = Result;
        s.PollIntervalMs = (int)_interval.Value;
        s.Fahrenheit = _unit.SelectedIndex == 0;
        s.MinimizeToTray = _minToTray.Checked;
        s.TrayShowsValue = _trayValue.Checked;
        s.StartMinimized = _startHidden.Checked;
        string dir = _logDir.Value.Trim();
        s.LogDirectory = dir.Length == 0 || dir == AppPaths.DefaultLogDir ? null : dir;
        for (int i = 0; i < Groups.Length; i++) Groups[i].Set(s, _groupToggles[i].Checked);
        s.ThrottleDrives = _throttle.Checked;
        SaveOverlay(s.Overlay);
    }
}
