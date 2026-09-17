using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public class AhkParser
{
    private List<Token> _tokens;
    private int _pos;
    private GrammarRules _grammar;
    private List<KeyValuePair<string, Token>> _warnings; // message, the token it is about (its range)
    private bool _hotstringXActive = false;

    /// <summary>Line lookup for the lexer's original source (AhkLexer.LineMap). When set, ParseProgram fills in every
    /// node's range line/column; offsets are filled in either way.</summary>
    public SourceLineMap LineMap { get; set; }
    private List<AstNode> _skippedComments = new List<AstNode>();

    private static readonly HashSet<string> KnownExceptionClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Error", "IndexError", "MemberError", "TargetError", "TimeoutError", "TypeError", "ValueError", "ZeroDivError", "OSError", "UninitializedVariableError", "Any"
    };

    public AhkParser(List<Token> tokens, GrammarRules grammar)
    {
        _tokens = tokens;
        _pos = 0;
        _grammar = grammar;
        _warnings = new List<KeyValuePair<string, Token>>();
    }

    private void Warn(string message, Token at)
    {
        _warnings.Add(new KeyValuePair<string, Token>(message, at));
    }

    // -- Source ranges (see SourceRanges) ------------------------------------

    private static bool IsTrivia(Token t)
    {
        return t.Type == TokenType.Newline || t.Type == TokenType.Comment || t.Type == TokenType.EOF;
    }

    /// <summary>
    /// Widens <paramref name="n"/>'s range over the tokens consumed since <paramref name="from"/> (leading/trailing line
    /// breaks and comments excluded). Every parse function that returns a node calls this.
    /// </summary>
    private AstNode Stamp(AstNode n, int from)
    {
        if (n == null) return n;
        int a = from, b = Math.Min(_pos, _tokens.Count) - 1;
        while (a <= b && IsTrivia(_tokens[a])) a++;
        while (b >= a && IsTrivia(_tokens[b])) b--;
        if (a > b) return n;
        int s = _tokens[a].StartOffset, e = _tokens[b].EndOffset;
        if (s < 0 || e < s) return n;
        if (n.StartOffset < 0 || s < n.StartOffset) n.StartOffset = s;
        if (n.EndOffset < 0 || e > n.EndOffset) n.EndOffset = e;
        return n;
    }

    /// <summary>The node's text starts at <paramref name="t"/> (a keyword before its anchor: `else`, `case`, `until`).</summary>
    private static AstNode StartAt(AstNode n, Token t)
    {
        if (n != null && t != null && t.StartOffset >= 0) n.StartOffset = t.StartOffset;
        return n;
    }

    /// <summary>An empty range at the start of <paramref name="t"/>.</summary>
    private static AstNode EmptyAt(AstNode n, Token t)
    {
        if (n != null && t != null && t.StartOffset >= 0) n.StartOffset = n.EndOffset = t.StartOffset;
        return n;
    }

    /// <summary>
    /// An omitted list item (`f(a,,b)`, `[,x]`): an empty range right after the `(` / `[` / `,` before it, or — first in a
    /// list without brackets (`MsgBox ,, 3`, `for , v in x`) — at the comma that follows it.
    /// </summary>
    private AstNode Omitted()
    {
        var n = new AstNode("Omitted", Current.Line, Current.Column);
        int p = _pos - 1;
        while (p >= 0 && IsTrivia(_tokens[p])) p--;
        bool afterSeparator = p >= 0 && (_tokens[p].Type == TokenType.Comma || _tokens[p].Type == TokenType.LParen || _tokens[p].Type == TokenType.LBracket);
        if (afterSeparator && _tokens[p].EndOffset >= 0) n.StartOffset = n.EndOffset = _tokens[p].EndOffset;
        else EmptyAt(n, Current);
        return n;
    }

    /// <summary>The range of the tokens [from, _pos), or false if only trivia was consumed.</summary>
    private bool ConsumedRange(int from, out int start, out int end)
    {
        var probe = new AstNode("", 0, 0);
        Stamp(probe, from);
        start = probe.StartOffset; end = probe.EndOffset;
        return probe.HasRange;
    }

    private Token Current { get { return _pos < _tokens.Count ? _tokens[_pos] : PastEnd(); } }
    private Token Peek(int offset) { int i = _pos + offset; return i < _tokens.Count ? _tokens[i] : PastEnd(); }

    /// <summary>What reading past the last token gives: an EOF at 0:0 whose range is the end of the text.</summary>
    private Token PastEnd()
    {
        var t = new Token(TokenType.EOF, "", 0, 0);
        for (int i = _tokens.Count - 1; i >= 0; i--)
            if (_tokens[i].EndOffset >= 0) { t.StartOffset = t.EndOffset = _tokens[i].EndOffset; break; }
        return t;
    }

    private Token Advance()
    {
        Token t = Current;
        _pos++;
        return t;
    }

    private void SkipNewlines()
    {
        while (Current.Type == TokenType.Newline || Current.Type == TokenType.Comment)
        {
            if (Current.Type == TokenType.Comment)
            {
                _skippedComments.Add(CreateCommentOrWarningNode(Current));
            }
            Advance();
        }
    }

    private void SkipNewlinesOnly()
    {
        while (Current.Type == TokenType.Newline)
            Advance();
    }

    private bool Match(TokenType type)
    {
        SkipNewlines();
        if (Current.Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Look ahead to determine if the current Identifier(... is a function definition.
    /// Pattern: Name(params) { or Name(params) =>
    /// Does NOT consume any tokens.
    /// </summary>
    private bool IsFunctionDefinition()
    {
        // Check if there is space between Identifier and LParen
        Token ident = Current;
        Token lp = Peek(1);
        if (ident.Line != lp.Line || ident.Column + ident.Value.Length < lp.Column)
            return false;

        // Current is Identifier, Peek(1) is LParen
        int i = _pos + 2; // skip Identifier and LParen
        int depth = 1;
        while (i < _tokens.Count && depth > 0)
        {
            if (_tokens[i].Type == TokenType.LParen) depth++;
            else if (_tokens[i].Type == TokenType.RParen) depth--;
            if (depth == 0) break;
            i++;
        }
        if (depth != 0) return false;
        i++; // skip past RParen
        // Skip newlines
        while (i < _tokens.Count && (_tokens[i].Type == TokenType.Newline || _tokens[i].Type == TokenType.Comment))
            i++;
        if (i >= _tokens.Count) return false;
        return _tokens[i].Type == TokenType.LBrace || _tokens[i].Type == TokenType.FatArrow;
    }

    private bool IsClassDefinition()
    {
        if (Current.Type != TokenType.Class)
            return false;

        Token name = Peek(1);
        if (name.Type != TokenType.Identifier)
            return false;

        int i = _pos + 2;
        while (i < _tokens.Count && (_tokens[i].Type == TokenType.Newline || _tokens[i].Type == TokenType.Comment))
        {
            i++;
        }

        if (i >= _tokens.Count)
            return false;

        return _tokens[i].Type == TokenType.LBrace || _tokens[i].Type == TokenType.Extends;
    }

    private Token Expect(TokenType type, string context)
    {
        SkipNewlines();
        if (Current.Type == type)
            return Advance();

        // Error recovery: create error node, skip to recovery point
        Warn(string.Format("Expected {0} in {1} at line {2}:{3}, got {4}",
            type, context, Current.Line, Current.Column, Current.Type), Current);
        return new Token(type, "__missing__", Current.Line, Current.Column);
    }

    // -- Program -----------------------------------------------------------

    public AstNode ParseProgram()
    {
        var program = new AstNode("Program", 1, 1);

        var ranges = new List<Tuple<string, int, int>>();
        var inlinedRangesList = new List<string>();
        var stack = new Stack<Tuple<string, int>>();
        var markerSpans = new Dictionary<int, int[]>(); // begin-marker line -> source range of begin..end marker
        var beginOffsets = new Stack<int>();
        for (int idx = 0; idx < _tokens.Count; idx++)
        {
            var tok = _tokens[idx];
            // Only top-level markers (written at column 1) stand for a file that can be rebuilt into an #Include;
            // an include inlined inside a block (`if dev {` ⏎ `#Include x`) stays inline code.
            if (tok.Type == TokenType.Comment && tok.Value != null && tok.Column <= 1)
            {
                string val = tok.Value.Trim();
                if (val.StartsWith("; --- begin:") && val.EndsWith("---"))
                {
                    int colonIdx = val.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string fileName = val.Substring(colonIdx + 1).Replace("---", "").Trim();
                        stack.Push(Tuple.Create(fileName, tok.Line));
                        beginOffsets.Push(tok.StartOffset);
                    }
                }
                else if (val.StartsWith("; --- end:") && val.EndsWith("---"))
                {
                    if (stack.Count > 0)
                    {
                        var startInfo = stack.Pop();
                        int beginAt = beginOffsets.Pop();
                        if (beginAt >= 0 && tok.EndOffset >= beginAt) markerSpans[startInfo.Item2] = new[] { beginAt, tok.EndOffset };
                        inlinedRangesList.Add(string.Format("{0}:{1}-{2}", startInfo.Item1, startInfo.Item2, tok.Line));
                        ranges.Add(Tuple.Create(startInfo.Item1, startInfo.Item2, tok.Line));
                    }
                }
            }
        }
        if (inlinedRangesList.Count > 0)
        {
            program.Metadata = "inlined_ranges:" + string.Join("|", inlinedRangesList);
        }

        int progGuard = _tokens.Count + 1;
        while (Current.Type != TokenType.EOF && progGuard-- > 0)
        {
            SkipNewlinesOnly();
            if (Current.Type == TokenType.EOF) break;

            // Preserve comments as AST nodes
            if (Current.Type == TokenType.Comment)
            {
                program.AddChild(CreateCommentOrWarningNode(Current));
                Advance();
                continue;
            }

            int before = _pos;
            try
            {
                var stmt = ParseStatement();
                foreach (var comment in _skippedComments)
                {
                    program.AddChild(comment);
                }
                _skippedComments.Clear();

                if (stmt != null)
                    program.AddChild(stmt);
            }
            catch (Exception ex)
            {
                // Error recovery: log error, skip to next line
                var errNode = new AstNode("Error", Current.Line, Current.Column);
                errNode.Value = ex.Message;
                errNode.Metadata = "recovery";
                program.AddChild(errNode);
                SkipToRecovery();
                RangeOverConsumed(errNode, before); // the statement it gave up on, through the recovery point
            }
            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

        if (ranges.Count > 0)
        {
            GroupInlinedIncludes(program, ranges, markerSpans);
        }

        // Source ranges: the program is the whole text; the rest from the tokens
        program.StartOffset = 0;
        program.EndOffset = LineMap != null ? LineMap.Length : (_tokens.Count > 0 ? Math.Max(0, _tokens[_tokens.Count - 1].EndOffset) : 0);
        SourceRanges.Compute(program, _tokens, LineMap);

        // Attach warnings (at the token they are about; Line/Column stay 0:0, the position is in the message)
        foreach (var w in _warnings)
        {
            var warnNode = new AstNode("Warning", 0, 0);
            warnNode.Value = w.Key;
            if (w.Value != null && w.Value.StartOffset >= 0)
            {
                warnNode.StartOffset = w.Value.StartOffset;
                warnNode.EndOffset = w.Value.Type == TokenType.Newline || w.Value.Type == TokenType.EOF ? w.Value.StartOffset : w.Value.EndOffset;
            }
            program.AddChild(warnNode);
        }

        // Scan for Unknown nodes and add warnings
        var unknownWarns = new List<AstNode>();
        CollectUnknownWarnings(program, unknownWarns);
        foreach (var w in unknownWarns)
            program.AddChild(w);

        if (LineMap != null) SourceRanges.ApplyLineMap(program, LineMap);
        StatementHead.MarkSourceHeads(program); // heads written in the source stay as written (AHK accepted them)
        return program;
    }

    /// <summary>Error recovery: the node covers everything consumed since <paramref name="from"/>.</summary>
    private void RangeOverConsumed(AstNode n, int from)
    {
        int s, e;
        if (ConsumedRange(from, out s, out e)) { n.StartOffset = s; n.EndOffset = e; }
        else EmptyAt(n, Current);
    }

    private void CollectUnknownWarnings(AstNode node, List<AstNode> warnings)
    {
        if (node.NodeType == "Unknown")
        {
            var warnNode = new AstNode("Warning", node.Line, node.Column);
            warnNode.Value = string.Format("Unknown/unhandled construct '{0}' at line {1}:{2}",
                node.Value, node.Line, node.Column);
            warnNode.CopyRangeFrom(node);
            warnings.Add(warnNode);
        }
        foreach (var child in node.ChildNodes)
            CollectUnknownWarnings(child, warnings);
    }

    private AstNode CreateCommentOrWarningNode(Token tok)
    {
        string val = tok.Value ?? "";
        string trimmed = val.Trim();
        if (trimmed.StartsWith(";", StringComparison.Ordinal))
        {
            string inner = trimmed.Substring(1).Trim();
            if (inner.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("duplicate include:", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("circular/empty include:", StringComparison.OrdinalIgnoreCase) ||
                inner.StartsWith("Include failed:", StringComparison.OrdinalIgnoreCase))
            {
                string warnVal = inner;
                if (inner.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    warnVal = inner.Substring(8).Trim();
                else if (inner.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    warnVal = inner.Substring(6).Trim();

                var warnNode = new AstNode("Warning", tok.Line, tok.Column);
                warnNode.Value = warnVal;
                warnNode.Metadata = "raw:" + trimmed; // written back exactly as it was (re-emits stay identical)
                return warnNode;
            }
        }
        var comment = new AstNode("Comment", tok.Line, tok.Column);
        comment.Value = tok.Value;
        // a /* ... */ comment spans lines: record where it ends (blank-line layout after it)
        int breaks = 0;
        foreach (char ch in val) if (ch == '\n') breaks++;
        if (breaks > 0) comment.EndLine = tok.Line + breaks;
        return comment;
    }

    // -- Statement ---------------------------------------------------------

    private AstNode ParseStatement() { int from = _pos; return Stamp(ParseStatementCore(), from); }
    private AstNode ParseClassMember() { int from = _pos; return Stamp(ParseClassMemberCore(), from); }
    private AstNode ParseExpression(int minPrec) { int from = _pos; return Stamp(ParseExpressionCore(minPrec), from); }
    private AstNode ParseUnary() { int from = _pos; return Stamp(ParseUnaryCore(), from); }
    private AstNode ParsePrimary() { int from = _pos; return Stamp(ParsePrimaryCore(), from); }
    private AstNode ParseBlock() { int from = _pos; return Stamp(ParseBlockCore(), from); }
    private AstNode ParsePropertyBody() { int from = _pos; return Stamp(ParsePropertyBodyCore(), from); }
    private AstNode ParseParameterList() { int from = _pos; return Stamp(ParseParameterListCore(), from); }
    private AstNode ParseArgumentList() { int from = _pos; return Stamp(ParseArgumentListCore(), from); }
    private AstNode ParseFatArrowFunction() { int from = _pos; return Stamp(ParseFatArrowFunctionCore(), from); }

    private AstNode ParseStatementCore()
    {
        SkipNewlines();
        Token t = Current;

        switch (t.Type)
        {
            case TokenType.Directive:
                return ParseDirective();
            case TokenType.Hotkey:
                return ParseHotkey();
            case TokenType.Hotstring:
                return ParseHotstring();
            case TokenType.If:
                return ParseIf();
            case TokenType.While:
                return ParseWhile();
            case TokenType.For:
                return ParseFor();
            case TokenType.Loop:
                return ParseLoop();
            case TokenType.Return:
                return ParseReturn();
            case TokenType.Class:
                if (IsClassDefinition())
                    return ParseClass();
                else
                    return ParseExpressionStatement();
            case TokenType.Try:
                return ParseTry();
            case TokenType.Switch:
                return ParseSwitch();
            case TokenType.Throw:
                return ParseThrow();
            case TokenType.Until:
                {
                    // `until cond` after a for / while body (Loop consumes its own): a statement that closes the
                    // loop above it, not the variable `until`.
                    Advance();
                    var node = new AstNode("Until", t.Line, t.Column);
                    node.AddChild(ParseExpression(0));
                    return node;
                }
            case TokenType.Break:
                {
                    Advance();
                    var node = new AstNode("Break", t.Line, t.Column);
                    if (Current.Line == t.Line && (Current.Type == TokenType.Identifier || Current.Type == TokenType.Number || IsKeyword(Current.Type)))
                    {
                        var arg = new AstNode(Current.Type == TokenType.Number ? "Number" : "Identifier", Current.Line, Current.Column);
                        arg.Value = Current.Value;
                        Advance();
                        node.AddChild(arg);
                    }
                    return node;
                }
            case TokenType.Continue:
                {
                    Advance();
                    var node = new AstNode("Continue", t.Line, t.Column);
                    if (Current.Line == t.Line && (Current.Type == TokenType.Identifier || Current.Type == TokenType.Number || IsKeyword(Current.Type)))
                    {
                        var arg = new AstNode(Current.Type == TokenType.Number ? "Number" : "Identifier", Current.Line, Current.Column);
                        arg.Value = Current.Value;
                        Advance();
                        node.AddChild(arg);
                    }
                    return node;
                }
            case TokenType.LBrace:
                return ParseBlock();
            case TokenType.Static:
                // Static nested function: `static Name(params) {` / `static Name(params) => expr`
                if ((Peek(1).Type == TokenType.Identifier || IsKeyword(Peek(1).Type)) && Peek(2).Type == TokenType.LParen)
                {
                    int save = _pos;
                    Advance(); // static
                    if (Current.Type == TokenType.Identifier && IsFunctionDefinition())
                    {
                        var fn = ParseNestedFunction();
                        fn.Metadata = "static";
                        fn.Line = t.Line;
                        fn.Column = t.Column;
                        return fn;
                    }
                    _pos = save;
                }
                return ParseDeclaration();
            case TokenType.Global:
            case TokenType.Local:
                return ParseDeclaration();
            default:
                // Label: Identifier followed by Colon (not :=) at statement level
                // (`default:` outside a switch is a label too: `goto default`)
                if ((t.Type == TokenType.Identifier || t.Type == TokenType.Default) && Peek(1).Type == TokenType.Colon
                    && (t.Type == TokenType.Identifier || Peek(2).Type == TokenType.Newline || Peek(2).Type == TokenType.EOF || Peek(2).Type == TokenType.Comment || Peek(2).Line != t.Line))
                {
                    Token labelName = Advance();
                    Advance(); // consume colon
                    var label = new AstNode("Label", labelName.Line, labelName.Column);
                    label.Value = labelName.Value;
                    return label;
                }

                // Check for nested function definition: Name(params) { ... } or Name(params) => expr
                if (t.Type == TokenType.Identifier && Peek(1).Type == TokenType.LParen)
                {
                    if (IsFunctionDefinition())
                        return ParseNestedFunction();
                }
                return ParseExpressionStatement();
        }
    }

    /// <summary>Name(params) { ... } or Name(params) => expr, at statement level (Current = Name).</summary>
    private AstNode ParseNestedFunction()
    {
        Token name = Advance();
        var method = new AstNode("Method", name.Line, name.Column);
        method.Value = name.Value;
        method.AddChild(ParseParameterList());
        SkipNewlines();
        if (Current.Type == TokenType.LBrace)
            method.AddChild(ParseBlock());
        else if (Current.Type == TokenType.FatArrow)
        {
            Advance();
            var body = new AstNode("FatArrowBody", Current.Line, Current.Column);
            body.AddChild(ParseExpression(0));
            method.AddChild(body);
        }
        return method;
    }

    // -- Specific Statement Parsers ----------------------------------------

    private AstNode ParseDirective()
    {
        Token t = Advance();
        var node = new AstNode("Directive", t.Line, t.Column);
        node.Value = t.Value;

        // `#HotIf <expression>`: the condition is real code (it reads variables and calls functions), so it is
        // parsed into the Directive's child and emitted from it; a trailing comment is kept in Metadata.
        string hotIfExpr, hotIfComment;
        if (SplitHotIf(t.Value, out hotIfExpr, out hotIfComment))
        {
            var subTokens = new AhkLexer(hotIfExpr).Tokenize();
            subTokens.RemoveAll(x => x.Type == TokenType.EOF || x.Type == TokenType.Newline);
            if (subTokens.Count > 0)
            {
                int exprAt = t.Value.IndexOf(hotIfExpr, 6, StringComparison.Ordinal);
                if (t.StartOffset >= 0 && exprAt >= 0) ShiftTokens(subTokens, t.StartOffset + exprAt);
                var sub = new AhkParser(subTokens, _grammar);
                var expr = sub.ParseExpression(0);
                if (expr != null) SourceRanges.Compute(expr, subTokens, null);
                if (expr != null && sub._pos >= subTokens.Count && !ContainsErrorNode(expr))
                {
                    node.AddChild(expr);
                    node.Metadata = "hotif" + (hotIfComment != null ? ";" + hotIfComment : "");
                }
            }
        }

        if (t.Value != null && t.Value.StartsWith("#Hotstring", StringComparison.OrdinalIgnoreCase))
        {
            string opts = t.Value.Substring("#Hotstring".Length).Trim();
            string[] parts = opts.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Equals("X0", StringComparison.OrdinalIgnoreCase))
                {
                    _hotstringXActive = false;
                }
                else if (part.Equals("X", StringComparison.OrdinalIgnoreCase))
                {
                    _hotstringXActive = true;
                }
            }
        }

        return node;
    }

    /// <summary>
    /// Parses a standalone expression (the inside of a `%...%` deref, a directive's condition). Null unless the
    /// whole text is one clean expression.
    /// </summary>
    public static AstNode ParseExpressionText(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Trim().Length == 0) return null;
        var tokens = new AhkLexer(code.Trim()).Tokenize();
        tokens.RemoveAll(x => x.Type == TokenType.EOF || x.Type == TokenType.Newline);
        if (tokens.Count == 0) return null;
        var p = new AhkParser(tokens, new GrammarRules());
        AstNode expr;
        try { expr = p.ParseExpression(0); }
        catch { return null; }
        return expr != null && p._pos >= tokens.Count && !ContainsErrorNode(expr) ? expr : null;
    }

    /// <summary>`#HotIf expr ; comment` → expr and comment (a `;` after a blank, outside quotes, starts it).</summary>
    private static bool SplitHotIf(string directive, out string expr, out string comment)
    {
        expr = comment = null;
        if (directive == null || directive.Length <= 6 || !directive.StartsWith("#HotIf", StringComparison.OrdinalIgnoreCase)
            || (directive[6] != ' ' && directive[6] != '\t')) return false;
        string rest = directive.Substring(6);
        char quote = '\0';
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (quote != '\0') { if (c == '`') i++; else if (c == quote) quote = '\0'; continue; }
            if (c == '"' || c == '\'') quote = c;
            else if (c == ';' && i > 0 && (rest[i - 1] == ' ' || rest[i - 1] == '\t'))
            {
                comment = rest.Substring(i).TrimEnd();
                rest = rest.Substring(0, i);
                break;
            }
        }
        expr = rest.Trim();
        return expr.Length > 0;
    }

    /// <summary>Moves a sub-lexed text's token offsets to where that text sits in the file.</summary>
    private static void ShiftTokens(List<Token> tokens, int delta)
    {
        foreach (var k in tokens)
        {
            if (k.StartOffset < 0) continue;
            k.StartOffset += delta;
            k.EndOffset += delta;
        }
    }

    private static bool ContainsErrorNode(AstNode n)
    {
        if (n == null) return false;
        if (n.NodeType == "Error" || n.NodeType == "Unknown") return true;
        foreach (var c in n.ChildNodes) if (ContainsErrorNode(c)) return true;
        return false;
    }

    private AstNode ParseHotkey()
    {
        Token t = Advance();
        var node = new AstNode("Hotkey", t.Line, t.Column);
        node.Value = t.Value;

        // Remap: `Origin::Destination` where the rest of the line is exactly one key name (see AhkKeyNames).
        var remap = TryParseRemap(t);
        if (remap != null) return remap;

        // Check if the body is inline (on the same line)
        bool isInline = false;
        int idx = _pos;
        while (idx < _tokens.Count && _tokens[idx].Type == TokenType.Comment)
        {
            idx++;
        }
        if (idx < _tokens.Count && _tokens[idx].Type != TokenType.Newline && _tokens[idx].Type != TokenType.EOF)
        {
            isInline = true;
        }

        if (isInline)
        {
            node.Metadata = "inline";
            node.AddChild(ParseStatement());
        }
        else
        {
            SkipNewlines();
            if (Current.Type == TokenType.LBrace)
                node.AddChild(ParseBlock());
            else if (Current.Type != TokenType.EOF && Current.Type != TokenType.Newline)
                node.AddChild(ParseStatement());
        }
        return node;
    }

    /// <summary>
    /// The same-line tokens after `Origin::`, if they are contiguous (no whitespace) and spell a remap destination,
    /// become Remap(Value = origin){ KeyName = destination }. The destination is key text, never code.
    /// </summary>
    private AstNode TryParseRemap(Token hotkeyToken)
    {
        int p = _pos;
        var sb = new StringBuilder();
        Token prev = null;
        while (p < _tokens.Count)
        {
            Token tk = _tokens[p];
            if (tk.Type == TokenType.Newline || tk.Type == TokenType.EOF || tk.Type == TokenType.Comment || tk.Line != hotkeyToken.Line) break;
            if (prev != null && !Adjacent(prev, tk)) return null; // `a Up`, `Send "x"` …: code, not a key
            sb.Append(tk.Value);
            prev = tk;
            p++;
        }
        if (prev == null || !AhkKeyNames.IsRemapDestination(sb.ToString())) return null;

        var remap = new AstNode("Remap", hotkeyToken.Line, hotkeyToken.Column);
        remap.Value = hotkeyToken.Value;
        var dest = new AstNode("KeyName", _tokens[_pos].Line, _tokens[_pos].Column);
        dest.Value = sb.ToString();
        remap.AddChild(dest);
        int destFrom = _pos;
        _pos = p;
        Stamp(dest, destFrom);
        return remap;
    }

    private AstNode ParseHotstring()
    {
        Token t = Advance();
        var node = new AstNode("Hotstring", t.Line, t.Column);
        node.Value = t.Value;

        string options, trigger, replacement;
        bool isExec = IsHotstringExecutable(t.Value, out options, out trigger, out replacement);

        if (isExec)
        {
            node.Value = ":" + options + ":" + trigger + "::";
            node.Metadata = "inline";

            var subLexer = new AhkLexer(replacement);
            var subTokens = subLexer.Tokenize();
            if (subTokens.Count > 0 && subTokens[subTokens.Count - 1].Type == TokenType.EOF)
            {
                subTokens.RemoveAt(subTokens.Count - 1);
            }

            // Extract trailing comment if present
            Token commentToken = null;
            for (int i = subTokens.Count - 1; i >= 0; i--)
            {
                if (subTokens[i].Type == TokenType.Comment)
                {
                    commentToken = subTokens[i];
                    subTokens.RemoveAt(i);
                    break;
                }
            }

            if (commentToken != null)
            {
                node.Metadata = "inline;" + commentToken.Value;
            }

            if (t.StartOffset >= 0) ShiftTokens(subTokens, t.StartOffset + t.Value.Length - replacement.Length); // the text after `::`
            var subParser = new AhkParser(subTokens, _grammar);
            subParser._hotstringXActive = this._hotstringXActive;
            var bodyNode = subParser.ParseStatement();
            if (bodyNode != null)
            {
                SourceRanges.Compute(bodyNode, subTokens, null);
                node.AddChild(bodyNode);
            }
        }
        else
        {
            if (t.Value != null && t.Value.Trim().EndsWith("::"))
            {
                bool isInline = false;
                int idx = _pos;
                while (idx < _tokens.Count && _tokens[idx].Type == TokenType.Comment)
                {
                    idx++;
                }
                if (idx < _tokens.Count && _tokens[idx].Type != TokenType.Newline && _tokens[idx].Type != TokenType.EOF)
                {
                    isInline = true;
                }

                if (isInline)
                {
                    node.Metadata = "inline";
                    node.AddChild(ParseStatement());
                }
                else
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.LBrace)
                        node.AddChild(ParseBlock());
                    else if (Current.Type != TokenType.EOF && Current.Type != TokenType.Newline)
                        node.AddChild(ParseStatement());
                }
            }
        }
        return node;
    }

    private bool IsHotstringExecutable(string hsValue, out string options, out string trigger, out string replacement)
    {
        options = "";
        trigger = "";
        replacement = "";

        if (string.IsNullOrEmpty(hsValue) || !hsValue.StartsWith(":"))
            return false;

        int secondColon = hsValue.IndexOf(':', 1);
        if (secondColon < 0)
            return false;

        options = hsValue.Substring(1, secondColon - 1);

        int endColons = hsValue.IndexOf("::", secondColon + 1);
        if (endColons < 0)
            return false;

        trigger = hsValue.Substring(secondColon + 1, endColons - (secondColon + 1));
        replacement = hsValue.Substring(endColons + 2);

        bool localX = false;
        bool localX0 = false;
        
        string optUpper = options.ToUpperInvariant();
        if (optUpper.Contains("X0"))
        {
            localX0 = true;
        }
        else if (optUpper.Contains("X"))
        {
            localX = true;
        }

        bool isExecutable = false;
        if (localX)
            isExecutable = true;
        else if (localX0)
            isExecutable = false;
        else
            isExecutable = _hotstringXActive;

        return isExecutable;
    }

    private bool IsAssignmentOperator(TokenType type)
    {
        return TokenKinds.IsAssignment(type);
    }

    private bool IsValidLValue(AstNode node)
    {
        if (node == null) return false;
        return node.NodeType == "Identifier" || node.NodeType == "Member" || node.NodeType == "Index";
    }

    private bool IsParenConditionFollowedByOperator()
    {
        if (Current.Type != TokenType.LParen)
            return false;

        int depth = 0;
        int i = _pos;
        while (i < _tokens.Count)
        {
            if (_tokens[i].Type == TokenType.LParen) depth++;
            else if (_tokens[i].Type == TokenType.RParen)
            {
                depth--;
                if (depth == 0)
                {
                    i++; // move to token after RParen
                    // Skip any comments
                    while (i < _tokens.Count && _tokens[i].Type == TokenType.Comment)
                        i++;
                    if (i < _tokens.Count)
                    {
                        TokenType nextType = _tokens[i].Type;
                        // `if (a && b)()` / `(x).y` / `(x)[1]`: directly adjacent postfix continues the expression
                        if ((nextType == TokenType.LParen || nextType == TokenType.LBracket || nextType == TokenType.Dot)
                            && Adjacent(_tokens[i - 1], _tokens[i]))
                            return true;
                        return GetPrecedence(nextType) > 0;
                    }
                    break;
                }
            }
            i++;
        }
        return false;
    }

    private AstNode ParseIf()
    {
        Token t = Advance(); // consume 'if'
        var node = new AstNode("If", t.Line, t.Column);

        // Condition
        if (Current.Type != TokenType.LBrace)
        {
            if (Current.Type == TokenType.LParen && !IsParenConditionFollowedByOperator())
            {
                node.AddChild(ParsePrimary());
            }
            else
            {
                node.AddChild(ParseExpression(0));
            }
        }

        // Then block
        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());

        // Else
        if (AtContinuation(TokenType.Else))
        {
            Token elseTok = Advance();
            SkipNewlines();
            var elseBranch = StartAt(new AstNode("Else", Current.Line, Current.Column), elseTok);
            elseBranch.AddChild(ParseStatementOrBlock());
            node.AddChild(elseBranch);
        }

        return node;
    }

    private AstNode ParseWhile()
    {
        Token t = Advance();
        var node = new AstNode("While", t.Line, t.Column);
        if (Current.Type != TokenType.LBrace)
        {
            if (Current.Type == TokenType.LParen && !IsParenConditionFollowedByOperator())
            {
                node.AddChild(ParsePrimary());
            }
            else
            {
                node.AddChild(ParseExpression(0));
            }
        }
        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());
        ParseLoopTail(node);
        return node;
    }

    /// <summary>
    /// What may follow a loop's body (Loop, For, While): `until cond`, then `else` (run when the loop had no
    /// iterations). Both belong to the loop node, so nothing can end up between them and the body.
    /// </summary>
    private void ParseLoopTail(AstNode loopNode)
    {
        if (AtContinuation(TokenType.Until))
        {
            Token untilTok = Advance();
            var untilNode = StartAt(new AstNode("Until", Current.Line, Current.Column), untilTok);
            untilNode.AddChild(ParseExpression(0));
            loopNode.AddChild(untilNode);
        }
        if (AtContinuation(TokenType.Else))
        {
            Token elseTok = Advance();
            var elseNode = StartAt(new AstNode("Else", Current.Line, Current.Column), elseTok);
            SkipNewlines();
            elseNode.AddChild(ParseStatementOrBlock());
            loopNode.AddChild(elseNode);
        }
    }

    /// <summary>
    /// Optional continuation of the statement just parsed (`else`, `catch`, `finally`, `until`): true — positioned
    /// on it — only when the next significant token is <paramref name="type"/>. Otherwise nothing is consumed, so
    /// the blank lines and comments after the statement stay where they are (a consuming look-ahead used to buffer
    /// those comments and emit them in front of the statement).
    /// </summary>
    private bool AtContinuation(TokenType type)
    {
        int p = _pos;
        while (p < _tokens.Count && (_tokens[p].Type == TokenType.Newline || _tokens[p].Type == TokenType.Comment)) p++;
        if (p >= _tokens.Count || _tokens[p].Type != type) return false;
        SkipNewlines();
        return true;
    }
    /// <summary>At `(`: true if the reserved word `in` occurs directly inside this pair of parentheses.</summary>
    private bool ParenContainsTopLevelIn()
    {
        int depth = 0;
        for (int p = _pos; p < _tokens.Count; p++)
        {
            var tk = _tokens[p];
            if (tk.Type == TokenType.LParen || tk.Type == TokenType.LBracket || tk.Type == TokenType.LBrace) depth++;
            else if (tk.Type == TokenType.RParen || tk.Type == TokenType.RBracket || tk.Type == TokenType.RBrace)
            {
                if (--depth <= 0) return false;
            }
            else if (tk.Type == TokenType.Newline || tk.Type == TokenType.EOF) return false;
            else if (depth == 1 && tk.Type == TokenType.Identifier && string.Equals(tk.Value, "in", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>`in` is reserved in v2 (never a variable name), so it always ends a for-loop's variable list.</summary>
    private bool IsForInKeyword()
    {
        return Current.Type == TokenType.Identifier && string.Equals(Current.Value, "in", StringComparison.OrdinalIgnoreCase);
    }

    private AstNode ParseFor()
    {
        Token t = Advance();
        var node = new AstNode("For", t.Line, t.Column);

        // Variable slots: comma-separated, each `[&]name`, `*` or empty (omitted), terminated by the
        // reserved word `in`. Omitted slots are kept: `for k, v, in x` passes 3 vars to __Enum.
        // Parenthesised header: `for (i, v in list)`: the `(` wraps the whole header, not a variable.
        bool parenHeader = Current.Type == TokenType.LParen && ParenContainsTopLevelIn();
        if (parenHeader)
        {
            Advance();
            node.Metadata = "paren";
        }
        var vars = new AstNode("ForVars", t.Line, t.Column);
        vars.StartOffset = Current.StartOffset; // where an empty list sits
        while (!IsForInKeyword())
        {
            if (Current.Type == TokenType.Comma)
            {
                vars.AddChild(Omitted());
                Advance();
                if (IsForInKeyword()) vars.AddChild(Omitted());
                continue;
            }

            if (Current.Type == TokenType.BitwiseAnd)
            {
                Advance();
                if (!(Current.Type == TokenType.Identifier || IsKeyword(Current.Type))) break;
                var v = new AstNode("Identifier", Current.Line, Current.Column);
                v.Value = Advance().Value;
                v.Metadata = "byref";
                vars.AddChild(v);
            }
            else if (Current.Type == TokenType.Star)
            {
                var v = new AstNode("Identifier", Current.Line, Current.Column);
                v.Value = "*";
                v.Metadata = "variadic";
                vars.AddChild(v);
                Advance();
            }
            else if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
            {
                var v = new AstNode("Identifier", Current.Line, Current.Column);
                v.Value = Advance().Value;
                vars.AddChild(v);
            }
            else break; // malformed; the collection parse below reports it

            if (Current.Type != TokenType.Comma) break;
            Advance();
            if (IsForInKeyword()) vars.AddChild(Omitted()); // trailing `k, v, in`
        }

        node.AddChild(vars);

        // 'in' keyword (parsed as identifier)
        if (Current.Type == TokenType.Identifier && Current.Value.ToLower() == "in")
            Advance();

        node.AddChild(ParseExpression(0)); // collection
        if (parenHeader) Expect(TokenType.RParen, "for");
        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());
        ParseLoopTail(node); // for ... [until] [else]
        return node;
    }

    private AstNode ParseLoop()
    {
        Token t = Advance();
        var node = new AstNode("Loop", t.Line, t.Column);

        // Skip any comments on the same line
        while (Current.Type == TokenType.Comment && Current.Line == t.Line)
        {
            _skippedComments.Add(CreateCommentOrWarningNode(Current)); // kept (it used to be dropped)
            Advance();
        }

        // Check for Loop variants: Parse, Files, Reg, Read (must be on the same line)
        if (Current.Type == TokenType.Identifier && Current.Line == t.Line)
        {
            string variant = Current.Value.ToLower();
            // `Loop Files.Length` / `Loop Parse(...)`: a name used in an expression, not the loop variant
            Token afterVariant = Peek(1);
            bool usedAsValue = (afterVariant.Type == TokenType.Dot || afterVariant.Type == TokenType.LParen || afterVariant.Type == TokenType.LBracket)
                && Adjacent(Current, afterVariant);
            if (!usedAsValue && (variant == "parse" || variant == "files" || variant == "reg" || variant == "read"))
            {
                node.Value = Current.Value; // store variant name
                Advance(); // consume variant keyword

                // First argument can be space-separated or comma-separated
                // e.g., "Loop Parse t {" or "Loop Parse, t {"
                if (Current.Type == TokenType.Comma) { Advance(); node.Metadata = "comma"; } // `Loop Parse, t` (kept 1:1)
                if (Current.Type != TokenType.LBrace && Current.Type != TokenType.Newline
                    && Current.Type != TokenType.EOF)
                {
                    node.AddChild(ParseExpression(0));
                }

                // Parse additional comma-separated arguments
                while (Current.Type == TokenType.Comma)
                {
                    Advance(); // consume comma
                    SkipNewlines();
                    if (Current.Type != TokenType.LBrace && Current.Type != TokenType.Newline
                        && Current.Type != TokenType.EOF && Current.Type != TokenType.Comma)
                    {
                        node.AddChild(ParseExpression(0));
                    }
                    else
                    {
                        // Omitted argument
                        node.AddChild(Omitted());
                    }
                }

                SkipNewlines();
                node.AddChild(ParseStatementOrBlock());

                ParseLoopTail(node); // [until] [else]


                return node;
            }
        }

        // Standard Loop [count] { body } (must be on the same line)
        if (Current.Type != TokenType.LBrace && Current.Type != TokenType.Newline && Current.Type != TokenType.EOF && Current.Line == t.Line)
        {
            node.AddChild(ParseExpression(0));
        }

        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());

        ParseLoopTail(node); // [until] [else]


        return node;
    }

    private AstNode ParseReturn()
    {
        Token t = Advance();
        var node = new AstNode("Return", t.Line, t.Column);

        if (Current.Type != TokenType.Newline && Current.Type != TokenType.EOF
            && Current.Type != TokenType.RBrace && Current.Type != TokenType.Comment)
        {
            node.AddChild(ParseExpression(0));
        }

        return node;
    }

    private AstNode ParseClass()
    {
        Token t = Advance();
        var node = new AstNode("Class", t.Line, t.Column);

        Token name = Expect(TokenType.Identifier, "class");
        node.Value = name.Value;

        // extends
        SkipNewlines();
        if (Current.Type == TokenType.Extends)
        {
            Advance();
            var extendsNode = new AstNode("Extends", Current.Line, Current.Column);
            int baseFrom = _pos;
            string baseName = Expect(TokenType.Identifier, "extends").Value;
            // Support dotted names: extends WebView2.Base
            while (Current.Type == TokenType.Dot)
            {
                Advance();
                if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                    baseName += "." + Advance().Value;
            }
            extendsNode.Value = baseName;
            Stamp(extendsNode, baseFrom); // the base class name (`WebView2.Base`), without the keyword
            node.AddChild(extendsNode);
        }

        // Class body
        SkipNewlines();
        Expect(TokenType.LBrace, "class body");

        int classGuard = _tokens.Count;
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && classGuard-- > 0)
        {
            SkipNewlinesOnly();
            if (Current.Type == TokenType.RBrace) break;

            // Preserve comments as AST nodes
            if (Current.Type == TokenType.Comment)
            {
                node.AddChild(CreateCommentOrWarningNode(Current));
                Advance();
                continue;
            }

            int before = _pos;
            try
            {
                // Class members: methods, properties, static
                var member = ParseClassMember();
                foreach (var comment in _skippedComments)
                {
                    node.AddChild(comment);
                }
                _skippedComments.Clear();

                if (member != null)
                    node.AddChild(member);
            }
            catch (Exception ex)
            {
                var err = new AstNode("Error", Current.Line, Current.Column);
                err.Value = ex.Message;
                node.AddChild(err);
                SkipToRecovery();
                RangeOverConsumed(err, before);
            }
            // Safety: if nothing was consumed, force advance to prevent infinite loop
            if (_pos == before) Advance();
        }

        if (Current.Type == TokenType.RBrace) node.EndLine = Current.Line; // the closing brace's line (blank-line layout)
        Expect(TokenType.RBrace, "class body");
        return node;
    }

    private AstNode ParseClassMemberCore()
    {
        SkipNewlines();

        bool isStatic = false;
        if (Current.Type == TokenType.Static)
        {
            isStatic = true;
            Advance();
            SkipNewlines();
        }

        // Nested class (or static class)
        if (Current.Type == TokenType.Class && IsClassDefinition())
        {
            var cls = ParseClass();
            if (isStatic) cls.Metadata = "static";
            return cls;
        }

        // Method or property - also accept keywords as member names (catch, try, finally, throw, etc.)
        if (Current.Type == TokenType.Identifier || (IsKeyword(Current.Type) && Current.Type != TokenType.Static))
        {
            Token name = Advance();

            // Method: Name(params) { }
            if (Current.Type == TokenType.LParen)
            {
                var method = new AstNode("Method", name.Line, name.Column);
                method.Value = name.Value;
                if (isStatic) method.Metadata = "static";

                method.AddChild(ParseParameterList());
                SkipNewlines();
                if (Current.Type == TokenType.LBrace)
                    method.AddChild(ParseBlock());
                else if (Current.Type == TokenType.FatArrow)
                {
                    Advance();
                    var body = new AstNode("FatArrowBody", Current.Line, Current.Column);
                    body.AddChild(ParseExpression(0));
                    method.AddChild(body);
                }

                return method;
            }

            // Indexed property: Name[params] => expr or Name[params] { get/set }
            if (Current.Type == TokenType.LBracket)
            {
                int paramsFrom = _pos;
                Advance(); // consume [
                var prop = new AstNode("Property", name.Line, name.Column);
                prop.Value = name.Value;
                if (isStatic) prop.Metadata = "static";

                // Parse index parameters
                var indexParams = new AstNode("Parameters", Current.Line, Current.Column);
                int idxGuard = _tokens.Count;
                while (Current.Type != TokenType.RBracket && Current.Type != TokenType.EOF && idxGuard-- > 0)
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.RBracket) break;

                    if (Current.Type == TokenType.Star)
                    {
                        var p = new AstNode("Parameter", Current.Line, Current.Column);
                        p.Value = "*";
                        p.Metadata = "variadic";
                        indexParams.AddChild(p);
                        Advance();
                    }
                    else if (Current.Type == TokenType.Identifier || Current.Type == TokenType.This)
                    {
                        var p = new AstNode("Parameter", Current.Line, Current.Column);
                        p.Value = Advance().Value;
                        if (Current.Type == TokenType.Star) { Advance(); p.Metadata = "variadic"; }
                        indexParams.AddChild(p);
                    }
                    else Advance();

                    if (Current.Type == TokenType.Comma) Advance();
                }
                Expect(TokenType.RBracket, "indexed property");
                Stamp(indexParams, paramsFrom); // `[a, b]`, like `(a, b)` of a method
                prop.AddChild(indexParams);

                SkipNewlines();
                if (Current.Type == TokenType.FatArrow)
                {
                    Advance();
                    prop.AddChild(ParseExpression(0));
                }
                else if (Current.Type == TokenType.LBrace)
                {
                    prop.AddChild(ParsePropertyBody());
                }
                return prop;
            }

            // Property with => (fat arrow getter)
            if (Current.Type == TokenType.FatArrow)
            {
                Advance();
                var prop = new AstNode("Property", name.Line, name.Column);
                prop.Value = name.Value;
                if (isStatic) prop.Metadata = "static";
                prop.AddChild(ParseExpression(0));
                return prop;
            }

            // Property with { get { } set { } } or { get => expr, set => expr }
            if (Current.Type == TokenType.LBrace)
            {
                var prop = new AstNode("Property", name.Line, name.Column);
                prop.Value = name.Value;
                if (isStatic) prop.Metadata = "static";
                prop.AddChild(ParsePropertyBody());
                return prop;
            }

            // Dotted property path: static Prototype.Name := value
            if (Current.Type == TokenType.Dot)
            {
                string fullName = name.Value;
                while (Current.Type == TokenType.Dot)
                {
                    Advance();
                    if (Current.Type == TokenType.Identifier || Current.Type == TokenType.This || Current.Type == TokenType.Super)
                        fullName += "." + Advance().Value;
                }
                if (Current.Type == TokenType.ColonAssign)
                {
                    Advance();
                    var dotAssign = new AstNode("StaticAssign", name.Line, name.Column);
                    dotAssign.Value = fullName;
                    if (isStatic) dotAssign.Metadata = "static";
                    dotAssign.AddChild(ParseExpression(0));
                    // Handle comma-separated declarations: static a.b := 1, c.d := 2
                    while (AtListComma())
                    {
                        Advance();
                        SkipNewlines();
                        var item = ParseClassVarChainItem(isStatic);
                        if (item == null) break;
                        dotAssign.AddChild(item);
                    }
                    return dotAssign;
                }
                var dotDecl = new AstNode("Declaration", name.Line, name.Column);
                dotDecl.Value = fullName;
                if (isStatic) dotDecl.Metadata = "static";
                return dotDecl;
            }

            // Assignment: static Name := value
            if (Current.Type == TokenType.ColonAssign)
            {
                Advance();
                var assign = new AstNode("StaticAssign", name.Line, name.Column);
                assign.Value = name.Value;
                if (isStatic) assign.Metadata = "static";
                assign.AddChild(ParseExpression(0));
                // Handle comma-separated: static x := 1, y := 2, z := 3
                while (AtListComma()) // also on a continuation line starting with , (after comment lines)
                {
                    Advance();
                    SkipNewlines();
                    var item = ParseClassVarChainItem(isStatic);
                    if (item == null) break;
                    assign.AddChild(item);
                }
                return assign;
            }

            // Just a name - bare declaration
            var decl = new AstNode("Declaration", name.Line, name.Column);
            decl.Value = name.Value;
            if (isStatic) decl.Metadata = "static";
            return decl;
        }

        // A directive may sit in a class body (`#DllLoad x.dll`); it applies to the script as usual.
        if (Current.Type == TokenType.Directive && !isStatic)
            return ParseDirective();

        // Fallback - unrecognized class member
        var unknown = new AstNode("Unknown", Current.Line, Current.Column);
        unknown.Value = Current.Value;
        Warn(string.Format("Unrecognized class member '{0}' at line {1}:{2}",
            Current.Value, Current.Line, Current.Column), Current);
        Advance();
        return unknown;
    }

    private AstNode ParseParameterListCore()
    {
        var node = new AstNode("Parameters", Current.Line, Current.Column);
        Expect(TokenType.LParen, "parameters");

        int paramGuard = _tokens.Count;
        while (Current.Type != TokenType.RParen && Current.Type != TokenType.EOF && paramGuard-- > 0)
        {
            SkipNewlines();
            if (Current.Type == TokenType.RParen) break;

            int before = _pos;
            var param = new AstNode("Parameter", Current.Line, Current.Column);

            // Variadic: params*
            bool isByRef = false;
            if (Current.Type == TokenType.BitwiseAnd)
            {
                isByRef = true;
                Advance();
            }

            // Variadic discard: (*) or name*
            if (Current.Type == TokenType.Star)
            {
                Advance();
                param.Value = "*";
                param.Metadata = "variadic";
                node.AddChild(param);
            }
            else if (Current.Type == TokenType.Identifier || Current.Type == TokenType.This
                || IsKeyword(Current.Type))
            {
                param.Value = Advance().Value;
                if (isByRef) param.Metadata = "byref";

                // Default value
                if (Current.Type == TokenType.ColonAssign)
                {
                    Advance();
                    param.AddChild(ParseExpression(0));
                }

                // Variadic
                if (Current.Type == TokenType.Star)
                {
                    Advance();
                    param.Metadata = "variadic";
                }

                // Optional parameter: name?
                if (Current.Type == TokenType.Ternary)
                {
                    Advance();
                    if (param.Metadata == null) param.Metadata = "optional";
                    else param.Metadata += ",optional";
                }

                node.AddChild(param);
            }
            else
            {
                // Unexpected token in parameter list - skip it to prevent infinite loop
                Advance();
            }
            Stamp(param, before); // `&name := 1`, `args*`, `opt?`

            if (Current.Type == TokenType.Comma)
                Advance();

            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

        Expect(TokenType.RParen, "parameters");
        return node;
    }
    private AstNode ParsePropertyBodyCore()
    {
        Token t = Advance(); // consume {
        var block = new AstNode("Block", t.Line, t.Column);

        int guard = _tokens.Count;
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && guard-- > 0)
        {
            SkipNewlines();
            if (Current.Type == TokenType.RBrace) break;

            int before = _pos;

            // get/set accessor
            if (Current.Type == TokenType.Identifier &&
                (Current.Value.ToLower() == "get" || Current.Value.ToLower() == "set"))
            {
                Token acc = Advance();
                var accessor = new AstNode("Method", acc.Line, acc.Column);
                accessor.Value = acc.Value.ToLower();

                // get/set may have parameter list: set(value) { }
                if (Current.Type == TokenType.LParen)
                    accessor.AddChild(ParseParameterList());

                SkipNewlines();
                if (Current.Type == TokenType.FatArrow)
                {
                    Advance();
                    var body = new AstNode("FatArrowBody", Current.Line, Current.Column);
                    body.AddChild(ParseExpression(0));
                    accessor.AddChild(body);
                }
                else if (Current.Type == TokenType.LBrace)
                {
                    accessor.AddChild(ParseBlock());
                }

                block.AddChild(accessor);
            }
            else
            {
                // Unexpected content in property body - skip
                Advance();
            }

            if (_pos == before) Advance();
        }

        if (Current.Type == TokenType.RBrace) block.EndLine = Current.Line; // the closing brace's line (blank-line layout)
        Expect(TokenType.RBrace, "property body");
        return block;
    }

    private AstNode ParseTry()
    {
        Token t = Advance();
        var node = new AstNode("Try", t.Line, t.Column);
        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());

        while (true)
        {
            if (!AtContinuation(TokenType.Catch))
                break;

            Token catchTok = Advance();
            var catchNode = new AstNode("Catch", catchTok.Line, catchTok.Column);

            // catch [ExceptionClass1, ExceptionClass2, ...] [as OutputVar]
            // Only parse type/variable if it's on the SAME line as 'catch' (not after a newline)
            bool catchHasNewline = (Current.Type == TokenType.Newline || Current.Type == TokenType.Comment || Current.Line > catchTok.Line);
            SkipNewlines();
            if (!catchHasNewline && (Current.Type == TokenType.Identifier || IsKeyword(Current.Type)))
            {
                var list = new List<string>();
                string outputVar = null;
                bool seenAs = false;

                while (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                {
                    Token identTok = Advance();
                    string val = identTok.Value;

                    if (val.ToLower() == "as")
                    {
                        seenAs = true;
                        SkipNewlines();
                        if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                        {
                            outputVar = Advance().Value;
                        }
                        else
                        {
                            Warn(string.Format("Expected catch variable after 'as' at line {0}:{1}", Current.Line, Current.Column), Current);
                        }
                        break;
                    }

                    list.Add(val);

                    int commaPos = _pos;
                    SkipNewlines();
                    if (Current.Type == TokenType.Comma)
                    {
                        Advance(); // consume comma
                        SkipNewlines();
                    }
                    else
                    {
                        if (Current.Type != TokenType.Identifier || Current.Value.ToLower() != "as")
                        {
                            _pos = commaPos; // restore position
                            break;
                        }
                    }
                }

                if (seenAs)
                {
                    catchNode.Value = string.Join(", ", list);
                    catchNode.Metadata = outputVar;
                }
                else
                {
                    if (list.Count > 1)
                    {
                        catchNode.Value = string.Join(", ", list);
                    }
                    else if (list.Count == 1)
                    {
                        string name = list[0];
                        bool isClass = KnownExceptionClasses.Contains(name) || char.IsUpper(name[0]);
                        if (isClass)
                        {
                            catchNode.Value = name;
                        }
                        else
                        {
                            catchNode.Metadata = name;
                        }
                    }
                }
            }

            SkipNewlines();
            catchNode.AddChild(ParseStatementOrBlock());
            node.AddChild(catchNode);
        }

        if (AtContinuation(TokenType.Else))
        {
            Token elseTok = Advance();
            var elseNode = StartAt(new AstNode("Else", Current.Line, Current.Column), elseTok);
            SkipNewlines();
            elseNode.AddChild(ParseStatementOrBlock());
            node.AddChild(elseNode);
        }

        if (AtContinuation(TokenType.Finally))
        {
            Token finallyTok = Advance();
            var finallyNode = StartAt(new AstNode("Finally", Current.Line, Current.Column), finallyTok);
            SkipNewlines();
            finallyNode.AddChild(ParseStatementOrBlock());
            node.AddChild(finallyNode);
        }

        return node;
    }

    private AstNode ParseSwitch()
    {
        Token t = Advance();
        var node = new AstNode("Switch", t.Line, t.Column);

        if (Current.Type != TokenType.LBrace)
        {
            node.AddChild(ParseExpression(0));

            // Handle optional case-sensitivity flag: switch value, 0 { ... }
            if (Current.Type == TokenType.Comma)
            {
                Advance(); // consume comma
                SkipNewlines();
                if (Current.Type != TokenType.LBrace)
                {
                    var csNode = ParseExpression(0);
                    node.Metadata = csNode.Value ?? "0"; // store case-sense flag
                }
            }
        }

        SkipNewlines();
        Expect(TokenType.LBrace, "switch body");

        int switchGuard = _tokens.Count;
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && switchGuard-- > 0)
        {
            SkipNewlines();
            if (Current.Type == TokenType.RBrace) break;

            int beforeSwitch = _pos;
            if (Current.Type == TokenType.Case)
            {
                Token caseTok = Advance();
                var caseNode = StartAt(new AstNode("Case", Current.Line, Current.Column), caseTok);

                // Parse comma-separated case values: case 1, 2, 3:
                caseNode.AddChild(ParseExpression(0));
                while (AtListComma())
                {
                    Advance();
                    SkipNewlines(); // `case "a",` ⏎ `"b":`
                    if (Current.Type == TokenType.Colon) break; // trailing comma: `case 'a', 'b',:`
                    caseNode.AddChild(ParseExpression(0));
                }

                Expect(TokenType.Colon, "case");

                // Case body: statements until next case/default/}
                var body = new AstNode("CaseBody", Current.Line, Current.Column);
                if (_pos > 0) body.StartOffset = _tokens[_pos - 1].EndOffset; // empty: right after the colon
                int caseGuard = _tokens.Count;
                while (Current.Type != TokenType.Case && Current.Type != TokenType.Default
                    && Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && caseGuard-- > 0)
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.Case || Current.Type == TokenType.Default
                        || Current.Type == TokenType.RBrace) break;
                    int beforeCase = _pos;
                    body.AddChild(ParseStatement());
                    if (_pos == beforeCase) Advance();
                }
                caseNode.AddChild(body);
                node.AddChild(caseNode);
            }
            else if (Current.Type == TokenType.Default)
            {
                Token defaultTok = Advance();
                Expect(TokenType.Colon, "default");
                var defNode = StartAt(new AstNode("Default", Current.Line, Current.Column), defaultTok);
                if (_pos > 0) defNode.EndOffset = _tokens[_pos - 1].EndOffset; // `default:` itself
                var body = new AstNode("DefaultBody", Current.Line, Current.Column);
                if (_pos > 0) body.StartOffset = _tokens[_pos - 1].EndOffset;
                int defGuard = _tokens.Count;
                // `default:` needn't be last: its body ends at the next case too.
                while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && defGuard-- > 0)
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.RBrace || Current.Type == TokenType.Case || Current.Type == TokenType.Default) break;
                    int beforeDef = _pos;
                    body.AddChild(ParseStatement());
                    if (_pos == beforeDef) Advance();
                }
                defNode.AddChild(body);
                node.AddChild(defNode);
            }
            else
            {
                // Error recovery
                Advance();
            }
            if (_pos == beforeSwitch) Advance();
        }

        if (Current.Type == TokenType.RBrace) node.EndLine = Current.Line; // the closing brace's line (blank-line layout)
        Expect(TokenType.RBrace, "switch body");
        return node;
    }

    private AstNode ParseThrow()
    {
        Token t = Advance();
        var node = new AstNode("Throw", t.Line, t.Column);
        if (Current.Type != TokenType.Newline && Current.Type != TokenType.EOF
            && Current.Type != TokenType.RBrace && Current.Type != TokenType.Comment)
        {
            node.AddChild(ParseExpression(0));
        }
        return node;
    }

    /// <summary>
    /// `name := expr` gives the value (RHS) as the child, as before. Any other assignment operator
    /// (`global Counter += delta`, valid in AHK) gives BinaryExpr(op, name, expr): read as "the value assigned"
    /// it is still exactly right (x := (x += d) ≡ x += d), and the emitter writes it back as `x += d`.
    /// </summary>
    private AstNode ParseDeclarationInitializer(Token nameTok)
    {
        if (Current.Type == TokenType.ColonAssign)
        {
            Advance();
            return ParseExpression(0);
        }
        if (IsAssignmentOperator(Current.Type))
        {
            Token op = Advance();
            var bin = new AstNode("BinaryExpr", op.Line, op.Column);
            bin.Value = op.Value;
            bin.AddChild(new AstNode("Identifier", nameTok.Line, nameTok.Column) { Value = nameTok.Value });
            bin.AddChild(ParseExpression(0));
            return bin;
        }
        return null;
    }

    private AstNode ParseDeclaration()
    {
        Token scope = Advance();
        var node = new AstNode("Declaration", scope.Line, scope.Column);
        node.Metadata = scope.Value.ToLower();

        // Parse first variable
        if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
        {
            Token nameTok = Advance();
            node.Value = nameTok.Value;
            var init = ParseDeclarationInitializer(nameTok);
            if (init != null) node.AddChild(init);

            // Handle comma-separated additional variables: global a, b, c := 1
            while (Current.Type == TokenType.Comma)
            {
                Advance(); // consume comma
                SkipNewlines();
                if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                {
                    var extra = new AstNode("Declaration", Current.Line, Current.Column);
                    extra.Metadata = node.Metadata;
                    Token extraTok = Advance();
                    extra.Value = extraTok.Value;
                    var extraInit = ParseDeclarationInitializer(extraTok);
                    if (extraInit != null) extra.AddChild(extraInit);
                    node.AddChild(extra);
                }
            }
        }

        return node;
    }

    private AstNode ParseBlockCore()
    {
        Token t = Advance(); // consume {
        var block = new AstNode("Block", t.Line, t.Column);

        int blockGuard = _tokens.Count;
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && blockGuard-- > 0)
        {
            SkipNewlinesOnly();
            if (Current.Type == TokenType.RBrace) break;

            // Preserve comments as AST nodes
            if (Current.Type == TokenType.Comment)
            {
                block.AddChild(CreateCommentOrWarningNode(Current));
                Advance();
                continue;
            }

            int before = _pos;
            try
            {
                var stmt = ParseStatement();
                foreach (var comment in _skippedComments)
                {
                    block.AddChild(comment);
                }
                _skippedComments.Clear();

                if (stmt != null)
                    block.AddChild(stmt);
            }
            catch (Exception ex)
            {
                var err = new AstNode("Error", Current.Line, Current.Column);
                err.Value = ex.Message;
                block.AddChild(err);
                SkipToRecovery();
                RangeOverConsumed(err, before);
            }
            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

        if (Current.Type == TokenType.RBrace) block.EndLine = Current.Line; // the closing brace's line (blank-line layout)
        Expect(TokenType.RBrace, "block");
        return block;
    }

    private AstNode ParseStatementOrBlock()
    {
        SkipNewlines();
        if (Current.Type == TokenType.LBrace)
            return ParseBlock();
        return ParseStatement();
    }

    // -- Expression Parser (Pratt / Precedence Climbing) -------------------

    /// <summary>
    /// Keyword tokens that, in the middle of an expression, can only be variable names (`"Class: " class`).
    /// Deliberately excludes keywords that may follow an expression on the same line (else, until, catch, …).
    /// </summary>
    private static bool IsKeywordUsableAsVariable(TokenType type)
    {
        return type == TokenType.Class || type == TokenType.Extends || type == TokenType.New
            || type == TokenType.Global || type == TokenType.Local || type == TokenType.Static;
    }

    /// <summary>
    /// True if the list continues with a comma: on this line, or as a continuation line that starts with a comma
    /// (possibly after comment lines). On true, Current is that comma; on false, the position is unchanged.
    /// </summary>
    private bool AtListComma()
    {
        if (Current.Type == TokenType.Comma) return true;
        int save = _pos;
        int saveSkipped = _skippedComments.Count;
        SkipNewlines();
        if (Current.Type == TokenType.Comma) return true;
        _pos = save;
        if (_skippedComments.Count > saveSkipped) _skippedComments.RemoveRange(saveSkipped, _skippedComments.Count - saveSkipped);
        return false;
    }

    /// <summary>
    /// One item after a comma in a class variable declaration: `name(.name)* := expr` is another class variable
    /// assignment (`static types := Map(), types.CaseSense := false` sets this.types.CaseSense), a lone `name` is a
    /// bare declaration, anything else is an expression evaluated in the initializer.
    /// </summary>
    private AstNode ParseClassVarChainItem(bool isStatic)
    {
        if (Current.Type == TokenType.Identifier)
        {
            int p = _pos + 1;
            while (p + 1 < _tokens.Count && _tokens[p].Type == TokenType.Dot && (_tokens[p + 1].Type == TokenType.Identifier || IsKeyword(_tokens[p + 1].Type)))
                p += 2;
            if (p < _tokens.Count && _tokens[p].Type == TokenType.ColonAssign)
            {
                Token first = Current;
                string name = Advance().Value;
                while (_pos < p) { Advance(); name += "." + Advance().Value; }
                Advance(); // :=
                var a = new AstNode("StaticAssign", first.Line, first.Column);
                a.Value = name;
                if (isStatic) a.Metadata = "static";
                a.AddChild(ParseExpression(0));
                return a;
            }
            TokenType after = p < _tokens.Count ? _tokens[p].Type : TokenType.EOF;
            if (p == _pos + 1 && (after == TokenType.Comma || after == TokenType.Newline || after == TokenType.EOF || after == TokenType.Comment))
            {
                Token nm = Advance();
                var d = new AstNode("Declaration", nm.Line, nm.Column);
                d.Value = nm.Value;
                if (isStatic) d.Metadata = "static";
                return d;
            }
        }
        if (Current.Type == TokenType.Newline || Current.Type == TokenType.EOF) return null;
        return ParseExpression(0);
    }

    private static bool Adjacent(Token a, Token b)
    {
        return a != null && b != null && a.Line == b.Line && a.Column + (a.Value ?? "").Length == b.Column;
    }

    /// <summary>
    /// Tokens that can only continue an expression (binary/assignment/ternary operators). After `Name `, anything
    /// else (including `-`, `+`, `&`, `!`, `~`, `++`, `--`, `not`, `(`, `[`, `{`, `,`) starts a call argument.
    /// Verified against AutoHotkey 2.0: `f - 1` and `y ++` are calls; `f ? a : b`, `f . x`, `f * 2` are not.
    /// </summary>
    private static bool IsExpressionContinuation(Token t)
    {
        if (TokenKinds.IsAssignment(t.Type)) return true;
        switch (t.Type)
        {
            case TokenType.Dot: case TokenType.DotDot: case TokenType.Star: case TokenType.Slash: case TokenType.IntDiv: case TokenType.Power:
            case TokenType.Equal: case TokenType.NotEqual: case TokenType.StrictEqual: case TokenType.StrictNotEqual:
            case TokenType.Less: case TokenType.Greater: case TokenType.LessEqual: case TokenType.GreaterEqual: case TokenType.RegexEqual:
            case TokenType.LogicalAnd: case TokenType.LogicalOr: case TokenType.BitwiseOr: case TokenType.BitwiseXor:
            case TokenType.ShiftLeft: case TokenType.ShiftRight: case TokenType.UnsignedShiftRight:
            case TokenType.Ternary: case TokenType.Colon: case TokenType.NullCoalesce: case TokenType.FatArrow: case TokenType.Is:
                return true;
            case TokenType.Identifier:
                return string.Equals(t.Value, "in", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Value, "contains", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    /// <summary>
    /// AHK v2 function-call statement: `Name args` / `obj.Method args` / bare `Name`. The callee (a name or an
    /// unbroken a.b.c chain) must be followed by whitespace or the end of the statement, and the next token must not
    /// be an expression continuation. Produces Call(callee, Arguments{command}) so the call is a real call for every
    /// analysis, while the emitter keeps the paren-less form. Arguments follow AHK: `f ,, 5` has two omitted
    /// leading args; a trailing comma adds none.
    /// </summary>
    private AstNode TryParseCallStatement()
    {
        Token first = Current;
        bool thisOrSuper = first.Type == TokenType.This || first.Type == TokenType.Super;
        if (first.Type != TokenType.Identifier && !thisOrSuper) return null;
        // `this.x.Method args` / `super.Method args`: this/super only as the start of a member chain
        if (thisOrSuper && !(Peek(1).Type == TokenType.Dot && Adjacent(first, Peek(1)))) return null;
        if (string.Equals(first.Value, "goto", StringComparison.OrdinalIgnoreCase)) return null; // label name, not an expression
        if (IsExpressionContinuation(first)) return null; // `in` / `contains`

        int p = _pos;
        while (p + 2 < _tokens.Count && _tokens[p + 1].Type == TokenType.Dot && Adjacent(_tokens[p], _tokens[p + 1])
               && (_tokens[p + 2].Type == TokenType.Identifier || IsKeyword(_tokens[p + 2].Type)) && Adjacent(_tokens[p + 1], _tokens[p + 2]))
            p += 2;

        Token last = _tokens[p];
        Token next = p + 1 < _tokens.Count ? _tokens[p + 1] : null;
        bool ends = next == null || next.Type == TokenType.Newline || next.Type == TokenType.EOF
                    || next.Type == TokenType.Comment || next.Type == TokenType.RBrace || next.Line != last.Line;
        if (!ends && (Adjacent(last, next) || IsExpressionContinuation(next))) return null;

        // Build the callee exactly like the postfix parser would.
        AstNode callee = first.Type == TokenType.This ? new AstNode("This", first.Line, first.Column)
            : first.Type == TokenType.Super ? new AstNode("Super", first.Line, first.Column)
            : new AstNode("Identifier", first.Line, first.Column) { Value = first.Value };
        Advance();
        while (_pos < p)
        {
            Advance(); // .
            Token member = Advance();
            var memberNode = new AstNode("Member", member.Line, member.Column);
            memberNode.Value = member.Value;
            memberNode.AddChild(callee);
            callee = memberNode;
        }

        var call = new AstNode("Call", first.Line, first.Column);
        call.Metadata = "command";
        call.AddChild(callee);
        var args = new AstNode("Arguments", first.Line, first.Column);
        args.Metadata = "command";
        args.StartOffset = _tokens[_pos - 1].EndOffset; // no arguments: an empty range after the callee
        call.AddChild(args);
        if (ends) return call;

        bool expectArg = true;
        while (true)
        {
            if (Current.Type == TokenType.Comma)
            {
                if (expectArg) args.AddChild(Omitted());
                Advance();
                expectArg = true;
                continue;
            }
            if (Current.Type == TokenType.Newline || Current.Type == TokenType.EOF || Current.Type == TokenType.Comment || Current.Type == TokenType.RBrace)
            {
                // Continuation both ways: a line ending with a comma continues on the next line (`f 1,` ⏎ `2`),
                // and so does a next line starting with a comma. Only at EOF does a trailing comma add nothing.
                int savePos = _pos;
                int saveSkipped = _skippedComments.Count;
                SkipNewlines();
                if (Current.Type == TokenType.Comma) continue;
                if (expectArg && Current.Type != TokenType.EOF && Current.Type != TokenType.RBrace) continue;
                _pos = savePos;
                if (_skippedComments.Count > saveSkipped) _skippedComments.RemoveRange(saveSkipped, _skippedComments.Count - saveSkipped);
                break;
            }
            if (!expectArg) break;
            int before = _pos;
            args.AddChild(ParseExpression(0));
            if (_pos == before) break; // nothing parseable: leave it to the statement loop
            expectArg = false;
        }
        return call;
    }

    private AstNode ParseExpressionStatement()
    {
        // `Goto Label`: a statement naming a label, not an expression (`Goto("Label")` / `Goto(expr)` stay calls).
        if (Current.Type == TokenType.Identifier && string.Equals(Current.Value, "goto", StringComparison.OrdinalIgnoreCase))
        {
            Token g = Current, label = Peek(1), after = Peek(2);
            if (label != null && label.Line == g.Line && !Adjacent(g, label)
                && (label.Type == TokenType.Identifier || IsKeyword(label.Type))
                && (after == null || after.Line != label.Line || after.Type == TokenType.Newline || after.Type == TokenType.EOF
                    || after.Type == TokenType.Comment || after.Type == TokenType.RBrace))
            {
                Advance();
                Advance();
                return new AstNode("Goto", g.Line, g.Column) { Value = label.Value };
            }
        }

        var callStatement = TryParseCallStatement();
        if (callStatement != null) return callStatement;

        var expr = ParseExpression(0);

        // Check for comma continuation starting on the next line
        int savePos = _pos;
        int saveSkipped = _skippedComments.Count;
        SkipNewlines();
        bool hasCommaContinuation = (Current.Type == TokenType.Comma);
        if (!hasCommaContinuation)
        {
            _pos = savePos; // restore if not continuing
            if (_skippedComments.Count > saveSkipped)
            {
                _skippedComments.RemoveRange(saveSkipped, _skippedComments.Count - saveSkipped);
            }
        }

        // Multi-statement comma: x := 1, y := 2
        // Also handles command-style calls with omitted args: FuncName ,, arg2
        if (Current.Type == TokenType.Comma)
        {
            var multi = new AstNode("MultiStatement", expr.Line, expr.Column);
            multi.AddChild(expr);
            while (Current.Type == TokenType.Comma)
            {
                Advance(); // consume comma
                SkipNewlines();
                if (Current.Type == TokenType.Newline || Current.Type == TokenType.EOF) break;
                // Consecutive commas = omitted item
                if (Current.Type == TokenType.Comma)
                {
                    multi.AddChild(Omitted());
                    continue;
                }
                multi.AddChild(ParseExpression(0));
                AtListComma(); // every item may be followed by a continuation line starting with a comma
            }
            return multi;
        }

        return expr;
    }

    private AstNode ParseExpressionCore(int minPrec)
    {
        var left = ParseUnary();

        while (true)
        {
            // Skip newlines only if next token is an operator (continuation)
            int savePos = _pos;
            int saveSkipped = _skippedComments.Count;
            SkipNewlines();

            bool didOverridePrecedence = false;
            int prec = GetPrecedence(Current.Type);
            if (prec < minPrec || prec == 0)
            {
                if (prec < minPrec && IsAssignmentOperator(Current.Type) && IsValidLValue(left))
                {
                    didOverridePrecedence = true;
                }
                else
                {
                    _pos = savePos; // restore if we shouldn't continue
                    if (_skippedComments.Count > saveSkipped)
                    {
                        _skippedComments.RemoveRange(saveSkipped, _skippedComments.Count - saveSkipped);
                    }

                    // Inside ( ) / [ ] the lexer drops line breaks but keeps comments: `("a" ; note` ⏎ `"b")` still
                    // concatenates. A comment directly followed by an operand (no Newline token) is such a case.
                    if (minPrec <= 11 && Current.Type == TokenType.Comment)
                    {
                        int q = _pos;
                        while (q < _tokens.Count && _tokens[q].Type == TokenType.Comment) q++;
                        if (q < _tokens.Count && _tokens[q].Type != TokenType.Newline && _tokens[q].Type != TokenType.EOF
                            && _tokens[q].Type != TokenType.RParen && _tokens[q].Type != TokenType.RBracket && _tokens[q].Type != TokenType.Comma)
                        {
                            while (Current.Type == TokenType.Comment) { _skippedComments.Add(CreateCommentOrWarningNode(Current)); Advance(); }
                        }
                    }
                    if (minPrec <= 11 && Current.Type != TokenType.Newline
                        && Current.Type != TokenType.EOF && Current.Type != TokenType.Comment)
                    {
                        Token prev = _pos > 0 ? _tokens[_pos - 1] : null;
                        if (prev != null)
                        {
                            TokenType ct = Current.Type;
                            if (ct == TokenType.String || ct == TokenType.Number || ct == TokenType.Identifier
                                || ct == TokenType.LParen || ct == TokenType.This || ct == TokenType.Super
                                || IsKeywordUsableAsVariable(ct))
                            {
                                int beforeConcat = _pos;
                                var concatRight = ParseExpression(12);
                                if (_pos > beforeConcat) // only if tokens were consumed
                                {
                                    Token cprev = _tokens[beforeConcat - 1];
                                    Token cnext = _tokens[beforeConcat];
                                    bool hasSpace = cprev.Line != cnext.Line || cprev.Column + cprev.Value.Length < cnext.Column;

                                    var concatNode = new AstNode("Concat", left.Line, left.Column);
                                    concatNode.Value = hasSpace ? " " : "";
                                    concatNode.Metadata = hasSpace ? "space" : "nospace";
                                    concatNode.AddChild(left);
                                    concatNode.AddChild(concatRight);
                                    left = concatNode;
                                    continue;
                                }
                            }
                        }
                    }

                    break;
                }
            }

            Token op = Advance();

            // Ternary or Unset Modifier
            if (op.Type == TokenType.Ternary)
            {
                // If it's the unset modifier (e.g. `param?`), the next token is typically a comma or closing paren/bracket.
                // We can check if the next token is NOT an expression start (or specifically Comma/RParen/RBracket).
                if (Current.Type == TokenType.Comma || Current.Type == TokenType.RParen || Current.Type == TokenType.RBracket || Current.Type == TokenType.Newline || Current.Type == TokenType.EOF)
                {
                    var unsetNode = new AstNode("UnsetModifier", op.Line, op.Column);
                    unsetNode.AddChild(left);
                    left = unsetNode;
                    continue;
                }

                var then = ParseExpression(0);
                Expect(TokenType.Colon, "ternary");
                var elseExpr = ParseExpression(0);
                var ternary = new AstNode("Ternary", op.Line, op.Column);
                ternary.AddChild(left);
                ternary.AddChild(then);
                ternary.AddChild(elseExpr);
                left = ternary;
                continue;
            }

            // Assignment operators are right-associative
            bool rightAssoc = IsRightAssociative(op.Type);
            var right = ParseExpression(rightAssoc ? prec : prec + 1);

            var binary = new AstNode("BinaryExpr", op.Line, op.Column);
            binary.Value = op.Value;
            binary.AddChild(left);
            binary.AddChild(right);

            if (didOverridePrecedence)
            {
                // implicit: AHK's own grouping of the unparenthesised source (`a && b := 1` is `a && (b := 1)`)
                var grouped = new AstNode("Grouped", op.Line, op.Column) { Metadata = "implicit" };
                grouped.AddChild(binary);
                left = grouped;
            }
            else
            {
                left = binary;
            }
        }

        return left;
    }

    private AstNode ParseUnaryCore()
    {
        SkipNewlines();
        Token t = Current;

        // Unary operators
        if (t.Type == TokenType.LogicalNot || t.Type == TokenType.BitwiseNot
            || t.Type == TokenType.Minus || t.Type == TokenType.Plus
            || t.Type == TokenType.Star || t.Type == TokenType.BitwiseAnd
            || t.Type == TokenType.Increment || t.Type == TokenType.Decrement)
        {
            Advance();
            var node = new AstNode("UnaryExpr", t.Line, t.Column);
            node.Value = t.Value;
            var operand = ParseUnary();

            if (IsValidLValue(operand) && IsAssignmentOperator(Current.Type))
            {
                Token op = Advance();
                var right = ParseExpression(0);
                var assign = new AstNode("BinaryExpr", op.Line, op.Column);
                assign.Value = op.Value;
                assign.AddChild(operand);
                assign.AddChild(right);

                var grouped = new AstNode("Grouped", op.Line, op.Column) { Metadata = "implicit" }; // `!x := y` is `!(x := y)`
                grouped.AddChild(assign);
                node.AddChild(grouped);
            }
            else
            {
                node.AddChild(operand);
            }
            return node;
        }

        return ParsePostfix();
    }

    private AstNode ParsePostfix()
    {
        var node = ParsePrimary();

        while (true)
        {
            // Method call: node(args)
            if (Current.Type == TokenType.LParen)
            {
                // In AHK, there must be NO space between the expression and the open parenthesis for a call.
                if (_pos > 0)
                {
                    Token prev = _tokens[_pos - 1];
                    if (prev.Line != Current.Line || prev.Column + prev.Value.Length < Current.Column)
                    {
                        break;
                    }
                }

                // Check for: obj.method (params) => body
                // where the parens are both the call's arg list and the fat arrow's param list
                if (IsFatArrowCallArg())
                {
                    var call = new AstNode("Call", Current.Line, Current.Column);
                    call.AddChild(node);
                    var args = new AstNode("Arguments", Current.Line, Current.Column);
                    args.AddChild(ParseFatArrowFunction());
                    call.AddChild(args);
                    node = call;
                    continue;
                }

                var callNode = new AstNode("Call", Current.Line, Current.Column);
                callNode.AddChild(node);
                callNode.AddChild(ParseArgumentList());
                node = callNode;
                continue;
            }

            // Index: node[index, ...]
            if (Current.Type == TokenType.LBracket)
            {
                Advance();
                var index = new AstNode("Index", Current.Line, Current.Column);
                index.AddChild(node);

                // Parse comma-separated index expressions (supports multi-dim)
                int idxGuard = _tokens.Count;
                while (Current.Type != TokenType.RBracket && Current.Type != TokenType.EOF && idxGuard-- > 0)
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.RBracket) break;

                    // Handle empty/omitted args: obj[,,2]
                    if (Current.Type == TokenType.Comma)
                    {
                        index.AddChild(Omitted());
                        Advance();
                        continue;
                    }

                    int before = _pos;
                    index.AddChild(ParseExpression(0));
                    if (Current.Type == TokenType.Comma) Advance();
                    if (_pos == before) Advance();
                }

                Expect(TokenType.RBracket, "index");
                node = index;
                continue;
            }

            // Member access: node.member (only if followed by identifier/keyword, and no space after the dot)
            if (Current.Type == TokenType.Dot)
            {
                int dotNext = _pos + 1;
                bool hasSpaceAfter = false;
                if (dotNext < _tokens.Count)
                {
                    Token next = _tokens[dotNext];
                    if (next.Line != Current.Line || Current.Column + 1 < next.Column)
                        hasSpaceAfter = true;
                }

                if (!hasSpaceAfter && dotNext < _tokens.Count && (_tokens[dotNext].Type == TokenType.Identifier || IsKeyword(_tokens[dotNext].Type)))
                {
                    Advance(); // consume dot
                    Token member = Advance();
                    var memberNode = new AstNode("Member", member.Line, member.Column);
                    memberNode.Value = member.Value;
                    memberNode.AddChild(node);
                    node = memberNode;
                    continue;
                }
            }

            // Variadic spread: expr* (used in function calls: func(args*))
            if (Current.Type == TokenType.Star)
            {
                // Check if this is a spread (followed by ) or , or EOF/newline)
                var afterStar = _pos + 1 < _tokens.Count ? _tokens[_pos + 1].Type : TokenType.EOF;
                if (afterStar == TokenType.RParen || afterStar == TokenType.RBracket || afterStar == TokenType.Comma ||
                    afterStar == TokenType.Newline || afterStar == TokenType.EOF)
                {
                    Advance(); // consume *
                    var spread = new AstNode("Variadic", node.Line, node.Column);
                    spread.AddChild(node);
                    node = spread;
                    continue;
                }
            }

            // Postfix increment/decrement: expr++, expr--
            if (Current.Type == TokenType.Increment || Current.Type == TokenType.Decrement)
            {
                var opTok = Advance();
                var postfix = new AstNode("PostfixExpr", opTok.Line, opTok.Column);
                postfix.Value = opTok.Value;
                postfix.AddChild(node);
                node = postfix;
                continue;
            }

            break;
        }

        return node;
    }

    private AstNode ParsePrimaryCore()
    {
        SkipNewlines();
        Token t = Current;

        switch (t.Type)
        {
            case TokenType.Number:
                Advance();
                return new AstNode("Number", t.Line, t.Column) { Value = t.Value };

            case TokenType.String:
                Advance();
                var strNode = new AstNode("String", t.Line, t.Column) { Value = t.Value, Metadata = t.Metadata };
                if (strNode.Metadata != null && strNode.Metadata.StartsWith("raw:"))
                {
                    ExtractCommentsFromRaw(strNode.Metadata.Substring(4), strNode);
                    // a continuation-section string spans source lines: record where it ends (blank-line layout)
                    int breaks = 0;
                    foreach (char ch in strNode.Metadata) if (ch == '\n') breaks++;
                    if (breaks > 0) strNode.EndLine = t.Line + breaks;
                }
                return strNode;

            case TokenType.New:
            case TokenType.Identifier:
            case TokenType.If:
            case TokenType.Else:
            case TokenType.While:
            case TokenType.For:
            case TokenType.Loop:
            case TokenType.Until:
            case TokenType.Break:
            case TokenType.Continue:
            case TokenType.Return:
            case TokenType.Class:
            case TokenType.Extends:
            case TokenType.Try:
            case TokenType.Catch:
            case TokenType.Finally:
            case TokenType.Throw:
            case TokenType.Switch:
            case TokenType.Case:
            case TokenType.Default:
            case TokenType.Global:
            case TokenType.Local:
            case TokenType.Static:
                Advance();
                // Single-param fat arrow: identifier => expression
                if (Current.Type == TokenType.FatArrow)
                {
                    Advance(); // consume =>
                    var arrow = new AstNode("FatArrow", t.Line, t.Column) { Metadata = "bare" }; // `x => e`: no parentheses
                    var parms = new AstNode("Parameters", t.Line, t.Column);
                    var p = new AstNode("Parameter", t.Line, t.Column);
                    p.Value = t.Value;
                    parms.AddChild(p);
                    arrow.AddChild(parms);
                    arrow.AddChild(ParseExpression(0));
                    return arrow;
                }
                return new AstNode("Identifier", t.Line, t.Column) { Value = t.Value };

            case TokenType.This:
                Advance();
                // 'this' as fat-arrow param: this => expr
                if (Current.Type == TokenType.FatArrow)
                {
                    Advance(); // consume =>
                    var arrow = new AstNode("FatArrow", t.Line, t.Column) { Metadata = "bare" }; // `x => e`: no parentheses
                    var parms = new AstNode("Parameters", t.Line, t.Column);
                    var p = new AstNode("Parameter", t.Line, t.Column);
                    p.Value = "this";
                    parms.AddChild(p);
                    arrow.AddChild(parms);
                    arrow.AddChild(ParseExpression(0));
                    return arrow;
                }
                return new AstNode("This", t.Line, t.Column);

            case TokenType.Super:
                Advance();
                return new AstNode("Super", t.Line, t.Column);

            case TokenType.LParen:
                Advance();
                // Could be a fat-arrow function: (params) => expr
                // Or a grouped expression: (expr) or (expr1, expr2, ...)
                if (IsFatArrowFunction())
                {
                    _pos--; // back up to re-parse the paren
                    return ParseFatArrowFunction();
                }

                var innerComments = new List<AstNode>();

                // Grab any skipped comments that were before or on the LParen line
                for (int i = 0; i < _skippedComments.Count; i++)
                {
                    var c = _skippedComments[i];
                    if (c.Line <= t.Line)
                    {
                        innerComments.Add(c);
                        _skippedComments.RemoveAt(i);
                        i--;
                    }
                }

                while (Current.Type == TokenType.Comment)
                {
                    innerComments.Add(CreateCommentOrWarningNode(Current));
                    Advance();
                }

                var expr = ParseExpression(0);

                while (Current.Type == TokenType.Comment)
                {
                    innerComments.Add(CreateCommentOrWarningNode(Current));
                    Advance();
                }

                // AHK2 comma as multi-statement inside parens: (a := 1, b := 2)
                if (Current.Type == TokenType.Comma)
                {
                    var seq = new AstNode("Sequence", t.Line, t.Column);
                    seq.AddChild(expr);
                    while (Current.Type == TokenType.Comma)
                    {
                        Advance();
                        SkipNewlines();
                        if (Current.Type == TokenType.RParen) break;

                        while (Current.Type == TokenType.Comment)
                        {
                            innerComments.Add(CreateCommentOrWarningNode(Current));
                            Advance();
                        }

                        seq.AddChild(ParseExpression(0));

                        while (Current.Type == TokenType.Comment)
                        {
                            innerComments.Add(CreateCommentOrWarningNode(Current));
                            Advance();
                        }
                    }
                    Token closeParen = Expect(TokenType.RParen, "grouped expression");

                    // Grab any skipped comments up to RParen
                    for (int i = 0; i < _skippedComments.Count; i++)
                    {
                        var c = _skippedComments[i];
                        if (c.Line <= closeParen.Line)
                        {
                            innerComments.Add(c);
                            _skippedComments.RemoveAt(i);
                            i--;
                        }
                    }

                    var grouped2 = new AstNode("Grouped", t.Line, t.Column);
                    grouped2.EndLine = closeParen.Line;
                    grouped2.EndColumn = closeParen.Column;
                    grouped2.AddChild(seq);
                    foreach (var comment in innerComments)
                    {
                        grouped2.AddChild(comment);
                    }
                    return grouped2;
                }

                Token closeParen2 = Expect(TokenType.RParen, "grouped expression");

                // Grab any skipped comments up to RParen
                for (int i = 0; i < _skippedComments.Count; i++)
                {
                    var c = _skippedComments[i];
                    if (c.Line <= closeParen2.Line)
                    {
                        innerComments.Add(c);
                        _skippedComments.RemoveAt(i);
                        i--;
                    }
                }

                var grouped = new AstNode("Grouped", t.Line, t.Column);
                grouped.EndLine = closeParen2.Line;
                grouped.EndColumn = closeParen2.Column;
                grouped.AddChild(expr);
                foreach (var comment in innerComments)
                {
                    grouped.AddChild(comment);
                }
                return grouped;

            case TokenType.LBracket:
                return ParseArrayLiteral();

            case TokenType.LBrace:
                return ParseObjectLiteral();

            default:
                // Error recovery: unknown primary
                Advance();
                var err = new AstNode("Error", t.Line, t.Column);
                err.Value = "Unexpected token: " + t.Type + " '" + t.Value + "'";
                if (t.Type == TokenType.EOF) EmptyAt(err, t); // at the end of the text
                return err;
        }
    }

    private AstNode ParseArrayLiteral()
    {
        Token t = Advance(); // [
        var node = new AstNode("Array", t.Line, t.Column);

        int arrGuard = _tokens.Count;
        while (Current.Type != TokenType.RBracket && Current.Type != TokenType.EOF && arrGuard-- > 0)
        {
            SkipNewlines();
            if (Current.Type == TokenType.RBracket) break;
            int before = _pos;
            if (Current.Type == TokenType.Comma)
            {
                // omitted element (`[ , , "A"]`, `[1,,3]`): an unset item
                node.AddChild(Omitted());
                Advance();
                continue;
            }
            node.AddChild(ParseExpression(0));
            if (Current.Type == TokenType.Comma) Advance();
            if (_pos == before) Advance();
        }

        Expect(TokenType.RBracket, "array literal");
        return node;
    }

    private AstNode ParseObjectLiteral()
    {
        Token t = Advance(); // {
        var node = new AstNode("Object", t.Line, t.Column);

        int objGuard = _tokens.Count;
        while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && objGuard-- > 0)
        {
            SkipNewlines();
            if (Current.Type == TokenType.RBrace) break;

            int before = _pos;

            // Parse key: any word-based identifier/keyword/operator can be a key
            AstNode key;
            if (!string.IsNullOrEmpty(Current.Value) && (char.IsLetter(Current.Value[0]) || Current.Value[0] == '_') && Current.Type != TokenType.String)
            {
                Token keyTok = Current;
                Advance();
                key = new AstNode("Identifier", keyTok.Line, keyTok.Column) { Value = keyTok.Value };
            }
            else
            {
                key = ParseExpression(0);
            }

            Expect(TokenType.Colon, "object literal");
            var value = ParseExpression(0);

            var pair = new AstNode("KeyValue", key.Line, key.Column);
            pair.AddChild(key);
            pair.AddChild(value);
            node.AddChild(pair);

            // Skip trailing comments/newlines before looking for comma separator
            SkipNewlines();
            if (Current.Type == TokenType.Comma) Advance();
            if (_pos == before) Advance();
        }

        if (Current.Type == TokenType.RBrace) node.EndLine = Current.Line; // the closing brace's line (blank-line layout)
        Expect(TokenType.RBrace, "object literal");
        return node;
    }

    private AstNode ParseArgumentListCore()
    {
        Token t = Advance(); // (
        var node = new AstNode("Arguments", t.Line, t.Column);

        SkipNewlines();
        if (Current.Type == TokenType.RParen)
        {
            Advance(); // )
            return node;
        }

        int argGuard = _tokens.Count;
        while (_pos < _tokens.Count && argGuard-- > 0)
        {
            SkipNewlines();

            if (Current.Type == TokenType.Comma)
            {
                node.AddChild(Omitted());
                Advance(); // consume comma
                continue;
            }
            else if (Current.Type == TokenType.RParen)
            {
                node.AddChild(Omitted());
                break;
            }
            else
            {
                node.AddChild(ParseExpression(0));
            }

            SkipNewlines();
            if (Current.Type == TokenType.Comma)
            {
                Advance(); // consume separator comma
            }
            else if (Current.Type == TokenType.RParen)
            {
                break;
            }
            else
            {
                break;
            }
        }

        Expect(TokenType.RParen, "arguments");
        return node;
    }

    private bool IsFatArrowFunction()
    {
        // Heuristic: scan ahead for ) followed by =>
        // The outer LParen was already consumed by ParsePrimary, so we
        // start scanning from _pos (first token inside the outer parens)
        // with depth=1 to find the matching RParen for the outer LParen.
        int save = _pos; // Start scanning from current position (already inside outer paren)
        int depth = 1;
        while (save < _tokens.Count && depth > 0)
        {
            if (_tokens[save].Type == TokenType.LParen) depth++;
            else if (_tokens[save].Type == TokenType.RParen) depth--;
            save++;
        }
        // Skip newlines and comments
        while (save < _tokens.Count && (_tokens[save].Type == TokenType.Newline || _tokens[save].Type == TokenType.Comment))
            save++;

        return save < _tokens.Count && _tokens[save].Type == TokenType.FatArrow;
    }

    private AstNode ParseFatArrowFunctionCore()
    {
        Token t = Current;
        var node = new AstNode("FatArrow", t.Line, t.Column);

        // Parameters
        node.AddChild(ParseParameterList());

        // =>
        SkipNewlines();
        Expect(TokenType.FatArrow, "fat arrow");

        // Body expression
        node.AddChild(ParseExpression(0));

        return node;
    }

    /// <summary>
    /// Check if (tokens...) => follows at Current position (before consuming LParen).
    /// This detects the pattern: obj.method (params) => body
    /// where the parens serve dual duty as call args and fat arrow params.
    /// </summary>
    private bool IsFatArrowCallArg()
    {
        int save = _pos + 1; // Start after the (
        int depth = 1;
        while (save < _tokens.Count && depth > 0)
        {
            if (_tokens[save].Type == TokenType.LParen) depth++;
            else if (_tokens[save].Type == TokenType.RParen) depth--;
            save++;
        }
        // Skip newlines and comments
        while (save < _tokens.Count && (_tokens[save].Type == TokenType.Newline || _tokens[save].Type == TokenType.Comment))
            save++;
        return save < _tokens.Count && _tokens[save].Type == TokenType.FatArrow;
    }

    // -- Precedence Table --------------------------------------------------

    private int GetPrecedence(TokenType type)
    {
        if (TokenKinds.IsAssignment(type)) return 1;
        switch (type)
        {
            case TokenType.Ternary: return 2;
            case TokenType.NullCoalesce: return 3;
            case TokenType.LogicalOr: return 4;
            case TokenType.LogicalAnd: return 5;
            case TokenType.BitwiseOr: return 6;
            case TokenType.BitwiseXor: return 7;
            case TokenType.BitwiseAnd: return 8;
            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.StrictEqual:
            case TokenType.StrictNotEqual:
            case TokenType.RegexEqual: return 9;
            case TokenType.Less:
            case TokenType.Greater:
            case TokenType.LessEqual:
            case TokenType.GreaterEqual:
            case TokenType.Is: return 10;
            case TokenType.ShiftLeft:
            case TokenType.ShiftRight:
            case TokenType.UnsignedShiftRight: return 11;
            case TokenType.DotDot: return 12; // concatenation
            case TokenType.Dot: return 12; // single dot is also concatenation (spaces around it)
            case TokenType.Plus:
            case TokenType.Minus: return 13;
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.IntDiv: return 14;
            case TokenType.Power: return 15;
            default: return 0;
        }
    }

    private bool IsRightAssociative(TokenType type)
    {
        return TokenKinds.IsAssignment(type) || type == TokenType.Power;
    }

    private bool IsKeyword(TokenType type)
    {
        return type >= TokenType.If && type <= TokenType.New;
    }

    // -- Error Recovery ----------------------------------------------------

    private void SkipToRecovery()
    {
        int depth = 0;
        int maxSkip = 100; // prevent infinite loops
        while (_pos < _tokens.Count && maxSkip-- > 0)
        {
            TokenType t = Current.Type;
            if (t == TokenType.LBrace) depth++;
            else if (t == TokenType.RBrace)
            {
                if (depth > 0) depth--;
                else { Advance(); return; }
            }
            else if (t == TokenType.Newline && depth == 0)
            {
                Advance();
                return;
            }
            Advance();
        }
    }

    private void GroupInlinedIncludes(AstNode program, List<Tuple<string, int, int>> ranges, Dictionary<int, int[]> markerSpans)
    {
        // Sort ranges by span size ascending so we process innermost first
        var sortedRanges = ranges.OrderBy(r => r.Item3 - r.Item2).ToList();

        foreach (var range in sortedRanges)
        {
            string fileName = range.Item1;
            int startLine = range.Item2;
            int endLine = range.Item3;

            var includeNode = new AstNode("Include", startLine, 1);
            includeNode.Value = fileName;
            includeNode.Metadata = "#Include " + fileName;
            includeNode.EndLine = endLine; // spans to its `; --- end` marker (blank-line layout after it)
            int[] span;
            if (markerSpans != null && markerSpans.TryGetValue(startLine, out span)) { includeNode.StartOffset = span[0]; includeNode.EndOffset = span[1]; }

            var movedChildren = new List<AstNode>();
            var newProgramChildren = new List<AstNode>();

            int insertIndex = -1;
            for (int i = 0; i < program.ChildCount; i++)
            {
                var child = program.GetChild(i);
                if (child.Line >= startLine && child.Line <= endLine)
                {
                    if (insertIndex == -1)
                    {
                        insertIndex = newProgramChildren.Count;
                    }

                    // Check if it's the boundary comments
                    if (child.NodeType == "Comment" && child.Value != null)
                    {
                        string val = child.Value.Trim();
                        bool isBegin = val.StartsWith("; --- begin:") && val.Contains(fileName);
                        bool isEnd = val.StartsWith("; --- end:") && val.Contains(fileName);
                        if (isBegin || isEnd || child.Line == startLine || child.Line == endLine)
                        {
                            // Discard boundary comment
                            continue;
                        }
                    }

                    movedChildren.Add(child);
                }
                else
                {
                    newProgramChildren.Add(child);
                }
            }

            foreach (var child in movedChildren)
            {
                includeNode.AddChild(child);
            }

            if (insertIndex != -1)
            {
                newProgramChildren.Insert(insertIndex, includeNode);
            }
            else
            {
                newProgramChildren.Add(includeNode);
            }

            program.SetChildren(newProgramChildren);
        }
    }
    private void ExtractCommentsFromRaw(string raw, AstNode parent)
    {
        if (string.IsNullOrEmpty(raw)) return;

        string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ';')
                {
                    bool isEscaped = (i > 0 && line[i - 1] == '`');
                    if (!isEscaped)
                    {
                        bool isComment = (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t');
                        if (isComment)
                        {
                            string commentText = line.Substring(i);
                            var commentNode = new AstNode("Comment", parent.Line + lineIdx, i + 1);
                            commentNode.Value = commentText;
                            parent.AddChild(commentNode);
                            break;
                        }
                    }
                }
            }
        }
    }
}
