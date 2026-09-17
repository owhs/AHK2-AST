// `AstWorkbench.exe --tour <dir> script.ahk`: walks through the main screens and saves a screenshot of each into
// <dir>, then exits. Used by the harness (`harness.ps1 shot <file> --tour`) to check the UI on its hidden desktop.
// A tour never saves the session or layout.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal partial class AstWorkbenchForm
{
    private string _tourDir;
    private readonly Queue<KeyValuePair<int, Action>> _tour = new Queue<KeyValuePair<int, Action>>();

    [DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    public void EnableTour(string dir) { _tourDir = dir; }

    void StartTourIfRequested()
    {
        if (_tourDir == null) return;
        Directory.CreateDirectory(_tourDir);
        var original = WbTheme.Current;
        CommandPalette palette = null;
        Step(3000, () => Shot("01-script"));
        Step(300, () => { palette = new CommandPalette(this, "min", PaletteItems); palette.Show(this); });
        Step(800, () => { Shot("02-palette", palette); palette.Close(); });
        Step(300, () => { var f = _flows.FirstOrDefault(x => x.Name == "Minify (aggressive)") ?? _currentFlow; SelectFlow(f); RunFlow(f); });
        Step(-1, () => Shot("03-flow-output"));   // -1: waits for the engine to be idle
        Step(300, () =>
        {
            var od = _dockPanel.Documents.OfType<OutputDocument>().FirstOrDefault();
            if (od != null && od.OutputText != null) CompareTexts("source", od.SourceText, od.TabText, od.OutputText);
        });
        Step(2500, () => Shot("04-compare"));
        Step(300, () =>
        {
            var t = _lastScript;
            if (t != null) t.Activate();
            var sym = _outline.AllSymbols().FirstOrDefault(s => s.Kind == SymbolKind.Method) ?? _outline.AllSymbols().FirstOrDefault(s => s.Kind == SymbolKind.Function);
            if (sym != null) Navigate(sym.File, sym.Line, sym.Column, sym.Line, sym.Column + sym.Name.Length);
            ShowPanel(_astPanel);
        });
        Step(1500, () => Shot("05-navigate"));
        Step(300, () =>
        {
            // type a syntax error into the main script: live parse → squiggle + Problems
            var t = Scripts().FirstOrDefault(d => d.OwnerPath == null);
            if (t == null) return;
            t.Activate();
            t.Editor.GoToLine(36);
            t.Editor.InsertText("broken := (1 +\r\n");
            t.Editor.GoToLine(36);
        });
        Step(-1, () => { });
        Step(1200, () => Shot("07-live-error"));
        Step(300, () => { var t = Scripts().FirstOrDefault(d => d.OwnerPath == null); if (t != null) t.Editor.Undo(); OpenFlowEditor(_currentFlow); });
        Step(1500, () => Shot("08-flow-editor"));
        Step(300, () => { var light = ThemeManager.Themes.FirstOrDefault(x => !x.IsDark); if (light != null) SwitchTheme(light); });
        Step(1500, () => Shot("06-light-theme"));
        Step(300, () => SwitchTheme(original));
        Step(500, () => { foreach (var d in Scripts()) d.ForceClose = true; foreach (var d in Scripts().ToList()) d.Editor.IsChanged = false; Close(); });
        NextStep();
    }

    void Step(int delay, Action a) { _tour.Enqueue(new KeyValuePair<int, Action>(delay, a)); }

    void NextStep()
    {
        if (_tour.Count == 0) return;
        var s = _tour.Peek();
        var timer = new Timer { Interval = Math.Max(1, s.Key < 0 ? 500 : s.Key) };
        int waited = 0;
        timer.Tick += (o, e) =>
        {
            if (s.Key < 0 && (_busy || HasPendingJobs()) && waited < 120000) { waited += timer.Interval; return; }
            timer.Stop(); timer.Dispose();
            _tour.Dequeue();
            try { s.Value(); } catch (Exception ex) { File.AppendAllText(Path.Combine(_tourDir, "tour-errors.txt"), ex + "\r\n"); }
            NextStep();
        };
        timer.Start();
    }

    bool HasPendingJobs() { lock (_jobLock) return _jobs.Count > 0 || _pendingParse != null; }

    void Shot(string name, Form f = null)
    {
        f = f ?? this;
        var r = f.Bounds;
        using (var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(f.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            }
            bmp.Save(Path.Combine(_tourDir, name + ".png"), ImageFormat.Png);
        }
    }
}
