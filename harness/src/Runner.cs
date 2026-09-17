using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AstHarness
{
    public class FlowDef
    {
        public string Name;
        public string Title;
        public string File;
        public Dictionary<string, object> Raw;
        public List<string> Modes = new List<string> { "inline" };
        public bool Idempotent, AstEquiv, Risky;
        /// <summary>Which scripts the flow promises to handle: `all` (every failure is a bug), `entry` (not applicable
        /// to library files, e.g. aggressive tree-shaking of a file with no entry point), `best-effort` (an option that
        /// is knowingly not compatible with every script: failures are reported, not scored as hard).</summary>
        public string Compatibility = "all";
        /// <summary>The flow renames identifiers: warnings are compared by kind, not by the (renamed) name.</summary>
        public bool Renames;
        public bool Validate = true;
        public int StepCount;
        public List<string> StepTitles = new List<string>();
        public string Hash;

        /// <summary>Engine flow JSON: no diagnostics banner, output dirs pointed at the item's scratch folder, optional step prefix.</summary>
        public string EngineJson(string outDir, int stepLimit)
        {
            var js = Util.NewJson();
            var copy = js.Deserialize<Dictionary<string, object>>(js.Serialize(Raw));
            copy.Remove("Harness");
            var meta = copy.ContainsKey("Meta") ? copy["Meta"] as Dictionary<string, object> : null;
            if (meta == null) { meta = new Dictionary<string, object>(); copy["Meta"] = meta; }
            meta["EmitDiagnosticsComments"] = false;
            string custom = meta.ContainsKey("CustomProperties") ? Convert.ToString(meta["CustomProperties"]) : "";
            meta["CustomProperties"] = (custom ?? "") + "\nWorkspaceDir=" + outDir + "\nOutputDir=" + outDir;
            if (stepLimit >= 0 && copy.ContainsKey("Steps"))
            {
                var steps = copy["Steps"] as System.Collections.ArrayList;
                if (steps != null && steps.Count > stepLimit) steps.RemoveRange(stepLimit, steps.Count - stepLimit);
            }
            return js.Serialize(copy);
        }

        public static List<FlowDef> Load(string dir, string only)
        {
            var list = new List<FlowDef>();
            var js = Util.NewJson();
            foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                var raw = js.Deserialize<Dictionary<string, object>>(System.IO.File.ReadAllText(f));
                var fd = new FlowDef { File = f, Raw = raw, Name = Regex.Replace(Path.GetFileNameWithoutExtension(f), @"^\d+-", "") };
                fd.Hash = Util.Short(Util.Sha1(System.IO.File.ReadAllText(f)));
                var h = raw.ContainsKey("Harness") ? raw["Harness"] as Dictionary<string, object> : null;
                if (h != null)
                {
                    if (h.ContainsKey("Modes")) fd.Modes = ((System.Collections.ArrayList)h["Modes"]).Cast<object>().Select(Convert.ToString).ToList();
                    fd.Idempotent = h.ContainsKey("Idempotent") && (bool)h["Idempotent"];
                    fd.AstEquiv = h.ContainsKey("AstEquiv") && (bool)h["AstEquiv"];
                    fd.Risky = h.ContainsKey("Risky") && (bool)h["Risky"];
                    if (h.ContainsKey("Compatibility")) fd.Compatibility = h["Compatibility"].ToString().ToLowerInvariant();
                    fd.Renames = h.ContainsKey("Renames") && (bool)h["Renames"];
                    if (h.ContainsKey("Validate")) fd.Validate = (bool)h["Validate"];
                    if (h.ContainsKey("Enabled") && !(bool)h["Enabled"]) continue;
                }
                var meta = raw.ContainsKey("Meta") ? raw["Meta"] as Dictionary<string, object> : null;
                fd.Title = meta != null && meta.ContainsKey("Name") ? Convert.ToString(meta["Name"]) : fd.Name;
                var steps = raw.ContainsKey("Steps") ? raw["Steps"] as System.Collections.ArrayList : null;
                if (steps != null)
                    foreach (var s in steps)
                    {
                        var sd = s as Dictionary<string, object>;
                        fd.StepTitles.Add(sd != null && sd.ContainsKey("Title") ? Convert.ToString(sd["Title"]) : "?");
                    }
                fd.StepCount = fd.StepTitles.Count;
                list.Add(fd);
            }
            if (!string.IsNullOrEmpty(only))
            {
                var wanted = Regex.Split(only, @"[,\s]+").Where(x => x.Length > 0).ToList(); // PowerShell turns a,b into "a b"
                list = list.Where(fd => wanted.Any(w => fd.Name.Equals(w, StringComparison.OrdinalIgnoreCase) || fd.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            return list;
        }
    }

    public class Issue
    {
        public string Kind;       // see Runner.HardKinds / SoftKinds
        public string Severity;   // hard | soft
        public string Signature;  // grouping key
        public string Detail;
    }

    public class ItemResult
    {
        public string Article;
        public string Path;
        public string Tier;
        public string Role;
        public string Flow;       // "(parse)" | "(baseline)" | flow name
        public string Mode;
        public string Status;     // pass | fail | soft | skip
        public List<Issue> Issues = new List<Issue>();
        public int InBytes, OutBytes;
        public long EngineMs, AhkMs;
        public string Artifact;
        public string CacheKey;

        public string Key { get { return Article + "|" + Flow + "|" + Mode; } }
    }

    public class Baseline
    {
        public string Status;
        public string Error;
        public List<string> Warnings = new List<string>();
        public long Ms;
    }

    class RunContext
    {
        public string RunId, RunDir, Temp;
        public List<FlowDef> Flows;
        public string EngineHash;
        public ConcurrentDictionary<string, Baseline> BaselineCache;
        public ConcurrentDictionary<string, ItemResult> ResultCache;
        public bool UseCache = true, Exec, Shots, Verbose, KeepTemp;
        public List<string> Runnable = new List<string>();
        public int Done, Total, HardFails, SoftFails;
        public readonly object ConsoleLock = new object();
        public string ResultsPath;
    }

    public static class Runner
    {
        static readonly string BaselineCachePath = System.IO.Path.Combine("state", "baseline-cache.json");

        static string EngineHash()
        {
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine.hash");
            return File.Exists(f) ? File.ReadAllText(f).Trim() : "unknown";
        }

        /// <summary>Cached verdicts are only reused if neither the engine nor the harness logic changed.</summary>
        static string HarnessHash()
        {
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "harness.hash");
            return File.Exists(f) ? Util.Short(File.ReadAllText(f).Trim()) : "dev";
        }

        static RunContext NewContext(Args a, string label)
        {
            var ctx = new RunContext();
            ctx.RunId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + (label != null ? "-" + label : "");
            ctx.RunDir = Path.Combine(Paths.Out, ctx.RunId);
            Directory.CreateDirectory(ctx.RunDir);
            ctx.Temp = Path.Combine(Paths.Temp, "run-" + ctx.RunId);
            ctx.Flows = FlowDef.Load(Paths.Flows, a.Get("flows", null));
            if (!a.Has("risky")) ctx.Flows = ctx.Flows.Where(f => !f.Risky || a.Get("flows", "").Contains(f.Name)).ToList();
            ctx.EngineHash = EngineHash();
            ctx.UseCache = !a.Has("no-cache");
            ctx.Exec = a.Has("exec");
            ctx.Shots = a.Has("shots");
            ctx.Verbose = a.Has("verbose");
            ctx.KeepTemp = a.Has("keep");
            ctx.BaselineCache = LoadCache<Baseline>(Path.Combine(Paths.State, "baseline-cache.json"));
            ctx.ResultCache = LoadCache<ItemResult>(Path.Combine(Paths.State, "result-cache.json"));
            string rf = Path.Combine(Paths.CorpusCfg, "runnable.txt");
            if (File.Exists(rf))
                ctx.Runnable = File.ReadAllLines(rf).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            ctx.ResultsPath = Path.Combine(ctx.RunDir, "results.jsonl");
            WorkerPool.TimeoutMs = a.GetInt("engine-timeout", 60000);
            return ctx;
        }

        static ConcurrentDictionary<string, T> LoadCache<T>(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new ConcurrentDictionary<string, T>(Util.FromJson<Dictionary<string, T>>(File.ReadAllText(path)));
            }
            catch (Exception ex) { Console.WriteLine("  (cache " + Path.GetFileName(path) + " unreadable, starting fresh: " + ex.Message + ")"); }
            return new ConcurrentDictionary<string, T>();
        }

        static void SaveCaches(RunContext ctx)
        {
            File.WriteAllText(Path.Combine(Paths.State, "baseline-cache.json"), Util.ToJson(new Dictionary<string, Baseline>(ctx.BaselineCache)));
            File.WriteAllText(Path.Combine(Paths.State, "result-cache.json"), Util.ToJson(new Dictionary<string, ItemResult>(ctx.ResultCache)));
        }

        // ================================================================== run (corpus)

        public static int Run(Args a)
        {
            var corpus = Corpus.Load();
            var arts = Corpus.Select(corpus, a).ToList();
            if (a.Has("failing"))
            {
                var prev = Report.LoadLatestResults(a.Get("tier", "core"));
                var failing = new HashSet<string>(prev.Where(r => r.Status == "fail" || r.Status == "soft").Select(r => r.Article));
                arts = arts.Where(x => failing.Contains(x.Id)).ToList();
                Console.WriteLine("--failing: " + arts.Count + " articles had issues last run");
            }
            var ctx = NewContext(a, a.Get("tier", "core"));
            int workers = a.GetInt("workers", Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2)));
            ctx.Total = arts.Count;
            Console.WriteLine(string.Format("run {0}: {1} articles × {2} flows ({3} flow-runs each), {4} workers, engine {5}, AHK {6}",
                ctx.RunId, arts.Count, ctx.Flows.Count, ctx.Flows.Sum(f => f.Modes.Count), workers, Util.Short(ctx.EngineHash), Ahk.Version));

            var all = new ConcurrentBag<ItemResult>();
            var sw = Stopwatch.StartNew();
            var progress = new Timer(_ => PrintProgress(ctx, sw), null, 5000, 5000);
            Parallel.ForEach(arts, new ParallelOptions { MaxDegreeOfParallelism = workers }, art =>
            {
                List<ItemResult> results;
                try { results = ProcessArticle(art, ctx); }
                catch (Exception ex)
                {
                    results = new List<ItemResult> { new ItemResult { Article = art.Id, Path = art.Path, Tier = art.Tier, Role = art.Role, Flow = "(harness)", Mode = "", Status = "fail",
                        Issues = { new Issue { Kind = "harness-error", Severity = "hard", Signature = "harness-error: " + ex.GetType().Name, Detail = ex.ToString() } } } };
                }
                foreach (var r in results)
                {
                    all.Add(r);
                    Util.AppendLine(ctx.ResultsPath, Util.ToJson(r));
                }
                Interlocked.Increment(ref ctx.Done);
            });
            progress.Dispose();
            PrintProgress(ctx, sw);
            SaveCaches(ctx);
            if (!ctx.KeepTemp) TryDelete(ctx.Temp);

            var summary = Report.Write(ctx.RunId, ctx.RunDir, all.ToList(), ctx.Flows, a.Get("tier", "core"), sw.Elapsed, ctx.EngineHash);
            Console.WriteLine();
            Console.WriteLine(summary);
            return all.Any(r => r.Status == "fail") ? 1 : 0;
        }

        static void PrintProgress(RunContext ctx, Stopwatch sw)
        {
            lock (ctx.ConsoleLock)
                Console.WriteLine(string.Format("  [{0}/{1}] {2:F0}s  hard={3} soft={4}", ctx.Done, ctx.Total, sw.Elapsed.TotalSeconds, ctx.HardFails, ctx.SoftFails));
        }

        static void TryDelete(string dir)
        {
            Util.SafeDeleteTree(dir);
        }

        static string IncludeSignature(PreparedScript prep)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(prep.Includes);
            var sb = new StringBuilder();
            while (queue.Count > 0 && seen.Count < 300)
            {
                string f = queue.Dequeue();
                if (!seen.Add(f) || !File.Exists(f)) continue;
                sb.Append(f).Append('@').Append(File.GetLastWriteTimeUtc(f).Ticks).Append(';');
                try { foreach (var inc in Ahk.Prepare(Util.ReadScript(f), f, false).Includes) queue.Enqueue(inc); } catch { }
            }
            return Util.Short(Util.Sha1(sb.ToString()));
        }

        static List<string> NormWarnings(AhkOutcome o)
        {
            return o.Warnings.Select(w => w.Message + " | " + (w.Specifically ?? "")).OrderBy(x => x).ToList();
        }

        static Baseline GetBaseline(Article art, string text, string exe, PreparedScript prep, RunContext ctx)
        {
            if (art.IncSig == null) art.IncSig = IncludeSignature(prep);
            string key = art.Hash + "|" + art.IncSig + "|" + Ahk.Version + "|" + Path.GetFileName(exe) + "|" + HarnessHash();
            Baseline b;
            if (ctx.UseCache && ctx.BaselineCache.TryGetValue(key, out b)) return b;
            string copy = Path.Combine(ctx.Temp, art.Id, "baseline", Path.GetFileName(art.Path));
            Ahk.WriteRelocated(copy, prep, art.Path);
            var o = Ahk.Validate(exe, copy, prep.LineOffset, 20000);
            b = new Baseline { Status = o.Status, Warnings = NormWarnings(o), Ms = o.ElapsedMs };
            if (!o.Ok) b.Error = o.Errors.Count > 0 ? ("line " + o.Errors[0].Line + ": " + o.Errors[0].Message + (string.IsNullOrEmpty(o.Errors[0].Specifically) ? "" : " [" + Util.Clip(o.Errors[0].Specifically, 120) + "]")) : o.Status;
            ctx.BaselineCache[key] = b;
            return b;
        }

        static bool IsRunnable(RunContext ctx, string path)
        {
            return ctx.Runnable.Any(r => path.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Only harness cases and user allow-listed scripts may ever be executed.</summary>
        public static bool MayExecute(string path)
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Path.GetFullPath(Paths.Cases) + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            string rf = Path.Combine(Paths.CorpusCfg, "runnable.txt");
            return File.Exists(rf) && File.ReadAllLines(rf).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Any(r => full.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static List<ItemResult> ProcessArticle(Article art, RunContext ctx)
        {
            var results = new List<ItemResult>();
            string text = Util.ReadScript(art.Path);
            string why;
            string exe = Ahk.PickExe(text, out why);
            Func<string, string, ItemResult> mk = (flow, mode) => new ItemResult { Article = art.Id, Path = art.Path, Tier = art.Tier, Role = art.Role, Flow = flow, Mode = mode, InBytes = text.Length };
            if (exe == null)
            {
                var s = mk("(baseline)", ""); s.Status = "skip"; s.Issues.Add(new Issue { Kind = "not-v2", Severity = "info", Signature = why, Detail = why });
                results.Add(s);
                return results;
            }

            var prep = Ahk.Prepare(text, art.Path, true);
            var baseline = GetBaseline(art, text, exe, prep, ctx);
            if (baseline.Status != "ok")
            {
                var s = mk("(baseline)", ""); s.Status = "skip";
                s.Issues.Add(new Issue { Kind = "baseline-fail", Severity = "info", Signature = Signature(baseline.Error ?? baseline.Status), Detail = baseline.Error });
                results.Add(s);
                return results;
            }

            // Parser sanity: AHK accepts this file, so any Error node the parser produces is a false positive.
            results.Add(ParseCheck(art, text, ctx, mk));

            var runtimeOrig = (AhkOutcome)null;
            bool exec = ctx.Exec && IsRunnable(ctx, art.Path);
            foreach (var flow in ctx.Flows)
                foreach (string mode in flow.Modes)
                {
                    var r = RunFlowItem(art, text, exe, baseline, flow, mode, ctx, mk, exec, ref runtimeOrig);
                    results.Add(r);
                }

            foreach (var r in results) Tally(ctx, r);
            if (!ctx.KeepTemp) TryDelete(Path.Combine(ctx.Temp, art.Id));
            return results;
        }

        static void Tally(RunContext ctx, ItemResult r)
        {
            if (r.Status == "fail") Interlocked.Increment(ref ctx.HardFails);
            else if (r.Status == "soft") Interlocked.Increment(ref ctx.SoftFails);
            if (r.Status == "fail" && ctx.Verbose)
                lock (ctx.ConsoleLock)
                    Console.WriteLine("  FAIL " + r.Flow + "/" + r.Mode + "  " + r.Path + "\n       " + string.Join("\n       ", r.Issues.Select(i => i.Kind + ": " + Util.Clip(i.Detail, 300))));
        }

        static ItemResult ParseCheck(Article art, string text, RunContext ctx, Func<string, string, ItemResult> mk)
        {
            var ir = mk("(parse)", "");
            ir.CacheKey = art.Hash + "|parse|" + ctx.EngineHash + "|" + HarnessHash();
            ItemResult cached;
            if (ctx.UseCache && ctx.ResultCache.TryGetValue(ir.CacheKey, out cached)) return Rebind(cached, art);

            var r = WorkerPool.Call(new WorkerJob { Id = art.Id, Op = "parse", Path = art.Path, Source = text });
            ir.EngineMs = r.Ms;
            if (EngineFailure(r, ir, "(parse)")) { }
            else
            {
                var errs = r.ParseIssues.Where(p => p.Type == "Error").ToList();
                if (errs.Count > 0)
                    ir.Issues.Add(new Issue
                    {
                        Kind = "parse-false-error",
                        Severity = "hard",
                        Signature = "parse error on valid code: " + Signature(errs[0].Message),
                        Detail = string.Join("\n", errs.Take(5).Select(e => "line " + e.Line + ":" + e.Column + " " + e.Message + "\n" + Util.Context(text, e.Line, 1).TrimEnd())) + (errs.Count > 5 ? "\n(+" + (errs.Count - 5) + " more)" : ""),
                    });
                var unknown = r.ParseIssues.Where(p => p.Type == "Unknown").ToList();
                if (unknown.Count > 0)
                    ir.Issues.Add(new Issue { Kind = "parse-unknown", Severity = "soft", Signature = "Unknown node on valid code", Detail = "line " + unknown[0].Line + ": " + unknown[0].Message + "\n" + Util.Context(text, unknown[0].Line, 1) });
            }
            Finish(ir);
            if (ctx.UseCache) ctx.ResultCache[ir.CacheKey] = ir;
            return ir;
        }

        static ItemResult Rebind(ItemResult cached, Article art)
        {
            cached.Article = art.Id; cached.Path = art.Path; cached.Tier = art.Tier; cached.Role = art.Role;
            return cached;
        }

        static bool EngineFailure(WorkerReply r, ItemResult ir, string where)
        {
            if (r.Crash != null)
            {
                string kind = r.Crash.StartsWith("timeout") ? "engine-hang" : "engine-crash";
                ir.Issues.Add(new Issue { Kind = kind, Severity = "hard", Signature = kind + (r.Crash.Contains("stack overflow") || r.Crash.Contains("StackOverflow") ? ": stack overflow" : ""), Detail = r.Crash });
                return true;
            }
            if (r.Exception != null)
            {
                string first = r.Exception.Split('\n')[0];
                ir.Issues.Add(new Issue { Kind = "engine-exception", Severity = "hard", Signature = "exception: " + Signature(first) + TopFrame(r.Exception), Detail = r.Exception });
                return true;
            }
            return false;
        }

        static string TopFrame(string ex)
        {
            var m = Regex.Match(ex, @"at ([\w\.`<>]+)\(");
            return m.Success ? " @ " + m.Groups[1].Value : "";
        }

        static ItemResult RunFlowItem(Article art, string text, string exe, Baseline baseline, FlowDef flow, string mode, RunContext ctx,
            Func<string, string, ItemResult> mk, bool exec, ref AhkOutcome runtimeOrig)
        {
            var ir = mk(flow.Name, mode);
            ir.CacheKey = art.Hash + "|" + art.IncSig + "|" + flow.Hash + "|" + mode + "|" + ctx.EngineHash + "|" + HarnessHash() + "|" + Ahk.Version + (exec ? "|exec" : "");
            ItemResult cached;
            if (ctx.UseCache && ctx.ResultCache.TryGetValue(ir.CacheKey, out cached) && (cached.Artifact == null || Directory.Exists(cached.Artifact)))
                return Rebind(cached, art);

            if (flow.Compatibility.Contains("entry") && art.Role == "lib")
            {
                ir.Status = "skip";
                ir.Issues.Add(new Issue { Kind = "not-applicable", Severity = "info", Signature = "n/a: library file (the flow needs an entry point)", Detail = "" });
                return ir;
            }

            string name = Path.GetFileName(art.Path);
            string itemDir = Path.Combine(ctx.Temp, art.Id, flow.Name + "-" + mode);
            string rawOut = Path.Combine(itemDir, "raw", name);
            var job = new WorkerJob
            {
                Id = art.Id + ":" + flow.Name,
                Op = "flow",
                Path = art.Path,
                Source = text,
                FlowJson = flow.EngineJson(itemDir, -1),
                Mode = mode,
                Idempotent = flow.Idempotent,
                AstEquiv = flow.AstEquiv,
                OutPath = rawOut,
            };
            var r = WorkerPool.Call(job);
            ir.EngineMs = r.Ms;
            ir.OutBytes = r.OutBytes;
            string outText = null;
            AhkOutcome validated = null;

            if (EngineFailure(r, ir, flow.Name)) { }
            else if (r.LogErrors.Count > 0)
            {
                ir.Issues.Add(new Issue { Kind = "plugin-error", Severity = "hard", Signature = "plugin error: " + Signature(PluginErrorHead(r.LogErrors[0])), Detail = string.Join("\n", r.LogErrors.Take(3)) });
            }
            else if (r.OutputIsEmpty)
            {
                bool shakes = flow.Raw.ContainsKey("Steps") && Util.ToJson(flow.Raw["Steps"]).Contains("TreeShakerConfig");
                ir.Issues.Add(new Issue { Kind = "empty-output", Severity = shakes ? "soft" : "hard", Signature = shakes ? "empty output (everything tree-shaken)" : "empty output", Detail = "flow produced no code" });
            }
            else if (flow.Validate)
            {
                outText = Util.ReadScript(rawOut);
                var prep = Ahk.Prepare(outText, art.Path, true);
                string vPath = Path.Combine(itemDir, name);
                Ahk.WriteRelocated(vPath, prep, art.Path);
                validated = Ahk.Validate(exe, vPath, prep.LineOffset, 20000);
                ir.AhkMs = validated.ElapsedMs;
                if (!validated.Ok)
                {
                    var e = validated.Errors.FirstOrDefault();
                    string detail = e == null ? validated.Status : ("AHK: line " + e.Line + ": " + e.Message + (string.IsNullOrEmpty(e.Specifically) ? "" : "\n     specifically: " + Util.Clip(e.Specifically, 300)) + "\n" + Util.Context(outText, e.Line, 3));
                    if (flow.StepCount > 1) detail += "\nfirst failing step: " + TriageStep(art, text, exe, flow, mode, itemDir, ctx);
                    ir.Issues.Add(new Issue { Kind = "validate-fail", Severity = "hard", Signature = "output rejected by AHK: " + Signature(e == null ? validated.Status : e.Message), Detail = detail });
                }
                else
                {
                    var before = new List<string>(baseline.Warnings);
                    var added = new List<string>();
                    // A renaming flow changes the names in the warnings, so there only kinds and counts are compared.
                    Func<string, string> key = w => flow.Renames ? w.Split('|')[0].Trim() : w;
                    before = before.Select(key).ToList();
                    foreach (var w in NormWarnings(validated)) { if (!before.Remove(key(w))) added.Add(w); }
                    if (added.Count > 0)
                        ir.Issues.Add(new Issue { Kind = "new-warnings", Severity = "soft", Signature = "new warning: " + Signature(added[0].Split('|')[0].Trim()),
                            Detail = string.Join("\n", added.Take(5)) + FirstWarningContext(validated, outText) });

                    var hot = HotOracle.Compare(HotSource(text, art.Path, mode), outText);
                    if (hot.Count > 0)
                        ir.Issues.Add(new Issue { Kind = "hotkey-diff", Severity = "hard", Signature = "hotkey/hotstring changed: " + Regex.Replace(Regex.Replace(hot[0], "`[^`]*`", "`…`"), @"\(orig line \d+\)", "").Trim(),
                            Detail = string.Join("\n", hot.Take(8)) });
                }
            }

            if (r.IdemDiff != null)
                ir.Issues.Add(new Issue { Kind = "not-idempotent", Severity = "soft", Signature = "flow(flow(x)) != flow(x)", Detail = r.IdemDiff });
            if (r.AstDiff != null)
                ir.Issues.Add(new Issue { Kind = "ast-diff", Severity = "soft", Signature = "AST changes after re-parse", Detail = r.AstDiff });
            if (r.TokenDiff != null)
                ir.Issues.Add(new Issue { Kind = "token-diff", Severity = "soft", Signature = "emitted tokens differ from the original", Detail = r.TokenDiff });

            if (exec && validated != null && validated.Ok)
            {
                if (runtimeOrig == null) runtimeOrig = Execute(exe, text, art.Path, Path.Combine(ctx.Temp, art.Id, "exec-orig"), ctx.Shots ? Path.Combine(ctx.RunDir, "shots", art.Id + "_orig") : null);
                var rt = Execute(exe, outText, art.Path, Path.Combine(itemDir, "exec"), ctx.Shots ? Path.Combine(ctx.RunDir, "shots", art.Id + "_" + flow.Name) : null);
                string diff = Transcript.Diff(runtimeOrig, rt);
                if (diff != null)
                    ir.Issues.Add(new Issue { Kind = "runtime-diff", Severity = "hard", Signature = "runtime behaviour differs" + (rt.Status == "runtime-error" && runtimeOrig.Status != "runtime-error" ? ": new runtime error " + Signature(rt.FirstError) : ""), Detail = diff });
            }

            if (flow.Compatibility.Contains("best-effort"))
                foreach (var i in ir.Issues.Where(i => i.Severity == "hard"))
                {
                    i.Severity = "soft";
                    i.Signature = "[best-effort] " + i.Signature;
                }
            Finish(ir);
            if ((ir.Status == "fail" || (flow.Compatibility.Contains("best-effort") && ir.Issues.Any(i => i.Signature.StartsWith("[best-effort]")))) && ctx.RunId != "reduce") ir.Artifact = SaveArtifact(ctx, art, text, flow, mode, rawOut, ir);
            if (ctx.UseCache) ctx.ResultCache[ir.CacheKey] = ir;
            return ir;
        }

        /// <summary>The original declarations the output must preserve: main file, plus every included file when inlining.</summary>
        static string HotSource(string text, string path, string mode)
        {
            if (mode == "local") return text;
            var sb = new StringBuilder(text);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(Ahk.Prepare(text, path, false).Includes);
            while (queue.Count > 0 && seen.Count < 300)
            {
                string f = queue.Dequeue();
                if (!seen.Add(f) || !File.Exists(f)) continue;
                string inc = Util.ReadScript(f);
                sb.Append("\n").Append(inc);
                foreach (var x in Ahk.Prepare(inc, f, false).Includes) queue.Enqueue(x);
            }
            return sb.ToString();
        }

        static string FirstWarningContext(AhkOutcome o, string outText)
        {
            var w = o.Warnings.FirstOrDefault();
            return w != null && w.Line > 0 ? "\n" + Util.Context(outText, w.Line, 2) : "";
        }

        static string PluginErrorHead(string log)
        {
            var m = Regex.Match(log, @"Plugin (\S+) failed: (?:System\.)?([\w\.]+Exception)?:?\s*([^\r\n]*)");
            if (m.Success) return m.Groups[1].Value + " " + m.Groups[2].Value + " " + Util.Clip(m.Groups[3].Value, 80);
            return Util.Clip(log.Split('\n')[0], 120);
        }

        /// <summary>Re-runs growing step prefixes to find the first step whose output AHK rejects.</summary>
        static string TriageStep(Article art, string text, string exe, FlowDef flow, string mode, string itemDir, RunContext ctx)
        {
            for (int k = 1; k < flow.StepCount; k++)
            {
                string dir = Path.Combine(itemDir, "triage" + k);
                string raw = Path.Combine(dir, "raw", Path.GetFileName(art.Path));
                var r = WorkerPool.Call(new WorkerJob { Id = art.Id + ":triage", Op = "flow", Path = art.Path, Source = text, FlowJson = flow.EngineJson(dir, k), Mode = mode, OutPath = raw });
                if (r.Crash != null || r.Exception != null || r.LogErrors.Count > 0) return flow.StepTitles[k - 1] + " (engine failure)";
                var prep = Ahk.Prepare(Util.ReadScript(raw), art.Path, true);
                string v = Path.Combine(dir, Path.GetFileName(art.Path));
                Ahk.WriteRelocated(v, prep, art.Path);
                if (!Ahk.Validate(exe, v, prep.LineOffset, 20000).Ok) return "#" + k + " " + flow.StepTitles[k - 1];
            }
            return "#" + flow.StepCount + " " + flow.StepTitles[flow.StepCount - 1];
        }

        static AhkOutcome Execute(string exe, string text, string origPath, string dir, string shots)
        {
            var prep = Ahk.Prepare(text, origPath, false);
            string p = Path.Combine(dir, Path.GetFileName(origPath));
            Ahk.WriteRelocated(p, prep, origPath);
            return Ahk.Execute(exe, p, prep.LineOffset, 4000, shots);
        }

        static void Finish(ItemResult ir)
        {
            if (ir.Issues.Any(i => i.Severity == "hard")) ir.Status = "fail";
            else if (ir.Issues.Any(i => i.Severity == "soft")) ir.Status = "soft";
            else ir.Status = "pass";
        }

        static string SaveArtifact(RunContext ctx, Article art, string text, FlowDef flow, string mode, string rawOut, ItemResult ir)
        {
            try
            {
                string dir = Path.Combine(ctx.RunDir, "fail", Util.SafeName(art.Id + "_" + flow.Name + "-" + mode, 80));
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "original" + Path.GetExtension(art.Path)), text, new UTF8Encoding(true));
                if (File.Exists(rawOut)) File.Copy(rawOut, Path.Combine(dir, "output" + Path.GetExtension(art.Path)), true);
                var sb = new StringBuilder();
                sb.AppendLine("article: " + art.Path);
                sb.AppendLine("flow:    " + flow.Name + " (" + flow.File + "), mode " + mode);
                sb.AppendLine("repro:   harness.ps1 check \"" + art.Path + "\" --flows " + flow.Name);
                foreach (var i in ir.Issues) sb.AppendLine().AppendLine("[" + i.Severity + "] " + i.Kind + " — " + i.Signature).AppendLine(i.Detail);
                File.WriteAllText(Path.Combine(dir, "issue.txt"), sb.ToString(), new UTF8Encoding(false));
                return dir;
            }
            catch { return null; }
        }

        /// <summary>Grouping key: strips numbers, quotes' contents beyond a few chars, paths.</summary>
        public static string Signature(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Split('\n')[0].Trim();
            s = Regex.Replace(s, @"[A-Za-z]:\\[^\s""']+", "<path>");
            s = Regex.Replace(s, @"\b\d+\b", "N");
            return Util.Clip(s, 140);
        }

        // ================================================================== evaluation of arbitrary text (reducer)

        /// <summary>
        /// Evaluates <paramref name="text"/> as if it were the file at <paramref name="origPath"/> (includes resolve
        /// from its folder). flow == null means the parser check. Returns null when AHK rejects the text itself.
        /// </summary>
        public static List<Issue> Evaluate(string text, string origPath, FlowDef flow, string mode, string tempRoot, bool exec)
        {
            var ctx = new RunContext
            {
                RunId = "reduce", RunDir = tempRoot, Temp = tempRoot, Flows = new List<FlowDef>(), EngineHash = EngineHash(),
                UseCache = false, KeepTemp = false,
                BaselineCache = new ConcurrentDictionary<string, Baseline>(), ResultCache = new ConcurrentDictionary<string, ItemResult>(),
            };
            string hash = Util.Sha1(Util.NormalizeNewlines(text)) + Guid.NewGuid().ToString("N").Substring(0, 6);
            var art = new Article { Id = Util.Short(hash), Path = origPath, Tier = "reduce", Role = "entry", Hash = hash };
            string why;
            string exe = Ahk.PickExe(text, out why) ?? Ahk.Exe64;
            try
            {
                var baseline = GetBaseline(art, text, exe, Ahk.Prepare(text, origPath, true), ctx);
                if (baseline.Status != "ok") return null;
                Func<string, string, ItemResult> mk = (f, m) => new ItemResult { Article = art.Id, Path = origPath, Flow = f, Mode = m };
                AhkOutcome rt = null;
                var ir = flow == null ? ParseCheck(art, text, ctx, mk) : RunFlowItem(art, text, exe, baseline, flow, mode, ctx, mk, exec, ref rt);
                return ir.Issues;
            }
            finally { TryDelete(Path.Combine(tempRoot, art.Id)); }
        }

        // ================================================================== check (single file, verbose)

        public static int Check(Args a)
        {
            if (a.Positional.Count == 0) { Console.WriteLine("usage: check <file> [--flows a,b] [--exec]"); return 2; }
            string path = Path.GetFullPath(a.Positional[0]);
            var ctx = NewContext(a, "check");
            ctx.UseCache = false;
            ctx.KeepTemp = true;
            string text = Util.ReadScript(path);
            string hash = Util.Sha1(Util.NormalizeNewlines(text));
            var art = new Article { Id = Util.Short(hash), Path = path, Tier = "adhoc", Role = "entry", Hash = hash };
            bool exec = a.Has("exec");
            if (exec) ctx.Runnable.Add(path);
            Console.WriteLine("check " + path + " — " + ctx.Flows.Count + " flows");
            var results = ProcessArticle(art, ctx);
            int hard = 0;
            foreach (var r in results)
            {
                string mark = r.Status == "pass" ? "ok  " : r.Status == "soft" ? "soft" : r.Status == "skip" ? "skip" : "FAIL";
                Console.WriteLine(string.Format("  {0} {1,-22} {2,-7} engine {3,5}ms  ahk {4,4}ms  {5,7} → {6,7} bytes", mark, r.Flow, r.Mode, r.EngineMs, r.AhkMs, r.InBytes, r.OutBytes));
                foreach (var i in r.Issues)
                {
                    Console.WriteLine("       [" + i.Kind + "] " + i.Signature);
                    if (r.Status != "pass") foreach (var l in (i.Detail ?? "").Split('\n').Take(14)) Console.WriteLine("         " + l.TrimEnd());
                }
                if (r.Artifact != null) Console.WriteLine("       artifacts: " + r.Artifact);
                if (r.Status == "fail") hard++;
            }
            Console.WriteLine(hard == 0 ? "no hard failures" : hard + " hard failure(s)");
            Console.WriteLine("scratch: " + ctx.Temp);
            return hard == 0 ? 0 : 1;
        }

        // ================================================================== cases

        public static int Cases(Args a)
        {
            var ctx = NewContext(a, "cases");
            ctx.UseCache = false;
            ctx.Verbose = false; // per-case output below
            string filter = a.Get("filter", null);
            var files = Directory.GetFiles(Paths.Cases, "*.ah*", SearchOption.AllDirectories).Where(f => filter == null || f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(f => f).ToList();
            var allFlows = ctx.Flows;
            var all = new List<ItemResult>();
            int failed = 0;
            foreach (string f in files)
            {
                var spec = CaseSpec.Parse(f);
                ctx.Flows = spec.Flows == null ? allFlows : allFlows.Where(fl => spec.Flows.Any(w => fl.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase))).ToList();
                if (spec.Run) ctx.Runnable.Add(f);
                ctx.Exec = spec.Run;
                string text = Util.ReadScript(f);
                string hash = Util.Sha1(Util.NormalizeNewlines(text));
                var art = new Article { Id = Util.Short(hash), Path = f, Tier = "case", Role = "entry", Hash = hash };
                var results = ProcessArticle(art, ctx);

                // The case's own expectations about the ORIGINAL (proves the case exercises what it claims).
                if (spec.Expect.Count > 0)
                {
                    var o = Execute(Ahk.PickExe(text, out spec.Why) ?? Ahk.Exe64, text, f, Path.Combine(ctx.Temp, art.Id, "expect"), null);
                    string transcript = Transcript.Render(o);
                    foreach (string e in spec.Expect)
                        if (transcript.IndexOf(e, StringComparison.Ordinal) < 0)
                        {
                            var bad = new ItemResult { Article = art.Id, Path = f, Tier = "case", Flow = "(expect)", Mode = "", Status = "fail" };
                            bad.Issues.Add(new Issue { Kind = "case-expectation", Severity = "hard", Signature = "original does not show expected output", Detail = "expected `" + e + "` in:\n" + transcript });
                            results.Add(bad);
                        }
                }
                var bads = results.Where(r => r.Status == "fail" || r.Status == "soft").ToList();
                string label = Path.GetFileName(f) + (spec.Issue != null ? " (issue #" + spec.Issue + ")" : "");
                if (bads.Count == 0) Console.WriteLine("  ok    " + label + "  [" + results.Count(r => r.Status == "pass") + " checks]");
                else
                {
                    if (bads.Any(r => r.Status == "fail")) failed++;
                    Console.WriteLine("  " + (bads.Any(r => r.Status == "fail") ? "FAIL" : "soft") + "  " + label);
                    foreach (var r in bads)
                        foreach (var i in r.Issues)
                        {
                            Console.WriteLine("        " + r.Flow + (r.Mode.Length > 0 ? "/" + r.Mode : "") + " [" + i.Kind + "] " + i.Signature);
                            if (a.Has("verbose")) foreach (var l in (i.Detail ?? "").Split('\n').Take(12)) Console.WriteLine("            " + l.TrimEnd());
                        }
                }
                all.AddRange(results);
            }
            foreach (var r in all) Util.AppendLine(ctx.ResultsPath, Util.ToJson(r));
            Report.Write(ctx.RunId, ctx.RunDir, all, allFlows, "cases", TimeSpan.Zero, ctx.EngineHash);
            if (!ctx.KeepTemp) TryDelete(ctx.Temp);
            Console.WriteLine((files.Count - failed) + "/" + files.Count + " cases clean  —  details: " + Path.Combine(ctx.RunDir, "summary.md"));
            return failed == 0 ? 0 : 1;
        }
    }

    public class CaseSpec
    {
        public List<string> Flows;
        public bool Run;
        public List<string> Expect = new List<string>();
        public string Issue;
        public string Why;

        public static CaseSpec Parse(string file)
        {
            var s = new CaseSpec();
            foreach (string raw in File.ReadLines(file).Take(40))
            {
                var m = Regex.Match(raw, @"^\s*;\s*@(\w+)\s*:?\s*(.*)$");
                if (!m.Success) continue;
                string key = m.Groups[1].Value.ToLowerInvariant(), val = m.Groups[2].Value.Trim();
                if (key == "flows") s.Flows = Regex.Split(val, @"[,\s]+").Where(x => x.Length > 0).ToList();
                else if (key == "run") s.Run = true;
                else if (key == "expect") s.Expect.Add(val);
                else if (key == "issue") s.Issue = val.TrimStart('#');
            }
            return s;
        }
    }

    /// <summary>Runtime behaviour as comparable lines: dialogs, runtime errors, stdout lines, exit state.</summary>
    public static class Transcript
    {
        public static List<string> Lines(AhkOutcome o)
        {
            var lines = new List<string>();
            foreach (var w in o.Windows)
            {
                if (w.Kind == "MsgBox") lines.Add("msgbox: " + w.Text.Replace("\r", "").Replace("\n", "⏎"));
                else if (w.Kind == "AhkError") lines.Add("runtime error: " + FirstLine(w.Text));
                else if (w.Kind == "AhkWarning") lines.Add(FirstLine(w.Text).Replace("Warning: ", "warning: "));
                else lines.Add("window: " + w.Class + " '" + w.Title + "' [" + string.Join(", ", w.Controls.Select(c => Regex.Replace(c, @"\|#\d+$", "")).Take(60)) + "]");
            }
            foreach (var l in Util.NormalizeNewlines(o.Stdout).TrimEnd('\n').Split('\n'))
                if (o.Stdout.Length > 0) lines.Add("out| " + l);
            lines.Add(o.Alive ? "still running" : "exit " + o.ExitCode);
            return lines;
        }

        public static string Render(AhkOutcome o) { return string.Join("\n", Lines(o)); }

        static string FirstLine(string s) { return (s ?? "").Split('\n')[0].Trim(); }

        /// <summary>Line diff (LCS) of the two transcripts: "-" only in original, "+" only in transformed.</summary>
        public static string Diff(AhkOutcome orig, AhkOutcome xf)
        {
            var a = Lines(orig);
            var b = Lines(xf);
            if (a.SequenceEqual(b)) return null;
            int n = a.Count, m = b.Count;
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            var outLines = new List<string>();
            int x = 0, y = 0, shown = 0;
            while ((x < n || y < m) && shown < 24)
            {
                if (x < n && y < m && a[x] == b[y]) { x++; y++; continue; }
                if (y < m && (x == n || lcs[x, y + 1] >= lcs[x + 1, y])) { outLines.Add("+ " + Util.Clip(b[y], 200)); y++; }
                else { outLines.Add("- " + Util.Clip(a[x], 200)); x++; }
                shown++;
            }
            return string.Join("\n", outLines) + (shown >= 24 ? "\n…" : "");
        }
    }
}