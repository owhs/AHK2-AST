; @issue 2
; A hotkey whose action is a single valid key is a remap, not a call to function/variable "b".
#Requires AutoHotkey v2.0
a::b
*CapsLock::Ctrl
XButton2::^LButton
F13::Numpad5
+F14::vk41
