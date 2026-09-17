#Requires AutoHotkey v2.0

#Hotstring X
::btw:: MsgBox ;; opens a msgbox then typing "btw"+[, -[],....]
:X0:abc::expansion ;; typing "abc"+[, -[],....] changes/expands to "expansion"
#Hotstring X0
:X:last:: MsgBox "Also a function" ;typing last+[, -[],....] opens msgbox
