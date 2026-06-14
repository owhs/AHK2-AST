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
    [STAThread]
    static void Main(string[] args)
    {
        UiLoader.Initialize();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.IO.File.WriteAllText("crash.log", e.ExceptionObject.ToString());
        };
        Application.ThreadException += (s, e) =>
        {
            System.IO.File.WriteAllText("crash.log", e.Exception.ToString());
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new AstWorkbenchForm();
        if (args.Length > 0 && File.Exists(args[0]))
            form.LoadFileOnShown(args[0]);
        Application.Run(form);
    }

    // ── Controls ──────────────────────────────────────────────────────────
    private MenuStrip _menu;
    private ToolStrip _toolbar;
    private StatusStrip _status;
    private ToolStripStatusLabel _statusLabel;
    private ToolStripStatusLabel _statusStats;
    private ToolStripStatusLabel _statusPos;

    // Main layout
    private DockPanel _dockPanel;
    private DockContent _sourceContent;
    private PipelineBuilderContent _pipelineBuilderContent;
    private DockContent _treeContent;
    private DockContent _emitTreeContent;
    private DockContent _diagContentWindow;
    private DockContent _emitSourceContent;

    // Source panel
    private Panel _sourcePanel;
    private RichTextBox _sourceEditor;
    private RichTextBox _emitSourceEditor;
    private Panel _lineNumberPanel;

    // AST tree
    private Panel _treePanel;
    private TreeView _astTree;
    private TextBox _treeFilter;
    private Label _treeStats;

    private Panel _emitTreePanel;
    private TreeView _emitAstTree;
    private TextBox _emitTreeFilter;
    private Label _emitTreeStats;

    // Trace visualizer panel
    private DockContent _traceVisualizerContent;
    private SplitContainer _traceSplit;
    private TreeView _traceTree;
    private RichTextBox _traceDetails;
    private CheckBox _chkAutoLoadTrace;
    private Button _btnLoadTrace;
    private Button _btnClearTrace;
    private Button _btnOpenHtmlView;
    private string _lastLoadedTraceFile;
    private Panel _tracePanel;
    private BorderlessTabControl _traceTabs;
    private Panel _traceTabStrip;
    private Button _btnTraceCallTree;
    private Button _btnTraceWaterfall;
    private Button _btnTraceStats;
    private TabPage _traceTreeTab;
    private TabPage _traceWaterfallTab;
    private TabPage _traceStatsTab;
    private Panel _chartScrollPanel;
    private TraceWaterfallChart _waterfallChart;
    private ListView _traceStatsList;
    private List<TraceItem> _flatTraceItems = new List<TraceItem>();


    private AstNode _emitCurrentAst;

    // Diagnostics tabs
    private Panel _diagTabs;
    private Panel _diagContent;
    private Button _btnErrTab;
    private Button _btnLogTab;
    private Button _btnRunTab;
    private Button _btnEmitTab;
    private Panel _errPanel;
    private Panel _logPanel;
    private Panel _runPanel;
    private Panel _emitPanel;
    private DataGridView _errorGrid;
    private RichTextBox _parseLog;
    private RichTextBox _runOutput;
    private RichTextBox _emitView;

    // Run config
    private TextBox _runDirBox;
    private TextBox _ahkPathBox;
    private TextBox _tempPathBox;

    // State
    private string _currentFile;
    private AhkAstEngine _engine;
    private AstNode _currentAst;
    private Process _runProcess;
    private string _pendingLoadFile;
    private ToolStripMenuItem _recentFilesMenu;
    private bool _followIncludes = true;
    private bool _inlineIncludes = true; // show inlined includes in source editor
    private bool _wordWrap;
    private System.Windows.Forms.Timer _syntaxHighlightTimer;
    private System.Windows.Forms.Timer _sourceEditorScrollTimer;
    private System.Windows.Forms.Timer _emitSourceEditorScrollTimer;
    private System.Windows.Forms.Timer _emitViewScrollTimer;
    private bool _debugMode;
    private bool _isHighlighting;
    private bool _autoFocusCode = true;
    private Button _btnDebugTab;
    private Panel _debugPanel;
    private RichTextBox _debugLog;

    // Emit options state
    private bool _emitComments = true;
    private bool _emitBlankLines = true;
    private bool _useTabs = false;
    private int _indentSize = 4;
    private string _lastEmittedCode = ""; // raw emitted code for copy/export
    private Dictionary<string, Dictionary<int, int>> _sourceLineMappings = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
    private bool _sourceEditorIsInlined = false;

    private static readonly ImageList _nodeIcons = BuildNodeIcons();

    public AstWorkbenchForm()
    {
        try
        {
            // Force native dark scrollbars across the application on Windows 10 (1809+) & Windows 11
            SetPreferredAppMode(2); // 2 = ForceDark
        }
        catch
        {
            try
            {
                AllowDarkThemeForApp(true);
            }
            catch { }
        }

        try
        {
            _debugMode = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DEBUG")) ||
                         File.Exists(Path.Combine(Path.GetDirectoryName(typeof(AstWorkbench).Assembly.Location) ?? "", "DEBUG"));
        }
        catch { }

        _engine = new AhkAstEngine();
        _engine.OnMissingPlugin = (title, configType) =>
        {
            DialogResult res = DialogResult.No;
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() =>
                {
                    res = MessageBox.Show(
                        this,
                        string.Format("The flow step '{0}' requires the plugin '{1}', which is not included in this build of AstEngine.dll.\n\nDo you want to skip this step and execute the rest of the flow?", title, configType),
                        "Missing Plugin",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );
                }));
            }
            else
            {
                res = MessageBox.Show(
                    this,
                    string.Format("The flow step '{0}' requires the plugin '{1}', which is not included in this build of AstEngine.dll.\n\nDo you want to skip this step and execute the rest of the flow?", title, configType),
                    "Missing Plugin",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
            }
            return res == DialogResult.Yes;
        };

        try
        {
            var state = AHK2AST.UI.WorkbenchState.Load();
            _inlineIncludes = state.InlineIncludes;
            _followIncludes = state.FollowIncludes;
            _wordWrap = state.WordWrap;

            if (!string.IsNullOrEmpty(state.ThemeName))
            {
                var savedTheme = ThemeManager.Themes.FirstOrDefault(t => string.Equals(t.Name, state.ThemeName, StringComparison.OrdinalIgnoreCase));
                if (savedTheme != null)
                {
                    WbTheme.Current = savedTheme;
                }
            }
        }
        catch { }

        // Try loading grammar from known path
        // Assembly.Location is empty when loaded from byte[] — guard against that
        try
        {
            string asmLoc = typeof(AstWorkbench).Assembly.Location;
            if (!string.IsNullOrEmpty(asmLoc))
            {
                string grammarPath = Path.Combine(
                    Path.GetDirectoryName(asmLoc), "ahk2_grammar.json");
                if (File.Exists(grammarPath))
                    _engine.LoadGrammar(grammarPath);
            }

            if (_engine != null)
            {
                // Fallback: try relative to base directory
                string altPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "src", "ast", "ahk2_grammar.json");
                if (File.Exists(altPath))
                    _engine.LoadGrammar(altPath);
            }
        }
        catch { /* Grammar is optional — parser works without it */ }

        _syntaxHighlightTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _syntaxHighlightTimer.Tick += (s, e) =>
        {
            LogDebug("Timer Tick: _syntaxHighlightTimer");
            _syntaxHighlightTimer.Stop();
            HighlightControl(_sourceEditor);
        };

        _sourceEditorScrollTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _sourceEditorScrollTimer.Tick += (s, e) =>
        {
            _sourceEditorScrollTimer.Stop();
            HighlightControl(_sourceEditor);
        };

        _emitSourceEditorScrollTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _emitSourceEditorScrollTimer.Tick += (s, e) =>
        {
            _emitSourceEditorScrollTimer.Stop();
            HighlightControl(_emitSourceEditor);
        };

        _emitViewScrollTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _emitViewScrollTimer.Tick += (s, e) =>
        {
            _emitViewScrollTimer.Stop();
            HighlightControl(_emitView);
        };

        InitializeForm();
        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        BuildLayout();

        WbTheme.Apply(this);
        ApplyDeepTheme();
    }

    public void LoadFileOnShown(string path)
    {
        _pendingLoadFile = path;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Theme the dock panel if needed
        try
        {
            if (_dockPanel != null)
            {
                _dockPanel.Theme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
                
                string layoutPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DockLayout.xml");
                if (File.Exists(layoutPath))
                {
                    // Basic attempt to restore layout
                    try { _dockPanel.LoadFromXml(layoutPath, GetContentFromPersistString); } catch { }
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(_pendingLoadFile))
            LoadFile(_pendingLoadFile);
    }

    private IDockContent GetContentFromPersistString(string persistString)
    {
        if (persistString == typeof(PipelineBuilderContent).ToString())
        {
            if (_pipelineBuilderContent == null)
            {
                _pipelineBuilderContent = new PipelineBuilderContent();
                _pipelineBuilderContent.HideOnClose = true;
                _pipelineBuilderContent.GetSourceCodeRequested += () => _sourceEditor.Text;
                _pipelineBuilderContent.GetActiveFilePathRequested += () => _currentFile;
            }
            return _pipelineBuilderContent;
        }
        // Simplified: return existing contents based on Text property or default if not tracked.
        if (persistString.Contains("Source") && _sourceContent != null) return _sourceContent;
        if (persistString.Contains("AST") && _treeContent != null) return _treeContent;
        if (persistString.Contains("EscaperUnescaperContent")) return GetOrCreateEscapeWorkspace();
        return null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        try
        {
            string layoutPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DockLayout.xml");
            if (_dockPanel != null) _dockPanel.SaveAsXml(layoutPath);
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int AllowDarkThemeForApp(bool allow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyThemeToTitleBar();
    }

    private void ApplyThemeToTitleBar()
    {
        if (Environment.OSVersion.Version.Major >= 10)
        {
            int useImmersiveDarkMode = WbTheme.Current.IsDark ? 1 : 0;
            DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
            DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));

            // Flush the window frame to force an instant titlebar repaint
            SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED
        }
    }

    private void RefreshTheme()
    {
        ApplyThemeToTitleBar();

        BackColor = WbTheme.Crust;
        ForeColor = WbTheme.Text;
        Font = WbTheme.UIFont;

        _menu.BackColor = WbTheme.Crust;
        _menu.ForeColor = WbTheme.Text;

        WbTheme.Apply(this);
        ApplyDeepTheme();

        // Refresh editor syntax highlighting
        HighlightControl(_sourceEditor);

        // Redraw tree view
        if (_astTree != null) _astTree.Invalidate();
    }

    // ── Form Setup ────────────────────────────────────────────────────────

    private void InitializeForm()
    {
        Text = "AHK2AST Workbench";
        Size = new Size(1600, 950);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WbTheme.Crust;
        ForeColor = WbTheme.Text;
        Font = WbTheme.UIFont;
        DoubleBuffered = true;
        Icon = BuildAppIcon();
    }

    // ── File Operations ───────────────────────────────────────────────────

    public void LoadFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            _sourceEditor.Text = File.ReadAllText(path, Encoding.UTF8);
            _sourceEditorIsInlined = false;
            HighlightControl(_sourceEditor);
            _currentFile = path;
            Text = "AHK# AST Workbench — " + Path.GetFileName(path);

            // Auto-set working directory to file's directory
            if (string.IsNullOrEmpty(_runDirBox.Text))
                _runDirBox.Text = Path.GetDirectoryName(path);

            if (_sourceContent != null)
            {
                if (_sourceContent.IsHidden)
                {
                    _sourceContent.Show(_dockPanel, DockState.Document);
                }
                _sourceContent.Activate();
            }

            _statusLabel.Text = "Loaded: " + path;
            ParseCurrent();
            AddToRecentFiles(path);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Error loading file: " + ex.Message;
        }
    }

    private void AddToRecentFiles(string path)
    {
        try
        {
            var state = AHK2AST.UI.WorkbenchState.Load();
            if (state.RecentFiles == null) state.RecentFiles = new List<string>();
            state.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            state.RecentFiles.Insert(0, path);
            if (state.RecentFiles.Count > 10)
            {
                state.RecentFiles = state.RecentFiles.Take(10).ToList();
            }
            state.Save();
            UpdateRecentFilesMenu();
        }
        catch { }
    }

    private void OpenFileDialog()
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Filter = "AHK Scripts (*.ahk)|*.ahk|All Files (*.*)|*.*";
            dlg.Title = "Open AHK Script";
            if (_currentFile != null)
                dlg.InitialDirectory = Path.GetDirectoryName(_currentFile);
            if (dlg.ShowDialog() == DialogResult.OK)
                LoadFile(dlg.FileName);
        }
    }

    /*private void OpenFolderDialog()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Select AHK project folder";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _runDirBox.Text = dlg.SelectedPath;
                // Find first .ahk file
                var files = Directory.GetFiles(dlg.SelectedPath, "*.ahk", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    LoadFile(files[0]);
            }
        }
    }*/

    private void SaveSource()
    {
        if (string.IsNullOrEmpty(_currentFile))
        { SaveSourceAs(); return; }

        if (_sourceEditorIsInlined)
        {
            var result = MessageBox.Show(
                "The source editor currently shows inlined includes. Saving will permanently overwrite your file with the inlined code, destroying #include directives. Are you sure you want to proceed?",
                "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        File.WriteAllText(_currentFile, _sourceEditor.Text, Encoding.UTF8);
        _statusLabel.Text = "Saved: " + _currentFile;
    }

    private void SaveSourceAs()
    {
        using (var dlg = new SaveFileDialog())
        {
            dlg.Filter = "AHK Scripts (*.ahk)|*.ahk|All Files (*.*)|*.*";
            dlg.Title = "Save AHK Script";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _currentFile = dlg.FileName;
                File.WriteAllText(_currentFile, _sourceEditor.Text, Encoding.UTF8);
                Text = "AHK# AST Workbench — " + Path.GetFileName(_currentFile);
                _statusLabel.Text = "Saved: " + _currentFile;
            }
        }
    }

    internal class LineMapping
    {
        public string FileName { get; set; }
        public int OriginalLine { get; set; }
    }
}
