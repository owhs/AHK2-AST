; @run
; Statement shapes transforms must keep valid. AHK reads `name <op> ...` and `a.b <op> ...` at the start of a
; line as a function-call statement, so dropping `(fixed)`'s parentheses breaks it (src/ast/StatementHead.cs).
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

Frame(fixed := false) {
    c := {}
    (fixed) && c.Fixed := true
    return c.HasOwnProp("Fixed") ? "fixed" : "free"
}
out(Frame(true) " " Frame())

; `(_ := unset)`: `_` is never read, but a bare `unset` can't replace the assignment here.
Pick(L, R, f?) {
    return L ? 1 : R ? 2 : (IsSet(f) && f() ? 3 : (_ := unset))
}
out(Pick(0, 0, () => 1))

; unused items of a comma statement go away without leaving a bare `0` behind
Chain() {
    a := 0, b := 1, c := 2, d := 0
    return b + c
}
out(Chain())

; the expression inside a member deref reads (and renames) variables
Events() {
    static names := Map(1, "Ping")
    obj := {OnPing: (this) => "pong"}
    return obj.On%names[1]%()
}
out(Events())

; a block can't share the `case x:` line
Choose(n) {
    switch n {
        case 0:
        {
            r := "zero"
        }
        default:
        {
            r := "other"
        }
    }
    return r
}
out(Choose(0) " " Choose(1))

; `goto label` names a label, it is not an expression
Jump() {
    n := 0
again:
    n++
    if n < 3
        goto again
    return n
}
out(Jump())

; a pruned branch's block right after `f(x)` must not turn the call into a function definition
Noop(x) => x
Noop(1)
if (A_IsCompiled) {
    out("compiled")
} else {
    out("script")
}

; a bare `global` must stay the function's first line (trace inserts its own lines)
counter := 0
AssumeGlobal() {
    global
    counter := 5
}
AssumeGlobal()
out(counter)

; a #include line inside a continuation section is text
text := "
(
#include not_a_real_file.ahk
)"
out(StrLen(text))
/*
a block comment may run to the end of the file
}
