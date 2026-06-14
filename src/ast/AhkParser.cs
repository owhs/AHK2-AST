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
    private List<string> _warnings;

    private static readonly HashSet<string> KnownExceptionClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Error", "IndexError", "MemberError", "TargetError", "TimeoutError", "TypeError", "ValueError", "ZeroDivError", "OSError", "UninitializedVariableError", "Any"
    };

    public AhkParser(List<Token> tokens, GrammarRules grammar)
    {
        _tokens = tokens;
        _pos = 0;
        _grammar = grammar;
        _warnings = new List<string>();
    }

    private Token Current { get { return _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.EOF, "", 0, 0); } }
    private Token Peek(int offset) { int i = _pos + offset; return i < _tokens.Count ? _tokens[i] : new Token(TokenType.EOF, "", 0, 0); }

    private Token Advance()
    {
        Token t = Current;
        _pos++;
        return t;
    }

    private void SkipNewlines()
    {
        while (Current.Type == TokenType.Newline || Current.Type == TokenType.Comment)
            Advance();
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

    private Token Expect(TokenType type, string context)
    {
        SkipNewlines();
        if (Current.Type == type)
            return Advance();

        // Error recovery: create error node, skip to recovery point
        _warnings.Add(string.Format("Expected {0} in {1} at line {2}:{3}, got {4}",
            type, context, Current.Line, Current.Column, Current.Type));
        return new Token(type, "__missing__", Current.Line, Current.Column);
    }

    // -- Program -----------------------------------------------------------

    public AstNode ParseProgram()
    {
        var program = new AstNode("Program", 1, 1);

        var ranges = new List<Tuple<string, int, int>>();
        var inlinedRangesList = new List<string>();
        var stack = new Stack<Tuple<string, int>>();
        for (int idx = 0; idx < _tokens.Count; idx++)
        {
            var tok = _tokens[idx];
            if (tok.Type == TokenType.Comment && tok.Value != null)
            {
                string val = tok.Value.Trim();
                if (val.StartsWith("; --- begin:") && val.EndsWith("---"))
                {
                    int colonIdx = val.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string fileName = val.Substring(colonIdx + 1).Replace("---", "").Trim();
                        stack.Push(Tuple.Create(fileName, tok.Line));
                    }
                }
                else if (val.StartsWith("; --- end:") && val.EndsWith("---"))
                {
                    if (stack.Count > 0)
                    {
                        var startInfo = stack.Pop();
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
            }
            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

        if (ranges.Count > 0)
        {
            GroupInlinedIncludes(program, ranges);
        }

        // Attach warnings
        foreach (string w in _warnings)
        {
            var warnNode = new AstNode("Warning", 0, 0);
            warnNode.Value = w;
            program.AddChild(warnNode);
        }

        // Scan for Unknown nodes and add warnings
        var unknownWarns = new List<AstNode>();
        CollectUnknownWarnings(program, unknownWarns);
        foreach (var w in unknownWarns)
            program.AddChild(w);

        return program;
    }

    private void CollectUnknownWarnings(AstNode node, List<AstNode> warnings)
    {
        if (node.NodeType == "Unknown")
        {
            var warnNode = new AstNode("Warning", node.Line, node.Column);
            warnNode.Value = string.Format("Unknown/unhandled construct '{0}' at line {1}:{2}",
                node.Value, node.Line, node.Column);
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
                return warnNode;
            }
        }
        var comment = new AstNode("Comment", tok.Line, tok.Column);
        comment.Value = tok.Value;
        return comment;
    }

    // -- Statement ---------------------------------------------------------

    private AstNode ParseStatement()
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
                return ParseClass();
            case TokenType.Try:
                return ParseTry();
            case TokenType.Switch:
                return ParseSwitch();
            case TokenType.Throw:
                return ParseThrow();
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
            case TokenType.Global:
            case TokenType.Local:
            case TokenType.Static:
                return ParseDeclaration();
            default:
                // Label: Identifier followed by Colon (not :=) at statement level
                if (t.Type == TokenType.Identifier && Peek(1).Type == TokenType.Colon)
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
                }
                return ParseExpressionStatement();
        }
    }

    // -- Specific Statement Parsers ----------------------------------------

    private AstNode ParseDirective()
    {
        Token t = Advance();
        var node = new AstNode("Directive", t.Line, t.Column);
        node.Value = t.Value;
        return node;
    }

    private AstNode ParseHotkey()
    {
        Token t = Advance();
        var node = new AstNode("Hotkey", t.Line, t.Column);
        node.Value = t.Value;
        SkipNewlines();
        if (Current.Type == TokenType.LBrace)
            node.AddChild(ParseBlock());
        else if (Current.Type != TokenType.EOF && Current.Type != TokenType.Newline)
            node.AddChild(ParseStatement());
        return node;
    }

    private AstNode ParseHotstring()
    {
        Token t = Advance();
        var node = new AstNode("Hotstring", t.Line, t.Column);
        node.Value = t.Value;
        if (t.Value != null && t.Value.Trim().EndsWith("::"))
        {
            SkipNewlines();
            if (Current.Type == TokenType.LBrace)
                node.AddChild(ParseBlock());
            else if (Current.Type != TokenType.EOF && Current.Type != TokenType.Newline)
                node.AddChild(ParseStatement());
        }
        return node;
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
        SkipNewlines();
        if (Current.Type == TokenType.Else)
        {
            Advance();
            SkipNewlines();
            var elseBranch = new AstNode("Else", Current.Line, Current.Column);
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
        return node;
    }

    private AstNode ParseFor()
    {
        Token t = Advance();
        var node = new AstNode("For", t.Line, t.Column);

        // for key, value in collection - parse simple identifiers for vars
        var vars = new AstNode("ForVars", t.Line, t.Column);

        // Handle byref: for &v in collection
        if (Current.Type == TokenType.BitwiseAnd)
        {
            Advance();
            if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
            {
                var v1 = new AstNode("Identifier", Current.Line, Current.Column);
                v1.Value = Advance().Value;
                v1.Metadata = "byref";
                vars.AddChild(v1);
            }
        }
        else if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
        {
            var v1 = new AstNode("Identifier", Current.Line, Current.Column);
            v1.Value = Advance().Value;
            vars.AddChild(v1);
        }

        if (Current.Type == TokenType.Comma)
        {
            Advance();
            // Handle byref: for k, &v in collection
            if (Current.Type == TokenType.BitwiseAnd)
            {
                Advance();
                if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                {
                    var v2 = new AstNode("Identifier", Current.Line, Current.Column);
                    v2.Value = Advance().Value;
                    v2.Metadata = "byref";
                    vars.AddChild(v2);
                }
            }
            else if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
            {
                var v2 = new AstNode("Identifier", Current.Line, Current.Column);
                v2.Value = Advance().Value;
                vars.AddChild(v2);
            }

            // Handle 3rd var: for k, &v, * in collection
            if (Current.Type == TokenType.Comma)
            {
                Advance();
                if (Current.Type == TokenType.Star)
                {
                    var v3 = new AstNode("Identifier", Current.Line, Current.Column);
                    v3.Value = "*";
                    v3.Metadata = "variadic";
                    vars.AddChild(v3);
                    Advance();
                }
                else if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                {
                    var v3 = new AstNode("Identifier", Current.Line, Current.Column);
                    v3.Value = Advance().Value;
                    vars.AddChild(v3);
                }
            }
        }

        node.AddChild(vars);

        // 'in' keyword (parsed as identifier)
        if (Current.Type == TokenType.Identifier && Current.Value.ToLower() == "in")
            Advance();

        node.AddChild(ParseExpression(0)); // collection
        SkipNewlines();
        node.AddChild(ParseStatementOrBlock());

        // for ... else (else runs when iterable is empty)
        SkipNewlines();
        if (Current.Type == TokenType.Else)
        {
            Advance();
            var elseNode = new AstNode("Else", Current.Line, Current.Column);
            SkipNewlines();
            elseNode.AddChild(ParseStatementOrBlock());
            node.AddChild(elseNode);
        }
        return node;
    }

    private AstNode ParseLoop()
    {
        Token t = Advance();
        var node = new AstNode("Loop", t.Line, t.Column);

        // Skip any comments on the same line
        while (Current.Type == TokenType.Comment && Current.Line == t.Line)
        {
            Advance();
        }

        // Check for Loop variants: Parse, Files, Reg, Read (must be on the same line)
        if (Current.Type == TokenType.Identifier && Current.Line == t.Line)
        {
            string variant = Current.Value.ToLower();
            if (variant == "parse" || variant == "files" || variant == "reg" || variant == "read")
            {
                node.Value = Current.Value; // store variant name
                Advance(); // consume variant keyword

                // First argument can be space-separated or comma-separated
                // e.g., "Loop Parse t {" or "Loop Parse, t {"
                if (Current.Type == TokenType.Comma) Advance();
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
                        node.AddChild(new AstNode("Omitted", Current.Line, Current.Column));
                    }
                }

                SkipNewlines();
                node.AddChild(ParseStatementOrBlock());

                // Until clause
                SkipNewlines();
                if (Current.Type == TokenType.Until)
                {
                    Advance();
                    var untilNode = new AstNode("Until", Current.Line, Current.Column);
                    untilNode.AddChild(ParseExpression(0));
                    node.AddChild(untilNode);
                }

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

        // Until clause
        SkipNewlines();
        if (Current.Type == TokenType.Until)
        {
            Advance();
            var untilNode = new AstNode("Until", Current.Line, Current.Column);
            untilNode.AddChild(ParseExpression(0));
            node.AddChild(untilNode);
        }

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
            string baseName = Expect(TokenType.Identifier, "extends").Value;
            // Support dotted names: extends WebView2.Base
            while (Current.Type == TokenType.Dot)
            {
                Advance();
                if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                    baseName += "." + Advance().Value;
            }
            extendsNode.Value = baseName;
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
                node.AddChild(ParseClassMember());
            }
            catch (Exception ex)
            {
                var err = new AstNode("Error", Current.Line, Current.Column);
                err.Value = ex.Message;
                node.AddChild(err);
                SkipToRecovery();
            }
            // Safety: if nothing was consumed, force advance to prevent infinite loop
            if (_pos == before) Advance();
        }

        Expect(TokenType.RBrace, "class body");
        return node;
    }

    private AstNode ParseClassMember()
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
        if (Current.Type == TokenType.Class)
        {
            var cls = ParseClass();
            if (isStatic) cls.Metadata = "static";
            return cls;
        }

        // Method or property - also accept keywords as member names (catch, try, finally, throw, etc.)
        if (Current.Type == TokenType.Identifier || (IsKeyword(Current.Type) && Current.Type != TokenType.Class && Current.Type != TokenType.Static))
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
                    while (Current.Type == TokenType.Comma)
                    {
                        Advance();
                        SkipNewlines();
                        if (Current.Type == TokenType.Identifier)
                        {
                            var nextName = Advance().Value;
                            while (Current.Type == TokenType.Dot)
                            {
                                Advance();
                                if (Current.Type == TokenType.Identifier || Current.Type == TokenType.This)
                                    nextName += "." + Advance().Value;
                            }
                            if (Current.Type == TokenType.ColonAssign)
                            {
                                Advance();
                                var nextAssign = new AstNode("StaticAssign", Current.Line, Current.Column);
                                nextAssign.Value = nextName;
                                if (isStatic) nextAssign.Metadata = "static";
                                nextAssign.AddChild(ParseExpression(0));
                                dotAssign.AddChild(nextAssign);
                            }
                        }
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
                while (Current.Type == TokenType.Comma)
                {
                    Advance();
                    SkipNewlines();
                    if (Current.Type == TokenType.Identifier)
                    {
                        var nextName = Advance();
                        if (Current.Type == TokenType.ColonAssign)
                        {
                            Advance();
                            var nextAssign = new AstNode("StaticAssign", nextName.Line, nextName.Column);
                            nextAssign.Value = nextName.Value;
                            if (isStatic) nextAssign.Metadata = "static";
                            nextAssign.AddChild(ParseExpression(0));
                            assign.AddChild(nextAssign);
                        }
                        else
                        {
                            var nextDecl = new AstNode("Declaration", nextName.Line, nextName.Column);
                            nextDecl.Value = nextName.Value;
                            if (isStatic) nextDecl.Metadata = "static";
                            assign.AddChild(nextDecl);
                        }
                    }
                }
                return assign;
            }

            // Just a name - bare declaration
            var decl = new AstNode("Declaration", name.Line, name.Column);
            decl.Value = name.Value;
            if (isStatic) decl.Metadata = "static";
            return decl;
        }

        // Fallback - unrecognized class member
        var unknown = new AstNode("Unknown", Current.Line, Current.Column);
        unknown.Value = Current.Value;
        _warnings.Add(string.Format("Unrecognized class member '{0}' at line {1}:{2}",
            Current.Value, Current.Line, Current.Column));
        Advance();
        return unknown;
    }

    private AstNode ParseParameterList()
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

            if (Current.Type == TokenType.Comma)
                Advance();

            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

        Expect(TokenType.RParen, "parameters");
        return node;
    }
    private AstNode ParsePropertyBody()
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
            SkipNewlines();
            if (Current.Type != TokenType.Catch)
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
                            _warnings.Add(string.Format("Expected catch variable after 'as' at line {0}:{1}", Current.Line, Current.Column));
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

        SkipNewlines();
        if (Current.Type == TokenType.Else)
        {
            Advance();
            var elseNode = new AstNode("Else", Current.Line, Current.Column);
            SkipNewlines();
            elseNode.AddChild(ParseStatementOrBlock());
            node.AddChild(elseNode);
        }

        SkipNewlines();
        if (Current.Type == TokenType.Finally)
        {
            Advance();
            var finallyNode = new AstNode("Finally", Current.Line, Current.Column);
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
                Advance();
                var caseNode = new AstNode("Case", Current.Line, Current.Column);

                // Parse comma-separated case values: case 1, 2, 3:
                caseNode.AddChild(ParseExpression(0));
                while (Current.Type == TokenType.Comma)
                {
                    Advance();
                    caseNode.AddChild(ParseExpression(0));
                }

                Expect(TokenType.Colon, "case");

                // Case body: statements until next case/default/}
                var body = new AstNode("CaseBody", Current.Line, Current.Column);
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
                Advance();
                Expect(TokenType.Colon, "default");
                var defNode = new AstNode("Default", Current.Line, Current.Column);
                var body = new AstNode("DefaultBody", Current.Line, Current.Column);
                int defGuard = _tokens.Count;
                while (Current.Type != TokenType.RBrace && Current.Type != TokenType.EOF && defGuard-- > 0)
                {
                    SkipNewlines();
                    if (Current.Type == TokenType.RBrace) break;
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

    private AstNode ParseDeclaration()
    {
        Token scope = Advance();
        var node = new AstNode("Declaration", scope.Line, scope.Column);
        node.Metadata = scope.Value.ToLower();

        // Parse first variable
        if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
        {
            node.Value = Advance().Value;
            if (Current.Type == TokenType.ColonAssign)
            {
                Advance();
                node.AddChild(ParseExpression(0));
            }

            // Handle comma-separated additional variables: global a, b, c := 1
            while (Current.Type == TokenType.Comma)
            {
                Advance(); // consume comma
                SkipNewlines();
                if (Current.Type == TokenType.Identifier || IsKeyword(Current.Type))
                {
                    var extra = new AstNode("Declaration", Current.Line, Current.Column);
                    extra.Metadata = node.Metadata;
                    extra.Value = Advance().Value;
                    if (Current.Type == TokenType.ColonAssign)
                    {
                        Advance();
                        extra.AddChild(ParseExpression(0));
                    }
                    node.AddChild(extra);
                }
            }
        }

        return node;
    }

    private AstNode ParseBlock()
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
                block.AddChild(ParseStatement());
            }
            catch (Exception ex)
            {
                var err = new AstNode("Error", Current.Line, Current.Column);
                err.Value = ex.Message;
                block.AddChild(err);
                SkipToRecovery();
            }
            // Safety: if nothing was consumed, force advance
            if (_pos == before) Advance();
        }

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

    private AstNode ParseExpressionStatement()
    {
        var expr = ParseExpression(0);

        // Check for comma continuation starting on the next line
        int savePos = _pos;
        SkipNewlines();
        bool hasCommaContinuation = (Current.Type == TokenType.Comma);
        if (!hasCommaContinuation)
        {
            _pos = savePos; // restore if not continuing
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
                // Consecutive commas = omitted argument (e.g., WinGetPos ,, &Width)
                if (Current.Type == TokenType.Comma)
                {
                    multi.AddChild(new AstNode("Omitted", Current.Line, Current.Column));
                    continue;
                }
                multi.AddChild(ParseExpression(0));
            }
            return multi;
        }

        return expr;
    }

    private AstNode ParseExpression(int minPrec)
    {
        var left = ParseUnary();

        while (true)
        {
            // Skip newlines only if next token is an operator (continuation)
            int savePos = _pos;
            SkipNewlines();

            int prec = GetPrecedence(Current.Type);
            if (prec < minPrec || prec == 0)
            {
                _pos = savePos; // restore if we shouldn't continue

                if (minPrec <= 11 && Current.Type != TokenType.Newline
                    && Current.Type != TokenType.EOF && Current.Type != TokenType.Comment)
                {
                    Token prev = _pos > 0 ? _tokens[_pos - 1] : null;
                    if (prev != null)
                    {
                        TokenType ct = Current.Type;
                        if (ct == TokenType.String || ct == TokenType.Number || ct == TokenType.Identifier
                            || ct == TokenType.LParen || ct == TokenType.This || ct == TokenType.Super)
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
            left = binary;
        }

        return left;
    }

    private AstNode ParseUnary()
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
            node.AddChild(ParseUnary());
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
                        index.AddChild(new AstNode("Omitted", Current.Line, Current.Column));
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

    private AstNode ParsePrimary()
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
                return new AstNode("String", t.Line, t.Column) { Value = t.Value };

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
                    var arrow = new AstNode("FatArrow", t.Line, t.Column);
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
                    var arrow = new AstNode("FatArrow", t.Line, t.Column);
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
                var expr = ParseExpression(0);
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
                        seq.AddChild(ParseExpression(0));
                    }
                    Expect(TokenType.RParen, "grouped expression");
                    var grouped2 = new AstNode("Grouped", t.Line, t.Column);
                    grouped2.AddChild(seq);
                    return grouped2;
                }
                Expect(TokenType.RParen, "grouped expression");
                var grouped = new AstNode("Grouped", t.Line, t.Column);
                grouped.AddChild(expr);
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

        Expect(TokenType.RBrace, "object literal");
        return node;
    }

    private AstNode ParseArgumentList()
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
                node.AddChild(new AstNode("Omitted", Current.Line, Current.Column));
                Advance(); // consume comma
                continue;
            }
            else if (Current.Type == TokenType.RParen)
            {
                node.AddChild(new AstNode("Omitted", Current.Line, Current.Column));
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

    private AstNode ParseFatArrowFunction()
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
        switch (type)
        {
            case TokenType.ColonAssign:
            case TokenType.PlusAssign:
            case TokenType.MinusAssign:
            case TokenType.StarAssign:
            case TokenType.SlashAssign:
            case TokenType.DotAssign:
            case TokenType.NullCoalesceAssign:
            case TokenType.BitwiseAndAssign:
            case TokenType.BitwiseOrAssign:
            case TokenType.BitwiseXorAssign:
            case TokenType.IntDivAssign:
                return 1;
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
        return type == TokenType.ColonAssign || type == TokenType.PlusAssign
            || type == TokenType.MinusAssign || type == TokenType.StarAssign
            || type == TokenType.SlashAssign || type == TokenType.DotAssign
            || type == TokenType.NullCoalesceAssign
            || type == TokenType.BitwiseAndAssign || type == TokenType.BitwiseOrAssign
            || type == TokenType.BitwiseXorAssign || type == TokenType.IntDivAssign
            || type == TokenType.Power;
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

    private void GroupInlinedIncludes(AstNode program, List<Tuple<string, int, int>> ranges)
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
}

