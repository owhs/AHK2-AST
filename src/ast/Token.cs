using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public enum TokenType
{
    // Literals
    Number, String, Identifier,
    // Operators
    Plus, Minus, Star, Slash, IntDiv, Power,
    Dot, DotDot, Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign,
    DotAssign, ColonAssign,
    BitwiseAndAssign, BitwiseOrAssign, BitwiseXorAssign, IntDivAssign, // &=, |=, ^=, //=
    ShiftLeftAssign, ShiftRightAssign, UnsignedShiftRightAssign, // <<=, >>=, >>>=
    Equal, NotEqual, StrictEqual, StrictNotEqual, // ==, !=, ===, !==
    Less, Greater, LessEqual, GreaterEqual,
    RegexEqual, // ~=
    LogicalAnd, LogicalOr, LogicalNot,
    BitwiseAnd, BitwiseOr, BitwiseXor, BitwiseNot,
    ShiftLeft, ShiftRight, UnsignedShiftRight,
    Ternary, Colon,
    NullCoalesce, NullCoalesceAssign, // ??, ??=
    Increment, Decrement, // ++, --
    FatArrow,
    // Delimiters
    LParen, RParen, LBracket, RBracket, LBrace, RBrace,
    Comma, Semicolon,
    // Keywords
    If, Else, While, For, Loop, Until, Break, Continue, Return,
    Class, Extends, Super, This, Is,
    Try, Catch, Finally, Throw,
    Switch, Case, Default,
    Global, Local, Static,
    New,
    // AHK-specific
    Directive, Hotkey, Hotstring,
    ContinuationStart, ContinuationEnd,
    // Special
    Newline, Comment, EOF, Unknown
}

public class Token
{
    public TokenType Type;
    public string Value;
    public int Line;
    public int Column;
    public string Metadata;
    /// <summary>Character offsets [StartOffset, EndOffset) of the token in the ORIGINAL source (before continuation
    /// sections were joined); -1 for tokens the parser made up. Line/Column are the logical (joined-text) position.</summary>
    public int StartOffset = -1;
    public int EndOffset = -1;

    public Token(TokenType type, string value, int line, int col)
    {
        Type = type; Value = value; Line = line; Column = col;
    }

    public override string ToString()
    {
        return string.Format("[{0} '{1}' @{2}:{3}]", Type, Value, Line, Column);
    }
}

/// <summary>Single source of truth for token classes used by both the lexer and the parser.</summary>
public static class TokenKinds
{
    /// <summary>Every assignment operator: := += -= *= /= //= .= |= &amp;= ^= &lt;&lt;= &gt;&gt;= &gt;&gt;&gt;= ??=</summary>
    public static bool IsAssignment(TokenType type)
    {
        switch (type)
        {
            case TokenType.Assign:
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
            case TokenType.ShiftLeftAssign:
            case TokenType.ShiftRightAssign:
            case TokenType.UnsignedShiftRightAssign:
                return true;
            default:
                return false;
        }
    }
}