using System;
using System.Collections.Generic;
using System.Linq;

namespace AstHost
{
    /// <summary>
    /// The `outline` reply: functions, classes (members, extends), hotkeys, hotstrings, globals, labels and includes,
    /// each with its range (and file index when includes are followed).
    /// </summary>
    public static class Outline
    {
        class Ctx
        {
            public string MainText;
            public FileTable Files;
            public readonly List<object> Functions = new List<object>(), Classes = new List<object>(), Hotkeys = new List<object>(),
                Hotstrings = new List<object>(), Labels = new List<object>(), Includes = new List<object>();
            public readonly Dictionary<string, JObj> Globals = new Dictionary<string, JObj>(StringComparer.OrdinalIgnoreCase);
            public readonly List<object> GlobalList = new List<object>();
        }

        public static JObj Build(AstNode root, string text, FileTable files)
        {
            var c = new Ctx { MainText = text, Files = files };
            Walk(root, 0, c);
            return new JObj()
                .Add("functions", c.Functions)
                .Add("classes", c.Classes)
                .Add("hotkeys", c.Hotkeys)
                .Add("hotstrings", c.Hotstrings)
                .Add("globals", c.GlobalList)
                .Add("labels", c.Labels)
                .Add("includes", c.Includes);
        }

        /// <summary>Statements in global scope: top level, and blocks of top-level if/loop/try (not function or hotkey bodies).</summary>
        static void Walk(AstNode n, int file, Ctx c)
        {
            for (int i = 0; i < n.ChildCount; i++)
            {
                var s = n.GetChild(i);
                if (s == null) continue;
                switch (s.NodeType)
                {
                    case "Method":
                        c.Functions.Add(Function(s, file, c));
                        break;
                    case "Class":
                        c.Classes.Add(Class(s, file, c));
                        break;
                    case "Hotkey":
                    case "Remap":
                        AddHotkey(s, file, c);
                        break;
                    case "Hotstring":
                        {
                            string opts, trigger, repl;
                            TreeJson.SplitHotstring(s.Value, out opts, out trigger, out repl);
                            var o = new JObj().Add("options", opts).Add("trigger", trigger).Add("executes", s.ChildCount > 0);
                            if (s.ChildCount == 0) o.Add("replacement", repl);
                            c.Hotstrings.Add(Range(o, s, file));
                            break;
                        }
                    case "Label":
                        c.Labels.Add(Range(new JObj().Add("name", s.Value), s, file));
                        break;
                    case "Include":
                        if (s.ChildFile != null)
                        {
                            c.Includes.Add(Range(new JObj().Add("path", s.ChildFile).Add("directive", s.Metadata ?? ""), s, file));
                            Walk(s, c.Files.Index(s.ChildFile), c);
                        }
                        else Walk(s, file, c);
                        break;
                    case "Directive":
                        if ((s.Value ?? "").TrimStart().StartsWith("#Include", StringComparison.OrdinalIgnoreCase))
                            c.Includes.Add(Range(new JObj().Add("path", null).Add("directive", (s.Value ?? "").Trim()), s, file)); // not followed
                        break;
                    case "BinaryExpr":
                        Assignment(s, file, c);
                        break;
                    case "MultiStatement":
                        foreach (var item in s.ChildNodes) if (item != null && item.NodeType == "BinaryExpr") Assignment(item, file, c);
                        break;
                    case "Declaration":
                        if (s.Metadata == "global") Declaration(s, file, c);
                        break;
                    case "FatArrow": case "Property": case "Comment": case "Warning": case "Error":
                        break;
                    default:
                        Walk(s, file, c); // if / loop / try / blocks at global scope
                        break;
                }
            }
        }

        static void AddHotkey(AstNode s, int file, Ctx c)
        {
            if (s.NodeType == "Remap")
            {
                c.Hotkeys.Add(Range(new JObj().Add("trigger", s.Value).Add("remap", s.ChildCount > 0 ? s.GetChild(0).Value : ""), s, file));
                return;
            }
            c.Hotkeys.Add(Range(new JObj().Add("trigger", s.Value).Add("inline", s.Metadata == "inline"), s, file));
            // stacked hotkeys (`a::` ⏎ `b::x()`) nest; the rest of a hotkey body is its own scope
            foreach (var ch in s.ChildNodes)
                if (ch != null && (ch.NodeType == "Hotkey" || ch.NodeType == "Remap")) AddHotkey(ch, file, c);
        }

        static void Assignment(AstNode s, int file, Ctx c)
        {
            if (s.ChildCount < 1 || (s.Value ?? "") != ":=") return;
            var target = s.GetChild(0);
            if (target == null || target.NodeType != "Identifier" || (target.Value ?? "").IndexOf('%') >= 0) return;
            AddGlobal(target.Value, "assign", s, file, c);
            // `a := b := 0`
            if (s.ChildCount > 1 && s.GetChild(1) != null && s.GetChild(1).NodeType == "BinaryExpr") Assignment(s.GetChild(1), file, c);
        }

        static void Declaration(AstNode s, int file, Ctx c)
        {
            if (!string.IsNullOrEmpty(s.Value)) AddGlobal(s.Value, "declaration", s, file, c);
            foreach (var ch in s.ChildNodes)
                if (ch != null && ch.NodeType == "Declaration" && !string.IsNullOrEmpty(ch.Value)) AddGlobal(ch.Value, "declaration", ch, file, c);
        }

        static void AddGlobal(string name, string kind, AstNode at, int file, Ctx c)
        {
            JObj g;
            if (c.Globals.TryGetValue(name, out g)) { g["count"] = (int)g["count"] + 1; return; }
            g = Range(new JObj().Add("name", name).Add("kind", kind).Add("count", 1), at, file);
            c.Globals[name] = g;
            c.GlobalList.Add(g);
        }

        static JObj Function(AstNode m, int file, Ctx c)
        {
            var o = new JObj().Add("name", m.Value).Add("static", m.Metadata == "static");
            var ps = m.ChildNodes.FirstOrDefault(x => x != null && x.NodeType == "Parameters");
            o.Add("params", Params(ps, file, c));
            var body = m.ChildNodes.FirstOrDefault(x => x != null && (x.NodeType == "Block" || x.NodeType == "FatArrowBody"));
            o.Add("fatArrow", body != null && body.NodeType == "FatArrowBody");
            Range(o, m, file);
            if (body != null && body.HasRange) o.Add("body", RangeOnly(body));
            return o;
        }

        static List<object> Params(AstNode ps, int file, Ctx c)
        {
            var list = new List<object>();
            if (ps == null) return list;
            foreach (var p in ps.ChildNodes)
            {
                if (p == null || p.NodeType != "Parameter") continue;
                string m = p.Metadata ?? "";
                var o = new JObj().Add("name", p.Value)
                    .Add("byref", m.Contains("byref")).Add("variadic", m.Contains("variadic")).Add("optional", m.Contains("optional") || p.ChildCount > 0);
                var def = p.ChildNodes.FirstOrDefault(x => x != null);
                if (def != null) o.Add("default", Text(def, file, c));
                list.Add(Range(o, p, file));
            }
            return list;
        }

        static JObj Class(AstNode cls, int file, Ctx c)
        {
            var o = new JObj().Add("name", cls.Value).Add("static", cls.Metadata == "static");
            var ext = cls.ChildNodes.FirstOrDefault(x => x != null && x.NodeType == "Extends");
            o.Add("extends", ext != null ? ext.Value : null);
            if (ext != null && ext.HasRange) o.Add("extendsRange", RangeOnly(ext));
            var members = new List<object>();
            foreach (var m in cls.ChildNodes)
            {
                if (m == null) continue;
                switch (m.NodeType)
                {
                    case "Method":
                        {
                            var mo = new JObj().Add("kind", "method").Add("name", m.Value).Add("static", m.Metadata == "static");
                            mo.Add("params", Params(m.ChildNodes.FirstOrDefault(x => x != null && x.NodeType == "Parameters"), file, c));
                            members.Add(Range(mo, m, file));
                            break;
                        }
                    case "Property":
                        {
                            var po = new JObj().Add("kind", "property").Add("name", m.Value).Add("static", m.Metadata == "static");
                            po.Add("params", Params(m.ChildNodes.FirstOrDefault(x => x != null && x.NodeType == "Parameters"), file, c));
                            var accessors = new List<object>();
                            var body = m.ChildNodes.FirstOrDefault(x => x != null && x.NodeType == "Block");
                            if (body == null) accessors.Add("get"); // `Prop => expr`
                            else foreach (var a in body.ChildNodes) if (a != null && a.NodeType == "Method") accessors.Add(a.Value);
                            po.Add("accessors", accessors);
                            members.Add(Range(po, m, file));
                            break;
                        }
                    case "StaticAssign":
                        members.Add(Range(new JObj().Add("kind", "var").Add("name", m.Value).Add("static", m.Metadata == "static")
                            .Add("init", m.ChildCount > 0 ? Text(m.GetChild(0), file, c) : null), m, file));
                        foreach (var more in m.ChildNodes.Skip(1))
                            if (more != null && (more.NodeType == "StaticAssign" || more.NodeType == "Declaration"))
                                members.Add(Range(new JObj().Add("kind", "var").Add("name", more.Value).Add("static", more.Metadata == "static"), more, file));
                        break;
                    case "Declaration":
                        members.Add(Range(new JObj().Add("kind", "var").Add("name", m.Value).Add("static", m.Metadata == "static"), m, file));
                        break;
                    case "Class":
                        {
                            var nested = Class(m, file, c);
                            nested["kind"] = "class";
                            members.Add(nested);
                            break;
                        }
                }
            }
            o.Add("members", members);
            return Range(o, cls, file);
        }

        /// <summary>The source text of a node in the main text (null for include files: their text is not at hand).</summary>
        static string Text(AstNode n, int file, Ctx c)
        {
            if (file != 0 || n == null || !n.HasRange || c.MainText == null || n.EndOffset > c.MainText.Length) return null;
            return c.MainText.Substring(n.StartOffset, n.EndOffset - n.StartOffset);
        }

        static JObj Range(JObj o, AstNode n, int file)
        {
            if (n.HasRange)
            {
                o.Add("start", new JObj().Add("line", n.RangeStartLine).Add("col", n.RangeStartColumn).Add("offset", n.StartOffset));
                o.Add("end", new JObj().Add("line", n.RangeEndLine).Add("col", n.RangeEndColumn).Add("offset", n.EndOffset));
            }
            else { o.Add("start", null); o.Add("end", null); }
            o.Add("file", file);
            return o;
        }

        static JObj RangeOnly(AstNode n)
        {
            return new JObj()
                .Add("start", new JObj().Add("line", n.RangeStartLine).Add("col", n.RangeStartColumn).Add("offset", n.StartOffset))
                .Add("end", new JObj().Add("line", n.RangeEndLine).Add("col", n.RangeEndColumn).Add("offset", n.EndOffset));
        }
    }
}
