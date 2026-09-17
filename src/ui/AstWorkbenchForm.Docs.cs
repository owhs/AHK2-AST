// Documents: open/new/save/close, which script is "active", navigation to file:line (opening include files as
// needed), drag & drop, and restoring the session (open files + panel layout).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal class TraceVisualizerDockContent : DockContent
{
    protected override string GetPersistString() { return "TraceVisualizer"; }
}

internal partial class AstWorkbenchForm
{
    private ScriptDocument _lastScript;
    static readonly string LayoutFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DockLayout.v2.xml");

    List<DockContent> Documents() { return _dockPanel.Documents.OfType<DockContent>().ToList(); }
    IEnumerable<ScriptDocument> Scripts() { return _dockPanel.Documents.OfType<ScriptDocument>(); }

    /// <summary>The script tab last activated (stays set while an output or tool tab is in front).</summary>
    ScriptDocument ActiveScript
    {
        get
        {
            var d = _dockPanel.ActiveDocument as ScriptDocument;
            if (d != null) return d;
            return _lastScript != null && !_lastScript.IsDisposed && _lastScript.DockPanel != null ? _lastScript : null;
        }
    }

    /// <summary>The script that gets parsed and that flows run on: the active script, or the script that
    /// includes it when it was opened by following an #Include.</summary>
    ScriptDocument AnalysisTarget
    {
        get
        {
            var s = ActiveScript;
            if (s == null) return null;
            if (s.OwnerPath != null)
            {
                var owner = FindScript(s.OwnerPath);
                if (owner != null) return owner;
            }
            return s;
        }
    }

    CodeEditor ActiveEditor
    {
        get
        {
            var d = _dockPanel.ActiveDocument;
            if (d is ScriptDocument) return ((ScriptDocument)d).Editor;
            if (d is OutputDocument) return ((OutputDocument)d).Editor;
            return null;
        }
    }

    IEnumerable<CodeEditor> AllEditors()
    {
        foreach (var d in _dockPanel.Documents)
        {
            if (d is ScriptDocument) yield return ((ScriptDocument)d).Editor;
            else if (d is OutputDocument) yield return ((OutputDocument)d).Editor;
        }
    }

    public ScriptDocument FindScript(string path)
    {
        if (path == null) return null;
        return Scripts().FirstOrDefault(d => !d.IsUntitled && string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    ScriptDocument AddScript(ScriptDocument d)
    {
        d.Editor.WordWrap = _state.WordWrap;
        d.Editor.Zoom = _state.EditorZoom;
        d.Edited += OnScriptEdited;
        d.CaretMoved += OnCaretMoved;
        d.Saved += s => { AddRecent(s.FilePath); OnScriptSaved(s); };
        d.FormClosed += (s, e) => OnScriptClosed(d);
        foreach (var w in _dockPanel.Documents.OfType<WelcomeDocument>().ToList()) w.Close();
        d.Show(_dockPanel, DockState.Document);
        return d;
    }

    void NewScript()
    {
        var d = AddScript(new ScriptDocument(this, null));
        d.Editor.SetContent("#Requires AutoHotkey v2.0\r\n\r\n");
        d.Editor.Selection.Start = new FastColoredTextBoxNS.Place(0, 2);
        d.Activate();
        d.Editor.Focus();
    }

    void OpenFileDialog()
    {
        using (var dlg = new OpenFileDialog { Filter = "AutoHotkey scripts (*.ahk;*.ah2)|*.ahk;*.ah2|All files (*.*)|*.*", Title = "Open script", Multiselect = true })
        {
            var t = AnalysisTarget;
            if (t != null && !t.IsUntitled) dlg.InitialDirectory = Path.GetDirectoryName(t.FilePath);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dlg.FileNames) OpenFile(f);
        }
    }

    public ScriptDocument OpenFile(string path, string ownerPath = null)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { path = Path.GetFullPath(path); } catch { return null; }
        var existing = FindScript(path);
        if (existing != null) { existing.Activate(); return existing; }
        if (!File.Exists(path)) { Status("File not found: " + path, WbTheme.Red); return null; }
        try
        {
            var d = new ScriptDocument(this, path) { OwnerPath = ownerPath };
            AddScript(d);
            d.Activate();
            if (ownerPath == null) AddRecent(path);
            Status("Opened " + path, WbTheme.Subtext0);
            return d;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(CrashLog, "==== " + DateTime.Now + " open " + path + "\r\n" + ex + "\r\n\r\n"); } catch { }
            MessageBox.Show(this, "Could not open " + path + ":\n\n" + ex.Message, "Open", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    void AddRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_state.RecentFiles == null) _state.RecentFiles = new List<string>();
        _state.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _state.RecentFiles.Insert(0, path);
        if (_state.RecentFiles.Count > 15) _state.RecentFiles.RemoveRange(15, _state.RecentFiles.Count - 15);
        _state.Save();
    }

    void SaveAll()
    {
        foreach (var d in Scripts().Where(x => x.IsDirty).ToList()) if (!d.Save()) return;
    }

    void CloseActiveTab()
    {
        var d = _dockPanel.ActiveDocument as DockContent;
        if (d != null) d.Close();
    }

    public void OpenAsNewScript(string text, string suggestedPath)
    {
        var d = AddScript(new ScriptDocument(this, null) { SuggestedPath = suggestedPath });
        d.Editor.SetContent(text);
        d.Editor.IsChanged = true;
        d.Activate();
    }

    // ── Active document tracking ───────────────────────────────────────────────────────────────────────

    void OnActiveDocumentChanged()
    {
        var sd = _dockPanel.ActiveDocument as ScriptDocument;
        var prevTarget = _lastTarget;
        if (sd != null) _lastScript = sd;
        var target = AnalysisTarget;
        _statusScript.Text = target == null ? "" : "▸ " + target.DisplayName;
        Text = target == null ? "AHK2 AST Workbench" : target.DisplayName + " — AHK2 AST Workbench";
        UpdateToolbar();
        if (target != prevTarget)
        {
            _lastTarget = target;
            if (target == null) ClearAnalysis();
            else RequestParse(0);
        }
        else if (sd != null) ShowDiagnosticsIn(sd);
        UpdateCaretStatus();
    }

    ScriptDocument _lastTarget;

    void OnScriptClosed(ScriptDocument d)
    {
        if (_lastScript == d) _lastScript = Scripts().FirstOrDefault(x => x != d);
        if (IsHandleCreated && !IsDisposed && !Disposing) BeginInvoke((Action)OnActiveDocumentChanged);
    }

    void OnScriptSaved(ScriptDocument d)
    {
        // an include was saved: the script that includes it sees the change only after a re-parse
        var t = AnalysisTarget;
        if (t != null && t != d) RequestParse(0);
    }

    // ── Navigation ─────────────────────────────────────────────────────────────────────────────────────

    public void Navigate(string file, int line, int col, int endLine = 0, int endCol = 0)
    {
        ScriptDocument doc;
        var target = AnalysisTarget;
        if (file == null || (target != null && !target.IsUntitled && string.Equals(file, target.FilePath, StringComparison.OrdinalIgnoreCase)) || (target != null && target.IsUntitled && file == null))
            doc = target;
        else
            doc = FindScript(file) ?? OpenFile(file, target != null ? target.FilePath : null);
        if (doc == null) return;
        if (_dockPanel.ActiveDocument != doc) doc.Activate();
        _navigating = true;
        try { doc.Editor.SelectSpan(line, Math.Max(1, col), endLine > 0 ? endLine : line, endCol > 0 ? endCol : Math.Max(1, col)); }
        finally { _navigating = false; }
        doc.Editor.Focus();
        SyncAstToCaret(doc);
    }

    bool _navigating;

    // ── Session ────────────────────────────────────────────────────────────────────────────────────────

    void SaveSession()
    {
        if (_tourDir != null) return;
        _state.Session = Scripts().Where(d => !d.IsUntitled).Select(d => d.FilePath).ToList();
        var a = ActiveScript;
        _state.ActiveFile = a != null ? a.FilePath : null;
        _state.Save();
        try { _dockPanel.SaveAsXml(LayoutFile); } catch { }
    }

    void RestoreSession()
    {
        if (_tourDir != null) return;
        bool layoutOk = false;
        if (File.Exists(LayoutFile))
        {
            try
            {
                foreach (var c in _dockPanel.Contents.OfType<DockContent>().ToList()) c.DockPanel = null;
                _dockPanel.LoadFromXml(LayoutFile, PersistToContent);
                layoutOk = true;
            }
            catch
            {
                foreach (var c in _dockPanel.Contents.OfType<DockContent>().ToList()) c.DockPanel = null;
            }
            // panels the saved layout didn't have (new ones) go to their default place
            if (layoutOk && (_outline.DockPanel == null || _problems.DockPanel == null || _astPanel.DockPanel == null || _console.DockPanel == null))
            { foreach (var c in new DockContent[] { _outline, _astPanel, _problems, _console }) c.DockPanel = null; DefaultLayout(); }
        }
        if (!layoutOk && _outline.DockPanel == null) DefaultLayout();
        // files that were open but aren't in the layout (e.g. layout missing)
        foreach (var p in (_state.Session ?? new List<string>()).Where(File.Exists))
            if (FindScript(p) == null) OpenFile(p);
        var active = FindScript(_state.ActiveFile);
        if (active != null) active.Activate();
    }

    IDockContent PersistToContent(string s)
    {
        if (s == "OutlinePanel") return _outline;
        if (s == "AstPanel") return _astPanel;
        if (s == "ProblemsPanel") return _problems;
        if (s == "ConsolePanel") return _console;
        if (s == "TraceVisualizer") return _traceVisualizerContent;
        if (s.StartsWith("script|"))
        {
            string path = s.Substring(7);
            if (!File.Exists(path) || FindScript(path) != null) return null;
            try
            {
                var d = new ScriptDocument(this, path);
                d.Editor.WordWrap = _state.WordWrap;
                d.Editor.Zoom = _state.EditorZoom;
                d.Edited += OnScriptEdited;
                d.CaretMoved += OnCaretMoved;
                d.Saved += x => { AddRecent(x.FilePath); OnScriptSaved(x); };
                d.FormClosed += (o, e) => OnScriptClosed(d);
                return d;
            }
            catch { return null; }
        }
        return null;
    }

    // ── Drag & drop ────────────────────────────────────────────────────────────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AllowDrop = true;
        _dockPanel.AllowDrop = true;
        DragEventHandler enter = (s, a) => { if (a.Data.GetDataPresent(DataFormats.FileDrop)) a.Effect = DragDropEffects.Copy; };
        DragEventHandler drop = (s, a) =>
        {
            var files = a.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) foreach (var f in files) if (File.Exists(f)) OpenFile(f);
        };
        DragEnter += enter; DragDrop += drop;
        _dockPanel.DragEnter += enter; _dockPanel.DragDrop += drop;
    }
}
