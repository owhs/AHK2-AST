// Commands (one registry feeds the menus, the toolbar, keyboard shortcuts and the command palette), the menu
// bar, toolbar, status bar, themes and the welcome page.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal class WbCommand
{
    public string Id, Title, Category;
    public Keys Keys;
    public char Glyph;
    public Action Run;
    public Func<bool> Enabled;
    public string KeyText
    {
        get
        {
            if (Keys == Keys.None) return "";
            return new KeysConverter().ConvertToString(Keys).Replace("OemQuestion", "/").Replace("Oem2", "/").Replace("Oemplus", "+")
                .Replace("OemMinus", "-").Replace("D0", "0").Replace("Return", "Enter");
        }
    }
}

internal partial class AstWorkbenchForm
{
    private readonly Dictionary<string, WbCommand> _cmds = new Dictionary<string, WbCommand>();
    private readonly List<WbCommand> _cmdOrder = new List<WbCommand>();
    private ToolStripMenuItem _recentMenu, _themeMenu, _flowsMenu;
    private ToolStripSplitButton _flowButton;
    private List<FlowEntry> _flows = new List<FlowEntry>();
    private FlowEntry _currentFlow;

    WbCommand Def(string id, string category, string title, Keys keys, char glyph, Action run, Func<bool> enabled = null)
    {
        var c = new WbCommand { Id = id, Category = category, Title = title, Keys = keys, Glyph = glyph, Run = run, Enabled = enabled };
        _cmds[id] = c;
        _cmdOrder.Add(c);
        return c;
    }

    void Exec(string id)
    {
        WbCommand c;
        if (!_cmds.TryGetValue(id, out c)) return;
        if (c.Enabled != null && !c.Enabled()) return;
        c.Run();
    }

    void DefineCommands()
    {
        Func<bool> hasScript = () => ActiveScript != null;
        Func<bool> hasTarget = () => AnalysisTarget != null;

        Def("file.new", "File", "New script", Keys.Control | Keys.N, Glyphs.New, NewScript);
        Def("file.open", "File", "Open…", Keys.Control | Keys.O, Glyphs.Open, OpenFileDialog);
        Def("file.save", "File", "Save", Keys.Control | Keys.S, Glyphs.Save, () => ActiveScript.Save(), hasScript);
        Def("file.saveas", "File", "Save as…", Keys.Control | Keys.Shift | Keys.S, Glyphs.Save, () => ActiveScript.SaveAs(), hasScript);
        Def("file.saveall", "File", "Save all", Keys.Control | Keys.Alt | Keys.S, Glyphs.SaveAll, SaveAll, () => Scripts().Any(d => d.IsDirty));
        Def("file.close", "File", "Close tab", Keys.Control | Keys.W, Glyphs.Close, CloseActiveTab, () => _dockPanel.ActiveDocument != null);
        Def("file.reveal", "File", "Show in Explorer", Keys.None, Glyphs.Folder, () => { var s = ActiveScript; if (s != null && !s.IsUntitled) Process.Start("explorer.exe", "/select,\"" + s.FilePath + "\""); }, () => ActiveScript != null && !ActiveScript.IsUntitled);
        Def("file.copypath", "File", "Copy file path", Keys.None, Glyphs.Copy, () => Clipboard.SetText(ActiveScript.FilePath), () => ActiveScript != null && !ActiveScript.IsUntitled);
        Def("file.exit", "File", "Exit", Keys.None, '\0', Close);

        Def("edit.find", "Edit", "Find…", Keys.Control | Keys.F, Glyphs.Search, () => { var e = ActiveEditor; if (e != null) e.ShowFindDialog(); }, () => ActiveEditor != null);
        Def("edit.replace", "Edit", "Replace…", Keys.Control | Keys.H, Glyphs.Edit, () => { var e = ActiveEditor; if (e != null && !e.ReadOnly) e.ShowReplaceDialog(); }, () => ActiveEditor != null);
        Def("edit.goto", "Edit", "Go to line…", Keys.Control | Keys.G, '\0', () => ShowPalette(":"), () => ActiveEditor != null);
        Def("edit.symbol", "Edit", "Go to symbol…", Keys.Control | Keys.Shift | Keys.O, Glyphs.Function, () => ShowPalette("@"));
        Def("edit.comment", "Edit", "Toggle line comment", Keys.Control | Keys.OemQuestion, '\0', () => { var e = ActiveEditor; if (e != null && !e.ReadOnly) e.CommentSelected(); }, () => ActiveEditor != null);
        Def("edit.foldall", "Edit", "Fold all", Keys.None, Glyphs.Collapse, () => { var e = ActiveEditor; if (e != null) e.CollapseAllFoldingBlocks(); }, () => ActiveEditor != null);
        Def("edit.unfoldall", "Edit", "Unfold all", Keys.None, Glyphs.Expand, () => { var e = ActiveEditor; if (e != null) e.ExpandAllFoldingBlocks(); }, () => ActiveEditor != null);

        Def("view.palette", "View", "Command palette…", Keys.Control | Keys.Shift | Keys.P, Glyphs.Search, () => ShowPalette(""));
        Def("view.outline", "View", "Outline", Keys.Control | Keys.Shift | Keys.L, Glyphs.List, () => ShowPanel(_outline));
        Def("view.ast", "View", "AST", Keys.Control | Keys.Shift | Keys.A, Glyphs.Tree, () => ShowPanel(_astPanel));
        Def("view.problems", "View", "Problems", Keys.Control | Keys.Shift | Keys.M, Glyphs.Warning, () => ShowPanel(_problems));
        Def("view.console", "View", "Console", Keys.Control | Keys.Shift | Keys.U, Glyphs.List, () => ShowPanel(_console));
        Def("view.trace", "View", "Trace visualizer", Keys.Control | Keys.T, Glyphs.Trace, ShowTraceVisualizer);
        Def("view.wrap", "View", "Word wrap", Keys.Alt | Keys.Z, '\0', ToggleWordWrap);
        Def("view.zoomin", "View", "Zoom in", Keys.Control | Keys.Oemplus, '\0', () => Zoom(10));
        Def("view.zoomout", "View", "Zoom out", Keys.Control | Keys.OemMinus, '\0', () => Zoom(-10));
        Def("view.zoomreset", "View", "Reset zoom", Keys.Control | Keys.D0, '\0', () => Zoom(0));
        Def("view.layout", "View", "Reset layout", Keys.None, '\0', ResetLayout);
        Def("view.welcome", "View", "Welcome page", Keys.None, Glyphs.Info, ShowWelcome);

        Def("script.run", "Script", "Run script", Keys.F5, Glyphs.Run, RunActive, hasTarget);
        Def("script.stop", "Script", "Stop", Keys.Shift | Keys.F5, Glyphs.Stop, () => StopRun(true), () => IsRunning);
        Def("script.validate", "Script", "Validate with AutoHotkey", Keys.F7, Glyphs.Validate, ValidateActive, hasTarget);
        Def("script.parse", "Script", "Re-parse now", Keys.Control | Keys.R, Glyphs.Refresh, () => RequestParse(0), hasTarget);
        Def("script.live", "Script", "Parse while typing", Keys.None, '\0', () => { _state.LiveParse = !_state.LiveParse; _state.Save(); UpdateMenuChecks(); });
        Def("script.includes", "Script", "Follow #Include files", Keys.None, '\0', () => { _state.FollowIncludes = !_state.FollowIncludes; _state.Save(); UpdateMenuChecks(); RequestParse(0); });
        Def("script.ahkpath", "Script", "Set AutoHotkey path…", Keys.None, Glyphs.Settings, () => PickAhkPath());

        Def("flow.run", "Flow", "Run current flow", Keys.F6, Glyphs.Flow, () => RunFlow(_currentFlow), () => hasTarget() && _currentFlow != null);
        Def("flow.pick", "Flow", "Run a flow…", Keys.Control | Keys.Shift | Keys.F6, Glyphs.Flow, () => ShowPalette("flow "), hasTarget);
        Def("flow.new", "Flow", "New flow…", Keys.None, Glyphs.New, () => OpenFlowEditor(null));
        Def("flow.edit", "Flow", "Edit current flow…", Keys.None, Glyphs.Edit, () => OpenFlowEditor(_currentFlow), () => _currentFlow != null);
        Def("flow.folder", "Flow", "Open flows folder", Keys.None, Glyphs.Folder, () => { Directory.CreateDirectory(FlowCatalog.UserDir); Process.Start("explorer.exe", "\"" + FlowCatalog.UserDir + "\""); });
        Def("flow.refresh", "Flow", "Reload flows", Keys.None, Glyphs.Refresh, ReloadFlows);

        Def("tools.compare", "Tools", "Compare workspace", Keys.None, Glyphs.Compare, () => ShowDoc(GetOrCreateDiff()));
        Def("tools.compareDisk", "Tools", "Compare with file on disk", Keys.None, Glyphs.Compare, CompareWithDisk, () => ActiveScript != null && !ActiveScript.IsUntitled);
        Def("tools.escape", "Tools", "String escape tool", Keys.None, Glyphs.Code, () => ShowDoc(GetOrCreateEscape()));
        Def("tools.nim", "Tools", "Nim build manager", Keys.Control | Keys.B, Glyphs.Build, () => OpenNimBuildManager(), () => HasNimPlugin);

        Def("help.about", "Help", "About", Keys.None, Glyphs.Info, () => { using (var f = new AboutForm()) f.ShowDialog(this); });
        Def("help.keys", "Help", "Keyboard shortcuts", Keys.None, Glyphs.Keyboard, () => ShowPalette(""));
    }

    // ── Menu ───────────────────────────────────────────────────────────────────────────────────────────

    ToolStripMenuItem Item(string id)
    {
        var c = _cmds[id];
        var mi = new ToolStripMenuItem(c.Title) { Tag = c, ShortcutKeyDisplayString = c.KeyText };
        if (c.Glyph != '\0') mi.Image = Glyphs.Get(c.Glyph, WbTheme.Subtext1);
        mi.Click += (s, e) => Exec(id);
        return mi;
    }

    ToolStripMenuItem Menu(string title, params object[] items)
    {
        var m = new ToolStripMenuItem(title);
        foreach (var it in items)
        {
            if (it == null) m.DropDownItems.Add(new ToolStripSeparator());
            else if (it is string) m.DropDownItems.Add(Item((string)it));
            else m.DropDownItems.Add((ToolStripItem)it);
        }
        m.DropDownOpening += (s, e) => UpdateMenuChecks();
        return m;
    }

    void BuildShell()
    {
        DefineCommands();
        _recentMenu = new ToolStripMenuItem("Open recent");
        _recentMenu.DropDownOpening += (s, e) => FillRecentMenu();
        _recentMenu.DropDownItems.Add("(none)");
        _themeMenu = new ToolStripMenuItem("Theme");
        foreach (var t in ThemeManager.Themes)
        {
            var theme = t;
            var mi = new ToolStripMenuItem(t.Name + (t.IsDark ? "" : "  (light)")) { Tag = t };
            mi.Click += (s, e) => SwitchTheme(theme);
            _themeMenu.DropDownItems.Add(mi);
        }
        _flowsMenu = new ToolStripMenuItem("Flows");

        _menu = new MenuStrip { Dock = DockStyle.Top, Padding = new Padding(4, 2, 0, 2) };
        _menu.Items.Add(Menu("File", "file.new", "file.open", _recentMenu, null, "file.save", "file.saveas", "file.saveall", null, "file.close", "file.reveal", "file.copypath", null, "file.exit"));
        _menu.Items.Add(Menu("Edit", "edit.find", "edit.replace", "edit.goto", "edit.symbol", null, "edit.comment", "edit.foldall", "edit.unfoldall"));
        _menu.Items.Add(Menu("View", "view.palette", null, "view.outline", "view.ast", "view.problems", "view.console", "view.trace", null, "view.wrap", "view.zoomin", "view.zoomout", "view.zoomreset", null, _themeMenu, "view.layout", "view.welcome"));
        _menu.Items.Add(Menu("Script", "script.run", "script.stop", "script.validate", null, "script.parse", "script.live", "script.includes", null, "script.ahkpath"));
        _flowsMenu.DropDownOpening += (s, e) => FillFlowsMenu(_flowsMenu.DropDownItems, true);
        _flowsMenu.DropDownItems.Add("…");
        _menu.Items.Add(_flowsMenu);
        _menu.Items.Add(Menu("Tools", "tools.compare", "tools.compareDisk", "tools.escape", "tools.nim"));
        _menu.Items.Add(Menu("Help", "help.keys", "help.about"));
        MainMenuStrip = _menu;

        // toolbar
        _toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3), ImageScalingSize = new Size(16, 16) };
        TB("file.new"); TB("file.open"); TB("file.save"); TB("file.saveall");
        _toolbar.Items.Add(new ToolStripSeparator());
        _flowButton = new ToolStripSplitButton("Flow") { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ToolTipText = "Run this flow on the script (F6); the arrow picks another" };
        _flowButton.ButtonClick += (s, e) => Exec("flow.run");
        _flowButton.DropDownOpening += (s, e) => FillFlowsMenu(_flowButton.DropDownItems, false);
        _flowButton.DropDownItems.Add("…");
        _toolbar.Items.Add(_flowButton);
        _toolbar.Items.Add(new ToolStripSeparator());
        TB("script.run", true); TB("script.stop"); TB("script.validate", true);
        var search = new ToolStripButton("Search commands, flows, symbols…   Ctrl+Shift+P")
        {
            Alignment = ToolStripItemAlignment.Right, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, Tag = Glyphs.Search
        };
        search.Click += (s, e) => ShowPalette("");
        _toolbar.Items.Add(search);

        // status bar
        _status = new StatusStrip { SizingGrip = false, Padding = new Padding(8, 0, 8, 0) };
        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusScript = new ToolStripStatusLabel("") { ToolTipText = "The script that is parsed and that flows run on" };
        _statusParse = new ToolStripStatusLabel("");
        _statusProblems = new ToolStripStatusLabel("") { IsLink = false, ToolTipText = "Show problems" };
        _statusProblems.Click += (s, e) => ShowPanel(_problems);
        _statusPos = new ToolStripStatusLabel("");
        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _statusScript, _statusParse, _statusProblems, _statusPos });

        _dockPanel = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingWindow, ShowDocumentIcon = false };
        _dockPanel.Theme = UiTheming.NewDockTheme();
        _dockPanel.ActiveDocumentChanged += (s, e) => OnActiveDocumentChanged();
        _dockPanel.ContentRemoved += (s, e) => { if (IsHandleCreated && !IsDisposed && !Disposing && Documents().Count == 0) BeginInvoke((Action)OnActiveDocumentChanged); };

        Controls.Add(_dockPanel);
        Controls.Add(_toolbar);
        Controls.Add(_menu);
        Controls.Add(_status);

        _problems = new ProblemsPanel(this);
        _problems.CountsChanged += (e, w) => UpdateProblemStatus(e, w);
        _astPanel = new AstPanel(this);
        _outline = new OutlinePanel(this);
        _console = new ConsolePanel(this);
        _traceVisualizerContent = new TraceVisualizerDockContent { TabText = "Trace", Text = "Trace", HideOnClose = true };
        BuildTraceVisualizerPanel();
        _traceVisualizerContent.Controls.Add(_tracePanel);

        ReloadFlows();
        DefaultLayout();
    }

    void TB(string id, bool withText = false)
    {
        var c = _cmds[id];
        var b = new ToolStripButton(withText ? c.Title : "") { Tag = c, DisplayStyle = withText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image,
            ToolTipText = c.Title + (c.Keys != Keys.None ? "  (" + c.KeyText + ")" : "") };
        b.Click += (s, e) => Exec(id);
        _toolbar.Items.Add(b);
    }

    void DefaultLayout()
    {
        _outline.Show(_dockPanel, DockState.DockRight);
        _astPanel.Show(_outline.Pane, DockAlignment.Bottom, 0.55);
        _problems.Show(_dockPanel, DockState.DockBottom);
        _console.Show(_problems.Pane, null);
        _problems.Activate();
        _dockPanel.DockRightPortion = 0.26;
        _dockPanel.DockBottomPortion = 0.22;
    }

    void ResetLayout()
    {
        foreach (var c in new DockContent[] { _outline, _astPanel, _problems, _console, _traceVisualizerContent })
            c.DockPanel = null;
        DefaultLayout();
    }

    /// <summary>Enables/checks menu and toolbar items from the commands' state (called on menu open and often).</summary>
    void UpdateMenuChecks()
    {
        foreach (ToolStripItem top in _menu.Items)
        {
            var m = top as ToolStripMenuItem;
            if (m == null) continue;
            foreach (ToolStripItem it in m.DropDownItems)
            {
                var c = it.Tag as WbCommand;
                if (c == null) continue;
                it.Enabled = c.Enabled == null || c.Enabled();
                var mi = it as ToolStripMenuItem;
                if (mi == null) continue;
                if (c.Id == "script.live") mi.Checked = _state.LiveParse;
                else if (c.Id == "script.includes") mi.Checked = _state.FollowIncludes;
                else if (c.Id == "view.wrap") mi.Checked = _state.WordWrap;
            }
        }
        foreach (ToolStripItem it in _themeMenu.DropDownItems)
            ((ToolStripMenuItem)it).Checked = it.Tag == WbTheme.Current;
        UpdateToolbar();
    }

    void UpdateToolbar()
    {
        foreach (ToolStripItem it in _toolbar.Items)
        {
            var c = it.Tag as WbCommand;
            if (c != null) it.Enabled = c.Enabled == null || c.Enabled();
        }
        _flowButton.Enabled = AnalysisTarget != null && _currentFlow != null;
        _flowButton.Text = _currentFlow != null ? _currentFlow.Name : "No flow";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // keys the editor must keep (find/replace/goto are FCTB's own dialogs when an editor has focus)
        foreach (var c in _cmdOrder)
        {
            if (c.Keys == Keys.None || c.Keys != keyData) continue;
            if (c.Enabled != null && !c.Enabled()) return base.ProcessCmdKey(ref msg, keyData);
            c.Run();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── Recent files, flows menus ──────────────────────────────────────────────────────────────────────

    void FillRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        var recent = (_state.RecentFiles ?? new List<string>()).Where(File.Exists).ToList();
        if (recent.Count == 0) { _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none)") { Enabled = false }); return; }
        foreach (var p in recent)
        {
            var path = p;
            var mi = new ToolStripMenuItem(Path.GetFileName(p)) { ShortcutKeyDisplayString = Path.GetDirectoryName(p), ToolTipText = p };
            mi.Click += (s, e) => OpenFile(path);
            _recentMenu.DropDownItems.Add(mi);
        }
        _recentMenu.DropDownItems.Add(new ToolStripSeparator());
        _recentMenu.DropDownItems.Add("Clear list", null, (s, e) => { _state.RecentFiles.Clear(); _state.Save(); });
    }

    void FillFlowsMenu(ToolStripItemCollection items, bool withCommands)
    {
        items.Clear();
        foreach (var group in _flows.GroupBy(f => f.Folder))
        {
            var folder = new ToolStripMenuItem(group.Key) { Image = Glyphs.Get(group.First().BuiltIn ? Glyphs.Flow : Glyphs.Folder, WbTheme.Subtext1) };
            foreach (var f in group)
            {
                var flow = f;
                var mi = new ToolStripMenuItem(f.Name) { ToolTipText = f.Description, Checked = f == _currentFlow };
                mi.Click += (s, e) => { SelectFlow(flow); if (AnalysisTarget != null) RunFlow(flow); };
                folder.DropDownItems.Add(mi);
            }
            items.Add(folder);
        }
        items.Add(new ToolStripSeparator());
        foreach (var id in new[] { "flow.run", "flow.pick", "flow.new", "flow.edit", "flow.folder", "flow.refresh" })
        {
            if (!withCommands && (id == "flow.run" || id == "flow.pick")) continue;
            var it = Item(id);
            var c = _cmds[id];
            it.Enabled = c.Enabled == null || c.Enabled();
            items.Add(it);
        }
    }

    void ReloadFlows()
    {
        _flows = FlowCatalog.Load();
        _currentFlow = _flows.FirstOrDefault(f => f.Id == _state.LastFlow) ?? _flows.FirstOrDefault(f => f.Name == "Minify (safe)") ?? _flows.FirstOrDefault();
        UpdateToolbar();
    }

    void SelectFlow(FlowEntry f)
    {
        _currentFlow = f;
        _state.LastFlow = f.Id;
        _state.Save();
        UpdateToolbar();
    }

    // ── Command palette ────────────────────────────────────────────────────────────────────────────────

    void ShowPalette(string initial)
    {
        var p = new CommandPalette(this, initial, PaletteItems);
        UiTheming.TitleBar(p);
        p.Show(this);
    }

    IEnumerable<PaletteItem> PaletteItems(string q)
    {
        q = q ?? "";
        if (q.StartsWith(":"))
        {
            var ed = ActiveEditor;
            int line;
            string num = q.Substring(1).Trim();
            if (ed == null) yield break;
            if (int.TryParse(num, out line) && line > 0)
                yield return new PaletteItem { Title = "Go to line " + line, Detail = "of " + ed.LinesCount, Run = () => { ed.GoToLine(Math.Min(line, ed.LinesCount)); ed.Focus(); } };
            else
                yield return new PaletteItem { Title = "Type a line number", Detail = "1 – " + ed.LinesCount };
            yield break;
        }
        if (q.StartsWith("@"))
        {
            string s = q.Substring(1).Trim();
            foreach (var it in _outline.AllSymbols().Where(x => x.Kind != SymbolKind.Include)
                .Select(x => new { x, score = CommandPalette.Fuzzy(x.Name, s) }).Where(x => x.score >= 0)
                .OrderByDescending(x => x.score).Take(200))
            {
                var sym = it.x;
                yield return new PaletteItem
                {
                    Title = sym.Name, Category = sym.Kind.ToString(), Detail = (sym.Detail ?? "") + "  " + (sym.File != null ? Path.GetFileName(sym.File) : "") + ":" + sym.Line,
                    Run = () => Navigate(sym.File, sym.Line, sym.Column, sym.Line, sym.Column + sym.Name.Length)
                };
            }
            yield break;
        }
        bool flowOnly = q.StartsWith("flow ", StringComparison.OrdinalIgnoreCase);
        string query = flowOnly ? q.Substring(5).Trim() : q.Trim();
        var all = new List<PaletteItem>();
        foreach (var f in _flows)
        {
            var flow = f;
            all.Add(new PaletteItem { Category = "Run flow", Title = f.Name, Detail = f.Description, Run = () => { SelectFlow(flow); RunFlow(flow); } });
        }
        if (!flowOnly)
        {
            foreach (var c in _cmdOrder)
            {
                if (c.Enabled != null && !c.Enabled()) continue;
                var cmd = c;
                all.Add(new PaletteItem { Category = c.Category, Title = c.Title, Shortcut = c.KeyText, Run = () => Exec(cmd.Id) });
            }
            foreach (var r in (_state.RecentFiles ?? new List<string>()).Where(File.Exists))
            {
                var path = r;
                all.Add(new PaletteItem { Category = "Open recent", Title = Path.GetFileName(r), Detail = Path.GetDirectoryName(r), Run = () => OpenFile(path) });
            }
            foreach (var t in ThemeManager.Themes)
            {
                var theme = t;
                all.Add(new PaletteItem { Category = "Theme", Title = t.Name, Run = () => SwitchTheme(theme) });
            }
        }
        if (query.Length == 0)
        {
            foreach (var p in all.Where(x => x.Category != "Theme" && x.Category != "Open recent")) yield return p;
            yield break;
        }
        foreach (var p in all)
            p.Score = Math.Max(CommandPalette.Fuzzy(p.Title, query), CommandPalette.Fuzzy(p.Category + " " + p.Title, query) - 50);
        foreach (var p in all.Where(x => x.Score >= 0).OrderByDescending(x => x.Score)) yield return p;
    }

    // ── Themes ─────────────────────────────────────────────────────────────────────────────────────────

    void SwitchTheme(ThemePalette t)
    {
        WbTheme.Current = t;
        _state.ThemeName = t.Name;
        _state.Save();
        try { SetPreferredAppMode(t.IsDark ? 2 : 0); } catch { }

        // DockPanelSuite only takes a new theme with no content attached. Save the exact layout (every tab gets a
        // temporary id), detach, switch, and load the layout back onto the same objects.
        var contents = _dockPanel.Contents.OfType<DockContent>().ToList();
        var byId = new Dictionary<string, IDockContent>();
        for (int i = 0; i < contents.Count; i++)
        {
            string id = "swap:" + i;
            byId[id] = contents[i];
            contents[i].DockHandler.GetPersistStringCallback = () => id;
        }
        var active = _dockPanel.ActiveDocument as DockContent;
        var ms = new MemoryStream();
        _dockPanel.SaveAsXml(ms, System.Text.Encoding.UTF8, true);
        foreach (var c in contents) c.DockHandler.GetPersistStringCallback = null;
        _dockPanel.SuspendLayout(true);
        foreach (var c in contents) c.DockPanel = null;
        _dockPanel.Theme = UiTheming.NewDockTheme();
        ms.Position = 0;
        try { _dockPanel.LoadFromXml(ms, s => { IDockContent c; return byId.TryGetValue(s, out c) ? c : null; }); }
        catch
        {
            foreach (var c in contents) if (c.DockPanel == null) c.Show(_dockPanel, c is ToolPanel ? DockState.DockRight : DockState.Document);
        }
        _dockPanel.ResumeLayout(true, true);
        if (active != null && !active.IsDisposed) active.Activate();
        ApplyTheme();
    }

    void ApplyTheme()
    {
        BackColor = WbTheme.Crust;
        UiTheming.TitleBar(this);
        UiTheming.Apply(_menu);
        UiTheming.Apply(_toolbar);
        _status.Renderer = new DarkToolStripRenderer();
        _status.BackColor = WbTheme.Crust;
        _status.ForeColor = WbTheme.Subtext0;
        foreach (ToolStripItem it in _status.Items) it.ForeColor = WbTheme.Subtext0;
        _dockPanel.BackColor = WbTheme.Crust;
        RefreshMenuIcons(_menu.Items);
        foreach (ToolStripItem it in _toolbar.Items)
        {
            var c = it.Tag as WbCommand;
            char g = c != null ? c.Glyph : it.Tag is char ? (char)it.Tag : '\0';
            if (g == '\0') continue;
            Color col = c != null && c.Id == "script.run" ? WbTheme.Green : c != null && c.Id == "script.stop" ? WbTheme.Red : WbTheme.Subtext1;
            it.Image = Glyphs.Get(g, col);
        }
        _flowButton.Image = Glyphs.Get(Glyphs.Flow, WbTheme.Yellow);
        foreach (var c in _dockPanel.Contents.OfType<Control>().Concat(new Control[] { _problems, _astPanel, _outline, _console }).Distinct())
        {
            var th = c as IThemed;
            if (th != null) th.ApplyTheme();
            else if (c is PipelineBuilderContent) ((PipelineBuilderContent)c).RefreshTheme();
            else UiTheming.Apply(c);
        }
        if (_tracePanel != null) UiTheming.Apply(_tracePanel);
        UpdateMenuChecks();
        Invalidate(true);
    }

    void RefreshMenuIcons(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            var c = it.Tag as WbCommand;
            if (c != null && c.Glyph != '\0') it.Image = Glyphs.Get(c.Glyph, WbTheme.Subtext1);
            var mi = it as ToolStripMenuItem;
            if (mi != null) { mi.DropDown.BackColor = WbTheme.Mantle; RefreshMenuIcons(mi.DropDownItems); }
        }
    }

    void ShowPanel(DockContent p)
    {
        if (p.DockPanel == null) p.Show(_dockPanel, DockState.DockRight);
        p.Show(_dockPanel);
        p.Activate();
    }

    void ShowDoc(DockContent d)
    {
        d.Show(_dockPanel, DockState.Document);
        d.Activate();
    }

    void ToggleWordWrap()
    {
        _state.WordWrap = !_state.WordWrap;
        _state.Save();
        foreach (var e in AllEditors()) e.WordWrap = _state.WordWrap;
        if (_diffContent != null) _diffContent.SetWordWrap(_state.WordWrap);
        if (_escapeContent != null) _escapeContent.SetWordWrap(_state.WordWrap);
        UpdateMenuChecks();
    }

    void Zoom(int delta)
    {
        _state.EditorZoom = delta == 0 ? 100 : Math.Max(50, Math.Min(300, _state.EditorZoom + delta));
        _state.Save();
        foreach (var e in AllEditors()) e.Zoom = _state.EditorZoom;
    }

    void UpdateProblemStatus(int errors, int warnings)
    {
        _statusProblems.Text = (errors > 0 ? "✖ " + errors : "✔ 0") + "   ⚠ " + warnings;
        _statusProblems.ForeColor = errors > 0 ? WbTheme.Red : warnings > 0 ? WbTheme.Yellow : WbTheme.Green;
        _problems.TabText = errors + warnings > 0 ? "Problems (" + (errors + warnings) + ")" : "Problems";
    }

    // ── Welcome ────────────────────────────────────────────────────────────────────────────────────────

    void ShowWelcome()
    {
        var existing = _dockPanel.Documents.OfType<WelcomeDocument>().FirstOrDefault();
        if (existing != null) { existing.Activate(); return; }
        var w = new WelcomeDocument(this, _state.RecentFiles ?? new List<string>(), new[] { "file.new", "file.open", "view.palette" }.Select(id => _cmds[id]).ToList(), Exec);
        w.Show(_dockPanel, DockState.Document);
    }
}

internal class WelcomeDocument : DockContent, IThemed
{
    readonly Panel _body;

    public WelcomeDocument(AstWorkbenchForm owner, List<string> recent, List<WbCommand> actions, Action<string> exec)
    {
        TabText = Text = "Welcome";
        DockAreas = DockAreas.Document;
        _body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(60, 40, 40, 40) };
        Controls.Add(_body);
        int y = 40;
        _body.Controls.Add(new Label { Text = "AHK2 AST Workbench", Font = new Font("Segoe UI Light", 26f), AutoSize = true, Location = new Point(60, y), Tag = "title" });
        y += 60;
        _body.Controls.Add(new Label { Text = "Parse, inspect, optimise, minify and run AutoHotkey v2 scripts.", Font = new Font("Segoe UI", 11f), AutoSize = true, Location = new Point(63, y), Tag = "dim" });
        y += 50;
        _body.Controls.Add(Heading("Start", 63, y)); y += 32;
        foreach (var a in actions)
        {
            var cmd = a;
            _body.Controls.Add(Link(a.Title + (a.Keys != Keys.None ? "    " + a.KeyText : ""), 63, y, () => exec(cmd.Id)));
            y += 28;
        }
        y += 20;
        _body.Controls.Add(Heading("Recent", 63, y)); y += 32;
        int shown = 0;
        foreach (var r in recent.Where(File.Exists).Take(10))
        {
            var path = r;
            _body.Controls.Add(Link(Path.GetFileName(r), 63, y, () => owner.LoadFile(path)));
            _body.Controls.Add(new Label { Text = Path.GetDirectoryName(r), AutoSize = true, Location = new Point(300, y + 2), Tag = "dim" });
            y += 28; shown++;
        }
        if (shown == 0) { _body.Controls.Add(new Label { Text = "Nothing yet. Open a script, or drop one on this window.", AutoSize = true, Location = new Point(63, y), Tag = "dim" }); y += 28; }
        y += 20;
        _body.Controls.Add(Heading("How it works", 63, y)); y += 32;
        foreach (var tip in new[]
        {
            "The script is parsed as you type: Problems, the Outline and the AST follow along. Click anything to jump to it.",
            "Pick a flow on the toolbar (Beautify, Tree-shake, Minify…) and press F6. The result opens as a new tab,",
            "      where you can compare it with the source, validate it with AutoHotkey, run it, or save it.",
            "F5 runs the script, F7 asks AutoHotkey to validate it. Ctrl+Shift+P finds any command; @ finds symbols; : goes to a line.",
        })
        {
            _body.Controls.Add(new Label { Text = tip, AutoSize = true, Location = new Point(63, y), Tag = "dim" });
            y += 24;
        }
        ApplyTheme();
    }

    Label Heading(string t, int x, int y)
    {
        return new Label { Text = t.ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 9f), AutoSize = true, Location = new Point(x, y), Tag = "head" };
    }

    LinkLabel Link(string t, int x, int y, Action a)
    {
        var l = new LinkLabel { Text = t, AutoSize = true, Location = new Point(x, y), Font = new Font("Segoe UI", 10.5f), LinkBehavior = LinkBehavior.HoverUnderline };
        l.LinkClicked += (s, e) => a();
        return l;
    }

    public void ApplyTheme()
    {
        BackColor = _body.BackColor = WbTheme.Base;
        UiTheming.DarkScrollbars(_body);
        foreach (Control c in _body.Controls)
        {
            c.BackColor = WbTheme.Base;
            var ll = c as LinkLabel;
            if (ll != null) { ll.LinkColor = WbTheme.Blue; ll.ActiveLinkColor = WbTheme.Sky; ll.VisitedLinkColor = WbTheme.Blue; continue; }
            string tag = c.Tag as string;
            c.ForeColor = tag == "dim" ? WbTheme.Subtext0 : tag == "head" ? WbTheme.Overlay0 : WbTheme.Text;
        }
    }

    protected override string GetPersistString() { return ""; }
}
