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
    // ── Emit Helpers ──────────────────────────────────────────────────────

    private void CopyEmitToClipboard()
    {
        if (string.IsNullOrEmpty(_lastEmittedCode))
        {
            _statusLabel.Text = "Nothing to copy — emit code first";
            _statusLabel.ForeColor = WbTheme.Yellow;
            return;
        }
        try
        {
            Clipboard.SetText(_lastEmittedCode);
            _statusLabel.Text = "✅ Emitted code copied to clipboard (" + _lastEmittedCode.Split('\n').Length + " lines)";
            _statusLabel.ForeColor = WbTheme.Green;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Copy failed: " + ex.Message;
            _statusLabel.ForeColor = WbTheme.Red;
        }
    }

    private void ExportEmitToFile()
    {
        if (string.IsNullOrEmpty(_lastEmittedCode))
        {
            _statusLabel.Text = "Nothing to export — emit code first";
            _statusLabel.ForeColor = WbTheme.Yellow;
            return;
        }
        using (var dlg = new SaveFileDialog())
        {
            dlg.Filter = "AHK Scripts (*.ahk)|*.ahk|All Files (*.*)|*.*";
            dlg.Title = "Export emitted code";
            if (!string.IsNullOrEmpty(_currentFile))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(_currentFile);
                dlg.FileName = Path.GetFileNameWithoutExtension(_currentFile) + "_emitted.ahk";
            }
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(dlg.FileName, _lastEmittedCode, Encoding.UTF8);
                _statusLabel.Text = "✅ Exported to " + dlg.FileName;
                _statusLabel.ForeColor = WbTheme.Green;
            }
        }
    }

    private void InlineEmitInSource()
    {
        if (string.IsNullOrEmpty(_lastEmittedCode))
        {
            _statusLabel.Text = "Nothing to inline — emit code first";
            _statusLabel.ForeColor = WbTheme.Yellow;
            return;
        }
        _sourceEditor.Text = _lastEmittedCode;
        _statusLabel.Text = "⤵ Source editor updated with emitted code";
        _statusLabel.ForeColor = WbTheme.Sky;
    }

}
