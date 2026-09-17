using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public static class AstEmitter
{
    // Thread-static options for the current emit pass
    [ThreadStatic] private static EmitOptions _opts;

    /// <summary>Build indentation string for the given level using current options.</summary>
    private static string MakePad(int indent)
    {
        if (indent <= 0) return "";
        if (_opts != null && _opts.UseTabs)
            return new string('\t', indent);
        int size = _opts != null && _opts.IndentSize > 0 ? _opts.IndentSize : 4;
        return new string(' ', indent * size);
    }

    public static string Emit(AstNode node)
    {
        return Emit(node, new EmitOptions());
    }

    public static string Emit(AstNode node, EmitOptions options)
    {
        return Emit(node, options, 0);
    }

    public static string Emit(AstNode node, EmitOptions options, int initialIndent)
    {
        _opts = options ?? new EmitOptions();
        var outer = _heads;
        var outerGroups = _leadingGroups;
        using (Prof.Time("emit.heads")) StatementHead.CollectBoth(node, out _heads, out _leadingGroups); // one walk for both
        var outerMax = _maxLine;
        _maxLine = new Dictionary<AstNode, int>();
        try { return EmitNode(node, initialIndent); }
        finally { _heads = outer; _leadingGroups = outerGroups; _maxLine = outerMax; }
    }

    // Leading operands of statements that AHK would otherwise read as a function-call statement (see StatementHead).
    [ThreadStatic] private static HashSet<AstNode> _heads;
    // Groups opening a statement line: always written on one line (a lone ( line starts a continuation section).
    [ThreadStatic] private static HashSet<AstNode> _leadingGroups;

    /// <summary>
    /// Writes a string literal whose text contains line feeds (from a continuation section) back as a continuation
    /// section that reproduces exactly the same value under AHK's section rules (see ContinuationJoiner): LTrim0
    /// when the first line is indented, RTrim0 when a line has trailing blanks. A line starting with `)` can't live
    /// in a section, so that case falls back to a one-line literal with `n escapes (and `; where a blank precedes ;).
    /// </summary>
    public static string MultilineStringLiteral(string literal)
    {
        string lit = literal.Replace("\r\n", "\n").Replace('\r', '\n');
        char q = lit.Length >= 2 && (lit[0] == '"' || lit[0] == '\'') && lit[lit.Length - 1] == lit[0] ? lit[0] : '\0';
        string inner = q != '\0' ? lit.Substring(1, lit.Length - 2) : lit;
        string[] lines = inner.Split('\n');
        bool unsafeLine = false, trailing = false;
        foreach (string l in lines)
        {
            if (l.TrimStart(' ', '\t').StartsWith(")")) unsafeLine = true;
            if (l.EndsWith(" ") || l.EndsWith("\t")) trailing = true;
        }
        if (unsafeLine)
        {
            var one = new StringBuilder();
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '\n') one.Append("`n");
                else if (c == ';' && i > 0 && (inner[i - 1] == ' ' || inner[i - 1] == '\t')) one.Append("`;");
                else one.Append(c);
            }
            return q != '\0' ? q + one.ToString() + q : one.ToString();
        }
        var opts = new List<string>();
        if (lines[0].Length > 0 && (lines[0][0] == ' ' || lines[0][0] == '\t')) opts.Add("LTrim0");
        if (trailing) opts.Add("RTrim0");
        string open = "(" + (opts.Count > 0 ? " " + string.Join(" ", opts) : "");
        return q != '\0' ? q + "\n" + open + "\n" + inner + "\n)" + q : open + "\n" + inner + "\n)";
    }

    static readonly HashSet<string> CompoundAssignOps = new HashSet<string>
    {
        "+=", "-=", "*=", "/=", "//=", ".=", "|=", "&=", "^=", "<<=", ">>=", ">>>=", "??="
    };

    /// <summary>
    /// True when a declaration's initializer is `name op= expr` on the declared name itself
    /// (`global Counter += delta`); such an initializer is emitted as-is instead of `name := ...`.
    /// </summary>
    public static bool IsCompoundDeclarationInit(AstNode decl, AstNode init)
    {
        return init != null && init.NodeType == "BinaryExpr" && CompoundAssignOps.Contains(init.Value ?? "")
            && init.ChildCount == 2 && init.GetChild(0) != null && init.GetChild(0).NodeType == "Identifier"
            && string.Equals(init.GetChild(0).Value, decl.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeEmitChild(AstNode node, int index, int indent)
    {
        if (node == null || index < 0 || index >= node.ChildCount)
            return "";
        var child = node.GetChild(index);
        return child != null ? EmitNode(child, indent) : "";
    }

    private static string EmitNode(AstNode node, int indent)
    {
        if (node == null) return "";
        if (IsWrittenExpr(node.NodeType))
        {
            var sb = new StringBuilder();
            Write(sb, node, indent);
            return sb.ToString();
        }
        if (_heads != null && _heads.Contains(node)) return "(" + EmitNodeCore(node, 0) + ")";
        return EmitNodeCore(node, indent);
    }

    // ---- expressions: written into one StringBuilder ---------------------------------------------------------------
    // Expression nodes are the bulk of a tree. Each used to return a string for its parent to concatenate (one or more
    // throwaway strings per node); now a whole expression is appended into a single builder.

    private static bool IsWrittenExpr(string type)
    {
        switch (type)
        {
            case "BinaryExpr": case "UnaryExpr": case "PostfixExpr": case "UnsetModifier": case "Call": case "Arguments":
            case "Member": case "Index": case "Number": case "Identifier": case "String": case "This":
            case "Array": case "Object": case "Ternary":
                return true;
        }
        return false;
    }

    private static void Write(StringBuilder sb, AstNode node, int indent)
    {
        if (node == null) return;
        bool wrap = _heads != null && _heads.Contains(node); // a statement head AHK would misread (StatementHead)
        if (wrap) { sb.Append('('); indent = 0; }
        if (IsWrittenExpr(node.NodeType)) WriteExpr(sb, node, indent);
        else sb.Append(EmitNodeCore(node, indent));
        if (wrap) sb.Append(')');
    }

    private static void WriteChild(StringBuilder sb, AstNode node, int index, int indent)
    {
        if (node != null && index >= 0 && index < node.ChildCount) Write(sb, node.GetChild(index), indent);
    }

    private static void WriteExpr(StringBuilder sb, AstNode node, int indent)
    {
        switch (node.NodeType)
        {
            case "BinaryExpr":
                WriteChild(sb, node, 0, 0);
                sb.Append(' ').Append(node.Value).Append(' ');
                WriteChild(sb, node, 1, indent);
                return;

            case "UnaryExpr":
            {
                string op = node.Value;
                sb.Append(op);
                if (!string.IsNullOrEmpty(op) && char.IsLetter(op[0])) sb.Append(' '); // `not x`
                WriteChild(sb, node, 0, 0);
                return;
            }

            case "PostfixExpr":
                WriteChild(sb, node, 0, 0);
                sb.Append(node.Value);
                return;

            case "UnsetModifier":
                WriteChild(sb, node, 0, 0);
                sb.Append('?');
                return;

            case "Call":
                WriteChild(sb, node, 0, 0);
                WriteChild(sb, node, 1, 0);
                return;

            case "Arguments":
            {
                if (node.Metadata != "command")
                {
                    sb.Append('(');
                    for (int i = 0; i < node.ChildCount; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        Write(sb, node.GetChild(i), 0);
                    }
                    sb.Append(')');
                    return;
                }
                // Function-call statement: `Name a, b`, `Name ,, c`, or bare `Name`.
                if (node.ChildCount == 0) return;
                sb.Append(' ');
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var arg = node.GetChild(i);
                    int sepAt = -1;
                    if (i > 0) { sepAt = sb.Length; sb.Append(", "); }
                    int before = sb.Length;
                    if (arg != null && arg.NodeType != "Omitted") Write(sb, arg, 0);
                    if (sepAt >= 0 && sb.Length == before) sb.Remove(sepAt + 1, 1); // an omitted argument: `,` alone
                }
                return;
            }

            case "Member":
                WriteChild(sb, node, 0, 0);
                sb.Append('.').Append(node.Value);
                return;

            case "Index":
                WriteChild(sb, node, 0, 0);
                sb.Append('[');
                for (int i = 1; i < node.ChildCount; i++)
                {
                    if (i > 1) sb.Append(", ");
                    Write(sb, node.GetChild(i), 0);
                }
                sb.Append(']');
                return;

            case "Number":
            case "Identifier":
                sb.Append(node.Value);
                return;

            case "String":
            {
                if (node.Metadata != null && node.Metadata.StartsWith("raw:")) { sb.Append(node.Metadata, 4, node.Metadata.Length - 4); return; }
                string val = node.Value;
                if (!string.IsNullOrEmpty(val) && (val.IndexOf('\n') >= 0 || val.IndexOf('\r') >= 0)) sb.Append(MultilineStringLiteral(val));
                else sb.Append(val);
                return;
            }

            case "This":
                sb.Append("this");
                return;

            case "Array":
                sb.Append('[');
                for (int i = 0; i < node.ChildCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Write(sb, node.GetChild(i), 0);
                }
                sb.Append(']');
                return;

            case "Object":
                sb.Append('{');
                for (int i = 0; i < node.ChildCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var kv = node.GetChild(i);
                    WriteChild(sb, kv, 0, 0);
                    sb.Append(": ");
                    WriteChild(sb, kv, 1, 0);
                }
                sb.Append('}');
                return;

            case "Ternary":
                WriteChild(sb, node, 0, 0);
                sb.Append(" ? ");
                WriteChild(sb, node, 1, 0);
                sb.Append(" : ");
                WriteChild(sb, node, 2, 0);
                return;
        }
    }

    private static string EmitNodeCore(AstNode node, int indent)
    {
        if (IsWrittenExpr(node.NodeType))
        {
            var wsb = new StringBuilder();
            WriteExpr(wsb, node, indent);
            return wsb.ToString();
        }
        string pad = MakePad(indent);

        switch (node.NodeType)
        {
            case "Program":
                return EmitChildren(node.ChildNodes, 0);

            case "Directive":
                if (IsParsedHotIf(node))
                {
                    string comment = node.Metadata.Length > 6 ? "  " + node.Metadata.Substring(6) : "";
                    return "#HotIf " + EmitNode(node.GetChild(0), 0) + comment;
                }
                return node.Value;

            case "Class":
            {
                string ext = "";
                var extendsNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Extends");
                if (extendsNode != null)
                    ext = " extends " + extendsNode.Value;
                var bodyChildren = node.ChildNodes.Where(c => c != null && c.NodeType != "Extends").ToArray();
                string body = EmitChildren(bodyChildren, indent + 1);
                return string.Format("{0}class {1}{2} {{\n{3}\n{0}}}", pad, node.Value, ext, body);
            }

            case "Method":
            {
                string stat = node.Metadata == "static" ? "static " : "";
                string paramStr = "";
                string body = "";
                if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Parameters")
                {
                    var paramNode = node.GetChild(0);
                    bool isAccessor = (node.Value == "get" || node.Value == "set");
                    if (isAccessor && paramNode.ChildCount == 0)
                    {
                        paramStr = "";
                    }
                    else
                    {
                        paramStr = SafeEmitChild(node, 0, 0);
                    }
                    body = node.ChildCount > 1 ? SafeEmitChild(node, 1, indent) : "{\n" + pad + "}";
                }
                else
                {
                    paramStr = "";
                    body = node.ChildCount > 0 ? SafeEmitChild(node, 0, indent) : "{\n" + pad + "}";
                }
                return string.Format("{0}{1}{2}{3} {4}", pad, stat, node.Value, paramStr, body);
            }

            case "Parameters":
                return "(" + string.Join(", ", node.ChildNodes.Select(c => EmitNode(c, 0))) + ")";

            case "Block":
            {
                string stmts = EmitChildren(node.ChildNodes, indent + 1);
                if (string.IsNullOrEmpty(stmts))
                {
                    return "{\n" + pad + "}";
                }
                return "{\n" + stmts + "\n" + pad + "}";
            }

            case "If":
            {
                string cond = node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "true";
                var bodyNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                string body = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        body = " " + EmitNode(bodyNode, indent);
                    else
                        body = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                string elseStr = "";
                if (node.ChildCount > 2 && node.GetChild(2) != null && node.GetChild(2).NodeType == "Else")
                {
                    var elseNode = node.GetChild(2);
                    elseStr = "\n" + EmitNode(elseNode, indent);
                }
                return pad + "if " + cond + body + elseStr;
            }

            case "While":
            {
                string cond = SafeEmitChild(node, 0, 0);
                var bodyNode = node.ChildCount > 1 ? node.GetChild(1) : null;
                string body = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        body = " " + EmitNode(bodyNode, indent);
                    else
                        body = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                return pad + "while " + cond + body + LoopTail(node, 2, indent);
            }

            case "Return":
                return pad + "return" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0, 0) : "");

            case "FatArrow":
                // `x => e` written without parentheses stays so (a single plain parameter only)
                if (node.Metadata == "bare" && node.ChildCount > 0 && node.GetChild(0).NodeType == "Parameters" && node.GetChild(0).ChildCount == 1
                    && string.IsNullOrEmpty(node.GetChild(0).GetChild(0).Metadata) && node.GetChild(0).GetChild(0).ChildCount == 0)
                    return node.GetChild(0).GetChild(0).Value + " => " + SafeEmitChild(node, 1, 0);
                return SafeEmitChild(node, 0, 0) + " => " + SafeEmitChild(node, 1, 0);

            case "Grouped":
            {
                var exprNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                // A group the parser made to record AHK's grouping of unparenthesised source (`a && b := 1`) is
                // written as the source had it, wherever AHK would group it the same way again.
                if (node.Metadata == "implicit" && exprNode != null && node.ChildCount == 1 && node.Parent != null
                    && ((node.Parent.NodeType == "BinaryExpr" && node.Parent.ChildCount == 2 && node.Parent.GetChild(1) == node)
                        || node.Parent.NodeType == "UnaryExpr"
                        || (node.Parent.NodeType == "Concat" && node.Parent.ChildCount > 0 && node.Parent.GetChild(0) != node)))
                    return EmitNode(exprNode, 0);
                var commentNodes = node.ChildNodes.Skip(1).Where(c => c.NodeType == "Comment").ToArray();
                bool isMultiLine = ((node.EndLine > node.Line) || (commentNodes.Length > 0))
                    && !(_leadingGroups != null && _leadingGroups.Contains(node));

                if (isMultiLine)
                {
                    string closePad = MakePad(indent);
                    string commentsText = commentNodes.Length > 0 
                        ? " " + string.Join(" ", commentNodes.Select(c => c.Value)) 
                        : "";
                    
                    // The content starts on the `(` line: a line that starts with `(` and has no `)` would open a
                    // continuation section (that silently regrouped `if (((a) && (b)))`).
                    string innerText = exprNode != null ? EmitNode(exprNode, indent + 1).TrimStart() : "";
                    if (commentsText.Length > 0)
                        return "(" + commentsText + "\n" + MakePad(indent + 1) + innerText + "\n" + closePad + ")";
                    return "(" + innerText + "\n" + closePad + ")";
                }

                return "(" + SafeEmitChild(node, 0, 0) + ")";
            }

            case "Sequence":
            {
                var parts = new List<string>();
                foreach (var child in node.ChildNodes)
                    parts.Add(EmitNode(child, 0));
                return string.Join(", ", parts.ToArray());
            }

            case "Error":
                return pad + "; ERROR: " + node.Value;

            case "Warning":
                // a warning read back from an emitted comment is written exactly as it was
                if (node.Metadata != null && node.Metadata.StartsWith("raw:")) return node.Metadata.Substring(4);
                return "; WARNING: " + node.Value;

            case "StaticAssign":
            {
                var sab = new StringBuilder();
                sab.Append(pad + (node.Metadata == "static" ? "static " : ""));
                sab.Append(node.Value + " := ");
                // First child is the value, rest are comma-chained assigns
                if (node.ChildCount > 0)
                {
                    sab.Append(SafeEmitChild(node, 0, 0));
                    for (int ci = 1; ci < node.ChildCount; ci++)
                    {
                        var chainChild = node.GetChild(ci);
                        if (chainChild != null && chainChild.NodeType == "StaticAssign")
                            sab.Append(", " + chainChild.Value + " := " + (chainChild.ChildCount > 0 ? SafeEmitChild(chainChild, 0, 0) : "\"\""));
                        else if (chainChild != null && chainChild.NodeType == "Declaration")
                            sab.Append(", " + chainChild.Value); // chained bare name: no repeated `static`
                        else
                            sab.Append(", " + EmitNode(chainChild, 0));
                    }
                }
                return sab.ToString();
            }

            case "Concat":
            {
                var sb = new StringBuilder();
                string childPad = MakePad(indent);
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child == null) continue;

                    if (indent > 0 && HasGroupedAncestor(node))
                    {
                        string part = EmitNode(child, indent).TrimStart();
                        string first = part.Split('\n')[0];
                        // never start a line with an unclosed `(` (continuation section): keep it on the previous line
                        if (sb.Length > 0 && first.StartsWith("(") && first.IndexOf(')') < 0) sb.Append(" ").Append(part);
                        else
                        {
                            if (sb.Length > 0) sb.Append("\n");
                            sb.Append(childPad).Append(part);
                        }
                    }
                    else
                    {
                        string emitted = EmitNode(child, 0);
                        if (sb.Length > 0)
                        {
                            string sep = node.Metadata == "nospace" ? "" : " ";
                            sb.Append(sep);
                        }
                        sb.Append(emitted);
                    }
                }
                return sb.ToString();
            }

            case "Include":
            {
                if (_opts != null && _opts.PreserveIncludes)
                {
                    return pad + (string.IsNullOrEmpty(node.Metadata) ? "; include" : node.Metadata);
                }
                if (node.ChildCount > 0)
                {
                    // no comments wanted: no begin/end marker comments either, the inlined code simply follows
                    if (_opts != null && !_opts.EmitComments) return EmitChildren(node.ChildNodes, indent);
                    string children = EmitChildren(node.ChildNodes, indent);
                    string fileName = !string.IsNullOrEmpty(node.Value)
                        ? System.IO.Path.GetFileName(node.Value) : "unknown";
                    return pad + "; --- begin: " + fileName + " ---\n"
                        + children + "\n"
                        + pad + "; --- end: " + fileName + " ---";
                }
                if (_opts != null && !_opts.PreserveIncludes)
                {
                    if (!string.IsNullOrEmpty(node.Metadata) && !node.Metadata.TrimStart().StartsWith(";"))
                    {
                        return pad + "; duplicate include: " + (!string.IsNullOrEmpty(node.Value) ? System.IO.Path.GetFileName(node.Value) : "unknown");
                    }
                }
                return pad + (string.IsNullOrEmpty(node.Metadata) ? "; include" : node.Metadata);
            }

            case "For":
            {
                string fvars = node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "";
                string fcoll = node.ChildCount > 1 ? SafeEmitChild(node, 1, 0) : "";
                var bodyNode = node.ChildCount > 2 ? node.GetChild(2) : null;
                string body = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        body = " " + EmitNode(bodyNode, indent);
                    else
                        body = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                if (node.Metadata == "paren") return pad + "for (" + fvars + " in " + fcoll + ")" + body + LoopTail(node, 3, indent);
                return pad + "for " + fvars + " in " + fcoll + body + LoopTail(node, 3, indent);
            }

            case "ForVars":
            {
                // Omitted slots emit as nothing: `k, v,` / `, v` / `k,, v`.
                var fsb = new StringBuilder();
                var slots = node.ChildNodes;
                for (int i = 0; i < slots.Length; i++)
                {
                    string s = slots[i] == null || slots[i].NodeType == "Omitted" ? "" : EmitNode(slots[i], 0);
                    if (i > 0) fsb.Append(s.Length > 0 ? ", " : ",");
                    fsb.Append(s);
                }
                return fsb.ToString();
            }

            case "Loop":
            {
                string variant = !string.IsNullOrEmpty(node.Value) ? " " + node.Value + (node.Metadata == "comma" ? "," : "") : "";
                var nonUntilChildren = node.ChildNodes.Where(c => c != null && c.NodeType != "Until" && c.NodeType != "Else").ToList();
                AstNode lbody = null;
                var args = new List<AstNode>();
                if (nonUntilChildren.Count > 0)
                {
                    lbody = nonUntilChildren[nonUntilChildren.Count - 1];
                    for (int j = 0; j < nonUntilChildren.Count - 1; j++)
                    {
                        args.Add(nonUntilChildren[j]);
                    }
                }
                string argsStr = args.Count > 0 ? " " + string.Join(", ", args.Select(c => EmitNode(c, 0))) : "";
                string body = "";
                if (lbody != null)
                {
                    if (lbody.NodeType == "Block")
                        body = " " + EmitNode(lbody, indent);
                    else
                        body = "\n" + IndentedBody(lbody, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                return pad + "loop" + variant + argsStr + body + LoopTail(node, 0, indent);
            }

            case "MultiStatement":
                return pad + string.Join(", ", node.ChildNodes.Select(c => EmitNode(c, 0)));

            case "Omitted":
                return "";

            case "Until":
                return pad + "until " + (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "");

            case "Switch":
            {
                var sexpr = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType != "Case" && c.NodeType != "Default");
                string scases = string.Join("\n", node.ChildNodes
                    .Where(c => c != null && (c.NodeType == "Case" || c.NodeType == "Default"))
                    .Select(c => EmitNode(c, indent + 1)));
                string csFlag = !string.IsNullOrEmpty(node.Metadata) ? ", " + node.Metadata : "";
                return pad + "switch" + (sexpr != null ? " " + EmitNode(sexpr, 0) : "") + csFlag + " {\n" + scases + "\n" + pad + "}";
            }

            case "Case":
            {
                var values = new List<string>();
                for (int ci = 0; ci < node.ChildCount - 1; ci++)
                {
                    values.Add(SafeEmitChild(node, ci, 0));
                }
                string valStr = string.Join(", ", values);
                string bodyStr = node.ChildCount > 0 ? "\n" + SafeEmitChild(node, node.ChildCount - 1, indent + 1) : "";
                return pad + "case " + valStr + ":" + bodyStr;
            }

            case "Try":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string tbody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        tbody = " " + EmitNode(bodyNode, indent);
                    else
                        tbody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    tbody = " {\n" + pad + "}";
                }
                string trest = string.Join("\n", node.ChildNodes.Skip(1).Select(c => EmitNode(c, indent)));
                return pad + "try" + tbody + (trest.Length > 0 ? "\n" + trest : "");
            }

            case "Catch":
            {
                string ctype = !string.IsNullOrEmpty(node.Value) ? " " + node.Value : "";
                string cvar = !string.IsNullOrEmpty(node.Metadata) ? " as " + node.Metadata : "";
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string cbody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        cbody = " " + EmitNode(bodyNode, indent);
                    else
                        cbody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    cbody = " {\n" + pad + "}";
                }
                return pad + "catch" + ctype + cvar + cbody;
            }

            case "Finally":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string fbody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        fbody = " " + EmitNode(bodyNode, indent);
                    else
                        fbody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    fbody = " {\n" + pad + "}";
                }
                return pad + "finally" + fbody;
            }

            case "Throw":
                return pad + "throw " + (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "");

            case "Break":
                return pad + "break" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0, 0) : "");

            case "Continue":
                return pad + "continue" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0, 0) : "");

            case "Goto":
                return pad + "goto " + node.Value;

            case "New":
                return "new " + (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "");

            case "Remap":
                return pad + node.Value + "::" + (node.ChildCount > 0 && node.GetChild(0) != null ? node.GetChild(0).Value : "");

            case "Hotkey":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string hbody = "";
                if (bodyNode != null)
                {
                    // a colon-key hotkey (`:::`, `+:::`) can't take `{` on its line: AHK reads `+::: {` differently
                    if (bodyNode.NodeType == "Block" && (node.Value ?? "").EndsWith(":"))
                        hbody = "\n" + pad + EmitNode(bodyNode, indent);
                    else if (bodyNode.NodeType == "Block")
                        hbody = " " + EmitNode(bodyNode, indent);
                    else if (node.Metadata == "inline")
                    {
                        string bodyText = EmitNode(bodyNode, 0);
                        if (bodyNode.NodeType == "Identifier")
                            hbody = bodyText;
                        else
                            hbody = " " + bodyText;
                    }
                    else
                        hbody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                return pad + node.Value + "::" + hbody;
            }

            case "Hotstring":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string hbody = "";
                string metadata = node.Metadata ?? "";
                bool isInline = metadata.StartsWith("inline");
                if (bodyNode != null)
                {
                    // `::btw::` ⏎ `{ ... }`: the block must stay on its own line; after `::btw::` on the same line
                    // `{` would be replacement text. (X-option inline actions are code and may share the line.)
                    if (bodyNode.NodeType == "Block" && !isInline)
                        hbody = "\n" + pad + EmitNode(bodyNode, indent);
                    else if (bodyNode.NodeType == "Block")
                        hbody = " " + EmitNode(bodyNode, indent);
                    else if (isInline)
                    {
                        string bodyText = EmitNode(bodyNode, 0);
                        if (bodyNode.NodeType == "Identifier")
                            hbody = bodyText;
                        else
                            hbody = " " + bodyText;
                    }
                    else
                        hbody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                string commentPart = "";
                if (metadata.Contains(";"))
                {
                    int semiIdx = metadata.IndexOf(';');
                    commentPart = " " + metadata.Substring(semiIdx + 1);
                }
                return pad + node.Value + hbody + commentPart;
            }

            case "Declaration":
            {
                string dscope = !string.IsNullOrEmpty(node.Metadata) ? node.Metadata + " " : "";
                var sb = new System.Text.StringBuilder();
                sb.Append(pad).Append(dscope).Append(node.Value);
                int startChainedIdx = 0;
                if (node.ChildCount > 0 && node.GetChild(0).NodeType != "Declaration")
                {
                    var dinit = node.GetChild(0);
                    if (IsCompoundDeclarationInit(node, dinit))
                        sb.Append(" ").Append(dinit.Value).Append(" ").Append(EmitNode(dinit.GetChild(1), 0));
                    else
                        sb.Append(" := ").Append(EmitNode(dinit, 0));
                    startChainedIdx = 1;
                }
                // `global a, b := 1, c` stays one statement: chained names follow on the same line.
                for (int idx = startChainedIdx; idx < node.ChildCount; idx++)
                {
                    var child = node.GetChild(idx);
                    if (child != null && child.NodeType == "Declaration")
                        sb.Append(", ").Append(DeclarationItem(child, false));
                }
                return sb.ToString();
            }

            case "Property":
            {
                string pstat = node.Metadata == "static" ? "static " : "";
                // Indexed property: Name[params] => expr
                if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Parameters")
                {
                    string indexParams = "[" + string.Join(", ", node.GetChild(0).ChildNodes.Select(p => EmitNode(p, 0))) + "]";
                    if (node.ChildCount > 1 && node.GetChild(1) != null && node.GetChild(1).NodeType == "Block")
                        return pad + pstat + node.Value + indexParams + " " + SafeEmitChild(node, 1, indent);
                    if (node.ChildCount > 1)
                        return pad + pstat + node.Value + indexParams + " => " + SafeEmitChild(node, 1, 0);
                    return pad + pstat + node.Value + indexParams;
                }
                if (node.ChildCount > 0 && node.GetChild(0) != null && node.GetChild(0).NodeType == "Block")
                    return pad + pstat + node.Value + " " + SafeEmitChild(node, 0, indent);
                if (node.ChildCount > 0)
                    return pad + pstat + node.Value + " => " + SafeEmitChild(node, 0, 0);
                return pad + pstat + node.Value;
            }

            case "FatArrowBody":
                return "=> " + (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "");

            case "Else":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string ebody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        ebody = " " + EmitNode(bodyNode, indent);
                    else if (bodyNode.NodeType == "If")
                        ebody = " " + EmitNode(bodyNode, indent).TrimStart();
                    else
                        ebody = "\n" + IndentedBody(bodyNode, indent + 1);
                }
                else
                {
                    ebody = " {\n" + pad + "}";
                }
                return pad + "else" + ebody;
            }

            case "Unknown":
                return pad + "; WARNING: unknown construct: " + node.Value;

            case "CaseBody":
            case "DefaultBody":
                return EmitChildren(node.ChildNodes, indent);

            case "Default":
                return pad + "default:" + (node.ChildCount > 0 ? "\n" + SafeEmitChild(node, 0, indent + 1) : "");

            case "KeyValue":
                return (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "") + ": "
                    + (node.ChildCount > 1 ? SafeEmitChild(node, 1, 0) : "");

            case "Comment":
                if (_opts != null && !_opts.EmitComments) return null; // null = skip this node
                // A block comment left open at the end of the source is closed, or it would swallow anything placed
                // after it (appended helpers, following files when inlining).
                if (node.Value != null && node.Value.StartsWith("/*") && !node.Value.TrimEnd().EndsWith("*/"))
                    return pad + node.Value.TrimEnd() + "\n*/";
                return pad + node.Value;

            case "Variadic":
                return (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "") + "*";

            case "Extends":
                return "extends " + node.Value;

            case "Parameter":
            {
                // Variadic discard: (*) - value is already "*", don't double it
                if (node.Value == "*") return "*";
                string meta = node.Metadata ?? "";
                string pref = meta.Contains("byref") ? "&" : "";
                string suff = meta.Contains("variadic") ? "*" : (meta.Contains("optional") ? "?" : "");
                string pdef = node.ChildCount > 0 ? " := " + SafeEmitChild(node, 0, 0) : "";
                return pref + node.Value + suff + pdef;
            }

            case "Super":
                return "super";

            case "Label":
                return pad + node.Value + ":";

            default:
                return pad + "; [" + node.NodeType + "]" + (string.IsNullOrEmpty(node.Value) ? "" : " " + node.Value);
        }
    }

    /// <summary>
    /// Emit a list of child nodes, collapsing inline comments onto the same line
    /// as their preceding sibling when they share the same source line number.
    /// Inserts blank lines between statements when source line gaps exist.
    /// </summary>
    private static string EmitChildren(AstNode[] children, int childIndent)
    {
        if (children == null || children.Length == 0) return "";
        bool wantBlanks = _opts != null && _opts.EmitBlankLines;
        bool wantComments = _opts == null || _opts.EmitComments;

        var lines = new List<string>();
        int lastEmittedLine = -1; // track the deepest source line of last emitted node

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == null) continue; // Safely skip null children

            // Skip comments if disabled
            if (!wantComments && children[i].NodeType == "Comment")
                continue;

            // Insert blank lines to match source gaps (capped at 1)
            if (wantBlanks && lastEmittedLine > 0 && children[i].Line > 0)
            {
                int gap = children[i].Line - lastEmittedLine - 1;
                if (gap > 1) gap = 1; // at most 1 blank line between statements
                if (gap > 0)
                    lines.Add("");
            }

            string emitted;
            if (children[i].NodeType == "Block" && FollowsFunctionHeaderLike(children, i))
            {
                // `f(x)` followed by a line `{` is a function definition to AHK. A bare block has no scope, so a
                // transform's leftover block after such a call is written as its statements.
                emitted = EmitChildren(children[i].ChildNodes, childIndent);
                if (string.IsNullOrEmpty(emitted)) continue;
            }
            else emitted = EmitNode(children[i], childIndent);
            if (emitted == null) continue; // node chose to be skipped (e.g., disabled comment)

            // Ensure statement-level indentation: expression nodes (BinaryExpr, Call, etc.)
            // don't add their own pad since they can be sub-expressions. When used as
            // statements in EmitChildren, we need to prepend indentation if missing.
            if (childIndent > 0 && emitted.Length > 0)
            {
                string expectedPad = MakePad(childIndent);
                if (!emitted.StartsWith(expectedPad))
                    emitted = expectedPad + emitted;
            }

            // Track the deepest source line in this node's subtree
            lastEmittedLine = GetMaxLine(children[i]);

            // Append inline comments that share a source line with the statement: its first line, or its last
            // (`try X  ; c` is written as `try` ⏎ `X  ; c`, and must read back the same way)
            int stmtFirst = children[i].Line, stmtLast = lastEmittedLine;
            while (wantComments && i + 1 < children.Length
                && children[i + 1] != null
                && children[i + 1].NodeType == "Comment"
                && children[i + 1].Line > 0
                && (children[i + 1].Line == stmtFirst || children[i + 1].Line == stmtLast))
            {
                i++;
                emitted += "  " + children[i].Value;
                int commentLine = children[i].Line;
                if (commentLine > lastEmittedLine)
                    lastEmittedLine = commentLine;
            }

            lines.Add(emitted);
        }
        return string.Join("\n", lines);
    }

    /// <summary>`name`, `name := init` or `name op= expr` of a declaration (nested chain items included).</summary>
    private static string DeclarationItem(AstNode decl, bool minified)
    {
        var sb = new StringBuilder(decl.Value);
        int idx = 0;
        if (decl.ChildCount > 0 && decl.GetChild(0) != null && decl.GetChild(0).NodeType != "Declaration")
        {
            var init = decl.GetChild(0);
            if (IsCompoundDeclarationInit(decl, init))
                sb.Append(minified ? "" : " ").Append(init.Value).Append(minified ? "" : " ").Append(EmitNode(init.GetChild(1), 0));
            else
                sb.Append(minified ? ":=" : " := ").Append(EmitNode(init, 0));
            idx = 1;
        }
        for (; idx < decl.ChildCount; idx++)
        {
            var c = decl.GetChild(idx);
            if (c != null && c.NodeType == "Declaration") sb.Append(minified ? "," : ", ").Append(DeclarationItem(c, minified));
        }
        return sb.ToString();
    }
    /// <summary>`#HotIf expr` whose condition was parsed into the node's child (the child is the source of truth).</summary>
    public static bool IsParsedHotIf(AstNode node)
    {
        return node != null && node.NodeType == "Directive" && node.ChildCount > 0 && node.GetChild(0) != null
            && node.Metadata != null && node.Metadata.StartsWith("hotif");
    }

    /// <summary>A braceless body on its own line: expression statements don't pad themselves, so pad here.</summary>
    private static string IndentedBody(AstNode body, int indent)
    {
        string s = EmitNode(body, indent);
        string p = MakePad(indent);
        return s.StartsWith(p) ? s : p + s;
    }

    /// <summary>A loop's `until` / `else` children (from index <paramref name="from"/>), each on its own line.</summary>
    private static string LoopTail(AstNode loop, int from, int indent)
    {
        var sb = new StringBuilder();
        for (int i = from; i < loop.ChildCount; i++)
        {
            var c = loop.GetChild(i);
            if (c == null) continue;
            if (c.NodeType == "Until") sb.Append("\n" + MakePad(indent) + "until " + (c.ChildCount > 0 ? SafeEmitChild(c, 0, 0) : ""));
            else if (c.NodeType == "Else") sb.Append("\n" + EmitNode(c, indent));
        }
        return sb.ToString();
    }

    /// <summary>True when the statement before children[i] (comments skipped) is a `Name(...)` call.</summary>
    public static bool FollowsFunctionHeaderLike(AstNode[] children, int i)
    {
        for (int k = i - 1; k >= 0; k--)
        {
            var p = children[k];
            if (p == null || p.NodeType == "Comment") continue;
            if (p.NodeType != "Call" || p.ChildCount < 2) return false;
            var callee = p.GetChild(0);
            var args = p.GetChild(1);
            return callee != null && callee.NodeType == "Identifier" && args != null && args.Metadata != "command";
        }
        return false;
    }

    /// <summary>
    /// Recursively find the maximum source line number in a node's subtree.
    /// This gives the true "end" of a multi-line construct even when EndLine is unset.
    /// </summary>
    private static int GetMaxLine(AstNode node)
    {
        if (node == null) return 0;
        // memoised per Emit: asked for each statement at every nesting level, it re-walked whole subtrees
        int cached;
        if (_maxLine != null && _maxLine.TryGetValue(node, out cached)) return cached;
        int max = node.EndLine > 0 ? node.EndLine : node.Line;
        foreach (var child in node.ChildNodes)
        {
            if (child == null) continue;
            int childMax = GetMaxLine(child);
            if (childMax > max) max = childMax;
        }
        if (_maxLine != null) _maxLine[node] = max;
        return max;
    }

    [ThreadStatic] private static Dictionary<AstNode, int> _maxLine;

    private static bool HasGroupedAncestor(AstNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent.NodeType == "Grouped")
                return true;
            parent = parent.Parent;
        }
        return false;
    }
}
