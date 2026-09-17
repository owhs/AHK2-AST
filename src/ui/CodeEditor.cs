// The code editor used for scripts and flow output: FastColoredTextBox with AHK v2 colouring, folding, bracket
// matching, find/replace (Ctrl+F / Ctrl+H), go to line (Ctrl+G), Ctrl+wheel zoom and diagnostic squiggles with
// hover tooltips.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FastColoredTextBoxNS;

internal class Diagnostic
{
    public bool IsError;
    public string Message;
    public string File;     // full path, null = the parsed text itself
    public int Line;        // 1-based, 0 = unknown
    public int Column;      // 1-based
    public int Length;      // 0 = to the end of the token
    public string Source = "parser";
}

internal class CodeEditor : FastColoredTextBox, IThemed
{
    readonly AhkHighlighter _highlighter;
    readonly WavyLineStyle _errStyle = new WavyLineStyle(255, Color.Red);
    readonly WavyLineStyle _warnStyle = new WavyLineStyle(255, Color.Gold);
    readonly MarkerStyle _flashStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(70, Color.Gold)));
    List<Diagnostic> _diags = new List<Diagnostic>();
    readonly Dictionary<int, bool> _diagLines = new Dictionary<int, bool>(); // 0-based line -> has an error (else warning)
    readonly Timer _flashTimer = new Timer { Interval = 900 };

    public bool Plain { get; private set; }

    public CodeEditor(bool readOnly, bool plain = false)
    {
        Plain = plain;
        Dock = DockStyle.Fill;
        // its own Font object: zooming disposes the previous font, which must not be the shared theme font
        Font = new Font(WbTheme.MonoFont.FontFamily, WbTheme.MonoFont.Size, WbTheme.MonoFont.Style);
        ReadOnly = readOnly;
        BorderStyle = BorderStyle.None;
        TabLength = 4;
        AutoIndent = true;
        AutoIndentChars = false;
        ShowFoldingLines = false;
        HighlightFoldingIndicator = true;
        LeftBracket = '('; RightBracket = ')';
        LeftBracket2 = '{'; RightBracket2 = '}';
        BracketsHighlightStrategy = BracketsHighlightStrategy.Strategy2;
        AllowSeveralTextStyleDrawing = true;
        DelayedEventsInterval = 250;
        DelayedTextChangedInterval = 250;
        Paddings = new Padding(0, 2, 0, 2);
        AutoCompleteBrackets = !readOnly;
        AutoCompleteBracketsList = new[] { '(', ')', '{', '}', '[', ']', '"', '"' };
        ToolTipDelay = 350;
        Cursor = Cursors.IBeam;

        if (!plain) _highlighter = new AhkHighlighter(this);
        AddStyle(_warnStyle);
        AddStyle(_errStyle);
        AddStyle(_flashStyle);

        AutoIndentNeeded += OnAutoIndentNeeded;
        PaintLine += OnPaintLine;
        ToolTipNeeded += OnToolTipNeeded;
        _flashTimer.Tick += (s, e) => { _flashTimer.Stop(); Range.ClearStyle(_flashStyle); };

        // Ctrl+/ toggles ; comments like other editors
        HotkeysMapping[Keys.Control | Keys.OemQuestion] = FCTBAction.CommentSelected;
        CommentPrefix = ";";
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = WbTheme.Base;
        ForeColor = WbTheme.Text;
        IndentBackColor = WbTheme.Base;
        PaddingBackColor = WbTheme.Base;
        LineNumberColor = WbTheme.Overlay0;
        ServiceLinesColor = WbTheme.Base;
        CurrentLineColor = Color.FromArgb(WbTheme.Current.IsDark ? 40 : 60, WbTheme.Surface1);
        SelectionColor = Color.FromArgb(120, WbTheme.Accent);
        CaretColor = WbTheme.Rosewater;
        FoldingIndicatorColor = WbTheme.Accent;
        BracketsStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(90, WbTheme.Overlay0)));
        BracketsStyle2 = new MarkerStyle(new SolidBrush(Color.FromArgb(90, WbTheme.Overlay0)));
        ServiceColors.CollapseMarkerForeColor = WbTheme.Subtext0;
        ServiceColors.CollapseMarkerBackColor = WbTheme.Base;
        ServiceColors.CollapseMarkerBorderColor = WbTheme.Surface2;
        ServiceColors.ExpandMarkerForeColor = WbTheme.Subtext0;
        ServiceColors.ExpandMarkerBackColor = WbTheme.Base;
        ServiceColors.ExpandMarkerBorderColor = WbTheme.Surface2;
        FoldedBlockStyle = new FoldedBlockStyle(new SolidBrush(WbTheme.Subtext0), new SolidBrush(WbTheme.Surface0), FontStyle.Regular);
        SetWavyColor(_errStyle, WbTheme.Red);
        SetWavyColor(_warnStyle, WbTheme.Yellow);
        if (_highlighter != null) _highlighter.ApplyTheme();
        UiTheming.DarkScrollbars(this);
        Invalidate();
    }

    static void SetWavyColor(WavyLineStyle st, Color c)
    {
        // WavyLineStyle keeps its pen private; rebuild through reflection-free public ctor semantics
        var f = typeof(WavyLineStyle).GetField("Pen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (f != null) f.SetValue(st, new Pen(c));
        else
        {
            var p = typeof(WavyLineStyle).GetProperty("Pen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (p != null && p.CanWrite) p.SetValue(st, new Pen(c), null);
        }
    }

    /// <summary>Replaces all text and re-colours it in one pass (much faster than incremental for big files).</summary>
    public void SetContent(string text, bool keepView = false)
    {
        int line = Selection.Start.iLine, ch = Selection.Start.iChar, top = VerticalScroll.Value;
        BeginUpdate();
        try
        {
            Text = text ?? "";
            ClearUndo();
            if (_highlighter != null) _highlighter.HighlightAll();
            if (keepView)
            {
                line = Math.Min(line, LinesCount - 1);
                Selection.Start = new Place(Math.Min(ch, GetLineLength(Math.Max(0, line))), Math.Max(0, line));
            }
            else Selection.Start = Place.Empty;
        }
        finally { EndUpdate(); }
        if (keepView) { VerticalScroll.Value = Math.Min(top, VerticalScroll.Maximum); UpdateScrollbars(); }
        IsChanged = false;
        Invalidate();
    }

    // ── Navigation ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Selects from (line, col) to (endLine, endCol), all 1-based, and scrolls it into view.</summary>
    public void SelectSpan(int line, int col, int endLine, int endCol, bool flash = true)
    {
        if (LinesCount == 0 || line <= 0) return;
        int l0 = Math.Min(line, LinesCount) - 1;
        int c0 = Math.Max(0, Math.Min(col - 1, GetLineLength(l0)));
        int l1 = Math.Min(Math.Max(endLine, line), LinesCount) - 1;
        int c1 = Math.Max(0, Math.Min(endCol - 1, GetLineLength(l1)));
        if (l1 == l0 && c1 < c0) c1 = c0;
        var r = new Range(this, new Place(c0, l0), new Place(c1, l1));
        ExpandBlock(l0);
        Selection = new Range(this, r.Start, r.Start);
        DoRangeVisible(r, true);
        Selection = r;
        Invalidate();
        if (flash)
        {
            Range.ClearStyle(_flashStyle);
            var fr = r.Start == r.End ? new Range(this, new Place(0, l0), new Place(GetLineLength(l0), l0)) : r;
            fr.SetStyle(_flashStyle);
            _flashTimer.Stop(); _flashTimer.Start();
        }
    }

    public void GoToLine(int line, int col = 1)
    {
        SelectSpan(line, col, line, col);
    }

    /// <summary>1-based caret line and column.</summary>
    public int CaretLine { get { return Selection.Start.iLine + 1; } }
    public int CaretColumn { get { return Selection.Start.iChar + 1; } }

    // ── Diagnostics ──────────────────────────────────────────────────────────────────────────────────

    public void SetDiagnostics(IEnumerable<Diagnostic> diags)
    {
        _diags = diags == null ? new List<Diagnostic>() : diags.ToList();
        Range.ClearStyle(_errStyle, _warnStyle);
        _diagLines.Clear();
        foreach (var d in _diags)
        {
            if (d.Line <= 0 || d.Line > LinesCount) continue;
            int l = d.Line - 1;
            bool prev;
            _diagLines[l] = d.IsError || (_diagLines.TryGetValue(l, out prev) && prev);
            int len = GetLineLength(l);
            int c0 = Math.Max(0, Math.Min(d.Column - 1, len));
            int c1 = d.Length > 0 ? Math.Min(len, c0 + d.Length) : TokenEnd(l, c0);
            if (c1 <= c0) { c0 = FirstNonBlank(l); c1 = len; }
            if (c1 <= c0) continue;
            new Range(this, new Place(c0, l), new Place(c1, l)).SetStyle(d.IsError ? (Style)_errStyle : _warnStyle);
        }
        Invalidate();
    }

    /// <summary>Lines with a problem: a faint tint behind the text and a dot in the gutter.</summary>
    void OnPaintLine(object sender, PaintLineEventArgs e)
    {
        bool isError;
        if (!_diagLines.TryGetValue(e.LineIndex, out isError)) return;
        Color c = isError ? WbTheme.Red : WbTheme.Yellow;
        using (var tint = new SolidBrush(Color.FromArgb(WbTheme.Current.IsDark ? 28 : 40, c)))
            e.Graphics.FillRectangle(tint, e.LineRect);
        using (var dot = new SolidBrush(c))
        {
            float d = Math.Max(5f, CharHeight * 0.4f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(dot, 3, e.LineRect.Y + (e.LineRect.Height - d) / 2, d, d);
        }
    }

    int FirstNonBlank(int l)
    {
        var line = this[l];
        for (int i = 0; i < line.Count; i++) if (!char.IsWhiteSpace(line[i].c)) return i;
        return 0;
    }

    int TokenEnd(int l, int c0)
    {
        var line = this[l];
        int i = c0;
        if (i < line.Count && (char.IsLetterOrDigit(line[i].c) || line[i].c == '_'))
            while (i < line.Count && (char.IsLetterOrDigit(line[i].c) || line[i].c == '_')) i++;
        else if (i < line.Count) i++;
        return i;
    }

    void OnToolTipNeeded(object sender, ToolTipNeededEventArgs e)
    {
        int line = e.Place.iLine + 1;
        var hits = _diags.Where(d => d.Line == line).ToList();
        if (hits.Count == 0) return;
        e.ToolTipTitle = hits.Any(h => h.IsError) ? "Error" : "Warning";
        e.ToolTipText = string.Join("\n", hits.Select(h => h.Message).ToArray());
        e.ToolTipIcon = hits.Any(h => h.IsError) ? ToolTipIcon.Error : ToolTipIcon.Warning;
    }

    // ── Auto-indent: one level after `{`, back one on `}` ────────────────────────────────────────────────

    void OnAutoIndentNeeded(object sender, AutoIndentEventArgs e)
    {
        string cur = e.LineText.Trim();
        if (cur.StartsWith("}")) { e.Shift = -e.TabLength; e.ShiftNextLines = -e.TabLength; return; }
        string code = StripComment(e.LineText).TrimEnd();
        if (code.EndsWith("{")) { e.ShiftNextLines = e.TabLength; return; }
    }

    static string StripComment(string s)
    {
        bool inStr = false; char q = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr) { if (c == '`') i++; else if (c == q) inStr = false; continue; }
            if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
            if (c == ';' && (i == 0 || char.IsWhiteSpace(s[i - 1]))) return s.Substring(0, i);
        }
        return s;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            Zoom = Math.Max(50, Math.Min(300, Zoom + (e.Delta > 0 ? 10 : -10)));
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _flashTimer.Dispose();
        base.Dispose(disposing);
    }
}
