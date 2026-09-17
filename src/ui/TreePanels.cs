// AST and Outline panels. Both use RichTree: an owner-drawn TreeView whose rows show a coloured head, the main
// text, and a dim suffix (e.g. `BinaryExpr  x := Foo.M(1)  16:1`).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal class RichNode : TreeNode
{
    public string Head, Main, Suffix;
    public Color HeadColor;
    public object Payload;
    public bool Loaded;

    public RichNode(string head, Color headColor, string main, string suffix, object payload)
    {
        Head = head; HeadColor = headColor; Main = main; Suffix = suffix; Payload = payload;
        Text = (head + "  " + main).Trim();
    }
}

internal class RichTree : TreeView
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    /// <summary>EnsureVisible also scrolls sideways to the node; bring the tree back to its left edge.</summary>
    public void ScrollLeft() { if (IsHandleCreated) SendMessage(Handle, 0x114 /*WM_HSCROLL*/, (IntPtr)6 /*SB_LEFT*/, IntPtr.Zero); }

    public RichTree()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawText;
        BorderStyle = BorderStyle.None;
        HideSelection = false;
        FullRowSelect = false;
        ShowLines = false;
        ItemHeight = 22;
        Font = WbTheme.UIFont;
        Indent = 16;
        DoubleBuffered = true;
        UiTheming.DarkScrollbars(this);
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        var rn = e.Node as RichNode;
        if (rn == null || e.Bounds.Width == 0) { e.DrawDefault = true; base.OnDrawNode(e); return; }
        var g = e.Graphics;
        bool sel = (e.State & TreeNodeStates.Selected) != 0;
        var bounds = new Rectangle(e.Bounds.X, e.Bounds.Y, ClientSize.Width - e.Bounds.X, e.Bounds.Height);
        using (var bg = new SolidBrush(sel ? (Focused ? WbTheme.Selection : WbTheme.Surface0) : BackColor))
            g.FillRectangle(bg, bounds);
        int x = e.Bounds.X + 2;
        const TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (!string.IsNullOrEmpty(rn.Head))
        {
            var sz = TextRenderer.MeasureText(g, rn.Head, Font, Size.Empty, f);
            TextRenderer.DrawText(g, rn.Head, Font, new Rectangle(x, bounds.Y, sz.Width + 2, bounds.Height), rn.HeadColor, f);
            x += sz.Width + 8;
        }
        if (!string.IsNullOrEmpty(rn.Main))
        {
            var sz = TextRenderer.MeasureText(g, rn.Main, Font, Size.Empty, f);
            TextRenderer.DrawText(g, rn.Main, Font, new Rectangle(x, bounds.Y, Math.Max(0, bounds.Right - x), bounds.Height), WbTheme.Text, f | TextFormatFlags.EndEllipsis);
            x += sz.Width + 8;
        }
        if (!string.IsNullOrEmpty(rn.Suffix) && x < bounds.Right - 20)
            TextRenderer.DrawText(g, rn.Suffix, Font, new Rectangle(x, bounds.Y, bounds.Right - x, bounds.Height), WbTheme.Overlay0, f | TextFormatFlags.EndEllipsis);
    }
}

internal class AstPanel : ToolPanel
{
    readonly IWorkbenchHost _host;
    readonly RichTree _tree;
    readonly SearchBox _search;
    readonly ToolStripLabel _stats;
    readonly ToolStripButton _btnSync, _btnComments;
    readonly Timer _searchTimer = new Timer { Interval = 300 };
    ParseResult _result;
    bool _suppress;

    public bool SyncWithEditor { get { return _btnSync.Checked; } }

    public AstPanel(IWorkbenchHost host) : base("AST")
    {
        _host = host;
        _search = new SearchBox { Cue = "Search nodes (type or text)", Width = 200 };
        BarSearch(_search, 220);
        _btnComments = BarButton(Glyphs.Filter, "Hide comments", Rebuild, true);
        _btnComments.CheckOnClick = true; _btnComments.Checked = true;
        _btnSync = BarButton(Glyphs.Link, "Follow the editor caret", () => { }, true);
        _btnSync.CheckOnClick = true; _btnSync.Checked = true;
        BarButton(Glyphs.Collapse, "Collapse all", () => { _tree.CollapseAll(); if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand(); }, true);
        _stats = new ToolStripLabel("") { Alignment = ToolStripItemAlignment.Right };
        Bar.Items.Add(_stats);

        _tree = new RichTree { Dock = DockStyle.Fill };
        _tree.BeforeExpand += (s, e) => Populate(e.Node as RichNode);
        // a click shows the code; arrow keys just move (Enter shows it), so the keyboard stays in the tree
        _tree.AfterSelect += (s, e) => { if (!_suppress && e.Action == TreeViewAction.ByMouse) ShowInEditor(e.Node as RichNode); };
        _tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) _tree.SelectedNode = e.Node; };
        _tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ShowInEditor(_tree.SelectedNode as RichNode); };
        var cm = new ContextMenuStrip { Renderer = new DarkMenuRenderer() };
        cm.Items.Add("Show in editor", null, (s, e) => ShowInEditor(_tree.SelectedNode as RichNode));
        cm.Items.Add("Copy code", null, (s, e) => { var n = Selected; if (n != null) Clipboard.SetText(AstEmitter.Emit(n, new EmitOptions(), 0)); });
        cm.Items.Add("Copy node", null, (s, e) => { var n = Selected; if (n != null) Clipboard.SetText(n.NodeType + (n.Value != null ? " = " + n.Value : "") + " @" + n.Line + ":" + n.Column); });
        cm.Items.Add("Expand below", null, (s, e) => ExpandBelow(_tree.SelectedNode as RichNode, 3000));
        _tree.ContextMenuStrip = cm;
        Controls.Add(_tree);
        _tree.BringToFront();

        _search.TextChanged += (s, e) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); Rebuild(); };
        Rebuild();
        ApplyTheme();
    }

    AstNode Selected { get { var rn = _tree.SelectedNode as RichNode; return rn == null ? null : rn.Payload as AstNode; } }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        if (_tree != null) { _tree.BackColor = WbTheme.Base; _tree.ForeColor = WbTheme.Text; _tree.Invalidate(); }
        if (_stats != null) _stats.ForeColor = WbTheme.Overlay0;
    }

    public void SetResult(ParseResult r)
    {
        // keep the user's place: remember the path of types/indices to the selected node
        var selPath = PathOf(Selected);
        _result = r;
        _stats.Text = r == null || r.Root == null ? "" : string.Format("{0:N0} nodes · {1:N0} ms", r.NodeCount, r.Ms);
        Rebuild();
        if (selPath != null && r != null) { var n = Resolve(r.Root, selPath); if (n != null) Reveal(n, false); }
    }

    static List<int> PathOf(AstNode n)
    {
        if (n == null) return null;
        var p = new List<int>();
        for (; n.Parent != null; n = n.Parent)
        {
            int idx = -1;
            for (int i = 0; i < n.Parent.ChildCount; i++) if (n.Parent.GetChild(i) == n) { idx = i; break; }
            if (idx < 0) return null;
            p.Insert(0, idx);
        }
        return p;
    }

    static AstNode Resolve(AstNode root, List<int> path)
    {
        var n = root;
        foreach (int i in path) { if (n == null || i >= n.ChildCount) return null; n = n.GetChild(i); }
        return n;
    }

    void Rebuild()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (_result == null || _result.Root == null)
            _tree.Nodes.Add(new RichNode("", WbTheme.Overlay0, "Open a script to see its syntax tree", "", null));
        else
        {
            string q = _search.Text.Trim();
            if (q.Length == 0)
            {
                var root = MakeNode(_result.Root);
                _tree.Nodes.Add(root);
                root.Expand();
            }
            else
            {
                var hits = new List<AstNode>();
                Search(_result.Root, q, hits, 2000);
                foreach (var h in hits) _tree.Nodes.Add(MakeNode(h, true));
                if (hits.Count == 0) _tree.Nodes.Add(new RichNode("", WbTheme.Overlay0, "No nodes match \"" + q + "\"", "", null));
                else if (hits.Count >= 2000) _tree.Nodes.Add(new RichNode("", WbTheme.Overlay0, "First 2,000 matches shown", "", null));
            }
        }
        _tree.EndUpdate();
    }

    static void Search(AstNode n, string q, List<AstNode> acc, int max)
    {
        if (n == null || acc.Count >= max) return;
        if (n.NodeType.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (n.Value != null && n.Value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
            acc.Add(n);
        for (int i = 0; i < n.ChildCount; i++) Search(n.GetChild(i), q, acc, max);
    }

    RichNode MakeNode(AstNode n, bool withContext = false)
    {
        string snippet = AstModel.Snippet(n);
        string suffix = n.Line > 0 ? n.Line + ":" + n.Column : "";
        if (n.NodeType == "Include" && !string.IsNullOrEmpty(n.Value))
        {
            snippet = Path.GetFileName(n.Value);
            suffix = Path.GetDirectoryName(n.Value) + "   " + suffix;
        }
        if (withContext)
        {
            string f = AstModel.FileOf(n, _result.MainFile);
            if (f != null && !string.Equals(f, _result.MainFile, StringComparison.OrdinalIgnoreCase)) suffix += "  " + Path.GetFileName(f);
        }
        if (n.IsHealed) suffix += "  healed";
        var rn = new RichNode(n.NodeType, TypeColor(n.NodeType), snippet, suffix, n);
        if (n.ChildCount > 0) rn.Nodes.Add(new TreeNode("…"));
        return rn;
    }

    void Populate(RichNode rn)
    {
        if (rn == null || rn.Loaded) return;
        rn.Loaded = true;
        var n = rn.Payload as AstNode;
        _tree.BeginUpdate();
        rn.Nodes.Clear();
        if (n != null)
            for (int i = 0; i < n.ChildCount; i++)
            {
                var c = n.GetChild(i);
                if (c == null || (_btnComments.Checked && c.NodeType == "Comment")) continue;
                rn.Nodes.Add(MakeNode(c));
            }
        _tree.EndUpdate();
    }

    void ExpandBelow(RichNode rn, int budget)
    {
        if (rn == null) return;
        _tree.BeginUpdate();
        var q = new Queue<RichNode>();
        q.Enqueue(rn);
        while (q.Count > 0 && budget > 0)
        {
            var x = q.Dequeue();
            Populate(x);
            x.Expand();
            foreach (TreeNode c in x.Nodes) { var rc = c as RichNode; if (rc != null && rc.Nodes.Count > 0) { q.Enqueue(rc); budget--; } }
        }
        _tree.EndUpdate();
    }

    void ShowInEditor(RichNode rn)
    {
        if (rn == null || _result == null) return;
        var n = rn.Payload as AstNode;
        if (n == null || n.NodeType == "Program") return;
        if (n.NodeType == "Include" && n.ChildCount > 0 && !string.IsNullOrEmpty(n.Value))
        {
            // the #Include line in its parent, not the included file
            _host.Navigate(AstModel.FileOf(n, _result.MainFile), n.Line, n.Column);
            return;
        }
        int l0, c0, l1, c1;
        AstModel.Span(n, out l0, out c0, out l1, out c1);
        if (l0 <= 0) return;
        _host.Navigate(AstModel.FileOf(n, _result.MainFile), l0, c0, l1, c1);
    }

    /// <summary>Selects the tree row of `target`, expanding its ancestors (no editor navigation).</summary>
    public void Reveal(AstNode target, bool scroll = true)
    {
        if (target == null || _result == null || _search.Text.Length > 0 || _tree.Nodes.Count == 0) return;
        var chain = new List<AstNode>();
        for (var p = target; p != null; p = p.Parent) chain.Insert(0, p);
        if (chain[0] != _result.Root) return;
        var cur = _tree.Nodes[0] as RichNode;
        _suppress = true;
        try
        {
            for (int i = 1; i < chain.Count && cur != null; i++)
            {
                Populate(cur);
                if (!cur.IsExpanded) cur.Expand();
                RichNode next = null;
                foreach (TreeNode c in cur.Nodes) { var rc = c as RichNode; if (rc != null && rc.Payload == chain[i]) { next = rc; break; } }
                cur = next;
            }
            if (cur != null)
            {
                _tree.SelectedNode = cur;
                if (scroll) { cur.EnsureVisible(); _tree.ScrollLeft(); }
            }
        }
        finally { _suppress = false; }
    }

    public static Color TypeColor(string t)
    {
        switch (t)
        {
            case "Program": case "Block": case "Include": return WbTheme.Blue;
            case "Class": case "Method": case "Property": case "Extends": return WbTheme.Green;
            case "If": case "Else": case "While": case "Loop": case "For": case "Switch": case "Case": case "Try": case "Catch":
            case "Finally": case "Return": case "Break": case "Continue": case "Throw": case "Until": case "Goto": return WbTheme.Teal;
            case "String": case "Number": return WbTheme.Peach;
            case "Identifier": case "Parameter": case "Member": return WbTheme.Sapphire;
            case "Error": return WbTheme.Red;
            case "Warning": return WbTheme.Yellow;
            case "Directive": case "Hotkey": case "Hotstring": case "Label": return WbTheme.Pink;
            case "Comment": return WbTheme.Overlay0;
            default: return WbTheme.Mauve;
        }
    }
}

internal class OutlinePanel : ToolPanel
{
    readonly IWorkbenchHost _host;
    readonly RichTree _tree;
    readonly SearchBox _search;
    readonly ImageList _icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    List<Symbol> _symbols = new List<Symbol>();
    bool _everSet;
    readonly Timer _searchTimer = new Timer { Interval = 200 };

    public OutlinePanel(IWorkbenchHost host) : base("Outline")
    {
        _host = host;
        _search = new SearchBox { Cue = "Filter symbols", Width = 200 };
        BarSearch(_search, 220);
        BarButton(Glyphs.Collapse, "Collapse all", () => _tree.CollapseAll(), true);
        BarButton(Glyphs.Expand, "Expand all", () => _tree.ExpandAll(), true);
        _tree = new RichTree { Dock = DockStyle.Fill, ImageList = _icons };
        _tree.AfterSelect += (s, e) => { if (e.Action == TreeViewAction.ByMouse) Go(e.Node as RichNode); };
        _tree.NodeMouseClick += (s, e) => { if (e.Node == _tree.SelectedNode) Go(e.Node as RichNode); };
        _tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Go(_tree.SelectedNode as RichNode); };
        Controls.Add(_tree);
        _tree.BringToFront();
        _search.TextChanged += (s, e) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); Rebuild(); };
        Rebuild();
        _search.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Down && _tree.Nodes.Count > 0) { _tree.Focus(); _tree.SelectedNode = _tree.Nodes[0]; e.Handled = true; }
            if (e.KeyCode == Keys.Enter) { var first = FirstLeaf(_tree.Nodes); if (first != null) Go(first); e.Handled = true; }
        };
        ApplyTheme();
    }

    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }

    static RichNode FirstLeaf(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            var rn = n as RichNode;
            if (rn != null && rn.Payload is Symbol && ((Symbol)rn.Payload).Kind != SymbolKind.Include) return rn;
            var f = FirstLeaf(n.Nodes);
            if (f != null) return f;
        }
        return null;
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _icons.Images.Clear();
        foreach (SymbolKind k in Enum.GetValues(typeof(SymbolKind)))
            _icons.Images.Add(Glyphs.Badge(Letter(k), KindColor(k)));
        if (_tree != null) { _tree.BackColor = WbTheme.Base; _tree.Invalidate(); }
    }

    static string Letter(SymbolKind k)
    {
        switch (k)
        {
            case SymbolKind.Class: return "C";
            case SymbolKind.Method: return "m";
            case SymbolKind.Function: return "ƒ";
            case SymbolKind.Property: return "p";
            case SymbolKind.Field: return "v";
            case SymbolKind.Hotkey: return "K";
            case SymbolKind.Hotstring: return "S";
            case SymbolKind.Label: return "L";
            case SymbolKind.Include: return "#";
            default: return "g";
        }
    }

    static char Glyph(SymbolKind k)
    {
        switch (k)
        {
            case SymbolKind.Class: return Glyphs.Class;
            case SymbolKind.Method: case SymbolKind.Function: return Glyphs.Function;
            case SymbolKind.Property: case SymbolKind.Field: return Glyphs.Tag;
            case SymbolKind.Hotkey: case SymbolKind.Hotstring: return Glyphs.Keyboard;
            case SymbolKind.Label: return Glyphs.ChevronRight;
            case SymbolKind.Include: return Glyphs.Document;
            default: return Glyphs.Variable;
        }
    }

    public static Color KindColor(SymbolKind k)
    {
        switch (k)
        {
            case SymbolKind.Class: return WbTheme.Yellow;
            case SymbolKind.Method: case SymbolKind.Function: return WbTheme.Blue;
            case SymbolKind.Property: case SymbolKind.Field: return WbTheme.Lavender;
            case SymbolKind.Hotkey: case SymbolKind.Hotstring: return WbTheme.Peach;
            case SymbolKind.Label: return WbTheme.Pink;
            case SymbolKind.Include: return WbTheme.Subtext0;
            default: return WbTheme.Red;
        }
    }

    public void SetSymbols(List<Symbol> symbols)
    {
        _symbols = symbols ?? new List<Symbol>();
        _everSet = symbols != null;
        Rebuild();
    }

    void Rebuild()
    {
        string q = _search.Text.Trim();
        var expanded = new HashSet<string>();
        CollectExpanded(_tree.Nodes, expanded);
        bool first = _tree.Nodes.Count == 0 || !(_tree.Nodes[0] is RichNode) || ((RichNode)_tree.Nodes[0]).Payload == null;
        var toExpand = new List<TreeNode>();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        Add(_tree.Nodes, _symbols, q, expanded, "", toExpand, first);
        foreach (var n in toExpand) n.Expand();
        if (_tree.Nodes.Count == 0)
            _tree.Nodes.Add(new RichNode("", WbTheme.Overlay0, q.Length > 0 ? "No symbols match \"" + q + "\"" : _symbols.Count == 0 && _everSet ? "No classes, functions or hotkeys" : "Open a script to see its outline", "", null));
        _tree.EndUpdate();
    }

    static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> acc)
    {
        foreach (TreeNode n in nodes) { if (n.IsExpanded) acc.Add(n.Name); CollectExpanded(n.Nodes, acc); }
    }

    bool Add(TreeNodeCollection into, List<Symbol> syms, string q, HashSet<string> expanded, string parentKey, List<TreeNode> toExpand, bool first)
    {
        bool any = false;
        foreach (var s in syms)
        {
            bool self = q.Length == 0 || s.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            string suffix = s.Kind == SymbolKind.Include ? "" : s.Line.ToString();
            var rn = new RichNode("", Color.Empty, s.Name, (s.Detail != null && s.Kind != SymbolKind.Include ? s.Detail + "  " : "") + suffix, s)
            {
                ImageIndex = (int)s.Kind, SelectedImageIndex = (int)s.Kind, Name = parentKey + "/" + s.Kind + ":" + s.Name
            };
            bool kids = Add(rn.Nodes, s.Children, self && s.Kind != SymbolKind.Include ? "" : q, expanded, rn.Name, toExpand, first);
            if (!self && !kids) continue;
            into.Add(rn);
            any = true;
            if (q.Length > 0 && kids || expanded.Contains(rn.Name) || (first && s.Kind == SymbolKind.Class && parentKey.Length == 0)) toExpand.Add(rn);
        }
        return any;
    }

    void Go(RichNode rn)
    {
        var s = rn == null ? null : rn.Payload as Symbol;
        if (s == null) return;
        if (s.Kind == SymbolKind.Include) { _host.Navigate(s.Detail, 1, 1); return; }
        _host.Navigate(s.File, s.Line, s.Column, s.Line, s.Column + (s.Kind == SymbolKind.Class ? 6 + s.Name.Length : s.Name.Length));
    }

    /// <summary>All symbols flattened (for Go to symbol).</summary>
    public IEnumerable<Symbol> AllSymbols()
    {
        var st = new Stack<Symbol>(_symbols.AsEnumerable().Reverse());
        while (st.Count > 0)
        {
            var s = st.Pop();
            yield return s;
            for (int i = s.Children.Count - 1; i >= 0; i--) st.Push(s.Children[i]);
        }
    }
}
