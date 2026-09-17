// AutoHotkey v2 syntax colouring for FastColoredTextBox.
//
// AhkScanner is a line scanner with a carried state (inside a /* block comment */, inside a quoted continuation
// section), so a change only re-colours from the edited line until the state settles again. AhkHighlighter owns
// the styles and writes them straight into the editor's character buffer (only its own style bits, so error
// squiggles and other layers survive a re-colour), and sets the { } folding markers from the same scan.

using System;
using System.Collections.Generic;
using System.Drawing;
using FastColoredTextBoxNS;

internal enum AhkTok : byte
{
    None, Comment, String, Escape, Number, Keyword, Directive, BuiltinVar, Function, BuiltinFunction, ClassName,
    Property, Label, Constant, Operator
}

internal struct AhkSpan
{
    public int Start, Length;
    public AhkTok Kind;
    public AhkSpan(int start, int length, AhkTok kind) { Start = start; Length = length; Kind = kind; }
}

internal static class AhkScanner
{
    public const int Normal = 0, BlockComment = 1, StringSection = 2, CodeSection = 3;

    // the engine's table generated from the AutoHotkey docs (AhkBuiltins) on top of the lists below
    static AhkScanner()
    {
        foreach (var f in AhkBuiltins.Functions.Keys) BuiltinFunctions.Add(f);
        foreach (var c in AhkBuiltins.Classes.Keys) if (c.IndexOf('.') < 0) BuiltinClasses.Add(c);
    }

    static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "and", "as", "break", "case", "catch", "class", "continue", "contains", "default", "else", "extends", "finally",
        "for", "global", "goto", "if", "in", "is", "local", "loop", "not", "or", "return", "static", "switch", "throw",
        "try", "until", "while"
    };

    static readonly HashSet<string> Constants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "this", "super", "unset"
    };

    static readonly HashSet<string> BuiltinClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Any", "Array", "BoundFunc", "Buffer", "Class", "ClipboardAll", "Closure", "ComObjArray", "ComObject", "ComValue",
        "ComValueRef", "Enumerator", "Error", "File", "Float", "Func", "Gui", "IndexError", "InputHook", "Integer",
        "KeyError", "Map", "MemberError", "MemoryError", "Menu", "MenuBar", "MethodError", "Number", "Object", "OSError",
        "Primitive", "PropertyError", "RegExMatchInfo", "String", "TargetError", "TimeoutError", "TypeError",
        "UnsetError", "UnsetItemError", "ValueError", "VarRef", "ZeroDivisionError"
    };

    public static readonly HashSet<string> BuiltinFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "ACos", "ASin", "ATan", "BlockInput", "CallbackCreate", "CallbackFree", "CaretGetPos", "Ceil", "Chr",
        "Click", "ClipWait", "ComCall", "ComObjActive", "ComObjConnect", "ComObjFlags", "ComObjFromPtr", "ComObjGet",
        "ComObjQuery", "ComObjType", "ComObjValue", "ControlAddItem", "ControlChooseIndex", "ControlChooseString",
        "ControlClick", "ControlDeleteItem", "ControlFindItem", "ControlFocus", "ControlGetChecked", "ControlGetChoice",
        "ControlGetClassNN", "ControlGetEnabled", "ControlGetExStyle", "ControlGetFocus", "ControlGetHwnd",
        "ControlGetIndex", "ControlGetItems", "ControlGetPos", "ControlGetStyle", "ControlGetText", "ControlGetVisible",
        "ControlHide", "ControlHideDropDown", "ControlMove", "ControlSend", "ControlSendText", "ControlSetChecked",
        "ControlSetEnabled", "ControlSetExStyle", "ControlSetStyle", "ControlSetText", "ControlShow",
        "ControlShowDropDown", "CoordMode", "Cos", "Critical", "DateAdd", "DateDiff", "DetectHiddenText",
        "DetectHiddenWindows", "DirCopy", "DirCreate", "DirDelete", "DirExist", "DirMove", "DirSelect", "DllCall",
        "Download", "DriveEject", "DriveGetCapacity", "DriveGetFileSystem", "DriveGetLabel", "DriveGetList",
        "DriveGetSerial", "DriveGetSpaceFree", "DriveGetStatus", "DriveGetStatusCD", "DriveGetType", "DriveLock",
        "DriveRetract", "DriveSetLabel", "DriveUnlock", "EditGetCurrentCol", "EditGetCurrentLine", "EditGetLine",
        "EditGetLineCount", "EditGetSelectedText", "EditPaste", "EnvGet", "EnvSet", "Exit", "ExitApp", "Exp",
        "FileAppend", "FileCopy", "FileCreateShortcut", "FileDelete", "FileEncoding", "FileExist", "FileGetAttrib",
        "FileGetShortcut", "FileGetSize", "FileGetTime", "FileGetVersion", "FileInstall", "FileMove", "FileOpen",
        "FileRead", "FileRecycle", "FileRecycleEmpty", "FileSelect", "FileSetAttrib", "FileSetTime", "Floor", "Format",
        "FormatTime", "GetKeyName", "GetKeySC", "GetKeyState", "GetKeyVK", "GetMethod", "GroupActivate", "GroupAdd",
        "GroupClose", "GroupDeactivate", "GuiCtrlFromHwnd", "GuiFromHwnd", "HasBase", "HasMethod", "HasProp", "HotIf",
        "HotIfWinActive", "HotIfWinExist", "HotIfWinNotActive", "HotIfWinNotExist", "Hotkey", "Hotstring", "IL_Add",
        "IL_Create", "IL_Destroy", "ImageSearch", "IniDelete", "IniRead", "IniWrite", "InputBox", "InstallKeybdHook",
        "InstallMouseHook", "InStr", "IsAlnum", "IsAlpha", "IsDigit", "IsFloat", "IsInteger", "IsLabel", "IsLower",
        "IsNumber", "IsObject", "IsSet", "IsSetRef", "IsSpace", "IsTime", "IsUpper", "IsXDigit", "KeyHistory", "KeyWait",
        "ListHotkeys", "ListLines", "ListVars", "ListViewGetContent", "Ln", "LoadPicture", "Log", "LTrim", "Max",
        "MenuFromHandle", "MenuSelect", "Min", "Mod", "MonitorGet", "MonitorGetCount", "MonitorGetName",
        "MonitorGetPrimary", "MonitorGetWorkArea", "MouseClick", "MouseClickDrag", "MouseGetPos", "MouseMove", "MsgBox",
        "NumGet", "NumPut", "ObjAddRef", "ObjBindMethod", "ObjFromPtr", "ObjFromPtrAddRef", "ObjGetBase",
        "ObjGetCapacity", "ObjHasOwnProp", "ObjOwnPropCount", "ObjOwnProps", "ObjPtr", "ObjPtrAddRef", "ObjRelease",
        "ObjSetBase", "ObjSetCapacity", "OnClipboardChange", "OnError", "OnExit", "OnMessage", "Ord", "OutputDebug",
        "Pause", "Persistent", "PixelGetColor", "PixelSearch", "PostMessage", "ProcessClose", "ProcessExist",
        "ProcessGetName", "ProcessGetParent", "ProcessGetPath", "ProcessSetPriority", "ProcessWait",
        "ProcessWaitClose", "Random", "RegCreateKey", "RegDelete", "RegDeleteKey", "RegExMatch", "RegExReplace",
        "RegRead", "RegWrite", "Reload", "Round", "RTrim", "Run", "RunAs", "RunWait", "Send", "SendEvent", "SendInput",
        "SendLevel", "SendMessage", "SendMode", "SendPlay", "SendText", "SetCapsLockState", "SetControlDelay",
        "SetDefaultMouseSpeed", "SetKeyDelay", "SetMouseDelay", "SetNumLockState", "SetRegView", "SetScrollLockState",
        "SetStoreCapsLockMode", "SetTimer", "SetTitleMatchMode", "SetWinDelay", "SetWorkingDir", "Shutdown", "Sin",
        "Sleep", "Sort", "SoundBeep", "SoundGetInterface", "SoundGetMute", "SoundGetName", "SoundGetVolume",
        "SoundPlay", "SoundSetMute", "SoundSetVolume", "SplitPath", "Sqrt", "StatusBarGetText", "StatusBarWait",
        "StrCompare", "StrGet", "StrLen", "StrLower", "StrPtr", "StrPut", "StrReplace", "StrSplit", "StrTitle",
        "StrUpper", "SubStr", "Suspend", "SysGet", "SysGetIPAddresses", "Tan", "Thread", "ToolTip", "TraySetIcon",
        "TrayTip", "Trim", "Type", "VarSetStrCapacity", "VerCompare", "WinActivate", "WinActivateBottom", "WinActive",
        "WinClose", "WinExist", "WinGetClass", "WinGetClientPos", "WinGetControls", "WinGetControlsHwnd", "WinGetCount",
        "WinGetExStyle", "WinGetID", "WinGetIDLast", "WinGetList", "WinGetMinMax", "WinGetPID", "WinGetPos",
        "WinGetProcessName", "WinGetProcessPath", "WinGetStyle", "WinGetText", "WinGetTitle", "WinGetTransColor",
        "WinGetTransparent", "WinHide", "WinKill", "WinMaximize", "WinMinimize", "WinMinimizeAll", "WinMinimizeAllUndo",
        "WinMove", "WinMoveBottom", "WinMoveTop", "WinRedraw", "WinRestore", "WinSetAlwaysOnTop", "WinSetEnabled",
        "WinSetExStyle", "WinSetRegion", "WinSetStyle", "WinSetTitle", "WinSetTransColor", "WinSetTransparent",
        "WinShow", "WinWait", "WinWaitActive", "WinWaitClose", "WinWaitNotActive"
    };

    static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_' || c > 0x7F; }
    static bool IsIdChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c > 0x7F; }

    /// <summary>
    /// Scans one line. `state` is the state at the start of the line; returns the state at its end.
    /// `braceDepthChange` is opens minus closes; `leadingClose` is true when the first brace is a `}`.
    /// </summary>
    public static int ScanLine(string s, int state, List<AhkSpan> spans, out int opens, out int closes, out bool leadingClose, ref char sectionQuote)
    {
        spans.Clear();
        opens = 0; closes = 0; leadingClose = false;
        int n = s.Length;
        int i = 0;
        while (i < n && (s[i] == ' ' || s[i] == '\t')) i++;
        int first = i;

        if (state == BlockComment)
        {
            spans.Add(new AhkSpan(0, n, AhkTok.Comment));
            string t = s.Trim();
            return t.StartsWith("*/") || t.EndsWith("*/") ? Normal : BlockComment;
        }

        if (state == StringSection || state == CodeSection)
        {
            if (first < n && s[first] == ')')
            {
                // `)"` closes the section; the quote (if any) ends the string, the rest is code again
                int j = first + 1;
                if (state == StringSection)
                {
                    int q = sectionQuote != '\0' ? s.IndexOf(sectionQuote, j) : -1;
                    int end = q >= 0 ? q + 1 : j;
                    spans.Add(new AhkSpan(first, end - first, AhkTok.String));
                    j = end;
                }
                ScanCode(s, j, spans, ref opens, ref closes, ref leadingClose, false);
                sectionQuote = '\0';
                return Normal;
            }
            if (state == StringSection)
            {
                if (n > 0) spans.Add(new AhkSpan(0, n, AhkTok.String));
                return StringSection;
            }
            ScanCode(s, 0, spans, ref opens, ref closes, ref leadingClose, false);
            return CodeSection;
        }

        if (first >= n) return Normal;
        char c0 = s[first];

        // whole-line comments and block comment start
        if (c0 == ';') { spans.Add(new AhkSpan(first, n - first, AhkTok.Comment)); return Normal; }
        if (c0 == '/' && first + 1 < n && s[first + 1] == '*')
        {
            spans.Add(new AhkSpan(first, n - first, AhkTok.Comment));
            string t = s.Substring(first + 2).TrimEnd();
            return t.EndsWith("*/") ? Normal : BlockComment;
        }

        // continuation section opener: `(` [options] with no `)` on the line
        if (c0 == '(' && s.IndexOf(')', first) < 0)
        {
            spans.Add(new AhkSpan(first, n - first, AhkTok.Operator));
            return sectionQuote != '\0' ? StringSection : CodeSection;
        }

        // directives
        if (c0 == '#' && first + 1 < n && IsIdStart(s[first + 1]))
        {
            int j = first + 1;
            while (j < n && IsIdChar(s[j])) j++;
            string word = s.Substring(first, j - first);
            spans.Add(new AhkSpan(first, j - first, AhkTok.Directive));
            if (word.Equals("#Include", StringComparison.OrdinalIgnoreCase) || word.Equals("#IncludeAgain", StringComparison.OrdinalIgnoreCase)
                || word.Equals("#DllLoad", StringComparison.OrdinalIgnoreCase) || word.Equals("#Requires", StringComparison.OrdinalIgnoreCase)
                || word.Equals("#SingleInstance", StringComparison.OrdinalIgnoreCase) || word.Equals("#Warn", StringComparison.OrdinalIgnoreCase))
            {
                int semi = FindLineComment(s, j);
                int end = semi >= 0 ? semi : n;
                if (end > j) spans.Add(new AhkSpan(j, end - j, AhkTok.String));
                if (semi >= 0) spans.Add(new AhkSpan(semi, n - semi, AhkTok.Comment));
                return Normal;
            }
            ScanCode(s, j, spans, ref opens, ref closes, ref leadingClose, false);
            return Normal;
        }

        // hotstrings  :opts:abbrev::replacement
        if (c0 == ':')
        {
            int close = s.IndexOf(':', first + 1);
            int dbl = close >= 0 ? s.IndexOf("::", close + 1, StringComparison.Ordinal) : -1;
            if (close > first && dbl > close)
            {
                string opts = s.Substring(first + 1, close - first - 1);
                spans.Add(new AhkSpan(first, dbl + 2 - first, AhkTok.Label));
                int rest = dbl + 2;
                if (opts.IndexOf('x') >= 0 || opts.IndexOf('X') >= 0) ScanCode(s, rest, spans, ref opens, ref closes, ref leadingClose, false);
                else if (rest < n)
                {
                    string r = s.Substring(rest).Trim();
                    if (r == "{") { opens++; spans.Add(new AhkSpan(s.IndexOf('{', rest), 1, AhkTok.Operator)); }
                    else spans.Add(new AhkSpan(rest, n - rest, AhkTok.String));
                }
                return Normal;
            }
        }

        // hotkeys  ^!a::   ~LButton & RButton up::
        int hk = s.IndexOf("::", first, StringComparison.Ordinal);
        if (hk > first && IsHotkeyHead(s.Substring(first, hk - first)))
        {
            spans.Add(new AhkSpan(first, hk + 2 - first, AhkTok.Label));
            ScanCode(s, hk + 2, spans, ref opens, ref closes, ref leadingClose, false);
            return Normal;
        }

        // labels  Name:
        if (IsIdStart(c0))
        {
            int j = first;
            while (j < n && IsIdChar(s[j])) j++;
            if (j < n && s[j] == ':' && (j + 1 >= n || s[j + 1] != '=' && s[j + 1] != ':'))
            {
                string after = s.Substring(j + 1).Trim();
                if (after.Length == 0 || after.StartsWith(";"))
                {
                    spans.Add(new AhkSpan(first, j + 1 - first, AhkTok.Label));
                    if (after.Length > 0) { int semi = s.IndexOf(';', j + 1); spans.Add(new AhkSpan(semi, n - semi, AhkTok.Comment)); }
                    return Normal;
                }
            }
        }

        char openQuote = ScanCode(s, first, spans, ref opens, ref closes, ref leadingClose, true);
        sectionQuote = openQuote; // an unterminated quote may continue in a `(` section on the next line
        return Normal;
    }

    static bool IsHotkeyHead(string head)
    {
        string h = head.Trim();
        if (h.Length == 0) return false;
        if (h.IndexOf('"') >= 0 || h.IndexOf('\'') >= 0 || h.IndexOf('(') >= 0 || h.IndexOf(":=") >= 0 || h.IndexOf('{') >= 0) return false;
        // `a & b`, `x up`, otherwise no spaces at all
        string[] parts = h.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return true;
        if (parts.Length == 2) return parts[1].Equals("up", StringComparison.OrdinalIgnoreCase);
        if (parts.Length == 3) return parts[1] == "&";
        if (parts.Length == 4) return parts[1] == "&" && parts[3].Equals("up", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    static int FindLineComment(string s, int from)
    {
        for (int i = Math.Max(from, 1); i < s.Length; i++)
            if (s[i] == ';' && (s[i - 1] == ' ' || s[i - 1] == '\t')) return i;
        return -1;
    }

    /// <summary>Scans code from `i`; returns the quote char of a string left open at the end of the line.</summary>
    static char ScanCode(string s, int i, List<AhkSpan> spans, ref int opens, ref int closes, ref bool leadingClose, bool allowSection)
    {
        int n = s.Length;
        string prevWord = null;
        char prevSig = '\0'; // previous significant char
        while (i < n)
        {
            char c = s[i];
            if (c == ' ' || c == '\t') { i++; continue; }

            if (c == ';' && (i == 0 || s[i - 1] == ' ' || s[i - 1] == '\t'))
            {
                spans.Add(new AhkSpan(i, n - i, AhkTok.Comment));
                return '\0';
            }

            if (c == '"' || c == '\'')
            {
                int start = i++;
                bool closed = false;
                int segStart = start;
                while (i < n)
                {
                    char d = s[i];
                    if (d == '`' && i + 1 < n)
                    {
                        if (i > segStart) spans.Add(new AhkSpan(segStart, i - segStart, AhkTok.String));
                        spans.Add(new AhkSpan(i, 2, AhkTok.Escape));
                        i += 2; segStart = i;
                        continue;
                    }
                    i++;
                    if (d == c) { closed = true; break; }
                }
                if (i > segStart) spans.Add(new AhkSpan(segStart, i - segStart, AhkTok.String));
                prevSig = c; prevWord = null;
                if (!closed) return c;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1]) && !IsIdChar(prevSig)))
            {
                int start = i;
                if (c == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X'))
                {
                    i += 2;
                    while (i < n && Uri.IsHexDigit(s[i])) i++;
                }
                else
                {
                    while (i < n && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (i < n && (s[i] == 'e' || s[i] == 'E') && i + 1 < n && (char.IsDigit(s[i + 1]) || ((s[i + 1] == '-' || s[i + 1] == '+') && i + 2 < n && char.IsDigit(s[i + 2]))))
                    {
                        i += 2;
                        while (i < n && char.IsDigit(s[i])) i++;
                    }
                }
                if (i < n && IsIdChar(s[i])) { while (i < n && IsIdChar(s[i])) i++; prevSig = 'a'; continue; } // 1st, x1 etc.
                spans.Add(new AhkSpan(start, i - start, AhkTok.Number));
                prevSig = '0'; prevWord = null;
                continue;
            }

            if (IsIdStart(c))
            {
                int start = i;
                while (i < n && IsIdChar(s[i])) i++;
                string w = s.Substring(start, i - start);
                int k = i;
                while (k < n && (s[k] == ' ' || s[k] == '\t')) k++;
                char next = k < n ? s[k] : '\0';
                AhkTok kind = AhkTok.None;
                if (prevSig == '.') kind = next == '(' && k == i ? AhkTok.Function : AhkTok.Property;
                else if (prevWord != null && (prevWord.Equals("class", StringComparison.OrdinalIgnoreCase) || prevWord.Equals("extends", StringComparison.OrdinalIgnoreCase)))
                    kind = AhkTok.ClassName;
                else if (Keywords.Contains(w)) kind = AhkTok.Keyword;
                else if (Constants.Contains(w)) kind = AhkTok.Constant;
                else if ((w.Equals("get", StringComparison.OrdinalIgnoreCase) || w.Equals("set", StringComparison.OrdinalIgnoreCase)) && prevSig == '\0' && (next == '{' || next == '=' || next == '\0'))
                    kind = AhkTok.Keyword;
                else if (w.StartsWith("A_", StringComparison.OrdinalIgnoreCase)) kind = AhkTok.BuiltinVar;
                else if (next == '(' && k == i) kind = BuiltinFunctions.Contains(w) ? AhkTok.BuiltinFunction : AhkTok.Function;
                else if (BuiltinClasses.Contains(w)) kind = AhkTok.ClassName;
                else if (BuiltinFunctions.Contains(w) && prevSig == '\0') kind = AhkTok.BuiltinFunction; // `MsgBox "hi"`
                if (kind != AhkTok.None) spans.Add(new AhkSpan(start, i - start, kind));
                prevWord = w; prevSig = 'a';
                continue;
            }

            if (c == '{') { opens++; }
            else if (c == '}')
            {
                if (opens == 0 && closes == 0) leadingClose = true;
                if (opens > 0) opens--; else closes++;
            }
            if (c == '.' ) { prevSig = '.'; i++; continue; }
            prevSig = c; prevWord = null;
            i++;
        }
        return '\0';
    }
}

internal class AhkHighlighter
{
    readonly FastColoredTextBox _tb;
    readonly Dictionary<AhkTok, TextStyle> _styles = new Dictionary<AhkTok, TextStyle>();
    readonly Dictionary<AhkTok, StyleIndex> _masks = new Dictionary<AhkTok, StyleIndex>();
    StyleIndex _allMask;
    readonly List<byte> _lineState = new List<byte>(); // state at the start of each line
    readonly List<char> _lineQuote = new List<char>();
    readonly List<AhkSpan> _spans = new List<AhkSpan>();
    public bool Folding = true;

    public AhkHighlighter(FastColoredTextBox tb)
    {
        _tb = tb;
        _tb.Language = Language.Custom;
        // FastColoredTextBox has 16 style slots in all (the editor needs 3 more): constants share the keyword
        // style and the continuation-section `(` shares the directive style.
        foreach (AhkTok k in Enum.GetValues(typeof(AhkTok)))
        {
            if (k == AhkTok.None || k == AhkTok.Constant || k == AhkTok.Operator) continue;
            var st = new TextStyle(new SolidBrush(Color.Gray), null, FontStyle.Regular);
            _styles[k] = st;
            int idx = _tb.AddStyle(st);
            var m = (StyleIndex)(1 << idx);
            _masks[k] = m;
            _allMask |= m;
        }
        _styles[AhkTok.Constant] = _styles[AhkTok.Keyword]; _masks[AhkTok.Constant] = _masks[AhkTok.Keyword];
        _styles[AhkTok.Operator] = _styles[AhkTok.Directive]; _masks[AhkTok.Operator] = _masks[AhkTok.Directive];
        ApplyTheme();
        _tb.LineInserted += (s, e) =>
        {
            for (int j = 0; j < e.Count; j++) { _lineState.Insert(Math.Min(e.Index, _lineState.Count), 0); _lineQuote.Insert(Math.Min(e.Index, _lineQuote.Count), '\0'); }
        };
        _tb.LineRemoved += (s, e) =>
        {
            int cnt = Math.Min(e.Count, Math.Max(0, _lineState.Count - e.Index));
            if (cnt > 0) { _lineState.RemoveRange(e.Index, cnt); _lineQuote.RemoveRange(e.Index, cnt); }
        };
        _tb.TextChanged += (s, e) =>
        {
            if (e.ChangedRange == null) { HighlightAll(); return; }
            HighlightFrom(Math.Max(0, Math.Min(e.ChangedRange.Start.iLine, e.ChangedRange.End.iLine) - 1),
                          Math.Max(e.ChangedRange.Start.iLine, e.ChangedRange.End.iLine) + 1);
        };
    }

    public void ApplyTheme()
    {
        Set(AhkTok.Comment, WbTheme.Overlay0, FontStyle.Italic);
        Set(AhkTok.String, WbTheme.Green, FontStyle.Regular);
        Set(AhkTok.Escape, WbTheme.Teal, FontStyle.Regular);
        Set(AhkTok.Number, WbTheme.Peach, FontStyle.Regular);
        Set(AhkTok.Keyword, WbTheme.Mauve, FontStyle.Regular);
        Set(AhkTok.Directive, WbTheme.Pink, FontStyle.Regular);
        Set(AhkTok.BuiltinVar, WbTheme.Red, FontStyle.Regular);
        Set(AhkTok.Function, WbTheme.Blue, FontStyle.Regular);
        Set(AhkTok.BuiltinFunction, WbTheme.Sapphire, FontStyle.Regular);
        Set(AhkTok.ClassName, WbTheme.Yellow, FontStyle.Regular);
        Set(AhkTok.Property, WbTheme.Lavender, FontStyle.Regular);
        Set(AhkTok.Label, WbTheme.Peach, FontStyle.Bold);

        _tb.Invalidate();
    }

    void Set(AhkTok k, Color c, FontStyle fs)
    {
        var st = _styles[k];
        var old = st.ForeBrush as SolidBrush;
        st.ForeBrush = new SolidBrush(c);
        st.FontStyle = fs;
        if (old != null) old.Dispose();
    }

    void Sync()
    {
        while (_lineState.Count < _tb.LinesCount) { _lineState.Add(0); _lineQuote.Add('\0'); }
        if (_lineState.Count > _tb.LinesCount) { _lineState.RemoveRange(_tb.LinesCount, _lineState.Count - _tb.LinesCount); _lineQuote.RemoveRange(_tb.LinesCount, _lineQuote.Count - _tb.LinesCount); }
    }

    public void HighlightAll()
    {
        _lineState.Clear(); _lineQuote.Clear();
        Sync();
        HighlightFrom(0, _tb.LinesCount - 1, true);
    }

    void HighlightFrom(int from, int to, bool all = false)
    {
        Sync();
        int count = _tb.LinesCount;
        if (count == 0) return;
        from = Math.Min(from, count - 1);
        for (int i = from; i < count; i++)
        {
            int state = _lineState[i];
            char q = _lineQuote[i];
            var line = _tb[i];
            string text = line.Text;
            int opens, closes; bool leadClose;
            int end = AhkScanner.ScanLine(text, state, _spans, out opens, out closes, out leadClose, ref q);

            // clear our bits, then paint the spans
            for (int j = 0; j < line.Count; j++)
            {
                var ch = line[j];
                if ((ch.style & _allMask) != 0) { ch.style &= ~_allMask; line[j] = ch; }
            }
            foreach (var sp in _spans)
            {
                StyleIndex m = _masks[sp.Kind];
                int e2 = Math.Min(line.Count, sp.Start + sp.Length);
                for (int j = Math.Max(0, sp.Start); j < e2; j++) { var ch = line[j]; ch.style |= m; line[j] = ch; }
            }

            if (Folding)
            {
                line.FoldingStartMarker = opens > 0 ? "{" : null;
                line.FoldingEndMarker = closes > 0 || leadClose ? "}" : null;
                if (state == AhkScanner.Normal && end == AhkScanner.BlockComment) line.FoldingStartMarker = "/*";
                else if (state == AhkScanner.BlockComment && end == AhkScanner.Normal) line.FoldingEndMarker = "/*";
            }

            if (i + 1 < count)
            {
                char nq = q; // the quote a following `(` section would belong to
                bool same = _lineState[i + 1] == end && _lineQuote[i + 1] == nq;
                _lineState[i + 1] = (byte)end;
                _lineQuote[i + 1] = nq;
                if (!all && i >= to && same) break;
            }
        }
        _tb.Invalidate();
    }
}
