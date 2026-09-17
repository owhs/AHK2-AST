// AstHost.exe: the AHK2 AST engine as a long-running helper process for AxStudio (and anything else).
// One JSON request per line on stdin, one JSON reply per line on stdout; no windows; exits when stdin closes.
// The protocol is documented in src\host\PROTOCOL.md, the node types in NODES.md.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AstHost
{
    public static class Program
    {
        public const string Version = "1.0";
        public const int DefaultTimeoutMs = 60000;

        [STAThread]
        public static int Main(string[] args)
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var host = new Host();
            string line;
            while ((line = SafeReadLine(stdin)) != null)
            {
                line = line.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;
                string reply = host.HandleLine(line);
                try { stdout.WriteLine(reply); }
                catch (IOException) { break; } // the reader went away
                if (host.ExitRequested) break;
            }
            Host.DeleteTempBins();
            SharedTrees.ReleaseAll();
            return 0;
        }

        static string SafeReadLine(TextReader r)
        {
            try { return r.ReadLine(); }
            catch (IOException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }
    }

    public class Host
    {
        public bool ExitRequested;
        readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };

        // the last parse, reused by consecutive requests on the same text (outline + errors after a parse)
        string _cacheKey;
        ParsedSource _cache;

        /// <summary>One request line in, one reply line out. Never throws: a bad script or request is an error reply.</summary>
        public string HandleLine(string line)
        {
            object id = null;
            Dictionary<string, object> req;
            try
            {
                req = _json.DeserializeObject(line) as Dictionary<string, object>;
                if (req == null) return Error(null, "bad-request", "a request is one JSON object per line");
            }
            catch (Exception ex) { return Error(null, "bad-request", "invalid JSON: " + ex.Message); }
            req.TryGetValue("id", out id);
            string cmd = GetString(req, "cmd") ?? GetString(req, "command");
            if (cmd == null) return Error(id, "bad-request", "missing \"cmd\"");

            int timeout = GetInt(req, "timeoutMs", Program.DefaultTimeoutMs);
            string reply = null;
            Exception failure = null;
            // Each request runs on its own thread with a big stack (the parser recurses on deeply nested code) and a
            // time limit, so one pathological script cannot take the host down or hang it.
            var th = new Thread(() =>
            {
                try { reply = Dispatch(id, cmd, req); }
                catch (ThreadAbortException) { Thread.ResetAbort(); }
                catch (Exception ex) { failure = ex; }
            }, 256 * 1024 * 1024);
            th.IsBackground = true;
            th.Start();
            if (!th.Join(timeout))
            {
                try { th.Abort(); } catch { }
                _cacheKey = null; _cache = null;
                return Error(id, "timeout", "the request took longer than " + timeout + " ms and was stopped");
            }
            if (failure != null) return Error(id, failure is RequestException ? ((RequestException)failure).Code : "internal", failure.Message,
                                             failure is RequestException ? null : failure.ToString());
            return reply ?? Error(id, "internal", "no reply");
        }

        string Dispatch(object id, string cmd, Dictionary<string, object> req)
        {
            switch (cmd.ToLowerInvariant())
            {
                case "ping":
                case "version":
                    return Ok(id, new JObj().Add("version", Program.Version).Add("engine", typeof(AhkAstEngine).Assembly.GetName().Version.ToString()));
                case "exit":
                case "quit":
                    ExitRequested = true;
                    return Ok(id, new JObj());
                case "parse": return Parse(id, req);
                case "outline": return OutlineReply(id, req);
                case "errors": return Errors(id, req);
                case "edit": return Edit(id, req);
                case "flow": return Flow(id, req);
                case "nodeat": return NodeAt(id, req);
                case "release":
                    {
                        string name = GetString(req, "shm");
                        int n = name == null ? SharedTrees.ReleaseAll() : (SharedTrees.Release(name) ? 1 : 0);
                        return Ok(id, new JObj().Add("released", n));
                    }
                default: throw new RequestException("unknown-command", "unknown cmd \"" + cmd + "\" (parse, outline, errors, edit, flow, nodeAt, release, ping, exit)");
            }
        }

        // -- requests ----------------------------------------------------------------------------------------

        string Parse(object id, Dictionary<string, object> req)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var src = Load(req, GetBool(req, "includes", false));
            long parseMs = sw.ElapsedMilliseconds;
            int maxDepth = GetInt(req, "depth", int.MaxValue);
            var files = new FileTable(src.Path);
            string format = (GetString(req, "format") ?? "json").ToLowerInvariant();

            if (format == "bin")
            {
                // where the AXT1 bytes go: a file (default), named shared memory, or inline base64 — the last two never touch disk
                string to = (GetString(req, "to") ?? "file").ToLowerInvariant();
                sw.Restart();
                int nodes;
                byte[] bytes = TreeBin.Build(src.Tree, files, maxDepth, out nodes);
                long buildMs = sw.ElapsedMilliseconds;
                sw.Restart();
                var o = new JObj();
                if (to == "file")
                {
                    string outPath = GetString(req, "out");
                    outPath = outPath != null ? Path.GetFullPath(outPath) : TempBinPath();
                    try
                    {
                        string dir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllBytes(outPath, bytes); // one sequential write; replaces an existing file
                    }
                    catch (Exception ex) { throw new RequestException("io", "cannot write " + outPath + ": " + ex.Message); }
                    o.Add("bin", outPath);
                }
                else if (to == "shm") o.Add("shm", SharedTrees.Publish(bytes, GetString(req, "name")));
                else if (to == "base64") o.Add("base64", Convert.ToBase64String(bytes));
                else throw new RequestException("bad-request", "to must be file, shm or base64");
                long writeMs = sw.ElapsedMilliseconds;
                var probs = SourceEdit.Problems(src.Tree).Select(p => (object)ProblemJson(p, files)).ToList();
                return Ok(id, o.Add("nodes", nodes).Add("bytes", bytes.Length)
                    .Add("files", files.Paths).Add("problems", probs).Add("parseMs", parseMs).Add("buildMs", buildMs).Add("writeMs", writeMs));
            }
            if (format != "json") throw new RequestException("bad-request", "format must be json or bin");

            var sb = new StringBuilder(src.Text.Length * 4 + 256);
            sb.Append("{\"id\":"); Json.Value(sb, id);
            sb.Append(",\"ok\":true,\"files\":");
            var tree = new StringBuilder(src.Text.Length * 4);
            int count = TreeJson.Write(tree, src.Tree, 0, files, maxDepth);
            Json.Value(sb, files.Paths);
            sb.Append(",\"nodeCount\":").Append(count);
            sb.Append(",\"problems\":");
            Json.Value(sb, SourceEdit.Problems(src.Tree).Select(p => (object)ProblemJson(p, files)).ToList());
            sb.Append(",\"parseMs\":").Append(parseMs);
            sb.Append(",\"tree\":").Append(tree);
            sb.Append('}');
            return sb.ToString();
        }

        // Temp .axt files this host named: one per request (a reader may still be using the previous one), removed on exit.
        static readonly List<string> TempBins = new List<string>();
        static int _tempCounter;

        static string TempBinPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "AstHost");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "parse-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Interlocked.Increment(ref _tempCounter) + ".axt");
            lock (TempBins) TempBins.Add(p);
            return p;
        }

        public static void DeleteTempBins()
        {
            lock (TempBins)
            {
                foreach (var p in TempBins) { try { File.Delete(p); } catch { } }
                TempBins.Clear();
            }
        }

        string OutlineReply(object id, Dictionary<string, object> req)
        {
            var src = Load(req, GetBool(req, "includes", false));
            var files = new FileTable(src.Path);
            var o = Outline.Build(src.Tree, src.Text, files);
            o.Add("files", files.Paths);
            return Ok(id, o);
        }

        string Errors(object id, Dictionary<string, object> req)
        {
            var src = Load(req, GetBool(req, "includes", false));
            var files = new FileTable(src.Path);
            var list = new List<object>();
            foreach (var p in SourceEdit.Problems(src.Tree)) list.Add(ProblemJson(p, files));
            return Ok(id, new JObj().Add("files", files.Paths).Add("problems", list));
        }

        string Edit(object id, Dictionary<string, object> req)
        {
            string text = GetString(req, "text");
            if (text == null)
            {
                string path = GetString(req, "path");
                if (path == null) throw new RequestException("bad-request", "edit needs \"text\" (or \"path\")");
                text = ReadFile(path);
            }
            string op = (GetString(req, "op") ?? "").ToLowerInvariant();
            string newText = GetString(req, "newText") ?? "";
            var map = new SourceLineMap(text);

            EditResult r;
            object rangeObj;
            if (!req.TryGetValue("nodeRange", out rangeObj) && !req.TryGetValue("range", out rangeObj))
                throw new RequestException("bad-request", "edit needs \"nodeRange\": {\"start\":…, \"end\":…}");
            int start, end;
            ReadRange(rangeObj, map, out start, out end);

            if (op == "replacerange")
            {
                r = SourceEdit.ReplaceRange(text, start, end, newText);
            }
            else
            {
                var tree = new AhkAstEngine().Parse(text);
                var node = SourceEdit.FindByRange(tree, start, end, GetString(req, "nodeType"));
                if (node == null)
                    throw new RequestException("no-node", "no node has exactly the range " + start + ".." + end + (GetString(req, "nodeType") != null ? " and type " + GetString(req, "nodeType") : "") + " in this text");
                switch (op)
                {
                    case "replace": r = SourceEdit.ReplaceNodeText(text, node, newText); break;
                    case "insertbefore": r = SourceEdit.InsertBefore(text, node, newText); break;
                    case "insertafter": r = SourceEdit.InsertAfter(text, node, newText); break;
                    case "delete": r = SourceEdit.DeleteNode(text, node); break;
                    default: throw new RequestException("bad-request", "op must be replace, insertBefore, insertAfter, delete or replaceRange");
                }
            }
            if (r.Error != null) throw new RequestException("edit-failed", r.Error);
            var newMap = new SourceLineMap(r.Text);
            var files = new FileTable(null);
            var o = new JObj()
                .Add("text", r.Text)
                .Add("changed", r.Changed)
                .Add("written", RangeJson(r.StartOffset, r.EndOffset, newMap))
                .Add("problems", r.Problems.Select(p => (object)ProblemJson(p, files)).ToList())
                .Add("newProblems", r.NewProblems.Select(p => (object)ProblemJson(p, files)).ToList());
            return Ok(id, o);
        }

        string Flow(object id, Dictionary<string, object> req)
        {
            string path = GetString(req, "path");
            string text = GetString(req, "text");
            if (text == null)
            {
                if (path == null) throw new RequestException("bad-request", "flow needs \"path\" (or \"text\")");
                text = ReadFile(path);
            }
            object flowObj;
            if (!req.TryGetValue("flowJson", out flowObj) && !req.TryGetValue("flow", out flowObj))
                throw new RequestException("bad-request", "flow needs \"flowJson\" (a flow definition, as an object or a JSON string)");
            string flowJson = flowObj as string ?? _json.Serialize(flowObj);
            bool follow = GetBool(req, "includes", path != null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string output = new AhkAstEngine().ExecuteFlow(text, flowJson, false, path != null ? Path.GetFullPath(path) : null, follow);
            sw.Stop();
            var o = new JObj().Add("output", output).Add("ms", sw.ElapsedMilliseconds);
            if (GetBool(req, "log", false)) o.Add("log", PipelineLogger.Logs.ToList());
            return Ok(id, o);
        }

        /// <summary>The nodes at a position, outermost first (without their children).</summary>
        string NodeAt(object id, Dictionary<string, object> req)
        {
            var src = Load(req, false);
            var map = new SourceLineMap(src.Text);
            int offset = GetInt(req, "offset", -1);
            if (offset < 0)
            {
                int line = GetInt(req, "line", 0), col = GetInt(req, "col", 0);
                if (line < 1) throw new RequestException("bad-request", "nodeAt needs \"offset\" or \"line\" + \"col\"");
                offset = map.ToOffset(line, Math.Max(1, col));
            }
            var chain = new List<AstNode>();
            var n = src.Tree;
            while (n != null)
            {
                chain.Add(n);
                AstNode next = null;
                for (int i = 0; i < n.ChildCount; i++)
                {
                    var c = n.GetChild(i);
                    if (c == null) continue;
                    if (c.HasRange && c.StartOffset <= offset && offset < Math.Max(c.EndOffset, c.StartOffset + 1) && !SourceRanges.IsAttachedComment(c)) { next = c; break; }
                }
                n = next;
            }
            var files = new FileTable(src.Path);
            var list = new List<object>();
            foreach (var c in chain)
            {
                var sb = new StringBuilder();
                TreeJson.Write(sb, c, 0, files, 0);
                list.Add(new Json.Raw(sb.ToString()));
            }
            return Ok(id, new JObj().Add("offset", offset).Add("nodes", list));
        }

        // -- helpers -----------------------------------------------------------------------------------------

        class ParsedSource
        {
            public string Path, Text;
            public AstNode Tree;
        }

        ParsedSource Load(Dictionary<string, object> req, bool includes)
        {
            string path = GetString(req, "path");
            string text = GetString(req, "text");
            if (path != null) path = System.IO.Path.GetFullPath(path);
            if (text == null)
            {
                if (path == null) throw new RequestException("bad-request", "needs \"path\" or \"text\"");
                text = ReadFile(path);
            }
            string key = (path ?? "") + "|" + includes + "|" + text.Length + "|" + text.GetHashCode();
            if (key == _cacheKey && _cache != null && _cache.Text == text) return _cache;
            var engine = new AhkAstEngine();
            AstNode tree = includes && path != null ? engine.ParseSourceWithIncludes(text, path, false) : engine.Parse(text);
            _cache = new ParsedSource { Path = path, Text = text, Tree = tree };
            _cacheKey = includes ? null : key; // include files can change on disk: only cache single-file parses
            return _cache;
        }

        static string ReadFile(string path)
        {
            try { return File.ReadAllText(path); } // UTF-8 unless the file has another BOM, as AutoHotkey reads it
            catch (Exception ex) { throw new RequestException("io", "cannot read " + path + ": " + ex.Message); }
        }

        /// <summary>{"start": n, "end": n} with offsets, or start/end objects as the parse reply gives them ({offset} or {line, col}).</summary>
        static void ReadRange(object o, SourceLineMap map, out int start, out int end)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) throw new RequestException("bad-request", "nodeRange must be an object {\"start\":…, \"end\":…}");
            object s, e;
            d.TryGetValue("start", out s);
            d.TryGetValue("end", out e);
            if (s == null) d.TryGetValue("startOffset", out s);
            if (e == null) d.TryGetValue("endOffset", out e);
            start = Position(s, map);
            end = Position(e, map);
            if (start < 0 || end < start) throw new RequestException("bad-request", "nodeRange start/end are missing or reversed");
        }

        static int Position(object o, SourceLineMap map)
        {
            if (o == null) return -1;
            if (o is int || o is long || o is decimal || o is double) return Convert.ToInt32(o);
            var d = o as Dictionary<string, object>;
            if (d == null) return -1;
            object v;
            if (d.TryGetValue("offset", out v) && v != null) return Convert.ToInt32(v);
            object l, c;
            if (d.TryGetValue("line", out l) && d.TryGetValue("col", out c)) return map.ToOffset(Convert.ToInt32(l), Convert.ToInt32(c));
            return -1;
        }

        public static JObj RangeJson(int start, int end, SourceLineMap map)
        {
            int sl, sc, el, ec;
            map.ToLineCol(start, out sl, out sc);
            map.ToLineCol(end, out el, out ec);
            return new JObj()
                .Add("start", new JObj().Add("line", sl).Add("col", sc).Add("offset", start))
                .Add("end", new JObj().Add("line", el).Add("col", ec).Add("offset", end));
        }

        static JObj ProblemJson(SourceProblem p, FileTable files)
        {
            var o = new JObj().Add("kind", p.Kind).Add("message", p.Message);
            if (p.StartOffset >= 0)
            {
                o.Add("start", new JObj().Add("line", p.StartLine).Add("col", p.StartColumn).Add("offset", p.StartOffset));
                o.Add("end", new JObj().Add("line", p.EndLine).Add("col", p.EndColumn).Add("offset", p.EndOffset));
            }
            else { o.Add("start", null); o.Add("end", null); }
            o.Add("file", files.Index(p.File));
            return o;
        }

        static string Ok(object id, JObj body)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":"); Json.Value(sb, id);
            sb.Append(",\"ok\":true");
            foreach (DictionaryEntry e in body)
            {
                sb.Append(',');
                Json.Str(sb, (string)e.Key);
                sb.Append(':');
                Json.Value(sb, e.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        static string Error(object id, string code, string message, string detail = null)
        {
            var err = new JObj().Add("code", code).Add("message", message);
            if (detail != null) err.Add("detail", detail);
            return Json.Serialize(new JObj().Add("id", id).Add("ok", false).Add("error", err));
        }

        static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToInt32(v); } catch { return def; }
        }

        static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v).ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }
    }

    public class RequestException : Exception
    {
        public readonly string Code;
        public RequestException(string code, string message) : base(message) { Code = code; }
    }

    /// <summary>The files a reply mentions: index 0 is the parsed text (its path, or null for text without one).</summary>
    public class FileTable
    {
        public readonly List<string> Paths = new List<string>();

        public FileTable(string mainPath) { Paths.Add(mainPath); }

        public int Index(string path)
        {
            if (path == null) return 0;
            for (int i = 0; i < Paths.Count; i++) if (string.Equals(Paths[i], path, StringComparison.OrdinalIgnoreCase)) return i;
            Paths.Add(path);
            return Paths.Count - 1;
        }
    }
}
