using System.Text;
using LibreHardwareMonitor.Hardware;
using Thermals.Core;

namespace Thermals.UI;

internal sealed class MainForm : ThemedForm
{
    private readonly AppHost _host;

    private readonly Strip _toolbar = new() { Dock = DockStyle.Top, BottomLine = true };
    private readonly TabPills _tabs = new();
    private readonly InputBox _search = new() { SearchIcon = true, Placeholder = "Filter sensors" };
    private readonly FlatButton _reset = new() { Text = "Reset min/max", Glyph = Glyph.Reset };
    private readonly FlatButton _overlayButton = new();
    private readonly FlatButton _logButton = new() { Glyph = Glyph.Dot };
    private readonly FlatButton _menuButton = new() { Glyph = Glyph.Gear, AccessibleName = "Menu" };
    private readonly Banner _banner = new() { Dock = DockStyle.Top };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
    private readonly SensorGrid _sensors = new() { Dock = DockStyle.Fill, ShowPins = true, EmphasisColumn = 1 };
    private readonly SensorGrid _system = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly StatusLine _status = new() { Dock = DockStyle.Bottom };

    private readonly ContextMenuStrip _mainMenu = new(), _sensorMenu = new(), _systemMenu = new();
    private readonly ToolStripMenuItem _miAutostart, _miOnTop, _miPin;

    private readonly Dictionary<string, GridRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GridGroup> _groupsById = new(StringComparer.Ordinal);
    private int _structureVersion = -1;
    private string _appliedFilter = "";
    private bool _systemLoading, _systemLoaded, _closingForExit;
    private SensorSnapshot? _last;

    public MainForm(AppHost host)
    {
        _host = host;
        Text = "Thermals";
        MinimumSize = new Size(S(760), S(420));

        // ---------------- toolbar
        _tabs.Items.AddRange(new[] { "Sensors", "System" });
        _tabs.SelectedIndexChanged += (_, _) => ShowTab(_tabs.SelectedIndex);
        _search.Box.TextChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == 1) ApplySystemFilter(_search.Value.Trim());
            else if (_last != null) ApplySnapshot(_last);
        };
        _reset.Click += (_, _) => _host.Hub.ResetStats();
        _overlayButton.Click += (_, _) => _host.ToggleOverlay();
        _logButton.Click += (_, _) => _host.ToggleLogging();
        _menuButton.Click += (_, _) => _mainMenu.Show(_menuButton, new Point(_menuButton.Width, _menuButton.Height + S(4)), ToolStripDropDownDirection.BelowLeft);
        _toolbar.Controls.AddRange(new Control[] { _tabs, _search, _reset, _overlayButton, _logButton, _menuButton });
        _toolbar.Layout += (_, _) => LayoutToolbar();

        // ---------------- menus
        _miOnTop = new ToolStripMenuItem("Always on top", null, (_, _) => { TopMost = !TopMost; _miOnTop!.Checked = TopMost; });
        _miAutostart = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart());
        _mainMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Settings…", null, (_, _) => _host.ShowSettings(this)) { ShortcutKeyDisplayString = "Ctrl+," },
            new ToolStripMenuItem("Export current values…", null, (_, _) => ExportSnapshot()) { ShortcutKeyDisplayString = "Ctrl+E" },
            new ToolStripMenuItem("Open log folder", null, (_, _) => OpenLogFolder()),
            new ToolStripSeparator(),
            _miOnTop,
            _miAutostart,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Get the PawnIO driver", null, (_, _) => Platform.OpenUrl("https://pawnio.eu/")),
            new ToolStripMenuItem("Open data folder", null, (_, _) => Platform.OpenUrl(AppPaths.DataDir)),
            new ToolStripMenuItem("About Thermals", null, (_, _) => Dialogs.About(this)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => _host.Exit()),
        });
        _mainMenu.Opening += (_, _) => { _miAutostart.Checked = Autostart.IsEnabled(); _miOnTop.Checked = TopMost; };
        Theme.StyleMenu(_mainMenu);

        _miPin = new ToolStripMenuItem("Show in overlay", null, (_, _) => TogglePinSelected()) { ShortcutKeyDisplayString = "Space" };
        _sensorMenu.Items.AddRange(new ToolStripItem[]
        {
            _miPin,
            new ToolStripMenuItem("Copy", null, (_, _) => CopySensors()) { ShortcutKeyDisplayString = "Ctrl+C" },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Reset min / max / average", null, (_, _) => _host.Hub.ResetStats()) { ShortcutKeyDisplayString = "Ctrl+R" },
            new ToolStripMenuItem("Collapse all", null, (_, _) => _sensors.SetAllCollapsed(true)),
            new ToolStripMenuItem("Expand all", null, (_, _) => _sensors.SetAllCollapsed(false)),
        });
        _sensorMenu.Opening += (_, e) =>
        {
            var ids = SelectedSensorIds().ToList();
            _miPin.Enabled = ids.Count > 0;
            _miPin.Checked = ids.Count > 0 && ids.All(_host.IsOverlayItem);
        };
        Theme.StyleMenu(_sensorMenu);

        _systemMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Copy", null, (_, _) => CopySystem(selectedOnly: true)) { ShortcutKeyDisplayString = "Ctrl+C" },
            new ToolStripMenuItem("Copy all", null, (_, _) => CopySystem(selectedOnly: false)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Refresh", null, (_, _) => LoadSystemInfo(force: true)),
        });
        Theme.StyleMenu(_systemMenu);

        // ---------------- grids
        _sensors.Columns.AddRange(new[]
        {
            new GridColumn { Header = "Sensor", Weight = 2.6f, Color = Theme.TextSoft },
            new GridColumn { Header = "Current", Right = true, Mono = true },
            new GridColumn { Header = "Min", Right = true, Mono = true },
            new GridColumn { Header = "Max", Right = true, Mono = true },
            new GridColumn { Header = "Average", Right = true, Mono = true },
        });
        _sensors.EmptyText = "Reading sensors…";
        _sensors.ContextMenuStrip = _sensorMenu;
        _sensors.RowsActivated += (_, _) => TogglePinSelected();
        _sensors.CopyRequested += (_, _) => CopySensors();

        _system.Columns.AddRange(new[]
        {
            new GridColumn { Header = "Property", Weight = 1f, Color = Theme.TextDim },
            new GridColumn { Header = "Value", Weight = 2.8f, Color = Theme.TextSoft },
        });
        _system.EmptyText = "Collecting system information…";
        _system.EmphasisColumn = 1;
        _system.ContextMenuStrip = _systemMenu;
        _system.CopyRequested += (_, _) => CopySystem(selectedOnly: true);

        _content.Controls.Add(_sensors);
        _content.Controls.Add(_system);

        // Fill first, then edges (docking runs in reverse z-order).
        Controls.Add(_content);
        Controls.Add(_banner);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        ApplyMetrics();
        RestoreWindowBounds();
        UpdateBanner();
        _banner.Dismiss.Click += (_, _) => DismissNotice();
        CheckMemorySpeed();
        RefreshMenus();
    }

    // ------------------------------------------------------------------ layout

    private void ApplyMetrics()
    {
        _toolbar.Height = S(60);
        _banner.Height = S(40);
        _status.Height = S(30);
        MinimumSize = new Size(S(760), S(420));
        LayoutToolbar();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyMetrics();
    }

    private void LayoutToolbar()
    {
        int h = _toolbar.Height, pad = S(16), gap = S(8);
        var tp = _tabs.GetPreferredSize(Size.Empty);
        _tabs.SetBounds(pad, (h - tp.Height) / 2, tp.Width, tp.Height);

        int x = _toolbar.Width - pad;
        foreach (var b in new Control[] { _menuButton, _logButton, _overlayButton, _reset })
        {
            var p = b.GetPreferredSize(Size.Empty);
            x -= p.Width;
            b.SetBounds(x, (h - p.Height) / 2, p.Width, p.Height);
            x -= gap;
        }

        int searchW = Math.Min(S(280), x - _tabs.Right - S(24));
        _search.Visible = searchW >= S(120);
        if (_search.Visible) _search.SetBounds(x - searchW, (h - S(34)) / 2, searchW, S(34));
    }

    private void ShowTab(int index)
    {
        _sensors.Visible = index == 0;
        _system.Visible = index == 1;
        _search.Placeholder = index == 0 ? "Filter sensors" : "Filter";
        _reset.Visible = index == 0;
        LayoutToolbar();
        if (index == 1) { LoadSystemInfo(force: false); ApplySystemFilter(_search.Value.Trim()); }
        else if (_last != null) ApplySnapshot(_last);
        (index == 0 ? _sensors : _system).Focus();
    }

    // ------------------------------------------------------------------ snapshot → grid

    public void ApplySnapshot(SensorSnapshot s)
    {
        _last = s;
        string filter = _search.Value.Trim();
        if (_tabs.SelectedIndex == 1)
        {
            ApplySystemFilter(filter);
            UpdateStatus(s);
            return;
        }

        if (s.StructureVersion != _structureVersion || filter != _appliedFilter)
            Rebuild(s, filter);

        var o = _host.Settings.Overlay;
        foreach (var (id, row) in _rows)
        {
            if (!s.ById.TryGetValue(id, out var r)) continue;
            row.Cells[1] = SensorFormat.Format(r.Type, r.Value);
            bool limit = SensorFormat.IsLimit(r);   // fixed threshold: min/max/average mean nothing
            row.Cells[2] = limit ? "" : SensorFormat.Format(r.Type, r.Min);
            row.Cells[3] = limit ? "" : SensorFormat.Format(r.Type, r.Max);
            row.Cells[4] = limit ? "" : SensorFormat.Format(r.Type, r.Avg);
            row.ValueColor = TemperatureColor(r, o);
        }

        foreach (var node in s.Hardware)
            if (_groupsById.TryGetValue(node.Id, out var group))
                group.Note = DeviceNote(node);

        _sensors.Invalidate();
        UpdateStatus(s);
    }

    /// <summary>Right-hand note on a device header: read time, and for drives how often / whether asleep.</summary>
    private string? DeviceNote(HardwareNode node)
    {
        if (node.ParentName != null) return null;
        if (node.Asleep) return node.LastRead == null ? "Asleep · not woken" : $"Asleep · not woken · last read {Ago(node.LastRead.Value)}";
        if (node.UpdateMs <= 0) return null;
        string ms = node.UpdateMs >= 10 ? $"{node.UpdateMs:0} ms" : $"{node.UpdateMs:0.0} ms";
        if (node.Type != HardwareType.Storage || !_host.Settings.ThrottleDrives) return ms;
        return node.Id.StartsWith("/hdd/", StringComparison.Ordinal) ? $"{ms} · every minute" : $"{ms} · every 5 s";
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return $"{Math.Max(1, (int)d.TotalSeconds)} s ago";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        return $"{(int)d.TotalHours} h ago";
    }

    private static Color TemperatureColor(SensorReading r, OverlaySettings o)
    {
        if (r.Type != SensorType.Temperature || r.Value is not float c) return Color.Empty;
        if (SensorFormat.IsLimit(r)) return Theme.TextDim;
        if (c >= o.HotTempC) return Theme.Red;
        if (c >= o.WarnTempC) return Theme.Amber;
        return Color.Empty;
    }

    private void Rebuild(SensorSnapshot s, string filter)
    {
        _structureVersion = s.StructureVersion;
        _appliedFilter = filter;
        _rows.Clear();
        _groupsById.Clear();

        var groups = new List<GridGroup>();
        foreach (var node in s.Hardware)
        {
            bool nodeMatches = filter.Length == 0 || node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            GridGroup? group = null;
            foreach (var r in node.Sensors)
            {
                if (!nodeMatches &&
                    !r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !SensorFormat.TypeName(r.Type).Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                group ??= new GridGroup { Key = node.Id, Title = node.DisplayName, Subtitle = KindName(node.Type), Icon = IconFor(node.Type) };
                var row = new GridRow
                {
                    Tag = r.Id,
                    Cells = new[] { r.Name, "", "", "", "" },
                    Badge = SensorFormat.IsLimit(r) ? "Limit set by the drive" : SensorFormat.TypeName(r.Type),
                    Pinned = _host.IsOverlayItem(r.Id),
                };
                group.Rows.Add(row);
                _rows[r.Id] = row;
            }
            if (group != null)
            {
                groups.Add(group);
                _groupsById[node.Id] = group;
            }
        }

        _sensors.EmptyText = filter.Length > 0 ? $"No sensors match “{filter}”" : "Reading sensors…";
        _sensors.SetGroups(groups);
    }

    private static Glyph IconFor(HardwareType type) => type.ToString() switch
    {
        "Cpu" => Glyph.Chip,
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => Glyph.Gpu,
        "Memory" => Glyph.Ram,
        "Motherboard" => Glyph.Board,
        "SuperIO" or "EmbeddedController" => Glyph.Fan,
        "Storage" => Glyph.Drive,
        "Network" => Glyph.Network,
        "Battery" => Glyph.Battery,
        "Cooler" => Glyph.Fan,
        "Psu" => Glyph.Bolt,
        _ => Glyph.Chip,
    };

    private static Glyph IconFor(string systemSection) => systemSection switch
    {
        "Operating system" => Glyph.Windows,
        "Computer" => Glyph.Board,
        "Processor" => Glyph.Chip,
        "Memory" => Glyph.Ram,
        "Graphics" => Glyph.Gpu,
        "Storage" or "Volumes" => Glyph.Drive,
        "Network" => Glyph.Network,
        "Displays" => Glyph.Display,
        _ => Glyph.Info,
    };

    private static string KindName(HardwareType type) => type.ToString() switch
    {
        "Cpu" => "CPU",
        "GpuNvidia" => "GPU · NVIDIA",
        "GpuAmd" => "GPU · AMD",
        "GpuIntel" => "GPU · Intel",
        "Memory" => "Memory",
        "Motherboard" => "Motherboard",
        "SuperIO" => "Fans, voltages & temperatures",
        "Storage" => "Drive",
        "Network" => "Network",
        "Battery" => "Battery",
        "Cooler" => "Cooler",
        "EmbeddedController" => "Embedded controller",
        "Psu" => "Power supply",
        var other => other,
    };

    private void UpdateStatus(SensorSnapshot s)
    {
        string left = $"{s.All.Count} sensors · {s.Hardware.Count} devices";
        string middle = $"Poll {s.PollMs:0.0} ms every {_host.Settings.PollIntervalMs} ms";
        string log = _host.Logger.IsRunning ? "Logging to " + Path.GetFileName(_host.Logger.CurrentFile ?? "CSV") : "";
        _status.Set(left, middle, log);
    }

    // ------------------------------------------------------------------ pinning & copy

    private IEnumerable<string> SelectedSensorIds() => _sensors.SelectedRows.Select(r => (string)r.Tag);

    private void TogglePinSelected()
    {
        if (_last == null) return;
        var ids = SelectedSensorIds().ToList();
        if (ids.Count == 0) return;
        bool pin = !ids.All(_host.IsOverlayItem);
        foreach (var id in ids)
        {
            if (!_last.ById.TryGetValue(id, out var r)) continue;
            if (pin && !_host.IsOverlayItem(id))
                _host.Settings.Overlay.Items.Add(new OverlayItem { SensorId = id, Label = DefaultOverlay.AutoLabel(r) });
            else if (!pin)
                _host.Settings.Overlay.Items.RemoveAll(i => i.SensorId == id);
            if (_rows.TryGetValue(id, out var row)) row.Pinned = pin;
        }
        if (pin && !_host.Settings.Overlay.Enabled) _host.ToggleOverlay();
        _host.OverlayItemsChanged();
        _sensors.Invalidate();
    }

    private void CopySensors()
    {
        var sb = new StringBuilder();
        foreach (var row in _sensors.SelectedRows)
        {
            sb.Append(_sensors.GroupOf(row)?.Title ?? "").Append('\t');
            sb.AppendJoin('\t', row.Cells);
            sb.AppendLine();
        }
        SafeClipboard(sb.ToString());
    }

    // ------------------------------------------------------------------ system tab

    private List<InfoSection>? _systemSections;

    private void LoadSystemInfo(bool force)
    {
        if (_systemLoading || (_systemLoaded && !force)) return;
        _systemLoading = true;
        _system.SetGroups(Array.Empty<GridGroup>());

        Task.Run(() => SystemInfo.Collect()).ContinueWith(t =>
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                _systemLoading = false;
                _systemLoaded = true;
                _systemSections = t.IsCompletedSuccessfully ? t.Result : new List<InfoSection>();
                if (!t.IsCompletedSuccessfully)
                {
                    var err = new InfoSection("Error");
                    err.Add("Message", t.Exception?.GetBaseException().Message ?? "Unknown error");
                    _systemSections.Add(err);
                }
                ApplySystemFilter(_search.Value.Trim(), force: true);
            }));
        }, TaskScheduler.Default);
    }

    private string _systemFilter = "";

    private void ApplySystemFilter(string filter, bool force = false)
    {
        if (_systemSections == null || (!force && filter == _systemFilter)) return;
        _systemFilter = filter;
        var groups = new List<GridGroup>();
        int n = 0;
        foreach (var section in _systemSections)
        {
            var g = new GridGroup { Key = section.Title, Title = section.Title, Icon = IconFor(section.Title) };
            bool all = filter.Length == 0 || section.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);
            foreach (var kv in section.Rows)
            {
                if (!all && !kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !kv.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                bool warn = section.Warnings.Contains(kv.Key);
                g.Rows.Add(new GridRow
                {
                    Tag = n++,
                    Cells = new[] { kv.Key, kv.Value },
                    ValueColor = warn ? Theme.Amber : Theme.TextSoft,
                    Pinned = warn,   // gives the row the soft amber highlight
                });
            }
            if (g.Rows.Count > 0) groups.Add(g);
        }
        _system.EmptyText = filter.Length > 0 ? $"Nothing matches “{filter}”" : "Collecting system information…";
        _system.SetGroups(groups);
    }

    private void CopySystem(bool selectedOnly)
    {
        var sb = new StringBuilder();
        if (selectedOnly)
        {
            foreach (var row in _system.SelectedRows) sb.Append(row.Cells[0]).Append(": ").AppendLine(row.Cells[1]);
        }
        else
        {
            foreach (var g in _system.Groups)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("[" + g.Title + "]");
                foreach (var row in g.Rows) sb.Append(row.Cells[0]).Append(": ").AppendLine(row.Cells[1]);
            }
        }
        SafeClipboard(sb.ToString());
    }

    // ------------------------------------------------------------------ state shown in chrome

    public void RefreshMenus()
    {
        bool overlay = _host.Settings.Overlay.Enabled;
        _overlayButton.Kind = overlay ? ButtonKind.Primary : ButtonKind.Secondary;
        _overlayButton.Text = overlay ? "Overlay on" : "Overlay off";
        _overlayButton.Glyph = Glyph.Overlay;

        bool logging = _host.Logger.IsRunning;
        _logButton.Text = logging ? "Logging" : "Log";
        _logButton.GlyphColor = logging ? Theme.Red : Theme.TextMuted;

        LayoutToolbar();
        if (_last != null) UpdateStatus(_last);
    }

    public void SettingsChanged()
    {
        _structureVersion = -1;   // units or pinned items may have changed
        RefreshMenus();
    }

    private void UpdateBanner()
    {
        string? message = null;
        bool driverLink = false;

        if (!_host.IsElevated)
            message = "Not running as administrator, so CPU temperatures, voltages and fan speeds are unavailable.";
        else if (!_host.PawnIoInstalled)
        {
            message = "The PawnIO driver isn't installed, so CPU temperatures, motherboard fans and voltages are missing.";
            driverLink = true;
        }
        else if (_host.Hub.OpenError != null)
            message = "The hardware monitor failed to start: " + _host.Hub.OpenError;

        if (!_host.HotkeyRegistered)
            message = (message == null ? "" : message + "  ") + "Ctrl+Shift+O is taken by another app; use the Overlay button instead.";

        _banner.Visible = message != null;
        _banner.Message = message ?? "";
        _banner.Action.Visible = driverLink;
        if (driverLink)
        {
            _banner.Action.Text = "Install PawnIO";
            _banner.Action.Click += (_, _) => Platform.OpenUrl("https://pawnio.eu/");
        }
    }

    private string? _notice;   // id of the dismissable notice currently in the banner

    /// <summary>Background check for memory running below its rated speed (XMP off).</summary>
    private void CheckMemorySpeed()
    {
        const string id = "memory-speed";
        if (_host.Settings.DismissedNotices.Contains(id)) return;
        Task.Run(() => SystemInfo.CheckMemorySpeed()).ContinueWith(t =>
        {
            if (IsDisposed || !t.IsCompletedSuccessfully || t.Result is not { } issue) return;
            BeginInvoke(new Action(() =>
            {
                if (_banner.Visible) return;   // a setup problem is more important; the System tab still shows it
                _notice = id;
                _banner.Message = issue.Summary;
                _banner.Action.Text = "Show details";
                _banner.Action.Visible = true;
                _banner.Action.Click += ShowMemoryDetails;
                _banner.Dismiss.Visible = true;
                _banner.Visible = true;
                _banner.PerformLayout();
            }));
        }, TaskScheduler.Default);
    }

    private void ShowMemoryDetails(object? sender, EventArgs e) => _tabs.SelectedIndex = 1;

    private void DismissNotice()
    {
        if (_notice != null && !_host.Settings.DismissedNotices.Contains(_notice))
        {
            _host.Settings.DismissedNotices.Add(_notice);
            _host.Settings.Save();
        }
        _notice = null;
        _banner.Action.Click -= ShowMemoryDetails;
        _banner.Visible = false;
    }

    private void ToggleAutostart()
    {
        bool ok = Autostart.IsEnabled() ? Autostart.Disable() : Autostart.Enable();
        if (!ok) Dialogs.Message(this, "Thermals", "Couldn't change the startup task", "Details are in error.log in the data folder.");
    }

    private void OpenLogFolder()
    {
        string dir = string.IsNullOrWhiteSpace(_host.Settings.LogDirectory) ? AppPaths.DefaultLogDir : _host.Settings.LogDirectory;
        Directory.CreateDirectory(dir);
        Platform.OpenUrl(dir);
    }

    private void ExportSnapshot()
    {
        if (_last == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv|Text report (*.txt)|*.txt",
            FileName = $"Thermals_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sb = new StringBuilder();
            bool csv = dlg.FilterIndex == 1;
            if (csv) sb.AppendLine("Hardware,Sensor,Type,Current,Min,Max,Average,Unit");
            else sb.AppendLine($"Thermals by {AppPaths.Company} — {DateTime.Now:g}");
            foreach (var node in _last.Hardware)
            {
                if (!csv) sb.AppendLine().AppendLine(node.DisplayName);
                foreach (var r in node.Sensors)
                {
                    if (csv)
                    {
                        sb.AppendJoin(',', CsvLogger.Quote(node.DisplayName), CsvLogger.Quote(r.Name), r.Type.ToString(),
                            SensorFormat.Raw(r.Type, r.Value), SensorFormat.Raw(r.Type, r.Min),
                            SensorFormat.Raw(r.Type, r.Max), SensorFormat.Raw(r.Type, r.Avg), CsvLogger.Quote(SensorFormat.Unit(r.Type)));
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($"  {r.Name,-34} {SensorFormat.Format(r.Type, r.Value),14}   min {SensorFormat.Format(r.Type, r.Min),14}   max {SensorFormat.Format(r.Type, r.Max),14}");
                    }
                }
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Dialogs.Message(this, "Thermals", "Export failed", ex.Message);
        }
    }

    private static void SafeClipboard(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { Log.Error("Clipboard busy", ex); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                _search.Box.Focus();
                _search.Box.SelectAll();
                return true;
            case Keys.Control | Keys.R: _host.Hub.ResetStats(); return true;
            case Keys.Control | Keys.L: _host.ToggleLogging(); return true;
            case Keys.Control | Keys.E: ExportSnapshot(); return true;
            case Keys.Control | Keys.Oemcomma: _host.ShowSettings(this); return true;
            case Keys.Control | Keys.D1: _tabs.SelectedIndex = 0; return true;
            case Keys.Control | Keys.D2: _tabs.SelectedIndex = 1; return true;
            case Keys.Escape when _search.Box.Focused && _search.Value.Length > 0:
                _search.Value = "";
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ window lifecycle

    private void RestoreWindowBounds()
    {
        const int layoutVersion = 2;
        var s = _host.Settings;
        var bounds = new Rectangle(s.WindowX, s.WindowY, s.WindowW, s.WindowH);
        if (s.WindowLayoutVersion >= layoutVersion && s.WindowW >= MinimumSize.Width && s.WindowH >= MinimumSize.Height &&
            Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            // Taller by default: about 90% of the screen's usable height, up to 960 px (at 100% scale).
            var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Math.Min(S(1080), area.Width * 9 / 10), Math.Min(S(960), area.Height * 9 / 10));
            s.WindowLayoutVersion = layoutVersion;
        }
    }

    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (WindowState == FormWindowState.Normal) _host.SaveWindowBounds(Bounds);

        if (!_closingForExit && e.CloseReason == CloseReason.UserClosing)
        {
            if (_host.Settings.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                _host.MainWindowHidden();
                return;
            }
            base.OnFormClosing(e);
            BeginInvoke(new Action(_host.Exit));
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mainMenu.Dispose();
            _sensorMenu.Dispose();
            _systemMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
