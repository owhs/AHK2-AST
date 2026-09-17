using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AstHarness
{
    public static class Util
    {
        public static JavaScriptSerializer NewJson()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
        }

        public static string ToJson(object o)
        {
            return NewJson().Serialize(o);
        }

        public static T FromJson<T>(string s)
        {
            return NewJson().Deserialize<T>(s);
        }

        public static string Sha1(string text)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(40);
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Short(string hash) { return hash.Length > 10 ? hash.Substring(0, 10) : hash; }

        /// <summary>Reads a script the way the engine and AutoHotkey v2 do (UTF-8 unless a BOM says otherwise).</summary>
        public static string ReadScript(string path)
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public static void WriteScript(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }

        public static string NormalizeNewlines(string s)
        {
            return (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 1;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }

        /// <summary>Returns "line N: `a` vs `b`" for the first differing line, or null if equal (ignoring trailing whitespace).</summary>
        public static string FirstDiff(string a, string b)
        {
            var la = NormalizeNewlines(a).TrimEnd().Split('\n');
            var lb = NormalizeNewlines(b).TrimEnd().Split('\n');
            int n = Math.Max(la.Length, lb.Length);
            for (int i = 0; i < n; i++)
            {
                string x = i < la.Length ? la[i].TrimEnd() : "<eof>";
                string y = i < lb.Length ? lb[i].TrimEnd() : "<eof>";
                if (x != y) return "line " + (i + 1) + ": `" + Clip(x, 160) + "` vs `" + Clip(y, 160) + "`";
            }
            return null;
        }

        public static string Clip(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\r", "").Replace("\n", "⏎");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public static string LineAt(string text, int line)
        {
            if (line <= 0) return "";
            var lines = NormalizeNewlines(text).Split('\n');
            return line <= lines.Length ? lines[line - 1] : "";
        }

        public static string Context(string text, int line, int radius)
        {
            if (line <= 0) return "";
            var lines = NormalizeNewlines(text).Split('\n');
            var sb = new StringBuilder();
            for (int i = Math.Max(1, line - radius); i <= Math.Min(lines.Length, line + radius); i++)
                sb.AppendFormat("{0}{1,5}: {2}\n", i == line ? ">" : " ", i, lines[i - 1]);
            return sb.ToString();
        }

        public static string SafeName(string s, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            string r = sb.ToString();
            return r.Length > max ? r.Substring(0, max) : r;
        }

        /// <summary>
        /// Recursive delete that never descends into junctions/symlinks. It removes the link itself and leaves
        /// the target alone, because relocation dirs contain junctions to the user's real Lib folders.
        /// </summary>
        public static void SafeDeleteTree(string dir)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists) return;
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) { di.Delete(false); return; }
                foreach (var sub in di.GetDirectories()) SafeDeleteTree(sub.FullName);
                foreach (var f in di.GetFiles())
                {
                    try { f.Attributes = FileAttributes.Normal; f.Delete(); } catch { }
                }
                di.Delete(false);
            }
            catch { }
        }

        public static void AppendLine(string path, string line)
        {
            lock (typeof(Util))
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
        }
    }
}
