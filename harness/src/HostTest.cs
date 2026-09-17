using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AstHarness
{
    /// <summary>
    /// `host <AstHost.exe>`: runs the helper process the way AxStudio will (pipes, no window) and checks every request,
    /// the reply shapes, that bad input gives an error reply instead of killing the host, and that it exits when stdin
    /// closes. Engine-only: no AutoHotkey process is started.
    /// </summary>
    public static class HostTest
    {
        static int _fail, _pass;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000000 }; // deep trees (run on a 512 MB stack)

        static void Check(bool cond, string what, string detail = null)
        {
            Console.WriteLine((cond ? "  PASS  " : "  FAIL  ") + what + (cond || detail == null ? "" : "\n          " + Util.Clip(detail, 1500)));
            if (cond) _pass++; else _fail++;
        }

        class Proc : IDisposable
        {
            public Process P;
            int _id;
            public Proc(string exe)
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false)
                };
                P = Process.Start(psi);
                P.StandardInput.AutoFlush = true;
            }

            /// <summary>Sends a request object (an "id" is added) and returns the parsed reply and its raw line.</summary>
            public Dictionary<string, object> Ask(Dictionary<string, object> req, out string raw)
            {
                req["id"] = ++_id;
                return AskRaw(Json.Serialize(req), out raw);
            }

            public Dictionary<string, object> AskRaw(string line, out string raw)
            {
                var sw = Stopwatch.StartNew();
                P.StandardInput.WriteLine(line);
                raw = P.StandardOutput.ReadLine();
                LastMs = sw.ElapsedMilliseconds;
                if (raw == null) return null;
                return Json.DeserializeObject(raw) as Dictionary<string, object>;
            }

            public long LastMs;

            public void Dispose()
            {
                try { if (!P.HasExited) { P.StandardInput.Close(); if (!P.WaitForExit(5000)) P.Kill(); } } catch { }
            }
        }

        static Dictionary<string, object> Req(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static bool Ok(Dictionary<string, object> r) { return r != null && r.ContainsKey("ok") && true.Equals(r["ok"]); }
        static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        static object[] A(object o) { var a = o as System.Collections.ArrayList; return a != null ? a.ToArray() : (o as object[] ?? new object[0]); }
        static int I(object o) { return o == null ? -1 : Convert.ToInt32(o); }
        static string S(object o) { return o == null ? null : Convert.ToString(o); }

        static IEnumerable<Dictionary<string, object>> Nodes(Dictionary<string, object> n)
        {
            yield return n;
            if (n.ContainsKey("children")) foreach (var c in A(n["children"])) foreach (var x in Nodes(D(c))) yield return x;
        }

        public static int Run(Args a)
        {
            if (a.Positional.Count == 0 || !File.Exists(a.Positional[0])) { Console.WriteLine("usage: host <path to AstHost.exe>"); return 2; }
            string exe = Path.GetFullPath(a.Positional[0]);
            if (a.Has("corpus")) return Corpus(exe, a);
            _fail = _pass = 0;
            string dir = Path.Combine(Paths.Temp, "hosttest");
            Util.SafeDeleteTree(dir);
            Directory.CreateDirectory(dir);
            string raw;

            using (var h = new Proc(exe))
            {
                // ping
                var r = h.Ask(Req("cmd", "ping"), out raw);
                Check(Ok(r) && S(r["version"]) != null, "ping: replies with a version", raw);

                // parse: ranges, files, flags
                string src = "x := 1\r\nclass Foo extends Bar {\r\n    __New(a, b := 2) {\r\n        this.a := a\r\n    }\r\n}\r\nMsgBox \"hi\", \"t\"\r\n";
                r = h.Ask(Req("cmd", "parse", "text", src), out raw);
                var tree = Ok(r) ? D(r["tree"]) : null;
                Check(tree != null && S(tree["type"]) == "Program" && I(r["nodeCount"]) > 10, "parse text: a Program tree", raw);
                if (tree != null)
                {
                    bool allRanged = true, textOk = true; string bad = null;
                    foreach (var n in Nodes(tree))
                    {
                        var s = D(n["start"]); var e = D(n["end"]);
                        if (s == null || e == null) { allRanged = false; bad = S(n["type"]); continue; }
                        int so = I(s["offset"]), eo = I(e["offset"]);
                        if (so < 0 || eo < so || eo > src.Length) { textOk = false; bad = S(n["type"]) + " " + so + ".." + eo; }
                    }
                    Check(allRanged && textOk, "parse text: every node has start/end {line,col,offset} inside the text", bad);
                    var cls = Nodes(tree).FirstOrDefault(n => S(n["type"]) == "Class");
                    Check(cls != null && Slice(src, cls) == src.Substring(src.IndexOf("class"), src.IndexOf("}\r\nMsgBox") + 1 - src.IndexOf("class")),
                        "parse text: the class's range is exactly its text", cls == null ? "no class" : Slice(src, cls));
                    var call = Nodes(tree).FirstOrDefault(n => S(n["type"]) == "Call" && n.ContainsKey("flags") && D(n["flags"]).ContainsKey("command"));
                    Check(call != null && Slice(src, call) == "MsgBox \"hi\", \"t\"", "parse text: command call flagged, range covers its arguments", call == null ? raw : Slice(src, call));
                    var assign = Nodes(tree).FirstOrDefault(n => S(n["type"]) == "BinaryExpr" && S(n["value"]) == ":=");
                    Check(assign != null && D(assign["flags"]) != null && true.Equals(D(assign["flags"])["assign"]), "parse text: assignments are BinaryExpr \":=\" flagged assign", raw);
                    var p = Nodes(tree).FirstOrDefault(n => S(n["type"]) == "Parameter" && S(n["value"]) == "b");
                    Check(p != null && Slice(src, p) == "b := 2", "parse text: a parameter's range includes its default", p == null ? "none" : Slice(src, p));
                }

                // parse a file with includes: the included nodes carry the include's file index
                string proj = Path.Combine(dir, "proj");
                Directory.CreateDirectory(Path.Combine(proj, "a"));
                Directory.CreateDirectory(Path.Combine(proj, "b"));
                File.WriteAllText(Path.Combine(proj, @"a\JSON.ahk"), "A_Json() => 1\r\n");
                File.WriteAllText(Path.Combine(proj, @"b\JSON.ahk"), "B_Json() => 2\r\n");
                string mainPath = Path.Combine(proj, "main.ahk");
                string mainSrc = "#Include a\\JSON.ahk\r\n#Include b\\JSON.ahk\r\nx := A_Json() + B_Json()\r\n";
                File.WriteAllText(mainPath, mainSrc);
                r = h.Ask(Req("cmd", "parse", "path", mainPath, "includes", true), out raw);
                tree = Ok(r) ? D(r["tree"]) : null;
                var files = Ok(r) ? A(r["files"]).Select(S).ToList() : new List<string>();
                Check(files.Count == 3, "parse path+includes: two different JSON.ahk files are two includes (full paths)", raw);
                if (tree != null && files.Count == 3)
                {
                    var fn = Nodes(tree).FirstOrDefault(n => S(n["type"]) == "Method" && S(n["value"]) == "B_Json");
                    int fi = fn != null ? I(fn["file"]) : -1;
                    Check(fn != null && fi > 0 && Slice(File.ReadAllText(files[fi]), fn) == "B_Json() => 2", "parse path+includes: an included node's file + range point into that file", raw);
                    var inc = Nodes(tree).Where(n => S(n["type"]) == "Include").ToList();
                    Check(inc.Count == 2 && inc.All(n => I(n["file"]) == 0 && Slice(mainSrc, n).StartsWith("#Include")), "parse path+includes: an Include node's own range is its #Include line", raw);
                }

                // outline
                string osrc = "#Requires AutoHotkey v2.0\r\ncounter := 0\r\nglobal cfg := Map()\r\nAdd(a, b := 1, args*) {\r\n    return a + b\r\n}\r\nclass Widget extends Gui {\r\n    static count := 0\r\n    __New(title) {\r\n    }\r\n    Name {\r\n        get => this._n\r\n        set => this._n := value\r\n    }\r\n    class Inner {\r\n    }\r\n}\r\n^!t::MsgBox \"x\"\r\n::btw::by the way\r\nStart:\r\n";
                r = h.Ask(Req("cmd", "outline", "text", osrc), out raw);
                if (Ok(r))
                {
                    var fns = A(r["functions"]).Select(D).ToList();
                    var classes = A(r["classes"]).Select(D).ToList();
                    Check(fns.Count == 1 && S(fns[0]["name"]) == "Add" && A(fns[0]["params"]).Length == 3 && true.Equals(D(A(fns[0]["params"])[2])["variadic"])
                          && S(D(A(fns[0]["params"])[1])["default"]) == "1", "outline: function with params (default, variadic)", raw);
                    var w = classes.FirstOrDefault();
                    var members = w != null ? A(w["members"]).Select(D).ToList() : new List<Dictionary<string, object>>();
                    Check(w != null && S(w["name"]) == "Widget" && S(w["extends"]) == "Gui" && members.Any(m => S(m["name"]) == "__New" && A(m["params"]).Length == 1)
                          && members.Any(m => S(m["kind"]) == "property" && A(m["accessors"]).Length == 2) && members.Any(m => S(m["kind"]) == "class" && S(m["name"]) == "Inner")
                          && members.Any(m => S(m["kind"]) == "var" && true.Equals(m["static"])), "outline: class with extends, __New params, property accessors, static var, nested class", raw);
                    Check(A(r["hotkeys"]).Length == 1 && S(D(A(r["hotkeys"])[0])["trigger"]) == "^!t", "outline: hotkey", raw);
                    var hs = A(r["hotstrings"]).Select(D).FirstOrDefault();
                    Check(hs != null && S(hs["trigger"]) == "btw" && S(hs["replacement"]) == "by the way", "outline: hotstring trigger + replacement", raw);
                    var globals = A(r["globals"]).Select(D).Select(g => S(g["name"])).ToList();
                    Check(globals.Contains("counter") && globals.Contains("cfg"), "outline: globals (assignments and global declarations)", raw);
                    Check(A(r["labels"]).Length == 1, "outline: labels", raw);
                }
                else Check(false, "outline: replies ok", raw);

                // errors with real ranges
                string broken = "x := 1\r\ny := (2 +\r\nz := 3\r\n";
                r = h.Ask(Req("cmd", "errors", "text", broken), out raw);
                var probs = Ok(r) ? A(r["problems"]).Select(D).ToList() : new List<Dictionary<string, object>>();
                Check(probs.Count > 0 && probs.All(p => p["start"] != null && I(D(p["start"])["line"]) >= 1), "errors: problems have real ranges (not 0:0)", raw);

                // edits: replace / insertAfter / insertBefore / delete, ranges given as in the parse reply
                string esrc = "a := 1\r\nif a {\r\n    MsgBox \"one\"  ; say it\r\n}\r\nb := [1,\r\n      2]\r\n";
                r = h.Ask(Req("cmd", "parse", "text", esrc), out raw);
                tree = D(r["tree"]);
                var msg = Nodes(tree).First(n => S(n["type"]) == "Call");
                var str = Nodes(tree).First(n => S(n["type"]) == "String");
                var range = Req("start", msg["start"], "end", msg["end"]);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "replace", "nodeRange", Req("start", str["start"], "end", str["end"]), "newText", "\"two\""), out raw);
                Check(Ok(r) && S(r["text"]) == esrc.Replace("\"one\"", "\"two\"") && A(r["newProblems"]).Length == 0, "edit replace: only the node's text changes", raw);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "insertAfter", "nodeRange", range, "newText", "x := 2"), out raw);
                Check(Ok(r) && S(r["text"]) == esrc.Replace("; say it\r\n", "; say it\r\n    x := 2\r\n"), "edit insertAfter: a new line with the statement's indentation, after its comment", raw);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "insertBefore", "nodeRange", range, "newText", "y := 0\n z()"), out raw);
                Check(Ok(r) && S(r["text"]) == esrc.Replace("    MsgBox", "    y := 0\r\n     z()\r\n    MsgBox"), "edit insertBefore: lines re-indented, the file's line breaks", raw);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "delete", "nodeRange", range), out raw);
                Check(Ok(r) && S(r["text"]) == esrc.Replace("    MsgBox \"one\"  ; say it\r\n", ""), "edit delete: the statement's line and its comment go", raw);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "replace", "nodeRange", range, "newText", Slice(esrc, msg)), out raw);
                Check(Ok(r) && S(r["text"]) == esrc && false.Equals(r["changed"]), "edit: a no-op edit returns the text byte for byte", raw);
                var arr2 = Nodes(tree).First(n => S(n["type"]) == "Number" && S(n["value"]) == "2");
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "delete", "nodeRange", Req("start", arr2["start"], "end", arr2["end"])), out raw);
                Check(Ok(r) && S(r["text"]) == esrc.Replace("[1,\r\n      2]", "[1]"), "edit delete: a list item goes with its comma", raw);
                r = h.Ask(Req("cmd", "edit", "text", esrc, "op", "replace", "nodeRange", Req("start", str["start"], "end", str["end"]), "newText", "(\"x\""), out raw);
                Check(Ok(r) && A(r["newProblems"]).Length > 0, "edit: a breaking edit reports the new problems", raw);

                // flow (the studio minifies on build)
                var flow = FlowDef.Load(Paths.Flows, "minify-aggressive").FirstOrDefault();
                if (flow != null)
                {
                    string fdir = Path.Combine(dir, "flow");
                    r = h.Ask(Req("cmd", "flow", "path", mainPath, "flowJson", flow.EngineJson(fdir, flow.StepCount)), out raw);
                    Check(Ok(r) && S(r["output"]).Contains("=>") && !S(r["output"]).Contains("#Include"), "flow: minify-aggressive output with the includes inlined", raw);
                }

                // nodeAt
                r = h.Ask(Req("cmd", "nodeAt", "text", esrc, "line", 3, "col", 13), out raw);
                var chain = Ok(r) ? A(r["nodes"]).Select(D).Select(n => S(n["type"])).ToList() : new List<string>();
                Check(chain.Count > 2 && chain[0] == "Program" && chain.Last() == "String", "nodeAt: outermost → innermost at a position", raw);

                // crash safety: garbage, unknown commands, a pathological script, a timeout — the host stays up
                r = h.AskRaw("{not json", out raw);
                Check(r != null && !Ok(r) && S(D(r["error"])["code"]) == "bad-request", "bad JSON: an error reply", raw);
                r = h.Ask(Req("cmd", "frobnicate"), out raw);
                Check(r != null && !Ok(r) && S(D(r["error"])["code"]) == "unknown-command", "unknown cmd: an error reply", raw);
                r = h.Ask(Req("cmd", "parse", "path", Path.Combine(dir, "missing.ahk")), out raw);
                Check(r != null && !Ok(r) && S(D(r["error"])["code"]) == "io", "missing file: an error reply", raw);
                string deep = "x := " + new string('(', 3000) + "1" + new string(')', 3000) + "\r\n" + string.Concat(Enumerable.Repeat("if a {\r\n", 400)) + string.Concat(Enumerable.Repeat("}\r\n", 400));
                r = h.Ask(Req("cmd", "parse", "text", deep, "depth", 2), out raw);
                Check(r != null, "deeply nested script: a reply (" + (Ok(r) ? "ok" : "error") + "), host alive", raw == null ? "no reply" : Util.Clip(raw, 300));
                string big = string.Concat(Enumerable.Repeat("f(a, b) {\r\n  return a + b * (c - d)\r\n}\r\n", 20000));
                r = h.Ask(Req("cmd", "parse", "text", big, "timeoutMs", 1), out raw);
                Check(r != null && !Ok(r) && S(D(r["error"])["code"]) == "timeout", "timeout: a long request is stopped with an error reply", raw == null ? "no reply" : Util.Clip(raw, 300));
                r = h.Ask(Req("cmd", "ping"), out raw);
                Check(Ok(r), "after all that: the host still answers", raw);

                // speed: a large file
                r = h.Ask(Req("cmd", "outline", "text", big), out raw);
                Check(Ok(r), "outline of a 60,000-line text in " + h.LastMs + " ms", raw == null ? null : Util.Clip(raw, 200));

                // binary parse output (AXT1): the same tree as JSON, over a file, shared memory and base64
                string why;
                Check(SameForms(h, Req("cmd", "parse", "text", src), "file", dir, out why), "bin (file): same tree as the JSON parse", why);
                Check(SameForms(h, Req("cmd", "parse", "path", mainPath, "includes", true), "shm", dir, out why), "bin (shared memory) with includes: same tree + file table", why);
                Check(SameForms(h, Req("cmd", "parse", "text", osrc), "base64", dir, out why), "bin (base64): same tree", why);
                Check(SameForms(h, Req("cmd", "parse", "text", osrc, "depth", 2), "file", dir, out why), "bin depth 2: cut-off nodes flagged truncated with their real childCount", why);
                foreach (var cf in Directory.GetFiles(Paths.Cases, "*.ahk"))
                    if (!SameForms(h, Req("cmd", "parse", "path", cf, "includes", true), "file", dir, out why)) { Check(false, "bin = JSON for " + Path.GetFileName(cf), why); break; }
                Check(true, "bin = JSON for every harness\\cases file (with includes)");
                string outFile = Path.Combine(dir, "given.axt");
                File.WriteAllText(outFile, "old contents");
                r = h.Ask(Req("cmd", "parse", "text", src, "format", "bin", "out", outFile), out raw);
                Check(Ok(r) && S(r["bin"]) == outFile && new FileInfo(outFile).Length == I(r["bytes"]) && A(r["problems"]).Length == 0,
                    "bin to a given \"out\": an existing file is replaced", raw);
                r = h.Ask(Req("cmd", "parse", "text", src, "format", "bin", "to", "shm"), out raw);
                string shm = Ok(r) ? S(r["shm"]) : null;
                var r2 = h.Ask(Req("cmd", "release", "shm", shm), out raw);
                bool gone = false;
                try { using (System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(shm)) { } } catch (FileNotFoundException) { gone = true; }
                Check(shm != null && Ok(r2) && I(r2["released"]) == 1 && gone, "release: the shared memory is gone afterwards", raw);

                // timings the studio asked for
                string showcase = @"C:\Users\o\Downloads\AHK2-ActiveX-Gui\example\Showcase.ahk";
                if (File.Exists(showcase)) Timing(h, Req("cmd", "parse", "path", showcase, "includes", true), "Showcase.ahk (full tree, includes)");
                Timing(h, Req("cmd", "parse", "text", big), "60,000-line text");

                // exits when stdin closes
                h.P.StandardInput.Close();
                Check(h.P.WaitForExit(5000), "exits when stdin closes");
                string err = h.P.StandardError.ReadToEnd();
                Check(err.Length == 0, "nothing on stderr", err);
            }
            Util.SafeDeleteTree(dir);
            Console.WriteLine(_fail == 0 ? "HOST OK (" + _pass + " checks)" : "HOST FAILED (" + _fail + " of " + (_pass + _fail) + ")");
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>Parses the same request as JSON and as AXT1 (via <paramref name="to"/>) and compares node for node.</summary>
        static bool SameForms(Proc h, Dictionary<string, object> req, string to, string dir, out string why)
        {
            string raw;
            var j = h.Ask(new Dictionary<string, object>(req), out raw);
            if (!Ok(j)) { why = "json: " + Util.Clip(raw, 300); return false; }
            var breq = new Dictionary<string, object>(req);
            breq["format"] = "bin";
            breq["to"] = to;
            var b = h.Ask(breq, out raw);
            if (!Ok(b)) { why = "bin: " + Util.Clip(raw, 300); return false; }
            byte[] bytes;
            if (to == "file") bytes = File.ReadAllBytes(S(b["bin"]));
            else if (to == "base64") bytes = Convert.FromBase64String(S(b["base64"]));
            else
            {
                // what AutoHotkey does with OpenFileMapping + MapViewOfFile
                using (var m = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(S(b["shm"])))
                using (var v = m.CreateViewStream(0, I(b["bytes"]), System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read))
                {
                    bytes = new byte[I(b["bytes"])];
                    int got = 0;
                    while (got < bytes.Length) { int k = v.Read(bytes, got, bytes.Length - got); if (k <= 0) break; got += k; }
                }
                h.Ask(Req("cmd", "release", "shm", S(b["shm"])), out raw);
            }
            if (bytes.Length != I(b["bytes"])) { why = "bytes " + bytes.Length + " vs reply " + b["bytes"]; return false; }
            AxtReader.Tree t;
            try { t = AxtReader.Read(bytes); } catch (Exception ex) { why = "reader: " + ex.Message; return false; }
            if (t.NodeCount != I(j["nodeCount"]) || t.NodeCount != I(b["nodes"])) { why = "node count json " + j["nodeCount"] + " bin " + t.NodeCount + " reply " + b["nodes"]; return false; }
            var jf = A(j["files"]).Select(S).ToList();
            if (string.Join("|", jf) != string.Join("|", t.Files) || string.Join("|", jf) != string.Join("|", A(b["files"]).Select(S))) { why = "files differ"; return false; }
            if (A(j["problems"]).Length != A(b["problems"]).Length) { why = "problems differ"; return false; }
            why = AxtReader.Diff(D(j["tree"]), t.Root, "");
            return why == null;
        }

        static void Timing(Proc h, Dictionary<string, object> req, string what)
        {
            string raw;
            var b = new Dictionary<string, object>(req);
            b["format"] = "bin";
            h.Ask(b, out raw); // warm (and fills the include cache)
            // a different text each time, so the host's last-parse cache cannot answer
            if (b.ContainsKey("text")) b["text"] = (string)b["text"] + "\r\n; " + DateTime.Now.Ticks;
            else { b["text"] = File.ReadAllText(S(b["path"])) + "\r\n; " + DateTime.Now.Ticks; }
            var r = h.Ask(new Dictionary<string, object>(b), out raw);
            long binMs = h.LastMs;
            var js = h.Ask(new Dictionary<string, object>(req), out raw);
            long jsonMs = h.LastMs;
            if (!Ok(r)) { Check(false, "timing " + what, raw); return; }
            Check(true, string.Format("{0}: {1:N0} nodes; bin {2:N0} KB: parse {3} ms + build {4} ms + write {5} ms ({6} ms round trip) | JSON {7:N0} KB in {8} ms",
                what, I(r["nodes"]), I(r["bytes"]) / 1024, r["parseMs"], r["buildMs"], r["writeMs"], binMs, raw.Length / 1024, jsonMs));
        }

        /// <summary>`host <exe> --corpus`: bin = JSON for every corpus file (with includes) and the cases.</summary>
        public static int Corpus(string exe, Args a)
        {
            // deep trees: the JSON reader and the comparison recurse
            int code = 1;
            var th = new System.Threading.Thread(() => { code = CorpusOn(exe, a); }, 512 * 1024 * 1024);
            th.Start();
            th.Join();
            return code;
        }

        static int CorpusOn(string exe, Args a)
        {
            var files = new List<string>(Directory.GetFiles(Paths.Cases, "*.ahk"));
            files.AddRange(AstHarness.Corpus.Select(AstHarness.Corpus.Load(), a).Select(x => x.Path).Where(File.Exists));
            string dir = Path.Combine(Paths.Temp, "hosttest-corpus");
            Directory.CreateDirectory(dir);
            int ok = 0, bad = 0, errors = 0;
            using (var h = new Proc(exe))
            {
                int done = 0;
                foreach (var f in files)
                {
                    string why;
                    bool same;
                    if (++done % 100 == 0) Console.WriteLine("  " + done + " / " + files.Count);
                    try { same = SameForms(h, Req("cmd", "parse", "path", f, "includes", true), "file", dir, out why); }
                    catch (Exception ex) { same = false; why = "harness: " + ex.GetType().Name + " " + ex.Message; }
                    if (h.P.HasExited) { Console.WriteLine("  HOST DIED on " + f); return 1; }
                    if (same) ok++;
                    else if (why != null && (why.StartsWith("json:") || why.StartsWith("bin:"))) errors++;
                    else { bad++; if (bad <= 15) Console.WriteLine("  DIFF  " + f + "\n          " + Util.Clip(why, 600)); }
                }
            }
            Util.SafeDeleteTree(dir);
            Console.WriteLine(files.Count + " files: " + ok + " identical, " + bad + " differ, " + errors + " error replies (same for both forms)");
            Console.WriteLine(bad == 0 ? "BIN = JSON" : "BIN DIFFERS");
            return bad == 0 ? 0 : 1;
        }

        static string Slice(string text, Dictionary<string, object> node)
        {
            var s = D(node["start"]); var e = D(node["end"]);
            if (s == null || e == null) return null;
            int so = I(s["offset"]), eo = I(e["offset"]);
            return so >= 0 && eo >= so && eo <= text.Length ? text.Substring(so, eo - so) : null;
        }
    }
}
