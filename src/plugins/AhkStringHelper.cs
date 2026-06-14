using System;
using System.Text;

namespace AHK2AST.Plugins
{
    internal static class AhkStringHelper
    {
        public static string UnescapeAhkString(string val)
        {
            if (string.IsNullOrEmpty(val) || val.Length < 2) return val;
            char quote = val[0];
            if (quote != '"' && quote != '\'') return val;
            if (val[val.Length - 1] != quote) return val;

            string inner = val.Substring(1, val.Length - 2);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (c == '`')
                {
                    if (i + 1 < inner.Length)
                    {
                        char next = inner[i + 1];
                        switch (next)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'v': sb.Append('\v'); break;
                            case 'a': sb.Append('\a'); break;
                            case 'f': sb.Append('\f'); break;
                            case '`': sb.Append('`'); break;
                            case '\'': sb.Append('\''); break;
                            case '"': sb.Append('"'); break;
                            default: sb.Append(next); break;
                        }
                        i++;
                    }
                    else
                    {
                        sb.Append('`');
                    }
                }
                else if (c == quote && i + 1 < inner.Length && inner[i + 1] == quote)
                {
                    sb.Append(quote);
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string EscapeAhkString(string val)
        {
            if (val == null) return "\"\"";
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in val)
            {
                switch (c)
                {
                    case '\n': sb.Append("`n"); break;
                    case '\r': sb.Append("`r"); break;
                    case '\t': sb.Append("`t"); break;
                    case '\b': sb.Append("`b"); break;
                    case '\v': sb.Append("`v"); break;
                    case '\a': sb.Append("`a"); break;
                    case '\f': sb.Append("`f"); break;
                    case '`': sb.Append("``"); break;
                    case '"': sb.Append("`\""); break;
                    case ';':
                        if (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\t'))
                            sb.Append("`;");
                        else
                            sb.Append(';');
                        break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string NormalizeAhkString(string val, string targetQuote)
        {
            if (string.IsNullOrEmpty(val)) return val;
            string unescaped = UnescapeAhkString(val);
            StringBuilder sb = new StringBuilder();
            sb.Append(targetQuote);
            foreach (char c in unescaped)
            {
                if (c == '\n') sb.Append("`n");
                else if (c == '\r') sb.Append("`r");
                else if (c == '\t') sb.Append("`t");
                else if (c == '\b') sb.Append("`b");
                else if (c == '\v') sb.Append("`v");
                else if (c == '\a') sb.Append("`a");
                else if (c == '\f') sb.Append("`f");
                else if (c == '`') sb.Append("``");
                else if (c == targetQuote[0]) sb.Append("`" + targetQuote);
                else if (c == ';')
                {
                    if (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '\t'))
                        sb.Append("`;");
                    else
                        sb.Append(';');
                }
                else sb.Append(c);
            }
            sb.Append(targetQuote);
            return sb.ToString();
        }
    }
}
