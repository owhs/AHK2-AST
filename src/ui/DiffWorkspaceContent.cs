using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using FastColoredTextBoxNS;

public class DiffWorkspaceContent : DockContent
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    private FastColoredTextBox _leftEditor;
    private FastColoredTextBox _rightEditor;
    private SplitContainer _split;
    private CheckBox _chkShowDiffsOnly;
    private ThemedComboBox _cmbContext;

    private string _oldText = "";
    private string _newText = "";
    private List<string> _leftLineNumbers = new List<string>();
    private List<string> _rightLineNumbers = new List<string>();

    public DiffWorkspaceContent()
    {
        Text = "Diff Review";
        BackColor = WbTheme.Crust;

        var header = new Panel 
        { 
            Dock = DockStyle.Top, 
            Height = 32, 
            BackColor = WbTheme.Mantle, 
            Padding = new Padding(8, 4, 8, 4) 
        };

        _chkShowDiffsOnly = new CheckBox
        {
            Text = "Show Diffs Only",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            Checked = true,
            AutoSize = true,
            Location = new Point(8, 6),
            Font = WbTheme.UISmall
        };
        header.Controls.Add(_chkShowDiffsOnly);

        var lblContext = new Label
        {
            Text = "Context:",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(140, 8),
            Font = WbTheme.UISmall
        };
        header.Controls.Add(lblContext);

        _cmbContext = new ThemedComboBox
        {
            Location = new Point(200, 5),
            Width = 90,
            Font = WbTheme.UISmall
        };
        _cmbContext.Items.AddRange(new object[] { "0 lines", "1 line", "2 lines", "3 lines", "5 lines" });
        _cmbContext.SelectedIndex = 1; // Default to "1 line" to show only diffs tightly
        _cmbContext.SelectedIndexChanged += (s, e) => UpdateDiff();
        header.Controls.Add(_cmbContext);

        _chkShowDiffsOnly.CheckedChanged += (s, e) => 
        {
            _cmbContext.Enabled = _chkShowDiffsOnly.Checked;
            UpdateDiff();
        };

        _split = new SplitContainer 
        { 
            Dock = DockStyle.Fill, 
            Orientation = Orientation.Vertical, 
            SplitterDistance = 400 
        };

        _leftEditor = CreateEditor();
        _rightEditor = CreateEditor();

        // Fix scrollbars
        string scrollbarTheme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";
        _leftEditor.HandleCreated += (s, e) => SetWindowTheme(_leftEditor.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        _rightEditor.HandleCreated += (s, e) => SetWindowTheme(_rightEditor.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        if (_leftEditor.IsHandleCreated)
            SetWindowTheme(_leftEditor.Handle, scrollbarTheme, null);
        if (_rightEditor.IsHandleCreated)
            SetWindowTheme(_rightEditor.Handle, scrollbarTheme, null);

        var leftHeader = new Panel 
        { 
            Dock = DockStyle.Top, 
            Height = 24, 
            BackColor = WbTheme.Mantle, 
            Padding = new Padding(8, 4, 8, 4) 
        };
        var lblLeft = new Label 
        { 
            Text = "ORIGINAL (LEFT)", 
            ForeColor = WbTheme.Red, 
            Font = WbTheme.UIBold, 
            AutoSize = true, 
            Location = new Point(8, 4) 
        };
        leftHeader.Controls.Add(lblLeft);

        var rightHeader = new Panel 
        { 
            Dock = DockStyle.Top, 
            Height = 24, 
            BackColor = WbTheme.Mantle, 
            Padding = new Padding(8, 4, 8, 4) 
        };
        var lblRight = new Label 
        { 
            Text = "MODIFIED (RIGHT)", 
            ForeColor = WbTheme.Green, 
            Font = WbTheme.UIBold, 
            AutoSize = true, 
            Location = new Point(8, 4) 
        };
        rightHeader.Controls.Add(lblRight);

        // Sync scrolling
        bool inScrollSync = false;
        _leftEditor.Scroll += (s, e) => 
        { 
            if (inScrollSync) return;
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll) 
            {
                inScrollSync = true;
                try
                {
                    _rightEditor.VerticalScroll.Value = _leftEditor.VerticalScroll.Value; 
                }
                catch {}
                finally
                {
                    inScrollSync = false;
                }
            }
        };
        _rightEditor.Scroll += (s, e) => 
        { 
            if (inScrollSync) return;
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll) 
            {
                inScrollSync = true;
                try
                {
                    _leftEditor.VerticalScroll.Value = _rightEditor.VerticalScroll.Value; 
                }
                catch {}
                finally
                {
                    inScrollSync = false;
                }
            }
        };

        _split.Panel1.Controls.Add(_leftEditor);
        _split.Panel1.Controls.Add(leftHeader);
        leftHeader.BringToFront();

        _split.Panel2.Controls.Add(_rightEditor);
        _split.Panel2.Controls.Add(rightHeader);
        rightHeader.BringToFront();

        Controls.Add(_split);
        Controls.Add(header);

        _split.SendToBack();
    }

    private FastColoredTextBox CreateEditor()
    {
        bool wrap = false;
        try { wrap = AHK2AST.UI.WorkbenchState.Load().WordWrap; } catch {}
        var tb = new FastColoredTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoFont,
            LineNumberColor = Color.Transparent,
            IndentBackColor = WbTheme.Base,
            ReadOnly = true,
            WordWrap = wrap
        };

        tb.PaintLine += (s, e) =>
        {
            var editor = (FastColoredTextBox)s;
            string numberStr = "";
            if (editor == _leftEditor)
            {
                if (e.LineIndex >= 0 && e.LineIndex < _leftLineNumbers.Count)
                    numberStr = _leftLineNumbers[e.LineIndex];
            }
            else if (editor == _rightEditor)
            {
                if (e.LineIndex >= 0 && e.LineIndex < _rightLineNumbers.Count)
                    numberStr = _rightLineNumbers[e.LineIndex];
            }

            if (!string.IsNullOrEmpty(numberStr))
            {
                using (var brush = new SolidBrush(WbTheme.Overlay0))
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                {
                    // Draw line number right-aligned in the gutter area
                    var rect = new RectangleF(0, e.LineRect.Y, editor.LeftIndent - 8, e.LineRect.Height);
                    e.Graphics.DrawString(numberStr, editor.Font, brush, rect, sf);
                }
            }
        };

        return tb;
    }

    public void SetWordWrap(bool wrap)
    {
        _leftEditor.WordWrap = wrap;
        _rightEditor.WordWrap = wrap;
    }

    public void SetLeftText(string text)
    {
        _oldText = text;
        UpdateDiff();
    }

    public void SetRightText(string text)
    {
        _newText = text;
        UpdateDiff();
    }

    private void UpdateDiff()
    {
        ShowDiffInternal(_oldText, _newText);
    }

    public void ShowDiff(string oldText, string newText)
    {
        _oldText = oldText;
        _newText = newText;
        UpdateDiff();
    }

    private void ShowDiffInternal(string oldText, string newText)
    {
        if (oldText == null) oldText = "";
        if (newText == null) newText = "";

        var builder = new SideBySideDiffBuilder(new Differ());
        var model = builder.BuildDiffModel(oldText, newText);

        var leftLines = new List<string>();
        var rightLines = new List<string>();
        var lineStyles = new List<Tuple<ChangeType, ChangeType>>();

        bool showDiffsOnly = _chkShowDiffsOnly != null && _chkShowDiffsOnly.Checked;
        int contextSize = 1;
        if (_cmbContext != null && _cmbContext.SelectedIndex >= 0)
        {
            int idx = _cmbContext.SelectedIndex;
            if (idx == 0) contextSize = 0;
            else if (idx == 1) contextSize = 1;
            else if (idx == 2) contextSize = 2;
            else if (idx == 3) contextSize = 3;
            else if (idx == 4) contextSize = 5;
        }

        int N = model.OldText.Lines.Count;

        _leftLineNumbers.Clear();
        _rightLineNumbers.Clear();

        if (showDiffsOnly)
        {
            bool[] visible = new bool[N];
            for (int i = 0; i < N; i++)
            {
                var oldLine = model.OldText.Lines[i];
                var newLine = model.NewText.Lines[i];
                bool isChanged = (oldLine.Type == ChangeType.Deleted || oldLine.Type == ChangeType.Modified ||
                                  newLine.Type == ChangeType.Inserted || newLine.Type == ChangeType.Modified);
                if (isChanged)
                {
                    for (int j = Math.Max(0, i - contextSize); j <= Math.Min(N - 1, i + contextSize); j++)
                    {
                        visible[j] = true;
                    }
                }
            }

            int invisibleStart = -1;
            for (int i = 0; i < N; i++)
            {
                if (visible[i])
                {
                    if (invisibleStart != -1)
                    {
                        int count = i - invisibleStart;
                        leftLines.Add(string.Format("... [{0} lines unchanged] ...", count));
                        rightLines.Add(string.Format("... [{0} lines unchanged] ...", count));
                        _leftLineNumbers.Add("");
                        _rightLineNumbers.Add("");
                        lineStyles.Add(Tuple.Create(ChangeType.Imaginary, ChangeType.Imaginary));
                        invisibleStart = -1;
                    }

                    var oldLine = model.OldText.Lines[i];
                    var newLine = model.NewText.Lines[i];
                    leftLines.Add(oldLine.Text ?? "");
                    rightLines.Add(newLine.Text ?? "");
                    _leftLineNumbers.Add(oldLine.Position.HasValue ? oldLine.Position.Value.ToString() : "");
                    _rightLineNumbers.Add(newLine.Position.HasValue ? newLine.Position.Value.ToString() : "");
                    lineStyles.Add(Tuple.Create(oldLine.Type, newLine.Type));
                }
                else
                {
                    if (invisibleStart == -1)
                    {
                        invisibleStart = i;
                    }
                }
            }

            if (invisibleStart != -1)
            {
                int count = N - invisibleStart;
                leftLines.Add(string.Format("... [{0} lines unchanged] ...", count));
                rightLines.Add(string.Format("... [{0} lines unchanged] ...", count));
                _leftLineNumbers.Add("");
                _rightLineNumbers.Add("");
                lineStyles.Add(Tuple.Create(ChangeType.Imaginary, ChangeType.Imaginary));
            }
        }
        else
        {
            for (int i = 0; i < N; i++)
            {
                var oldLine = model.OldText.Lines[i];
                var newLine = model.NewText.Lines[i];
                leftLines.Add(oldLine.Text ?? "");
                rightLines.Add(newLine.Text ?? "");
                _leftLineNumbers.Add(oldLine.Position.HasValue ? oldLine.Position.Value.ToString() : "");
                _rightLineNumbers.Add(newLine.Position.HasValue ? newLine.Position.Value.ToString() : "");
                lineStyles.Add(Tuple.Create(oldLine.Type, newLine.Type));
            }
        }

        _leftEditor.Text = string.Join("\r\n", leftLines);
        _rightEditor.Text = string.Join("\r\n", rightLines);

        _leftEditor.ClearStylesBuffer();
        _rightEditor.ClearStylesBuffer();

        var deletedStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(80, WbTheme.Red))); // Transparent Red
        var insertedStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(80, WbTheme.Green))); // Transparent Green
        var modifiedOldStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(50, WbTheme.Red))); // Slight Red
        var modifiedNewStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(50, WbTheme.Green))); // Slight Green
        var imaginaryStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(30, WbTheme.Overlay0))); // Gray

        for (int i = 0; i < leftLines.Count; i++)
        {
            var styles = lineStyles[i];
            var oldType = styles.Item1;
            var newType = styles.Item2;

            if (oldType == ChangeType.Deleted)
            {
                if (i < _leftEditor.LinesCount)
                    _leftEditor.GetLine(i).SetStyle(deletedStyle);
            }
            else if (oldType == ChangeType.Modified)
            {
                if (i < _leftEditor.LinesCount)
                    _leftEditor.GetLine(i).SetStyle(modifiedOldStyle);
            }
            else if (oldType == ChangeType.Imaginary)
            {
                if (i < _leftEditor.LinesCount)
                    _leftEditor.GetLine(i).SetStyle(imaginaryStyle);
            }

            if (newType == ChangeType.Inserted)
            {
                if (i < _rightEditor.LinesCount)
                    _rightEditor.GetLine(i).SetStyle(insertedStyle);
            }
            else if (newType == ChangeType.Modified)
            {
                if (i < _rightEditor.LinesCount)
                    _rightEditor.GetLine(i).SetStyle(modifiedNewStyle);
            }
            else if (newType == ChangeType.Imaginary)
            {
                if (i < _rightEditor.LinesCount)
                    _rightEditor.GetLine(i).SetStyle(imaginaryStyle);
            }
        }
    }
}
