using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AstHost
{
    /// <summary>Nodes as JSON (see NODES.md): type, value, flags, meta, start/end {line, col, offset}, file, children.</summary>
    public static class TreeJson
    {
        /// <summary>Writes <paramref name="n"/> (children down to <paramref name="maxDepth"/> levels); returns the node count.</summary>
        public static int Write(StringBuilder sb, AstNode n, int file, FileTable files, int maxDepth)
        {
            int count = 1;
            sb.Append("{\"type\":"); Json.Str(sb, n.NodeType);
            if (!string.IsNullOrEmpty(n.Value)) { sb.Append(",\"value\":"); Json.Str(sb, n.Value); }
            var flags = Flags(n);
            if (flags.Count > 0) { sb.Append(",\"flags\":"); Json.Value(sb, flags); }
            if (!string.IsNullOrEmpty(n.Metadata)) { sb.Append(",\"meta\":"); Json.Str(sb, n.Metadata); }
            if (n.HasRange)
            {
                sb.Append(",\"start\":{\"line\":").Append(n.RangeStartLine).Append(",\"col\":").Append(n.RangeStartColumn).Append(",\"offset\":").Append(n.StartOffset)
                  .Append("},\"end\":{\"line\":").Append(n.RangeEndLine).Append(",\"col\":").Append(n.RangeEndColumn).Append(",\"offset\":").Append(n.EndOffset).Append('}');
            }
            else sb.Append(",\"start\":null,\"end\":null");
            sb.Append(",\"file\":").Append(file);
            if (n.ChildCount > 0 && maxDepth > 0)
            {
                // an Include's children are the included file's code (their ranges are in that file)
                int childFile = n.ChildFile != null ? files.Index(n.ChildFile) : file;
                sb.Append(",\"children\":[");
                bool first = true;
                for (int i = 0; i < n.ChildCount; i++)
                {
                    var c = n.GetChild(i);
                    if (c == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    count += Write(sb, c, childFile, files, maxDepth - 1);
                }
                sb.Append(']');
            }
            else if (n.ChildCount > 0) sb.Append(",\"childCount\":").Append(n.ChildCount);
            sb.Append('}');
            return count;
        }

        static readonly HashSet<string> AssignOps = new HashSet<string> { ":=", "+=", "-=", "*=", "/=", "//=", ".=", "|=", "&=", "^=", "<<=", ">>=", ">>>=", "??=" };

        /// <summary>Metadata split into named flags, per node type (the raw string is also sent as "meta").</summary>
        public static JObj Flags(AstNode n)
        {
            var f = new JObj();
            string m = n.Metadata ?? "";
            switch (n.NodeType)
            {
                case "Method": case "Property": case "Class": case "StaticAssign":
                    if (m == "static") f.Add("static", true);
                    break;
                case "Declaration":
                    if (n.Parent != null && n.Parent.NodeType == "Class") { f.Add("member", true); if (m == "static") f.Add("static", true); }
                    else if (m == "global" || m == "local" || m == "static") f.Add("scope", m);
                    break;
                case "Parameter": case "Identifier":
                    foreach (var part in m.Split(','))
                        if (part == "byref" || part == "variadic" || part == "optional") f.Add(part, true);
                    break;
                case "Call": case "Arguments":
                    if (m == "command") f.Add("command", true);
                    break;
                case "Hotkey":
                    if (m == "inline") f.Add("inline", true);
                    break;
                case "Hotstring":
                    {
                        string opts, trigger, replacement;
                        if (m.StartsWith("inline")) f.Add("inline", true);
                        if (m.StartsWith("inline;")) f.Add("comment", m.Substring(7));
                        if (SplitHotstring(n.Value, out opts, out trigger, out replacement))
                        {
                            f.Add("options", opts).Add("trigger", trigger);
                            bool executes = n.ChildCount > 0;
                            f.Add("executes", executes);
                            if (!executes) f.Add("replacement", replacement);
                        }
                        break;
                    }
                case "Loop":
                    if (m == "comma") f.Add("comma", true);
                    break;
                case "For":
                    if (m == "paren") f.Add("paren", true);
                    break;
                case "Catch":
                    if (m.Length > 0) f.Add("var", m);
                    if (!string.IsNullOrEmpty(n.Value)) f.Add("classes", n.Value.Split(',').Select(x => (object)x.Trim()).ToList());
                    break;
                case "Switch":
                    if (m.Length > 0) f.Add("caseSense", m);
                    break;
                case "Concat":
                    f.Add("space", m == "space");
                    break;
                case "Grouped":
                    if (m == "implicit") f.Add("implicit", true);
                    break;
                case "FatArrow":
                    if (m == "bare") f.Add("bare", true);
                    break;
                case "Directive":
                    {
                        string v = (n.Value ?? "").TrimEnd();
                        int sp = 0;
                        while (sp < v.Length && !char.IsWhiteSpace(v[sp])) sp++;
                        f.Add("name", v.Substring(0, sp)).Add("args", v.Substring(sp).Trim());
                        if (m.StartsWith("hotif")) { f.Add("hotif", true); if (m.StartsWith("hotif;")) f.Add("comment", m.Substring(6)); }
                        break;
                    }
                case "String":
                    if (m.StartsWith("raw:")) f.Add("raw", m.Substring(4));
                    break;
                case "Error":
                    if (m == "recovery") f.Add("recovery", true);
                    break;
                case "Warning":
                    if (m.StartsWith("raw:")) f.Add("raw", m.Substring(4));
                    break;
                case "Include":
                    if (m.StartsWith("; duplicate include:")) f.Add("duplicate", true);
                    else if (m.Length > 0) f.Add("directive", m);
                    break;
                case "BinaryExpr":
                    if (AssignOps.Contains(n.Value ?? "")) f.Add("assign", true);
                    break;
                case "Comment":
                    if (SourceRanges.IsAttachedComment(n)) f.Add("attached", true);
                    if ((n.Value ?? "").StartsWith("/*")) f.Add("block", true);
                    break;
            }
            return f;
        }

        /// <summary>`:opts:trigger::replacement` → its parts.</summary>
        public static bool SplitHotstring(string v, out string options, out string trigger, out string replacement)
        {
            options = trigger = replacement = "";
            if (string.IsNullOrEmpty(v) || v[0] != ':') return false;
            int second = v.IndexOf(':', 1);
            if (second < 0) return false;
            int end = v.IndexOf("::", second + 1, StringComparison.Ordinal);
            if (end < 0) return false;
            options = v.Substring(1, second - 1);
            trigger = v.Substring(second + 1, end - second - 1);
            replacement = v.Substring(end + 2);
            return true;
        }
    }
}
