namespace Thermals.UI;

internal sealed class GridColumn
{
    public required string Header { get; init; }
    public float Weight { get; init; } = 1f;
    public bool Right { get; init; }
    public bool Mono { get; init; }
    public Color Color { get; init; } = Theme.TextDim;
}

internal sealed class GridRow
{
    public required object Tag { get; init; }
    public required string[] Cells { get; init; }
    public string? Badge { get; set; }
    public bool Pinned { get; set; }
    /// <summary>Overrides the emphasis column's colour (e.g. hot temperatures). Empty = default.</summary>
    public Color ValueColor { get; set; } = Color.Empty;
}

internal sealed class GridGroup
{
    public required string Key { get; init; }
    /// <summary>Null = no header row (a plain list).</summary>
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Note { get; set; }
    public Glyph Icon { get; init; }
    public List<GridRow> Rows { get; } = new();
}

/// <summary>
/// Custom-drawn, virtualised list with collapsible groups, sticky column header, multi-select,
/// keyboard navigation and a slim dark scrollbar. Only visible rows are painted.
/// </summary>
internal sealed class SensorGrid : ThemedControl
{
    private readonly record struct Line(GridGroup Group, GridRow? Row, int Y, int H, bool Alt = false);

    private IReadOnlyList<GridGroup> _groups = Array.Empty<GridGroup>();
    private readonly List<Line> _lines = new();
    private readonly HashSet<object> _selected = new();
    private int _contentH, _scroll, _hover = -1, _anchor = -1, _focusLine = -1;
    private bool _dragThumb, _thumbHover;
    private int _dragOffset;
    private string? _emptyText;

    public SensorGrid()
    {
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
    }

    public List<GridColumn> Columns { get; } = new();
    public bool ShowPins { get; set; }
    public bool MultiSelect { get; set; } = true;
    public int EmphasisColumn { get; set; } = -1;
    public HashSet<string> Collapsed { get; } = new(StringComparer.Ordinal);

    public event EventHandler? SelectionChanged;
    public event EventHandler? RowsActivated;
    public event EventHandler? CopyRequested;

    public string? EmptyText { get => _emptyText; set { _emptyText = value; Invalidate(); } }

    private int HeaderH => S(34);
    private int GroupH => S(58);
    private int GroupGap => S(12);   // space above each device band
    private int RowH => S(28);
    private int Pad => S(20);
    private int ScrollW => S(12);
    private int Viewport => Math.Max(0, Height - HeaderH);
    private int MaxScroll => Math.Max(0, _contentH - Viewport);

    public void SetGroups(IReadOnlyList<GridGroup> groups)
    {
        _groups = groups;
        Relayout();
        Invalidate();
    }

    public IReadOnlyList<GridGroup> Groups => _groups;

    public IEnumerable<GridRow> SelectedRows
    {
        get
        {
            foreach (var g in _groups)
                foreach (var r in g.Rows)
                    if (_selected.Contains(r.Tag)) yield return r;
        }
    }

    public GridGroup? GroupOf(GridRow row) => _groups.FirstOrDefault(g => g.Rows.Contains(row));

    public void SelectTag(object? tag)
    {
        _selected.Clear();
        if (tag != null) _selected.Add(tag);
        _focusLine = _lines.FindIndex(l => l.Row != null && Equals(l.Row.Tag, tag));
        _anchor = _focusLine;
        if (_focusLine >= 0) EnsureVisible(_focusLine);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllCollapsed(bool collapsed)
    {
        Collapsed.Clear();
        if (collapsed) foreach (var g in _groups) if (g.Title != null) Collapsed.Add(g.Key);
        Relayout();
        Invalidate();
    }

    private void Relayout()
    {
        _lines.Clear();
        int y = 0;
        foreach (var g in _groups)
        {
            if (g.Title != null)
            {
                _lines.Add(new Line(g, null, y, GroupH));
                y += GroupH;
            }
            if (g.Title == null || !Collapsed.Contains(g.Key))
            {
                int n = 0;
                foreach (var r in g.Rows)
                {
                    _lines.Add(new Line(g, r, y, RowH, n++ % 2 == 1));
                    y += RowH;
                }
            }
        }
        _contentH = y + S(10);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        if (_focusLine >= _lines.Count) _focusLine = _lines.Count - 1;
        if (_hover >= _lines.Count) _hover = -1;
    }

    protected override void OnDpiUpdated() => Relayout();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
    }

    // ------------------------------------------------------------------ painting

    private void ColumnGeometry(out int[] xs, out int[] ws)
    {
        int right = Width - Pad - (MaxScroll > 0 ? ScrollW : 0);
        int left = Pad;
        int total = Math.Max(1, right - left);
        float sum = Math.Max(0.001f, Columns.Sum(c => c.Weight));
        int gap = S(12);
        xs = new int[Columns.Count];
        ws = new int[Columns.Count];
        float cursor = left;
        for (int i = 0; i < Columns.Count; i++)
        {
            float w = total * Columns[i].Weight / sum;
            xs[i] = (int)cursor;
            ws[i] = Math.Max(0, (int)w - (i < Columns.Count - 1 ? gap : 0));
            cursor += w;
        }
    }

    private int FirstVisible()
    {
        int lo = 0, hi = _lines.Count - 1, ans = _lines.Count;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_lines[mid].Y + _lines[mid].H > _scroll) { ans = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        return ans;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        if (Columns.Count == 0) return;
        ColumnGeometry(out var xs, out var ws);

        var rowFont = Theme.Ui(this, 9f);
        var monoFont = Theme.Mono(this, 9f);
        var badgeFont = Theme.Ui(this, 7.5f);
        var groupFont = Theme.Semibold(this, 11.5f);
        var subFont = Theme.Ui(this, 9f);
        int top = HeaderH;
        int bodyRight = Width - (MaxScroll > 0 ? ScrollW : 0);

        var state = g.Save();
        g.SetClip(new Rectangle(0, top, Width, Viewport));
        for (int i = FirstVisible(); i < _lines.Count; i++)
        {
            var line = _lines[i];
            int y = top + line.Y - _scroll;
            if (y > Height) break;
            var rect = new Rectangle(0, y, bodyRight, line.H);
            if (line.Row == null) PaintGroup(g, line, rect, groupFont, subFont, i == _hover);
            else PaintRow(g, line.Row, line.Alt, rect, xs, ws, rowFont, monoFont, badgeFont, i);
        }
        g.Restore(state);

        // Sticky column header.
        using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, 0, 0, Width, HeaderH);
        var headFont = Theme.Semibold(this, 7.5f);
        for (int c = 0; c < Columns.Count; c++)
        {
            int x = xs[c] + (c == 0 && ShowPins ? S(16) : 0);
            int w = ws[c] - (c == 0 && ShowPins ? S(16) : 0);
            Theme.DrawText(g, Columns[c].Header.ToUpperInvariant(), headFont, new Rectangle(x, 0, w, HeaderH), Theme.TextMuted,
                Columns[c].Right ? TextFormatFlags.Right : 0);
        }
        using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, 0, HeaderH - 1, Width, HeaderH - 1);

        if (_lines.Count == 0 && !string.IsNullOrEmpty(_emptyText))
            Theme.DrawText(g, _emptyText, Theme.Ui(this, 9.5f), new Rectangle(0, top, Width, S(80)), Theme.TextMuted, TextFormatFlags.HorizontalCenter);

        if (MaxScroll > 0)
        {
            var t = ThumbRect();
            Theme.FillRound(g, _dragThumb ? Theme.TextMuted : _thumbHover ? Theme.BorderStrong : Theme.Border, t, t.Width / 2f);
        }
    }

    private void PaintGroup(Graphics g, Line line, Rectangle r, Font title, Font sub, bool hover)
    {
        // A tinted band per device so each section stands out in a long list.
        int gap = line.Y > 0 ? GroupGap : S(6);
        var band = new RectangleF(S(8), r.Top + gap, r.Width - S(16), r.Height - gap - S(4));
        Theme.FillRound(g, hover ? Theme.GroupBandHover : Theme.GroupBand, band, S(6f));

        bool collapsed = Collapsed.Contains(line.Group.Key);
        var text = new Rectangle(r.Left, (int)band.Top, r.Width, (int)band.Height);
        float cs = S(13f);
        Theme.DrawGlyph(g, collapsed ? Glyph.ChevronRight : Glyph.ChevronDown,
            new RectangleF(Pad - S(2f), text.Top + (text.Height - cs) / 2, cs, cs), hover ? Theme.Text : Theme.TextDim);

        int x = Pad + S(20);
        if (line.Group.Icon != Glyph.None)
        {
            float isz = S(17f);
            Theme.DrawGlyph(g, line.Group.Icon, new RectangleF(x, text.Top + (text.Height - isz) / 2, isz, isz), Theme.TextSoft);
            x += S(28);
        }
        int noteW = string.IsNullOrEmpty(line.Group.Note) ? 0 : Theme.TextWidth(line.Group.Note, sub) + S(4);
        int available = r.Right - Pad - noteW - S(12) - x;
        int titleW = Math.Min(Theme.TextWidth(line.Group.Title!, title) + S(2), available);
        Theme.DrawText(g, line.Group.Title!, title, new Rectangle(x, text.Top, titleW, text.Height), Theme.Text);
        x += titleW + S(12);

        if (!string.IsNullOrEmpty(line.Group.Subtitle) && x < r.Right - Pad - noteW - S(30))
        {
            string s = collapsed ? $"{line.Group.Subtitle} · {line.Group.Rows.Count} sensors" : line.Group.Subtitle;
            Theme.DrawText(g, s, sub, new Rectangle(x, text.Top, r.Right - Pad - noteW - S(12) - x, text.Height), Theme.TextMuted);
        }
        if (noteW > 0)
            Theme.DrawText(g, line.Group.Note!, sub, new Rectangle(r.Right - Pad - noteW, text.Top, noteW, text.Height), Theme.TextMuted, TextFormatFlags.Right);
    }

    private void PaintRow(Graphics g, GridRow row, bool alt, Rectangle r, int[] xs, int[] ws, Font font, Font mono, Font badge, int index)
    {
        bool selected = _selected.Contains(row.Tag);
        Color bg = selected ? (Focused ? Theme.SelectedFocus : Theme.Selected)
            : index == _hover ? Theme.Hover
            : row.Pinned ? Theme.AmberWash
            : alt ? Theme.RowAlt
            : Theme.Surface;
        var pill = new RectangleF(S(8), r.Top + 1, r.Width - S(16), r.Height - 2);
        if (bg != Theme.Surface) Theme.FillRound(g, bg, pill, S(5f));
        if (index == _focusLine && Focused && ShowFocusCues) Theme.DrawRound(g, Theme.Amber, pill, S(5f));

        int x0 = xs[0], w0 = ws[0];
        if (ShowPins)
        {
            float d = S(6f);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var dot = new SolidBrush(row.Pinned ? Theme.Amber : Theme.PinOff))
                g.FillEllipse(dot, x0, r.Top + (r.Height - d) / 2, d, d);
            g.SmoothingMode = old;
            x0 += S(16);
            w0 -= S(16);
        }

        string name = row.Cells.Length > 0 ? row.Cells[0] : "";
        Color nameColor = row.Pinned ? Theme.Text : Columns[0].Color;
        int reserve = row.Badge != null ? S(70) : 0;
        int nameW = Math.Min(Theme.TextWidth(name, font) + S(2), Math.Max(S(40), w0 - reserve));
        Theme.DrawText(g, name, font, new Rectangle(x0, r.Top, nameW, r.Height), nameColor);
        if (row.Badge != null)
        {
            int bx = x0 + nameW + S(8);
            int bw = x0 + w0 - bx;
            if (bw > S(24)) Theme.DrawText(g, row.Badge, badge, new Rectangle(bx, r.Top, bw, r.Height), Theme.TextMuted);
        }

        for (int c = 1; c < Columns.Count && c < row.Cells.Length; c++)
        {
            var col = Columns[c];
            Color color = c == EmphasisColumn ? (row.ValueColor.IsEmpty ? Theme.Text : row.ValueColor) : col.Color;
            Theme.DrawText(g, row.Cells[c], col.Mono ? mono : font, new Rectangle(xs[c], r.Top, ws[c], r.Height), color,
                col.Right ? TextFormatFlags.Right : 0);
        }
    }

    // ------------------------------------------------------------------ scrolling

    private RectangleF ThumbRect()
    {
        int track = Viewport - S(8);
        int th = Math.Max(S(36), (int)((long)track * Viewport / Math.Max(1, _contentH)));
        float ty = HeaderH + S(4) + (float)(track - th) * _scroll / Math.Max(1, MaxScroll);
        float w = _thumbHover || _dragThumb ? S(8f) : S(6f);
        return new RectangleF(Width - S(3f) - w, ty, w, th);
    }

    private bool InScrollbar(Point p) => MaxScroll > 0 && p.X >= Width - ScrollW && p.Y >= HeaderH;

    private void ScrollTo(int value)
    {
        value = Math.Clamp(value, 0, MaxScroll);
        if (value == _scroll) return;
        _scroll = value;
        Invalidate();
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _lines.Count) return;
        var l = _lines[index];
        if (l.Y < _scroll) ScrollTo(l.Y - (index > 0 && _lines[index - 1].Row == null ? GroupH : 0));
        else if (l.Y + l.H > _scroll + Viewport) ScrollTo(l.Y + l.H - Viewport);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(_scroll - e.Delta * RowH * 3 / 120);
        base.OnMouseWheel(e);
    }

    // ------------------------------------------------------------------ mouse

    private int HitLine(Point p)
    {
        if (p.Y < HeaderH) return -1;
        int y = p.Y - HeaderH + _scroll;
        int lo = 0, hi = _lines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var l = _lines[mid];
            if (y < l.Y) hi = mid - 1;
            else if (y >= l.Y + l.H) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);

        if (InScrollbar(e.Location) && e.Button == MouseButtons.Left)
        {
            var t = ThumbRect();
            if (e.Y >= t.Top && e.Y <= t.Bottom)
            {
                _dragThumb = true;
                _dragOffset = e.Y - (int)t.Top;
            }
            else
            {
                ScrollTo(_scroll + (e.Y < t.Top ? -Viewport : Viewport));
            }
            Invalidate();
            return;
        }

        int i = HitLine(e.Location);
        if (i < 0) return;
        var line = _lines[i];

        if (line.Row == null)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                if (!Collapsed.Remove(line.Group.Key)) Collapsed.Add(line.Group.Key);
                Relayout();
                Invalidate();
            }
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            if (!_selected.Contains(line.Row.Tag)) SelectOnly(i);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (MultiSelect && (ModifierKeys & Keys.Control) != 0)
        {
            if (!_selected.Remove(line.Row.Tag)) _selected.Add(line.Row.Tag);
            _anchor = _focusLine = i;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (MultiSelect && (ModifierKeys & Keys.Shift) != 0 && _anchor >= 0)
        {
            SelectRange(_anchor, i);
        }
        else
        {
            SelectOnly(i);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragThumb)
        {
            int track = Viewport - S(8);
            int th = (int)ThumbRect().Height;
            float frac = (e.Y - _dragOffset - HeaderH - S(4)) / (float)Math.Max(1, track - th);
            ScrollTo((int)(Math.Clamp(frac, 0, 1) * MaxScroll));
            return;
        }

        bool thumb = InScrollbar(e.Location);
        int h = thumb ? -1 : HitLine(e.Location);
        if (h != _hover || thumb != _thumbHover)
        {
            _hover = h;
            _thumbHover = thumb;
            Cursor = h >= 0 && _lines[h].Row == null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragThumb) { _dragThumb = false; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        _thumbHover = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int i = HitLine(e.Location);
        if (i >= 0 && _lines[i].Row != null && e.Button == MouseButtons.Left)
            RowsActivated?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ selection & keys

    private void SelectOnly(int index)
    {
        _selected.Clear();
        if (_lines[index].Row is { } row) _selected.Add(row.Tag);
        _anchor = _focusLine = index;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRange(int a, int b)
    {
        _selected.Clear();
        for (int k = Math.Min(a, b); k <= Math.Max(a, b); k++)
            if (_lines[k].Row is { } row) _selected.Add(row.Tag);
        _focusLine = b;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private int NextRow(int from, int step)
    {
        int i = from;
        do
        {
            i += step;
            if (i < 0 || i >= _lines.Count) return from;
        } while (_lines[i].Row == null);
        return i;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown:
            case Keys.Home: case Keys.End: case Keys.Space: case Keys.Enter:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_lines.Count == 0) return;
        int target = -1;
        int page = Math.Max(1, Viewport / Math.Max(1, RowH) - 1);

        switch (e.KeyCode)
        {
            case Keys.Up: target = NextRow(_focusLine < 0 ? _lines.Count : _focusLine, -1); break;
            case Keys.Down: target = NextRow(_focusLine, 1); break;
            case Keys.PageUp:
                target = _focusLine;
                for (int k = 0; k < page; k++) target = NextRow(target, -1);
                break;
            case Keys.PageDown:
                target = _focusLine;
                for (int k = 0; k < page; k++) target = NextRow(target, 1);
                break;
            case Keys.Home: target = NextRow(-1, 1); break;
            case Keys.End: target = NextRow(_lines.Count, -1); break;
            case Keys.Space:
            case Keys.Enter:
                RowsActivated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            case Keys.A when e.Control && MultiSelect:
                _selected.Clear();
                foreach (var l in _lines) if (l.Row != null) _selected.Add(l.Row.Tag);
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            case Keys.C when e.Control:
                CopyRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
        }

        if (target >= 0 && target < _lines.Count && _lines[target].Row != null)
        {
            if (e.Shift && MultiSelect && _anchor >= 0) SelectRange(_anchor, target);
            else SelectOnly(target);
            EnsureVisible(target);
            e.Handled = true;
        }
    }
}
