; @run
; Broad expression / statement semantics. Every result goes to stdout so the original and each
; transformed output can be compared line by line.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

; numbers & operators
out(0x1F + 1e3 + 2.5 * 4 - 7 // 2 - 2 ** 3)
out((-17 >> 2) " " (-17 >>> 60) " " (5 & 3) " " (5 | 3) " " (5 ^ 3) " " (~0))
out(10 / 4 " " Mod(-7, 3) " " Round(2.675, 2) " " Integer("0x10") " " Float("1.5e2"))
n := unset
out(n ?? "fallback")
m := IsSet(n) ? 1 : 5
out(m)
s := "abc"
s .= "def"
out(s " " StrLen(s) " " SubStr(s, -2) " " InStr(s, "cd") " " StrUpper(s))
out("a" "b" . "c"  "d")
out("tab`there, quote `"q`" apostrophe 'a' backtick `` semicolon `; percent %")
out('single "quoted" `'escaped`'')
out(1 < 2 ? "lt" : "ge")
out(1 = 1.0 ? "eq" : "ne")
out("ABC" = "abc" ? "ci-eq" : "ci-ne")
out("ABC" == "abc" ? "cs-eq" : "cs-ne")
out((!0) " " (!"") " " (!"0") " " (not 1))
out([1, 2, 3] is Array ? "array" : "?")
out(Type(Map()) " " Type(1) " " Type(1.0) " " Type("") " " Type({}))

; continuation sections
cont := "
(
line one
  line two (indented)
)"
out(StrReplace(cont, "`n", "|"))
joined := "
(Join,
a
b
c
)"
out(joined)
trimmed := "
(LTrim
    x
    y
)"
out(StrReplace(trimmed, "`n", "|"))
expr := (
    1 +
    2 +
    3
)
out(expr)
arr := [
    "p",
    "q",
]
out(arr.Length " " arr[2])

; functions
Add(a, b := 10, c?) => a + b + (IsSet(c) ? c : 0)
out(Add(1) " " Add(1, 2) " " Add(1, 2, 3))
Sum(nums*) {
    t := 0
    for x in nums
        t += x
    return t
}
out(Sum(1, 2, 3, 4) " " Sum([5, 6]*))
Swap(&p, &q) {
    tmp := p, p := q, q := tmp
}
i := 1, j := 2
Swap(&i, &j)
out(i " " j)
Counter() {
    static count := 0
    return ++count
}
Counter(), Counter()
out(Counter())
MakeAdder(k) => (v) => v + k
add5 := MakeAdder(5)
out(add5(10))
Outer() {
    x := 3
    Inner() => x * 2
    return Inner()
}
out(Outer())
fnName := "Add"
out(%fnName%(2, 3))

; control flow
res := ""
Loop 5 {
    if (A_Index = 2)
        continue
    if (A_Index = 5)
        break
    res .= A_Index
}
out(res)
res := ""
Loop Parse, "a,b,c", ","
    res .= A_LoopField A_Index
out(res)
k := 0
while (k < 3)
    k++
out(k)
k := 0
Loop {
    k++
} until k >= 4
out(k)
res := ""
for key, val in Map("x", 1, "y", 2)
    res .= key "=" val ";"
out(res)
Classify(v) {
    switch v {
        case 1, 2: return "small"
        case "a": return "letter"
        default: return "other"
    }
}
out(Classify(2) " " Classify("A") " " Classify(9))
switch {
    case 1 > 2: out("wrong")
    case 2 > 1: out("switch-true")
}
try {
    throw ValueError("bad value", -1, "extra")
} catch ValueError as e {
    out("caught " Type(e) " " e.Message " " e.Extra)
} finally {
    out("finally")
}
try
    x := 1
catch
    out("never")
else
    out("try-else")
goto skip
out("not printed")
skip:
out("after goto")
