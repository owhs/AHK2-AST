using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AstHarness
{
    public class AhkMessage
    {
        public string File;
        public int Line;
        public string Message;
        public string Specifically;
        public bool IsWarning;

        public override string ToString()
        {
            return (IsWarning ? "warning" : "error") + " @" + Line + ": " + Message + (string.IsNullOrEmpty(Specifically) ? "" : "  [" + Specifically + "]");
        }
    }

    public class AhkOutcome
    {
        /// <summary>ok | load-error | runtime-error | timeout | launch-error | leak</summary>
        public string Status;
        public int ExitCode;
        public List<AhkMessage> Errors = new List<AhkMessage>();
        public List<AhkMessage> Warnings = new List<AhkMessage>();
        public string Stdout = "";
        public List<WindowCapture> Windows = new List<WindowCapture>();
        public long ElapsedMs;
        public bool Alive; // runtime: still running when time ran out (normal for persistent scripts)

        public bool Ok { get { return Status == "ok"; } }

        public string FirstError
        {
            get { return Errors.Count > 0 ? Errors[0].Message : (Status == "ok" ? "" : Status); }
        }
    }

    public class PreparedScript
    {
        public string Text;
        public int LineOffset;
        public List<string> Notes = new List<string>();
        public List<string> Includes = new List<string>(); // resolved include files (absolute)
    }

    public static class Ahk
    {
        public static string Exe64, Exe32, Version;

        public static void Locate(string configured)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured)) candidates.Add(configured);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(pf, @"AutoHotkey\v2\AutoHotkey64.exe"));
            candidates.Add(Path.Combine(pf, @"AutoHotkey\v2\AutoHotkey.exe"));
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { Exe64 = c; break; }
            }
            if (Exe64 == null) throw new FileNotFoundException("AutoHotkey v2 not found. Pass --ahk <path to AutoHotkey64.exe>.");
            string dir = Path.GetDirectoryName(Exe64);
            Exe32 = File.Exists(Path.Combine(dir, "AutoHotkey32.exe")) ? Path.Combine(dir, "AutoHotkey32.exe") : Exe64;
            Version = FileVersionInfo.GetVersionInfo(Exe64).ProductVersion;
        }

        static readonly Regex RequiresRx = new Regex(@"^\s*#Requires\s+AutoHotkey\b(?<rest>[^\r\n;]*)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        /// <summary>Returns null when the script targets something other than AutoHotkey v2 (v1, AHK_H, 2.1 alphas...).</summary>
        public static string PickExe(string text, out string reason)
        {
            reason = null;
            var m = RequiresRx.Match(text);
            if (!m.Success)
            {
                if (Regex.IsMatch(text, @"^\s*#Requires\s+AutoHotkey_H", RegexOptions.IgnoreCase | RegexOptions.Multiline)) { reason = "requires AutoHotkey_H"; return null; }
                return Exe64;
            }
            string rest = m.Groups["rest"].Value.Trim();
            var vm = Regex.Match(rest, @"v?(\d+)(?:\.(\d+))?");
            if (vm.Success)
            {
                int major = int.Parse(vm.Groups[1].Value);
                int minor = vm.Groups[2].Success ? int.Parse(vm.Groups[2].Value) : 0;
                if (major != 2) { reason = "requires AutoHotkey v" + major; return null; }
                if (minor > 0 && Version != null && !Version.StartsWith("2." + minor)) { reason = "requires v2." + minor; return null; }
            }
            if (rest.IndexOf("32-bit", StringComparison.OrdinalIgnoreCase) >= 0) return Exe32;
            return Exe64;
        }

        // ------------------------------------------------------------------ running

        public static AhkOutcome Validate(string exe, string scriptPath, int lineOffset, int timeoutMs)
        {
            var opt = new LaunchOptions
            {
                Exe = exe,
                Args = "/ErrorStdOut=UTF-8 /Validate \"" + scriptPath + "\"",
                WorkDir = Path.GetDirectoryName(scriptPath),
                TimeoutMs = timeoutMs,
                KillOnError = true,
                DismissDialogs = true,
                ScriptName = Path.GetFileName(scriptPath),
            };
            return Interpret(Sandbox.Instance.Run(opt), lineOffset, false);
        }

        public static AhkOutcome Execute(string exe, string scriptPath, int lineOffset, int timeoutMs, string shotDir)
        {
            var opt = new LaunchOptions
            {
                Exe = exe,
                Args = "/ErrorStdOut=UTF-8 \"" + scriptPath + "\"",
                WorkDir = Path.GetDirectoryName(scriptPath),
                TimeoutMs = timeoutMs,
                KillOnError = true,
                DismissDialogs = true,
                ShotDir = shotDir,
                FinalShot = shotDir != null, // also capture every window at the deadline (pages that render late)
                ScriptName = Path.GetFileName(scriptPath),
            };
            return Interpret(Sandbox.Instance.Run(opt), lineOffset, true);
        }

        static AhkOutcome Interpret(LaunchResult r, int lineOffset, bool runtime)
        {
            var o = new AhkOutcome { ExitCode = r.ExitCode, ElapsedMs = r.ElapsedMs, Windows = r.Windows, Stdout = r.Stdout ?? "" };
            ParseMessages(r.Stderr, lineOffset, o);
            ParseMessages(r.Stdout, lineOffset, o); // #Warn ..., StdOut lands here

            // Warnings / errors that still surfaced as dialogs (e.g. #Warn MsgBox inside an untouched include).
            foreach (var w in r.Windows)
            {
                if (w.Kind != "AhkWarning" && w.Kind != "AhkError") continue;
                var msg = ParseDialog(w.Text);
                msg.IsWarning = w.Kind == "AhkWarning";
                (msg.IsWarning ? o.Warnings : o.Errors).Add(msg);
            }

            if (r.LaunchError != null) { o.Status = "launch-error"; o.Errors.Add(new AhkMessage { Message = r.LaunchError }); }
            else if (r.LeakedWindows > 0) { o.Status = "leak"; o.Errors.Add(new AhkMessage { Message = "window leaked onto the user's desktop" }); }
            else if (r.KilledOnError || o.Errors.Count > 0) o.Status = runtime && !HasLoadError(r) ? "runtime-error" : "load-error";
            else if (r.TimedOut)
            {
                if (runtime) { o.Status = "ok"; o.Alive = true; }
                else o.Status = "timeout";
            }
            else if (r.ExitCode < 0) { o.Status = runtime ? "runtime-error" : "load-error"; o.Errors.Add(new AhkMessage { Message = "AutoHotkey crashed (" + NtStatus(r.ExitCode) + ")" }); }
            else if (r.ExitCode != 0 && !runtime) { o.Status = "load-error"; o.Errors.Add(new AhkMessage { Message = "exit code " + r.ExitCode + " without message" }); }
            else o.Status = "ok";
            return o;
        }

        static string NtStatus(int code)
        {
            switch (unchecked((uint)code))
            {
                case 0xC0000005: return "0xC0000005 access violation";
                case 0xC00000FD: return "0xC00000FD stack overflow";
                case 0xC0000409: return "0xC0000409 stack buffer overrun / fail-fast";
                case 0xC0000017: return "0xC0000017 out of memory";
                case 0xC000001D: return "0xC000001D illegal instruction";
                case 0xC0000374: return "0xC0000374 heap corruption";
                default: return "0x" + unchecked((uint)code).ToString("X8");
            }
        }

        static bool HasLoadError(LaunchResult r)
        {
            return !string.IsNullOrEmpty(r.Stderr) && r.Stderr.Contains(" : ==> ");
        }

        static readonly Regex MsgRx = new Regex(@"^(?<file>.+?) \((?<line>\d+)\) : ==> (?<msg>.*)$");

        static void ParseMessages(string text, int lineOffset, AhkOutcome o)
        {
            if (string.IsNullOrEmpty(text)) return;
            AhkMessage cur = null;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var m = MsgRx.Match(raw);
                if (m.Success)
                {
                    cur = new AhkMessage { File = m.Groups["file"].Value, Line = int.Parse(m.Groups["line"].Value), Message = m.Groups["msg"].Value.Trim() };
                    if (cur.Message.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
                    {
                        cur.IsWarning = true;
                        cur.Message = cur.Message.Substring(8).Trim();
                    }
                    if (lineOffset != 0 && cur.Line > lineOffset) cur.Line -= lineOffset;
                    (cur.IsWarning ? o.Warnings : o.Errors).Add(cur);
                }
                else if (cur != null && raw.TrimStart().StartsWith("Specifically:"))
                {
                    cur.Specifically = raw.TrimStart().Substring(13).Trim();
                }
            }
        }

        static AhkMessage ParseDialog(string text)
        {
            var msg = new AhkMessage { Message = "", Specifically = "" };
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string l = raw.Trim();
                if (msg.Message.Length == 0 && (l.StartsWith("Error:") || l.StartsWith("Warning:") || l.StartsWith("Critical Error:")))
                    msg.Message = l.Substring(l.IndexOf(':') + 1).Trim();
                else if (l.StartsWith("Specifically:")) msg.Specifically = l.Substring(13).Trim();
                else
                {
                    var lm = Regex.Match(l, @"^▶?\s*(\d{3,}):");
                    if (lm.Success && raw.Contains("▶")) msg.Line = int.Parse(lm.Groups[1].Value);
                }
            }
            if (msg.Message.Length == 0) msg.Message = (text ?? "").Split('\n')[0].Trim();
            return msg;
        }

        // ------------------------------------------------------------------ relocation

        static readonly Regex IncludeRx = new Regex(@"^(?<indent>\s*)#(?<kind>Include|IncludeAgain|DllLoad)\b[ \t,]*(?<arg>.*)$", RegexOptions.IgnoreCase);
        static readonly Regex WarnRx = new Regex(@"^(?<indent>\s*)#Warn\b[ \t,]*(?<arg>.*)$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Makes script text loadable from any folder as if it still lived at <paramref name="origPath"/>:
        /// relative / %A_ScriptDir% / &lt;Lib&gt; includes and #DllLoads are rewritten to absolute paths.
        /// When <paramref name="forceWarnStdOut"/> is set, AHK's default warnings are prepended in StdOut mode and every
        /// existing #Warn is switched to StdOut mode, so warnings are captured as text instead of dialogs.
        /// </summary>
        public static PreparedScript Prepare(string text, string origPath, bool forceWarnStdOut)
        {
            var ps = new PreparedScript();
            string origDir = Path.GetDirectoryName(origPath);
            string includeDir = origDir;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool inBlockComment = false, inSection = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                // Continuation sections are text (a string, usually): a `#Include` line in one is not a directive.
                // AHK starts a section at a line beginning with `(` that has no `)`, and ends it at a line beginning with `)`.
                if (inSection)
                {
                    if (trimmed.StartsWith(")")) inSection = false;
                    continue;
                }
                if (!inBlockComment && trimmed.StartsWith("(") && line.IndexOf(')') < 0)
                {
                    inSection = true;
                    continue;
                }
                if (inBlockComment)
                {
                    if (trimmed.StartsWith("*/") || line.TrimEnd().EndsWith("*/")) inBlockComment = false;
                    continue;
                }
                if (trimmed.StartsWith("/*"))
                {
                    if (!line.TrimEnd().EndsWith("*/") || trimmed.Length < 4) inBlockComment = true;
                    continue;
                }
                if (trimmed.Length == 0 || trimmed[0] != '#') continue;

                var wm = WarnRx.Match(line);
                if (wm.Success && forceWarnStdOut)
                {
                    lines[i] = wm.Groups["indent"].Value + "#Warn " + RewriteWarn(StripComment(wm.Groups["arg"].Value));
                    continue;
                }

                var m = IncludeRx.Match(line);
                if (!m.Success) continue;
                string kind = m.Groups["kind"].Value;
                string arg = StripComment(m.Groups["arg"].Value).Trim();
                bool optional = false;
                arg = Unquote(arg);
                if (arg.StartsWith("*i ", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("*i\t", StringComparison.OrdinalIgnoreCase))
                {
                    optional = true;
                    arg = Unquote(arg.Substring(3).Trim());
                }
                if (arg.Length == 0) continue;

                string resolved = null;
                bool isDir = false;
                if (arg.StartsWith("<") && arg.EndsWith(">"))
                {
                    if (kind.Equals("DllLoad", StringComparison.OrdinalIgnoreCase)) continue;
                    resolved = FindLocalLib(arg.Substring(1, arg.Length - 2).Trim(), origDir);
                    if (resolved == null)
                    {
                        string std = FindStdLib(arg.Substring(1, arg.Length - 2).Trim());
                        if (std != null) ps.Includes.Add(std);
                        continue; // user/standard lib: resolves identically from any folder
                    }
                }
                else
                {
                    string expanded = ExpandVars(arg, origPath);
                    if (expanded.Contains("%")) { ps.Notes.Add("unresolved variable in line " + (i + 1) + ": " + arg); continue; }
                    string full;
                    try { full = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(includeDir, expanded)); }
                    catch { continue; }
                    if (Directory.Exists(full))
                    {
                        if (kind.Equals("DllLoad", StringComparison.OrdinalIgnoreCase)) continue;
                        isDir = true;
                        includeDir = full;
                        resolved = full;
                    }
                    else if (File.Exists(full)) resolved = full;
                    else if (kind.Equals("DllLoad", StringComparison.OrdinalIgnoreCase)) continue; // system search path
                    else if (!Path.IsPathRooted(expanded) || expanded != arg) resolved = full; // keep AHK's view of the (missing) file
                    else continue;
                }

                if (!isDir && File.Exists(resolved) && !kind.Equals("DllLoad", StringComparison.OrdinalIgnoreCase)) ps.Includes.Add(resolved);
                lines[i] = m.Groups["indent"].Value + "#" + kind + " " + (optional ? "*i " : "") + "\"" + resolved + "\"";
            }

            string body = string.Join("\r\n", lines);
            if (forceWarnStdOut)
            {
                // Mirror AutoHotkey's default warning set (LocalSameAsGlobal is off by default), as text instead of dialogs.
                ps.LineOffset = 2;
                body = "#Warn VarUnset, StdOut\r\n#Warn Unreachable, StdOut\r\n" + body;
            }
            ps.Text = body;
            return ps;
        }

        /// <summary>
        /// Writes a prepared (relocated) script. If the original's folder has a Lib\ directory, a junction to it
        /// is placed beside the copy so &lt;Lib&gt; includes inside *included* files still resolve (AHK looks in
        /// A_ScriptDir\Lib). Clean up with Util.SafeDeleteTree, never a plain recursive delete.
        /// </summary>
        public static void WriteRelocated(string dest, PreparedScript prep, string origPath)
        {
            Util.WriteScript(dest, prep.Text);
            string lib = Path.Combine(Path.GetDirectoryName(origPath), "Lib");
            string link = Path.Combine(Path.GetDirectoryName(dest), "Lib");
            if (Directory.Exists(lib) && !Directory.Exists(link))
            {
                try { Native.CreateJunction(link, lib); } catch { }
            }
        }

        static string RewriteWarn(string arg)
        {
            var parts = arg.Split(new[] { ',' }, 2);
            string type = parts[0].Trim();
            string mode = parts.Length > 1 ? parts[1].Trim() : "";
            if (type.Length == 0) type = "All";
            if (!mode.Equals("Off", StringComparison.OrdinalIgnoreCase)) mode = "StdOut";
            return type + ", " + mode;
        }

        static string StripComment(string s)
        {
            for (int i = 1; i < s.Length; i++)
                if (s[i] == ';' && (s[i - 1] == ' ' || s[i - 1] == '\t')) return s.Substring(0, i).TrimEnd();
            return s.TrimEnd();
        }

        static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
                return s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        static string ExpandVars(string s, string origPath)
        {
            return Regex.Replace(s, @"%(A_\w+)%", m =>
            {
                string v = m.Groups[1].Value.ToLowerInvariant();
                switch (v)
                {
                    case "a_scriptdir": return Path.GetDirectoryName(origPath);
                    case "a_scriptfullpath": return origPath;
                    case "a_linefile": return origPath;
                    case "a_scriptname": return Path.GetFileName(origPath);
                    case "a_appdata": return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    case "a_appdatacommon": return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    case "a_mydocuments": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    case "a_desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    case "a_programfiles": return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    case "a_temp": return Path.GetTempPath().TrimEnd('\\');
                    case "a_windir": return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    case "a_username": return Environment.UserName;
                    case "a_computername": return Environment.MachineName;
                    case "a_ahkpath": return Exe64 ?? m.Value;
                    default: return m.Value;
                }
            });
        }

        static string FindLocalLib(string name, string origDir)
        {
            string dir = Path.Combine(origDir, "Lib");
            if (!Directory.Exists(dir)) return null;
            foreach (string n in LibCandidates(name))
            {
                string p = Path.Combine(dir, n);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            return null;
        }

        static string FindStdLib(string name)
        {
            var dirs = new List<string>();
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"AutoHotkey\Lib"));
            if (Exe64 != null) dirs.Add(Path.Combine(Path.GetDirectoryName(Exe64), "Lib"));
            foreach (var d in dirs)
                foreach (string n in LibCandidates(name))
                    if (File.Exists(Path.Combine(d, n))) return Path.Combine(d, n);
            return null;
        }

        static IEnumerable<string> LibCandidates(string name)
        {
            yield return name.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase) ? name : name + ".ahk";
            int us = name.IndexOf('_');
            if (us > 0) yield return name.Substring(0, us) + ".ahk";
        }
    }
}
