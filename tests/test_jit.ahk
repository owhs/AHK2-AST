#Requires AutoHotkey v2.0
#SingleInstance Force

logPath := A_ScriptDir "\test_jit_result.log"
if FileExist(logPath) {
    try FileDelete(logPath)
}

try {
    ; Try to instantiate the JIT engine via COM
    global jit
    try {
        jit := ComObject("Ahk2Ast.JitEngine")
    } catch Error as initErr {
        ; If it fails, locate AstEngine.dll and register it
        dllPath := ""
        possiblePaths := [
            A_ScriptDir "\..\build\AstEngine.dll",
            A_ScriptDir "\build\AstEngine.dll",
            A_ScriptDir "\AstEngine.dll"
        ]
        for path in possiblePaths {
            if FileExist(path) {
                dllPath := path
                break
            }
        }
        
        if (dllPath == "") {
            throw Error("Failed to load JitEngine COM class, and could not find AstEngine.dll for registration.`nOriginal error: " initErr.Message)
        }
        
        RegisterAstEngine(dllPath)
        jit := ComObject("Ahk2Ast.JitEngine")
    }
    
    ; 1. EvalNumeric
    expr := "2 * (3 + 4) ** 2 - 5"
    res1 := jit.EvalNumeric(expr)
    
    ; 2. CompileAndRun (with a math loop/logic)
    ahkCode := "
    (
        x := 10
        y := 20
        return x * y + 50
    )"
    res2 := jit.CompileAndRun(ahkCode)
    
    ; Write success log
    resultText := "SUCCESS`n"
        . "EvalNumeric: " res1 "`n"
        . "CompileAndRun: " res2 "`n"
    FileAppend(resultText, logPath)
    
} catch Error as err {
    errorText := "ERROR: " err.Message "`nFile: " err.File "`nLine: " err.Line "`n"
    FileAppend(errorText, logPath)
}

RegisterAstEngine(dllPath) {
    if !FileExist(dllPath) {
        throw Error("AstEngine.dll not found at: " dllPath)
    }
    
    ; Resolve full path and convert to file:/// URI
    Loop Files, dllPath
        fullPath := A_LoopFileFullPath
    
    if (SubStr(fullPath, 2, 1) = ":") {
        fullPath := "/" StrReplace(fullPath, "\", "/")
    } else {
        fullPath := StrReplace(fullPath, "\", "/")
    }
    codeBaseUrl := "file:///" LTrim(fullPath, "/")
    
    classesKey := "HKCU\Software\Classes"
    assemblyStr := "AstEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
    
    ; 1. Register Ahk2Ast.AstEngine
    clsidAst := "{B8C4D2E3-F5A6-7890-BCDE-F01234567890}"
    RegWrite("AhkAstEngine", "REG_SZ", classesKey "\Ahk2Ast.AstEngine")
    RegWrite(clsidAst, "REG_SZ", classesKey "\Ahk2Ast.AstEngine\CLSID")
    
    clsidAstKey := classesKey "\CLSID\" clsidAst
    RegWrite("AhkAstEngine", "REG_SZ", clsidAstKey)
    RegWrite("Ahk2Ast.AstEngine", "REG_SZ", clsidAstKey "\ProgId")
    
    inprocAst := clsidAstKey "\InprocServer32"
    RegWrite("mscoree.dll", "REG_SZ", inprocAst)
    RegWrite("Both", "REG_SZ", inprocAst, "ThreadingModel")
    RegWrite("AhkAstEngine", "REG_SZ", inprocAst, "Class")
    RegWrite(assemblyStr, "REG_SZ", inprocAst, "Assembly")
    RegWrite("v4.0.30319", "REG_SZ", inprocAst, "RuntimeVersion")
    RegWrite(codeBaseUrl, "REG_SZ", inprocAst, "CodeBase")
    
    inprocAstVer := inprocAst "\0.0.0.0"
    RegWrite("AhkAstEngine", "REG_SZ", inprocAstVer, "Class")
    RegWrite(assemblyStr, "REG_SZ", inprocAstVer, "Assembly")
    RegWrite("v4.0.30319", "REG_SZ", inprocAstVer, "RuntimeVersion")
    RegWrite(codeBaseUrl, "REG_SZ", inprocAstVer, "CodeBase")
    
    RegWrite("", "REG_SZ", clsidAstKey "\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}")
    
    ; 2. Register Ahk2Ast.JitEngine
    clsidJit := "{C9D5E3F4-A6B7-8901-CDEF-012345678901}"
    RegWrite("AhkJitEngine", "REG_SZ", classesKey "\Ahk2Ast.JitEngine")
    RegWrite(clsidJit, "REG_SZ", classesKey "\Ahk2Ast.JitEngine\CLSID")
    
    clsidJitKey := classesKey "\CLSID\" clsidJit
    RegWrite("AhkJitEngine", "REG_SZ", clsidJitKey)
    RegWrite("Ahk2Ast.JitEngine", "REG_SZ", clsidJitKey "\ProgId")
    
    inprocJit := clsidJitKey "\InprocServer32"
    RegWrite("mscoree.dll", "REG_SZ", inprocJit)
    RegWrite("Both", "REG_SZ", inprocJit, "ThreadingModel")
    RegWrite("AhkJitEngine", "REG_SZ", inprocJit, "Class")
    RegWrite(assemblyStr, "REG_SZ", inprocJit, "Assembly")
    RegWrite("v4.0.30319", "REG_SZ", inprocJit, "RuntimeVersion")
    RegWrite(codeBaseUrl, "REG_SZ", inprocJit, "CodeBase")
    
    inprocJitVer := inprocJit "\0.0.0.0"
    RegWrite("AhkJitEngine", "REG_SZ", inprocJitVer, "Class")
    RegWrite(assemblyStr, "REG_SZ", inprocJitVer, "Assembly")
    RegWrite("v4.0.30319", "REG_SZ", inprocJitVer, "RuntimeVersion")
    RegWrite(codeBaseUrl, "REG_SZ", inprocJitVer, "CodeBase")
    
    RegWrite("", "REG_SZ", clsidJitKey "\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}")
}
