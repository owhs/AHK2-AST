; Parser gaps found by the converter/history tiers (all valid AHK v2, checked with /Validate):
; colon-key hotkeys, spaced hotstring options, a hotstring block on its own line, non-ASCII names, omitted
; array elements, a trailing comma in a case list, `Loop Files.Length`, `if (x)()`, a ternary line `: "a::b"`.
#Requires AutoHotkey v2.0
:::
{
    ToolTip "colon"
}
+:::
{
    ToolTip 1
}
: :btw::by the way
::omw::
{
    SendText "on my way"
}
★a★b★c:
Arr := [ , , "A", "B"]
switch x := 1 {
    case 'LBL','LABELS',:
        y := 1
}
Files := [1,2]
Loop Files.Length {
    z := A_Index
}
O := {G: (*) => 1}
if (O.HasProp("G") && O.G)()
    w := 1
r := x ? 1
    : "a::b"
`;::
{
    ToolTip "semicolon key"
}
