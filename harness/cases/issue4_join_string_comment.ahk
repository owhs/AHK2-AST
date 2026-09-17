; @issue 4
; @run
; @expect msgbox: First part,second part
; Comments are allowed inside a string that is continued with a join section.
#Requires AutoHotkey v2.0
str := "First part, ; comment"
(
    second part
)"
MsgBox str
