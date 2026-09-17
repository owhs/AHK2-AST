#Requires AutoHotkey v2.0

str := "First part, ; comment"
(
    second part
)"
MsgBox str ;should be "First part, second part"
