; @run
; Language conformance: every construct prints what it computed, and the transcript of each flow's output must
; match the original's. Covers the things transforms most easily get subtly wrong.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

; --- dynamic names and VarRefs --------------------------------------------------------------------------
Counter := 3, item_1 := "one", item_2 := "two"
name := "Counter"
out(%name% " " item_%Counter - 1% " " %"item_" 2%)
ref := &Counter
%ref% += 1
out(Counter)
Bump(&v, by := 1) {
    v += by
}
Bump(&Counter, 10)
out(Counter)
holder := {ref: &Counter}
%holder.ref% := 99
out(Counter)

; --- closures, nested functions, statics --------------------------------------------------------------
MakeCounter(start) {
    n := start
    inc(*) => ++n   ; called as a method: gets the object as its first parameter
    get(*) => n
    return {inc: inc, get: get}
}
c := MakeCounter(5)
c.inc(), c.inc()
out(c.get())
Tally() {
    static calls := 0
    return ++calls
}
Tally(), Tally()
out(Tally())
Outer() {
    x := 1
    Inner() {
        x += 1
        return x
    }
    Inner()
    return Inner()
}
out(Outer())

; --- parameters -----------------------------------------------------------------------------------------
Join(sep, parts*) {
    s := ""
    for i, p in parts
        s .= (i > 1 ? sep : "") p
    return s
}
arr := ["a", "b", "c"]
out(Join("-", arr*) " " Join("+", 1, 2) " " Join(","))
Opt(a, b?, c := "C") => a (IsSet(b) ? b : "_") c
out(Opt(1) " " Opt(1, 2) " " Opt(1, , 3))

; --- objects, meta-functions, properties -------------------------------------------------------------
class Bag {
    __New() {
        this.items := Map()
    }
    __Item[key] {
        get => this.items.Has(key) ? this.items[key] : "none"
        set => this.items[key] := value
    }
    __Get(name, params) => "get:" name
    __Call(name, params) => "call:" name "(" params.Length ")"
    Count => this.items.Count
    static Make(pairs*) {
        b := this()
        loop pairs.Length // 2
            b[pairs[A_Index * 2 - 1]] := pairs[A_Index * 2]
        return b
    }
}
b := Bag.Make("x", 1, "y", 2)
out(b["x"] " " b["z"] " " b.Count " " b.missing " " b.Frob(1, 2))
class Temp {
    static _c := 0
    static C {
        get => Temp._c
        set => Temp._c := value * 2
    }
    Scale[f] => f * 10
}
Temp.C := 21
out(Temp.C " " Temp().Scale[4])
m := Map("k1", "v1", "k2", "v2")
s := ""
for k, v in m
    s .= k "=" v ";"
out(s)
o := {a: 1, b: {c: [10, 20, 30]}}
out(o.b.c[-1] " " o.b.c.Length " " o.HasOwnProp("a") " " ObjOwnPropCount(o))
prop := "a"
out(o.%prop%)

; --- operators ------------------------------------------------------------------------------------------
x := unset
out(x ?? "dflt")
out((5 // 2) " " (-5 // 2) " " (7 / 2) " " Mod(-7, 3) " " (2 ** 10) " " (1 << 4) " " (0xFF & 0x0F) " " (-1 >>> 60))
out(StrCompare("abc", "abd") " " ("B" = "b") " " ("B" == "b") " " ("10" < "9") " " (10 < 9))
s := "", s .= "x" . 1 2
out(s)
out((1 ? "t" : "f") (0 ? "t" : "f") ("" ? "t" : "f") ("0" ? "t" : "f") ("0.0" ? "t" : "f"))
out(1 && "" || "fallback")
y := 0
(y) || y := 4
out(y)

; --- exceptions -----------------------------------------------------------------------------------------
try {
    throw ValueError("bad", -1, "extra")
} catch TypeError {
    out("type")
} catch ValueError as e {
    out("value " e.Message " " e.Extra)
} else {
    out("no error")
} finally {
    out("finally")
}
try
    Integer("x")
catch as e
    out(Type(e))
Risky() {
    try
        return "from try"
    finally
        out("cleanup")
}
out(Risky())

; --- control flow ---------------------------------------------------------------------------------------
Classify(n) {
    switch {
        case n < 0: return "neg"
        case n = 0: return "zero"
        default: return "pos"
    }
}
out(Classify(-1) Classify(0) Classify(5))
switch "ABC", false {
    case "abc": out("ci-match")
    default: out("no-match")
}
total := 0
loop 10 {
    if A_Index = 3
        continue
    if A_Index > 5
        break
    total += A_Index
}
out(total)
parts := ""
loop parse "a,b;c", ",;"
    parts .= A_LoopField "|"
out(parts)
n := 0
loop
    n++
until n = 2
out(n)
w := 5
while w < 3
    w++
else
    out("while-else")
Outer2:
loop 3 {
    i := A_Index
    loop 3 {
        if A_Index = 2
            continue Outer2
        if i = 3
            break Outer2
    }
}
out(i)
for x in []
    out("never")
else
    out("empty")
k := 0
Again:
k++
if k < 3
    goto Again
out(k)

; --- strings --------------------------------------------------------------------------------------------
out(Format("{:05.1f}|{:-4}|{:x}", 3.14159, "ab", 255))
out(StrSplit("a b  c", " ").Length " " StrReplace("aaa", "a", "b", , &cnt) cnt)
RegExMatch("key=value", "(?<k>\w+)=(?<v>\w+)", &mt)
out(mt["k"] mt.v " " RegExReplace("a1b22", "\d+", "#"))
out(SubStr("hello", 2, 3) " " InStr("hello", "l", , -1) " " Ord("A") Chr(66))
text := "
(
  indented
    more
)"
out(StrLen(text) " " InStr(text, "more"))
out('single `'q`' "dq"' " " "tab`tsep")

; --- #HotIf condition names (validated, not run) ---------------------------------------------------------
HotMode := false
IsHotMode() => HotMode
#HotIf IsHotMode() && HotMode
F24::out("never pressed")
#HotIf
ExitApp
