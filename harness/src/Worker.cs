using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AstHarness
{
    public class WorkerJob
    {
        public string Id;
        public string Op;          // parse | flow
        public string Path;        // the article (its folder is the include root)
        public string Source;      // optional override of the file's text (reducer)
        public string FlowJson;
        public string Mode;        // inline | local
        public bool Idempotent;
        public bool AstEquiv;
        public string OutPath;     // where to write the flow output
    }

    public class ParseIssue
    {
        public string Type;
        public int Line;
        public int Column;
        public string Message;
    }

    public class WorkerReply
    {
        public string Id;
        public bool Ok;
        public string Crash;        // worker died / hung (set by the pool, not the worker)
        public string Exception;    // engine threw out of the API
        public List<string> LogErrors = new List<string>();
        public List<ParseIssue> ParseIssues = new List<ParseIssue>();
        public int NodeCount;
        public string IdemDiff;
        public string AstDiff;
        public string TokenDiff;
        public bool OutputIsEmpty;
        public int InBytes, OutBytes, InLines, OutLines;
        public long Ms;
    }

    /// <summary>Engine side: runs inside "AstHarness.exe worker", one JSON job per stdin line.</summary>
    public static class Worker
    {
        public static int RunLoop()
        {
            var protocol = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            protocol.AutoFlush = true;
            Console.SetOut(Console.Error); // engine chatter must never corrupt the protocol stream
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            Environment.SetEnvironmentVariable("AHK2AST_HEADLESS", "true");

            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                WorkerReply reply;
                try
                {
                    var job = Util.FromJson<WorkerJob>(line);
                    reply = Execute(job);
                }
                catch (Exception ex)
                {
                    reply = new WorkerReply { Ok = false, Exception = "protocol: " + ex.Message };
                }
                protocol.WriteLine(Util.ToJson(reply));
            }
            return 0;
        }

        public static WorkerReply Execute(WorkerJob job)
        {
            var r = new WorkerReply { Id = job.Id };
            var sw = Stopwatch.StartNew();
            try
            {
                string source = job.Source ?? Util.ReadScript(job.Path);
                r.InBytes = Encoding.UTF8.GetByteCount(source);
                r.InLines = Util.CountLines(source);
                var engine = new AhkAstEngine();

                if (job.Op == "parse")
                {
                    var ast = engine.Parse(source);
                    Collect(ast, r);
                    r.Ok = true;
                }
                else if (job.Op == "flow")
                {
                    bool inline = job.Mode != "local";
                    string output = engine.ExecuteFlow(source, job.FlowJson, !inline, job.Path, inline);
                    foreach (string l in PipelineLogger.Logs)
                        if (l.Contains("❌") || l.IndexOf("ERROR", StringComparison.Ordinal) >= 0) r.LogErrors.Add(Util.Clip(l, 2000));
                    output = output ?? "";
                    r.OutputIsEmpty = output.Trim().Length == 0 && HasCode(source); // comment-only files may legitimately empty
                    r.OutBytes = Encoding.UTF8.GetByteCount(output);
                    r.OutLines = Util.CountLines(output);
                    if (job.OutPath != null) Util.WriteScript(job.OutPath, output);

                    if (job.Idempotent && r.LogErrors.Count == 0)
                    {
                        var engine2 = new AhkAstEngine();
                        // same mode as the first pass: an inlined output's `; --- begin: f ---` regions are rebuilt into #Include f in local mode (by design)
                        string again = engine2.ExecuteFlow(output, job.FlowJson, !inline, job.Path, inline) ?? "";
                        r.IdemDiff = Util.FirstDiff(output, again);
                    }
                    if (job.AstEquiv && !inline && r.LogErrors.Count == 0)
                    {
                        var a1 = new AhkAstEngine().Parse(source);
                        var a2 = new AhkAstEngine().Parse(output);
                        r.AstDiff = AstDiff(a1, a2);
                        r.TokenDiff = TokenDiff(source, output);
                    }
                    r.Ok = true;
                }
                else throw new ArgumentException("unknown op " + job.Op);
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Exception = ex.GetType().Name + ": " + ex.Message + "\n" + Util.Clip(ex.StackTrace ?? "", 3000);
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>True if the script has any line that is not blank, a ; comment, or inside a /* */ block.</summary>
        static bool HasCode(string source)
        {
            bool inBlock = false;
            foreach (string raw in Util.NormalizeNewlines(source).Split('\n'))
            {
                string t = raw.Trim();
                if (inBlock) { if (t.StartsWith("*/") || t.EndsWith("*/")) inBlock = false; continue; }
                if (t.StartsWith("/*")) { if (!(t.Length >= 4 && t.EndsWith("*/"))) inBlock = true; continue; }
                if (t.Length == 0 || t[0] == ';') continue;
                // An optional include of a missing file (`#include *i reload-v1.ahk`) adds nothing when inlined.
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^#include(again)?[\s,]+\*i\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                return true;
            }
            return false;
        }

        static void Collect(AstNode node, WorkerReply r)
        {
            r.NodeCount++;
            if (node.NodeType == "Error" || node.NodeType == "Warning" || node.NodeType == "Unknown")
                r.ParseIssues.Add(new ParseIssue { Type = node.NodeType, Line = node.Line, Column = node.Column, Message = Util.Clip(node.Value, 300) });
            foreach (var c in node.ChildNodes) if (c != null) Collect(c, r);
        }

        /// <summary>
        /// Token fidelity: a 1:1 emit must lex to the same significant tokens as the original. Comments, line breaks
        /// and letter case (names and keywords are case-insensitive) don't count; string contents do. Anything the
        /// parser dropped, reordered or misread shows up here even when both versions load fine.
        /// </summary>
        static string TokenDiff(string original, string emitted)
        {
            var a = SignificantTokens(original);
            var b = SignificantTokens(emitted);
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                if (TokenKey(a[i]) == TokenKey(b[i])) continue;
                return "orig line " + a[i].Line + ": `" + Util.Clip(Around(a, i), 160) + "`\n   emitted line " + b[i].Line + ": `" + Util.Clip(Around(b, i), 160) + "`";
            }
            if (a.Count != b.Count)
                return "token count " + a.Count + " vs " + b.Count + (n < a.Count ? " (first missing at orig line " + a[n].Line + ": `" + Util.Clip(Around(a, n), 160) + "`)" : " (extra at emitted line " + b[n].Line + ": `" + Util.Clip(Around(b, n), 160) + "`)");
            return null;
        }

        static List<Token> SignificantTokens(string s)
        {
            List<Token> toks;
            try { toks = new AhkLexer(s).Tokenize(); }
            catch { return new List<Token>(); }
            var sig = toks.Where(t => t.Type != TokenType.Newline && t.Type != TokenType.Comment && t.Type != TokenType.EOF).ToList();
            // A trailing comma before `]` / `)` / `}` / a case `:` adds nothing in AHK (`[a, b,]` has 2 items, `f(1,)` passes 1 arg): layout.
            for (int i = sig.Count - 2; i >= 0; i--)
                if (sig[i].Type == TokenType.Comma && (sig[i + 1].Type == TokenType.RBracket || sig[i + 1].Type == TokenType.RParen || sig[i + 1].Type == TokenType.RBrace || sig[i + 1].Type == TokenType.Colon))
                    sig.RemoveAt(i);
            return sig;
        }

        static string TokenKey(Token t)
        {
            string v = t.Value ?? "";
            if (t.Type == TokenType.String) return "S:" + v;
            if (t.Type == TokenType.Directive || t.Type == TokenType.Hotkey || t.Type == TokenType.Hotstring)
            {
                // whole-line tokens: a trailing comment and runs of blanks are layout
                var m = System.Text.RegularExpressions.Regex.Match(v, @"^(.*?)(?:[ \t]+;.*)?$");
                v = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value.Trim(), @"[ \t]+", " ").Replace(":: ", "::");
            }
            return t.Type + ":" + v.ToLowerInvariant();
        }

        static string Around(List<Token> toks, int i)
        {
            var parts = new List<string>();
            for (int k = Math.Max(0, i - 3); k <= Math.Min(toks.Count - 1, i + 2); k++)
                parts.Add((k == i ? "»" : "") + (toks[k].Value ?? toks[k].Type.ToString()) + (k == i ? "«" : ""));
            return string.Join(" ", parts);
        }
        // Structural comparison, ignoring positions and trivia. Each node is keyed by its ancestor path.
        static string AstDiff(AstNode a, AstNode b)
        {
            var la = new List<KeyValuePair<string, int>>();
            var lb = new List<KeyValuePair<string, int>>();
            Flatten(a, "", la);
            Flatten(b, "", lb);
            int n = Math.Min(la.Count, lb.Count);
            for (int i = 0; i < n; i++)
            {
                if (la[i].Key != lb[i].Key)
                    return "orig line " + la[i].Value + ": `" + Util.Clip(la[i].Key, 200) + "`\n   emitted: `" + Util.Clip(lb[i].Key, 200) + "`";
            }
            if (la.Count != lb.Count)
                return "node count " + la.Count + " vs " + lb.Count + (n < la.Count ? " (first missing: " + Util.Clip(la[n].Key, 200) + " @ line " + la[n].Value + ")" : "");
            return null;
        }

        static void Flatten(AstNode node, string path, List<KeyValuePair<string, int>> list)
        {
            if (node == null || node.NodeType == "Comment") return;
            string v = (node.Value ?? "").Trim();
            string type = node.NodeType;
            if (type == "String") v = NormalizeStringLiteral(v);
            // An Include rebuilt from `; --- begin: f ---` markers is the same thing as the `#Include f` line it emits.
            if (type == "Include" && node.ChildCount == 0) { type = "Directive"; v = "#Include " + v; }
            // a directive's own text is compared modulo layout: runs of blanks, a trailing comment (its #HotIf
            // condition, if any, is a child and compared as code)
            if (type == "Directive")
                v = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(v, @"[ \t]+;.*$", ""), @"[ \t]+", " ").Trim();
            if (type == "Directive" && v.StartsWith("#Include", StringComparison.OrdinalIgnoreCase))
            {
                string arg = v.Substring(8).Trim().Trim('"', '\'').Replace('/', '\\'); // may be `<Lib>`: no Path API here
                v = "#Include " + arg.Substring(arg.LastIndexOf('\\') + 1);
            }
            string self = type + (v.Length > 0 ? "=" + v : "");
            list.Add(new KeyValuePair<string, int>(path + self, node.Line));
            string childPath = path + node.NodeType + " > ";
            foreach (var c in node.ChildNodes) Flatten(c, childPath, list);
        }
        static string NormalizeStringLiteral(string v)
        {
            // 'x' and "x" are the same literal.
            if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[v.Length - 1] == v[0]) return v.Substring(1, v.Length - 2);
            return v;
        }
    }

    /// <summary>Coordinator side: a pool of crash-isolated worker processes with per-job timeouts.</summary>
    public class WorkerClient : IDisposable
    {
        readonly Process _p;
        readonly IntPtr _job;
        readonly StreamWriter _in;
        readonly StreamReader _out;
        readonly StringBuilder _err = new StringBuilder();
        public bool Dead;

        public WorkerClient()
        {
            var psi = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AstHarness.exe"), "worker")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            psi.EnvironmentVariables["AHK2AST_HEADLESS"] = "true";
            lock (Sandbox.LaunchLock) _p = Process.Start(psi);
            _job = WorkerJobObject.Create();
            if (_job != IntPtr.Zero) WorkerJobObject.Assign(_job, _p.Handle);
            _in = new StreamWriter(_p.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
            _out = _p.StandardOutput;
            _p.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (_err)
                {
                    _err.AppendLine(e.Data);
                    if (_err.Length > 8000) _err.Remove(0, _err.Length - 6000);
                }
            };
            _p.BeginErrorReadLine();
        }

        public WorkerReply Call(WorkerJob job, int timeoutMs)
        {
            lock (_err) _err.Clear();
            try
            {
                _in.WriteLine(Util.ToJson(job));
                var t = _out.ReadLineAsync();
                if (!t.Wait(timeoutMs))
                {
                    Kill();
                    return new WorkerReply { Id = job.Id, Ok = false, Crash = "timeout: engine did not finish within " + timeoutMs + "ms (hang / infinite loop)" };
                }
                if (t.Result == null)
                {
                    _p.WaitForExit(2000);
                    Dead = true;
                    string tail;
                    lock (_err) tail = _err.ToString().Trim();
                    return new WorkerReply { Id = job.Id, Ok = false, Crash = "worker died (exit " + SafeExit() + "): " + Util.Clip(tail, 1500) };
                }
                return Util.FromJson<WorkerReply>(t.Result);
            }
            catch (Exception ex)
            {
                Kill();
                return new WorkerReply { Id = job.Id, Ok = false, Crash = "worker failure: " + ex.Message };
            }
        }

        string SafeExit()
        {
            try
            {
                if (!_p.HasExited) return "?";
                int c = _p.ExitCode;
                if (c == unchecked((int)0xC00000FD)) return "0xC00000FD stack overflow";
                return c.ToString() + " / 0x" + c.ToString("X8");
            }
            catch { return "?"; }
        }

        void Kill()
        {
            Dead = true;
            try { if (!_p.HasExited) _p.Kill(); } catch { }
        }

        /// <summary>Never blocks: closing the kill-on-close job ends the worker (and anything it started) at once.</summary>
        public void Dispose()
        {
            Dead = true;
            if (_job != IntPtr.Zero) Native.CloseHandle(_job);
            try { if (!_p.HasExited) _p.Kill(); } catch { }
        }
    }

    public static class WorkerPool
    {
        static readonly ConcurrentBag<WorkerClient> Idle = new ConcurrentBag<WorkerClient>();
        static readonly List<WorkerClient> All = new List<WorkerClient>();
        public static int TimeoutMs = 60000;

        public static WorkerReply Call(WorkerJob job)
        {
            WorkerClient c;
            if (!Idle.TryTake(out c) || c.Dead)
            {
                c = new WorkerClient();
                lock (All) All.Add(c);
            }
            var r = c.Call(job, TimeoutMs);
            if (c.Dead) c.Dispose(); else Idle.Add(c);
            return r;
        }

        public static void ShutdownAll()
        {
            List<WorkerClient> all;
            lock (All) { all = new List<WorkerClient>(All); All.Clear(); }
            foreach (var c in all) c.Dispose();
        }
    }

    static class WorkerJobObject
    {
        public static IntPtr Create()
        {
            IntPtr job = Native.CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var ext = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            ext.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | Native.JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;
            int size = Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ext, p, false);
                Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, p, (uint)size);
            }
            finally { Marshal.FreeHGlobal(p); }
            return job;
        }

        public static void Assign(IntPtr job, IntPtr process)
        {
            Native.AssignProcessToJobObject(job, process);
        }
    }
}
