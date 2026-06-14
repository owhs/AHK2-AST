// AHK# AST Workbench — Comprehensive Parser, Analyzer & Test Runner
// A premium WinForms GUI for parsing, inspecting, debugging, and running AHK2 scripts.
// Compiled into ahk#.bridge.dll alongside AhkAstEngine.cs.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

// ═══════════════════════════════════════════════════════════════════════════════
// COM-Visible Entry Point
// ═══════════════════════════════════════════════════════════════════════════════

// Main Form
// ═══════════════════════════════════════════════════════════════════════════════

internal partial class AstWorkbenchForm : Form
{
    // ── Menu ──────────────────────────────────────────────────────────────

    private void BuildMenu()
    {
        _menu = new MenuStrip();
        _menu.BackColor = WbTheme.Mantle;
        _menu.ForeColor = WbTheme.Text;
        _menu.Renderer = new DarkMenuRenderer();
        _menu.Font = WbTheme.UIFont;
        _menu.Padding = new Padding(4, 2, 0, 2);

        // File
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(MakeMenuItem("&Open File...", Keys.Control | Keys.O, (s, e) => OpenFileDialog()));
        // file.DropDownItems.Add(MakeMenuItem("Open &Folder...", Keys.None, (s, e) => OpenFolderDialog()));
        _recentFilesMenu = new ToolStripMenuItem("Recent &Files");
        file.DropDownItems.Add(_recentFilesMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MakeMenuItem("Save Source &As...", Keys.Control | Keys.Shift | Keys.S, (s, e) => SaveSourceAs()));
        file.DropDownItems.Add(MakeMenuItem("&Close Tab", Keys.Control | Keys.W, (s, e) => CloseActiveTab()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MakeMenuItem("E&xit", Keys.Alt | Keys.F4, (s, e) => Close()));

        // Parse
        var parse = new ToolStripMenuItem("&Parse");
        parse.DropDownItems.Add(MakeMenuItem("Parse &Current", Keys.F5, (s, e) => ParseCurrent()));
        parse.DropDownItems.Add(MakeMenuItem("Parse from &Clipboard", Keys.Control | Keys.Shift | Keys.V, (s, e) => ParseClipboard()));
        //parse.DropDownItems.Add(MakeMenuItem("Run &Security Audit", Keys.Control | Keys.Shift | Keys.A, (s, e) => RunSecurityAudit()));
        parse.DropDownItems.Add(new ToolStripSeparator());

        var followIncludesItem = new ToolStripMenuItem("Follow #&Include directives");
        followIncludesItem.Checked = _followIncludes;
        followIncludesItem.Click += (s, e) =>
        {
            _followIncludes = !_followIncludes;
            followIncludesItem.Checked = _followIncludes;
            try
            {
                var state = AHK2AST.UI.WorkbenchState.Load();
                state.FollowIncludes = _followIncludes;
                state.Save();
            }
            catch { }
            ParseCurrent();
        };
        parse.DropDownItems.Add(followIncludesItem);

        var inlineIncludesItem = new ToolStripMenuItem("Inline includes in &source editor");
        inlineIncludesItem.Checked = _inlineIncludes;
        inlineIncludesItem.Click += (s, e) =>
        {
            _inlineIncludes = !_inlineIncludes;
            inlineIncludesItem.Checked = _inlineIncludes;
            try
            {
                var state = AHK2AST.UI.WorkbenchState.Load();
                state.InlineIncludes = _inlineIncludes;
                state.Save();
            }
            catch { }

            if (!_inlineIncludes && !string.IsNullOrEmpty(_currentFile) && File.Exists(_currentFile))
            {
                try
                {
                    _sourceEditor.Text = File.ReadAllText(_currentFile, Encoding.UTF8);
                    _sourceEditorIsInlined = false;
                }
                catch { }
            }
            ParseCurrent();
        };
        parse.DropDownItems.Add(inlineIncludesItem);
        parse.DropDownItems.Add(new ToolStripSeparator());

        // Emit options
        var emitCommentsItem = new ToolStripMenuItem("Emit &Comments");
        emitCommentsItem.Checked = _emitComments;
        emitCommentsItem.Click += (s, e) =>
        {
            _emitComments = !_emitComments;
            emitCommentsItem.Checked = _emitComments;
        };
        parse.DropDownItems.Add(emitCommentsItem);

        var emitBlanksItem = new ToolStripMenuItem("Emit &Blank Lines");
        emitBlanksItem.Checked = _emitBlankLines;
        emitBlanksItem.Click += (s, e) =>
        {
            _emitBlankLines = !_emitBlankLines;
            emitBlanksItem.Checked = _emitBlankLines;
        };
        parse.DropDownItems.Add(emitBlanksItem);

        var useTabsItem = new ToolStripMenuItem("Indent with &Tabs");
        useTabsItem.Checked = _useTabs;
        useTabsItem.Click += (s, e) =>
        {
            _useTabs = !_useTabs;
            useTabsItem.Checked = _useTabs;
        };
        parse.DropDownItems.Add(useTabsItem);
        parse.DropDownItems.Add(new ToolStripSeparator());

        parse.DropDownItems.Add(MakeMenuItem("&Emit from AST", Keys.Control | Keys.E, (s, e) => EmitFromAst()));

        // Run
        var run = new ToolStripMenuItem("&Run");
        run.DropDownItems.Add(MakeMenuItem("&Test Run", Keys.F9, (s, e) => TestRun()));
        run.DropDownItems.Add(MakeMenuItem("&Stop", Keys.Shift | Keys.F9, (s, e) => StopRun()));
        run.DropDownItems.Add(new ToolStripSeparator());
        run.DropDownItems.Add(MakeMenuItem("Set &Working Directory...", Keys.None, (s, e) => SetWorkingDir()));
        run.DropDownItems.Add(MakeMenuItem("Set &AHK Path...", Keys.None, (s, e) => SetAhkPath()));
        run.DropDownItems.Add(MakeMenuItem("Set &Temp Script Path...", Keys.None, (s, e) => SetTempPath()));

        // View
        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(MakeMenuItem("&Expand All Nodes", Keys.Control | Keys.Shift | Keys.E, (s, e) => _astTree.ExpandAll()));
        view.DropDownItems.Add(MakeMenuItem("&Collapse All Nodes", Keys.Control | Keys.Shift | Keys.C, (s, e) => _astTree.CollapseAll()));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(MakeMenuItem("Clear &Log", Keys.None, (s, e) => _parseLog.Clear()));
        view.DropDownItems.Add(MakeMenuItem("Clear &Output", Keys.None, (s, e) => _runOutput.Clear()));

        var autoFocusItem = new ToolStripMenuItem("Auto-focus &Code on AST Selection");
        autoFocusItem.Checked = _autoFocusCode;
        autoFocusItem.Click += (s, e) =>
        {
            _autoFocusCode = !_autoFocusCode;
            autoFocusItem.Checked = _autoFocusCode;
        };
        view.DropDownItems.Add(autoFocusItem);

        var wordWrapItem = new ToolStripMenuItem("Word &Wrap");
        wordWrapItem.Checked = _wordWrap;
        wordWrapItem.Click += (s, e) => ToggleWordWrap(wordWrapItem);
        view.DropDownItems.Add(wordWrapItem);

        view.DropDownItems.Add(MakeMenuItem("Show &Trace Visualizer", Keys.Control | Keys.T, (s, e) => ShowTraceVisualizer()));
        view.DropDownItems.Add(new ToolStripSeparator());

        var themesItem = new ToolStripMenuItem("Themes");
        foreach (var theme in ThemeManager.Themes)
        {
            var tItem = new ToolStripMenuItem(theme.Name);
            tItem.Checked = (WbTheme.Current.Name == theme.Name);
            tItem.Click += (s, e) =>
            {
                foreach (ToolStripMenuItem item in themesItem.DropDownItems)
                {
                    item.Checked = (item == tItem);
                }
                WbTheme.Current = theme;
                try
                {
                    var state = AHK2AST.UI.WorkbenchState.Load();
                    state.ThemeName = theme.Name;
                    state.Save();
                }
                catch { }
                RefreshTheme();
            };
            themesItem.DropDownItems.Add(tItem);
        }
        view.DropDownItems.Add(themesItem);

        var flows = new ToolStripMenuItem("&Flows");
        PopulateFlowsMenu(flows);

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(MakeMenuItem("&About...", Keys.None, (s, e) => ShowAboutDialog()));

        bool hasNim = AHK2AST.Plugins.PluginRegistry.RegisteredPluginTypes.Any(t => t.Name == "NimTranspilerPlugin" || t.FullName == "AHK2AST.Plugins.NimTranspilerPlugin");

        var menuItems = new List<ToolStripItem> { file, parse, run };
        if (hasNim)
        {
            var build = new ToolStripMenuItem("&Build");
            build.DropDownItems.Add(MakeMenuItem("&Nim Build Manager...", Keys.Control | Keys.B, (s, e) => OpenNimBuildManager()));
            menuItems.Add(build);
        }
        menuItems.AddRange(new ToolStripItem[] { view, flows, help });

        _menu.Items.AddRange(menuItems.ToArray());
        Controls.Add(_menu);
    }

    private void PopulateFlowsMenu(ToolStripMenuItem flowsMenu)
    {
        flowsMenu.DropDownItems.Clear();

        var newFlowItem = MakeMenuItem("➕ New Flow...", Keys.None, (s, e) => OpenPipelineBuilder(null, null));
        flowsMenu.DropDownItems.Add(newFlowItem);

        var refreshItem = MakeMenuItem("🔄 Refresh Flows", Keys.None, (s, e) => PopulateFlowsMenu(flowsMenu));
        flowsMenu.DropDownItems.Add(refreshItem);
        flowsMenu.DropDownItems.Add(new ToolStripSeparator());

        string flowsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows");
        if (!Directory.Exists(flowsDir)) return;

        var folders = new Dictionary<string, ToolStripMenuItem>();

        foreach (var file in Directory.GetFiles(flowsDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict != null && dict.ContainsKey("Meta"))
                {
                    var meta = dict["Meta"] as Dictionary<string, object>;
                    if (meta != null)
                    {
                        string name = meta.ContainsKey("Name") ? meta["Name"].ToString() : Path.GetFileNameWithoutExtension(file);
                        string icon = meta.ContainsKey("Icon") ? meta["Icon"].ToString() : "";
                        string folder = meta.ContainsKey("Folder") ? meta["Folder"].ToString() : "";

                        var item = new ToolStripMenuItem(string.Format("{0} {1}", icon, name).Trim());
                        item.Click += (s, e) =>
                        {
                            try
                            {
                                OpenPipelineBuilder(file, json);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Failed to load flow: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        };

                        if (!string.IsNullOrEmpty(folder))
                        {
                            if (!folders.ContainsKey(folder))
                            {
                                var folderMenu = new ToolStripMenuItem(folder);
                                folders[folder] = folderMenu;
                                flowsMenu.DropDownItems.Add(folderMenu);
                            }
                            folders[folder].DropDownItems.Add(item);
                        }
                        else
                        {
                            flowsMenu.DropDownItems.Add(item);
                        }
                    }
                }
            }
            catch { }
        }
        UpdateRecentFilesMenu();
        MainMenuStrip = _menu;
    }

    private ToolStripMenuItem MakeMenuItem(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        item.Click += handler;
        return item;
    }

    private void OpenPipelineBuilder(string filePath, string flowJson)
    {
        var builder = new PipelineBuilderContent();
        builder.GetSourceCodeRequested += () => _sourceEditor.Text;
        builder.GetActiveFilePathRequested += () => _currentFile;
        builder.ExecuteFlowRequested += (json, autoRun) =>
        {
            try
            {
                _emitView.Clear();
                AppendLog(_emitView, "═══ RUNNING PIPELINE FLOW ═══", WbTheme.Lavender);
                AppendLog(_emitView, "Started at " + DateTime.Now.ToString("HH:mm:ss"), WbTheme.Overlay0);
                AppendLog(_emitView, "", WbTheme.Text);

                string result = _engine.ExecuteFlow(_sourceEditor.Text, json, !_inlineIncludes, _currentFile, _followIncludes);

                foreach (string log in PipelineLogger.Logs)
                {
                    Color logColor = WbTheme.Text;
                    if (log.Contains("❌ ERROR") || log.Contains("failed"))
                    {
                        logColor = WbTheme.Red;
                    }
                    else if (log.StartsWith("[Step") || log.StartsWith("Starting") || log.StartsWith("Finished") || log.StartsWith("Pipeline") || log.StartsWith("Running"))
                    {
                        logColor = WbTheme.Sky;
                    }
                    else if (log.TrimStart().StartsWith("🌳") || log.TrimStart().StartsWith("⚡") || log.TrimStart().StartsWith("🤐") || log.TrimStart().StartsWith("🛠️") || log.TrimStart().StartsWith("✨") || log.TrimStart().StartsWith("-") || log.TrimStart().StartsWith("Pruned") || log.TrimStart().StartsWith("Folded") || log.TrimStart().StartsWith("Inlined") || log.TrimStart().StartsWith("Renamed"))
                    {
                        logColor = WbTheme.Yellow;
                    }
                    else if (log.Contains("completed successfully") || log.Contains("complete"))
                    {
                        logColor = WbTheme.Green;
                    }
                    else
                    {
                        logColor = WbTheme.Subtext0;
                    }
                    AppendLog(_emitView, log, logColor);
                }

                _lastEmittedCode = result;
                ShowEmitSourceTab(result);
                SelectTab(3); // Switch to Emit tab
                _statusLabel.Text = "✅ Flow executed successfully";
                _statusLabel.ForeColor = WbTheme.Green;

                if (!string.IsNullOrEmpty(result) && autoRun)
                {
                    RunAhkScript(result);
                }
            }
            catch (Exception ex)
            {
                AppendLog(_emitView, "FLOW FATAL ERROR: " + ex.ToString(), WbTheme.Red);
                MessageBox.Show("Flow execution failed: " + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        builder.FormClosed += (s, e) =>
        {
            this.BeginInvoke(new Action(() =>
            {
                UpdateActiveDocumentPanes();
            }));
        };
        builder.Show(_dockPanel, DockState.Document);
        if (!string.IsNullOrEmpty(flowJson))
        {
            builder.LoadFlow(flowJson, filePath);
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────────────

    private void BuildToolbar()
    {
        var btnStop = new ToolStripButton("x Stop") { ToolTipText = "Stop running script (Shift+F9)", Alignment = ToolStripItemAlignment.Right };
        btnStop.Click += (s, e) => StopRun();

        var btnRun = new ToolStripButton("* Run") { ToolTipText = "Test run script (F9)", Alignment = ToolStripItemAlignment.Right };
        btnRun.Click += (s, e) => TestRun();

        var sep2 = new ToolStripSeparator() { Alignment = ToolStripItemAlignment.Right };

        var btnEmit = new ToolStripButton("[=] Emit") { ToolTipText = "Regenerate source from AST (Ctrl+E)", Alignment = ToolStripItemAlignment.Right };
        btnEmit.Click += (s, e) => EmitFromAst();

        var btnParse = new ToolStripButton("> Parse") { ToolTipText = "Parse current source (F5)", Alignment = ToolStripItemAlignment.Right };
        btnParse.Click += (s, e) => ParseCurrent();



        var sep1 = new ToolStripSeparator() { Alignment = ToolStripItemAlignment.Right };

        var btnOpen = new ToolStripButton("Open") { ToolTipText = "Open AHK file (Ctrl+O)", Alignment = ToolStripItemAlignment.Right };
        btnOpen.Click += (s, e) => OpenFileDialog();

        var btnDiff = new ToolStripButton("Diff Workspace") { ToolTipText = "Open Diff Workspace", Alignment = ToolStripItemAlignment.Left };
        btnDiff.Click += (s, e) => GetOrCreateDiffWorkspace();

        var btnEscape = new ToolStripButton("Escape Tool") { ToolTipText = "Open Escaper/Unescaper Tool", Alignment = ToolStripItemAlignment.Left };
        btnEscape.Click += (s, e) => GetOrCreateEscapeWorkspace();

        // Items are added right-to-left because they are right-aligned
        _menu.Items.AddRange(new ToolStripItem[] {
            btnDiff, btnEscape, btnStop, btnRun, sep2, btnEmit, btnParse, sep1, btnOpen
        });
    }

    // ── Status Bar ────────────────────────────────────────────────────────

    private void BuildStatusBar()
    {
        _status = new StatusStrip();
        _status.BackColor = WbTheme.Mantle;
        _status.ForeColor = WbTheme.Subtext0;
        _status.Font = WbTheme.UISmall;
        _status.SizingGrip = true;
        _status.Renderer = new DarkToolStripRenderer();

        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStats = new ToolStripStatusLabel("") { TextAlign = ContentAlignment.MiddleRight, AutoSize = true };
        var statusSep = new ToolStripStatusLabel(" | ") { TextAlign = ContentAlignment.MiddleRight, AutoSize = true, ForeColor = WbTheme.Surface1 };
        _statusPos = new ToolStripStatusLabel("Ln 1, Col 1") { TextAlign = ContentAlignment.MiddleRight, AutoSize = true };

        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _statusStats, statusSep, _statusPos });
        Controls.Add(_status);
    }

    // ── Layout ────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        this.IsMdiContainer = true;

        ThemeBase initTheme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
        CustomizeDockPalette(initTheme);

        _dockPanel = new DockPanel
        {
            Dock = DockStyle.Fill,
            DocumentStyle = DocumentStyle.DockingWindow,
            BackColor = WbTheme.Crust,
            Theme = initTheme,
            DockRightPortion = 0.5 // Set Source and AST to 50/50 initially
        };

        Controls.Add(_dockPanel);
        _dockPanel.SendToBack();

        BuildSourcePanel();

        BuildTreePanel();
        BuildEmitTreePanel();
        BuildDiagTabs();
        BuildTraceVisualizerPanel();

        _sourceContent = new DockContent { Text = "Source Editor", BackColor = WbTheme.Base, CloseButtonVisible = false, HideOnClose = true };
        _sourceContent.FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
            }
        };
        _sourceContent.Controls.Add(_sourcePanel);
        _sourceContent.Show(_dockPanel, DockState.Document);

        _emitSourceEditor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoFont,
            BorderStyle = BorderStyle.None,
            WordWrap = _wordWrap,
            AcceptsTab = true,
            DetectUrls = false,
            ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both,
            HideSelection = false
        };
        _emitSourceEditor.HandleCreated += (s, e) => SetWindowTheme(_emitSourceEditor.Handle, "DarkMode_Explorer", null);
        AttachEditorContextMenu(_emitSourceEditor);
        _emitSourceEditor.VScroll += (s, e) =>
        {
            if (_isHighlighting) return;
            _emitSourceEditorScrollTimer.Stop();
            _emitSourceEditorScrollTimer.Start();
        };
        _emitSourceEditor.Resize += (s, e) =>
        {
            if (_isHighlighting) return;
            _emitSourceEditorScrollTimer.Stop();
            _emitSourceEditorScrollTimer.Start();
        };

        _emitSourceContent = new DockContent { Text = "Emitted Source", BackColor = WbTheme.Base, HideOnClose = true };
        _emitSourceContent.Controls.Add(_emitSourceEditor);
        // Do not show EmitSourceContent by default, it will be shown when emitting.

        _treeContent = new DockContent { Text = "Source AST", BackColor = WbTheme.Base, HideOnClose = true };
        _treeContent.Controls.Add(_treePanel);
        _treeContent.Show(_dockPanel, DockState.DockRight);

        _emitTreeContent = new DockContent { Text = "Emitted AST", BackColor = WbTheme.Base, HideOnClose = true };
        _emitTreeContent.Controls.Add(_emitTreePanel);
        _emitTreeContent.Show(_dockPanel, DockState.DockRight);
        _emitTreeContent.Hide(); // Hidden by default, shown when emitted source is active

        _diagContentWindow = new DockContent { Text = "Diagnostics", BackColor = WbTheme.Base, HideOnClose = true };
        _diagContentWindow.Controls.Add(_diagTabs);
        _diagContentWindow.Show(_dockPanel, DockState.DockBottom);

        _traceVisualizerContent = new DockContent { Text = "Trace Visualizer", BackColor = WbTheme.Base, HideOnClose = true };
        _traceVisualizerContent.Controls.Add(_tracePanel);
        _traceVisualizerContent.Show(_dockPanel, DockState.DockBottom);
        _traceVisualizerContent.Hide();

        _dockPanel.ActiveDocumentChanged += (s, e) => UpdateActiveDocumentPanes();

        // Ensure correct Z-order
        _dockPanel.BringToFront();
    }

    private void UpdateActiveDocumentPanes()
    {
        if (_dockPanel.ActiveDocument == _sourceContent)
        {
            _emitTreeContent.Hide();
            _treeContent.Show(_dockPanel, DockState.DockRight);
        }
        else if (_dockPanel.ActiveDocument == _emitSourceContent)
        {
            _treeContent.Hide();
            _emitTreeContent.Show(_dockPanel, DockState.DockRight);
        }
        else
        {
            _treeContent.Hide();
            _emitTreeContent.Hide();
        }

        var pb = _dockPanel.ActiveDocument as PipelineBuilderContent;
        if (pb != null && !pb.IsDisposed && !pb.Disposing)
        {
            pb.Refresh();
            pb.PerformLayout();
        }
    }

    private void ShowEmitSourceTab(string content)
    {
        if (_emitSourceContent == null || _emitSourceContent.IsDisposed)
        {
            _emitSourceEditor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = WbTheme.Base,
                ForeColor = WbTheme.Text,
                Font = WbTheme.MonoFont,
                ReadOnly = true,
                Multiline = true,
                WordWrap = _wordWrap,
                ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both
            };
            _emitSourceEditor.HandleCreated += (s, e) => SetWindowTheme(_emitSourceEditor.Handle, "DarkMode_Explorer", null);
            _emitSourceContent = new DockContent { Text = "Emitted Source", BackColor = WbTheme.Base, HideOnClose = true };
            _emitSourceContent.Controls.Add(_emitSourceEditor);
        }
        _emitSourceEditor.Clear();
        _emitSourceEditor.Text = content;
        _emitSourceContent.Show(_dockPanel, DockState.Document);
        _emitSourceContent.Activate();
        HighlightControl(_emitSourceEditor);
    }

    private void CloseActiveTab()
    {
        var activeDoc = _dockPanel.ActiveDocument as DockContent;
        if (activeDoc != null)
        {
            if (activeDoc.CloseButtonVisible || activeDoc is PipelineBuilderContent || activeDoc == _emitSourceContent)
            {
                activeDoc.Close();
            }
        }
    }

    // ── Diagnostics Tabs ──────────────────────────────────────────────────

    private void BuildDiagTabs()
    {
        _diagTabs = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Crust };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, BackColor = WbTheme.Mantle, WrapContents = false };
        _diagContent = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Base };

        _btnErrTab = new Button { Text = "! Errors (0)", FlatStyle = FlatStyle.Flat, Height = 32, Width = 120, BackColor = WbTheme.Mantle, ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, Margin = new Padding(0) };
        _btnErrTab.FlatAppearance.BorderSize = 0;
        _btnLogTab = new Button { Text = "Parse Log", FlatStyle = FlatStyle.Flat, Height = 32, Width = 120, BackColor = WbTheme.Mantle, ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, Margin = new Padding(0) };
        _btnLogTab.FlatAppearance.BorderSize = 0;
        _btnRunTab = new Button { Text = "Run Output", FlatStyle = FlatStyle.Flat, Height = 32, Width = 120, BackColor = WbTheme.Mantle, ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, Margin = new Padding(0) };
        _btnRunTab.FlatAppearance.BorderSize = 0;
        _btnEmitTab = new Button { Text = "Emit", FlatStyle = FlatStyle.Flat, Height = 32, Width = 120, BackColor = WbTheme.Mantle, ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, Margin = new Padding(0) };
        _btnEmitTab.FlatAppearance.BorderSize = 0;

        header.Controls.AddRange(new Control[] { _btnErrTab, _btnLogTab, _btnRunTab, _btnEmitTab });

        _diagTabs.Controls.Add(_diagContent);
        _diagTabs.Controls.Add(header);

        // ── Error List tab ──
        _errPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        _errorGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            GridColor = WbTheme.Surface0,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = WbTheme.Base,
                ForeColor = WbTheme.Text,
                SelectionBackColor = WbTheme.Selection,
                SelectionForeColor = WbTheme.Text,
                Font = WbTheme.MonoSmall
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = WbTheme.Mantle,
                ForeColor = WbTheme.Subtext0,
                Font = WbTheme.UISmall
            },
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowTemplate = { Height = 26 }
        };

        _errorGrid.Columns.AddRange(new DataGridViewColumn[] {
            new DataGridViewTextBoxColumn { Name = "Sev", HeaderText = "!", Width = 30, FillWeight = 8 },
            new DataGridViewTextBoxColumn { Name = "Line", HeaderText = "Line", Width = 50, FillWeight = 10 },
            new DataGridViewTextBoxColumn { Name = "Col", HeaderText = "Col", Width = 40, FillWeight = 8 },
            new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Code", Width = 60, FillWeight = 12 },
            new DataGridViewTextBoxColumn { Name = "Message", HeaderText = "Message", Width = 300, FillWeight = 42 },
            new DataGridViewTextBoxColumn { Name = "Context", HeaderText = "Context", Width = 200, FillWeight = 20 }
        });

        _errorGrid.CellDoubleClick += ErrorGrid_CellDoubleClick;
        _errorGrid.SelectionChanged += ErrorGrid_SelectionChanged;
        EnableDoubleBuffering(_errorGrid);
        _errPanel.Controls.Add(_errorGrid);

        // ── Parse Log tab ──
        _logPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        _parseLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = _wordWrap,
            DetectUrls = false,
            ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both
        };
        _parseLog.HandleCreated += (s, e) => SetWindowTheme(_parseLog.Handle, "DarkMode_Explorer", null);
        _logPanel.Controls.Add(_parseLog);

        // ── Run Output tab ──
        _runPanel = new Panel { Dock = DockStyle.Fill, Visible = false };

        // Run config bar
        var runConfig = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = WbTheme.Mantle, Padding = new Padding(8, 4, 8, 4) };

        var lblDir = new Label { Text = "Working Dir:", ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(8, 8) };
        _runDirBox = new TextBox { Location = new Point(90, 5), Width = 300, BackColor = WbTheme.Surface0, ForeColor = WbTheme.Text, Font = WbTheme.UISmall, BorderStyle = BorderStyle.None };
        var btnBrowseDir = new Button { Text = "...", Location = new Point(394, 4), Size = new Size(28, 22), FlatStyle = FlatStyle.Flat, BackColor = WbTheme.Surface0, ForeColor = WbTheme.Text, Font = WbTheme.UISmall };
        btnBrowseDir.Click += (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog()) { if (dlg.ShowDialog() == DialogResult.OK) _runDirBox.Text = dlg.SelectedPath; }
        };

        var lblTemp = new Label { Text = "Temp Script:", ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(8, 36) };
        _tempPathBox = new TextBox { Location = new Point(90, 33), Width = 300, BackColor = WbTheme.Surface0, ForeColor = WbTheme.Text, Font = WbTheme.UISmall, BorderStyle = BorderStyle.None };
        SetCueText(_tempPathBox, "(leave blank for temp folder)");

        runConfig.Controls.AddRange(new Control[] { lblDir, _runDirBox, btnBrowseDir, lblTemp, _tempPathBox });
        runConfig.Resize += (s, e) =>
        {
            int w = Math.Max(100, runConfig.Width - 140);
            _runDirBox.Width = w;
            _tempPathBox.Width = w;
            btnBrowseDir.Location = new Point(90 + w + 4, 4);
        };

        _runOutput = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = _wordWrap,
            DetectUrls = false,
            ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both
        };
        _runOutput.HandleCreated += (s, e) => SetWindowTheme(_runOutput.Handle, "DarkMode_Explorer", null);

        _runPanel.Controls.Add(_runOutput);
        _runPanel.Controls.Add(runConfig);

        // ── Emit tab ──
        _emitPanel = new Panel { Dock = DockStyle.Fill, Visible = false };

        // Emit toolbar
        var emitToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(6, 4, 6, 4),
            WrapContents = false
        };

        var btnCopyEmit = new Button
        {
            Text = "Copy",
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Margin = new Padding(0, 0, 8, 0),
            Height = 24
        };
        btnCopyEmit.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnCopyEmit.Click += (s, e) => CopyEmitToClipboard();

        var btnExportEmit = new Button
        {
            Text = "Export",
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Margin = new Padding(0, 0, 8, 0),
            Height = 24
        };
        btnExportEmit.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnExportEmit.Click += (s, e) => ExportEmitToFile();

        var btnInlineSource = new Button
        {
            Text = "Inline in Source",
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Margin = new Padding(0, 0, 8, 0),
            Height = 24
        };
        btnInlineSource.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnInlineSource.Click += (s, e) => InlineEmitInSource();

        emitToolbar.Controls.AddRange(new Control[] { btnCopyEmit, btnExportEmit, btnInlineSource });

        _emitView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = _wordWrap,
            DetectUrls = false,
            ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both
        };
        _emitView.HandleCreated += (s, e) => SetWindowTheme(_emitView.Handle, "DarkMode_Explorer", null);
        AttachEditorContextMenu(_emitView);
        _emitView.VScroll += (s, e) =>
        {
            if (_isHighlighting) return;
            _emitViewScrollTimer.Stop();
            _emitViewScrollTimer.Start();
        };
        _emitView.Resize += (s, e) =>
        {
            if (_isHighlighting) return;
            _emitViewScrollTimer.Stop();
            _emitViewScrollTimer.Start();
        };
        _emitPanel.Controls.Add(_emitView);
        _emitPanel.Controls.Add(emitToolbar);

        _diagContent.Controls.AddRange(new Control[] { _errPanel, _logPanel, _runPanel, _emitPanel });

        _btnErrTab.Click += (s, e) => SelectTab(0);
        _btnLogTab.Click += (s, e) => SelectTab(1);
        _btnRunTab.Click += (s, e) => SelectTab(2);
        _btnEmitTab.Click += (s, e) => SelectTab(3);

        if (_debugMode)
        {
            _btnDebugTab = new Button { Text = "Debug Log", FlatStyle = FlatStyle.Flat, Height = 32, Width = 120, BackColor = WbTheme.Mantle, ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, Margin = new Padding(0) };
            _btnDebugTab.FlatAppearance.BorderSize = 0;
            header.Controls.Add(_btnDebugTab);

            _debugPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            _debugLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = WbTheme.Base,
                ForeColor = WbTheme.Text,
                Font = WbTheme.MonoSmall,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };
            _debugLog.HandleCreated += (s, e) => SetWindowTheme(_debugLog.Handle, "DarkMode_Explorer", null);
            _debugPanel.Controls.Add(_debugLog);
            _diagContent.Controls.Add(_debugPanel);

            _btnDebugTab.Click += (s, e) => SelectTab(4);
        }

        SelectTab(0);
    }

    private void SelectTab(int index)
    {
        _errPanel.Visible = index == 0;
        _logPanel.Visible = index == 1;
        _runPanel.Visible = index == 2;
        _emitPanel.Visible = index == 3;
        if (_debugPanel != null) _debugPanel.Visible = index == 4;

        _btnErrTab.BackColor = index == 0 ? WbTheme.Surface0 : WbTheme.Mantle;
        _btnErrTab.ForeColor = index == 0 ? WbTheme.Lavender : WbTheme.Subtext0;
        _btnLogTab.BackColor = index == 1 ? WbTheme.Surface0 : WbTheme.Mantle;
        _btnLogTab.ForeColor = index == 1 ? WbTheme.Lavender : WbTheme.Subtext0;
        _btnRunTab.BackColor = index == 2 ? WbTheme.Surface0 : WbTheme.Mantle;
        _btnRunTab.ForeColor = index == 2 ? WbTheme.Lavender : WbTheme.Subtext0;
        _btnEmitTab.BackColor = index == 3 ? WbTheme.Surface0 : WbTheme.Mantle;
        _btnEmitTab.ForeColor = index == 3 ? WbTheme.Lavender : WbTheme.Subtext0;

        if (_btnDebugTab != null)
        {
            _btnDebugTab.BackColor = index == 4 ? WbTheme.Surface0 : WbTheme.Mantle;
            _btnDebugTab.ForeColor = index == 4 ? WbTheme.Lavender : WbTheme.Subtext0;
        }
    }

    internal static void CustomizeDockPalette(ThemeBase theme)
    {
        if (theme == null) return;
        try
        {
            var propInfo = theme.GetType().GetProperty("ColorPalette");
            if (propInfo == null) return;
            var palette = propInfo.GetValue(theme, null);
            if (palette == null) return;

            Color accentBg = WbTheme.Current.Selection;
            Color accentText = WbTheme.Current.Text;
            Color accentHover = Color.FromArgb(
                (accentBg.R * 3 + accentText.R) / 4,
                (accentBg.G * 3 + accentText.G) / 4,
                (accentBg.B * 3 + accentText.B) / 4);

            Color cCrust = WbTheme.Current.Crust;
            Color cMantle = WbTheme.Current.Mantle;
            Color cSurf0 = WbTheme.Current.Surface0;
            Color cSurf1 = WbTheme.Current.Surface1;
            Color cSub = WbTheme.Current.Subtext0;
            Color cTxt = WbTheme.Current.Text;
            bool dark = WbTheme.Current.IsDark;

            foreach (var prop in palette.GetType().GetProperties())
            {
                if (prop.PropertyType == typeof(Color) && prop.CanWrite)
                {
                    var c = (Color)prop.GetValue(palette, null);
                    prop.SetValue(palette, MapPaletteColor(c, prop.Name, accentBg, accentHover, accentText, cCrust, cMantle, cSurf0, cSurf1, cSub, cTxt, dark), null);
                }
                else
                {
                    var subObj = prop.GetValue(palette, null);
                    if (subObj != null && subObj.GetType() != typeof(Color))
                    {
                        foreach (var subProp in subObj.GetType().GetProperties())
                        {
                            if (subProp.PropertyType == typeof(Color) && subProp.CanWrite)
                            {
                                var c = (Color)subProp.GetValue(subObj, null);
                                subProp.SetValue(subObj, MapPaletteColor(c, prop.Name + "." + subProp.Name, accentBg, accentHover, accentText, cCrust, cMantle, cSurf0, cSurf1, cSub, cTxt, dark), null);
                            }
                        }
                    }
                }
            }

            // DockPanelSuite caches SolidBrushes and Pens in the PaintingService.
            // We must also update these to ensure buttons and borders use the new palette.
            var psProp = theme.GetType().GetProperty("PaintingService");
            var paintingService = psProp == null ? null : psProp.GetValue(theme, null);
            if (paintingService != null)
            {
                foreach (var prop in paintingService.GetType().GetProperties())
                {
                    if (prop.PropertyType == typeof(SolidBrush))
                    {
                        var brush = (SolidBrush)prop.GetValue(paintingService, null);
                        if (brush != null)
                        {
                            brush.Color = MapPaletteColor(brush.Color, prop.Name, accentBg, accentHover, accentText, cCrust, cMantle, cSurf0, cSurf1, cSub, cTxt, dark);
                        }
                    }
                    else if (prop.PropertyType == typeof(Pen))
                    {
                        var pen = (Pen)prop.GetValue(paintingService, null);
                        if (pen != null)
                        {
                            pen.Color = MapPaletteColor(pen.Color, prop.Name, accentBg, accentHover, accentText, cCrust, cMantle, cSurf0, cSurf1, cSub, cTxt, dark);
                        }
                    }
                }
            }

            // DockPanelSuite also caches button glyphs as Bitmaps in the ImageService.
            // We must tint these images pixel-by-pixel to remove hard-coded blue pixels.
            var imgProp = theme.GetType().GetProperty("ImageService");
            var imageService = imgProp == null ? null : imgProp.GetValue(theme, null);
            if (imageService != null)
            {
                foreach (var prop in imageService.GetType().GetProperties())
                {
                    if (prop.PropertyType == typeof(System.Drawing.Bitmap) || prop.PropertyType == typeof(System.Drawing.Image))
                    {
                        var bmp = prop.GetValue(imageService, null) as System.Drawing.Bitmap;
                        if (bmp != null)
                        {
                            try
                            {
                                // We must use a lock/clone approach if the image is locked, 
                                // but typically these cached bitmaps are just standard instances.
                                for (int x = 0; x < bmp.Width; x++)
                                {
                                    for (int y = 0; y < bmp.Height; y++)
                                    {
                                        Color c = bmp.GetPixel(x, y);
                                        Color newC = MapPaletteColor(c, "ImagePixel", accentBg, accentHover, accentText, cCrust, cMantle, cSurf0, cSurf1, cSub, cTxt, dark);
                                        if (c != newC)
                                        {
                                            bmp.SetPixel(x, y, newC);
                                        }
                                    }
                                }
                            }
                            catch { } // Ignore locked bitmaps or pixel format errors
                        }
                    }
                }
            }
        }
        catch { }
    }

    internal static Color MapPaletteColor(Color c, string propName, Color accentBg, Color accentHover, Color accentText, Color crust, Color mantle, Color surf0, Color surf1, Color subtext, Color text, bool isDark)
    {
        if (c.A == 0) return c;

        // Ensure text/glyphs never get mapped to surface background colors due to brightness rules
        if (propName.Contains("Text") || propName.Contains("Glyph") || propName.Contains("Arrow"))
        {
            if (propName.Contains("Inactive") || propName.Contains("Unselected")) return subtext;
            return text;
        }

        // Is it fundamentally blue-ish? (covers dark accents and light blue glyphs like 208,230,245)
        if (c.B > c.R + 20 && c.B > c.G + 10)
        {
            if (propName.Contains("Hover") || propName.Contains("Pressed") || propName.Contains("ActiveHovered"))
            {
                return accentHover;
            }

            if (propName.Contains("Background") || propName.Contains("Border") || propName.Contains("Active")) return accentBg;

            // Fallback for anonymous pixels
            if (c.R > 150) return text; // Very bright blue -> Text
            return c.G > 140 ? accentHover : accentBg; // Mid-bright blue -> Hover/Bg
        }

        // Is grey-ish
        int diff = Math.Max(Math.Max(c.R, c.G), c.B) - Math.Min(Math.Min(c.R, c.G), c.B);
        if (diff < 40)
        {
            int avg = (c.R + c.G + c.B) / 3;
            if (isDark)
            {
                if (avg < 50) return crust;
                if (avg < 70) return mantle;
                if (avg < 100) return surf0;
                if (avg < 150) return surf1;
                if (avg < 220) return subtext;
                return text;
            }
            else
            {
                if (avg < 50) return text;
                if (avg < 100) return subtext;
                if (avg < 150) return surf1;
                if (avg < 220) return mantle;
                return crust;
            }
        }
        return c;
    }

    private void UpdateRecentFilesMenu()
    {
        if (_recentFilesMenu == null) return;
        _recentFilesMenu.DropDownItems.Clear();
        var state = AHK2AST.UI.WorkbenchState.Load();
        if (state.RecentFiles == null) state.RecentFiles = new List<string>();

        // Remove files that no longer exist
        state.RecentFiles.RemoveAll(p => !System.IO.File.Exists(p));
        state.Save();

        if (state.RecentFiles.Count == 0)
        {
            var noneItem = new ToolStripMenuItem("(none)") { Enabled = false };
            _recentFilesMenu.DropDownItems.Add(noneItem);
            return;
        }

        foreach (var path in state.RecentFiles)
        {
            var item = new ToolStripMenuItem(path);
            string targetPath = path;
            item.Click += (s, e) => LoadFile(targetPath);
            _recentFilesMenu.DropDownItems.Add(item);
        }
    }

    private DiffWorkspaceContent GetOrCreateDiffWorkspace()
    {
        foreach (var doc in _dockPanel.Documents)
        {
            var diffDoc = doc as DiffWorkspaceContent;
            if (diffDoc != null)
            {
                diffDoc.Show(_dockPanel);
                return diffDoc;
            }
        }
        var newDiff = new DiffWorkspaceContent();
        newDiff.Show(_dockPanel, DockState.Document);
        UpdateControlTheme(newDiff);
        return newDiff;
    }

    private EscaperUnescaperContent GetOrCreateEscapeWorkspace()
    {
        foreach (var doc in _dockPanel.Documents)
        {
            var escDoc = doc as EscaperUnescaperContent;
            if (escDoc != null)
            {
                escDoc.Show(_dockPanel);
                return escDoc;
            }
        }
        var newEsc = new EscaperUnescaperContent();
        newEsc.Show(_dockPanel, DockState.Document);
        UpdateControlTheme(newEsc);
        return newEsc;
    }

    private void AttachEditorContextMenu(RichTextBox editor)
    {
        var menu = new ContextMenuStrip { BackColor = WbTheme.Crust, ForeColor = WbTheme.Text };
        menu.Renderer = new DarkMenuRenderer();
        
        var sendLeft = new ToolStripMenuItem("Send to Diff Viewer as Original (Left)") { Image = null };
        sendLeft.Click += (s, e) => 
        {
            var diff = GetOrCreateDiffWorkspace();
            string text = (editor.SelectionLength > 0) ? editor.SelectedText : editor.Text;
            diff.SetLeftText(text);
        };
        menu.Items.Add(sendLeft);

        var sendRight = new ToolStripMenuItem("Send to Diff Viewer as Modified (Right)") { Image = null };
        sendRight.Click += (s, e) => 
        {
            var diff = GetOrCreateDiffWorkspace();
            string text = (editor.SelectionLength > 0) ? editor.SelectedText : editor.Text;
            diff.SetRightText(text);
        };
        menu.Items.Add(sendRight);

        var sendEscapeLiteral = new ToolStripMenuItem("Send to Escape Tool as Escaped") { Image = null };
        sendEscapeLiteral.Click += (s, e) => 
        {
            var esc = GetOrCreateEscapeWorkspace();
            string text = (editor.SelectionLength > 0) ? editor.SelectedText : editor.Text;
            esc.SetEscapedText(text);
        };
        menu.Items.Add(sendEscapeLiteral);

        var sendEscapeRaw = new ToolStripMenuItem("Send to Escape Tool as Unescaped") { Image = null };
        sendEscapeRaw.Click += (s, e) => 
        {
            var esc = GetOrCreateEscapeWorkspace();
            string text = (editor.SelectionLength > 0) ? editor.SelectedText : editor.Text;
            esc.SetUnescapedText(text);
        };
        menu.Items.Add(sendEscapeRaw);

        menu.Items.Add(new ToolStripSeparator());

        var cut = new ToolStripMenuItem("Cut", null, (s, e) => editor.Cut());
        var copy = new ToolStripMenuItem("Copy", null, (s, e) => editor.Copy());
        var paste = new ToolStripMenuItem("Paste", null, (s, e) => editor.Paste());
        var selectAll = new ToolStripMenuItem("Select All", null, (s, e) => editor.SelectAll());

        menu.Opening += (s, e) =>
        {
            bool hasSelection = editor.SelectionLength > 0;
            sendLeft.Text = hasSelection ? "Send Selection to Diff Viewer as Original (Left)" : "Send to Diff Viewer as Original (Left)";
            sendRight.Text = hasSelection ? "Send Selection to Diff Viewer as Modified (Right)" : "Send to Diff Viewer as Modified (Right)";
            sendEscapeLiteral.Text = hasSelection ? "Send Selection to Escape Tool as Escaped" : "Send to Escape Tool as Escaped";
            sendEscapeRaw.Text = hasSelection ? "Send Selection to Escape Tool as Unescaped" : "Send to Escape Tool as Unescaped";

            cut.Enabled = !editor.ReadOnly && hasSelection;
            copy.Enabled = hasSelection;
            paste.Enabled = !editor.ReadOnly && Clipboard.ContainsText();
            selectAll.Enabled = editor.TextLength > 0;
        };

        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(selectAll);

        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = WbTheme.Crust;
            item.ForeColor = WbTheme.Text;
        }

        editor.ContextMenuStrip = menu;
    }

    private void OpenNimBuildManager()
    {
        foreach (var doc in _dockPanel.Documents)
        {
            var buildDoc = doc as NimBuildContent;
            if (buildDoc != null)
            {
                buildDoc.Show(_dockPanel);
                buildDoc.Activate();
                return;
            }
        }
        var newBuild = new NimBuildContent(() => _sourceEditor.Text, () => _currentFile, _engine);
        newBuild.Show(_dockPanel, DockState.Document);
        UpdateControlTheme(newBuild);
        newBuild.Activate();
    }

    private void ToggleWordWrap(ToolStripMenuItem item)
    {
        _wordWrap = !_wordWrap;
        item.Checked = _wordWrap;

        try
        {
            var state = AHK2AST.UI.WorkbenchState.Load();
            state.WordWrap = _wordWrap;
            state.Save();
        }
        catch { }

        var editors = new[] { _sourceEditor, _emitSourceEditor, _emitView, _parseLog, _runOutput };
        foreach (var ed in editors)
        {
            if (ed != null)
            {
                ed.WordWrap = _wordWrap;
                ed.ScrollBars = _wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
            }
        }

        foreach (var doc in _dockPanel.Documents)
        {
            var esc = doc as EscaperUnescaperContent;
            if (esc != null)
            {
                esc.SetWordWrap(_wordWrap);
            }
            var diff = doc as DiffWorkspaceContent;
            if (diff != null)
            {
                diff.SetWordWrap(_wordWrap);
            }
        }
        
        if (_lineNumberPanel != null)
        {
            _lineNumberPanel.Invalidate();
        }
    }

    private void ShowAboutDialog()
    {
        using (var about = new AboutForm())
        {
            about.ShowDialog(this);
        }
    }
}

public class AboutForm : Form
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    private Panel pluginsPanel;

    public AboutForm()
    {
        Text = "About AHK2AST Workbench";
        Size = new Size(400, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = WbTheme.Base;
        ForeColor = WbTheme.Text;
        Font = WbTheme.UIFont;

        var lblTitle = new Label
        {
            Text = "AHK2AST Workbench",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = WbTheme.Lavender,
            Location = new Point(15, 10),
            AutoSize = true
        };
        Controls.Add(lblTitle);

        string workbenchVer = typeof(AstWorkbenchForm).Assembly.GetName().Version.ToString();
        string engineVer = typeof(AhkAstEngine).Assembly.GetName().Version.ToString();

        var lblVersion = new Label
        {
            Text = string.Format("Workbench: v{0}\nAST Engine: v{1}", workbenchVer, engineVer),
            ForeColor = WbTheme.Subtext0,
            Font = WbTheme.UISmall,
            Location = new Point(15, 45),
            AutoSize = true
        };
        Controls.Add(lblVersion);

        var lblPluginsHeader = new Label
        {
            Text = "Bundled AST Plugins:",
            ForeColor = WbTheme.Lavender,
            Font = WbTheme.UIBold,
            Location = new Point(15, 90),
            AutoSize = true
        };
        Controls.Add(lblPluginsHeader);

        pluginsPanel = new Panel
        {
            Location = new Point(15, 115),
            Size = new Size(355, 180),
            BackColor = WbTheme.Mantle,
            AutoScroll = true
        };
        pluginsPanel.Paint += (s, e) =>
        {
            using (var pen = new Pen(WbTheme.Surface0))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, pluginsPanel.Width - 1, pluginsPanel.Height - 1);
            }
        };

        int y = 5;
        foreach (var type in AHK2AST.Plugins.PluginRegistry.RegisteredPluginTypes)
        {
            string name = type.Name;
            string category = "";
            string icon = "🔌";
            string version = type.Assembly.GetName().Version.ToString();

            try
            {
                var instance = Activator.CreateInstance(type) as AHK2AST.Plugins.IFlowPlugin;
                if (instance != null)
                {
                    name = instance.Name;
                    category = instance.Category;
                    if (!string.IsNullOrEmpty(instance.Icon)) icon = instance.Icon;
                    if (!string.IsNullOrEmpty(instance.Version)) version = instance.Version;
                }
            }
            catch {}

            var lblPlugin = new Label
            {
                Text = string.Format("{0} {1} ({2})", icon, name, category),
                ForeColor = WbTheme.Text,
                Font = WbTheme.UIBold,
                Location = new Point(8, y),
                AutoSize = true
            };
            pluginsPanel.Controls.Add(lblPlugin);

            var lblPluginVer = new Label
            {
                Text = string.Format("Version: {0}", version),
                ForeColor = WbTheme.Subtext0,
                Font = WbTheme.UISmall,
                Location = new Point(28, y + 16),
                AutoSize = true
            };
            pluginsPanel.Controls.Add(lblPluginVer);

            y += 38;
        }

        Controls.Add(pluginsPanel);

        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(140, 310),
            Size = new Size(100, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall
        };
        btnClose.FlatAppearance.BorderColor = WbTheme.Surface1;
        btnClose.Click += (s, e) => Close();
        btnClose.MouseEnter += (s, e) => { btnClose.FlatAppearance.BorderColor = WbTheme.Accent; btnClose.ForeColor = WbTheme.Accent; };
        btnClose.MouseLeave += (s, e) => { btnClose.FlatAppearance.BorderColor = WbTheme.Surface1; btnClose.ForeColor = WbTheme.Text; };
        Controls.Add(btnClose);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            string scrollbarTheme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";
            SetWindowTheme(pluginsPanel.Handle, scrollbarTheme, null);
        }
        catch {}
    }
}

