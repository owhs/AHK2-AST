; @run
; @expect out| counter=5
; @expect out| shl=8 shr=2 ushr=1
; @expect out| Class: Btn
; @expect out| static nested=7
; @expect out| case=hit
; @expect out| seq=6
; Valid v2 that used to produce parser errors: compound assignment in a declaration, <<= >>= >>>=,
; `class` as a variable name, static nested functions, case lists and comma sequences continued on
; following lines (also across comment lines).
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

Counter := 2
Bump(delta) {
    global Counter += delta
}
Bump(3)
out("counter=" Counter)

a := 1, b := 16, c := -1
a <<= 3
b >>= 3
c >>>= 63
out("shl=" a " shr=" b " ushr=" c)

ShowClass() {
    class := "Btn"   ; a local may shadow the built-in Class
    out("Class: " class "")
}
ShowClass()

Outer() {
    static Helper(x) => x + 5
    return Helper(2)
}
out("static nested=" Outer())

Pick(v) {
    switch v {
    case "a"
       , "b"   ; first two
       , "c":
        return "hit"
    default:
        return "miss"
    }
}
out("case=" Pick("c"))

x := 1
    , y := 2   ; second
    ; a comment line in between
    , z := 3
out("seq=" (x + y + z))
