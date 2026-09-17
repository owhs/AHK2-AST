using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AstHarness
{
    /// <summary>
    /// `ranges`: checks the engine's source ranges (AstNode.StartOffset/EndOffset, SourceRanges) on real scripts.
    /// For every node of every file: the range is inside the file and inside its parent's, starts and ends on token
    /// boundaries, and the line/column agree with the offsets. Statements and outermost expressions must re-parse from
    /// source.Substring(range) to the same subtree (type, value, children). Also: a no-op edit through SourceEdit
    /// returns the file byte for byte.
    /// </summary>
    public static class RangeCheck
    {
        class Stats
        {
            public int Files, CleanFiles, Nodes, Reparsed, NoopEdits;
            public readonly Dictionary<string, int> Fails = new Dictionary<string, int>();
            public readonly Dictionary<string, List<string>> Examples = new Dictionary<string, List<string>>();

            public void Fail(string kind, string example)
            {
                int n; Fails.TryGetValue(kind, out n); Fails[kind] = n + 1;
                List<string> ex;
                if (!Examples.TryGetValue(kind, out ex)) Examples[kind] = ex = new List<string>();
                if (ex.Count < 12) ex.Add(example);
            }
        }

        static readonly HashSet<string> ExpressionTypes = new HashSet<string>
        {
            "BinaryExpr", "UnaryExpr", "PostfixExpr", "Call", "Member", "Index", "Array", "Object", "Ternary",
            "Concat", "Grouped", "Number", "String", "Identifier", "FatArrow"
        };

        /// <summary>`ranges [files] [--tier t] [--filter x] [--limit N] [--deep] [--verbose]`; with no files, the corpus + cases.</summary>
        public static int Run(Args a)
        {
            var files = a.Positional.Select(Path.GetFullPath).ToList();
            if (files.Count == 0)
            {
                files.AddRange(Directory.GetFiles(Paths.Cases, "*.ahk"));
                try { files.AddRange(Corpus.Select(Corpus.Load(), a).Select(x => x.Path).Where(File.Exists)); }
                catch (FileNotFoundException) { }
            }
            var st = Check(files, a.Has("deep"), a.Has("verbose"));
            Print(st);
            return st.Fails.Count == 0 ? 0 : 1;
        }

        /// <summary>Used by selftest: the cases folder, every node re-parsed.</summary>
        public static bool SelfTest(out string detail)
        {
            var st = Check(Directory.GetFiles(Paths.Cases, "*.ahk").ToList(), true, false);
            var sb = new StringBuilder();
            sb.Append(st.Files + " files, " + st.Nodes + " nodes, " + st.Reparsed + " re-parsed, " + st.NoopEdits + " edits checked");
            foreach (var kv in st.Fails) sb.Append("\n          " + kv.Key + " ×" + kv.Value + ": " + st.Examples[kv.Key][0]);
            detail = sb.ToString();
            return st.Fails.Count == 0 && st.Nodes > 0;
        }

        static Stats Check(List<string> files, bool deep, bool verbose)
        {
            // deep trees (long else-if chains, generated code): the engine recurses, give it the stack a worker has
            Stats result = null;
            var th = new System.Threading.Thread(() => { result = CheckOn(files, deep, verbose); }, 256 * 1024 * 1024);
            th.Start();
            th.Join();
            return result;
        }

        static Stats CheckOn(List<string> files, bool deep, bool verbose)
        {
            var st = new Stats();
            foreach (string f in files)
            {
                string text;
                try { text = Util.ReadScript(f); } catch { continue; }
                st.Files++;
                try { CheckFile(f, text, deep, st); }
                catch (Exception ex) { st.Fail("crash", Path.GetFileName(f) + ": " + ex.GetType().Name + " " + ex.Message); }
                if (verbose && st.Files % 100 == 0) Console.WriteLine("  " + st.Files + " files, " + st.Nodes + " nodes");
            }
            return st;
        }

        static void CheckFile(string path, string text, bool deep, Stats st)
        {
            var lexer = new AhkLexer(text);
            var tokens = lexer.Tokenize();
            var starts = new HashSet<int>();
            var ends = new HashSet<int>();
            foreach (var t in tokens) { if (t.StartOffset >= 0) { starts.Add(t.StartOffset); ends.Add(t.EndOffset); } }
            var map = lexer.LineMap;
            foreach (var sec in map.Sections) { starts.Add(sec[0]); ends.Add(sec[1] + 1); } // a joined section's `(` … `)` lines
            var root = new AhkAstEngine().Parse(text);
            string name = Path.GetFileName(path);

            // no-op edit: replacing a node's text with itself changes nothing
            st.NoopEdits++;
            var firstStmt = root.ChildNodes.FirstOrDefault(n => n.HasRange && n.NodeType != "Comment" && n.NodeType != "Warning");
            if (firstStmt != null)
            {
                var r = SourceEdit.ReplaceNodeText(text, firstStmt, text.Substring(firstStmt.StartOffset, firstStmt.EndOffset - firstStmt.StartOffset), false);
                if (r.Text != text) st.Fail("noop-edit", name + ": replacing " + firstStmt.NodeType + " with its own text changed the file");
            }

            // re-parsing a piece only means something for code the engine parses cleanly
            bool clean = SourceEdit.Problems(root).Count == 0;
            if (clean) st.CleanFiles++;
            if (clean) EditRoundTrips(root, text, name, st);
            Walk(root, null, false, text, starts, ends, map, name, deep && clean, clean, st);
        }

        /// <summary>
        /// A probe statement inserted before / after a few statements (top level and inside blocks) and deleted again
        /// must give the file back byte for byte, and must parse as its own statement in between.
        /// </summary>
        static void EditRoundTrips(AstNode root, string text, string file, Stats st)
        {
            var stmts = new List<AstNode>();
            CollectStatements(root, stmts);
            if (stmts.Count == 0) return;
            var picks = new[] { stmts[0], stmts[stmts.Count / 2], stmts[stmts.Count - 1] }.Distinct().ToList();
            foreach (var s in picks)
            {
                foreach (bool before in new[] { true, false })
                {
                    st.NoopEdits++;
                    var r = before ? SourceEdit.InsertBefore(text, s, "__edit_probe := 1") : SourceEdit.InsertAfter(text, s, "__edit_probe := 1");
                    string what = (before ? "insert before " : "insert after ") + Where(file, s, text);
                    if (r.Error != null) { st.Fail("edit-roundtrip", what + ": " + r.Error); continue; }
                    var probe = FindProbe(r.Tree);
                    if (probe == null) { st.Fail("edit-roundtrip", what + ": the inserted statement does not parse as one"); continue; }
                    if (r.NewProblems.Count > 0) { st.Fail("edit-roundtrip", what + ": new problem " + r.NewProblems[0].Message); continue; }
                    // a statement alone on its line(s) gets whole new lines, which a delete removes again exactly; next to other
                    // code on the same line (`{ a }`, `} b`), inserting breaks that line and deleting does not join it back
                    if (!AloneOnItsLines(text, s)) continue;
                    var d = SourceEdit.DeleteNode(r.Text, SourceEdit.StatementOf(probe), false);
                    if (d.Text != text) st.Fail("edit-roundtrip", what + ": delete did not restore the file" + FirstDiff(text, d.Text));
                }
            }
        }

        static bool AloneOnItsLines(string text, AstNode s)
        {
            int ls = s.StartOffset;
            while (ls > 0 && text[ls - 1] != '\n') ls--;
            if (text.Substring(ls, s.StartOffset - ls).Trim(' ', '\t').Length > 0) return false;
            int le = s.EndOffset;
            while (le < text.Length && text[le] != '\n') le++;
            string rest = text.Substring(s.EndOffset, le - s.EndOffset).Trim(' ', '\t', '\r');
            return rest.Length == 0 || rest[0] == ';';
        }

        static void CollectStatements(AstNode n, List<AstNode> into)
        {
            foreach (var c in n.ChildNodes)
            {
                if (c == null || !c.HasRange) continue;
                if ((n.NodeType == "Program" || n.NodeType == "Block") && c.NodeType != "Comment" && c.NodeType != "Warning"
                    && !(n.Parent != null && n.Parent.NodeType == "Property"))
                    into.Add(c);
                if (c.NodeType != "Include" && c.NodeType != "Class") CollectStatements(c, into);
            }
        }

        static AstNode FindProbe(AstNode n)
        {
            if (n == null) return null;
            if (n.NodeType == "BinaryExpr" && n.ChildCount == 2 && n.GetChild(0).NodeType == "Identifier" && n.GetChild(0).Value == "__edit_probe") return n;
            foreach (var c in n.ChildNodes) { var f = FindProbe(c); if (f != null) return f; }
            return null;
        }

        static string FirstDiff(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            Func<string, string> clip = s => s.Substring(i, Math.Min(40, s.Length - i)).Replace("\r", "\\r").Replace("\n", "\\n");
            return " at offset " + i + ": want «" + clip(a) + "» got «" + clip(b) + "»";
        }

        static string Where(string file, AstNode n, string text)
        {
            string snippet = n.HasRange && n.EndOffset <= text.Length ? text.Substring(n.StartOffset, Math.Min(60, n.EndOffset - n.StartOffset)) : "";
            return file + ":" + n.RangeStartLine + ":" + n.RangeStartColumn + " " + n.NodeType + (string.IsNullOrEmpty(n.Value) ? "" : "=" + Util.Clip(n.Value, 30))
                   + "  «" + snippet.Replace("\r", "").Replace("\n", "⏎") + "»";
        }

        static void Walk(AstNode n, AstNode parent, bool subParsed, string text, HashSet<int> starts, HashSet<int> ends, SourceLineMap map,
                         string file, bool deep, bool reparse, Stats st)
        {
            st.Nodes++;
            if (!n.HasRange)
            {
                st.Fail("missing-range", Where(file, n, text) + (parent != null ? "  (in " + parent.NodeType + ")" : ""));
            }
            else
            {
                if (n.EndOffset > text.Length) st.Fail("out-of-file", Where(file, n, text));
                int l, c;
                map.ToLineCol(n.StartOffset, out l, out c);
                if (l != n.RangeStartLine || c != n.RangeStartColumn) st.Fail("line-col", Where(file, n, text) + " offset says " + l + ":" + c);
                if (parent != null && parent.HasRange && (n.StartOffset < parent.StartOffset || n.EndOffset > parent.EndOffset) && !SourceRanges.IsAttachedComment(n))
                    st.Fail("not-nested", Where(file, n, text) + "  outside " + Where(file, parent, text));
                if (!subParsed && parent != null && !(n.NodeType == "Comment" && parent.NodeType == "String"))
                {
                    bool empty = n.StartOffset == n.EndOffset;
                    bool okStart = starts.Contains(n.StartOffset) || (empty && ends.Contains(n.StartOffset));
                    bool okEnd = ends.Contains(n.EndOffset) || (empty && starts.Contains(n.EndOffset));
                    if (!okStart || !okEnd) st.Fail("token-boundary", Where(file, n, text) + (okStart ? " (end)" : " (start)"));
                    else if (reparse && !TouchesSection(map, n)) Reparse(n, parent, text, file, deep, st);
                }
            }
            // the #HotIf condition and an X hotstring's action are parsed from part of one token
            bool sub = subParsed || n.NodeType == "Directive" || n.NodeType == "Hotstring";
            for (int i = 0; i < n.ChildCount; i++)
            {
                var ch = n.GetChild(i);
                if (ch != null) Walk(ch, n, sub, text, starts, ends, map, file, deep, reparse, st);
            }
        }

        /// <summary>Statements (children of Program/Block) and outermost expressions re-parse to the same subtree.</summary>
        static void Reparse(AstNode n, AstNode parent, string text, string file, bool deep, Stats st)
        {
            if (n.StartOffset == n.EndOffset) return;
            string sub = text.Substring(n.StartOffset, n.EndOffset - n.StartOffset);
            bool statement = (parent.NodeType == "Program" || parent.NodeType == "Block") && n.NodeType != "Comment" && n.NodeType != "Warning"
                             && n.NodeType != "Error" && n.NodeType != "Include";
            bool expr = ExpressionTypes.Contains(n.NodeType) && !(n.NodeType == "Call" && n.Metadata == "command")
                        && (deep || !ExpressionTypes.Contains(parent.NodeType));
            if (!statement && !expr) return;
            // an object key may be any word (`{And: 1}`); alone it is an operator
            if (!statement && parent.NodeType == "KeyValue" && parent.ChildCount > 0 && parent.GetChild(0) == n) return;
            // a continuation section inside the wrapper `__rc := ( … )` would join differently
            if (!statement && n.Parent != null && ContainsSection(n, text)) return;
            if (statement && parent.NodeType == "Block" && parent.Parent != null && parent.Parent.NodeType == "Property") return; // get/set live in a property body
            AstNode got;
            if (statement)
            {
                // a hotstring's meaning depends on the `#Hotstring X` in force above it
                string context = "";
                if (n.NodeType == "Hotstring")
                {
                    int d = text.LastIndexOf("#Hotstring", n.StartOffset, StringComparison.OrdinalIgnoreCase);
                    if (d >= 0) { int eol = text.IndexOf('\n', d); context = text.Substring(d, (eol < 0 ? text.Length : eol) - d).TrimEnd('\r') + "\n"; }
                }
                var prog = new AhkAstEngine().Parse(context + sub);
                got = prog.ChildNodes.FirstOrDefault(x => x.NodeType != "Comment" && !(context.Length > 0 && x.NodeType == "Directive"));
            }
            else
            {
                var prog = new AhkAstEngine().Parse("__rc := (" + sub + "\n)");
                var assign = prog.ChildNodes.FirstOrDefault(x => x.NodeType != "Comment");
                got = assign != null && assign.NodeType == "BinaryExpr" && assign.ChildCount == 2 ? assign.GetChild(1) : null;
                if (got != null && got.NodeType == "Grouped") got = got.ChildNodes.FirstOrDefault(x => x.NodeType != "Comment"); // the wrapper's own ( )
            }
            st.Reparsed++;
            string want = Shape(n), have = got == null ? "(nothing)" : Shape(got);
            if (want != have)
                st.Fail(statement ? "reparse-statement" : "reparse-expression", Where(file, n, text) + "\n              want " + Util.Clip(want, 160) + "\n              got  " + Util.Clip(have, 160));
        }

        /// <summary>
        /// A node that starts or ends inside a joined continuation section (not spanning all of it): its text is a piece of
        /// the section, which only means code once joined (`2 *` ⏎ `(` ⏎ `1 + 1` ⏎ `)` is `2 * 1 + 1`).
        /// </summary>
        static bool TouchesSection(SourceLineMap map, AstNode n)
        {
            foreach (var sec in map.Sections)
            {
                if (n.StartOffset == sec[0]) return true; // starts with the section itself (its first operand is in it)
                bool startIn = n.StartOffset > sec[0] && n.StartOffset <= sec[1];
                bool endIn = n.EndOffset > sec[0] && n.EndOffset <= sec[1] + 1;
                bool spans = n.StartOffset <= sec[0] && n.EndOffset >= sec[1] + 1;
                if ((startIn || endIn) && !spans) return true;
                if (spans && n.Parent != null && n.Parent.StartOffset == n.StartOffset && n.Parent.EndOffset == n.EndOffset
                    && n.Parent.NodeType != "Program" && n.Parent.NodeType != "Block") return true; // widened to the whole section
            }
            return false;
        }

        static bool ContainsSection(AstNode n, string text)
        {
            string piece = text.Substring(n.StartOffset, n.EndOffset - n.StartOffset);
            if (piece.IndexOf('\n') < 0) return false;
            var lx = new AhkLexer(piece);
            lx.Tokenize();
            return lx.LineMap.Sections.Count > 0;
        }

        static string Shape(AstNode n)
        {
            var sb = new StringBuilder();
            Shape(n, sb);
            return sb.ToString();
        }

        static void Shape(AstNode n, StringBuilder sb)
        {
            // an implicit group has no parentheses in the source (`a && b := 1`): the parser adds it either way
            if (n.NodeType == "Grouped" && n.Metadata == "implicit" && n.ChildCount == 1) { Shape(n.GetChild(0), sb); return; }
            sb.Append(n.NodeType);
            // (a directive's value keeps the \r of its line; its range does not)
            if (!string.IsNullOrEmpty(n.Value) && n.NodeType != "Warning" && n.NodeType != "Error")
                sb.Append('=').Append(n.NodeType == "Directive" ? n.Value.TrimEnd() : n.Value.TrimEnd('\r')); // trailing blanks / \r: in the value, not the range
            var kids = n.ChildNodes.Where(c => c != null && c.NodeType != "Comment" && c.NodeType != "Warning").ToList();
            if (kids.Count == 0) return;
            sb.Append('(');
            for (int i = 0; i < kids.Count; i++) { if (i > 0) sb.Append(','); Shape(kids[i], sb); }
            sb.Append(')');
        }

        static void Print(Stats st)
        {
            Console.WriteLine(st.Files + " files (" + st.CleanFiles + " parse cleanly and are re-parsed piecewise), " + st.Nodes + " nodes, " + st.Reparsed + " re-parsed, " + st.NoopEdits + " edits checked");
            if (st.Fails.Count == 0) { Console.WriteLine("RANGES OK"); return; }
            foreach (var kv in st.Fails.OrderByDescending(k => k.Value))
            {
                Console.WriteLine("  " + kv.Key + " ×" + kv.Value);
                foreach (var ex in st.Examples[kv.Key]) Console.WriteLine("      " + ex);
            }
            Console.WriteLine("RANGES FAILED");
        }
    }
}
