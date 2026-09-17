; Stacked hotkeys (`~*Shift::` directly above `~*Ctrl::f()`) share the next one's action; every emitter must
; keep each on its own line. Executable hotstrings keep their action.
#Requires AutoHotkey v2.0
Update() {
    ToolTip "x"
}
~*Shift::
~*Ctrl::Update()

~*Ctrl up::
~*Shift up::SetTimer(Update, 250)

:X:btw::Update()

; A hotkey line followed by a function definition: the hotkey calls it, and it is an ordinary global function.
~F23::
ToggleIt(*) {
    ToolTip "t"
}
CallToggle() => ToggleIt()
