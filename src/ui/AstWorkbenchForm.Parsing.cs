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
    // ── Parsing ───────────────────────────────────────────────────────────

    private bool _isParsing;

    private void ParseCurrent()
    {
        if (_dockPanel.ActiveDocument == _emitSourceContent)
        {
            ParseEmittedCode();
            return;
        }

        if (_isParsing) return;

        string source = _sourceEditor.Text;
        if (string.IsNullOrEmpty(source))
        {
            _statusLabel.Text = "Nothing to parse";
            return;
        }

        string astTopNodePath = (_astTree != null && _astTree.TopNode != null) ? _astTree.TopNode.FullPath : null;
        string astSelectedNodePath = (_astTree != null && _astTree.SelectedNode != null) ? _astTree.SelectedNode.FullPath : null;
        int parseLogScroll = (_parseLog != null && _parseLog.IsHandleCreated) ? SendMessageInt(_parseLog.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
        int emitViewScroll = (_emitView != null && _emitView.IsHandleCreated) ? SendMessageInt(_emitView.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
        int debugLogScroll = (_debugLog != null && _debugLog.IsHandleCreated) ? SendMessageInt(_debugLog.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;

        _isParsing = true;
        _statusLabel.Text = "⏳ Parsing...";
        _statusLabel.ForeColor = WbTheme.Sky;
        _parseLog.Clear();
        _errorGrid.Rows.Clear();

        string currentFile = _currentFile;
        bool followIncludes = _followIncludes;
        int lineCount = source.Split('\n').Length;

        // Auto-detect indentation style from source
        DetectIndentStyle(source);

        var sw = Stopwatch.StartNew();

        // Progress timer — updates UI every 200ms while parsing
        string[] spinner = new string[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        int spinIdx = 0;
        var progressTimer = new System.Windows.Forms.Timer { Interval = 200 };
        progressTimer.Tick += (s, e) =>
        {
            string spin = spinner[spinIdx++ % spinner.Length];
            double elapsed = sw.Elapsed.TotalSeconds;
            _statusLabel.Text = string.Format("{0} Parsing {1:N0} lines... ({2:F1}s elapsed)",
                spin, lineCount, elapsed);
        };
        progressTimer.Start();

        // Parse on background thread
        var worker = new BackgroundWorker();
        worker.DoWork += (s, e) =>
        {
            var engine = new AhkAstEngine();
            AstNode ast;
            if (followIncludes && !string.IsNullOrEmpty(currentFile))
                ast = engine.ParseFileWithIncludes(currentFile, true);
            else
                ast = engine.Parse(source);

            // Collect diagnostics on background thread (no UI access)
            var errors = engine.GetErrors(ast);
            var warnings = CollectWarnings(ast);
            int nodeCount = 0;
            int depth = 0;
            CountNodes(ast, 0, ref nodeCount, ref depth);

            e.Result = new object[] { engine, ast, errors, warnings, nodeCount, depth };
        };

        worker.RunWorkerCompleted += (s, e) =>
        {
            progressTimer.Stop();
            progressTimer.Dispose();
            sw.Stop();
            _isParsing = false;

            if (e.Error != null)
            {
                _statusLabel.Text = "Parse failed: " + e.Error.Message;
                _statusLabel.ForeColor = WbTheme.Red;
                AppendLog(_parseLog, "FATAL: " + e.Error.ToString(), WbTheme.Red);
                return;
            }

            var result = (object[])e.Result;
            _engine = (AhkAstEngine)result[0];
            _currentAst = (AstNode)result[1];
            var errors = (AstNode[])result[2];
            var warnings = (List<AstNode>)result[3];
            int nodeCount = (int)result[4];
            int depth = (int)result[5];

            int errorCount = errors != null ? errors.Length : 0;
            int warnCount = warnings.Count;

            // Build tree (must be on UI thread) — cap at MAX_TREE_NODES for responsiveness
            const int MAX_TREE_NODES = 10000;
            _statusLabel.Text = string.Format("⏳ Building tree view... ({0:N0} nodes)", nodeCount);
            Application.DoEvents();

            _astTree.BeginUpdate();
            _astTree.Nodes.Clear();
            int treeNodeCount = 0;
            PopulateTree(_currentAst, null, ref treeNodeCount, MAX_TREE_NODES, _astTree);
            _astTree.EndUpdate();
            if (_astTree.Nodes.Count > 0)
            {
                _astTree.Nodes[0].Expand();
            }

            // Populate error grid (limit to 500 to keep responsive)
            _statusLabel.Text = "⏳ Populating diagnostics...";
            Application.DoEvents();

            PopulateErrorGrid(errors, warnings);
            WriteParseLog(source, sw.ElapsedMilliseconds, nodeCount, depth, errorCount, warnCount, errors, warnings);

            // Update UI
            _treeStats.Text = string.Format("  {0} nodes, depth {1}", nodeCount, depth);
            _btnErrTab.Text = string.Format("! Errors ({0})", errorCount + warnCount);
            _statusStats.Text = string.Format("{0} nodes | {1} errors | {2} warnings | {3}ms",
                nodeCount, errorCount, warnCount, sw.ElapsedMilliseconds);

            if (errorCount > 0)
            {
                _statusLabel.Text = string.Format("!! Parsed with {0} error(s) and {1} warning(s) in {2}ms", errorCount, warnCount, sw.ElapsedMilliseconds);
                _statusLabel.ForeColor = WbTheme.Red;
                SelectTab(0);
            }
            else if (warnCount > 0)
            {
                _statusLabel.Text = string.Format("OK Parsed with {0} warning(s) in {1}ms", warnCount, sw.ElapsedMilliseconds);
                _statusLabel.ForeColor = WbTheme.Yellow;
                SelectTab(0);
            }
            else
            {
                _statusLabel.Text = string.Format("OK Parsed successfully -- {0} nodes in {1}ms", nodeCount, sw.ElapsedMilliseconds);
                _statusLabel.ForeColor = WbTheme.Green;
            }

            // Inline includes: update source editor with expanded code
            if (_inlineIncludes && _currentAst != null)
            {
                try
                {
                    _sourceLineMappings.Clear();
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var allIncluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int inlinedLineIndex = 0;
                    string expanded = PreprocessInlinedSource(currentFile, visited, allIncluded, ref inlinedLineIndex);
                    _sourceEditor.Text = expanded;
                    _sourceEditorIsInlined = true;
                    _statusLabel.Text += " | Source inlined with includes";
                }
                catch { /* non-fatal */ }
            }
            else if (_sourceEditorIsInlined && !string.IsNullOrEmpty(currentFile) && File.Exists(currentFile))
            {
                try
                {
                    _sourceEditor.Text = File.ReadAllText(currentFile, Encoding.UTF8);
                    _sourceEditorIsInlined = false;
                }
                catch { /* non-fatal */ }
            }

            // Restore scroll positions mathematically
            if (parseLogScroll > 0 && _parseLog != null && _parseLog.IsHandleCreated)
            {
                int parseLogNew = SendMessageInt(_parseLog.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (parseLogNew != parseLogScroll) SendMessageInt(_parseLog.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(parseLogScroll - parseLogNew));
            }
            if (emitViewScroll > 0 && _emitView != null && _emitView.IsHandleCreated)
            {
                int emitViewNew = SendMessageInt(_emitView.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (emitViewNew != emitViewScroll) SendMessageInt(_emitView.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(emitViewScroll - emitViewNew));
            }
            if (debugLogScroll > 0 && _debugLog != null && _debugLog.IsHandleCreated)
            {
                int debugLogNew = SendMessageInt(_debugLog.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (debugLogNew != debugLogScroll) SendMessageInt(_debugLog.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(debugLogScroll - debugLogNew));
            }
            if (!string.IsNullOrEmpty(astSelectedNodePath) && _astTree != null)
            {
                var node = FindNodeByPath(_astTree.Nodes, astSelectedNodePath);
                if (node != null) _astTree.SelectedNode = node;
            }
            if (!string.IsNullOrEmpty(astTopNodePath) && _astTree != null)
            {
                var node = FindNodeByPath(_astTree.Nodes, astTopNodePath);
                if (node != null) _astTree.TopNode = node;
            }
        };

        worker.RunWorkerAsync();
    }

    private TreeNode FindNodeByPath(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.FullPath == path) return n;
            if (path.StartsWith(n.FullPath + _astTree.PathSeparator))
            {
                var found = FindNodeByPath(n.Nodes, path);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Auto-detect indentation style from source: tabs vs spaces, and indent size.
    /// Scans the first 100 lines for the first indented line.
    /// </summary>
    private void DetectIndentStyle(string source)
    {
        string[] srcLines = source.Split('\n');
        int limit = Math.Min(srcLines.Length, 100);
        for (int i = 0; i < limit; i++)
        {
            string line = srcLines[i];
            if (line.Length == 0 || !char.IsWhiteSpace(line[0])) continue;
            // Skip blank-only lines
            if (line.Trim().Length == 0) continue;

            if (line[0] == '\t')
            {
                _useTabs = true;
                return;
            }
            else if (line[0] == ' ')
            {
                // Count leading spaces
                int spaces = 0;
                while (spaces < line.Length && line[spaces] == ' ') spaces++;
                _useTabs = false;
                // Common indent sizes: 2, 3, 4, 8
                if (spaces <= 2) _indentSize = 2;
                else if (spaces <= 4) _indentSize = spaces;
                else _indentSize = 4;
                return;
            }
        }
    }

    private void ParseEmittedCode()
    {
        if (_isParsing) return;

        string source = _emitSourceEditor.Text;
        if (string.IsNullOrEmpty(source))
        {
            _statusLabel.Text = "Nothing to parse in emitted source";
            return;
        }

        _isParsing = true;
        _statusLabel.Text = "⏳ Parsing emitted source...";
        _statusLabel.ForeColor = WbTheme.Sky;

        int lineCount = source.Split('\n').Length;
        var sw = Stopwatch.StartNew();

        // Progress timer — updates UI every 200ms while parsing
        string[] spinner = new string[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        int spinIdx = 0;
        var progressTimer = new System.Windows.Forms.Timer { Interval = 200 };
        progressTimer.Tick += (s, e) =>
        {
            string spin = spinner[spinIdx++ % spinner.Length];
            double elapsed = sw.Elapsed.TotalSeconds;
            _statusLabel.Text = string.Format("{0} Parsing emitted {1:N0} lines... ({2:F1}s elapsed)",
                spin, lineCount, elapsed);
        };
        progressTimer.Start();

        // Parse on background thread
        var worker = new BackgroundWorker();
        worker.DoWork += (s, e) =>
        {
            var engine = new AhkAstEngine();
            // Emitted code never follows includes
            AstNode ast = engine.Parse(source);

            int nodeCount = 0;
            int depth = 0;
            CountNodes(ast, 0, ref nodeCount, ref depth);

            e.Result = new object[] { engine, ast, nodeCount, depth };
        };

        worker.RunWorkerCompleted += (s, e) =>
        {
            progressTimer.Stop();
            progressTimer.Dispose();
            sw.Stop();
            _isParsing = false;

            if (e.Error != null)
            {
                _statusLabel.Text = "Parse emitted failed: " + e.Error.Message;
                _statusLabel.ForeColor = WbTheme.Red;
                return;
            }

            var result = (object[])e.Result;
            _emitCurrentAst = (AstNode)result[1];
            int nodeCount = (int)result[2];
            int depth = (int)result[3];

            // Build tree (must be on UI thread)
            const int MAX_TREE_NODES = 10000;
            _statusLabel.Text = string.Format("⏳ Building emitted tree view... ({0:N0} nodes)", nodeCount);
            Application.DoEvents();

            _emitAstTree.BeginUpdate();
            _emitAstTree.Nodes.Clear();
            int treeNodeCount = 0;
            PopulateTree(_emitCurrentAst, null, ref treeNodeCount, MAX_TREE_NODES, _emitAstTree);
            _emitAstTree.EndUpdate();
            if (_emitAstTree.Nodes.Count > 0)
            {
                _emitAstTree.Nodes[0].Expand();
            }

            // Update UI
            _emitTreeStats.Text = string.Format("  {0} nodes, depth {1}", nodeCount, depth);

            _statusLabel.Text = string.Format("OK Parsed emitted source successfully -- {0} nodes in {1}ms", nodeCount, sw.ElapsedMilliseconds);
            _statusLabel.ForeColor = WbTheme.Green;

            // Show Emit Tree if we are looking at the Emitted Source
            if (_dockPanel.ActiveDocument == _emitSourceContent)
            {
                _treeContent.Hide();
                _emitTreeContent.Show(_dockPanel, DockState.DockRight);
            }
        };

        worker.RunWorkerAsync();
    }

    private void ParseClipboard()
    {
        if (Clipboard.ContainsText())
        {
            _sourceEditor.Text = Clipboard.GetText();
            HighlightControl(_sourceEditor);
            _currentFile = null;
            Text = "AHK# AST Workbench — (clipboard)";
            ParseCurrent();
        }
    }

    private string PreprocessInlinedSource(string filePath, HashSet<string> visited, HashSet<string> allIncluded, ref int currentInlinedLineIndex, string mainScriptDir = null)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (string.IsNullOrEmpty(mainScriptDir))
        {
            mainScriptDir = Path.GetDirectoryName(fullPath);
        }
        string fileName = Path.GetFileName(fullPath);
        if (allIncluded.Contains(fileName) || allIncluded.Contains(fullPath))
        {
            string line = "; duplicate include: " + filePath;
            AddLineMapping(fullPath, 1, currentInlinedLineIndex);
            currentInlinedLineIndex++;
            return line + "\n";
        }
        if (visited.Contains(fileName) || visited.Contains(fullPath))
        {
            string line = "; Warning: Circular include: " + filePath;
            AddLineMapping(fullPath, 1, currentInlinedLineIndex);
            currentInlinedLineIndex++;
            return line + "\n";
        }

        if (!File.Exists(fullPath))
        {
            string line = "; Error: Include not found: " + filePath;
            AddLineMapping(fullPath, 1, currentInlinedLineIndex);
            currentInlinedLineIndex++;
            return line + "\n";
        }

        visited.Add(fileName);
        visited.Add(fullPath);
        allIncluded.Add(fileName);
        allIncluded.Add(fullPath);
        string[] lines = File.ReadAllLines(fullPath, Encoding.UTF8);
        var sb = new StringBuilder();

        string includeDir = Path.GetDirectoryName(fullPath);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("#Include", StringComparison.OrdinalIgnoreCase))
            {
                // Extract path from directive
                int idx = trimmed.IndexOf("Include", StringComparison.OrdinalIgnoreCase);
                string rest = trimmed.Substring(idx + 7).Trim();

                // 1. Strip comments
                int commentIdx = rest.IndexOf(" ;");
                if (commentIdx > 0) rest = rest.Substring(0, commentIdx).TrimEnd();

                // 2. Strip surrounding quotes first (e.g. for `#Include "*i file.ahk"`)
                if (rest.Length > 2 && ((rest[0] == '\'' && rest[rest.Length - 1] == '\'')
                    || (rest[0] == '"' && rest[rest.Length - 1] == '"')))
                    rest = rest.Substring(1, rest.Length - 2).Trim();

                // 3. Detect and strip optional marker *i
                bool optional = false;
                if (rest.StartsWith("*i", StringComparison.OrdinalIgnoreCase) &&
                    (rest.Length == 2 || rest[2] == ' ' || rest[2] == '\t'))
                {
                    optional = true;
                    rest = rest.Substring(2).Trim();
                }

                // 4. Strip surrounding quotes again (e.g. for `#Include *i "file.ahk"`)
                if (rest.Length > 2 && ((rest[0] == '\'' && rest[rest.Length - 1] == '\'')
                    || (rest[0] == '"' && rest[rest.Length - 1] == '"')))
                    rest = rest.Substring(1, rest.Length - 2).Trim();

                bool isLibInclude = false;
                string libName = "";
                if (rest.StartsWith("<") && rest.EndsWith(">") && rest.Length > 2)
                {
                    isLibInclude = true;
                    libName = rest.Substring(1, rest.Length - 2).Trim();
                }

                string subFilePath = null;
                if (isLibInclude)
                {
                    string libFileName = libName;
                    if (!libFileName.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
                    {
                        libFileName += ".ahk";
                    }

                    var searchDirs = new List<string>();
                    if (!string.IsNullOrEmpty(mainScriptDir))
                    {
                        searchDirs.Add(Path.Combine(mainScriptDir, "Lib"));
                    }
                    if (!string.IsNullOrEmpty(includeDir) && includeDir != mainScriptDir)
                    {
                        searchDirs.Add(Path.Combine(includeDir, "Lib"));
                    }

                    try
                    {
                        string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        if (!string.IsNullOrEmpty(myDocs))
                        {
                            searchDirs.Add(Path.Combine(myDocs, "AutoHotkey", "Lib"));
                        }
                    }
                    catch { }

                    try
                    {
                        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        if (!string.IsNullOrEmpty(programFiles))
                        {
                            searchDirs.Add(Path.Combine(programFiles, "AutoHotkey", "v2", "Lib"));
                            searchDirs.Add(Path.Combine(programFiles, "AutoHotkey", "Lib"));
                        }
                    }
                    catch { }

                    try
                    {
                        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                        if (!string.IsNullOrEmpty(programFilesX86))
                        {
                            searchDirs.Add(Path.Combine(programFilesX86, "AutoHotkey", "v2", "Lib"));
                            searchDirs.Add(Path.Combine(programFilesX86, "AutoHotkey", "Lib"));
                        }
                    }
                    catch { }

                    searchDirs.Add(@"C:\Program Files\AutoHotkey\v2\Lib");
                    searchDirs.Add(@"C:\Program Files\AutoHotkey\Lib");

                    try
                    {
                        string envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                        foreach (string dir in envPath.Split(';'))
                        {
                            string trimmedDir = dir.Trim();
                            if (string.IsNullOrEmpty(trimmedDir)) continue;
                            if (File.Exists(Path.Combine(trimmedDir, "AutoHotkey.exe")) || 
                                File.Exists(Path.Combine(trimmedDir, "AutoHotkey64.exe")) ||
                                File.Exists(Path.Combine(trimmedDir, "AutoHotkey32.exe")))
                            {
                                searchDirs.Add(Path.Combine(trimmedDir, "Lib"));
                                searchDirs.Add(Path.Combine(trimmedDir, "..", "Lib"));
                            }
                        }
                    }
                    catch { }

                    foreach (var sDir in searchDirs)
                    {
                        try
                        {
                            if (Directory.Exists(sDir))
                            {
                                string candidate = Path.Combine(sDir, libFileName);
                                if (File.Exists(candidate))
                                {
                                    subFilePath = Path.GetFullPath(candidate);
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (subFilePath == null)
                    {
                        if (!optional)
                        {
                            sb.AppendLine("; Error: Library include not found: <" + libName + ">");
                        }
                        AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                        currentInlinedLineIndex++;
                        continue;
                    }
                }
                else
                {
                    if (rest.EndsWith("\\") || rest.EndsWith("/"))
                    {
                        string dirPath = Path.IsPathRooted(rest) ? rest : Path.Combine(includeDir, rest);
                        try { if (Directory.Exists(dirPath)) includeDir = Path.GetFullPath(dirPath); } catch { }
                        sb.AppendLine(line);
                        AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                        currentInlinedLineIndex++;
                        continue;
                    }

                    subFilePath = Path.IsPathRooted(rest) ? rest : Path.Combine(includeDir, rest);
                    try { subFilePath = Path.GetFullPath(subFilePath); }
                    catch
                    {
                        sb.AppendLine(line);
                        AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                        currentInlinedLineIndex++;
                        continue;
                    }
                }

                string beginComment = "; --- begin: " + Path.GetFileName(subFilePath) + " ---";
                sb.AppendLine(beginComment);
                AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                currentInlinedLineIndex++;

                string subContent = PreprocessInlinedSource(subFilePath, visited, allIncluded, ref currentInlinedLineIndex, mainScriptDir);
                sb.Append(subContent);

                string endComment = "; --- end: " + Path.GetFileName(subFilePath) + " ---";
                sb.AppendLine(endComment);
                AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                currentInlinedLineIndex++;
            }
            else
            {
                sb.AppendLine(line);
                AddLineMapping(fullPath, i + 1, currentInlinedLineIndex);
                currentInlinedLineIndex++;
            }
        }

        visited.Remove(fileName);
        visited.Remove(fullPath);
        return sb.ToString();
    }

    private void AddLineMapping(string fileName, int originalLine, int inlinedLineIndex)
    {
        string fn = Path.GetFileName(fileName);
        Dictionary<int, int> fileMap;
        if (!_sourceLineMappings.TryGetValue(fn, out fileMap))
        {
            fileMap = new Dictionary<int, int>();
            _sourceLineMappings[fn] = fileMap;
        }
        if (!fileMap.ContainsKey(originalLine))
        {
            fileMap[originalLine] = inlinedLineIndex;
        }
    }



}
