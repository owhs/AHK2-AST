using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AstHost
{
    /// <summary>
    /// The parse tree as an AXT1 file (PROTOCOL.md, "Binary parse output"): fixed 48-byte node records in pre-order, a
    /// string table, and tables of type names, flag names and file paths, so a reader (AutoHotkey: FileRead RAW +
    /// NumGet/StrGet) can walk a big tree lazily without parsing JSON. Same nodes, ranges and flags as TreeJson.
    /// </summary>
    public static class TreeBin
    {
        public const int HeaderSize = 64, RecordSize = 48;
        const uint None = 0xFFFFFFFF;

        /// <summary>Boolean flags, in bit order. Bits after these are added on first use (never more than 32).</summary>
        public static readonly string[] BaseFlags =
        {
            "truncated", "attached", "static", "member", "byref", "variadic", "optional", "command", "inline",
            "executes", "comma", "paren", "space", "implicit", "bare", "hotif", "recovery", "duplicate", "assign", "block"
        };

        class Rec
        {
            public AstNode Node;
            public int Parent, Next = -1, ChildCount, File, Type;
            public uint Flags;
        }

        /// <summary>Writes <paramref name="root"/> (children down to <paramref name="maxDepth"/> levels) to <paramref name="path"/> in one write.</summary>
        public static byte[] Write(string path, AstNode root, FileTable files, int maxDepth, out int nodes)
        {
            var file = Build(root, files, maxDepth, out nodes);
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, file); // one sequential write; replaces an existing file
            return file;
        }

        /// <summary>The AXT1 bytes of <paramref name="root"/> (children down to <paramref name="maxDepth"/> levels).</summary>
        public static byte[] Build(AstNode root, FileTable files, int maxDepth, out int nodeCount)
        {
            var recs = new List<Rec>(1024);
            var types = new List<string>();
            var typeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var flags = new List<string>(BaseFlags);
            var flagIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < flags.Count; i++) flagIndex[flags[i]] = i;

            Add(root, -1, 0, maxDepth, recs, files, types, typeIndex, flags, flagIndex);

            // strings: each distinct string once (u32 length in UTF-16 units, the units, padding to 4 bytes)
            var strings = new MemoryStream();
            var strRef = new Dictionary<string, uint>(StringComparer.Ordinal);
            Func<string, uint> str = s =>
            {
                if (s == null) return None;
                uint r;
                if (strRef.TryGetValue(s, out r)) return r;
                r = (uint)strings.Length;
                var bytes = Encoding.Unicode.GetBytes(s);
                strings.Write(BitConverter.GetBytes((uint)s.Length), 0, 4);
                strings.Write(bytes, 0, bytes.Length);
                int pad = (4 - bytes.Length % 4) % 4;
                for (int p = 0; p < pad; p++) strings.WriteByte(0);
                strRef[s] = r;
                return r;
            };

            int n = recs.Count;
            int nodesOffset = HeaderSize;
            var nodeBytes = new byte[(long)n * RecordSize];
            for (int i = 0; i < n; i++)
            {
                var r = recs[i];
                var node = r.Node;
                int o = i * RecordSize;
                PutU16(nodeBytes, o + 0, r.Type);
                PutU16(nodeBytes, o + 2, r.File);
                PutU32(nodeBytes, o + 4, r.Flags);
                PutU32(nodeBytes, o + 8, r.Parent < 0 ? None : (uint)r.Parent);
                PutU32(nodeBytes, o + 12, r.Next < 0 ? None : (uint)r.Next);
                PutU32(nodeBytes, o + 16, (uint)r.ChildCount);
                bool ranged = node.HasRange;
                PutU32(nodeBytes, o + 20, ranged ? (uint)node.StartOffset : None);
                PutU32(nodeBytes, o + 24, ranged ? (uint)node.EndOffset : None);
                PutU32(nodeBytes, o + 28, ranged ? (uint)node.RangeStartLine : 0);
                PutU32(nodeBytes, o + 32, ranged ? (uint)node.RangeEndLine : 0);
                PutU32(nodeBytes, o + 36, string.IsNullOrEmpty(node.Value) ? None : str(node.Value));
                PutU32(nodeBytes, o + 40, string.IsNullOrEmpty(node.Metadata) ? None : str(node.Metadata));
                PutU32(nodeBytes, o + 44, ranged ? (uint)node.RangeStartColumn : 0);
            }
            var typeRefs = new uint[types.Count];
            for (int i = 0; i < types.Count; i++) typeRefs[i] = str(types[i]);
            var flagRefs = new uint[flags.Count];
            for (int i = 0; i < flags.Count; i++) flagRefs[i] = str(flags[i]);
            var fileRefs = new uint[files.Paths.Count];
            for (int i = 0; i < files.Paths.Count; i++) fileRefs[i] = files.Paths[i] == null ? None : str(files.Paths[i]);

            int typeTableOffset = nodesOffset + nodeBytes.Length;
            int flagTableOffset = typeTableOffset + typeRefs.Length * 4;
            int fileTableOffset = flagTableOffset + flagRefs.Length * 4;
            int stringsOffset = fileTableOffset + fileRefs.Length * 4;
            int stringsBytes = (int)strings.Length;
            var file = new byte[stringsOffset + stringsBytes];

            Encoding.ASCII.GetBytes("AXT1", 0, 4, file, 0);
            PutU32(file, 4, 1);
            PutU32(file, 8, (uint)n);
            PutU32(file, 12, RecordSize);
            PutU32(file, 16, (uint)nodesOffset);
            PutU32(file, 20, (uint)stringsOffset);
            PutU32(file, 24, (uint)stringsBytes);
            PutU32(file, 28, (uint)typeRefs.Length);
            PutU32(file, 32, (uint)typeTableOffset);
            PutU32(file, 36, (uint)flagRefs.Length);
            PutU32(file, 40, (uint)flagTableOffset);
            PutU32(file, 44, (uint)fileRefs.Length);
            PutU32(file, 48, (uint)fileTableOffset);
            Buffer.BlockCopy(nodeBytes, 0, file, nodesOffset, nodeBytes.Length);
            for (int i = 0; i < typeRefs.Length; i++) PutU32(file, typeTableOffset + i * 4, typeRefs[i]);
            for (int i = 0; i < flagRefs.Length; i++) PutU32(file, flagTableOffset + i * 4, flagRefs[i]);
            for (int i = 0; i < fileRefs.Length; i++) PutU32(file, fileTableOffset + i * 4, fileRefs[i]);
            strings.Position = 0;
            strings.Read(file, stringsOffset, stringsBytes);
            nodeCount = n;
            return file;
        }

        static int Add(AstNode node, int parent, int file, int depthLeft, List<Rec> recs, FileTable files,
                       List<string> types, Dictionary<string, int> typeIndex, List<string> flags, Dictionary<string, int> flagIndex)
        {
            int idx = recs.Count;
            var r = new Rec { Node = node, Parent = parent, File = file };
            recs.Add(r);
            int t;
            if (!typeIndex.TryGetValue(node.NodeType, out t)) { t = types.Count; types.Add(node.NodeType); typeIndex[node.NodeType] = t; }
            r.Type = t;
            // the JSON form's boolean flags (true ones) become bits; its other flags are derived from value/meta
            foreach (DictionaryEntry e in TreeJson.Flags(node))
            {
                if (!(e.Value is bool) || !(bool)e.Value) continue;
                string name = (string)e.Key;
                int bit;
                if (!flagIndex.TryGetValue(name, out bit))
                {
                    if (flags.Count >= 32) continue;
                    bit = flags.Count; flags.Add(name); flagIndex[name] = bit;
                }
                r.Flags |= 1u << bit;
            }
            int kids = 0;
            for (int i = 0; i < node.ChildCount; i++) if (node.GetChild(i) != null) kids++;
            r.ChildCount = kids;
            if (kids == 0) return idx;
            if (depthLeft <= 0) { r.Flags |= 1u; return idx; } // "truncated": real childCount, no records after it
            int childFile = node.ChildFile != null ? files.Index(node.ChildFile) : file;
            int prev = -1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                var c = node.GetChild(i);
                if (c == null) continue;
                int ci = Add(c, idx, childFile, depthLeft - 1, recs, files, types, typeIndex, flags, flagIndex);
                if (prev >= 0) recs[prev].Next = ci;
                prev = ci;
            }
            return idx;
        }

        static void PutU16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        static void PutU32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
    }
}
