using System.Globalization;
using Thermals.Core;

namespace Thermals.UI;

/// <summary>Base for every custom-drawn control: double-buffered, DPI-aware, hover/press tracking.</summary>
internal abstract class ThemedControl : Control
{
    protected bool Hovered, Pressed;

    protected ThemedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
    }

    protected int S(int px) => Theme.Scale(this, px);
    protected float S(float px) => Theme.Scale(this, px);
    protected bool ShowFocus => Focused && ShowFocusCues;
    protected Color ParentColor => Parent?.BackColor ?? Theme.Surface;

    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        OnDpiUpdated();
        PerformLayout();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        OnDpiUpdated();
    }

    protected virtual void OnDpiUpdated() { }

    protected void FocusRing(Graphics g, float radius)
    {
        if (ShowFocus) Theme.DrawRound(g, Theme.Amber, Theme.Outline(Size, 1f), radius, Math.Max(1.5f, S(1.5f)));
    }
}

/// <summary>Plain container that paints a fill and optional divider lines.</summary>
internal sealed class Strip : ThemedControl
{
    public bool TopLine { get; set; }
    public bool BottomLine { get; set; }

    public Color Fill
    {
        get => BackColor;
        set => BackColor = value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var pen = new Pen(Theme.Divider);
        if (TopLine) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        if (BottomLine) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

internal enum ButtonKind { Primary, Secondary, Ghost }

internal sealed class FlatButton : ThemedControl, IButtonControl
{
    private ButtonKind _kind = ButtonKind.Secondary;
    private Glyph _glyph;
    private Color? _glyphColor;

    public FlatButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
    }

    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }
    public Glyph Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }
    public Color? GlyphColor { get => _glyphColor; set { _glyphColor = value; Invalidate(); } }
    public DialogResult DialogResult { get; set; }
    private bool IconOnly => string.IsNullOrEmpty(Text);

    public void NotifyDefault(bool value) { }
    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = Text;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; }
        base.OnKeyUp(e);
    }

    private Font TextFont => Kind == ButtonKind.Primary ? Theme.Semibold(this) : Theme.Ui(this);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int h = S(34);
        if (IconOnly) return new Size(h, h);
        int w = Theme.TextWidth(Text, TextFont) + S(28);
        if (Glyph != Glyph.None) w += S(14) + S(7);
        return new Size(w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);

        Color fill, text;
        bool outline = false;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = Pressed ? Theme.AmberPressed : Hovered ? Theme.AmberHover : Theme.Amber;
                text = Theme.OnAmber;
                break;
            case ButtonKind.Ghost:
                fill = Pressed ? Theme.Selected : Hovered ? Theme.RaisedHover : ParentColor;
                text = Hovered ? Theme.Text : Theme.TextSoft;
                break;
            default:
                fill = Pressed ? Theme.Selected : Hovered ? Theme.RaisedHover : Theme.Raised;
                text = Theme.Text;
                outline = true;
                break;
        }
        if (!Enabled) { text = Theme.TextMuted; fill = Kind == ButtonKind.Ghost ? ParentColor : Theme.Raised; }

        float radius = S(6f);
        var r = Theme.Outline(Size);
        Theme.FillRound(g, fill, r, radius);
        if (outline) Theme.DrawRound(g, Hovered ? Theme.BorderStrong : Theme.Border, r, radius);
        FocusRing(g, radius);

        var font = TextFont;
        int glyphSize = S(14), gap = S(7);
        int textW = IconOnly ? 0 : Theme.TextWidth(Text, font);
        int contentW = (Glyph != Glyph.None ? glyphSize : 0) + (Glyph != Glyph.None && !IconOnly ? gap : 0) + textW;
        int x = (Width - contentW) / 2;
        if (Glyph != Glyph.None)
        {
            Theme.DrawGlyph(g, Glyph, new RectangleF(x, (Height - glyphSize) / 2f, glyphSize, glyphSize), GlyphColor ?? text);
            x += glyphSize + gap;
        }
        if (!IconOnly) Theme.DrawText(g, Text, font, new Rectangle(x, 0, textW + S(2), Height), text);
    }
}

/// <summary>Pill-shaped tab selector.</summary>
internal sealed class TabPills : ThemedControl
{
    private int _selected, _hover = -1;

    public TabPills()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public List<string> Items { get; } = new();
    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= Items.Count) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<Rectangle> Rects()
    {
        var list = new List<Rectangle>(Items.Count);
        int x = 0;
        foreach (var item in Items)
        {
            int w = Theme.TextWidth(item, Theme.Semibold(this)) + S(28);
            list.Add(new Rectangle(x, 0, w, Height));
            x += w + S(4);
        }
        return list;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int w = 0;
        foreach (var item in Items) w += Theme.TextWidth(item, Theme.Semibold(this)) + S(28) + S(4);
        return new Size(Math.Max(0, w - S(4)), S(34));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        var rects = Rects();
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            bool sel = i == _selected;
            if (sel) Theme.FillRound(g, Theme.Selected, new RectangleF(r.X, r.Y, r.Width, r.Height), S(6f));
            if (sel && ShowFocus) Theme.DrawRound(g, Theme.Amber, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), S(6f), S(1.5f));
            var color = sel ? Theme.Text : i == _hover ? Theme.TextSoft : Theme.TextDim;
            Theme.DrawText(g, Items[i], sel ? Theme.Semibold(this) : Theme.Ui(this), r, color, TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Rects().FindIndex(r => r.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = Rects().FindIndex(r => r.Contains(e.Location));
        if (i >= 0) SelectedIndex = i;
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(Items.Count - 1, _selected + 1);
        base.OnKeyDown(e);
    }
}

/// <summary>Dark text field: a borderless TextBox inside a rounded frame.</summary>
internal class InputBox : ThemedControl
{
    public readonly TextBox Box = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Chrome,
        ForeColor = Theme.Text,
    };

    private bool _searchIcon;

    public InputBox()
    {
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
    }

    public bool SearchIcon { get => _searchIcon; set { _searchIcon = value; PerformLayout(); Invalidate(); } }
    public string Value { get => Box.Text; set => Box.Text = value; }
    public string Placeholder { get => Box.PlaceholderText; set => Box.PlaceholderText = value; }

    protected virtual int RightReserve => 0;

    public override Size GetPreferredSize(Size proposedSize) => new(S(220), S(34));

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var font = Theme.Ui(this, 9.5f);
        if (!ReferenceEquals(Box.Font, font)) Box.Font = font;
        int left = S(10) + (SearchIcon ? S(22) : 0);
        int h = Box.PreferredHeight;
        Box.SetBounds(left, (Height - h) / 2, Math.Max(10, Width - left - S(10) - RightReserve), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        var r = Theme.Outline(Size);
        Theme.FillRound(g, Theme.Chrome, r, S(6f));
        var border = Box.Focused ? Theme.Amber : Hovered ? Theme.BorderStrong : Theme.Border;
        Theme.DrawRound(g, border, r, S(6f));
        if (SearchIcon)
        {
            float s = S(14f);
            Theme.DrawGlyph(g, Glyph.Search, new RectangleF(S(11f), (Height - s) / 2, s, s), Theme.TextMuted);
        }
    }
}

/// <summary>Numeric field with − / + steppers and an optional unit.</summary>
internal sealed class NumberBox : InputBox
{
    private decimal _value;
    private string _unit = "";
    private int _hoverStep;   // -1 minus, +1 plus

    public NumberBox()
    {
        Box.Leave += (_, _) => Commit();
        Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { Value = _value + Increment; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Value = _value - Increment; e.Handled = true; }
        };
        Box.MouseWheel += (_, e) => { if (Box.Focused) Value = _value + (e.Delta > 0 ? Increment : -Increment); };
        Box.Text = "0";
    }

    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Increment { get; set; } = 1;
    public int DecimalPlaces { get; set; }
    public event EventHandler? ValueChanged;

    public string Unit { get => _unit; set { _unit = value; PerformLayout(); Invalidate(); } }

    public decimal Value
    {
        get => _value;
        set
        {
            var v = Math.Round(Math.Clamp(value, Minimum, Maximum), DecimalPlaces);
            bool changed = v != _value;
            _value = v;
            Box.Text = v.ToString(DecimalPlaces > 0 ? "0." + new string('#', DecimalPlaces) : "0", CultureInfo.CurrentCulture);
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Commit()
    {
        if (decimal.TryParse(Box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var d)) Value = d;
        else Value = _value;
    }

    private int StepW => S(22);
    private int UnitW => _unit.Length == 0 ? 0 : Theme.TextWidth(_unit, Theme.Ui(this, 9f)) + S(8);
    protected override int RightReserve => UnitW + StepW * 2 + S(4);

    private Rectangle MinusRect => new(Width - StepW * 2 - S(4), S(4), StepW, Height - S(8));
    private Rectangle PlusRect => new(Width - StepW - S(4), S(4), StepW, Height - S(8));

    public override Size GetPreferredSize(Size proposedSize) => new(S(130), S(34));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        if (_unit.Length > 0)
            Theme.DrawText(g, _unit, Theme.Ui(this, 9f), new Rectangle(MinusRect.Left - UnitW, 0, UnitW, Height), Theme.TextMuted);
        DrawStep(g, MinusRect, Glyph.Minus, _hoverStep == -1);
        DrawStep(g, PlusRect, Glyph.Plus, _hoverStep == 1);
    }

    private void DrawStep(Graphics g, Rectangle r, Glyph glyph, bool hover)
    {
        if (hover) Theme.FillRound(g, Theme.Raised, new RectangleF(r.X, r.Y, r.Width, r.Height), S(4f));
        float s = S(12f);
        Theme.DrawGlyph(g, glyph, new RectangleF(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s), hover ? Theme.Text : Theme.TextDim);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = MinusRect.Contains(e.Location) ? -1 : PlusRect.Contains(e.Location) ? 1 : 0;
        if (h != _hoverStep) { _hoverStep = h; Cursor = h != 0 ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverStep = 0; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (MinusRect.Contains(e.Location)) { Commit(); Value = _value - Increment; }
            else if (PlusRect.Contains(e.Location)) { Commit(); Value = _value + Increment; }
        }
        base.OnMouseDown(e);
    }

    protected override void OnClick(EventArgs e)
    {
        // Steppers are handled on mouse down; only clicks on the text area focus the box.
        if (_hoverStep == 0) Box.Focus();
    }
}

internal sealed class ToggleSwitch : ThemedControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(S(42), S(24));

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) Checked = !Checked;
        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        var r = Theme.Outline(Size);
        float radius = r.Height / 2;
        Color track = _checked ? (Hovered ? Theme.AmberHover : Theme.Amber) : (Hovered ? Theme.BorderStrong : Theme.Border);
        Theme.FillRound(g, track, r, radius);
        FocusRing(g, radius);
        float d = Height - S(6f);
        float x = _checked ? Width - S(3f) - d : S(3f);
        using var knob = new SolidBrush(_checked ? Theme.OnAmber : Theme.TextSoft);
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillEllipse(knob, x, S(3f), d, d);
        g.SmoothingMode = old;
    }
}

/// <summary>Drop-down picker built on a dark menu (the native ComboBox can't be fully themed).</summary>
internal sealed class ChoiceBox : ThemedControl
{
    private readonly ContextMenuStrip _menu = new();
    private int _selected = -1;

    public ChoiceBox()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        Theme.StyleMenu(_menu);
    }

    public List<object> Items { get; } = new();
    public event EventHandler? SelectedIndexChanged;
    public object? SelectedItem => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < -1 || value >= Items.Count) return;
            _selected = value;
            AccessibleName = SelectedItem?.ToString();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(S(220), S(34));

    protected override void OnClick(EventArgs e) { Focus(); ShowMenu(); base.OnClick(e); }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space || (e.KeyCode == Keys.Down && e.Alt)) ShowMenu();
        else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(Items.Count - 1, _selected + 1);
        else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, _selected - 1);
        base.OnKeyDown(e);
    }

    private void ShowMenu()
    {
        _menu.Items.Clear();
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == _selected };
            item.Click += (_, _) => SelectedIndex = index;
            _menu.Items.Add(item);
        }
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, new Point(0, Height + S(2)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        var r = Theme.Outline(Size);
        Theme.FillRound(g, Hovered ? Theme.Raised : Theme.Chrome, r, S(6f));
        Theme.DrawRound(g, Hovered ? Theme.BorderStrong : Theme.Border, r, S(6f));
        FocusRing(g, S(6f));
        Theme.DrawText(g, SelectedItem?.ToString() ?? "", Theme.Ui(this, 9.5f), new Rectangle(S(12), 0, Width - S(40), Height), Theme.TextSoft);
        float s = S(12f);
        Theme.DrawGlyph(g, Glyph.ChevronDown, new RectangleF(Width - S(12f) - s, (Height - s) / 2, s, s), Theme.TextDim);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class Slider : ThemedControl
{
    private int _value, _min, _max = 100;
    private bool _dragging;

    public Slider()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
    }

    public event EventHandler? ValueChanged;
    public Func<int, string>? Formatter { get; set; }
    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int LabelW => Formatter != null ? S(48) : 0;
    private float TrackLeft => S(9f);
    private float TrackRight => Width - LabelW - S(9f);

    public override Size GetPreferredSize(Size proposedSize) => new(S(260), S(30));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        float cy = Height / 2f, th = S(4f);
        float frac = _max > _min ? (_value - _min) / (float)(_max - _min) : 0;
        float kx = TrackLeft + (TrackRight - TrackLeft) * frac;
        Theme.FillRound(g, Theme.Border, new RectangleF(TrackLeft, cy - th / 2, TrackRight - TrackLeft, th), th / 2);
        if (kx > TrackLeft) Theme.FillRound(g, Theme.Amber, new RectangleF(TrackLeft, cy - th / 2, kx - TrackLeft, th), th / 2);

        float d = S(16f);
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var knob = new SolidBrush(Hovered || _dragging ? Theme.Text : Theme.TextSoft))
            g.FillEllipse(knob, kx - d / 2, cy - d / 2, d, d);
        if (ShowFocus)
            using (var pen = new Pen(Theme.Amber, S(1.5f)))
                g.DrawEllipse(pen, kx - d / 2 - S(2f), cy - d / 2 - S(2f), d + S(4f), d + S(4f));
        g.SmoothingMode = old;

        if (Formatter != null)
            Theme.DrawText(g, Formatter(_value), Theme.Ui(this, 9f), new Rectangle(Width - LabelW, 0, LabelW, Height), Theme.TextSoft, TextFormatFlags.Right);
    }

    private void SetFromX(int x)
    {
        float frac = (x - TrackLeft) / Math.Max(1f, TrackRight - TrackLeft);
        Value = _min + (int)Math.Round(Math.Clamp(frac, 0, 1) * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); _dragging = true; SetFromX(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta) * Math.Max(1, (_max - _min) / 20);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = Math.Max(1, (_max - _min) / 50);
        if (e.KeyCode == Keys.Left) Value -= step;
        if (e.KeyCode == Keys.Right) Value += step;
        if (e.KeyCode == Keys.Home) Value = _min;
        if (e.KeyCode == Keys.End) Value = _max;
        base.OnKeyDown(e);
    }
}

/// <summary>Miniature screen with four clickable corners.</summary>
internal sealed class CornerPicker : ThemedControl
{
    private OverlayCorner _value = OverlayCorner.TopRight;
    private OverlayCorner? _hover;

    public CornerPicker()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.RadioButton;
    }

    public event EventHandler? ValueChanged;

    public OverlayCorner Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(S(116), S(68));

    private RectangleF Chip(OverlayCorner c)
    {
        float w = S(24f), h = S(13f), m = S(7f);
        bool left = c is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;
        bool top = c is OverlayCorner.TopLeft or OverlayCorner.TopRight;
        return new RectangleF(left ? m : Width - m - w, top ? m : Height - m - h, w, h);
    }

    private OverlayCorner At(Point p)
    {
        bool left = p.X < Width / 2, top = p.Y < Height / 2;
        return top ? (left ? OverlayCorner.TopLeft : OverlayCorner.TopRight) : (left ? OverlayCorner.BottomLeft : OverlayCorner.BottomRight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        var r = Theme.Outline(Size);
        Theme.FillRound(g, Theme.Chrome, r, S(6f));
        Theme.DrawRound(g, Theme.Border, r, S(6f));
        FocusRing(g, S(6f));
        foreach (var c in Enum.GetValues<OverlayCorner>())
        {
            var chip = Chip(c);
            if (c == _value) Theme.FillRound(g, Theme.Amber, chip, S(3f));
            else Theme.DrawRound(g, c == _hover ? Theme.TextDim : Theme.BorderStrong, chip, S(3f));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = At(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = null; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); Value = At(e.Location); }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool left = _value is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;
        bool top = _value is OverlayCorner.TopLeft or OverlayCorner.TopRight;
        if (e.KeyCode == Keys.Left) left = true;
        if (e.KeyCode == Keys.Right) left = false;
        if (e.KeyCode == Keys.Up) top = true;
        if (e.KeyCode == Keys.Down) top = false;
        Value = top ? (left ? OverlayCorner.TopLeft : OverlayCorner.TopRight) : (left ? OverlayCorner.BottomLeft : OverlayCorner.BottomRight);
        base.OnKeyDown(e);
    }
}

/// <summary>Preset colour swatches plus a custom picker.</summary>
internal sealed class SwatchPicker : ThemedControl
{
    private static readonly Color[] Presets =
    {
        Color.FromArgb(242, 242, 242), Color.FromArgb(255, 255, 255), Color.FromArgb(127, 214, 232),
        Color.FromArgb(155, 229, 143), Color.FromArgb(201, 182, 255),
    };

    private Color _value = Presets[0];
    private int _hover = -2;   // -1 = custom button

    public SwatchPicker()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? ValueChanged;

    public Color Value
    {
        get => _value;
        set
        {
            if (_value.ToArgb() == value.ToArgb()) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Cell => S(32);
    private bool IsCustom => Presets.All(p => p.ToArgb() != _value.ToArgb());
    private Rectangle CustomRect => new(Presets.Length * Cell + S(6), 0, Theme.TextWidth("Custom…", Theme.Ui(this)) + S(36), Height);

    public override Size GetPreferredSize(Size proposedSize) => new(Presets.Length * Cell + S(6) + Theme.TextWidth("Custom…", Theme.Ui(this)) + S(36), S(32));

    private int HitTest(Point p)
    {
        if (CustomRect.Contains(p)) return -1;
        int i = p.X / Cell;
        return i >= 0 && i < Presets.Length ? i : -2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        for (int i = 0; i < Presets.Length; i++)
        {
            float pad = S(4f);
            var cell = new RectangleF(i * Cell + pad, pad, Cell - pad * 2, Height - pad * 2);
            Theme.FillRound(g, Presets[i], cell, S(5f));
            if (Presets[i].ToArgb() == _value.ToArgb())
                Theme.DrawRound(g, Theme.Amber, RectangleF.Inflate(cell, S(2.5f), S(2.5f)), S(7f), S(2f));
            else if (_hover == i)
                Theme.DrawRound(g, Theme.TextDim, RectangleF.Inflate(cell, S(2f), S(2f)), S(7f));
        }

        var cr = CustomRect;
        var crf = new RectangleF(cr.X + 0.5f, cr.Y + 0.5f, cr.Width - 1, cr.Height - 1);
        Theme.FillRound(g, _hover == -1 ? Theme.RaisedHover : Theme.Raised, crf, S(6f));
        Theme.DrawRound(g, IsCustom ? Theme.Amber : Theme.Border, crf, S(6f), IsCustom ? S(1.5f) : 1f);
        float chip = S(12f);
        Theme.FillRound(g, IsCustom ? _value : Theme.TextMuted, new RectangleF(cr.X + S(10f), (Height - chip) / 2, chip, chip), S(3f));
        Theme.DrawText(g, "Custom…", Theme.Ui(this), new Rectangle(cr.X + S(28), 0, cr.Width - S(30), Height), Theme.Text);
        FocusRing(g, S(6f));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -2; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) { base.OnMouseDown(e); return; }
        Focus();
        int h = HitTest(e.Location);
        if (h >= 0) Value = Presets[h];
        else if (h == -1) PickCustom();
        base.OnMouseDown(e);
    }

    private void PickCustom()
    {
        using var dlg = new ColorDialog { Color = _value, FullOpen = true };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Value = Color.FromArgb(255, dlg.Color);
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int i = Array.FindIndex(Presets, p => p.ToArgb() == _value.ToArgb());
        if (e.KeyCode == Keys.Left && i > 0) Value = Presets[i - 1];
        if (e.KeyCode == Keys.Right && i < Presets.Length - 1) Value = Presets[Math.Max(0, i + 1)];
        if (e.KeyCode is Keys.Enter or Keys.Space) PickCustom();
        base.OnKeyDown(e);
    }
}

/// <summary>Text field with a Browse button for picking a folder.</summary>
internal sealed class FolderPicker : ThemedControl
{
    public readonly InputBox Input = new();
    private readonly FlatButton _browse = new() { Text = "Browse…", Kind = ButtonKind.Secondary };

    public FolderPicker()
    {
        Controls.Add(Input);
        Controls.Add(_browse);
        _browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = Input.Value, UseDescriptionForTitle = true, Description = "Folder for CSV logs" };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Input.Value = dlg.SelectedPath;
        };
    }

    public string Value { get => Input.Value; set => Input.Value = value; }

    public override Size GetPreferredSize(Size proposedSize) => new(S(360), S(34));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var b = _browse.GetPreferredSize(Size.Empty);
        _browse.SetBounds(Width - b.Width, 0, b.Width, Height);
        Input.SetBounds(0, 0, Width - b.Width - S(8), Height);
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(ParentColor);
}

/// <summary>Amber and red temperature limits side by side.</summary>
internal sealed class ThresholdPair : ThemedControl
{
    public readonly NumberBox Warn = new() { Minimum = 30, Maximum = 250 };
    public readonly NumberBox Hot = new() { Minimum = 30, Maximum = 250 };

    public ThresholdPair()
    {
        Controls.Add(Warn);
        Controls.Add(Hot);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(S(300), S(34));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int dot = S(18), gap = S(16);
        int box = (Width - dot * 2 - gap) / 2;
        Warn.SetBounds(dot, 0, box, Height);
        Hot.SetBounds(dot * 2 + box + gap, 0, box, Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentColor);
        float d = S(10f);
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var amber = new SolidBrush(Theme.Amber)) g.FillEllipse(amber, 0, (Height - d) / 2, d, d);
        using (var red = new SolidBrush(Theme.Red)) g.FillEllipse(red, Hot.Left - S(18f), (Height - d) / 2, d, d);
        g.SmoothingMode = old;
    }
}

/// <summary>Status line along the bottom of the main window.</summary>
internal sealed class StatusLine : ThemedControl
{
    private string _left = "", _middle = "", _log = "";

    public StatusLine() => BackColor = Theme.Chrome;

    public void Set(string left, string middle, string log)
    {
        if (left == _left && middle == _middle && log == _log) return;
        _left = left;
        _middle = middle;
        _log = log;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Chrome);
        using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, 0, 0, Width, 0);
        var font = Theme.Ui(this, 8.5f);
        int x = S(16);

        string brand = AppPaths.Company;
        int brandW = Theme.TextWidth(brand, Theme.Semibold(this, 8.5f));
        int limit = Width - brandW - S(32);

        foreach (var text in new[] { _left, _middle })
        {
            if (text.Length == 0 || x >= limit) continue;
            int w = Math.Min(Theme.TextWidth(text, font) + S(2), limit - x);
            Theme.DrawText(g, text, font, new Rectangle(x, 0, w, Height), Theme.TextMuted);
            x += w + S(20);
        }

        if (_log.Length > 0 && x < limit)
        {
            float d = S(7f);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var red = new SolidBrush(Theme.Red)) g.FillEllipse(red, x, (Height - d) / 2, d, d);
            g.SmoothingMode = old;
            x += S(13);
            Theme.DrawText(g, _log, font, new Rectangle(x, 0, Math.Max(0, limit - x), Height), Theme.RedText);
        }

        Theme.DrawText(g, brand, Theme.Semibold(this, 8.5f), new Rectangle(Width - brandW - S(16), 0, brandW + S(2), Height), Theme.TextMuted);
    }
}

/// <summary>Warning band under the toolbar (missing admin rights, driver, hotkey…).</summary>
internal sealed class Banner : ThemedControl
{
    public readonly FlatButton Action = new() { Kind = ButtonKind.Ghost };
    public readonly FlatButton Dismiss = new() { Kind = ButtonKind.Ghost, Text = "Dismiss", Visible = false };
    private string _message = "";

    public Banner()
    {
        BackColor = Theme.BannerFill;
        Controls.Add(Action);
        Controls.Add(Dismiss);
    }

    public string Message { get => _message; set { _message = value; Invalidate(); } }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int x = Width - S(12);
        foreach (var b in new[] { Dismiss, Action })
        {
            if (!b.Visible) continue;
            var p = b.GetPreferredSize(Size.Empty);
            x -= p.Width;
            b.SetBounds(x, (Height - S(28)) / 2, p.Width, S(28));
            x -= S(4);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        float s = S(15f);
        Theme.DrawGlyph(g, Glyph.Warning, new RectangleF(S(16f), (Height - s) / 2, s, s), Theme.Amber);
        int right = Action.Visible ? Action.Left - S(12) : Dismiss.Visible ? Dismiss.Left - S(12) : Width - S(16);
        Theme.DrawText(g, _message, Theme.Ui(this, 9f), new Rectangle(S(42), 0, Math.Max(0, right - S(42)), Height), Theme.TextSoft);
    }
}
