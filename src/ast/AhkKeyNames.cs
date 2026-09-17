using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// AutoHotkey v2 key names, as far as the remap rule needs them. A hotkey whose same-line action is exactly one
/// key name (optionally prefixed with ^ ! + # &lt; &gt;) is a remap (`a::b`, `XButton2::^LButton`, `F15::+`).
/// Verified against AutoHotkey 2.0.19 via ListHotkeys: `Return` and `Pause` are statements/functions, not remap
/// targets, and neither are joystick buttons or combined vkNNscNNN codes; `Sleep`, `Help`, `Enter` are keys.
/// </summary>
public static class AhkKeyNames
{
    static readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LButton", "RButton", "MButton", "XButton1", "XButton2", "WheelDown", "WheelUp", "WheelLeft", "WheelRight",
        "CapsLock", "Space", "Tab", "Enter", "Escape", "Esc", "Backspace", "BS", "ScrollLock", "Delete", "Del",
        "Insert", "Ins", "Home", "End", "PgUp", "PgDn", "Up", "Down", "Left", "Right",
        "NumLock", "NumpadDiv", "NumpadMult", "NumpadAdd", "NumpadSub", "NumpadEnter", "NumpadDot", "NumpadDel",
        "NumpadIns", "NumpadClear", "NumpadUp", "NumpadDown", "NumpadLeft", "NumpadRight", "NumpadHome", "NumpadEnd",
        "NumpadPgUp", "NumpadPgDn",
        "LWin", "RWin", "Control", "Ctrl", "Alt", "Shift", "LControl", "LCtrl", "RControl", "RCtrl",
        "LShift", "RShift", "LAlt", "RAlt",
        "Browser_Back", "Browser_Forward", "Browser_Refresh", "Browser_Stop", "Browser_Search", "Browser_Favorites",
        "Browser_Home", "Volume_Mute", "Volume_Down", "Volume_Up", "Media_Next", "Media_Prev", "Media_Stop",
        "Media_Play_Pause", "Launch_Mail", "Launch_Media", "Launch_App1", "Launch_App2",
        "AppsKey", "PrintScreen", "CtrlBreak", "Help", "Sleep",
    };

    static readonly Regex Coded = new Regex(@"^(F([1-9]|1\d|2[0-4])|Numpad\d|vk[0-9A-Fa-f]{1,2}|sc[0-9A-Fa-f]{1,3})$", RegexOptions.IgnoreCase);

    /// <summary>True if <paramref name="key"/> (no modifiers) names a key that can be a remap destination.</summary>
    public static bool IsKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.Length == 1) return !char.IsWhiteSpace(key[0]) && key[0] != '{' && key[0] != '(' && key[0] != ',' && key[0] != ';';
        return Named.Contains(key) || Coded.IsMatch(key);
    }

    /// <summary>True if <paramref name="text"/> is a remap destination: optional ^!+#&lt;&gt; modifiers, then one key.</summary>
    public static bool IsRemapDestination(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int i = 0;
        while (i < text.Length - 1 && "^!+#<>".IndexOf(text[i]) >= 0) i++;
        return IsKey(text.Substring(i));
    }
}
