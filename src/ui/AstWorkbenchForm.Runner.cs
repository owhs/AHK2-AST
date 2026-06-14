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
    // ── Test Runner ───────────────────────────────────────────────────────

    private void TestRun()
    {
        var activeDoc = _dockPanel.ActiveDocument;
        if (activeDoc == _sourceContent)
        {
            // if on source editor, and do f5/run... emit it, and run the emitted code
            string oldText = _emitSourceEditor.Text;
            _emitSourceEditor.Text = "";
            EmitFromAst();
            if (!string.IsNullOrEmpty(_emitSourceEditor.Text))
            {
                RunAhkScript(_emitSourceEditor.Text);
            }
            else
            {
                _emitSourceEditor.Text = oldText;
            }
        }
        else if (activeDoc is PipelineBuilderContent)
        {
            // if i am in a flow editor and do run, run it and run the emitted code
            var pb = (PipelineBuilderContent)activeDoc;
            bool hasDiagram = false;
            foreach (Control c in pb.VisualInspector.Controls)
            {
                if (c is StepCardControl && ((StepCardControl)c).Title == "Logic Flow Diagram")
                {
                    hasDiagram = true;
                    break;
                }
            }
            pb.ExecuteFlow(!hasDiagram);
        }
        else if (activeDoc == _emitSourceContent)
        {
            // if i am in an emitted code and run, just run it
            RunAhkScript(_emitSourceEditor.Text);
        }
        else
        {
            // Default fallback: run the main source editor
            RunAhkScript(_sourceEditor.Text);
        }
    }

    private void RunAhkScript(string source)
    {
        StopRun(); // Kill any existing process

        if (source == null)
        {
            _statusLabel.Text = "Nothing to run";
            return;
        }

        string trimmed = source.TrimStart();
        string firstLine = GetCleanFirstNonCommentLine(source);
        bool isHtml = firstLine.StartsWith("<html", StringComparison.OrdinalIgnoreCase) || 
                      firstLine.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                      firstLine.StartsWith("<div", StringComparison.OrdinalIgnoreCase) ||
                      firstLine.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
        
        bool isMd = firstLine.StartsWith("# ") || 
                    firstLine.StartsWith("## ") || 
                    firstLine.StartsWith("### ") ||
                    firstLine.StartsWith("```") ||
                    trimmed.Contains("```mermaid") ||
                    trimmed.Contains("```\n") ||
                    trimmed.Contains("```\r\n");

        bool isJson = (firstLine.StartsWith("[") || firstLine.StartsWith("{")) && 
                      (firstLine.Contains("\":") || firstLine.Contains("\" :") || source.Contains("\":") || source.Contains("\" :"));

        if (isHtml)
        {
            _statusLabel.Text = "HTML report detected. Opening in browser...";
            _statusLabel.ForeColor = WbTheme.Sky;
            try
            {
                string tempHtml = Path.Combine(Path.GetTempPath(), "ahk2ast_report_" + DateTime.Now.Ticks + ".html");
                File.WriteAllText(tempHtml, source, Encoding.UTF8);
                Process.Start(new ProcessStartInfo { FileName = tempHtml, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Failed to open HTML: " + ex.Message;
                _statusLabel.ForeColor = WbTheme.Red;
            }
            return;
        }

        if (isMd)
        {
            _statusLabel.Text = "Markdown report detected.";
            _statusLabel.ForeColor = WbTheme.Sky;
            return;
        }

        if (isJson)
        {
            _statusLabel.Text = "JSON data detected.";
            _statusLabel.ForeColor = WbTheme.Sky;
            return;
        }

        bool hasNim = AHK2AST.Plugins.PluginRegistry.RegisteredPluginTypes.Any(t => t.Name == "NimTranspilerPlugin" || t.FullName == "AHK2AST.Plugins.NimTranspilerPlugin");

        // Check if the source code is transpiled Nim code
        if (hasNim && (source.Contains("import AhkStdLib") || source.StartsWith("# --- DllCall Bindings ---") || source.StartsWith("import std/")))
        {
            _statusLabel.Text = "Transpiled Nim detected. Directing to Nim Build Manager...";
            _statusLabel.ForeColor = WbTheme.Sky;
            
            OpenNimBuildManager();
            
            foreach (var doc in _dockPanel.Documents)
            {
                var buildDoc = doc as NimBuildContent;
                if (buildDoc != null)
                {
                    buildDoc.TriggerBuildFromTranspiler(source);
                    return;
                }
            }
            return;
        }

        // Determine AHK path
        string ahkPath = FindAhkPath();
        if (string.IsNullOrEmpty(ahkPath))
        {
            _statusLabel.Text = "[X] AutoHotkey.exe not found -- set path via Run menu";
            _statusLabel.ForeColor = WbTheme.Red;
            return;
        }

        // Determine temp script path
        string scriptPath;
        if (!string.IsNullOrEmpty(_tempPathBox.Text))
        {
            scriptPath = _tempPathBox.Text;
        }
        else if (!string.IsNullOrEmpty(_currentFile))
        {
            // Use a temp file alongside the current file
            scriptPath = Path.Combine(
                Path.GetDirectoryName(_currentFile),
                "__ast_workbench_temp.ahk");
        }
        else
        {
            scriptPath = Path.Combine(Path.GetTempPath(), "__ast_workbench_temp.ahk");
        }

        // Determine working directory
        string workDir = _runDirBox.Text;
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            workDir = !string.IsNullOrEmpty(_currentFile)
                ? Path.GetDirectoryName(_currentFile)
                : Path.GetDirectoryName(scriptPath);
        }

        // Write temp script
        File.WriteAllText(scriptPath, source, Encoding.UTF8);

        // Switch to Run Output tab
        SelectTab(2);
        _runOutput.Clear();
        AppendLog(_runOutput, "═══ TEST RUN ═══", WbTheme.Lavender);
        AppendLog(_runOutput, "  AHK:    " + ahkPath, WbTheme.Subtext0);
        AppendLog(_runOutput, "  Script: " + scriptPath, WbTheme.Subtext0);
        AppendLog(_runOutput, "  CWD:    " + workDir, WbTheme.Subtext0);
        AppendLog(_runOutput, "────────────────────────────────────────", WbTheme.Overlay0);
        AppendLog(_runOutput, "", WbTheme.Text);

        var sw = Stopwatch.StartNew();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ahkPath,
                Arguments = "\"" + scriptPath + "\"",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _runProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _runProcess.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    BeginInvoke((Action)(() => AppendLog(_runOutput, "  " + e.Data, WbTheme.Text)));
            };

            _runProcess.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    BeginInvoke((Action)(() => AppendLog(_runOutput, "  ⚠ " + e.Data, WbTheme.Red)));
            };

            _runProcess.Exited += (s, e) =>
            {
                sw.Stop();
                int exitCode = -1;
                try { exitCode = _runProcess.ExitCode; } catch { }

                BeginInvoke((Action)(() =>
                {
                    AppendLog(_runOutput, "", WbTheme.Text);
                    AppendLog(_runOutput, "────────────────────────────────────────", WbTheme.Overlay0);

                    if (exitCode == 0)
                    {
                        AppendLog(_runOutput, string.Format("  OK Exited ({0}) after {1:F1}s", exitCode, sw.ElapsedMilliseconds / 1000.0), WbTheme.Green);
                        _statusLabel.Text = string.Format("OK Script exited ({0}) after {1:F1}s", exitCode, sw.ElapsedMilliseconds / 1000.0);
                        _statusLabel.ForeColor = WbTheme.Green;
                    }
                    else
                    {
                        AppendLog(_runOutput, string.Format("  FAIL Exited ({0}) after {1:F1}s", exitCode, sw.ElapsedMilliseconds / 1000.0), WbTheme.Red);
                        _statusLabel.Text = string.Format("FAIL Script exited ({0}) after {1:F1}s", exitCode, sw.ElapsedMilliseconds / 1000.0);
                        _statusLabel.ForeColor = WbTheme.Red;
                    }

                    // Clean up temp file (only if it was auto-generated)
                    if (string.IsNullOrEmpty(_tempPathBox.Text))
                    {
                        try { File.Delete(scriptPath); } catch { }
                    }

                    // Auto load trace
                    AutoLoadTraceAfterRun(workDir);
                }));
            };

            _runProcess.Start();
            _runProcess.BeginOutputReadLine();
            _runProcess.BeginErrorReadLine();

            _statusLabel.Text = "Running...";
            _statusLabel.ForeColor = WbTheme.Sky;
        }
        catch (Exception ex)
        {
            AppendLog(_runOutput, "  [X] Failed to launch: " + ex.Message, WbTheme.Red);
            _statusLabel.Text = "Run failed: " + ex.Message;
            _statusLabel.ForeColor = WbTheme.Red;
        }
    }

    private void StopRun()
    {
        if (_runProcess != null && !_runProcess.HasExited)
        {
            try
            {
                _runProcess.Kill();
                AppendLog(_runOutput, "  [X] Process killed by user", WbTheme.Yellow);
                _statusLabel.Text = "Process stopped";
                _statusLabel.ForeColor = WbTheme.Yellow;
            }
            catch { }
        }
        _runProcess = null;
    }

    private string FindAhkPath()
    {
        // 1. User-configured path
        if (_ahkPathBox != null && !string.IsNullOrEmpty(_ahkPathBox.Text) && File.Exists(_ahkPathBox.Text))
            return _ahkPathBox.Text;

        // 2. Common install locations
        string[] candidates = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AutoHotkey", "v2", "AutoHotkey.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AutoHotkey", "v2", "AutoHotkey64.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AutoHotkey", "v2", "AutoHotkey32.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AutoHotkey", "AutoHotkey.exe"),
            @"C:\Program Files\AutoHotkey\v2\AutoHotkey.exe",
            @"C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe",
            @"C:\Program Files\AutoHotkey\AutoHotkey.exe"
        };

        foreach (string path in candidates)
            if (File.Exists(path)) return path;

        // 3. PATH lookup
        string envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in envPath.Split(';'))
        {
            string full = Path.Combine(dir.Trim(), "AutoHotkey.exe");
            if (File.Exists(full)) return full;
            full = Path.Combine(dir.Trim(), "AutoHotkey64.exe");
            if (File.Exists(full)) return full;
        }

        return null;
    }

    // ── Config Dialogs ────────────────────────────────────────────────────

    private void SetWorkingDir()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Select working directory for test runs";
            if (!string.IsNullOrEmpty(_runDirBox.Text))
                dlg.SelectedPath = _runDirBox.Text;
            if (dlg.ShowDialog() == DialogResult.OK)
                _runDirBox.Text = dlg.SelectedPath;
        }
    }

    private void SetAhkPath()
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Filter = "AutoHotkey (*.exe)|*.exe";
            dlg.Title = "Select AutoHotkey.exe";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                if (_ahkPathBox == null)
                    _ahkPathBox = new TextBox();
                _ahkPathBox.Text = dlg.FileName;
                _statusLabel.Text = "AHK path set: " + dlg.FileName;
            }
        }
    }

    private void SetTempPath()
    {
        using (var dlg = new SaveFileDialog())
        {
            dlg.Filter = "AHK Scripts (*.ahk)|*.ahk";
            dlg.Title = "Set temp script location";
            if (dlg.ShowDialog() == DialogResult.OK)
                _tempPathBox.Text = dlg.FileName;
        }
    }

    private static string GetCleanFirstNonCommentLine(string source)
    {
        if (string.IsNullOrEmpty(source)) return "";
        using (var reader = new System.IO.StringReader(source))
        {
            string line;
            bool inBlockComment = false;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (inBlockComment)
                {
                    if (trimmed.EndsWith("*/"))
                    {
                        inBlockComment = false;
                    }
                    continue;
                }

                if (trimmed.StartsWith("/*"))
                {
                    if (!trimmed.EndsWith("*/"))
                    {
                        inBlockComment = true;
                    }
                    continue;
                }

                if (trimmed.StartsWith(";"))
                {
                    continue;
                }

                return trimmed;
            }
        }
        return "";
    }

}
