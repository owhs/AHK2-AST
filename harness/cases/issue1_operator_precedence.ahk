; @issue 1
; @run
; Assignment precedence is raised when that avoids a syntax error: not x:=y is not (x:=y),
; x==y && z:=1 is x==y && (z:=1), ++Var := X is ++(Var := X), c ? X:=2 : Y:=2 assigns in the branches.
#Requires AutoHotkey v2.0
x := 2, y := 1, z := 0
r := x == y && z := 1
FileAppend "r=" r " z=" z "`n", "*"
x := 1
r := x == y && z := 7
FileAppend "r=" r " z=" z "`n", "*"
a := 5
b := not a := 0
FileAppend "b=" b " a=" a "`n", "*"
v := 1
w := ++v := 5
FileAppend "w=" w " v=" v "`n", "*"
Z := 1, X := 0, Y := 0
t := Z > 0 ? X := 2 : Y := 3
FileAppend "t=" t " X=" X " Y=" Y "`n", "*"
if (x = 2 && q := 42)
    FileAppend "q=" q "`n", "*"
