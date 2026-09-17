using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AHK2AST.Plugins
{
    /// <summary>
    /// Compile-time AHK v2 values for constant folding, following AHK's own rules so a folded expression means
    /// exactly what the original did. A value is a <c>long</c> (Integer), a <c>double</c> (Float) or a
    /// <c>string</c>; true/false are the Integers 1/0. Anything whose AHK result isn't certain (float formatting,
    /// numeric-looking strings, locale-dependent case folding, overflow) reports "unknown" and is left alone.
    /// </summary>
    public static class AhkConst
    {
        static readonly Regex NumericLike = new Regex(@"^\s*[-+]?(0[xX][0-9a-fA-F]+|\d+\.?\d*([eE][-+]?\d+)?|\.\d+([eE][-+]?\d+)?)\s*$");
        static readonly Regex IntegerText = new Regex(@"^\d+$");
        static readonly Regex FloatText = new Regex(@"^(\d+\.\d*|\.\d+|\d+)([eE][-+]?\d+)?$");

        /// <summary>The constant value of a literal (Number, String, true/false, parenthesised), or null.</summary>
        public static object Get(AstNode node)
        {
            if (node == null) return null;
            switch (node.NodeType)
            {
                case "Grouped":
                    return node.ChildCount == 1 ? Get(node.GetChild(0)) : null;
                case "Number":
                    return ParseNumber(node.Value);
                case "String":
                    if (node.Value == null || node.Value.Length < 2) return null;
                    return AhkStringHelper.UnescapeAhkString(node.Value);
                case "Identifier":
                case "Literal":
                    if (string.Equals(node.Value, "true", StringComparison.OrdinalIgnoreCase)) return 1L;
                    if (string.Equals(node.Value, "false", StringComparison.OrdinalIgnoreCase)) return 0L;
                    return null;
            }
            return null;
        }

        public static object ParseNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long h;
                if (t.Length > 2 && t.Length <= 18 && long.TryParse(t.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out h)) return h;
                return null;
            }
            if (IntegerText.IsMatch(t))
            {
                long l;
                return long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out l) ? (object)l : null;
            }
            if (FloatText.IsMatch(t))
            {
                double d;
                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (object)d : null;
            }
            return null;
        }

        public static bool IsNumber(object v) { return v is long || v is double; }

        /// <summary>AHK truthiness, or null when unknown (a numeric-looking string other than "0").</summary>
        public static bool? Truthy(object v)
        {
            if (v is long) return (long)v != 0;
            if (v is double) return (double)v != 0.0;
            var s = v as string;
            if (s == null) return null;
            if (s.Length == 0 || s == "0") return false;
            if (NumericLike.IsMatch(s))
            {
                // A numeric string is false when its number is 0 ("0.0", "0x0" — verified); with blanks around it: unknown.
                if (s.Trim().Length != s.Length) return null;
                object n = ParseNumber(s.TrimStart('+'));
                if (n == null) return null;
                return n is long ? (long)n != 0 : (double)n != 0.0;
            }
            return true;
        }

        /// <summary>The text AHK produces when the value is used as a string, or null (floats are not guessed).</summary>
        public static string ToText(object v)
        {
            if (v is long) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v as string;
        }

        /// <summary>A literal node for the value, or null when it can't be written back exactly.</summary>
        public static AstNode ToNode(object v, int line, int col)
        {
            // A comparison / `!` result: `true` / `false` are exactly the Integers 1 / 0, and read better.
            if (v is bool) return new AstNode("Identifier", line, col) { Value = (bool)v ? "true" : "false" };
            if (v is long)
            {
                long l = (long)v;
                if (l < 0) return null; // `-5` is an expression, not a literal; leave negatives to the original text
                return new AstNode("Number", line, col) { Value = l.ToString(CultureInfo.InvariantCulture) };
            }
            if (v is double)
            {
                double d = (double)v;
                if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) return null;
                string r = d.ToString("R", CultureInfo.InvariantCulture);
                if (r.IndexOfAny(new[] { 'E', 'e' }) >= 0) return null;
                if (r.IndexOf('.') < 0) r += ".0"; // stays a Float
                return new AstNode("Number", line, col) { Value = r };
            }
            var s = v as string;
            if (s != null) return new AstNode("String", line, col) { Value = AhkStringHelper.EscapeAhkString(s) };
            return null;
        }

        /// <summary>Arithmetic on two numbers, or null. Integer + - * wrap like AHK's 64-bit integers are avoided:
        /// an overflowing result is unknown.</summary>
        public static object Arith(string op, object a, object b)
        {
            if (!IsNumber(a) || !IsNumber(b)) return null;
            bool ints = a is long && b is long;
            try
            {
                switch (op)
                {
                    case "+": return ints ? (object)checked((long)a + (long)b) : D(a) + D(b);
                    case "-": return ints ? (object)checked((long)a - (long)b) : D(a) - D(b);
                    case "*": return ints ? (object)checked((long)a * (long)b) : D(a) * D(b);
                    case "/": return D(b) == 0 ? null : (object)(D(a) / D(b)); // true division: always a Float
                    case "//":
                        // Floor division; only the clear-cut all-integer, non-negative case is folded.
                        if (!ints || (long)b == 0 || (long)a < 0 || (long)b < 0) return null;
                        return (long)a / (long)b;
                }
            }
            catch (OverflowException) { return null; }
            return null;
        }

        static double D(object v) { return v is long ? (double)(long)v : (double)v; }

        /// <summary>`=`, `==`, `!=`, `!==` (and `&lt;&gt;`) between two constants, or null when AHK's result isn't certain.</summary>
        public static bool? Compare(string op, object a, object b)
        {
            bool caseSensitive = op == "==" || op == "!==";
            bool negate = op == "!=" || op == "!==" || op == "<>";
            if (op != "=" && op != "==" && !negate) return null;
            bool? eq = null;
            if (IsNumber(a) && IsNumber(b))
            {
                eq = D(a) == D(b);
            }
            else if (a is string && b is string)
            {
                string sa = (string)a, sb = (string)b;
                if (NumericLike.IsMatch(sa) || NumericLike.IsMatch(sb)) return null; // may compare numerically
                if (caseSensitive) eq = string.Equals(sa, sb, StringComparison.Ordinal);
                else if (IsAscii(sa) && IsAscii(sb)) eq = string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
                else return null; // non-ASCII case folding depends on StringCaseSense
            }
            else return null;
            return negate ? !eq.Value : eq.Value;
        }

        static bool IsAscii(string s)
        {
            foreach (char c in s) if (c > 127) return false;
            return true;
        }
    }
}
