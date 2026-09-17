; @run
; @expect out| s1=[a1\nb1\nc]
; @expect out| s2=[PQ]
; @expect out| s3=[line1\n  line2]
; @expect out| s4=[First part,second part]
; @expect out| z=[p-q]
; @expect out| w=[m n]
; @expect out| v=[lit`tab]
; @expect out| q1=[<W T='M'>]
; @expect out| q2=[say "hi" and 'x']
; @expect out| q3=[esc " and `q]
; @expect out| prec=3
; @expect out| call=[text]
; @expect out| words=[5Z]
; @expect out| instr=[abcdef]
; Continuation sections are text: lines are indentation-trimmed (first line's indent), joined with `n (or Join),
; glued to the preceding line (whose ; comment is stripped even inside quotes) and followed by the rest of the
; `)` line. In code the join is whitespace; inside a string it is a real line feed. (AHK 2.0.19 outputs above.)
#Requires AutoHotkey v2.0
show(tag, v) => FileAppend(tag "=[" StrReplace(v, "`n", "\n") "]`n", "*")
x := 1
s1 :=
(
    "a" x "
    b" x "
    c"
)
show("s1", s1)
p := "P", q := "Q"
s2 :=
    ; a comment line between the operator and the section
(
    p
    q
)
show("s2", s2)
s3 := "
(
    line1
      line2
)"
show("s3", s3)
s4 := "First part, ; comment"
(
    second part
)"
show("s4", s4)
z := "
( Join-
    p
    q
)"
show("z", z)
w := "
(LTrim Join`s
      m
    n
)"
show("w", w)
v := "
( `
    lit`tab
)"
show("v", v)
; inside an open string, that string's quote character is literal in the section; escapes still work
q1 := '
(
<W T='M'>
)'
show("q1", q1)
q2 := "
(
say "hi" and 'x'
)"
show("q2", q2)
q3 := "
(
esc `" and ``q
)"
show("q3", q3)
; an expression section is text, not a parenthesised group: 2 *1 + 1 = 3 (grouping would give 4)
prec := 2 *
(
1 + 1
)
FileAppend "prec=" prec "`n", "*"
; a line ending in a word character gets a space before the section (in code only)
MsgBoxish(s) => FileAppend("call=[" s "]`n", "*")
MsgBoxish
(
    "text"
)
y := 5, z := "Z"
w2 := y
(
z
)
show("words", w2)
ins := "abc
(
def
)"
show("instr", ins)