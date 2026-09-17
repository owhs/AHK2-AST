using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AstHost
{
    /// <summary>
    /// Minimal JSON writer. Output is pure ASCII (everything else as \uXXXX), so a reply survives any pipe code page
    /// on the reading side.
    /// </summary>
    public static class Json
    {
        public static string Str(string s)
        {
            var sb = new StringBuilder((s ?? "").Length + 2);
            Str(sb, s);
            return sb.ToString();
        }

        public static void Str(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Any value made of dictionaries, lists, strings, numbers, booleans and null (and raw JSON via <see cref="Raw"/>).</summary>
        public static void Value(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            var raw = v as Raw;
            if (raw != null) { sb.Append(raw.Text); return; }
            var s = v as string;
            if (s != null) { Str(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal) { sb.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture)); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Value(sb, e.Value);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Value(sb, item);
                }
                sb.Append(']');
                return;
            }
            Str(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        public static string Serialize(object v)
        {
            var sb = new StringBuilder();
            Value(sb, v);
            return sb.ToString();
        }

        /// <summary>Already-serialized JSON to embed as a value.</summary>
        public class Raw
        {
            public readonly string Text;
            public Raw(string text) { Text = text; }
        }
    }

    /// <summary>An insertion-ordered string → value map (JSON objects keep their key order).</summary>
    public class JObj : IDictionary
    {
        readonly List<DictionaryEntry> _items = new List<DictionaryEntry>();

        public JObj Add(string key, object value) { _items.Add(new DictionaryEntry(key, value)); return this; }

        public object this[object key]
        {
            get { foreach (var e in _items) if ((string)e.Key == (string)key) return e.Value; return null; }
            set
            {
                for (int i = 0; i < _items.Count; i++) if ((string)_items[i].Key == (string)key) { _items[i] = new DictionaryEntry(key, value); return; }
                _items.Add(new DictionaryEntry(key, value));
            }
        }

        public int Count { get { return _items.Count; } }
        public IDictionaryEnumerator GetEnumerator() { return new Enumerator(_items); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public bool Contains(object key) { foreach (var e in _items) if ((string)e.Key == (string)key) return true; return false; }
        void IDictionary.Add(object key, object value) { Add((string)key, value); }
        public void Clear() { _items.Clear(); }
        public void Remove(object key) { _items.RemoveAll(e => (string)e.Key == (string)key); }
        public bool IsFixedSize { get { return false; } }
        public bool IsReadOnly { get { return false; } }
        public ICollection Keys { get { var l = new List<object>(); foreach (var e in _items) l.Add(e.Key); return l; } }
        public ICollection Values { get { var l = new List<object>(); foreach (var e in _items) l.Add(e.Value); return l; } }
        public void CopyTo(Array array, int index) { foreach (var e in _items) array.SetValue(e, index++); }
        public bool IsSynchronized { get { return false; } }
        public object SyncRoot { get { return this; } }

        class Enumerator : IDictionaryEnumerator
        {
            readonly List<DictionaryEntry> _l;
            int _i = -1;
            public Enumerator(List<DictionaryEntry> l) { _l = l; }
            public DictionaryEntry Entry { get { return _l[_i]; } }
            public object Key { get { return _l[_i].Key; } }
            public object Value { get { return _l[_i].Value; } }
            public object Current { get { return _l[_i]; } }
            public bool MoveNext() { return ++_i < _l.Count; }
            public void Reset() { _i = -1; }
        }
    }
}
