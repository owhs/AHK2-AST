; helper for linefile_and_selfregistration.ahk: finds a sibling file through its own A_LineFile
class LineFileLib {
    static Dir := SubStr(A_LineFile, 1, InStr(A_LineFile, "\", , -1))
    static Name() => SubStr(A_LineFile, InStr(A_LineFile, "\", , -1) + 1)
    static IsMain() => A_LineFile = A_ScriptFullPath
}