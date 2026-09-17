using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AstHarness
{
    public class Args
    {
        public readonly List<string> Positional = new List<string>();
        public readonly Dictionary<string, string> Named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Args(string[] argv, int start)
        {
            for (int i = start; i < argv.Length; i++)
            {
                string a = argv[i];
                if (a.StartsWith("--"))
                {
                    string key = a.Substring(2), val = "true";
                    int eq = key.IndexOf('=');
                    if (eq >= 0) { val = key.Substring(eq + 1); key = key.Substring(0, eq); }
                    else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--") && !IsFlag(key)) val = argv[++i];
                    Named[key] = val;
                }
                else Positional.Add(a);
            }
        }

        static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "verbose", "shots", "exec", "failing", "no-cache", "keep", "risky", "keep-dialogs", "workbench", "phases", "tour", "deep", "install", "corpus"
        };
        static bool IsFlag(string k) { return Flags.Contains(k); }

        public string Get(string key, string def) { string v; return Named.TryGetValue(key, out v) ? v : def; }
        public int GetInt(string key, int def) { string v; int n; return Named.TryGetValue(key, out v) && int.TryParse(v, out n) ? n : def; }
        public bool Has(string key) { return Named.ContainsKey(key) && Named[key] != "false"; }
    }

    public static class Paths
    {
        public static string Harness;   // <repo>\harness
        public static string Repo;      // <repo>
        public static string State { get { return Path.Combine(Harness, "state"); } }
        public static string Out { get { return Path.Combine(Harness, "out"); } }
        public static string Flows { get { return Path.Combine(Harness, "flows"); } }
        public static string Cases { get { return Path.Combine(Harness, "cases"); } }
        public static string CorpusCfg { get { return Path.Combine(Harness, "corpus"); } }
        public static string Temp { get { return Path.Combine(Path.GetTempPath(), "AstHarness"); } }

        public static void Init()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            // <repo>\harness\bin\AstHarness.exe
            Harness = Path.GetFullPath(Path.Combine(exeDir, ".."));
            Repo = Path.GetFullPath(Path.Combine(Harness, ".."));
            Directory.CreateDirectory(State);
            Directory.CreateDirectory(Out);
            Directory.CreateDirectory(Temp);
        }
    }

    public static class Program
    {
        public const string Usage = @"
AstHarness — corpus / workflow test harness for the AHK2 AST engine

  selftest                      prove the sandbox: hidden desktop, dialog capture, job isolation
  scan [--roots file]           discover + classify .ahk files into harness\state\corpus.json
  corpus [--tier t]             summarise the corpus
  run [options]                 baseline-validate articles, run every flow, validate outputs, report
      --tier core|converter|history|all   (default core)      --flows a,b,c  (default: all in harness\flows)
      --filter <substring>      --limit N      --failing (only items failing last run)
      --workers N               --no-cache     --exec (also run allow-listed scripts & compare behaviour)
  cases [--filter x]            run harness\cases\*.ahk regression cases (incl. runtime comparisons)
  check <file> [--flows a,b]    one article through every flow, verbose (no cache)
  validate <file>               AHK /Validate in the sandbox (warnings captured, never a dialog on screen)
  exec <file> [--ms N] [--shots]   run a script in the sandbox, print dialogs/windows/stdout
  reduce <file> --flow f        delta-debug a failing article down to a minimal repro
  shot [<file>] [--ms N] [--tour]  screenshot AstWorkbench on the hidden desktop (--tour: every main screen)
  bench [files] [--reps N]      engine speed, CPU ms best of N (default files: corpus\bench.txt)
      --flows a,b   --phases (per-phase breakdown from the engine's Prof timers)
  report [--run id]             re-print the summary of a run (default: latest)
  ranges [files] [--deep]       check source ranges + no-op edits on the corpus (--tier/--filter/--limit as for run)
  host [--install]              build AstHost.exe and test its JSON protocol (--install: copy to build\ and AxStudio)
  builtins --docs <dir>         regenerate src\ast\AhkBuiltins.cs from the AutoHotkey help files
  ast <file> | ast --code <c>   dump the parsed AST (+ round-trip emit with --emit; \n = newline)
      --flow <name>             instead print what that flow produces (inline mode)
  worker                        (internal) engine worker process
";

        [STAThread]
        public static int Main(string[] argv)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (argv.Length == 0) { Console.WriteLine(Usage); return 0; }
            string cmd = argv[0].ToLowerInvariant();
            var a = new Args(argv, 1);

            if (cmd == "worker") return Worker.RunLoop();

            Paths.Init();
            int code = 2;
            try
            {
                code = Dispatch(cmd, a);
            }
            finally
            {
                WorkerPool.ShutdownAll();
                // Guarantee the process ends even if some thread is stuck (sandbox watcher, a pipe, a timer).
                try { Console.Out.Flush(); Console.Error.Flush(); } catch { }
                Environment.Exit(code);
            }
            return code;
        }

        static int Dispatch(string cmd, Args a)
        {
            {
                Ahk.Locate(a.Get("ahk", null));
                switch (cmd)
                {
                    case "selftest": return SelfTest.Run(Paths.Temp, a.Has("verbose"));
                    case "validate": return Commands.Validate(a);
                    case "exec": return Commands.Exec(a);
                    case "scan": return Corpus.Scan(a);
                    case "corpus": return Corpus.Summary(a);
                    case "run": return Runner.Run(a);
                    case "cases": return Runner.Cases(a);
                    case "check": return Runner.Check(a);
                    case "reduce": return Reducer.Run(a);
                    case "shot": return Commands.Shot(a);
                    case "ast": return Commands.Ast(a);
                    case "bench": return Commands.Bench(a);
                    case "report": return Report.Print(a);
                    case "ranges": return RangeCheck.Run(a);
                    case "host": return HostTest.Run(a);
                    case "builtins": return Builtins.Run(a);
                    default: Console.WriteLine(Usage); return 2;
                }
            }
        }
    }

    public static class Commands
    {
        public static int Validate(Args a)
        {
            if (a.Positional.Count == 0) { Console.WriteLine("usage: validate <file>"); return 2; }
            string path = Path.GetFullPath(a.Positional[0]);
            string text = Util.ReadScript(path);
            string why;
            string exe = Ahk.PickExe(text, out why) ?? Ahk.Exe64;
            if (why != null) Console.WriteLine("note: " + why + " — validating with " + exe);
            var prep = Ahk.Prepare(text, path, true);
            string copy = Path.Combine(Paths.Temp, "validate", Guid.NewGuid().ToString("N"), Path.GetFileName(path));
            Ahk.WriteRelocated(copy, prep, path);
            var o = Ahk.Validate(exe, copy, prep.LineOffset, a.GetInt("ms", 20000));
            Util.SafeDeleteTree(Path.GetDirectoryName(copy));
            PrintOutcome(o, text);
            return o.Ok ? 0 : 1;
        }

        public static int Exec(Args a)
        {
            if (a.Positional.Count == 0) { Console.WriteLine("usage: exec <file> [--ms N] [--shots]"); return 2; }
            string path = Path.GetFullPath(a.Positional[0]);
            string text = Util.ReadScript(path);
            string why;
            string exe = Ahk.PickExe(text, out why) ?? Ahk.Exe64;
            var prep = Ahk.Prepare(text, path, false);
            string dir = Path.Combine(Paths.Temp, "exec", Guid.NewGuid().ToString("N"));
            string copy = Path.Combine(dir, Path.GetFileName(path));
            Ahk.WriteRelocated(copy, prep, path);
            string shots = a.Has("shots") ? Path.Combine(Paths.Out, "shots", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + Util.SafeName(Path.GetFileNameWithoutExtension(path), 40)) : null;
            var o = Ahk.Execute(exe, copy, prep.LineOffset, a.GetInt("ms", 5000), shots);
            Util.SafeDeleteTree(dir);
            PrintOutcome(o, text);
            if (shots != null) Console.WriteLine("screenshots: " + shots);
            return o.Ok ? 0 : 1;
        }

        public static void PrintOutcome(AhkOutcome o, string source)
        {
            Console.WriteLine("status: " + o.Status + "   exit=" + o.ExitCode + "   " + o.ElapsedMs + "ms" + (o.Alive ? "   (still running at deadline)" : ""));
            foreach (var e in o.Errors)
            {
                Console.WriteLine("ERROR   line " + e.Line + ": " + e.Message + (string.IsNullOrEmpty(e.Specifically) ? "" : "\n        specifically: " + e.Specifically));
                if (source != null && e.Line > 0) Console.Write(Util.Context(source, e.Line, 2));
            }
            foreach (var w in o.Warnings)
                Console.WriteLine("WARN    line " + w.Line + ": " + w.Message + (string.IsNullOrEmpty(w.Specifically) ? "" : "  [" + w.Specifically + "]"));
            foreach (var w in o.Windows)
            {
                Console.WriteLine("WINDOW  " + w.Kind + " '" + w.Title + "' (" + w.Class + ", " + w.Width + "x" + w.Height + ") action=" + w.Action + (w.Screenshot != null ? " shot=" + w.Screenshot : ""));
                if (w.Text.Length > 0) Console.WriteLine("        text: " + Util.Clip(w.Text, w.Kind == "Window" ? 6000 : 400));
                if (w.Kind == "Window") foreach (var c in w.Controls.Take(40)) Console.WriteLine("        ctl: " + Util.Clip(c, 120));
            }
            if (o.Stdout.Trim().Length > 0) Console.WriteLine("stdout:\n" + Util.Clip(o.Stdout, 2000));
        }

        /// <summary>
        /// In-process engine timing: for each file, the best of N runs of parse (+includes), a 1:1 emit, and each
        /// flow. `bench [files...] [--flows a,b] [--reps 3]`; with no files, the list in harness\corpus\bench.txt.
        /// </summary>
        public static int Bench(Args a)
        {
            var files = a.Positional.Select(Path.GetFullPath).ToList();
            string listFile = Path.Combine(Paths.CorpusCfg, "bench.txt");
            if (files.Count == 0 && File.Exists(listFile))
                files = File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#") && File.Exists(l)).ToList();
            if (files.Count == 0) { Console.WriteLine("usage: bench <file>... [--flows a,b] [--reps N]   (or list files in corpus\\bench.txt)"); return 2; }
            int reps = a.GetInt("reps", 3);
            var flows = FlowDef.Load(Paths.Flows, a.Get("flows", "roundtrip,minify-aggressive,treeshake-aggressive,node-diagram"));
            string tmp = Path.Combine(Paths.Temp, "bench");
            Directory.CreateDirectory(tmp);
            if (a.Has("phases"))
            {
                // one warm-up pass (JIT), then a measured pass with the engine's phase timers on
                foreach (string f in files) foreach (var fl in flows) new AhkAstEngine().ExecuteFlow(Util.ReadScript(f), fl.EngineJson(tmp, -1), false, f, true);
                Prof.Reset();
                Prof.Enabled = true;
                var whole = System.Diagnostics.Stopwatch.StartNew();
                foreach (string f in files) foreach (var fl in flows) new AhkAstEngine().ExecuteFlow(Util.ReadScript(f), fl.EngineJson(tmp, -1), false, f, true);
                Prof.Enabled = false;
                Console.WriteLine(string.Format("wall {0} ms ({1} files x {2} flows)", whole.ElapsedMilliseconds, files.Count, flows.Count));
                foreach (var r in Prof.Report().Take(a.GetInt("top", 40)))
                    Console.WriteLine(string.Format("{0,9:0.0} ms {1,8}x  {2}", r.Item2, r.Item3, r.Item1));
                return 0;
            }
            Func<Action, long> best = act =>
            {
                // CPU time of this process, not wall clock: other programs' load on the machine doesn't count
                long b = long.MaxValue;
                var me = System.Diagnostics.Process.GetCurrentProcess();
                for (int r = 0; r < reps; r++)
                {
                    me.Refresh();
                    var t0 = me.TotalProcessorTime;
                    act();
                    me.Refresh();
                    b = Math.Min(b, (long)(me.TotalProcessorTime - t0).TotalMilliseconds);
                }
                return b;
            };
            GC.Collect();
            var totals = new Dictionary<string, long>();
            Console.WriteLine(string.Format("{0,-34} {1,8} {2,9} {3,7} {4,7}  {5}", "file", "in KB", "out KB", "parse", "emit", string.Join("  ", flows.Select(f => f.Name))));
            foreach (string f in files)
            {
                string src = Util.ReadScript(f);
                AstNode tree = null;
                long parse = best(() => { tree = new AhkAstEngine().ParseFileWithIncludes(f, false); });
                string outText = null;
                long emit = best(() => { outText = AstEmitter.Emit(tree); });
                var cols = new List<string>();
                foreach (var fl in flows)
                {
                    string json = fl.EngineJson(tmp, -1);
                    long ms = best(() => { new AhkAstEngine().ExecuteFlow(src, json, false, f, true); });
                    cols.Add(ms.ToString().PadLeft(fl.Name.Length));
                    long t; totals.TryGetValue(fl.Name, out t); totals[fl.Name] = t + ms;
                }
                long tp; totals.TryGetValue("parse", out tp); totals["parse"] = tp + parse;
                long te; totals.TryGetValue("emit", out te); totals["emit"] = te + emit;
                Console.WriteLine(string.Format("{0,-34} {1,8} {2,9} {3,7} {4,7}  {5}", Util.Clip(Path.GetFileName(f), 34), src.Length / 1024, (outText ?? "").Length / 1024, parse, emit, string.Join("  ", cols)));
            }
            Console.WriteLine(string.Format("{0,-34} {1,8} {2,9} {3,7} {4,7}  {5}", "TOTAL (ms, best of " + reps + ")", "", "", totals["parse"], totals["emit"],
                string.Join("  ", flows.Select(fl => totals[fl.Name].ToString().PadLeft(fl.Name.Length)))));
            return 0;
        }

        public static int Ast(Args a)
        {
            string code = a.Get("code", null);
            if (code == null && a.Positional.Count > 0) code = Util.ReadScript(Path.GetFullPath(a.Positional[0]));
            if (code == null) { Console.WriteLine("usage: ast <file> | ast --code \"code\" [--emit]"); return 2; }
            if (a.Get("code", null) != null) code = code.Replace("\\n", "\n"); // only for --code (a file may contain `\n`)
            string flowName = a.Get("flow", null);
            if (flowName != null)
            {
                // Print what one flow makes of the code (inline mode), e.g. `ast x.ahk --flow treeshake-safe`.
                var flow = FlowDef.Load(Paths.Flows, flowName).FirstOrDefault();
                if (flow == null) { Console.WriteLine("unknown flow: " + flowName); return 2; }
                string path = a.Positional.Count > 0 ? Path.GetFullPath(a.Positional[0]) : Path.Combine(Paths.Temp, "snippet.ahk");
                string dir = Path.Combine(Paths.Temp, "ast-flow", Guid.NewGuid().ToString("N"));
                Console.WriteLine(new AhkAstEngine().ExecuteFlow(code, flow.EngineJson(dir, flow.StepCount), false, path, true));
                Util.SafeDeleteTree(dir);
                return 0;
            }
            var engine = new AhkAstEngine();
            var root = engine.Parse(code);
            Console.Write(DumpTree(root, 0));
            if (a.Has("emit")) Console.WriteLine("---- emit ----\n" + engine.Emit(root));
            return 0;
        }

        static string DumpTree(AstNode n, int depth)
        {
            var sb = new StringBuilder();
            sb.Append(new string(' ', depth * 2)).Append(n.NodeType);
            if (!string.IsNullOrEmpty(n.Value)) sb.Append(" = ").Append(Util.Clip(n.Value, 100));
            if (!string.IsNullOrEmpty(n.Metadata)) sb.Append("  {").Append(Util.Clip(n.Metadata, 60)).Append("}");
            sb.Append("  @").Append(n.Line).Append(':').Append(n.Column);
            if (n.HasRange) sb.Append("  [").Append(n.RangeStartLine).Append(':').Append(n.RangeStartColumn).Append('-').Append(n.RangeEndLine).Append(':').Append(n.RangeEndColumn).Append(']');
            sb.Append('\n');
            foreach (var c in n.ChildNodes) if (c != null) sb.Append(DumpTree(c, depth + 1));
            return sb.ToString();
        }

        public static int Shot(Args a)
        {
            return WorkbenchShot.Run(a);
        }
    }
}
