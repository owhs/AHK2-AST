using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>A parse problem with its source range (ranges are in the text that was parsed).</summary>
public class SourceProblem
{
    public string Kind;      // Error | Warning
    public string Message;
    public AstNode Node;     // the Error/Warning/Unknown node
    public int StartOffset = -1, EndOffset = -1;
    public int StartLine, StartColumn, EndLine, EndColumn;
    public string File;      // null = the parsed text itself; else the include file the problem is in

    /// <summary>The message without "at line X:Y" (positions move when the text around them changes).</summary>
    public string Key { get { return Kind + ":" + Regex.Replace(Message ?? "", @"\s*at line \d+:\d+", ""); } }
}

/// <summary>The result of a text-preserving edit.</summary>
public class EditResult
{
    public string Text;                 // the new source: the original with one splice, nothing re-emitted
    public bool Changed;
    public int StartOffset, EndOffset;  // what was written, as a range in the new text (empty for a delete)
    public AstNode Tree;                // the new text re-parsed (null when the edit was asked not to re-parse)
    public List<SourceProblem> Problems = new List<SourceProblem>();    // every parse problem of the new text
    public List<SourceProblem> NewProblems = new List<SourceProblem>(); // those the old text did not have
    public string Error;                // why nothing was done (the edit was not possible)
}

/// <summary>
/// Edits that change only the text of the node they touch. The emitter reformats (joins multi-line calls, moves
/// comments, splits `} else {`); a designer that edits a user's script needs the rest of the file untouched, so these
/// splice the original string using the node's source range (AstNode.StartOffset/EndOffset) and re-parse the result
/// to report problems the edit introduced.
/// </summary>
public static class SourceEdit
{
    /// <summary>Nodes that hold statements (their children are what InsertBefore/After/DeleteNode work on).</summary>
    static bool IsStatementList(AstNode n)
    {
        if (n == null) return false;
        switch (n.NodeType)
        {
            case "Program": case "Block": case "CaseBody": case "DefaultBody": case "Class": case "Include":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Nodes whose children are comma-separated items (deleting one removes its comma too).</summary>
    static bool IsCommaList(AstNode n)
    {
        if (n == null) return false;
        switch (n.NodeType)
        {
            case "Arguments": case "Array": case "Object": case "Parameters": case "MultiStatement": case "Sequence":
            case "Index": case "ForVars": case "Case":
                return true;
            case "Declaration": case "StaticAssign":
                return true;
            default:
                return false;
        }
    }

    /// <summary>The statement <paramref name="n"/> is part of (itself when it is one).</summary>
    public static AstNode StatementOf(AstNode n)
    {
        while (n != null && n.Parent != null && !IsStatementList(n.Parent)) n = n.Parent;
        return n;
    }

    /// <summary>
    /// The node with exactly this range: of type <paramref name="type"/> when given, else the outermost one (a statement
    /// rather than the expression that is all of it). Null if none.
    /// </summary>
    public static AstNode FindByRange(AstNode root, int start, int end, string type)
    {
        AstNode found = null;
        Find(root, start, end, type, ref found);
        return found;
    }

    static void Find(AstNode n, int start, int end, string type, ref AstNode found)
    {
        if (n == null || found != null) return;
        if (n.HasRange && (n.StartOffset > start || n.EndOffset < end) && n.NodeType != "Program") return; // not inside
        if (n.StartOffset == start && n.EndOffset == end && (type == null || string.Equals(n.NodeType, type, StringComparison.OrdinalIgnoreCase))
            && n.NodeType != "Program")
        {
            found = n;
            return;
        }
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c == null || c.ChildFile != null) continue; // a followed #Include: another file's offsets
            Find(c, start, end, type, ref found);
        }
    }

    // -- the edits ----------------------------------------------------------------------------------------

    public static EditResult ReplaceRange(string source, int start, int end, string newText, bool reparse = true)
    {
        source = source ?? "";
        if (start < 0 || end < start || end > source.Length) return Fail(source, "range " + start + ".." + end + " is outside the text");
        newText = newText ?? "";
        string text = source.Substring(0, start) + newText + source.Substring(end);
        return Finish(source, text, start, start + newText.Length, reparse);
    }

    /// <summary>The node's text replaced by <paramref name="newText"/>, as given.</summary>
    public static EditResult ReplaceNodeText(string source, AstNode node, string newText, bool reparse = true)
    {
        if (node == null || !node.HasRange) return Fail(source, "the node has no source range");
        return ReplaceRange(source, node.StartOffset, node.EndOffset, newText, reparse);
    }

    /// <summary>
    /// <paramref name="newText"/> as new line(s) above the statement <paramref name="node"/> is in, indented like it
    /// (the text's own common indentation is replaced; its relative indentation is kept).
    /// </summary>
    public static EditResult InsertBefore(string source, AstNode node, string newText, bool reparse = true)
    {
        var s = StatementOf(node);
        if (s == null || !s.HasRange) return Fail(source, "the node has no source range");
        string eol = LineBreakAt(source, s.StartOffset);
        int lineStart = LineStart(source, s.StartOffset);
        string indent = source.Substring(lineStart, s.StartOffset - lineStart);
        if (indent.Trim(' ', '\t').Length == 0)
        {
            // the statement starts its line: new lines go above it
            string block = Indent(newText, indent, eol) + eol;
            return Insert(source, lineStart, block, 0, block.Length - eol.Length, reparse);
        }
        // something precedes it on its line (`{ stmt`): break the line in front of it
        string lineIndent = LeadingBlanks(source, lineStart);
        string ins = Indent(newText, "", eol).Replace(eol, eol + lineIndent) + eol + lineIndent;
        return Insert(source, s.StartOffset, ins, 0, ins.Length - eol.Length - lineIndent.Length, reparse);
    }

    /// <summary><paramref name="newText"/> as new line(s) below the statement <paramref name="node"/> is in, indented like it.</summary>
    public static EditResult InsertAfter(string source, AstNode node, string newText, bool reparse = true)
    {
        var s = StatementOf(node);
        if (s == null || !s.HasRange) return Fail(source, "the node has no source range");
        string eol = LineBreakAt(source, s.EndOffset);
        int lineStart = LineStart(source, s.StartOffset);
        string indent = LeadingBlanks(source, lineStart);
        int lineEnd = LineEnd(source, s.EndOffset);
        string rest = source.Substring(s.EndOffset, lineEnd - s.EndOffset);
        string body = Indent(newText, indent, eol);
        if (IsBlankOrComment(rest))
        {
            // after the statement's last line (its trailing comment stays with it), ending with that line's break
            int next = AfterLineBreak(source, lineEnd);
            if (next > lineEnd)
                return Insert(source, next, body + eol, 0, body.Length, reparse);
            string ins = eol + body; // the last line has no break: add one before the new text
            return Insert(source, lineEnd, ins, eol.Length, ins.Length, reparse);
        }
        // more code follows on the same line (`stmt }`): break the line after the statement
        string after = eol + body + eol + LeadingBlanks(source, LineStart(source, s.EndOffset));
        return Insert(source, s.EndOffset, after, eol.Length, eol.Length + body.Length, reparse);
    }

    /// <summary>
    /// Removes the node. A statement that is alone on its line(s) goes with those lines, its trailing same-line comment
    /// included; an item of a comma list goes with its comma; anything else just loses its text.
    /// </summary>
    public static EditResult DeleteNode(string source, AstNode node, bool reparse = true)
    {
        if (node == null || !node.HasRange) return Fail(source, "the node has no source range");
        source = source ?? "";
        int start = node.StartOffset, end = node.EndOffset;
        bool statement = node.Parent == null || IsStatementList(node.Parent);

        if (!statement && IsCommaList(node.Parent) && node.Parent.ChildCount > 1)
        {
            // `f(a, b, c)` minus b: its comma goes too (the one after it with the blanks up to the next item, which then
            // sits where b did; for the last item the comma before it); items may be on lines of their own
            int j = SkipBlanks(source, end, true);
            if (j < source.Length && source[j] == ',')
            {
                end = SkipBlanks(source, j + 1, true);
            }
            else
            {
                int k = SkipBlanksBack(source, start, true);
                if (k > 0 && source[k - 1] == ',') start = k - 1;
            }
            return ReplaceRange(source, start, end, "", reparse);
        }

        int lineStart = LineStart(source, start);
        int lineEnd = LineEnd(source, end);
        bool aloneBefore = source.Substring(lineStart, start - lineStart).Trim(' ', '\t').Length == 0;
        bool aloneAfter = IsBlankOrComment(source.Substring(end, lineEnd - end));
        if (aloneBefore && aloneAfter)
        {
            // whole lines, through the line break
            int next = AfterLineBreak(source, lineEnd);
            if (next == lineEnd && lineStart > 0)
            {
                // the last line: take the line break before it instead
                int p = lineStart;
                if (p > 0 && source[p - 1] == '\n') p--;
                if (p > 0 && source[p - 1] == '\r') p--;
                lineStart = p;
            }
            return ReplaceRange(source, lineStart, next, "", reparse);
        }
        if (aloneAfter)
        {
            // `x:: stmt ; note` → `x::` : the statement, its comment and the blanks before it
            return ReplaceRange(source, SkipBlanksBack(source, start, false), lineEnd, "", reparse);
        }
        // code follows on the line: the statement and the blanks after it
        return ReplaceRange(source, start, SkipBlanks(source, end, false), "", reparse);
    }

    // -- problems -----------------------------------------------------------------------------------------

    /// <summary>Every Error / Warning / Unknown node of a parsed tree as a problem with its range.</summary>
    public static List<SourceProblem> Problems(AstNode root)
    {
        var list = new List<SourceProblem>();
        Collect(root, null, null, list);
        return list;
    }

    static void Collect(AstNode n, string file, AstNode ranged, List<SourceProblem> list)
    {
        if (n == null) return;
        if (n.HasRange && n.NodeType != "Program") ranged = n;
        if (n.NodeType == "Error" || n.NodeType == "Warning")
        {
            // a problem without a range of its own (an include that could not be read) is shown at the nearest
            // node that has one
            var at = n.HasRange ? n : ranged;
            var p = new SourceProblem { Kind = n.NodeType, Message = n.Value, Node = n, File = n.HasRange ? file : RangeFile(n, file) };
            if (at != null)
            {
                p.StartOffset = at.StartOffset; p.EndOffset = at.EndOffset;
                p.StartLine = at.RangeStartLine; p.StartColumn = at.RangeStartColumn; p.EndLine = at.RangeEndLine; p.EndColumn = at.RangeEndColumn;
            }
            list.Add(p);
        }
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c == null) continue;
            if (n.ChildFile != null) Collect(c, n.ChildFile, n, list); // children: that file (the #Include line as fallback)
            else Collect(c, file, ranged, list);
        }
    }

    static string RangeFile(AstNode n, string file)
    {
        // no range: shown at the #Include line, which is in the file that includes this one
        for (var p = n.Parent; p != null; p = p.Parent)
            if (p.ChildFile != null)
            {
                for (var q = p.Parent; q != null; q = q.Parent) if (q.ChildFile != null) return q.ChildFile;
                return null;
            }
        return file;
    }

    // -- helpers ------------------------------------------------------------------------------------------

    static EditResult Insert(string source, int at, string ins, int writtenFrom, int writtenTo, bool reparse)
    {
        string text = source.Substring(0, at) + ins + source.Substring(at);
        return Finish(source, text, at + writtenFrom, at + writtenTo, reparse);
    }

    static EditResult Fail(string source, string why)
    {
        return new EditResult { Text = source, Changed = false, Error = why };
    }

    static EditResult Finish(string before, string after, int start, int end, bool reparse)
    {
        var r = new EditResult { Text = after, Changed = after != before, StartOffset = start, EndOffset = end };
        if (!reparse) return r;
        r.Tree = new AhkAstEngine().Parse(after);
        r.Problems = Problems(r.Tree);
        var old = Problems(new AhkAstEngine().Parse(before)).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var p in r.Problems)
        {
            int n;
            if (old.TryGetValue(p.Key, out n) && n > 0) { old[p.Key] = n - 1; continue; }
            r.NewProblems.Add(p);
        }
        return r;
    }

    /// <summary>The file's line break: CRLF if its first line break is one, else LF (CRLF for a one-line text).</summary>
    public static string LineBreak(string s)
    {
        int i = (s ?? "").IndexOf('\n');
        if (i < 0) return "\r\n";
        return i > 0 && s[i - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>The line break that ends the line <paramref name="offset"/> is on (the file's usual one for the last line).</summary>
    static string LineBreakAt(string s, int offset)
    {
        int i = Math.Max(0, Math.Min(offset, s.Length));
        while (i < s.Length && s[i] != '\n') i++;
        if (i >= s.Length) return LineBreak(s);
        return i > 0 && s[i - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>Past the \r\n or \n at <paramref name="i"/>; <paramref name="i"/> itself when there is none (a lone \r is no line break).</summary>
    static int AfterLineBreak(string s, int i)
    {
        if (i + 1 < s.Length && s[i] == '\r' && s[i + 1] == '\n') return i + 2;
        if (i < s.Length && s[i] == '\n') return i + 1;
        return i;
    }

    static int LineStart(string s, int offset)
    {
        int i = Math.Min(offset, s.Length);
        while (i > 0 && s[i - 1] != '\n') i--;
        return i;
    }

    /// <summary>The end of the line <paramref name="offset"/> is on (before its \r\n / \n).</summary>
    static int LineEnd(string s, int offset)
    {
        int i = Math.Max(0, Math.Min(offset, s.Length));
        while (i < s.Length && s[i] != '\n') i++;
        if (i > 0 && i <= s.Length && i - 1 >= offset && s[i - 1] == '\r') i--;
        return i;
    }

    static string LeadingBlanks(string s, int lineStart)
    {
        int i = lineStart;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.Substring(lineStart, i - lineStart);
    }

    static int SkipBlanks(string s, int i, bool lineBreaksToo)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || (lineBreaksToo && (s[i] == '\r' || s[i] == '\n')))) i++;
        return i;
    }

    static int SkipBlanksBack(string s, int i, bool lineBreaksToo)
    {
        while (i > 0 && (s[i - 1] == ' ' || s[i - 1] == '\t' || (lineBreaksToo && (s[i - 1] == '\r' || s[i - 1] == '\n')))) i--;
        return i;
    }

    /// <summary>Blank, or a `;` comment (what follows a statement on its line; the lexer reads any `;` there as one).</summary>
    static bool IsBlankOrComment(string rest)
    {
        string t = rest.TrimStart(' ', '\t');
        return t.Length == 0 || t[0] == ';';
    }

    /// <summary>
    /// The lines of <paramref name="text"/> with their common indentation replaced by <paramref name="indent"/> and
    /// joined with <paramref name="eol"/>. Blank lines stay empty.
    /// </summary>
    static string Indent(string text, string indent, string eol)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 1 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        int common = int.MaxValue;
        foreach (var l in lines)
        {
            if (l.Trim().Length == 0) continue;
            int n = 0;
            while (n < l.Length && (l[n] == ' ' || l[n] == '\t')) n++;
            common = Math.Min(common, n);
        }
        if (common == int.MaxValue) common = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append(eol);
            string l = lines[i].TrimEnd('\r');
            if (l.Trim().Length == 0) continue;
            sb.Append(indent).Append(l.Substring(Math.Min(common, l.Length)));
        }
        return sb.ToString();
    }
}
