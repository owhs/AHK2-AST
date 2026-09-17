// What the UI derives from a parsed tree: diagnostics with real file/line positions, the outline (classes,
// functions, hotkeys...), source spans of nodes, and one-line code previews for the AST view.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal enum SymbolKind { Class, Method, Property, Field, Function, Hotkey, Hotstring, Label, Include, Global }

internal class Symbol
{
    public SymbolKind Kind;
    public string Name;
    public string Detail;
    public string File;
    public int Line, Column;
    public AstNode Node;
    public List<Symbol> Children = new List<Symbol>();
}

internal class ParseResult
{
    public AstNode Root;
    public string MainFile;      // null for an unsaved script
    public string Text;
    public int Version;
    public int NodeCount, Depth;
    public long Ms;
    public List<Diagnostic> Diagnostics = new List<Diagnostic>();
    public List<Symbol> Symbols = new List<Symbol>();
    public List<string> Includes = new List<string>();
    public Exception Failure;
}

internal static class AstModel
{
    static readonly Regex AtLine = new Regex(@"at line (\d+):(\d+)", RegexOptions.Compiled);

    public static ParseResult Analyze(AstNode root, string mainFile, string text, int version, long ms)
    {
        var r = new ParseResult { Root = root, MainFile = mainFile, Text = text, Version = version, Ms = ms };
        int count = 0, depth = 0;
        Count(root, 0, ref count, ref depth);
        r.NodeCount = count; r.Depth = depth;
        CollectDiagnostics(root, mainFile, null, r.Diagnostics);
        r.Symbols = BuildOutline(root, mainFile);
        CollectIncludes(root, r.Includes);
        return r;
    }

    static void Count(AstNode n, int d, ref int count, ref int depth)
    {
        if (n == null) return;
        count++;
        if (d > depth) depth = d;
        for (int i = 0; i < n.ChildCount; i++) Count(n.GetChild(i), d + 1, ref count, ref depth);
    }

    static void CollectIncludes(AstNode n, List<string> acc)
    {
        if (n == null) return;
        if (n.NodeType == "Include" && !string.IsNullOrEmpty(n.Value) && File.Exists(n.Value) && !acc.Contains(n.Value, StringComparer.OrdinalIgnoreCase))
            acc.Add(n.Value);
        for (int i = 0; i < n.ChildCount; i++) CollectIncludes(n.GetChild(i), acc);
    }

    /// <summary>The file a node's positions refer to: its nearest enclosing Include, else the main file.</summary>
    public static string FileOf(AstNode n, string mainFile)
    {
        for (var p = n == null ? null : n.Parent; p != null; p = p.Parent)
            if (p.NodeType == "Include" && !string.IsNullOrEmpty(p.Value) && p.ChildCount > 0) return p.Value;
        return mainFile;
    }

    static void CollectDiagnostics(AstNode n, string file, AstNode includeNode, List<Diagnostic> acc)
    {
        if (n == null) return;
        if (n.NodeType == "Error" || n.NodeType == "Warning")
        {
            var d = new Diagnostic { IsError = n.NodeType == "Error", Message = n.Value ?? "", File = file, Line = n.Line, Column = n.Column };
            if (d.Line <= 0)
            {
                var m = AtLine.Match(d.Message);
                if (m.Success) { d.Line = int.Parse(m.Groups[1].Value); d.Column = int.Parse(m.Groups[2].Value); }
                else if (includeNode != null)
                {
                    // e.g. "Circular include" inside an included file: point at the #Include line in the parent
                    d.File = FileOf(includeNode, file == null ? null : file) ?? d.File;
                    d.Line = includeNode.Line; d.Column = includeNode.Column;
                }
            }
            if (d.Message.StartsWith("[")) { } // keep the engine's "[file] ..." prefix as-is
            acc.Add(d);
        }
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c != null && c.NodeType == "Include" && !string.IsNullOrEmpty(c.Value))
            {
                // children of an Include carry positions inside that file
                for (int j = 0; j < c.ChildCount; j++) CollectDiagnostics(c.GetChild(j), c.Value, c, acc);
                if (c.ChildCount == 0) CollectDiagnostics(c, file, includeNode, acc);
            }
            else CollectDiagnostics(c, file, includeNode, acc);
        }
    }

    // ── Outline ──────────────────────────────────────────────────────────────────────────────────────

    public static List<Symbol> BuildOutline(AstNode root, string mainFile)
    {
        var list = new List<Symbol>();
        if (root != null) OutlineChildren(root, mainFile, list, false);
        return list;
    }

    static void OutlineChildren(AstNode parent, string file, List<Symbol> acc, bool inClass)
    {
        for (int i = 0; i < parent.ChildCount; i++)
        {
            var n = parent.GetChild(i);
            if (n == null) continue;
            switch (n.NodeType)
            {
                case "Class":
                {
                    var s = Sym(inClass ? SymbolKind.Class : SymbolKind.Class, n.Value, file, n);
                    var ext = FirstChild(n, "Extends");
                    if (ext != null) s.Detail = "extends " + ext.Value;
                    OutlineChildren(n, file, s.Children, true);
                    acc.Add(s);
                    break;
                }
                case "Method":
                {
                    var s = Sym(inClass ? SymbolKind.Method : SymbolKind.Function, n.Value, file, n);
                    s.Detail = ParamList(n);
                    if (!string.IsNullOrEmpty(n.Metadata) && n.Metadata.Contains("static")) s.Detail = "static " + s.Detail;
                    acc.Add(s);
                    break;
                }
                case "Property":
                    if (inClass) acc.Add(Sym(SymbolKind.Property, n.Value, file, n));
                    break;
                case "StaticAssign":
                    if (inClass)
                    {
                        var s = Sym(SymbolKind.Field, n.Value, file, n);
                        if (n.Metadata == "static") s.Detail = "static";
                        acc.Add(s);
                    }
                    break;
                case "Hotkey":
                    acc.Add(Sym(SymbolKind.Hotkey, n.Value, file, n));
                    break;
                case "Hotstring":
                    acc.Add(Sym(SymbolKind.Hotstring, n.Value, file, n));
                    break;
                case "Label":
                    acc.Add(Sym(SymbolKind.Label, n.Value + ":", file, n));
                    break;
                case "Declaration":
                    if (!inClass && n.Metadata == "global") acc.Add(Sym(SymbolKind.Global, n.Value, file, n));
                    break;
                case "Include":
                {
                    if (string.IsNullOrEmpty(n.Value)) break;
                    var s = Sym(SymbolKind.Include, Path.GetFileName(n.Value), file, n);
                    s.Detail = n.Value;
                    OutlineChildren(n, n.Value, s.Children, false);
                    acc.Add(s);
                    break;
                }
                case "Directive":
                    break;
                default:
                    // definitions nested in blocks under #HotIf / if / hotkey bodies are still worth listing
                    if (!inClass && (n.NodeType == "Block" || n.NodeType == "If" || n.NodeType == "Else")) OutlineChildren(n, file, acc, false);
                    break;
            }
        }
    }

    static Symbol Sym(SymbolKind k, string name, string file, AstNode n)
    {
        return new Symbol { Kind = k, Name = name ?? "", File = file, Line = n.Line, Column = n.Column, Node = n };
    }

    static AstNode FirstChild(AstNode n, string type)
    {
        for (int i = 0; i < n.ChildCount; i++) if (n.GetChild(i) != null && n.GetChild(i).NodeType == type) return n.GetChild(i);
        return null;
    }

    static string ParamList(AstNode method)
    {
        var ps = FirstChild(method, "Parameters");
        if (ps == null) return "()";
        var names = new List<string>();
        for (int i = 0; i < ps.ChildCount; i++)
        {
            var p = ps.GetChild(i);
            if (p == null) continue;
            string s = p.Value ?? "";
            if (p.NodeType == "Variadic") s = (s.Length > 0 ? s : "") + "*";
            if (!string.IsNullOrEmpty(p.Metadata) && p.Metadata.Contains("byref")) s = "&" + s;
            if (p.ChildCount > 0) s += "?";
            names.Add(s);
        }
        return "(" + string.Join(", ", names.ToArray()) + ")";
    }

    // ── Spans and lookup ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The source range of a node, from its own and its descendants' token positions (the engine stores start
    /// positions only). Nested Include subtrees belong to other files and are skipped.
    /// </summary>
    public static void Span(AstNode n, out int l0, out int c0, out int l1, out int c1)
    {
        l0 = int.MaxValue; c0 = int.MaxValue; l1 = 0; c1 = 0;
        SpanRec(n, ref l0, ref c0, ref l1, ref c1, true);
        if (l0 == int.MaxValue) { l0 = n.Line; c0 = n.Column; l1 = n.Line; c1 = n.Column; }
    }

    static void SpanRec(AstNode n, ref int l0, ref int c0, ref int l1, ref int c1, bool top)
    {
        if (n == null) return;
        if (!top && n.NodeType == "Include") return;
        if (n.Line > 0)
        {
            if (n.Line < l0 || (n.Line == l0 && n.Column < c0)) { l0 = n.Line; c0 = n.Column; }
            int len = TokenLength(n);
            int el = n.Line, ec = n.Column + len;
            if (n.EndLine > el) { el = n.EndLine; ec = n.EndColumn > 0 ? n.EndColumn : 2; }
            if (el > l1 || (el == l1 && ec > c1)) { l1 = el; c1 = ec; }
        }
        for (int i = 0; i < n.ChildCount; i++) SpanRec(n.GetChild(i), ref l0, ref c0, ref l1, ref c1, false);
    }

    static int TokenLength(AstNode n)
    {
        string v = n.Value;
        switch (n.NodeType)
        {
            case "Identifier": case "Number": case "Member": case "Parameter": case "KeyName":
                return string.IsNullOrEmpty(v) ? 1 : (n.NodeType == "Member" ? 0 : v.Length);
            case "String":
                if (string.IsNullOrEmpty(v) || v.IndexOf('\n') >= 0) return 1;
                if (n.Metadata != null && n.Metadata.StartsWith("raw:")) return n.Metadata.Length - 4;
                return v.Length;
            case "This": return 4;
            case "Super": return 5;
            case "Comment": case "Directive": case "Hotstring":
                return string.IsNullOrEmpty(v) || v.IndexOf('\n') >= 0 ? 1 : v.Length;
            case "Class": case "Method": case "Property": case "Label": case "Hotkey":
                return string.IsNullOrEmpty(v) ? 1 : v.Length;
            default:
                return 1;
        }
    }

    /// <summary>The deepest node of `file` (null = main) whose span contains the 1-based position.</summary>
    public static AstNode FindAt(AstNode root, string mainFile, string file, int line, int col)
    {
        if (root == null) return null;
        AstNode scope = root;
        if (file != null && mainFile != null && !string.Equals(file, mainFile, StringComparison.OrdinalIgnoreCase))
        {
            scope = FindInclude(root, file);
            if (scope == null) return null;
        }
        AstNode best = null;
        Descend(scope, line, col, ref best, true);
        return best;
    }

    static AstNode FindInclude(AstNode n, string file)
    {
        if (n == null) return null;
        if (n.NodeType == "Include" && n.ChildCount > 0 && string.Equals(n.Value, file, StringComparison.OrdinalIgnoreCase)) return n;
        for (int i = 0; i < n.ChildCount; i++)
        {
            var f = FindInclude(n.GetChild(i), file);
            if (f != null) return f;
        }
        return null;
    }

    static void Descend(AstNode n, int line, int col, ref AstNode best, bool top)
    {
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c == null || c.NodeType == "Include" && c.ChildCount > 0) continue;
            int l0, c0, l1, c1;
            Span(c, out l0, out c0, out l1, out c1);
            bool after = line > l0 || (line == l0 && col >= c0);
            bool before = line < l1 || (line == l1 && col <= c1);
            if (after && before)
            {
                best = c;
                Descend(c, line, col, ref best, false);
                return;
            }
        }
    }

    // ── Labels ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>For statements with a body: the keyword and what precedes the body (`if x > 1`), else null.</summary>
    static string HeadOnly(AstNode n)
    {
        int body = -1;
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c != null && (c.NodeType == "Block" || c.NodeType == "CaseBody" || c.NodeType == "DefaultBody" || c.NodeType == "Else"
                || c.NodeType == "Catch" || c.NodeType == "Finally" || c.NodeType == "Case" || c.NodeType == "Default")) { body = i; break; }
        }
        if (body < 0) return null;
        var parts = new List<string> { n.NodeType.ToLowerInvariant() };
        for (int i = 0; i < body; i++)
        {
            var c = n.GetChild(i);
            if (c == null) continue;
            if (c.NodeType == "ForVars")
            {
                var vars = new List<string>();
                for (int j = 0; j < c.ChildCount; j++) vars.Add(c.GetChild(j) == null ? "" : c.GetChild(j).Value);
                parts.Add(string.Join(", ", vars.ToArray()) + " in");
                continue;
            }
            try { parts.Add(AstEmitter.Emit(c, new EmitOptions { EmitComments = false }, 0)); } catch { }
        }
        if (n.Value != null && n.NodeType != "If" && parts.Count == 1) parts.Add(n.Value);
        return string.Join(" ", parts.ToArray());
    }

    /// <summary>A one-line preview of the code a node stands for (clipped).</summary>
    public static string Snippet(AstNode n, int max = 90)
    {
        if (n == null) return "";
        string s;
        switch (n.NodeType)
        {
            case "Program": return "";
            case "Include": return n.Value ?? "";
            case "Comment": s = n.Value ?? ""; break;
            case "Class":
            {
                var ext = FirstChild(n, "Extends");
                return "class " + n.Value + (ext != null ? " extends " + ext.Value : "");
            }
            case "Method": return (n.Value ?? "") + ParamList(n);
            case "Block": case "Parameters": case "CaseBody": case "DefaultBody": return "";
            case "Hotkey": return (n.Value ?? "") + "::";
            case "Property": return n.Value ?? "";
            default:
                s = HeadOnly(n);
                if (s == null)
                {
                    try { s = AstEmitter.Emit(n, new EmitOptions { EmitComments = false }, 0); }
                    catch { s = n.Value ?? ""; }
                }
                break;
        }
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        if (s.Length > max) s = s.Substring(0, max - 1) + "…";
        return s;
    }
}
