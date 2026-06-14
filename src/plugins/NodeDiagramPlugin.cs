using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AHK2AST.Plugins
{
    public class NodeDiagramConfig
    {
        [Category("Node Diagram"), DisplayName("Include Branches"), Description("Visualize internal control flow branches (If, Loop, For, While, Try) inside functions and classes.")]
        public bool IncludeBranches { get; set; }

        [Category("Node Diagram"), DisplayName("Include Library Details"), Description("If true, internal branches and details of included files will also be visualized. If false, only declarations are shown for includes.")]
        public bool IncludeLibraryDetails { get; set; }

        [Category("Node Diagram"), DisplayName("Collapse Includes"), Description("Represent each #Include file as a single compact node. Double-clicking the file node expands its contents.")]
        public bool CollapseIncludes { get; set; }

        [Category("Node Diagram"), DisplayName("Launch Browser"), Description("Automatically open the interactive HTML visualization in your default browser.")]
        public bool LaunchBrowser { get; set; }

        [Category("Node Diagram"), DisplayName("Output Path"), Description("Target file path to write the logic flow diagram (defaults to flow_diagram.html in output directory).")]
        public string OutputPath { get; set; }

        public NodeDiagramConfig()
        {
            IncludeBranches = true;
            IncludeLibraryDetails = false;
            CollapseIncludes = true;
            LaunchBrowser = true;
            OutputPath = "";
        }
    }

    public class NodeDiagramPlugin : IFlowPlugin
    {
        public string Name { get { return "Analysis.NodeDiagram"; } }
        public string Target { get; set; }

        public string Category { get { return "Analysis"; } }
        public string StepTitle { get { return "Logic Flow Diagram"; } }
        public string Icon { get { return "📊"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(NodeDiagramConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (NodeDiagramConfig)config; }

        public NodeDiagramConfig Config { get; set; }

        public NodeDiagramPlugin()
        {
            Config = new NodeDiagramConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return "Error: Root AST node is null.";

            var builder = new LogicFlowGraphBuilder(Config.IncludeBranches, Config.IncludeLibraryDetails, Config.CollapseIncludes);
            builder.Build(root);

            string mermaid = GenerateMermaid(builder);
            string html = GenerateHtml(builder, mermaid);

            try
            {
                string outPath = Config.OutputPath;
                if (string.IsNullOrEmpty(outPath))
                {
                    try
                    {
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                        {
                            outPath = Path.Combine(baseDir, "flow_diagram.html");
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(outPath))
                {
                    outPath = Path.Combine(Path.GetTempPath(), "flow_diagram.html");
                }

                File.WriteAllText(outPath, html, Encoding.UTF8);

                bool isHeadless = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AHK2AST_HEADLESS")) ||
                                  AppDomain.CurrentDomain.FriendlyName.IndexOf("VerifyTest", StringComparison.OrdinalIgnoreCase) >= 0;
                if (Config.LaunchBrowser && !isHeadless)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to output visual flow diagram HTML: " + ex.Message);
            }

            return mermaid;
        }

        private string GenerateMermaid(LogicFlowGraphBuilder builder)
        {
            var sb = new StringBuilder();
            sb.AppendLine("```mermaid");
            sb.AppendLine("graph TD");
            sb.AppendLine("    %% Node styles");
            sb.AppendLine("    classDef global fill:#8aadf4,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine("    classDef hotkey fill:#a6da95,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine("    classDef function fill:#b7bdf8,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine("    classDef classNode fill:#c6a0f6,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine("    classDef method fill:#f5bde6,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine("    classDef branch fill:#f5a97f,stroke:#313244,stroke-width:2px,color:#24273a;");
            sb.AppendLine();

            sb.AppendLine("    %% Nodes");
            foreach (var node in builder.Nodes)
            {
                string safeLabel = node.Label.Replace("\"", "\\\"");
                string styleClass = node.Category.ToLower();
                if (styleClass == "class") styleClass = "classNode";
                sb.AppendLine(string.Format("    {0}[\"{1}\"]:::{2}", node.Id, safeLabel, styleClass));
            }
            sb.AppendLine();

            sb.AppendLine("    %% Edges");
            foreach (var edge in builder.Edges)
            {
                if (string.IsNullOrEmpty(edge.Label))
                {
                    sb.AppendLine(string.Format("    {0} --> {1}", edge.From, edge.To));
                }
                else
                {
                    sb.AppendLine(string.Format("    {0} -->|{1}| {2}", edge.From, edge.Label, edge.To));
                }
            }

            sb.AppendLine("```");
            return sb.ToString();
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private string GenerateHtml(LogicFlowGraphBuilder builder, string mermaid)
        {
            string template = GetHtmlTemplate();

            var nodeJson = new StringBuilder();
            foreach (var node in builder.Nodes)
            {
                string label = node.Label.Replace("\"", "\\\"");
                string title = string.IsNullOrEmpty(node.Tooltip)
                    ? string.Format("Category: {0} | File: {1}", node.Category, node.FilePath)
                    : node.Tooltip;

                string escapedTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
                nodeJson.AppendLine(string.Format("            {{ id: \"{0}\", label: \"{1}\", color: \"{2}\", title: \"{3}\", file: \"{4}\", category: \"{5}\" }},",
                    EscapeJsonString(node.Id), EscapeJsonString(label), EscapeJsonString(node.Color), escapedTitle, EscapeJsonString(node.FilePath), EscapeJsonString(node.Category)));
            }

            var edgeJson = new StringBuilder();
            foreach (var edge in builder.Edges)
            {
                string titleField = string.IsNullOrEmpty(edge.Label) ? "" : string.Format(", title: \"{0}\"", EscapeJsonString(edge.Label));
                edgeJson.AppendLine(string.Format("            {{ from: \"{0}\", to: \"{1}\"{2} }},",
                    EscapeJsonString(edge.From), EscapeJsonString(edge.To), titleField));
            }

            var fileJson = new StringBuilder();
            var uniqueFilePaths = new List<string>();
            foreach (var n in builder.Nodes)
            {
                if (!string.IsNullOrEmpty(n.FilePath) && !uniqueFilePaths.Contains(n.FilePath))
                {
                    uniqueFilePaths.Add(n.FilePath);
                }
            }
            foreach (var filePath in uniqueFilePaths)
            {
                if (filePath != "Main Script" && !filePath.StartsWith("file_"))
                {
                    int defCount = builder.Definitions.Count(d => d.FilePath == filePath);
                    fileJson.AppendLine(string.Format("            {{ path: \"{0}\", name: \"{1}\", isInclude: true, count: {2} }},",
                        EscapeJsonString(filePath),
                        EscapeJsonString(System.IO.Path.GetFileName(filePath)),
                        defCount
                    ));
                }
                else if (filePath == "Main Script")
                {
                    int defCount = builder.Definitions.Count(d => d.FilePath == "Main Script");
                    fileJson.AppendLine(string.Format("            {{ path: \"Main Script\", name: \"Main Script\", isInclude: false, count: {0} }},",
                        defCount
                    ));
                }
            }

            var defJson = new StringBuilder();
            foreach (var def in builder.Definitions)
            {
                var logicTypesStr = string.Join(",", def.LogicTypes.Select(t => "\"" + EscapeJsonString(t) + "\"").ToArray());
                var incomingStr = string.Join(",", def.Incoming.Select(t => "\"" + EscapeJsonString(t) + "\"").ToArray());
                var outgoingStr = string.Join(",", def.Outgoing.Select(t => "\"" + EscapeJsonString(t) + "\"").ToArray());

                defJson.AppendLine(string.Format("            {{ id: \"{0}\", label: \"{1}\", category: \"{2}\", filePath: \"{3}\", line: {4}, column: {5}, code: \"{6}\", parameters: \"{7}\", extends: \"{8}\", logicTypes: [{9}], incoming: [{10}], outgoing: [{11}] }},",
                    EscapeJsonString(def.Id),
                    EscapeJsonString(def.Label),
                    EscapeJsonString(def.Category),
                    EscapeJsonString(def.FilePath),
                    def.Line,
                    def.Column,
                    EscapeJsonString(def.Code),
                    EscapeJsonString(def.Parameters),
                    EscapeJsonString(def.Extends),
                    logicTypesStr,
                    incomingStr,
                    outgoingStr
                ));
            }

            return template
                .Replace("/* NODES_PLACEHOLDER */", nodeJson.ToString())
                .Replace("/* EDGES_PLACEHOLDER */", edgeJson.ToString())
                .Replace("/* FILES_PLACEHOLDER */", fileJson.ToString())
                .Replace("/* DEFINITIONS_PLACEHOLDER */", defJson.ToString())
                .Replace("/* COLLAPSE_INCLUDES_PLACEHOLDER */", Config.CollapseIncludes.ToString().ToLower())
                .Replace("/* MERMAID_PLACEHOLDER */", EscapeJsonString(mermaid));
        }

        private string GetHtmlTemplate()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>AHK2 Codebase Explorer</title>
    <script type=""text/javascript"" src=""https://unpkg.com/vis-network/standalone/umd/vis-network.min.js""></script>
    <link href=""https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap"" rel=""stylesheet"">
    <link rel=""stylesheet"" href=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/themes/prism-tomorrow.min.css"" />
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/prism.min.js""></script>
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/prism/1.29.0/components/prism-clike.min.js""></script>
    <style type=""text/css"">
        :root {
            --bg-main: #0b0c10;
            --bg-panel: #11121d;
            --bg-panel-hover: #181926;
            --bg-header: rgba(17, 18, 29, 0.85);
            --border-color: rgba(255, 255, 255, 0.08);
            --text-main: #cdd6f4;
            --text-muted: #a6adc8;
            --text-dim: #7f849c;
            --accent: #b4befe;
            --accent-hover: #c6a0f6;
            --font-sans: 'Outfit', sans-serif;
            --font-mono: 'JetBrains Mono', monospace;
            
            --color-global: #8aadf4;
            --color-hotkey: #a6da95;
            --color-function: #8bd5ca;
            --color-class: #c6a0f6;
            --color-method: #f5bde6;
            --color-property: #f9e2af;
            --color-branch: #f5a97f;
            --color-include: #b4befe;
        }

        * {
            box-sizing: border-box;
            scrollbar-width: thin;
            scrollbar-color: rgba(255, 255, 255, 0.1) transparent;
        }

        body {
            background-color: var(--bg-main);
            color: var(--text-main);
            font-family: var(--font-sans);
            margin: 0;
            padding: 0;
            height: 100vh;
            overflow: hidden;
            display: flex;
            flex-direction: column;
        }

        /* Header Style */
        header.app-header {
            background-color: var(--bg-header);
            backdrop-filter: blur(12px);
            height: 60px;
            min-height: 60px;
            border-bottom: 1px solid var(--border-color);
            padding: 0 24px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            z-index: 100;
        }

        header.app-header h1 {
            margin: 0;
            font-size: 1.25rem;
            color: var(--accent);
            font-weight: 700;
            display: flex;
            align-items: center;
            gap: 12px;
        }

        header.app-header h1 span.tag {
            font-size: 0.75rem;
            background: rgba(180, 190, 254, 0.12);
            color: var(--accent);
            padding: 3px 8px;
            border-radius: 6px;
            border: 1px solid rgba(180, 190, 254, 0.2);
            font-weight: 500;
        }

        .dashboard-stats {
            display: flex;
            align-items: center;
            gap: 20px;
        }

        .stat-badge {
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-color);
            padding: 4px 10px;
            border-radius: 6px;
            font-size: 0.75rem;
            color: var(--text-muted);
            display: flex;
            align-items: center;
            gap: 6px;
        }

        .stat-badge strong {
            color: var(--accent);
        }

        /* Core Layout Grid */
        .app-layout {
            display: flex;
            flex: 1;
            height: calc(100vh - 60px);
            overflow: hidden;
        }

        /* Three columns config */
        aside.sidebar {
            width: 320px;
            min-width: 320px;
            max-width: 320px;
            background-color: var(--bg-panel);
            border-right: 1px solid var(--border-color);
            display: flex;
            flex-direction: column;
            overflow: hidden;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
        }

        aside.sidebar.collapsed {
            width: 0px !important;
            min-width: 0px !important;
            max-width: 0px !important;
            border-right: none !important;
        }

        main.canvas-panel {
            flex: 1;
            background-color: var(--bg-main);
            position: relative;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        section.wiki-panel {
            width: 420px;
            min-width: 420px;
            max-width: 420px;
            background-color: var(--bg-panel);
            border-left: 1px solid var(--border-color);
            display: flex;
            flex-direction: column;
            overflow-y: auto;
            position: relative;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
        }

        section.wiki-panel.collapsed {
            width: 0px !important;
            min-width: 0px !important;
            max-width: 0px !important;
            border-left: none !important;
        }

        /* Sidebar Tabs & Search */
        .sidebar-search {
            padding: 16px;
            border-bottom: 1px solid var(--border-color);
        }

        .search-input-wrapper {
            position: relative;
            width: 100%;
        }

        .search-input {
            width: 100%;
            background-color: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 10px 14px;
            color: var(--text-main);
            font-family: inherit;
            font-size: 0.85rem;
            transition: all 0.25s ease;
        }

        .search-input:focus {
            outline: none;
            border-color: var(--accent);
            background-color: rgba(255, 255, 255, 0.08);
            box-shadow: 0 0 0 2px rgba(180, 190, 254, 0.2);
        }

        .sidebar-tabs {
            display: flex;
            background: rgba(255, 255, 255, 0.02);
            border-bottom: 1px solid var(--border-color);
            padding: 0 8px;
            gap: 2px;
        }

        .tab-btn {
            flex: 1;
            background: none;
            border: none;
            color: var(--text-dim);
            padding: 12px 2px;
            font-size: 0.78rem;
            font-weight: 600;
            cursor: pointer;
            text-align: center;
            border-bottom: 2px solid transparent;
            transition: all 0.2s ease;
            font-family: inherit;
        }

        .tab-btn:hover {
            color: var(--text-main);
        }

        .tab-btn.active {
            color: var(--accent);
            border-bottom-color: var(--accent);
        }

        .sidebar-content {
            flex: 1;
            overflow-y: auto;
            padding: 16px;
        }

        .tab-pane {
            display: none;
        }

        .tab-pane.active {
            display: block;
        }

        /* Folder Structure Styles */
        .file-header {
            font-size: 0.8rem;
            color: var(--text-muted);
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            margin: 16px 0 8px 0;
            padding-bottom: 4px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.05);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .file-header:first-of-type {
            margin-top: 0;
        }

        .file-header span.badge {
            font-size: 0.7rem;
            background: rgba(255, 255, 255, 0.05);
            padding: 2px 6px;
            border-radius: 4px;
            color: var(--text-dim);
        }

        .def-list-item {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 8px 10px;
            border-radius: 6px;
            cursor: pointer;
            font-size: 0.85rem;
            color: var(--text-muted);
            transition: all 0.15s ease;
            margin-bottom: 4px;
            border: 1px solid transparent;
        }

        .def-list-item:hover {
            background-color: var(--bg-panel-hover);
            color: var(--text-main);
            border-color: rgba(255,255,255,0.03);
        }

        .def-list-item.selected {
            background-color: rgba(180, 190, 254, 0.08);
            border-color: rgba(180, 190, 254, 0.2);
            color: var(--accent);
        }

        .def-indicator {
            width: 8px;
            height: 8px;
            border-radius: 50%;
            flex-shrink: 0;
        }

        .category-global { background-color: var(--color-global); }
        .category-hotkey { background-color: var(--color-hotkey); }
        .category-function { background-color: var(--color-function); }
        .category-class { background-color: var(--color-class); }
        .category-method { background-color: var(--color-method); }
        .category-property { background-color: var(--color-property); }
        .category-branch { background-color: var(--color-branch); }

        /* Workflow Category Buttons */
        .workflow-group {
            display: flex;
            flex-direction: column;
            gap: 10px;
        }

        .workflow-btn {
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 12px 16px;
            text-align: left;
            cursor: pointer;
            transition: all 0.2s ease;
            display: flex;
            justify-content: space-between;
            align-items: center;
            color: var(--text-muted);
            font-family: inherit;
        }

        .workflow-btn:hover {
            background: rgba(255, 255, 255, 0.05);
            border-color: rgba(255, 255, 255, 0.15);
            color: #fff;
            transform: translateX(2px);
        }

        .workflow-btn.active {
            background: rgba(180, 190, 254, 0.06);
            border-color: var(--accent);
            color: var(--accent);
        }

        .workflow-btn h4 {
            margin: 0;
            font-size: 0.85rem;
            font-weight: 600;
        }

        .workflow-btn p {
            margin: 2px 0 0 0;
            font-size: 0.75rem;
            color: var(--text-dim);
        }

        .workflow-btn span.count {
            background: rgba(255, 255, 255, 0.05);
            padding: 4px 8px;
            border-radius: 6px;
            font-size: 0.75rem;
            font-weight: 700;
        }

        .workflow-btn.active span.count {
            background: rgba(180, 190, 254, 0.15);
        }

        /* Canvas Panel Layout */
        #graph-container {
            width: 100%;
            height: 100%;
            background-color: var(--bg-main);
        }

        .graph-toolbar {
            position: absolute;
            top: 20px;
            left: 20px;
            background: rgba(17, 18, 29, 0.85);
            backdrop-filter: blur(8px);
            border: 1px solid var(--border-color);
            padding: 6px 12px;
            border-radius: 8px;
            display: flex;
            gap: 8px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.3);
            z-index: 5;
            flex-wrap: wrap;
            max-width: calc(100% - 40px);
        }

        .toolbar-btn {
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-color);
            color: var(--text-muted);
            padding: 6px 12px;
            font-size: 0.75rem;
            font-weight: 500;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s ease;
            font-family: inherit;
            display: inline-flex;
            align-items: center;
            gap: 6px;
        }

        .toolbar-btn:hover {
            background: rgba(255, 255, 255, 0.08);
            border-color: rgba(255,255,255,0.15);
            color: #fff;
        }

        .toolbar-btn.active {
            background: var(--accent);
            color: #11121d;
            border-color: var(--accent);
            font-weight: 600;
        }

        .legend-overlay {
            position: absolute;
            bottom: 20px;
            left: 20px;
            background: rgba(17, 18, 29, 0.85);
            backdrop-filter: blur(8px);
            border: 1px solid var(--border-color);
            padding: 10px 14px;
            border-radius: 8px;
            display: flex;
            gap: 14px;
            font-size: 0.75rem;
            color: var(--text-dim);
            pointer-events: none;
            z-index: 5;
            box-shadow: 0 4px 20px rgba(0,0,0,0.2);
        }

        .legend-indicator {
            display: flex;
            align-items: center;
            gap: 6px;
        }

        /* Wiki Details Panel */
        .wiki-empty-state {
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            flex: 1;
            padding: 40px;
            text-align: center;
            color: var(--text-dim);
        }

        .wiki-empty-state svg {
            stroke: var(--text-dim);
            margin-bottom: 16px;
            opacity: 0.6;
        }

        .wiki-empty-state h3 {
            margin: 0 0 8px 0;
            color: var(--text-muted);
            font-size: 1.1rem;
            font-weight: 600;
        }

        .wiki-empty-state p {
            margin: 0;
            font-size: 0.85rem;
            line-height: 1.4;
        }

        .wiki-details-content {
            padding: 24px;
            display: flex;
            flex-direction: column;
            gap: 20px;
        }

        .wiki-header {
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 16px;
        }

        .wiki-title-row {
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            gap: 12px;
            margin-bottom: 8px;
        }

        .wiki-title {
            margin: 0;
            font-size: 1.35rem;
            font-weight: 700;
            color: #fff;
            letter-spacing: -0.02em;
            word-break: break-all;
        }

        .wiki-badge {
            background-color: var(--color-function);
            color: #0b0c10;
            padding: 3px 8px;
            border-radius: 4px;
            font-size: 0.7rem;
            font-weight: 700;
            text-transform: uppercase;
            flex-shrink: 0;
        }

        .wiki-tags {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
        }

        .wiki-tag {
            font-size: 0.7rem;
            background: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-color);
            color: var(--text-muted);
            padding: 2px 8px;
            border-radius: 4px;
            font-weight: 600;
            text-transform: uppercase;
        }

        .wiki-meta-box {
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 14px;
            font-size: 0.8rem;
            display: flex;
            flex-direction: column;
            gap: 8px;
        }

        .meta-row {
            display: flex;
            justify-content: space-between;
        }

        .meta-label {
            color: var(--text-dim);
        }

        .meta-value {
            color: var(--text-muted);
            font-weight: 500;
            word-break: break-all;
            text-align: right;
        }

        .wiki-section-title {
            font-size: 0.85rem;
            text-transform: uppercase;
            color: var(--accent);
            font-weight: 700;
            margin: 0 0 10px 0;
            letter-spacing: 0.05em;
            border-left: 3px solid var(--accent);
            padding-left: 8px;
        }

        /* X-Refs Listings */
        .xref-list {
            display: flex;
            flex-direction: column;
            gap: 6px;
        }

        .xref-item {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 8px 12px;
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            font-size: 0.8rem;
            cursor: pointer;
            transition: all 0.15s ease;
            color: var(--text-muted);
        }

        .xref-item:hover {
            background: var(--bg-panel-hover);
            border-color: rgba(255, 255, 255, 0.15);
            color: #fff;
            transform: translateX(2px);
        }

        .xref-empty {
            font-size: 0.8rem;
            color: var(--text-dim);
            font-style: italic;
        }

        /* Codeblock styling */
        .code-container {
            position: relative;
            border-radius: 8px;
            overflow: hidden;
            border: 1px solid var(--border-color);
            background-color: #1d1f21;
        }

        pre[class*=""language-""] {
            margin: 0 !important;
            padding: 14px !important;
            background: transparent !important;
            font-family: var(--font-mono) !important;
            font-size: 0.8rem !important;
            line-height: 1.5 !important;
            overflow-x: auto;
        }

        code[class*=""language-""] {
            font-family: var(--font-mono) !important;
            text-shadow: none !important;
        }

        .copy-btn {
            position: absolute;
            top: 8px;
            right: 8px;
            background: rgba(255, 255, 255, 0.06);
            border: 1px solid var(--border-color);
            color: var(--text-muted);
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.7rem;
            font-family: inherit;
            cursor: pointer;
            transition: all 0.2s ease;
            backdrop-filter: blur(4px);
        }

        .copy-btn:hover {
            background: rgba(255, 255, 255, 0.12);
            color: #fff;
        }

        /* Tooltip Vis.js Style Overrides */
        div.vis-tooltip {
            background-color: #11121d !important;
            color: var(--text-main) !important;
            border: 1px solid var(--border-color) !important;
            border-radius: 8px !important;
            padding: 12px !important;
            font-family: var(--font-sans) !important;
            font-size: 0.8rem !important;
            box-shadow: 0 4px 24px rgba(0,0,0,0.5) !important;
            max-width: 320px !important;
        }

        /* Upgraded select styles for Finder */
        .finder-select {
            width: 100%;
            background-color: rgba(255, 255, 255, 0.04);
            border: 1px solid var(--border-color);
            color: var(--text-main);
            border-radius: 6px;
            padding: 8px 12px;
            font-size: 0.85rem;
            font-family: inherit;
            margin-bottom: 10px;
        }
        .finder-select:focus {
            outline: none;
            border-color: var(--accent);
        }
    </style>
</head>
<body>

    <!-- App Header -->
    <header class=""app-header"">
        <div style=""display: flex; align-items: center; gap: 16px;"">
            <button id=""btn-toggle-sidebar"" class=""toolbar-btn active"" title=""Toggle Left Sidebar"">📂 Sidebar</button>
            <h1>AHK2 Explorer <span class=""tag"">Interactive v4.0</span></h1>
        </div>
        
        <div class=""dashboard-stats"">
            <button id=""btn-copy-mermaid"" class=""toolbar-btn"" style=""background: rgba(198, 160, 246, 0.08); border-color: rgba(198, 160, 246, 0.2); color: var(--accent-hover);"">📋 Copy Mermaid</button>
            <button id=""btn-export-png"" class=""toolbar-btn"" style=""background: rgba(166, 218, 149, 0.08); border-color: rgba(166, 218, 149, 0.2); color: var(--color-hotkey);"">💾 Export PNG</button>
            <div class=""stat-badge"">Files: <strong id=""stat-files"">0</strong></div>
            <div class=""stat-badge"">Functions: <strong id=""stat-funcs"">0</strong></div>
            <div class=""stat-badge"">Classes: <strong id=""stat-classes"">0</strong></div>
            <div class=""stat-badge"">Connections: <strong id=""stat-edges"">0</strong></div>
            <button id=""btn-toggle-wiki"" class=""toolbar-btn active"" title=""Toggle Right Details"">📖 Inspector</button>
        </div>
    </header>

    <!-- Main Layout Container -->
    <div class=""app-layout"">
        
        <!-- Left Sidebar (Explorers) -->
        <aside class=""sidebar"">
            <div class=""sidebar-search"">
                <div class=""search-input-wrapper"">
                    <input type=""text"" id=""search-box"" class=""search-input"" placeholder=""Search functions, classes, code..."" />
                </div>
                <div style=""margin-top: 8px; display: flex; gap: 8px;"">
                    <button id=""quick-btn-global"" class=""toolbar-btn"" style=""flex: 2; justify-content: center; background: rgba(180, 190, 254, 0.08); border-color: rgba(180, 190, 254, 0.2); color: var(--accent); padding: 4px 8px; font-size: 0.75rem;"">🌐 Global</button>
                    <button id=""quick-btn-collapse-all"" class=""toolbar-btn"" style=""flex: 1.5; justify-content: center; padding: 4px 8px; font-size: 0.75rem;"">Collapse All</button>
                    <button id=""quick-btn-expand-all"" class=""toolbar-btn"" style=""flex: 1.5; justify-content: center; padding: 4px 8px; font-size: 0.75rem;"">Expand All</button>
                </div>
            </div>
            
            <div class=""sidebar-tabs"">
                <button id=""tab-btn-modules"" class=""tab-btn active"">📑 Files</button>
                <button id=""tab-btn-workflows"" class=""tab-btn"">⚡ Workflows</button>
                <button id=""tab-btn-strands"" class=""tab-btn"">🕸️ Strands</button>
                <button id=""tab-btn-finder"" class=""tab-btn"">🧭 Finder</button>
                <button id=""tab-btn-index"" class=""tab-btn"">🔍 Index</button>
            </div>
            
            <div class=""sidebar-content"">
                <!-- Modules and Files Explorer -->
                <div id=""tab-modules"" class=""tab-pane active"">
                    <div id=""file-explorer-tree"">
                        <!-- Populated Dynamically -->
                    </div>
                </div>
                
                <!-- Logic Workflow Categories -->
                <div id=""tab-workflows"" class=""tab-pane"">
                    <div class=""workflow-group"">
                        <button class=""workflow-btn"" data-workflow=""gui"">
                            <div>
                                <h4>🖥️ GUI Workflows</h4>
                                <p>WPF, Window UI, and Controls</p>
                            </div>
                            <span class=""count"" id=""count-gui"">0</span>
                        </button>
                        <button class=""workflow-btn"" data-workflow=""event"">
                            <div>
                                <h4>🔔 Event-Driven Workflows</h4>
                                <p>Hotkeys and Input Event callbacks</p>
                            </div>
                            <span class=""count"" id=""count-event"">0</span>
                        </button>
                        <button class=""workflow-btn"" data-workflow=""async"">
                            <div>
                                <h4>🔄 Async & Callbacks</h4>
                                <p>Timers, Threads, and external DLLs</p>
                            </div>
                            <span class=""count"" id=""count-async"">0</span>
                        </button>
                        <button class=""workflow-btn"" data-workflow=""state"">
                            <div>
                                <h4>⚙️ State & Master Flags</h4>
                                <p>Global configurations and assume-global code</p>
                            </div>
                            <span class=""count"" id=""count-state"">0</span>
                        </button>
                        <button class=""workflow-btn"" data-workflow=""core"">
                            <div>
                                <h4>📦 Core Logic</h4>
                                <p>Standard functions and internal helpers</p>
                            </div>
                            <span class=""count"" id=""count-core"">0</span>
                        </button>
                    </div>
                    
                    <div id=""workflow-list-header"" style=""display:none; margin-top:20px;"">
                        <div class=""file-header"">Active Workflow Results</div>
                        <div id=""workflow-results-container""></div>
                    </div>
                </div>
                
                <!-- Strands Explorer -->
                <div id=""tab-strands"" class=""tab-pane"">
                    <div id=""strands-container"">
                        <!-- Populated Dynamically -->
                    </div>
                </div>

                <!-- Path/Route Finder tab -->
                <div id=""tab-finder"" class=""tab-pane"">
                    <div class=""wiki-meta-box"" style=""margin-bottom: 16px;"">
                        <h4 style=""margin: 0 0 10px 0; font-size: 0.85rem; color: var(--accent);"">Compass Route Explorer</h4>
                        <p style=""margin: 0 0 14px 0; font-size: 0.78rem; line-height: 1.4; color: var(--text-muted);"">Explore dynamic execution chains by finding the shortest directed path between any two symbols.</p>
                        
                        <label style=""font-size: 0.72rem; text-transform: uppercase; color: var(--text-dim); display: block; margin-bottom: 4px; font-weight: bold;"">Start Node</label>
                        <select id=""path-start-select"" class=""finder-select"">
                            <option value="""">-- Select Start --</option>
                        </select>

                        <label style=""font-size: 0.72rem; text-transform: uppercase; color: var(--text-dim); display: block; margin-bottom: 4px; font-weight: bold;"">End Node</label>
                        <select id=""path-end-select"" class=""finder-select"">
                            <option value="""">-- Select End --</option>
                        </select>

                        <div style=""display: flex; gap: 8px; margin-top: 8px;"">
                            <button id=""path-find-btn"" class=""toolbar-btn active"" style=""flex: 1.5; justify-content: center;"">Find Route</button>
                            <button id=""path-clear-btn"" class=""toolbar-btn"" style=""flex: 1; justify-content: center;"">Clear</button>
                        </div>
                    </div>

                    <div class=""file-header"">Steps in Route</div>
                    <div id=""path-results-list"">
                        <div class=""xref-empty"">No active search. Select nodes above to map path.</div>
                    </div>
                </div>

                <!-- Alphabetical Flat Index -->
                <div id=""tab-index"" class=""tab-pane"">
                    <div id=""flat-index-container"">
                        <!-- Populated Dynamically -->
                    </div>
                </div>
            </div>
        </aside>
        
        <!-- Center Panel (Vis.js Graph Container) -->
        <main class=""canvas-panel"">
            <div id=""graph-container""></div>
            
            <!-- Floating Toolbar -->
            <div class=""graph-toolbar"">
                <button id=""toolbar-branches"" class=""toolbar-btn active"">Branches: Shown</button>
                <button id=""toolbar-drag"" class=""toolbar-btn"">Dragging: Off</button>
                <span style=""border-left: 1px solid var(--border-color); height: 20px; align-self: center; margin: 0 4px;""></span>
                <button id=""mode-view-full"" class=""toolbar-btn active"">Full</button>
                <button id=""mode-view-focus"" class=""toolbar-btn"">Focus Node</button>
                <button id=""mode-view-includes"" class=""toolbar-btn"">Includes-Only</button>
                <button id=""mode-view-classes"" class=""toolbar-btn"">Classes-Only</button>
                <span style=""border-left: 1px solid var(--border-color); height: 20px; align-self: center; margin: 0 4px;""></span>
                <button id=""toolbar-layout"" class=""toolbar-btn active"">Layout: Flow</button>
                <button id=""toolbar-direction"" class=""toolbar-btn"">Direction: Vertical</button>
                <button id=""toolbar-physics"" class=""toolbar-btn"">Physics: Off</button>
                <button id=""toolbar-zoom-in"" class=""toolbar-btn"">➕</button>
                <button id=""toolbar-zoom-out"" class=""toolbar-btn"">➖</button>
                <button id=""toolbar-fit"" class=""toolbar-btn"">Fit</button>
                <button id=""toolbar-reset"" class=""toolbar-btn"">Reset</button>
            </div>

            <!-- Legend Overlay -->
            <div class=""legend-overlay"">
                <div class=""legend-indicator""><div class=""def-indicator category-global""></div>Global</div>
                <div class=""legend-indicator""><div class=""def-indicator category-hotkey""></div>Hotkey</div>
                <div class=""legend-indicator""><div class=""def-indicator category-function""></div>Function</div>
                <div class=""legend-indicator""><div class=""def-indicator category-class""></div>Class</div>
                <div class=""legend-indicator""><div class=""def-indicator category-method""></div>Method</div>
                <div class=""legend-indicator""><div class=""def-indicator category-property""></div>Property</div>
                <div class=""legend-indicator""><div class=""def-indicator category-branch""></div>Branch</div>
                <div class=""legend-indicator""><div class=""def-indicator category-include""></div>Include</div>
            </div>
        </main>
        
        <!-- Right Panel (Wiki Details Panel) -->
        <section class=""wiki-panel"">
            
            <!-- Empty State -->
            <div id=""wiki-empty"" class=""wiki-empty-state"">
                <svg width=""48"" height=""48"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""1.5"">
                    <path d=""M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25"" stroke-linecap=""round"" stroke-linejoin=""round""/>
                </svg>
                <h3>Select a Node to Explore</h3>
                <p>Click any node in the graph, files sidebar, or flat index list to inspect its source code, dependencies, logical workflow, and caller hierarchy.</p>
            </div>
            
            <!-- Details Container (Hidden by default) -->
            <div id=""wiki-details"" class=""wiki-details-content"">
                <div class=""wiki-header"">
                    <div class=""wiki-title-row"">
                        <h2 class=""wiki-title"" id=""wiki-node-title"">MyFunction</h2>
                        <span class=""wiki-badge"" id=""wiki-node-badge"">Function</span>
                    </div>
                    <div style=""margin-top: 8px; display: flex; gap: 6px; margin-bottom: 8px; flex-wrap: wrap;"">
                        <button id=""wiki-btn-focus"" class=""toolbar-btn"" style=""background: rgba(180, 190, 254, 0.08); border-color: rgba(180, 190, 254, 0.2); color: var(--accent); padding: 4px 10px; font-size: 0.75rem;"">🔍 Isolate Strand</button>
                        <button id=""wiki-btn-locate"" class=""toolbar-btn"" style=""padding: 4px 10px; font-size: 0.75rem;"">📍 Locate in Graph</button>
                        <button id=""wiki-btn-set-start"" class=""toolbar-btn"" style=""background: rgba(166, 218, 149, 0.04); border-color: rgba(166, 218, 149, 0.1); color: var(--color-hotkey); padding: 4px 10px; font-size: 0.75rem;"">🚩 Start Route</button>
                        <button id=""wiki-btn-set-end"" class=""toolbar-btn"" style=""background: rgba(245, 169, 127, 0.04); border-color: rgba(245, 169, 127, 0.1); color: var(--color-branch); padding: 4px 10px; font-size: 0.75rem;"">🏁 End Route</button>
                    </div>
                    <div class=""wiki-tags"" id=""wiki-node-tags"">
                        <span class=""wiki-tag"">gui</span>
                        <span class=""wiki-tag"">async</span>
                    </div>
                </div>
                
                <!-- Insights Meta Box -->
                <div id=""wiki-insights-box"" class=""wiki-meta-box"" style=""background: rgba(180, 190, 254, 0.03); border-color: rgba(180, 190, 254, 0.15);"">
                    <h4 style=""margin: 0 0 8px 0; font-size: 0.82rem; color: var(--accent); text-transform: uppercase; letter-spacing: 0.02em;"">Static Insights</h4>
                    <div class=""meta-row"" style=""margin-bottom: 6px;"">
                        <span class=""meta-label"">Complexity (McCabe Branch):</span>
                        <span class=""meta-value"" id=""wiki-insight-mccabe"" style=""font-weight: bold;"">1</span>
                    </div>
                    <div class=""meta-row"" style=""margin-bottom: 6px;"">
                        <span class=""meta-label"">Impact Score (Upstream + Down):</span>
                        <span class=""meta-value"" id=""wiki-insight-impact"" style=""font-weight: bold;"">0 (0 in + 0 out)</span>
                    </div>
                    <div id=""wiki-insight-orphan"" style=""display: none; margin-top: 8px; padding: 6px 10px; background: rgba(243, 139, 168, 0.1); border: 1px solid rgba(243, 139, 168, 0.25); border-radius: 6px; font-size: 0.75rem; color: #f38ba8; text-align: center; font-weight: bold;"">
                        ⚠️ DEAD CODE CANDIDATE: Uncalled / Orphan Method
                    </div>
                </div>

                <!-- File Meta Box -->
                <div class=""wiki-meta-box"">
                    <div class=""meta-row"">
                        <span class=""meta-label"">Defined In:</span>
                        <span class=""meta-value"" id=""wiki-meta-file"">Main Script</span>
                    </div>
                    <div class=""meta-row"">
                        <span class=""meta-label"">Line Range:</span>
                        <span class=""meta-value"" id=""wiki-meta-lines"">Line 42</span>
                    </div>
                </div>
                
                <!-- Called By (X-Refs) -->
                <div>
                    <h3 class=""wiki-section-title"">Called By (X-Refs)</h3>
                    <div class=""xref-list"" id=""wiki-refs-incoming"">
                        <!-- Populated Dynamically -->
                    </div>
                </div>
                
                <!-- Calls (Dependencies) -->
                <div>
                    <h3 class=""wiki-section-title"">Calls (Dependencies)</h3>
                    <div class=""xref-list"" id=""wiki-refs-outgoing"">
                        <!-- Populated Dynamically -->
                    </div>
                </div>
                
                <!-- Source Code -->
                <div>
                    <h3 class=""wiki-section-title"">Source Code</h3>
                    <div class=""code-container"">
                        <button class=""copy-btn"" id=""wiki-code-copy"">Copy</button>
                        <pre><code class=""language-javascript"" id=""wiki-code-block"">Loading...</code></pre>
                    </div>
                </div>
            </div>
            
        </section>
        
    </div>

    <!-- Graph and Explore script -->
    <script type=""text/javascript"">
        // Data injected by C# plugin
        var rawFiles = [
/* FILES_PLACEHOLDER */
        ];

        var rawDefinitions = [
/* DEFINITIONS_PLACEHOLDER */
        ];

        var rawNodes = [
/* NODES_PLACEHOLDER */
        ];

        var rawEdges = [
/* EDGES_PLACEHOLDER */
        ];

        var collapseIncludesDefault = /* COLLAPSE_INCLUDES_PLACEHOLDER */;
        var rawMermaid = ""/* MERMAID_PLACEHOLDER */"";

        // --- Core Indexes & Navigation States ---
        var originalLabels = {};
        var collapsedNodes = new Set();
        var collapsedFiles = new Set();
        var selectedDefinitionId = null;
        var currentLayout = 'hierarchical'; // 'hierarchical' or 'free'
        var currentDirection = 'UD'; // 'UD' (vertical) or 'LR' (horizontal)
        var showBranches = true;
        var activeWorkflowFilter = null; // null or 'gui', 'event', 'async', 'state', 'core'
        var activeFocusNode = null; // Node isolated/focused
        var currentViewMode = 'full'; // 'full', 'neighborhood', 'includes', 'classes'
        var dragNodesEnabled = false; // default read-only
        var activeRoute = null;

        // Build mapping of dependencies
        var outgoingEdges = {};
        rawNodes.forEach(function(n) {
            originalLabels[n.id] = n.label;
            outgoingEdges[n.id] = [];
            // If it is a class, method, function (i.e. collapsible definition), collapse by default unless it's global
            if (n.id !== 'global_code' && n.id.indexOf('hotkey_') !== 0 && n.id.indexOf('file_') !== 0) {
                collapsedNodes.add(n.id);
            }
        });
        rawEdges.forEach(function(e) {
            if (outgoingEdges[e.from]) {
                outgoingEdges[e.from].push(e.to);
            }
        });

        // Initialize default include files collapsing
        if (collapseIncludesDefault) {
            rawFiles.forEach(function(f) {
                if (f.isInclude) {
                    collapsedFiles.add(f.path);
                }
            });
        }

        // Set up statistics counters in the header
        document.getElementById('stat-files').innerText = rawFiles.length;
        document.getElementById('stat-funcs').innerText = rawDefinitions.filter(function(d) { return d.category === 'Function'; }).length;
        document.getElementById('stat-classes').innerText = rawDefinitions.filter(function(d) { return d.category === 'Class'; }).length;
        document.getElementById('stat-edges').innerText = rawEdges.length;

        // Set up workflow badge counts
        ['gui', 'event', 'async', 'state', 'core'].forEach(function(t) {
            var count = rawDefinitions.filter(function(d) { return d.logicTypes.indexOf(t) !== -1; }).length;
            document.getElementById('count-' + t).innerText = count;
        });

        // Determine if node has child control flow or definitions inside
        function hasChildren(nodeId) {
            return outgoingEdges[nodeId] && outgoingEdges[nodeId].length > 0;
        }

        // Isolate flow helper (DFS/BFS for incoming and outgoing nodes)
        function getFocusedSubGraphNodes(focusNodeId) {
            var combined = new Set();
            combined.add(focusNodeId);

            // 1. Descendants
            var queue = [focusNodeId];
            var visited = new Set();
            while (queue.length > 0) {
                var curr = queue.shift();
                if (!visited.has(curr)) {
                    visited.add(curr);
                    combined.add(curr);
                    var children = outgoingEdges[curr] || [];
                    children.forEach(function(c) {
                        queue.push(c);
                    });
                }
            }

            // 2. Ancestors
            var incomingEdges = {};
            rawEdges.forEach(function(e) {
                if (!incomingEdges[e.to]) incomingEdges[e.to] = [];
                incomingEdges[e.to].push(e.from);
            });

            queue = [focusNodeId];
            visited = new Set();
            while (queue.length > 0) {
                var curr = queue.shift();
                if (!visited.has(curr)) {
                    visited.add(curr);
                    combined.add(curr);
                    var parents = incomingEdges[curr] || [];
                    parents.forEach(function(p) {
                        queue.push(p);
                    });
                }
            }

            return combined;
        }

        // BFS path finder
        function findRoute(startId, endId) {
            if (!startId || !endId) return null;
            if (startId === endId) return [startId];
            
            var queue = [[startId]];
            var visited = new Set();
            visited.add(startId);
            
            while (queue.length > 0) {
                var path = queue.shift();
                var node = path[path.length - 1];
                var neighbors = outgoingEdges[node] || [];
                for (var i = 0; i < neighbors.length; i++) {
                    var next = neighbors[i];
                    if (next === endId) {
                        return path.concat([next]);
                    }
                    if (!visited.has(next)) {
                        visited.add(next);
                        queue.push(path.concat([next]));
                    }
                }
            }
            return null;
        }

        // Calculate visible nodes based on collapsed states and view modes
        function getVisibleNodes() {
            var visible = new Set();
            var queue = [];
            
            rawNodes.forEach(function(n) {
                if (n.id === 'global_code' || n.id.indexOf('hotkey_') === 0 || n.color === '#b4befe') {
                    visible.add(n.id);
                    queue.push(n.id);
                }
            });

            while (queue.length > 0) {
                var current = queue.shift();
                
                if (collapsedNodes.has(current)) {
                    continue;
                }

                if (current.indexOf('file_') === 0) {
                    var filename = current.substring(5);
                    if (collapsedFiles.has(filename)) {
                        continue;
                    }
                }

                var children = outgoingEdges[current] || [];
                children.forEach(function(childId) {
                    var childNodeObj = rawNodes.find(function(rn) { return rn.id === childId; });
                    if (childNodeObj && childNodeObj.file && childNodeObj.file !== 'Main Script') {
                        if (collapsedFiles.has(childNodeObj.file)) {
                            return; // skip if include file node is collapsed
                        }
                    }

                    if (!visible.has(childId)) {
                        visible.add(childId);
                        queue.push(childId);
                    }
                });
            }

            // Apply Neighborhood Focus mode
            if (currentViewMode === 'neighborhood' && activeHighlightNode) {
                var neighbors = new Set();
                neighbors.add(activeHighlightNode);
                rawEdges.forEach(function(e) {
                    if (e.from === activeHighlightNode) neighbors.add(e.to);
                    if (e.to === activeHighlightNode) neighbors.add(e.from);
                });
                var intersection = new Set();
                visible.forEach(function(id) {
                    if (neighbors.has(id)) intersection.add(id);
                });
                return intersection;
            }

            // Apply Isolate focus strand filter
            if (activeFocusNode) {
                var focusSet = getFocusedSubGraphNodes(activeFocusNode);
                var intersection = new Set();
                visible.forEach(function(id) {
                    if (focusSet.has(id)) {
                        intersection.add(id);
                    }
                });
                intersection.add(activeFocusNode);
                return intersection;
            }

            return visible;
        }

        // Get dynamic nodes & edges for Vis.js rendering
        function getDynamicGraphData() {
            // Includes-only mode
            if (currentViewMode === 'includes') {
                var includeNodes = rawNodes.filter(function(n) {
                    return n.id === 'global_code' || n.id.indexOf('file_') === 0;
                });
                var includeEdges = [];
                var edgeSeen = new Set();
                rawEdges.forEach(function(e) {
                    var fromNode = rawNodes.find(function(rn) { return rn.id === e.from; });
                    var toNode = rawNodes.find(function(rn) { return rn.id === e.to; });
                    if (fromNode && toNode) {
                        var fromFile = fromNode.id.indexOf('file_') === 0 ? fromNode.id : (fromNode.file && fromNode.file !== 'Main Script' ? 'file_' + fromNode.file : 'global_code');
                        var toFile = toNode.id.indexOf('file_') === 0 ? toNode.id : (toNode.file && toNode.file !== 'Main Script' ? 'file_' + toNode.file : 'global_code');
                        if (fromFile !== toFile) {
                            var key = fromFile + ""->"" + toFile;
                            if (!edgeSeen.has(key)) {
                                edgeSeen.add(key);
                                includeEdges.push({ from: fromFile, to: toFile });
                            }
                        }
                    }
                });
                return { nodes: includeNodes, edges: includeEdges };
            }

            // Classes-only mode
            if (currentViewMode === 'classes') {
                var classNodes = rawNodes.filter(function(n) {
                    return n.color === '#c6a0f6';
                });
                var classIds = new Set(classNodes.map(function(n) { return n.id; }));
                var classEdges = [];
                var edgeSeen = new Set();
                
                rawDefinitions.forEach(function(d) {
                    if (d.category === 'Class' && d.extends) {
                        var baseClassId = 'class_' + d.extends;
                        if (classIds.has(baseClassId)) {
                            var key = d.id + ""->extends->"" + baseClassId;
                            edgeSeen.add(key);
                            classEdges.push({ from: d.id, to: baseClassId, title: 'extends', label: 'extends', color: { color: '#c6a0f6' } });
                        }
                    }
                });
                
                rawEdges.forEach(function(e) {
                    var fromNode = rawNodes.find(function(rn) { return rn.id === e.from; });
                    var toNode = rawNodes.find(function(rn) { return rn.id === e.to; });
                    if (fromNode && toNode) {
                        var fromClass = fromNode.id.indexOf('class_') === 0 ? fromNode.id : (fromNode.id.startsWith('method_') || fromNode.id.startsWith('prop_') ? 'class_' + fromNode.id.split('_')[1] : null);
                        var toClass = toNode.id.indexOf('class_') === 0 ? toNode.id : (toNode.id.startsWith('method_') || toNode.id.startsWith('prop_') ? 'class_' + toNode.id.split('_')[1] : null);
                        if (fromClass && toClass && fromClass !== toClass && classIds.has(fromClass) && classIds.has(toClass)) {
                            var key = fromClass + ""->"" + toClass;
                            if (!edgeSeen.has(key)) {
                                edgeSeen.add(key);
                                classEdges.push({ from: fromClass, to: toClass, title: 'references' });
                            }
                        }
                    }
                });
                return { nodes: classNodes, edges: classEdges };
            }

            var visibleNodesSet = getVisibleNodes();
            
            var visibleNodesArray = rawNodes.filter(function(n) {
                var isVisible = visibleNodesSet.has(n.id);
                if (!isVisible) return false;
                
                if (!showBranches && n.color === '#f5a97f') return false;

                // If active workflow filter is set, check if matching
                if (activeWorkflowFilter) {
                    if (n.id !== 'global_code' && n.id.indexOf('file_') !== 0) {
                        var def = rawDefinitions.find(function(d) { return d.id === n.id; });
                        if (!def || def.logicTypes.indexOf(activeWorkflowFilter) === -1) {
                            return false;
                        }
                    }
                }
                return true;
            });

            function getRedirectedNodeId(nodeId) {
                var n = rawNodes.find(function(rn) { return rn.id === nodeId; });
                if (!n) return nodeId;
                if (n.file && n.file !== 'Main Script' && collapsedFiles.has(n.file)) {
                    return 'file_' + n.file;
                }
                return nodeId;
            }

            var rawEdgesToUse = rawEdges;

            if (!showBranches) {
                var bypassedEdges = [];
                function findNonBranchDescendants(nodeId, visited) {
                    if (visited.has(nodeId)) return [];
                    visited.add(nodeId);
                    
                    var nodeObj = rawNodes.find(function(rn) { return rn.id === nodeId; });
                    if (!nodeObj) return [];
                    if (nodeObj.color !== '#f5a97f') {
                        return [nodeId];
                    }
                    
                    var results = [];
                    var children = outgoingEdges[nodeId] || [];
                    children.forEach(function(childId) {
                        results = results.concat(findNonBranchDescendants(childId, visited));
                    });
                    return results;
                }

                rawEdges.forEach(function(e) {
                    var fromNode = rawNodes.find(function(rn) { return rn.id === e.from; });
                    if (!fromNode || fromNode.color === '#f5a97f') return;

                    var targets = findNonBranchDescendants(e.to, new Set());
                    targets.forEach(function(targetId) {
                        bypassedEdges.push({ from: e.from, to: targetId, title: e.title });
                    });
                });
                rawEdgesToUse = bypassedEdges;
            }

            var redirectedEdges = [];
            rawEdgesToUse.forEach(function(e) {
                var fromRedir = getRedirectedNodeId(e.from);
                var toRedir = getRedirectedNodeId(e.to);
                if (fromRedir !== toRedir) {
                    redirectedEdges.push({ from: fromRedir, to: toRedir, title: e.title });
                }
            });

            var activeEdges = [];
            var seen = new Set();
            redirectedEdges.forEach(function(e, idx) {
                var key = e.from + ""->"" + e.to + (e.title || """");
                if (!seen.has(key)) {
                    seen.add(key);
                    activeEdges.push(Object.assign({ id: idx }, e));
                }
            });

            return {
                nodes: visibleNodesArray,
                edges: activeEdges
            };
        }

        // Generate Vis.js network layout options
        function getNetworkOptions(layoutType, direction) {
            var opts = {
                nodes: {
                    shape: 'dot',
                    size: 20,
                    font: {
                        color: '#cdd6f4',
                        size: 13,
                        face: 'Outfit',
                        strokeWidth: 2,
                        strokeColor: '#0b0c10'
                    },
                    borderWidth: 2,
                    color: { border: '#11121d' },
                    shadow: { enabled: true, color: 'rgba(0,0,0,0.5)', size: 4, x: 2, y: 2 }
                },
                edges: {
                    arrows: { to: { enabled: true, scaleFactor: 1, type: 'arrow' } },
                    color: { color: 'rgba(255,255,255,0.12)', highlight: '#b4befe', hover: '#b4befe' },
                    width: 2.0,
                    shadow: { enabled: true, color: 'rgba(0,0,0,0.3)', size: 2, x: 1, y: 1 },
                    smooth: {
                        enabled: true,
                        type: layoutType === 'hierarchical' ? 'cubicBezier' : 'continuous',
                        roundness: 0.5
                    }
                },
                interaction: { hover: true, tooltipDelay: 120, navigationButtons: true, keyboard: true, dragNodes: dragNodesEnabled }
            };

            if (layoutType === 'hierarchical') {
                opts.layout = {
                    hierarchical: {
                        enabled: true,
                        direction: direction,
                        sortMethod: 'directed',
                        nodeSpacing: 200,
                        levelSeparation: 140,
                        blockShifting: true,
                        edgeMinimization: true,
                        parentCentralization: true
                    }
                };
                opts.physics = { enabled: false };
            } else {
                opts.layout = { hierarchical: { enabled: false } };
                opts.physics = {
                    enabled: document.getElementById('toolbar-physics').classList.contains('active'),
                    solver: 'barnesHut',
                    barnesHut: {
                        gravitationalConstant: -4000,
                        centralGravity: 0.3,
                        springLength: 130,
                        springConstant: 0.05,
                        damping: 0.09,
                        avoidOverlap: 0.8
                    },
                    stabilization: { enabled: true, iterations: 100 }
                };
            }
            return opts;
        }

        // Initialize vis datasets
        var visNodes = new vis.DataSet([]);
        var visEdges = new vis.DataSet([]);
        var container = document.getElementById('graph-container');
        var graphData = { nodes: visNodes, edges: visEdges };
        var network = new vis.Network(container, graphData, getNetworkOptions(currentLayout, currentDirection));

        // Rebuild graph elements based on filters, collapsing, and routing
        function rebuildGraph() {
            var dataObj = getDynamicGraphData();
            var currentNodes = visNodes.get();
            
            function makeHtmlTooltip(htmlString) {
                var el = document.createElement('div');
                el.innerHTML = htmlString;
                return el;
            }

            // If an active route is calculated, overlay opacity changes
            if (activeRoute) {
                var routeSet = new Set(activeRoute);
                var targetNodes = dataObj.nodes.map(function(n) {
                    var isOnPath = routeSet.has(n.id);
                    var nodeOpts = {
                        id: n.id,
                        label: originalLabels[n.id],
                        color: isOnPath ? { background: n.color, border: '#a6da95' } : n.color,
                        title: makeHtmlTooltip(n.title),
                        opacity: isOnPath ? 1.0 : 0.15
                    };
                    if (isOnPath) {
                        nodeOpts.borderWidth = 4;
                        nodeOpts.shadow = { enabled: true, color: '#a6da95', size: 10, x: 0, y: 0 };
                    }
                    return nodeOpts;
                });
                
                var targetEdges = dataObj.edges.map(function(e) {
                    var fromIdx = activeRoute.indexOf(e.from);
                    var toIdx = activeRoute.indexOf(e.to);
                    var isOnPath = (fromIdx !== -1 && toIdx !== -1 && toIdx === fromIdx + 1);
                    return {
                        id: e.id,
                        from: e.from,
                        to: e.to,
                        title: e.title,
                        opacity: isOnPath ? 1.0 : 0.1,
                        color: { color: isOnPath ? '#a6da95' : '#313244' },
                        width: isOnPath ? 4.0 : 1.0
                    };
                });
                
                visNodes.clear();
                visNodes.update(targetNodes);
                visEdges.clear();
                visEdges.update(targetEdges);
                return;
            }
            
            var targetNodes = dataObj.nodes.map(function(n) {
                var baseLabel = originalLabels[n.id];
                var newLabel = baseLabel;
                
                if (n.id.indexOf('file_') === 0) {
                    var filename = n.id.substring(5);
                    var isCollapsed = collapsedFiles.has(filename);
                    newLabel = '[Include] ' + filename.split('\\').pop().split('/').pop() + (isCollapsed ? '  [+]' : '  [-]');
                    return {
                        id: n.id,
                        label: newLabel,
                        color: { background: n.color, border: isCollapsed ? '#f9e2af' : '#11121d' },
                        title: makeHtmlTooltip(n.title),
                        borderWidth: isCollapsed ? 4 : 2,
                        opacity: 1.0
                    };
                } else {
                    var isCollapsed = collapsedNodes.has(n.id);
                    if (hasChildren(n.id)) {
                        newLabel = baseLabel + (isCollapsed ? '  [+]' : '  [-]');
                    }
                    return {
                        id: n.id,
                        label: newLabel,
                        color: { background: n.color, border: isCollapsed ? '#f9e2af' : '#11121d' },
                        title: makeHtmlTooltip(n.title),
                        borderWidth: isCollapsed ? 4 : 2,
                        opacity: 1.0
                    };
                }
            });

            var targetIds = new Set(targetNodes.map(function(n) { return n.id; }));
            var toRemove = currentNodes.filter(function(n) { return !targetIds.has(n.id); }).map(function(n) { return n.id; });
            
            visNodes.remove(toRemove);
            visNodes.update(targetNodes);

            visEdges.clear();
            visEdges.add(dataObj.edges);
        }

        // Highlight flow dependencies of a selected node
        var activeHighlightNode = null;
        function highlightNodeFlow(nodeId) {
            if (activeRoute) return; // ignore highlighting during path display
            if (!nodeId) {
                visNodes.update(visNodes.get().map(function(n) { return { id: n.id, opacity: 1 }; }));
                visEdges.update(visEdges.get().map(function(e) { return { id: e.id, opacity: 1, color: { color: 'rgba(255,255,255,0.12)' }, width: 2.0 }; }));
                return;
            }

            var connectedNodes = new Set();
            connectedNodes.add(nodeId);
            
            var edgeUpdates = [];
            visEdges.get().forEach(function(e) {
                var isConnected = (e.from === nodeId || e.to === nodeId);
                if (isConnected) {
                    connectedNodes.add(e.from);
                    connectedNodes.add(e.to);
                    edgeUpdates.push({ id: e.id, opacity: 1, color: { color: '#f38ba8' }, width: 3.5 });
                } else {
                    edgeUpdates.push({ id: e.id, opacity: 0.15, color: { color: '#313244' }, width: 1.0 });
                }
            });

            visNodes.update(visNodes.get().map(function(n) {
                return { id: n.id, opacity: connectedNodes.has(n.id) ? 1 : 0.15 };
            }));
            visEdges.update(edgeUpdates);
        }

        // --- Static Complexity and Impact Analysis ---
        function getMethodBranchComplexity(nodeId) {
            var count = 0;
            var queue = [nodeId];
            var visited = new Set();
            while (queue.length > 0) {
                var curr = queue.shift();
                if (!visited.has(curr)) {
                    visited.add(curr);
                    var children = outgoingEdges[curr] || [];
                    children.forEach(function(c) {
                        var childNodeObj = rawNodes.find(function(rn) { return rn.id === c; });
                        if (childNodeObj) {
                            if (childNodeObj.color === '#f5a97f') { // Branch node
                                count++;
                                queue.push(c);
                            }
                        }
                    });
                }
            }
            return count + 1; // Cyclomatic Complexity baseline
        }

        function getImpactScore(nodeId) {
            var upstream = new Set();
            var queue = [nodeId];
            var visited = new Set();
            var incomingEdges = {};
            rawEdges.forEach(function(e) {
                if (!incomingEdges[e.to]) incomingEdges[e.to] = [];
                incomingEdges[e.to].push(e.from);
            });

            while (queue.length > 0) {
                var curr = queue.shift();
                if (!visited.has(curr)) {
                    visited.add(curr);
                    var parents = incomingEdges[curr] || [];
                    parents.forEach(function(p) {
                        var pNode = rawNodes.find(function(rn) { return rn.id === p; });
                        if (pNode && pNode.color !== '#f5a97f') { // Ignore branch nodes
                            upstream.add(p);
                            queue.push(p);
                        }
                    });
                }
            }

            var downstream = new Set();
            queue = [nodeId];
            visited = new Set();
            while (queue.length > 0) {
                var curr = queue.shift();
                if (!visited.has(curr)) {
                    visited.add(curr);
                    var children = outgoingEdges[curr] || [];
                    children.forEach(function(c) {
                        var cNode = rawNodes.find(function(rn) { return rn.id === c; });
                        if (cNode && cNode.color !== '#f5a97f') { // Ignore branch nodes
                            downstream.add(c);
                            queue.push(c);
                        }
                    });
                }
            }

            return {
                callers: upstream.size,
                dependencies: downstream.size,
                total: upstream.size + downstream.size
            };
        }

        function isOrphanNode(nodeId) {
            if (nodeId === 'global_code' || nodeId.indexOf('hotkey_') === 0 || nodeId.indexOf('file_') === 0) return false;
            var incoming = rawEdges.filter(function(e) { return e.to === nodeId; });
            return incoming.length === 0;
        }

        // --- Sidebar View Emitters & Populators ---
        
        // Populate Tab 1: Include Files Explorer
        function populateFilesExplorer() {
            var container = document.getElementById('file-explorer-tree');
            container.innerHTML = '';

            rawFiles.forEach(function(file) {
                var fileSection = document.createElement('div');
                fileSection.style.marginBottom = '20px';

                var header = document.createElement('div');
                header.className = 'file-header';
                header.innerHTML = file.name + ' <span class=""badge"">' + file.count + ' definitions</span>';
                header.style.cursor = 'pointer';
                header.addEventListener('click', function() {
                    var graphNodeId = file.isInclude ? 'file_' + file.path : 'global_code';
                    network.selectNodes([graphNodeId]);
                    selectWikiNode(graphNodeId);
                    network.focus(graphNodeId, { scale: 1.1, animation: { duration: 400 } });
                });
                fileSection.appendChild(header);

                var fileDefs = rawDefinitions.filter(function(d) { return d.filePath === file.path; });
                if (fileDefs.length === 0) {
                    var emptyMsg = document.createElement('div');
                    emptyMsg.className = 'xref-empty';
                    emptyMsg.style.paddingLeft = '10px';
                    emptyMsg.innerText = 'No top-level functions or classes.';
                    fileSection.appendChild(emptyMsg);
                } else {
                    fileDefs.forEach(function(def) {
                        var item = document.createElement('div');
                        item.className = 'def-list-item';
                        item.id = 'side-def-' + def.id;
                        
                        var ind = document.createElement('div');
                        ind.className = 'def-indicator category-' + def.category.toLowerCase();
                        item.appendChild(ind);

                        var text = document.createElement('span');
                        text.innerText = def.label + (def.category === 'Class' ? ' (Class)' : '()');
                        item.appendChild(text);

                        item.addEventListener('click', function() {
                            navigateToNode(def.id);
                        });
                        fileSection.appendChild(item);
                    });
                }
                container.appendChild(fileSection);
            });
        }

        // Populate Tab 2: Workflow Categories Result
        function showWorkflowResults(t) {
            var resultsHeader = document.getElementById('workflow-list-header');
            var resultsContainer = document.getElementById('workflow-results-container');
            resultsContainer.innerHTML = '';

            if (!t) {
                resultsHeader.style.display = 'none';
                return;
            }

            resultsHeader.style.display = 'block';
            var filteredDefs = rawDefinitions.filter(function(d) { return d.logicTypes.indexOf(t) !== -1; });
            
            if (filteredDefs.length === 0) {
                var emptyMsg = document.createElement('div');
                emptyMsg.className = 'xref-empty';
                emptyMsg.innerText = 'No workflow functions matching this logic type.';
                resultsContainer.appendChild(emptyMsg);
            } else {
                filteredDefs.forEach(function(def) {
                    var item = document.createElement('div');
                    item.className = 'def-list-item';
                    
                    var ind = document.createElement('div');
                    ind.className = 'def-indicator category-' + def.category.toLowerCase();
                    item.appendChild(ind);

                    var text = document.createElement('span');
                    text.innerText = def.label + (def.category === 'Class' ? ' (Class)' : '()');
                    item.appendChild(text);

                    item.addEventListener('click', function() {
                        navigateToNode(def.id);
                    });
                    resultsContainer.appendChild(item);
                });
            }
        }

        // Populate Tab 3: Strands View (Hierarchy Tree)
        function populateStrandsView(nodeId) {
            var container = document.getElementById('strands-container');
            if (!container) return;
            container.innerHTML = '';

            if (!nodeId) {
                container.innerHTML = '<div class=""wiki-empty-state"" style=""padding: 20px;"">' +
                    '<h3>No Node Selected</h3>' +
                    '<p>Select any function or class node in the graph, explorer, or index to analyze its hierarchical incoming and outgoing call paths.</p>' +
                    '</div>';
                return;
            }

            var nodeObj = rawNodes.find(function(rn) { return rn.id === nodeId; });
            var label = nodeObj ? nodeObj.label : nodeId;

            var activeHeader = document.createElement('div');
            activeHeader.className = 'file-header';
            activeHeader.style.color = 'var(--accent)';
            activeHeader.innerText = 'Focus Node: ' + label;
            container.appendChild(activeHeader);

            var incHeader = document.createElement('div');
            incHeader.className = 'file-header';
            incHeader.innerText = 'Incoming Call Chains';
            container.appendChild(incHeader);

            var incTree = document.createElement('div');
            incTree.style.marginLeft = '4px';
            buildCallChainTree(nodeId, 'incoming', 0, incTree, new Set());
            container.appendChild(incTree);

            var outHeader = document.createElement('div');
            outHeader.className = 'file-header';
            outHeader.innerText = 'Outgoing Call Chains';
            container.appendChild(outHeader);

            var outTree = document.createElement('div');
            outTree.style.marginLeft = '4px';
            buildCallChainTree(nodeId, 'outgoing', 0, outTree, new Set());
            container.appendChild(outTree);
        }

        function buildCallChainTree(nodeId, direction, depth, parentElement, visited) {
            if (depth > 4) {
                var limitMsg = document.createElement('div');
                limitMsg.className = 'xref-empty';
                limitMsg.style.paddingLeft = (depth * 8) + 'px';
                limitMsg.innerText = '... (depth limit)';
                parentElement.appendChild(limitMsg);
                return;
            }

            var neighbors = [];
            if (direction === 'incoming') {
                rawEdges.forEach(function(e) {
                    if (e.to === nodeId) neighbors.push(e.from);
                });
            } else {
                rawEdges.forEach(function(e) {
                    if (e.from === nodeId) neighbors.push(e.to);
                });
            }

            neighbors = Array.from(new Set(neighbors));

            if (neighbors.length === 0) {
                if (depth === 0) {
                    var emptyMsg = document.createElement('div');
                    emptyMsg.className = 'xref-empty';
                    emptyMsg.style.paddingLeft = '8px';
                    emptyMsg.innerText = direction === 'incoming' ? 'No incoming calls.' : 'No outgoing calls.';
                    parentElement.appendChild(emptyMsg);
                }
                return;
            }

            neighbors.forEach(function(neighborId) {
                var neighborNode = rawNodes.find(function(rn) { return rn.id === neighborId; });
                if (!neighborNode) return;

                var item = document.createElement('div');
                item.className = 'def-list-item';
                item.style.paddingLeft = (depth * 8 + 6) + 'px';
                
                var ind = document.createElement('div');
                var cat = (neighborNode.category || 'branch').toLowerCase();
                ind.className = 'def-indicator category-' + cat;
                item.appendChild(ind);

                var text = document.createElement('span');
                text.innerText = neighborNode.label;
                item.appendChild(text);

                item.addEventListener('click', function() {
                    navigateToNode(neighborId);
                });
                parentElement.appendChild(item);

                if (!visited.has(neighborId)) {
                    var newVisited = new Set(visited);
                    newVisited.add(neighborId);
                    buildCallChainTree(neighborId, direction, depth + 1, parentElement, newVisited);
                } else {
                    var cyclicMsg = document.createElement('span');
                    cyclicMsg.style.color = 'var(--text-dim)';
                    cyclicMsg.style.fontSize = '0.75rem';
                    cyclicMsg.style.fontStyle = 'italic';
                    cyclicMsg.style.marginLeft = '6px';
                    cyclicMsg.innerText = '(cycle)';
                    text.appendChild(cyclicMsg);
                }
            });
        }

        // Populate Tab 4: Alphabetical Flat Index
        function populateFlatIndex() {
            var container = document.getElementById('flat-index-container');
            container.innerHTML = '';

            var sortedDefs = rawDefinitions.slice().sort(function(a, b) {
                return a.label.toLowerCase().localeCompare(b.label.toLowerCase());
            });

            sortedDefs.forEach(function(def) {
                var item = document.createElement('div');
                item.className = 'def-list-item';
                item.id = 'idx-def-' + def.id;

                var ind = document.createElement('div');
                ind.className = 'def-indicator category-' + def.category.toLowerCase();
                item.appendChild(ind);

                var text = document.createElement('span');
                text.innerText = def.label + ' (' + def.category + ')';
                item.appendChild(text);

                item.addEventListener('click', function() {
                    navigateToNode(def.id);
                });
                container.appendChild(item);
            });
        }

        // Populate Dropdowns in Path Finder
        function populatePathDropdowns() {
            var startSel = document.getElementById('path-start-select');
            var endSel = document.getElementById('path-end-select');
            startSel.innerHTML = '<option value="""">-- Select Start Node --</option>';
            endSel.innerHTML = '<option value="""">-- Select End Node --</option>';
            
            var sorted = rawNodes.slice().sort(function(a, b) {
                return a.label.toLowerCase().localeCompare(b.label.toLowerCase());
            });
            
            sorted.forEach(function(n) {
                if (n.color !== '#f5a97f') { // Skip branching nodes from direct path selection
                    var opt1 = document.createElement('option');
                    opt1.value = n.id;
                    opt1.innerText = n.label;
                    startSel.appendChild(opt1);
                    
                    var opt2 = document.createElement('option');
                    opt2.value = n.id;
                    opt2.innerText = n.label;
                    endSel.appendChild(opt2);
                }
            });
        }

        function executePathSearch() {
            var startVal = document.getElementById('path-start-select').value;
            var endVal = document.getElementById('path-end-select').value;
            var list = document.getElementById('path-results-list');
            list.innerHTML = '';
            
            if (!startVal || !endVal) {
                list.innerHTML = '<div class=""xref-empty"">Please select both start and end nodes.</div>';
                return;
            }
            
            activeRoute = findRoute(startVal, endVal);
            if (!activeRoute) {
                list.innerHTML = '<div class=""xref-empty"" style=""color: #f38ba8;"">No directed path found.</div>';
                rebuildGraph();
                return;
            }
            
            activeRoute.forEach(function(nodeId, idx) {
                var nodeObj = rawNodes.find(function(rn) { return rn.id === nodeId; });
                var label = nodeObj ? nodeObj.label : nodeId;
                
                var step = document.createElement('div');
                step.className = 'def-list-item';
                step.innerHTML = '<span style=""color: var(--accent); font-weight: bold; margin-right: 6px;"">' + (idx + 1) + '.</span>' + label;
                step.addEventListener('click', function() {
                    navigateToNode(nodeId);
                });
                list.appendChild(step);
            });
            
            rebuildGraph();
            network.fit({
                nodes: activeRoute,
                animation: { duration: 500 }
            });
        }

        // --- Details Rendering ---
        function selectWikiNode(nodeId) {
            selectedDefinitionId = nodeId;
            
            document.querySelectorAll('.def-list-item').forEach(function(el) {
                el.classList.remove('selected');
            });
            var sideItem = document.getElementById('side-def-' + nodeId);
            if (sideItem) sideItem.classList.add('selected');
            var idxItem = document.getElementById('idx-def-' + nodeId);
            if (idxItem) idxItem.classList.add('selected');

            // Update Focus Strand button look
            var focusBtn = document.getElementById('wiki-btn-focus');
            if (activeFocusNode === nodeId) {
                focusBtn.innerText = '❌ Show Full Graph';
                focusBtn.style.backgroundColor = 'rgba(243, 139, 168, 0.15)';
                focusBtn.style.color = '#f38ba8';
                focusBtn.style.borderColor = 'rgba(243, 139, 168, 0.3)';
            } else {
                focusBtn.innerText = '🔍 Isolate Strand';
                focusBtn.style.backgroundColor = 'rgba(255, 255, 255, 0.04)';
                focusBtn.style.color = 'var(--text-muted)';
                focusBtn.style.borderColor = 'var(--border-color)';
            }

            populateStrandsView(nodeId);

            var def = rawDefinitions.find(function(d) { return d.id === nodeId; });
            if (!def) {
                var fileNode = rawNodes.find(function(rn) { return rn.id === nodeId; });
                if (fileNode) {
                    document.getElementById('wiki-empty').style.display = 'none';
                    document.getElementById('wiki-details').style.display = 'flex';
                    document.getElementById('wiki-insights-box').style.display = 'none';
                    
                    document.getElementById('wiki-node-title').innerText = fileNode.label;
                    document.getElementById('wiki-node-badge').innerText = fileNode.id.indexOf('file_') === 0 ? 'Include' : 'Control Flow';
                    document.getElementById('wiki-node-badge').style.backgroundColor = 'var(--color-include)';
                    
                    var tagContainer = document.getElementById('wiki-node-tags');
                    tagContainer.innerHTML = '';
                    
                    document.getElementById('wiki-meta-file').innerText = fileNode.file || 'Main Script';
                    document.getElementById('wiki-meta-lines').innerText = 'System Node';
                    
                    document.getElementById('wiki-refs-incoming').innerHTML = '<span class=""xref-empty"">References managed via visual call flow graph.</span>';
                    document.getElementById('wiki-refs-outgoing').innerHTML = '<span class=""xref-empty"">References managed via visual call flow graph.</span>';
                    
                    var codeText = '; Reconstructed emitted representation not available for dynamic system branches.';
                    document.getElementById('wiki-code-block').innerText = codeText;
                    Prism.highlightElement(document.getElementById('wiki-code-block'));
                } else {
                    document.getElementById('wiki-empty').style.display = 'flex';
                    document.getElementById('wiki-details').style.display = 'none';
                }
                return;
            }

            document.getElementById('wiki-empty').style.display = 'none';
            document.getElementById('wiki-details').style.display = 'flex';
            document.getElementById('wiki-insights-box').style.display = 'block';

            document.getElementById('wiki-node-title').innerText = def.label + def.parameters;
            
            var badge = document.getElementById('wiki-node-badge');
            badge.innerText = def.category;
            badge.style.backgroundColor = 'var(--color-' + def.category.toLowerCase() + ')';
            if (def.category === 'Class' || def.category === 'Global') {
                badge.style.color = '#11121d';
            } else {
                badge.style.color = '#0b0c10';
            }

            var tagsContainer = document.getElementById('wiki-node-tags');
            tagsContainer.innerHTML = '';
            def.logicTypes.forEach(function(t) {
                var tag = document.createElement('span');
                tag.className = 'wiki-tag';
                tag.style.borderColor = 'var(--accent)';
                tag.innerText = t;
                tagsContainer.appendChild(tag);
            });

            document.getElementById('wiki-meta-file').innerText = def.filePath;
            document.getElementById('wiki-meta-lines').innerText = 'Line ' + def.line;

            // Load Insights details
            var complexity = getMethodBranchComplexity(nodeId);
            document.getElementById('wiki-insight-mccabe').innerText = complexity + ' (Cyclomatic Branches)';
            
            var impact = getImpactScore(nodeId);
            document.getElementById('wiki-insight-impact').innerText = impact.total + ' (' + impact.callers + ' upstream callers | ' + impact.dependencies + ' downstream deps)';

            var isOrphan = isOrphanNode(nodeId);
            document.getElementById('wiki-insight-orphan').style.display = isOrphan ? 'block' : 'none';

            // Render Incoming Refs (Called By)
            var incomingContainer = document.getElementById('wiki-refs-incoming');
            incomingContainer.innerHTML = '';
            if (def.incoming.length === 0) {
                incomingContainer.innerHTML = '<div class=""xref-empty"">No detected incoming calls (orphan method).</div>';
            } else {
                def.incoming.forEach(function(incId) {
                    var incDef = rawDefinitions.find(function(d) { return d.id === incId; });
                    if (incDef) {
                        var el = document.createElement('div');
                        el.className = 'xref-item';
                        el.innerHTML = '← ' + incDef.label;
                        el.addEventListener('click', function() {
                            navigateToNode(incId);
                        });
                        incomingContainer.appendChild(el);
                    }
                });
            }

            // Render Outgoing Refs (Calls)
            var outgoingContainer = document.getElementById('wiki-refs-outgoing');
            outgoingContainer.innerHTML = '';
            if (def.outgoing.length === 0) {
                outgoingContainer.innerHTML = '<div class=""xref-empty"">Calls no external functions (terminal method).</div>';
            } else {
                def.outgoing.forEach(function(outId) {
                    var outDef = rawDefinitions.find(function(d) { return d.id === outId; });
                    if (outDef) {
                        var el = document.createElement('div');
                        el.className = 'xref-item';
                        el.innerHTML = '→ ' + outDef.label;
                        el.addEventListener('click', function() {
                            navigateToNode(outId);
                        });
                        outgoingContainer.appendChild(el);
                    }
                });
            }

            // Code highlight
            document.getElementById('wiki-code-block').innerText = def.code;
            Prism.highlightElement(document.getElementById('wiki-code-block'));
        }

        // Navigation Redirect
        function navigateToNode(nodeId) {
            var def = rawDefinitions.find(function(d) { return d.id === nodeId; });
            if (def && def.filePath !== 'Main Script') {
                if (collapsedFiles.has(def.filePath)) {
                    collapsedFiles.delete(def.filePath);
                    rebuildGraph();
                }
            }

            network.selectNodes([nodeId]);
            selectWikiNode(nodeId);
            if (currentViewMode === 'full') {
                highlightNodeFlow(nodeId);
            } else {
                rebuildGraph();
            }
            
            network.focus(nodeId, {
                scale: 1.25,
                animation: {
                    duration: 400,
                    easingFunction: 'easeInOutQuad'
                }
            });
        }

        // --- Event Listeners setup ---
        network.on(""selectNode"", function(params) {
            if (params.nodes.length > 0) {
                activeHighlightNode = params.nodes[0];
                selectWikiNode(activeHighlightNode);
                if (currentViewMode === 'full') {
                    highlightNodeFlow(activeHighlightNode);
                } else {
                    rebuildGraph();
                }
            }
        });

        network.on(""deselectNode"", function(params) {
            activeHighlightNode = null;
            if (currentViewMode === 'full') {
                highlightNodeFlow(null);
            } else {
                rebuildGraph();
            }
            document.getElementById('wiki-empty').style.display = 'flex';
            document.getElementById('wiki-details').style.display = 'none';
            populateStrandsView(null);
        });

        network.on(""doubleClick"", function(params) {
            if (params.nodes.length > 0) {
                var clickedNodeId = params.nodes[0];
                if (clickedNodeId.indexOf('file_') === 0) {
                    var filename = clickedNodeId.substring(5);
                    if (collapsedFiles.has(filename)) {
                        collapsedFiles.delete(filename);
                    } else {
                        collapsedFiles.add(filename);
                    }
                    rebuildGraph();
                } else if (hasChildren(clickedNodeId)) {
                    if (collapsedNodes.has(clickedNodeId)) {
                        collapsedNodes.delete(clickedNodeId);
                    } else {
                        collapsedNodes.add(clickedNodeId);
                    }
                    rebuildGraph();
                }
            }
        });

        // Search highlight
        document.getElementById('search-box').addEventListener('input', function(e) {
            var query = e.target.value.toLowerCase();
            if (!query) {
                rebuildGraph();
                return;
            }
            
            collapsedNodes.clear();
            collapsedFiles.clear();
            rebuildGraph();

            var visibleNodes = visNodes.get();
            var updates = visibleNodes.map(function(n) {
                var def = rawDefinitions.find(function(d) { return d.id === n.id; });
                var codeMatch = def && def.code.toLowerCase().indexOf(query) !== -1;
                var match = query && (originalLabels[n.id].toLowerCase().indexOf(query) !== -1 || codeMatch);
                return {
                    id: n.id,
                    font: {
                        background: match ? '#f38ba8' : 'rgba(0,0,0,0)',
                        color: match ? '#11121d' : '#cdd6f4',
                        strokeWidth: match ? 0 : 2
                    },
                    size: match ? 30 : 20
                };
            });
            visNodes.update(updates);
        });

        // Tab Switching
        ['modules', 'workflows', 'strands', 'finder', 'index'].forEach(function(tab) {
            document.getElementById('tab-btn-' + tab).addEventListener('click', function() {
                document.querySelectorAll('.tab-btn').forEach(function(b) { b.classList.remove('active'); });
                document.querySelectorAll('.tab-pane').forEach(function(p) { p.classList.remove('active'); });
                
                document.getElementById('tab-btn-' + tab).classList.add('active');
                document.getElementById('tab-' + tab).classList.add('active');
            });
        });

        // Workflow buttons
        document.querySelectorAll('.workflow-btn').forEach(function(btn) {
            btn.addEventListener('click', function() {
                var val = btn.getAttribute('data-workflow');
                
                if (activeWorkflowFilter === val) {
                    activeWorkflowFilter = null;
                    btn.classList.remove('active');
                    showWorkflowResults(null);
                } else {
                    document.querySelectorAll('.workflow-btn').forEach(function(b) { b.classList.remove('active'); });
                    activeWorkflowFilter = val;
                    btn.classList.add('active');
                    showWorkflowResults(val);
                }
                
                rebuildGraph();
                network.fit({ animation: { duration: 400 } });
            });
        });

        // Copy button
        document.getElementById('wiki-code-copy').addEventListener('click', function() {
            var code = document.getElementById('wiki-code-block').innerText;
            navigator.clipboard.writeText(code).then(function() {
                var btn = document.getElementById('wiki-code-copy');
                btn.innerText = 'Copied!';
                setTimeout(function() { btn.innerText = 'Copy'; }, 1500);
            });
        });

        // Toolbar: Branches
        var btnBranches = document.getElementById('toolbar-branches');
        btnBranches.addEventListener('click', function() {
            showBranches = !showBranches;
            btnBranches.innerText = showBranches ? 'Branches: Shown' : 'Branches: Hidden';
            if (showBranches) {
                btnBranches.classList.add('active');
            } else {
                btnBranches.classList.remove('active');
            }
            rebuildGraph();
        });

        // Drag node toggle
        document.getElementById('toolbar-drag').addEventListener('click', function() {
            dragNodesEnabled = !dragNodesEnabled;
            this.classList.toggle('active', dragNodesEnabled);
            this.innerText = dragNodesEnabled ? 'Dragging: On' : 'Dragging: Off';
            network.setOptions({ interaction: { dragNodes: dragNodesEnabled } });
        });

        // Pane toggling
        var sidebarCollapsed = false;
        document.getElementById('btn-toggle-sidebar').addEventListener('click', function() {
            sidebarCollapsed = !sidebarCollapsed;
            document.querySelector('aside.sidebar').classList.toggle('collapsed', sidebarCollapsed);
            this.classList.toggle('active', !sidebarCollapsed);
            setTimeout(function() { network.setSize('100%', '100%'); network.redraw(); }, 350);
        });

        var wikiCollapsed = false;
        document.getElementById('btn-toggle-wiki').addEventListener('click', function() {
            wikiCollapsed = !wikiCollapsed;
            document.querySelector('section.wiki-panel').classList.toggle('collapsed', wikiCollapsed);
            this.classList.toggle('active', !wikiCollapsed);
            setTimeout(function() { network.setSize('100%', '100%'); network.redraw(); }, 350);
        });

        // PNG Export
        document.getElementById('btn-export-png').addEventListener('click', function() {
            network.once(""afterDrawing"", function() {
                var canvas = document.querySelector('canvas');
                if (canvas) {
                    var dataUrl = canvas.toDataURL(""image/png"");
                    var link = document.createElement('a');
                    link.download = 'codebase_flow_diagram.png';
                    link.href = dataUrl;
                    link.click();
                }
            });
            network.redraw();
        });

        // Copy Mermaid
        document.getElementById('btn-copy-mermaid').addEventListener('click', function() {
            navigator.clipboard.writeText(rawMermaid).then(function() {
                var btn = document.getElementById('btn-copy-mermaid');
                btn.innerText = 'Copied!';
                setTimeout(function() { btn.innerText = '📋 Copy Mermaid'; }, 1500);
            });
        });

        // Finder controls
        document.getElementById('path-find-btn').addEventListener('click', executePathSearch);
        document.getElementById('path-clear-btn').addEventListener('click', function() {
            document.getElementById('path-start-select').value = '';
            document.getElementById('path-end-select').value = '';
            document.getElementById('path-results-list').innerHTML = '<div class=""xref-empty"">No active search. Select nodes above to map path.</div>';
            activeRoute = null;
            rebuildGraph();
            network.fit({ animation: { duration: 400 } });
        });

        // Wiki Set Start/End path nodes
        document.getElementById('wiki-btn-set-start').addEventListener('click', function() {
            if (selectedDefinitionId) {
                document.getElementById('path-start-select').value = selectedDefinitionId;
                document.getElementById('tab-btn-finder').click();
                executePathSearch();
            }
        });
        document.getElementById('wiki-btn-set-end').addEventListener('click', function() {
            if (selectedDefinitionId) {
                document.getElementById('path-end-select').value = selectedDefinitionId;
                document.getElementById('tab-btn-finder').click();
                executePathSearch();
            }
        });

        // Exploration View Modes
        var viewModes = ['full', 'focus', 'includes', 'classes'];
        viewModes.forEach(function(m) {
            document.getElementById('mode-view-' + m).addEventListener('click', function() {
                viewModes.forEach(function(x) { document.getElementById('mode-view-' + x).classList.remove('active'); });
                this.classList.add('active');
                currentViewMode = m;
                rebuildGraph();
                network.fit({ animation: { duration: 400 } });
            });
        });

        // Toolbar: Layout
        var btnLayout = document.getElementById('toolbar-layout');
        var btnDirection = document.getElementById('toolbar-direction');
        var btnPhysics = document.getElementById('toolbar-physics');
        
        btnLayout.addEventListener('click', function() {
            if (currentLayout === 'hierarchical') {
                currentLayout = 'free';
                btnLayout.innerText = 'Layout: Free';
                btnLayout.classList.remove('active');
                btnDirection.style.display = 'none';
            } else {
                currentLayout = 'hierarchical';
                btnLayout.innerText = 'Layout: Flow';
                btnLayout.classList.add('active');
                btnDirection.style.display = 'inline-flex';
            }
            network.setOptions(getNetworkOptions(currentLayout, currentDirection));
        });

        btnDirection.addEventListener('click', function() {
            if (currentDirection === 'UD') {
                currentDirection = 'LR';
                btnDirection.innerText = 'Direction: Horizontal';
            } else {
                currentDirection = 'UD';
                btnDirection.innerText = 'Direction: Vertical';
            }
            network.setOptions(getNetworkOptions(currentLayout, currentDirection));
        });

        btnPhysics.addEventListener('click', function() {
            var active = btnPhysics.classList.toggle('active');
            btnPhysics.innerText = active ? 'Physics: On' : 'Physics: Off';
            network.setOptions(getNetworkOptions(currentLayout, currentDirection));
        });

        document.getElementById('toolbar-fit').addEventListener('click', function() {
            network.fit({ animation: { duration: 400 } });
        });

        // Zoom In & Out
        document.getElementById('toolbar-zoom-in').addEventListener('click', function() {
            var scale = network.getScale();
            network.moveTo({ scale: scale * 1.3, animation: { duration: 200 } });
        });

        document.getElementById('toolbar-zoom-out').addEventListener('click', function() {
            var scale = network.getScale();
            network.moveTo({ scale: scale / 1.3, animation: { duration: 200 } });
        });

        // Wiki Panel Locate in Graph
        document.getElementById('wiki-btn-locate').addEventListener('click', function() {
            if (selectedDefinitionId) {
                network.focus(selectedDefinitionId, { scale: 1.3, animation: { duration: 400 } });
                network.selectNodes([selectedDefinitionId]);
            }
        });

        // Wiki Panel Isolate Strand
        document.getElementById('wiki-btn-focus').addEventListener('click', function() {
            if (activeFocusNode === selectedDefinitionId) {
                activeFocusNode = null;
                this.innerText = '🔍 Isolate Strand';
                this.style.backgroundColor = 'rgba(255, 255, 255, 0.04)';
                this.style.color = 'var(--text-muted)';
                this.style.borderColor = 'var(--border-color)';
            } else {
                activeFocusNode = selectedDefinitionId;
                this.innerText = '❌ Show Full Graph';
                this.style.backgroundColor = 'rgba(243, 139, 168, 0.15)';
                this.style.color = '#f38ba8';
                this.style.borderColor = 'rgba(243, 139, 168, 0.3)';
            }
            rebuildGraph();
            network.fit({ animation: { duration: 400 } });
        });

        // Quick Access button: Global Entry Point
        document.getElementById('quick-btn-global').addEventListener('click', function() {
            navigateToNode('global_code');
        });

        // Quick Access button: Collapse All
        document.getElementById('quick-btn-collapse-all').addEventListener('click', function() {
            rawNodes.forEach(function(n) {
                if (n.id !== 'global_code' && n.id.indexOf('hotkey_') !== 0 && n.id.indexOf('file_') !== 0) {
                    collapsedNodes.add(n.id);
                }
            });
            rebuildGraph();
        });

        // Quick Access button: Expand All
        document.getElementById('quick-btn-expand-all').addEventListener('click', function() {
            collapsedNodes.clear();
            rebuildGraph();
        });

        document.getElementById('toolbar-reset').addEventListener('click', function() {
            collapsedNodes.clear();
            collapsedFiles.clear();
            activeFocusNode = null;
            activeRoute = null;
            if (collapseIncludesDefault) {
                rawFiles.forEach(function(f) {
                    if (f.isInclude) collapsedFiles.add(f.path);
                });
            }
            rawNodes.forEach(function(n) {
                if (n.id !== 'global_code' && n.id.indexOf('hotkey_') !== 0 && n.id.indexOf('file_') !== 0) {
                    collapsedNodes.add(n.id);
                }
            });
            document.getElementById('search-box').value = '';
            activeWorkflowFilter = null;
            document.querySelectorAll('.workflow-btn').forEach(function(b) { b.classList.remove('active'); });
            showWorkflowResults(null);
            showBranches = true;
            btnBranches.innerText = 'Branches: Shown';
            btnBranches.classList.add('active');
            
            document.getElementById('path-start-select').value = '';
            document.getElementById('path-end-select').value = '';
            document.getElementById('path-results-list').innerHTML = '<div class=""xref-empty"">No active search. Select nodes above to map path.</div>';

            currentViewMode = 'full';
            viewModes.forEach(function(x) { document.getElementById('mode-view-' + x).classList.remove('active'); });
            document.getElementById('mode-view-full').classList.add('active');

            rebuildGraph();
            selectWikiNode(null);
            network.fit({ animation: { duration: 500 } });
        });

        // Initialize lists
        populateFilesExplorer();
        populateFlatIndex();
        populatePathDropdowns();
        rebuildGraph();
        populateStrandsView(null);
    </script>
</body>
</html>";
        }
    }

    public class LogicFlowGraphBuilder
    {
        private bool _includeBranches;
        private bool _includeLibraryDetails;
        private bool _collapseIncludes;
        private int _nodeCounter;

        public class FlowNode
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public string Category { get; set; }
            public string Color { get; set; }
            public string FilePath { get; set; }
            public string Tooltip { get; set; }
        }

        public class FlowEdge
        {
            public string From { get; set; }
            public string To { get; set; }
            public string Label { get; set; }
        }

        public class DefinitionInfo
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public string Category { get; set; }
            public string FilePath { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
            public string Code { get; set; }
            public string Parameters { get; set; }
            public string Extends { get; set; }
            public List<string> LogicTypes { get; set; }
            public List<string> Incoming { get; set; }
            public List<string> Outgoing { get; set; }
            public AstNode Node { get; set; }
        }

        public List<FlowNode> Nodes { get; private set; }
        public List<FlowEdge> Edges { get; private set; }
        public List<DefinitionInfo> Definitions { get; private set; }
        private HashSet<string> _edgeSet;

        private Dictionary<string, string> _symbolToNodeId;

        private class FileBoundary
        {
            public string FileName { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
        }

        private List<FileBoundary> _fileBoundaries = new List<FileBoundary>();

        private void ScanForComments(AstNode node, List<AstNode> commentNodes)
        {
            if (node == null) return;
            if (node.NodeType == "Comment")
            {
                commentNodes.Add(node);
            }
            foreach (var child in node.ChildNodes)
            {
                ScanForComments(child, commentNodes);
            }
        }

        private void BuildFileBoundaries(AstNode root)
        {
            _fileBoundaries = new List<FileBoundary>();
            var commentNodes = new List<AstNode>();
            ScanForComments(root, commentNodes);
            commentNodes.Sort(delegate (AstNode a, AstNode b) { return a.Line.CompareTo(b.Line); });

            var stack = new Stack<Tuple<string, int>>();
            foreach (var comment in commentNodes)
            {
                string val = comment.Value ?? "";

                int beginIdx = val.IndexOf("--- begin:", StringComparison.OrdinalIgnoreCase);
                if (beginIdx >= 0)
                {
                    string filename = val.Substring(beginIdx + 10).Replace("---", "").Trim();
                    if (!string.IsNullOrEmpty(filename))
                    {
                        stack.Push(Tuple.Create(filename, comment.Line));
                    }
                    continue;
                }

                int endIdx = val.IndexOf("--- end:", StringComparison.OrdinalIgnoreCase);
                if (endIdx >= 0)
                {
                    string filename = val.Substring(endIdx + 8).Replace("---", "").Trim();
                    if (!string.IsNullOrEmpty(filename))
                    {
                        var tempStack = new List<Tuple<string, int>>();
                        Tuple<string, int> match = null;
                        while (stack.Count > 0)
                        {
                            var top = stack.Pop();
                            if (string.Equals(top.Item1, filename, StringComparison.OrdinalIgnoreCase))
                            {
                                match = top;
                                break;
                            }
                            else
                            {
                                tempStack.Add(top);
                            }
                        }

                        if (match != null)
                        {
                            _fileBoundaries.Add(new FileBoundary
                            {
                                FileName = match.Item1,
                                StartLine = match.Item2,
                                EndLine = comment.Line
                            });
                        }

                        for (int i = tempStack.Count - 1; i >= 0; i--)
                        {
                            stack.Push(tempStack[i]);
                        }
                    }
                }
            }

            _fileBoundaries.Sort(delegate (FileBoundary a, FileBoundary b)
            {
                return (a.EndLine - a.StartLine).CompareTo(b.EndLine - b.StartLine);
            });
        }

        private string GetFilePathForLine(int line, string defaultPath)
        {
            if (_fileBoundaries != null && _fileBoundaries.Count > 0)
            {
                foreach (var boundary in _fileBoundaries)
                {
                    if (line >= boundary.StartLine && line <= boundary.EndLine)
                    {
                        return boundary.FileName;
                    }
                }
            }
            return defaultPath;
        }

        public LogicFlowGraphBuilder(bool includeBranches, bool includeLibraryDetails, bool collapseIncludes)
        {
            _includeBranches = includeBranches;
            _includeLibraryDetails = includeLibraryDetails;
            _collapseIncludes = collapseIncludes;
            _nodeCounter = 0;
            Nodes = new List<FlowNode>();
            Edges = new List<FlowEdge>();
            Definitions = new List<DefinitionInfo>();
            _edgeSet = new HashSet<string>();
            _symbolToNodeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public void AddNode(string id, string label, string category, string color, string filePath = "Main Script", string tooltip = null)
        {
            if (Nodes.Any(n => n.Id == id)) return;
            Nodes.Add(new FlowNode { Id = id, Label = label, Category = category, Color = color, FilePath = filePath, Tooltip = tooltip });
        }

        public void AddEdge(string from, string to, string label = null)
        {
            if (from == to) return;
            string key = from + "->" + to + (label ?? "");
            if (_edgeSet.Add(key))
            {
                Edges.Add(new FlowEdge { From = from, To = to, Label = label });
            }
        }

        private static string SimpleHtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&#39;");
        }

        private static string GetParametersString(AstNode methodNode)
        {
            if (methodNode == null) return "()";
            var parametersNode = methodNode.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
            if (parametersNode == null || parametersNode.ChildCount == 0) return "()";

            var sb = new StringBuilder();
            sb.Append("(");
            for (int i = 0; i < parametersNode.ChildCount; i++)
            {
                var param = parametersNode.GetChild(i);
                if (param == null) continue;

                if (i > 0) sb.Append(", ");

                if (param.Metadata == "byref")
                {
                    sb.Append("&");
                }

                if (param.Value == "*")
                {
                    sb.Append("*");
                }
                else
                {
                    sb.Append(param.Value);
                }

                if (param.Metadata == "variadic")
                {
                    sb.Append("*");
                }

                if (param.ChildCount > 0)
                {
                    try
                    {
                        sb.Append(" := ");
                        sb.Append(AstEmitter.Emit(param.GetChild(0)));
                    }
                    catch
                    {
                        sb.Append(" := ?");
                    }
                }
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static string GetClassExtendsString(AstNode classNode)
        {
            if (classNode == null) return null;
            foreach (var child in classNode.ChildNodes)
            {
                if (child != null && child.NodeType == "Extends")
                {
                    return child.Value;
                }
            }
            return null;
        }

        public void Build(AstNode root)
        {
            BuildFileBoundaries(root);
            RegisterStaticDeclarations(root);

            string globalId = "global_code";
            string globalTooltip = "<strong>Global Entry Point</strong><br/><span style='color: #a6adc8;'>File: Main Script</span>";
            AddNode(globalId, "Global Entry Point", "Global", "#8aadf4", "Main Script", globalTooltip);

            var globalDef = new DefinitionInfo
            {
                Id = globalId,
                Label = "Global Entry Point",
                Category = "Global",
                FilePath = "Main Script",
                Line = 1,
                Column = 1,
                Node = root,
                Parameters = "",
                Extends = "",
                LogicTypes = new List<string>(),
                Incoming = new List<string>(),
                Outgoing = new List<string>()
            };
            Definitions.Add(globalDef);

            TraverseFlow(root, globalId, null);

            // Populate call dependencies
            foreach (var edge in Edges)
            {
                var fromDef = Definitions.FirstOrDefault(d => d.Id == edge.From);
                var toDef = Definitions.FirstOrDefault(d => d.Id == edge.To);
                if (fromDef != null && toDef != null)
                {
                    if (!fromDef.Outgoing.Contains(toDef.Id))
                    {
                        fromDef.Outgoing.Add(toDef.Id);
                    }
                    if (!toDef.Incoming.Contains(fromDef.Id))
                    {
                        toDef.Incoming.Add(fromDef.Id);
                    }
                }
            }

            // Post-process definition properties: Code emission and LogicTypes analysis
            foreach (var def in Definitions)
            {
                string codeStr = "";
                try
                {
                    codeStr = AstEmitter.Emit(def.Node);
                }
                catch (Exception ex)
                {
                    codeStr = "; Error emitting code snippet: " + ex.Message;
                }
                def.Code = codeStr;
                def.LogicTypes = AnalyzeLogicTypes(def.Node, def.Category);
            }

            // Add parent include file nodes and defines edges
            var uniqueFilePaths = new List<string>();
            foreach (var n in Nodes)
            {
                if (!string.IsNullOrEmpty(n.FilePath) && !uniqueFilePaths.Contains(n.FilePath))
                {
                    uniqueFilePaths.Add(n.FilePath);
                }
            }

            foreach (var filePath in uniqueFilePaths)
            {
                if (filePath != "Main Script" && !filePath.StartsWith("file_"))
                {
                    string fileNodeId = "file_" + filePath;
                    if (!Nodes.Any(n => n.Id == fileNodeId))
                    {
                        string tooltip = string.Format("<strong>Include File: {0}</strong><br/><span style='color: #a6adc8;'>Double-click to expand/collapse this include file's contents.</span>", SimpleHtmlEncode(filePath));
                        Nodes.Add(new FlowNode
                        {
                            Id = fileNodeId,
                            Label = filePath,
                            Category = "Include",
                            Color = "#b4befe",
                            FilePath = filePath,
                            Tooltip = tooltip
                        });
                    }

                    // Connect this include file node to its top-level contents via "defines" edges
                    var children = Nodes.Where(n => n.FilePath == filePath && n.Id != fileNodeId && (n.Category == "Function" || n.Category == "Class" || n.Category == "Hotkey")).ToList();
                    foreach (var child in children)
                    {
                        AddEdge(fileNodeId, child.Id, "defines");
                    }
                }
            }
        }

        private static List<string> AnalyzeLogicTypes(AstNode node, string category)
        {
            var types = new List<string>();

            if (category == "Hotkey" || category == "Hotstring")
            {
                types.Add("event");
                return types;
            }

            if (node == null)
            {
                types.Add("core");
                return types;
            }

            bool isGui = false;
            bool isEvent = false;
            bool isAsync = false;
            bool isState = false;

            var queue = new Queue<AstNode>();
            queue.Enqueue(node);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == null) continue;

                string valLower = (current.Value ?? "").ToLower();
                string typeLower = (current.NodeType ?? "").ToLower();

                if (typeLower == "call")
                {
                    var callee = current.ChildCount > 0 ? current.GetChild(0) : null;
                    if (callee != null)
                    {
                        string calleeVal = (callee.Value ?? "").ToLower();
                        if (calleeVal.Contains("gui") || calleeVal.Contains("show") || calleeVal.Contains("wpf") || calleeVal.Contains("control") || calleeVal.Contains("window"))
                        {
                            isGui = true;
                        }
                        if (calleeVal == "settimer" || calleeVal.Contains("timer") || calleeVal == "dllcall" || calleeVal == "run" || calleeVal.Contains("async"))
                        {
                            isAsync = true;
                        }
                        if (calleeVal.StartsWith("on") && calleeVal.Length > 2 && char.IsUpper(callee.Value[2]))
                        {
                            isEvent = true;
                        }
                    }
                }
                else if (typeLower == "identifier")
                {
                    if (valLower.Contains("gui") || valLower.Contains("xaml") || valLower.Contains("wpf") || valLower.Contains("show") || valLower.Contains("control"))
                    {
                        isGui = true;
                    }
                    if (valLower == "settimer" || valLower.Contains("timer") || valLower == "dllcall" || valLower.Contains("async"))
                    {
                        isAsync = true;
                    }
                    if (valLower.StartsWith("g_") || valLower.Contains("config") || valLower.Contains("setting") || valLower.Contains("state") || valLower.Contains("flag"))
                    {
                        isState = true;
                    }
                }
                else if (typeLower == "global")
                {
                    isState = true;
                }

                foreach (var child in current.ChildNodes)
                {
                    queue.Enqueue(child);
                }
            }

            string name = (node.Value ?? "").ToLower();
            if (name.Contains("gui") || name.Contains("show") || name.Contains("xaml") || name.Contains("control")) isGui = true;
            if (name.StartsWith("on") && name.Length > 2) isEvent = true;
            if (name.Contains("timer") || name.Contains("async") || name.Contains("callback")) isAsync = true;
            if (name.Contains("config") || name.Contains("setting") || name.Contains("state") || name.Contains("flag")) isState = true;

            if (isGui) types.Add("gui");
            if (isEvent) types.Add("event");
            if (isAsync) types.Add("async");
            if (isState) types.Add("state");

            if (types.Count == 0)
            {
                types.Add("core");
            }

            return types;
        }

        private void RegisterStaticDeclarations(AstNode node, string currentFilePath = "Main Script")
        {
            if (node == null) return;

            string nodeFile = GetFilePathForLine(node.Line, currentFilePath);

            if (node.NodeType == "Include")
            {
                string filePath = !string.IsNullOrEmpty(node.Value) ? System.IO.Path.GetFileName(node.Value) : "Include File";
                foreach (var child in node.ChildNodes)
                {
                    RegisterStaticDeclarations(child, filePath);
                }
                return;
            }

            if (node.NodeType == "Class")
            {
                string className = node.Value;
                string classId = "class_" + className;
                _symbolToNodeId[className] = classId;

                string extendsStr = GetClassExtendsString(node);
                string tooltip = string.Format("<strong>class {0}</strong>{1}<br/><span style='color: #a6adc8;'>Class | File: {2} (Line {3})</span>",
                    SimpleHtmlEncode(className),
                    !string.IsNullOrEmpty(extendsStr) ? " extends " + SimpleHtmlEncode(extendsStr) : "",
                    SimpleHtmlEncode(nodeFile),
                    node.Line);

                AddNode(classId, className + " (Class)", "Class", "#c6a0f6", nodeFile, tooltip);

                Definitions.Add(new DefinitionInfo
                {
                    Id = classId,
                    Label = className,
                    Category = "Class",
                    FilePath = nodeFile,
                    Line = node.Line,
                    Column = node.Column,
                    Node = node,
                    Parameters = "",
                    Extends = extendsStr,
                    LogicTypes = new List<string>(),
                    Incoming = new List<string>(),
                    Outgoing = new List<string>()
                });

                foreach (var child in node.ChildNodes)
                {
                    if (child == null) continue;
                    string childFile = GetFilePathForLine(child.Line, nodeFile);
                    if (child.NodeType == "Method")
                    {
                        string methodId = "method_" + className + "_" + child.Value;
                        _symbolToNodeId[className + "." + child.Value] = methodId;

                        string paramsStr = GetParametersString(child);
                        string methodTooltip = string.Format("<strong>{0}.{1}{2}</strong><br/><span style='color: #a6adc8;'>Method | File: {3} (Line {4})</span>",
                            SimpleHtmlEncode(className),
                            SimpleHtmlEncode(child.Value),
                            SimpleHtmlEncode(paramsStr),
                            SimpleHtmlEncode(childFile),
                            child.Line);

                        AddNode(methodId, child.Value + "()", "Method", "#f5bde6", childFile, methodTooltip);

                        Definitions.Add(new DefinitionInfo
                        {
                            Id = methodId,
                            Label = className + "." + child.Value,
                            Category = "Method",
                            FilePath = childFile,
                            Line = child.Line,
                            Column = child.Column,
                            Node = child,
                            Parameters = paramsStr,
                            Extends = "",
                            LogicTypes = new List<string>(),
                            Incoming = new List<string>(),
                            Outgoing = new List<string>()
                        });
                    }
                    else if (child.NodeType == "Property")
                    {
                        string propId = "prop_" + className + "_" + child.Value;
                        _symbolToNodeId[className + "." + child.Value] = propId;

                        string propTooltip = string.Format("<strong>{0}.{1}</strong><br/><span style='color: #a6adc8;'>Property | File: {2} (Line {3})</span>",
                            SimpleHtmlEncode(className),
                            SimpleHtmlEncode(child.Value),
                            SimpleHtmlEncode(childFile),
                            child.Line);

                        AddNode(propId, child.Value + " (Prop)", "Method", "#f5bde6", childFile, propTooltip);

                        Definitions.Add(new DefinitionInfo
                        {
                            Id = propId,
                            Label = className + "." + child.Value,
                            Category = "Property",
                            FilePath = childFile,
                            Line = child.Line,
                            Column = child.Column,
                            Node = child,
                            Parameters = "",
                            Extends = "",
                            LogicTypes = new List<string>(),
                            Incoming = new List<string>(),
                            Outgoing = new List<string>()
                        });
                    }
                }

                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Class")
                    {
                        RegisterStaticDeclarations(child, nodeFile);
                    }
                }
                return;
            }

            if (node.NodeType == "Method")
            {
                string funcName = node.Value;
                string funcId = "func_" + funcName;
                _symbolToNodeId[funcName] = funcId;

                string paramsStr = GetParametersString(node);
                string tooltip = string.Format("<strong>{0}{1}</strong><br/><span style='color: #a6adc8;'>Function | File: {2} (Line {3})</span>",
                    SimpleHtmlEncode(funcName),
                    SimpleHtmlEncode(paramsStr),
                    SimpleHtmlEncode(nodeFile),
                    node.Line);

                AddNode(funcId, funcName + "()", "Function", "#b7bdf8", nodeFile, tooltip);

                Definitions.Add(new DefinitionInfo
                {
                    Id = funcId,
                    Label = funcName,
                    Category = "Function",
                    FilePath = nodeFile,
                    Line = node.Line,
                    Column = node.Column,
                    Node = node,
                    Parameters = paramsStr,
                    Extends = "",
                    LogicTypes = new List<string>(),
                    Incoming = new List<string>(),
                    Outgoing = new List<string>()
                });
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                RegisterStaticDeclarations(child, nodeFile);
            }
        }

        private void TraverseFlow(AstNode node, string currentCallerId, string currentContainerName, string currentFilePath = "Main Script")
        {
            if (node == null) return;

            string resolvedFile = GetFilePathForLine(node.Line, currentFilePath);

            if (node.NodeType == "Include")
            {
                string filePath = !string.IsNullOrEmpty(node.Value) ? System.IO.Path.GetFileName(node.Value) : "Include File";
                foreach (var child in node.ChildNodes)
                {
                    TraverseFlow(child, currentCallerId, currentContainerName, filePath);
                }
                return;
            }

            if (node.NodeType == "Hotkey" || node.NodeType == "Hotstring")
            {
                _nodeCounter++;
                string hotkeyId = "hotkey_" + _nodeCounter;

                string tooltip = string.Format("<strong>{0}</strong><br/><span style='color: #a6adc8;'>Hotkey | File: {1} (Line {2})</span>",
                    SimpleHtmlEncode(node.Value),
                    SimpleHtmlEncode(resolvedFile),
                    node.Line);

                AddNode(hotkeyId, node.Value, "Hotkey", "#a6da95", resolvedFile, tooltip);

                Definitions.Add(new DefinitionInfo
                {
                    Id = hotkeyId,
                    Label = node.Value,
                    Category = node.NodeType,
                    FilePath = resolvedFile,
                    Line = node.Line,
                    Column = node.Column,
                    Node = node,
                    Parameters = "",
                    Extends = "",
                    LogicTypes = new List<string>(),
                    Incoming = new List<string>(),
                    Outgoing = new List<string>()
                });

                foreach (var child in node.ChildNodes)
                {
                    TraverseFlow(child, hotkeyId, node.Value, resolvedFile);
                }
                return;
            }

            if (node.NodeType == "Class")
            {
                string className = node.Value;
                foreach (var child in node.ChildNodes)
                {
                    if (child == null) continue;
                    if (child.NodeType == "StaticAssign" || child.NodeType == "Declaration")
                    {
                        TraverseFlow(child, "global_code", className, resolvedFile);
                    }
                    else if (child.NodeType == "Method")
                    {
                        string methodId = "method_" + className + "_" + child.Value;
                        foreach (var mc in child.ChildNodes)
                        {
                            TraverseFlow(mc, methodId, className + "." + child.Value, resolvedFile);
                        }
                    }
                    else if (child.NodeType == "Property")
                    {
                        string propId = "prop_" + className + "_" + child.Value;
                        foreach (var pc in child.ChildNodes)
                        {
                            TraverseFlow(pc, propId, className + "." + child.Value, resolvedFile);
                        }
                    }
                    else if (child.NodeType == "Class")
                    {
                        TraverseFlow(child, "global_code", className, resolvedFile);
                    }
                }
                return;
            }

            if (node.NodeType == "Method")
            {
                string funcName = node.Value;
                string funcId = "func_" + funcName;
                foreach (var child in node.ChildNodes)
                {
                    TraverseFlow(child, funcId, funcName, resolvedFile);
                }
                return;
            }

            if (node.NodeType == "Call")
            {
                var callee = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (callee != null)
                {
                    if (callee.NodeType == "Identifier")
                    {
                        string name = callee.Value;
                        string targetId;
                        if (_symbolToNodeId.TryGetValue(name, out targetId))
                        {
                            AddEdge(currentCallerId, targetId, "calls");
                        }
                    }
                    else if (callee.NodeType == "Member")
                    {
                        string memberName = callee.Value;
                        var obj = callee.ChildCount > 0 ? callee.GetChild(0) : null;
                        if (obj != null && obj.NodeType == "Identifier")
                        {
                            string objName = obj.Value;
                            string fullSymbol = objName + "." + memberName;
                            string targetId;
                            if (_symbolToNodeId.TryGetValue(fullSymbol, out targetId))
                            {
                                AddEdge(currentCallerId, targetId, "calls");
                            }
                            else
                            {
                                var matchingMethods = _symbolToNodeId.Where(kv => kv.Key.EndsWith("." + memberName)).Select(kv => kv.Value).ToList();
                                foreach (var methodId in matchingMethods)
                                {
                                    AddEdge(currentCallerId, methodId, "calls (dynamic)");
                                }
                            }
                        }
                        else
                        {
                            var matchingMethods = _symbolToNodeId.Where(kv => kv.Key.EndsWith("." + memberName)).Select(kv => kv.Value).ToList();
                            foreach (var methodId in matchingMethods)
                            {
                                AddEdge(currentCallerId, methodId, "calls (dynamic)");
                            }
                        }
                    }
                }

                foreach (var child in node.ChildNodes)
                {
                    TraverseFlow(child, currentCallerId, currentContainerName, resolvedFile);
                }
                return;
            }

            if (node.NodeType == "Identifier")
            {
                string name = node.Value;
                string targetId;
                if (_symbolToNodeId.TryGetValue(name, out targetId))
                {
                    if (currentCallerId != targetId)
                    {
                        AddEdge(currentCallerId, targetId, "references");
                    }
                }
                return;
            }

            bool showBranchHere = _includeBranches;
            if (resolvedFile != "Main Script" && !_includeLibraryDetails)
            {
                showBranchHere = false;
            }

            if (showBranchHere && (node.NodeType == "If" || node.NodeType == "While" || node.NodeType == "Loop" || node.NodeType == "For" || node.NodeType == "Try"))
            {
                _nodeCounter++;
                string branchId = "branch_" + _nodeCounter;
                string label = node.NodeType;

                if (node.NodeType == "If" && node.ChildCount > 0)
                {
                    string cond = "";
                    try { cond = AstEmitter.Emit(node.GetChild(0)); } catch { cond = "?"; }
                    if (cond.Length > 20) cond = cond.Substring(0, 17) + "...";
                    label = "If (" + cond + ")";
                }
                else if ((node.NodeType == "While" || node.NodeType == "Loop" || node.NodeType == "For") && node.ChildCount > 0)
                {
                    label = node.NodeType + " loop";
                }

                string tooltip = string.Format("<strong>{0}</strong><br/><span style='color: #a6adc8;'>Control Flow | File: {1} (Line {2})</span>",
                    SimpleHtmlEncode(label),
                    SimpleHtmlEncode(resolvedFile),
                    node.Line);

                AddNode(branchId, label, "Branch", "#f5a97f", resolvedFile, tooltip);
                AddEdge(currentCallerId, branchId, "flows");

                foreach (var child in node.ChildNodes)
                {
                    TraverseFlow(child, branchId, currentContainerName, resolvedFile);
                }
                return;
            }

            foreach (var child in node.ChildNodes)
            {
                TraverseFlow(child, currentCallerId, currentContainerName, resolvedFile);
            }
        }
    }
}
