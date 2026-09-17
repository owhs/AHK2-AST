using System;
using System.Collections.Generic;

/// <summary>
/// AHK decides what a line is from its first word. An expression statement whose leading operand is a bare name
/// or a plain member chain, followed by an operator, a comma or another operand, is read as a function-call
/// statement instead (`x && f()` is `x(&& f())`, `a.b ?? c` is `a.b(?? c)`, `x "s"` is `x("s")`), and a line
/// starting with a literal (`0, y := 1`, `[1].Length`) is no statement at all. Verified against AHK /Validate:
///   bare name + ` ?` or ` ??`            -> expression (the only safe followers for a bare name)
///   bare name / this / super + any other -> call statement (syntax error or wrong meaning)
///   a.b / this.b + anything              -> call statement
///   x[1] ..., f() ..., f().b ...         -> expression (a chain ending in `]` or `)` is safe)
///   Number / String / Array / Object     -> not a recognised action
/// Transforms (and the minifier's paren-free output) can produce such statements from valid source, e.g. by
/// dropping `(fixed) &&`'s parentheses. The emitters parenthesise the leading operand found here, which keeps the
/// expression meaning and is harmless anywhere else.
/// </summary>
public static class StatementHead
{
    /// <summary>Leading operands, anywhere under <paramref name="root"/>, that must be emitted in parentheses.
    /// The root itself is treated as a statement.</summary>
    public static HashSet<AstNode> Collect(AstNode root)
    {
        var set = new HashSet<AstNode>();
        Walk(root, true, set, delegate (AstNode stmt)
        {
            string follower;
            var head = UnsafeHead(stmt, out follower);
            // Written like this in the source, which AHK accepted: keep it (AHK's full rule has quirks, e.g.
            // `x = 2 ? a : b` and `x "s" ? a : b` are expressions while `x < 2 ? a : b` is not).
            if (head != null && head.SourceHeadFollower != null && head.SourceHeadFollower == (follower ?? "")) return null;
            return head;
        });
        return set;
    }

    /// <summary>Both sets of Collect and CollectLeadingGroups in a single walk (the emitter needs both).</summary>
    public static void CollectBoth(AstNode root, out HashSet<AstNode> heads, out HashSet<AstNode> groups)
    {
        var h = new HashSet<AstNode>();
        var g = new HashSet<AstNode>();
        WalkBoth(root, true, h, g);
        heads = h;
        groups = g;
    }

    static void WalkBoth(AstNode node, bool statement, HashSet<AstNode> heads, HashSet<AstNode> groups)
    {
        if (node == null) return;
        if (statement)
        {
            string follower;
            var head = UnsafeHead(node, out follower);
            if (head != null && !(head.SourceHeadFollower != null && head.SourceHeadFollower == (follower ?? ""))) heads.Add(head);
            var group = LeadingGroup(node);
            if (group != null) groups.Add(group);
        }
        var children = node.ChildNodes;
        for (int i = 0; i < children.Length; i++)
            WalkBoth(children[i], IsStatementSlot(node, i), heads, groups);
    }
    /// <summary>Records on every statement's leading operand what follows it (parser: the tree is the source).</summary>
    public static void MarkSourceHeads(AstNode root)
    {
        Walk(root, true, new HashSet<AstNode>(), delegate (AstNode stmt)
        {
            string follower;
            var head = UnsafeHead(stmt, out follower);
            if (head != null) head.SourceHeadFollower = follower ?? "";
            return null;
        });
    }

    /// <summary>
    /// Parenthesised groups that open a statement line. Such a group must be written on one line: a line that
    /// starts with ( and has no ) is a continuation section to AHK.
    /// </summary>
    public static HashSet<AstNode> CollectLeadingGroups(AstNode root)
    {
        var set = new HashSet<AstNode>();
        Walk(root, true, set, LeadingGroup);
        return set;
    }

    static void Walk(AstNode node, bool statement, HashSet<AstNode> set, Func<AstNode, AstNode> select)
    {
        if (node == null) return;
        if (statement)
        {
            var head = select(node);
            if (head != null) set.Add(head);
        }
        var children = node.ChildNodes;
        for (int i = 0; i < children.Length; i++)
            Walk(children[i], IsStatementSlot(node, i), set, select);
    }

    static AstNode LeadingGroup(AstNode stmt)
    {
        AstNode cur = stmt;
        while (cur != null && cur.ChildCount > 0)
        {
            string t = cur.NodeType;
            if (t == "Grouped") return cur;
            if (t == "MultiStatement" || t == "Sequence" || t == "BinaryExpr" || t == "Ternary" || t == "Concat"
                || t == "Member" || t == "Index" || t == "Call" || t == "PostfixExpr")
                cur = cur.GetChild(0);
            else return null;
        }
        return null;
    }

    /// <summary>True when child <paramref name="i"/> of <paramref name="parent"/> starts a line of its own.</summary>
    static bool IsStatementSlot(AstNode parent, int i)
    {
        switch (parent.NodeType)
        {
            case "Program":
            case "Block":
            case "CaseBody":
            case "DefaultBody":
            case "Include":
                return true;
            case "If":
            case "While":
                return i == 1;
            case "For":
                return i == 2;
            case "Else":
            case "Try":
            case "Catch":
            case "Finally":
            case "Hotkey":
            case "Hotstring":
                return i == 0;
            case "Loop":
            {
                // The body is the last non-Until child; the ones before it are the loop's arguments.
                int last = -1;
                for (int k = 0; k < parent.ChildCount; k++)
                    if (parent.GetChild(k) != null && parent.GetChild(k).NodeType != "Until" && parent.GetChild(k).NodeType != "Else") last = k;
                return i == last;
            }
            default:
                return false;
        }
    }

    /// <summary>The operand to parenthesise when <paramref name="stmt"/> starts a line, or null if it is safe.</summary>
    public static AstNode UnsafeHead(AstNode stmt)
    {
        string follower;
        return UnsafeHead(stmt, out follower);
    }

    public static AstNode UnsafeHead(AstNode stmt, out string follower)
    {
        AstNode cur = stmt;
        follower = null; // what directly follows the leading operand; null = nothing
        while (cur != null)
        {
            switch (cur.NodeType)
            {
                case "MultiStatement":
                case "Sequence":
                    if (cur.ChildCount == 0) return null;
                    if (cur.ChildCount > 1) follower = ",";
                    cur = cur.GetChild(0);
                    continue;
                case "BinaryExpr":
                    if (IsAssignmentOp(cur.Value)) return null; // `x := ...`, `a.b += ...`: the target is fine
                    follower = cur.Value;
                    cur = cur.GetChild(0);
                    continue;
                case "Ternary":
                    follower = "?";
                    cur = cur.GetChild(0);
                    continue;
                case "Concat":
                    if (cur.ChildCount > 1) follower = " ";
                    cur = cur.ChildCount > 0 ? cur.GetChild(0) : null;
                    continue;
            }
            break;
        }
        if (cur == null) return null;

        // cur is the leading primary chain: find its root and whether it is made of `.name` links only.
        AstNode root = cur;
        bool membersOnly = true;
        while (root != null && (root.NodeType == "Member" || root.NodeType == "Index" || root.NodeType == "Call"))
        {
            if (root.NodeType != "Member") membersOnly = false;
            root = root.ChildCount > 0 ? root.GetChild(0) : null;
        }
        if (root == null) return null;

        switch (root.NodeType)
        {
            case "Number":
            case "String":
            case "Array":
            case "Object":
                return cur;
        }
        if (follower == null) return null; // a lone name / `a.b` statement is left as written
        if (root.NodeType != "Identifier" && root.NodeType != "This" && root.NodeType != "Super") return null;
        if (cur == root)
        {
            if (root.NodeType == "Identifier" && (follower == "?" || follower == "??")) return null;
            return cur;
        }
        return membersOnly ? cur : null;
    }

    static bool IsAssignmentOp(string op)
    {
        switch (op)
        {
            case ":=": case "+=": case "-=": case "*=": case "/=": case "//=": case ".=":
            case "|=": case "&=": case "^=": case "<<=": case ">>=": case ">>>=": case "??=":
                return true;
        }
        return false;
    }
}
