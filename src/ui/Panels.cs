// Tool panels: Problems (errors and warnings, click to jump) and Console (script runs, flow logs).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal class ProblemsPanel : ToolPanel
{
    readonly IWorkbenchHost _host;
    readonly ListView _list;
    readonly ToolStripButton _btnErrors, _btnWarnings;
    readonly SearchBox _filter;
    List<Diagnostic> _all = new List<Diagnostic>();
    string _mainFile;
    readonly ImageList _icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

    public event Action<int, int> CountsChanged;

    public ProblemsPanel(IWorkbenchHost host) : base("Problems")
    {
        _host = host;
        _btnErrors = BarButton(Glyphs.Error, "Show errors", Refill, false, "0 errors");
        _btnWarnings = BarButton(Glyphs.Warning, "Show warnings", Refill, false, "0 warnings");
        _btnErrors.CheckOnClick = _btnWarnings.CheckOnClick = true;
        _btnErrors.Checked = _btnWarnings.Checked = true;
        _filter = new SearchBox { Cue = "Filter problems", Width = 220 };
        _filter.TextChanged += (s, e) => Refill();
        BarSearch(_filter, 240, true);

        _list = new ThemedListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None, SmallImageList = _icons, MultiSelect = true, HideSelection = false, Font = WbTheme.UIFont
        };
        _list.Columns.Add("Message", 600);
        _list.Columns.Add("File", 200);
        _list.Columns.Add("Line", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Source", 80);
        _list.ItemActivate += (s, e) => Open();
        _list.MouseClick += (s, e) => Open();
        _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.C && e.Control) CopySelected(); };
        _list.Resize += (s, e) => FitColumns();
        var cm = new ContextMenuStrip();
        cm.Items.Add("Copy", null, (s, e) => CopySelected());
        cm.Items.Add("Copy all", null, (s, e) => { foreach (ListViewItem it in _list.Items) it.Selected = true; CopySelected(); });
        _list.ContextMenuStrip = cm;
        Controls.Add(_list);
        _list.BringToFront();
        ApplyTheme();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _icons.Images.Clear();
        _icons.Images.Add(Glyphs.Get(Glyphs.Error, WbTheme.Red) ?? Glyphs.Dot(WbTheme.Red));
        _icons.Images.Add(Glyphs.Get(Glyphs.Warning, WbTheme.Yellow) ?? Glyphs.Dot(WbTheme.Yellow));
        if (_btnErrors != null) { _btnErrors.Image = Glyphs.Get(Glyphs.Error, WbTheme.Red); _btnWarnings.Image = Glyphs.Get(Glyphs.Warning, WbTheme.Yellow); }
        if (ContextMenuStrip != null) UiTheming.Apply(ContextMenuStrip);
        if (_list != null && _list.ContextMenuStrip != null) _list.ContextMenuStrip.Renderer = new DarkMenuRenderer();
    }

    void FitColumns()
    {
        int w = _list.ClientSize.Width - _list.Columns[1].Width - _list.Columns[2].Width - _list.Columns[3].Width;
        if (w > 150) _list.Columns[0].Width = w;
    }

    public void SetDiagnostics(IEnumerable<Diagnostic> diags, string mainFile)
    {
        _all = diags.ToList();
        _mainFile = mainFile;
        Refill();
    }

    void Refill()
    {
        int errs = _all.Count(d => d.IsError), warns = _all.Count - errs;
        _btnErrors.Text = errs + (errs == 1 ? " error" : " errors");
        _btnWarnings.Text = warns + (warns == 1 ? " warning" : " warnings");
        string f = _filter.Text.Trim();
        var shown = _all.Where(d => (d.IsError ? _btnErrors.Checked : _btnWarnings.Checked)
            && (f.Length == 0 || d.Message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || (d.File ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(d => d.IsError ? 0 : 1).ThenBy(d => d.File == _mainFile ? "" : d.File ?? "").ThenBy(d => d.Line).Take(5000);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var d in shown)
        {
            var it = new ListViewItem(CleanMessage(d.Message), d.IsError ? 0 : 1) { Tag = d };
            it.SubItems.Add(d.File != null ? Path.GetFileName(d.File) : "(unsaved)");
            it.SubItems.Add(d.Line > 0 ? d.Line + ":" + d.Column : "");
            it.SubItems.Add(d.Source);
            it.ToolTipText = d.File;
            _list.Items.Add(it);
        }
        _list.EndUpdate();
        FitColumns();
        if (CountsChanged != null) CountsChanged(errs, warns);
    }

    static string CleanMessage(string m)
    {
        return Regex.Replace(m ?? "", @"\s+", " ").Trim();
    }

    void Open()
    {
        if (_list.SelectedItems.Count == 0) return;
        var d = (Diagnostic)_list.SelectedItems[0].Tag;
        if (d.Line > 0) _host.Navigate(d.File, d.Line, d.Column);
    }

    void CopySelected()
    {
        var lines = _list.SelectedItems.Cast<ListViewItem>().Select(it =>
        {
            var d = (Diagnostic)it.Tag;
            return string.Format("{0}({1}:{2}) {3}: {4}", d.File ?? "(unsaved)", d.Line, d.Column, d.IsError ? "error" : "warning", CleanMessage(d.Message));
        }).ToArray();
        if (lines.Length > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }
}

/// <summary>A details ListView with a themed header and rows (the native header ignores colours).</summary>
internal class ThemedListView : ListView
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string app, string id);

    public ThemedListView()
    {
        OwnerDraw = true;
        DoubleBuffered = true;
        HandleCreated += (s, e) => { try { SetWindowTheme(Handle, WbTheme.Current.IsDark ? "DarkMode_ItemsView" : "ItemsView", null); } catch { } };
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (var bg = new SolidBrush(WbTheme.Mantle)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(WbTheme.Surface0)) e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
            (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, r, WbTheme.Subtext0, flags);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        bool sel = e.Item.Selected;
        using (var bg = new SolidBrush(sel ? (Focused ? WbTheme.Selection : WbTheme.Surface0) : BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
        var r = e.Bounds;
        int x = r.X + 4;
        if (e.ColumnIndex == 0 && SmallImageList != null && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < SmallImageList.Images.Count)
        {
            e.Graphics.DrawImage(SmallImageList.Images[e.Item.ImageIndex], x, r.Y + (r.Height - 16) / 2, 16, 16);
            x += 20;
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
            (Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        Color fg = e.ColumnIndex == 0 ? WbTheme.Text : WbTheme.Subtext0;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, new Rectangle(x, r.Y, Math.Max(0, r.Right - x - 4), r.Height), fg, flags);
    }
}

/// <summary>Output of script runs and flow logs. Lines like `C:\x.ahk (12) : ==> ...` are clickable.</summary>
internal class ConsolePanel : ToolPanel
{
    readonly IWorkbenchHost _host;
    readonly RichTextBox _box;
    readonly ToolStripLabel _title;
    static readonly Regex FileLine = new Regex(@"(?<file>[A-Za-z]:\\[^\r\n():*?""<>|]+?\.ahk\d?)\s*\((?<line>\d+)\)", RegexOptions.Compiled);

    public ConsolePanel(IWorkbenchHost host) : base("Console")
    {
        _host = host;
        _title = new ToolStripLabel("") { ForeColor = WbTheme.Subtext0 };
        Bar.Items.Add(_title);
        BarButton(Glyphs.Clear, "Clear", () => _box.Clear(), true);
        BarButton(Glyphs.Copy, "Copy all", () => { if (_box.TextLength > 0) Clipboard.SetText(_box.Text); }, true);
        _box = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = WbTheme.MonoSmall, WordWrap = false,
            DetectUrls = false, HideSelection = false, ScrollBars = RichTextBoxScrollBars.Both
        };
        _box.MouseDoubleClick += (s, e) => OpenLineAt(_box.GetCharIndexFromPosition(e.Location));
        Controls.Add(_box);
        _box.BringToFront();
        ApplyTheme();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        if (_box != null) { _box.BackColor = WbTheme.Mantle; _box.ForeColor = WbTheme.Text; }
    }

    public void Begin(string title)
    {
        _title.Text = title;
        _box.Clear();
    }

    public void Write(string text, Color color)
    {
        if (IsDisposed) return;
        if (_box.TextLength > 2000000) _box.Clear();
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = color;
        _box.AppendText(text + "\n");
        _box.SelectionColor = _box.ForeColor;
        _box.ScrollToCaret();
    }

    public void WriteLog(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            Color c = l.Contains("❌") || l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ? WbTheme.Red
                : l.Contains("⚠") || l.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 ? WbTheme.Yellow
                : l.Contains("✅") || l.Contains("complete") ? WbTheme.Green : WbTheme.Subtext1;
            Write(l, c);
        }
    }

    void OpenLineAt(int charIndex)
    {
        int line = _box.GetLineFromCharIndex(charIndex);
        if (line < 0 || line >= _box.Lines.Length) return;
        var m = FileLine.Match(_box.Lines[line]);
        if (m.Success) _host.Navigate(m.Groups["file"].Value.Trim(), int.Parse(m.Groups["line"].Value), 1);
    }
}
