using System;
using System.Collections.Generic;

/// <summary>
/// Gives every node of a freshly parsed tree its source range: the whole text it spans in the original file
/// (leftmost child through closing bracket/brace), as offsets and as line/column (see AstNode.StartOffset).
///
/// Most ranges are set while parsing: each parse function stamps the node it returns with the tokens it consumed
/// (AhkParser.Stamp), and a few constructs set theirs explicitly (keywords that precede a node's anchor token, empty
/// lists, omitted items, errors). This pass fills in the rest — nodes built inside a parse loop (a.b.c chains, binary
/// expressions) — from their anchor token and their children, and makes every range contain its children.
/// </summary>
public static class SourceRanges
{
    /// <summary>Nodes whose anchor token is not part of their own text: their range is their children's.</summary>
    static bool ChildrenOnly(AstNode n)
    {
        switch (n.NodeType)
        {
            case "CaseBody":
            case "DefaultBody":
            case "ForVars":
                return true;
            case "Arguments":
                return n.Metadata == "command"; // anchored at the callee: `MsgBox "x"`
            default:
                return false;
        }
    }

    static long Key(int line, int col) { return ((long)line << 32) | (uint)col; }

    /// <summary>Nodes whose children are statements / members, comments included as their own lines.</summary>
    public static bool HoldsStatements(AstNode n)
    {
        switch (n.NodeType)
        {
            case "Program": case "Block": case "Class": case "CaseBody": case "DefaultBody": case "Include": case "String":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A comment the parser attached to a node it does not lie in: a `(` keeps the comments of the lines before it,
    /// a continued line's comment moves to the end of the logical line, a comment after a one-line statement can be
    /// held back into the next block. Its range is its own text, outside its parent's; comments never widen a parent.
    /// </summary>
    public static bool IsAttachedComment(AstNode n)
    {
        return n != null && n.NodeType == "Comment" && n.Parent != null
               && n.HasRange && n.Parent.HasRange && (n.StartOffset < n.Parent.StartOffset || n.EndOffset > n.Parent.EndOffset);
    }

    public static void Compute(AstNode root, List<Token> tokens, SourceLineMap lineMap)
    {
        if (root == null || tokens == null) return;
        var byPos = new Dictionary<long, Token>(tokens.Count);
        var openers = new List<int[]>(); // start offset, end offset of the matching closer
        var stack = new List<Token>();
        foreach (var t in tokens)
        {
            if (t.StartOffset < 0) continue;
            long k = Key(t.Line, t.Column);
            if (!byPos.ContainsKey(k)) byPos[k] = t;
        }
        // brackets in source order (the token list is, apart from comments moved by line continuation)
        foreach (var t in tokens)
        {
            if (t.StartOffset < 0) continue;
            switch (t.Type)
            {
                case TokenType.LParen: case TokenType.LBracket: case TokenType.LBrace:
                    stack.Add(t);
                    break;
                case TokenType.RParen: case TokenType.RBracket: case TokenType.RBrace:
                    TokenType open = t.Type == TokenType.RParen ? TokenType.LParen : t.Type == TokenType.RBracket ? TokenType.LBracket : TokenType.LBrace;
                    for (int i = stack.Count - 1; i >= 0; i--)
                    {
                        if (stack[i].Type != open) continue;
                        openers.Add(new[] { stack[i].StartOffset, t.EndOffset });
                        stack.RemoveRange(i, stack.Count - i);
                        break;
                    }
                    break;
            }
        }
        openers.Sort((a, b) => a[0].CompareTo(b[0]));
        var brackets = new BracketIndex(openers);
        Visit(root, byPos, brackets, lineMap);
    }

    static void Visit(AstNode n, Dictionary<long, Token> byPos, BracketIndex brackets, SourceLineMap lineMap)
    {
        int s = n.StartOffset, e = n.EndOffset;
        bool explicitRange = s >= 0;
        bool childrenOnly = ChildrenOnly(n);
        int hint = s; // children-only nodes: where an empty one sits
        if (childrenOnly) { s = e = -1; explicitRange = false; }
        else if (explicitRange) { if (e < s) e = s; }
        else
        {
            Token t;
            if (byPos.TryGetValue(Key(n.Line, n.Column), out t))
            {
                bool trivia = t.Type == TokenType.Newline || t.Type == TokenType.EOF
                              || (t.Type == TokenType.Comment && n.NodeType != "Comment" && n.NodeType != "Warning");
                if (!trivia) { s = t.StartOffset; e = t.EndOffset; }
                else if (n.ChildCount == 0) { s = e = t.StartOffset; }
            }
            else if (n.NodeType == "Comment" && lineMap != null && n.Line > 0)
            {
                // a comment found inside a continuation string's raw text: no token of its own
                s = lineMap.ToOffset(n.Line, n.Column);
                e = Math.Min(lineMap.Length, s + (n.Value ?? "").TrimEnd('\r').Length);
            }
        }

        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c == null) continue;
            Visit(c, byPos, brackets, lineMap);
            if (!c.HasRange) continue;
            // comments never widen their parent: the parser may hand a comment to a node it lies outside of (see
            // IsAttachedComment); those inside are covered by the parent's own tokens anyway
            if (c.NodeType == "Comment" && n.NodeType != "String") continue;
            if (s < 0 || c.StartOffset < s) s = c.StartOffset;
            if (e < 0 || c.EndOffset > e) e = c.EndOffset;
        }

        if (childrenOnly && s < 0)
        {
            Token t;
            if (hint >= 0) s = e = hint;
            else if (byPos.TryGetValue(Key(n.Line, n.Column), out t)) s = e = t.StartOffset;
        }
        // a node built from its anchor and children closes every bracket it opens: `a[1` → `a[1]`, `f(x` → `f(x)`
        if (!explicitRange && !childrenOnly && s >= 0) e = brackets.Close(s, e);
        if (s >= 0 && lineMap != null && lineMap.Sections.Count > 0) CloseSections(lineMap.Sections, ref s, ref e);
        if (s >= 0) { n.StartOffset = s; n.EndOffset = Math.Max(s, e); }
    }

    /// <summary>
    /// A joined continuation section is one piece of text: a range that starts before its `(` line and ends inside it
    /// runs through the closing `)` (`x := ⏎ ( ⏎ a ⏎ )`), and one that starts inside it and ends after it starts at `(`.
    /// </summary>
    static void CloseSections(List<int[]> sections, ref int s, ref int e)
    {
        int lo = 0, hi = sections.Count - 1, last = -1; // last section whose `(` is before e
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (sections[mid][0] < e) { last = mid; lo = mid + 1; } else hi = mid - 1;
        }
        if (last >= 0)
        {
            var sec = sections[last];
            if (s < sec[0] && e <= sec[1]) e = sec[1] + 1;
        }
        foreach (var sec in sections)
        {
            if (sec[0] >= s) break;
            if (s > sec[0] && s <= sec[1] && e > sec[1] + 1) { s = sec[0]; break; }
        }
    }

    /// <summary>Sets RangeStart/End line and column from the offsets, for a whole tree (skipping other files' nodes).</summary>
    public static void ApplyLineMap(AstNode n, SourceLineMap lineMap)
    {
        if (n == null || lineMap == null) return;
        if (n.HasRange)
        {
            lineMap.ToLineCol(n.StartOffset, out n.RangeStartLine, out n.RangeStartColumn);
            lineMap.ToLineCol(n.EndOffset, out n.RangeEndLine, out n.RangeEndColumn);
        }
        for (int i = 0; i < n.ChildCount; i++)
        {
            var c = n.GetChild(i);
            if (c != null) ApplyLineMap(c, lineMap);
        }
    }

    /// <summary>Adds <paramref name="delta"/> to every range in a subtree (a sub-parse of part of a token's text).</summary>
    public static void Shift(AstNode n, int delta)
    {
        if (n == null) return;
        if (n.StartOffset >= 0) n.StartOffset += delta;
        if (n.EndOffset >= 0) n.EndOffset += delta;
        for (int i = 0; i < n.ChildCount; i++) Shift(n.GetChild(i), delta);
    }

    /// <summary>Openers sorted by offset with a range-max over their closers' ends (sparse table).</summary>
    sealed class BracketIndex
    {
        readonly int[] _start;
        readonly int[][] _max;

        public BracketIndex(List<int[]> openers)
        {
            int n = openers.Count;
            _start = new int[n];
            var lvl0 = new int[n];
            for (int i = 0; i < n; i++) { _start[i] = openers[i][0]; lvl0[i] = openers[i][1]; }
            var levels = new List<int[]> { lvl0 };
            for (int w = 1; (1 << w) <= n; w++)
            {
                var prev = levels[w - 1];
                var cur = new int[n - (1 << w) + 1];
                for (int i = 0; i < cur.Length; i++) cur[i] = Math.Max(prev[i], prev[i + (1 << (w - 1))]);
                levels.Add(cur);
            }
            _max = levels.ToArray();
        }

        int Max(int lo, int hi) // inclusive indices, lo <= hi
        {
            int w = 0;
            while ((1 << (w + 1)) <= hi - lo + 1) w++;
            return Math.Max(_max[w][lo], _max[w][hi - (1 << w) + 1]);
        }

        int LowerBound(int offset)
        {
            int i = Array.BinarySearch(_start, offset);
            if (i < 0) return ~i;
            while (i > 0 && _start[i - 1] == offset) i--;
            return i;
        }

        /// <summary>The end of [s, e) extended over the closers of every bracket opened inside it.</summary>
        public int Close(int s, int e)
        {
            if (_start.Length == 0) return e;
            for (int guard = 0; guard < 8; guard++)
            {
                int lo = LowerBound(s), hi = LowerBound(e) - 1;
                if (lo > hi) return e;
                int m = Max(lo, hi);
                if (m <= e) return e;
                e = m;
            }
            return e;
        }
    }
}
