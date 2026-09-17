// Shared look & feel: menu/toolbar renderers, DockPanelSuite palette mapping, recursive control theming and
// font-glyph icons. Everything reads WbTheme at call time, so re-applying after a theme switch is enough.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

internal static class UiTheming
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static void DarkScrollbars(Control c)
    {
        if (c == null) return;
        Action apply = () => { try { SetWindowTheme(c.Handle, WbTheme.Current.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
        if (c.IsHandleCreated) apply();
        else c.HandleCreated += (s, e) => apply();
    }

    public static void TitleBar(Form f)
    {
        if (f == null || !f.IsHandleCreated || Environment.OSVersion.Version.Major < 10) return;
        try
        {
            int dark = WbTheme.Current.IsDark ? 1 : 0;
            DwmSetWindowAttribute(f.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(f.Handle, 19, ref dark, sizeof(int));
            SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }
        catch { }
    }

    /// <summary>Colours a control tree. Controls that manage their own look implement IThemed.</summary>
    public static void Apply(Control root)
    {
        if (root == null) return;
        var themed = root as IThemed;
        if (themed != null) { themed.ApplyTheme(); return; }

        if (root is ToolStrip)
        {
            var ts = (ToolStrip)root;
            ts.Renderer = root is MenuStrip ? (ToolStripRenderer)new DarkMenuRenderer() : new DarkToolStripRenderer();
            ts.BackColor = WbTheme.Mantle;
            ts.ForeColor = WbTheme.Text;
        }
        else if (root is TextBoxBase)
        {
            root.BackColor = root is RichTextBox ? WbTheme.Base : WbTheme.Surface0;
            root.ForeColor = WbTheme.Text;
            DarkScrollbars(root);
        }
        else if (root is TreeView)
        {
            var tv = (TreeView)root;
            tv.BackColor = WbTheme.Base; tv.ForeColor = WbTheme.Text; tv.LineColor = WbTheme.Surface2;
            DarkScrollbars(tv);
        }
        else if (root is ListView)
        {
            root.BackColor = WbTheme.Base; root.ForeColor = WbTheme.Text;
            DarkScrollbars(root);
        }
        else if (root is DataGridView)
        {
            var g = (DataGridView)root;
            g.BackgroundColor = WbTheme.Base; g.GridColor = WbTheme.Surface0;
            g.DefaultCellStyle.BackColor = WbTheme.Base; g.DefaultCellStyle.ForeColor = WbTheme.Text;
            g.DefaultCellStyle.SelectionBackColor = WbTheme.Selection; g.DefaultCellStyle.SelectionForeColor = WbTheme.Text;
            g.ColumnHeadersDefaultCellStyle.BackColor = WbTheme.Mantle; g.ColumnHeadersDefaultCellStyle.ForeColor = WbTheme.Subtext0;
            DarkScrollbars(g);
        }
        else if (root is Button)
        {
            var b = (Button)root;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = WbTheme.Surface0; b.ForeColor = WbTheme.Text;
            b.FlatAppearance.BorderColor = WbTheme.Surface1;
        }
        else if (root is ComboBox || root is CheckBox || root is Label || root is RadioButton)
        {
            if (!(root is Label) || root.BackColor != Color.Transparent) root.BackColor = root.Parent != null ? root.Parent.BackColor : WbTheme.Base;
            root.ForeColor = root is Label && ((Label)root).Tag as string == "dim" ? WbTheme.Subtext0 : WbTheme.Text;
        }
        else if (root is PropertyGrid)
        {
            var pg = (PropertyGrid)root;
            pg.BackColor = WbTheme.Mantle; pg.ViewBackColor = WbTheme.Base; pg.ViewForeColor = WbTheme.Text;
            pg.LineColor = WbTheme.Mantle; pg.CategoryForeColor = WbTheme.Subtext0; pg.HelpBackColor = WbTheme.Mantle;
            pg.HelpForeColor = WbTheme.Text; pg.CommandsBackColor = WbTheme.Mantle;
        }
        else if (root.GetType().Name == "FastColoredTextBox")
        {
            root.BackColor = WbTheme.Base; root.ForeColor = WbTheme.Text;
            try
            {
                root.GetType().GetProperty("LineNumberColor").SetValue(root, WbTheme.Overlay0, null);
                root.GetType().GetProperty("IndentBackColor").SetValue(root, WbTheme.Base, null);
            }
            catch { }
            DarkScrollbars(root);
        }
        else if (root is Panel || root is UserControl || root is SplitContainer || root is SplitterPanel || root is TabPage || root is DockContent)
        {
            if (!(root.Tag is string && (string)root.Tag == "keep")) root.BackColor = root is DockContent ? WbTheme.Base : (root.Tag as string == "bar" ? WbTheme.Mantle : WbTheme.Base);
            root.ForeColor = WbTheme.Text;
            if (root is ScrollableControl && ((ScrollableControl)root).AutoScroll) DarkScrollbars(root);
        }

        foreach (Control child in root.Controls) Apply(child);
    }

    // ── DockPanelSuite ─────────────────────────────────────────────────────────────────────────────────

    public static ThemeBase NewDockTheme()
    {
        ThemeBase t = WbTheme.Current.IsDark ? (ThemeBase)new VS2015DarkTheme() : new VS2015LightTheme();
        CustomizeDockPalette(t);
        return t;
    }

    internal static void CustomizeDockPalette(ThemeBase theme)
    {
        if (theme == null) return;
        try
        {
            var propInfo = theme.GetType().GetProperty("ColorPalette");
            var palette = propInfo == null ? null : propInfo.GetValue(theme, null);
            if (palette == null) return;

            Color accentBg = WbTheme.Current.Selection;
            Color accentText = WbTheme.Current.Text;
            Color accentHover = Color.FromArgb((accentBg.R * 3 + accentText.R) / 4, (accentBg.G * 3 + accentText.G) / 4, (accentBg.B * 3 + accentText.B) / 4);
            Func<Color, string, Color> map = (c, name) => MapPaletteColor(c, name, accentBg, accentHover, accentText, WbTheme.Crust, WbTheme.Mantle,
                WbTheme.Surface0, WbTheme.Surface1, WbTheme.Subtext0, WbTheme.Text, WbTheme.Current.IsDark);

            foreach (var prop in palette.GetType().GetProperties())
            {
                if (prop.PropertyType == typeof(Color) && prop.CanWrite)
                    prop.SetValue(palette, map((Color)prop.GetValue(palette, null), prop.Name), null);
                else
                {
                    var sub = prop.GetValue(palette, null);
                    if (sub == null || sub.GetType() == typeof(Color)) continue;
                    foreach (var sp in sub.GetType().GetProperties())
                        if (sp.PropertyType == typeof(Color) && sp.CanWrite)
                            sp.SetValue(sub, map((Color)sp.GetValue(sub, null), prop.Name + "." + sp.Name), null);
                }
            }

            // DockPanelSuite caches brushes/pens and tab glyph bitmaps: recolour those too.
            var psProp = theme.GetType().GetProperty("PaintingService");
            var ps = psProp == null ? null : psProp.GetValue(theme, null);
            if (ps != null)
                foreach (var prop in ps.GetType().GetProperties())
                {
                    if (prop.PropertyType == typeof(SolidBrush)) { var b = (SolidBrush)prop.GetValue(ps, null); if (b != null) b.Color = map(b.Color, prop.Name); }
                    else if (prop.PropertyType == typeof(Pen)) { var p = (Pen)prop.GetValue(ps, null); if (p != null) p.Color = map(p.Color, prop.Name); }
                }
            var isProp = theme.GetType().GetProperty("ImageService");
            var imgs = isProp == null ? null : isProp.GetValue(theme, null);
            if (imgs != null)
                foreach (var prop in imgs.GetType().GetProperties())
                {
                    var bmp = (prop.PropertyType == typeof(Bitmap) || prop.PropertyType == typeof(Image)) ? prop.GetValue(imgs, null) as Bitmap : null;
                    if (bmp == null) continue;
                    try
                    {
                        for (int x = 0; x < bmp.Width; x++)
                            for (int y = 0; y < bmp.Height; y++)
                            {
                                Color c = bmp.GetPixel(x, y), n = map(c, "ImagePixel");
                                if (c != n) bmp.SetPixel(x, y, n);
                            }
                    }
                    catch { }
                }
        }
        catch { }
    }

    internal static Color MapPaletteColor(Color c, string propName, Color accentBg, Color accentHover, Color accentText, Color crust, Color mantle, Color surf0, Color surf1, Color subtext, Color text, bool isDark)
    {
        if (c.A == 0) return c;
        if (propName.Contains("Border") || propName.Contains("Outline") || propName.Contains("Separator") || propName.Contains("Splitter"))
            return isDark ? mantle : surf1;
        if (propName.Contains("Text") || propName.Contains("Glyph") || propName.Contains("Arrow"))
            return propName.Contains("Inactive") || propName.Contains("Unselected") ? subtext : text;
        if (c.B > c.R + 20 && c.B > c.G + 10)
        {
            if (propName.Contains("Hover") || propName.Contains("Pressed") || propName.Contains("ActiveHovered")) return accentHover;
            if (propName.Contains("Background") || propName.Contains("Border") || propName.Contains("Active")) return accentBg;
            if (c.R > 150) return text;
            return c.G > 140 ? accentHover : accentBg;
        }
        int diff = Math.Max(Math.Max(c.R, c.G), c.B) - Math.Min(Math.Min(c.R, c.G), c.B);
        if (diff < 40)
        {
            int avg = (c.R + c.G + c.B) / 3;
            if (isDark)
            {
                if (avg < 50) return crust;
                if (avg < 70) return mantle;
                if (avg < 100) return surf0;
                if (avg < 150) return surf1;
                if (avg < 220) return subtext;
                return text;
            }
            if (avg < 50) return text;
            if (avg < 100) return subtext;
            if (avg < 150) return surf1;
            if (avg < 220) return mantle;
            return crust;
        }
        return c;
    }
}

/// <summary>A control that re-colours itself (called on creation and on every theme switch).</summary>
internal interface IThemed
{
    void ApplyTheme();
}

/// <summary>
/// Icons drawn from the Windows icon font (Segoe Fluent Icons on 11, Segoe MDL2 Assets on 10), tinted with the
/// theme. No image files to ship, and they follow the theme colour.
/// </summary>
internal static class Glyphs
{
    public const char New = '\uE710', Open = '\uE8E5', Save = '\uE74E', SaveAll = '\uEA35', Run = '\uE768', Stop = '\uE71A',
        Refresh = '\uE72C', Search = '\uE721', Settings = '\uE713', Code = '\uE943', Flow = '\uE945', Compare = '\uE8F1',
        Copy = '\uE8C8', Close = '\uE711', Check = '\uE73E', Warning = '\uE7BA', Error = '\uEA39', Info = '\uE946',
        Folder = '\uE8B7', List = '\uE8FD', Tree = '\uE71D', Edit = '\uE70F', Palette = '\uE790', Validate = '\uE9D5',
        Filter = '\uE71C', Export = '\uEDE1', Build = '\uE90F', Trace = '\uE9D9', Document = '\uE8A5', Class = '\uE8F9',
        Function = '\uE8EF', Keyboard = '\uE92E', Tag = '\uE8EC', Link = '\uE71B', Variable = '\uE943', Clear = '\uE894',
        Up = '\uE70E', Down = '\uE70D', ChevronRight = '\uE76C', Collapse = '\uE70E', Expand = '\uE70D';

    static string _family;
    static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

    static string Family
    {
        get
        {
            if (_family != null) return _family;
            _family = "";
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families)
                {
                    if (f.Name == "Segoe Fluent Icons") { _family = f.Name; break; }
                    if (f.Name == "Segoe MDL2 Assets") _family = f.Name;
                }
            return _family;
        }
    }

    public static bool Available { get { return Family.Length > 0; } }

    public static Bitmap Get(char glyph, Color color, int size = 16)
    {
        if (!Available) return null;
        string key = glyph + "|" + color.ToArgb() + "|" + size;
        Bitmap bmp;
        if (Cache.TryGetValue(key, out bmp)) return bmp;
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font(Family, size * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0.5f, size, size), sf);
        }
        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>A small rounded badge with a letter (symbol kinds in the outline).</summary>
    public static Bitmap Badge(string letter, Color color, int size = 16)
    {
        string key = "badge|" + letter + "|" + color.ToArgb() + "|" + size;
        Bitmap bmp;
        if (Cache.TryGetValue(key, out bmp)) return bmp;
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var fill = new SolidBrush(Color.FromArgb(55, color)))
        using (var pen = new Pen(Color.FromArgb(170, color)))
        using (var font = new Font("Segoe UI", size * 0.56f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fg = new SolidBrush(color))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var path = new GraphicsPath())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var r = new RectangleF(1.5f, 1.5f, size - 3, size - 3);
            float rad = 3.5f;
            path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            g.DrawString(letter, font, fg, new RectangleF(0, 0.5f, size, size), sf);
        }
        Cache[key] = bmp;
        return bmp;
    }

    public static Bitmap Dot(Color color, int size = 16)
    {
        string key = "dot|" + color.ToArgb() + "|" + size;
        Bitmap bmp;
        if (Cache.TryGetValue(key, out bmp)) return bmp;
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var b = new SolidBrush(color))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float d = size * 0.5f;
            g.FillEllipse(b, (size - d) / 2, (size - d) / 2, d, d);
        }
        Cache[key] = bmp;
        return bmp;
    }
}

// ── Renderers ──────────────────────────────────────────────────────────────────────────────────────────

internal class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled) return;
        if (e.Item.Selected || e.Item.Pressed)
            using (var brush = new SolidBrush(WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
            using (var pen = new Pen(WbTheme.Surface1))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var mi = e.Item as ToolStripMenuItem;
        bool shortcut = mi != null && e.Text == mi.ShortcutKeyDisplayString || (mi != null && mi.ShortcutKeys != Keys.None && e.Text != mi.Text);
        e.TextColor = !e.Item.Enabled ? WbTheme.Overlay0 : shortcut ? WbTheme.Subtext0 : (e.Item.Selected ? WbTheme.Text : WbTheme.Subtext1);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = WbTheme.Subtext0;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item.Pressed)
            using (var brush = new SolidBrush(WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle rect = e.ImageRectangle;
        rect.Inflate(-1, -1);
        using (var pen = new Pen(WbTheme.Accent, 2))
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, rect.X + 3, rect.Y + rect.Height / 2, rect.X + rect.Width / 2 - 1, rect.Bottom - 3);
            e.Graphics.DrawLine(pen, rect.X + rect.Width / 2 - 1, rect.Bottom - 3, rect.Right - 3, rect.Y + 3);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using (var pen = new Pen(WbTheme.Surface0))
        {
            if (e.Vertical) e.Graphics.DrawLine(pen, e.Item.Width / 2, 4, e.Item.Width / 2, e.Item.Height - 4);
            else e.Graphics.DrawLine(pen, 28, e.Item.Height / 2, e.Item.Width - 4, e.Item.Height / 2);
        }
    }
}

internal class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return WbTheme.Surface1; } }
    public override Color MenuItemBorder { get { return WbTheme.Surface1; } }
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
    public override Color ButtonSelectedHighlight { get { return WbTheme.Surface1; } }
    public override Color ButtonSelectedBorder { get { return WbTheme.Surface1; } }
    public override Color ButtonPressedBorder { get { return WbTheme.Surface1; } }
    public override Color ButtonCheckedHighlight { get { return WbTheme.Surface1; } }
    public override Color CheckBackground { get { return WbTheme.Surface1; } }
    public override Color CheckSelectedBackground { get { return WbTheme.Surface1; } }
    public override Color CheckPressedBackground { get { return WbTheme.Surface1; } }
}

internal class DarkToolStripRenderer : DarkMenuRenderer
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? (e.Item.ForeColor == SystemColors.ControlText ? WbTheme.Text : e.Item.ForeColor) : WbTheme.Overlay0;
        var f = e.TextFormat; e.TextFormat = f | TextFormatFlags.NoPrefix;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using (var brush = new SolidBrush(e.ToolStrip is StatusStrip ? WbTheme.Crust : WbTheme.Mantle))
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) { base.OnRenderToolStripBorder(e); return; }
        if (e.ToolStrip is StatusStrip) return;
        using (var pen = new Pen(WbTheme.Surface0))
            e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Bottom - 1, e.AffectedBounds.Right, e.AffectedBounds.Bottom - 1);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var b = e.Item as ToolStripButton;
        if (e.Item.Selected || e.Item.Pressed || (b != null && b.Checked))
            using (var brush = new SolidBrush(b != null && b.Checked && !e.Item.Selected ? WbTheme.Surface0 : WbTheme.Surface1))
                e.Graphics.FillRectangle(brush, new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2));
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderButtonBackground(e);
    }

    protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
    {
        OnRenderButtonBackground(e);
        var sb = (ToolStripSplitButton)e.Item;
        using (var pen = new Pen(WbTheme.Surface1))
            e.Graphics.DrawLine(pen, sb.SplitterBounds.Left, 4, sb.SplitterBounds.Left, sb.Height - 4);
        DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds, WbTheme.Subtext0, ArrowDirection.Down));
    }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) { }
}
