using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Thermals.UI;

/// <summary>Noisewater Labs dark palette, fonts and drawing helpers shared by every window.</summary>
internal static class Theme
{
    // Surfaces
    public static readonly Color Window = Color.FromArgb(11, 12, 15);
    public static readonly Color Surface = Color.FromArgb(20, 22, 28);
    public static readonly Color Chrome = Color.FromArgb(14, 15, 19);
    public static readonly Color Raised = Color.FromArgb(27, 29, 36);
    public static readonly Color RaisedHover = Color.FromArgb(36, 39, 48);
    public static readonly Color Hover = Color.FromArgb(31, 34, 42);
    public static readonly Color RowAlt = Color.FromArgb(24, 26, 33);   // every other row, for scanning
    public static readonly Color Selected = Color.FromArgb(34, 37, 46);
    public static readonly Color SelectedFocus = Color.FromArgb(40, 44, 56);
    public static readonly Color Border = Color.FromArgb(42, 45, 54);
    public static readonly Color BorderStrong = Color.FromArgb(62, 66, 78);
    public static readonly Color Divider = Color.FromArgb(35, 38, 46);
    public static readonly Color GroupBand = Color.FromArgb(29, 31, 39);
    public static readonly Color GroupBandHover = Color.FromArgb(35, 38, 47);

    // Text
    public static readonly Color Text = Color.FromArgb(242, 242, 242);
    public static readonly Color TextSoft = Color.FromArgb(215, 218, 225);
    public static readonly Color TextDim = Color.FromArgb(169, 173, 184);
    public static readonly Color TextMuted = Color.FromArgb(140, 145, 158);

    // Accents (same as the overlay's warning colours)
    public static readonly Color Amber = Color.FromArgb(255, 190, 70);
    public static readonly Color AmberHover = Color.FromArgb(255, 206, 116);
    public static readonly Color AmberPressed = Color.FromArgb(230, 168, 52);
    public static readonly Color OnAmber = Color.FromArgb(23, 20, 12);
    public static readonly Color Red = Color.FromArgb(255, 92, 82);
    public static readonly Color RedText = Color.FromArgb(255, 138, 128);
    public static readonly Color PinOff = Color.FromArgb(47, 50, 60);
    public static readonly Color AmberWash = Blend(Surface, Amber, 0.06f);
    public static readonly Color BannerFill = Blend(Surface, Amber, 0.13f);

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t),
        (int)Math.Round(a.G + (b.G - a.G) * t),
        (int)Math.Round(a.B + (b.B - a.B) * t));

    // ------------------------------------------------------------------ fonts

    private static readonly HashSet<string> InstalledFamilies = LoadFamilies();
    public static readonly string UiFamily = Pick("Segoe UI", "Tahoma", "Arial");
    public static readonly string SemiboldFamily = Pick("Segoe UI Semibold", "Segoe UI", "Tahoma", "Arial");
    public static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

    private static HashSet<string> LoadFamilies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var fonts = new InstalledFontCollection();
            foreach (var f in fonts.Families) set.Add(f.Name);
        }
        catch
        {
            // Fall back to whatever GDI+ substitutes.
        }
        return set;
    }

    public static bool IsInstalled(string family) => InstalledFamilies.Contains(family);

    private static string Pick(params string[] names) => names.FirstOrDefault(IsInstalled) ?? names[^1];

    private static readonly Dictionary<string, Font> FontCache = new();

    /// <summary>A cached font sized in pixels for <paramref name="dpi"/>. Never dispose these.</summary>
    public static Font Font(int dpi, float pt, FontStyle style, string family)
    {
        string key = $"{family}|{pt}|{(int)style}|{dpi}";
        lock (FontCache)
        {
            if (!FontCache.TryGetValue(key, out var font))
            {
                font = new Font(family, pt * dpi / 72f, style, GraphicsUnit.Pixel);
                FontCache[key] = font;
            }
            return font;
        }
    }

    public static Font Ui(Control c, float pt = 9f, FontStyle style = FontStyle.Regular) => Font(c.DeviceDpi, pt, style, UiFamily);
    public static Font Semibold(Control c, float pt = 9f) => Font(c.DeviceDpi, pt, FontStyle.Regular, SemiboldFamily);
    public static Font Mono(Control c, float pt = 9f) => Font(c.DeviceDpi, pt, FontStyle.Regular, MonoFamily);

    public static int Scale(Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96.0);
    public static float Scale(Control c, float px) => px * c.DeviceDpi / 96f;

    public const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                             TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                                             TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping;

    public static void DrawText(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags extra = 0) =>
        TextRenderer.DrawText(g, text, font, r, color, TextFlags | extra);

    public static int TextWidth(string text, Font font) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;

    // ------------------------------------------------------------------ shapes

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = AppHost.RoundedRect(r, radius))
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = AppHost.RoundedRect(r, radius))
        using (var pen = new Pen(color, width))
            g.DrawPath(pen, path);
        g.SmoothingMode = old;
    }

    /// <summary>Pixel-aligned rectangle for a 1px outline inside <paramref name="size"/>.</summary>
    public static RectangleF Outline(Size size, float inset = 0.5f) =>
        new(inset, inset, size.Width - inset * 2 - 0.01f, size.Height - inset * 2 - 0.01f);

    public static void DrawGlyph(Graphics g, Glyph glyph, RectangleF r, Color color)
    {
        if (glyph == Glyph.None) return;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = r.Width, h = r.Height, x = r.X, y = r.Y;
        float cx = x + w / 2, cy = y + h / 2;
        using var pen = new Pen(color, Math.Max(1.4f, w * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);

        switch (glyph)
        {
            case Glyph.Dot:
                g.FillEllipse(brush, cx - w * 0.24f, cy - w * 0.24f, w * 0.48f, w * 0.48f);
                break;

            case Glyph.Gear:
            {
                float ro = w * 0.30f, ri = w * 0.12f;
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI / 4 * i;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * ro, cy + (float)Math.Sin(a) * ro,
                        cx + (float)Math.Cos(a) * w * 0.45f, cy + (float)Math.Sin(a) * w * 0.45f);
                }
                g.DrawEllipse(pen, cx - ro, cy - ro, ro * 2, ro * 2);
                g.DrawEllipse(pen, cx - ri, cy - ri, ri * 2, ri * 2);
                break;
            }

            case Glyph.Reset:
            {
                float rr = w * 0.36f;
                g.DrawArc(pen, cx - rr, cy - rr, rr * 2, rr * 2, -60, 300);
                // Arrow head at the arc's start (top right).
                double a = -60 * Math.PI / 180;
                float ax = cx + (float)Math.Cos(a) * rr, ay = cy + (float)Math.Sin(a) * rr;
                g.DrawLines(pen, new[] { new PointF(ax - w * 0.22f, ay - w * 0.04f), new PointF(ax, ay), new PointF(ax + w * 0.02f, ay - w * 0.24f) });
                break;
            }

            case Glyph.Overlay:
                g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.18f, w * 0.84f, h * 0.64f);
                g.FillRectangle(brush, x + w * 0.52f, y + h * 0.30f, w * 0.30f, h * 0.20f);
                break;

            case Glyph.Search:
                g.DrawEllipse(pen, x + w * 0.10f, y + h * 0.10f, w * 0.56f, h * 0.56f);
                g.DrawLine(pen, x + w * 0.60f, y + h * 0.60f, x + w * 0.90f, y + h * 0.90f);
                break;

            case Glyph.ChevronDown:
                g.DrawLines(pen, new[] { new PointF(x + w * 0.22f, y + h * 0.38f), new PointF(cx, y + h * 0.64f), new PointF(x + w * 0.78f, y + h * 0.38f) });
                break;

            case Glyph.ChevronRight:
                g.DrawLines(pen, new[] { new PointF(x + w * 0.38f, y + h * 0.22f), new PointF(x + w * 0.64f, cy), new PointF(x + w * 0.38f, y + h * 0.78f) });
                break;

            case Glyph.ChevronUp:
                g.DrawLines(pen, new[] { new PointF(x + w * 0.22f, y + h * 0.62f), new PointF(cx, y + h * 0.36f), new PointF(x + w * 0.78f, y + h * 0.62f) });
                break;

            case Glyph.Minus:
                g.DrawLine(pen, x + w * 0.25f, cy, x + w * 0.75f, cy);
                break;

            case Glyph.Plus:
                g.DrawLine(pen, x + w * 0.25f, cy, x + w * 0.75f, cy);
                g.DrawLine(pen, cx, y + h * 0.25f, cx, y + h * 0.75f);
                break;

            case Glyph.Check:
                g.DrawLines(pen, new[] { new PointF(x + w * 0.22f, y + h * 0.52f), new PointF(x + w * 0.42f, y + h * 0.72f), new PointF(x + w * 0.80f, y + h * 0.30f) });
                break;

            case Glyph.Folder:
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.10f, y + h * 0.78f), new PointF(x + w * 0.10f, y + h * 0.22f),
                    new PointF(x + w * 0.40f, y + h * 0.22f), new PointF(x + w * 0.50f, y + h * 0.34f),
                    new PointF(x + w * 0.90f, y + h * 0.34f), new PointF(x + w * 0.90f, y + h * 0.78f),
                    new PointF(x + w * 0.10f, y + h * 0.78f),
                });
                break;

            case Glyph.Chip:
            {
                var body = new RectangleF(x + w * 0.27f, y + h * 0.27f, w * 0.46f, h * 0.46f);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
                g.FillRectangle(brush, x + w * 0.40f, y + h * 0.40f, w * 0.20f, h * 0.20f);
                foreach (float t in new[] { 0.38f, 0.62f })
                {
                    g.DrawLine(pen, x + w * t, y + h * 0.08f, x + w * t, y + h * 0.27f);
                    g.DrawLine(pen, x + w * t, y + h * 0.73f, x + w * t, y + h * 0.92f);
                    g.DrawLine(pen, x + w * 0.08f, y + h * t, x + w * 0.27f, y + h * t);
                    g.DrawLine(pen, x + w * 0.73f, y + h * t, x + w * 0.92f, y + h * t);
                }
                break;
            }

            case Glyph.Gpu:
                g.DrawRectangle(pen, x + w * 0.06f, y + h * 0.26f, w * 0.88f, h * 0.48f);
                g.DrawEllipse(pen, x + w * 0.24f, y + h * 0.36f, w * 0.28f, h * 0.28f);
                g.DrawLine(pen, x + w * 0.64f, y + h * 0.42f, x + w * 0.82f, y + h * 0.42f);
                g.DrawLine(pen, x + w * 0.64f, y + h * 0.58f, x + w * 0.82f, y + h * 0.58f);
                g.DrawLine(pen, x + w * 0.16f, y + h * 0.74f, x + w * 0.16f, y + h * 0.88f);
                break;

            case Glyph.Ram:
                g.DrawRectangle(pen, x + w * 0.06f, y + h * 0.30f, w * 0.88f, h * 0.34f);
                for (int i = 0; i < 3; i++)
                    g.FillRectangle(brush, x + w * (0.17f + i * 0.25f), y + h * 0.39f, w * 0.16f, h * 0.16f);
                for (int i = 0; i < 5; i++)
                    g.DrawLine(pen, x + w * (0.16f + i * 0.17f), y + h * 0.64f, x + w * (0.16f + i * 0.17f), y + h * 0.76f);
                break;

            case Glyph.Board:
                g.DrawRectangle(pen, x + w * 0.10f, y + h * 0.10f, w * 0.80f, h * 0.80f);
                g.DrawRectangle(pen, x + w * 0.24f, y + h * 0.24f, w * 0.26f, h * 0.26f);
                g.DrawLine(pen, x + w * 0.60f, y + h * 0.30f, x + w * 0.76f, y + h * 0.30f);
                g.DrawLine(pen, x + w * 0.24f, y + h * 0.66f, x + w * 0.76f, y + h * 0.66f);
                g.DrawLine(pen, x + w * 0.24f, y + h * 0.78f, x + w * 0.60f, y + h * 0.78f);
                break;

            case Glyph.Drive:
                g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.24f, w * 0.84f, h * 0.52f);
                g.DrawLine(pen, x + w * 0.08f, y + h * 0.54f, x + w * 0.92f, y + h * 0.54f);
                g.FillEllipse(brush, x + w * 0.70f, y + h * 0.60f, w * 0.10f, w * 0.10f);
                break;

            case Glyph.Network:
                g.DrawArc(pen, x + w * 0.06f, y + h * 0.18f, w * 0.88f, h * 0.88f, 220, 100);
                g.DrawArc(pen, x + w * 0.22f, y + h * 0.36f, w * 0.56f, h * 0.56f, 220, 100);
                g.FillEllipse(brush, cx - w * 0.08f, y + h * 0.70f, w * 0.16f, w * 0.16f);
                break;

            case Glyph.Battery:
                g.DrawRectangle(pen, x + w * 0.06f, y + h * 0.30f, w * 0.78f, h * 0.40f);
                g.FillRectangle(brush, x + w * 0.86f, y + h * 0.42f, w * 0.08f, h * 0.16f);
                g.FillRectangle(brush, x + w * 0.14f, y + h * 0.38f, w * 0.40f, h * 0.24f);
                break;

            case Glyph.Fan:
                g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                for (int i = 0; i < 3; i++)
                    g.DrawArc(pen, cx - w * 0.22f, cy - h * 0.22f, w * 0.44f, h * 0.44f, i * 120, 70);
                g.FillEllipse(brush, cx - w * 0.07f, cy - h * 0.07f, w * 0.14f, h * 0.14f);
                break;

            case Glyph.Bolt:
                g.DrawPolygon(pen, new[]
                {
                    new PointF(x + w * 0.58f, y + h * 0.06f), new PointF(x + w * 0.22f, y + h * 0.56f),
                    new PointF(x + w * 0.48f, y + h * 0.56f), new PointF(x + w * 0.40f, y + h * 0.94f),
                    new PointF(x + w * 0.78f, y + h * 0.42f), new PointF(x + w * 0.52f, y + h * 0.42f),
                });
                break;

            case Glyph.Display:
                g.DrawRectangle(pen, x + w * 0.06f, y + h * 0.14f, w * 0.88f, h * 0.56f);
                g.DrawLine(pen, cx, y + h * 0.70f, cx, y + h * 0.86f);
                g.DrawLine(pen, x + w * 0.30f, y + h * 0.86f, x + w * 0.70f, y + h * 0.86f);
                break;

            case Glyph.Windows:
                g.DrawRectangle(pen, x + w * 0.08f, y + h * 0.14f, w * 0.84f, h * 0.72f);
                g.DrawLine(pen, x + w * 0.08f, y + h * 0.34f, x + w * 0.92f, y + h * 0.34f);
                g.FillEllipse(brush, x + w * 0.16f, y + h * 0.20f, w * 0.08f, w * 0.08f);
                break;

            case Glyph.Info:
                g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                g.DrawLine(pen, cx, y + h * 0.44f, cx, y + h * 0.72f);
                g.FillEllipse(brush, cx - w * 0.06f, y + h * 0.24f, w * 0.12f, w * 0.12f);
                break;

            case Glyph.Warning:
                g.DrawPolygon(pen, new[] { new PointF(cx, y + h * 0.12f), new PointF(x + w * 0.92f, y + h * 0.86f), new PointF(x + w * 0.08f, y + h * 0.86f) });
                g.DrawLine(pen, cx, y + h * 0.40f, cx, y + h * 0.60f);
                g.FillEllipse(brush, cx - w * 0.05f, y + h * 0.69f, w * 0.10f, w * 0.10f);
                break;
        }
        g.SmoothingMode = old;
    }

    // ------------------------------------------------------------------ windows

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 1809+), plus caption/border colours on Windows 11.</summary>
    public static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            int caption = ColorTranslator.ToWin32(Chrome);
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            int border = ColorTranslator.ToWin32(Border);
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }
        catch
        {
            // dwmapi unavailable: keep the default title bar.
        }
    }

    /// <summary>Applies the dark renderer to a menu and every item added to it later.</summary>
    public static void StyleMenu(ToolStripDropDown menu)
    {
        menu.Renderer = new DarkMenuRenderer();
        menu.BackColor = Raised;
        menu.ForeColor = Text;
        menu.Padding = new Padding(0, 4, 0, 4);
        if (menu is ToolStripDropDownMenu m)
        {
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
        }
        menu.Opening += (_, _) => menu.Font = Font(menu.DeviceDpi, 9f, FontStyle.Regular, UiFamily);
        foreach (ToolStripItem item in menu.Items) StyleItem(item);
        menu.ItemAdded += (_, e) => StyleItem(e.Item!);
    }

    private static void StyleItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        if (item is ToolStripMenuItem mi)
        {
            mi.Padding = new Padding(2, 4, 8, 4);
            if (mi.HasDropDownItems) StyleMenu(mi.DropDown);
        }
    }
}

internal enum Glyph
{
    None, Dot, Gear, Reset, Overlay, Search, ChevronDown, ChevronRight, ChevronUp, Minus, Plus, Check, Folder, Warning,
    // Device icons for list headers
    Chip, Gpu, Ram, Board, Drive, Network, Battery, Fan, Bolt, Display, Windows, Info,
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextDim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        float size = Math.Min(r.Width, r.Height);
        Theme.DrawGlyph(e.Graphics, Glyph.Check,
            new RectangleF(r.X + (r.Width - size) / 2, r.Y + (r.Height - size) / 2, size, size), Theme.Amber);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Divider);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Raised;
        public override Color ImageMarginGradientBegin => Theme.Raised;
        public override Color ImageMarginGradientMiddle => Theme.Raised;
        public override Color ImageMarginGradientEnd => Theme.Raised;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Selected;
        public override Color MenuItemSelected => Theme.Selected;
        public override Color MenuItemSelectedGradientBegin => Theme.Selected;
        public override Color MenuItemSelectedGradientEnd => Theme.Selected;
        public override Color MenuItemPressedGradientBegin => Theme.Selected;
        public override Color MenuItemPressedGradientMiddle => Theme.Selected;
        public override Color MenuItemPressedGradientEnd => Theme.Selected;
        public override Color SeparatorDark => Theme.Divider;
        public override Color SeparatorLight => Theme.Divider;
        public override Color CheckBackground => Theme.Raised;
        public override Color CheckSelectedBackground => Theme.Selected;
        public override Color CheckPressedBackground => Theme.Selected;
        public override Color ButtonSelectedBorder => Theme.Selected;
    }
}

/// <summary>Base for top-level windows: dark title bar, dark background, app icon.</summary>
internal class ThemedForm : Form
{
    public ThemedForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font(Theme.UiFamily, 9f);
        Icon = AppHost.LoadAppIcon();
    }

    protected int S(int px) => Theme.Scale(this, px);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(Handle);
    }
}
