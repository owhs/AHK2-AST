// Document tabs: ScriptDocument (a file you edit — the editor always holds exactly that file) and OutputDocument
// (what a flow produced, read-only, with the actions you'd take next). Plus small shared UI pieces.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

/// <summary>What documents and panels need from the main window.</summary>
internal interface IWorkbenchHost
{
    void Status(string text, Color color);
    void Navigate(string file, int line, int col, int endLine = 0, int endCol = 0);
    void CompareTexts(string leftTitle, string left, string rightTitle, string right);
    void RunScriptText(string text, string suggestedDir, string title);
    void ValidateText(string text, string suggestedDir, string title);
    void OpenAsNewScript(string text, string suggestedPath);
    void RerunFlow(OutputDocument doc);
    ScriptDocument FindScript(string path);
}

internal class ScriptDocument : DockContent, IThemed
{
    static int _untitled;

    public readonly CodeEditor Editor;
    readonly IWorkbenchHost _host;
    public string FilePath { get; private set; }
    public bool IsUntitled { get { return FilePath == null; } }
    public bool IsDirty { get { return Editor.IsChanged; } }
    /// <summary>Set when opened by following an #Include: the script that includes this file.</summary>
    public string OwnerPath;
    /// <summary>Default location offered by Save for an unsaved tab.</summary>
    public string SuggestedPath;
    readonly string _untitledName;

    Encoding _encoding = new UTF8Encoding(false);
    string _newline = "\r\n";
    FileSystemWatcher _watcher;
    DateTime _diskStamp;
    bool _externalChangePending;
    bool _suppressEdited;

    /// <summary>Text edited by the user (not by loading).</summary>
    public event Action<ScriptDocument> Edited;
    public event Action<ScriptDocument> CaretMoved;
    public event Action<ScriptDocument> Saved;

    public ScriptDocument(IWorkbenchHost host, string path)
    {
        _host = host;
        Editor = new CodeEditor(false);
        Controls.Add(Editor);
        DockAreas = DockAreas.Document | DockAreas.Float;
        if (path == null) _untitledName = "Untitled-" + (++_untitled);
        Editor.TextChanged += (s, e) => { UpdateTitle(); if (!_suppressEdited && Edited != null) Edited(this); };
        Editor.SelectionChangedDelayed += (s, e) => { if (CaretMoved != null) CaretMoved(this); };
        Editor.KeyDown += (s, e) =>
        {
            // Ctrl+S goes to the main menu; nothing to do here, but keep Ctrl+Tab for dock switching
        };
        if (path != null) Load(path);
        UpdateTitle();
        ApplyTheme();
    }

    public string DisplayName { get { return IsUntitled ? _untitledName : Path.GetFileName(FilePath); } }

    public void ApplyTheme()
    {
        BackColor = WbTheme.Base;
        Editor.ApplyTheme();
    }

    void UpdateTitle()
    {
        string t = DisplayName + (IsDirty ? " ●" : "");
        if (TabText != t) { TabText = t; Text = t; }
        ToolTipText = FilePath ?? "Not saved yet";
    }

    public void Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = Decode(bytes, out _encoding);
        _newline = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
        FilePath = Path.GetFullPath(path);
        _suppressEdited = true;
        try { Editor.SetContent(text); }
        finally { _suppressEdited = false; }
        _diskStamp = File.GetLastWriteTimeUtc(FilePath);
        Watch();
        UpdateTitle();
    }

    static string Decode(byte[] b, out Encoding enc)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = Encoding.Unicode; return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
        try { enc = new UTF8Encoding(false); return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException) { enc = Encoding.Default; return Encoding.Default.GetString(b); }
    }

    /// <summary>The text with the file's own line endings.</summary>
    public string GetText()
    {
        string t = Editor.Text;
        if (_newline == "\n") t = t.Replace("\r\n", "\n");
        return t;
    }

    public bool Save()
    {
        if (IsUntitled) return SaveAs();
        try
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            File.WriteAllText(FilePath, GetText(), _encoding);
            _diskStamp = File.GetLastWriteTimeUtc(FilePath);
            Editor.IsChanged = false;
            Editor.Invalidate();
            UpdateTitle();
            _host.Status("Saved " + FilePath, WbTheme.Green);
            if (Saved != null) Saved(this);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save " + FilePath + ":\n\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { if (_watcher != null) _watcher.EnableRaisingEvents = true; }
    }

    public bool SaveAs()
    {
        using (var dlg = new SaveFileDialog { Filter = "AutoHotkey script (*.ahk)|*.ahk|All files (*.*)|*.*", Title = "Save script as" })
        {
            if (!IsUntitled) { dlg.InitialDirectory = Path.GetDirectoryName(FilePath); dlg.FileName = Path.GetFileName(FilePath); }
            else if (SuggestedPath != null) { string dir = Path.GetDirectoryName(SuggestedPath); if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir; dlg.FileName = Path.GetFileName(SuggestedPath); }
            else dlg.FileName = _untitledName + ".ahk";
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            FilePath = Path.GetFullPath(dlg.FileName);
            Watch();
            return Save();
        }
    }

    public void Reload()
    {
        if (IsUntitled || !File.Exists(FilePath)) return;
        byte[] bytes = File.ReadAllBytes(FilePath);
        string text = Decode(bytes, out _encoding);
        _suppressEdited = true;
        try { Editor.SetContent(text, true); }
        finally { _suppressEdited = false; }
        _diskStamp = File.GetLastWriteTimeUtc(FilePath);
        _externalChangePending = false;
        UpdateTitle();
        if (Edited != null) Edited(this);
    }

    void Watch()
    {
        if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
        if (IsUntitled) return;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath), Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler h = (s, e) => { try { BeginInvoke((Action)CheckDisk); } catch { } };
            _watcher.Changed += h;
            _watcher.Created += h;
        }
        catch { _watcher = null; }
    }

    void CheckDisk()
    {
        if (IsDisposed || IsUntitled || !File.Exists(FilePath)) return;
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(FilePath); } catch { return; }
        if (stamp == _diskStamp) return;
        if (!IsDirty) { Reload(); _host.Status("Reloaded " + DisplayName + " (changed on disk)", WbTheme.Sky); }
        else { _externalChangePending = true; if (DockPanel != null && DockPanel.ActiveDocument == this) AskReload(); }
    }

    void AskReload()
    {
        if (!_externalChangePending) return;
        _externalChangePending = false;
        var r = MessageBox.Show(this, DisplayName + " was changed on disk and has unsaved edits here.\n\nReload it from disk (losing the edits here)?",
            "File changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r == DialogResult.Yes) Reload();
        else _diskStamp = File.GetLastWriteTimeUtc(FilePath);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        if (_externalChangePending) BeginInvoke((Action)AskReload);
    }

    /// <summary>Asks about unsaved edits; false = the user cancelled.</summary>
    public bool ConfirmClose()
    {
        if (!IsDirty) return true;
        var r = MessageBox.Show(this, "Save changes to " + DisplayName + "?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return Save();
        return true;
    }

    public bool ForceClose;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ForceClose && e.CloseReason == CloseReason.UserClosing && !ConfirmClose()) { e.Cancel = true; return; }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_watcher != null) _watcher.Dispose();
        base.OnFormClosed(e);
    }

    protected override string GetPersistString()
    {
        return IsUntitled ? "" : "script|" + FilePath;
    }
}

internal class OutputDocument : DockContent, IThemed
{
    readonly IWorkbenchHost _host;
    public readonly CodeEditor Editor;
    readonly ToolStrip _bar;
    readonly Label _info;

    public string FlowName { get; private set; }
    public string FlowJson { get; private set; }
    public string SourcePath { get; private set; }
    public string SourceText { get; private set; }
    public string Kind { get; private set; }
    public string OutputText { get; private set; }
    public List<string> Log = new List<string>();
    public long ElapsedMs;

    public OutputDocument(IWorkbenchHost host, string flowName, string flowJson, string sourcePath, string sourceText)
    {
        _host = host;
        FlowName = flowName; FlowJson = flowJson; SourcePath = sourcePath; SourceText = sourceText;
        DockAreas = DockAreas.Document | DockAreas.Float;

        _bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(6, 3, 6, 1), CanOverflow = true };
        AddAction(Glyphs.Save, "Save as…", "Save the output to a file", SaveAs);
        AddAction(Glyphs.Copy, null, "Copy the output to the clipboard", () => { if (!string.IsNullOrEmpty(OutputText)) Clipboard.SetText(OutputText); _host.Status("Output copied", WbTheme.Green); });
        _bar.Items.Add(new ToolStripSeparator());
        AddAction(Glyphs.Compare, "Compare", "Side-by-side diff: source vs output", () => _host.CompareTexts(SourceName, SourceText, TabText, OutputText));
        AddAction(Glyphs.Validate, "Validate", "Ask AutoHotkey to load the output without running it (/Validate)", () => _host.ValidateText(OutputText, SourceDir, TabText));
        AddAction(Glyphs.Run, "Run", "Run the output with AutoHotkey (in the source's folder)", () => _host.RunScriptText(OutputText, SourceDir, TabText));
        _bar.Items.Add(new ToolStripSeparator());
        AddAction(Glyphs.Document, null, "Open the output as a new editable script", () => _host.OpenAsNewScript(OutputText, SuggestedFileName()));
        AddAction(Glyphs.Refresh, null, "Run the flow again on the current source", () => _host.RerunFlow(this));
        _info = new Label { Dock = DockStyle.Top, Height = 22, Text = "Running…", Padding = new Padding(10, 3, 6, 0), Font = WbTheme.UISmall, AutoEllipsis = true };

        Editor = new CodeEditor(true);
        Controls.Add(Editor);
        Controls.Add(_info);
        Controls.Add(_bar);
        TabText = Text = flowName + " → " + (sourcePath != null ? Path.GetFileName(sourcePath) : "output");
        ToolTipText = flowName + " output";
        ApplyTheme();
    }

    string SourceName { get { return SourcePath != null ? Path.GetFileName(SourcePath) : "source"; } }
    string SourceDir { get { return SourcePath != null ? Path.GetDirectoryName(SourcePath) : null; } }

    void AddAction(char glyph, string text, string tip, Action a)
    {
        var b = new ToolStripButton(text ?? "") { ToolTipText = tip, Tag = glyph,
            DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText, Name = tip };
        b.Click += (s, e) => { if (OutputText != null) a(); };
        _bar.Items.Add(b);
    }

    public void ApplyTheme()
    {
        BackColor = WbTheme.Base;
        UiTheming.Apply(_bar);
        foreach (ToolStripItem it in _bar.Items)
            if (it.Tag is char) it.Image = Glyphs.Get((char)it.Tag, (char)it.Tag == Glyphs.Run ? WbTheme.Green : WbTheme.Subtext1);
        _info.BackColor = WbTheme.Mantle;
        Editor.ApplyTheme();
    }

    public void SetRunning(string text)
    {
        _info.Text = text;
        _info.ForeColor = WbTheme.Sky;
        foreach (ToolStripItem it in _bar.Items) it.Enabled = false;
    }

    /// <summary>`inputBytes`: the source plus the files it includes (flows inline them), 0 = just the source.</summary>
    public void SetResult(string output, long ms, List<string> log, string sourceText, long inputBytes = 0, int includes = 0)
    {
        OutputText = output ?? "";
        ElapsedMs = ms;
        Log = log ?? new List<string>();
        if (sourceText != null) SourceText = sourceText;
        Kind = DetectKind(OutputText);
        Editor.SetContent(OutputText);
        long inB = inputBytes > 0 ? inputBytes : Encoding.UTF8.GetByteCount(SourceText ?? ""), outB = Encoding.UTF8.GetByteCount(OutputText);
        string size = Kind == "ahk"
            ? string.Format("{0}{1} → {2} ({3}{4:0}%)", Human(inB), includes > 0 ? " with " + includes + " includes" : "", Human(outB),
                outB <= inB ? "−" : "+", inB == 0 ? 0 : Math.Abs(100.0 * (outB - inB) / inB))
            : Kind.ToUpperInvariant() + " · " + Human(outB);
        int warn = Log.Count(l => l.Contains("⚠") || l.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0);
        int err = Log.Count(l => l.Contains("❌") || l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0);
        _info.Text = string.Format("{0}  ·  {1:N0} lines  ·  {2}  ·  {3:N0} ms{4}", FlowName, Editor.LinesCount, size, ms,
            err > 0 ? "  ·  " + err + " error(s) in log" : warn > 0 ? "  ·  " + warn + " warning(s) in log" : "");
        _info.ForeColor = err > 0 ? WbTheme.Red : warn > 0 ? WbTheme.Yellow : WbTheme.Subtext1;
        foreach (ToolStripItem it in _bar.Items) it.Enabled = true;
    }

    public void SetFailed(string message)
    {
        _info.Text = FlowName + " failed: " + message;
        _info.ForeColor = WbTheme.Red;
        foreach (ToolStripItem it in _bar.Items) if (it.Name.StartsWith("Run the flow again")) it.Enabled = true;
    }

    static string Human(long b)
    {
        if (b < 1024) return b + " B";
        if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
        return (b / 1048576.0).ToString("0.00") + " MB";
    }

    public static string DetectKind(string text)
    {
        string t = (text ?? "").TrimStart();
        if (t.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "html";
        if ((t.StartsWith("{") || t.StartsWith("[")) && t.Contains("\":")) return "json";
        if (t.StartsWith("# ") || t.StartsWith("## ") || t.StartsWith("```")) return "markdown";
        if (t.Contains("import AhkStdLib") || t.StartsWith("# --- DllCall Bindings ---") || t.StartsWith("import std/")) return "nim";
        return "ahk";
    }

    string SuggestedFileName()
    {
        string ext = Kind == "html" ? ".html" : Kind == "json" ? ".json" : Kind == "markdown" ? ".md" : Kind == "nim" ? ".nim" : ".ahk";
        string baseName = SourcePath != null ? Path.GetFileNameWithoutExtension(SourcePath) : "output";
        string slug = new string(FlowName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        string name = baseName + "." + slug + ext;
        return SourcePath != null ? Path.Combine(Path.GetDirectoryName(SourcePath), name) : name;
    }

    void SaveAs()
    {
        string suggested = SuggestedFileName();
        using (var dlg = new SaveFileDialog { Title = "Save output", FileName = Path.GetFileName(suggested), Filter = "All files (*.*)|*.*" })
        {
            string dir = Path.GetDirectoryName(suggested);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (SourcePath != null && string.Equals(Path.GetFullPath(dlg.FileName), SourcePath, StringComparison.OrdinalIgnoreCase)
                && MessageBox.Show(this, "This overwrites the source script itself. Continue?", "Save output", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            File.WriteAllText(dlg.FileName, OutputText, new UTF8Encoding(false));
            _host.Status("Saved " + dlg.FileName, WbTheme.Green);
        }
    }

    protected override string GetPersistString() { return ""; }
}

/// <summary>A TextBox with grey placeholder text.</summary>
internal class SearchBox : TextBox
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    string _cue;
    public string Cue
    {
        get { return _cue; }
        set { _cue = value; if (IsHandleCreated) SendMessage(Handle, 0x1501, (IntPtr)1, value); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_cue != null) SendMessage(Handle, 0x1501, (IntPtr)1, _cue);
    }

    public SearchBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        Font = WbTheme.UIFont;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && Text.Length > 0) { Text = ""; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>A dock panel with a thin header toolbar and a body.</summary>
internal class ToolPanel : DockContent, IThemed
{
    protected readonly ToolStrip Bar;

    public ToolPanel(string title)
    {
        TabText = Text = title;
        HideOnClose = true;
        DockAreas = DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockBottom | DockAreas.DockTop | DockAreas.Float | DockAreas.Document;
        Bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(4, 2, 4, 2), CanOverflow = true };
        Controls.Add(Bar);
    }

    protected ToolStripButton BarButton(char glyph, string tip, Action a, bool right = false, string text = null)
    {
        var b = new ToolStripButton(text ?? "") { ToolTipText = tip, Tag = glyph, DisplayStyle = text == null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText };
        if (right) b.Alignment = ToolStripItemAlignment.Right;
        b.Click += (s, e) => a();
        Bar.Items.Add(b);
        return b;
    }

    public virtual void ApplyTheme()
    {
        BackColor = WbTheme.Base;
        UiTheming.Apply(Bar);
        foreach (ToolStripItem it in Bar.Items)
        {
            if (it.Tag is char) it.Image = Glyphs.Get((char)it.Tag, WbTheme.Subtext1);
            var host = it as ToolStripControlHost;
            if (host != null) { host.Control.BackColor = WbTheme.Surface0; host.Control.ForeColor = WbTheme.Text; }
        }
        foreach (Control c in Controls) if (c != Bar) UiTheming.Apply(c);
    }

    /// <summary>A search box hosted in the header bar (ToolStripControlHost ignores the control's width unless told).</summary>
    protected ToolStripControlHost BarSearch(SearchBox box, int width, bool right = false)
    {
        box.Width = width;
        var host = new ToolStripControlHost(box) { AutoSize = false, Width = width, Margin = new Padding(2, 2, 6, 2) };
        if (right) host.Alignment = ToolStripItemAlignment.Right;
        Bar.Items.Add(host);
        return host;
    }

    protected override string GetPersistString() { return GetType().Name; }
}
