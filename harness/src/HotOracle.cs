using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AstHarness
{
    /// <summary>
    /// Engine-independent check of hotkeys, remaps and hotstrings. AHK's /Validate accepts "a::b()" just as
    /// happily as the remap "a::b", and keystrokes can't be simulated on the hidden desktop, so the harness
    /// compares these declarations textually: every original label must survive, remap targets must be
    /// unchanged, and plain (non-X) hotstring replacement text must be byte-identical.
    /// </summary>
    public static class HotOracle
    {
        public class Entry
        {
            public string Kind;   // hotkey | remap | hotstring | hotstring-x
            public string Label;  // normalised
            public string Payload;
            public int Line;
        }

        static readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LButton","RButton","MButton","XButton1","XButton2","WheelDown","WheelUp","WheelLeft","WheelRight",
            "CapsLock","Space","Tab","Enter","Escape","Esc","Backspace","BS","ScrollLock","Delete","Del","Insert","Ins",
            "Home","End","PgUp","PgDn","Up","Down","Left","Right","NumLock","NumpadDiv","NumpadMult","NumpadAdd","NumpadSub",
            "NumpadEnter","NumpadDot","NumpadDel","NumpadIns","NumpadClear","NumpadUp","NumpadDown","NumpadLeft","NumpadRight",
            "NumpadHome","NumpadEnd","NumpadPgUp","NumpadPgDn","LWin","RWin","Control","Ctrl","Alt","Shift","LControl","LCtrl",
            "RControl","RCtrl","LShift","RShift","LAlt","RAlt","Browser_Back","Browser_Forward","Browser_Refresh","Browser_Stop",
            "Browser_Search","Browser_Favorites","Browser_Home","Volume_Mute","Volume_Down","Volume_Up","Media_Next","Media_Prev",
            "Media_Stop","Media_Play_Pause","Launch_Mail","Launch_Media","Launch_App1","Launch_App2","AppsKey","PrintScreen",
            "CtrlBreak","Help","Sleep"
        };

        static bool IsKey(string k)
        {
            if (k.Length == 1) return !char.IsWhiteSpace(k[0]) && "{(,;".IndexOf(k[0]) < 0;
            if (Named.Contains(k)) return true;
            if (Regex.IsMatch(k, @"^(F([1-9]|1\d|2[0-4])|Numpad\d|vk[0-9A-Fa-f]{1,2}|sc[0-9A-Fa-f]{1,3})$", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        /// <summary>Hotkey origins may be any key, including ones that can't be remap destinations.</summary>
        static bool IsOriginKey(string k)
        {
            return IsKey(k) || Regex.IsMatch(k, @"^(Pause|Return|\d?Joy\d{1,2}|vk[0-9A-Fa-f]{1,2}sc[0-9A-Fa-f]{1,3})$", RegexOptions.IgnoreCase);
        }

        static readonly Regex HotkeyRx = new Regex(@"^\s*(?<label>[~$*<>^!+#]*(?<k1>[^\s:&][^\s:&]*|:)(?:\s+&\s+[~$*]*(?<k2>[^\s:&]+))?(?:\s+up)?)::(?<rest>.*)$", RegexOptions.IgnoreCase);
        static readonly Regex HotstringRx = new Regex(@"^\s*:(?<opts>[^:\s]*):(?<abbr>.+?)::(?<rest>.*)$");
        // Remap destination: optional ^!+#<> modifiers then exactly one key (AHK 2.0: `a Up` is code, not a remap).
        static readonly Regex RemapRx = new Regex(@"^[\^!+#<>]*(?<key>\S+)$");

        public static List<Entry> Extract(string text)
        {
            var list = new List<Entry>();
            bool xDefault = false, inComment = false;
            int depth = 0;
            var lines = Util.NormalizeNewlines(text).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.Trim();
                if (inComment) { if (t.StartsWith("*/") || t.EndsWith("*/")) inComment = false; continue; }
                if (t.StartsWith("/*")) { if (!t.EndsWith("*/") || t.Length < 4) inComment = true; continue; }
                if (t.StartsWith("(") && !t.Contains(")")) // continuation section: skip to its end
                {
                    while (i + 1 < lines.Length && !lines[i + 1].TrimStart().StartsWith(")")) i++;
                    continue;
                }
                var dm = Regex.Match(t, @"^#Hotstring\s+(.*)$", RegexOptions.IgnoreCase);
                if (dm.Success)
                {
                    string o = StripComment(dm.Groups[1].Value);
                    if (Regex.IsMatch(o, @"(^|\s)X0(\s|$)", RegexOptions.IgnoreCase)) xDefault = false;
                    else if (Regex.IsMatch(o, @"(^|\s)X(\s|$)", RegexOptions.IgnoreCase)) xDefault = true;
                    continue;
                }
                // Hotkeys/hotstrings only exist at the top level.
                if (depth == 0)
                {
                    var hs = HotstringRx.Match(line);
                    if (hs.Success)
                    {
                        string opts = hs.Groups["opts"].Value;
                        bool x = xDefault;
                        var xm = Regex.Match(opts, @"X(0?)", RegexOptions.IgnoreCase);
                        if (xm.Success) x = xm.Groups[1].Value != "0";
                        string rest = StripComment(hs.Groups["rest"].Value);
                        list.Add(new Entry { Kind = x ? "hotstring-x" : "hotstring", Label = ":" + opts.ToUpperInvariant() + ":" + hs.Groups["abbr"].Value, Payload = x ? rest.Trim() : rest.TrimStart(), Line = i + 1 });
                    }
                    else
                    {
                        var hk = HotkeyRx.Match(line);
                        if (hk.Success && IsOriginKey(hk.Groups["k1"].Value) && (!hk.Groups["k2"].Success || IsOriginKey(hk.Groups["k2"].Value)))
                        {
                            string rest = StripComment(hk.Groups["rest"].Value).Trim();
                            var rm = RemapRx.Match(rest);
                            bool remap = rest.Length > 0 && rm.Success && IsKey(rm.Groups["key"].Value) && !rest.StartsWith("{");
                            list.Add(new Entry { Kind = remap ? "remap" : "hotkey", Label = Regex.Replace(hk.Groups["label"].Value, @"\s+", " ").Trim().ToLowerInvariant(), Payload = remap ? rest.ToLowerInvariant() : "", Line = i + 1 });
                        }
                    }
                }
                depth += BraceDelta(line);
                if (depth < 0) depth = 0;
            }
            return list;
        }

        static int BraceDelta(string line)
        {
            int d = 0;
            bool inStr = false;
            char q = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inStr)
                {
                    if (c == '`') i++;
                    else if (c == q) inStr = false;
                    continue;
                }
                if (c == '"' || c == '\'') { inStr = true; q = c; }
                else if (c == ';' && (i == 0 || line[i - 1] == ' ' || line[i - 1] == '\t')) break;
                else if (c == '{') d++;
                else if (c == '}') d--;
            }
            return d;
        }

        static string StripComment(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == ';' && (i == 0 || s[i - 1] == ' ' || s[i - 1] == '\t') && (i == 0 || s[i - 1] != '`')) return s.Substring(0, i).TrimEnd();
            return s.TrimEnd();
        }

        /// <summary>Returns human-readable problems (empty when equivalent).</summary>
        public static List<string> Compare(string original, string output)
        {
            var problems = new List<string>();
            var a = Extract(original);
            if (a.Count == 0) return problems;
            var b = Extract(output);
            var byLabel = new Dictionary<string, List<Entry>>();
            foreach (var e in b)
            {
                List<Entry> l;
                if (!byLabel.TryGetValue(e.Label, out l)) byLabel[e.Label] = l = new List<Entry>();
                l.Add(e);
            }
            foreach (var e in a)
            {
                List<Entry> cands;
                if (!byLabel.TryGetValue(e.Label, out cands) || cands.Count == 0)
                {
                    problems.Add("lost " + e.Kind + " `" + e.Label + "` (orig line " + e.Line + ")");
                    continue;
                }
                var match = cands[0];
                cands.RemoveAt(0);
                if (e.Kind == "remap" && (match.Kind != "remap" || match.Payload != e.Payload))
                    problems.Add("remap `" + e.Label + "::" + e.Payload + "` became " + Describe(match) + " (orig line " + e.Line + ")");
                else if (e.Kind == "hotstring" && (match.Kind != "hotstring" || match.Payload != e.Payload))
                    problems.Add("hotstring `" + e.Label + "::" + Util.Clip(e.Payload, 60) + "` became " + Describe(match) + " (orig line " + e.Line + ")");
                else if (e.Kind == "hotstring-x" && match.Kind != "hotstring-x")
                    problems.Add("X (expression) hotstring `" + e.Label + "` became a plain replacement `" + Util.Clip(match.Payload, 60) + "` (orig line " + e.Line + ")");
            }
            return problems;
        }

        static string Describe(Entry e)
        {
            return e.Kind + " `" + e.Label + "::" + Util.Clip(e.Payload, 60) + "`";
        }
    }
}
