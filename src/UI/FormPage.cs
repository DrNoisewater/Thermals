using Thermals.Core;

namespace Thermals.UI;

/// <summary>Settings page: a vertical stack of label + control rows, painted in the app's style.</summary>
internal sealed class FormPage : ThemedControl
{
    private sealed class Row
    {
        public string? Title, Hint, Note;
        public Control? Control;
        public int Width;
        public bool Section, Fill;
        public int Y, H;
    }

    private readonly List<Row> _rows = new();

    public void AddSection(string title) => _rows.Add(new Row { Title = title, Section = true });
    public void AddNote(string text) => _rows.Add(new Row { Note = text });

    public void AddRow(string title, string? hint, Control control, int width = 0)
    {
        _rows.Add(new Row { Title = title, Hint = hint, Control = control, Width = width });
        Controls.Add(control);
    }

    public void AddFill(Control control)
    {
        _rows.Add(new Row { Control = control, Fill = true });
        Controls.Add(control);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = S(24), right = Width - S(24);
        int y = S(14);
        foreach (var r in _rows)
        {
            if (r.Section) { r.H = S(38); }
            else if (r.Note != null)
            {
                var size = TextRenderer.MeasureText(r.Note, Theme.Ui(this, 9f), new Size(Math.Max(1, right - left), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                r.H = size.Height + S(16);
            }
            else if (r.Fill && r.Control != null)
            {
                r.H = Math.Max(S(80), Height - y - S(16));
                r.Control.SetBounds(left, y, right - left, r.H);
            }
            else if (r.Control != null)
            {
                var pref = r.Control.GetPreferredSize(Size.Empty);
                int w = r.Width > 0 ? S(r.Width) : pref.Width;
                int ch = pref.Height;
                r.H = Math.Max(r.Hint != null ? S(54) : S(46), ch + S(12));
                r.Control.SetBounds(right - w, y + (r.H - ch) / 2, w, ch);
            }
            r.Y = y;
            y += r.H;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int left = S(24), right = Width - S(24);
        var title = Theme.Ui(this, 9.5f);
        var hint = Theme.Ui(this, 8.5f);

        foreach (var r in _rows)
        {
            if (r.Section)
            {
                Theme.DrawText(g, r.Title!.ToUpperInvariant(), Theme.Semibold(this, 7.5f), new Rectangle(left, r.Y + S(14), right - left, S(20)), Theme.TextMuted);
                continue;
            }
            if (r.Note != null)
            {
                TextRenderer.DrawText(g, r.Note, Theme.Ui(this, 9f), new Rectangle(left, r.Y + S(4), right - left, r.H - S(8)), Theme.TextDim,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                continue;
            }
            if (r.Title == null || r.Control == null) continue;

            int labelRight = r.Control.Left - S(16);
            if (r.Hint != null)
            {
                int mid = r.Y + r.H / 2;
                Theme.DrawText(g, r.Title, title, new Rectangle(left, mid - S(20), labelRight - left, S(20)), Theme.Text);
                Theme.DrawText(g, r.Hint, hint, new Rectangle(left, mid, labelRight - left, S(18)), Theme.TextMuted);
            }
            else
            {
                Theme.DrawText(g, r.Title, title, new Rectangle(left, r.Y, labelRight - left, r.H), Theme.Text);
            }
        }
    }
}

/// <summary>Dark replacements for MessageBox and a text prompt.</summary>
internal static class Dialogs
{
    public static void Message(IWin32Window? owner, string title, string heading, string body, bool showIcon = false)
    {
        using var form = NewDialog(owner, title);
        var content = new MessageBody(heading, body, showIcon) { Dock = DockStyle.Fill };
        var ok = new FlatButton { Text = "OK", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK };
        var footer = Footer(form, ok);

        form.Controls.Add(content);
        form.Controls.Add(footer);
        form.AcceptButton = ok;
        form.CancelButton = ok;

        int width = Theme.Scale(form, 460);
        int textH = content.MeasureHeight(width);
        form.ClientSize = new Size(width, textH + footer.Height);
        Show(form, owner);
    }

    public static string? Prompt(IWin32Window? owner, string title, string label, string value)
    {
        using var form = NewDialog(owner, title);
        var input = new InputBox { Value = value };
        var page = new FormPage { Dock = DockStyle.Fill };
        page.AddRow(label, null, input, 240);
        var ok = new FlatButton { Text = "OK", Kind = ButtonKind.Primary, DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Cancel", Kind = ButtonKind.Secondary, DialogResult = DialogResult.Cancel };
        var footer = Footer(form, ok, cancel);

        form.Controls.Add(page);
        form.Controls.Add(footer);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.ClientSize = new Size(Theme.Scale(form, 460), Theme.Scale(form, 76) + footer.Height);
        form.Shown += (_, _) => { input.Box.Focus(); input.Box.SelectAll(); };
        return Show(form, owner) == DialogResult.OK ? input.Value : null;
    }

    private static ThemedForm NewDialog(IWin32Window? owner, string title) => new()
    {
        Text = title,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false,
        ShowInTaskbar = false,
        StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen,
    };

    private static Strip Footer(Form form, params FlatButton[] buttons)
    {
        var footer = new Strip { Dock = DockStyle.Bottom, TopLine = true, Fill = Theme.Chrome, Height = Theme.Scale(form, 60) };
        foreach (var b in buttons) footer.Controls.Add(b);
        footer.Layout += (_, _) =>
        {
            int x = footer.Width - Theme.Scale(footer, 16);
            foreach (var b in buttons)
            {
                var p = b.GetPreferredSize(Size.Empty);
                int w = Math.Max(p.Width, Theme.Scale(footer, 88));
                x -= w;
                b.SetBounds(x, (footer.Height - p.Height) / 2, w, p.Height);
                x -= Theme.Scale(footer, 8);
            }
        };
        return footer;
    }

    private static DialogResult Show(Form form, IWin32Window? owner) =>
        owner != null ? form.ShowDialog(owner) : form.ShowDialog();

    private sealed class MessageBody : ThemedControl
    {
        private readonly string _heading, _body;
        private readonly bool _icon;
        private readonly Icon? _appIcon;

        public MessageBody(string heading, string body, bool icon)
        {
            _heading = heading;
            _body = body;
            _icon = icon;
            if (icon) _appIcon = AppHost.LoadAppIcon();
        }

        private int TextLeft => _icon ? S(24) + S(56) + S(16) : S(24);

        public int MeasureHeight(int width)
        {
            int w = width - TextLeft - S(24);
            int head = TextRenderer.MeasureText(_heading, Theme.Semibold(this, 12f), new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            int body = TextRenderer.MeasureText(_body, Theme.Ui(this, 9f), new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            return Math.Max(S(24) + head + S(10) + body + S(24), _icon ? S(104) : 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            if (_appIcon != null)
            {
                int s = S(56);
                using var big = new Icon(_appIcon, s, s);
                g.DrawIcon(big, new Rectangle(S(24), S(24), s, s));
            }
            int x = TextLeft, w = Width - x - S(24);
            var headFont = Theme.Semibold(this, 12f);
            int headH = TextRenderer.MeasureText(_heading, headFont, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, _heading, headFont, new Rectangle(x, S(24), w, headH), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _body, Theme.Ui(this, 9f), new Rectangle(x, S(24) + headH + S(10), w, Height), Theme.TextDim,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _appIcon?.Dispose();
            base.Dispose(disposing);
        }
    }

    public static void About(IWin32Window? owner) => Message(owner, "About Thermals", "Thermals",
        $"Version {Application.ProductVersion.Split('+')[0]}  ·  by {AppPaths.Company}\n\n" +
        "Hardware monitor with an on-screen overlay.\n" +
        "Sensor engine: LibreHardwareMonitor (MPL-2.0).\n\n" +
        $"Settings and logs:\n{AppPaths.DataDir}\n\n" +
        $"© {DateTime.Now.Year} {AppPaths.Company}", showIcon: true);
}
