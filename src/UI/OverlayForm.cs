using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Thermals.Core;
using Thermals.Native;

namespace Thermals.UI;

/// <summary>
/// Always-on-top, click-through, per-pixel-alpha overlay drawn into a layered window with
/// UpdateLayeredWindow. It never takes focus, never appears in Alt-Tab and only redraws when
/// the text it shows actually changes. Works over windowed and borderless-fullscreen apps;
/// exclusive-fullscreen games bypass the desktop compositor and can't be drawn over this way.
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly record struct Row(string Label, string Value, Color ValueColor);

    private readonly AppHost _host;
    private readonly List<Row> _rows = new();
    private string _lastKey = "";
    private long _lastTopmostTick;
    private Bitmap? _bitmap;

    // Layout cache (rebuilt when settings, DPI or monitor change).
    private Font? _labelFont, _valueFont;
    private float _dpi;
    private string _fontKey = "";
    private int _stableWidth, _stableValueWidth;

    /// <summary>Settings dialog shows unsaved changes live through this.</summary>
    public OverlaySettings? Preview { get; set; }
    private OverlaySettings O => Preview ?? _host.Settings.Overlay;

    public OverlayForm(AppHost host)
    {
        _host = host;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Thermals overlay";
        Size = new Size(1, 1);
        Location = new Point(-32000, -32000);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                          NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE |
                          NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        // Belt and braces: never accept mouse input even if the style is lost.
        if (m.Msg == NativeMethods.WM_NCHITTEST)
        {
            m.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    public void ShowOverlay()
    {
        if (!Visible) Show();
        ResetLayout();
    }

    /// <summary>Forces a full re-layout and redraw on the next snapshot.</summary>
    public void ResetLayout()
    {
        _lastKey = "";
        _stableWidth = 0;
        _stableValueWidth = 0;
        _fontKey = "";
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            ResetLayout();
            if (_host.Hub.Latest is { } s) ApplySnapshot(s);
        }));
    }

    public void ApplySnapshot(SensorSnapshot s)
    {
        if (!Visible || IsDisposed) return;
        var o = O;

        _rows.Clear();
        var textColor = Color.FromArgb(o.TextColorArgb);
        foreach (var item in o.Items)
        {
            if (s.ById.TryGetValue(item.SensorId, out var r))
                _rows.Add(new Row(item.Label, SensorFormat.FormatCompact(r.Type, r.Value), ValueColor(r, o, textColor)));
            else
                _rows.Add(new Row(item.Label, SensorFormat.Missing, textColor));
        }

        // Cheap change detection: skip all GDI work when nothing visible changed.
        var key = new System.Text.StringBuilder(_rows.Count * 16);
        foreach (var row in _rows) key.Append(row.Label).Append('\u0001').Append(row.Value).Append('\u0001').Append(row.ValueColor.ToArgb()).Append('\u0002');
        string k = key.ToString();

        long now = Environment.TickCount64;
        if (k == _lastKey)
        {
            if (now - _lastTopmostTick > 3000) KeepOnTop(now);
            return;
        }
        _lastKey = k;
        Render();
        KeepOnTop(now);
    }

    private static Color ValueColor(SensorReading r, OverlaySettings o, Color normal)
    {
        if (!o.ColorThresholds || r.Type != SensorType.Temperature || r.Value is not float c || SensorFormat.IsLimit(r)) return normal;
        if (c >= o.HotTempC) return Color.FromArgb(255, 255, 92, 82);
        if (c >= o.WarnTempC) return Color.FromArgb(255, 255, 190, 70);
        return normal;
    }

    /// <summary>Other topmost windows can bump us; quietly re-assert z-order.</summary>
    private void KeepOnTop(long now)
    {
        _lastTopmostTick = now;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    private Screen ResolveScreen()
    {
        string? dev = O.MonitorDevice;
        return Screen.AllScreens.FirstOrDefault(sc => sc.DeviceName == dev) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private void EnsureFonts(float dpi)
    {
        var o = O;
        string fk = $"{o.FontFamily}|{o.FontSizePt}|{dpi}";
        if (fk == _fontKey && _labelFont != null) return;

        _labelFont?.Dispose();
        _valueFont?.Dispose();
        float px = o.FontSizePt * dpi / 72f;
        try
        {
            _labelFont = new Font(o.FontFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
            _valueFont = new Font(o.FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            _labelFont = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
            _valueFont = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        _fontKey = fk;
        _dpi = dpi;
        _stableWidth = 0;
        _stableValueWidth = 0;
    }

    private void Render()
    {
        var o = O;
        var screen = ResolveScreen();
        float dpi = NativeMethods.GetScreenDpi(screen);
        EnsureFonts(dpi);
        float scale = _dpi / 96f;

        if (_rows.Count == 0)
            _rows.Add(new Row("Overlay", "pin sensors in Thermals", Color.FromArgb(o.TextColorArgb)));

        int pad = (int)Math.Round(8 * scale);
        int colGap = (int)Math.Round(14 * scale);
        int rowGap = (int)Math.Round(1 * scale);

        // Measure. Width only grows (until a re-layout) so the overlay doesn't jitter as digits change.
        using var measureFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
        measureFormat.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        float labelW = 0, valueW = 0;
        int rowH;
        using (var probe = new Bitmap(1, 1, PixelFormat.Format32bppArgb))
        using (var mg = Graphics.FromImage(probe))
        {
            mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var row in _rows)
            {
                labelW = Math.Max(labelW, mg.MeasureString(row.Label, _labelFont!, int.MaxValue, measureFormat).Width);
                valueW = Math.Max(valueW, mg.MeasureString(row.Value, _valueFont!, int.MaxValue, measureFormat).Width);
            }
            rowH = (int)Math.Ceiling(Math.Max(_labelFont!.GetHeight(mg), _valueFont!.GetHeight(mg)));
        }

        _stableValueWidth = Math.Max(_stableValueWidth, (int)Math.Ceiling(valueW));
        int width = pad * 2 + (int)Math.Ceiling(labelW) + colGap + _stableValueWidth;
        _stableWidth = Math.Max(_stableWidth, width);
        width = _stableWidth;
        int height = pad * 2 + _rows.Count * rowH + Math.Max(0, _rows.Count - 1) * rowGap;

        if (_bitmap == null || _bitmap.Width != width || _bitmap.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        }

        using (var g = Graphics.FromImage(_bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (o.BackgroundAlpha > 0)
            {
                using var bg = new SolidBrush(Color.FromArgb(o.BackgroundAlpha, 14, 15, 19));
                using var path = AppHost.RoundedRect(new RectangleF(0, 0, width, height), 6 * scale);
                g.FillPath(bg, path);
            }

            var text = Color.FromArgb(o.TextColorArgb);
            using var labelBrush = new SolidBrush(Color.FromArgb(Math.Min(255, (int)(text.A * 0.72)), text));
            using var shadow = new SolidBrush(Color.FromArgb(o.BackgroundAlpha < 120 ? 200 : 90, 0, 0, 0));
            float shadowOffset = Math.Max(1f, scale);

            float y = pad;
            float valueRight = width - pad;
            foreach (var row in _rows)
            {
                float vw = g.MeasureString(row.Value, _valueFont!, int.MaxValue, measureFormat).Width;
                float vx = valueRight - vw;

                g.DrawString(row.Label, _labelFont!, shadow, pad + shadowOffset, y + shadowOffset, measureFormat);
                g.DrawString(row.Value, _valueFont!, shadow, vx + shadowOffset, y + shadowOffset, measureFormat);
                g.DrawString(row.Label, _labelFont!, labelBrush, pad, y, measureFormat);
                using (var vb = new SolidBrush(row.ValueColor))
                    g.DrawString(row.Value, _valueFont!, vb, vx, y, measureFormat);

                y += rowH + rowGap;
            }
        }

        var area = screen.WorkingArea;
        int m = (int)Math.Round(o.MarginPx * scale);
        int x = o.Corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? area.Left + m : area.Right - width - m;
        int top = o.Corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? area.Top + m : area.Bottom - height - m;
        Present(_bitmap, x, top);
    }

    private void Present(Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));   // premultiplied 32-bit DIB
            old = NativeMethods.SelectObject(memDc, hBitmap);

            var size = new NativeMethods.SIZE(bmp.Width, bmp.Height);
            var src = new NativeMethods.POINT(0, 0);
            var dst = new NativeMethods.POINT(x, y);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            if (old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _bitmap?.Dispose();
            _labelFont?.Dispose();
            _valueFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
