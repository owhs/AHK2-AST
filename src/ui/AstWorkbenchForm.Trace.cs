// AHK# AST Workbench — Trace Visualizer
// Provides a dedicated dock panel for loading, parsing, and rendering AHK execution traces.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal partial class AstWorkbenchForm : Form
{
    private void BuildTraceVisualizerPanel()
    {
        _tracePanel = new Panel { Dock = DockStyle.Fill, BackColor = WbTheme.Base };

        // 1. Toolbar / Header panel
        var header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = WbTheme.Mantle, Padding = new Padding(8, 4, 8, 4) };
        var headerLabel = new Label { Text = "TRACE VISUALIZER", ForeColor = WbTheme.Subtext0, Font = WbTheme.UIFont, AutoSize = true, Location = new Point(8, 11) };

        _chkAutoLoadTrace = new CheckBox
        {
            Text = "Auto Load Trace",
            ForeColor = WbTheme.Subtext1,
            Font = WbTheme.UISmall,
            Checked = true,
            AutoSize = true,
            Location = new Point(180, 11),
            FlatStyle = FlatStyle.Flat
        };

        _btnLoadTrace = new Button
        {
            Text = "Load JSON...",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Height = 24,
            Width = 90,
            Location = new Point(320, 8)
        };
        _btnLoadTrace.FlatAppearance.BorderColor = WbTheme.Surface1;
        _btnLoadTrace.Click += (s, e) => LoadTraceDialog();

        _btnClearTrace = new Button
        {
            Text = "Clear",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Height = 24,
            Width = 70,
            Location = new Point(415, 8)
        };
        _btnClearTrace.FlatAppearance.BorderColor = WbTheme.Surface1;
        _btnClearTrace.Click += (s, e) => ClearTrace();

        _btnOpenHtmlView = new Button
        {
            Text = "Open HTML View...",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Surface0,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Height = 24,
            Width = 130,
            Location = new Point(490, 8)
        };
        _btnOpenHtmlView.FlatAppearance.BorderColor = WbTheme.Surface1;
        _btnOpenHtmlView.Click += (s, e) => OpenHtmlTraceView();

        header.Controls.AddRange(new Control[] { headerLabel, _chkAutoLoadTrace, _btnLoadTrace, _btnClearTrace, _btnOpenHtmlView });

        // 2. Split container
        _traceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
            BackColor = WbTheme.Mantle
        };

        // 3. Tab Control (Top pane)
        _traceTabs = new BorderlessTabControl
        {
            Dock = DockStyle.Fill,
            Font = WbTheme.UISmall,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(0, 1) // Hide standard headers
        };

        // Custom Tab Strip Panel
        _traceTabStrip = new Panel
        {
            Name = "tabStrip",
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = WbTheme.Mantle
        };

        _btnTraceCallTree = new Button
        {
            Name = "btnCallTree",
            Text = "Call Tree",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.UISmall,
            Height = 30,
            Width = 100,
            Location = new Point(0, 0)
        };
        _btnTraceCallTree.FlatAppearance.BorderSize = 0;

        _btnTraceWaterfall = new Button
        {
            Name = "btnWaterfall",
            Text = "Timeline Waterfall",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Mantle,
            ForeColor = WbTheme.Subtext1,
            Font = WbTheme.UISmall,
            Height = 30,
            Width = 140,
            Location = new Point(100, 0)
        };
        _btnTraceWaterfall.FlatAppearance.BorderSize = 0;

        _btnTraceStats = new Button
        {
            Name = "btnStats",
            Text = "Execution Stats",
            FlatStyle = FlatStyle.Flat,
            BackColor = WbTheme.Mantle,
            ForeColor = WbTheme.Subtext1,
            Font = WbTheme.UISmall,
            Height = 30,
            Width = 120,
            Location = new Point(240, 0)
        };
        _btnTraceStats.FlatAppearance.BorderSize = 0;

        Action<int> selectTab = (index) =>
        {
            _traceTabs.SelectedIndex = index;
            _btnTraceCallTree.BackColor = index == 0 ? WbTheme.Base : WbTheme.Mantle;
            _btnTraceCallTree.ForeColor = index == 0 ? WbTheme.Text : WbTheme.Subtext1;

            _btnTraceWaterfall.BackColor = index == 1 ? WbTheme.Base : WbTheme.Mantle;
            _btnTraceWaterfall.ForeColor = index == 1 ? WbTheme.Text : WbTheme.Subtext1;

            _btnTraceStats.BackColor = index == 2 ? WbTheme.Base : WbTheme.Mantle;
            _btnTraceStats.ForeColor = index == 2 ? WbTheme.Text : WbTheme.Subtext1;
        };

        _btnTraceCallTree.Click += (s, e) => selectTab(0);
        _btnTraceWaterfall.Click += (s, e) => selectTab(1);
        _btnTraceStats.Click += (s, e) => selectTab(2);

        _traceTabStrip.Controls.AddRange(new Control[] { _btnTraceCallTree, _btnTraceWaterfall, _btnTraceStats });

        // Tab 1: Call Tree
        _traceTreeTab = new TabPage("Call Tree") { BackColor = WbTheme.Base };
        _traceTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            FullRowSelect = true,
            HideSelection = false,
            ItemHeight = 22,
            DrawMode = TreeViewDrawMode.OwnerDrawText
        };
        _traceTree.HandleCreated += (s, e) => SetWindowTheme(_traceTree.Handle, "DarkMode_Explorer", null);
        _traceTree.DrawNode += TraceTree_DrawNode;
        _traceTree.AfterSelect += TraceTree_AfterSelect;
        EnableDoubleBuffering(_traceTree);
        _traceTreeTab.Controls.Add(_traceTree);

        // Tab 2: Timeline Waterfall Flame Chart
        _traceWaterfallTab = new TabPage("Timeline Waterfall") { BackColor = WbTheme.Base };
        _chartScrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = WbTheme.Base
        };
        _waterfallChart = new TraceWaterfallChart
        {
            Location = new Point(0, 0),
            Height = 200,
            Width = 400
        };
        _waterfallChart.ItemSelected += (s, item) =>
        {
            SelectTreeNodeForItem(item);
            ShowTraceDetailsForDict(item.RawDict);
        };
        _chartScrollPanel.Controls.Add(_waterfallChart);
        _chartScrollPanel.Resize += (s, e) =>
        {
            _waterfallChart.Width = Math.Max(_chartScrollPanel.Width - 25, 400);
        };
        _traceWaterfallTab.Controls.Add(_chartScrollPanel);

        // Tab 3: Execution Stats
        _traceStatsTab = new TabPage("Execution Stats") { BackColor = WbTheme.Base };
        _traceStatsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoSmall,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true
        };
        _traceStatsList.DrawColumnHeader += (s, e) =>
        {
            using (var brush = new SolidBrush(WbTheme.Mantle))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            using (var pen = new Pen(WbTheme.Surface1))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            Color textCol = WbTheme.Subtext0;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var col = _traceStatsList.Columns[e.ColumnIndex];
            if (col.TextAlign == HorizontalAlignment.Right)
                flags |= TextFormatFlags.Right;
            else if (col.TextAlign == HorizontalAlignment.Center)
                flags |= TextFormatFlags.HorizontalCenter;
            else
                flags |= TextFormatFlags.Left;

            var textBounds = e.Bounds;
            textBounds.Inflate(-6, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, _traceStatsList.Font, textBounds, textCol, flags);
        };
        _traceStatsList.DrawItem += (s, e) => { /* do not draw default to avoid standard list view item drawing */ };
        _traceStatsList.DrawSubItem += (s, e) =>
        {
            bool isSelected = (e.ItemState & ListViewItemStates.Selected) != 0;
            Color bg = isSelected ? WbTheme.Selection : _traceStatsList.BackColor;
            Color fg = isSelected ? WbTheme.Text : WbTheme.Subtext0;

            // Paint cell background
            using (var brush = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // Determine text formatting flags and alignment
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var col = _traceStatsList.Columns[e.ColumnIndex];
            if (col.TextAlign == HorizontalAlignment.Right)
                flags |= TextFormatFlags.Right;
            else if (col.TextAlign == HorizontalAlignment.Center)
                flags |= TextFormatFlags.HorizontalCenter;
            else
                flags |= TextFormatFlags.Left;

            // Draw cell text
            var textBounds = e.Bounds;
            textBounds.Inflate(-6, 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _traceStatsList.Font, textBounds, fg, flags);

            // Draw custom gridlines (subtle dark line matching theme)
            using (var pen = new Pen(WbTheme.Surface0))
            {
                // Bottom border
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                // Right border
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            }
        };
        _traceStatsList.HandleCreated += (s, e) => SetWindowTheme(_traceStatsList.Handle, "DarkMode_Explorer", null);
        _traceStatsList.Columns.Add("Function / Line", 220);
        _traceStatsList.Columns.Add("File / Category", 130);
        _traceStatsList.Columns.Add("Calls", 60, HorizontalAlignment.Right);
        _traceStatsList.Columns.Add("Total Time (ms)", 110, HorizontalAlignment.Right);
        _traceStatsList.Columns.Add("Avg Time (ms)", 100, HorizontalAlignment.Right);
        _traceStatsList.Columns.Add("Max Time (ms)", 100, HorizontalAlignment.Right);
        _traceStatsList.Columns.Add("% of Total", 90, HorizontalAlignment.Right);
        _traceStatsList.SelectedIndexChanged += TraceStatsList_SelectedIndexChanged;
        
        // Sorting behavior
        _traceStatsList.ColumnClick += (s, e) =>
        {
            bool asc = true;
            var comparer = _traceStatsList.ListViewItemSorter as ListViewItemComparer;
            if (comparer != null)
            {
                if (comparer.Col == e.Column) asc = !comparer.Asc;
            }
            _traceStatsList.ListViewItemSorter = new ListViewItemComparer(e.Column, asc);
            _traceStatsList.Sort();
        };

        // Auto-stretch first column to prevent unthemed white slack space on the right of header columns
        _traceStatsList.Resize += (s, e) =>
        {
            int otherColsWidth = 0;
            for (int idx = 1; idx < _traceStatsList.Columns.Count; idx++)
            {
                otherColsWidth += _traceStatsList.Columns[idx].Width;
            }
            int remaining = _traceStatsList.ClientSize.Width - otherColsWidth - 4;
            if (remaining > 100)
            {
                _traceStatsList.Columns[0].Width = remaining;
            }
        };

        _traceStatsTab.Controls.Add(_traceStatsList);

        _traceTabs.TabPages.AddRange(new TabPage[] { _traceTreeTab, _traceWaterfallTab, _traceStatsTab });
        _traceSplit.Panel1.Controls.Add(_traceTabs);
        _traceSplit.Panel1.Controls.Add(_traceTabStrip); // Add custom tab strip

        // 4. Details panel (Bottom pane)
        _traceDetails = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = WbTheme.MonoFont,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            WordWrap = true,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        _traceDetails.HandleCreated += (s, e) => SetWindowTheme(_traceDetails.Handle, "DarkMode_Explorer", null);
        _traceSplit.Panel2.Controls.Add(_traceDetails);

        _tracePanel.Controls.Add(_traceSplit);
        _tracePanel.Controls.Add(header);
    }

    private void TraceTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
    {
        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        Color bg = selected ? WbTheme.Selection : WbTheme.Base;
        Color fg = WbTheme.Text;

        if (e.Node.Tag is Dictionary<string, object>)
        {
            var dict = (Dictionary<string, object>)e.Node.Tag;
            bool isLine = dict.ContainsKey("Type") && dict["Type"].ToString() == "Line";

            if (dict.ContainsKey("Error"))
            {
                fg = WbTheme.Red;
            }
            else if (isLine)
            {
                fg = WbTheme.Overlay0;
            }
            else
            {
                double elapsed = dict.ContainsKey("Elapsed") ? Convert.ToDouble(dict["Elapsed"]) : 0.0;
                if (elapsed > 100.0) fg = WbTheme.Red;
                else if (elapsed > 10.0) fg = WbTheme.Yellow;
                else fg = WbTheme.Green;
            }
        }

        using (var bgBrush = new SolidBrush(bg))
        {
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Node.Text, _traceTree.Font, e.Bounds, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void TraceTree_AfterSelect(object sender, TreeViewEventArgs e)
    {
        var node = e.Node;
        if (node == null || node.Tag == null) return;

        var dict = node.Tag as Dictionary<string, object>;
        if (dict == null) return;

        ShowTraceDetailsForDict(dict);
        _waterfallChart.SelectItem(dict);
    }

    private void TraceStatsList_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (_traceStatsList.SelectedItems.Count == 0) return;
        var lvi = _traceStatsList.SelectedItems[0];
        var list = lvi.Tag as List<TraceItem>;
        ShowStatsDetails(list);
    }

    private void SelectTreeNodeForItem(TraceItem item)
    {
        if (item == null || item.RawDict == null) return;
        var node = FindTreeNodeByTag(_traceTree.Nodes, item.RawDict);
        if (node != null)
        {
            _traceTree.SelectedNode = node;
            _traceTree.Focus();
        }
    }

    private TreeNode FindTreeNodeByTag(TreeNodeCollection nodes, object tag)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag == tag) return node;
            var child = FindTreeNodeByTag(node.Nodes, tag);
            if (child != null) return child;
        }
        return null;
    }

    private void ShowTraceDetailsForDict(Dictionary<string, object> dict)
    {
        if (dict == null) return;

        var sb = new StringBuilder();
        bool isLine = dict.ContainsKey("Type") && dict["Type"].ToString() == "Line";

        if (isLine)
        {
            sb.AppendLine("═══ LINE TRACE DETAILS ═══");
            sb.AppendLine();
            sb.AppendLine("Type:       Line Execution");
            sb.AppendLine("Line:       " + (dict.ContainsKey("Line") ? dict["Line"].ToString() : ""));
            sb.AppendLine("File:       " + (dict.ContainsKey("File") ? dict["File"].ToString() : "Main"));
            sb.AppendLine("Code:       " + (dict.ContainsKey("Code") ? dict["Code"].ToString() : ""));
            if (dict.ContainsKey("Timestamp"))
                sb.AppendLine("Timestamp:  " + dict["Timestamp"].ToString());
            sb.AppendLine("Start time: " + (dict.ContainsKey("Start") ? string.Format("{0:F4} ms", dict["Start"]) : "0 ms"));
            sb.AppendLine("Time delta: " + (dict.ContainsKey("Elapsed") ? string.Format("{0:F4} ms", dict["Elapsed"]) : "0 ms"));
        }
        else
        {
            sb.AppendLine("═══ FUNCTION TRACE DETAILS ═══");
            sb.AppendLine();
            sb.AppendLine("Function:   " + (dict.ContainsKey("Name") ? dict["Name"].ToString() : ""));
            sb.AppendLine("File:       " + (dict.ContainsKey("File") ? dict["File"].ToString() : "Main"));
            if (dict.ContainsKey("Timestamp"))
                sb.AppendLine("Timestamp:  " + dict["Timestamp"].ToString());
            sb.AppendLine("Start time: " + (dict.ContainsKey("Start") ? string.Format("{0:F4} ms", dict["Start"]) : "0 ms"));
            sb.AppendLine("Duration:   " + (dict.ContainsKey("Elapsed") ? string.Format("{0:F4} ms", dict["Elapsed"]) : "0 ms"));

            if (dict.ContainsKey("Params"))
            {
                sb.AppendLine();
                sb.AppendLine("Parameters passed:");
                var paramsObj = dict["Params"];
                if (paramsObj is System.Collections.ArrayList)
                {
                    var list = (System.Collections.ArrayList)paramsObj;
                    for (int i = 0; i < list.Count; i += 2)
                    {
                        if (i + 1 < list.Count)
                        {
                            sb.AppendLine(string.Format("  - {0}: {1}", list[i], list[i + 1]));
                        }
                    }
                }
                else if (paramsObj != null && !string.IsNullOrEmpty(paramsObj.ToString()))
                {
                    sb.AppendLine("  " + paramsObj.ToString());
                }
                else
                {
                    sb.AppendLine("  (none)");
                }
            }

            if (dict.ContainsKey("RetVal"))
            {
                sb.AppendLine();
                sb.AppendLine("Return Value:");
                sb.AppendLine("  " + dict["RetVal"].ToString());
            }

            if (dict.ContainsKey("Error"))
            {
                sb.AppendLine();
                sb.AppendLine("❌ ERROR / EXCEPTION:");
                sb.AppendLine("  " + dict["Error"].ToString());
            }
        }

        _traceDetails.Text = sb.ToString();
    }

    private void PopulateStatsTab(List<TraceItem> flatItems)
    {
        _traceStatsList.BeginUpdate();
        _traceStatsList.Items.Clear();

        double totalTime = 0.0;
        foreach (var item in flatItems)
        {
            if (item.Depth == 0) totalTime += item.Elapsed;
        }
        if (totalTime <= 0.0)
        {
            foreach (var item in flatItems)
            {
                double end = item.Start + item.Elapsed;
                if (end > totalTime) totalTime = end;
            }
        }

        var groups = new Dictionary<string, List<TraceItem>>();
        foreach (var item in flatItems)
        {
            string key = item.Name + "||" + item.File;
            if (!groups.ContainsKey(key))
            {
                groups[key] = new List<TraceItem>();
            }
            groups[key].Add(item);
        }

        foreach (var kv in groups)
        {
            var list = kv.Value;
            var first = list[0];

            int calls = list.Count;
            double sumTime = 0.0;
            double maxTime = 0.0;
            foreach (var item in list)
            {
                sumTime += item.Elapsed;
                if (item.Elapsed > maxTime) maxTime = item.Elapsed;
            }
            double avgTime = sumTime / calls;
            double pct = totalTime > 0 ? (sumTime * 100.0 / totalTime) : 0.0;

            var lvi = new ListViewItem(first.Name);
            lvi.SubItems.Add(first.File);
            lvi.SubItems.Add(calls.ToString());
            lvi.SubItems.Add(string.Format("{0:F4}", sumTime));
            lvi.SubItems.Add(string.Format("{0:F4}", avgTime));
            lvi.SubItems.Add(string.Format("{0:F4}", maxTime));
            lvi.SubItems.Add(string.Format("{0:F1}%", pct));
            lvi.Tag = list;

            _traceStatsList.Items.Add(lvi);
        }

        _traceStatsList.ListViewItemSorter = new ListViewItemComparer(3, false); // Sort by Total Time desc
        _traceStatsList.Sort();
        _traceStatsList.EndUpdate();
    }

    private void ShowStatsDetails(List<TraceItem> list)
    {
        if (list == null || list.Count == 0) return;
        var first = list[0];

        var sb = new StringBuilder();
        sb.AppendLine("═══ FUNCTION PROFILE STATISTICS ═══");
        sb.AppendLine();
        sb.AppendLine("Function:     " + first.Name);
        sb.AppendLine("File:         " + first.File);
        sb.AppendLine("Total Calls:  " + list.Count);

        double sumTime = 0.0;
        double maxTime = 0.0;
        double minTime = double.MaxValue;
        foreach (var item in list)
        {
            sumTime += item.Elapsed;
            if (item.Elapsed > maxTime) maxTime = item.Elapsed;
            if (item.Elapsed < minTime) minTime = item.Elapsed;
        }
        if (minTime == double.MaxValue) minTime = 0.0;

        sb.AppendLine("Total Time:   " + string.Format("{0:F4} ms", sumTime));
        sb.AppendLine("Average Time: " + string.Format("{0:F4} ms", sumTime / list.Count));
        sb.AppendLine("Min Time:     " + string.Format("{0:F4} ms", minTime));
        sb.AppendLine("Max Time:     " + string.Format("{0:F4} ms", maxTime));
        sb.AppendLine();

        // Find callers
        var callers = new Dictionary<string, int>();
        foreach (var item in list)
        {
            if (item.Parent != null)
            {
                string pName = item.Parent.Name;
                callers[pName] = callers.ContainsKey(pName) ? callers[pName] + 1 : 1;
            }
        }

        sb.AppendLine("Callers (Who called this):");
        if (callers.Count > 0)
        {
            foreach (var kv in callers)
            {
                sb.AppendLine(string.Format("  - {0} ({1} calls)", kv.Key, kv.Value));
            }
        }
        else
        {
            sb.AppendLine("  (none - root level function)");
        }
        sb.AppendLine();

        // Find callees
        var callees = new Dictionary<string, int>();
        foreach (var item in list)
        {
            foreach (var child in item.Children)
            {
                string cName = child.Name;
                callees[cName] = callees.ContainsKey(cName) ? callees[cName] + 1 : 1;
            }
        }

        sb.AppendLine("Callees (What this called):");
        if (callees.Count > 0)
        {
            foreach (var kv in callees)
            {
                sb.AppendLine(string.Format("  - {0} ({1} calls)", kv.Key, kv.Value));
            }
        }
        else
        {
            sb.AppendLine("  (none - leaf level function)");
        }

        _traceDetails.Text = sb.ToString();
    }

    private void LoadTraceDialog()
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Filter = "Trace Logs (*.json)|*.json|All Files (*.*)|*.*";
            dlg.Title = "Load Trace JSON";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                LoadTraceFile(dlg.FileName);
            }
        }
    }

    private void ClearTrace()
    {
        _traceTree.Nodes.Clear();
        _traceDetails.Clear();
        _traceStatsList.Items.Clear();
        _flatTraceItems.Clear();
        _waterfallChart.SetTraceData(new List<TraceItem>(), 0.0);
        _statusLabel.Text = "Trace cleared";
    }

    public void LoadTraceFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            _lastLoadedTraceFile = filePath;
            string jsonText = File.ReadAllText(filePath, Encoding.UTF8);
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            var rootList = serializer.Deserialize<List<object>>(jsonText);

            _traceTree.BeginUpdate();
            _traceTree.Nodes.Clear();

            _flatTraceItems.Clear();
            var parsedRootItems = new List<TraceItem>();

            if (rootList != null)
            {
                foreach (var obj in rootList)
                {
                    var dict = obj as Dictionary<string, object>;
                    if (dict != null)
                    {
                        AddTraceNodeToTree(dict, _traceTree.Nodes);
                    }
                }

                // Build trace items hierarchy
                ParseTraceItems(rootList, parsedRootItems, null, 0);
            }
            _traceTree.EndUpdate();
            _traceTree.ExpandAll();

            // Populate Waterfall flame timeline barchart
            double totalDuration = 0.0;
            int maxDepth = 0;
            foreach (var item in _flatTraceItems)
            {
                double end = item.Start + item.Elapsed;
                if (end > totalDuration) totalDuration = end;
                if (item.Depth > maxDepth) maxDepth = item.Depth;
            }

            _waterfallChart.SetTraceData(parsedRootItems, totalDuration);
            int requiredHeight = 40 + (maxDepth + 1) * 24 + 30;
            _waterfallChart.Height = Math.Max(requiredHeight, _chartScrollPanel.Height - 10);
            _waterfallChart.Width = Math.Max(_chartScrollPanel.Width - 25, 400);

            // Populate Stats Tab
            PopulateStatsTab(_flatTraceItems);

            if (_traceVisualizerContent != null)
            {
                if (_traceVisualizerContent.IsHidden)
                {
                    _traceVisualizerContent.Show(_dockPanel, DockState.DockBottom);
                }
                _traceVisualizerContent.Activate();
            }

            _statusLabel.Text = "Loaded trace file: " + Path.GetFileName(filePath);
            _statusLabel.ForeColor = WbTheme.Green;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load trace: " + ex.Message, "Trace Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ParseTraceItems(System.Collections.IEnumerable rawList, List<TraceItem> targetList, TraceItem parent, int depth)
    {
        if (rawList == null) return;
        foreach (var obj in rawList)
        {
            var dict = obj as Dictionary<string, object>;
            if (dict == null) continue;

            var item = new TraceItem();
            item.RawDict = dict;
            item.Parent = parent;
            item.Depth = depth;
            item.Type = dict.ContainsKey("Type") && dict["Type"].ToString() == "Line" ? "Line" : "Function";
            item.Name = item.Type == "Line"
                ? "Line " + (dict.ContainsKey("Line") ? dict["Line"].ToString() : "")
                : (dict.ContainsKey("Name") ? dict["Name"].ToString() : "Function");
            item.Start = dict.ContainsKey("Start") ? Convert.ToDouble(dict["Start"]) : 0.0;
            item.Elapsed = dict.ContainsKey("Elapsed") ? Convert.ToDouble(dict["Elapsed"]) : 0.0;
            item.File = dict.ContainsKey("File") ? dict["File"].ToString() : "Main";
            item.Timestamp = dict.ContainsKey("Timestamp") ? dict["Timestamp"].ToString() : "";

            targetList.Add(item);
            _flatTraceItems.Add(item);

            if (dict.ContainsKey("Children"))
            {
                var children = dict["Children"] as System.Collections.ArrayList;
                if (children != null)
                {
                    ParseTraceItems(children, item.Children, item, depth + 1);
                }
            }
        }
    }

    private void AddTraceNodeToTree(Dictionary<string, object> nodeData, TreeNodeCollection parentNodes)
    {
        string text = "";
        bool isLine = nodeData.ContainsKey("Type") && nodeData["Type"].ToString() == "Line";

        if (isLine)
        {
            string code = nodeData.ContainsKey("Code") ? nodeData["Code"].ToString() : "";
            double elapsed = nodeData.ContainsKey("Elapsed") ? Convert.ToDouble(nodeData["Elapsed"]) : 0.0;
            int line = nodeData.ContainsKey("Line") ? Convert.ToInt32(nodeData["Line"]) : 0;
            string file = nodeData.ContainsKey("File") ? nodeData["File"].ToString() : "Main";
            text = string.Format("Line {0} [{1}]: {2} ({3:F2} ms)", line, file, code, elapsed);
        }
        else
        {
            string name = nodeData.ContainsKey("Name") ? nodeData["Name"].ToString() : "Function";
            double elapsed = nodeData.ContainsKey("Elapsed") ? Convert.ToDouble(nodeData["Elapsed"]) : 0.0;
            string file = nodeData.ContainsKey("File") ? nodeData["File"].ToString() : "Main";
            text = string.Format("{0}() [{1}] ({2:F2} ms)", name, file, elapsed);
            if (nodeData.ContainsKey("Error"))
            {
                text += " [❌ ERROR]";
            }
        }

        var node = new TreeNode(text);
        node.Tag = nodeData;

        parentNodes.Add(node);

        if (nodeData.ContainsKey("Children"))
        {
            var children = nodeData["Children"] as System.Collections.ArrayList;
            if (children != null)
            {
                foreach (var childObj in children)
                {
                    var childDict = childObj as Dictionary<string, object>;
                    if (childDict != null)
                    {
                        AddTraceNodeToTree(childDict, node.Nodes);
                    }
                }
            }
        }
    }

    private void AutoLoadTraceAfterRun(string workDir)
    {
        if (_chkAutoLoadTrace == null || !_chkAutoLoadTrace.Checked) return;

        string tracePath = Path.Combine(workDir, "__trace_dump.json");
        if (!File.Exists(tracePath) && !string.IsNullOrEmpty(_currentFile))
        {
            tracePath = Path.Combine(Path.GetDirectoryName(_currentFile), "__trace_dump.json");
        }

        if (File.Exists(tracePath))
        {
            BeginInvoke((Action)(() => LoadTraceFile(tracePath)));
        }
    }

    private void ShowTraceVisualizer()
    {
        if (_traceVisualizerContent != null)
        {
            _traceVisualizerContent.Show(_dockPanel, DockState.DockBottom);
            _traceVisualizerContent.Activate();
        }
    }

    private void OpenHtmlTraceView()
    {
        string jsonPath = _lastLoadedTraceFile;
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            string fallback = Path.Combine(Directory.GetCurrentDirectory(), "__trace_dump.json");
            if (File.Exists(fallback))
            {
                jsonPath = fallback;
            }
        }

        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            MessageBox.Show("No trace file loaded. Please compile/run a script with tracing enabled or load a trace JSON first.",
                            "No Trace Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string jsonText = File.ReadAllText(jsonPath, Encoding.UTF8);
            string base64Json = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonText));

            string html = AHK2AST.TraceViewerHtmlTemplate.Html
                .Replace("/* TRACE_DATA_PLACEHOLDER */", base64Json)
                .Replace("trace_log.json", Path.GetFileName(jsonPath));

            string tempHtmlPath = Path.Combine(Path.GetTempPath(), "ahk_trace_viewer.html");
            File.WriteAllText(tempHtmlPath, html, Encoding.UTF8);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempHtmlPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to launch HTML trace visualizer: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ── Custom Sorter for ListView ─────────────────────────────────────────
internal class ListViewItemComparer : System.Collections.IComparer
{
    private int _col;
    private bool _asc;
    public int Col { get { return _col; } }
    public bool Asc { get { return _asc; } }

    public ListViewItemComparer(int col, bool asc)
    {
        _col = col;
        _asc = asc;
    }

    public int Compare(object x, object y)
    {
        var lviX = (ListViewItem)x;
        var lviY = (ListViewItem)y;

        string textX = lviX.SubItems[_col].Text;
        string textY = lviY.SubItems[_col].Text;

        double dX, dY;
        bool isNumX = double.TryParse(textX.Replace("%", "").Trim(), out dX);
        bool isNumY = double.TryParse(textY.Replace("%", "").Trim(), out dY);

        int res;
        if (isNumX && isNumY)
        {
            res = dX.CompareTo(dY);
        }
        else
        {
            res = string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);
        }

        return _asc ? res : -res;
    }
}

// ── Trace Waterfall flame timeline chart control ───────────────────────
internal class TraceWaterfallChart : Control
{
    private List<TraceItem> _items = new List<TraceItem>();
    private List<TraceItem> _flatItems = new List<TraceItem>();
    private double _totalDuration = 0.0;
    private TraceItem _selectedItem = null;
    private TraceItem _hoveredItem = null;
    private ToolTip _toolTip = new ToolTip();

    public event EventHandler<TraceItem> ItemSelected;

    public TraceWaterfallChart()
    {
        DoubleBuffered = true;
        BackColor = WbTheme.Base;
        ForeColor = WbTheme.Text;
    }

    public void SetTraceData(List<TraceItem> items, double totalDuration)
    {
        _items = items;
        _totalDuration = totalDuration;
        _selectedItem = null;
        _hoveredItem = null;

        _flatItems.Clear();
        FlattenItems(_items);

        Invalidate();
    }

    private void FlattenItems(List<TraceItem> list)
    {
        foreach (var item in list)
        {
            _flatItems.Add(item);
            FlattenItems(item.Children);
        }
    }

    public void SelectItem(Dictionary<string, object> rawDict)
    {
        _selectedItem = _flatItems.Find(x => x.RawDict == rawDict);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_totalDuration <= 0 || _flatItems.Count == 0)
        {
            using (var brush = new SolidBrush(WbTheme.Subtext1))
            {
                e.Graphics.DrawString("No trace data loaded", Font, brush, 10, 10);
            }
            return;
        }

        float width = ClientSize.Width - 25;
        float scaleX = width / (float)_totalDuration;

        int barHeight = 22;
        int spacing = 2;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Draw grid lines for time intervals
        DrawTimeGrid(e.Graphics, scaleX, ClientSize.Height);

        foreach (var item in _flatItems)
        {
            float x = 10 + (float)item.Start * scaleX;
            float w = (float)item.Elapsed * scaleX;
            if (w < 2) w = 2; // Min width

            float y = 30 + item.Depth * (barHeight + spacing);
            item.Rect = new RectangleF(x, y, w, barHeight);

            // Colors based on execution speed / error
            Color barColor = WbTheme.Green;
            if (item.RawDict.ContainsKey("Error"))
            {
                barColor = WbTheme.Red;
            }
            else if (item.Type == "Line")
            {
                barColor = WbTheme.Surface1;
            }
            else
            {
                if (item.Elapsed > 100.0) barColor = WbTheme.Red;
                else if (item.Elapsed > 10.0) barColor = WbTheme.Yellow;
            }

            bool isSelected = (item == _selectedItem);
            bool isHovered = (item == _hoveredItem);

            // Draw bar background
            using (var brush = new SolidBrush(barColor))
            {
                e.Graphics.FillRectangle(brush, item.Rect);
            }

            // Draw border when selected or hovered
            if (isSelected)
            {
                using (var pen = new Pen(WbTheme.Text, 2))
                {
                    e.Graphics.DrawRectangle(pen, item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height);
                }
            }
            else if (isHovered)
            {
                using (var pen = new Pen(WbTheme.Subtext0, 1.5f))
                {
                    e.Graphics.DrawRectangle(pen, item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height);
                }
            }

            // Draw label inside bar if width permits
            if (w > 30)
            {
                string label = string.Format("{0} ({1:F2} ms)", item.Name, item.Elapsed);
                Color textCol = (barColor == WbTheme.Yellow) ? Color.Black : WbTheme.Base;
                if (item.Type == "Line") textCol = WbTheme.Text;

                TextRenderer.DrawText(e.Graphics, label, Font, Rectangle.Round(item.Rect), textCol,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    private void DrawTimeGrid(Graphics g, float scaleX, float height)
    {
        double interval = CalculateTimeInterval(_totalDuration);
        using (var pen = new Pen(WbTheme.Surface0, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        using (var brush = new SolidBrush(WbTheme.Subtext1))
        {
            for (double t = 0; t <= _totalDuration; t += interval)
            {
                float x = 10 + (float)t * scaleX;
                g.DrawLine(pen, x, 25, x, height);
                g.DrawString(string.Format("{0:F1} ms", t), Font, brush, x, 5);
            }
        }
    }

    private double CalculateTimeInterval(double total)
    {
        if (total < 10) return 1.0;
        if (total < 50) return 5.0;
        if (total < 200) return 20.0;
        if (total < 1000) return 100.0;
        return Math.Max(200.0, Math.Round(total / 5.0));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        TraceItem hit = null;
        foreach (var item in _flatItems)
        {
            if (item.Rect.Contains(e.Location))
            {
                hit = item;
                break;
            }
        }

        if (hit != _hoveredItem)
        {
            _hoveredItem = hit;
            Invalidate();

            if (hit != null)
            {
                string tip = string.Format("{0}\nFile: {1}\nStart: {2:F2} ms\nDuration: {3:F4} ms",
                    hit.Name, hit.File, hit.Start, hit.Elapsed);
                if (!string.IsNullOrEmpty(hit.Timestamp))
                {
                    tip += "\nTime: " + hit.Timestamp;
                }
                if (hit.RawDict.ContainsKey("Error"))
                {
                    tip += "\n❌ Error: " + hit.RawDict["Error"].ToString();
                }
                _toolTip.SetToolTip(this, tip);
            }
            else
            {
                _toolTip.Hide(this);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (_hoveredItem != null)
        {
            _selectedItem = _hoveredItem;
            var handler = ItemSelected;
            if (handler != null)
            {
                handler(this, _selectedItem);
            }
            Invalidate();
        }
    }
}

// ── TraceItem Helper Class ─────────────────────────────────────────────
internal class TraceItem
{
    public string Name { get; set; }
    public string Type { get; set; } // "Function" or "Line"
    public double Start { get; set; }
    public double Elapsed { get; set; }
    public string Timestamp { get; set; }
    public int Depth { get; set; }
    public string File { get; set; }
    public TraceItem Parent { get; set; }
    public Dictionary<string, object> RawDict { get; set; }
    public List<TraceItem> Children { get; set; }

    public RectangleF Rect { get; set; }

    public TraceItem()
    {
        Children = new List<TraceItem>();
    }
}
internal partial class AstWorkbenchForm : Form
{
    public void RefreshTabStripTheme()
    {
        if (_traceTabs == null || _traceTabStrip == null) return;
        int index = _traceTabs.SelectedIndex;
        
        _traceTabStrip.BackColor = WbTheme.Mantle;
        _btnTraceCallTree.BackColor = index == 0 ? WbTheme.Base : WbTheme.Mantle;
        _btnTraceCallTree.ForeColor = index == 0 ? WbTheme.Text : WbTheme.Subtext1;

        _btnTraceWaterfall.BackColor = index == 1 ? WbTheme.Base : WbTheme.Mantle;
        _btnTraceWaterfall.ForeColor = index == 1 ? WbTheme.Text : WbTheme.Subtext1;

        _btnTraceStats.BackColor = index == 2 ? WbTheme.Base : WbTheme.Mantle;
        _btnTraceStats.ForeColor = index == 2 ? WbTheme.Text : WbTheme.Subtext1;
    }
}

internal class BorderlessTabControl : TabControl
{
    private const int TCM_ADJUSTRECT = 0x1328;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }
}
