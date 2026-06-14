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

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
[Guid("C3D4E5F6-A7B8-9012-CDEF-345678901ABC")]
[ProgId("Ahk2Ast.AstWorkbench")]
public class AstWorkbench
{
    private AstWorkbenchForm _form;
    private string _pendingFile;

    public AstWorkbench()
    {
        // EnableVisualStyles can fail when the assembly is loaded from
        // a byte array (Assembly.Location is empty). Catch and continue.
        try { Application.EnableVisualStyles(); } catch { }
    }

    public void OpenFile(string path)
    {
        if (_form != null && !_form.IsDisposed)
            _form.LoadFile(path);
        else
            _pendingFile = path;
    }

    public void ShowDialog()
    {
        _form = new AstWorkbenchForm();
        if (!string.IsNullOrEmpty(_pendingFile))
            _form.LoadFileOnShown(_pendingFile);
        Application.Run(_form);
    }

    public void Show()
    {
        _form = new AstWorkbenchForm();
        if (!string.IsNullOrEmpty(_pendingFile))
            _form.LoadFileOnShown(_pendingFile);
        _form.Show();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════