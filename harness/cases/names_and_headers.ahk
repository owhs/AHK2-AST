; @run
; @expect out| cs=1 b=2
; @expect out| dyn=two dyn2=pp
; @expect out| set=10,20
; @expect out| ref=5
; @expect out| paren=1a2b
; - `static types := Map(), types.CaseSense := false`: items after a comma may assign to members.
; - `b%n%`, `pre%n%post`: ONE variable whose name is computed (not a concatenation of variables).
; - `for (i, v in list)`: parenthesised for header.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

class Reg {
    static types := Map(), types.CaseSense := false
    static a := 1, b := 2
}
Reg.types["X"] := 1
out("cs=" Reg.types.Has("x") " b=" Reg.b)

n := 2
b2 := "two"
pre2post := "pp"
out("dyn=" b%n% " dyn2=" pre%n%post)
v1 := 0, v2 := 0
Loop 2
    v%A_Index% := A_Index * 10
out("set=" v1 "," v2)
w2 := 0
ref := &(w%n% := 5)
out("ref=" %ref%)

opts := ""
for (i, opt in ["a", "b"])
    opts .= i opt
out("paren=" opts)
