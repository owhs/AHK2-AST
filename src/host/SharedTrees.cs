using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace AstHost
{
    /// <summary>
    /// `"to":"shm"`: AXT1 bytes in a named, page-file-backed shared memory section (no file on disk). The reader opens
    /// it by name (OpenFileMapping + MapViewOfFile) and reads with NumGet on the view pointer. The host keeps its
    /// handle — and so the section — until `release` or exit; a reader's own open view keeps it alive after that too.
    /// </summary>
    public static class SharedTrees
    {
        static readonly Dictionary<string, MemoryMappedFile> Live = new Dictionary<string, MemoryMappedFile>(StringComparer.OrdinalIgnoreCase);
        static int _counter;

        public static string Publish(byte[] bytes, string requestedName)
        {
            string name = !string.IsNullOrEmpty(requestedName) ? requestedName
                : "Local\\AstHost-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Interlocked.Increment(ref _counter);
            lock (Live)
            {
                MemoryMappedFile old;
                if (Live.TryGetValue(name, out old)) { old.Dispose(); Live.Remove(name); } // same name again: replaced
            }
            MemoryMappedFile mmf;
            try { mmf = MemoryMappedFile.CreateNew(name, Math.Max(1, bytes.Length), MemoryMappedFileAccess.ReadWrite); }
            catch (Exception ex) { throw new RequestException("io", "cannot create shared memory " + name + ": " + ex.Message); }
            using (var view = mmf.CreateViewStream(0, bytes.Length, MemoryMappedFileAccess.Write))
                view.Write(bytes, 0, bytes.Length);
            lock (Live) Live[name] = mmf;
            return name;
        }

        public static bool Release(string name)
        {
            lock (Live)
            {
                MemoryMappedFile m;
                if (!Live.TryGetValue(name, out m)) return false;
                m.Dispose();
                Live.Remove(name);
                return true;
            }
        }

        public static int ReleaseAll()
        {
            lock (Live)
            {
                int n = Live.Count;
                foreach (var m in Live.Values) m.Dispose();
                Live.Clear();
                return n;
            }
        }
    }
}
