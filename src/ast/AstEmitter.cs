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
        return EmitNode(node, initialIndent);
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
        string pad = MakePad(indent);

        switch (node.NodeType)
        {
            case "Program":
                return EmitChildren(node.ChildNodes, 0);

            case "Directive":
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
                        body = "\n" + EmitNode(bodyNode, indent + 1);
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
                        body = "\n" + EmitNode(bodyNode, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                return pad + "while " + cond + body;
            }

            case "Return":
                return pad + "return" + (node.ChildCount > 0 ? " " + SafeEmitChild(node, 0, 0) : "");

            case "BinaryExpr":
                return SafeEmitChild(node, 0, 0) + " " + node.Value + " " + SafeEmitChild(node, 1, 0);

            case "UnaryExpr":
            {
                string op = node.Value;
                bool wordOp = !string.IsNullOrEmpty(op) && char.IsLetter(op[0]);
                string space = wordOp ? " " : "";
                return op + space + SafeEmitChild(node, 0, 0);
            }

            case "PostfixExpr":
                return SafeEmitChild(node, 0, 0) + node.Value;

            case "UnsetModifier":
                return SafeEmitChild(node, 0, 0) + "?";

            case "Call":
                return SafeEmitChild(node, 0, 0) + SafeEmitChild(node, 1, 0);

            case "Arguments":
                return "(" + string.Join(", ", node.ChildNodes.Select(c => EmitNode(c, 0))) + ")";

            case "Member":
                return SafeEmitChild(node, 0, 0) + "." + node.Value;

            case "Index":
            {
                var indices = new List<string>();
                for (int i = 1; i < node.ChildCount; i++)
                {
                    indices.Add(SafeEmitChild(node, i, 0));
                }
                return SafeEmitChild(node, 0, 0) + "[" + string.Join(", ", indices) + "]";
            }

            case "Number":
            case "Identifier":
                return node.Value;

            case "String":
            {
                string val = node.Value;
                if (!string.IsNullOrEmpty(val) && (val.Contains('\n') || val.Contains('\r')))
                {
                    if (val.StartsWith("\"") && val.EndsWith("\""))
                    {
                        string inner = val.Substring(1, val.Length - 2);
                        return "\"\n(\n" + inner + "\n)\"";
                    }
                    else if (val.StartsWith("'") && val.EndsWith("'"))
                    {
                        string inner = val.Substring(1, val.Length - 2);
                        return "'\n(\n" + inner + "\n)'";
                    }
                    else
                    {
                        return "(\n" + val + "\n)";
                    }
                }
                return val;
            }

            case "This":
                return "this";

            case "Array":
                return "[" + string.Join(", ", node.ChildNodes.Select(c => EmitNode(c, 0))) + "]";

            case "Object":
                return "{" + string.Join(", ",
                    node.ChildNodes.Select(kv =>
                        SafeEmitChild(kv, 0, 0) + ": " + SafeEmitChild(kv, 1, 0))) + "}";

            case "FatArrow":
                return SafeEmitChild(node, 0, 0) + " => " + SafeEmitChild(node, 1, 0);

            case "Ternary":
                return SafeEmitChild(node, 0, 0) + " ? "
                    + SafeEmitChild(node, 1, 0) + " : " + SafeEmitChild(node, 2, 0);

            case "Grouped":
                return "(" + SafeEmitChild(node, 0, 0) + ")";

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
                        else
                            sab.Append(", " + EmitNode(chainChild, 0));
                    }
                }
                return sab.ToString();
            }

            case "Concat":
            {
                var sb = new StringBuilder();
                for (int i = 0; i < node.ChildCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child == null) continue;
                    string emitted = EmitNode(child, 0);
                    if (sb.Length > 0)
                    {
                        string sep = node.Metadata == "nospace" ? "" : " ";
                        sb.Append(sep);
                    }
                    sb.Append(emitted);
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
                        body = "\n" + EmitNode(bodyNode, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                return pad + "for " + fvars + " in " + fcoll + body;
            }

            case "ForVars":
                return string.Join(", ", node.ChildNodes.Select(c => EmitNode(c, 0)));

            case "Loop":
            {
                string variant = !string.IsNullOrEmpty(node.Value) ? " " + node.Value : "";
                var until = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Until");
                var nonUntilChildren = node.ChildNodes.Where(c => c != null && c.NodeType != "Until").ToList();
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
                        body = "\n" + EmitNode(lbody, indent + 1);
                }
                else
                {
                    body = " {\n" + pad + "}";
                }
                string result = pad + "loop" + variant + argsStr + body;
                if (until != null) result += "\n" + pad + "until " + (until.ChildCount > 0 ? SafeEmitChild(until, 0, 0) : "");
                return result;
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
                        tbody = "\n" + EmitNode(bodyNode, indent + 1);
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
                        cbody = "\n" + EmitNode(bodyNode, indent + 1);
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
                        fbody = "\n" + EmitNode(bodyNode, indent + 1);
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

            case "New":
                return "new " + (node.ChildCount > 0 ? SafeEmitChild(node, 0, 0) : "");

            case "Hotkey":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string hbody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        hbody = " " + EmitNode(bodyNode, indent);
                    else
                        hbody = "\n" + EmitNode(bodyNode, indent + 1);
                }
                return pad + node.Value + "::" + hbody;
            }

            case "Hotstring":
            {
                var bodyNode = node.ChildCount > 0 ? node.GetChild(0) : null;
                string hbody = "";
                if (bodyNode != null)
                {
                    if (bodyNode.NodeType == "Block")
                        hbody = " " + EmitNode(bodyNode, indent);
                    else
                        hbody = "\n" + EmitNode(bodyNode, indent + 1);
                }
                return pad + node.Value + hbody;
            }

            case "Declaration":
            {
                string dscope = !string.IsNullOrEmpty(node.Metadata) ? node.Metadata + " " : "";
                var sb = new System.Text.StringBuilder();
                sb.Append(pad).Append(dscope).Append(node.Value);
                int startChainedIdx = 0;
                if (node.ChildCount > 0 && node.GetChild(0).NodeType != "Declaration")
                {
                    sb.Append(" := ").Append(EmitNode(node.GetChild(0), 0));
                    startChainedIdx = 1;
                }
                for (int idx = startChainedIdx; idx < node.ChildCount; idx++)
                {
                    var child = node.GetChild(idx);
                    if (child.NodeType == "Declaration")
                    {
                        sb.Append("\n").Append(EmitNode(child, indent));
                    }
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
                        ebody = "\n" + EmitNode(bodyNode, indent + 1);
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

            string emitted = EmitNode(children[i], childIndent);
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

            // Append inline comments that share the same source line
            while (wantComments && i + 1 < children.Length
                && children[i + 1] != null
                && children[i + 1].NodeType == "Comment"
                && children[i + 1].Line > 0
                && children[i + 1].Line == children[i].Line)
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

    /// <summary>
    /// Recursively find the maximum source line number in a node's subtree.
    /// This gives the true "end" of a multi-line construct even when EndLine is unset.
    /// </summary>
    private static int GetMaxLine(AstNode node)
    {
        if (node == null) return 0;
        int max = node.EndLine > 0 ? node.EndLine : node.Line;
        foreach (var child in node.ChildNodes)
        {
            if (child == null) continue;
            int childMax = GetMaxLine(child);
            if (childMax > max) max = childMax;
        }
        return max;
    }
}
