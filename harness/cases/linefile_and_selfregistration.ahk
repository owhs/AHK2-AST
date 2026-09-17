; @run
; @expect out| linefile_lib.inc found not-main
; @expect out| plugin one
; Two things inlining / tree-shaking must keep (both broke AHK2-ActiveX-Gui's Showcase):
;  - A_LineFile inside an included file is that file's path: libraries find their resources through it.
;  - A class whose static initializer registers it runs at load even though nothing names the class again.
#Requires AutoHotkey v2.0
#Include _inc\linefile_lib.inc
out(s) => FileAppend(s "`n", "*")

out(LineFileLib.Name() " " (FileExist(LineFileLib.Dir "nested_gen.inc") ? "found" : "missing") " " (LineFileLib.IsMain() ? "main" : "not-main"))

class Registry {
    static Items := Map()
    static Add(name, fn) => Registry.Items[name] := fn
}
class PluginOne {
    static _reg := Registry.Add("one", () => "plugin one")
}
out(Registry.Items["one"]())