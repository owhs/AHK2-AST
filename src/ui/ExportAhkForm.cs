using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class ExportAhkForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public string SelectedStyle { get; private set; }
    public string SourceType { get; private set; }
    public string SourceFilePath { get; private set; }
    public bool FollowIncludes { get; private set; }
    public bool InlineIncludes { get; private set; }
    public bool DecideAtRuntime { get; private set; }

    private RadioButton _rbStyleRaw;
    private RadioButton _rbStyleSharp;
    private RadioButton _rbStyleCom;

    private RadioButton _rbSourceFile;
    private RadioButton _rbSourceArg;
    private RadioButton _rbSourceString;

    private Label _lblSourcePath;
    private TextBox _txtSourcePath;
    private Button _btnBrowse;

    private CheckBox _chkFollowIncludes;
    private CheckBox _chkInlineIncludes;
    private CheckBox _chkDecideAtRuntime;

    public ExportAhkForm(string defaultFilePath = null)
    {
        var state = AHK2AST.UI.WorkbenchState.Load();
        SelectedStyle = "raw";
        SourceType = "file";
        FollowIncludes = state.FollowIncludes;
        InlineIncludes = state.InlineIncludes;

        this.Text = "Export AutoHotkey Script";
        this.Size = new Size(450, 505);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = WbTheme.Crust;
        this.ForeColor = WbTheme.Text;

        // Apply dark title bar if possible
        try
        {
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref useImmersiveDarkMode, sizeof(int));
            DwmSetWindowAttribute(this.Handle, 19, ref useImmersiveDarkMode, sizeof(int));
        }
        catch { }

        // Header Label
        var lblHeader = new Label
        {
            Text = "📜 Export Standalone AHK v2 Script",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = WbTheme.Accent,
            Location = new Point(20, 15),
            Size = new Size(400, 30)
        };
        this.Controls.Add(lblHeader);

        // Group 1: Style
        var gpStyle = new GroupBox
        {
            Text = "Export Style",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = WbTheme.Subtext0,
            Location = new Point(20, 55),
            Size = new Size(390, 105),
            FlatStyle = FlatStyle.Flat
        };
        _rbStyleRaw = new RadioButton
        {
            Text = "Raw AutoHotkey v2 (CLR Style - self-contained)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 25),
            Size = new Size(360, 20),
            Checked = true
        };
        _rbStyleCom = new RadioButton
        {
            Text = "Raw AutoHotkey v2 (COM Style - requires registered DLL)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 50),
            Size = new Size(360, 20)
        };
        _rbStyleSharp = new RadioButton
        {
            Text = "AHK# (AHKSharp) Style (requires #include <AHK#>)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 75),
            Size = new Size(360, 20)
        };
        gpStyle.Controls.Add(_rbStyleRaw);
        gpStyle.Controls.Add(_rbStyleCom);
        gpStyle.Controls.Add(_rbStyleSharp);
        this.Controls.Add(gpStyle);

        // Group 2: Source Type
        var gpSource = new GroupBox
        {
            Text = "Input Source Type",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = WbTheme.Subtext0,
            Location = new Point(20, 170),
            Size = new Size(390, 150),
            FlatStyle = FlatStyle.Flat
        };
        _rbSourceFile = new RadioButton
        {
            Text = "Read source file dynamically (reads a file path)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 22),
            Size = new Size(360, 18),
            Checked = true
        };
        _rbSourceArg = new RadioButton
        {
            Text = "Use command argument (accepts a filepath or raw code as arg)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 42),
            Size = new Size(360, 18)
        };
        _rbSourceString = new RadioButton
        {
            Text = "Load file into runtime textbox (allows editing before execution)",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 62),
            Size = new Size(360, 18)
        };

        _lblSourcePath = new Label
        {
            Text = "File Path:",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 95),
            Size = new Size(60, 20)
        };

        _txtSourcePath = new TextBox
        {
            Text = !string.IsNullOrEmpty(defaultFilePath) ? defaultFilePath : "source.ahk",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(80, 93),
            Size = new Size(220, 20)
        };

        _btnBrowse = new Button
        {
            Text = "Browse...",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(305, 91),
            Size = new Size(75, 23),
            Cursor = Cursors.Hand
        };
        _btnBrowse.FlatAppearance.BorderSize = 0;
        _btnBrowse.Click += BtnBrowse_Click;

        _chkDecideAtRuntime = new CheckBox
        {
            Text = "Allow to decide which script at runtime",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 120),
            Size = new Size(360, 20),
            Checked = false
        };

        // Hook enable/disable toggling
        _rbSourceFile.CheckedChanged += (s, e) => TogglePathControls();
        _rbSourceString.CheckedChanged += (s, e) => TogglePathControls();

        gpSource.Controls.Add(_rbSourceFile);
        gpSource.Controls.Add(_rbSourceArg);
        gpSource.Controls.Add(_rbSourceString);
        gpSource.Controls.Add(_lblSourcePath);
        gpSource.Controls.Add(_txtSourcePath);
        gpSource.Controls.Add(_btnBrowse);
        gpSource.Controls.Add(_chkDecideAtRuntime);
        this.Controls.Add(gpSource);

        // Group 3: Engine Options
        var gpEngine = new GroupBox
        {
            Text = "Engine Options",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = WbTheme.Subtext0,
            Location = new Point(20, 330),
            Size = new Size(390, 75),
            FlatStyle = FlatStyle.Flat
        };
        _chkFollowIncludes = new CheckBox
        {
            Text = "Follow #Include directives",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 22),
            Size = new Size(360, 20),
            Checked = FollowIncludes
        };
        _chkInlineIncludes = new CheckBox
        {
            Text = "Inline includes in source editor",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            ForeColor = WbTheme.Text,
            Location = new Point(15, 45),
            Size = new Size(360, 20),
            Checked = InlineIncludes
        };
        gpEngine.Controls.Add(_chkFollowIncludes);
        gpEngine.Controls.Add(_chkInlineIncludes);
        this.Controls.Add(gpEngine);

        // Buttons
        var btnExport = new Button
        {
            Text = "Export",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            BackColor = WbTheme.Accent,
            ForeColor = WbTheme.Base,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(210, 420),
            Size = new Size(95, 32),
            Cursor = Cursors.Hand
        };
        btnExport.FlatAppearance.BorderSize = 0;
        btnExport.Click += BtnExport_Click;

        var btnCancel = new Button
        {
            Text = "Cancel",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(315, 420),
            Size = new Size(95, 32),
            Cursor = Cursors.Hand
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (s, e) => {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        };

        this.Controls.Add(btnExport);
        this.Controls.Add(btnCancel);

        // Set default buttons
        this.AcceptButton = btnExport;
        this.CancelButton = btnCancel;

        // Initialize state
        TogglePathControls();
    }

    private void TogglePathControls()
    {
        bool needsFile = _rbSourceFile.Checked || _rbSourceString.Checked;
        _lblSourcePath.Enabled = needsFile;
        _txtSourcePath.Enabled = needsFile;
        _btnBrowse.Enabled = needsFile;
        _chkDecideAtRuntime.Enabled = needsFile;
        if (!needsFile)
        {
            _chkDecideAtRuntime.Checked = false;
        }
    }

    private void BtnBrowse_Click(object sender, EventArgs e)
    {
        using (var ofd = new OpenFileDialog { Filter = "AutoHotkey Script (*.ahk)|*.ahk|All Files (*.*)|*.*" })
        {
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtSourcePath.Text = ofd.FileName;
            }
        }
    }

    private void BtnExport_Click(object sender, EventArgs e)
    {
        if (_rbStyleRaw.Checked) SelectedStyle = "raw";
        else if (_rbStyleSharp.Checked) SelectedStyle = "ahksharp";
        else SelectedStyle = "com";

        if (_rbSourceFile.Checked) SourceType = "file";
        else if (_rbSourceArg.Checked) SourceType = "argument";
        else SourceType = "string";

        SourceFilePath = _txtSourcePath.Text;
        FollowIncludes = _chkFollowIncludes.Checked;
        InlineIncludes = _chkInlineIncludes.Checked;
        DecideAtRuntime = _chkDecideAtRuntime.Checked;

        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}
