#Requires AutoHotkey v2.0

a := "Hello"
b := "world"
Var :=
    ; These get implicitly concatenated
    (
        a
        b
    )

MsgBox var ;should be Helloworld
