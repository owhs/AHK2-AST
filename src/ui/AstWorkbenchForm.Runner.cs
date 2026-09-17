// Running and validating with AutoHotkey. A saved, unmodified script runs as itself; edited text and flow
// output run from a hidden temp file in the script's folder (so A_ScriptDir and relative paths still work).
// Stop only ever ends the process the workbench started.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal partial class AstWorkbenchForm
{
    private Process _runProcess;
    private string _runTemp;

    bool IsRunning { get { try { return _runProcess != null && !_runProcess.HasExited; } catch { return false; } } }

    string Ahk()
    {
        string p = AhkRuntime.Find(_state.AhkPath);
        if (p == null)
        {
            var r = MessageBox.Show(this, "AutoHotkey v2 was not found. Choose AutoHotkey64.exe (or AutoHotkey.exe) now?", "AutoHotkey", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes && PickAhkPath()) p = _state.AhkPath;
        }
        return p;
    }

    bool PickAhkPath()
    {
        using (var dlg = new OpenFileDialog { Filter = "AutoHotkey (*.exe)|*.exe", Title = "Choose the AutoHotkey v2 executable" })
        {
            string cur = AhkRuntime.Find(_state.AhkPath);
            if (cur != null) dlg.InitialDirectory = Path.GetDirectoryName(cur);
            if (dlg.ShowDialog(this) != DialogResult.OK) return false;
            _state.AhkPath = dlg.FileName;
            _state.Save();
            Status("AutoHotkey: " + dlg.FileName, WbTheme.Green);
            return true;
        }
    }

    void RunActive()
    {
        var t = AnalysisTarget;
        if (t == null) return;
        if (t.IsUntitled || t.IsDirty)
        {
            var r = t.IsUntitled ? DialogResult.No : MessageBox.Show(this, t.DisplayName + " has unsaved changes.\n\nYes: save, then run the file.\nNo: run the edited text without saving.",
                "Run", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes) { if (!t.Save()) return; }
            else { RunScriptText(t.GetText(), t.IsUntitled ? null : Path.GetDirectoryName(t.FilePath), t.DisplayName + " (unsaved)"); return; }
        }
        StartRun(t.FilePath, Path.GetDirectoryName(t.FilePath), t.DisplayName, null);
    }

    public void RunScriptText(string text, string dir, string title)
    {
        string kind = OutputDocument.DetectKind(text);
        if (kind == "html" || kind == "markdown" || kind == "json")
        {
            string ext = kind == "html" ? ".html" : kind == "json" ? ".json" : ".md";
            string p = Path.Combine(Path.GetTempPath(), "ahk2ast_" + DateTime.Now.Ticks + ext);
            File.WriteAllText(p, text, new UTF8Encoding(false));
            try { Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); Status("Opened " + title + " in the default app", WbTheme.Sky); }
            catch (Exception ex) { Status("Could not open: " + ex.Message, WbTheme.Red); }
            return;
        }
        if (kind == "nim")
        {
            if (!HasNimPlugin) { Status("This build has no Nim transpiler plugin", WbTheme.Yellow); return; }
            OpenNimBuildManager().TriggerBuildFromTranspiler(text);
            return;
        }
        string temp;
        try { temp = AhkRuntime.TempScript(dir, text); }
        catch (Exception ex) { Status("Could not write the temp script: " + ex.Message, WbTheme.Red); return; }
        StartRun(temp, Path.GetDirectoryName(temp), title, temp);
    }

    void StartRun(string script, string workDir, string title, string tempToDelete)
    {
        string ahk = Ahk();
        if (ahk == null) { if (tempToDelete != null) TryDelete(tempToDelete); return; }
        if (IsRunning) StopRun(false);

        ShowPanel(_console);
        _console.Begin("Run: " + title);
        _console.Write("▶ " + title, WbTheme.Lavender);
        _console.Write("  " + ahk + "  " + (tempToDelete != null ? "(temp copy) " : "") + script, WbTheme.Overlay0);
        var sw = Stopwatch.StartNew();
        try
        {
            var p = new Process { StartInfo = AhkRuntime.StartInfo(ahk, script, workDir, false), EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) UI(() => _console.Write(e.Data, WbTheme.Text)); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) UI(() => _console.Write(tempToDelete != null ? e.Data.Replace(tempToDelete, title) : e.Data, WbTheme.Red)); };
            p.Exited += (s, e) =>
            {
                int code = -1;
                try { code = p.ExitCode; } catch { }
                UI(() =>
                {
                    _console.Write(string.Format("■ exited with code {0} after {1:0.0} s", code, sw.Elapsed.TotalSeconds), code == 0 ? WbTheme.Green : WbTheme.Red);
                    Status(title + (code == 0 ? " finished" : " exited with code " + code), code == 0 ? WbTheme.Green : WbTheme.Red);
                    if (tempToDelete != null) TryDelete(tempToDelete);
                    if (_runProcess == p) { _runProcess = null; _runTemp = null; }
                    UpdateToolbar();
                    AutoLoadTraceAfterRun(workDir);
                });
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _runProcess = p;
            _runTemp = tempToDelete;
            Status("Running " + title + "…  (Shift+F5 stops it)", WbTheme.Sky);
        }
        catch (Exception ex)
        {
            _console.Write("Could not start AutoHotkey: " + ex.Message, WbTheme.Red);
            if (tempToDelete != null) TryDelete(tempToDelete);
        }
        UpdateToolbar();
    }

    void StopRun(bool user)
    {
        var p = _runProcess;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(); // only the process this workbench started
                if (user) { _console.Write("■ stopped", WbTheme.Yellow); Status("Stopped", WbTheme.Yellow); }
            }
        }
        catch { }
        if (_runTemp != null) { var t = _runTemp; ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(500); TryDelete(t); }); }
        _runProcess = null;
        _runTemp = null;
        UpdateToolbar();
    }

    static void TryDelete(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } catch { }
    }

    void UI(Action a)
    {
        try { if (!IsDisposed) BeginInvoke(a); } catch { }
    }

    // ── Validate ───────────────────────────────────────────────────────────────────────────────────────

    void ValidateActive()
    {
        var t = AnalysisTarget;
        if (t == null) return;
        if (!t.IsUntitled && !t.IsDirty) Validate(t.FilePath, Path.GetDirectoryName(t.FilePath), t.DisplayName, null);
        else ValidateText(t.GetText(), t.IsUntitled ? null : Path.GetDirectoryName(t.FilePath), t.DisplayName);
    }

    public void ValidateText(string text, string dir, string title)
    {
        string temp;
        try { temp = AhkRuntime.TempScript(dir, text); }
        catch (Exception ex) { Status("Could not write the temp script: " + ex.Message, WbTheme.Red); return; }
        Validate(temp, Path.GetDirectoryName(temp), title, temp);
    }

    void Validate(string script, string workDir, string title, string temp)
    {
        string ahk = Ahk();
        if (ahk == null) { if (temp != null) TryDelete(temp); return; }
        Status("Validating " + title + " with AutoHotkey…", WbTheme.Sky);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string output = ""; int code = -1;
            try
            {
                using (var p = Process.Start(AhkRuntime.StartInfo(ahk, script, workDir, true)))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var outp = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } output = "AutoHotkey did not finish validating within 20 s."; }
                    else { code = p.ExitCode; output = (err.Result + outp.Result).Trim(); }
                }
            }
            catch (Exception ex) { output = ex.Message; }
            if (temp != null) { output = output.Replace(temp, title); TryDelete(temp); }
            UI(() =>
            {
                bool ok = code == 0 && output.Length == 0;
                _console.Begin("Validate: " + title);
                if (ok) _console.Write("✓ AutoHotkey accepts " + title + " (loads without errors)", WbTheme.Green);
                else { _console.Write("✖ AutoHotkey rejects " + title + " (exit code " + code + ")", WbTheme.Red); _console.Write(output, WbTheme.Text); ShowPanel(_console); }
                Status(ok ? "✓ " + title + " is valid AutoHotkey" : "✖ " + title + ": " + output.Split('\n').FirstOrDefault(), ok ? WbTheme.Green : WbTheme.Red);
            });
        });
    }
}
