; @run
; @expect out| vars=3
; @expect out| vars=1
; @expect out| k=a v=1
; For-loop variable lists are comma-separated slots, each `[&]name` or empty, ended by the reserved
; word `in`. An omitted trailing slot still counts: `for x, y, in e` calls e.__Enum(3).
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

class Counted {
    __Enum(n) {
        out("vars=" n)
        done := false
        return (&a?, &b?, &c?) => done ? false : (done := true)
    }
}
m := Map("a", 1)
for k, v, in m
    out("k=" k " v=" v)
for , v in m
    out("only v=" v)
for i, in [10, 20]
    out("i=" i)
for x, y, in Counted()
    out("iter3")
for x in Counted()
    out("iter1")
for key, value in m {
    out(key value)
}
