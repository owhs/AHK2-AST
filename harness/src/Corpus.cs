using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AstHarness
{
    public class Article
    {
        public string Id;          // short content hash
        public string Path;
        public string Tier;        // core | converter | history
        public string Role;        // entry | lib
        public string Hash;
        public int Bytes;
        public int Lines;
        public int Dups;           // other paths with identical content
        public int IncludedBy;     // how many corpus files #Include this one
        public string Skip;        // non-null => not a v2 test article (e.g. "requires AutoHotkey v1")
        [System.Web.Script.Serialization.ScriptIgnore] public string IncSig; // transient: hash of transitive include files + mtimes
    }

    public class CorpusFile
    {
        public string Generated;
        public List<Article> Articles = new List<Article>();
    }

    public static class Corpus
    {
        public static string ManifestPath { get { return Path.Combine(Paths.State, "corpus.json"); } }

        static readonly string[] TierOrder = { "core", "converter", "history" };

        public static CorpusFile Load()
        {
            if (!File.Exists(ManifestPath)) throw new FileNotFoundException("No corpus yet — run: harness.ps1 scan");
            return Util.FromJson<CorpusFile>(File.ReadAllText(ManifestPath));
        }

        static List<KeyValuePair<string, string>> ReadRoots(string file)
        {
            var roots = new List<KeyValuePair<string, string>>();
            foreach (string raw in File.ReadAllLines(file))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                var m = Regex.Match(l, @"^(\w+)\s+(.+)$");
                if (!m.Success) continue;
                string dir = Environment.ExpandEnvironmentVariables(m.Groups[2].Value.Trim());
                if (Directory.Exists(dir)) roots.Add(new KeyValuePair<string, string>(m.Groups[1].Value.ToLowerInvariant(), Path.GetFullPath(dir).TrimEnd('\\')));
                else Console.WriteLine("  (skipping missing root " + dir + ")");
            }
            return roots;
        }

        static List<string> ReadExcludes(string file)
        {
            var list = new List<string>();
            if (!File.Exists(file)) return list;
            foreach (string raw in File.ReadAllLines(file))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                list.Add(Environment.ExpandEnvironmentVariables(l));
            }
            return list;
        }

        public static int Scan(Args a)
        {
            string rootsFile = a.Get("roots", Path.Combine(Paths.CorpusCfg, "roots.txt"));
            var roots = ReadRoots(rootsFile);
            var excludes = ReadExcludes(Path.Combine(Paths.CorpusCfg, "exclude.txt"));
            Console.WriteLine("Scanning " + roots.Count + " roots...");

            // path -> tier (most specific root wins)
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots.OrderBy(r => r.Value.Length))
            {
                foreach (string f in SafeEnumerate(root.Value))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".ahk" && ext != ".ah2" && ext != ".ahk2") continue;
                    if (excludes.Any(x => f.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    files[f] = root.Key; // longer (more specific) roots overwrite shorter ones
                }
            }
            Console.WriteLine("  " + files.Count + " candidate files");

            var byHash = new Dictionary<string, Article>();
            var includeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ordered = files.OrderBy(kv => Array.IndexOf(TierOrder, kv.Value) < 0 ? 99 : Array.IndexOf(TierOrder, kv.Value)).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);
            int tooBig = 0, unreadable = 0;
            foreach (var kv in ordered)
            {
                string text;
                try
                {
                    var fi = new FileInfo(kv.Key);
                    if (fi.Length > 3 * 1024 * 1024 || fi.Length == 0) { tooBig++; continue; }
                    text = Util.ReadScript(kv.Key);
                }
                catch { unreadable++; continue; }

                string hash = Util.Sha1(Util.NormalizeNewlines(text));
                Article existing;
                if (byHash.TryGetValue(hash, out existing)) { existing.Dups++; continue; }

                string why;
                Ahk.PickExe(text, out why);
                var art = new Article
                {
                    Id = Util.Short(hash),
                    Path = kv.Key,
                    Tier = kv.Value,
                    Hash = hash,
                    Bytes = text.Length,
                    Lines = Util.CountLines(text),
                    Skip = why,
                };
                byHash[hash] = art;

                try
                {
                    foreach (string inc in Ahk.Prepare(text, kv.Key, false).Includes)
                    {
                        int n;
                        includeCounts.TryGetValue(inc, out n);
                        includeCounts[inc] = n + 1;
                    }
                }
                catch { }
            }

            var manifest = new CorpusFile { Generated = DateTime.Now.ToString("s") };
            foreach (var art in byHash.Values)
            {
                int n;
                includeCounts.TryGetValue(art.Path, out n);
                art.IncludedBy = n;
                bool inLib = art.Path.Split('\\').Any(seg => seg.Equals("Lib", StringComparison.OrdinalIgnoreCase) || seg.Equals("Libs", StringComparison.OrdinalIgnoreCase));
                art.Role = (n > 0 || inLib || !HasEntryPoint(art.Path)) ? "lib" : "entry";
                manifest.Articles.Add(art);
            }
            manifest.Articles = manifest.Articles.OrderBy(x => Array.IndexOf(TierOrder, x.Tier)).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllText(ManifestPath, Util.ToJson(manifest));
            Console.WriteLine("  " + manifest.Articles.Count + " unique articles (" + tooBig + " empty/oversized, " + unreadable + " unreadable)");
            PrintSummary(manifest);
            Console.WriteLine("Manifest: " + ManifestPath);
            return 0;
        }

        static IEnumerable<string> SafeEnumerate(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] sub = new string[0], fs = new string[0];
                try
                {
                    var di = new DirectoryInfo(dir);
                    // Never follow junctions/symlinks: they are how the same files show up many times.
                    if (dir != root && (di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    sub = Directory.GetDirectories(dir);
                    fs = Directory.GetFiles(dir);
                }
                catch { }
                foreach (var f in fs) yield return f;
                foreach (var s in sub)
                {
                    string name = Path.GetFileName(s);
                    if (name == ".git" || name == "node_modules" || name == "$RECYCLE.BIN") continue;
                    stack.Push(s);
                }
            }
        }

        public static int Summary(Args a)
        {
            var m = Load();
            PrintSummary(m);
            return 0;
        }

        /// <summary>
        /// A script with no auto-execute code and no hotkeys / hotstrings (only functions, classes, directives)
        /// is a library even when nothing in the corpus includes it: run on its own it does nothing.
        /// </summary>
        static bool HasEntryPoint(string path)
        {
            // Text scan (no engine: a scan must survive any file): at brace depth 0, anything other than a function
            // or class definition, directive, comment or blank line is auto-execute code; `::` is a hotkey/hotstring.
            string text;
            try { text = Util.ReadScript(path); } catch { return true; }
            int depth = 0;
            bool inBlockComment = false, pendingDef = false;
            foreach (string raw in Util.NormalizeNewlines(text).Split('\n'))
            {
                string line = raw.Trim();
                if (inBlockComment) { if (line.StartsWith("*/") || line.EndsWith("*/")) inBlockComment = false; continue; }
                if (line.StartsWith("/*")) { if (!line.EndsWith("*/") || line.Length < 4) inBlockComment = true; continue; }
                string code = System.Text.RegularExpressions.Regex.Replace(line, "\"(?:[^\"`]|`.)*\"|'(?:[^'`]|`.)*'", "\"\"");
                int semi = System.Text.RegularExpressions.Regex.Match(code, @"(^|\s);").Index;
                if (code.StartsWith(";")) code = ""; else if (semi > 0) code = code.Substring(0, semi).Trim();
                if (code.Length == 0) continue;
                if (depth == 0)
                {
                    if (code.Contains("::")) return true; // hotkey / hotstring / remap
                    bool isDef = code.StartsWith("#")
                        || System.Text.RegularExpressions.Regex.IsMatch(code, @"^class\s+\w", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        || System.Text.RegularExpressions.Regex.IsMatch(code, @"^[\w$#@]+\(.*\)\s*(\{.*|=>.*)?$")
                        || (pendingDef && code.StartsWith("{"));
                    if (!isDef) return true;
                    pendingDef = System.Text.RegularExpressions.Regex.IsMatch(code, @"^[\w$#@]+\(.*\)$") || System.Text.RegularExpressions.Regex.IsMatch(code, @"^class\s+\w[^{]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                foreach (char c in code) { if (c == '{') depth++; else if (c == '}' && depth > 0) depth--; }
            }
            return false;
        }
        static void PrintSummary(CorpusFile m)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format("  {0,-10} {1,7} {2,7} {3,7} {4,9} {5,10}", "tier", "total", "entry", "lib", "not-v2", "lines"));
            foreach (var g in m.Articles.GroupBy(x => x.Tier).OrderBy(g => Array.IndexOf(TierOrder, g.Key)))
            {
                Console.WriteLine(string.Format("  {0,-10} {1,7} {2,7} {3,7} {4,9} {5,10:N0}", g.Key, g.Count(),
                    g.Count(x => x.Role == "entry" && x.Skip == null), g.Count(x => x.Role == "lib" && x.Skip == null), g.Count(x => x.Skip != null), g.Sum(x => x.Lines)));
            }
            var skips = m.Articles.Where(x => x.Skip != null).GroupBy(x => x.Skip).OrderByDescending(g => g.Count()).Take(6);
            foreach (var g in skips) Console.WriteLine("    skip: " + g.Key + " ×" + g.Count());
        }

        public static IEnumerable<Article> Select(CorpusFile m, Args a)
        {
            string tier = a.Get("tier", "core").ToLowerInvariant();
            string filter = a.Get("filter", null);
            string role = a.Get("role", null);
            var q = m.Articles.Where(x => x.Skip == null);
            if (tier != "all") { var tiers = tier.Split(','); q = q.Where(x => tiers.Contains(x.Tier)); }
            if (role != null) q = q.Where(x => x.Role == role);
            if (filter != null) q = q.Where(x => x.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            int limit = a.GetInt("limit", 0);
            if (limit > 0) q = q.Take(limit);
            return q;
        }
    }
}
