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
    // ── Utility ───────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    private const int EM_SETCUEBANNER = 0x1501;

    private static void SetCueText(TextBox textBox, string cueText)
    {
        // Defer until handle is created — calling Handle on orphaned controls
        // can trigger path resolution errors when assembly is byte-loaded
        if (textBox.IsHandleCreated)
        {
            SendMessage(textBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, cueText);
        }
        else
        {
            textBox.HandleCreated += (s, e) =>
            {
                SendMessage(textBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, cueText);
            };
        }
    }

    private void AppendLog(RichTextBox rtb, string text, Color color)
    {
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionLength = 0;
        rtb.SelectionColor = color;
        rtb.AppendText(text + "\n");
    }

    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_LINESCROLL = 0x00B6;

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageInt(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETREDRAW = 0x0B;

    private static readonly Regex _highlightRegex = new Regex(
        @"(?<comment>(?:^|(?<=\s));.*|(?s)/\*.*?\*/)" +
        @"|(?<string>""(?:""""|[^""])*""|'(?:''|[^'])*')" +
        @"|(?<directive>\b#(?:Include|Requires|NoTrayIcon|SingleInstance|UseHook|InputLevel|HotIf|ErrorStdOut)\b)" +
        @"|(?<keyword>\b(?:if|else|loop|while|for|try|catch|finally|throw|return|break|continue|global|local|static|class|extends|in|is|as|new|and|or|not)\b)" +
        @"|(?<builtin>\bA_[a-zA-Z0-9_]+\b)" +
        @"|(?<number>\b0x[0-9a-fA-F]+\b|\b\d+(?:\.\d+)?\b)" +
        @"|(?<function>\b[a-zA-Z0-9_]+(?=\())",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private void HighlightControl(RichTextBox rtb)
    {
        LogDebug("Call: HighlightControl (" + (rtb == _sourceEditor ? "sourceEditor" : "emitView") + ")");
        if (rtb == null || !rtb.IsHandleCreated) return;

        string text = rtb.Text;
        if (string.IsNullOrEmpty(text)) return;

        _isHighlighting = true; // Block event firing during highlighting

        int selStart = rtb.SelectionStart;
        int selLen = rtb.SelectionLength;

        // Save scroll position
        int firstVisible = SendMessageInt(rtb.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (firstVisible < 0) firstVisible = 0;

        string[] lines = rtb.Lines;

        int lineHeight = rtb.Font.Height;
        int visibleLinesCount = rtb.Height / (lineHeight > 0 ? lineHeight : 15) + 10; // add a margin of 10 lines
        int lastLine = Math.Min(lines.Length - 1, firstVisible + visibleLinesCount);

        int startChar = rtb.GetFirstCharIndexFromLine(firstVisible);
        if (startChar < 0) startChar = 0;

        int endChar = rtb.TextLength;
        if (lastLine < lines.Length - 1)
        {
            int nextLineChar = rtb.GetFirstCharIndexFromLine(lastLine + 1);
            if (nextLineChar >= 0)
                endChar = nextLineChar;
        }

        // Freeze painting to prevent flickering
        SendMessageInt(rtb.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

        var sw = Stopwatch.StartNew();

        try
        {
            // Reset color of only the visible range to WbTheme.Text
            rtb.Select(startChar, endChar - startChar);
            rtb.SelectionColor = WbTheme.Text;

            // Match only within the visible substring
            string visibleText = text.Substring(startChar, endChar - startChar);
            var matches = _highlightRegex.Matches(visibleText);
            foreach (Match m in matches)
            {
                int fullIndex = m.Index + startChar;

                Color color = WbTheme.Text;
                if (m.Groups["comment"].Success) color = WbTheme.Overlay0;
                else if (m.Groups["string"].Success) color = WbTheme.Peach;
                else if (m.Groups["directive"].Success) color = WbTheme.Sky;
                else if (m.Groups["keyword"].Success) color = WbTheme.Mauve;
                else if (m.Groups["builtin"].Success) color = WbTheme.Lavender;
                else if (m.Groups["number"].Success) color = WbTheme.Yellow;
                else if (m.Groups["function"].Success) color = WbTheme.Blue;

                if (color != WbTheme.Text)
                {
                    rtb.Select(fullIndex, m.Length);
                    rtb.SelectionColor = color;
                }
            }
        }
        catch { }
        finally
        {
            rtb.Select(selStart, selLen);

            // Restore scroll position
            int newFirstVisible = SendMessageInt(rtb.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            int delta = firstVisible - newFirstVisible;
            if (delta != 0)
            {
                SendMessageInt(rtb.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));
            }

            // Unfreeze painting
            SendMessageInt(rtb.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            rtb.Invalidate();

            _isHighlighting = false; // Release event blocking

            sw.Stop();
            LogDebug("Finish: HighlightControl in " + sw.ElapsedMilliseconds + "ms");
        }
     }

    private static void EnableDoubleBuffering(Control control)
    {
        if (control == null) return;
        try
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (prop != null)
            {
                prop.SetValue(control, true, null);
            }
        }
        catch { }
    }

    private void LogDebug(string msg)
    {
        if (!_debugMode || _debugLog == null || _debugLog.IsDisposed) return;
        if (_debugLog.InvokeRequired)
        {
            try { _debugLog.BeginInvoke((Action)(() => LogDebug(msg))); } catch { }
            return;
        }

        string time = DateTime.Now.ToString("HH:mm:ss.fff");
        _debugLog.AppendText(string.Format("[{0}] {1}\n", time, msg));

        if (_debugLog.TextLength > 50000)
        {
            _debugLog.Clear();
        }
        _debugLog.SelectionStart = _debugLog.TextLength;
        _debugLog.ScrollToCaret();
    }

}
