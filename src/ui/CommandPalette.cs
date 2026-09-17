// Ctrl+Shift+P: type to find any command, flow, recent file or symbol. `@name` = go to symbol, `:123` = go to line.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal class PaletteItem
{
    public string Title;
    public string Detail;
    public string Shortcut;
    public string Category;
    public Action Run;
    public int Score;
}

internal class CommandPalette : Form
{
    readonly TextBox _input;
    readonly ListBox _list;
    readonly Func<string, IEnumerable<PaletteItem>> _provider;
    List<PaletteItem> _items = new List<PaletteItem>();
    bool _done;

    public CommandPalette(Form owner, string initial, Func<string, IEnumerable<PaletteItem>> provider)
    {
        _provider = provider;
        Owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        BackColor = WbTheme.Surface1;
        Padding = new Padding(1);
        int w = Math.Min(680, owner.ClientSize.Width - 80);
        Size = new Size(w, 420);
        var origin = owner.PointToScreen(new Point((owner.ClientSize.Width - w) / 2, 70));
        Location = origin;

        var inner = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Mantle, Padding = new Padding(8) };
        _input = new TextBox
        {
            Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, Font = new Font(WbTheme.UIFont.FontFamily, 11f),
            BackColor = WbTheme.Surface0, ForeColor = WbTheme.Text, Text = initial ?? ""
        };
        _list = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30,
            BackColor = WbTheme.Mantle, ForeColor = WbTheme.Text, IntegralHeight = false, Font = WbTheme.UIFont
        };
        var gap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = WbTheme.Mantle };
        inner.Controls.Add(_list);
        inner.Controls.Add(gap);
        inner.Controls.Add(_input);
        Controls.Add(inner);
        UiTheming.DarkScrollbars(_list);

        _input.TextChanged += (s, e) => Refresh(_input.Text);
        _input.KeyDown += OnKey;
        _list.DrawItem += DrawItem;
        _list.MouseClick += (s, e) => Accept();
        _list.MouseMove += (s, e) => { int i = _list.IndexFromPoint(e.Location); if (i >= 0 && i != _list.SelectedIndex) _list.SelectedIndex = i; };
        Deactivate += (s, e) => { if (!_done) Close(); };
        Shown += (s, e) => { _input.Focus(); _input.SelectionStart = _input.TextLength; };
        Refresh(_input.Text);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: Move(1); e.Handled = true; break;
            case Keys.Up: Move(-1); e.Handled = true; break;
            case Keys.PageDown: Move(8); e.Handled = true; break;
            case Keys.PageUp: Move(-8); e.Handled = true; break;
            case Keys.Enter: Accept(); e.Handled = e.SuppressKeyPress = true; break;
            case Keys.Escape: _done = true; Close(); e.Handled = e.SuppressKeyPress = true; break;
        }
    }

    void Move(int d)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Max(0, Math.Min(_list.Items.Count - 1, _list.SelectedIndex + d));
    }

    void Accept()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _items.Count) return;
        var item = _items[_list.SelectedIndex];
        _done = true;
        Close();
        if (item.Run != null) Owner.BeginInvoke(item.Run);
    }

    void Refresh(string q)
    {
        _items = _provider(q).Take(200).ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var it in _items) _list.Items.Add(it.Title);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.EndUpdate();
    }

    void DrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _items.Count) return;
        var it = _items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? WbTheme.Selection : WbTheme.Mantle)) e.Graphics.FillRectangle(bg, e.Bounds);
        const TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        int x = e.Bounds.X + 10;
        if (!string.IsNullOrEmpty(it.Category))
        {
            var cs = TextRenderer.MeasureText(it.Category + ":", WbTheme.UIFont);
            TextRenderer.DrawText(e.Graphics, it.Category + ":", WbTheme.UIFont, new Rectangle(x, e.Bounds.Y, cs.Width, e.Bounds.Height), WbTheme.Subtext0, f);
            x += cs.Width;
        }
        int right = e.Bounds.Right - 10;
        if (!string.IsNullOrEmpty(it.Shortcut))
        {
            var ss = TextRenderer.MeasureText(it.Shortcut, WbTheme.UISmall);
            right -= ss.Width;
            TextRenderer.DrawText(e.Graphics, it.Shortcut, WbTheme.UISmall, new Rectangle(right, e.Bounds.Y, ss.Width, e.Bounds.Height), WbTheme.Subtext0, f);
            right -= 12;
        }
        var ts = TextRenderer.MeasureText(it.Title, WbTheme.UIFont);
        TextRenderer.DrawText(e.Graphics, it.Title, WbTheme.UIFont, new Rectangle(x, e.Bounds.Y, Math.Min(ts.Width + 4, right - x), e.Bounds.Height), WbTheme.Text, f);
        x += ts.Width + 10;
        if (!string.IsNullOrEmpty(it.Detail) && x < right - 30)
            TextRenderer.DrawText(e.Graphics, it.Detail, WbTheme.UISmall, new Rectangle(x, e.Bounds.Y, right - x, e.Bounds.Height), WbTheme.Overlay0, f);
    }

    /// <summary>Subsequence match score (higher is better), -1 = no match.</summary>
    public static int Fuzzy(string text, string q)
    {
        if (string.IsNullOrEmpty(q)) return 0;
        if (string.IsNullOrEmpty(text)) return -1;
        int idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return 1000 - idx * 2 - (text.Length - q.Length) / 4 + (idx == 0 ? 200 : 0);
        int score = 0, ti = 0, run = 0;
        foreach (char qc in q)
        {
            if (qc == ' ') continue;
            bool found = false;
            while (ti < text.Length)
            {
                char tc = text[ti++];
                if (char.ToLowerInvariant(tc) == char.ToLowerInvariant(qc))
                {
                    run++;
                    score += 10 + run * 5 + (ti == 1 || !char.IsLetterOrDigit(text[Math.Max(0, ti - 2)]) || char.IsUpper(tc) ? 15 : 0);
                    found = true;
                    break;
                }
                run = 0;
            }
            if (!found) return -1;
        }
        return score - text.Length / 3;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // drop shadow
    }
}
