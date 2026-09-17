// AHK2 AST Workbench — main window.
//
// Layout: script and output tabs in the middle; Outline and AST on the right; Problems and Console at the
// bottom. The active script is parsed in the background as you type (Problems, squiggles, Outline, AST follow).
// Flows run on the same background worker and open their result as an output tab.
//
// Partial files: .Shell (commands, menu, toolbar, palette, themes), .Docs (documents, navigation, session),
// .Analysis (engine worker, live parse, editor <-> AST sync), .Flows (flow runs, compare, flow editor),
// .Runner (run / stop / validate with AutoHotkey), .Trace (trace visualizer).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal partial class AstWorkbenchForm : Form, IWorkbenchHost
{
    [STAThread]
    static void Main(string[] args)
    {
        UiLoader.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception, true);
        Application.ThreadException += (s, e) => ReportCrash(e.Exception, false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new AstWorkbenchForm();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--tour" && i + 1 < args.Length) { form.EnableTour(args[++i]); continue; }
            if (File.Exists(args[i])) form.LoadFileOnShown(args[i]);
        }
        Application.Run(form);
    }

    static readonly string CrashLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

    /// <summary>Appends to crash.log and tells the user (the app keeps running for UI-thread exceptions).</summary>
    static void ReportCrash(Exception ex, bool fatal)
    {
        try { File.AppendAllText(CrashLog, "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + (fatal ? " (fatal)" : "") + "\r\n" + ex + "\r\n\r\n"); } catch { }
        if (ex == null) return;
        try
        {
            MessageBox.Show("Something went wrong: " + ex.Message + "\n\nDetails were written to " + CrashLog + (fatal ? "" : "\n\nThe workbench keeps running; save your work if things look off."),
                "AST Workbench", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }

    // ── Shell ───────────────────────────────────────────────────────────────────────────────────────────
    private MenuStrip _menu;
    private ToolStrip _toolbar;
    private StatusStrip _status;
    private ToolStripStatusLabel _statusLabel, _statusScript, _statusParse, _statusProblems, _statusPos;
    private DockPanel _dockPanel;

    // ── Panels ──────────────────────────────────────────────────────────────────────────────────────────
    private ProblemsPanel _problems;
    private AstPanel _astPanel;
    private OutlinePanel _outline;
    private ConsolePanel _console;
    private DiffWorkspaceContent _diffContent;
    private EscaperUnescaperContent _escapeContent;

    // ── Trace visualizer (AstWorkbenchForm.Trace.cs) ────────────────────────────────────────────────────
    private DockContent _traceVisualizerContent;
    private SplitContainer _traceSplit;
    private TreeView _traceTree;
    private RichTextBox _traceDetails;
    private CheckBox _chkAutoLoadTrace;
    private Button _btnLoadTrace, _btnClearTrace, _btnOpenHtmlView;
    private string _lastLoadedTraceFile;
    private Panel _tracePanel;
    private BorderlessTabControl _traceTabs;
    private Panel _traceTabStrip;
    private Button _btnTraceCallTree, _btnTraceWaterfall, _btnTraceStats;
    private TabPage _traceTreeTab, _traceWaterfallTab, _traceStatsTab;
    private Panel _chartScrollPanel;
    private TraceWaterfallChart _waterfallChart;
    private ListView _traceStatsList;
    private List<TraceItem> _flatTraceItems = new List<TraceItem>();

    // ── State ───────────────────────────────────────────────────────────────────────────────────────────
    private AHK2AST.UI.WorkbenchState _state;
    private AhkAstEngine _engine;            // for flows (has the missing-plugin prompt)
    private readonly List<string> _pendingLoads = new List<string>();

    /// <summary>Path of the script the analysis and flows work on (used by the trace visualizer).</summary>
    private string _currentFile { get { var t = AnalysisTarget; return t == null ? null : t.FilePath; } }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    public AstWorkbenchForm()
    {
        try { SetPreferredAppMode(WbTheme.Current.IsDark ? 2 : 0); } catch { }
        _state = AHK2AST.UI.WorkbenchState.Load();
        var theme = ThemeManager.Themes.FirstOrDefault(t => string.Equals(t.Name, _state.ThemeName, StringComparison.OrdinalIgnoreCase));
        if (theme != null) WbTheme.Current = theme;

        _engine = new AhkAstEngine();
        _engine.OnMissingPlugin = (title, configType) =>
        {
            DialogResult res = DialogResult.No;
            Action ask = () => res = MessageBox.Show(this,
                string.Format("The flow step '{0}' needs the plugin '{1}', which is not in this build of AstEngine.dll.\n\nSkip this step and run the rest of the flow?", title, configType),
                "Missing plugin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (InvokeRequired) Invoke(ask); else ask();
            return res == DialogResult.Yes;
        };

        Text = "AHK2 AST Workbench";
        Icon = BuildAppIcon();
        Size = new Size(1600, 950);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        Font = WbTheme.UIFont;
        BackColor = WbTheme.Crust;

        BuildShell();
        StartEngineWorker();
        ApplyTheme();
    }

    public void LoadFileOnShown(string path) { _pendingLoads.Add(path); }

    /// <summary>For the COM wrapper (AstWorkbench.OpenFile).</summary>
    public void LoadFile(string path) { OpenFile(path); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiTheming.TitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RestoreSession();
        foreach (var p in _pendingLoads) OpenFile(p);
        _pendingLoads.Clear();
        if (Documents().Count == 0) ShowWelcome();
        StartTourIfRequested();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var d in Scripts().ToList())
            if (!d.ConfirmClose()) { e.Cancel = true; return; }
        SaveSession();
        StopRun(false);
        base.OnFormClosing(e);
        foreach (var d in Scripts().ToList()) d.ForceClose = true;
    }

    // ── IWorkbenchHost ──────────────────────────────────────────────────────────────────────────────────

    public void Status(string text, Color color)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => Status(text, color))); return; }
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    static Icon BuildAppIcon()
    {
        try
        {
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (ico != null) return ico;
        }
        catch { }
        return SystemIcons.Application;
    }
}

internal class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 220);
        BackColor = WbTheme.Base;
        ForeColor = WbTheme.Text;
        Font = WbTheme.UIFont;
        var title = new Label { Text = "AHK2 AST Workbench", Font = new Font("Segoe UI", 16f), AutoSize = true, Location = new Point(24, 22), ForeColor = WbTheme.Text };
        var body = new Label
        {
            Text = "Parser, emitter and optimiser for AutoHotkey v2.\n\nEngine " + typeof(AhkAstEngine).Assembly.GetName().Version +
                   "\nFlows are verified by the test harness on a corpus of ~1,000 real scripts.",
            AutoSize = false, Location = new Point(26, 70), Size = new Size(410, 90), ForeColor = WbTheme.Subtext1
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(360, 170), Size = new Size(76, 28) };
        Controls.AddRange(new Control[] { title, body, ok });
        AcceptButton = ok;
        UiTheming.Apply(ok);
        HandleCreated += (s, e) => UiTheming.TitleBar(this);
    }
}
