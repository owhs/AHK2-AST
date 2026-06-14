using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

public class EmitOptions
{
    /// <summary>Include comment nodes in output.</summary>
    public bool EmitComments { get; set; }
    /// <summary>Preserve blank lines between statements using source line gaps.</summary>
    public bool EmitBlankLines { get; set; }
    /// <summary>Preserve original indentation from source instead of re-indenting.</summary>
    public bool PreserveIndent { get; set; }
    /// <summary>Use tabs instead of spaces for indentation.</summary>
    public bool UseTabs { get; set; }
    /// <summary>Number of spaces (or tabs) per indent level. Default 4.</summary>
    public int IndentSize { get; set; }
    /// <summary>Keep #Include directives in the source instead of expanding/inlining them.</summary>
    public bool PreserveIncludes { get; set; }

    /// <summary>Default: comments on, blank lines on, 4-space indent.</summary>
    public EmitOptions()
    {
        EmitComments = true;
        EmitBlankLines = true;
        PreserveIndent = false;
        UseTabs = false;
        IndentSize = 4;
    }

    /// <summary>Preset: faithful 1:1 recreation.</summary>
    public static EmitOptions Faithful
    {
        get { return new EmitOptions { EmitComments = true, EmitBlankLines = true, PreserveIndent = true }; }
    }

    /// <summary>Preset: compact (no comments, no blanks).</summary>
    public static EmitOptions Compact
    {
        get { return new EmitOptions { EmitComments = false, EmitBlankLines = false, PreserveIndent = false }; }
    }
}
