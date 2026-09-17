using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AstHarness
{
    public static class Report
    {
        static string LastRunFile(string tier) { return Path.Combine(Paths.State, "last-run-" + Util.SafeName(tier, 30) + ".txt"); }

        public static List<ItemResult> LoadResults(string runDir)
        {
            var list = new List<ItemResult>();
            string p = Path.Combine(runDir, "results.jsonl");
            if (!File.Exists(p)) return list;
            var js = Util.NewJson();
            foreach (string l in File.ReadLines(p))
                if (l.Trim().Length > 0) list.Add(js.Deserialize<ItemResult>(l));
            return list;
        }

        public static List<ItemResult> LoadLatestResults(string tier)
        {
            string f = LastRunFile(tier);
            if (!File.Exists(f)) return new List<ItemResult>();
            return LoadResults(Path.Combine(Paths.Out, File.ReadAllText(f).Trim()));
        }

        public static string Write(string runId, string runDir, List<ItemResult> results, List<FlowDef> flows, string tier, TimeSpan elapsed, string engineHash)
        {
            // Compare each check with its latest known status from ANY earlier run of this tier (a run with
            // fewer flows must not hide regressions in the others), then fold this run into that record.
            string lf = LastRunFile(tier);
            string prevId = File.Exists(lf) ? File.ReadAllText(lf).Trim() : null;
            string latestFile = Path.Combine(Paths.State, "latest-status-" + Util.SafeName(tier, 30) + ".json");
            Dictionary<string, string> latest = new Dictionary<string, string>();
            try { if (File.Exists(latestFile)) latest = Util.FromJson<Dictionary<string, string>>(File.ReadAllText(latestFile)); } catch { }
            if (latest.Count == 0 && prevId != null && prevId != runId)
                foreach (var r in LoadResults(Path.Combine(Paths.Out, prevId))) latest[r.Key] = r.Status + "|" + prevId;
            List<ItemResult> prev = null;
            if (latest.Count > 0)
            {
                prev = new List<ItemResult>();
                foreach (var kv in latest)
                {
                    var parts = kv.Key.Split('|');
                    var st = kv.Value.Split('|');
                    if (parts.Length == 3) prev.Add(new ItemResult { Article = parts[0], Flow = parts[1], Mode = parts[2], Status = st[0] });
                }
                prevId = "the latest known result of each check";
            }

            var sb = new StringBuilder();
            var articles = results.GroupBy(r => r.Article).ToList();
            int tested = articles.Count(g => g.Any(r => r.Flow != "(baseline)"));
            int skipped = articles.Count - tested;
            var items = results.Where(r => r.Flow != "(baseline)").ToList();
            int pass = items.Count(r => r.Status == "pass"), fail = items.Count(r => r.Status == "fail"), soft = items.Count(r => r.Status == "soft");

            sb.AppendLine("# Harness run " + runId);
            sb.AppendLine();
            sb.AppendLine("engine `" + Util.Short(engineHash) + "` · AutoHotkey " + Ahk.Version + " · tier `" + tier + "`" + (elapsed.TotalSeconds > 0 ? " · " + elapsed.TotalSeconds.ToString("F0") + "s" : ""));
            sb.AppendLine();
            sb.AppendLine("- articles: **" + articles.Count + "** — " + tested + " tested (AHK accepts the original), " + skipped + " skipped");
            sb.AppendLine("- checks: **" + items.Count + "** — " + pass + " pass · **" + fail + " hard fail** · " + soft + " soft");
            int affected = items.Where(r => r.Status == "fail").Select(r => r.Article).Distinct().Count();
            sb.AppendLine("- articles with a hard failure: " + affected + " / " + tested);
            sb.AppendLine();

            sb.AppendLine("## Per flow");
            sb.AppendLine();
            sb.AppendLine("| flow | mode | pass | fail | soft | size Δ | top hard issue |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
            foreach (var g in items.GroupBy(r => r.Flow + "|" + r.Mode).OrderBy(g => FlowOrder(flows, g.First().Flow)))
            {
                var first = g.First();
                var top = g.Where(r => r.Status == "fail").SelectMany(r => r.Issues.Where(i => i.Severity == "hard")).GroupBy(i => i.Signature).OrderByDescending(x => x.Count()).FirstOrDefault();
                long inB = g.Where(r => r.OutBytes > 0).Sum(r => (long)r.InBytes), outB = g.Where(r => r.OutBytes > 0).Sum(r => (long)r.OutBytes);
                string delta = inB > 0 && first.Flow != "(parse)" ? ((outB - inB) * 100.0 / inB).ToString("+0;-0") + "%" : "";
                sb.AppendLine(string.Format("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |", first.Flow, first.Mode, g.Count(r => r.Status == "pass"), g.Count(r => r.Status == "fail"), g.Count(r => r.Status == "soft"),
                    delta, top == null ? "" : Md(top.Key) + " ×" + top.Count()));
            }
            sb.AppendLine();

            AppendIssues(sb, "Hard failures by signature", items, "hard", 40);
            AppendIssues(sb, "Soft issues by signature", items, "soft", 15);

            if (prev != null && prev.Count > 0)
            {
                var prevMap = new Dictionary<string, ItemResult>();
                foreach (var r in prev) prevMap[r.Key] = r;
                var newly = new List<ItemResult>();
                var fixedList = new List<ItemResult>();
                foreach (var r in items)
                {
                    ItemResult p;
                    if (!prevMap.TryGetValue(r.Key, out p)) continue;
                    if (r.Status == "fail" && p.Status != "fail") newly.Add(r);
                    if (r.Status != "fail" && p.Status == "fail") fixedList.Add(r);
                }
                sb.AppendLine("## Changes since " + prevId);
                sb.AppendLine();
                sb.AppendLine("- **newly failing: " + newly.Count + "**" + (newly.Count > 0 ? "  ← regressions, look at these first" : ""));
                foreach (var r in newly.Take(25)) sb.AppendLine("  - `" + r.Flow + "/" + r.Mode + "` " + r.Path + " — " + Md(r.Issues.First(i => i.Severity == "hard").Signature));
                sb.AppendLine("- fixed: " + fixedList.Count);
                foreach (var r in fixedList.Take(10)) sb.AppendLine("  - `" + r.Flow + "/" + r.Mode + "` " + r.Path);
                sb.AppendLine();
            }

            var skips = results.Where(r => r.Flow == "(baseline)").SelectMany(r => r.Issues).GroupBy(i => i.Kind + ": " + i.Signature).OrderByDescending(g => g.Count()).Take(12).ToList();
            if (skips.Count > 0)
            {
                sb.AppendLine("## Skipped (not part of the score)");
                sb.AppendLine();
                foreach (var g in skips) sb.AppendLine("- " + g.Count() + " × " + Md(g.Key));
                sb.AppendLine();
            }

            var slow = items.Where(r => r.Flow == "(parse)").OrderByDescending(r => r.EngineMs).Take(5).ToList();
            if (slow.Count > 0 && slow[0].EngineMs > 500)
            {
                sb.AppendLine("## Slowest parses");
                sb.AppendLine();
                foreach (var r in slow) sb.AppendLine("- " + r.EngineMs + "ms — " + r.Path + " (" + r.InBytes + " chars)");
                sb.AppendLine();
            }

            sb.AppendLine("Artifacts for every hard failure: `" + Path.Combine(runDir, "fail") + "` · raw results: `results.jsonl`");
            string md = sb.ToString();
            File.WriteAllText(Path.Combine(runDir, "summary.md"), md, new UTF8Encoding(false));
            File.WriteAllText(lf, runId);
            foreach (var r in results) if (r.Flow != "(baseline)") latest[r.Key] = r.Status + "|" + runId;
            File.WriteAllText(latestFile, Util.ToJson(latest));
            return md;
        }

        static int FlowOrder(List<FlowDef> flows, string name)
        {
            if (name == "(parse)") return -1;
            int i = flows.FindIndex(f => f.Name == name);
            return i < 0 ? 999 : i;
        }

        static void AppendIssues(StringBuilder sb, string title, List<ItemResult> items, string severity, int max)
        {
            var groups = items.SelectMany(r => r.Issues.Where(i => i.Severity == severity).Select(i => new { r, i }))
                .GroupBy(x => x.i.Signature).OrderByDescending(g => g.Select(x => x.r.Article).Distinct().Count()).ThenByDescending(g => g.Count()).ToList();
            if (groups.Count == 0) return;
            sb.AppendLine("## " + title + " (" + groups.Count + ")");
            sb.AppendLine();
            int n = 0;
            foreach (var g in groups.Take(max))
            {
                n++;
                int arts = g.Select(x => x.r.Article).Distinct().Count();
                string flows = string.Join(", ", g.GroupBy(x => x.r.Flow).OrderByDescending(f => f.Count()).Select(f => f.Key + "×" + f.Count()).Take(6));
                sb.AppendLine(n + ". **" + Md(g.Key) + "** — " + arts + " article(s), " + g.Count() + " check(s) · " + g.First().i.Kind + " · " + flows);
                foreach (var ex in g.GroupBy(x => x.r.Article).Take(3).Select(x => x.First()))
                {
                    sb.AppendLine("   - `" + ex.r.Path + "` (" + ex.r.Flow + (ex.r.Mode.Length > 0 ? "/" + ex.r.Mode : "") + ")" + (ex.r.Artifact != null ? " → `" + ex.r.Artifact + "`" : ""));
                }
                string detail = (g.First().i.Detail ?? "").Trim();
                if (detail.Length > 0)
                {
                    sb.AppendLine("     ```");
                    foreach (var l in detail.Split('\n').Take(9)) sb.AppendLine("     " + l.TrimEnd());
                    sb.AppendLine("     ```");
                }
            }
            if (groups.Count > max) sb.AppendLine("… " + (groups.Count - max) + " more signatures in results.jsonl");
            sb.AppendLine();
        }

        static string Md(string s) { return (s ?? "").Replace("|", "\\|").Replace("`", "'"); }

        public static int Print(Args a)
        {
            string id = a.Get("run", null);
            if (id == null)
            {
                string lf = LastRunFile(a.Get("tier", "core"));
                if (!File.Exists(lf)) { Console.WriteLine("no runs yet"); return 1; }
                id = File.ReadAllText(lf).Trim();
            }
            string p = Path.Combine(Paths.Out, id, "summary.md");
            Console.WriteLine(File.Exists(p) ? File.ReadAllText(p) : "no summary at " + p);
            return 0;
        }
    }
}
