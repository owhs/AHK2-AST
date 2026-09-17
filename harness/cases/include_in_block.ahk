; @run
; @expect out| gen=42
; @expect out| cls=ok
; #Include is a load-time directive: AutoHotkey applies it even inside a block, and its functions/classes are
; defined for the whole script. When inlining, the engine must resolve it there too (not leave it behind).
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")
dev := true
if (dev) {
    #Include _inc\nested_gen.inc
    out("gen=" Gen())
}
out("cls=" NestedIncClass.Hi())
