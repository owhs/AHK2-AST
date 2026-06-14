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

    public Token(TokenType type, string value, int line, int col)
    {
        Type = type; Value = value; Line = line; Column = col;
    }

    public override string ToString()
    {
        return string.Format("[{0} '{1}' @{2}:{3}]", Type, Value, Line, Column);
    }
}
