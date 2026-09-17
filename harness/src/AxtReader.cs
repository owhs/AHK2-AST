using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AstHarness
{
    /// <summary>
    /// Reads an AXT1 tree (AstHost `parse` with "format":"bin", PROTOCOL.md) into the same shape the JSON parse reply
    /// has, so the two forms can be compared node for node. Written from the documented layout only (offsets from the
    /// header), like the AutoHotkey reader.
    /// </summary>
    public static class AxtReader
    {
        const uint None = 0xFFFFFFFF;

        public class Tree
        {
            public int NodeCount, RecordSize;
            public List<string> Types = new List<string>(), Flags = new List<string>(), Files = new List<string>();
            public Dictionary<string, object> Root;
        }

        static uint U32(byte[] b, long o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
        static int U16(byte[] b, long o) { return b[o] | b[o + 1] << 8; }

        public static Tree Read(byte[] b)
        {
            if (b.Length < 64 || Encoding.ASCII.GetString(b, 0, 4) != "AXT1") throw new FormatException("not an AXT1 file");
            if (U32(b, 4) != 1) throw new FormatException("version " + U32(b, 4));
            var t = new Tree { NodeCount = (int)U32(b, 8), RecordSize = (int)U32(b, 12) };
            uint nodes = U32(b, 16), strings = U32(b, 20);
            Func<uint, string> str = r =>
            {
                if (r == None) return null;
                long at = strings + r;
                int len = (int)U32(b, at);
                return Encoding.Unicode.GetString(b, (int)at + 4, len * 2);
            };
            for (uint i = 0, n = U32(b, 28), o = U32(b, 32); i < n; i++) t.Types.Add(str(U32(b, o + i * 4)));
            for (uint i = 0, n = U32(b, 36), o = U32(b, 40); i < n; i++) t.Flags.Add(str(U32(b, o + i * 4)));
            for (uint i = 0, n = U32(b, 44), o = U32(b, 48); i < n; i++) t.Files.Add(str(U32(b, o + i * 4)));
            if (t.NodeCount == 0) return t;

            // pre-order: children of node i are i+1, then each child's nextSibling
            var built = new Dictionary<string, object>[t.NodeCount];
            for (int i = 0; i < t.NodeCount; i++)
            {
                long r = nodes + (long)i * t.RecordSize;
                var d = new Dictionary<string, object>();
                d["type"] = t.Types[U16(b, r)];
                d["file"] = U16(b, r + 2);
                uint flags = U32(b, r + 4);
                var set = new List<string>();
                for (int bit = 0; bit < t.Flags.Count; bit++) if ((flags & (1u << bit)) != 0) set.Add(t.Flags[bit]);
                d["flagset"] = set;
                d["parent"] = U32(b, r + 8);
                d["next"] = U32(b, r + 12);
                d["childCount"] = (int)U32(b, r + 16);
                uint so = U32(b, r + 20), eo = U32(b, r + 24);
                if (so != None)
                {
                    d["start"] = new Dictionary<string, object> { { "line", (int)U32(b, r + 28) }, { "col", (int)U32(b, r + 44) }, { "offset", (int)so } };
                    d["end"] = new Dictionary<string, object> { { "line", (int)U32(b, r + 32) }, { "offset", (int)eo } };
                }
                string v = str(U32(b, r + 36)), m = str(U32(b, r + 40));
                if (v != null) d["value"] = v;
                if (m != null) d["meta"] = m;
                built[i] = d;
            }
            for (int i = 0; i < t.NodeCount; i++)
            {
                var d = built[i];
                int cc = (int)d["childCount"];
                if (cc == 0 || ((List<string>)d["flagset"]).Contains("truncated")) continue;
                var kids = new List<object>();
                uint c = (uint)(i + 1);
                while (c != None && kids.Count < cc)
                {
                    kids.Add(built[c]);
                    if ((uint)built[c]["parent"] != (uint)i) throw new FormatException("node " + c + ": parent " + built[c]["parent"] + ", expected " + i);
                    c = (uint)built[c]["next"];
                }
                if (kids.Count != cc) throw new FormatException("node " + i + ": " + kids.Count + " children linked, childCount " + cc);
                d["children"] = kids;
            }
            t.Root = built[0];
            return t;
        }

        /// <summary>The first difference between a JSON parse node and the AXT1 node, or null when they agree.</summary>
        public static string Diff(Dictionary<string, object> json, Dictionary<string, object> bin, string path)
        {
            Func<object, string> s = o => o == null ? null : Convert.ToString(o);
            if (s(json["type"]) != s(bin["type"])) return path + ": type " + json["type"] + " vs " + bin["type"];
            string at = path + "/" + json["type"];
            object jv, bv;
            json.TryGetValue("value", out jv); bin.TryGetValue("value", out bv);
            if (s(jv) != s(bv)) return at + ": value «" + jv + "» vs «" + bv + "»";
            json.TryGetValue("meta", out jv); bin.TryGetValue("meta", out bv);
            if (s(jv) != s(bv)) return at + ": meta «" + jv + "» vs «" + bv + "»";
            if (Convert.ToInt32(json["file"]) != Convert.ToInt32(bin["file"])) return at + ": file " + json["file"] + " vs " + bin["file"];
            string rj = Pos(json, "start", true) + "-" + Pos(json, "end", false), rb = Pos(bin, "start", true) + "-" + Pos(bin, "end", false);
            if (rj != rb) return at + ": range " + rj + " vs " + rb;

            // flags: the JSON form's true booleans (+ "truncated" where children were cut off) = the bits that are set
            var jf = new List<string>();
            object fo;
            if (json.TryGetValue("flags", out fo) && fo is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)fo) if (kv.Value is bool && (bool)kv.Value) jf.Add(kv.Key);
            bool jTrunc = json.ContainsKey("childCount") && !json.ContainsKey("children");
            if (jTrunc) jf.Add("truncated");
            var bf = (List<string>)bin["flagset"];
            if (string.Join(",", jf.OrderBy(x => x)) != string.Join(",", bf.OrderBy(x => x)))
                return at + ": flags [" + string.Join(",", jf) + "] vs [" + string.Join(",", bf) + "]";

            var jk = json.ContainsKey("children") ? ((System.Collections.IEnumerable)json["children"]).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
            var bk = bin.ContainsKey("children") ? ((List<object>)bin["children"]).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
            int jCount = jTrunc ? Convert.ToInt32(json["childCount"]) : jk.Count;
            if (jCount != (int)bin["childCount"]) return at + ": childCount " + jCount + " vs " + bin["childCount"];
            if (jk.Count != bk.Count) return at + ": children " + jk.Count + " vs " + bk.Count;
            for (int i = 0; i < jk.Count; i++)
            {
                string d = Diff(jk[i], bk[i], at + "[" + i + "]");
                if (d != null) return d;
            }
            return null;
        }

        static string Pos(Dictionary<string, object> n, string key, bool withCol)
        {
            object o;
            if (!n.TryGetValue(key, out o) || o == null) return "none";
            var p = (Dictionary<string, object>)o;
            return p["offset"] + "@" + p["line"] + (withCol ? ":" + p["col"] : "");
        }
    }
}
