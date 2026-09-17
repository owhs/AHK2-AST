; @issue 4
; @run
; @expect msgbox: Helloworld
; A continuation section without quotes joins the expressions (implicit concatenation).
#Requires AutoHotkey v2.0
a := "Hello"
b := "world"
Var :=
    ; These get implicitly concatenated
    (
        a
        b
    )
MsgBox Var
