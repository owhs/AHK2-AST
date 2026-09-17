; @run
; @expect out| 123
; @expect out| --3
; @expect out| only-command
; @expect out| hi 5
; @expect out| 12-
; @expect out| items=1 first=3
; Function-call statements: `Name args`, `obj.Method args`, bare `Name`. After `Name` + whitespace, anything
; but a binary/assignment operator starts the argument list (AHK 2.0: `f - 1` is f(-1), `f ,, 3` has two
; omitted args, a line ending in a comma continues on the next line). Such calls must be real calls for tree-shaking and renaming.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")
Show(a?, b?, c?) => out((IsSet(a) ? a : "-") (IsSet(b) ? b : "-") (IsSet(c) ? c : "-"))
OnlyCommandCalled() {
    out("only-command")
}
class K {
    static Hi(x?) => out("hi " (IsSet(x) ? x : "none"))
}
Show 1, 2, 3
Show ,, 3
Show , 2
Show 1,
    2
Show
Show - 1
Show (1)+2
OnlyCommandCalled
K.Hi
K.Hi 5
x := 7
Show x
Show "a"
    , "b"
Show !0, ~0
class Holder {
    items := []
    Add(x) {
        this.items.Push {v: x}
        this.Report
    }
    Report() => out("items=" this.items.Length " first=" this.items[1].v)
}
Holder().Add(3)