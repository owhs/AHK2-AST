; @run
; A nested class named after its global base (`class Error extends Error`): base-chain walks must not loop.
; Also `default:` outside a switch is a label, `default:` needn't be the last branch, and a case list may
; continue on lines starting with a comma.
#Requires AutoHotkey v2.0
out(s) => FileAppend(s "`n", "*")

class Sock {
    class Error extends Error {
    }
    static Kind(v) {
        switch v {
        default: return "other"
        case "a"
           , "b":
            return "ab"
        }
    }
}
try throw Sock.Error("boom")
catch Error as e
    out(Type(e) " " e.Message)
out(Sock.Kind("b") " " Sock.Kind("z"))

Jump(n) {
    if n > 1
        goto default
    return "low"
default:
    return "high"
}
out(Jump(1) " " Jump(2))

i := 0
for x in [1, 2, 3]
    i += x
until i >= 3
out(i)

; loop ... else: the else runs when the loop had no iterations (nothing may come between them)
for x in []
    out("never")
else
    out("empty")
