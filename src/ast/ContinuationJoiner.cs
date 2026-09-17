using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// AutoHotkey v2 continuation sections are a purely textual transform applied before a line is parsed.
/// Measured against AutoHotkey 2.0.19:
///  - A section starts at a line whose first non-blank character is `(` and which contains no `)`;
///    the rest of that line are options. It ends at the first line whose first non-blank character is `)`.
///  - The section's lines are merged into the preceding code line (comment-only and blank lines in between are
///    skipped) with NO separator, and whatever follows the closing `)` is appended: `x := "a` + `b` + `"`.
///  - The preceding line has its `;` comment removed first. Comment stripping is not quote-aware:
///    `"First part, ; c"` + section `second` + `)"` gives "First part,second".
///  - Options: Join&lt;s&gt; (default a line feed), LTrim / LTrim0 (default: remove the first line's indentation from every
///    line, keeping relative indentation), RTrim0 (default: trailing blanks removed), Comments/Comment/Com/C
///    (strip `;` comments inside), and ` (backticks are literal).
///  - A line feed from joining is whitespace in code (`p` ⏎ `q` is `p q`) but a real line feed inside a string.
/// Joined line feeds are written as <see cref="Nl"/> so the lexer can tell them apart; each merged line keeps its
/// position and the consumed section lines become empty, so every later line keeps its original line number.
/// Sections that belong to a hotkey/hotstring line are left alone (their text is not code).
/// </summary>
public static class ContinuationJoiner
{
    /// <summary>Virtual line feed produced by joining a continuation section.</summary>
    public const char Nl = '\uE000';

    public static string Join(string src)
    {
        SourceMap ignored;
        return Join(src, out ignored);
    }

    /// <summary>
    /// Joins sections and returns a map from offsets in the joined text back to the original source, for every
    /// character that was copied verbatim (untouched lines, the head of a merged line, the text after `)`).
    /// </summary>
    public static string Join(string src, out SourceMap map)
    {
        map = null;
        if (string.IsNullOrEmpty(src) || src.IndexOf('(') < 0) return src;
        var lines = new List<string>(src.Split('\n'));
        var lineStart = new int[lines.Count];
        for (int i = 1; i < lines.Count; i++) lineStart[i] = lineStart[i - 1] + lines[i - 1].Length + 1;
        // Per output line: verbatim pieces as (offset within the new line, original offset, length).
        var pieces = new List<int[]>[lines.Count];
        var sections = new List<int[]>(); // original offsets of each joined section's `(` and `)`
        bool changed = false, inBlockComment = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string ts = line.TrimStart(' ', '\t');
            if (inBlockComment)
            {
                if (ts.StartsWith("*/") || line.TrimEnd().EndsWith("*/")) inBlockComment = false;
                continue;
            }
            if (ts.StartsWith("/*"))
            {
                if (!(ts.Length >= 4 && line.TrimEnd().EndsWith("*/"))) inBlockComment = true;
                continue;
            }
            if (ts.Length == 0 || ts[0] != '(' || ts.IndexOf(')') >= 0) continue;

            int prev = i - 1;
            while (prev >= 0 && IsBlankOrComment(lines[prev])) prev--;
            if (prev < 0 || IsHotLine(lines[prev])) continue;

            int end = i + 1;
            while (end < lines.Count && !lines[end].TrimStart(' ', '\t').StartsWith(")")) end++;
            if (end >= lines.Count) continue; // unterminated: leave for the lexer to report

            var content = new List<string>();
            var contentStart = new List<int>();
            for (int k = i + 1; k < end; k++) { content.Add(lines[k].TrimEnd('\r')); contentStart.Add(lineStart[k]); }
            string prevLine = lines[prev];
            bool cr = prevLine.EndsWith("\r");
            string head = StripComment(prevLine.TrimEnd('\r'));
            // Inside an open string, that string's quote character is literal throughout the section
            // (`v := '` ⏎ `<W T='M'>` ⏎ `)'` is "<W T='M'>"); escapes like `" still work.
            char openQuote = OpenQuoteAtEnd(head);
            List<int[]> runs;
            string joined = ApplyOptions(content, contentStart, ts.Substring(1), openQuote, out runs);
            // In code, a line ending in a word character is followed by a space so the words don't merge
            // (`MsgBox` ⏎ `(` ⏎ `"x"` is MsgBox "x"; `x := y` + `z` is y z) — but `"p"` + `"q"` / `(y)` + `y` / text
            // inside an open string get nothing (AutoHotkey 2.0.19).
            int lead = 0;
            if (openQuote == '\0' && head.Length > 0 && (char.IsLetterOrDigit(head[head.Length - 1]) || head[head.Length - 1] == '_'))
            {
                joined = " " + joined;
                lead = 1;
            }
            string closing = lines[end].TrimEnd('\r').TrimStart(' ', '\t').Substring(1);

            string endLine = lines[end].TrimEnd('\r');
            int closeParen = endLine.IndexOf(')');
            var prevPieces = pieces[prev];
            if (prevPieces == null)
            {
                prevPieces = new List<int[]>();
                if (head.Length > 0) prevPieces.Add(new[] { 0, lineStart[prev], head.Length });
                pieces[prev] = prevPieces;
            }
            else TrimPieces(prevPieces, head.Length); // an earlier join already merged into this line
            // the section's own text (after trimming/escaping) maps back too, flagged so OriginalSpan ignores it
            foreach (var r in runs) prevPieces.Add(new[] { head.Length + lead + r[0], r[1], r[2], 1 });
            int closingAt = head.Length + joined.Length;
            if (closing.Length > 0) prevPieces.Add(new[] { closingAt, lineStart[end] + closeParen + 1, closing.Length });

            sections.Add(new[] { lineStart[i] + (lines[i].Length - lines[i].TrimStart(' ', '\t').Length), lineStart[end] + closeParen });
            lines[prev] = head + joined + closing + (cr ? "\r" : "");
            for (int k = i; k <= end; k++) { lines[k] = lines[k].EndsWith("\r") ? "\r" : ""; pieces[k] = new List<int[]>(); }
            changed = true;
            i = end;
        }
        if (!changed) return src;

        map = new SourceMap();
        map.Sections.AddRange(sections);
        var sb = new StringBuilder(src.Length);
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) { map.Add(sb.Length, lineStart[i] - 1, 1); sb.Append('\n'); } // every line keeps its own line feed
            int at = sb.Length;
            if (pieces[i] == null) map.Add(at, lineStart[i], lines[i].Length);
            else foreach (var p in pieces[i]) map.Add(at + p[0], p[1], p[2], p.Length > 3);
            sb.Append(lines[i]);
        }
        return sb.ToString();
    }

    static void TrimPieces(List<int[]> pieces, int maxEnd)
    {
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var p = pieces[i];
            if (p[0] >= maxEnd) pieces.RemoveAt(i);
            else if (p[0] + p[2] > maxEnd) p[2] = maxEnd - p[0];
        }
    }

    static bool IsBlankOrComment(string line)
    {
        string t = line.Trim();
        return t.Length == 0 || t[0] == ';';
    }

    /// <summary>Hotkey / hotstring lines own their sections as replacement text; the lexer handles those.</summary>
    static bool IsHotLine(string line)
    {
        string t = StripComment(line.TrimEnd('\r')).Trim();
        if (t.Length > 1 && t[0] == ':' && t.IndexOf("::", 1, StringComparison.Ordinal) > 0) return true;
        return t.EndsWith("::");
    }

    /// <summary>Removes a `;` comment (a semicolon at line start or after a blank, not escaped) and the blanks before it.</summary>
    public static string StripComment(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != ';') continue;
            if (i == 0) return "";
            char p = line[i - 1];
            if (p == ' ' || p == '\t') return line.Substring(0, i).TrimEnd(' ', '\t');
        }
        return line;
    }

    /// <summary>The quote character of a string literal left open at the end of <paramref name="line"/>, else '\0'.</summary>
    static char OpenQuoteAtEnd(string line)
    {
        char q = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q != '\0')
            {
                if (c == '`') { i++; continue; }
                if (c == q) q = '\0';
            }
            else if (c == '"' || c == '\'') q = c;
        }
        return q;
    }

    /// <summary>Escapes every unescaped <paramref name="q"/> (existing `x escape pairs are copied untouched).</summary>
    static string EscapeQuote(string s, char q)
    {
        if (s.IndexOf(q) < 0) return s;
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '`' && i + 1 < s.Length) { sb.Append(c).Append(s[i + 1]); i++; }
            else if (c == q) sb.Append('`').Append(q);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The section's text. <paramref name="runs"/> gets (offset in the result, original offset, length) for every run of
    /// characters copied from the source (trimmed blanks, join strings and added escape characters map to nothing).
    /// </summary>
    static string ApplyOptions(List<string> content, List<int> contentStart, string optionText, char openQuote, out List<int[]> runs)
    {
        string join = Nl.ToString();
        bool ltrimAll = false, ltrimNone = false, rtrim = true, comments = false, literalBackticks = false;
        foreach (string raw in optionText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string o = raw;
            if (o.StartsWith("Join", StringComparison.OrdinalIgnoreCase)) join = Unescape(o.Substring(4));
            else if (o.Equals("LTrim", StringComparison.OrdinalIgnoreCase)) { ltrimAll = true; ltrimNone = false; }
            else if (o.Equals("LTrim0", StringComparison.OrdinalIgnoreCase)) { ltrimNone = true; ltrimAll = false; }
            else if (o.Equals("RTrim0", StringComparison.OrdinalIgnoreCase)) rtrim = false;
            else if (o.Equals("RTrim", StringComparison.OrdinalIgnoreCase)) rtrim = true;
            else if (o.Equals("Comments", StringComparison.OrdinalIgnoreCase) || o.Equals("Comment", StringComparison.OrdinalIgnoreCase)
                     || o.Equals("Com", StringComparison.OrdinalIgnoreCase) || o.Equals("C", StringComparison.OrdinalIgnoreCase)) comments = true;
            else if (o == "`") literalBackticks = true;
        }

        var lines = new List<string>();
        var starts = new List<int>();
        for (int li = 0; li < content.Count; li++)
        {
            string s = content[li];
            if (comments)
            {
                if (s.TrimStart(' ', '\t').StartsWith(";")) continue;
                s = StripComment(s);
            }
            lines.Add(s);
            starts.Add(contentStart[li]);
        }

        int firstIndent = 0;
        if (!ltrimAll && !ltrimNone && lines.Count > 0)
            while (firstIndent < lines[0].Length && (lines[0][firstIndent] == ' ' || lines[0][firstIndent] == '\t')) firstIndent++;

        runs = new List<int[]>();
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            string s = lines[i];
            int n = 0;
            if (ltrimAll) { while (n < s.Length && (s[n] == ' ' || s[n] == '\t')) n++; }
            else if (!ltrimNone) { while (n < firstIndent && n < s.Length && (s[n] == ' ' || s[n] == '\t')) n++; }
            int e = s.Length;
            if (rtrim) while (e > n && (s[e - 1] == ' ' || s[e - 1] == '\t')) e--;
            if (i > 0) sb.Append(join);
            // copy s[n, e) with its origins; escape characters added below map to nothing
            var chars = new StringBuilder(e - n + 8);
            var origin = new List<int>(e - n + 8);
            for (int k = n; k < e; k++)
            {
                char c = s[k];
                if (literalBackticks && c == '`') { chars.Append('`'); origin.Add(-1); }
                chars.Append(c); origin.Add(starts[i] + k);
            }
            if (openQuote != '\0')
            {
                var esc = new StringBuilder(chars.Length + 8);
                var escOrigin = new List<int>(origin.Count + 8);
                for (int k = 0; k < chars.Length; k++)
                {
                    char c = chars[k];
                    if (c == '`' && k + 1 < chars.Length) { esc.Append(c).Append(chars[k + 1]); escOrigin.Add(origin[k]); escOrigin.Add(origin[k + 1]); k++; }
                    else if (c == openQuote) { esc.Append('`').Append(c); escOrigin.Add(-1); escOrigin.Add(origin[k]); }
                    else { esc.Append(c); escOrigin.Add(origin[k]); }
                }
                chars = esc; origin = escOrigin;
            }
            int at = sb.Length;
            for (int k = 0; k < origin.Count; k++)
            {
                if (origin[k] < 0) continue;
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && last[0] + last[2] == at + k && last[1] + last[2] == origin[k]) last[2]++;
                else runs.Add(new[] { at + k, origin[k], 1 });
            }
            sb.Append(chars.ToString());
        }
        return sb.ToString();
    }

    static string Unescape(string s)
    {
        return s.Replace("`s", " ").Replace("`t", "\t").Replace("`n", Nl.ToString()).Replace("`r", "").Replace("``", "`");
    }
}

/// <summary>Maps offsets in joined text back to the original source for verbatim-copied ranges.</summary>
public class SourceMap
{
    readonly List<int[]> _seg = new List<int[]>(); // outStart, origStart, length, 1 = section content (ascending outStart)

    /// <summary>Each joined section as the original offsets of its opening `(` and closing `)` (ascending).</summary>
    public readonly List<int[]> Sections = new List<int[]>();

    public void Add(int outStart, int origStart, int length, bool sectionContent = false)
    {
        if (length > 0) _seg.Add(new[] { outStart, origStart, length, sectionContent ? 1 : 0 });
    }

    int Find(int outOffset)
    {
        int lo = 0, hi = _seg.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var s = _seg[mid];
            if (outOffset < s[0]) hi = mid - 1;
            else if (outOffset >= s[0] + s[2]) lo = mid + 1;
            else return mid;
        }
        return ~lo; // not mapped: ~(index of the next segment)
    }

    /// <summary>
    /// Original offset of the character at <paramref name="outOffset"/>, or -1 if it was not copied verbatim from a code
    /// line (the head of a merged line, the text after `)`, untouched lines). Section content does not count.
    /// </summary>
    public int Map(int outOffset)
    {
        int i = Find(outOffset);
        if (i < 0 || _seg[i][3] != 0) return -1;
        return _seg[i][1] + (outOffset - _seg[i][0]);
    }

    /// <summary>
    /// Original offset for a position in the joined text, section content included. A position that maps to nothing
    /// (a join string, an added escape, the end of the text) snaps forward to the next mapped character, or — when
    /// <paramref name="asEnd"/> — back to just after the previous one. <paramref name="origLength"/> caps the result.
    /// </summary>
    public int MapAny(int outOffset, bool asEnd, int origLength)
    {
        if (asEnd)
        {
            if (outOffset <= 0) return 0;
            int j = Find(outOffset - 1);
            if (j >= 0) return _seg[j][1] + (outOffset - 1 - _seg[j][0]) + 1;
            int prev = ~j - 1;
            return prev >= 0 ? _seg[prev][1] + _seg[prev][2] : 0;
        }
        int i = Find(outOffset);
        if (i >= 0) return _seg[i][1] + (outOffset - _seg[i][0]);
        int next = ~i;
        return next < _seg.Count ? _seg[next][1] : origLength;
    }
}

/// <summary>Offsets ↔ 1-based line/column in a source text (lines end at '\n'; a '\r' before it is part of the line).</summary>
public class SourceLineMap
{
    readonly int[] _starts;
    public readonly int Length;
    /// <summary>Continuation sections the lexer joined: offsets of each `(` and `)` line marker (see SourceRanges).</summary>
    public List<int[]> Sections = new List<int[]>();

    public SourceLineMap(string text)
    {
        text = text ?? "";
        Length = text.Length;
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        _starts = starts.ToArray();
    }

    public int LineCount { get { return _starts.Length; } }

    public void ToLineCol(int offset, out int line, out int col)
    {
        if (offset < 0) { line = col = 0; return; }
        if (offset > Length) offset = Length;
        int i = Array.BinarySearch(_starts, offset);
        if (i < 0) i = ~i - 1;
        line = i + 1;
        col = offset - _starts[i] + 1;
    }

    /// <summary>Offset of a 1-based line/column (clamped to the text), or -1 for line &lt; 1.</summary>
    public int ToOffset(int line, int col)
    {
        if (line < 1) return -1;
        if (line > _starts.Length) return Length;
        int o = _starts[line - 1] + Math.Max(0, col - 1);
        int lineEnd = line < _starts.Length ? _starts[line] : Length;
        return Math.Min(o, lineEnd);
    }

    public int LineStart(int line) { return line < 1 ? 0 : line > _starts.Length ? Length : _starts[line - 1]; }
}