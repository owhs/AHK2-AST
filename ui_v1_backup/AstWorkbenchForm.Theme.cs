// AHK# AST Workbench — Comprehensive Parser, Analyzer & Test Runner
// A premium WinForms GUI for parsing, inspecting, debugging, and running AHK2 scripts.
// Compiled into ahk#.bridge.dll alongside AhkAstEngine.cs.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

// ═══════════════════════════════════════════════════════════════════════════════
// COM-Visible Entry Point
// ═══════════════════════════════════════════════════════════════════════════════

// Main Form
// ═══════════════════════════════════════════════════════════════════════════════

internal partial class AstWorkbenchForm : Form
{
    // ── Deep Theme Application ────────────────────────────────────────────

    private void RebuildNodeIcons()
    {
        try
        {
            if (_nodeIcons == null) return;
            _nodeIcons.Images.Clear();

            Color[] colors = {
                WbTheme.Blue,      // 0: Program/Block
                WbTheme.Green,     // 1: Class/Method
                WbTheme.Teal,      // 2: Flow control
                WbTheme.Mauve,     // 3: Expression
                WbTheme.Peach,     // 4: Literal
                WbTheme.Sapphire,  // 5: Identifier
                WbTheme.Red,       // 6: Error
                WbTheme.Yellow,    // 7: Warning
                WbTheme.Sky,       // 8: Directive
                WbTheme.Overlay0   // 9: Other
            };

            foreach (Color c in colors)
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var brush = new SolidBrush(c))
                        g.FillEllipse(brush, 2, 2, 12, 12);
                }
                _nodeIcons.Images.Add(bmp);
            }
        }
        catch { }
    }

    private void UpdateControlTheme(Control parent)
    {
        if (parent == null) return;

        // Custom rules for headers and special panels
        if (parent is DockContent)
        {
            parent.BackColor = WbTheme.Crust;
            parent.ForeColor = WbTheme.Text;
        }
        else if (parent is Panel)
        {
            // Check if it's a header panel (top docked with specific height or name)
            if (parent.Dock == DockStyle.Top && (parent.Height == 32 || parent.Height == 64 || parent.Height == 40))
            {
                parent.BackColor = WbTheme.Mantle;
            }
            else if (parent == _lineNumberPanel)
            {
                parent.BackColor = WbTheme.Mantle;
            }
            else if (parent.Name == "tabStrip")
            {
                parent.BackColor = WbTheme.Mantle;
            }
            else
            {
                parent.BackColor = WbTheme.Base;
            }

            var pnl = (Panel)parent;
            if (pnl.AutoScroll)
            {
                SetWindowTheme(pnl.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
        }
        else if (parent is TabPage)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
        }
        else if (parent is SplitContainer)
        {
            parent.BackColor = WbTheme.Mantle;
            var sc = (SplitContainer)parent;
            sc.Panel1.BackColor = WbTheme.Base;
            sc.Panel2.BackColor = WbTheme.Base;
        }
        else if (parent is Button)
        {
            var btn = (Button)parent;
            if (btn.Parent != null && btn.Parent.Name == "tabStrip")
            {
                // Managed by selected tab state
            }
            else
            {
                btn.BackColor = WbTheme.Surface0;
                btn.ForeColor = WbTheme.Text;
                btn.FlatAppearance.BorderColor = WbTheme.Surface1;
            }
        }
        else if (parent is DataGridView)
        {
            var grid = (DataGridView)parent;
            grid.BackgroundColor = WbTheme.Base;
            grid.ForeColor = WbTheme.Text;
            grid.GridColor = WbTheme.Surface0;
            grid.DefaultCellStyle.BackColor = WbTheme.Base;
            grid.DefaultCellStyle.ForeColor = WbTheme.Text;
            grid.DefaultCellStyle.SelectionBackColor = WbTheme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = WbTheme.Text;
            grid.ColumnHeadersDefaultCellStyle.BackColor = WbTheme.Mantle;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = WbTheme.Subtext0;
        }
        else if (parent is Label)
        {
            // Header title label
            if (parent.Parent is Panel && parent.Parent.Dock == DockStyle.Top && (parent.Parent.Height == 32 || parent.Parent.Height == 64))
            {
                if (parent == _treeStats || parent == _emitTreeStats)
                {
                    parent.ForeColor = WbTheme.Overlay0;
                }
                else
                {
                    parent.ForeColor = WbTheme.Subtext0;
                }
            }
            else
            {
                parent.ForeColor = WbTheme.Text;
            }
        }
        else if (parent is TextBox)
        {
            if (parent == _treeFilter || parent == _emitTreeFilter)
            {
                parent.BackColor = WbTheme.Surface0;
                parent.ForeColor = WbTheme.Text;
            }
        }
        else if (parent is TreeView)
        {
            var tv = (TreeView)parent;
            tv.BackColor = WbTheme.Base;
            tv.ForeColor = WbTheme.Text;
            tv.LineColor = WbTheme.Surface2;
            SetWindowTheme(tv.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        else if (parent is ListView)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
            SetWindowTheme(parent.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        else if (parent is TabControl)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
        }
        else if (parent is TraceWaterfallChart)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
            parent.Invalidate();
        }
        else if (parent is RichTextBox)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
            SetWindowTheme(parent.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        else if (parent is ComboBox)
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
            try { ((ComboBox)parent).FlatStyle = FlatStyle.Flat; } catch {}
            SetWindowTheme(parent.Handle, "", "");
        }
        else if (parent.GetType().Name == "FastColoredTextBox")
        {
            parent.BackColor = WbTheme.Base;
            parent.ForeColor = WbTheme.Text;
            try
            {
                parent.GetType().GetProperty("LineNumberColor").SetValue(parent, WbTheme.Overlay0, null);
                parent.GetType().GetProperty("IndentBackColor").SetValue(parent, WbTheme.Base, null);
            }
            catch {}
            SetWindowTheme(parent.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }

        foreach (Control child in parent.Controls)
        {
            UpdateControlTheme(child);
        }
    }

    private void ApplyDeepTheme()
    {
        this.SuspendLayout();
        if (_dockPanel != null) _dockPanel.SuspendLayout();

        try
        {
            // 1. Rebuild node icons with new theme accents
            RebuildNodeIcons();

            // 2. Safely change docking theme
            if (_dockPanel != null)
            {
                var states = new System.Collections.Generic.Dictionary<IDockContent, DockState>();
                var contents = _dockPanel.Contents.ToList();

                // Undock everything before changing theme (avoids crashing)
                foreach (var content in contents)
                {
                    states[content] = content.DockHandler.DockState;
                    content.DockHandler.DockPanel = null;
                }

                try
                {
                    ThemeBase newTheme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
                    CustomizeDockPalette(newTheme);

                    // DockPanelSuite optimization bypass: if the new theme has the same class type as the old theme,
                    // it ignores the setter. So we temporarily assign the opposite theme class to force a full redraw.
                    if (_dockPanel.Theme != null && _dockPanel.Theme.GetType() == newTheme.GetType())
                    {
                        _dockPanel.Theme = WbTheme.Current.IsDark ? (ThemeBase)new VS2015LightTheme() : new VS2015DarkTheme();
                    }

                    _dockPanel.Theme = newTheme;
                }
                catch { }

                _dockPanel.BackColor = WbTheme.Crust;

                // Redock everything back to its previous state
                foreach (var content in contents)
                {
                    var c = content as DockContent;
                    if (c != null && states[content] != DockState.Hidden && states[content] != DockState.Unknown)
                    {
                        c.Show(_dockPanel, states[content]);
                    }
                    
                    var pb = content as PipelineBuilderContent;
                    if (pb != null)
                    {
                        pb.RefreshTheme();
                    }
                }

                // Apply recursive control theming to all docked/undocked contents
                foreach (var content in contents)
                {
                    var c = content as Control;
                    if (c != null)
                    {
                        UpdateControlTheme(c);
                    }
                }
            }

            // 3. Apply recursive deep control theming to the entire main form
            UpdateControlTheme(this);

            // 4. Menu & Toolbar & Status Bar
            if (_menu != null)
            {
                _menu.BackColor = WbTheme.Mantle;
                _menu.ForeColor = WbTheme.Text;
                _menu.Invalidate();
            }
            if (_toolbar != null)
            {
                _toolbar.BackColor = WbTheme.Mantle;
                _toolbar.ForeColor = WbTheme.Text;
                _toolbar.Invalidate();
            }
            _status.BackColor = WbTheme.Crust;
            _status.ForeColor = WbTheme.Subtext0;
            _status.Items[2].ForeColor = WbTheme.Surface2; // the separator string "|"

            // 5. Select tab sync to apply the new active/inactive state tab colors
            int activeTab = 0;
            if (_logPanel != null && _logPanel.Visible) activeTab = 1;
            else if (_runPanel != null && _runPanel.Visible) activeTab = 2;
            else if (_emitPanel != null && _emitPanel.Visible) activeTab = 3;
            else if (_debugPanel != null && _debugPanel.Visible) activeTab = 4;
            SelectTab(activeTab);
            RefreshTabStripTheme();

            string scrollbarTheme = WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer";

            // 6. Error Grid Styling
            if (_errorGrid != null)
            {
                _errorGrid.BackgroundColor = WbTheme.Mantle;
                _errorGrid.ForeColor = WbTheme.Text;
                _errorGrid.GridColor = WbTheme.Surface0;

                _errorGrid.DefaultCellStyle.BackColor = WbTheme.Mantle;
                _errorGrid.DefaultCellStyle.ForeColor = WbTheme.Text;
                _errorGrid.DefaultCellStyle.SelectionBackColor = WbTheme.Selection;
                _errorGrid.DefaultCellStyle.SelectionForeColor = WbTheme.Text;

                _errorGrid.ColumnHeadersDefaultCellStyle.BackColor = WbTheme.Mantle;
                _errorGrid.ColumnHeadersDefaultCellStyle.ForeColor = WbTheme.Subtext0;

                SetWindowTheme(_errorGrid.Handle, scrollbarTheme, null);
            }

            // 7. Re-apply scrollbar themes to tree and editors
            if (_sourceEditor != null)
            {
                _sourceEditor.BackColor = WbTheme.Base;
                _sourceEditor.ForeColor = WbTheme.Text;
                SetWindowTheme(_sourceEditor.Handle, scrollbarTheme, null);
            }
            if (_astTree != null)
            {
                _astTree.BackColor = WbTheme.Base;
                _astTree.ForeColor = WbTheme.Text;
                SetWindowTheme(_astTree.Handle, scrollbarTheme, null);
            }
            if (_emitAstTree != null)
            {
                _emitAstTree.BackColor = WbTheme.Base;
                _emitAstTree.ForeColor = WbTheme.Text;
                SetWindowTheme(_emitAstTree.Handle, scrollbarTheme, null);
            }
            if (_emitSourceEditor != null)
            {
                _emitSourceEditor.BackColor = WbTheme.Base;
                _emitSourceEditor.ForeColor = WbTheme.Text;
                SetWindowTheme(_emitSourceEditor.Handle, scrollbarTheme, null);
            }
            if (_traceTree != null)
            {
                _traceTree.BackColor = WbTheme.Base;
                _traceTree.ForeColor = WbTheme.Text;
                SetWindowTheme(_traceTree.Handle, scrollbarTheme, null);
            }
            if (_traceDetails != null)
            {
                _traceDetails.BackColor = WbTheme.Base;
                _traceDetails.ForeColor = WbTheme.Text;
                SetWindowTheme(_traceDetails.Handle, scrollbarTheme, null);
            }

            // Output Panels
            if (_parseLog != null)
            {
                _parseLog.BackColor = WbTheme.Mantle;
                _parseLog.ForeColor = WbTheme.Text;
                SetWindowTheme(_parseLog.Handle, scrollbarTheme, null);
            }
            if (_runOutput != null)
            {
                _runOutput.BackColor = WbTheme.Mantle;
                _runOutput.ForeColor = WbTheme.Text;
                SetWindowTheme(_runOutput.Handle, scrollbarTheme, null);
            }
            if (_emitView != null)
            {
                _emitView.BackColor = WbTheme.Mantle;
                _emitView.ForeColor = WbTheme.Text;
                SetWindowTheme(_emitView.Handle, scrollbarTheme, null);
            }
            if (_debugLog != null)
            {
                _debugLog.BackColor = WbTheme.Mantle;
                _debugLog.ForeColor = WbTheme.Text;
                SetWindowTheme(_debugLog.Handle, scrollbarTheme, null);
            }
        }
        finally
        {
            if (_dockPanel != null) _dockPanel.ResumeLayout(true);
            this.ResumeLayout(true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Core Operations
    // ═══════════════════════════════════════════════════════════════════════

}

// ═══════════════════════════════════════════════════════════════════════════════
// Dark Theme Renderers
// ═══════════════════════════════════════════════════════════════════════════════

internal class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
        {
            using (var brush = new SolidBrush(WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
        }
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Prevent white line rendering by doing nothing
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? WbTheme.Text : WbTheme.Subtext1;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
        {
            using (var brush = new SolidBrush(WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle rect = e.ImageRectangle;
        rect.Inflate(-1, -1);
        using (var bgBrush = new SolidBrush(WbTheme.Surface1))
            e.Graphics.FillRectangle(bgBrush, rect);
        using (var pen = new Pen(WbTheme.Surface2))
            e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

        // Draw sexy checkmark
        using (var pen = new Pen(WbTheme.Text, 2))
        {
            e.Graphics.DrawLine(pen, rect.X + 3, rect.Y + rect.Height / 2, rect.X + rect.Width / 2 - 1, rect.Bottom - 3);
            e.Graphics.DrawLine(pen, rect.X + rect.Width / 2 - 1, rect.Bottom - 3, rect.Right - 3, rect.Y + 3);
        }
    }
}

internal class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return WbTheme.Surface0; } }
    public override Color MenuItemBorder { get { return WbTheme.Surface0; } }
    public override Color MenuItemSelected { get { return WbTheme.Surface1; } }
    public override Color MenuStripGradientBegin { get { return WbTheme.Mantle; } }
    public override Color MenuStripGradientEnd { get { return WbTheme.Mantle; } }
    public override Color MenuItemSelectedGradientBegin { get { return WbTheme.Surface1; } }
    public override Color MenuItemSelectedGradientEnd { get { return WbTheme.Surface1; } }
    public override Color MenuItemPressedGradientBegin { get { return WbTheme.Surface0; } }
    public override Color MenuItemPressedGradientEnd { get { return WbTheme.Surface0; } }
    public override Color ToolStripDropDownBackground { get { return WbTheme.Mantle; } }
    public override Color ImageMarginGradientBegin { get { return WbTheme.Mantle; } }
    public override Color ImageMarginGradientMiddle { get { return WbTheme.Mantle; } }
    public override Color ImageMarginGradientEnd { get { return WbTheme.Mantle; } }
    public override Color SeparatorDark { get { return WbTheme.Surface0; } }
    public override Color SeparatorLight { get { return WbTheme.Surface0; } }
}

internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkMenuColors()) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
        {
            using (var brush = new SolidBrush(WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = WbTheme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using (var brush = new SolidBrush(WbTheme.Mantle))
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using (var pen = new Pen(WbTheme.Surface0))
            e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Bottom - 1, e.AffectedBounds.Right, e.AffectedBounds.Bottom - 1);
    }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e)
    {
        using (var brush = new SolidBrush(WbTheme.Subtext0))
        {
            var r = e.AffectedBounds;
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3 - i; j++)
                {
                    e.Graphics.FillRectangle(brush, r.Right - 6 - j * 4, r.Bottom - 6 - i * 4, 2, 2);
                }
            }
        }
    }
}
