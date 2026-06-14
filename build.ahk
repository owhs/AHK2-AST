#Requires AutoHotkey v2.0
#SingleInstance Force

; =========================================================================
; AHK# AST Workbench — Dynamic Build Script & Compilation Manager
; =========================================================================
; Provides both a highly polished GUI and a headless CLI.
; Dynamically scans the plugins folder and compiles custom builds.
; =========================================================================

; --- Constants & Configuration ---
global ProjectDir := A_ScriptDir
if (!DirExist(ProjectDir "\src") && DirExist(ProjectDir "\..\src")) {
    ProjectDir := ProjectDir "\.."
}
global SrcDir := ProjectDir "\src"
global BuildDir := ProjectDir "\build"
global TestDir := ProjectDir "\tests"

global CorePlugins := Map(
    "AhkStringHelper.cs", true,
    "FlowDefinition.cs", true,
    "PipelineMeta.cs", true
)

; Size estimates map (space provided for user adjustments)
global PluginSizes := Map(
    "LogicShakerPlugin.cs", "28 KB",
    "NodeDiagramPlugin.cs", "224 KB",
    "TransformAndFormatPlugins.cs", "70 KB",
    "EvalAnalysisPlugin.cs", "70 KB",
    "IoPlugins.cs", "12 KB",
    "AutoFixPlugin.cs", "8 KB",
    "NimTranspilerPlugin.cs", "15 KB"
)

; --- Global State Variables ---
global Headless := false
global Verbose := false
global CleanBuild := true
global CompileUi := true
global CompileEngine := true
global CompileWorkbench := true
global CompileTests := true
global SelectedPluginsList := []
global AvailablePlugins := []

; --- CLI & Mode Initialization ---
InitializeMode()

InitializeMode() {
    global Headless, Verbose, CleanBuild, CompileUi, CompileEngine, CompileWorkbench, CompileTests, SelectedPluginsList, AvailablePlugins

    ; Scan for plugins in the directory
    AvailablePlugins := ScanPlugins()

    if (A_Args.Length > 0) {
        ; Headless / CLI Mode
        Headless := true
        DllCall("AttachConsole", "int", -1)

        ; Parse Arguments
        allPluginsOpt := false
        specifiedPlugins := []

        idx := 1
        while (idx <= A_Args.Length) {
            arg := A_Args[idx]
            if (arg = "--headless") {
                ; Already handled
            } else if (arg = "--all") {
                allPluginsOpt := true
            } else if (SubStr(arg, 1, 10) = "--plugins=") {
                pluginsStr := SubStr(arg, 11)
                Loop parse, pluginsStr, "," {
                    if (A_LoopField != "") {
                        specifiedPlugins.Push(Trim(A_LoopField))
                    }
                }
            } else if (arg = "--engine-only") {
                CompileWorkbench := false
                CompileUi := false
                CompileTests := false
            } else if (arg = "--run-tests") {
                CompileTests := true
            } else if (arg = "--no-tests") {
                CompileTests := false
            } else if (arg = "--verbose") {
                Verbose := true
            } else if (arg = "--clean") {
                CleanBuild := true
            } else if (arg = "--no-clean") {
                CleanBuild := false
            } else if (arg = "--help" || arg = "-h") {
                ShowHelp()
                ExitApp(0)
            }
            idx++
        }

        ; Resolve plugin list
        if (allPluginsOpt) {
            SelectedPluginsList := AvailablePlugins
        } else if (specifiedPlugins.Length > 0) {
            for p in specifiedPlugins {
                ; Normalize names: ensure they end in .cs
                pName := p
                if (!RegExMatch(pName, "\.cs$")) {
                    pName .= ".cs"
                }

                ; Check if it exists in scanned plugins
                found := false
                for av in AvailablePlugins {
                    if (av = pName) {
                        SelectedPluginsList.Push(pName)
                        found := true
                        break
                    }
                }
                if (!found) {
                    Stdout("WARNING: Specified plugin file '" pName "' was not found in plugins folder.`n")
                }
            }
        } else {
            ; Default to compiling all plugins in headless mode if not specified
            SelectedPluginsList := AvailablePlugins
        }

        ; Enforce LogicShakerPlugin dependency
        hasShaker := false
        hasTransform := false
        for p in SelectedPluginsList {
            if (p = "LogicShakerPlugin.cs")
                hasShaker := true
            if (p = "TransformAndFormatPlugins.cs")
                hasTransform := true
        }
        if (hasTransform && !hasShaker) {
            SelectedPluginsList.Push("LogicShakerPlugin.cs")
            Stdout("INFO: Auto-including LogicShakerPlugin.cs (required by TransformAndFormatPlugins.cs)`n")
        }

        success := RunCompilationSequence()
        ExitApp(success ? 0 : 1)
    } else {
        ; GUI Mode
        CreateBuildGUI()
    }
}

ShowHelp() {
    helpText := "`n"
        . "=========================================================`n"
        . "AHK# AST Workbench Builder (AutoHotkey v2)`n"
        . "=========================================================`n"
        . "Usage: AutoHotkey.exe build.ahk [options]`n`n"
        . "Options:`n"
        . "  --headless              Run in CLI (headless) mode`n"
        . "  --all                   Select and build all discovered plugins (default)`n"
        . "  --plugins=File1,File2   Comma-separated list of plugins to include`n"
        . "  --engine-only           Build only AstEngine.dll (skips UI/workbench/tests)`n"
        . "  --run-tests             Compile and run verification tests (default)`n"
        . "  --no-tests              Skip compiling/running verification tests`n"
        . "  --clean                 Force delete old DLLs/EXEs before compile (default)`n"
        . "  --no-clean              Skip clean phase`n"
        . "  --verbose               Print full compiler command lines`n"
        . "  --help, -h              Show this help menu`n"
        . "=========================================================`n"
    Stdout(helpText)
}

; --- Plugin Directory Scanning ---
ScanPlugins() {
    plugins := []
    Loop Files, SrcDir "\plugins\*.cs" {
        name := A_LoopFileName
        if (CorePlugins.Has(name)) {
            continue
        }
        plugins.Push(name)
    }
    return plugins
}

; --- Terminal Output Helper ---
Stdout(text) {
    try {
        FileAppend(text, "*")
    }
    try {
        FileAppend(text, "build_ahk.log", "UTF-8")
    }
}

; --- General Logger ---
LogMsg(msg) {
    global LogConsole, Headless
    if (Headless) {
        Stdout(msg)
    } else {
        LogConsole.Value := LogConsole.Value . msg
        ; Scroll to bottom: WM_VSCROLL = 0x115, SB_BOTTOM = 7
        PostMessage(0x115, 7, 0, LogConsole.Hwnd)
    }
}

; --- Command Execution helper ---
RunCommand(cmd, &output) {
    tempFile := A_Temp "\ahk_ast_build_" A_TickCount "_" Random(1, 1000) ".tmp"
    cmdLine := A_ComSpec ' /c "' cmd ' > "' tempFile '" 2>&1"'
    exitCode := RunWait(cmdLine, , "Hide")
    if FileExist(tempFile) {
        try {
            output := FileRead(tempFile)
            FileDelete(tempFile)
        } catch {
            output := ""
        }
    } else {
        output := ""
    }
    return exitCode
}

; --- Dynamic csc.exe Locator ---
LocateCsc() {
    windir := EnvGet("SystemRoot")
    if (windir = "")
        windir := "C:\Windows"

    cscPath := windir "\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if !FileExist(cscPath) {
        cscPath := windir "\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    }
    if !FileExist(cscPath) {
        throw Error("FATAL: csc.exe not found. .NET Framework 4.x is required.")
    }
    return cscPath
}

EnsureVendorDependencies() {
    global Verbose
    
    vendorDir := ProjectDir "\vendor"
    if (!DirExist(vendorDir)) {
        DirCreate(vendorDir)
    }
    
    dependencies := [
        { file: "DiffPlex.dll", package: "DiffPlex", version: "1.7.0" },
        { file: "FastColoredTextBox.dll", package: "FCTB", version: "2.16.24" },
        { file: "WeifenLuo.WinFormsUI.Docking.dll", package: "DockPanelSuite", version: "3.0.6" },
        { file: "WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll", package: "DockPanelSuite.ThemeVS2015", version: "3.0.6" }
    ]
    
    missingCount := 0
    for dep in dependencies {
        if (!FileExist(vendorDir "\" dep.file)) {
            missingCount++
        }
    }
    
    if (missingCount = 0) {
        return true
    }
    
    LogMsg("Missing " missingCount " vendor dependencies. Commencing auto-download...`n")
    
    for dep in dependencies {
        targetPath := vendorDir "\" dep.file
        if (FileExist(targetPath)) {
            continue
        }
        
        LogMsg("Downloading " dep.package " version " dep.version " from NuGet...`n")
        
        zipPath := A_Temp "\" dep.package ".zip"
        extractPath := A_Temp "\" dep.package
        
        ; Download package
        url := "https://globalcdn.nuget.org/packages/" StrLower(dep.package) "." dep.version ".nupkg"
        try {
            if (FileExist(zipPath)) {
                FileDelete(zipPath)
            }
            Download(url, zipPath)
        } catch as err {
            LogMsg("❌ Failed to download " dep.package " from NuGet: " err.Message "`n")
            return false
        }
        
        ; Extract package
        LogMsg("Extracting " dep.file " from package...`n")
        try {
            if (DirExist(extractPath)) {
                DirDelete(extractPath, true)
            }
            DirCreate(extractPath)
            Unzip(zipPath, extractPath)
        } catch as err {
            LogMsg("❌ Failed to extract " dep.package ": " err.Message "`n")
            try FileDelete(zipPath)
            return false
        }
        
        ; Wait for DLL to appear due to async CopyHere
        timeout := A_TickCount + 10000
        foundPath := ""
        while (A_TickCount < timeout) {
            foundPath := FindFileRecursive(extractPath, dep.file)
            if (foundPath != "") {
                break
            }
            Sleep(100)
        }
        
        if (foundPath != "") {
            try {
                FileCopy(foundPath, targetPath, true)
                LogMsg("✓ Successfully installed " dep.file "`n")
            } catch as err {
                LogMsg("❌ Failed to copy " dep.file " to vendor folder: " err.Message "`n")
                CleanUpTemp(zipPath, extractPath)
                return false
            }
        } else {
            LogMsg("❌ Could not find " dep.file " inside the extracted NuGet package.`n")
            CleanUpTemp(zipPath, extractPath)
            return false
        }
        
        CleanUpTemp(zipPath, extractPath)
    }
    
    return true
}

Unzip(ZipFile, DestFolder) {
    sh := ComObject("Shell.Application")
    zip := sh.NameSpace(ZipFile)
    if (!zip) {
        throw Error("Unable to open zip namespace: " ZipFile)
    }
    dest := sh.NameSpace(DestFolder)
    if (!dest) {
        throw Error("Unable to open destination namespace: " DestFolder)
    }
    dest.CopyHere(zip.Items, 4|16)
}

FindFileRecursive(dir, targetName) {
    Loop Files, dir "\*", "R" {
        if (A_LoopFileName = targetName) {
            return A_LoopFileFullPath
        }
    }
    return ""
}

CleanUpTemp(zipPath, extractPath) {
    try {
        if (FileExist(zipPath)) {
            FileDelete(zipPath)
        }
        if (DirExist(extractPath)) {
            DirDelete(extractPath, true)
        }
    }
}

; --- Core Compilation Orchestrator ---
RunCompilationSequence() {
    global Verbose, CleanBuild, CompileUi, CompileEngine, CompileWorkbench, CompileTests, SelectedPluginsList

    LogMsg("`n=== Starting AHK# AST Build Sequence ===`n")

    ; Release file locks by terminating running instances
    if ProcessExist("AstWorkbench.exe") {
        LogMsg("Terminating running AstWorkbench.exe to release file locks...`n")
        ProcessClose("AstWorkbench.exe")
        Sleep(500)
    }
    if ProcessExist("VerifyTest.exe") {
        LogMsg("Terminating running VerifyTest.exe...`n")
        ProcessClose("VerifyTest.exe")
        Sleep(500)
    }
    
    ; Ensure vendor files are present or downloaded
    if (!EnsureVendorDependencies()) {
        LogMsg("❌ Build failed due to missing dependencies.`n")
        return false
    }

    ; Check if required plugins for tests are present
    hasShaker := false
    hasTransform := false
    hasEval := false
    for p in SelectedPluginsList {
        if (p = "LogicShakerPlugin.cs")
            hasShaker := true
        if (p = "TransformAndFormatPlugins.cs")
            hasTransform := true
        if (p = "EvalAnalysisPlugin.cs")
            hasEval := true
    }

    actualCompileTests := CompileTests
    if (CompileTests && !(hasShaker && hasTransform && hasEval)) {
        LogMsg("⚠️ Verification tests require LogicShakerPlugin, TransformAndFormatPlugins, and EvalAnalysisPlugin. Skipping test stage.`n")
        actualCompileTests := false
    }

    ; 1. Locate Compiler
    cscPath := ""
    try {
        cscPath := LocateCsc()
        LogMsg("Found C# Compiler: " cscPath "`n")
    } catch as err {
        LogMsg("ERROR: " err.Message "`n")
        if (!Headless) {
            MsgBox(err.Message, "Build Error", 16)
        }
        return false
    }

    ; Ensure build directory exists
    if (!DirExist(BuildDir)) {
        DirCreate(BuildDir)
    }

    ; 2. Clean Phase
    if (CleanBuild) {
        LogMsg("Cleaning previous build artifacts...`n")
        try FileDelete(BuildDir "\AstEngine.dll")
        try FileDelete(BuildDir "\AstWorkbench.exe")

        ; Remove stray vendor DLLs in build
        try FileDelete(BuildDir "\FastColoredTextBox.dll")
        try FileDelete(BuildDir "\WeifenLuo.WinFormsUI.Docking.dll")
        try FileDelete(BuildDir "\WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll")
        try FileDelete(BuildDir "\DiffPlex.dll")
        LogMsg("Clean completed successfully.`n")
    }

    ; Setup directories / libraries references
    SplitPath(cscPath, , &fxDir)
    wpfDir := fxDir "\WPF"

    libArgs := '/lib:"' fxDir '" /lib:"' wpfDir '" '

    refs := [
        "System.dll", "System.Core.dll", "Microsoft.CSharp.dll",
        "System.Data.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
        "System.Xml.dll", "System.Xml.Linq.dll", "System.Web.Extensions.dll",
        "System.Design.dll"
    ]
    refPaths := [
        ProjectDir "\vendor\FastColoredTextBox.dll",
        ProjectDir "\vendor\WeifenLuo.WinFormsUI.Docking.dll",
        ProjectDir "\vendor\WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll",
        ProjectDir "\vendor\DiffPlex.dll"
    ]

    refArgs := ""
    for r in refs {
        refArgs .= '/reference:"' r '" '
    }
    for p in refPaths {
        refArgs .= '/reference:"' p '" '
    }

    ; No longer compiling separate ui.dll — vendor DLL resources will be bundled directly into AstWorkbench.exe.

    ; 4. Compile Engine DLL (Stage 1)
    if (CompileEngine) {
        LogMsg("--- Stage 1: Compiling AST Engine (AstEngine.dll) ---`n")

        engineFiles := []
        ; Gather AST files
        Loop Files, SrcDir "\ast\*.cs" {
            engineFiles.Push(A_LoopFileFullPath)
        }
        ; Gather JIT files
        Loop Files, SrcDir "\jit\*.cs" {
            engineFiles.Push(A_LoopFileFullPath)
        }
        ; Gather Core Plugins
        engineFiles.Push(SrcDir "\plugins\AhkStringHelper.cs")
        engineFiles.Push(SrcDir "\plugins\FlowDefinition.cs")
        engineFiles.Push(SrcDir "\plugins\PipelineMeta.cs")

        ; Append selected optional plugins
        LogMsg("Including " SelectedPluginsList.Length " optional plugins:`n")
        for pFile in SelectedPluginsList {
            engineFiles.Push(SrcDir "\plugins\" pFile)
            LogMsg("  + " pFile "`n")
        }

        sourceArgs := ""
        for f in engineFiles {
            sourceArgs .= '"' f '" '
        }

        outEngineDll := BuildDir "\AstEngine.dll"
        cmd := '"' cscPath '" /nologo /target:library /optimize+ /unsafe /out:"' outEngineDll '" ' libArgs refArgs sourceArgs
        if (Verbose) {
            LogMsg("Command: " cmd "`n")
        }

        exitCode := RunCommand(cmd, &output)
        if (exitCode != 0) {
            LogMsg("❌ AST Engine Compilation FAILED:`n" output "`n")
            return false
        }

        dllSize := FileGetSize(outEngineDll) / 1024
        LogMsg("✓ Compiled AstEngine.dll successfully (" Round(dllSize, 1) " KB).`n")
    }

    ; 5. Compile Workbench EXE (Stage 2)
    if (CompileWorkbench) {
        LogMsg("--- Stage 2: Compiling Workbench UI (AstWorkbench.exe) ---`n")

        workbenchFiles := []
        workbenchFiles.Push(SrcDir "\AstWorkbench.cs")
        Loop Files, SrcDir "\ui\*.cs" {
            workbenchFiles.Push(A_LoopFileFullPath)
        }

        workbenchArgs := ""
        for f in workbenchFiles {
            workbenchArgs .= '"' f '" '
        }

        resources := [
            "/resource:`"" ProjectDir "\vendor\DiffPlex.dll`",ui.resources.DiffPlex.dll",
            "/resource:`"" ProjectDir "\vendor\FastColoredTextBox.dll`",ui.resources.FastColoredTextBox.dll",
            "/resource:`"" ProjectDir "\vendor\WeifenLuo.WinFormsUI.Docking.dll`",ui.resources.WeifenLuo.WinFormsUI.Docking.dll",
            "/resource:`"" ProjectDir "\vendor\WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll`",ui.resources.WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll"
        ]

        resourceArgs := ""
        for res in resources {
            resourceArgs .= res " "
        }

        outWorkbenchExe := BuildDir "\AstWorkbench.exe"
        manifestFile := ProjectDir "\app.manifest"

        cmd := '"' cscPath '" /nologo /target:winexe /optimize+ /unsafe /out:"' outWorkbenchExe '" /win32manifest:"' manifestFile '" /win32icon:"' ProjectDir '\icon.ico" ' libArgs '/reference:"' BuildDir '\AstEngine.dll" ' refArgs resourceArgs workbenchArgs
        if (Verbose) {
            LogMsg("Command: " cmd "`n")
        }

        exitCode := RunCommand(cmd, &output)
        if (exitCode != 0) {
            LogMsg("❌ Workbench Compilation FAILED:`n" output "`n")
            return false
        }

        exeSize := FileGetSize(outWorkbenchExe) / 1024
        LogMsg("✓ Compiled AstWorkbench.exe successfully (" Round(exeSize, 1) " KB).`n")
    }

    ; 6. Compile and Run Verification Tests (Stage 3)
    if (actualCompileTests) {
        LogMsg("--- Stage 3: Compiling Verification Tests (VerifyTest.exe) ---`n")

        testCs := TestDir "\VerifyTest.cs"
        outTestExe := TestDir "\VerifyTest.exe"
        refDll := BuildDir "\AstEngine.dll"

        cmd := '"' cscPath '" /nologo /out:"' outTestExe '" /reference:"' refDll '" /reference:System.dll /reference:System.Core.dll /reference:Microsoft.CSharp.dll "' testCs '"'
        if (Verbose) {
            LogMsg("Command: " cmd "`n")
        }

        exitCode := RunCommand(cmd, &output)
        if (exitCode != 0) {
            LogMsg("❌ Verification Tests Compilation FAILED:`n" output "`n")
            return false
        }
        LogMsg("✓ Verification tests compiled successfully.`n")

        LogMsg("Running verification test suite...`n")
        EnvSet("AHK2AST_HEADLESS", "true")

        testRunCmd := '"' outTestExe '"'
        testExitCode := RunCommand(testRunCmd, &testOutput)
        EnvSet("AHK2AST_HEADLESS", "")

        ; Remove test exe
        try FileDelete(outTestExe)

        LogMsg("--- Verification Test Results ---`n" testOutput)

        if (testExitCode != 0) {
            LogMsg("❌ Verification test suite FAILED (exit code: " testExitCode ")`n")
            return false
        }
        LogMsg("✓ Verification tests PASSED successfully.`n")
    }

    LogMsg("`n🎉 BUILD COMPLETED SUCCESSFULLY! 🎉`n")
    return true
}

; --- Modern Styling: Dark Window Frame ---
SetWindowDarkTheme(hwnd) {
    if (VerCompare(A_OSVersion, "10.0.17763") >= 0) {
        attr := (VerCompare(A_OSVersion, "10.0.18985") >= 0) ? 20 : 19
        DllCall("dwmapi\DwmSetWindowAttribute", "ptr", hwnd, "uint", attr, "int*", true, "uint", 4)
    }
}

; --- GUI Definition and Layout ---
CreateBuildGUI() {
    global AvailablePlugins, PluginSizes, LogConsole

    myGui := Gui("-MinimizeBox -MaximizeBox", "AHK# AST Workbench Builder")
    myGui.BackColor := "1a1a22"
    SetWindowDarkTheme(myGui.Hwnd)

    ; Set window icon
    if FileExist(ProjectDir "\icon.ico") {
        hIcon := DllCall("LoadImage", "ptr", 0, "str", ProjectDir "\icon.ico", "uint", 1, "int", 0, "int", 0, "uint", 0x10, "ptr")
        if hIcon {
            DllCall("SendMessage", "ptr", myGui.Hwnd, "uint", 0x80, "ptr", 0, "ptr", hIcon) ; WM_SETICON (small)
            DllCall("SendMessage", "ptr", myGui.Hwnd, "uint", 0x80, "ptr", 1, "ptr", hIcon) ; WM_SETICON (large)
        }
    }

    ; App Title
    myGui.SetFont("s16 bold cWhite", "Segoe UI")
    myGui.Add("Text", "x20 y15", "AHK# AST BUILDER")
    myGui.SetFont("s9 c8e8e9f")
    myGui.Add("Text", "x20 y+2", "Configure and compile a modular AstEngine assembly")

    ; Column 1: Plugins (x20)
    myGui.SetFont("s11 bold cWhite")
    myGui.Add("Text", "x20 y75", "Plugins to Include")
    myGui.SetFont("s10 cWhite")

    global CheckboxCtrls := []
    for idx, pluginFile in AvailablePlugins {
        sizeStr := PluginSizes.Has(pluginFile) ? PluginSizes[pluginFile] : "0 KB"
        displayName := StrReplace(pluginFile, ".cs")

        opt := (idx = 1) ? "x20 y+12" : "x20 y+8"
        chk := myGui.Add("Checkbox", opt " Checked cWhite", displayName " (+" sizeStr ")")
        chk.pluginFile := pluginFile
        chk.OnEvent("Click", OnPluginCheckboxClick)
        CheckboxCtrls.Push(chk)
    }

    ; Column 2: Build Options (x340)
    myGui.SetFont("s11 bold cWhite")
    myGui.Add("Text", "x340 y75", "Build Steps")
    myGui.SetFont("s10 cWhite")

    global GuiChkClean := myGui.Add("Checkbox", "x340 y+12 cWhite", "Clean Build Directory")
    global GuiChkEngine := myGui.Add("Checkbox", "x340 y+8 Checked cWhite", "Build Engine (AstEngine.dll)")
    global GuiChkWorkbench := myGui.Add("Checkbox", "x340 y+8 Checked cWhite", "Build Workbench (AstWorkbench.exe)")
    global GuiChkTests := myGui.Add("Checkbox", "x340 y+8 Checked cWhite", "Run Verification Tests")

    ; Calculate dynamic positioning based on the number of checkboxes to prevent overlapping
    lastChkY := 200
    if CheckboxCtrls.Length > 0 {
        CheckboxCtrls[CheckboxCtrls.Length].GetPos(, &cY, , &cH)
        lastChkY := cY + cH
    }
    GuiChkTests.GetPos(, &bY, , &bH)
    lastBtnY := bY + bH
    maxBottomY := Max(lastChkY, lastBtnY)

    ; Compile Button
    btnY := maxBottomY + 20
    global GuiBtnCompile := myGui.Add("Button", "x20 y" btnY " w580 h40 Background4f46e5 cWhite", "🔨 Compile Build")
    GuiBtnCompile.OnEvent("Click", OnCompileClick)

    ; Output Console
    lblY := btnY + 60
    myGui.SetFont("s11 bold cWhite")
    myGui.Add("Text", "x20 y" lblY, "Build Output Log")

    editY := lblY + 25
    myGui.SetFont("s9", "Consolas")
    LogConsole := myGui.Add("Edit", "x20 y" editY " w580 h180 Multi ReadOnly Background0d0d12 c00FF66", "")

    ; Set Close Event
    myGui.OnEvent("Close", (*) => ExitApp())

    guiHeight := editY + 200
    myGui.Show("w620 h" guiHeight)
}

; --- GUI Events ---
OnCompileClick(*) {
    global CheckboxCtrls, SelectedPluginsList
    global CleanBuild, CompileUi, CompileEngine, CompileWorkbench, CompileTests
    global GuiChkClean, GuiChkUi, GuiChkEngine, GuiChkWorkbench, GuiChkTests, LogConsole, GuiBtnCompile

    ; Disable controls during compilation
    GuiBtnCompile.Enabled := false
    LogConsole.Value := ""

    ; Gather options from checkboxes
    CleanBuild := GuiChkClean.Value
    CompileEngine := GuiChkEngine.Value
    CompileWorkbench := GuiChkWorkbench.Value
    CompileTests := GuiChkTests.Value

    SelectedPluginsList := []
    for chk in CheckboxCtrls {
        if (chk.Value) {
            SelectedPluginsList.Push(chk.pluginFile)
        }
    }

    ; Run build in a pseudo-synchronous way
    SetTimer(PerformBuildAsync, -10)
}

PerformBuildAsync() {
    global GuiBtnCompile
    RunCompilationSequence()
    GuiBtnCompile.Enabled := true
}

OnPluginCheckboxClick(ctrl, info) {
    global CheckboxCtrls
    
    chkShaker := ""
    chkTransform := ""
    for chk in CheckboxCtrls {
        if (chk.pluginFile = "LogicShakerPlugin.cs")
            chkShaker := chk
        if (chk.pluginFile = "TransformAndFormatPlugins.cs")
            chkTransform := chk
    }

    if (chkShaker && chkTransform) {
        if (ctrl.pluginFile = "TransformAndFormatPlugins.cs" && chkTransform.Value == 1) {
            chkShaker.Value := 1
        }
        if (ctrl.pluginFile = "LogicShakerPlugin.cs" && chkShaker.Value == 0 && chkTransform.Value == 1) {
            chkShaker.Value := 1
            ToolTip("LogicShakerPlugin is required by TransformAndFormatPlugins")
            SetTimer(() => ToolTip(), -2000)
        }
    }
}