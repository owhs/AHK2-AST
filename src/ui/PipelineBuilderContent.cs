using System;
using System.Text;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using AHK2AST.UI;
using AHK2AST.Plugins;

public class StepCardControl : UserControl
{
    public object ConfigObject { get; private set; }
    public string Title { get; private set; }
    public string Icon { get; private set; }
    private Label _lblTitle;

    private bool _isSelected;
    public bool IsSelected
    {
        get { return _isSelected; }
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                Invalidate(); // trigger redraw
            }
        }
    }

    public void SetStepIndex(int index)
    {
        _lblTitle.Text = string.Format("{0}  {1}. {2}", Icon, index, Title);
    }

    public void RefreshTheme()
    {
        BackColor = WbTheme.Crust;
        ForeColor = WbTheme.Text;
        _lblTitle.ForeColor = WbTheme.Text;
        if (this.ContextMenuStrip != null)
        {
            this.ContextMenuStrip.BackColor = WbTheme.Crust;
            this.ContextMenuStrip.ForeColor = WbTheme.Text;
            foreach (ToolStripItem item in this.ContextMenuStrip.Items)
            {
                item.ForeColor = WbTheme.Text;
            }
        }
        this.Invalidate();
    }

    public StepCardControl(string title, string icon, object configObj)
    {
        Title = title;
        Icon = icon;
        ConfigObject = configObj;
        this.Width = 280;
        this.Height = 50;
        this.Margin = new Padding(8);
        this.Padding = new Padding(3); // Important! Prevent Label from covering our border
        this.BackColor = WbTheme.Crust;
        this.Cursor = Cursors.Hand;

        // This is important so the user control can paint its border properly
        this.SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer, true);

        _lblTitle = new Label
        {
            Text = string.Format("{0}  {1}", icon, title),
            ForeColor = WbTheme.Text,
            BackColor = Color.Transparent, // Let the custom background shine through
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(7, 0, 0, 0) // adjusted slightly to compensate for parent padding
        };

        // Pass click through label to parent
        _lblTitle.Click += (s, e) => this.InvokeOnClick(this, EventArgs.Empty);
        _lblTitle.MouseDown += (s, e) => this.OnMouseDown(e); // Pass down for drag-and-drop
        _lblTitle.MouseMove += (s, e) => this.OnMouseMove(e);
        _lblTitle.MouseUp += (s, e) => this.OnMouseUp(e);
        _lblTitle.MouseEnter += (s, e) => this.OnMouseEnter(e);
        _lblTitle.MouseLeave += (s, e) => this.OnMouseLeave(e);

        this.Controls.Add(_lblTitle);

        // Right-click context menu for deletion
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Renderer = new DarkMenuRenderer();

        ToolStripMenuItem deleteItem = new ToolStripMenuItem("❌ Delete Step");
        deleteItem.Click += (s, e) =>
        {
            var parent = this.Parent as FlowLayoutPanel;
            if (parent != null)
            {
                parent.Controls.Remove(this);
                this.Dispose();
            }
        };
        menu.Items.Add(deleteItem);

        menu.BackColor = WbTheme.Crust;
        menu.ForeColor = WbTheme.Text;
        deleteItem.ForeColor = WbTheme.Text;

        this.ContextMenuStrip = menu;
        _lblTitle.ContextMenuStrip = menu;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (IsSelected)
        {
            using (var pen = new Pen(WbTheme.Accent, 2f)) // Sexy vibrant accent border
            {
                e.Graphics.DrawRectangle(pen, 1, 1, this.Width - 2, this.Height - 2);
            }
        }
        else
        {
            using (var pen = new Pen(WbTheme.Surface0, 1f)) // Subtle unselected border
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        this.BackColor = WbTheme.Surface0; // Themed Hover effect
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
        {
            this.BackColor = WbTheme.Crust; // Themed Restore
        }
    }
}

public class PipelineBuilderContent : DockContent
{
    public TreeView PaletteTree { get; private set; }
    public ReorderableFlowLayoutPanel VisualInspector { get; private set; }
    public PropertyGrid PluginProperties { get; private set; }
    private DockPanel _innerDock;

    private DockContent _paletteContent;
    private DockContent _propsContent;
    private DockContent _inspectorContent;
    private ToolStrip _toolbar;
    private ToolStripButton _btnSave;
    private ToolStripButton _btnEmit;
    private ToolStripButton _btnRun;
    private ToolStripButton _btnExportAhk;

    // The Pipeline's metadata configuration
    private PipelineMeta _pipelineMeta;

    private bool _initialLayoutDone = false;

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    public PipelineBuilderContent()
    {
        _pipelineMeta = new PipelineMeta();

        Text = "New Flow";
        BackColor = WbTheme.Crust;
        ForeColor = WbTheme.Text;
        _innerDock = new DockPanel { Dock = DockStyle.Fill, DocumentStyle = DocumentStyle.DockingSdi, BackColor = WbTheme.Crust };

        // Setup inner dock theme to match main window
        ThemeBase innerTheme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
        _innerDock.Theme = innerTheme;
        AstWorkbenchForm.CustomizeDockPalette(innerTheme);

        // Left Pane: Palette Tree (Toolbox)
        PaletteTree = new TreeView
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Base,
            ForeColor = WbTheme.Text,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            BorderStyle = BorderStyle.None,
            ShowLines = false,
            ItemHeight = 24
        };
        PaletteTree.HandleCreated += (s, e) => SetWindowTheme(PaletteTree.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);

        PopulatePalette();
        PaletteTree.ItemDrag += PaletteTree_ItemDrag;

        // Middle Pane: Visual Inspector (The Sexy Draggable Canvas)
        VisualInspector = new ReorderableFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Mantle,
            AutoScroll = true,
            Padding = new Padding(10)
        };
        VisualInspector.HandleCreated += (s, e) => SetWindowTheme(VisualInspector.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);

        _toolbar = new ToolStrip { Dock = DockStyle.Top, BackColor = WbTheme.Mantle, GripStyle = ToolStripGripStyle.Hidden };
        _toolbar.Renderer = new DarkMenuRenderer();
        _btnSave = new ToolStripButton("💾 Save Flow");
        _btnSave.ForeColor = WbTheme.Text;
        _btnSave.Click += BtnSave_Click;

        _btnEmit = new ToolStripButton("✨ Emit Flow");
        _btnEmit.ForeColor = WbTheme.Sky;
        _btnEmit.Click += BtnEmit_Click;

        _btnRun = new ToolStripButton("▶ Run Flow");
        _btnRun.ForeColor = WbTheme.Green;
        _btnRun.Click += BtnRun_Click;

        _btnExportAhk = new ToolStripButton("📜 Export AHK");
        _btnExportAhk.ForeColor = WbTheme.Lavender;
        _btnExportAhk.Click += BtnExportAhk_Click;

        _toolbar.Items.Add(_btnSave);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnEmit);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnRun);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(_btnExportAhk);

        var inspectorContainer = new Panel { Dock = DockStyle.Fill };
        inspectorContainer.Controls.Add(VisualInspector);
        inspectorContainer.Controls.Add(_toolbar);

        VisualInspector.ExternalItemDropped += VisualInspector_ExternalItemDropped;
        VisualInspector.ControlAdded += (s, e) => { MarkDirty(); UpdateStepIndices(); };
        VisualInspector.ControlRemoved += (s, e) => { MarkDirty(); UpdateStepIndices(); };
        VisualInspector.FlowOrderChanged += (s, e) => UpdateStepIndices();
        VisualInspector.Click += (s, e) =>
        {
            // Click empty space -> show pipeline properties, unselect cards
            PluginProperties.SelectedObject = _pipelineMeta;
            foreach (Control c in VisualInspector.Controls)
            {
                if (c is StepCardControl)
                {
                    ((StepCardControl)c).IsSelected = false;
                }
            }
        };

        PluginProperties = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            BackColor = WbTheme.Crust,
            ViewBackColor = WbTheme.Base,
            ViewForeColor = WbTheme.Text,
            LineColor = WbTheme.Crust,
            HelpBackColor = WbTheme.Crust,
            HelpForeColor = WbTheme.Text,
            HelpBorderColor = WbTheme.Crust,
            ViewBorderColor = WbTheme.Crust,
            CategorySplitterColor = WbTheme.Crust,
            CommandsBorderColor = WbTheme.Crust,
            CategoryForeColor = WbTheme.Text,
            ToolbarVisible = false,
            HelpVisible = true
        };

        PluginProperties.PropertyValueChanged += (s, e) => MarkDirty();

        PluginProperties.SelectedObject = _pipelineMeta;

        // Hack to remove the ugly 3D white border around the inner PropertyGridView
        try
        {
            foreach (Control c in PluginProperties.Controls)
            {
                if (c.GetType().Name == "PropertyGridView" || c.GetType().Name == "DocComment")
                {
                    var prop = c.GetType().GetProperty("BorderStyle");
                    if (prop != null) prop.SetValue(c, BorderStyle.None, null);
                }
            }
        }
        catch { }
        // Wrap panes in DockContents
        _paletteContent = new DockContent { Text = "Toolbox", CloseButtonVisible = false, HideOnClose = true, BackColor = WbTheme.Crust };
        _paletteContent.Controls.Add(PaletteTree);

        _propsContent = new DockContent { Text = "Properties", CloseButtonVisible = false, HideOnClose = true, BackColor = WbTheme.Crust };
        _propsContent.Controls.Add(PluginProperties);

        _inspectorContent = new DockContent { Text = "Flow", CloseButtonVisible = false, HideOnClose = true, BackColor = WbTheme.Crust };
        _inspectorContent.Controls.Add(inspectorContainer);

        this.Controls.Add(_innerDock);

        _innerDock.Layout += (s, e) => SaveLayoutState();
    }

    private void SaveLayoutState()
    {
        if (!_initialLayoutDone) return;
        if (this.IsDisposed || this.Disposing) return;
        if (_innerDock == null || _innerDock.IsDisposed || _innerDock.Disposing) return;

        // Don't save if the window size is zero or invalid
        if (this.Width <= 0 || this.Height <= 0) return;

        // Use actual control widths if they are valid
        int leftVal = (_paletteContent != null && _paletteContent.Width > 10) ? _paletteContent.Width : 280;
        int rightVal = (_propsContent != null && _propsContent.Width > 10) ? _propsContent.Width : 350;

        // Sanity checks: must be reasonable values and not garbage from shutdown resize
        if (leftVal < 100 || leftVal > this.Width * 0.75 || leftVal > 1200) return;
        if (rightVal < 100 || rightVal > this.Width * 0.75 || rightVal > 1200) return;

        try
        {
            var state = AHK2AST.UI.WorkbenchState.Load();
            state.MainSplitterDistance = leftVal;
            state.RightSplitterDistance = rightVal;
            state.Save();
        }
        catch { }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // We'll apply the layout in a BeginInvoke so the dock panel size is finalized
        this.BeginInvoke(new Action(() =>
        {
            if (this.IsDisposed || this.Disposing) return;
            if (_innerDock == null || _innerDock.IsDisposed || _innerDock.Disposing) return;

            if (!_initialLayoutDone && this.Width > 0)
            {
                var state = AHK2AST.UI.WorkbenchState.Load();
                int leftPortion = state.MainSplitterDistance;
                int rightPortion = state.RightSplitterDistance;

                if (leftPortion < 100 || leftPortion > 1200) leftPortion = 280;
                if (rightPortion < 100 || rightPortion > 1200) rightPortion = 350;

                // Apply absolute pixel sizes now that _innerDock has its actual size!
                _innerDock.DockLeftPortion = leftPortion;
                _innerDock.DockRightPortion = rightPortion;

                // Show the contents in the DockPanel now that the layout portions are configured on a fully-sized panel!
                _inspectorContent.Show(_innerDock, DockState.Document);
                _paletteContent.Show(_innerDock, DockState.DockLeft);
                _propsContent.Show(_innerDock, DockState.DockRight);

                _initialLayoutDone = true;
            }
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    public void RefreshTheme()
    {
        if (this.IsDisposed || this.Disposing) return;
        BackColor = WbTheme.Crust;
        ForeColor = WbTheme.Text;

        if (_innerDock != null && !_innerDock.IsDisposed && !_innerDock.Disposing)
        {
            var states = new System.Collections.Generic.Dictionary<IDockContent, DockState>();
            var contents = _innerDock.Contents.ToList();

            // Undock everything before changing theme (avoids crashing)
            foreach (var content in contents)
            {
                states[content] = content.DockHandler.DockState;
                content.DockHandler.DockPanel = null;
            }

            try
            {
                ThemeBase innerTheme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
                AstWorkbenchForm.CustomizeDockPalette(innerTheme);

                // DockPanelSuite optimization bypass: if the new theme has the same class type as the old theme,
                // it ignores the setter. So we temporarily assign the opposite theme class to force a full redraw.
                if (_innerDock.Theme != null && _innerDock.Theme.GetType() == innerTheme.GetType())
                {
                    _innerDock.Theme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015LightTheme() : new VS2015DarkTheme();
                }

                _innerDock.Theme = innerTheme;
            }
            catch { }

            _innerDock.BackColor = WbTheme.Crust;

            // Redock everything back to its previous state
            foreach (var content in contents)
            {
                var c = content as DockContent;
                if (c != null)
                {
                    DockState ds = states.ContainsKey(content) ? states[content] : DockState.Unknown;
                    if (ds == DockState.Unknown || ds == DockState.Hidden)
                    {
                        if (c == _paletteContent) ds = DockState.DockLeft;
                        else if (c == _propsContent) ds = DockState.DockRight;
                        else if (c == _inspectorContent) ds = DockState.Document;
                    }
                    if (ds != DockState.Hidden && ds != DockState.Unknown)
                    {
                        c.Show(_innerDock, ds);
                    }
                }
            }
        }

        if (_paletteContent != null)
        {
            _paletteContent.BackColor = WbTheme.Crust;
            _paletteContent.ForeColor = WbTheme.Text;
        }
        if (_propsContent != null)
        {
            _propsContent.BackColor = WbTheme.Crust;
            _propsContent.ForeColor = WbTheme.Text;
        }
        if (_inspectorContent != null)
        {
            _inspectorContent.BackColor = WbTheme.Crust;
            _inspectorContent.ForeColor = WbTheme.Text;
        }

        if (_toolbar != null)
        {
            _toolbar.BackColor = WbTheme.Mantle;
        }
        if (_btnSave != null)
        {
            _btnSave.ForeColor = WbTheme.Text;
        }
        if (_btnEmit != null)
        {
            _btnEmit.ForeColor = WbTheme.Sky;
        }
        if (_btnRun != null)
        {
            _btnRun.ForeColor = WbTheme.Green;
        }

        PaletteTree.BackColor = WbTheme.Base;
        PaletteTree.ForeColor = WbTheme.Text;
        VisualInspector.BackColor = WbTheme.Mantle;

        PluginProperties.BackColor = WbTheme.Crust;
        PluginProperties.ViewBackColor = WbTheme.Base;
        PluginProperties.ViewForeColor = WbTheme.Text;
        PluginProperties.LineColor = WbTheme.Crust;
        PluginProperties.HelpBackColor = WbTheme.Crust;
        PluginProperties.HelpForeColor = WbTheme.Text;
        PluginProperties.HelpBorderColor = WbTheme.Crust;
        PluginProperties.ViewBorderColor = WbTheme.Crust;
        PluginProperties.CategorySplitterColor = WbTheme.Crust;
        PluginProperties.CommandsBorderColor = WbTheme.Crust;
        PluginProperties.CategoryForeColor = WbTheme.Text;
        PluginProperties.Invalidate();
        PluginProperties.Refresh();

        string theme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";
        SetWindowTheme(PaletteTree.Handle, theme, null);
        SetWindowTheme(VisualInspector.Handle, theme, null);

        foreach (Control card in VisualInspector.Controls)
        {
            var step = card as StepCardControl;
            if (step != null)
                step.RefreshTheme();
        }
    }

    private void PopulatePalette()
    {
        var categories = new System.Collections.Generic.Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in AHK2AST.Plugins.PluginRegistry.RegisteredPluginTypes)
        {
            try
            {
                var instance = Activator.CreateInstance(type) as AHK2AST.Plugins.IFlowPlugin;
                if (instance == null) continue;

                string catName = instance.Category;
                if (string.IsNullOrEmpty(catName)) catName = "Other";

                TreeNode catNode;
                if (!categories.TryGetValue(catName, out catNode))
                {
                    catNode = PaletteTree.Nodes.Add(catName);
                    categories[catName] = catNode;
                }

                var itemNode = catNode.Nodes.Add(instance.StepTitle);
                itemNode.Tag = new PluginDragData
                {
                    Title = instance.StepTitle,
                    Icon = instance.Icon,
                    ConfigType = instance.ConfigType
                };
            }
            catch { }
        }

        PaletteTree.ExpandAll();
    }

    private void PaletteTree_ItemDrag(object sender, ItemDragEventArgs e)
    {
        var node = e.Item as TreeNode;
        if (node != null && node.Tag is PluginDragData)
        {
            DoDragDrop(node.Tag, DragDropEffects.Copy);
        }
    }

    private void VisualInspector_ExternalItemDropped(object sender, ItemDroppedEventArgs e)
    {
        object configObj = null;
        if (e.Data.ConfigType != null)
            configObj = Activator.CreateInstance(e.Data.ConfigType);

        AddStepCard(e.Data.Title, e.Data.Icon, configObj, e.Index);
    }

    private void AddStepCard(string title, string icon, object configObj, int index)
    {
        var card = new StepCardControl(title, icon, configObj);

        // When a card is clicked, show its config in the PropertyGrid and highlight it
        card.Click += (s, e) =>
        {
            PluginProperties.SelectedObject = card.ConfigObject;
            foreach (Control c in VisualInspector.Controls)
            {
                var step = c as StepCardControl;
                if (step != null)
                {
                    step.IsSelected = (step == card);
                }
            }
        };

        VisualInspector.Controls.Add(card);

        if (index >= 0 && index < VisualInspector.Controls.Count)
        {
            VisualInspector.Controls.SetChildIndex(card, index);
        }
        UpdateStepIndices();
    }

    private void UpdateStepIndices()
    {
        int index = 1;
        bool hasDiagram = false;
        for (int i = 0; i < VisualInspector.Controls.Count; i++)
        {
            var card = VisualInspector.Controls[i] as StepCardControl;
            if (card != null)
            {
                card.SetStepIndex(index++);
                if (card.Title == "Logic Flow Diagram")
                {
                    hasDiagram = true;
                }
            }
        }
        if (_btnRun != null)
        {
            _btnRun.Enabled = !hasDiagram;
        }
    }

    public event Action<string, bool> ExecuteFlowRequested;
    public event Func<string> GetSourceCodeRequested;
    public event Func<string> GetActiveFilePathRequested;

    private string SerializeCurrentFlow()
    {
        var def = new AHK2AST.Plugins.FlowDefinition();
        def.Meta = _pipelineMeta;

        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();

        for (int i = 0; i < VisualInspector.Controls.Count; i++)
        {
            var card = VisualInspector.Controls[i] as StepCardControl;
            if (card != null)
            {
                var stepDef = new AHK2AST.Plugins.FlowStepDefinition
                {
                    Title = card.Title,
                    Icon = card.Icon,
                    ConfigType = card.ConfigObject != null ? card.ConfigObject.GetType().FullName : "",
                    ConfigJson = card.ConfigObject != null ? serializer.Serialize(card.ConfigObject) : ""
                };
                def.Steps.Add(stepDef);
            }
        }
        return serializer.Serialize(def);
    }

    private bool _isDirty = false;
    public bool IsDirty
    {
        get { return _isDirty; }
        set
        {
            _isDirty = value;
            UpdateTabText();
        }
    }

    private void UpdateTabText()
    {
        string name = (_pipelineMeta == null || string.IsNullOrEmpty(_pipelineMeta.Name)) ? "New Flow" : _pipelineMeta.Name;
        Text = name + (IsDirty ? " *" : "");
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    public string CurrentFilePath { get; set; }

    private void BtnSave_Click(object sender, EventArgs e)
    {
        try
        {
            string json = SerializeCurrentFlow();

            string path = CurrentFilePath;
            if (string.IsNullOrEmpty(path))
            {
                string flowsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows");
                if (!System.IO.Directory.Exists(flowsDir))
                    System.IO.Directory.CreateDirectory(flowsDir);

                string safeName = string.Join("_", _pipelineMeta.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
                if (string.IsNullOrEmpty(safeName)) safeName = "Flow";

                path = System.IO.Path.Combine(flowsDir, safeName + ".json");
                CurrentFilePath = path;
            }

            System.IO.File.WriteAllText(path, json);
            IsDirty = false; // clears dirty and updates tab text
            MessageBox.Show("Flow saved to:\n" + path, "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error saving flow: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void ExecuteFlow(bool autoRun)
    {
        string json = SerializeCurrentFlow();
        if (ExecuteFlowRequested != null)
        {
            ExecuteFlowRequested(json, autoRun);
        }
    }

    private void BtnRun_Click(object sender, EventArgs e)
    {
        ExecuteFlow(true);
    }

    private void BtnEmit_Click(object sender, EventArgs e)
    {
        ExecuteFlow(false);
    }

    public void LoadFlow(string json, string filePath)
    {
        try
        {
            CurrentFilePath = filePath;
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var flowDef = serializer.Deserialize<AHK2AST.Plugins.FlowDefinition>(json);
            if (flowDef != null)
            {
                _pipelineMeta = flowDef.Meta ?? new PipelineMeta();
                PluginProperties.SelectedObject = _pipelineMeta;

                VisualInspector.Controls.Clear();
                if (flowDef.Steps != null)
                {
                    foreach (var step in flowDef.Steps)
                    {
                        object configObj = null;
                        if (!string.IsNullOrEmpty(step.ConfigType) && !string.IsNullOrEmpty(step.ConfigJson))
                        {
                            Type t = Type.GetType(step.ConfigType);
                            if (t == null) t = typeof(AhkAstEngine).Assembly.GetType(step.ConfigType);
                            if (t == null) t = typeof(AhkAstEngine).Assembly.GetType("AHK2AST.Plugins." + step.ConfigType);
                            if (t == null) t = typeof(AhkAstEngine).Assembly.GetType("AHK2AST." + step.ConfigType);

                            if (t != null)
                            {
                                configObj = serializer.Deserialize(step.ConfigJson, t);
                            }
                        }
                        AddStepCard(step.Title, step.Icon, configObj, -1);
                    }
                }
            }
            IsDirty = false; // Will also update tab text
        }
        catch { }
    }

    private void BtnExportAhk_Click(object sender, EventArgs e)
    {
        try
        {
            string defaultPath = "";
            if (GetActiveFilePathRequested != null)
            {
                defaultPath = GetActiveFilePathRequested();
            }
            var exportDlg = new ExportAhkForm(defaultPath);
            if (exportDlg.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string style = exportDlg.SelectedStyle;
            string sourceType = exportDlg.SourceType;
            bool followIncludes = exportDlg.FollowIncludes;
            bool inlineIncludes = exportDlg.InlineIncludes;
            string sourceFilePath = exportDlg.SourceFilePath;
            bool decideAtRuntime = exportDlg.DecideAtRuntime;

            string defaultName = _pipelineMeta != null && !string.IsNullOrEmpty(_pipelineMeta.Name)
                ? _pipelineMeta.Name
                : "MyFlow";
            string safeName = string.Join("_", defaultName.Split(System.IO.Path.GetInvalidFileNameChars()));

            var sfd = new SaveFileDialog
            {
                Filter = "AutoHotkey Script (*.ahk)|*.ahk",
                Title = "Export Flow as AutoHotkey Script",
                FileName = safeName + ".ahk"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            string outPath = sfd.FileName;
            string json = SerializeCurrentFlow();

            string scriptContent = GenerateAhkScript(json, style, sourceType, defaultName, followIncludes, inlineIncludes, sourceFilePath, decideAtRuntime);
            System.IO.File.WriteAllText(outPath, scriptContent, Encoding.UTF8);

            var confirmResult = MessageBox.Show(
                "Standalone AutoHotkey v2 script successfully exported to:\n" + outPath + "\n\nDo you want to run it now?",
                "Export Successful",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2); // Default to No

            if (confirmResult == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error exporting AHK script: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string GenerateAhkScript(string json, string style, string sourceType, string flowName, bool followIncludes, bool inlineIncludes, string sourceFilePath, bool decideAtRuntime)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#Requires AutoHotkey v2.0");
        sb.AppendLine("#SingleInstance Force");
        if (style == "ahksharp")
        {
            sb.AppendLine("#include <AHK#>");
        }
        sb.AppendLine();
        sb.AppendLine("; " + new string('=', 77));
        sb.AppendLine("; Standalone AutoHotkey v2 Pipeline Execution Script");
        sb.AppendLine("; Auto-generated by AHK# AST Workbench");
        sb.AppendLine("; Flow: " + flowName);
        sb.AppendLine("; " + new string('=', 77));
        sb.AppendLine();

        // 1. Setup Source Code loading based on sourceType
        if (sourceType == "file")
        {
            string defaultSource = !string.IsNullOrEmpty(sourceFilePath)
                ? sourceFilePath.Replace("\"", "\"\"")
                : "source.ahk";
            sb.AppendLine("; --- Input Source Code ---");
            sb.AppendLine("; Load source code dynamically.");
            if (decideAtRuntime)
            {
                sb.AppendLine("SourceFile := FileSelect(3, A_ScriptDir, \"Select AutoHotkey Script File to Parse\", \"AutoHotkey Files (*.ahk; *.txt)\")");
                sb.AppendLine("if (SourceFile = \"\") {");
                sb.AppendLine("    ExitApp()");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("SourceFile := \"" + defaultSource + "\"");
            }
            sb.AppendLine("SourceCode := FileExist(SourceFile) ? FileRead(SourceFile) : \"MsgBox('Hello World!')\"");
        }
        else if (sourceType == "string")
        {
            string defaultSource = !string.IsNullOrEmpty(sourceFilePath)
                ? sourceFilePath.Replace("\"", "\"\"")
                : "source.ahk";
            sb.AppendLine("; --- Input Source Code ---");
            sb.AppendLine("; Load initial code from file into a runtime multiline textbox for editing.");
            if (decideAtRuntime)
            {
                sb.AppendLine("SourceFile := FileSelect(3, A_ScriptDir, \"Select AutoHotkey Script File to Parse\", \"AutoHotkey Files (*.ahk; *.txt)\")");
                sb.AppendLine("if (SourceFile = \"\") {");
                sb.AppendLine("    ExitApp()");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("SourceFile := \"" + defaultSource + "\"");
            }
            sb.AppendLine("initialCode := FileExist(SourceFile) ? FileRead(SourceFile) : \"MsgBox('Hello World!')\"");
            sb.AppendLine("SourceCode := \"\"");
            sb.AppendLine("if !AhkAstSourceEditor.Show(initialCode, &SourceCode) {");
            sb.AppendLine("    ExitApp()");
            sb.AppendLine("}");
        }
        else // argument
        {
            sb.AppendLine("; --- Input Source Code ---");
            sb.AppendLine("; Expected path or code passed from command line args.");
            sb.AppendLine("if (A_Args.Length < 1) {");
            sb.AppendLine("    MsgBox(\"Usage: AutoHotkey.exe \" A_ScriptName \" <SourcePathOrCode>\", \"Error\", \"Iconx\")");
            sb.AppendLine("    ExitApp(1)");
            sb.AppendLine("}");
            sb.AppendLine("SourceArg := A_Args[1]");
            sb.AppendLine("SourceFile := FileExist(SourceArg) ? SourceArg : \"\"");
            sb.AppendLine("SourceCode := (SourceFile != \"\") ? FileRead(SourceFile) : SourceArg");
        }
        sb.AppendLine();

        // 2. DLL Loading and engine initialization
        sb.AppendLine("try {");
        sb.AppendLine("    ; --- 1. Load AST Engine ---");

        if (style == "com")
        {
            sb.AppendLine("    try {");
            sb.AppendLine("        engine := ComObject(\"Ahk2Ast.AstEngine\")");
            sb.AppendLine("    } catch Error {");
            sb.AppendLine("        ; If ComObject creation fails (e.g. not registered), try to locate and register AstEngine.dll automatically");
            sb.AppendLine("        EngineDll := A_ScriptDir \"\\AstEngine.dll\"");
            sb.AppendLine("        if !FileExist(EngineDll)");
            sb.AppendLine("            EngineDll := A_ScriptDir \"\\build\\AstEngine.dll\"");
            sb.AppendLine("        if !FileExist(EngineDll)");
            sb.AppendLine("            EngineDll := \"AstEngine.dll\"");
            sb.AppendLine();
            sb.AppendLine("        if FileExist(EngineDll) {");
            sb.AppendLine("            RegisterAstEngine(EngineDll)");
            sb.AppendLine("            engine := ComObject(\"Ahk2Ast.AstEngine\")");
            sb.AppendLine("        } else {");
            sb.AppendLine("            throw Error(\"COM object 'Ahk2Ast.AstEngine' is not registered, and 'AstEngine.dll' could not be found to auto-register.\")");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        else
        {
            sb.AppendLine("    EngineDll := A_ScriptDir \"\\AstEngine.dll\"");
            sb.AppendLine("    if !FileExist(EngineDll)");
            sb.AppendLine("        EngineDll := \"AstEngine.dll\"");
            sb.AppendLine();

            if (style == "raw")
            {
                sb.AppendLine("    engine := CLR.LoadLibrary(EngineDll).CreateInstance(\"AhkAstEngine\")");
            }
            else // ahksharp
            {
                sb.AppendLine("    engine := AHKSharp.CreateObject(\"AhkAstEngine\", EngineDll)");
            }
        }
        sb.AppendLine();

        // 3. Properties and values
        sb.AppendLine("    ; --- 2. Set Custom Properties / Arguments ---");
        sb.AppendLine("    engine.SetProperty(\"WorkspaceDir\", A_ScriptDir)");
        sb.AppendLine("    engine.SetProperty(\"CurrentDir\", A_ScriptDir)");
        if (sourceType == "file" || sourceType == "argument")
        {
            sb.AppendLine("    if (SourceFile != \"\") {");
            sb.AppendLine("        engine.SetProperty(\"InputFile\", SourceFile)");
            sb.AppendLine("    }");
        }

        // Add custom properties from meta
        if (_pipelineMeta != null && !string.IsNullOrEmpty(_pipelineMeta.CustomProperties))
        {
            var lines = _pipelineMeta.CustomProperties.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var idx = line.IndexOf('=');
                if (idx > 0)
                {
                    string key = line.Substring(0, idx).Trim();
                    string val = line.Substring(idx + 1).Trim();
                    sb.AppendLine(string.Format("    engine.SetProperty(\"{0}\", \"{1}\")", key.Replace("\"", "\"\""), val.Replace("\"", "\"\"")));
                }
            }
        }
        sb.AppendLine();

        // 4. Flow config JSON (formatted as clean AHK object literal)
        sb.AppendLine("    ; --- 3. Flow Definition Object ---");
        sb.AppendLine("    FlowConfig := " + SerializeFlowToAhkObject(json, "    "));
        sb.AppendLine();

        // 5. Execution
        sb.AppendLine("    ; --- 4. Execute Flow Pipeline ---");
        sb.AppendLine(string.Format("    ResultCode := engine.ExecuteFlow(SourceCode, FlowConfig, {0}, SourceFile, {1})",
            (!inlineIncludes).ToString().ToLower(),
            followIncludes.ToString().ToLower()));
        sb.AppendLine();

        // 6. Outputs / printing
        sb.AppendLine("    ; --- 5. Done ---");
        sb.AppendLine("    logs := engine.GetLogsString()");
        sb.AppendLine("    AhkAstResultViewer.Show(logs, ResultCode)");
        sb.AppendLine();
        sb.AppendLine("} catch Error as err {");
        sb.AppendLine("    AhkAstResultViewer.ShowError(err)");
        sb.AppendLine("}");

        // If raw style, append self-contained CLR class
        if (style == "raw")
        {
            sb.AppendLine();
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("; Self-contained CLR Assembly Loader for AutoHotkey v2");
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("class CLR {");
            sb.AppendLine("    static LoadLibrary(dllPath) {");
            sb.AppendLine("        if !(hModule := DllCall(\"GetModuleHandle\", \"str\", \"mscoree\", \"ptr\"))");
            sb.AppendLine("            hModule := DllCall(\"LoadLibrary\", \"str\", \"mscoree\", \"ptr\")");
            sb.AppendLine("        if !hModule");
            sb.AppendLine("            throw Error(\"Failed to load mscoree.dll\")");
            sb.AppendLine("        ");
            sb.AppendLine("        CLSID_CorRuntimeHost := CLR.GUID(\"{CB2F6723-AB3A-11D2-9C40-00C04FA30A3E}\")");
            sb.AppendLine("        IID_ICorRuntimeHost := CLR.GUID(\"{CB2F6722-AB3A-11D2-9C40-00C04FA30A3E}\")");
            sb.AppendLine("        ");
            sb.AppendLine("        hr := DllCall(\"mscoree\\CorBindToRuntimeEx\"");
            sb.AppendLine("            , \"wstr\", \"v4.0.30319\"");
            sb.AppendLine("            , \"ptr\", 0");
            sb.AppendLine("            , \"uint\", 0");
            sb.AppendLine("            , \"ptr\", CLSID_CorRuntimeHost, \"ptr\", IID_ICorRuntimeHost");
            sb.AppendLine("            , \"ptr*\", &pHost := 0, \"int\")");
            sb.AppendLine("        if (hr < 0)");
            sb.AppendLine("            throw Error(\"CorBindToRuntimeEx failed: \" hr)");
            sb.AppendLine("        ");
            sb.AppendLine("        hr := ComCall(10, pHost, \"int\")");
            sb.AppendLine("        if (hr < 0 && hr != -2147024891)");
            sb.AppendLine("            throw Error(\"Runtime Start failed: \" hr)");
            sb.AppendLine("            ");
            sb.AppendLine("        hr := ComCall(13, pHost, \"ptr*\", &pDomain := 0, \"int\")");
            sb.AppendLine("        if (hr < 0)");
            sb.AppendLine("            throw Error(\"GetDefaultDomain failed: \" hr)");
            sb.AppendLine("            ");
            sb.AppendLine("        ObjRelease(pHost)");
            sb.AppendLine("        return CLR.AppDomain(pDomain, dllPath)");
            sb.AppendLine("    }");
            sb.AppendLine("    ");
            sb.AppendLine("    static GUID(str) {");
            sb.AppendLine("        buf := Buffer(16)");
            sb.AppendLine("        DllCall(\"ole32\\CLSIDFromString\", \"wstr\", str, \"ptr\", buf)");
            sb.AppendLine("        return buf");
            sb.AppendLine("    }");
            sb.AppendLine("    ");
            sb.AppendLine("    class AppDomain {");
            sb.AppendLine("        __New(pAppDomain, dllPath) {");
            sb.AppendLine("            this.pAppDomain := pAppDomain");
            sb.AppendLine("            ");
            sb.AppendLine("            Loop Files, dllPath");
            sb.AppendLine("                this.dllPath := A_LoopFileFullPath");
            sb.AppendLine("            ");
            sb.AppendLine("            if (!this.dllPath || !FileExist(this.dllPath))");
            sb.AppendLine("                throw Error(\"DLL not found: \" dllPath)");
            sb.AppendLine("            ");
            sb.AppendLine("            ; Pre-load all other DLLs in the same directory to resolve references");
            sb.AppendLine("            dllDir := RegExReplace(this.dllPath, \"\\\\[^\\\\]+$\", \"\")");
            sb.AppendLine("            static nullVal := ComValue(13, 0)");
            sb.AppendLine("            appDomainObj := ComValue(9, pAppDomain)");
            sb.AppendLine("            typeofAssembly := appDomainObj.GetType().Assembly.GetType()");
            sb.AppendLine("            Loop Files, dllDir \"\\*.dll\" {");
            sb.AppendLine("                if (A_LoopFileFullPath != this.dllPath) {");
            sb.AppendLine("                    try {");
            sb.AppendLine("                        args := ComObjArray(0xC, 1)");
            sb.AppendLine("                        args[0] := A_LoopFileFullPath");
            sb.AppendLine("                        typeofAssembly.InvokeMember_3(\"LoadFrom\", 0x158, nullVal, nullVal, args)");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        CreateInstance(typeName) {");
            sb.AppendLine("            appDomainObj := ComValue(9, this.pAppDomain)");
            sb.AppendLine("            pObjectHandle := appDomainObj.CreateInstanceFrom(this.dllPath, typeName)");
            sb.AppendLine("            return pObjectHandle.Unwrap()");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        // Append self-contained Result Viewer GUI class
        sb.AppendLine();
        sb.AppendLine("; " + new string('=', 77));
        sb.AppendLine("; Self-contained Result Viewer GUI for AutoHotkey v2");
        sb.AppendLine("; " + new string('=', 77));
        sb.AppendLine("class AhkAstResultViewer {");
        sb.AppendLine("    static Show(logs, resultCode) {");
        sb.AppendLine("        myGui := Gui(\"-MinimizeBox -MaximizeBox +AlwaysOnTop\", \"AHK# AST Flow Execution Result\")");
        sb.AppendLine("        myGui.BackColor := \"1e1e2e\"");
        sb.AppendLine("        ");
        sb.AppendLine("        if (VerCompare(A_OSVersion, \"10.0.17763\") >= 0) {");
        sb.AppendLine("            attr := (VerCompare(A_OSVersion, \"10.0.18985\") >= 0) ? 20 : 19");
        sb.AppendLine("            DllCall(\"dwmapi\\DwmSetWindowAttribute\", \"ptr\", myGui.Hwnd, \"uint\", attr, \"int*\", true, \"uint\", 4)");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.SetFont(\"s13 bold cA6E3A1\", \"Segoe UI\")");
        sb.AppendLine("        myGui.Add(\"Text\", \"x20 y15 w560\", \"🎉 Pipeline Execution Succeeded!\")");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.SetFont(\"s9 cCDD6F4\", \"Segoe UI\")");
        sb.AppendLine("        tab := myGui.Add(\"Tab3\", \"x20 y50 w560 h380\", [\"Execution Log\", \"Output Preview\"])");
        sb.AppendLine("        ");
        sb.AppendLine("        ; Tab 1: Execution Log");
        sb.AppendLine("        tab.UseTab(1)");
        sb.AppendLine("        myGui.SetFont(\"s9.5\", \"Consolas\")");
        sb.AppendLine("        logEdit := myGui.Add(\"Edit\", \"x30 y85 w540 h330 Multi ReadOnly Background181825 cCDD6F4\")");
        sb.AppendLine("        logEdit.Value := logs");
        sb.AppendLine("        ");
        sb.AppendLine("        ; Tab 2: Output Preview");
        sb.AppendLine("        tab.UseTab(2)");
        sb.AppendLine("        myGui.SetFont(\"s9.5\", \"Consolas\")");
        sb.AppendLine("        codeEdit := myGui.Add(\"Edit\", \"x30 y85 w540 h330 Multi ReadOnly Background181825 cCDD6F4\")");
        sb.AppendLine("        codeEdit.Value := resultCode");
        sb.AppendLine("        ");
        sb.AppendLine("        tab.UseTab() ; Reset to main window");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.SetFont(\"s9 bold\", \"Segoe UI\")");
        sb.AppendLine("        btnCopy := myGui.Add(\"Button\", \"x20 y445 w120 h32\", \"📋 Copy Output\")");
        sb.AppendLine("        btnCopy.OnEvent(\"Click\", (*) => (A_Clipboard := resultCode, ToolTip(\"Copied to clipboard!\"), SetTimer(() => ToolTip(), -1500)))");
        sb.AppendLine("        ");
        sb.AppendLine("        btnClose := myGui.Add(\"Button\", \"x460 y445 w120 h32 +Default\", \"Close\")");
        sb.AppendLine("        btnClose.OnEvent(\"Click\", (*) => myGui.Destroy())");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.Show(\"w600 h495\")");
        sb.AppendLine("        WinWaitClose(myGui.Hwnd)");
        sb.AppendLine("    }");
        sb.AppendLine("    ");
        sb.AppendLine("    static ShowError(err) {");
        sb.AppendLine("        myGui := Gui(\"-MinimizeBox -MaximizeBox +AlwaysOnTop\", \"AHK# AST Flow Execution Error\")");
        sb.AppendLine("        myGui.BackColor := \"1e1e2e\"");
        sb.AppendLine("        ");
        sb.AppendLine("        if (VerCompare(A_OSVersion, \"10.0.17763\") >= 0) {");
        sb.AppendLine("            attr := (VerCompare(A_OSVersion, \"10.0.18985\") >= 0) ? 20 : 19");
        sb.AppendLine("            DllCall(\"dwmapi\\DwmSetWindowAttribute\", \"ptr\", myGui.Hwnd, \"uint\", attr, \"int*\", true, \"uint\", 4)");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.SetFont(\"s13 bold cF38BA8\", \"Segoe UI\")");
        sb.AppendLine("        myGui.Add(\"Text\", \"x20 y15 w560\", \"❌ Pipeline Execution Failed\")");
        sb.AppendLine("        ");
        sb.AppendLine("        errDetail := \"Error Type: \" Type(err) \"`nMessage: \" err.Message");
        sb.AppendLine("        if err.HasProp(\"What\") && err.What");
        sb.AppendLine("            errDetail .= \"`nWhat: \" err.What");
        sb.AppendLine("        if err.HasProp(\"File\") && err.File");
        sb.AppendLine("            errDetail .= \"`nFile: \" err.File");
        sb.AppendLine("        if err.HasProp(\"Line\") && err.Line");
        sb.AppendLine("            errDetail .= \"`nLine: \" err.Line");
        sb.AppendLine("        if err.HasProp(\"Extra\") && err.Extra");
        sb.AppendLine("            errDetail .= \"`nExtra: \" String(err.Extra)");
        sb.AppendLine("        if err.HasProp(\"Number\")");
        sb.AppendLine("            errDetail .= \"`nCOM HRESULT: \" Format(\"0x{:08X}\", err.Number)");
        sb.AppendLine("        if err.HasProp(\"Stack\") && err.Stack");
        sb.AppendLine("            errDetail .= \"`n`nStack Trace:`n\" err.Stack");
        sb.AppendLine("            ");
        sb.AppendLine("        myGui.SetFont(\"s9.5\", \"Consolas\")");
        sb.AppendLine("        errEdit := myGui.Add(\"Edit\", \"x20 y50 w560 h380 Multi ReadOnly Background181825 cFAB387\")");
        sb.AppendLine("        errEdit.Value := errDetail");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.SetFont(\"s9 bold\", \"Segoe UI\")");
        sb.AppendLine("        btnClose := myGui.Add(\"Button\", \"x460 y445 w120 h32 +Default\", \"Close\")");
        sb.AppendLine("        btnClose.OnEvent(\"Click\", (*) => myGui.Destroy())");
        sb.AppendLine("        ");
        sb.AppendLine("        myGui.Show(\"w600 h495\")");
        sb.AppendLine("        WinWaitClose(myGui.Hwnd)");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        if (sourceType == "string")
        {
            sb.AppendLine();
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("; Self-contained Source Code Editor GUI for AutoHotkey v2");
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("class AhkAstSourceEditor {");
            sb.AppendLine("    static Show(initialCode, &outCode) {");
            sb.AppendLine("        myGui := Gui(\"-MinimizeBox -MaximizeBox +AlwaysOnTop\", \"AHK# AST Input Source Editor\")");
            sb.AppendLine("        myGui.BackColor := \"1e1e2e\"");
            sb.AppendLine("        ");
            sb.AppendLine("        if (VerCompare(A_OSVersion, \"10.0.17763\") >= 0) {");
            sb.AppendLine("            attr := (VerCompare(A_OSVersion, \"10.0.18985\") >= 0) ? 20 : 19");
            sb.AppendLine("            DllCall(\"dwmapi\\DwmSetWindowAttribute\", \"ptr\", myGui.Hwnd, \"uint\", attr, \"int*\", true, \"uint\", 4)");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        myGui.SetFont(\"s13 bold cA6E3A1\", \"Segoe UI\")");
            sb.AppendLine("        myGui.Add(\"Text\", \"x20 y15 w560\", \"📝 Edit AutoHotkey v2 Source Code\")");
            sb.AppendLine("        ");
            sb.AppendLine("        myGui.SetFont(\"s9.5 cCDD6F4\", \"Consolas\")");
            sb.AppendLine("        codeEdit := myGui.Add(\"Edit\", \"x20 y50 w560 h380 Multi Background181825 cCDD6F4\")");
            sb.AppendLine("        codeEdit.Value := initialCode");
            sb.AppendLine("        ");
            sb.AppendLine("        myGui.SetFont(\"s9 bold\", \"Segoe UI\")");
            sb.AppendLine("        btnRun := myGui.Add(\"Button\", \"x20 y445 w120 h32 +Default\", \"▶ Run Pipeline\")");
            sb.AppendLine("        btnCancel := myGui.Add(\"Button\", \"x460 y445 w120 h32\", \"Cancel\")");
            sb.AppendLine("        ");
            sb.AppendLine("        submitted := false");
            sb.AppendLine("        ");
            sb.AppendLine("        btnRun.OnEvent(\"Click\", OnRun)");
            sb.AppendLine("        btnCancel.OnEvent(\"Click\", OnCancel)");
            sb.AppendLine("        ");
            sb.AppendLine("        OnRun(*) {");
            sb.AppendLine("            nonlocal submitted");
            sb.AppendLine("            submitted := true");
            sb.AppendLine("            outCode := codeEdit.Value");
            sb.AppendLine("            myGui.Destroy()");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        OnCancel(*) {");
            sb.AppendLine("            myGui.Destroy()");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        myGui.Show(\"w600 h495\")");
            sb.AppendLine("        WinWaitClose(myGui.Hwnd)");
            sb.AppendLine("        return submitted");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        if (style == "com")
        {
            sb.AppendLine();
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("; COM Auto-Registration Helper for AutoHotkey v2");
            sb.AppendLine("; " + new string('=', 77));
            sb.AppendLine("RegisterAstEngine(dllPath) {");
            sb.AppendLine("    if !FileExist(dllPath) {");
            sb.AppendLine("        throw Error(\"AstEngine.dll not found at: \" dllPath)");
            sb.AppendLine("    }");
            sb.AppendLine("    ");
            sb.AppendLine("    ; Resolve full path and convert to file:/// URI");
            sb.AppendLine("    Loop Files, dllPath");
            sb.AppendLine("        fullPath := A_LoopFileFullPath");
            sb.AppendLine("    ");
            sb.AppendLine("    if (SubStr(fullPath, 2, 1) = \":\") {");
            sb.AppendLine("        fullPath := \"/\" StrReplace(fullPath, \"\\\", \"/\")");
            sb.AppendLine("    } else {");
            sb.AppendLine("        fullPath := StrReplace(fullPath, \"\\\", \"/\")");
            sb.AppendLine("    }");
            sb.AppendLine("    codeBaseUrl := \"file:///\" LTrim(fullPath, \"/\")");
            sb.AppendLine("    ");
            sb.AppendLine("    classesKey := \"HKCU\\Software\\Classes\"");
            sb.AppendLine("    assemblyStr := \"AstEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null\"");
            sb.AppendLine("    ");
            sb.AppendLine("    ; 1. Register Ahk2Ast.AstEngine");
            sb.AppendLine("    clsidAst := \"{B8C4D2E3-F5A6-7890-BCDE-F01234567890}\"");
            sb.AppendLine("    RegWrite(\"AhkAstEngine\", \"REG_SZ\", classesKey \"\\Ahk2Ast.AstEngine\")");
            sb.AppendLine("    RegWrite(clsidAst, \"REG_SZ\", classesKey \"\\Ahk2Ast.AstEngine\\CLSID\")");
            sb.AppendLine("    ");
            sb.AppendLine("    clsidAstKey := classesKey \"\\CLSID\\\" clsidAst");
            sb.AppendLine("    RegWrite(\"AhkAstEngine\", \"REG_SZ\", clsidAstKey)");
            sb.AppendLine("    RegWrite(\"Ahk2Ast.AstEngine\", \"REG_SZ\", clsidAstKey \"\\ProgId\")");
            sb.AppendLine("    ");
            sb.AppendLine("    inprocAst := clsidAstKey \"\\InprocServer32\"");
            sb.AppendLine("    RegWrite(\"mscoree.dll\", \"REG_SZ\", inprocAst)");
            sb.AppendLine("    RegWrite(\"Both\", \"REG_SZ\", inprocAst, \"ThreadingModel\")");
            sb.AppendLine("    RegWrite(\"AhkAstEngine\", \"REG_SZ\", inprocAst, \"Class\")");
            sb.AppendLine("    RegWrite(assemblyStr, \"REG_SZ\", inprocAst, \"Assembly\")");
            sb.AppendLine("    RegWrite(\"v4.0.30319\", \"REG_SZ\", inprocAst, \"RuntimeVersion\")");
            sb.AppendLine("    RegWrite(codeBaseUrl, \"REG_SZ\", inprocAst, \"CodeBase\")");
            sb.AppendLine("    ");
            sb.AppendLine("    inprocAstVer := inprocAst \"\\0.0.0.0\"");
            sb.AppendLine("    RegWrite(\"AhkAstEngine\", \"REG_SZ\", inprocAstVer, \"Class\")");
            sb.AppendLine("    RegWrite(assemblyStr, \"REG_SZ\", inprocAstVer, \"Assembly\")");
            sb.AppendLine("    RegWrite(\"v4.0.30319\", \"REG_SZ\", inprocAstVer, \"RuntimeVersion\")");
            sb.AppendLine("    RegWrite(codeBaseUrl, \"REG_SZ\", inprocAstVer, \"CodeBase\")");
            sb.AppendLine("    ");
            sb.AppendLine("    RegWrite(\"\", \"REG_SZ\", clsidAstKey \"\\Implemented Categories\\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}\")");
            sb.AppendLine("    ");
            sb.AppendLine("    ; 2. Register Ahk2Ast.JitEngine");
            sb.AppendLine("    clsidJit := \"{C9D5E3F4-A6B7-8901-CDEF-012345678901}\"");
            sb.AppendLine("    RegWrite(\"AhkJitEngine\", \"REG_SZ\", classesKey \"\\Ahk2Ast.JitEngine\")");
            sb.AppendLine("    RegWrite(clsidJit, \"REG_SZ\", classesKey \"\\Ahk2Ast.JitEngine\\CLSID\")");
            sb.AppendLine("    ");
            sb.AppendLine("    clsidJitKey := classesKey \"\\CLSID\\\" clsidJit");
            sb.AppendLine("    RegWrite(\"AhkJitEngine\", \"REG_SZ\", clsidJitKey)");
            sb.AppendLine("    RegWrite(\"Ahk2Ast.JitEngine\", \"REG_SZ\", clsidJitKey \"\\ProgId\")");
            sb.AppendLine("    ");
            sb.AppendLine("    inprocJit := clsidJitKey \"\\InprocServer32\"");
            sb.AppendLine("    RegWrite(\"mscoree.dll\", \"REG_SZ\", inprocJit)");
            sb.AppendLine("    RegWrite(\"Both\", \"REG_SZ\", inprocJit, \"ThreadingModel\")");
            sb.AppendLine("    RegWrite(\"AhkJitEngine\", \"REG_SZ\", inprocJit, \"Class\")");
            sb.AppendLine("    RegWrite(assemblyStr, \"REG_SZ\", inprocJit, \"Assembly\")");
            sb.AppendLine("    RegWrite(\"v4.0.30319\", \"REG_SZ\", inprocJit, \"RuntimeVersion\")");
            sb.AppendLine("    RegWrite(codeBaseUrl, \"REG_SZ\", inprocJit, \"CodeBase\")");
            sb.AppendLine("    ");
            sb.AppendLine("    inprocJitVer := inprocJit \"\\0.0.0.0\"");
            sb.AppendLine("    RegWrite(\"AhkJitEngine\", \"REG_SZ\", inprocJitVer, \"Class\")");
            sb.AppendLine("    RegWrite(assemblyStr, \"REG_SZ\", inprocJitVer, \"Assembly\")");
            sb.AppendLine("    RegWrite(\"v4.0.30319\", \"REG_SZ\", inprocJitVer, \"RuntimeVersion\")");
            sb.AppendLine("    RegWrite(codeBaseUrl, \"REG_SZ\", inprocJitVer, \"CodeBase\")");
            sb.AppendLine("    ");
            sb.AppendLine("    RegWrite(\"\", \"REG_SZ\", clsidJitKey \"\\Implemented Categories\\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}\")");
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private string SerializeFlowToAhkObject(string json, string indent = "    ")
    {
        try
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var def = serializer.Deserialize<AHK2AST.Plugins.FlowDefinition>(json);
            if (def == null) return "{}";

            var sb = new StringBuilder();
            sb.AppendLine("{");

            // Meta
            sb.AppendLine(indent + "Meta: {");
            sb.AppendLine(indent + "    EmitDiagnosticsComments: " + def.Meta.EmitDiagnosticsComments.ToString().ToLower() + ",");
            sb.AppendLine(indent + "    CustomProperties: \"" + (def.Meta.CustomProperties ?? "").Replace("\r", "").Replace("\n", "`n").Replace("\"", "\"\"") + "\"");
            sb.AppendLine(indent + "},");

            // Steps
            sb.AppendLine(indent + "Steps: [");
            if (def.Steps != null)
            {
                for (int i = 0; i < def.Steps.Count; i++)
                {
                    var step = def.Steps[i];
                    sb.AppendLine(indent + "    {");
                    sb.AppendLine(indent + "        ConfigType: \"" + (step.ConfigType ?? "").Replace("\"", "\"\"") + "\",");

                    string configObjStr = "{}";
                    if (!string.IsNullOrEmpty(step.ConfigJson))
                    {
                        try
                        {
                            var dict = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(step.ConfigJson);
                            configObjStr = FormatDictionaryAsAhkObject(dict, indent + "        ");
                        }
                        catch { }
                    }
                    sb.AppendLine(indent + "        Config: " + configObjStr);
                    sb.AppendLine(indent + "    }" + (i < def.Steps.Count - 1 ? "," : ""));
                }
            }
            sb.AppendLine(indent + "]");

            sb.Append(indent.Substring(4) + "}");
            return sb.ToString();
        }
        catch
        {
            return "{}";
        }
    }

    private string FormatDictionaryAsAhkObject(System.Collections.Generic.Dictionary<string, object> dict, string indent)
    {
        if (dict == null || dict.Count == 0) return "{}";
        var sb = new StringBuilder();
        sb.AppendLine("{");
        var list = new System.Collections.Generic.List<string>();
        foreach (var kv in dict)
        {
            string valStr = FormatValueAsAhkValue(kv.Value, indent + "    ");
            list.Add(indent + "    " + kv.Key + ": " + valStr);
        }
        sb.AppendLine(string.Join(",\n", list.ToArray()));
        sb.Append(indent + "}");
        return sb.ToString();
    }

    private string FormatValueAsAhkValue(object val, string indent)
    {
        if (val == null) return "0";
        if (val is bool) return ((bool)val) ? "true" : "false";
        if (val is string) return "\"" + ((string)val).Replace("\r", "").Replace("\n", "`n").Replace("\"", "\"\"") + "\"";
        if (val is int || val is long || val is double || val is float || val is decimal) return val.ToString();

        var dict = val as System.Collections.Generic.IDictionary<string, object>;
        if (dict != null)
        {
            var d = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var k in dict.Keys) d[k.ToString()] = dict[k];
            return FormatDictionaryAsAhkObject(d, indent);
        }

        var list = val as System.Collections.IEnumerable;
        if (list != null)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var item in list)
            {
                parts.Add(FormatValueAsAhkValue(item, indent));
            }
            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }

        return "\"" + val.ToString().Replace("\"", "\"\"") + "\"";
    }
}
