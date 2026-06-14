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

// Theme — Dynamic Theming Architecture
// ═══════════════════════════════════════════════════════════════════════════════

public static class ThemeManager
{
    public static readonly ThemePalette[] Themes = new ThemePalette[]
    {
        // 1. Forest Midnight (Default)
        new ThemePalette {
            Name = "Forest Midnight", IsDark = true,
            Base = Color.FromArgb(15, 22, 18), Mantle = Color.FromArgb(20, 28, 24), Crust = Color.FromArgb(10, 16, 12),
            Surface0 = Color.FromArgb(35, 45, 40), Surface1 = Color.FromArgb(45, 55, 50), Surface2 = Color.FromArgb(55, 65, 60), Overlay0 = Color.FromArgb(85, 100, 90),
            Text = Color.FromArgb(220, 240, 230), Subtext0 = Color.FromArgb(180, 210, 195), Subtext1 = Color.FromArgb(160, 190, 175),
            Blue = Color.FromArgb(120, 180, 220), Lavender = Color.FromArgb(150, 190, 210), Sapphire = Color.FromArgb(100, 160, 200), Sky = Color.FromArgb(140, 200, 230), Teal = Color.FromArgb(100, 220, 180), Green = Color.FromArgb(140, 220, 120),
            Yellow = Color.FromArgb(220, 200, 100), Peach = Color.FromArgb(230, 160, 110), Maroon = Color.FromArgb(200, 90, 100), Red = Color.FromArgb(220, 100, 110), Mauve = Color.FromArgb(180, 140, 200), Pink = Color.FromArgb(210, 160, 190), Flamingo = Color.FromArgb(230, 180, 200), Rosewater = Color.FromArgb(240, 200, 210),
            ErrorBg = Color.FromArgb(45, 20, 25), WarnBg = Color.FromArgb(45, 40, 20), SuccessBg = Color.FromArgb(20, 45, 25), Selection = Color.FromArgb(45, 60, 50),
            Accent = Color.FromArgb(140, 220, 120) // Green
        },
        // 2. Catppuccin Mocha
        new ThemePalette {
            Name = "Catppuccin Mocha",
            IsDark = true,
            Base = Color.FromArgb(30, 30, 46),
            Mantle = Color.FromArgb(24, 24, 37),
            Crust = Color.FromArgb(17, 17, 27),
            Surface0 = Color.FromArgb(49, 50, 68),
            Surface1 = Color.FromArgb(69, 71, 90),
            Surface2 = Color.FromArgb(88, 91, 112),
            Overlay0 = Color.FromArgb(108, 112, 134),
            Text = Color.FromArgb(205, 214, 244),
            Subtext0 = Color.FromArgb(166, 173, 200),
            Subtext1 = Color.FromArgb(186, 194, 222),
            Blue = Color.FromArgb(137, 180, 250),
            Lavender = Color.FromArgb(180, 190, 254),
            Sapphire = Color.FromArgb(116, 199, 236),
            Sky = Color.FromArgb(137, 220, 235),
            Teal = Color.FromArgb(148, 226, 213),
            Green = Color.FromArgb(166, 227, 161),
            Yellow = Color.FromArgb(249, 226, 175),
            Peach = Color.FromArgb(250, 179, 135),
            Maroon = Color.FromArgb(235, 160, 172),
            Red = Color.FromArgb(243, 139, 168),
            Mauve = Color.FromArgb(203, 166, 247),
            Pink = Color.FromArgb(245, 194, 231),
            Flamingo = Color.FromArgb(242, 205, 205),
            Rosewater = Color.FromArgb(245, 224, 220),
            ErrorBg = Color.FromArgb(60, 30, 40),
            WarnBg = Color.FromArgb(60, 50, 30),
            SuccessBg = Color.FromArgb(30, 60, 40),
            Selection = Color.FromArgb(69, 71, 120),
            Accent = Color.FromArgb(203, 166, 247) // Mauve
        },
        // 3. Dracula
        new ThemePalette {
            Name = "Dracula",
            IsDark = true,
            Base = Color.FromArgb(40, 42, 54),
            Mantle = Color.FromArgb(33, 34, 44),
            Crust = Color.FromArgb(25, 26, 33),
            Surface0 = Color.FromArgb(68, 71, 90),
            Surface1 = Color.FromArgb(76, 79, 100),
            Surface2 = Color.FromArgb(98, 114, 164),
            Overlay0 = Color.FromArgb(118, 134, 184),
            Text = Color.FromArgb(248, 248, 242),
            Subtext0 = Color.FromArgb(210, 210, 210),
            Subtext1 = Color.FromArgb(230, 230, 230),
            Blue = Color.FromArgb(139, 233, 253),
            Lavender = Color.FromArgb(153, 169, 255),
            Sapphire = Color.FromArgb(139, 233, 253),
            Sky = Color.FromArgb(139, 233, 253),
            Teal = Color.FromArgb(139, 233, 253),
            Green = Color.FromArgb(80, 250, 123),
            Yellow = Color.FromArgb(241, 250, 140),
            Peach = Color.FromArgb(255, 184, 108),
            Maroon = Color.FromArgb(255, 85, 85),
            Red = Color.FromArgb(255, 85, 85),
            Mauve = Color.FromArgb(189, 147, 249),
            Pink = Color.FromArgb(255, 121, 198),
            Flamingo = Color.FromArgb(255, 146, 208),
            Rosewater = Color.FromArgb(255, 166, 218),
            ErrorBg = Color.FromArgb(60, 20, 20),
            WarnBg = Color.FromArgb(60, 60, 20),
            SuccessBg = Color.FromArgb(20, 60, 30),
            Selection = Color.FromArgb(68, 71, 90),
            Accent = Color.FromArgb(255, 121, 198) // Pink
        },
        // 4. One Dark Pro
        new ThemePalette {
            Name = "One Dark Pro",
            IsDark = true,
            Base = Color.FromArgb(40, 44, 52),
            Mantle = Color.FromArgb(33, 37, 43),
            Crust = Color.FromArgb(24, 26, 31),
            Surface0 = Color.FromArgb(59, 64, 72),
            Surface1 = Color.FromArgb(75, 82, 99),
            Surface2 = Color.FromArgb(92, 99, 112),
            Overlay0 = Color.FromArgb(108, 115, 128),
            Text = Color.FromArgb(171, 178, 191),
            Subtext0 = Color.FromArgb(144, 153, 171),
            Subtext1 = Color.FromArgb(158, 167, 185),
            Blue = Color.FromArgb(97, 175, 239),
            Lavender = Color.FromArgb(115, 188, 255),
            Sapphire = Color.FromArgb(86, 182, 194),
            Sky = Color.FromArgb(86, 182, 194),
            Teal = Color.FromArgb(86, 182, 194),
            Green = Color.FromArgb(152, 195, 121),
            Yellow = Color.FromArgb(229, 192, 123),
            Peach = Color.FromArgb(209, 154, 102),
            Maroon = Color.FromArgb(224, 108, 117),
            Red = Color.FromArgb(224, 108, 117),
            Mauve = Color.FromArgb(198, 120, 221),
            Pink = Color.FromArgb(210, 138, 232),
            Flamingo = Color.FromArgb(220, 148, 242),
            Rosewater = Color.FromArgb(230, 158, 252),
            ErrorBg = Color.FromArgb(60, 30, 35),
            WarnBg = Color.FromArgb(60, 50, 30),
            SuccessBg = Color.FromArgb(35, 60, 40),
            Selection = Color.FromArgb(62, 68, 81),
            Accent = Color.FromArgb(97, 175, 239) // Blue
        },
        // 5. Nord
        new ThemePalette {
            Name = "Nord",
            IsDark = true,
            Base = Color.FromArgb(46, 52, 64),
            Mantle = Color.FromArgb(36, 41, 51),
            Crust = Color.FromArgb(27, 31, 39),
            Surface0 = Color.FromArgb(59, 66, 82),
            Surface1 = Color.FromArgb(67, 76, 94),
            Surface2 = Color.FromArgb(76, 86, 106),
            Overlay0 = Color.FromArgb(90, 100, 120),
            Text = Color.FromArgb(216, 222, 233),
            Subtext0 = Color.FromArgb(180, 188, 204),
            Subtext1 = Color.FromArgb(198, 205, 218),
            Blue = Color.FromArgb(129, 161, 193),
            Lavender = Color.FromArgb(143, 188, 187),
            Sapphire = Color.FromArgb(136, 192, 208),
            Sky = Color.FromArgb(136, 192, 208),
            Teal = Color.FromArgb(143, 188, 187),
            Green = Color.FromArgb(163, 190, 140),
            Yellow = Color.FromArgb(235, 203, 139),
            Peach = Color.FromArgb(208, 135, 112),
            Maroon = Color.FromArgb(191, 97, 106),
            Red = Color.FromArgb(191, 97, 106),
            Mauve = Color.FromArgb(180, 142, 173),
            Pink = Color.FromArgb(190, 152, 183),
            Flamingo = Color.FromArgb(200, 162, 193),
            Rosewater = Color.FromArgb(210, 172, 203),
            ErrorBg = Color.FromArgb(60, 35, 40),
            WarnBg = Color.FromArgb(60, 55, 35),
            SuccessBg = Color.FromArgb(40, 60, 45),
            Selection = Color.FromArgb(67, 76, 94),
            Accent = Color.FromArgb(136, 192, 208) // Sapphire
        },
        // 6. Monokai Pro
        new ThemePalette {
            Name = "Monokai Pro",
            IsDark = true,
            Base = Color.FromArgb(45, 42, 46),
            Mantle = Color.FromArgb(34, 31, 34),
            Crust = Color.FromArgb(26, 24, 26),
            Surface0 = Color.FromArgb(64, 62, 65),
            Surface1 = Color.FromArgb(80, 78, 81),
            Surface2 = Color.FromArgb(114, 112, 114),
            Overlay0 = Color.FromArgb(147, 146, 147),
            Text = Color.FromArgb(252, 252, 250),
            Subtext0 = Color.FromArgb(200, 200, 200),
            Subtext1 = Color.FromArgb(220, 220, 220),
            Blue = Color.FromArgb(120, 220, 232),
            Lavender = Color.FromArgb(140, 230, 242),
            Sapphire = Color.FromArgb(120, 220, 232),
            Sky = Color.FromArgb(120, 220, 232),
            Teal = Color.FromArgb(120, 220, 232),
            Green = Color.FromArgb(169, 220, 118),
            Yellow = Color.FromArgb(255, 216, 102),
            Peach = Color.FromArgb(252, 152, 103),
            Maroon = Color.FromArgb(255, 97, 136),
            Red = Color.FromArgb(255, 97, 136),
            Mauve = Color.FromArgb(171, 157, 242),
            Pink = Color.FromArgb(181, 167, 252),
            Flamingo = Color.FromArgb(191, 177, 255),
            Rosewater = Color.FromArgb(201, 187, 255),
            ErrorBg = Color.FromArgb(60, 25, 35),
            WarnBg = Color.FromArgb(60, 55, 25),
            SuccessBg = Color.FromArgb(35, 60, 30),
            Selection = Color.FromArgb(64, 62, 65),
            Accent = Color.FromArgb(255, 216, 102) // Yellow
        },
        // 7. Midnight Blue (Two-Tone)
        new ThemePalette {
            Name = "Midnight Blue", IsDark = true,
            Base = Color.FromArgb(10, 15, 25), Mantle = Color.FromArgb(15, 20, 35), Crust = Color.FromArgb(5, 10, 20),
            Surface0 = Color.FromArgb(25, 35, 55), Surface1 = Color.FromArgb(35, 45, 65), Surface2 = Color.FromArgb(45, 55, 75), Overlay0 = Color.FromArgb(70, 90, 120),
            Text = Color.FromArgb(220, 230, 250), Subtext0 = Color.FromArgb(180, 190, 220), Subtext1 = Color.FromArgb(150, 160, 200),
            Blue = Color.FromArgb(100, 150, 255), Lavender = Color.FromArgb(150, 180, 255), Sapphire = Color.FromArgb(80, 120, 230), Sky = Color.FromArgb(120, 160, 240), Teal = Color.FromArgb(60, 180, 200), Green = Color.FromArgb(80, 200, 150),
            Yellow = Color.FromArgb(220, 180, 100), Peach = Color.FromArgb(200, 140, 100), Maroon = Color.FromArgb(200, 80, 100), Red = Color.FromArgb(220, 60, 80), Mauve = Color.FromArgb(180, 120, 220), Pink = Color.FromArgb(220, 150, 200), Flamingo = Color.FromArgb(240, 160, 180), Rosewater = Color.FromArgb(250, 190, 200),
            ErrorBg = Color.FromArgb(50, 20, 30), WarnBg = Color.FromArgb(50, 40, 20), SuccessBg = Color.FromArgb(20, 50, 30), Selection = Color.FromArgb(35, 50, 80),
            Accent = Color.FromArgb(100, 150, 255) // Blue
        },
        // 8. Crimson Dark (Two-Tone)
        new ThemePalette {
            Name = "Crimson Dark", IsDark = true,
            Base = Color.FromArgb(20, 10, 12), Mantle = Color.FromArgb(25, 15, 17), Crust = Color.FromArgb(15, 5, 8),
            Surface0 = Color.FromArgb(45, 25, 30), Surface1 = Color.FromArgb(55, 35, 40), Surface2 = Color.FromArgb(65, 45, 50), Overlay0 = Color.FromArgb(100, 60, 70),
            Text = Color.FromArgb(250, 220, 225), Subtext0 = Color.FromArgb(220, 180, 190), Subtext1 = Color.FromArgb(200, 150, 160),
            Blue = Color.FromArgb(100, 150, 255), Lavender = Color.FromArgb(150, 120, 220), Sapphire = Color.FromArgb(120, 100, 200), Sky = Color.FromArgb(180, 150, 230), Teal = Color.FromArgb(80, 160, 160), Green = Color.FromArgb(120, 200, 120),
            Yellow = Color.FromArgb(220, 180, 100), Peach = Color.FromArgb(250, 150, 120), Maroon = Color.FromArgb(200, 60, 80), Red = Color.FromArgb(255, 80, 100), Mauve = Color.FromArgb(220, 120, 150), Pink = Color.FromArgb(250, 140, 180), Flamingo = Color.FromArgb(255, 160, 190), Rosewater = Color.FromArgb(255, 190, 210),
            ErrorBg = Color.FromArgb(50, 15, 20), WarnBg = Color.FromArgb(50, 35, 15), SuccessBg = Color.FromArgb(25, 45, 25), Selection = Color.FromArgb(60, 30, 40),
            Accent = Color.FromArgb(255, 80, 100) // Red
        },
        // 9. Cyberpunk (Two-Tone)
        new ThemePalette {
            Name = "Cyberpunk", IsDark = true,
            Base = Color.FromArgb(12, 10, 28), Mantle = Color.FromArgb(16, 14, 34), Crust = Color.FromArgb(8, 6, 22),
            Surface0 = Color.FromArgb(32, 28, 55), Surface1 = Color.FromArgb(42, 38, 65), Surface2 = Color.FromArgb(52, 48, 75), Overlay0 = Color.FromArgb(80, 75, 110),
            Text = Color.FromArgb(240, 230, 255), Subtext0 = Color.FromArgb(200, 190, 230), Subtext1 = Color.FromArgb(170, 160, 200),
            Blue = Color.FromArgb(0, 240, 255), Lavender = Color.FromArgb(150, 180, 255), Sapphire = Color.FromArgb(80, 200, 255), Sky = Color.FromArgb(120, 220, 255), Teal = Color.FromArgb(0, 255, 200), Green = Color.FromArgb(100, 255, 120),
            Yellow = Color.FromArgb(255, 240, 0), Peach = Color.FromArgb(255, 160, 80), Maroon = Color.FromArgb(220, 60, 100), Red = Color.FromArgb(255, 0, 80), Mauve = Color.FromArgb(200, 80, 255), Pink = Color.FromArgb(255, 100, 200), Flamingo = Color.FromArgb(255, 130, 220), Rosewater = Color.FromArgb(255, 170, 240),
            ErrorBg = Color.FromArgb(50, 15, 30), WarnBg = Color.FromArgb(50, 45, 15), SuccessBg = Color.FromArgb(15, 50, 35), Selection = Color.FromArgb(50, 40, 80),
            Accent = Color.FromArgb(0, 240, 255) // Blue/Cyan
        },
        // 10. Autumn Dusk (Two-Tone)
        new ThemePalette {
            Name = "Autumn Dusk", IsDark = true,
            Base = Color.FromArgb(28, 20, 18), Mantle = Color.FromArgb(34, 25, 22), Crust = Color.FromArgb(22, 15, 14),
            Surface0 = Color.FromArgb(55, 40, 35), Surface1 = Color.FromArgb(65, 50, 45), Surface2 = Color.FromArgb(75, 60, 55), Overlay0 = Color.FromArgb(110, 90, 80),
            Text = Color.FromArgb(250, 235, 225), Subtext0 = Color.FromArgb(220, 200, 190), Subtext1 = Color.FromArgb(190, 170, 160),
            Blue = Color.FromArgb(130, 170, 220), Lavender = Color.FromArgb(160, 180, 210), Sapphire = Color.FromArgb(110, 150, 200), Sky = Color.FromArgb(150, 190, 230), Teal = Color.FromArgb(120, 200, 170), Green = Color.FromArgb(160, 210, 130),
            Yellow = Color.FromArgb(240, 190, 100), Peach = Color.FromArgb(250, 160, 110), Maroon = Color.FromArgb(210, 90, 80), Red = Color.FromArgb(230, 100, 90), Mauve = Color.FromArgb(190, 130, 180), Pink = Color.FromArgb(220, 150, 190), Flamingo = Color.FromArgb(240, 170, 200), Rosewater = Color.FromArgb(250, 190, 210),
            ErrorBg = Color.FromArgb(55, 25, 25), WarnBg = Color.FromArgb(55, 45, 20), SuccessBg = Color.FromArgb(30, 50, 25), Selection = Color.FromArgb(70, 50, 45),
            Accent = Color.FromArgb(250, 160, 110) // Peach
        },
        // 11. GitHub Light (Sexy Light)
        new ThemePalette {
            Name = "GitHub Light", IsDark = false,
            Base = Color.FromArgb(255, 255, 255), Mantle = Color.FromArgb(246, 248, 250), Crust = Color.FromArgb(234, 238, 242),
            Surface0 = Color.FromArgb(240, 243, 246), Surface1 = Color.FromArgb(225, 228, 232), Surface2 = Color.FromArgb(209, 213, 218), Overlay0 = Color.FromArgb(149, 157, 165),
            Text = Color.FromArgb(36, 41, 46), Subtext0 = Color.FromArgb(88, 96, 105), Subtext1 = Color.FromArgb(106, 115, 125),
            Blue = Color.FromArgb(9, 105, 218), Lavender = Color.FromArgb(130, 80, 223), Sapphire = Color.FromArgb(5, 80, 174), Sky = Color.FromArgb(10, 80, 160), Teal = Color.FromArgb(20, 120, 110), Green = Color.FromArgb(10, 110, 40),
            Yellow = Color.FromArgb(180, 110, 0), Peach = Color.FromArgb(180, 80, 10), Maroon = Color.FromArgb(150, 30, 40), Red = Color.FromArgb(164, 14, 38), Mauve = Color.FromArgb(102, 57, 186), Pink = Color.FromArgb(188, 60, 140), Flamingo = Color.FromArgb(220, 100, 150), Rosewater = Color.FromArgb(230, 140, 170),
            ErrorBg = Color.FromArgb(255, 238, 240), WarnBg = Color.FromArgb(255, 245, 204), SuccessBg = Color.FromArgb(234, 250, 236), Selection = Color.FromArgb(199, 224, 244),
            Accent = Color.FromArgb(9, 105, 218) // Blue
        },
        // 12. Solarized Light (Sexy Light)
        new ThemePalette {
            Name = "Solarized Light", IsDark = false,
            Base = Color.FromArgb(253, 246, 227), Mantle = Color.FromArgb(238, 232, 213), Crust = Color.FromArgb(220, 210, 190),
            Surface0 = Color.FromArgb(240, 235, 210), Surface1 = Color.FromArgb(225, 220, 195), Surface2 = Color.FromArgb(210, 205, 180), Overlay0 = Color.FromArgb(147, 161, 161),
            Text = Color.FromArgb(101, 123, 131), Subtext0 = Color.FromArgb(88, 110, 117), Subtext1 = Color.FromArgb(131, 148, 150),
            Blue = Color.FromArgb(38, 139, 210), Lavender = Color.FromArgb(108, 113, 196), Sapphire = Color.FromArgb(30, 120, 190), Sky = Color.FromArgb(30, 100, 160), Teal = Color.FromArgb(42, 161, 152), Green = Color.FromArgb(90, 110, 0),
            Yellow = Color.FromArgb(150, 110, 0), Peach = Color.FromArgb(203, 75, 22), Maroon = Color.FromArgb(220, 50, 47), Red = Color.FromArgb(180, 40, 35), Mauve = Color.FromArgb(211, 54, 130), Pink = Color.FromArgb(220, 80, 150), Flamingo = Color.FromArgb(230, 100, 160), Rosewater = Color.FromArgb(240, 120, 170),
            ErrorBg = Color.FromArgb(250, 220, 220), WarnBg = Color.FromArgb(250, 240, 200), SuccessBg = Color.FromArgb(220, 240, 210), Selection = Color.FromArgb(220, 225, 210),
            Accent = Color.FromArgb(203, 75, 22) // Peach
        }
    };
}

public class ThemePalette
{
    public string Name { get; set; }
    public bool IsDark { get; set; }

    public Color Base { get; set; }
    public Color Mantle { get; set; }
    public Color Crust { get; set; }
    public Color Surface0 { get; set; }
    public Color Surface1 { get; set; }
    public Color Surface2 { get; set; }
    public Color Overlay0 { get; set; }

    public Color Text { get; set; }
    public Color Subtext0 { get; set; }
    public Color Subtext1 { get; set; }

    public Color Blue { get; set; }
    public Color Lavender { get; set; }
    public Color Sapphire { get; set; }
    public Color Sky { get; set; }
    public Color Teal { get; set; }
    public Color Green { get; set; }
    public Color Yellow { get; set; }
    public Color Peach { get; set; }
    public Color Maroon { get; set; }
    public Color Red { get; set; }
    public Color Mauve { get; set; }
    public Color Pink { get; set; }
    public Color Flamingo { get; set; }
    public Color Rosewater { get; set; }

    public Color ErrorBg { get; set; }
    public Color WarnBg { get; set; }
    public Color SuccessBg { get; set; }
    public Color Selection { get; set; }
    public Color Accent { get; set; }
}

public static class WbTheme
{
    public static ThemePalette Current = ThemeManager.Themes[0];

    // Base
    public static Color Base { get { return Current.Base; } }
    public static Color Mantle { get { return Current.Mantle; } }
    public static Color Crust { get { return Current.Crust; } }
    public static Color Surface0 { get { return Current.Surface0; } }
    public static Color Surface1 { get { return Current.Surface1; } }
    public static Color Surface2 { get { return Current.Surface2; } }
    public static Color Overlay0 { get { return Current.Overlay0; } }

    // Text
    public static Color Text { get { return Current.Text; } }
    public static Color Subtext0 { get { return Current.Subtext0; } }
    public static Color Subtext1 { get { return Current.Subtext1; } }

    // Accents
    public static Color Blue { get { return Current.Blue; } }
    public static Color Lavender { get { return Current.Lavender; } }
    public static Color Sapphire { get { return Current.Sapphire; } }
    public static Color Sky { get { return Current.Sky; } }
    public static Color Teal { get { return Current.Teal; } }
    public static Color Green { get { return Current.Green; } }
    public static Color Yellow { get { return Current.Yellow; } }
    public static Color Peach { get { return Current.Peach; } }
    public static Color Maroon { get { return Current.Maroon; } }
    public static Color Red { get { return Current.Red; } }
    public static Color Mauve { get { return Current.Mauve; } }
    public static Color Pink { get { return Current.Pink; } }
    public static Color Flamingo { get { return Current.Flamingo; } }
    public static Color Rosewater { get { return Current.Rosewater; } }

    // Semantic
    public static Color ErrorBg { get { return Current.ErrorBg; } }
    public static Color WarnBg { get { return Current.WarnBg; } }
    public static Color SuccessBg { get { return Current.SuccessBg; } }
    public static Color Selection { get { return Current.Selection; } }
    public static Color Accent { get { return Current.Accent; } }

    public static readonly Font MonoFont = new Font("Cascadia Code", 10f, FontStyle.Regular);
    public static readonly Font MonoSmall = new Font("Cascadia Code", 9f, FontStyle.Regular);
    public static readonly Font UIFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
    public static readonly Font UIBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font UISmall = new Font("Segoe UI", 8.5f, FontStyle.Regular);

    public static void Apply(Control ctrl)
    {
        ctrl.BackColor = Base;
        ctrl.ForeColor = Text;
        ctrl.Font = UIFont;
        foreach (Control c in ctrl.Controls)
            Apply(c);
    }
}

public class ThemedComboBox : ComboBox
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            SetWindowTheme(Handle, "", "");
        }
        catch {}
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = isSelected ? WbTheme.Selection : WbTheme.Base;
        Color fg = isSelected ? WbTheme.Text : WbTheme.Subtext0;
        
        using (var brush = new SolidBrush(bg))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }
        
        string text = Items[e.Index].ToString();
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        
        var textRect = e.Bounds;
        textRect.Offset(4, 0);
        textRect.Width -= 4;
        
        TextRenderer.DrawText(e.Graphics, text, Font, textRect, fg, flags);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        
        if (m.Msg == 0xF) // WM_PAINT
        {
            using (var g = CreateGraphics())
            {
                Color borderCol = Focused ? WbTheme.Accent : WbTheme.Surface1;
                
                using (var pen = new Pen(borderCol))
                {
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
                
                int btnWidth = 20;
                var btnRect = new Rectangle(Width - btnWidth, 1, btnWidth - 1, Height - 2);
                using (var brush = new SolidBrush(WbTheme.Base))
                {
                    g.FillRectangle(brush, btnRect);
                }
                
                using (var penSep = new Pen(WbTheme.Surface0))
                {
                    g.DrawLine(penSep, Width - btnWidth, 1, Width - btnWidth, Height - 2);
                }
                
                using (var penArrow = new Pen(WbTheme.Subtext0, 1.5f))
                {
                    int cx = btnRect.X + btnRect.Width / 2;
                    int cy = btnRect.Y + btnRect.Height / 2;
                    g.DrawLine(penArrow, cx - 4, cy - 2, cx, cy + 2);
                    g.DrawLine(penArrow, cx, cy + 2, cx + 4, cy - 2);
                }
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════