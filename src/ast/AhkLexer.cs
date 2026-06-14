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

    public AhkLexer(string source)
    {
        _src = source ?? "";
        _pos = 0;
        _line = 1;
        _col = 1;
        _tokens = new List<Token>();
    }

    public List<Token> Tokenize()
    {
        while (_pos < _src.Length)
        {
            _tokenStartLine = _line;
            _tokenStartCol = _col;
            char c = Peek();

            // Skip whitespace (not newlines)
            if (c == ' ' || c == '\t' || c == '\r')
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
                string str = ReadDoubleQuotedString();
                _tokens.Add(new Token(TokenType.String, "\"" + str + "\"", _tokenStartLine, _tokenStartCol));
                continue;
            }
            if (c == '\'')
            {
                string str = ReadSingleQuotedString();
                _tokens.Add(new Token(TokenType.String, "'" + str + "'", _tokenStartLine, _tokenStartCol));
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
                if (IsContinuationSection())
                {
                    string section = ReadContinuationSection();
                    Emit(TokenType.String, section);
                    continue;
                }
            }

            // Identifiers and keywords
            if (char.IsLetter(c) || c == '_')
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



            // Variable dereferencing: %expr%
            if (c == '%')
            {
                int start = _pos;
                Advance(); // skip opening %
                while (_pos < _src.Length && _src[_pos] != '%' && _src[_pos] != '\n')
                    Advance();
                string inner = _src.Substring(start + 1, _pos - start - 1);
                if (_pos < _src.Length && _src[_pos] == '%')
                    Advance(); // skip closing %
                Emit(TokenType.Identifier, "%" + inner + "%");
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

        _tokens.Add(new Token(TokenType.EOF, "", _line, _col));
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

        // We need to find the second ':' to close the options.
        // It must be on the same line, before any whitespace or newline.
        while (p < _src.Length && _src[p] != '\n' && _src[p] != '\r' && _src[p] != ' ' && _src[p] != '\t' && _src[p] != ':')
        {
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
        while (_pos < _src.Length && _src[_pos] != '\n')
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
        // Unterminated block comment - resilient, don't crash
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

    private string ReadDoubleQuotedString()
    {
        var sb = new StringBuilder();
        Advance(); // skip opening "

        // Check for quoted continuation section: "
        // (LTrim
        // ...text...
        // )"
        // The " is immediately followed by whitespace/newline, then ( at start of next line
        if (_pos < _src.Length && (_src[_pos] == '\n' || (_src[_pos] == '\r' && _pos + 1 < _src.Length && _src[_pos + 1] == '\n')))
        {
            // Save position to check if next line starts with (
            int save = _pos;
            int saveLine = _line;
            int saveCol = _col;
            if (_src[_pos] == '\r') Advance();
            if (_pos < _src.Length && _src[_pos] == '\n') Advance();

            // Skip leading whitespace on next line
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t')) Advance();

            if (_pos < _src.Length && _src[_pos] == '(')
            {
                // This is a quoted continuation section
                Advance(); // skip (

                // Skip options on the ( line (LTrim, Join, etc.) and newline
                while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r') Advance();
                if (_pos < _src.Length && _src[_pos] == '\r') Advance();
                if (_pos < _src.Length && _src[_pos] == '\n') Advance();

                // Read lines until ) at start of line
                while (_pos < _src.Length)
                {
                    // Check for ) at start of line (with optional whitespace)
                    int ls = _pos;
                    while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
                        Advance();

                    if (_pos < _src.Length && _src[_pos] == ')')
                    {
                        Advance(); // skip )
                        // Check for closing " after )
                        if (_pos < _src.Length && _src[_pos] == '"')
                            Advance(); // skip closing "
                        break;
                    }

                    // Not a closing ), rewind and read the whole line
                    _pos = ls;
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
            else
            {
                // Not a continuation section, restore position
                _pos = save;
                _line = saveLine;
                _col = saveCol;
            }
        }

        // Normal double-quoted string
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
            else if (c == '\n' || c == '\r')
            {
                // Newline inside a non-continuation string - end the string
                break;
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

        // Check for continuation section: '
        // (LTrim
        // ...text...
        // )'
        if (_pos < _src.Length && (_src[_pos] == '\n' || (_src[_pos] == '\r' && _pos + 1 < _src.Length && _src[_pos + 1] == '\n')))
        {
            int save = _pos;
            int saveLine = _line;
            int saveCol = _col;
            if (_src[_pos] == '\r') Advance();
            if (_pos < _src.Length && _src[_pos] == '\n') Advance();

            // Skip leading whitespace on next line
            while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t')) Advance();

            if (_pos < _src.Length && _src[_pos] == '(')
            {
                // This is a continuation section
                Advance(); // skip (

                // Skip options on the ( line and newline
                while (_pos < _src.Length && _src[_pos] != '\n' && _src[_pos] != '\r') Advance();
                if (_pos < _src.Length && _src[_pos] == '\r') Advance();
                if (_pos < _src.Length && _src[_pos] == '\n') Advance();

                // Read lines until ) at start of line
                while (_pos < _src.Length)
                {
                    int ls = _pos;
                    while (_pos < _src.Length && (_src[_pos] == ' ' || _src[_pos] == '\t'))
                        Advance();

                    if (_pos < _src.Length && _src[_pos] == ')')
                    {
                        Advance(); // skip )
                        // Check for closing ' after )
                        if (_pos < _src.Length && _src[_pos] == '\'')
                            Advance();
                        break;
                    }

                    // Not a closing ), rewind and read the whole line
                    _pos = ls;
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
            else
            {
                // Not a continuation section, restore position
                _pos = save;
                _line = saveLine;
                _col = saveCol;
            }
        }

        // Normal single-quoted string
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
            else if (c == '\n' || c == '\r')
            {
                // Newline inside a non-continuation string - end the string
                break;
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

    private string ReadIdentifier()
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
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

        if (_src[p] == ':') return -1;

        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        while (p < _src.Length)
        {
            char c = _src[p];

            if (c == '\n')
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

        // Three-char operators
        if (c == '>' && c2 == '>' && c3 == '>') { Advance(); Advance(); Advance(); return new Token(TokenType.UnsignedShiftRight, ">>>", line, col); }
        if (c == '>' && c2 == '>' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.Assign, ">>=", line, col); }
        if (c == '/' && c2 == '/' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.IntDivAssign, "//=", line, col); }
        if (c == '<' && c2 == '<' && c3 == '=') { Advance(); Advance(); Advance(); return new Token(TokenType.Assign, "<<=", line, col); }
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
                if (IsContinuationStartOp(nextType))
                    continue;

                // Current line ends with continuation operator - remove newline
                if (IsContinuationEndOp(prevType))
                    continue;
            }

            result.Add(tokens[i]);
        }

        return result;
    }

    private static bool IsContinuationStartOp(TokenType type)
    {
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

