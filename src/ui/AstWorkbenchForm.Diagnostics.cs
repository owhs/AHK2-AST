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
    // ── Error Collection & Display ────────────────────────────────────────

    private List<AstNode> CollectWarnings(AstNode root)
    {
        var warnings = new List<AstNode>();
        CollectWarningsRecursive(root, warnings);
        return warnings;
    }

    private void CollectWarningsRecursive(AstNode node, List<AstNode> results)
    {
        if (node.NodeType == "Warning") results.Add(node);
        foreach (var child in node.ChildNodes)
            CollectWarningsRecursive(child, results);
    }

    private void PopulateErrorGrid(AstNode[] errors, List<AstNode> warnings)
    {
        _errorGrid.SuspendLayout();
        _errorGrid.Rows.Clear();

        int totalItems = (errors != null ? errors.Length : 0) + warnings.Count;
        int maxRows = 500;
        int rowCount = 0;

        if (errors != null)
        {
            foreach (var err in errors)
            {
                if (rowCount >= maxRows) break;
                string context = GetSourceContext(err.Line);
                _errorGrid.Rows.Add("●", err.Line, err.Column, "E001", err.Value, context);
                _errorGrid.Rows[_errorGrid.Rows.Count - 1].DefaultCellStyle.ForeColor = WbTheme.Red;
                rowCount++;
            }
        }

        foreach (var warn in warnings)
        {
            if (rowCount >= maxRows) break;
            string context = GetSourceContext(warn.Line);
            _errorGrid.Rows.Add("▲", warn.Line, warn.Column, "W001", warn.Value, context);
            _errorGrid.Rows[_errorGrid.Rows.Count - 1].DefaultCellStyle.ForeColor = WbTheme.Yellow;
            rowCount++;
        }

        if (totalItems > maxRows)
        {
            _errorGrid.Rows.Add("…", 0, 0, "", string.Format("{0} more items not shown (see Parse Log for full details)", totalItems - maxRows), "");
            _errorGrid.Rows[_errorGrid.Rows.Count - 1].DefaultCellStyle.ForeColor = WbTheme.Overlay0;
        }

        _errorGrid.ResumeLayout();
    }

    private string GetSourceContext(int line)
    {
        if (line < 1 || _sourceEditor.Lines.Length == 0) return "";
        int idx = Math.Min(line - 1, _sourceEditor.Lines.Length - 1);
        return _sourceEditor.Lines[idx].Trim();
    }

    private void ErrorGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _errorGrid.Rows[e.RowIndex];
        int line = 0;
        object lineVal = row.Cells["Line"].Value;
        if (lineVal != null) int.TryParse(lineVal.ToString(), out line);
        if (line > 0) HighlightSourceLine(line, 1);

        // Show detailed error popup
        object sevVal = row.Cells["Sev"].Value;
        object msgVal = row.Cells["Message"].Value;
        object ctxVal = row.Cells["Context"].Value;
        string sev = sevVal != null ? sevVal.ToString() : "";
        string msg = msgVal != null ? msgVal.ToString() : "";
        string ctx = ctxVal != null ? ctxVal.ToString() : "";
        string detail = BuildDetailedError(sev, line, msg, ctx);

        using (var popup = new Form())
        {
            popup.Text = "Error Detail — Line " + line;
            popup.Size = new Size(700, 400);
            popup.StartPosition = FormStartPosition.CenterParent;
            popup.BackColor = WbTheme.Base;
            popup.ForeColor = WbTheme.Text;
            popup.Font = WbTheme.UIFont;

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = WbTheme.Base,
                ForeColor = WbTheme.Text,
                Font = WbTheme.MonoFont,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                Text = detail,
                WordWrap = true
            };
            popup.Controls.Add(rtb);
            popup.ShowDialog(this);
        }
    }

    private void ErrorGrid_SelectionChanged(object sender, EventArgs e)
    {
        if (_errorGrid.SelectedRows.Count == 0) return;
        var row = _errorGrid.SelectedRows[0];
        int line = 0;
        object lineVal = row.Cells["Line"].Value;
        if (lineVal != null) int.TryParse(lineVal.ToString(), out line);
        if (line > 0) HighlightSourceLine(line, 1);
    }

    private string BuildDetailedError(string severity, int line, string message, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(severity == "●" ? "══ ERROR ══" : "══ WARNING ══");
        sb.AppendLine();
        sb.AppendLine("Message: " + message);
        sb.AppendLine(string.Format("Location: Line {0}", line));
        sb.AppendLine();

        // Source context (3 lines before/after)
        sb.AppendLine("── Source Context ──────────────────────────────────────");
        string[] lines = _sourceEditor.Lines;
        int start = Math.Max(0, line - 4);
        int end = Math.Min(lines.Length - 1, line + 2);

        for (int i = start; i <= end; i++)
        {
            string prefix = (i == line - 1) ? " ►  " : "    ";
            sb.AppendLine(string.Format("{0}{1,4} │ {2}", prefix, i + 1, i < lines.Length ? lines[i] : ""));
        }

        // Column pointer
        if (line > 0 && line <= lines.Length)
        {
            string theLine = lines[line - 1];
            int firstNonSpace = 0;
            while (firstNonSpace < theLine.Length && theLine[firstNonSpace] == ' ') firstNonSpace++;
            sb.AppendLine("         │ " + new string(' ', firstNonSpace) + "^^^");
        }

        sb.AppendLine("────────────────────────────────────────────────────────");
        return sb.ToString();
    }

    // ── Parse Log ─────────────────────────────────────────────────────────

    private void WriteParseLog(string source, long ms, int nodes, int depth, int errors, int warnings,
        AstNode[] errorNodes, List<AstNode> warnNodes)
    {
        _parseLog.Clear();

        // Header
        AppendLog(_parseLog, "═══════════════════════════════════════════════════════════", WbTheme.Overlay0);
        AppendLog(_parseLog, "  AHK# AST WORKBENCH — PARSE REPORT", WbTheme.Lavender);
        AppendLog(_parseLog, string.Format("  {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), WbTheme.Overlay0);
        AppendLog(_parseLog, "═══════════════════════════════════════════════════════════", WbTheme.Overlay0);
        AppendLog(_parseLog, "", WbTheme.Text);

        // File info
        if (!string.IsNullOrEmpty(_currentFile))
            AppendLog(_parseLog, "  File: " + _currentFile, WbTheme.Subtext1);

        // Stats
        AppendLog(_parseLog, string.Format("  Time:    {0}ms", ms), WbTheme.Subtext1);
        AppendLog(_parseLog, string.Format("  Source:  {0} lines, {1} chars", source.Split('\n').Length, source.Length), WbTheme.Subtext1);
        AppendLog(_parseLog, string.Format("  AST:     {0} nodes, max depth {1}", nodes, depth), WbTheme.Subtext1);
        AppendLog(_parseLog, "", WbTheme.Text);

        // Result summary
        if (errors == 0 && warnings == 0)
        {
            AppendLog(_parseLog, "  [OK] RESULT: Clean parse -- no errors or warnings", WbTheme.Green);
        }
        else
        {
            AppendLog(_parseLog, string.Format("  [!!] RESULT: {0} error(s), {1} warning(s)", errors, warnings),
                errors > 0 ? WbTheme.Red : WbTheme.Yellow);
        }

        AppendLog(_parseLog, "", WbTheme.Text);
        AppendLog(_parseLog, "───────────────────────────────────────────────────────────", WbTheme.Overlay0);

        // Detailed errors (limit to 50 to prevent UI freeze)
        if (errorNodes != null && errorNodes.Length > 0)
        {
            AppendLog(_parseLog, "", WbTheme.Text);
            AppendLog(_parseLog, "  ERRORS", WbTheme.Red);
            AppendLog(_parseLog, "  ──────", WbTheme.Red);

            int maxErrors = Math.Min(errorNodes.Length, 50);
            for (int i = 0; i < maxErrors; i++)
            {
                var err = errorNodes[i];
                AppendLog(_parseLog, "", WbTheme.Text);
                AppendLog(_parseLog, string.Format("  ● [{0}/{1}] Line {2}:{3}", i + 1, errorNodes.Length, err.Line, err.Column), WbTheme.Red);
                AppendLog(_parseLog, "     " + err.Value, WbTheme.Flamingo);

                // Source context
                WriteSourceContext(err.Line);
                AppendLog(_parseLog, "", WbTheme.Text);
            }
            if (errorNodes.Length > 50)
            {
                AppendLog(_parseLog, "", WbTheme.Text);
                AppendLog(_parseLog, string.Format("  ... {0} more errors omitted (see Error Summary below)", errorNodes.Length - 50), WbTheme.Overlay0);
            }
        }

        // Detailed warnings (limit to 50)
        if (warnNodes.Count > 0)
        {
            AppendLog(_parseLog, "", WbTheme.Text);
            AppendLog(_parseLog, "  WARNINGS", WbTheme.Yellow);
            AppendLog(_parseLog, "  ────────", WbTheme.Yellow);

            int maxWarns = Math.Min(warnNodes.Count, 50);
            for (int i = 0; i < maxWarns; i++)
            {
                var warn = warnNodes[i];
                AppendLog(_parseLog, "", WbTheme.Text);
                AppendLog(_parseLog, string.Format("  ▲ [{0}/{1}] Line {2}:{3}", i + 1, warnNodes.Count, warn.Line, warn.Column), WbTheme.Yellow);
                AppendLog(_parseLog, "     " + warn.Value, WbTheme.Rosewater);

                WriteSourceContext(warn.Line);
                AppendLog(_parseLog, "", WbTheme.Text);
            }
            if (warnNodes.Count > 50)
            {
                AppendLog(_parseLog, "", WbTheme.Text);
                AppendLog(_parseLog, string.Format("  ... {0} more warnings omitted (see Error Summary below)", warnNodes.Count - 50), WbTheme.Overlay0);
            }
        }

        // ── Error Summary ──────────────────────────────────────────────────
        if ((errorNodes != null && errorNodes.Length > 0) || warnNodes.Count > 0)
        {
            AppendLog(_parseLog, "", WbTheme.Text);
            AppendLog(_parseLog, "───────────────────────────────────────────────────────────", WbTheme.Overlay0);
            AppendLog(_parseLog, "", WbTheme.Text);
            AppendLog(_parseLog, "  ERROR SUMMARY — Root Cause Analysis", WbTheme.Lavender);
            AppendLog(_parseLog, "  ─────────────────────────────────────", WbTheme.Lavender);
            AppendLog(_parseLog, "", WbTheme.Text);

            // Group errors by normalized pattern
            var errorGroups = new Dictionary<string, List<AstNode>>();
            if (errorNodes != null)
            {
                foreach (var err in errorNodes)
                {
                    // Normalize: strip line/col-specific info, keep token type pattern
                    string key = NormalizeErrorPattern(err.Value);
                    if (!errorGroups.ContainsKey(key))
                        errorGroups[key] = new List<AstNode>();
                    errorGroups[key].Add(err);
                }
            }

            var warnGroups = new Dictionary<string, List<AstNode>>();
            foreach (var w in warnNodes)
            {
                string key = NormalizeErrorPattern(w.Value);
                if (!warnGroups.ContainsKey(key))
                    warnGroups[key] = new List<AstNode>();
                warnGroups[key].Add(w);
            }

            // Sort by count descending
            var sortedErrors = new List<KeyValuePair<string, List<AstNode>>>(errorGroups);
            sortedErrors.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            var sortedWarns = new List<KeyValuePair<string, List<AstNode>>>(warnGroups);
            sortedWarns.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));

            if (sortedErrors.Count > 0)
            {
                AppendLog(_parseLog, string.Format("  {0} unique error patterns ({1} total):", sortedErrors.Count, errors), WbTheme.Red);
                AppendLog(_parseLog, "", WbTheme.Text);

                int rank = 1;
                foreach (var grp in sortedErrors)
                {
                    var first = grp.Value[0];
                    string pct = errors > 0 ? string.Format("{0:F0}%", (grp.Value.Count * 100.0 / errors)) : "0%";
                    AppendLog(_parseLog, string.Format("    #{0}  [{1}x] ({2})  {3}",
                        rank, grp.Value.Count, pct, grp.Key), WbTheme.Red);

                    // Show first few line occurrences
                    int showLines = Math.Min(5, grp.Value.Count);
                    var lineNums = new List<string>();
                    for (int i = 0; i < showLines; i++)
                        lineNums.Add(grp.Value[i].Line + ":" + grp.Value[i].Column);
                    string extra = grp.Value.Count > 5 ? string.Format(" ... +{0} more", grp.Value.Count - 5) : "";
                    AppendLog(_parseLog, string.Format("         Lines: {0}{1}",
                        string.Join(", ", lineNums.ToArray()), extra), WbTheme.Subtext0);

                    // Show first occurrence context
                    if (first.Line > 0 && first.Line <= _sourceEditor.Lines.Length)
                    {
                        string ctx = _sourceEditor.Lines[Math.Min(first.Line - 1, _sourceEditor.Lines.Length - 1)].Trim();
                        if (ctx.Length > 80) ctx = ctx.Substring(0, 77) + "...";
                        AppendLog(_parseLog, "         First: " + ctx, WbTheme.Subtext0);
                    }
                    AppendLog(_parseLog, "", WbTheme.Text);
                    rank++;
                    if (rank > 20) { AppendLog(_parseLog, string.Format("    ... {0} more patterns omitted", sortedErrors.Count - 20), WbTheme.Overlay0); break; }
                }
            }

            if (sortedWarns.Count > 0)
            {
                AppendLog(_parseLog, string.Format("  {0} unique warning patterns ({1} total):", sortedWarns.Count, warnings), WbTheme.Yellow);
                AppendLog(_parseLog, "", WbTheme.Text);

                int rank = 1;
                foreach (var grp in sortedWarns)
                {
                    var first = grp.Value[0];
                    string pct = warnings > 0 ? string.Format("{0:F0}%", (grp.Value.Count * 100.0 / warnings)) : "0%";
                    AppendLog(_parseLog, string.Format("    #{0}  [{1}x] ({2})  {3}",
                        rank, grp.Value.Count, pct, grp.Key), WbTheme.Yellow);

                    int showLines = Math.Min(5, grp.Value.Count);
                    var lineNums = new List<string>();
                    for (int i = 0; i < showLines; i++)
                    {
                        string ln = grp.Value[i].Line > 0 ? grp.Value[i].Line + ":" + grp.Value[i].Column : "(internal)";
                        lineNums.Add(ln);
                    }
                    string extra = grp.Value.Count > 5 ? string.Format(" ... +{0} more", grp.Value.Count - 5) : "";
                    AppendLog(_parseLog, string.Format("         Lines: {0}{1}",
                        string.Join(", ", lineNums.ToArray()), extra), WbTheme.Subtext0);
                    AppendLog(_parseLog, "", WbTheme.Text);
                    rank++;
                    if (rank > 20) { AppendLog(_parseLog, string.Format("    ... {0} more patterns omitted", sortedWarns.Count - 20), WbTheme.Overlay0); break; }
                }
            }
        }

        AppendLog(_parseLog, "", WbTheme.Text);
        AppendLog(_parseLog, "═══════════════════════════════════════════════════════════", WbTheme.Overlay0);
        AppendLog(_parseLog, "  End of report", WbTheme.Overlay0);
        AppendLog(_parseLog, "═══════════════════════════════════════════════════════════", WbTheme.Overlay0);
    }

    private string NormalizeErrorPattern(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "(empty)";
        // Strip specific token values: 'xxx' -> '<token>'
        string normalized = System.Text.RegularExpressions.Regex.Replace(msg, "'[^']*'", "'…'");
        // Strip line:col references
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"line \d+:\d+", "line N:N");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"at line \d+:\d+", "at line N:N");
        return normalized.Trim();
    }

    private void WriteSourceContext(int line)
    {
        if (line < 1) return;
        string[] lines = _sourceEditor.Lines;
        int start = Math.Max(0, line - 4);
        int end = Math.Min(lines.Length - 1, line + 2);

        AppendLog(_parseLog, "     ┌──────────────────────────────────────────", WbTheme.Surface2);

        for (int i = start; i <= end; i++)
        {
            bool isErrLine = (i == line - 1);
            string prefix = isErrLine ? "  ►  " : "     ";
            string lineText = i < lines.Length ? lines[i] : "";
            Color color = isErrLine ? WbTheme.Red : WbTheme.Subtext0;

            AppendLog(_parseLog, string.Format("{0}│ {1,4} │ {2}", prefix, i + 1, lineText), color);
        }

        // Column pointer under error line
        if (line > 0 && line <= lines.Length)
        {
            string theLine = lines[line - 1];
            int firstNonSpace = 0;
            while (firstNonSpace < theLine.Length && char.IsWhiteSpace(theLine[firstNonSpace])) firstNonSpace++;
            string pointer = new string(' ', firstNonSpace) + "^^^";
            AppendLog(_parseLog, "     │      │ " + pointer, WbTheme.Red);
        }

        AppendLog(_parseLog, "     └──────────────────────────────────────────", WbTheme.Surface2);
    }

    private EmitOptions BuildEmitOptions()
    {
        return new EmitOptions
        {
            EmitComments = _emitComments,
            EmitBlankLines = _emitBlankLines,
            PreserveIndent = false,
            UseTabs = _useTabs,
            IndentSize = _indentSize,
            PreserveIncludes = !_inlineIncludes
        };
    }

    private void EmitFromAst()
    {
        if (_currentAst == null)
        {
            ParseCurrent();
            if (_currentAst == null) return;
        }

        try
        {
            var opts = BuildEmitOptions();
            string emitted = _engine.Emit(_currentAst, opts);
            _lastEmittedCode = emitted;
            _emitView.Clear();

            // Header with options summary
            string optsSummary = string.Format("Comments: {0} | Blank Lines: {1} | Indent: {2}",
                opts.EmitComments ? "ON" : "OFF", opts.EmitBlankLines ? "ON" : "OFF",
                opts.UseTabs ? "Tabs" : opts.IndentSize + " spaces");
            AppendLog(_emitView, "═══ REGENERATED SOURCE FROM AST ═══", WbTheme.Lavender);
            AppendLog(_emitView, string.Format("Generated at {0}  |  {1}", DateTime.Now.ToString("HH:mm:ss"), optsSummary), WbTheme.Overlay0);
            AppendLog(_emitView, "", WbTheme.Text);

            // Show emitted code with line numbers in the separate Editor!
            ShowEmitSourceTab(emitted);

            AppendLog(_emitView, "", WbTheme.Text);
            AppendLog(_emitView, string.Format("═══ {0} lines emitted ═══", emitted.Split('\n').Length), WbTheme.Lavender);

            HighlightControl(_emitView);

            SelectTab(3); // Switch to Emit tab
            _statusLabel.Text = "✅ Code emitted from AST — " + emitted.Split('\n').Length + " lines";
            _statusLabel.ForeColor = WbTheme.Green;
        }
        catch (Exception ex)
        {
            AppendLog(_emitView, "EMIT ERROR: " + ex.Message, WbTheme.Red);
            _statusLabel.Text = "Emit failed: " + ex.Message;
            _statusLabel.ForeColor = WbTheme.Red;
        }
    }


}
