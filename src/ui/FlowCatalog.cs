// Flows you can run: the built-in presets (BuiltinFlows.cs) and the ones saved from the flow editor
// (<exe dir>\Flows\*.json). Plus AhkRuntime: finding AutoHotkey, running scripts and /Validate.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal class FlowEntry
{
    public string Name, Folder, Description, Json, FilePath;
    public bool BuiltIn { get { return FilePath == null; } }
    public string Id { get { return BuiltIn ? "builtin:" + Name : "file:" + FilePath; } }
}

internal static class FlowCatalog
{
    public static string UserDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows"); } }

    public static List<FlowEntry> Load()
    {
        var list = BuiltinFlows.All.Select(a => new FlowEntry { Folder = a[0], Name = a[1], Description = a[2], Json = a[3] }).ToList();
        try
        {
            if (Directory.Exists(UserDir))
                foreach (var f in Directory.GetFiles(UserDir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        string json = File.ReadAllText(f);
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                        var meta = d != null && d.ContainsKey("Meta") ? d["Meta"] as Dictionary<string, object> : null;
                        Func<string, string> m = k => meta != null && meta.ContainsKey(k) && meta[k] != null ? meta[k].ToString() : "";
                        string name = m("Name");
                        list.Add(new FlowEntry
                        {
                            Name = name.Length > 0 ? name : Path.GetFileNameWithoutExtension(f),
                            Folder = m("Folder").Length > 0 ? m("Folder") : "My flows",
                            Description = m("Description"),
                            Json = json,
                            FilePath = f
                        });
                    }
                    catch { /* a broken flow file is skipped; it still opens in the flow editor */ }
                }
        }
        catch { }
        return list;
    }
}

internal static class AhkRuntime
{
    public static string Find(string configured)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        var dirs = new List<string>();
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AutoHotkey"))
                if (k != null && k.GetValue("InstallDir") is string) dirs.Add((string)k.GetValue("InstallDir"));
        }
        catch { }
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        dirs.Add(Path.Combine(pf, "AutoHotkey"));
        dirs.Add(@"C:\Program Files\AutoHotkey");
        string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dirs.Add(Path.Combine(la, "Programs", "AutoHotkey"));
        foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var sub in new[] { "v2", "" })
                foreach (var exe in new[] { Environment.Is64BitOperatingSystem ? "AutoHotkey64.exe" : "AutoHotkey32.exe", "AutoHotkey.exe", "AutoHotkey32.exe" })
                {
                    string p = Path.Combine(Path.Combine(d, sub), exe);
                    if (File.Exists(p)) return p;
                }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            foreach (var exe in new[] { "AutoHotkey64.exe", "AutoHotkey.exe" })
            {
                try { string p = Path.Combine(dir.Trim(), exe); if (File.Exists(p)) return p; } catch { }
            }
        return null;
    }

    public static ProcessStartInfo StartInfo(string ahk, string script, string workDir, bool validate)
    {
        return new ProcessStartInfo
        {
            FileName = ahk,
            Arguments = "/ErrorStdOut=UTF-8 " + (validate ? "/Validate " : "") + "\"" + script + "\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
    }

    /// <summary>A temp script next to `dir` (so A_ScriptDir and relative paths still work), removed after the run.</summary>
    public static string TempScript(string dir, string text)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
        string path = Path.Combine(dir, "__workbench_run_" + Process.GetCurrentProcess().Id + "_" + Environment.TickCount + ".ahk");
        try { File.WriteAllText(path, text, new UTF8Encoding(true)); }
        catch
        {
            path = Path.Combine(Path.GetTempPath(), Path.GetFileName(path));
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
        try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }
        return path;
    }
}
