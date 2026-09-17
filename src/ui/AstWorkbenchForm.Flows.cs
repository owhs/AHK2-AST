// Running flows (on the engine worker; the result opens as an output tab next to the script), comparing texts,
// and the flow editor / other tool tabs.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal partial class AstWorkbenchForm
{
    void RunFlow(FlowEntry flow)
    {
        if (flow == null) return;
        RunFlowJson(flow.Name, flow.Json, null);
    }

    public void RerunFlow(OutputDocument doc)
    {
        RunFlowJson(doc.FlowName, doc.FlowJson, doc);
    }

    void RunFlowJson(string name, string json, OutputDocument reuse, bool autoRun = false)
    {
        var target = AnalysisTarget;
        if (target == null) { Status("Open a script first", WbTheme.Yellow); return; }
        string text = target.Editor.Text;
        string path = target.FilePath;
        bool follow = _state.FollowIncludes;
        long inputBytes = 0; int includeCount = 0;
        if (follow && _lastResult != null && _lastResult.MainFile == path && _lastResult.Includes.Count > 0)
        {
            inputBytes = System.Text.Encoding.UTF8.GetByteCount(text);
            foreach (var inc in _lastResult.Includes) { try { inputBytes += new FileInfo(inc).Length; includeCount++; } catch { } }
        }

        var od = reuse ?? _dockPanel.Documents.OfType<OutputDocument>().FirstOrDefault(o => o.FlowName == name && string.Equals(o.SourcePath, path, StringComparison.OrdinalIgnoreCase));
        if (od == null || od.IsDisposed)
        {
            od = new OutputDocument(this, name, json, path, text);
            od.Editor.WordWrap = _state.WordWrap;
            od.Editor.Zoom = _state.EditorZoom;
            var outPane = _dockPanel.Documents.OfType<OutputDocument>().Select(o => o.Pane).FirstOrDefault(p => p != null && p != target.Pane);
            if (outPane != null) od.Show(outPane, null);
            else if (target.Pane != null) od.Show(target.Pane, DockAlignment.Right, 0.5);
            else od.Show(_dockPanel, DockState.Document);
        }
        else od.Activate();
        od.SetRunning("Running " + name + " on " + target.DisplayName + "…");
        Status("Running flow " + name + "…", WbTheme.Sky);

        var sw = Stopwatch.StartNew();
        EngineJob(() =>
        {
            string output = _engine.ExecuteFlow(text, json, false, path, follow);
            return new KeyValuePair<string, List<string>>(output, PipelineLogger.Logs.ToList());
        }, (res, err) =>
        {
            sw.Stop();
            if (od.IsDisposed) return;
            _console.Begin("Flow: " + name);
            if (err != null)
            {
                od.SetFailed(err.Message);
                _console.Write(err.ToString(), WbTheme.Red);
                Status("Flow " + name + " failed: " + err.Message, WbTheme.Red);
                return;
            }
            od.SetResult(res.Key, sw.ElapsedMilliseconds, res.Value, text, inputBytes, includeCount);
            _console.WriteLog(res.Value);
            Status(string.Format("{0} finished in {1:N0} ms", name, sw.ElapsedMilliseconds), WbTheme.Green);
            if (autoRun) RunScriptText(res.Key, path != null ? Path.GetDirectoryName(path) : null, od.TabText);
        });
    }

    // ── Compare ────────────────────────────────────────────────────────────────────────────────────────

    public void CompareTexts(string leftTitle, string left, string rightTitle, string right)
    {
        var diff = GetOrCreateDiff();
        diff.TabText = diff.Text = "Compare: " + leftTitle + " ↔ " + rightTitle;
        var t = AnalysisTarget;
        if (diff.DockPanel == null && t != null && t.Pane != null) diff.Show(t.Pane, null);
        else ShowDoc(diff);
        diff.Activate();
        diff.ShowDiff(left ?? "", right ?? "");
    }

    void CompareWithDisk()
    {
        var s = ActiveScript;
        if (s == null || s.IsUntitled || !File.Exists(s.FilePath)) return;
        CompareTexts(s.DisplayName + " (disk)", File.ReadAllText(s.FilePath), s.DisplayName + " (editor)", s.GetText());
    }

    DiffWorkspaceContent GetOrCreateDiff()
    {
        if (_diffContent == null || _diffContent.IsDisposed)
        {
            _diffContent = new DiffWorkspaceContent();
            _diffContent.SetWordWrap(_state.WordWrap);
            UiTheming.Apply(_diffContent);
        }
        return _diffContent;
    }

    EscaperUnescaperContent GetOrCreateEscape()
    {
        if (_escapeContent == null || _escapeContent.IsDisposed)
        {
            _escapeContent = new EscaperUnescaperContent();
            _escapeContent.SetWordWrap(_state.WordWrap);
            UiTheming.Apply(_escapeContent);
        }
        return _escapeContent;
    }

    // ── Flow editor ────────────────────────────────────────────────────────────────────────────────────

    void OpenFlowEditor(FlowEntry flow)
    {
        if (flow != null && !flow.BuiltIn)
        {
            var open = _dockPanel.Documents.OfType<PipelineBuilderContent>().FirstOrDefault(p => string.Equals(p.CurrentFilePath, flow.FilePath, StringComparison.OrdinalIgnoreCase));
            if (open != null) { open.Activate(); return; }
        }
        var pb = new PipelineBuilderContent();
        pb.GetSourceCodeRequested += () => AnalysisTarget != null ? AnalysisTarget.Editor.Text : "";
        pb.GetActiveFilePathRequested += () => AnalysisTarget != null ? AnalysisTarget.FilePath : null;
        pb.ExecuteFlowRequested += (json, autoRun) => RunFlowJson(FlowName(json), json, null, autoRun);
        pb.FormClosed += (s, e) => ReloadFlows();
        pb.Saved += p => { ReloadFlows(); var f = _flows.FirstOrDefault(x => string.Equals(x.FilePath, p, StringComparison.OrdinalIgnoreCase)); if (f != null) SelectFlow(f); Status("Flow saved: " + p, WbTheme.Green); };
        if (flow != null)
        {
            pb.LoadFlow(flow.Json, flow.FilePath);
            if (flow.BuiltIn)
            {
                pb.CurrentFilePath = null; // saving a built-in makes your own copy in the flows folder
                Status("Editing a copy of the built-in flow \"" + flow.Name + "\": Save stores it in " + FlowCatalog.UserDir, WbTheme.Sky);
            }
        }
        pb.Show(_dockPanel, DockState.Document);
        pb.RefreshTheme();
    }

    static string FlowName(string json)
    {
        try
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var meta = d["Meta"] as Dictionary<string, object>;
            var n = meta != null && meta.ContainsKey("Name") ? meta["Name"] as string : null;
            return string.IsNullOrEmpty(n) ? "Flow" : n;
        }
        catch { return "Flow"; }
    }

    // ── Nim ────────────────────────────────────────────────────────────────────────────────────────────

    bool HasNimPlugin
    {
        get { return AHK2AST.Plugins.PluginRegistry.RegisteredPluginTypes.Any(t => t.Name == "NimTranspilerPlugin"); }
    }

    NimBuildContent OpenNimBuildManager()
    {
        var nim = _dockPanel.Documents.OfType<NimBuildContent>().FirstOrDefault();
        if (nim == null)
        {
            nim = new NimBuildContent(() => AnalysisTarget != null ? AnalysisTarget.Editor.Text : "", () => AnalysisTarget != null ? AnalysisTarget.FilePath : null, _engine);
            UiTheming.Apply(nim);
            nim.Show(_dockPanel, DockState.Document);
        }
        nim.Activate();
        return nim;
    }
}
