; @run
; GUI construction, event sink methods named by string, control options. The transcript records
; the window and every control, so renamed/lost handlers or controls show up as a runtime diff.
#Requires AutoHotkey v2.0
class Handlers {
    OnPress(ctrl, info) {
    }
    OnPick(ctrl, info) {
    }
}
g := Gui("+Resize", "Harness GUI case", Handlers())
g.SetFont("s10", "Segoe UI")
g.Add("Text", "w220", "Name:")
ed := g.Add("Edit", "vName w220", "default text")
dd := g.Add("DropDownList", "vChoice w220 Choose2", ["one", "two", "three"])
dd.OnEvent("Change", "OnPick")
cb := g.Add("CheckBox", "vAgree Checked", "I agree")
lv := g.Add("ListView", "w220 r3", ["Col A", "Col B"])
lv.Add(, "a1", "b1")
btn := g.Add("Button", "Default w100", "&Press me")
btn.OnEvent("Click", "OnPress")
g.Show("NoActivate")
