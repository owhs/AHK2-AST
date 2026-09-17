// The engine worker (one background thread runs parses and flows one at a time: the engine's pipeline logger
// is static), the live parse of the active script, and fanning results out to Problems, Outline, AST and the
// editors' squiggles. Caret moves select the matching AST node.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal partial class AstWorkbenchForm
{
    private readonly object _jobLock = new object();
    private readonly Queue<Action> _jobs = new Queue<Action>();
    private Action _pendingParse;            // only the latest parse request matters
    private readonly AutoResetEvent _jobSignal = new AutoResetEvent(false);
    private Thread _worker;
    private volatile bool _busy;
    private ParseResult _lastResult;
    private System.Windows.Forms.Timer _parseTimer;

    void StartEngineWorker()
    {
        _parseTimer = new System.Windows.Forms.Timer();
        _parseTimer.Tick += (s, e) => { _parseTimer.Stop(); QueueParse(); };
        // deep scripts recurse deeply in the parser and emitter: give the worker a big stack
        _worker = new Thread(WorkerLoop, 256 * 1024 * 1024) { IsBackground = true, Name = "engine" };
        _worker.Start();
    }

    void WorkerLoop()
    {
        while (true)
        {
            _jobSignal.WaitOne();
            while (true)
            {
                Action job = null;
                lock (_jobLock)
                {
                    if (_jobs.Count > 0) job = _jobs.Dequeue();
                    else if (_pendingParse != null) { job = _pendingParse; _pendingParse = null; }
                }
                if (job == null) break;
                _busy = true;
                try { job(); }
                catch (Exception ex) { try { BeginInvoke((Action)(() => Status("Engine error: " + ex.Message, WbTheme.Red))); } catch { } }
                finally { _busy = false; }
            }
        }
    }

    /// <summary>Runs `work` on the engine thread, then `done` on the UI thread.</summary>
    void EngineJob<T>(Func<T> work, Action<T, Exception> done)
    {
        lock (_jobLock) _jobs.Enqueue(() =>
        {
            T result = default(T);
            Exception err = null;
            try { result = work(); } catch (Exception ex) { err = ex; }
            try { BeginInvoke((Action)(() => done(result, err))); } catch { }
        });
        _jobSignal.Set();
    }

    // ── Live parse ─────────────────────────────────────────────────────────────────────────────────────

    void RequestParse(int delayMs)
    {
        _parseTimer.Stop();
        if (AnalysisTarget == null) return;
        _parseTimer.Interval = Math.Max(1, delayMs);
        _parseTimer.Start();
        _statusParse.Text = "● parsing…";
        _statusParse.ForeColor = WbTheme.Sky;
    }

    void QueueParse()
    {
        var doc = AnalysisTarget;
        if (doc == null) return;
        string text = doc.Editor.Text;
        string path = doc.FilePath;
        int version = doc.Editor.TextVersion;
        bool follow = _state.FollowIncludes;
        Action job = () =>
        {
            var sw = Stopwatch.StartNew();
            ParseResult r;
            try
            {
                var eng = new AhkAstEngine();
                AstNode root = follow && path != null ? eng.ParseSourceWithIncludes(text, path, false) : eng.Parse(text);
                r = AstModel.Analyze(root, path, text, version, sw.ElapsedMilliseconds);
                r.Ms = sw.ElapsedMilliseconds;
            }
            catch (Exception ex) { r = new ParseResult { MainFile = path, Text = text, Version = version, Failure = ex }; }
            try { BeginInvoke((Action)(() => ApplyParse(doc, r))); } catch { }
        };
        lock (_jobLock) _pendingParse = job;
        _jobSignal.Set();
    }

    void ApplyParse(ScriptDocument doc, ParseResult r)
    {
        if (doc.IsDisposed || doc != AnalysisTarget) return;
        if (r.Failure != null)
        {
            _statusParse.Text = "✖ parser crashed";
            _statusParse.ForeColor = WbTheme.Red;
            _console.Write("Parser failure: " + r.Failure, WbTheme.Red);
            return;
        }
        _lastResult = r;
        _problems.SetDiagnostics(r.Diagnostics, r.MainFile);
        _outline.SetSymbols(r.Symbols);
        _astPanel.SetResult(r);
        foreach (var d in Scripts()) ShowDiagnosticsIn(d);
        bool stale = doc.Editor.TextVersion != r.Version;
        _statusParse.Text = string.Format("✓ parsed {0:N0} ms · {1:N0} nodes{2}", r.Ms, r.NodeCount, r.Includes.Count > 0 ? " · " + r.Includes.Count + " includes" : "");
        _statusParse.ForeColor = stale ? WbTheme.Subtext0 : WbTheme.Subtext1;
        _statusParse.ToolTipText = r.Includes.Count > 0 ? "Includes:\n" + string.Join("\n", r.Includes.ToArray()) : null;
        if (!stale) SyncAstToCaret(doc);
    }

    void ShowDiagnosticsIn(ScriptDocument d)
    {
        if (_lastResult == null) { d.Editor.SetDiagnostics(null); return; }
        var target = AnalysisTarget;
        IEnumerable<Diagnostic> mine;
        if (d == target) mine = _lastResult.Diagnostics.Where(x => x.File == null || (!d.IsUntitled && string.Equals(x.File, d.FilePath, StringComparison.OrdinalIgnoreCase)));
        else if (!d.IsUntitled && !d.IsDirty) mine = _lastResult.Diagnostics.Where(x => string.Equals(x.File, d.FilePath, StringComparison.OrdinalIgnoreCase));
        else mine = null;
        d.Editor.SetDiagnostics(mine);
    }

    void ClearAnalysis()
    {
        _lastResult = null;
        _problems.SetDiagnostics(new List<Diagnostic>(), null);
        _outline.SetSymbols(null);
        _astPanel.SetResult(null);
        _statusParse.Text = "";
    }

    void OnScriptEdited(ScriptDocument d)
    {
        if (d == AnalysisTarget && _state.LiveParse) RequestParse(d.Editor.TextLength > 400000 ? 1500 : 600);
        else if (d == AnalysisTarget) { _statusParse.Text = "● edited (Ctrl+R to parse)"; _statusParse.ForeColor = WbTheme.Subtext0; }
        UpdateToolbar();
    }

    // ── Caret ──────────────────────────────────────────────────────────────────────────────────────────

    void OnCaretMoved(ScriptDocument d)
    {
        UpdateCaretStatus();
        if (!_navigating && _lastResult != null && d.Editor.Focused) SyncAstToCaret(d);
    }

    void SyncAstToCaret(ScriptDocument d)
    {
        if (!_astPanel.SyncWithEditor || _astPanel.DockPanel == null || _astPanel.IsHidden || _lastResult == null || _lastResult.Root == null) return;
        if (d == AnalysisTarget && d.Editor.TextVersion != _lastResult.Version) return; // positions are stale until the next parse
        var node = AstModel.FindAt(_lastResult.Root, _lastResult.MainFile, d.FilePath, d.Editor.CaretLine, d.Editor.CaretColumn);
        if (node != null) _astPanel.Reveal(node);
    }

    void UpdateCaretStatus()
    {
        var e = ActiveEditor;
        if (e == null) { _statusPos.Text = ""; return; }
        int sel = e.SelectionLength;
        _statusPos.Text = string.Format("Ln {0}, Col {1}{2}", e.CaretLine, e.CaretColumn, sel > 0 ? "  (" + sel + " selected)" : "");
    }
}
