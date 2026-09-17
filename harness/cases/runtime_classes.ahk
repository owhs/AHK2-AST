; @run
; Classes, properties and dynamic member access — the hard cases for renaming / tree-shaking.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

class Animal {
    static Count := 0
    name := "?"
    __New(name) {
        this.name := name
        Animal.Count++
    }
    Speak() => this.name " makes " this.Sound()
    Sound() => "..."
    Describe {
        get => "[" Type(this) ":" this.name "]"
    }
}
class Dog extends Animal {
    Sound() => "woof"
    Speak() => super.Speak() "!"
}
class Stack {
    items := []
    Push(v) => (this.items.Push(v), this)
    Pop() => this.items.Pop()
    Length => this.items.Length
    __Item[i] {
        get => this.items[i]
        set => this.items[i] := value
    }
    __Enum(n) => this.items.__Enum(n)
}
class Config {
    static Settings := Map("mode", "fast")
    static Get(k) => Config.Settings[k]
    class Nested {
        static Value := 99
    }
}
class Dyn {
    __Get(name, params) => "dyn:" name
    __Call(name, params) => "call:" name "/" params.Length
}

d := Dog("Rex")
out(d.Speak())
out(d.Describe)
out(Animal.Count " " (d is Animal) " " HasBase(d, Animal.Prototype))
st := Stack()
st.Push(1).Push(2).Push(3)
st[2] := 20
out(st.Length " " st[2] " " st.Pop())
res := ""
for v in st
    res .= v ","
out(res)
out(Config.Get("mode") " " Config.Nested.Value)

; dynamic member names held in strings
prop := "name"
out(d.%prop%)
meth := "Sound"
out(d.%meth%())
out(HasMethod(d, "Speak") " " HasProp(d, "name") " " HasProp(d, "nope"))
bound := ObjBindMethod(d, "Speak")
out(bound())
f := d.Sound
out(f(d))
dy := Dyn()
out(dy.anything " " dy.DoIt(1, 2))
o := {alpha: 1, beta: {gamma: "deep"}}
out(o.alpha " " o.beta.gamma " " o.%"alpha"%)
props := ""
for k, v in o.OwnProps()
    props .= k ";"
out(props)
m := Map()
m["key"] := "mapval"
out(m["key"] " " m.Has("key") " " m.Count)

; a global referenced by name
global GlobalThing := "g-value"
ReadGlobal(n) => %n%
out(ReadGlobal("GlobalThing"))
out(Type(%"Dog"%))
