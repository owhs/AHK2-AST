using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AHK2AST.Plugins
{
    public enum AnalysisExportType
    {
        None,
        Json,
        Markdown,
        Html
    }

    public class EvalAnalysisConfig
    {
        [Category("Export"), DisplayName("Export Type"), Description("Optional format to write the static analysis report.")]
        public AnalysisExportType ExportType { get; set; }

        [Category("Export"), DisplayName("Output Path"), Description("Target file path to write the analysis report (defaults to analysis_report.{ext}).")]
        public string OutputPath { get; set; }

        [Category("Export"), DisplayName("Launch Browser"), Description("Automatically open the interactive HTML static analysis report in your default browser.")]
        public bool LaunchBrowser { get; set; }

        [Category("Caching"), DisplayName("Cache Includes"), Description("Enable include file caching to speed up parsing and verification.")]
        public bool CacheIncludes { get; set; }

        [Category("Caching"), DisplayName("Enable File Watchers"), Description("Use directory/file watchers to automatically invalidate cached files when edited.")]
        public bool EnableWatchers { get; set; }

        [Category("Rules"), DisplayName("Check Undefined Variables"), Description("Detect variable usage without any prior definition, local assignment, or global linkage.")]
        public bool CheckUndefinedVariables { get; set; }

        [Category("Rules"), DisplayName("Check Unused Symbols"), Description("Detect variables, parameters, or functions that are declared but never read.")]
        public bool CheckUnusedSymbols { get; set; }

        [Category("Rules"), DisplayName("Check Division By Zero"), Description("Detect divisions where the divisor statically evaluates to zero.")]
        public bool CheckDivZero { get; set; }

        [Category("Rules"), DisplayName("Check Assignments In Conditions"), Description("Detect variable assignments used in branch conditions without parenthesization.")]
        public bool CheckAssignmentsInConditions { get; set; }

        [Category("Rules"), DisplayName("Check Mismatched Arguments"), Description("Detect function calls passing incorrect argument counts to user functions/methods.")]
        public bool CheckMismatchedArgs { get; set; }

        [Category("Rules"), DisplayName("Check Return Outside Function"), Description("Detect return statements placed outside of functions or methods.")]
        public bool CheckReturnOutsideFunction { get; set; }

        [Category("Rules"), DisplayName("Check Duplicate Declarations"), Description("Detect multiple declarations of functions or classes with the same name in the same scope.")]
        public bool CheckDuplicateDeclarations { get; set; }

        [Category("Rules"), DisplayName("Check Constant Conditions"), Description("Detect conditional branches that statically evaluate to constant booleans.")]
        public bool CheckConstantConditions { get; set; }

        public EvalAnalysisConfig()
        {
            ExportType = AnalysisExportType.None;
            OutputPath = "";
            LaunchBrowser = false;
            CacheIncludes = true;
            EnableWatchers = false;

            CheckUndefinedVariables = true;
            CheckUnusedSymbols = true;
            CheckDivZero = true;
            CheckAssignmentsInConditions = true;
            CheckMismatchedArgs = true;
            CheckReturnOutsideFunction = true;
            CheckDuplicateDeclarations = true;
            CheckConstantConditions = true;
        }
    }

    public class AnalysisIssue
    {
        public string Severity { get; set; } // "Error" or "Warning"
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string FilePath { get; set; }
        public string RuleId { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] {1} ({2}:{3}) - {4} [{5}]", Severity, FilePath, Line, Column, Message, RuleId);
        }
    }

    public class SymbolScope
    {
        public SymbolScope Parent { get; set; }
        public Dictionary<string, SymbolInfo> Symbols { get; private set; }
        public bool IsFunction { get; set; }

        public SymbolScope(SymbolScope parent, bool isFunction = false)
        {
            Parent = parent;
            Symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
            IsFunction = isFunction;
        }

        public void Declare(string name, SymbolKind kind, int line, int col)
        {
            if (!Symbols.ContainsKey(name))
            {
                Symbols[name] = new SymbolInfo(name, kind, line, col);
            }
        }

        public SymbolInfo Lookup(string name)
        {
            SymbolInfo sym;
            if (Symbols.TryGetValue(name, out sym))
                return sym;
            if (Parent != null)
                return Parent.Lookup(name);
            return null;
        }
    }

    public enum SymbolKind
    {
        Variable,
        Parameter,
        Function,
        Class
    }

    public class SymbolInfo
    {
        public string Name { get; set; }
        public SymbolKind Kind { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public int ReadCount { get; set; }
        public int WriteCount { get; set; }

        public SymbolInfo(string name, SymbolKind kind, int line, int col)
        {
            Name = name;
            Kind = kind;
            Line = line;
            Column = col;
            ReadCount = 0;
            WriteCount = 0;
        }
    }

    public class FunctionSignature
    {
        public string Name { get; set; }
        public int MinArgs { get; set; }
        public int MaxArgs { get; set; }

        public FunctionSignature(string name, int min, int max)
        {
            Name = name;
            MinArgs = min;
            MaxArgs = max;
        }
    }

    public class EvalAnalysisPlugin : IFlowPlugin
    {
        public string Name { get { return "Analysis.Eval"; } }
        public string Target { get; set; }

        public string Category { get { return "Analysis"; } }
        public string StepTitle { get { return "Eval Analysis"; } }
        public string Icon { get { return "🔍"; } }
        public string Version { get { return "0.0.0.1"; } }
        public Type ConfigType { get { return typeof(EvalAnalysisConfig); } }
        public object GetConfig() { return Config; }
        public void SetConfig(object config) { Config = (EvalAnalysisConfig)config; }

        public EvalAnalysisConfig Config { get; set; }

        private List<AnalysisIssue> _issues = new List<AnalysisIssue>();
        private Dictionary<string, FunctionSignature> _userFunctions = new Dictionary<string, FunctionSignature>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _globalDeclarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Keywords
            "if", "else", "loop", "while", "for", "in", "switch", "case", "default", "try", "catch", "finally",
            "throw", "break", "continue", "return", "global", "local", "static", "class", "extends", "new",
            "as", "until", "unset", "and", "or", "not", "is", "true", "false", "this", "super",

            // AHK v2 Built-in Variables
            "A_AhkPath", "A_AhkVersion", "A_AppData", "A_AppDataCommon", "A_Args", "A_AllowAdminTemplates",
            "A_Clipboard", "A_ComputerName", "A_ComSpec", "A_ControlDelay", "A_CoordModeCaret", "A_CoordModeMenu",
            "A_CoordModeMouse", "A_CoordModePixel", "A_CoordModeToolTip", "A_Cursor", "A_DefaultMouseSpeed",
            "A_Desktop", "A_DesktopCommon", "A_DetectHiddenText", "A_DetectHiddenWindows", "A_EndChar",
            "A_EventInfo", "A_ExitReason", "A_FileEncoding", "A_HotkeyInterval", "A_HotkeyModifierTimeout",
            "A_Hour", "A_IconFile", "A_IconHidden", "A_IconNumber", "A_IconTip", "A_Index", "A_InitialWorkingDir",
            "A_IsAdmin", "A_IsCritical", "A_IsCompiled", "A_IsPaused", "A_IsSuspended", "A_IsUnicode",
            "A_KeyDelay", "A_KeyDelayPlay", "A_KeyDuration", "A_KeyDurationPlay", "A_Language", "A_LastError",
            "A_LineFile", "A_LineNumber", "A_ListLines", "A_LoopField", "A_LoopFileAttrib", "A_LoopFileDir",
            "A_LoopFileExt", "A_LoopFileFullPath", "A_LoopFileName", "A_LoopFileShortPath", "A_LoopFileSize",
            "A_LoopFileSizeKB", "A_LoopFileSizeMB", "A_LoopFileTimeAccessed", "A_LoopFileTimeCreated",
            "A_LoopFileTimeModified", "A_LoopReadLine", "A_LoopRegKey", "A_LoopRegName", "A_LoopRegTimeModified",
            "A_LoopRegType", "A_MaxHotkeysPerInterval", "A_MenuMaskKey", "A_Min", "A_Mon", "A_MouseDelay",
            "A_MouseDelayPlay", "A_MSec", "A_MyDocuments", "A_Now", "A_NowUTC", "A_OSVersion",
            "A_Paused", "A_PriorHotkey", "A_PriorKey", "A_Programs", "A_ProgramsCommon", "A_PtrSize",
            "A_RegView", "A_ScreenHeight", "A_ScreenWidth", "A_ScriptDir", "A_ScriptFullPath", "A_ScriptName",
            "A_Sec", "A_SendLevel", "A_SendMode", "A_StoreCapsLockMode", "A_Startup", "A_StartupCommon",
            "A_StartMenu", "A_StartMenuCommon", "A_Suspended", "A_SystemButtons", "A_Temp", "A_ThisForm",
            "A_ThisHotkey", "A_ThisLabel", "A_ThisMenu", "A_ThisMenuItem", "A_ThisMenuItemPos", "A_TickCount",
            "A_TimeIdle", "A_TimeIdlePhysical", "A_TimeIdleKeyboard", "A_TimeIdleMouse", "A_TimeSincePriorHotkey",
            "A_TimeSinceThisHotkey", "A_TitleMatchMode", "A_TitleMatchModeSpeed", "A_TrayMenu", "A_UserName",
            "A_WDAY", "A_WinDelay", "A_WinDir", "A_WorkingDir", "A_YDAY", "A_Year", "A_YWeek",

            // AHK v2 Built-in Functions
            "ControlAddItem", "ControlChooseIndex", "ControlChooseString", "ControlClick", "ControlDeleteItem",
            "ControlFindItem", "ControlFocus", "ControlGetChoice", "ControlGetClassNN", "ControlGetEnabled",
            "ControlGetFocus", "ControlGetHwnd", "ControlGetIndex", "ControlGetPos", "ControlGetStyle",
            "ControlGetExStyle", "ControlGetText", "ControlGetVisible", "ControlHide", "ControlHideDropDown",
            "ControlMove", "ControlSend", "ControlSendText", "ControlSetChecked", "ControlSetEnabled",
            "ControlSetStyle", "ControlSetExStyle", "ControlSetText", "ControlShow", "ControlShowDropDown",
            "MenuSelect", "MsgBox", "InputBox", "ToolTip", "TrayTip", "DirCopy", "DirCreate",
            "DirDelete", "DirExist", "DirMove", "DirSelect", "FileAppend", "FileCopy", "FileCreateShortcut",
            "FileDelete", "FileEncoding", "FileExist", "FileGetAttrib", "FileGetShortcut", "FileGetSize",
            "FileGetTime", "FileGetVersion", "FileInstall", "FileMove", "FileOpen", "FileRead",
            "FileRecycle", "FileRecycleEmpty", "FileSelect", "FileSetAttrib", "FileSetTime", "RegDelete",
            "RegDeleteKey", "RegWrite", "RegRead", "WinActivate", "WinActivateBottom", "WinActive",
            "WinClose", "WinExist", "WinGetClass", "WinGetClientPos", "WinGetControls", "WinGetControlsHwnd",
            "WinGetCount", "WinGetID", "WinGetIDLast", "WinGetList", "WinGetMinMax", "WinGetPID",
            "WinGetPos", "WinGetClientPos", "WinSetTitle", "WinSetAlwaysOnTop", "WinSetEnabled", "WinSetStyle",
            "WinSetExStyle", "WinSetRegion", "WinSetTransColor", "WinSetTransparent", "WinShow", "WinWait",
            "WinWaitActive", "WinWaitClose", "WinWaitNotActive", "ProcessClose", "ProcessExist", "ProcessGetName",
            "ProcessGetPath", "ProcessSetPriority", "ProcessWait", "ProcessWaitClose", "Abs", "Ceil",
            "Cos", "Exp", "Floor", "Log", "Ln", "Max", "Min", "Mod", "Random",
            "Round", "Sin", "Sqrt", "Tan", "ASin", "ACos", "ATan", "Chr",
            "Format", "InStr", "LoadPicture", "Ord", "RegExMatch", "RegExReplace", "StrCompare",
            "StrGet", "StrLen", "StrLower", "StrPtr", "StrPut", "StrReplace", "StrSplit",
            "StrUpper", "SubStr", "Trim", "LTrim", "RTrim", "Buffer", "CallbackCreate",
            "CallbackFree", "DllCall", "NumGet", "NumPut", "ObjBindMethod", "VarSetStrCapacity",
            "Array", "Map", "Object", "Class", "Func", "Struct", "Type", "Hotkey",
            "Hotstring", "HotIf", "HotIfWinActive", "HotIfWinExist", "HotIfWinNotActive", "HotIfWinNotExist",
            "ClipWait", "KeyWait", "MouseClick", "MouseClickDrag", "MouseGetPos", "MouseMove", "Send",
            "SendMode", "SendLevel", "SendMessage", "PostMessage", "OnMessage", "OnExit", "OnError",
            "SetTimer", "Sleep", "Shutdown", "Exit", "ExitApp", "Run", "RunWait",
            "EnvGet", "EnvSet", "SysGet", "SysGetIPAddresses", "MonitorGet", "MonitorGetCount",
            "MonitorGetName", "MonitorGetPrimary", "MonitorGetWorkArea", "SoundBeep", "SoundPlay",
            "SoundGetVolume", "SoundSetVolume", "SoundGetMute", "SoundSetMute", "ImageSearch", "PixelGetColor",
            "PixelSearch", "BlockInput", "CoordMode", "IniDelete", "IniWrite", "DirSelect", "FileSelect",
            "ControlGetHwnd", "GuiFromHwnd", "GuiCtrlFromHwnd", "Menu", "MenuBar", "InputHook",

            // Built-in Object Methods
            "HasMethod", "HasProp", "HasVal", "ObjHasOwnProp", "ObjOwnPropCount", "ObjOwnProps",
            "ObjAddRef", "ObjRelease", "ObjPtr", "ObjPtrAddRef", "ObjGetBase", "ObjSetBase",
            "WinGetProcessName", "WinGetProcessPath", "WinGetText", "WinGetTitle",

            // Extended built-in classes, functions, and exception classes to silence UndefinedVar warnings
            "Integer", "String", "Float", "Number", "IsObject", "IsSet", "SplitPath", "Gui", "Error", "ComCall",
            "ComValue", "ComObjArray", "ComObjValue", "IniRead", "FormatTime", "VarSetStrCapacity",
            "IsInteger", "IsNumber", "IsFloat", "IsString", "IsAlNum", "IsAlpha", "IsDigit", "IsSpace",
            "IsTime", "IsUpper", "IsLower", "IsXDigit", "DateAdd", "DateDiff", "ComObjQuery", "ComObjActive",
            "ComObject", "ComObjConnect", "ComObjCreate", "ComObjFlags", "ComObjGet", "ComObjType",
            "FileOpen", "FileExist", "DirExist", "IndexError", "MemberError", "PropertyError", "TypeError",
            "ValueError", "ZeroDivisionError", "OSError", "RegExError", "RegExMatchInfo", "Any", "ClipboardAll",
            "OutputDebug", "TraySetIcon", "GetKeyName", "GetKeyVK", "GetKeySC", "GetKeyState", "Sort", "SendInput",
            "SendPlay", "SendEvent", "WinKill", "WinMinimize", "WinMaximize", "WinRestore", "WinGetStyle", "WinGetExStyle",
            "Click", "XAML_DevTools_Instance", "Persistent"
        };

        private struct MinMax
        {
            public int Min;
            public int Max;
            public MinMax(int min, int max)
            {
                Min = min;
                Max = max;
            }
        }

        private static readonly Dictionary<string, MinMax> BuiltInFunctionSignatures = new Dictionary<string, MinMax>(StringComparer.OrdinalIgnoreCase)
        {
            { "Abs", new MinMax(1, 1) },
            { "Ceil", new MinMax(1, 1) },
            { "Cos", new MinMax(1, 1) },
            { "Exp", new MinMax(1, 1) },
            { "Floor", new MinMax(1, 1) },
            { "Log", new MinMax(1, 1) },
            { "Ln", new MinMax(1, 1) },
            { "Max", new MinMax(1, int.MaxValue) },
            { "Min", new MinMax(1, int.MaxValue) },
            { "Mod", new MinMax(2, 2) },
            { "Random", new MinMax(0, 2) },
            { "Round", new MinMax(1, 2) },
            { "Sin", new MinMax(1, 1) },
            { "Sqrt", new MinMax(1, 1) },
            { "Tan", new MinMax(1, 1) },
            { "ASin", new MinMax(1, 1) },
            { "ACos", new MinMax(1, 1) },
            { "ATan", new MinMax(1, 1) },
            { "Chr", new MinMax(1, 1) },
            { "Format", new MinMax(1, int.MaxValue) },
            { "InStr", new MinMax(2, 5) },
            { "LoadPicture", new MinMax(1, 3) },
            { "Ord", new MinMax(1, 1) },
            { "RegExMatch", new MinMax(2, 5) },
            { "RegExReplace", new MinMax(2, 6) },
            { "StrCompare", new MinMax(2, 3) },
            { "StrGet", new MinMax(1, 3) },
            { "StrLen", new MinMax(1, 1) },
            { "StrLower", new MinMax(1, 2) },
            { "StrPtr", new MinMax(1, 1) },
            { "StrPut", new MinMax(1, 3) },
            { "StrReplace", new MinMax(2, 6) },
            { "StrSplit", new MinMax(1, 4) },
            { "StrUpper", new MinMax(1, 2) },
            { "SubStr", new MinMax(1, 3) },
            { "Trim", new MinMax(1, 2) },
            { "LTrim", new MinMax(1, 2) },
            { "RTrim", new MinMax(1, 2) },
            { "CallbackCreate", new MinMax(1, 3) },
            { "CallbackFree", new MinMax(1, 1) },
            { "DllCall", new MinMax(1, int.MaxValue) },
            { "NumGet", new MinMax(2, 3) },
            { "NumPut", new MinMax(3, int.MaxValue) },
            { "ObjBindMethod", new MinMax(2, int.MaxValue) },
            { "VarSetStrCapacity", new MinMax(1, 2) },
            { "Hotkey", new MinMax(1, 3) },
            { "Hotstring", new MinMax(1, 3) },
            { "HotIf", new MinMax(0, 1) },
            { "HotIfWinActive", new MinMax(0, 2) },
            { "HotIfWinExist", new MinMax(0, 2) },
            { "HotIfWinNotActive", new MinMax(0, 2) },
            { "HotIfWinNotExist", new MinMax(0, 2) },
            { "ClipWait", new MinMax(0, 2) },
            { "KeyWait", new MinMax(1, 2) },
            { "MouseClick", new MinMax(0, 9) },
            { "MouseClickDrag", new MinMax(5, 7) },
            { "MouseGetPos", new MinMax(0, 5) },
            { "MouseMove", new MinMax(2, 4) },
            { "Send", new MinMax(1, 1) },
            { "SendMode", new MinMax(1, 1) },
            { "SendLevel", new MinMax(1, 1) },
            { "SendMessage", new MinMax(0, 8) },
            { "PostMessage", new MinMax(0, 7) },
            { "OnMessage", new MinMax(2, 3) },
            { "OnExit", new MinMax(1, 2) },
            { "OnError", new MinMax(1, 2) },
            { "SetTimer", new MinMax(0, 3) },
            { "Sleep", new MinMax(1, 1) },
            { "Shutdown", new MinMax(1, 1) },
            { "Exit", new MinMax(0, 1) },
            { "ExitApp", new MinMax(0, 1) },
            { "Run", new MinMax(1, 4) },
            { "RunWait", new MinMax(1, 4) },
            { "EnvGet", new MinMax(1, 1) },
            { "EnvSet", new MinMax(1, 2) },
            { "SysGet", new MinMax(1, 1) },
            { "SysGetIPAddresses", new MinMax(0, 0) },
            { "MonitorGet", new MinMax(0, 5) },
            { "MonitorGetCount", new MinMax(0, 0) },
            { "MonitorGetName", new MinMax(0, 1) },
            { "MonitorGetPrimary", new MinMax(0, 0) },
            { "MonitorGetWorkArea", new MinMax(0, 5) },
            { "SoundBeep", new MinMax(0, 2) },
            { "SoundPlay", new MinMax(1, 2) },
            { "SoundGetVolume", new MinMax(0, 2) },
            { "SoundSetVolume", new MinMax(1, 3) },
            { "SoundGetMute", new MinMax(0, 2) },
            { "SoundSetMute", new MinMax(1, 3) },
            { "ImageSearch", new MinMax(5, 7) },
            { "PixelGetColor", new MinMax(2, 3) },
            { "PixelSearch", new MinMax(5, 7) },
            { "BlockInput", new MinMax(1, 1) },
            { "CoordMode", new MinMax(1, 2) },
            { "DirCopy", new MinMax(2, 3) },
            { "DirCreate", new MinMax(1, 1) },
            { "DirDelete", new MinMax(1, 2) },
            { "DirExist", new MinMax(1, 1) },
            { "DirMove", new MinMax(2, 3) },
            { "DirSelect", new MinMax(0, 3) },
            { "FileAppend", new MinMax(1, 3) },
            { "FileCopy", new MinMax(2, 3) },
            { "FileCreateShortcut", new MinMax(2, 8) },
            { "FileDelete", new MinMax(1, 1) },
            { "FileExist", new MinMax(0, 1) },
            { "FileGetAttrib", new MinMax(0, 1) },
            { "FileGetShortcut", new MinMax(1, 7) },
            { "FileGetSize", new MinMax(0, 2) },
            { "FileGetTime", new MinMax(0, 2) },
            { "FileGetVersion", new MinMax(0, 1) },
            { "FileInstall", new MinMax(2, 3) },
            { "FileMove", new MinMax(2, 3) },
            { "FileOpen", new MinMax(2, 3) },
            { "FileRead", new MinMax(1, 2) },
            { "FileRecycle", new MinMax(1, 1) },
            { "FileRecycleEmpty", new MinMax(0, 1) },
            { "FileSelect", new MinMax(0, 4) },
            { "FileSetAttrib", new MinMax(1, 3) },
            { "FileSetTime", new MinMax(0, 4) },
            { "RegDelete", new MinMax(0, 3) },
            { "RegDeleteKey", new MinMax(0, 2) },
            { "RegWrite", new MinMax(1, 6) },
            { "RegRead", new MinMax(0, 3) },
            { "SplitPath", new MinMax(1, 5) },
            { "FormatTime", new MinMax(0, 2) },
            { "IsSet", new MinMax(1, 1) },
            { "IsObject", new MinMax(1, 1) },
            { "Integer", new MinMax(1, 1) },
            { "Float", new MinMax(1, 1) },
            { "String", new MinMax(1, 1) },
            { "Type", new MinMax(1, 1) },
            { "ComCall", new MinMax(2, int.MaxValue) },
            { "ComValue", new MinMax(2, 3) },
            { "ComObjArray", new MinMax(2, 8) },
            { "ComObjValue", new MinMax(1, 1) },
            { "Persistent", new MinMax(0, 1) }
        };

        public EvalAnalysisPlugin()
        {
            Config = new EvalAnalysisConfig();
        }

        public void Initialize(AhkAstEngine engine)
        {
        }

        public object Execute(AstNode root)
        {
            if (root == null) return null;

            // Apply watcher state
            AstFileCache.EnableWatchers = Config.EnableWatchers;
            if (!Config.CacheIncludes)
            {
                AstFileCache.Clear();
            }

            _issues.Clear();
            _userFunctions.Clear();
            _globalDeclarations.Clear();

            var globalScope = new SymbolScope(null);

            // Populate built-in functions in user functions list to prevent mismatched arg warnings on them
            // In a fuller implementation, we would register built-in min/max param constraints.

            // Pass 1: Collect declarations recursively across the program
            string mainFile = !string.IsNullOrEmpty(Target) ? Target : "Main Script";
            CollectDeclarations(root, globalScope, mainFile);

            // Pass 2: Main analysis run
            AnalyzeNode(root, globalScope, null, mainFile);

            // Pass 3: Check unused symbols
            if (Config.CheckUnusedSymbols)
            {
                CheckUnusedSymbolsInScope(globalScope, mainFile);
            }

            // Log summary metrics to PipelineLogger
            PipelineLogger.Log("  🔍 Analysis.Eval Summary:");
            int errorsCount = _issues.Count(i => i.Severity == "Error");
            int warningsCount = _issues.Count(i => i.Severity == "Warning");
            PipelineLogger.Log("    - Errors: {0}", errorsCount);
            PipelineLogger.Log("    - Warnings: {0}", warningsCount);
            foreach (var issue in _issues)
            {
                PipelineLogger.Log("    - [{0}] ({1}:{2}) {3} [{4}]", issue.Severity, issue.Line, issue.Column, issue.Message, issue.RuleId);
            }

            // Optional Export
            if (Config.ExportType != AnalysisExportType.None)
            {
                string report = ExportReport(errorsCount, warningsCount);
                if (report != null) return report;
            }

            return root;
        }

        private string ExportReport(int errors, int warnings)
        {
            string ext = Config.ExportType.ToString().ToLower();
            string path = Config.OutputPath;
            if (string.IsNullOrEmpty(path))
            {
                string folder = !string.IsNullOrEmpty(Target) ? Path.GetDirectoryName(Target) : (AppDomain.CurrentDomain.BaseDirectory ?? Path.GetTempPath());
                path = Path.Combine(folder, "analysis_report." + ext);
            }

            string reportContent = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (Config.ExportType == AnalysisExportType.Json)
                {
                    var serializer = new JavaScriptSerializer();
                    reportContent = serializer.Serialize(_issues);
                }
                else if (Config.ExportType == AnalysisExportType.Markdown)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("# AHK2 Static Analysis Report");
                    sb.AppendLine();
                    sb.AppendLine("## Summary");
                    sb.AppendLine(string.Format("- **Total Issues**: {0}", _issues.Count));
                    sb.AppendLine(string.Format("- **Errors**: {0}", errors));
                    sb.AppendLine(string.Format("- **Warnings**: {0}", warnings));
                    sb.AppendLine();
                    sb.AppendLine("## Detailed Issues");
                    sb.AppendLine("| Severity | Rule ID | File | Line:Col | Message |");
                    sb.AppendLine("| --- | --- | --- | --- | --- |");
                    foreach (var i in _issues)
                    {
                        sb.AppendLine(string.Format("| {0} | `{1}` | {2} | {3}:{4} | {5} |",
                            i.Severity, i.RuleId, Path.GetFileName(i.FilePath), i.Line, i.Column, i.Message.Replace("|", "\\|")));
                    }
                    reportContent = sb.ToString();
                }
                else if (Config.ExportType == AnalysisExportType.Html)
                {
                    reportContent = GenerateHtmlReport(_issues, errors, warnings);
                }

                if (!string.IsNullOrEmpty(path) && reportContent != null)
                {
                    File.WriteAllText(path, reportContent, Encoding.UTF8);

                    bool isHeadless = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AHK2AST_HEADLESS")) ||
                                      AppDomain.CurrentDomain.FriendlyName.IndexOf("VerifyTest", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (Config.ExportType == AnalysisExportType.Html && Config.LaunchBrowser && !isHeadless)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            PipelineLogger.Log("  ❌ ERROR: Failed to launch browser: {0}", ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PipelineLogger.Log("  ❌ ERROR: Failed to export analysis report: {0}", ex.Message);
            }
            return reportContent;
        }

        private void CollectInnerGlobals(AstNode node, SymbolScope globalScope)
        {
            if (node == null) return;
            if (node.NodeType == "Declaration" && node.Metadata == "global")
            {
                string name = node.Value;
                if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                {
                    globalScope.Declare(name, SymbolKind.Variable, node.Line, node.Column);
                    _globalDeclarations.Add(name);
                }
            }
            foreach (var child in node.ChildNodes)
            {
                if (child != null && child.NodeType != "Class" && child.NodeType != "Method")
                {
                    CollectInnerGlobals(child, globalScope);
                }
            }
        }

        private static bool IsAssignmentOperator(string op)
        {
            if (string.IsNullOrEmpty(op)) return false;
            return op == ":=" || op == "+=" || op == "-=" || op == "*=" || op == "/=" || op == "//=" || op == ".=" || op == "&=" || op == "|=" || op == "^=" || op == "<<=" || op == ">>=";
        }

        private void CollectDeclarations(AstNode node, SymbolScope scope, string currentFile)
        {
            if (node == null) return;

            string fileContext = currentFile;
            if (node.NodeType == "Include" && !string.IsNullOrEmpty(node.Value))
            {
                fileContext = node.Value;
            }

            if (node.NodeType == "Method")
            {
                string name = node.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    scope.Declare(name, SymbolKind.Function, node.Line, node.Column);
                    _globalDeclarations.Add(name);

                    // Parameter counts
                    int minArgs = 0;
                    int maxArgs = 0;
                    bool isVariadic = false;
                    var paramNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
                    if (paramNode != null)
                    {
                        foreach (var p in paramNode.ChildNodes)
                        {
                            if (p == null) continue;
                            string meta = p.Metadata ?? "";
                            if (meta.Contains("variadic"))
                            {
                                isVariadic = true;
                                break;
                            }
                            maxArgs++;
                            bool isOptional = meta.Contains("optional") || p.ChildCount > 0;
                            if (!isOptional)
                            {
                                minArgs = maxArgs;
                            }
                        }
                    }
                    if (isVariadic) maxArgs = int.MaxValue;

                    if (!_userFunctions.ContainsKey(name))
                    {
                        _userFunctions[name] = new FunctionSignature(name, minArgs, maxArgs);
                    }
                    else if (Config.CheckDuplicateDeclarations)
                    {
                        AddIssue("Warning", string.Format("Duplicate declaration of function '{0}'.", name), node.Line, node.Column, fileContext, "DuplicateDecl");
                    }
                }
                CollectInnerGlobals(node, scope);
                return;
            }
            else if (node.NodeType == "Class")
            {
                string name = node.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    scope.Declare(name, SymbolKind.Class, node.Line, node.Column);
                    _globalDeclarations.Add(name);

                    // Look for custom constructor __New inside class
                    var constructor = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Method" && c.Value.Equals("__New", StringComparison.OrdinalIgnoreCase));
                    if (constructor != null)
                    {
                        int minArgs = 0;
                        int maxArgs = 0;
                        bool isVariadic = false;
                        var paramNode = constructor.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
                        if (paramNode != null)
                        {
                            foreach (var p in paramNode.ChildNodes)
                            {
                                if (p == null) continue;
                                string meta = p.Metadata ?? "";
                                if (meta.Contains("variadic"))
                                {
                                    isVariadic = true;
                                    break;
                                }
                                maxArgs++;
                                bool isOptional = meta.Contains("optional") || p.ChildCount > 0;
                                if (!isOptional)
                                {
                                    minArgs = maxArgs;
                                }
                            }
                        }
                        if (isVariadic) maxArgs = int.MaxValue;

                        // Treat the class instantiation as a signature lookup using class name
                        _userFunctions[name] = new FunctionSignature(name, minArgs, maxArgs);
                    }
                    else
                    {
                        // Default constructor takes 0 arguments (or is unconstrained)
                        _userFunctions[name] = new FunctionSignature(name, 0, int.MaxValue);
                    }
                }
                return;
            }
            else if (node.NodeType == "Declaration")
            {
                string name = node.Value;
                if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                {
                    scope.Declare(name, SymbolKind.Variable, node.Line, node.Column);
                    _globalDeclarations.Add(name);
                }
            }
            else if (node.NodeType == "Assign" || node.NodeType == "ColonAssign" || node.NodeType == "StaticAssign" || (node.NodeType == "BinaryExpr" && node.Value != null && IsAssignmentOperator(node.Value)))
            {
                var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    string name = lhs.Value;
                    if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                    {
                        scope.Declare(name, SymbolKind.Variable, lhs.Line, lhs.Column);
                        _globalDeclarations.Add(name);
                    }
                }
            }
            else if (node.NodeType == "Call")
            {
                var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                var args = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (target != null && target.NodeType == "Identifier" && target.Value.Equals("IsSet", StringComparison.OrdinalIgnoreCase))
                {
                    if (args != null && args.ChildCount > 0)
                    {
                        var arg = args.GetChild(0);
                        if (arg != null && arg.NodeType == "Identifier")
                        {
                            string varName = arg.Value;
                            if (!string.IsNullOrEmpty(varName) && !ReservedNames.Contains(varName))
                            {
                                scope.Declare(varName, SymbolKind.Variable, arg.Line, arg.Column);
                                _globalDeclarations.Add(varName);
                            }
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                CollectDeclarations(child, scope, fileContext);
            }
        }

        private void AnalyzeNode(AstNode node, SymbolScope scope, SymbolScope currentFunctionScope, string currentFile)
        {
            if (node == null) return;

            string fileContext = currentFile;
            if (node.NodeType == "Include" && !string.IsNullOrEmpty(node.Value))
            {
                fileContext = node.Value;
            }

            // Check Return statement outside of function
            if (Config.CheckReturnOutsideFunction && node.NodeType == "Return")
            {
                if (currentFunctionScope == null)
                {
                    AddIssue("Error", "Return statement is placed outside of any function or method.", node.Line, node.Column, fileContext, "ReturnOutsideFunc");
                }
            }

            // Check Division by Zero
            if (Config.CheckDivZero && node.NodeType == "BinaryExpr" && (node.Value == "/" || node.Value == "//"))
            {
                var right = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (right != null)
                {
                    object val = EvaluateConstant(right);
                    if (val is double && (double)val == 0)
                    {
                        AddIssue("Warning", "Division by zero is detected statically.", node.Line, node.Column, fileContext, "DivZero");
                    }
                }
            }

            // Check Assignments inside conditions without parentheses
            if (Config.CheckAssignmentsInConditions && (node.NodeType == "If" || node.NodeType == "While"))
            {
                var cond = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (cond != null)
                {
                    if (cond.NodeType == "Assign" || cond.NodeType == "ColonAssign" || (cond.NodeType == "BinaryExpr" && cond.Value == ":="))
                    {
                        AddIssue("Warning", string.Format("Assignment in condition of {0}. Wrap in parentheses if intentional.", node.NodeType), cond.Line, cond.Column, fileContext, "AssignInCondition");
                    }
                }
            }

            // Check Constant conditions
            if (Config.CheckConstantConditions && (node.NodeType == "If" || node.NodeType == "While"))
            {
                var cond = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (cond != null && cond.NodeType != "Assign" && cond.NodeType != "ColonAssign" && !(cond.NodeType == "BinaryExpr" && cond.Value == ":="))
                {
                    object val = EvaluateConstant(cond);
                    if (val != null)
                    {
                        AddIssue("Warning", string.Format("Condition evaluates to a constant value '{0}'.", val.ToString().ToLower()), cond.Line, cond.Column, fileContext, "ConstantCondition");
                    }
                }
            }

            // Check Mismatched Call Arguments
            if (Config.CheckMismatchedArgs && node.NodeType == "Call")
            {
                var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                var args = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (target != null && target.NodeType == "Identifier" && args != null && args.NodeType == "Arguments")
                {
                    string funcName = target.Value;
                    int actualCount = args.ChildCount;
                    FunctionSignature sig;
                    if (_userFunctions.TryGetValue(funcName, out sig))
                    {
                        if (actualCount < sig.MinArgs || actualCount > sig.MaxArgs)
                        {
                            string rangeStr = sig.MaxArgs == int.MaxValue ? sig.MinArgs.ToString() + "+" : string.Format("{0}-{1}", sig.MinArgs, sig.MaxArgs);
                            AddIssue("Warning", string.Format("Function/Class '{0}' expects {1} arguments, but got {2}.", funcName, rangeStr, actualCount), node.Line, node.Column, fileContext, "MismatchedArgs");
                        }
                    }
                    else
                    {
                        MinMax biSig;
                        if (BuiltInFunctionSignatures.TryGetValue(funcName, out biSig))
                        {
                            if (actualCount < biSig.Min || actualCount > biSig.Max)
                            {
                                string rangeStr = biSig.Max == int.MaxValue ? biSig.Min.ToString() + "+" : string.Format("{0}-{1}", biSig.Min, biSig.Max);
                                AddIssue("Warning", string.Format("Built-in function '{0}' expects {1} arguments, but got {2}.", funcName, rangeStr, actualCount), node.Line, node.Column, fileContext, "MismatchedArgs");
                            }
                        }
                    }
                }
            }

            // Enter a new scope for class, function body, block, or lambda
            if (node.NodeType == "FatArrow")
            {
                var lambdaScope = new SymbolScope(scope, isFunction: true);

                var paramNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
                if (paramNode != null)
                {
                    foreach (var p in paramNode.ChildNodes)
                    {
                        if (p != null && !string.IsNullOrEmpty(p.Value) && p.Value != "*")
                        {
                            lambdaScope.Declare(p.Value, SymbolKind.Parameter, p.Line, p.Column);
                        }
                    }
                }

                var methodScope = currentFunctionScope;
                if (methodScope != null)
                {
                    if (methodScope.Lookup("this") != null)
                    {
                        lambdaScope.Declare("this", SymbolKind.Variable, node.Line, node.Column);
                    }
                    if (methodScope.Lookup("super") != null)
                    {
                        lambdaScope.Declare("super", SymbolKind.Variable, node.Line, node.Column);
                    }
                }

                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType != "Parameters")
                    {
                        CollectLocalDeclarations(child, lambdaScope);
                    }
                }

                foreach (var child in node.ChildNodes)
                {
                    AnalyzeNode(child, lambdaScope, lambdaScope, fileContext);
                }

                if (Config.CheckUnusedSymbols)
                {
                    CheckUnusedSymbolsInScope(lambdaScope, fileContext);
                }
                return;
            }

            if (node.NodeType == "Method")
            {
                var methodScope = new SymbolScope(scope, isFunction: true);

                // Declare parameters inside the function scope
                var paramNode = node.ChildNodes.FirstOrDefault(c => c != null && c.NodeType == "Parameters");
                if (paramNode != null)
                {
                    foreach (var p in paramNode.ChildNodes)
                    {
                        if (p != null && !string.IsNullOrEmpty(p.Value) && p.Value != "*")
                        {
                            methodScope.Declare(p.Value, SymbolKind.Parameter, p.Line, p.Column);
                        }
                    }
                }

                // If this is a property setter, declare the implicit 'value' parameter
                if (node.Value != null && node.Value.Equals("set", StringComparison.OrdinalIgnoreCase) && IsInsideProperty(node))
                {
                    methodScope.Declare("value", SymbolKind.Parameter, node.Line, node.Column);
                }

                // If inside a class definition, implicit 'this' and 'super' are variables
                var parentClass = FindParentClass(node);
                if (parentClass != null)
                {
                    methodScope.Declare("this", SymbolKind.Variable, node.Line, node.Column);
                    methodScope.Declare("super", SymbolKind.Variable, node.Line, node.Column);
                }

                // Pre-process any inner/nested declarations
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType != "Parameters")
                    {
                        CollectLocalDeclarations(child, methodScope);
                    }
                }

                // Analyze children in the method scope
                foreach (var child in node.ChildNodes)
                {
                    AnalyzeNode(child, methodScope, methodScope, fileContext);
                }

                // Warn on unused parameters/locals at function exit
                if (Config.CheckUnusedSymbols)
                {
                    CheckUnusedSymbolsInScope(methodScope, fileContext);
                }
                return;
            }

            if (node.NodeType == "Class")
            {
                var classScope = new SymbolScope(scope);
                // Class name itself is in parent, but static members can go here
                foreach (var child in node.ChildNodes)
                {
                    AnalyzeNode(child, classScope, currentFunctionScope, fileContext);
                }
                return;
            }

            // Parse writes & reads of variables
            if (node.NodeType == "Identifier")
            {
                string varName = node.Value;
                if (!string.IsNullOrEmpty(varName) && !ReservedNames.Contains(varName))
                {
                    // Skip dynamic references
                    if (varName.StartsWith("%") || varName.Contains("%"))
                    {
                        return;
                    }

                    bool isWrite = DetermineIfWriteContext(node);
                    bool isRead = DetermineIfReadContext(node);

                    SymbolInfo sym = scope.Lookup(varName);
                    if (sym == null)
                    {
                        // Check if it is declared globally
                        if (_globalDeclarations.Contains(varName))
                        {
                            // Statically resolved to a global variable/function
                        }
                        else if (isWrite)
                        {
                            // Assume-local creates local variable on assignment
                            scope.Declare(varName, SymbolKind.Variable, node.Line, node.Column);
                            sym = scope.Lookup(varName);
                        }
                        else if (Config.CheckUndefinedVariables && isRead)
                        {
                            AddIssue("Warning", string.Format("Usage of undeclared or undefined variable '{0}'.", varName), node.Line, node.Column, fileContext, "UndefinedVar");
                        }
                    }

                    if (sym != null)
                    {
                        if (isWrite) sym.WriteCount++;
                        if (isRead) sym.ReadCount++;
                    }
                }
            }

            foreach (var child in node.ChildNodes)
            {
                AnalyzeNode(child, scope, currentFunctionScope, fileContext);
            }
        }

        private void CollectLocalDeclarations(AstNode node, SymbolScope scope)
        {
            if (node == null) return;
            if (node.NodeType == "Method" && node.Parent != null && node.Parent.NodeType != "Class")
            {
                // This is a nested function declaration
                if (!string.IsNullOrEmpty(node.Value))
                {
                    scope.Declare(node.Value, SymbolKind.Function, node.Line, node.Column);
                }
                return; // Do not recurse into nested function body during local collection
            }

            if (node.NodeType == "Class" || node.NodeType == "FatArrow")
            {
                return; // Do not recurse into nested classes or lambdas during local collection
            }

            if (node.NodeType == "Declaration")
            {
                if (node.Metadata == "global") return;
                string name = node.Value;
                if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                {
                    scope.Declare(name, SymbolKind.Variable, node.Line, node.Column);
                }
            }
            else if (node.NodeType == "StaticAssign")
            {
                string name = node.Value;
                if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                {
                    scope.Declare(name, SymbolKind.Variable, node.Line, node.Column);
                }
            }
            else if (node.NodeType == "Assign" || node.NodeType == "ColonAssign" || (node.NodeType == "BinaryExpr" && node.Value != null && IsAssignmentOperator(node.Value)))
            {
                var lhs = node.ChildCount > 0 ? node.GetChild(0) : null;
                if (lhs != null && lhs.NodeType == "Identifier")
                {
                    string name = lhs.Value;
                    if (!string.IsNullOrEmpty(name) && !ReservedNames.Contains(name))
                    {
                        scope.Declare(name, SymbolKind.Variable, lhs.Line, lhs.Column);
                    }
                }
            }
            else if (node.NodeType == "Call")
            {
                var target = node.ChildCount > 0 ? node.GetChild(0) : null;
                var args = node.ChildCount > 1 ? node.GetChild(1) : null;
                if (target != null && target.NodeType == "Identifier" && target.Value.Equals("IsSet", StringComparison.OrdinalIgnoreCase))
                {
                    if (args != null && args.ChildCount > 0)
                    {
                        var arg = args.GetChild(0);
                        if (arg != null && arg.NodeType == "Identifier")
                        {
                            string varName = arg.Value;
                            if (!string.IsNullOrEmpty(varName) && !ReservedNames.Contains(varName))
                            {
                                scope.Declare(varName, SymbolKind.Variable, arg.Line, arg.Column);
                            }
                        }
                    }
                }
            }
            else if (node.NodeType == "ForVars")
            {
                foreach (var child in node.ChildNodes)
                {
                    if (child != null && child.NodeType == "Identifier" && !string.IsNullOrEmpty(child.Value) && !ReservedNames.Contains(child.Value))
                    {
                        scope.Declare(child.Value, SymbolKind.Variable, child.Line, child.Column);
                    }
                }
            }
            else if (node.NodeType == "Catch" && !string.IsNullOrEmpty(node.Metadata))
            {
                if (!ReservedNames.Contains(node.Metadata))
                {
                    scope.Declare(node.Metadata, SymbolKind.Variable, node.Line, node.Column);
                }
            }

            foreach (var child in node.ChildNodes)
            {
                if (child != null)
                {
                    CollectLocalDeclarations(child, scope);
                }
            }
        }

        private AstNode FindParentClass(AstNode node)
        {
            var p = node.Parent;
            while (p != null)
            {
                if (p.NodeType == "Class") return p;
                p = p.Parent;
            }
            return null;
        }

        private static bool IsInsideProperty(AstNode node)
        {
            var p = node.Parent;
            while (p != null)
            {
                if (p.NodeType == "Property") return true;
                if (p.NodeType == "Method" || p.NodeType == "Class") return false;
                p = p.Parent;
            }
            return false;
        }

        private bool DetermineIfWriteContext(AstNode node)
        {
            var parent = node.Parent;
            if (parent == null) return false;

            if (parent.NodeType == "KeyValue")
            {
                return false;
            }

            if (parent.NodeType == "Assign" || parent.NodeType == "ColonAssign" || parent.NodeType == "StaticAssign")
            {
                return parent.GetChild(0) == node;
            }
            if (parent.NodeType == "BinaryExpr")
            {
                string op = parent.Value;
                if (IsAssignmentOperator(op))
                {
                    return parent.GetChild(0) == node;
                }
            }
            if (parent.NodeType == "Declaration")
            {
                return parent.Value == node.Value;
            }
            if (parent.NodeType == "ForVars")
            {
                return true;
            }
            if (parent.NodeType == "Catch")
            {
                return parent.Metadata == node.Value;
            }
            if (parent.NodeType == "Parameter")
            {
                return parent.Value == node.Value;
            }
            if (parent.NodeType == "Increment" || parent.NodeType == "Decrement" || parent.NodeType == "PostfixExpr" || parent.NodeType == "UnaryExpr")
            {
                return parent.Value == "++" || parent.Value == "--" || parent.Value == "&";
            }

            return false;
        }

        private bool DetermineIfReadContext(AstNode node)
        {
            var parent = node.Parent;
            if (parent == null) return true;

            if (parent.NodeType == "KeyValue")
            {
                return parent.GetChild(1) == node;
            }

            if (parent.NodeType == "BinaryExpr")
            {
                string op = parent.Value;
                if (IsAssignmentOperator(op))
                {
                    if (op == ":=")
                    {
                        return parent.GetChild(1) == node;
                    }
                    return parent.GetChild(0) == node || parent.GetChild(1) == node;
                }
            }

            if (parent.NodeType == "Declaration" || parent.NodeType == "Parameter" || parent.NodeType == "Catch" || (parent.NodeType == "UnaryExpr" && parent.Value == "&"))
            {
                return false;
            }

            return true;
        }

        private void CheckUnusedSymbolsInScope(SymbolScope scope, string file)
        {
            foreach (var kv in scope.Symbols)
            {
                var sym = kv.Value;
                // Exclude implicit 'this'/'super', implicit property setter 'value', parameter tags matching underscore, and common callback parameters
                if (sym.Name.Equals("this", StringComparison.OrdinalIgnoreCase) ||
                    sym.Name.Equals("super", StringComparison.OrdinalIgnoreCase) ||
                    sym.Name.Equals("value", StringComparison.OrdinalIgnoreCase) ||
                    sym.Name.StartsWith("_") ||
                    (sym.Kind == SymbolKind.Parameter && (
                        sym.Name.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("event", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("state", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("hwnd", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("wParam", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("lParam", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("ui", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("ev", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("resultObj", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("thisObj", StringComparison.OrdinalIgnoreCase) ||
                        sym.Name.Equals("errorMsg", StringComparison.OrdinalIgnoreCase)
                    )))
                {
                    continue;
                }

                if (sym.ReadCount == 0)
                {
                    string symType = sym.Kind.ToString().ToLower();
                    AddIssue("Warning", string.Format("Unused {0} '{1}'.", symType, sym.Name), sym.Line, sym.Column, file, "UnusedSymbol");
                }
            }
        }

        private void AddIssue(string severity, string message, int line, int col, string file, string ruleId)
        {
            _issues.Add(new AnalysisIssue
            {
                Severity = severity,
                Message = message,
                Line = line,
                Column = col,
                FilePath = file,
                RuleId = ruleId
            });
        }

        private object EvaluateConstant(AstNode node)
        {
            if (node == null) return null;

            if (node.NodeType == "Grouped")
            {
                return node.ChildCount > 0 ? EvaluateConstant(node.GetChild(0)) : null;
            }

            if (node.NodeType == "Number")
            {
                double d;
                if (double.TryParse(node.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return d;
                return null;
            }

            if (node.NodeType == "String")
            {
                string val = node.Value;
                if (string.IsNullOrEmpty(val)) return "";
                if (val.StartsWith("\"") && val.EndsWith("\""))
                    return val.Substring(1, val.Length - 2);
                if (val.StartsWith("'") && val.EndsWith("'"))
                    return val.Substring(1, val.Length - 2);
                return val;
            }

            if (node.NodeType == "Identifier")
            {
                if (node.Value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (node.Value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                return null;
            }

            if (node.NodeType == "UnaryExpr" && (node.Value == "!" || node.Value == "not"))
            {
                object val = EvaluateConstant(node.GetChild(0));
                if (val is bool) return !(bool)val;
                if (val is double) return (double)val == 0;
                return null;
            }

            if (node.NodeType == "BinaryExpr")
            {
                string op = node.Value;
                object left = node.ChildCount > 0 ? EvaluateConstant(node.GetChild(0)) : null;
                object right = node.ChildCount > 1 ? EvaluateConstant(node.GetChild(1)) : null;

                if (left == null || right == null) return null;

                if (op == "==" || op == "=") return Equals(left, right);
                if (op == "!=") return !Equals(left, right);

                if (left is double && right is double)
                {
                    double l = (double)left;
                    double r = (double)right;
                    switch (op)
                    {
                        case "+": return l + r;
                        case "-": return l - r;
                        case "*": return l * r;
                        case "/": return r != 0 ? l / r : (object)null;
                        case "<": return l < r;
                        case ">": return l > r;
                        case "<=": return l <= r;
                        case ">=": return l >= r;
                    }
                }

                if (left is string && right is string)
                {
                    string l = (string)left;
                    string r = (string)right;
                    if (op == ".") return l + r;
                }

                if (left is bool && right is bool)
                {
                    bool l = (bool)left;
                    bool r = (bool)right;
                    if (op == "&&" || op == "and") return l && r;
                    if (op == "||" || op == "or") return l || r;
                }
            }

            return null;
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
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private string GenerateHtmlReport(List<AnalysisIssue> issues, int totalErrors, int totalWarnings)
        {
            var issuesJson = new StringBuilder();
            foreach (var issue in issues)
            {
                issuesJson.AppendLine(string.Format("            {{ severity: \"{0}\", ruleId: \"{1}\", file: \"{2}\", line: {3}, col: {4}, message: \"{5}\" }},",
                    EscapeJsonString(issue.Severity),
                    EscapeJsonString(issue.RuleId),
                    EscapeJsonString(Path.GetFileName(issue.FilePath)),
                    issue.Line,
                    issue.Column,
                    EscapeJsonString(issue.Message)
                ));
            }

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>AHK2 Static Analysis Report</title>
    <link href=""https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap"" rel=""stylesheet"">
    <style>
        :root {
            --bg-main: #0b0c10;
            --bg-panel: #11121d;
            --bg-panel-hover: #181926;
            --border-color: rgba(255, 255, 255, 0.08);
            --text-main: #cdd6f4;
            --text-muted: #a6adc8;
            --text-dim: #7f849c;
            --accent: #b4befe;
            --error: #f38ba8;
            --warning: #f9e2af;
            --success: #a6da95;
            --font-sans: 'Outfit', sans-serif;
            --font-mono: 'JetBrains Mono', monospace;
        }

        body {
            background-color: var(--bg-main);
            color: var(--text-main);
            font-family: var(--font-sans);
            margin: 0;
            padding: 40px;
            display: flex;
            justify-content: center;
        }

        .container {
            width: 100%;
            max-width: 1200px;
            display: flex;
            flex-direction: column;
            gap: 24px;
        }

        header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 20px;
        }

        h1 {
            margin: 0;
            font-size: 2rem;
            color: var(--accent);
            font-weight: 700;
        }

        .subtitle {
            color: var(--text-muted);
            margin: 4px 0 0 0;
            font-size: 0.95rem;
        }

        .dashboard {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 20px;
        }

        .stat-card {
            background: var(--bg-panel);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 24px;
            display: flex;
            flex-direction: column;
            gap: 8px;
            position: relative;
            overflow: hidden;
            transition: transform 0.2s cubic-bezier(0.4, 0, 0.2, 1), box-shadow 0.2s ease;
        }

        .stat-card:hover {
            transform: translateY(-2px);
            box-shadow: 0 8px 20px rgba(0, 0, 0, 0.4);
        }

        .stat-card::after {
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            width: 4px;
            height: 100%;
        }

        .stat-card.errors::after { background: var(--error); }
        .stat-card.warnings::after { background: var(--warning); }
        .stat-card.total::after { background: var(--accent); }

        .stat-val {
            font-size: 2.5rem;
            font-weight: 700;
            line-height: 1;
        }

        .stat-card.errors .stat-val { color: var(--error); }
        .stat-card.warnings .stat-val { color: var(--warning); }
        .stat-card.total .stat-val { color: var(--text-main); }

        .stat-label {
            color: var(--text-muted);
            font-size: 0.85rem;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }

        .filters {
            display: flex;
            gap: 12px;
            background: rgba(17, 18, 29, 0.7);
            backdrop-filter: blur(12px);
            -webkit-backdrop-filter: blur(12px);
            border: 1px solid var(--border-color);
            padding: 12px;
            border-radius: 8px;
            align-items: center;
        }

        .filter-btn {
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-color);
            color: var(--text-muted);
            padding: 8px 16px;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
            font-family: inherit;
            font-size: 0.85rem;
            font-weight: 500;
        }

        .filter-btn:hover {
            background: rgba(255, 255, 255, 0.08);
            color: #fff;
        }

        .filter-btn.active {
            background: var(--accent);
            color: var(--bg-main);
            border-color: var(--accent);
            font-weight: 600;
        }

        .search-box {
            margin-left: auto;
            position: relative;
        }

        .search-box input {
            background: rgba(255, 255, 255, 0.03);
            border: 1px solid var(--border-color);
            color: var(--text-main);
            padding: 8px 14px;
            border-radius: 6px;
            font-family: inherit;
            font-size: 0.85rem;
            width: 250px;
            outline: none;
            transition: all 0.25s ease;
        }

        .search-box input:focus {
            border-color: var(--accent);
            background: rgba(255, 255, 255, 0.06);
        }

        .issues-table-wrapper {
            background: rgba(17, 18, 29, 0.6);
            backdrop-filter: blur(12px);
            -webkit-backdrop-filter: blur(12px);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            overflow: hidden;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            text-align: left;
            font-size: 0.9rem;
        }

        th {
            background: rgba(255, 255, 255, 0.02);
            color: var(--text-muted);
            font-weight: 600;
            padding: 14px 20px;
            border-bottom: 1px solid var(--border-color);
            text-transform: uppercase;
            font-size: 0.75rem;
            letter-spacing: 0.05em;
        }

        td {
            padding: 14px 20px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.04);
            color: var(--text-muted);
            vertical-align: middle;
        }

        tr {
            transition: background-color 0.15s ease;
        }

        tr:last-child td {
            border-bottom: none;
        }

        tr:hover td {
            background: var(--bg-panel-hover);
        }

        .badge {
            display: inline-block;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.75rem;
            font-weight: 700;
            text-transform: uppercase;
        }

        .badge.error {
            background: rgba(243, 139, 168, 0.1);
            color: var(--error);
        }

        .badge.warning {
            background: rgba(249, 226, 175, 0.1);
            color: var(--warning);
        }

        .file-path {
            font-family: var(--font-mono);
            color: var(--text-main);
            font-size: 0.85rem;
        }

        .line-col {
            font-family: var(--font-mono);
            color: var(--text-dim);
        }

        .rule-id {
            font-family: var(--font-mono);
            color: var(--accent);
            font-size: 0.85rem;
        }

        .message-text {
            color: var(--text-main);
            line-height: 1.4;
        }

        .empty-state {
            padding: 60px;
            text-align: center;
            color: var(--success);
            font-size: 1.2rem;
            font-weight: 500;
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 12px;
        }

        .empty-state svg {
            width: 48px;
            height: 48px;
            stroke: var(--success);
        }
    </style>
</head>
<body>
    <div class=""container"">
        <header>
            <div>
                <h1>AHK2 Static Analysis</h1>
                <p class=""subtitle"">Deep scan static checking results and linter warnings</p>
            </div>
            <div id=""timestamp"" class=""subtitle"" style=""font-family: var(--font-mono);""></div>
        </header>

        <div class=""dashboard"">
            <div class=""stat-card total"">
                <div class=""stat-val"" id=""total-val"">0</div>
                <div class=""stat-label"">Total Issues</div>
            </div>
            <div class=""stat-card errors"">
                <div class=""stat-val"" id=""errors-val"">0</div>
                <div class=""stat-label"">Errors</div>
            </div>
            <div class=""stat-card warnings"">
                <div class=""stat-val"" id=""warnings-val"">0</div>
                <div class=""stat-label"">Warnings</div>
            </div>
        </div>

        <div class=""filters"">
            <button class=""filter-btn active"" onclick=""filterIssues('all')"" id=""btn-all"">All</button>
            <button class=""filter-btn"" onclick=""filterIssues('error')"" id=""btn-error"">Errors</button>
            <button class=""filter-btn"" onclick=""filterIssues('warning')"" id=""btn-warning"">Warnings</button>
            <div class=""search-box"">
                <input type=""text"" id=""search-input"" placeholder=""Search issues..."" oninput=""searchIssues()"">
            </div>
        </div>

        <div class=""filters"" style=""flex-wrap: wrap; gap: 10px; margin-top: 10px;"">
            <span style=""font-size: 0.85rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; align-self: center;"">Filter Rules:</span>
            <div id=""rule-filters-container"" style=""display: flex; flex-wrap: wrap; gap: 8px; align-items: center;"">
                <!-- Dynamically populated -->
            </div>
        </div>

        <div class=""issues-table-wrapper"">
            <table id=""issues-table"">
                <thead>
                    <tr>
                        <th style=""width: 120px;"">Severity</th>
                        <th style=""width: 150px;"">Rule ID</th>
                        <th style=""width: 200px;"">File</th>
                        <th style=""width: 100px;"">Line:Col</th>
                        <th>Message</th>
                    </tr>
                </thead>
                <tbody id=""issues-body"">
                </tbody>
            </table>
            <div id=""empty-view"" class=""empty-state"" style=""display: none;"">
                <svg viewBox=""0 0 24 24"" fill=""none"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                    <circle cx=""12"" cy=""12"" r=""10""></circle>
                    <polyline points=""12 6 12 12 16 14""></polyline>
                </svg>
                <span>No issues found. Your code is clean!</span>
            </div>
        </div>
    </div>

    <script>
        const issues = [
" + issuesJson.ToString() + @"        ];

        document.getElementById('timestamp').textContent = 'Generated: ' + new Date().toLocaleString();
        document.getElementById('total-val').textContent = issues.length;
        document.getElementById('errors-val').textContent = issues.filter(i => i.severity.toLowerCase() === 'error').length;
        document.getElementById('warnings-val').textContent = issues.filter(i => i.severity.toLowerCase() === 'warning').length;

        let currentFilter = 'all';
        let searchQuery = '';

        const ruleIds = [...new Set(issues.map(i => i.ruleId))].sort();
        const activeRules = new Set(ruleIds);

        function populateRuleFilters() {
            const container = document.getElementById('rule-filters-container');
            container.innerHTML = '';
            
            ruleIds.forEach(rule => {
                const btn = document.createElement('button');
                btn.className = 'filter-btn active';
                btn.style.fontSize = '0.78rem';
                btn.style.padding = '6px 12px';
                btn.id = 'rule-btn-' + rule;
                btn.textContent = rule;
                btn.onclick = () => toggleRuleFilter(rule);
                container.appendChild(btn);
            });
        }

        function toggleRuleFilter(rule) {
            const btn = document.getElementById('rule-btn-' + rule);
            if (activeRules.has(rule)) {
                activeRules.delete(rule);
                btn.classList.remove('active');
            } else {
                activeRules.add(rule);
                btn.classList.add('active');
            }
            renderIssues();
        }

        function renderIssues() {
            const tbody = document.getElementById('issues-body');
            const emptyView = document.getElementById('empty-view');
            const table = document.getElementById('issues-table');
            tbody.innerHTML = '';

            const filtered = issues.filter(i => {
                const matchesFilter = currentFilter === 'all' || i.severity.toLowerCase() === currentFilter;
                const matchesRule = activeRules.has(i.ruleId);
                const matchesSearch = searchQuery === '' || 
                    i.message.toLowerCase().includes(searchQuery) ||
                    i.ruleId.toLowerCase().includes(searchQuery) ||
                    i.file.toLowerCase().includes(searchQuery);
                return matchesFilter && matchesRule && matchesSearch;
            });

            if (filtered.length === 0) {
                table.style.display = 'none';
                emptyView.style.display = 'flex';
            } else {
                table.style.display = 'table';
                emptyView.style.display = 'none';
 
                filtered.forEach(i => {
                    const tr = document.createElement('tr');
                    
                    const badgeClass = i.severity.toLowerCase() === 'error' ? 'badge error' : 'badge warning';
                    
                    tr.innerHTML = `
                        <td><span class=""${badgeClass}"">${i.severity}</span></td>
                        <td><span class=""rule-id"">${i.ruleId}</span></td>
                        <td><span class=""file-path"">${i.file}</span></td>
                        <td><span class=""line-col"">${i.line}:${i.col}</span></td>
                        <td><span class=""message-text"">${i.message}</span></td>
                    `;
                    tbody.appendChild(tr);
                });
            }
        }

        function filterIssues(severity) {
            currentFilter = severity;
            document.querySelectorAll('.filter-btn').forEach(btn => {
                if (btn.id.startsWith('btn-')) {
                    btn.classList.remove('active');
                }
            });
            document.getElementById('btn-' + severity).classList.add('active');
            renderIssues();
        }

        function searchIssues() {
            searchQuery = document.getElementById('search-input').value.toLowerCase();
            renderIssues();
        }

        // Initial render
        populateRuleFilters();
        renderIssues();
    </script>
</body>
</html>";
        }
    }
}
