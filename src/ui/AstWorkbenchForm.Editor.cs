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
    // ── Source Editor ──────────────────────────────────────────────────────

    private void BuildSourcePanel()
    {
        _sourcePanel = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Base };

        // Header
        var header = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = WbTheme.Mantle, Padding = new Padding(8, 6, 8, 4) };
        var headerLabel = new Label { Text = "SOURCE EDITOR", ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(8, 8) };
        header.Controls.Add(headerLabel);

        // Line number gutter
        _lineNumberPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 52,
            BackColor = WbTheme.Mantle,
            Padding = new Padding(0)
        };
        _lineNumberPanel.Paint += LineNumberPanel_Paint;

        // Source editor
        _sourceEditor = new RichTextBox
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
        _sourceEditor.HandleCreated += (s, e) => SetWindowTheme(_sourceEditor.Handle, "DarkMode_Explorer", null);
        _sourceEditor.TextChanged += (s, e) =>
        {
            if (_isHighlighting) return;
            LogDebug("Event: TextChanged");
            _lineNumberPanel.Invalidate();
            _syntaxHighlightTimer.Stop();
            _syntaxHighlightTimer.Start();
        };
        _sourceEditor.VScroll += (s, e) =>
        {
            if (_isHighlighting) return;
            LogDebug("Event: VScroll");
            _lineNumberPanel.Invalidate();
            _sourceEditorScrollTimer.Stop();
            _sourceEditorScrollTimer.Start();
        };
        _sourceEditor.Resize += (s, e) =>
        {
            if (_isHighlighting) return;
            _sourceEditorScrollTimer.Stop();
            _sourceEditorScrollTimer.Start();
        };
        _sourceEditor.SelectionChanged += (s, e) =>
        {
            if (_isHighlighting) return;
            LogDebug("Event: SelectionChanged - Start: " + _sourceEditor.SelectionStart + ", Length: " + _sourceEditor.SelectionLength);
            UpdateCursorPosition();
        };

        _sourcePanel.Controls.Add(_sourceEditor);
        AttachEditorContextMenu(_sourceEditor);
        _sourcePanel.Controls.Add(_lineNumberPanel);
        _sourcePanel.Controls.Add(header);
    }

    private void LineNumberPanel_Paint(object sender, PaintEventArgs e)
    {
        LogDebug("Event: LineNumberPanel_Paint");
        if (_sourceEditor == null || !_sourceEditor.IsHandleCreated) return;

        e.Graphics.Clear(WbTheme.Mantle);

        const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        int firstLine = SendMessageInt(_sourceEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        int totalLines = _sourceEditor.Lines.Length;
        if (totalLines == 0) return;

        int firstChar = _sourceEditor.GetFirstCharIndexFromLine(firstLine);
        if (firstChar < 0) return;

        Point pos = _sourceEditor.GetPositionFromCharIndex(firstChar);
        int y = pos.Y;

        int lineHeight = _sourceEditor.Font.Height;
        int nextChar = _sourceEditor.GetFirstCharIndexFromLine(firstLine + 1);
        if (nextChar >= 0)
        {
            Point nextPos = _sourceEditor.GetPositionFromCharIndex(nextChar);
            if (nextPos.Y > pos.Y)
                lineHeight = nextPos.Y - pos.Y;
        }

        using (var brush = new SolidBrush(WbTheme.Overlay0))
        using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near })
        {
            for (int i = firstLine; i < totalLines; i++)
            {
                int lineStartChar = _sourceEditor.GetFirstCharIndexFromLine(i);
                if (lineStartChar < 0) break;
                Point linePos = _sourceEditor.GetPositionFromCharIndex(lineStartChar);
                if (linePos.Y > _lineNumberPanel.Height) break;

                e.Graphics.DrawString(
                    (i + 1).ToString(),
                    WbTheme.MonoSmall,
                    brush,
                    new RectangleF(0, linePos.Y, _lineNumberPanel.Width - 8, lineHeight),
                    sf);
            }
        }
    }

    private void UpdateCursorPosition()
    {
        int idx = _sourceEditor.SelectionStart;
        int line = _sourceEditor.GetLineFromCharIndex(idx);
        int firstChar = _sourceEditor.GetFirstCharIndexFromLine(line);
        int col = idx - firstChar;
        _statusPos.Text = string.Format("Ln {0}, Col {1}", line + 1, col + 1);
    }

}
