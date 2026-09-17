using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AstHarness
{
    /// <summary>
    /// Delta debugging (ddmin over lines): shrinks a failing article to a small script that
    ///  (a) AutoHotkey still accepts, and (b) still fails the flow with the same issue signature.
    /// Candidates are evaluated as if they lived at the original path, so includes keep resolving.
    /// </summary>
    public static class Reducer
    {
        public static int Run(Args a)
        {
            if (a.Positional.Count == 0 || !a.Named.ContainsKey("flow"))
            {
                Console.WriteLine("usage: reduce <file> --flow <name|parse> [--mode inline|local] [--signature <substring>] [--match <detail text>] [--exec] [--budget seconds]");
                return 2;
            }
            string path = Path.GetFullPath(a.Positional[0]);
            string flowName = a.Get("flow", "");
            FlowDef flow = null;
            if (!flowName.Equals("parse", StringComparison.OrdinalIgnoreCase))
            {
                flow = FlowDef.Load(Paths.Flows, flowName).FirstOrDefault();
                if (flow == null) { Console.WriteLine("no flow matching '" + flowName + "' in " + Paths.Flows); return 2; }
            }
            string mode = a.Get("mode", flow != null ? flow.Modes[0] : "");
            int budget = a.GetInt("budget", 180);
            int parallel = a.GetInt("workers", Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2)));
            string tmp = Path.Combine(Paths.Temp, "reduce-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            bool exec = a.Has("exec");
            if (exec && !Runner.MayExecute(path))
            {
                Console.WriteLine("--exec runs the script: only allowed for harness\\cases\\* or files listed in corpus\\runnable.txt");
                return 2;
            }
            string match = a.Get("match", null);
            string text = Util.ReadScript(path);
            var issues = Runner.Evaluate(text, path, flow, mode, tmp, exec);
            if (issues == null) { Console.WriteLine("AutoHotkey rejects the original — nothing to reduce."); return 1; }
            string want = a.Get("signature", null);
            Func<Issue, bool> matches = i => match == null || (i.Detail ?? "").IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
            var target = issues.FirstOrDefault(i => i.Severity == "hard" && matches(i) && (want == null || i.Signature.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0))
                      ?? issues.FirstOrDefault(i => want != null && matches(i) && i.Signature.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                      ?? issues.FirstOrDefault(i => match != null && matches(i)); // --match alone may target a soft issue
            if (target == null)
            {
                Console.WriteLine("No " + (want == null ? "hard failure" : "issue matching '" + want + "'") + " for " + (flow == null ? "parse" : flow.Name) + "/" + mode + ".");
                foreach (var i in issues) Console.WriteLine("  (" + i.Severity + ") " + i.Signature);
                return 1;
            }
            string sig = target.Signature;
            Console.WriteLine("reducing " + Path.GetFileName(path) + " (" + Util.CountLines(text) + " lines) for: " + sig + (match != null ? "  [detail contains: " + match + "]" : ""));

            var sw = Stopwatch.StartNew();
            int tests = 0;
            Func<List<string>, bool> interesting = lines =>
            {
                System.Threading.Interlocked.Increment(ref tests);
                var r = Runner.Evaluate(string.Join("\r\n", lines), path, flow, mode, tmp, exec);
                return r != null && r.Any(i => i.Signature == sig && matches(i));
            };

            var units = Util.NormalizeNewlines(text).Split('\n').ToList();

            // Cheap first pass: drop blank lines and full-line comments in one go.
            var noTrivia = units.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith(";")).ToList();
            if (noTrivia.Count < units.Count && interesting(noTrivia)) units = noTrivia;

            int n = 2;
            while (units.Count >= 2 && sw.Elapsed.TotalSeconds < budget)
            {
                int chunk = (int)Math.Ceiling(units.Count / (double)n);
                var complements = new List<List<string>>();
                for (int start = 0; start < units.Count; start += chunk)
                {
                    var c = new List<string>(units);
                    c.RemoveRange(start, Math.Min(chunk, units.Count - start));
                    complements.Add(c);
                }
                // Test complements in parallel; keep the first (in order) that still fails the same way.
                var ok = new bool[complements.Count];
                Parallel.For(0, complements.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
                {
                    if (sw.Elapsed.TotalSeconds < budget) ok[i] = interesting(complements[i]);
                });
                int hit = Array.IndexOf(ok, true);
                if (hit >= 0)
                {
                    units = complements[hit];
                    n = Math.Max(n - 1, 2);
                    Console.WriteLine(string.Format("  {0,5:F0}s  {1} lines  ({2} tests)", sw.Elapsed.TotalSeconds, units.Count, tests));
                }
                else
                {
                    if (n >= units.Count) break;
                    n = Math.Min(units.Count, n * 2);
                }
            }

            // Try to simplify remaining lines by trimming trailing comments.
            for (int i = 0; i < units.Count && sw.Elapsed.TotalSeconds < budget; i++)
            {
                int sc = units[i].IndexOf(" ;", StringComparison.Ordinal);
                if (sc <= 0) continue;
                var c = new List<string>(units);
                c[i] = units[i].Substring(0, sc).TrimEnd();
                if (interesting(c)) units = c;
            }

            string reduced = string.Join("\r\n", units);
            string outDir = Path.Combine(Paths.Out, "reduced");
            Directory.CreateDirectory(outDir);
            string outFile = Path.Combine(outDir, Util.SafeName(Path.GetFileNameWithoutExtension(path) + "_" + (flow == null ? "parse" : flow.Name), 60) + ".ahk");
            var sb = new StringBuilder();
            sb.AppendLine("; @flows " + (flow == null ? "roundtrip" : flow.Name));
            sb.AppendLine("; reduced from: " + path);
            sb.AppendLine("; signature:    " + sig);
            sb.Append(reduced);
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(true));
            Util.SafeDeleteTree(tmp);

            Console.WriteLine();
            Console.WriteLine("reduced to " + units.Count + " lines in " + sw.Elapsed.TotalSeconds.ToString("F0") + "s (" + tests + " tests) → " + outFile);
            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine(reduced);
            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine("Note: the repro may still #Include files next to the original. To keep it as a regression case,");
            Console.WriteLine("make it self-contained and copy it into harness\\cases\\.");
            return 0;
        }
    }

    public static class WorkbenchShot
    {
        public static int Run(Args a)
        {
            string wb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wb", "AstWorkbench.exe");
            if (!File.Exists(wb)) { Console.WriteLine("workbench not built: run harness.ps1 build --workbench"); return 2; }
            string file = a.Positional.Count > 0 ? Path.GetFullPath(a.Positional[0]) : null;
            string shots = a.Get("out", Path.Combine(Paths.Out, "shots", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_workbench"));
            // --tour: the workbench walks through its main screens itself and saves a screenshot of each (then exits)
            bool tour = a.Has("tour");
            var opt = new LaunchOptions
            {
                Exe = wb,
                Args = (tour ? "--tour \"" + shots + "\" " : "") + (file != null ? "\"" + file + "\"" : ""),
                WorkDir = Path.GetDirectoryName(wb),
                TimeoutMs = a.GetInt("ms", tour ? 90000 : 8000),
                KillOnError = false,
                DismissDialogs = !a.Has("keep-dialogs"),
                ShotDir = shots,
                FinalShot = true,
                ScriptName = "AstWorkbench",
            };
            Console.WriteLine("launching workbench on the hidden desktop" + (file != null ? " with " + file : "") + " for " + opt.TimeoutMs + "ms...");
            var r = Sandbox.Instance.Run(opt);
            if (r.LaunchError != null) { Console.WriteLine("launch failed: " + r.LaunchError); return 1; }
            foreach (var w in r.Windows)
                Console.WriteLine("  " + w.Kind + " '" + w.Title + "' " + w.Width + "x" + w.Height + " action=" + w.Action + (w.Screenshot != null ? "\n     → " + w.Screenshot : "") +
                    (w.Kind == "MsgBox" ? "\n     text: " + Util.Clip(w.Text, 400) : ""));
            string crash = Path.Combine(Path.GetDirectoryName(wb), "crash.log");
            if (File.Exists(crash) && File.GetLastWriteTime(crash) > DateTime.Now.AddMinutes(-2))
                Console.WriteLine("crash.log:\n" + Util.Clip(File.ReadAllText(crash), 3000));
            if (r.LeakedWindows > 0) Console.WriteLine("WARNING: " + r.LeakedWindows + " window(s) appeared on the user's desktop");
            if (tour && Directory.Exists(shots))
                foreach (var png in Directory.GetFiles(shots, "*.png").OrderBy(x => x)) Console.WriteLine("  tour: " + png);
            bool any = r.Windows.Any(w => w.Screenshot != null) || (tour && Directory.Exists(shots) && Directory.GetFiles(shots, "*.png").Length > 0);
            Console.WriteLine(any ? "screenshots in " + shots : "no screenshot captured");
            return any ? 0 : 1;
        }
    }
}
