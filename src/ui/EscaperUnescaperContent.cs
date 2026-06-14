using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using FastColoredTextBoxNS;

public class EscaperUnescaperContent : DockContent
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    private FastColoredTextBox _escapedEditor;
    private FastColoredTextBox _unescapedEditor;
    private SplitContainer _split;

    private ThemedComboBox _cmbQuoteStyle;
    private ThemedComboBox _cmbSyncMode;
    private CheckBox _chkUseChr;
    private CheckBox _chkUseChrAllCtrl;
    private CheckBox _chkVariablePlaceholders;
    private CheckBox _chkWordWrap;
    private Label _lblDetectStatus;
    private Label _lblWarning;

    private Label _lblLeft;
    private Label _lblRight;

    private string _lastDetectedQuoteStyle = "Single Quotes";
    private bool _isUpdating = false;
    private bool _initialSplitSet = false;

    public EscaperUnescaperContent()
    {
        Text = "Escaper && Unescaper";
        BackColor = WbTheme.Crust;

        // --- Top Options Header Panel ---
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(8, 6, 8, 6)
        };

        // Draw thin accent bottom border on header
        header.Paint += (s, e) =>
        {
            using (var pen = new Pen(WbTheme.Accent, 1f))
            {
                e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            }
        };

        var lblMode = new Label
        {
            Text = "Quote Mode:",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(8, 10),
            Font = WbTheme.UISmall
        };
        header.Controls.Add(lblMode);

        _cmbQuoteStyle = new ThemedComboBox
        {
            Location = new Point(85, 7),
            Width = 100,
            Font = WbTheme.UISmall
        };
        _cmbQuoteStyle.Items.AddRange(new object[] { "Smart Detect", "Single Quotes", "Double Quotes", "Raw (No Quotes)" });
        _cmbQuoteStyle.SelectedIndex = 0; // Smart Detect
        _cmbQuoteStyle.SelectedIndexChanged += (s, e) => TriggerSyncFromEscaped();
        header.Controls.Add(_cmbQuoteStyle);

        var lblSyncMode = new Label
        {
            Text = "Sync Mode:",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(195, 10),
            Font = WbTheme.UISmall
        };
        header.Controls.Add(lblSyncMode);

        _cmbSyncMode = new ThemedComboBox
        {
            Location = new Point(270, 7),
            Width = 150,
            Font = WbTheme.UISmall
        };
        _cmbSyncMode.Items.AddRange(new object[] { "Bidirectional (Auto)", "Escape (Left -> Right)", "Unescape (Right -> Left)" });
        _cmbSyncMode.SelectedIndex = 0; // Bidirectional (Auto)
        _cmbSyncMode.SelectedIndexChanged += (s, e) => ApplySyncModeChange();
        header.Controls.Add(_cmbSyncMode);

        _chkUseChr = new CheckBox
        {
            Text = "Escape Chr(X)",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            Checked = false,
            AutoSize = true,
            Location = new Point(435, 9),
            Font = WbTheme.UISmall
        };
        _chkUseChr.CheckedChanged += (s, e) => TriggerSyncFromUnescaped();
        header.Controls.Add(_chkUseChr);

        _chkUseChrAllCtrl = new CheckBox
        {
            Text = "Chr(X) Control Chars",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            Checked = false,
            AutoSize = true,
            Location = new Point(545, 9),
            Font = WbTheme.UISmall
        };
        _chkUseChrAllCtrl.CheckedChanged += (s, e) => TriggerSyncFromUnescaped();
        header.Controls.Add(_chkUseChrAllCtrl);

        _chkVariablePlaceholders = new CheckBox
        {
            Text = "Support {var} Placeholders",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            Checked = true,
            AutoSize = true,
            Location = new Point(680, 9),
            Font = WbTheme.UISmall
        };
        _chkVariablePlaceholders.CheckedChanged += (s, e) => TriggerSyncFromEscaped();
        header.Controls.Add(_chkVariablePlaceholders);

        bool wordWrapEnabled = false;
        try
        {
            wordWrapEnabled = AHK2AST.UI.WorkbenchState.Load().WordWrap;
        }
        catch { }

        _chkWordWrap = new CheckBox
        {
            Text = "Word Wrap",
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent,
            Checked = wordWrapEnabled,
            AutoSize = true,
            Location = new Point(850, 9),
            Font = WbTheme.UISmall
        };
        _chkWordWrap.CheckedChanged += (s, e) =>
        {
            _unescapedEditor.WordWrap = _chkWordWrap.Checked;
            _escapedEditor.WordWrap = _chkWordWrap.Checked;
            try
            {
                var state = AHK2AST.UI.WorkbenchState.Load();
                state.WordWrap = _chkWordWrap.Checked;
                state.Save();
            }
            catch { }
        };
        header.Controls.Add(_chkWordWrap);

        _lblDetectStatus = new Label
        {
            Text = "Detected: Smart",
            ForeColor = WbTheme.Green,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(960, 10),
            Font = WbTheme.UIBold,
            Visible = false
        };
        header.Controls.Add(_lblDetectStatus);

        _lblWarning = new Label
        {
            Text = "",
            ForeColor = WbTheme.Yellow,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(1100, 10),
            Font = WbTheme.UISmall,
            Visible = false
        };
        header.Controls.Add(_lblWarning);

        // Adjust warning layout dynamically on resize
        header.Resize += (s, e) =>
        {
            _lblWarning.Location = new Point(Math.Max(1100, header.Width - _lblWarning.Width - 16), 10);
        };

        // --- Side-by-Side Editors Panel ---
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 450
        };

        // Dynamic 50/50 split constraint on first layout
        _split.Resize += (s, e) =>
        {
            if (!_initialSplitSet && _split.Width > 0)
            {
                _split.SplitterDistance = _split.Width / 2;
                _initialSplitSet = true;
            }
        };

        // Paint centered vertical splitter line
        _split.Paint += (s, e) =>
        {
            using (var pen = new Pen(WbTheme.Surface1, 1f))
            {
                int x = _split.SplitterDistance + (_split.SplitterWidth / 2);
                e.Graphics.DrawLine(pen, x, 0, x, _split.Height);
            }
        };

        // --- LEFT CONTAINER: Unescaped Plain Text (Raw) ---
        var leftContainer = new Panel { Dock = DockStyle.Fill };
        var leftHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(8, 4, 8, 4)
        };
        _lblLeft = new Label
        {
            Text = "UNESCAPED PLAIN TEXT",
            ForeColor = WbTheme.Teal,
            Font = WbTheme.UIBold,
            AutoSize = true,
            Location = new Point(8, 6)
        };
        var btnCopyLeft = new Button
        {
            Text = "Copy",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Size = new Size(50, 20),
            Location = new Point(390, 4)
        };
        btnCopyLeft.FlatAppearance.BorderSize = 0;
        btnCopyLeft.Click += (s, e) => { try { Clipboard.SetText(_unescapedEditor.Text); } catch { } };

        // Premium Hover Interactions
        btnCopyLeft.MouseEnter += (s, e) => { btnCopyLeft.BackColor = WbTheme.Selection; btnCopyLeft.ForeColor = WbTheme.Accent; };
        btnCopyLeft.MouseLeave += (s, e) => { btnCopyLeft.BackColor = WbTheme.Surface0; btnCopyLeft.ForeColor = WbTheme.Text; };

        leftHeader.Controls.Add(_lblLeft);
        leftHeader.Controls.Add(btnCopyLeft);

        leftHeader.Resize += (s, e) =>
        {
            btnCopyLeft.Location = new Point(leftHeader.Width - btnCopyLeft.Width - 8, 4);
        };

        _unescapedEditor = CreateEditor();
        _unescapedEditor.TextChanged += UnescapedEditor_TextChanged;

        leftContainer.Controls.Clear();
        leftContainer.Controls.Add(_unescapedEditor);
        leftContainer.Controls.Add(leftHeader);
        leftHeader.SendToBack();
        _unescapedEditor.BringToFront();

        // --- RIGHT CONTAINER: Escaped AHK String Literal ---
        var rightContainer = new Panel { Dock = DockStyle.Fill };
        var rightHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(8, 4, 8, 4)
        };
        _lblRight = new Label
        {
            Text = "ESCAPED AHK STRING LITERAL",
            ForeColor = WbTheme.Lavender,
            Font = WbTheme.UIBold,
            AutoSize = true,
            Location = new Point(8, 6)
        };
        var btnCopyRight = new Button
        {
            Text = "Copy",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Size = new Size(50, 20),
            Location = new Point(390, 4)
        };
        btnCopyRight.FlatAppearance.BorderSize = 0;
        btnCopyRight.Click += (s, e) => { try { Clipboard.SetText(_escapedEditor.Text); } catch { } };

        // Premium Hover Interactions
        btnCopyRight.MouseEnter += (s, e) => { btnCopyRight.BackColor = WbTheme.Selection; btnCopyRight.ForeColor = WbTheme.Accent; };
        btnCopyRight.MouseLeave += (s, e) => { btnCopyRight.BackColor = WbTheme.Surface0; btnCopyRight.ForeColor = WbTheme.Text; };

        rightHeader.Controls.Add(_lblRight);
        rightHeader.Controls.Add(btnCopyRight);

        rightHeader.Resize += (s, e) =>
        {
            btnCopyRight.Location = new Point(rightHeader.Width - btnCopyRight.Width - 8, 4);
        };

        _escapedEditor = CreateEditor();
        _escapedEditor.TextChanged += EscapedEditor_TextChanged;

        rightContainer.Controls.Clear();
        rightContainer.Controls.Add(_escapedEditor);
        rightContainer.Controls.Add(rightHeader);
        rightHeader.SendToBack();
        _escapedEditor.BringToFront();

        _split.Panel1.Controls.Add(leftContainer);
        _split.Panel2.Controls.Add(rightContainer);

        // --- Docking Layout Order ---
        Controls.Clear();
        Controls.Add(_split);
        Controls.Add(header);
        header.SendToBack();
        _split.BringToFront();

        // --- Scroll Sync Logic ---
        bool inScrollSync = false;
        _escapedEditor.Scroll += (s, e) =>
        {
            if (inScrollSync) return;
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                inScrollSync = true;
                try
                {
                    _unescapedEditor.VerticalScroll.Value = _escapedEditor.VerticalScroll.Value;
                    _unescapedEditor.UpdateScrollbars();
                }
                catch { }
                finally
                {
                    inScrollSync = false;
                }
            }
        };
        _unescapedEditor.Scroll += (s, e) =>
        {
            if (inScrollSync) return;
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                inScrollSync = true;
                try
                {
                    _escapedEditor.VerticalScroll.Value = _unescapedEditor.VerticalScroll.Value;
                    _escapedEditor.UpdateScrollbars();
                }
                catch { }
                finally
                {
                    inScrollSync = false;
                }
            }
        };

        // Fix scrollbars
        string scrollbarTheme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";
        _escapedEditor.HandleCreated += (s, e) => SetWindowTheme(_escapedEditor.Handle, scrollbarTheme, null);
        _unescapedEditor.HandleCreated += (s, e) => SetWindowTheme(_unescapedEditor.Handle, scrollbarTheme, null);
        if (_escapedEditor.IsHandleCreated) SetWindowTheme(_escapedEditor.Handle, scrollbarTheme, null);
        if (_unescapedEditor.IsHandleCreated) SetWindowTheme(_unescapedEditor.Handle, scrollbarTheme, null);
    }

    private FastColoredTextBox CreateEditor()
    {
        bool wrap = false;
        try { wrap = AHK2AST.UI.WorkbenchState.Load().WordWrap; } catch { }
        var tb = new FastColoredTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoFont,
            LineNumberColor = WbTheme.Overlay0,
            IndentBackColor = WbTheme.Mantle,
            ReadOnly = false,
            ShowLineNumbers = true,
            AutoScroll = true,
            WordWrap = wrap
        };
        return tb;
    }

    public void SetWordWrap(bool wrap)
    {
        if (_chkWordWrap != null && _chkWordWrap.Checked != wrap)
        {
            _chkWordWrap.Checked = wrap;
        }
        _unescapedEditor.WordWrap = wrap;
        _escapedEditor.WordWrap = wrap;
    }

    private void ApplySyncModeChange()
    {
        string mode = _cmbSyncMode.SelectedItem != null ? _cmbSyncMode.SelectedItem.ToString() : "Bidirectional (Auto)";
        if (mode.Contains("Escape (Left -> Right)"))
        {
            _unescapedEditor.ReadOnly = false;
            _escapedEditor.ReadOnly = true;
            _lblLeft.Text = "UNESCAPED PLAIN TEXT (INPUT)";
            _lblRight.Text = "ESCAPED AHK STRING LITERAL (OUTPUT)";
            TriggerSyncFromUnescaped();
        }
        else if (mode.Contains("Unescape (Right -> Left)"))
        {
            _unescapedEditor.ReadOnly = true;
            _escapedEditor.ReadOnly = false;
            _lblLeft.Text = "UNESCAPED PLAIN TEXT (OUTPUT)";
            _lblRight.Text = "ESCAPED AHK STRING LITERAL (INPUT)";
            TriggerSyncFromEscaped();
        }
        else // Bidirectional (Auto)
        {
            _unescapedEditor.ReadOnly = false;
            _escapedEditor.ReadOnly = false;
            _lblLeft.Text = "UNESCAPED PLAIN TEXT";
            _lblRight.Text = "ESCAPED AHK STRING LITERAL";
        }
    }

    public void SetEscapedText(string text)
    {
        _escapedEditor.Text = text;
        // Automatically switch to Unescape mode if programmatically sent as Escaped (Right is input)
        _cmbSyncMode.SelectedIndex = 2; // Unescape (Right -> Left)
        TriggerSyncFromEscaped();
    }

    public void SetUnescapedText(string text)
    {
        _unescapedEditor.Text = text;
        // Automatically switch to Escape mode if programmatically sent as Unescaped (Left is input)
        _cmbSyncMode.SelectedIndex = 1; // Escape (Left -> Right)
        TriggerSyncFromUnescaped();
    }

    private void EscapedEditor_TextChanged(object sender, EventArgs e)
    {
        if (_isUpdating) return;
        string mode = _cmbSyncMode.SelectedItem != null ? _cmbSyncMode.SelectedItem.ToString() : "Bidirectional (Auto)";
        if (mode.Contains("Escape (Left -> Right)")) return; // escaped editor is output-only in Escape mode

        // Only sync if escaped editor has focus or both editors are unfocused (programmatic)
        if (_escapedEditor.ContainsFocus || (!_escapedEditor.ContainsFocus && !_unescapedEditor.ContainsFocus))
        {
            TriggerSyncFromEscaped();
        }
    }

    private void UnescapedEditor_TextChanged(object sender, EventArgs e)
    {
        if (_isUpdating) return;
        string mode = _cmbSyncMode.SelectedItem != null ? _cmbSyncMode.SelectedItem.ToString() : "Bidirectional (Auto)";
        if (mode.Contains("Unescape (Right -> Left)")) return; // unescaped editor is output-only in Unescape mode

        // Only sync if unescaped editor has focus or both editors are unfocused (programmatic)
        if (_unescapedEditor.ContainsFocus || (!_escapedEditor.ContainsFocus && !_unescapedEditor.ContainsFocus))
        {
            TriggerSyncFromUnescaped();
        }
    }

    private void TriggerSyncFromEscaped()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            string escapedText = _escapedEditor.Text;
            string quoteStyle = _cmbQuoteStyle.SelectedItem != null ? _cmbQuoteStyle.SelectedItem.ToString() : "Smart Detect";
            bool supportPlaceholders = _chkVariablePlaceholders.Checked;

            string warning = null;
            string unescaped = ParseAndUnescape(escapedText, quoteStyle, supportPlaceholders, out warning);

            if (_unescapedEditor.Text != unescaped)
            {
                _unescapedEditor.Text = unescaped;
            }

            if (!string.IsNullOrEmpty(warning))
            {
                _lblWarning.Text = warning;
                _lblWarning.Visible = true;
                _lblWarning.ForeColor = WbTheme.Yellow;
            }
            else
            {
                _lblWarning.Visible = false;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void TriggerSyncFromUnescaped()
    {
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            string unescapedText = _unescapedEditor.Text;
            string quoteStyle = _cmbQuoteStyle.SelectedItem != null ? _cmbQuoteStyle.SelectedItem.ToString() : "Smart Detect";
            if (quoteStyle == "Smart Detect")
            {
                quoteStyle = _lastDetectedQuoteStyle;
            }
            bool useChrQuotes = _chkUseChr.Checked;
            bool useChrAllCtrl = _chkUseChrAllCtrl.Checked;
            bool supportPlaceholders = _chkVariablePlaceholders.Checked;

            string escaped = Escape(unescapedText, quoteStyle, useChrQuotes, useChrAllCtrl, supportPlaceholders);

            if (_escapedEditor.Text != escaped)
            {
                _escapedEditor.Text = escaped;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    // --- Parser & Unescapers ---

    private string ParseAndUnescape(string text, string quoteMode, bool supportPlaceholders, out string warning)
    {
        warning = null;
        if (string.IsNullOrEmpty(text)) return "";

        string trimmed = text.Trim();
        char quoteChar = '\0';

        // Smart detect wrapper quote style
        if (quoteMode == "Smart Detect")
        {
            if (trimmed.StartsWith("'"))
            {
                _lastDetectedQuoteStyle = "Single Quotes";
                _lblDetectStatus.Text = "Detected: Single Quotes";
                _lblDetectStatus.Visible = true;
                quoteChar = '\'';
            }
            else if (trimmed.StartsWith("\""))
            {
                _lastDetectedQuoteStyle = "Double Quotes";
                _lblDetectStatus.Text = "Detected: Double Quotes";
                _lblDetectStatus.Visible = true;
                quoteChar = '"';
            }
            else
            {
                _lastDetectedQuoteStyle = "Raw (No Quotes)";
                _lblDetectStatus.Text = "Detected: Raw String";
                _lblDetectStatus.Visible = true;
                quoteChar = '\0';
            }
        }
        else
        {
            _lblDetectStatus.Visible = false;
            if (quoteMode == "Single Quotes") quoteChar = '\'';
            else if (quoteMode == "Double Quotes") quoteChar = '"';
        }

        // Tokenize text
        var tokens = TokenizeExpression(text, quoteMode);

        bool hasVars = tokens.Any(t => t.Type == "Variable");
        if (hasVars && !supportPlaceholders)
        {
            warning = "Warning: Detected potential AHK variable concatenation, but placeholders are disabled.";
        }

        // Evaluate tokens
        StringBuilder sb = new StringBuilder();
        foreach (var t in tokens)
        {
            if (t.Type == "Literal" || t.Type == "Chr")
            {
                sb.Append(t.EvaluatedValue);
            }
            else if (t.Type == "Variable")
            {
                if (supportPlaceholders)
                {
                    sb.Append("{" + t.EvaluatedValue + "}");
                }
                else
                {
                    sb.Append(t.EvaluatedValue);
                }
            }
            else if (t.Type == "Operator")
            {
                // Skip dots
            }
            else
            {
                sb.Append(t.RawText);
            }
        }

        return sb.ToString();
    }

    private List<AhkStringPart> TokenizeExpression(string text, string quoteMode)
    {
        string trimmed = text.Trim();
        bool isExpression = false;

        if (quoteMode == "Single Quotes" || quoteMode == "Double Quotes")
        {
            isExpression = true;
        }
        else if (quoteMode == "Smart Detect")
        {
            if (trimmed.StartsWith("'") || trimmed.StartsWith("\""))
            {
                isExpression = true;
            }
        }

        if (!isExpression)
        {
            var parts = new List<AhkStringPart>();
            string val = UnescapeLiteralContent(text, '\0');
            parts.Add(new AhkStringPart { Type = "Literal", RawText = text, EvaluatedValue = val });
            return parts;
        }

        return TokenizeExpressionInternal(text);
    }

    private List<AhkStringPart> TokenizeExpressionInternal(string text)
    {
        var parts = new List<AhkStringPart>();
        int i = 0;
        int len = text.Length;

        while (i < len)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '.')
            {
                parts.Add(new AhkStringPart { Type = "Operator", RawText = "." });
                i++;
                continue;
            }

            // Single quote literal
            if (text[i] == '\'')
            {
                int start = i;
                i++; // skip quote
                while (i < len)
                {
                    if (text[i] == '`' && i + 1 < len)
                    {
                        i += 2;
                    }
                    else if (text[i] == '\'' && i + 1 < len && text[i + 1] == '\'')
                    {
                        i += 2;
                    }
                    else if (text[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
                string raw = text.Substring(start, i - start);
                string val = UnescapeLiteralContent(raw, '\'');
                parts.Add(new AhkStringPart { Type = "Literal", RawText = raw, EvaluatedValue = val });
                continue;
            }

            // Double quote literal
            if (text[i] == '"')
            {
                int start = i;
                i++; // skip quote
                while (i < len)
                {
                    if (text[i] == '`' && i + 1 < len)
                    {
                        i += 2;
                    }
                    else if (text[i] == '"' && i + 1 < len && text[i + 1] == '"')
                    {
                        i += 2;
                    }
                    else if (text[i] == '"')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
                string raw = text.Substring(start, i - start);
                string val = UnescapeLiteralContent(raw, '"');
                parts.Add(new AhkStringPart { Type = "Literal", RawText = raw, EvaluatedValue = val });
                continue;
            }

            // Chr(...) or Char(...) function call
            if (i + 4 < len && (text.Substring(i, 4).Equals("Chr(", StringComparison.OrdinalIgnoreCase) ||
                                (i + 5 < len && text.Substring(i, 5).Equals("Char(", StringComparison.OrdinalIgnoreCase))))
            {
                bool isChar = text.Substring(i, 4).Equals("Char", StringComparison.OrdinalIgnoreCase);
                int start = i;
                i += isChar ? 5 : 4;
                int parenCount = 1;
                StringBuilder argSb = new StringBuilder();
                while (i < len && parenCount > 0)
                {
                    char c = text[i];
                    if (c == ')')
                    {
                        parenCount--;
                        if (parenCount == 0)
                        {
                            i++;
                            break;
                        }
                    }
                    else if (c == '(')
                    {
                        parenCount++;
                    }
                    argSb.Append(c);
                    i++;
                }
                string raw = text.Substring(start, i - start);
                string arg = argSb.ToString().Trim();
                string val = "";
                try
                {
                    int code = arg.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? Convert.ToInt32(arg, 16)
                        : Convert.ToInt32(arg);
                    val = char.ConvertFromUtf32(code);
                }
                catch
                {
                    val = "?";
                }
                parts.Add(new AhkStringPart { Type = "Chr", RawText = raw, EvaluatedValue = val });
                continue;
            }

            // Variable
            if (char.IsLetter(text[i]) || text[i] == '_' || text[i] == '#' || text[i] == '@')
            {
                int start = i;
                i++;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
                string raw = text.Substring(start, i - start);
                parts.Add(new AhkStringPart { Type = "Variable", RawText = raw, EvaluatedValue = raw });
                continue;
            }

            // Fallback for unknown / other symbols
            parts.Add(new AhkStringPart { Type = "Unknown", RawText = text[i].ToString(), EvaluatedValue = text[i].ToString() });
            i++;
        }

        return parts;
    }

    private static string UnescapeLiteralContent(string literal, char quoteChar)
    {
        string content = literal;
        if (quoteChar != '\0' && literal.Length >= 2 && literal.StartsWith(quoteChar.ToString()) && literal.EndsWith(quoteChar.ToString()))
        {
            content = literal.Substring(1, literal.Length - 2);
        }

        StringBuilder sb = new StringBuilder();
        int i = 0;
        int len = content.Length;
        while (i < len)
        {
            char c = content[i];
            if (c == '`')
            {
                if (i + 1 < len)
                {
                    char next = content[i + 1];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); i += 2; break;
                        case 'r': sb.Append('\r'); i += 2; break;
                        case 't': sb.Append('\t'); i += 2; break;
                        case 'b': sb.Append('\b'); i += 2; break;
                        case 'a': sb.Append('\a'); i += 2; break;
                        case 'f': sb.Append('\f'); i += 2; break;
                        case 'v': sb.Append('\v'); i += 2; break;
                        case '`': sb.Append('`'); i += 2; break;
                        case '\'':
                            if (quoteChar == '\'') sb.Append('\'');
                            else sb.Append("`'");
                            i += 2;
                            break;
                        case '"':
                            if (quoteChar == '"') sb.Append('"');
                            else sb.Append("`\"");
                            i += 2;
                            break;
                        default:
                            sb.Append('`');
                            sb.Append(next);
                            i += 2;
                            break;
                    }
                }
                else
                {
                    sb.Append('`');
                    i++;
                }
            }
            else if (quoteChar == '\'' && c == '\'' && i + 1 < len && content[i + 1] == '\'')
            {
                sb.Append('\'');
                i += 2;
            }
            else if (quoteChar == '"' && c == '"' && i + 1 < len && content[i + 1] == '"')
            {
                sb.Append('"');
                i += 2;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    // --- Escapers ---

    public static string Escape(string rawText, string quoteMode, bool useChrQuotes, bool useChrAllCtrl, bool supportPlaceholders)
    {
        if (rawText == null) rawText = "";

        char quoteChar = '\0';
        if (quoteMode == "Single Quotes" || quoteMode == "Smart Detect") quoteChar = '\'';
        else if (quoteMode == "Double Quotes") quoteChar = '"';

        if (supportPlaceholders)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"\{([a-zA-Z_#@$][a-zA-Z0-9_#@$]*)\}");
            var matches = regex.Matches(rawText);

            if (matches.Count > 0)
            {
                var parts = new List<string>();
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    int literalLen = match.Index - lastIndex;
                    if (literalLen > 0)
                    {
                        string lit = rawText.Substring(lastIndex, literalLen);
                        parts.Add(EscapePart(lit, quoteChar, useChrQuotes, useChrAllCtrl));
                    }
                    parts.Add(match.Groups[1].Value);
                    lastIndex = match.Index + match.Length;
                }
                if (lastIndex < rawText.Length)
                {
                    string lit = rawText.Substring(lastIndex);
                    parts.Add(EscapePart(lit, quoteChar, useChrQuotes, useChrAllCtrl));
                }

                // Filter out empty quotes
                StringBuilder sb = new StringBuilder();
                bool first = true;
                foreach (var part in parts)
                {
                    bool isVar = true;
                    if (quoteChar != '\0')
                    {
                        if (part.StartsWith(quoteChar.ToString()) && part.EndsWith(quoteChar.ToString()))
                        {
                            isVar = false;
                            if (part.Length == 2 && parts.Count > 1)
                            {
                                continue; // skip empty quote like '' or ""
                            }
                        }
                    }
                    else
                    {
                        isVar = false;
                    }

                    if (!first)
                    {
                        sb.Append(" . ");
                    }
                    sb.Append(part);
                    first = false;
                }
                return sb.ToString();
            }
        }

        return EscapePart(rawText, quoteChar, useChrQuotes, useChrAllCtrl);
    }

    private static string EscapePart(string text, char quoteChar, bool useChrQuotes, bool useChrAllCtrl)
    {
        StringBuilder sb = new StringBuilder();
        if (quoteChar != '\0')
        {
            sb.Append(quoteChar);
        }

        int len = text.Length;
        for (int i = 0; i < len; i++)
        {
            char c = text[i];
            if (c == '`')
            {
                sb.Append("``");
            }
            else if (c == '\n')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(10)") : "`n");
            }
            else if (c == '\r')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(13)") : "`r");
            }
            else if (c == '\t')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(9)") : "`t");
            }
            else if (c == '\b')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(8)") : "`b");
            }
            else if (c == '\a')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(7)") : "`a");
            }
            else if (c == '\f')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(12)") : "`f");
            }
            else if (c == '\v')
            {
                sb.Append(useChrAllCtrl ? CloseConcatOpen(quoteChar, "Chr(11)") : "`v");
            }
            else if (quoteChar != '\0' && c == quoteChar)
            {
                if (useChrQuotes)
                {
                    string chrFunc = (quoteChar == '\'') ? "Chr(39)" : "Chr(34)";
                    sb.Append(CloseConcatOpen(quoteChar, chrFunc));
                }
                else
                {
                    sb.Append('`');
                    sb.Append(quoteChar);
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (quoteChar != '\0')
        {
            sb.Append(quoteChar);
        }

        string result = sb.ToString();
        if (quoteChar != '\0')
        {
            string emptyStart = string.Format("{0}{0} . ", quoteChar);
            if (result.StartsWith(emptyStart))
            {
                result = result.Substring(emptyStart.Length);
            }
            string emptyEnd = string.Format(" . {0}{0}", quoteChar);
            if (result.EndsWith(emptyEnd))
            {
                result = result.Substring(0, result.Length - emptyEnd.Length);
            }
        }
        return result;
    }

    private static string CloseConcatOpen(char quoteChar, string expr)
    {
        if (quoteChar == '\0') return expr;
        return string.Format("{0} . {1} . {0}", quoteChar, expr);
    }

    public class AhkStringPart
    {
        public string Type { get; set; } // "Literal", "Chr", "Variable", "Operator", "Unknown"
        public string RawText { get; set; }
        public string EvaluatedValue { get; set; }
    }
}
