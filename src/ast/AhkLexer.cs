using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public class AhkLexer
{
    private string _src;
    private int _pos;
    private int _line;
    private int _col;
    private List<Token> _tokens;
    private int _tokenStartLine;
    private int _tokenStartCol;
    private bool _readContinuationSection = false;

    private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase)
    {
        {"if", TokenType.If}, {"else", TokenType.Else},
        {"while", TokenType.While}, {"for", TokenType.For},
        {"loop", TokenType.Loop}, {"until", TokenType.Until},
        {"break", TokenType.Break}, {"continue", TokenType.Continue},
        {"return", TokenType.Return},
        {"class", TokenType.Class}, {"extends", TokenType.Extends},
        {"super", TokenType.Super}, {"this", TokenType.This},
        {"try", TokenType.Try}, {"catch", TokenType.Catch},
        {"finally", TokenType.Finally}, {"throw", TokenType.Throw},
        {"switch", TokenType.Switch}, {"case", TokenType.Case},
        {"default", TokenType.Default},
        {"global", TokenType.Global}, {"local", TokenType.Local},
        {"static", TokenType.Static}, {"new", TokenType.New},
        {"and", TokenType.LogicalAnd}, {"or", TokenType.LogicalOr},
        {"not", TokenType.LogicalNot}, {"is", TokenType.Is}
    };

    private readonly string _orig;   // source before continuation sections were joined
    private readonly SourceMap _map; // joined-text offset -> original offset (null when nothing was joined)

    /// <summary>
    /// Original source text of the joined-text range [start, end) when that range spans a joined continuation
    /// section (so its original text differs) and both ends were copied verbatim; otherwise null.
    /// </summary>
    private string OriginalSpan(int start, int end)
    {
        if (_map == null || end <= start) return null;
        int o1 = _map.Map(start), o2 = _map.Map(end - 1);
        if (o1 < 0 || o2 < o1) return null;
        int len = o2 + 1 - o1;
        if (len == end - start) return null; // nothing joined inside
        return _orig.Substring(o1, len);
    }

    public AhkLexer(string source)
    {
        _orig = source ?? "";
        _src = ContinuationJoiner.Join(_orig, out _map); // continuation sections are merged as text first
        _pos = 0;
        _line = 1;
        _col = 1;
        _tokens = new List<Token>();
    }

    /// <summary>The source as given (offsets on tokens and nodes refer to it).</summary>
    public string OriginalSource { get { return _orig; } }

    private SourceLineMap _lineMap;
    /// <summary>Line/column lookup for <see cref="OriginalSource"/>.</summary>
    public SourceLineMap LineMap
    {
        get
        {
            if (_lineMap == null)
            {
                _lineMap = new SourceLineMap(_orig);
                if (_map != null) _lineMap.Sections = _map.Sections;
            }
            return _lineMap;
        }
    }

    /// <summary>Original-source offset of a joined-text position (a token's start, or its end when <paramref name="asEnd"/>).</summary>
    private int OrigOffset(int joinedPos, bool asEnd)
    {
        if (_map == null) return Math.Min(joinedPos, _orig.Length);
        return _map.MapAny(joinedPos, asEnd, _orig.Length);
    }

    /// <summary>Gives the tokens added since <paramref name="from"/> the original range of joined text [start, end).</summary>
    private void StampOffsets(int from, int start, int end)
    {
        if (from >= _tokens.Count) return;
        int s = OrigOffset(start, false), e = OrigOffset(end, true);
        while (e > s && (_orig[e - 1] == ' ' || _orig[e - 1] == '\t' || _orig[e - 1] == '\r')) e--; // `#Directive x ⏎` reads to the line end
        if (e < s) e = s;
        for (int i = from; i < _tokens.Count; i++)
        {
            _tokens[i].StartOffset = s;
            _tokens[i].EndOffset = e;
        }
    }

    public List<Token> Tokenize()
    {
        int stampFrom = 0, stampStart = 0;
        while (_pos < _src.Length)
        {
            // every token added in the previous round spans the text read in that round
            StampOffsets(stampFrom, stampStart, _pos);
            stampFrom = _tokens.Count;
            stampStart = _pos;
            _tokenStartLine = _line;
            _tokenStartCol = _col;
            char c = Peek();

            // Skip whitespace (not newlines)
            if (c == ' ' || c == '\t' || c == '\r' || c == ContinuationJoiner.Nl) // a joined section's line feed is whitespace in code
            { Advance(); continue; }

            // Newlines
            if (c == '\n')
            {
                Emit(TokenType.Newline, "\\n");
                Advance();
                continue;
            }

            // Comments
            if (c == ';')
            {
                string comment = ReadLineComment();
                Emit(TokenType.Comment, comment);
                continue;
            }
            if (c == '/' && PeekAt(1) == '*')
            {
                string comment = ReadBlockComment();
                Emit(TokenType.Comment, comment);
                continue;
            }

            // Hotkey detection (including modifier prefixes) - MUST check before Directive since # can be a hotkey modifier
            if (IsStartOfLine())
            {
                int dblColon = FindHotkeyDoubleColon();
                if (dblColon >= 0)
                {
                    string hotkeyTrigger = _src.Substring(_pos, dblColon - _pos).Trim();
                    int charsConsumed = (dblColon + 2) - _pos;
                    _pos = dblColon + 2;
                    _col += charsConsumed;
                    Emit(TokenType.Hotkey, hotkeyTrigger);
                    continue;
                }
            }

            // Hotstring detection (must be before operators/delimiters and standard colons)
            if (IsStartOfLine() && c == ':')
            {
                int hsLength;
                if (IsHotstringStart(out hsLength))
                {
                    string hs = _src.Substring(_pos, hsLength);
                    _pos += hsLength;
                    _col += hsLength;
                    Emit(TokenType.Hotstring, hs);
                    continue;
                }
            }

            // Directives (#Include, #Requires, etc.)
            if (c == '#' && (_col == 1 || _tokens.Count == 0 || LastTokenIs(TokenType.Newline)))
            {
                string directive = ReadDirective();
                Emit(TokenType.Directive, directive);
                continue;
            }

            // Strings
            if (c == '"')
            {
                int startPos = _pos;
                _readContinuationSection = false;
                string str = ReadDoubleQuotedString().Replace(ContinuationJoiner.Nl, '\n'); // joined section: real line feed
                var tok = new Token(TokenType.String, "\"" + str + "\"", _tokenStartLine, _tokenStartCol);
                if (_readContinuationSection)
                {
                    tok.Metadata = "raw:" + _src.Substring(startPos, _pos - startPos);
                }
                else
                {
                    string raw = OriginalSpan(startPos, _pos); // spans a joined section: keep the original layout
                    if (raw != null) tok.Metadata = "raw:" + raw;
                }
                _tokens.Add(tok);
                continue;
            }
            if (c == '\'')
            {
                int startPos = _pos;
                _readContinuationSection = false;
                string str = ReadSingleQuotedString().Replace(ContinuationJoiner.Nl, '\n');
                var tok = new Token(TokenType.String, "'" + str + "'", _tokenStartLine, _tokenStartCol);
                if (_readContinuationSection)
                {
                    tok.Metadata = "raw:" + _src.Substring(startPos, _pos - startPos);
                }
                else
                {
                    string raw = OriginalSpan(startPos, _pos);
                    if (raw != null) tok.Metadata = "raw:" + raw;
                }
                _tokens.Add(tok);
                continue;
            }

            // Numbers
            if (char.IsDigit(c) || (c == '.' && _pos + 1 < _src.Length && char.IsDigit(PeekAt(1))))
            {
                string num = ReadNumber();
                Emit(TokenType.Number, num);
                continue;
            }

            // Continuation section: ( at start of line
            if (c == '(' && IsStartOfLine())
            {
                // Check if this is truly a continuation section (next chars aren't expression)
                if (IsContinuationSection() && !PrecededByContinuationOperator())
                {
                    int startPos = _pos;
                    string section = ReadContinuationSection();
                    var tok = new Token(TokenType.String, section, _tokenStartLine, _tokenStartCol);
                    tok.Metadata = "raw:" + _src.Substring(startPos, _pos - startPos);
                    _tokens.Add(tok);
                    continue;
                }
            }

            // Identifiers and keywords
            if (IsNameStart(c))
            {
                string ident = ReadIdentifier();

                // Hotkey detection: identifier followed by ::
                if (_pos + 1 < _src.Length && Peek() == ':' && PeekAt(1) == ':')
                {
                    Advance(); Advance(); // consume ::
                    Emit(TokenType.Hotkey, ident);
                    continue;
                }

                // Hotstring detection: :options:trigger::replacement
                // (handled separately)

                // Dynamic variable name: `b%A_Index%`, `pre%n%post` is ONE variable reference, not a concatenation.
                if (Peek() == '%' && DerefCloses(_pos))
                {
                    Emit(TokenType.Identifier, ReadDynamicNameTail(ident));
                    continue;
                }

                TokenType kwType;
                if (Keywords.TryGetValue(ident, out kwType))
                    Emit(kwType, ident);
                else
                    Emit(TokenType.Identifier, ident);
                continue;
            }

            // Operators and delimiters
            Token opToken = ReadOperator();
            if (opToken != null)
            {
                _tokens.Add(opToken);
                continue;
            }



            // Variable dereferencing: %expr%, possibly part of a longer dynamic name (`%n%x`, `%a%_%b%`)
            if (c == '%')
            {
                Emit(TokenType.Identifier, ReadDynamicNameTail(""));
                continue;
            }

            // Unicode operators (AHK2 supports these as alternatives)
            if (c == '\u2260') { Emit(TokenType.NotEqual, "\u2260"); Advance(); continue; }      // -
            if (c == '\u2264') { Emit(TokenType.LessEqual, "\u2264"); Advance(); continue; }     // -
            if (c == '\u2265') { Emit(TokenType.GreaterEqual, "\u2265"); Advance(); continue; }  // -

            // Unknown character - emit and continue (resilient)
            Emit(TokenType.Unknown, c.ToString());
            Advance();
        }

        StampOffsets(stampFrom, stampStart, _pos);
        _tokens.Add(new Token(TokenType.EOF, "", _line, _col) { StartOffset = _orig.Length, EndOffset = _orig.Length });
        _tokens = ProcessContinuations(_tokens);
        return _tokens;
    }

    // -- Character helpers -------------------------------------------------

    private char Peek() { return _pos < _src.Length ? _src[_pos] : '\0'; }
    private char PeekAt(int offset) { return _pos + offset < _src.Length ? _src[_pos + offset] : '\0'; }

    private char Advance()
    {
        char c = _src[_pos++];
        if (c == '\n') { _line++; _col = 1; }
        else _col++;
        return c;
    }

    private void Emit(TokenType type, string value)
    {
        _tokens.Add(new Token(type, value, _tokenStartLine, _tokenStartCol));
    }

    /// <summary>True if the `%` at <paramref name="p"/> has a closing `%` on the same line.</summary>
    private bool DerefCloses(int p)
    {
        for (int i = p + 1; i < _src.Length; i++)
        {
            char ch = _src[i];
            if (ch == '%') return true;
            if (ch == '\n' || ch == ContinuationJoiner.Nl) return false;
        }
        return false;
    }

    /// <summary>
    /// Reads the rest of a dynamic name starting at `%` or at identifier characters: any run of identifier characters
    /// and `%expr%` parts with no whitespace between them (`b%A_Index%`, `%n%x`, `a%x%b%y%`). An unclosed `%`
    /// keeps its old behaviour (read to the end of the line).
    /// </summary>
    private string ReadDynamicNameTail(string head)
    {
        var sb = new StringBuilder(head);
        while (_pos < _src.Length)
        {
            char ch = Peek();
            if (ch == '%')
            {
                if (sb.Length > 0 && !DerefCloses(_pos)) break;
                Advance(); // opening %
                sb.Append('%');
                while (_pos < _src.Length && _src[_pos] != '%' && _src[_pos] != '\n' && _src[_pos] != ContinuationJoiner.Nl)
                    sb.Append(Advance());
                if (_pos < _src.Length && _src[_pos] == '%') sb.Append(Advance());
            }
            else if (IsNameChar(ch))
            {
                if (sb.Length == 0 || sb[sb.Length - 1] != '%') break; // identifier chars only continue after a deref
                while (_pos < _src.Length && IsNameChar(Peek())) sb.Append(Advance());
            }
            else break;
        }
        return sb.ToString();
    }

    private bool LastTokenIs(TokenType type)
    {
        return _tokens.Count > 0 && _tokens[_tokens.Count - 1].Type == type;
    }

    private bool IsStartOfLine()
    {
        // Check if previous non-whitespace token was a newline or this is the first token
        for (int i = _tokens.Count - 1; i >= 0; i--)
        {
            if (_tokens[i].Type == TokenType.Newline) return true;
            if (_tokens[i].Type != TokenType.Comment) return false;
        }
        return true;
    }

    private bool IsHotstringStart(out int length)
    {
        length = 0;
        if (!IsStartOfLine()) return false;
        if (Peek() != ':') return false;

        int p = _pos;
        // Skip first ':'
        p++;

        // The options run to the second ':'. They are letters, digits, `*` `?` `-` and blanks (`: :btw::` has a
        // blank option list); anything else (a quote, a paren...) means this is no hotstring — e.g. a ternary
        // continuation line `: "a::b"`.
        while (p < _src.Length && _src[p] != ':')
        {
            char oc = _src[p];
            if (!(char.IsLetterOrDigit(oc) || oc == '*' || oc == '?' || oc == '-' || oc == ' ' || oc == '\t')) return false;
            p++;
        }

        if (p >= _src.Length || _src[p] != ':') return false;

        // Found the second ':'. Now skip it.
        p++;

        // Now we need to find the ending '::' on the same line.
        bool foundEnd = false;
        while (p + 1 < _src.Length && _src[p] != '\n' && _src[p] != '\r')
        {
            if (_src[p] == ':' && _src[p + 1] == ':')
            {
                foundEnd = true;
                p += 2; // skip the '::'
                break;
            }
            p++;
        }

        if (!foundEnd) return false;

        // Yes! It is a hotstring.
        // The hotstring token should consume the rest of the line (replacement can be inline).
        while (p < _src.Length && _src[p] != '\n' && _src[p] != '\r')
        {
            p++;
        }

        length = p - _pos;
        return true;
    }

    // -- Reading methods ---------------------------------------------------

    private string ReadLineComment()
    {
        int start = _pos;
        while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != ContinuationJoiner.Nl)
            Advance();
        return _src.Substring(start, _pos - start);
    }

    private string ReadBlockComment()
    {
        int start = _pos;
        Advance(); Advance(); // skip /*
        while (_pos + 1 < _src.Length)
        {
            if (_src[_pos] == '*' && _src[_pos + 1] == '/')
            {
                Advance(); Advance();
                return _src.Substring(start, _pos - start);
            }
            Advance();
        }
        // Unterminated: AHK lets a block comment run to the end of the file (the last character included).
        while (_pos < _src.Length) Advance();
        return _src.Substring(start);
    }

    private string ReadDirective()
    {
        int start = _pos;
        Advance(); // skip #
        while (_pos < _src.Length && _src[_pos] != '\n')
            Advance();
        return _src.Substring(start, _pos - start);
    }

    private bool CheckAndReadContinuationSection(StringBuilder sb)
    {
        // Save position to check if next line starts with (
        int save = _pos;
        int saveLine = _line;
        int saveCol = _col;

        // Skip any comments and whitespace on the current line
        while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r')
        {
            if (_src[_pos] == ';')
            {
                bool isComment = (_col == 1 || _pos == 0);
                if (!isComment && _pos > 0)
                {
                    char prev = _src[_pos - 1];
                    if (prev == ' ' || prev == '\t')
                        isComment = true;
                }
                if (isComment)
                {
                    while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r')
                        Advance();
                    break;
                }
            }
            Advance();
        }

        if (_pos < _src.Length && (_src[_pos] == '\n' || (_src[_pos] == '\r' && _pos + 1 < _src.Length && _src[_pos + 1] == '\n')))
        {
            if (_src[_pos] == '\r') Advance();
            if (_pos < _src.Length && _src[_pos] == '\n') Advance();

            // Skip leading whitespace on next line
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t')) Advance();

            if (_pos < _src.Length && _src[_pos] == '(')
            {
                _readContinuationSection = true;
                // This is a quoted continuation section
                Advance(); // skip (

                // Parse options on the ( line (Join, LTrim, etc.)
                string options = "";
                while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r')
                {
                    options += _src[_pos];
                    Advance();
                }
                if (_pos < _src.Length && _src[_pos] == '\r') Advance();
                if (_pos < _src.Length && _src[_pos] == '\n') Advance();

                string joinStr = "\n";
                if (options.IndexOf("Join", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int idx = options.IndexOf("Join", StringComparison.OrdinalIgnoreCase);
                    string afterJoin = options.Substring(idx + 4).Trim();
                    if (afterJoin.Length == 0 || afterJoin.StartsWith("`") || afterJoin.StartsWith(" "))
                    {
                        if (afterJoin.Length == 0)
                        {
                            joinStr = ""; // Join with nothing
                        }
                        else if (afterJoin.StartsWith("`s"))
                        {
                            joinStr = " ";
                        }
                        else if (afterJoin.StartsWith("`n"))
                        {
                            joinStr = "\n";
                        }
                        else if (afterJoin.StartsWith("`r"))
                        {
                            joinStr = "\r";
                        }
                        else if (afterJoin.StartsWith("`t"))
                        {
                            joinStr = "\t";
                        }
                    }
                    else
                    {
                        // Custom join character
                        joinStr = afterJoin.Substring(0, 1);
                    }
                }

                // Read lines until ) at start of line
                while (_pos < _src.Length)
                {
                    // Check for ) at start of line (with optional whitespace)
                    int ls = _pos;
                    int lsLine = _line;
                    int lsCol = _col;
                    while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
                        Advance();

                    if (_pos < _src.Length && _src[_pos] == ')')
                    {
                        Advance(); // skip )
                        // Check for closing quote after )
                        if (_pos < _src.Length && (_src[_pos] == '"' || _src[_pos] == '\''))
                            Advance(); // skip closing quote
                        break;
                    }

                    // Not a closing ), rewind and read the whole line
                    _pos = ls;
                    _line = lsLine;
                    _col = lsCol;
                    
                    var lineContent = new StringBuilder();
                    while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r')
                    {
                        lineContent.Append(_src[_pos]);
                        Advance();
                    }
                    
                    string lineStr = lineContent.ToString();
                    bool ltrim = true;
                    if (options.IndexOf("LTrim0", StringComparison.OrdinalIgnoreCase) >= 0)
                        ltrim = false;
                    
                    if (ltrim)
                        lineStr = lineStr.TrimStart(' ', '\t');

                    sb.Append(lineStr);
                    sb.Append(joinStr);

                    if (_pos < _src.Length && _src[_pos] == '\r') Advance();
                    if (_pos < _src.Length && _src[_pos] == '\n') Advance();
                }

                // Trim trailing join string
                if (sb.Length >= joinStr.Length && joinStr.Length > 0)
                {
                    bool match = true;
                    for (int j = 0; j < joinStr.Length; j++)
                    {
                        if (sb[sb.Length - joinStr.Length + j] != joinStr[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                        sb.Length -= joinStr.Length;
                }

                return true;
            }
        }

        // Restore position
        _pos = save;
        _line = saveLine;
        _col = saveCol;
        return false;
    }

    private string ReadDoubleQuotedString()
    {
        var sb = new StringBuilder();
        Advance(); // skip opening "

        // Check if starts immediately with continuation section
        int checkPos = _pos;
        while (checkPos < _src.Length && (_src[checkPos] == ' ' || _src[checkPos] == '\t'))
        {
            checkPos++;
        }
        if (checkPos < _src.Length && (_src[checkPos] == ';' || _src[checkPos] == '\n' || _src[checkPos] == '\r'))
        {
            if (CheckAndReadContinuationSection(sb))
            {
                return sb.ToString();
            }
        }

        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '"')
            {
                Advance();
                if (_pos < _src.Length && _src[_pos] == '"')
                {
                    sb.Append('"'); // escaped ""
                    Advance();
                }
                else
                    break; // end of string
            }
            else if (c == '`')
            {
                Advance();
                if (_pos < _src.Length)
                {
                    // Keep the backtick escape as-is for round-trip fidelity
                    sb.Append('`');
                    sb.Append(_src[_pos]);
                    Advance();
                }
            }
            else if (c == ';')
            {
                // Check if it is a comment start
                bool isComment = false;
                if (sb.Length > 0)
                {
                    char prev = sb[sb.Length - 1];
                    if (prev == ' ' || prev == '\t')
                        isComment = true;
                }
                if (isComment)
                {
                    // Semicolon starts a comment! Check if next line is continuation section
                    if (CheckAndReadContinuationSection(sb))
                    {
                        break;
                    }
                    else
                    {
                        sb.Append(c);
                        Advance();
                    }
                }
                else
                {
                    sb.Append(c);
                    Advance();
                }
            }
            else if (c == '\n' || c == '\r')
            {
                // Newline - check for continuation section
                if (CheckAndReadContinuationSection(sb))
                {
                    break;
                }
                else
                {
                    break;
                }
            }
            else
            {
                sb.Append(c);
                Advance();
            }
        }
        return sb.ToString();
    }

    private string ReadSingleQuotedString()
    {
        var sb = new StringBuilder();
        Advance(); // skip opening '

        // Check if starts immediately with continuation section
        int checkPos = _pos;
        while (checkPos < _src.Length && (_src[checkPos] == ' ' || _src[checkPos] == '\t'))
        {
            checkPos++;
        }
        if (checkPos < _src.Length && (_src[checkPos] == ';' || _src[checkPos] == '\n' || _src[checkPos] == '\r'))
        {
            if (CheckAndReadContinuationSection(sb))
            {
                return sb.ToString();
            }
        }

        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c == '\'')
            {
                Advance();
                if (_pos < _src.Length && _src[_pos] == '\'')
                {
                    sb.Append('\'');
                    Advance();
                }
                else
                    break;
            }
            else if (c == '`')
            {
                Advance();
                if (_pos < _src.Length)
                {
                    sb.Append('`');
                    sb.Append(_src[_pos]);
                    Advance();
                }
            }
            else if (c == ';')
            {
                // Check if it is a comment start
                bool isComment = false;
                if (sb.Length > 0)
                {
                    char prev = sb[sb.Length - 1];
                    if (prev == ' ' || prev == '\t')
                        isComment = true;
                }
                if (isComment)
                {
                    // Semicolon starts a comment! Check if next line is continuation section
                    if (CheckAndReadContinuationSection(sb))
                    {
                        break;
                    }
                    else
                    {
                        sb.Append(c);
                        Advance();
                    }
                }
                else
                {
                    sb.Append(c);
                    Advance();
                }
            }
            else if (c == '\n' || c == '\r')
            {
                // Newline - check for continuation section
                if (CheckAndReadContinuationSection(sb))
                {
                    break;
                }
                else
                {
                    break;
                }
            }
            else
            {
                sb.Append(c);
                Advance();
            }
        }
        return sb.ToString();
    }

    private string ReadNumber()
    {
        int start = _pos;
        // Hex: 0x...
        if (Peek() == '0' && (PeekAt(1) == 'x' || PeekAt(1) == 'X'))
        {
            Advance(); Advance();
            while (_pos < _src.Length && IsHexDigit(_src[_pos]))
                Advance();
            return _src.Substring(start, _pos - start);
        }
        // Decimal
        bool hasDot = false;
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (char.IsDigit(c)) { Advance(); continue; }
            if (c == '.' && !hasDot) { hasDot = true; Advance(); continue; }
            if (c == 'e' || c == 'E')
            {
                Advance();
                if (_pos < _src.Length && (_src[_pos] == '+' || _src[_pos] == '-'))
                    Advance();
                continue;
            }
            break;
        }
        return _src.Substring(start, _pos - start);
    }

    /// <summary>
    /// AHK v2 names are letters, digits, underscore and any non-ASCII character (`★a★b★c:` is a valid label).
    /// The joiner's private-use line marker and non-ASCII blanks are not name characters.
    /// </summary>
    private static bool IsNameChar(char c)
    {
        if (c < 128) return char.IsLetterOrDigit(c) || c == '_';
        return c != ContinuationJoiner.Nl && !char.IsWhiteSpace(c);
    }

    private static bool IsNameStart(char c)
    {
        return IsNameChar(c) && !(c >= '0' && c <= '9');
    }

    private string ReadIdentifier()
    {
        int start = _pos;
        while (_pos < _src.Length && IsNameChar(_src[_pos]))
            Advance();
        return _src.Substring(start, _pos - start);
    }

    private bool IsContinuationSection()
    {
        // A continuation section starts with ( on its own line
        // After ( there may be options (LTrim, Join, etc.) then newline
        int p = _pos + 1;
        while (p < _src.Length && (_src[p] == ' ' || _src[p] == '\t')) p++;
        // Allow options text on the ( line
        if (p < _src.Length && (_src[p] == '\n' || _src[p] == '\r'))
            return true;
        // Check if there are word chars (options like LTrim) followed by newline
        while (p < _src.Length && _src[p] != '\n' && _src[p] != '\r' && _src[p] != ')')
            p++;
        return p < _src.Length && (_src[p] == '\n' || _src[p] == '\r');
    }

    private bool PrecededByContinuationOperator()
    {
        for (int i = _tokens.Count - 1; i >= 0; i--)
        {
            TokenType tt = _tokens[i].Type;
            if (tt != TokenType.Newline && tt != TokenType.Comment)
            {
                return IsContinuationEndOp(tt);
            }
        }
        return false;
    }


    private string ReadContinuationSection()
    {
        var sb = new StringBuilder();
        Advance(); // skip (
        // Skip rest of opening line (options like LTrim, Join, etc.)
        while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r') Advance();
        if (_pos < _src.Length && _src[_pos] == '\r') Advance();
        if (_pos < _src.Length && _src[_pos] == '\n') Advance();

        while (_pos < _src.Length)
        {
            // Check for closing ) at start of line (with optional leading whitespace)
            int lineStart = _pos;
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
                Advance();
            if (_pos < _src.Length && _src[_pos] == ')')
            {
                Advance(); // skip )
                // Check for closing " after ) (quoted continuation)
                if (_pos < _src.Length && _src[_pos] == '"')
                    Advance();
                break;
            }
            // Not a closing ), rewind and read the whole line
            _pos = lineStart;
            while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r')
            {
                sb.Append(_src[_pos]);
                Advance();
            }
            sb.Append('\n');
            if (_pos < _src.Length && _src[_pos] == '\r') Advance();
            if (_pos < _src.Length && _src[_pos] == '\n') Advance();
        }

        // Trim trailing newline
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
            sb.Length--;

        return sb.ToString();
    }

    private string ReadHotstring()
    {
        int start = _pos;
        while (_pos < _src.Length && _src[_pos] != '\n')
            Advance();
        return _src.Substring(start, _pos - start);
    }

    private int FindHotkeyDoubleColon()
    {
        int p = _pos;
        while (p < _src.Length && (_src[p] == ' ' || _src[p] == '\t' || _src[p] == '\r'))
        {
            p++;
        }

        if (p >= _src.Length) return -1;

        // `:::` is a hotkey on the colon key; any other line starting with ':' is left to the hotstring rules
        if (_src[p] == ':')
        {
            bool colonKey = p + 2 < _src.Length && _src[p + 1] == ':' && _src[p + 2] == ':'
                && (p + 3 >= _src.Length || _src[p + 3] == ' ' || _src[p + 3] == '\t' || _src[p + 3] == '\r' || _src[p + 3] == '\n' || _src[p + 3] == '{' || _src[p + 3] == ';');
            return colonKey ? p + 1 : -1;
        }

        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        while (p < _src.Length)
        {
            char c = _src[p];

            if (c == '\n' || c == ContinuationJoiner.Nl)
                break;

            if (inDoubleQuote)
            {
                if (c == '"')
                {
                    inDoubleQuote = false;
                }
                else if (c == '`' && p + 1 < _src.Length)
                {
                    p++;
                }
            }
            else if (inSingleQuote)
            {
                if (c == '\'')
                {
                    inSingleQuote = false;
                }
                else if (c == '`' && p + 1 < _src.Length)
                {
                    p++;
                }
            }
            else
            {
                // `` `; `` escapes the semicolon (a hotkey on the ; key: `` `;:: ``), so it starts no comment
                if (c == '`' && p + 1 < _src.Length && _src[p + 1] != '\n')
                {
                    p += 2;
                    continue;
                }
                if (c == ';')
                {
                    break;
                }
                if (c == '/' && p + 1 < _src.Length && _src[p + 1] == '*')
                {
                    break;
                }
                if (c == '"')
                {
                    inDoubleQuote = true;
                }
                else if (c == '\'')
                {
                    inSingleQuote = true;
                }
                else if (c == ':' && p + 1 < _src.Length && _src[p + 1] == ':')
                {
                    // `+:::` is Shift+colon: when only modifier symbols precede, the key itself is the first ':'
                    // (`a:::` stays a remap of `a` to the colon key)
                    if (p + 2 < _src.Length && _src[p + 2] == ':'
                        && _src.Substring(_pos, p - _pos).Trim().All(ch => "~*$!^+#<>".IndexOf(ch) >= 0))
                        return p + 1;
                    return p;
                }
            }
            p++;
        }
        return -1;
    }

    private Token ReadOperator()
    {
        int line = _line, col = _col;
        char c = Peek();
        char c2 = PeekAt(1);
        char c3 = PeekAt(2);

        // Four-char operator (must be tested before >>>)
        if (c == '>' && c2 == '>' && c3 == '>' && PeekAt(3) == '=') { Advance(); Advance(); Advance(); Advance(); return new Token(TokenType.UnsignedShiftRightAssign, ">>>=", line, col); }

        // Three-char operators
        if (c == '>' && c2 == '>' && c3 == '>') { Advance(); Advance(); Advance(); return new Token(TokenType.UnsignedShiftRight, ">>>", line, col); }
        if (c == '>' && c2 == '>' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.ShiftRightAssign, ">>=", line, col); }
        if (c == '/' && c2 == '/' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.IntDivAssign, "//=", line, col); }
        if (c == '<' && c2 == '<' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.ShiftLeftAssign, "<<=", line, col); }
        if (c == '!' && c2 == '=' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.StrictNotEqual, "!==", line, col); }
        if (c == '=' && c2 == '=' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.StrictEqual, "===", line, col); }
        if (c == '?' && c2 == '?' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.NullCoalesceAssign, "??=", line, col); }

        // Two-char operators
        if (c == '=' && c2 == '>') { Advance(); Advance(); return new Token(TokenType.FatArrow, "=>", line, col); }
        if (c == ':' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.ColonAssign, ":=", line, col); }
        if (c == '+' && c2 == '+') { Advance(); Advance(); return new Token(TokenType.Increment, "++", line, col); }
        if (c == '-' && c2 == '-') { Advance(); Advance(); return new Token(TokenType.Decrement, "--", line, col); }
        if (c == '+' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.PlusAssign, "+=", line, col); }
        if (c == '-' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.MinusAssign, "-=", line, col); }
        if (c == '*' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.StarAssign, "*=", line, col); }
        if (c == '/' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.SlashAssign, "/=", line, col); }
        if (c == '.' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.DotAssign, ".=", line, col); }
        if (c == '&' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.BitwiseAndAssign, "&=", line, col); }
        if (c == '|' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.BitwiseOrAssign, "|=", line, col); }
        if (c == '^' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.BitwiseXorAssign, "^=", line, col); }
        if (c == '&' && c2 == '&') { Advance(); Advance(); return new Token(TokenType.LogicalAnd, "&&", line, col); }
        if (c == '|' && c2 == '|') { Advance(); Advance(); return new Token(TokenType.LogicalOr, "||", line, col); }
        if (c == '?' && c2 == '?') { Advance(); Advance(); return new Token(TokenType.NullCoalesce, "??", line, col); }
        if (c == '=' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.Equal, "==", line, col); }
        if (c == '!' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.NotEqual, "!=", line, col); }
        if (c == '<' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.LessEqual, "<=", line, col); }
        if (c == '>' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.GreaterEqual, ">=", line, col); }
        if (c == '<' && c2 == '<') { Advance(); Advance(); return new Token(TokenType.ShiftLeft, "<<", line, col); }
        if (c == '>' && c2 == '>') { Advance(); Advance(); return new Token(TokenType.ShiftRight, ">>", line, col); }
        if (c == '~' && c2 == '=') { Advance(); Advance(); return new Token(TokenType.RegexEqual, "~=", line, col); }
        if (c == '/' && c2 == '/') { Advance(); Advance(); return new Token(TokenType.IntDiv, "//", line, col); }
        if (c == '*' && c2 == '*') { Advance(); Advance(); return new Token(TokenType.Power, "**", line, col); }
        if (c == '.' && c2 == '.') { Advance(); Advance(); return new Token(TokenType.DotDot, "..", line, col); }

        // Single-char operators
        switch (c)
        {
            case '+': Advance(); return new Token(TokenType.Plus, "+", line, col);
            case '-': Advance(); return new Token(TokenType.Minus, "-", line, col);
            case '*': Advance(); return new Token(TokenType.Star, "*", line, col);
            case '/': Advance(); return new Token(TokenType.Slash, "/", line, col);
            case '=': Advance(); return new Token(TokenType.Equal, "=", line, col);
            case '<': Advance(); return new Token(TokenType.Less, "<", line, col);
            case '>': Advance(); return new Token(TokenType.Greater, ">", line, col);
            case '!': Advance(); return new Token(TokenType.LogicalNot, "!", line, col);
            case '~': Advance(); return new Token(TokenType.BitwiseNot, "~", line, col);
            case '&': Advance(); return new Token(TokenType.BitwiseAnd, "&", line, col);
            case '|': Advance(); return new Token(TokenType.BitwiseOr, "|", line, col);
            case '^': Advance(); return new Token(TokenType.BitwiseXor, "^", line, col);
            case '?': Advance(); return new Token(TokenType.Ternary, "?", line, col);
            case ':': Advance(); return new Token(TokenType.Colon, ":", line, col);
            case '.': Advance(); return new Token(TokenType.Dot, ".", line, col);
            case '(': Advance(); return new Token(TokenType.LParen, "(", line, col);
            case ')': Advance(); return new Token(TokenType.RParen, ")", line, col);
            case '[': Advance(); return new Token(TokenType.LBracket, "[", line, col);
            case ']': Advance(); return new Token(TokenType.RBracket, "]", line, col);
            case '{': Advance(); return new Token(TokenType.LBrace, "{", line, col);
            case '}': Advance(); return new Token(TokenType.RBrace, "}", line, col);
            case ',': Advance(); return new Token(TokenType.Comma, ",", line, col);
        }

        return null;
    }

    private bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    // -- AHK2 Line Continuation Processing ----------------------------------
    // Removes Newline tokens in continuation positions:
    //  1. Inside balanced () or [] - always continuation
    //  2. Before a line starting with a continuation operator (. , + - etc.)
    //  3. After a line ending with a continuation operator

    private List<Token> ProcessContinuations(List<Token> tokens)
    {
        var result = new List<Token>(tokens.Count);
        int parenDepth = 0;
        int bracketDepth = 0;
        // Comments at the end of a line that continues on the next one (`users  ; note` ⏎ `.filter(...)`) would sit
        // between an operand and its `.member`; they move to the end of the joined logical line instead.
        var deferredComments = new List<Token>();

        for (int i = 0; i < tokens.Count; i++)
        {
            TokenType tt = tokens[i].Type;

            // Track delimiter depth
            if (tt == TokenType.LParen) parenDepth++;
            else if (tt == TokenType.RParen && parenDepth > 0) parenDepth--;
            else if (tt == TokenType.LBracket) bracketDepth++;
            else if (tt == TokenType.RBracket && bracketDepth > 0) bracketDepth--;

            // Inside parens or brackets - skip ALL newlines
            if (tt == TokenType.Newline && (parenDepth > 0 || bracketDepth > 0))
                continue;

            // At top level, check continuation rules
            if (tt == TokenType.Newline)
            {
                // Find next non-trivial token
                TokenType nextType = TokenType.EOF;
                for (int j = i + 1; j < tokens.Count; j++)
                {
                    if (tokens[j].Type != TokenType.Newline && tokens[j].Type != TokenType.Comment)
                    { nextType = tokens[j].Type; break; }
                }

                // Find previous non-trivial token in result
                TokenType prevType = TokenType.EOF;
                for (int j = result.Count - 1; j >= 0; j--)
                {
                    if (result[j].Type != TokenType.Newline && result[j].Type != TokenType.Comment)
                    { prevType = result[j].Type; break; }
                }

                // Next line starts with continuation operator - remove newline
                // Current line ends with continuation operator - remove newline
                if (IsContinuationStartOp(nextType) || IsContinuationEndOp(prevType))
                {
                    // only a comment between an operand and the next line's leading operator is in the way; after a
                    // line-ending `{` / `(` / operator it stays where it is
                    if (IsContinuationEndOp(prevType)) continue;
                    int k = result.Count;
                    while (k > 0 && result[k - 1].Type == TokenType.Comment) k--;
                    if (k < result.Count)
                    {
                        deferredComments.AddRange(result.GetRange(k, result.Count - k));
                        result.RemoveRange(k, result.Count - k);
                    }
                    continue;
                }

                // a line break that ends the logical line: the deferred comments go before it
                result.AddRange(deferredComments);
                deferredComments.Clear();
            }
            else if (tt == TokenType.EOF && deferredComments.Count > 0)
            {
                result.AddRange(deferredComments);
                deferredComments.Clear();
            }

            result.Add(tokens[i]);
        }

        return result;
    }

    private static bool IsContinuationStartOp(TokenType type)
    {
        if (TokenKinds.IsAssignment(type)) return true;
        switch (type)
        {
            case TokenType.Dot:
            case TokenType.Comma:
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.IntDiv:
            case TokenType.Power:
            case TokenType.DotDot:
            case TokenType.Ternary:
            case TokenType.Colon:
            case TokenType.LogicalAnd:
            case TokenType.LogicalOr:
            case TokenType.LogicalNot:
            case TokenType.BitwiseAnd:
            case TokenType.BitwiseOr:
            case TokenType.BitwiseXor:
            case TokenType.BitwiseNot:
            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.Less:
            case TokenType.Greater:
            case TokenType.LessEqual:
            case TokenType.GreaterEqual:
            case TokenType.ShiftLeft:
            case TokenType.ShiftRight:
            case TokenType.UnsignedShiftRight:
            case TokenType.RegexEqual:
            case TokenType.ColonAssign:
            case TokenType.PlusAssign:
            case TokenType.MinusAssign:
            case TokenType.StarAssign:
            case TokenType.SlashAssign:
            case TokenType.DotAssign:
            case TokenType.BitwiseAndAssign:
            case TokenType.BitwiseOrAssign:
            case TokenType.BitwiseXorAssign:
            case TokenType.IntDivAssign:
            case TokenType.FatArrow:
                return true;
            default:
                return false;
        }
    }

    private static bool IsContinuationEndOp(TokenType type)
    {
        if (TokenKinds.IsAssignment(type)) return true;
        switch (type)
        {
            case TokenType.Dot:
            case TokenType.Comma:
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Star:
            case TokenType.Slash:
            case TokenType.IntDiv:
            case TokenType.Power:
            case TokenType.DotDot:
            case TokenType.Ternary:
            case TokenType.Colon:
            case TokenType.LogicalAnd:
            case TokenType.LogicalOr:
            case TokenType.BitwiseAnd:
            case TokenType.BitwiseOr:
            case TokenType.BitwiseXor:
            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.StrictEqual:
            case TokenType.StrictNotEqual:
            case TokenType.Less:
            case TokenType.Greater:
            case TokenType.LessEqual:
            case TokenType.GreaterEqual:
            case TokenType.Is:
            case TokenType.ShiftLeft:
            case TokenType.ShiftRight:
            case TokenType.UnsignedShiftRight:
            case TokenType.RegexEqual:
            case TokenType.NullCoalesce:
            case TokenType.NullCoalesceAssign:
            case TokenType.ColonAssign:
            case TokenType.PlusAssign:
            case TokenType.MinusAssign:
            case TokenType.StarAssign:
            case TokenType.SlashAssign:
            case TokenType.DotAssign:
            case TokenType.BitwiseAndAssign:
            case TokenType.BitwiseOrAssign:
            case TokenType.BitwiseXorAssign:
            case TokenType.IntDivAssign:
            case TokenType.FatArrow:
            case TokenType.LParen:
            case TokenType.LBracket:
            case TokenType.LBrace:
                return true;
            default:
                return false;
        }
    }
}

