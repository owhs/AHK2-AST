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
    // ── AST Tree ──────────────────────────────────────────────────────────

    private void BuildTreePanel()
    {
        _treePanel = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Base };

        // Header + filter bar
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = WbTheme.Mantle, Padding = new Padding(8, 4, 8, 4) };
        var headerLabel = new Label { Text = "SOURCE AST", ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(8, 6) };

        _treeStats = new Label { Text = "", ForeColor = WbTheme.Overlay0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(70, 6) };

        _treeFilter = new TextBox
        {
            Location = new Point(8, 30),
            Width = 300,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            BorderStyle = BorderStyle.FixedSingle
        };
        SetCueText(_treeFilter, "Filter nodes by type or value...");
        _treeFilter.TextChanged += (s, e) => FilterTree(_treeFilter.Text);

        header.Controls.Add(headerLabel);
        header.Controls.Add(_treeStats);
        header.Controls.Add(_treeFilter);
        header.Resize += (s, e) => { _treeFilter.Width = Math.Max(100, header.Width - 20); };

        // Tree
        _astTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            HideSelection = false,
            ItemHeight = 22,
            DrawMode = TreeViewDrawMode.OwnerDrawText,
            ImageList = _nodeIcons
        };
        _astTree.HandleCreated += (s, e) => SetWindowTheme(_astTree.Handle, "DarkMode_Explorer", null);
        _astTree.DrawNode += AstTree_DrawNode;
        _astTree.AfterSelect += AstTree_AfterSelect;
        _astTree.BeforeExpand += AstTree_BeforeExpand;
        EnableDoubleBuffering(_astTree);

        _treePanel.Controls.Add(_astTree);
        _treePanel.Controls.Add(header);
    }

    private void BuildEmitTreePanel()
    {
        _emitTreePanel = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Base };

        // Header + filter bar
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = WbTheme.Mantle, Padding = new Padding(8, 4, 8, 4) };
        var headerLabel = new Label { Text = "EMITTED AST", ForeColor = WbTheme.Subtext0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(8, 6) };

        _emitTreeStats = new Label { Text = "", ForeColor = WbTheme.Overlay0, Font = WbTheme.UISmall, AutoSize = true, Location = new Point(80, 6) };

        _emitTreeFilter = new TextBox
        {
            Location = new Point(8, 30),
            Width = 300,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            BorderStyle = BorderStyle.FixedSingle
        };
        SetCueText(_emitTreeFilter, "Filter nodes by type or value...");
        _emitTreeFilter.TextChanged += (s, e) => FilterEmitTree(_emitTreeFilter.Text);

        header.Controls.Add(headerLabel);
        header.Controls.Add(_emitTreeStats);
        header.Controls.Add(_emitTreeFilter);
        header.Resize += (s, e) => { _emitTreeFilter.Width = Math.Max(100, header.Width - 20); };

        // Tree
        _emitAstTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            HideSelection = false,
            ItemHeight = 22,
            DrawMode = TreeViewDrawMode.OwnerDrawText,
            ImageList = _nodeIcons
        };
        _emitAstTree.HandleCreated += (s, e) => SetWindowTheme(_emitAstTree.Handle, "DarkMode_Explorer", null);
        _emitAstTree.DrawNode += AstTree_DrawNode; // Reuse drawing logic
        _emitAstTree.AfterSelect += EmitAstTree_AfterSelect;
        _emitAstTree.BeforeExpand += EmitAstTree_BeforeExpand;
        EnableDoubleBuffering(_emitAstTree);

        _emitTreePanel.Controls.Add(_emitAstTree);
        _emitTreePanel.Controls.Add(header);
    }

    private void AstTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
    {
        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        Color bg = selected ? WbTheme.Selection : WbTheme.Base;
        Color fg = WbTheme.Text;

        // Color by node type tag — Tag is now an AstNode or a string ("_lazy")
        string tag = "";
        if (e.Node.Tag is AstNode)
            tag = ((AstNode)e.Node.Tag).NodeType;
        else if (e.Node.Tag is string)
            tag = (string)e.Node.Tag;
        if (tag == "Error") fg = WbTheme.Red;
        else if (tag == "Warning") fg = WbTheme.Yellow;
        else if (tag == "Comment") fg = WbTheme.Overlay0;
        else if (tag == "Class" || tag == "Method") fg = WbTheme.Green;
        else if (tag == "Call" || tag == "BinaryExpr") fg = WbTheme.Mauve;
        else if (tag == "String") fg = WbTheme.Peach;
        else if (tag == "Number") fg = WbTheme.Peach;
        else if (tag == "Directive") fg = WbTheme.Sky;
        else if (tag == "Identifier") fg = WbTheme.Blue;
        else if (tag == "_lazy") fg = WbTheme.Overlay0;

        using (var bgBrush = new SolidBrush(bg))
        using (var fgBrush = new SolidBrush(fg))
        {
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, _astTree.Font, e.Bounds, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void AstTree_AfterSelect(object sender, TreeViewEventArgs e)
    {
        if (!_autoFocusCode) return;
        if (e.Node == null || e.Node.Tag == null) return;

        AstNode ast = e.Node.Tag as AstNode;
        if (ast != null)
        {
            HighlightAstNode(ast, e.Node, _sourceEditor, _astTree);
        }
    }

    private void EmitAstTree_AfterSelect(object sender, TreeViewEventArgs e)
    {
        if (!_autoFocusCode) return;
        if (e.Node == null || e.Node.Tag == null) return;

        AstNode ast = e.Node.Tag as AstNode;
        if (ast != null)
        {
            HighlightAstNode(ast, e.Node, _emitSourceEditor, _emitAstTree);
        }
    }

    private void AstTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
        // Lazy-load: if the node has a single "_lazy" placeholder child, expand its AstNode children on demand
        if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Name == "_lazy" && e.Node.Tag is AstNode)
        {
            var astNode = (AstNode)e.Node.Tag;
            _astTree.BeginUpdate();
            e.Node.Nodes.Clear();
            int childCount = 0;
            // Allow generous budget for this subtree (another 10K batch)
            foreach (var child in astNode.ChildNodes)
                PopulateTree(child, e.Node, ref childCount, 10000, _astTree);
            _astTree.EndUpdate();
        }
    }

    private void EmitAstTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
        // Lazy-load: if the node has a single "_lazy" placeholder child, expand its AstNode children on demand
        if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Name == "_lazy" && e.Node.Tag is AstNode)
        {
            var astNode = (AstNode)e.Node.Tag;
            _emitAstTree.BeginUpdate();
            e.Node.Nodes.Clear();
            int childCount = 0;
            foreach (var child in astNode.ChildNodes)
                PopulateTree(child, e.Node, ref childCount, 10000, _emitAstTree);
            _emitAstTree.EndUpdate();
        }
    }

    private void HighlightAstNode(AstNode ast, TreeNode treeNode, RichTextBox editor, TreeView treeView)
    {
        try
        {
            int targetLine = ast.Line;
            int targetCol = ast.Column;
            string searchText = ast.Value;

            // Find the file this node belongs to by walking up the tree
            string includeFileName = null;
            TreeNode current = treeNode.Parent;
            while (current != null)
            {
                AstNode parentAst = current.Tag as AstNode;
                if (parentAst != null && parentAst.NodeType == "Include")
                {
                    if (!string.IsNullOrEmpty(parentAst.Value))
                        includeFileName = System.IO.Path.GetFileName(parentAst.Value);
                    break;
                }
                current = current.Parent;
            }

            string[] lines = editor.Lines;
            int estimatedLineIndex = targetLine - 1;

            // If the source is inlined, we must mathematically map the line number by skipping over injected includes
            if (_inlineIncludes)
            {
                string fn = includeFileName ?? (string.IsNullOrEmpty(_currentFile) ? "" : System.IO.Path.GetFileName(_currentFile));
                Dictionary<int, int> fileMap;
                if (_sourceLineMappings.TryGetValue(fn, out fileMap))
                {
                    int inlinedIdx;
                    if (fileMap.TryGetValue(targetLine, out inlinedIdx))
                    {
                        estimatedLineIndex = inlinedIdx;
                    }
                }
            }
            else if (!_inlineIncludes && includeFileName != null)
            {
                // If not inlined, the node is in a different file that isn't visible
                return;
            }

            if (estimatedLineIndex < 0) estimatedLineIndex = 0;
            if (estimatedLineIndex >= lines.Length) estimatedLineIndex = lines.Length - 1;

            int bestLineIndex = estimatedLineIndex;
            int matchCol = -1;

            // If the AST node's text spans multiple lines, just search for the first line
            // otherwise IndexOf on a single editor line will always fail to find the \n character
            string firstLineOfSearch = searchText ?? "";
            int nlIdx = firstLineOfSearch.IndexOf('\n');
            if (nlIdx >= 0)
                firstLineOfSearch = firstLineOfSearch.Substring(0, nlIdx).TrimEnd('\r');

            // Robust text search: look around the estimated line for the node's value
            if (!string.IsNullOrEmpty(firstLineOfSearch))
            {
                int searchRange = _inlineIncludes ? 250 : 5; // Emitted lines can shift slightly due to blank lines
                bool found = false;

                for (int radius = 0; radius <= searchRange; radius++)
                {
                    // Check down
                    int down = estimatedLineIndex + radius;
                    if (down < lines.Length)
                    {
                        int idx = lines[down].IndexOf(firstLineOfSearch, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            // Ensure we don't accidentally match in the wrong scope
                            bestLineIndex = down;
                            matchCol = idx;
                            found = true;
                            break;
                        }
                    }
                    // Check up
                    int up = estimatedLineIndex - radius;
                    if (up >= 0)
                    {
                        int idx = lines[up].IndexOf(firstLineOfSearch, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            bestLineIndex = up;
                            matchCol = idx;
                            found = true;
                            break;
                        }
                    }
                }

                // Fallback to exact column if we couldn't find the text, or if the text was too generic
                if (!found && targetCol > 0)
                {
                    matchCol = targetCol - 1;
                }
            }
            else if (targetCol > 0)
            {
                matchCol = targetCol - 1;
            }

            int firstChar = editor.GetFirstCharIndexFromLine(bestLineIndex);
            if (firstChar < 0) return;

            int selStart = firstChar + (matchCol >= 0 ? matchCol : 0);
            int selLen = string.IsNullOrEmpty(firstLineOfSearch) ? lines[bestLineIndex].Length - (matchCol >= 0 ? matchCol : 0) : firstLineOfSearch.Length;

            if (selLen < 1) selLen = 1;
            if (selStart + selLen > editor.TextLength) selLen = editor.TextLength - selStart;
            if (selStart < 0 || selStart >= editor.TextLength) return;

            SendMessageInt(editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                editor.Select(selStart, 0);
                editor.ScrollToCaret();
                editor.Select(selStart, selLen);
            }
            finally
            {
                SendMessageInt(editor.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                editor.Invalidate();
            }
        }
        catch (Exception ex)
        {
            LogDebug("Highlight Error: " + ex.Message);
        }
    }

    private void HighlightSourceLine(int line, int col)
    {
        try
        {
            string[] lines = _sourceEditor.Lines;
            if (line < 1 || line > lines.Length) return;

            int firstChar = _sourceEditor.GetFirstCharIndexFromLine(line - 1);
            if (firstChar < 0) return;

            int selStart = firstChar + (col > 0 ? col - 1 : 0);
            int selLen = lines[line - 1].Length - (col > 0 ? col - 1 : 0);
            if (selLen < 1) selLen = 1;

            if (selStart + selLen > _sourceEditor.TextLength)
                selLen = _sourceEditor.TextLength - selStart;
            if (selStart < 0 || selStart >= _sourceEditor.TextLength) return;

            SendMessageInt(_sourceEditor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                _sourceEditor.Select(selStart, 0);
                _sourceEditor.ScrollToCaret();
                _sourceEditor.Select(selStart, selLen);
            }
            finally
            {
                SendMessageInt(_sourceEditor.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                _sourceEditor.Invalidate();
            }
        }
        catch (Exception ex)
        {
            LogDebug("Highlight Error: " + ex.Message);
        }
    }

    // ── Tree Population ───────────────────────────────────────────────────

    private void CountNodes(AstNode node, int currentDepth, ref int count, ref int maxDepth)
    {
        if (node == null) return;
        count++;
        if (currentDepth > maxDepth) maxDepth = currentDepth;
        foreach (var child in node.ChildNodes)
            CountNodes(child, currentDepth + 1, ref count, ref maxDepth);
    }

    private void PopulateTree(AstNode astNode, TreeNode parentTreeNode, ref int count, int maxNodes, TreeView treeView, int currentDepth = 0)
    {
        if (astNode == null) return;
        count++;

        // Cap: stop adding nodes to keep the UI responsive
        if (count > maxNodes)
            return;

        // Build display text
        string display = astNode.NodeType;
        if (!string.IsNullOrEmpty(astNode.Value))
        {
            string val = astNode.Value.Length > 50 ? astNode.Value.Substring(0, 50) + "..." : astNode.Value;
            display += "  ═ " + val;
        }
        if (astNode.Line > 0)
            display += string.Format("  ({0}:{1})", astNode.Line, astNode.Column);
        if (astNode.IsHealed)
            display += "  [healed]";

        // Choose icon
        int iconIdx = GetNodeIconIndex(astNode.NodeType);

        var treeNode = new TreeNode(display)
        {
            Tag = astNode,  // Store AstNode reference for lazy loading
            Name = string.Format("{0}|{1}|{2}|{3}|{4}",
                astNode.NodeType, astNode.Line, astNode.Column, astNode.EndLine, astNode.EndColumn),
            ImageIndex = iconIdx,
            SelectedImageIndex = iconIdx
        };

        if (parentTreeNode == null)
            treeView.Nodes.Add(treeNode);
        else
            parentTreeNode.Nodes.Add(treeNode);

        // Depth-based lazy loading: only populate direct children (currentDepth = 0).
        // For any child of those children (currentDepth >= 1), add a lazy-load placeholder.
        if (currentDepth >= 1 && astNode.ChildNodes.Length > 0)
        {
            var placeholder = new TreeNode(string.Format("  ⋯ {0} children (expand to load)", astNode.ChildNodes.Length))
            {
                ForeColor = WbTheme.Overlay0
            };
            placeholder.Name = "_lazy";
            placeholder.Tag = "_lazy";
            treeNode.Nodes.Add(placeholder);
            return;
        }

        foreach (var child in astNode.ChildNodes)
            PopulateTree(child, treeNode, ref count, maxNodes, treeView, currentDepth + 1);
    }

    private int GetNodeIconIndex(string nodeType)
    {
        switch (nodeType)
        {
            case "Program":
            case "Block": return 0;     // Blue
            case "Class":
            case "Method":
            case "Property": return 1;  // Green
            case "If":
            case "While":
            case "For":
            case "Loop":
            case "Switch":
            case "Try": return 2;       // Teal
            case "Call":
            case "BinaryExpr":
            case "UnaryExpr":
            case "Ternary": return 3;   // Mauve
            case "String":
            case "Number": return 4;    // Peach
            case "Identifier": return 5; // Blue light
            case "Error": return 6;     // Red
            case "Warning": return 7;   // Yellow
            case "Directive": return 8; // Sky
            case "Comment": return 9;   // Gray (muted)
            default: return 9;          // Gray
        }
    }

    // ── Tree Filtering ────────────────────────────────────────────────────

    private void FilterTree(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            // Reparse to show full tree
            if (_currentAst != null)
            {
                _astTree.BeginUpdate();
                _astTree.Nodes.Clear();
                int n = 0;
                PopulateTree(_currentAst, null, ref n, 10000, _astTree);
                _astTree.EndUpdate();
                if (_astTree.Nodes.Count > 0) _astTree.Nodes[0].Expand();
            }
            return;
        }

        // Walk tree and show only matching nodes
        _astTree.BeginUpdate();
        FilterTreeNodes(_astTree.Nodes, filter.ToLower());
        _astTree.EndUpdate();
    }

    private void FilterEmitTree(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            // Reparse to show full tree
            if (_emitCurrentAst != null)
            {
                _emitAstTree.BeginUpdate();
                _emitAstTree.Nodes.Clear();
                int n = 0;
                PopulateTree(_emitCurrentAst, null, ref n, 10000, _emitAstTree);
                _emitAstTree.EndUpdate();
                if (_emitAstTree.Nodes.Count > 0) _emitAstTree.Nodes[0].Expand();
            }
            return;
        }

        // Walk tree and show only matching nodes
        _emitAstTree.BeginUpdate();
        FilterTreeNodes(_emitAstTree.Nodes, filter.ToLower());
        _emitAstTree.EndUpdate();
    }

    private bool FilterTreeNodes(TreeNodeCollection nodes, string filter)
    {
        bool anyVisible = false;
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            TreeNode node = nodes[i];
            string tag = "";
            if (node.Tag is AstNode) tag = ((AstNode)node.Tag).NodeType.ToLower();
            else if (node.Tag is string) tag = ((string)node.Tag).ToLower();
            string text = node.Text.ToLower();

            bool childMatch = FilterTreeNodes(node.Nodes, filter);
            bool selfMatch = tag.Contains(filter) || text.Contains(filter);

            if (selfMatch || childMatch)
            {
                anyVisible = true;
                if (selfMatch) node.Expand();
            }
            else
            {
                nodes.RemoveAt(i);
            }
        }
        return anyVisible;
    }

    // ── Node Icons ────────────────────────────────────────────────────────

    private static ImageList BuildNodeIcons()
    {
        var list = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

        Color[] colors = {
            WbTheme.Blue,      // 0: Program/Block
            WbTheme.Green,     // 1: Class/Method
            WbTheme.Teal,      // 2: Flow control
            WbTheme.Mauve,     // 3: Expression
            WbTheme.Peach,     // 4: Literal
            WbTheme.Sapphire,  // 5: Identifier
            WbTheme.Red,       // 6: Error
            WbTheme.Yellow,    // 7: Warning
            WbTheme.Sky,       // 8: Directive
            WbTheme.Overlay0   // 9: Other
        };

        foreach (Color c in colors)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(c))
                    g.FillEllipse(brush, 2, 2, 12, 12);
            }
            list.Images.Add(bmp);
        }

        return list;
    }

    private static Icon BuildAppIcon()
    {
        try
        {
            // 1. Try extracting icon from the executing assembly (which contains the win32 icon resource)
            string loc = typeof(AstWorkbenchForm).Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
            {
                Icon icon = Icon.ExtractAssociatedIcon(loc);
                if (icon != null) return icon;
            }
        }
        catch { }

        try
        {
            // 2. Fallback: Try loading icon.ico directly from application's current or base directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(appDir, "icon.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            // 3. Fallback: Try parent directory (in case running from build/ and icon.ico is in project root)
            string parentIconPath = Path.Combine(Path.GetDirectoryName(appDir.TrimEnd(Path.DirectorySeparatorChar)) ?? "", "icon.ico");
            if (File.Exists(parentIconPath))
            {
                return new Icon(parentIconPath);
            }
        }
        catch { }

        // 4. Ultimate Fallback: Generate the original custom vector-styled tree icon dynamically
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(WbTheme.Base);
            using (var brush = new SolidBrush(WbTheme.Lavender))
                g.FillEllipse(brush, 2, 2, 28, 28);
            using (var pen = new Pen(WbTheme.Mauve, 2f))
            {
                g.DrawLine(pen, 16, 6, 16, 16);
                g.DrawLine(pen, 16, 16, 8, 22);
                g.DrawLine(pen, 16, 16, 24, 22);
                g.DrawLine(pen, 16, 10, 10, 14);
                g.DrawLine(pen, 16, 10, 22, 14);
            }
        }
        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
