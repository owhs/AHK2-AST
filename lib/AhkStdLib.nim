{.experimental: "dotOperators".}
{.experimental: "callOperator".}
import std/[strutils, tables, hashes, widestrs, os, osproc, macros, random, math, times, algorithm]
export strutils

macro ahk_Bind*(fn: typed, args: varargs[untyped]): untyped =
  let fnType = fn.getTypeImpl
  var paramCount = 0
  if fnType.kind == nnkProcTy and fnType.len > 0:
    let params = fnType[0] # nnkFormalParams
    for k in 1 ..< params.len:
      let defs = params[k]
      paramCount += defs.len - 2

  let cSym = newIdentNode("c")
  let iSym = newIdentNode("i")

  var call = newCall(fn)
  for arg in args:
    call.add(arg)

  let boundCount = args.len
  if boundCount < paramCount:
    call.add(cSym)
    if boundCount + 1 < paramCount:
      call.add(iSym)

  result = newProc(
    params = [ident("AhkVar"), 
              newIdentDefs(cSym, ident("AhkVar"), newNilLit()),
              newIdentDefs(iSym, ident("AhkVar"), newNilLit())],
    body = newStmtList(call),
    procType = nnkLambda
  )

macro ObjBindMethod*(obj: untyped, methodStr: static string, args: varargs[untyped]): untyped =
  let methodIdent = newIdentNode(methodStr)
  var stmts = newStmtList()
  let selfVal = newIdentNode("selfVal")
  stmts.add(newLetStmt(selfVal, obj))
  
  var boundArgs: seq[NimNode] = @[]
  for i, arg in args:
    let boundArgName = newIdentNode("boundArg" & $i)
    stmts.add(newLetStmt(boundArgName, arg))
    boundArgs.add(boundArgName)
  
  var call = newCall(methodIdent, selfVal)
  for ba in boundArgs:
    call.add(ba)
    
  let closure = newProc(
    params = [ident("AhkVar")],
    body = newStmtList(call),
    procType = nnkLambda
  )
  
  stmts.add(newCall("toAhkVar", closure))
  result = newBlockStmt(stmts)

when defined(windows):
  import winim/[lean, com]
  import winim/inc/tlhelp32
  export lean, com, tlhelp32
  proc SetWindowTheme*(hwnd: HWND, pszSubAppName: LPCWSTR, pszSubIdList: LPCWSTR): HRESULT {.stdcall, dynlib: "uxtheme", importc: "SetWindowTheme".}
  proc IsUserAnAdmin*(): WINBOOL {.stdcall, dynlib: "shell32", importc: "IsUserAnAdmin".}
  const
    STM_SETIMAGE*: int32 = 0x0172
    IMAGE_BITMAP*: int32 = 0
    LR_LOADFROMFILE*: int32 = 0x0010
    LR_CREATEDIBSECTION*: int32 = 0x2000
    SS_BITMAP*: int32 = 0x000E
    SS_REALSIZECONTROL*: int32 = 0x0800
    BS_AUTOCHECKBOX*: int32 = 0x00000003
    BS_AUTORADIOBUTTON*: int32 = 0x00000009
    BS_GROUPBOX*: int32 = 0x00000007
    SS_CENTER*: int32 = 0x00000001
    SS_RIGHT*: int32 = 0x00000002
    SS_LEFT*: int32 = 0x00000000
    BS_CENTER*: int32 = 0x00000300
    BS_RIGHT*: int32 = 0x00000200
    BS_LEFT*: int32 = 0x00000100
    ES_CENTER*: int32 = 0x00000001
    ES_RIGHT*: int32 = 0x00000002
    ES_LEFT*: int32 = 0x00000000
    LVS_REPORT*: int32 = 0x0001
    LVS_SHOWSELALWAYS*: int32 = 0x0008
    LVM_DELETEITEM*: int32 = 0x1008
    LVM_DELETEALLITEMS*: int32 = 0x1009
    LVM_GETITEMCOUNT*: int32 = 0x1004
    LVM_INSERTITEMW*: int32 = 0x104D
    LVM_SETITEMTEXTW*: int32 = 0x1074
    LVM_GETNEXTITEM*: int32 = 0x100C
    LVM_SETITEMSTATE*: int32 = 0x102B
    LVM_INSERTCOLUMNW*: int32 = 0x1061
    LVNI_ALL*: int32 = 0x0000
    LVNI_FOCUSED*: int32 = 0x0001
    LVNI_SELECTED*: int32 = 0x0002
    LVIF_TEXT*: int32 = 0x0001
    LVIF_STATE*: int32 = 0x0008
    LVIF_PARAM*: int32 = 0x0004
    LVCF_TEXT*: int32 = 0x0001
    LVCF_WIDTH*: int32 = 0x0002
    LVIS_SELECTED*: int32 = 0x0002
    LVIS_FOCUSED*: int32 = 0x0001

type
  AhkKind* = enum
    akNull, akInt, akFloat, akString, akArray, akMap, akObject

  AhkVar* = ref object
    case kind*: AhkKind
    of akNull: discard
    of akInt: iVal*: int
    of akFloat: fVal*: float
    of akString: sVal*: string
    of akArray: aVal*: seq[AhkVar]
    of akMap: mVal*: Table[string, AhkVar]
    of akObject: oVal*: RootRef

  AhkClosure0* = proc(): AhkVar {.closure.}
  AhkClosure1* = proc(a: AhkVar): AhkVar {.closure.}
  AhkClosure2* = proc(a, b: AhkVar): AhkVar {.closure.}
  AhkClosure3* = proc(a, b, c: AhkVar): AhkVar {.closure.}
  AhkClosure4* = proc(a, b, c, d: AhkVar): AhkVar {.closure.}
  AhkClosure5* = proc(a, b, c, d, e: AhkVar): AhkVar {.closure.}
  AhkClosure6* = proc(a, b, c, d, e, f: AhkVar): AhkVar {.closure.}
  AhkClosure7* = proc(a, b, c, d, e, f, g: AhkVar): AhkVar {.closure.}
  AhkClosure8* = proc(a, b, c, d, e, f, g, h: AhkVar): AhkVar {.closure.}

  AhkFunctionObj* = ref object of RootObj
    arity*: int
    cb0*: AhkClosure0
    cb1*: AhkClosure1
    cb2*: AhkClosure2
    cb3*: AhkClosure3
    cb4*: AhkClosure4
    cb5*: AhkClosure5
    cb6*: AhkClosure6
    cb7*: AhkClosure7
    cb8*: AhkClosure8

proc toString*(v: AhkVar): string
proc toBool*(v: AhkVar): bool
proc toInt32*(v: AhkVar): int32
proc toInt64*(v: AhkVar): int64
proc toFloat64*(v: AhkVar): float64
proc parseColor*(opt: string): int32
when defined(windows):
  proc switchTabPage*(tabHwnd: HWND, pageIndex: int)
template Message*(e: ref Exception): string = e.msg

converter toAhkVar*(i: int): AhkVar = AhkVar(kind: akInt, iVal: i)
converter toAhkVar*(f: float): AhkVar = AhkVar(kind: akFloat, fVal: f)
converter toAhkVar*(s: string): AhkVar = AhkVar(kind: akString, sVal: s)
converter toAhkVar*(b: bool): AhkVar = AhkVar(kind: akInt, iVal: if b: 1 else: 0)
converter toAhkVar*(v: AhkVar): AhkVar = v
converter toAhkVar*(cb: AhkClosure0): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 0, cb0: cb))
converter toAhkVar*(cb: AhkClosure1): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 1, cb1: cb))
converter toAhkVar*(cb: AhkClosure2): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 2, cb2: cb))
converter toAhkVar*(cb: AhkClosure3): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 3, cb3: cb))
converter toAhkVar*(cb: AhkClosure4): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 4, cb4: cb))
converter toAhkVar*(cb: AhkClosure5): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 5, cb5: cb))
converter toAhkVar*(cb: AhkClosure6): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 6, cb6: cb))
converter toAhkVar*(cb: AhkClosure7): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 7, cb7: cb))
converter toAhkVar*(cb: AhkClosure8): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 8, cb8: cb))

converter toAhkVar*(cb: proc(): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 0, cb0: cb))
converter toAhkVar*(cb: proc(a: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 1, cb1: cb))
converter toAhkVar*(cb: proc(a, b: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 2, cb2: cb))
converter toAhkVar*(cb: proc(a, b, c: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 3, cb3: cb))
converter toAhkVar*(cb: proc(a, b, c, d: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 4, cb4: cb))
converter toAhkVar*(cb: proc(a, b, c, d, e: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 5, cb5: cb))
converter toAhkVar*(cb: proc(a, b, c, d, e, f: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 6, cb6: cb))
converter toAhkVar*(cb: proc(a, b, c, d, e, f, g: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 7, cb7: cb))
converter toAhkVar*(cb: proc(a, b, c, d, e, f, g, h: AhkVar): AhkVar {.nimcall.}): AhkVar = AhkVar(kind: akObject, oVal: AhkFunctionObj(arity: 8, cb8: cb))


var userClassGetFieldHook*: proc(o: RootRef, field: string): AhkVar {.closure.}
var userClassSetFieldHook*: proc(o: RootRef, field: string, val: AhkVar) {.closure.}


type
  AhkGui* = ref object of RootObj
    hwnd*: HWND
    controls*: seq[AhkControl]
    closeCallback*: proc(gui: AhkVar): AhkVar {.closure.}
    properties*: Table[string, AhkVar]
    currentTabControl*: HWND
    currentTabPage*: int
    when defined(windows):
      font*: HFONT
      backColor*: int32
      backBrush*: HBRUSH
      textColor*: int32

  AhkControl* = ref object of RootObj
    hwnd*: HWND
    kind*: string
    x*, y*, width*, height*: int32
    clickCallback*: proc(ctrl: AhkVar, info: AhkVar): AhkVar {.closure.}
    changeCallback*: proc(ctrl: AhkVar, info: AhkVar): AhkVar {.closure.}
    properties*: Table[string, AhkVar]
    tabControl*: HWND
    tabPage*: int
    when defined(windows):
      textColor*: int32
      backColor*: int32
      backBrush*: HBRUSH

  AhkBuffer* = ref object of RootObj
    bytes*: seq[byte]

  AhkMenu* = ref object of RootObj
    properties*: Table[string, AhkVar]

converter toAhkVar*(g: AhkGui): AhkVar = AhkVar(kind: akObject, oVal: g)
converter toAhkVar*(buf: AhkBuffer): AhkVar = AhkVar(kind: akObject, oVal: buf)
converter toAhkVar*(c: AhkControl): AhkVar = AhkVar(kind: akObject, oVal: c)
converter toAhkVar*(m: AhkMenu): AhkVar = AhkVar(kind: akObject, oVal: m)
converter toAhkVar*(r: RootRef): AhkVar = AhkVar(kind: akObject, oVal: r)

proc Menu*(): AhkVar =
  var m = AhkMenu(properties: initTable[string, AhkVar]())
  return toAhkVar(m)

proc AhkMenu_Add*(self: AhkVar, menuItemName: AhkVar = nil, callbackOrSubmenu: AhkVar = nil, options: AhkVar = nil): AhkVar =
  return self

proc AhkMenu_Delete*(self: AhkVar, menuItemName: AhkVar = nil): AhkVar =
  return self

proc AhkMenu_Show*(self: AhkVar, x: AhkVar = nil, y: AhkVar = nil): AhkVar =
  return self

proc TraySetIcon*(fileName: AhkVar = nil, iconNumber: AhkVar = nil, freeze: AhkVar = nil): AhkVar =
  return nil

proc TrayTip*(text: AhkVar = nil, title: AhkVar = nil, options: AhkVar = nil): AhkVar =
  return nil

proc CoordMode*(target: AhkVar, relativeTo: AhkVar = nil): AhkVar =
  return nil

proc EditPaste*(text: AhkVar, control: AhkVar = nil, winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    var hwnd: HWND = 0
    if control != nil:
      if control.kind == akInt:
        hwnd = cast[HWND](control.iVal)
      elif control.kind == akObject and control.oVal != nil:
        if control.oVal of AhkControl:
          hwnd = AhkControl(control.oVal).hwnd
        elif control.oVal of AhkGui:
          hwnd = AhkGui(control.oVal).hwnd
    if hwnd != 0:
      let wstr: LPCWSTR = newWideCString(text.toString())
      discard SendMessage(hwnd, 0x00C2, 1, cast[LPARAM](wstr))
  return nil

var A_TrayMenu*: AhkVar = Menu()
converter toAhkVar*(p: pointer): AhkVar = AhkVar(kind: akInt, iVal: cast[int](p))
converter toAhkVar*(i: int32): AhkVar = AhkVar(kind: akInt, iVal: i.int)
converter toAhkVar*(i: uint32): AhkVar = AhkVar(kind: akInt, iVal: i.int)
converter toAhkVar*(i: int64): AhkVar = AhkVar(kind: akInt, iVal: i.int)
converter toAhkVar*(i: uint64): AhkVar = AhkVar(kind: akInt, iVal: i.int)

proc AhkArray*(args: varargs[AhkVar, toAhkVar]): AhkVar =
  var s: seq[AhkVar] = @[]
  for val in args:
    s.add(val)
  return AhkVar(kind: akArray, aVal: s)

proc Map*(args: varargs[AhkVar, toAhkVar]): AhkVar =
  var t = initTable[string, AhkVar]()
  var i = 0
  while i < args.len:
    if i + 1 < args.len:
      t[args[i].toString()] = args[i+1]
    i += 2
  return AhkVar(kind: akMap, mVal: t)

proc `[]`*(v: AhkVar, index: int): AhkVar =
  if v == nil: return nil
  case v.kind:
  of akArray:
    if index >= 1 and index <= v.aVal.len:
      return v.aVal[index - 1]
  of akMap:
    let key = $index
    if v.mVal.contains(key):
      return v.mVal[key]
  else: discard
  return nil

proc `[]`*(v: AhkVar, index: string): AhkVar =
  if v == nil: return nil
  case v.kind:
  of akMap:
    if v.mVal.contains(index):
      return v.mVal[index]
  of akObject:
    if v.oVal != nil:
      let idx = index.toLowerAscii()
      if v.oVal of AhkControl:
        let ctrl = AhkControl(v.oVal)
        if idx == "hwnd": return toAhkVar(ctrl.hwnd)
        elif idx == "kind": return toAhkVar(ctrl.kind)
        elif idx == "x": return toAhkVar(ctrl.x)
        elif idx == "y": return toAhkVar(ctrl.y)
        elif idx == "width" or idx == "w": return toAhkVar(ctrl.width)
        elif idx == "height" or idx == "h": return toAhkVar(ctrl.height)
        elif idx == "value":
          when defined(windows):
            let k = ctrl.kind.toLowerAscii()
            if k == "checkbox" or k == "radio":
              let state = SendMessage(ctrl.hwnd, 0x00F0, 0, 0)
              return toAhkVar(if state == 1: 1 else: 0)
            elif k in ["tab", "tab3"]:
              let sel = SendMessage(ctrl.hwnd, 0x130B, 0, 0).int
              return toAhkVar(sel + 1)
            elif k in ["combobox", "ddl", "dropdownlist"]:
              let sel = SendMessage(ctrl.hwnd, 0x0147, 0, 0).int
              return toAhkVar(sel + 1)
            elif k == "listbox":
              let sel = SendMessage(ctrl.hwnd, 0x0188, 0, 0).int
              return toAhkVar(sel + 1)
            else:
              let length = GetWindowTextLength(ctrl.hwnd)
              if length == 0: return toAhkVar("")
              var buf = newSeq[WCHAR](length + 1)
              GetWindowText(ctrl.hwnd, addr buf[0], length + 1)
              return toAhkVar($cast[LPCWSTR](addr buf[0]))
          else:
            return toAhkVar("")
        elif idx == "text":
          when defined(windows):
            let length = GetWindowTextLength(ctrl.hwnd)
            if length == 0: return toAhkVar("")
            var buf = newSeq[WCHAR](length + 1)
            GetWindowText(ctrl.hwnd, addr buf[0], length + 1)
            return toAhkVar($cast[LPCWSTR](addr buf[0]))
          else:
            return toAhkVar("")
        else:
          if ctrl.properties.contains(idx):
            return ctrl.properties[idx]
          return nil
      elif v.oVal of AhkGui:
        let gui = AhkGui(v.oVal)
        if idx == "hwnd": return toAhkVar(gui.hwnd)
        else:
          if gui.properties.contains(idx):
            return gui.properties[idx]
          return nil
      elif v.oVal of AhkBuffer:
        let buf = AhkBuffer(v.oVal)
        if idx == "size": return toAhkVar(buf.bytes.len)
        elif idx == "ahk_ptr" or idx == "ptr":
          let address = if buf.bytes.len == 0: 0 else: cast[int](addr buf.bytes[0])
          return toAhkVar(address)
        else: return nil
      elif v.oVal of AhkMenu:
        let m = AhkMenu(v.oVal)
        if m.properties.contains(idx):
          return m.properties[idx]
        return nil
      elif v.oVal of AhkFunctionObj:
        if idx == "call": return v
        else: return nil
      else:
        if userClassGetFieldHook != nil:
          return userClassGetFieldHook(v.oVal, index)
  else: discard
  return nil

proc `[]`*(v: AhkVar, index: AhkVar): AhkVar =
  if v == nil or index == nil: return nil
  case index.kind:
  of akInt: return v[index.iVal]
  of akString: return v[index.sVal]
  else: return v[index.toString()]

proc `()`*(v: AhkVar, args: varargs[AhkVar, toAhkVar]): AhkVar =
  if v == nil or v.kind != akObject or v.oVal == nil: return nil
  if v.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(v.oVal)
    case fn.arity:
    of 0:
      if fn.cb0 != nil: return fn.cb0()
    of 1:
      if fn.cb1 != nil:
        let a = if args.len > 0: args[0] else: nil
        return fn.cb1(a)
    of 2:
      if fn.cb2 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        return fn.cb2(a, b)
    of 3:
      if fn.cb3 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        return fn.cb3(a, b, c)
    of 4:
      if fn.cb4 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        let d = if args.len > 3: args[3] else: nil
        return fn.cb4(a, b, c, d)
    of 5:
      if fn.cb5 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        let d = if args.len > 3: args[3] else: nil
        let e = if args.len > 4: args[4] else: nil
        return fn.cb5(a, b, c, d, e)
    of 6:
      if fn.cb6 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        let d = if args.len > 3: args[3] else: nil
        let e = if args.len > 4: args[4] else: nil
        let f = if args.len > 5: args[5] else: nil
        return fn.cb6(a, b, c, d, e, f)
    of 7:
      if fn.cb7 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        let d = if args.len > 3: args[3] else: nil
        let e = if args.len > 4: args[4] else: nil
        let f = if args.len > 5: args[5] else: nil
        let g = if args.len > 6: args[6] else: nil
        return fn.cb7(a, b, c, d, e, f, g)
    of 8:
      if fn.cb8 != nil:
        let a = if args.len > 0: args[0] else: nil
        let b = if args.len > 1: args[1] else: nil
        let c = if args.len > 2: args[2] else: nil
        let d = if args.len > 3: args[3] else: nil
        let e = if args.len > 4: args[4] else: nil
        let f = if args.len > 5: args[5] else: nil
        let g = if args.len > 6: args[6] else: nil
        let h = if args.len > 7: args[7] else: nil
        return fn.cb8(a, b, c, d, e, f, g, h)
    else: discard
  return nil

proc `[]=`*(v: AhkVar, index: int, val: AhkVar) =
  if v == nil: return
  case v.kind:
  of akArray:
    if index >= 1 and index <= v.aVal.len:
      v.aVal[index - 1] = val
    elif index == v.aVal.len + 1:
      v.aVal.add(val)
  of akMap:
    v.mVal[$index] = val
  else: discard

proc `[]=`*(v: AhkVar, index: string, val: AhkVar) =
  if v == nil: return
  case v.kind:
  of akMap:
    v.mVal[index] = val
  of akObject:
    if v.oVal != nil:
      let idx = index.toLowerAscii()
      if v.oVal of AhkControl:
        let ctrl = AhkControl(v.oVal)
        if idx == "value" or idx == "text":
          when defined(windows):
            let k = ctrl.kind.toLowerAscii()
            if (k == "checkbox" or k == "radio") and idx == "value":
              let state = if val.toBool(): 1 else: 0
              SendMessage(ctrl.hwnd, 0x00F1, cast[WPARAM](state), 0)
            elif k in ["tab", "tab3"] and idx == "value":
              switchTabPage(ctrl.hwnd, val.toInt32().int)
            elif k in ["combobox", "ddl", "dropdownlist"]:
              if idx == "value":
                let index = val.toInt32() - 1
                SendMessage(ctrl.hwnd, 0x014E, cast[WPARAM](index), 0)
              else:
                let wstr: LPCWSTR = newWideCString(val.toString())
                SendMessage(ctrl.hwnd, 0x014D, cast[WPARAM](-1), cast[LPARAM](wstr))
            elif k == "listbox":
              if idx == "value":
                let index = val.toInt32() - 1
                SendMessage(ctrl.hwnd, 0x0186, cast[WPARAM](index), 0)
              else:
                let wstr: LPCWSTR = newWideCString(val.toString())
                SendMessage(ctrl.hwnd, 0x018C, cast[WPARAM](-1), cast[LPARAM](wstr))
            else:
              let formattedText = val.toString().replace("\r\n", "\n").replace("\n", "\r\n")
              SetWindowText(ctrl.hwnd, newWideCString(formattedText))
        elif idx == "enabled":
          when defined(windows):
            EnableWindow(ctrl.hwnd, if val.toBool(): 1 else: 0)
        else:
          ctrl.properties[idx] = val
      elif v.oVal of AhkGui:
        let gui = AhkGui(v.oVal)
        if idx == "backcolor":
          when defined(windows):
            let col = parseColor("c" & val.toString())
            if col != -1:
              gui.backColor = col
              if gui.backBrush != 0:
                DeleteObject(gui.backBrush)
              gui.backBrush = CreateSolidBrush(cast[COLORREF](col))
              RedrawWindow(gui.hwnd, nil, 0, RDW_INVALIDATE or RDW_UPDATENOW or RDW_ERASE or RDW_ALLCHILDREN)
        else:
          gui.properties[idx] = val
      elif v.oVal of AhkBuffer:
        let buf = AhkBuffer(v.oVal)
        if idx == "size":
          buf.bytes.setLen(val.toInt32().int)
      elif v.oVal of AhkMenu:
        let m = AhkMenu(v.oVal)
        m.properties[idx] = val
      else:
        if userClassSetFieldHook != nil:
          userClassSetFieldHook(v.oVal, index, val)
  else: discard

proc `[]=`*(v: AhkVar, index: AhkVar, val: AhkVar) =
  if v == nil or index == nil: return
  case index.kind:
  of akInt: v[index.iVal] = val
  of akString: v[index.sVal] = val
  else: v[index.toString()] = val

macro `.`*(v: AhkVar, field: untyped, args: varargs[untyped]): untyped =
  let key = $field
  if args.len == 0:
    quote do:
      `v`[`key`]
  else:
    var call = newCall(newTree(nnkBracketExpr, v, newLit(key)))
    for arg in args:
      call.add(arg)
    return call

macro `.=`*(v: AhkVar, field: untyped, val: untyped): untyped =
  let key = $field
  quote do:
    `v`[`key`] = `val`

proc Mod*(number: AhkVar, divisor: AhkVar): AhkVar =
  if number == nil or divisor == nil: return nil
  if number.kind == akFloat or divisor.kind == akFloat:
    let n = number.toFloat64()
    let d = divisor.toFloat64()
    if d == 0.0: return nil
    return toAhkVar(n - d * floor(n / d))
  else:
    let n = number.toInt64()
    let d = divisor.toInt64()
    if d == 0: return nil
    return toAhkVar(n mod d)

proc Type*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar("String")
  case v.kind:
  of akNull: return toAhkVar("String")
  of akInt: return toAhkVar("Integer")
  of akFloat: return toAhkVar("Float")
  of akString: return toAhkVar("String")
  of akArray: return toAhkVar("Array")
  of akMap: return toAhkVar("Map")
  of akObject:
    if v.oVal == nil: return toAhkVar("Object")
    if v.oVal of AhkGui: return toAhkVar("Gui")
    if v.oVal of AhkControl: return toAhkVar("Gui.Control")
    if v.oVal of AhkBuffer: return toAhkVar("Buffer")
    if v.oVal of AhkMenu: return toAhkVar("Menu")
    if v.oVal of AhkFunctionObj: return toAhkVar("Func")
    return toAhkVar("Object")

proc Length*(v: AhkVar): int =
  if v == nil: return 0
  case v.kind:
  of akArray: return v.aVal.len
  of akMap: return v.mVal.len
  of akString: return v.sVal.len
  else: return 0

proc StrLen*(v: AhkVar): AhkVar =
  return toAhkVar(v.Length())

proc Push*(v: AhkVar, args: varargs[AhkVar, toAhkVar]): AhkVar =
  if v != nil and v.kind == akArray:
    for val in args:
      v.aVal.add(val)
  return v

proc Pop*(v: AhkVar): AhkVar =
  if v == nil or v.kind != akArray or v.aVal.len == 0: return nil
  let lastVal = v.aVal[^1]
  v.aVal.setLen(v.aVal.len - 1)
  return lastVal

proc RemoveAt*(v: AhkVar, index: AhkVar, length: AhkVar = nil): AhkVar =
  if v == nil or v.kind != akArray: return nil
  let idx = index.toInt32() - 1
  if idx < 0 or idx >= v.aVal.len: return nil
  
  let lenVal = if length == nil: 1 else: length.toInt32()
  if lenVal <= 0: return nil
  
  let limit = min(v.aVal.len, idx + lenVal)
  let count = limit - idx
  if count <= 0: return nil
  
  if length == nil:
    let removed = v.aVal[idx]
    for i in idx ..< v.aVal.len - 1:
      v.aVal[i] = v.aVal[i + 1]
    v.aVal.setLen(v.aVal.len - 1)
    return removed
  else:
    var removedList: seq[AhkVar] = @[]
    for i in idx ..< limit:
      removedList.add(v.aVal[i])
    for i in idx ..< v.aVal.len - count:
      v.aVal[i] = v.aVal[i + count]
    v.aVal.setLen(v.aVal.len - count)
    return AhkVar(kind: akArray, aVal: removedList)

proc InsertAt*(v: AhkVar, index: AhkVar, val: AhkVar): AhkVar =
  if v == nil or v.kind != akArray: return v
  let idx = index.toInt32() - 1
  if idx < 0: return v
  if idx >= v.aVal.len:
    v.aVal.add(val)
  else:
    v.aVal.add(nil)
    for i in countdown(v.aVal.len - 1, idx + 1):
      v.aVal[i] = v.aVal[i - 1]
    v.aVal[idx] = val
  return v

proc Has*(v: AhkVar, key: AhkVar): bool =
  if v == nil: return false
  case v.kind:
  of akMap: return v.mVal.contains(key.toString())
  of akArray:
    for item in v.aVal:
      if item == key: return true
  else: discard
  return false

proc Has*(v: AhkVar, key: string): bool =
  if v == nil: return false
  case v.kind:
  of akMap: return v.mVal.contains(key)
  else: discard
  return false

proc HasProp*(v: AhkVar, name: string): bool =
  if v == nil: return false
  case v.kind:
  of akMap: return v.mVal.contains(name)
  of akObject:
    if v.oVal != nil:
      let idx = name.toLowerAscii()
      if v.oVal of AhkControl:
        let ctrl = AhkControl(v.oVal)
        if idx == "hwnd" or idx == "kind" or idx == "x" or idx == "y" or idx == "width" or idx == "w" or idx == "height" or idx == "h" or idx == "value" or idx == "text" or idx == "enabled":
          return true
        return ctrl.properties.contains(idx)
      elif v.oVal of AhkGui:
        let gui = AhkGui(v.oVal)
        if idx == "hwnd" or idx == "backcolor":
          return true
        return gui.properties.contains(idx)
      elif v.oVal of AhkBuffer:
        if idx == "size" or idx == "ahk_ptr" or idx == "ptr":
          return true
      elif v.oVal of AhkMenu:
        let m = AhkMenu(v.oVal)
        return m.properties.contains(idx)
      else:
        if userClassGetFieldHook != nil:
          let val = userClassGetFieldHook(v.oVal, name)
          return val != nil
  else: discard
  return false

proc HasProp*(v: AhkVar, name: AhkVar): bool =
  if v == nil or name == nil: return false
  return v.HasProp(name.toString())

proc HasProp*[T](t: typedesc[T], name: string): bool =
  return true

proc HasProp*[T](t: typedesc[T], name: AhkVar): bool =
  return true

var A_Args*: AhkVar
var argsSeq: seq[AhkVar] = @[]
for param in commandLineParams():
  argsSeq.add(toAhkVar(param))
A_Args = AhkVar(kind: akArray, aVal: argsSeq)

var A_Index*: AhkVar = toAhkVar(0)
var A_LoopFileName*: AhkVar = toAhkVar("")
var A_LoopFileFullPath*: AhkVar = toAhkVar("")
var A_LoopFilePath*: AhkVar = toAhkVar("")
var A_LoopFileDir*: AhkVar = toAhkVar("")
var A_LoopFileExt*: AhkVar = toAhkVar("")
var A_LoopFileSize*: AhkVar = toAhkVar(0)
var A_LoopField*: AhkVar = toAhkVar("")
var A_LoopRegName*: AhkVar = toAhkVar("")
var A_LoopRegType*: AhkVar = toAhkVar("")
var A_LoopRegKey*: AhkVar = toAhkVar("")
var currentLoopRegValue*: AhkVar = toAhkVar("")

var A_WinDir*: AhkVar
block:
  let env = getEnv("SystemRoot")
  A_WinDir = toAhkVar(if env == "": "C:\\Windows" else: env)

var A_IsAdmin*: AhkVar
block:
  when defined(windows):
    A_IsAdmin = toAhkVar(if IsUserAnAdmin() != 0: 1 else: 0)
  else:
    A_IsAdmin = toAhkVar(0)


proc toString*(v: AhkVar): string =
  if v == nil: return ""
  case v.kind:
  of akNull: ""
  of akInt: $v.iVal
  of akFloat: $v.fVal
  of akString: v.sVal
  of akArray:
    var parts: seq[string] = @[]
    for item in v.aVal: parts.add(item.toString())
    "[" & parts.join(", ") & "]"
  of akMap:
    "Map"
  of akObject:
    "Object"

converter toStr*(v: AhkVar): string = v.toString()

proc toBool*(v: AhkVar): bool =
  if v == nil: return false
  case v.kind:
  of akNull: false
  of akInt: v.iVal != 0
  of akFloat: v.fVal != 0.0
  of akString: v.sVal != ""
  of akArray: v.aVal.len > 0
  of akMap: v.mVal.len > 0
  of akObject: v.oVal != nil

proc toBool*(b: bool): bool = b

converter toBoolConv*(v: AhkVar): bool = v.toBool()

iterator items*(v: AhkVar): AhkVar =
  if v != nil:
    case v.kind:
    of akArray:
      for item in v.aVal:
        yield item
    of akMap:
      for key in v.mVal.keys:
        yield toAhkVar(key)
    else:
      yield v

iterator pairs*(v: AhkVar): (AhkVar, AhkVar) =
  if v != nil:
    case v.kind:
    of akArray:
      for i, item in v.aVal:
        yield (toAhkVar(i + 1), item)
    of akMap:
      for key, val in v.mVal:
        yield (toAhkVar(key), val)
    else:
      yield (toAhkVar(1), v)

# Math / Operators overloads for dynamic behavior
proc `+`*(a, b: AhkVar): AhkVar =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
    return AhkVar(kind: akFloat, fVal: af + bf)
  else:
    let ai = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
    let bi = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
    return AhkVar(kind: akInt, iVal: ai + bi)

proc `-`*(a, b: AhkVar): AhkVar =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
    return AhkVar(kind: akFloat, fVal: af - bf)
  else:
    let ai = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
    let bi = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
    return AhkVar(kind: akInt, iVal: ai - bi)

proc `*`*(a, b: AhkVar): AhkVar =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
    return AhkVar(kind: akFloat, fVal: af * bf)
  else:
    let ai = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
    let bi = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
    return AhkVar(kind: akInt, iVal: ai * bi)

proc `/`*(a, b: AhkVar): AhkVar =
  let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
  let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
  return AhkVar(kind: akFloat, fVal: af / bf)

proc `div`*(a, b: AhkVar): AhkVar =
  let ai = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let bi = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 1)
  if bi == 0: return toAhkVar(0)
  return toAhkVar(ai div bi)

proc `&`*(a, b: AhkVar): AhkVar =
  return AhkVar(kind: akString, sVal: a.toString() & b.toString())

proc `&`*(a: AhkVar, b: string): AhkVar =
  return AhkVar(kind: akString, sVal: a.toString() & b)

proc `&`*(a: string, b: AhkVar): AhkVar =
  return AhkVar(kind: akString, sVal: a & b.toString())

proc `+=`*(a: var AhkVar, b: AhkVar) =
  a = a + b

proc `-=`*(a: var AhkVar, b: AhkVar) =
  a = a - b

proc `*=`*(a: var AhkVar, b: AhkVar) =
  a = a * b

proc `/=`*(a: var AhkVar, b: AhkVar) =
  a = a / b

proc `&=`*(a: var AhkVar, b: AhkVar) =
  a = a & b

proc `&=`*(a: var AhkVar, b: string) =
  a = a & b

proc `shl`*(a, b: AhkVar): AhkVar =
  let valA = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let valB = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
  return toAhkVar(valA shl valB)

proc `shr`*(a, b: AhkVar): AhkVar =
  let valA = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let valB = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
  return toAhkVar(valA shr valB)

proc `or`*(a, b: AhkVar): AhkVar =
  let valA = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let valB = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
  return toAhkVar(valA or valB)

proc `and`*(a, b: AhkVar): AhkVar =
  let valA = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let valB = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
  return toAhkVar(valA and valB)

proc `xor`*(a, b: AhkVar): AhkVar =
  let valA = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
  let valB = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 0)
  return toAhkVar(valA xor valB)

proc `==`*(a, b: AhkVar): bool =
  if system.isNil(a) and system.isNil(b): return true
  if system.isNil(a) or system.isNil(b): return false
  if a.kind == b.kind:
    case a.kind:
    of akNull: true
    of akInt: a.iVal == b.iVal
    of akFloat: a.fVal == b.fVal
    of akString: a.sVal == b.sVal
    of akArray: false
    of akMap: false
    of akObject: a.oVal == b.oVal
  else:
    a.toString() == b.toString()

proc `<`*(a, b: AhkVar): bool =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.sVal))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.sVal))
    return af < bf
  else:
    let ai = if a.kind == akInt: a.iVal else: parseInt(a.sVal)
    let bi = if b.kind == akInt: b.iVal else: parseInt(b.sVal)
    return ai < bi

proc `>`*(a, b: AhkVar): bool =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.sVal))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.sVal))
    return af > bf
  else:
    let ai = if a.kind == akInt: a.iVal else: parseInt(a.sVal)
    let bi = if b.kind == akInt: b.iVal else: parseInt(b.sVal)
    return ai > bi

proc `<=`*(a, b: AhkVar): bool =
  return (a < b) or (a == b)

proc `>=`*(a, b: AhkVar): bool =
  return (a > b) or (a == b)

# Core built-ins mapping
proc MsgBox*(text: AhkVar, title: AhkVar = "AHK", options: AhkVar = 0): AhkVar =
  let t = text.toString()
  let tl = title.toString()
  let opt = if options.kind == akInt: options.iVal else: 0
  echo tl, ": ", t
  when defined(windows):
    if getEnv("AHK2AST_HEADLESS") == "":
      MessageBox(0, t, tl, cast[int32](opt))

proc Send*(keys: AhkVar): AhkVar =
  let k = keys.toString()
  when defined(windows):
    discard
  else:
    echo "Sending keys: ", k

proc execShellCmdHidden*(cmd: string): int =
  when defined(windows):
    var si: STARTUPINFO
    var pi: PROCESS_INFORMATION
    si.cb = sizeof(si).int32
    si.dwFlags = STARTF_USESHOWWINDOW
    si.wShowWindow = SW_HIDE
    
    var commandToRun = cmd
    let cmdLower = cmd.toLowerAscii()
    let cIndex = cmdLower.find(" /c ")
    if cIndex != -1:
      let prefix = cmdLower[0 .. cIndex].strip(chars = {'"', ' '})
      if prefix.endsWith("cmd.exe") or prefix.endsWith("cmd"):
        commandToRun = cmd[cIndex + 4 .. ^1]

    let cmdLine = "cmd.exe /c " & commandToRun
    var cmdMutable = newWideCString(cmdLine)
    let success = CreateProcess(
      nil,
      cmdMutable,
      nil,
      nil,
      true, # Inherit handles so redirection works
      0x08000000.DWORD, # CREATE_NO_WINDOW
      nil,
      nil,
      &si,
      &pi
    )
    if success == 0:
      return -1
      
    while true:
      let waitRes = MsgWaitForMultipleObjects(1, &pi.hProcess, false, 10, QS_ALLINPUT)
      if waitRes == WAIT_OBJECT_0:
        break
      elif waitRes == (WAIT_OBJECT_0 + 1):
        var msg: MSG
        while PeekMessage(&msg, 0, 0, 0, PM_REMOVE):
          TranslateMessage(&msg)
          DispatchMessage(&msg)
      else:
        sleep(10)
        
    var exitCode: DWORD
    GetExitCodeProcess(pi.hProcess, &exitCode)
    CloseHandle(pi.hProcess)
    CloseHandle(pi.hThread)
    return exitCode.int
  else:
    result = execShellCmd(cmd)

proc RunWait*(target: AhkVar, workingDir: AhkVar = nil, options: AhkVar = nil): AhkVar =
  let cmd = target.toString()
  let exitCode = execShellCmdHidden(cmd)
  return toAhkVar(exitCode)

proc Run*(target: AhkVar, workingDir: AhkVar = nil, options: AhkVar = nil): AhkVar =
  let cmd = target.toString()
  when defined(windows):
    var si: STARTUPINFO
    var pi: PROCESS_INFORMATION
    si.cb = sizeof(si).int32
    si.dwFlags = STARTF_USESHOWWINDOW
    si.wShowWindow = SW_HIDE
    
    var cmdLine = "cmd.exe /c " & cmd
    var cmdMutable = newWideCString(cmdLine)
    let success = CreateProcess(
      nil,
      cmdMutable,
      nil,
      nil,
      true,
      0x08000000.DWORD, # CREATE_NO_WINDOW
      nil,
      nil,
      &si,
      &pi
    )
    if success != 0:
      CloseHandle(pi.hProcess)
      CloseHandle(pi.hThread)
  else:
    discard execShellCmdHidden(cmd)
  return toAhkVar(0)

# DllCall Helper Converters
proc toInt32*(v: AhkVar): int32 =
  if v == nil: return 0
  case v.kind:
  of akInt: cast[int32](v.iVal)
  of akFloat: cast[int32](v.fVal)
  of akString:
    try: cast[int32](parseInt(v.sVal))
    except: 0
  else: 0

proc toUInt32*(v: AhkVar): uint32 =
  if v == nil: return 0
  case v.kind:
  of akInt: cast[uint32](v.iVal)
  of akFloat: cast[uint32](v.fVal)
  of akString:
    try: cast[uint32](parseUInt(v.sVal))
    except: 0
  else: 0

proc toInt64*(v: AhkVar): int64 =
  if v == nil: return 0
  case v.kind:
  of akInt: cast[int64](v.iVal)
  of akFloat: cast[int64](v.fVal)
  of akString:
    try: cast[int64](parseBiggestInt(v.sVal))
    except: 0
  else: 0

proc toUInt64*(v: AhkVar): uint64 =
  if v == nil: return 0
  case v.kind:
  of akInt: cast[uint64](v.iVal)
  of akFloat: cast[uint64](v.fVal)
  of akString:
    try: cast[uint64](parseBiggestUInt(v.sVal))
    except: 0
  else: 0

proc toPointer*(v: AhkVar): pointer =
  if v == nil: return nil
  case v.kind:
  of akInt: cast[pointer](v.iVal)
  of akString: cast[pointer](cstring(v.sVal))
  of akObject:
    if v.oVal == nil: nil
    else: cast[pointer](v.oVal)
  else: nil

proc toCstring*(v: AhkVar): cstring =
  if v == nil: return nil
  case v.kind:
  of akString: cstring(v.sVal)
  else: cstring(v.toString())

proc toWstr*(v: AhkVar): wstring =
  if v == nil: return wstring("")
  return +$v.toString()

proc toFloat32*(v: AhkVar): float32 =
  if v == nil: return 0.0
  case v.kind:
  of akFloat: cast[float32](v.fVal)
  of akInt: cast[float32](v.iVal)
  of akString:
    try: cast[float32](parseFloat(v.sVal))
    except: 0.0
  else: 0.0

proc toFloat64*(v: AhkVar): float64 =
  if v == nil: return 0.0
  case v.kind:
  of akFloat: v.fVal
  of akInt: v.iVal.float64
  of akString:
    try: parseFloat(v.sVal)
    except: 0.0
  else: 0.0

var classRegistered = false
var activeControls: seq[AhkControl] = @[]
var activeGuis: seq[AhkGui] = @[]
var messageCallbacks: Table[int, proc(wParam, lParam, msg, hwnd: AhkVar): AhkVar {.closure.}]
var activeHotkeys: Table[string, proc(key: AhkVar): AhkVar {.closure.}]

when defined(windows):
  proc switchTabPage*(tabHwnd: HWND, pageIndex: int) =
    let curSel = SendMessage(tabHwnd, 0x130B, 0, 0).int # TCM_GETCURSEL
    if curSel != pageIndex - 1:
      discard SendMessage(tabHwnd, 0x130C, cast[WPARAM](pageIndex - 1), 0) # TCM_SETCURSEL
    
    # Hide/Show controls associated with this tab control
    for ctrl in activeControls:
      if ctrl.tabControl == tabHwnd:
        if ctrl.tabPage == pageIndex:
          ShowWindow(ctrl.hwnd, SW_SHOW)
        else:
          ShowWindow(ctrl.hwnd, SW_HIDE)
          
    let parentHwnd = GetParent(tabHwnd)
    if parentHwnd != 0:
      RedrawWindow(parentHwnd, nil, 0, RDW_INVALIDATE or RDW_UPDATENOW or RDW_ERASE or RDW_ALLCHILDREN)

proc wndProc(hwnd: HWND, uMsg: UINT, wParam: WPARAM, lParam: LPARAM): LRESULT {.stdcall.} =
  let msgInt = uMsg.int
  if messageCallbacks.contains(msgInt):
    let cb = messageCallbacks[msgInt]
    let res = cb(toAhkVar(wParam.int), toAhkVar(lParam.int), toAhkVar(msgInt), toAhkVar(cast[int](hwnd)))
    if res != nil and res.kind != akNull and res.toBool():
      return cast[LRESULT](res.toInt32())
      
  case uMsg:
  of WM_NOTIFY:
    let hdr = cast[LPNMHDR](lParam)
    if hdr.code == cast[UINT](-551): # TCN_SELCHANGE
      let tabHwnd = hdr.hwndFrom
      let curSel = SendMessage(tabHwnd, 0x130B, 0, 0).int + 1
      switchTabPage(tabHwnd, curSel)
    return 0
  of WM_COMMAND:
    let code = (wParam.int shr 16) and 0xffff
    let ctrlHwnd = cast[HWND](lParam)
    if ctrlHwnd != 0:
      for ctrl in activeControls:
        if ctrl.hwnd == ctrlHwnd:
          let k = ctrl.kind.toLowerAscii()
          if (k == "button" or k == "checkbox" or k == "radio") and code == BN_CLICKED:
            if ctrl.clickCallback != nil:
              discard ctrl.clickCallback(toAhkVar(ctrl), nil)
          elif (k == "combobox" or k == "ddl" or k == "dropdownlist") and code == 1: # CBN_SELCHANGE
            if ctrl.changeCallback != nil:
              discard ctrl.changeCallback(toAhkVar(ctrl), nil)
          elif k == "listbox" and code == 1: # LBN_SELCHANGE
            if ctrl.changeCallback != nil:
              discard ctrl.changeCallback(toAhkVar(ctrl), nil)
          elif k == "edit" and code == 0x0300: # EN_CHANGE
            if ctrl.changeCallback != nil:
              discard ctrl.changeCallback(toAhkVar(ctrl), nil)
    return 0
  of WM_ERASEBKGND:
    var gui: AhkGui = nil
    for g in activeGuis:
      if g.hwnd == hwnd:
        gui = g
        break
    if gui != nil and gui.backBrush != 0:
      var r: RECT
      GetClientRect(hwnd, &r)
      let hdc = cast[HDC](wParam)
      FillRect(hdc, &r, gui.backBrush)
      return 1
    return DefWindowProc(hwnd, uMsg, wParam, lParam)
  of WM_CTLCOLORSTATIC, WM_CTLCOLOREDIT:
    let ctrlHwnd = cast[HWND](lParam)
    let hdc = cast[HDC](wParam)
    var ctrl: AhkControl = nil
    for c in activeControls:
      if c.hwnd == ctrlHwnd:
        ctrl = c
        break
    var parentGui: AhkGui = nil
    for g in activeGuis:
      if g.hwnd == hwnd:
        parentGui = g
        break
    if ctrl != nil:
      var tColor: int32 = -1
      if ctrl.textColor != -1:
        tColor = ctrl.textColor
      elif parentGui != nil and parentGui.textColor != -1:
        tColor = parentGui.textColor
      if tColor != -1:
        SetTextColor(hdc, cast[COLORREF](tColor))
      var bBrush: HBRUSH = 0
      var bColor: int32 = -1
      if ctrl.backBrush != 0:
        bBrush = ctrl.backBrush
        bColor = ctrl.backColor
      elif parentGui != nil and parentGui.backBrush != 0:
        bBrush = parentGui.backBrush
        bColor = parentGui.backColor
      if bBrush != 0:
        SetBkColor(hdc, cast[COLORREF](bColor))
        SetBkMode(hdc, TRANSPARENT)
        return cast[LRESULT](bBrush)
    return DefWindowProc(hwnd, uMsg, wParam, lParam)
  of WM_CLOSE:
    for gui in activeGuis:
      if gui.hwnd == hwnd:
        if gui.closeCallback != nil:
          discard gui.closeCallback(toAhkVar(gui))
    PostQuitMessage(0)
    return 0
  of WM_DESTROY:
    PostQuitMessage(0)
    return 0
  else:
    return DefWindowProc(hwnd, uMsg, wParam, lParam)

proc registerGuiClass() =
  if classRegistered: return
  var wndclass: WNDCLASSEX
  wndclass.cbSize = sizeof(WNDCLASSEX).int32
  wndclass.style = cast[UINT](CS_HREDRAW or CS_VREDRAW)
  wndclass.lpfnWndProc = wndProc
  wndclass.hInstance = GetModuleHandle(nil)
  wndclass.hCursor = LoadCursor(0, IDC_ARROW)
  wndclass.hIcon = LoadIcon(wndclass.hInstance, MAKEINTRESOURCE(1))
  if wndclass.hIcon == 0:
    wndclass.hIcon = LoadIcon(0, IDI_APPLICATION)
  wndclass.hIconSm = LoadImage(wndclass.hInstance, MAKEINTRESOURCE(1), IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), 0)
  wndclass.hbrBackground = cast[HBRUSH](COLOR_BTNFACE + 1)
  wndclass.lpszClassName = newWideCString("AhkGuiClass")
  RegisterClassEx(&wndclass)
  classRegistered = true


proc parseCoordinate(p: string, prevCoord: int32, hasCoord: var bool, coord: var int32) =
  let suffix = if p.len > 2: p[2..^1] else: ""
  var offset: int32 = 0
  if suffix != "":
    try:
      if suffix.startsWith("+"):
        offset = parseInt(suffix[1..^1]).int32
      elif suffix.startsWith("-"):
        offset = -parseInt(suffix[1..^1]).int32
      else:
        offset = parseInt(suffix).int32
    except: discard
  coord = prevCoord + offset
  hasCoord = true

proc parseColor*(opt: string): int32 =
  if not opt.startsWith("c"): return -1
  let val = opt[1..^1].toLowerAscii()
  case val
  of "white": return 0x00FFFFFF
  of "black": return 0x00000000
  of "red": return 0x000000FF
  of "green": return 0x0000FF00
  of "blue": return 0x00FF0000
  of "yellow": return 0x0000FFFF
  else:
    try:
      let hexVal = parseHexInt(val)
      let r = (hexVal shr 16) and 0xFF
      let g = (hexVal shr 8) and 0xFF
      let b = hexVal and 0xFF
      return cast[int32]((b shl 16) or (g shl 8) or r)
    except:
      return -1

proc parseControlOptions*(options: string, x, y, w, h: var int32, hasX, hasY, hasW, hasH: var bool, alignment: var int, prevX, prevY, prevW, prevH: int32) =
  if options == "": return
  let parts = options.split(Whitespace)
  for part in parts:
    let p = part.strip().toLowerAscii()
    if p == "": continue
    if p.startsWith("x") and not p.startsWith("xs") and not p.startsWith("xp"):
      if p.len > 1 and (p[1] == '+' or p[1] == '-'):
        parseCoordinate("xp" & p[1..^1], prevX + prevW, hasX, x)
      else:
        try:
          x = parseInt(p[1..^1]).int32
          hasX = true
        except: discard
    elif p.startsWith("xp"):
      parseCoordinate(p, prevX, hasX, x)
    elif p.startsWith("xs"):
      parseCoordinate(p, prevX, hasX, x)
    elif p.startsWith("y") and not p.startsWith("ys") and not p.startsWith("yp"):
      if p.len > 1 and (p[1] == '+' or p[1] == '-'):
        parseCoordinate("yp" & p[1..^1], prevY + prevH, hasY, y)
      else:
        try:
          y = parseInt(p[1..^1]).int32
          hasY = true
        except: discard
    elif p.startsWith("yp"):
      parseCoordinate(p, prevY, hasY, y)
    elif p.startsWith("ys"):
      parseCoordinate(p, prevY, hasY, y)
    elif p.startsWith("w"):
      try:
        w = parseInt(p[1..^1]).int32
        hasW = true
      except: discard
    elif p.startsWith("h"):
      try:
        h = parseInt(p[1..^1]).int32
        hasH = true
      except: discard
    elif p.startsWith("r") and p.len > 1 and p[1].isDigit:
      try:
        let rows = parseInt(p[1..^1]).int32
        h = rows * 18 + 6
        hasH = true
      except: discard
    elif p == "center":
      alignment = 1
    elif p == "left":
      alignment = 0
    elif p == "right":
      alignment = 2

proc parseControlStyles(options: string, style: var int32, exStyle: var int32, isChecked: var bool) =
  if options == "": return
  let parts = options.split(Whitespace)
  for part in parts:
    let p = part.strip().toLowerAscii()
    if p == "": continue
    
    let isMin = p.startsWith("-")
    let clean = if p.startsWith("+") or p.startsWith("-"): p[1..^1] else: p
    
    case clean
    of "disabled":
      if isMin: style = style and (not WS_DISABLED)
      else: style = style or WS_DISABLED
    of "hidden":
      if isMin: style = style or WS_VISIBLE
      else: style = style and (not WS_VISIBLE)
    of "readonly":
      if isMin: style = style and (not 0x0800.int32)
      else: style = style or 0x0800.int32
    of "password":
      if isMin: style = style and (not 0x0020.int32)
      else: style = style or 0x0020.int32
    of "checked", "checked1":
      if isMin: isChecked = false
      else: isChecked = true
    of "checked0":
      isChecked = false
    of "multi":
      if isMin: style = style and (not 0x0004.int32)
      else: style = style or 0x0004.int32 or 0x0040.int32 or 0x00200000.int32 or 0x1000.int32
    else: discard


proc applyStyleOption(part: string, style: var int32, exStyle: var int32) =
  let p = part.strip().toLowerAscii()
  if p == "": return
  let isMin = p.startsWith("-")
  let clean = if p.startsWith("+") or p.startsWith("-"): p[1..^1] else: p
  
  case clean
  of "resize":
    if isMin:
      style = style and (not WS_THICKFRAME) and (not WS_MAXIMIZEBOX)
    else:
      style = style or WS_THICKFRAME or WS_MAXIMIZEBOX
  of "maximizebox":
    if isMin:
      style = style and (not WS_MAXIMIZEBOX)
    else:
      style = style or WS_MAXIMIZEBOX
  of "minimizebox":
    if isMin:
      style = style and (not WS_MINIMIZEBOX)
    else:
      style = style or WS_MINIMIZEBOX
  of "alwaysontop":
    if isMin:
      exStyle = exStyle and (not WS_EX_TOPMOST)
    else:
      exStyle = exStyle or WS_EX_TOPMOST
  of "border":
    if isMin:
      style = style and (not WS_BORDER)
    else:
      style = style or WS_BORDER
  of "caption":
    if isMin:
      style = style and (not WS_CAPTION)
    else:
      style = style or WS_CAPTION
  of "toolwindow":
    if isMin:
      exStyle = exStyle and (not WS_EX_TOOLWINDOW)
    else:
      exStyle = exStyle or WS_EX_TOOLWINDOW
  of "disabled":
    if isMin:
      style = style and (not WS_DISABLED)
    else:
      style = style or WS_DISABLED
  else: discard

proc Gui*(options: string = "", title: string = ""): AhkGui =
  when defined(windows):
    registerGuiClass()
    var windowStyle: int32 = WS_OVERLAPPED or WS_CAPTION or WS_SYSMENU or WS_MINIMIZEBOX
    var windowExStyle: int32 = 0
    
    if options != "":
      let parts = options.split(Whitespace)
      for part in parts:
        applyStyleOption(part, windowStyle, windowExStyle)
          
    let appName = getAppFilename().splitFile.name
    let titleStr = if title != "": title else: (if appName != "": appName else: "AHK Window")
    
    let hwnd = CreateWindowEx(
      cast[DWORD](windowExStyle),
      newWideCString("AhkGuiClass"),
      newWideCString(titleStr),
      cast[DWORD](windowStyle),
      CW_USEDEFAULT, CW_USEDEFAULT, 400, 300,
      0, 0, GetModuleHandle(nil), nil
    )
    let gui = AhkGui(hwnd: hwnd, controls: @[], textColor: -1, backColor: -1, backBrush: 0, properties: initTable[string, AhkVar](), currentTabControl: 0, currentTabPage: 0)
    activeGuis.add(gui)
    return gui
  else:
    echo "Creating GUI window"
    let gui = AhkGui(hwnd: 0, controls: @[], textColor: -1, backColor: -1, backBrush: 0, properties: initTable[string, AhkVar](), currentTabControl: 0, currentTabPage: 0)
    activeGuis.add(gui)
    return gui

proc Opt*(self: AhkGui, options: string) =
  when defined(windows):
    var style = GetWindowLong(self.hwnd, GWL_STYLE)
    var exStyle = GetWindowLong(self.hwnd, GWL_EXSTYLE)
    
    let parts = options.split(Whitespace)
    for part in parts:
      applyStyleOption(part, style, exStyle)
        
    discard SetWindowLong(self.hwnd, GWL_STYLE, style)
    discard SetWindowLong(self.hwnd, GWL_EXSTYLE, exStyle)
    discard SetWindowPos(self.hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_FRAMECHANGED)


proc AddInternal*(self: AhkGui, controlType: string, options: string = "", text: string = ""): AhkControl =
  when defined(windows):
    var winClass = ""
    var style: int32 = WS_CHILD or WS_VISIBLE
    var exStyle: int32 = 0
    var alignment = -1 # -1=not specified, 0=left, 1=center, 2=right
    var isChecked = false
    
    var ctrlTextColor: int32 = -1
    var ctrlBackColor: int32 = -1
    var ctrlBackBrush: HBRUSH = 0
    
    var hasX = false
    var hasY = false
    var hasW = false
    var hasH = false
    var x: int32 = 0
    var y: int32 = 0
    var w: int32 = 0
    var h: int32 = 0
    
    var prevX: int32 = 10
    var prevY: int32 = 10
    var prevW: int32 = 0
    var prevH: int32 = 0
    
    var prev: AhkControl = nil
    var offsetX: int32 = 0
    var offsetY: int32 = 0
    
    if self.currentTabControl != 0:
      # Find tab control
      var tabCtrl: AhkControl = nil
      for c in self.controls:
        if c.hwnd == self.currentTabControl:
          tabCtrl = c
          break
      if tabCtrl != nil:
        var rect: RECT
        rect.left = 0
        rect.top = 0
        rect.right = tabCtrl.width
        rect.bottom = tabCtrl.height
        discard SendMessage(tabCtrl.hwnd, 0x1328, 0, cast[LPARAM](addr rect))
        if rect.left == 0 and rect.top == 0:
          rect.left = 4
          rect.top = 26
        offsetX = tabCtrl.x + rect.left
        offsetY = tabCtrl.y + rect.top
        
      # Find prev control on the same tab page
      for i in countdown(self.controls.len - 1, 0):
        let c = self.controls[i]
        if c.tabControl == self.currentTabControl and c.tabPage == self.currentTabPage:
          prev = c
          break
      if prev != nil:
        prevX = prev.x
        prevY = prev.y
        prevW = prev.width
        prevH = prev.height
      else:
        prevX = offsetX
        prevY = offsetY
        prevW = 0
        prevH = 0
    else:
      if self.controls.len > 0:
        prev = self.controls[^1]
        prevX = prev.x
        prevY = prev.y
        prevW = prev.width
        prevH = prev.height
        
    var isLiteralX = false
    var isLiteralY = false
    if options != "":
      let parts = options.split(Whitespace)
      for part in parts:
        let p = part.strip().toLowerAscii()
        if p.startsWith("x") and not p.startsWith("xp") and not p.startsWith("xs") and not (p.len > 1 and (p[1] == '+' or p[1] == '-')):
          isLiteralX = true
        if p.startsWith("y") and not p.startsWith("yp") and not p.startsWith("ys") and not (p.len > 1 and (p[1] == '+' or p[1] == '-')):
          isLiteralY = true
          
    parseControlOptions(options, x, y, w, h, hasX, hasY, hasW, hasH, alignment, prevX, prevY, prevW, prevH)
    parseControlStyles(options, style, exStyle, isChecked)
    
    if self.currentTabControl != 0:
      if isLiteralX and hasX:
        x = x + offsetX
      if isLiteralY and hasY:
        y = y + offsetY
        
    if options != "":
      let parts = options.split(Whitespace)
      for part in parts:
        let p = part.strip().toLowerAscii()
        if p == "": continue
        if p.startsWith("c") and not p.startsWith("center"):
          let col = parseColor(p)
          if col != -1:
            ctrlTextColor = col
        elif p.startsWith("background"):
          let colorPart = "c" & p["background".len .. ^1]
          let col = parseColor(colorPart)
          if col != -1:
            ctrlBackColor = col
            ctrlBackBrush = CreateSolidBrush(cast[COLORREF](col))
            
    case controlType.toLowerAscii():
    of "text", "label":
      winClass = "STATIC"
      let alignStyle = if alignment == 1: SS_CENTER else: (if alignment == 2: SS_RIGHT else: SS_LEFT)
      style = style or alignStyle
    of "button":
      winClass = "BUTTON"
      let alignStyle = if alignment == 0: BS_LEFT elif alignment == 1: BS_CENTER elif alignment == 2: BS_RIGHT else: 0.int32
      style = style or BS_PUSHBUTTON or alignStyle
    of "edit":
      winClass = "EDIT"
      let alignStyle = if alignment == 1: ES_CENTER else: (if alignment == 2: ES_RIGHT else: ES_LEFT)
      let scrollStyle = if (style and 0x0004.int32) != 0: 0.int32 else: ES_AUTOHSCROLL
      style = style or alignStyle or WS_BORDER or scrollStyle
    of "checkbox":
      winClass = "BUTTON"
      style = style or BS_AUTOCHECKBOX
    of "radio":
      winClass = "BUTTON"
      style = style or BS_AUTORADIOBUTTON
    of "picture", "pic":
      winClass = "STATIC"
      style = style or SS_BITMAP or SS_REALSIZECONTROL
    of "combobox":
      winClass = "COMBOBOX"
      style = style or CBS_DROPDOWN or WS_VSCROLL
    of "ddl", "dropdownlist":
      winClass = "COMBOBOX"
      style = style or CBS_DROPDOWNLIST or WS_VSCROLL
    of "listbox":
      winClass = "LISTBOX"
      style = style or LBS_STANDARD or WS_VSCROLL
    of "groupbox":
      winClass = "BUTTON"
      style = style or BS_GROUPBOX
    of "listview":
      winClass = "SysListView32"
      style = style or WS_BORDER or LVS_REPORT or LVS_SHOWSELALWAYS
    of "tab", "tab3":
      winClass = "SysTabControl32"
    else:
      winClass = "STATIC"
      
    if not hasX:
      if self.currentTabControl != 0:
        if prev != nil:
          x = prev.x
        else:
          x = offsetX + 10
      else:
        x = 10
        
    if not hasY:
      if self.currentTabControl != 0:
        if prev != nil:
          y = prev.y + prev.height + 10
        else:
          y = offsetY + 10
      else:
        if self.controls.len > 0:
          let prevGlobal = self.controls[^1]
          y = prevGlobal.y + prevGlobal.height + 10
        else:
          y = 10
          
    if not hasW:
      let k = controlType.toLowerAscii()
      if k in ["edit", "combobox", "ddl", "dropdownlist", "listbox"]:
        w = 120
      elif k == "groupbox":
        w = 150
      elif k in ["tab", "tab3"]:
        w = 300
      else:
        let textLen = text.len
        w = max(120.int32, (textLen.int32 * 8) + 30)
        
    if not hasH:
      let k = controlType.toLowerAscii()
      if k == "listbox":
        h = 100
      elif k == "groupbox":
        h = 80
      elif k in ["combobox", "ddl", "dropdownlist"]:
        h = 150
      elif k in ["tab", "tab3"]:
        h = 200
      else:
        h = 23
        
    let ctrlHwnd = CreateWindowEx(
      cast[DWORD](exStyle),
      newWideCString(winClass),
      newWideCString(text),
      cast[DWORD](style),
      x, y, w, h,
      self.hwnd,
      cast[HMENU](self.controls.len + 100),
      GetModuleHandle(nil),
      nil
    )
    if controlType.toLowerAscii() in ["checkbox", "radio"]:
      discard SetWindowTheme(ctrlHwnd, newWideCString(""), newWideCString(""))
    let f = if self.font != 0: self.font else: GetStockObject(DEFAULT_GUI_FONT)
    SendMessage(ctrlHwnd, WM_SETFONT, cast[WPARAM](f), 1)
    
    if isChecked:
      SendMessage(ctrlHwnd, 0x00F1, 0x0001, 0)
      
    if controlType.toLowerAscii() in ["picture", "pic"] and text != "":
      let hImage = LoadImage(0, newWideCString(text), IMAGE_BITMAP, 0, 0, LR_LOADFROMFILE or LR_CREATEDIBSECTION)
      if hImage != 0:
        SendMessage(ctrlHwnd, STM_SETIMAGE, IMAGE_BITMAP, cast[LPARAM](hImage))
        
    if self.currentTabControl != 0 and self.currentTabPage > 1:
      ShowWindow(ctrlHwnd, SW_HIDE)
      
    let ctrl = AhkControl(
      hwnd: ctrlHwnd,
      kind: controlType,
      x: x,
      y: y,
      width: w,
      height: h,
      textColor: ctrlTextColor,
      backColor: ctrlBackColor,
      backBrush: ctrlBackBrush,
      tabControl: self.currentTabControl,
      tabPage: self.currentTabPage,
      properties: initTable[string, AhkVar]()
    )
    self.controls.add(ctrl)
    activeControls.add(ctrl)
    return ctrl
  else:
    echo "Adding control: ", controlType, " (", text, ")"
    let ctrl = AhkControl(hwnd: 0, kind: controlType, x: 10, y: 10, width: 120, height: 23, tabControl: self.currentTabControl, tabPage: self.currentTabPage, properties: initTable[string, AhkVar]())
    self.controls.add(ctrl)
    activeControls.add(ctrl)
    return ctrl

proc Add*(self: AhkGui, controlType: string, options: string = "", text: AhkVar = nil): AhkControl =
  let textStr = if text == nil: "" elif text.kind == akArray: "" else: text.toString()
  result = self.AddInternal(controlType, options, textStr)
  
  when defined(windows):
    let k = controlType.toLowerAscii()
    if k == "listview" and text != nil and text.kind == akArray:
      for colIdx, colVal in text.aVal:
        var col: LVCOLUMNW
        col.mask = cast[UINT](LVCF_TEXT or LVCF_WIDTH)
        col.cx = 150
        col.pszText = newWideCString(colVal.toString())
        discard SendMessage(result.hwnd, LVM_INSERTCOLUMNW, cast[WPARAM](colIdx), cast[LPARAM](addr col))
    elif k in ["tab", "tab3"] and text != nil and text.kind == akArray:
      for tabIdx, tabVal in text.aVal:
        let wstr: LPCWSTR = newWideCString(tabVal.toString())
        var item: TCITEMW
        item.mask = 1 # TCIF_TEXT
        item.pszText = wstr
        discard SendMessage(result.hwnd, 0x133E, cast[WPARAM](tabIdx), cast[LPARAM](addr item))
    elif k in ["combobox", "ddl", "dropdownlist"] and text != nil and text.kind == akArray:
      for itemIdx, itemVal in text.aVal:
        let wstr: LPCWSTR = newWideCString(itemVal.toString())
        discard SendMessage(result.hwnd, 0x0143, 0, cast[LPARAM](wstr))
    elif k == "listbox" and text != nil and text.kind == akArray:
      for itemIdx, itemVal in text.aVal:
        let wstr: LPCWSTR = newWideCString(itemVal.toString())
        discard SendMessage(result.hwnd, 0x0180, 0, cast[LPARAM](wstr))

proc Add*(self: AhkVar, controlType: string, options: string = "", text: AhkVar = nil): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkGui:
    return toAhkVar(AhkGui(self.oVal).Add(controlType, options, text))
  return nil

proc AhkGui_Add*(self: AhkVar, controlType: AhkVar = nil, options: AhkVar = nil, text: AhkVar = nil): AhkVar =
  let ct = if controlType == nil: "" else: controlType.toString()
  let opt = if options == nil: "" else: options.toString()
  return self.Add(ct, opt, text)


proc OnEvent*(self: AhkControl, eventName: string, callback: proc(): AhkVar): AhkVar =
  let ev = eventName.toLowerAscii()
  if ev == "click":
    self.clickCallback = proc(ctrl: AhkVar, info: AhkVar): AhkVar = callback()
  elif ev == "change":
    self.changeCallback = proc(ctrl: AhkVar, info: AhkVar): AhkVar = callback()

proc OnEvent*(self: AhkControl, eventName: string, callback: proc(ctrl: AhkVar): AhkVar): AhkVar =
  let ev = eventName.toLowerAscii()
  if ev == "click":
    self.clickCallback = proc(ctrl: AhkVar, info: AhkVar): AhkVar = callback(ctrl)
  elif ev == "change":
    self.changeCallback = proc(ctrl: AhkVar, info: AhkVar): AhkVar = callback(ctrl)

proc OnEvent*(self: AhkControl, eventName: string, callback: proc(ctrl: AhkVar, info: AhkVar): AhkVar): AhkVar =
  let ev = eventName.toLowerAscii()
  if ev == "click":
    self.clickCallback = callback
  elif ev == "change":
    self.changeCallback = callback

proc OnEvent*(self: AhkGui, eventName: string, callback: proc(): AhkVar): AhkVar =
  if eventName.toLowerAscii() == "close":
    self.closeCallback = proc(gui: AhkVar): AhkVar = callback()

proc OnEvent*(self: AhkGui, eventName: string, callback: proc(gui: AhkVar): AhkVar): AhkVar =
  if eventName.toLowerAscii() == "close":
    self.closeCallback = callback

proc Show*(self: AhkGui, options: string = ""): AhkVar =
  when defined(windows):
    var maxRight: int32 = 0
    var maxBottom: int32 = 0
    for ctrl in self.controls:
      if ctrl.x + ctrl.width > maxRight:
        maxRight = ctrl.x + ctrl.width
      if ctrl.y + ctrl.height > maxBottom:
        maxBottom = ctrl.y + ctrl.height

    var cw = if maxRight > 0: maxRight + 10 else: 400
    var ch = if maxBottom > 0: maxBottom + 10 else: 300
    
    var hasX = false
    var hasY = false
    var hasW = false
    var hasH = false
    var optX: int32 = 0
    var optY: int32 = 0
    var optW: int32 = 0
    var optH: int32 = 0
    
    if options != "":
      let parts = options.split(Whitespace)
      for part in parts:
        let p = part.strip().toLowerAscii()
        if p == "": continue
        if p.startsWith("x"):
          try:
            optX = parseInt(p[1..^1]).int32
            hasX = true
          except: discard
        elif p.startsWith("y"):
          try:
            optY = parseInt(p[1..^1]).int32
            hasY = true
          except: discard
        elif p.startsWith("w"):
          try:
            optW = parseInt(p[1..^1]).int32
            hasW = true
          except: discard
        elif p.startsWith("h"):
          try:
            optH = parseInt(p[1..^1]).int32
            hasH = true
          except: discard
          
    if hasW: cw = optW
    if hasH: ch = optH

    var rect = RECT(left: 0, top: 0, right: cw, bottom: ch)
    var style = GetWindowLong(self.hwnd, GWL_STYLE)
    var menu = GetMenu(self.hwnd)
    discard AdjustWindowRect(&rect, cast[DWORD](style), if menu != 0: 1 else: 0)

    let w = rect.right - rect.left
    let h = rect.bottom - rect.top

    var x = optX
    var y = optY
    
    if not hasX or not hasY:
      let scrW = GetSystemMetrics(SM_CXSCREEN)
      let scrH = GetSystemMetrics(SM_CYSCREEN)
      if not hasX: x = (scrW - w) div 2
      if not hasY: y = (scrH - h) div 2

    discard MoveWindow(self.hwnd, x, y, w, h, 1)

    ShowWindow(self.hwnd, SW_SHOW)
    UpdateWindow(self.hwnd)
    let isHeadless = getEnv("AHK2AST_HEADLESS") != ""
    if isHeadless:
      var msg: MSG
      while PeekMessage(&msg, 0, 0, 0, PM_REMOVE):
        if msg.message == WM_KEYDOWN:
          let wp = msg.wParam
          var triggered = false
          if wp == VK_RETURN:
            if activeHotkeys.contains("enter"):
              discard activeHotkeys["enter"](toAhkVar("Enter"))
              triggered = true
          elif wp == VK_ESCAPE:
            if activeHotkeys.contains("escape"):
              discard activeHotkeys["escape"](toAhkVar("Escape"))
              triggered = true
          elif wp == VK_F4 and (GetKeyState(VK_MENU) < 0):
            if activeHotkeys.contains("!f4"):
              triggered = true
          if triggered:
            continue
        TranslateMessage(&msg)
        DispatchMessage(&msg)
    else:
      var msg: MSG
      while GetMessage(&msg, 0, 0, 0) > 0:
        if msg.message == WM_KEYDOWN:
          let wp = msg.wParam
          var triggered = false
          if wp == VK_RETURN:
            if activeHotkeys.contains("enter"):
              discard activeHotkeys["enter"](toAhkVar("Enter"))
              triggered = true
          elif wp == VK_ESCAPE:
            if activeHotkeys.contains("escape"):
              discard activeHotkeys["escape"](toAhkVar("Escape"))
              triggered = true
          elif wp == VK_F4 and (GetKeyState(VK_MENU) < 0):
            if activeHotkeys.contains("!f4"):
              triggered = true
          if triggered:
            continue
        TranslateMessage(&msg)
        DispatchMessage(&msg)
  else:
    echo "Showing GUI window"

proc Opt*(self: AhkControl, options: string): AhkControl =
  when defined(windows):
    let parts = options.split(Whitespace)
    for part in parts:
      let p = part.strip().toLowerAscii()
      if p == "": continue
      if p.startsWith("background"):
        let colorPart = "c" & p["background".len .. ^1]
        let col = parseColor(colorPart)
        if col != -1:
          self.backColor = col
          if self.backBrush != 0:
            DeleteObject(self.backBrush)
          self.backBrush = CreateSolidBrush(cast[COLORREF](col))
  return self

proc Opt*(self: AhkVar, options: string): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkGui:
      AhkGui(self.oVal).Opt(options)
    elif self.oVal of AhkControl:
      discard AhkControl(self.oVal).Opt(options)
  return self

proc Move*(self: AhkControl, x: AhkVar = nil, y: AhkVar = nil, w: AhkVar = nil, h: AhkVar = nil): AhkControl =
  var cx = self.x
  var cy = self.y
  var cw = self.width
  var ch = self.height
  
  when defined(windows):
    if self.hwnd != 0:
      var rect = RECT()
      if GetWindowRect(self.hwnd, &rect) != 0:
        let parentHwnd = GetParent(self.hwnd)
        if parentHwnd != 0:
          var pt1 = POINT(x: rect.left, y: rect.top)
          var pt2 = POINT(x: rect.right, y: rect.bottom)
          ScreenToClient(parentHwnd, &pt1)
          ScreenToClient(parentHwnd, &pt2)
          cx = pt1.x
          cy = pt1.y
          cw = pt2.x - pt1.x
          ch = pt2.y - pt1.y
        else:
          cx = rect.left
          cy = rect.top
          cw = rect.right - rect.left
          ch = rect.bottom - rect.top

  if x != nil: cx = x.toInt32()
  if y != nil: cy = y.toInt32()
  if w != nil: cw = w.toInt32()
  if h != nil: ch = h.toInt32()
  
  self.x = cx
  self.y = cy
  self.width = cw
  self.height = ch
  
  when defined(windows):
    if self.hwnd != 0:
      discard MoveWindow(self.hwnd, cx, cy, cw, ch, 1)
  return self

proc Move*(self: AhkGui, x: AhkVar = nil, y: AhkVar = nil, w: AhkVar = nil, h: AhkVar = nil): AhkGui =
  var cx: int32 = 0
  var cy: int32 = 0
  var cw: int32 = 400
  var ch: int32 = 300
  
  when defined(windows):
    if self.hwnd != 0:
      var rect = RECT()
      if GetWindowRect(self.hwnd, &rect) != 0:
        cx = rect.left
        cy = rect.top
        cw = rect.right - rect.left
        ch = rect.bottom - rect.top

  if x != nil: cx = x.toInt32()
  if y != nil: cy = y.toInt32()
  if w != nil: cw = w.toInt32()
  if h != nil: ch = h.toInt32()
  
  when defined(windows):
    if self.hwnd != 0:
      discard MoveWindow(self.hwnd, cx, cy, cw, ch, 1)
  return self

proc Move*(self: AhkVar, x: AhkVar = nil, y: AhkVar = nil, w: AhkVar = nil, h: AhkVar = nil): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      discard AhkControl(self.oVal).Move(x, y, w, h)
    elif self.oVal of AhkGui:
      discard AhkGui(self.oVal).Move(x, y, w, h)
  return self


proc GuiCtrlFromHwnd*(hwnd: AhkVar): AhkVar =
  if hwnd == nil: return nil
  let targetHwnd = cast[HWND](hwnd.toInt32())
  for ctrl in activeControls:
    if ctrl.hwnd == targetHwnd:
      return toAhkVar(ctrl)
  return nil

proc Redraw*(self: AhkControl): AhkControl =
  when defined(windows):
    if self.hwnd != 0:
      RedrawWindow(self.hwnd, nil, 0, RDW_INVALIDATE or RDW_UPDATENOW or RDW_ERASE or RDW_ALLCHILDREN)
  return self

proc Redraw*(self: AhkGui): AhkGui =
  when defined(windows):
    if self.hwnd != 0:
      RedrawWindow(self.hwnd, nil, 0, RDW_INVALIDATE or RDW_UPDATENOW or RDW_ERASE or RDW_ALLCHILDREN)
  return self

proc Redraw*(self: AhkVar): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      discard AhkControl(self.oVal).Redraw()
    elif self.oVal of AhkGui:
      discard AhkGui(self.oVal).Redraw()
  return self

proc Add*(self: AhkVar, controlType: string, options: string = "", text: string = ""): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkGui:
    return toAhkVar(AhkGui(self.oVal).Add(controlType, options, text))
  return nil

proc AddText*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Text", options, text)

proc AddEdit*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Edit", options, text)

proc AddButton*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Button", options, text)

proc AddCheckbox*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Checkbox", options, text)

proc AddRadio*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Radio", options, text)

proc AddProgress*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Progress", options, text)

proc AddProgress*(self: AhkVar, options: string = "", value: AhkVar = nil): AhkVar =
  let valStr = if value == nil: "" else: value.toString()
  return self.Add("Progress", options, valStr)

proc AddDropDownList*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("DropDownList", options, text)

proc AddComboBox*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("ComboBox", options, text)

proc AddListBox*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("ListBox", options, text)

proc AddGroupBox*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("GroupBox", options, text)

proc AddPicture*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Picture", options, text)

proc AddPic*(self: AhkVar, options: string = "", text: string = ""): AhkVar =
  return self.Add("Picture", options, text)

proc OnEvent*(self: AhkVar, eventName: string, callback: proc(): AhkVar): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      return AhkControl(self.oVal).OnEvent(eventName, callback)
    elif self.oVal of AhkGui:
      return AhkGui(self.oVal).OnEvent(eventName, callback)
  return nil

proc OnEvent*(self: AhkVar, eventName: string, callback: proc(ctrl: AhkVar): AhkVar): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      return AhkControl(self.oVal).OnEvent(eventName, callback)
    elif self.oVal of AhkGui:
      return AhkGui(self.oVal).OnEvent(eventName, callback)
  return nil

proc OnEvent*(self: AhkVar, eventName: string, callback: proc(ctrl: AhkVar, info: AhkVar): AhkVar): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      return AhkControl(self.oVal).OnEvent(eventName, callback)
  return nil

proc Submit*(self: AhkGui, hide: AhkVar = nil): AhkVar =
  let shouldHide = if hide == nil: true else: hide.toBool()
  when defined(windows):
    if shouldHide:
      ShowWindow(self.hwnd, SW_HIDE)
  
  var res = Map()
  for ctrl in self.controls:
    var name = ""
    if ctrl.properties.contains("name"):
      name = ctrl.properties["name"].toString()
    elif ctrl.properties.contains("v"):
      name = ctrl.properties["v"].toString()
      
    if name != "":
      let val = toAhkVar(ctrl)["value"]
      res[name] = val
      
  return res

proc Submit*(self: AhkVar, hide: AhkVar = nil): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkGui:
    return AhkGui(self.oVal).Submit(hide)
  return nil

proc Show*(self: AhkVar, options: string = ""): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkGui:
      return AhkGui(self.oVal).Show(options)
    elif self.oVal of AhkMenu:
      return AhkMenu_Show(self, toAhkVar(options))
  return nil

proc SetFont*(self: AhkGui, options: string = "", fontName: string = ""): AhkVar =
  when defined(windows):
    var size = 10
    var weight: int32 = FW_NORMAL
    var italic: byte = 0
    var underline: byte = 0
    var strike: byte = 0
    var col: int32 = -1
    
    if options != "":
      let parts = options.split(Whitespace)
      for part in parts:
        let p = part.strip().toLowerAscii()
        if p == "": continue
        if p.startsWith("s"):
          try: size = parseInt(p[1..^1])
          except: discard
        elif p == "bold":
          weight = FW_BOLD
        elif p == "italic":
          italic = 1
        elif p == "underline":
          underline = 1
        elif p == "strike":
          strike = 1
        elif p.startsWith("c") and not p.startsWith("center"):
          col = parseColor(p)
          
    if col != -1:
      self.textColor = col
          
    let hdc = GetDC(self.hwnd)
    let dpiY = if hdc != 0: GetDeviceCaps(hdc, LOGPIXELSY) else: 96
    if hdc != 0: ReleaseDC(self.hwnd, hdc)
    
    let height = -MulDiv(size.int32, dpiY, 72)
    
    let faceName = if fontName != "": fontName else: "Segoe UI"
    
    let hFont = CreateFont(
      height, 0, 0, 0,
      weight, cast[DWORD](italic), cast[DWORD](underline), cast[DWORD](strike),
      cast[DWORD](DEFAULT_CHARSET), cast[DWORD](OUT_DEFAULT_PRECIS), cast[DWORD](CLIP_DEFAULT_PRECIS),
      cast[DWORD](DEFAULT_QUALITY), cast[DWORD](DEFAULT_PITCH or FF_DONTCARE),
      newWideCString(faceName)
    )
    if hFont != 0:
      self.font = hFont
  return nil

proc SetFont*(self: AhkVar, options: string = "", fontName: string = ""): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkGui:
    return AhkGui(self.oVal).SetFont(options, fontName)
  return nil

macro GetPos*(self: AhkControl | AhkGui | AhkVar, x: untyped = nil, y: untyped = nil, width: untyped = nil, height: untyped = nil): untyped =
  result = newStmtList()
  
  proc getVarNode(n: NimNode): NimNode =
    if n.kind == nnkPrefix and $n[0] == "&": return n[1]
    return n
    
  let xVar = getVarNode(x)
  let yVar = getVarNode(y)
  let wVar = getVarNode(width)
  let hVar = getVarNode(height)
  
  let s = genSym(nskVar, "s")
  result.add(quote do:
    var `s` = `self`
  )
  
  if xVar.kind != nnkNilLit:
    result.add(quote do:
      if `s` != nil:
        when `s` is AhkVar:
          if `s`.kind == akObject and `s`.oVal != nil:
            if `s`.oVal of AhkControl:
              `xVar` = toAhkVar(AhkControl(`s`.oVal).x)
            elif `s`.oVal of AhkGui:
              when defined(windows):
                var r: RECT
                GetWindowRect(AhkGui(`s`.oVal).hwnd, &r)
                `xVar` = toAhkVar(r.left.int)
        elif `s` is AhkControl:
          `xVar` = toAhkVar(`s`.x)
        elif `s` is AhkGui:
          when defined(windows):
            var r: RECT
            GetWindowRect(`s`.hwnd, &r)
            `xVar` = toAhkVar(r.left.int)
    )
    
  if yVar.kind != nnkNilLit:
    result.add(quote do:
      if `s` != nil:
        when `s` is AhkVar:
          if `s`.kind == akObject and `s`.oVal != nil:
            if `s`.oVal of AhkControl:
              `yVar` = toAhkVar(AhkControl(`s`.oVal).y)
            elif `s`.oVal of AhkGui:
              when defined(windows):
                var r: RECT
                GetWindowRect(AhkGui(`s`.oVal).hwnd, &r)
                `yVar` = toAhkVar(r.top.int)
        elif `s` is AhkControl:
          `yVar` = toAhkVar(`s`.y)
        elif `s` is AhkGui:
          when defined(windows):
            var r: RECT
            GetWindowRect(`s`.hwnd, &r)
            `yVar` = toAhkVar(r.top.int)
    )
    
  if wVar.kind != nnkNilLit:
    result.add(quote do:
      if `s` != nil:
        when `s` is AhkVar:
          if `s`.kind == akObject and `s`.oVal != nil:
            if `s`.oVal of AhkControl:
              `wVar` = toAhkVar(AhkControl(`s`.oVal).width)
            elif `s`.oVal of AhkGui:
              when defined(windows):
                var r: RECT
                GetWindowRect(AhkGui(`s`.oVal).hwnd, &r)
                `wVar` = toAhkVar((r.right - r.left).int)
        elif `s` is AhkControl:
          `wVar` = toAhkVar(`s`.width)
        elif `s` is AhkGui:
          when defined(windows):
            var r: RECT
            GetWindowRect(`s`.hwnd, &r)
            `wVar` = toAhkVar((r.right - r.left).int)
    )
    
  if hVar.kind != nnkNilLit:
    result.add(quote do:
      if `s` != nil:
        when `s` is AhkVar:
          if `s`.kind == akObject and `s`.oVal != nil:
            if `s`.oVal of AhkControl:
              `hVar` = toAhkVar(AhkControl(`s`.oVal).height)
            elif `s`.oVal of AhkGui:
              when defined(windows):
                var r: RECT
                GetWindowRect(AhkGui(`s`.oVal).hwnd, &r)
                `hVar` = toAhkVar((r.bottom - r.top).int)
        elif `s` is AhkControl:
          `hVar` = toAhkVar(`s`.height)
        elif `s` is AhkGui:
          when defined(windows):
            var r: RECT
            GetWindowRect(`s`.hwnd, &r)
            `hVar` = toAhkVar((r.bottom - r.top).int)
    )
    
  result.add(quote do:
    AhkVar(kind: akNull)
  )

macro MouseGetPos*(x: untyped = nil, y: untyped = nil, win: untyped = nil, control: untyped = nil, flag: untyped = nil): untyped =
  result = newStmtList()
  
  proc getVarNode(n: NimNode): NimNode =
    if n.kind == nnkPrefix and $n[0] == "&": return n[1]
    return n
    
  let xVar = getVarNode(x)
  let yVar = getVarNode(y)
  let winVar = getVarNode(win)
  let ctrlVar = getVarNode(control)
  
  let ptSym = genSym(nskVar, "pt")
  result.add(quote do:
    when defined(windows):
      var `ptSym`: POINT
      GetCursorPos(&`ptSym`)
  )
  
  if xVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `xVar` = toAhkVar(cast[int](`ptSym`.x))
    )
  if yVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `yVar` = toAhkVar(cast[int](`ptSym`.y))
    )
  if winVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        let hwnd = WindowFromPoint(`ptSym`)
        `winVar` = toAhkVar(cast[int](hwnd))
    )
  if ctrlVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        let hwnd = WindowFromPoint(`ptSym`)
        `ctrlVar` = toAhkVar(cast[int](hwnd))
    )
    
  result.add(quote do:
    AhkVar(kind: akNull)
  )

when defined(windows):
  type MonitorEnumData = object
    targetIndex: int
    currentIndex: int
    rect: RECT
    found: bool

  proc monitorEnumProc(hMonitor: HMONITOR, hdcMonitor: HDC, lprcMonitor: LPRECT, dwData: LPARAM): WINBOOL {.stdcall.} =
    let data = cast[ptr MonitorEnumData](dwData)
    data.currentIndex += 1
    if data.currentIndex == data.targetIndex:
      data.rect = lprcMonitor[]
      data.found = true
      return false
    return true

  proc getMonitorRect*(targetIndex: int, r: var RECT): bool =
    var data = MonitorEnumData(targetIndex: targetIndex, currentIndex: 0, found: false)
    EnumDisplayMonitors(0, nil, cast[MONITORENUMPROC](monitorEnumProc), cast[LPARAM](addr data))
    if data.found:
      r = data.rect
      return true
    return false

proc MonitorGetCount*(): AhkVar =
  when defined(windows):
    return toAhkVar(GetSystemMetrics(80))
  else:
    return toAhkVar(1)

macro MonitorGet*(index: untyped = nil, left: untyped = nil, top: untyped = nil, right: untyped = nil, bottom: untyped = nil): untyped =
  result = newStmtList()
  
  proc getVarNode(n: NimNode): NimNode =
    if n.kind == nnkPrefix and $n[0] == "&": return n[1]
    return n

  let lVar = getVarNode(left)
  let tVar = getVarNode(top)
  let rVar = getVarNode(right)
  let bVar = getVarNode(bottom)

  let indexVal = if index.kind == nnkNilLit: quote do: 1 else: quote do: `index`.toInt32().int
  
  let rectSym = genSym(nskVar, "r")
  result.add(quote do:
    when defined(windows):
      var `rectSym`: RECT
      let idx = `indexVal`
      let targetIdx = if idx <= 0: 1 else: idx
      if getMonitorRect(targetIdx, `rectSym`):
        discard
      else:
        `rectSym`.left = 0
        `rectSym`.top = 0
        `rectSym`.right = GetSystemMetrics(0)
        `rectSym`.bottom = GetSystemMetrics(1)
  )

  if lVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `lVar` = toAhkVar(cast[int](`rectSym`.left))
    )
  if tVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `tVar` = toAhkVar(cast[int](`rectSym`.top))
    )
  if rVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `rVar` = toAhkVar(cast[int](`rectSym`.right))
    )
  if bVar.kind != nnkNilLit:
    result.add(quote do:
      when defined(windows):
        `bVar` = toAhkVar(cast[int](`rectSym`.bottom))
    )

  result.add(quote do:
    AhkVar(kind: akNull)
  )


# --- Additional AHK Built-ins and Helpers ---

randomize()

template `&`*(v: var AhkVar): var AhkVar = v

template A_ScriptDir*(): AhkVar = toAhkVar(getAppDir())
template A_Desktop*(): AhkVar = toAhkVar(getHomeDir() / "Desktop")
template A_Temp*(): AhkVar = toAhkVar(getTempDir())
template A_WorkingDir*(): AhkVar = toAhkVar(getCurrentDir())
template A_Now*(): AhkVar = toAhkVar(now().format("yyyyMMddHHmmss"))
template A_TickCount*(): AhkVar =
  when defined(windows):
    toAhkVar(GetTickCount().int)
  else:
    toAhkVar(0)
template A_ComSpec*(): AhkVar = toAhkVar(getEnv("COMSPEC"))
template A_OSVersion*(): AhkVar = toAhkVar("10.0.22000")
template A_PtrSize*(): AhkVar = toAhkVar(sizeof(pointer))
template A_IsCompiled*(): AhkVar = toAhkVar(true)
template A_ScreenWidth*(): AhkVar =
  when defined(windows):
    toAhkVar(GetSystemMetrics(0))
  else:
    toAhkVar(1920)
template A_ScreenHeight*(): AhkVar =
  when defined(windows):
    toAhkVar(GetSystemMetrics(1))
  else:
    toAhkVar(1080)

proc ExitApp*(code: AhkVar = 0): AhkVar =
  quit(code.toInt32().int)

proc Sleep*(ms: AhkVar): AhkVar =
  os.sleep(ms.toInt32().int)
  return nil

proc Sleep*(ms: int): AhkVar =
  os.sleep(ms)
  return nil

proc keyNameToVk(name: string): int32 =
  let n = name.toLowerAscii().strip()
  case n
  of "lbutton": return 0x01
  of "rbutton": return 0x02
  of "mbutton": return 0x04
  of "ctrl", "control": return 0x11
  of "alt": return 0x12
  of "shift": return 0x10
  of "enter", "return": return 0x0D
  of "escape", "esc": return 0x1B
  of "space": return 0x20
  of "tab": return 0x09
  of "backspace", "bs": return 0x08
  of "capslock": return 0x14
  of "numlock": return 0x90
  of "scrolllock": return 0x91
  else:
    if n.len == 1:
      let c = n[0]
      if c >= 'a' and c <= 'z':
        return cast[int32](ord(c) - 32)
      elif c >= '0' and c <= '9':
        return cast[int32](ord(c))
    return 0

proc GetKeyState*(keyName: AhkVar, mode: AhkVar = nil): AhkVar =
  if keyName == nil: return toAhkVar(false)
  let vk = keyNameToVk(keyName.toString())
  if vk == 0: return toAhkVar(false)
  
  when defined(windows):
    let m = if mode == nil: "" else: mode.toString().toLowerAscii()
    if m == "t":
      let state = GetKeyState(vk)
      return toAhkVar((state and 1) != 0)
    else:
      let state = GetAsyncKeyState(vk)
      return toAhkVar(state < 0)
  else:
    return toAhkVar(false)

proc KeyWait*(keyName: AhkVar, options: AhkVar = nil): AhkVar =
  if keyName == nil: return toAhkVar(0)
  let vk = keyNameToVk(keyName.toString())
  if vk == 0: return toAhkVar(0)
  
  var waitMode = "U"
  var timeout = -1.0
  var logical = false
  
  if options != nil:
    let optStr = options.toString().toLowerAscii()
    if "d" in optStr:
      waitMode = "D"
    if "l" in optStr:
      logical = true
      
    for p in optStr.split(Whitespace):
      if p.startsWith("t"):
        try:
          timeout = parseFloat(p[1..^1])
        except:
          discard
          
  let startTime = epochTime()
  
  while true:
    var isDown = false
    when defined(windows):
      if logical:
        isDown = GetKeyState(vk) < 0
      else:
        isDown = GetAsyncKeyState(vk) < 0
        
    let conditionMet = if waitMode == "D": isDown else: not isDown
    if conditionMet:
      return toAhkVar(1)
      
    if timeout > 0.0:
      if epochTime() - startTime >= timeout:
        return toAhkVar(0)
        
    os.sleep(10)

proc Max*(a, b: AhkVar): AhkVar =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
    return toAhkVar(max(af, bf))
  else:
    let ai = if a.kind == akInt: a.iVal else: parseInt(a.toString())
    let bi = if b.kind == akInt: b.iVal else: parseInt(b.toString())
    return toAhkVar(max(ai, bi))

proc Min*(a, b: AhkVar): AhkVar =
  if a.kind == akFloat or b.kind == akFloat:
    let af = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: parseFloat(a.toString()))
    let bf = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: parseFloat(b.toString()))
    return toAhkVar(min(af, bf))
  else:
    let ai = if a.kind == akInt: a.iVal else: parseInt(a.toString())
    let bi = if b.kind == akInt: b.iVal else: parseInt(b.toString())
    return toAhkVar(min(ai, bi))

proc SubStr*(str: AhkVar, start: AhkVar, length: AhkVar = nil): AhkVar =
  let s = str.toString()
  var st = start.toInt32().int
  if st < 0:
    st = s.len + st + 1
  if st < 1: st = 1
  if st > s.len: return toAhkVar("")
  let idx = st - 1
  if length == nil:
    return toAhkVar(s[idx .. ^1])
  else:
    let L = length.toInt32().int
    if L <= 0: return toAhkVar("")
    let endIdx = min(s.len - 1, idx + L - 1)
    if endIdx < idx: return toAhkVar("")
    return toAhkVar(s[idx .. endIdx])

proc StrReplace*(str: AhkVar, search: AhkVar, replacement: AhkVar = ""): AhkVar =
  return toAhkVar(str.toString().replace(search.toString(), replacement.toString()))

proc Trim*(str: AhkVar): AhkVar =
  return toAhkVar(str.toString().strip())

proc StrLower*(str: AhkVar): AhkVar =
  return toAhkVar(str.toString().toLowerAscii())

proc StrSplit*(str: AhkVar, delimiters: AhkVar = nil, omitChars: AhkVar = nil, maxParts: AhkVar = nil): AhkVar =
  let s = str.toString()
  let omit = if omitChars == nil: "" else: omitChars.toString()
  let maxP = if maxParts == nil: -1 else: maxParts.toInt32()
  
  var parts = AhkArray()
  
  proc addPart(item: string) =
    var processed = item
    if omit != "":
      var omitSet: set[char] = {}
      for c in omit: omitSet.incl(c)
      processed = processed.strip(chars = omitSet)
    discard parts.Push(toAhkVar(processed))

  if delimiters == nil or delimiters.kind == akNull:
    var count = 0
    for c in s:
      if omit != "" and omit.contains(c):
        continue
      addPart($c)
      count += 1
      if maxP > 0 and count >= maxP:
        break
  elif delimiters.kind == akArray:
    var delims: seq[string] = @[]
    for d in delimiters.aVal:
      delims.add(d.toString())
    
    var tempS = s
    let placeholder = "\x01"
    for d in delims:
      if d != "":
        tempS = tempS.replace(d, placeholder)
    
    let splitted = tempS.split(placeholder)
    var count = 0
    for item in splitted:
      addPart(item)
      count += 1
      if maxP > 0 and count >= maxP:
        break
  else:
    let del = delimiters.toString()
    if del == "":
      var count = 0
      for c in s:
        if omit != "" and omit.contains(c):
          continue
        addPart($c)
        count += 1
        if maxP > 0 and count >= maxP:
          break
    else:
      let splitted = s.split(del)
      var count = 0
      for item in splitted:
        addPart(item)
        count += 1
        if maxP > 0 and count >= maxP:
          break
          
  return parts

proc ahk_Mod*(a, b: AhkVar): AhkVar =
  if a == nil or b == nil:
    return toAhkVar(0)
  
  let isFloat = a.kind == akFloat or b.kind == akFloat
  if isFloat:
    let va = if a.kind == akFloat: a.fVal else: (if a.kind == akInt: a.iVal.float else: (try: parseFloat(a.toString()) except: 0.0))
    let vb = if b.kind == akFloat: b.fVal else: (if b.kind == akInt: b.iVal.float else: (try: parseFloat(b.toString()) except: 1.0))
    if vb == 0.0:
      return toAhkVar(0.0)
    let res = va - vb * floor(va / vb)
    return toAhkVar(res)
  else:
    let va = if a.kind == akInt: a.iVal else: (try: parseInt(a.toString()) except: 0)
    let vb = if b.kind == akInt: b.iVal else: (try: parseInt(b.toString()) except: 1)
    if vb == 0:
      return toAhkVar(0)
    return toAhkVar(va mod vb)


proc VerCompare*(v1, v2: AhkVar): AhkVar =
  let s1 = v1.toString().split('.')
  let s2 = v2.toString().split('.')
  for i in 0 .. max(s1.len, s2.len) - 1:
    let p1 = if i < s1.len: (try: parseInt(s1[i]) except: 0) else: 0
    let p2 = if i < s2.len: (try: parseInt(s2[i]) except: 0) else: 0
    if p1 < p2: return toAhkVar(-1)
    if p1 > p2: return toAhkVar(1)
  return toAhkVar(0)

proc EnvGet*(name: AhkVar): AhkVar =
  return toAhkVar(getEnv(name.toString()))

proc EnvSet*(name, value: AhkVar): AhkVar =
  putEnv(name.toString(), value.toString())
  return nil

proc SysGet*(index: AhkVar): AhkVar =
  if index == nil: return nil
  when defined(windows):
    return toAhkVar(GetSystemMetrics(index.toInt32()))
  else:
    return toAhkVar(0)

proc SetWorkingDir*(dir: AhkVar): AhkVar =
  if dir == nil: return nil
  try:
    setCurrentDir(dir.toString())
  except:
    discard
  return nil

proc DirExist*(path: AhkVar): AhkVar =
  return toAhkVar(dirExists(path.toString()))

proc DirCreate*(path: AhkVar): AhkVar =
  createDir(path.toString())
  return nil

proc FileExist*(path: AhkVar): AhkVar =
  let p = path.toString()
  if dirExists(p): return toAhkVar("D")
  if fileExists(p): return toAhkVar("A")
  return toAhkVar("")

proc FileGetAttrib*(path: AhkVar = nil): AhkVar =
  let p = if path == nil: "" else: path.toString()
  when defined(windows):
    let attrs = GetFileAttributesW(newWideCString(p))
    if attrs == INVALID_FILE_ATTRIBUTES:
      return toAhkVar("")
    var resultStr = ""
    if (attrs and FILE_ATTRIBUTE_READONLY) != 0: resultStr &= "R"
    if (attrs and FILE_ATTRIBUTE_ARCHIVE) != 0: resultStr &= "A"
    if (attrs and FILE_ATTRIBUTE_SYSTEM) != 0: resultStr &= "S"
    if (attrs and FILE_ATTRIBUTE_HIDDEN) != 0: resultStr &= "H"
    if (attrs and FILE_ATTRIBUTE_DIRECTORY) != 0: resultStr &= "D"
    if (attrs and FILE_ATTRIBUTE_NORMAL) != 0: resultStr &= "N"
    if (attrs and FILE_ATTRIBUTE_OFFLINE) != 0: resultStr &= "O"
    if (attrs and FILE_ATTRIBUTE_TEMPORARY) != 0: resultStr &= "T"
    return toAhkVar(resultStr)
  else:
    if dirExists(p): return toAhkVar("D")
    elif fileExists(p): return toAhkVar("A")
    else: return toAhkVar("")

proc FileDelete*(path: AhkVar): AhkVar =
  try:
    removeFile(path.toString())
  except: discard
  return nil

proc DirDelete*(path: AhkVar, recurse: AhkVar = false): AhkVar =
  try:
    if recurse.toBool():
      removeDir(path.toString())
    else:
      removeDir(path.toString())
  except: discard
  return nil

proc FileCopy*(src, dest: AhkVar, overwrite: AhkVar = false): AhkVar =
  try:
    copyFile(src.toString(), dest.toString())
  except: discard
  return nil

proc FileMove*(src, dest: AhkVar, overwrite: AhkVar = false): AhkVar =
  try:
    let s = src.toString()
    let d = dest.toString()
    if overwrite.toBool() and fileExists(d):
      removeFile(d)
    moveFile(s, d)
  except: discard
  return nil

proc DirMove*(src, dest: AhkVar, flag: AhkVar = 0): AhkVar =
  try:
    let s = src.toString()
    let d = dest.toString()
    let f = if flag == nil: "0" else: flag.toString().toLowerAscii()
    if (f == "1" or f == "r") and dirExists(d):
      removeDir(d)
    moveDir(s, d)
  except: discard
  return nil

proc FormatTime*(timestamp: AhkVar = nil, format: AhkVar = nil): AhkVar =
  let fmt = if format == nil or format.toString() == "": "yyyy-MM-dd HH:mm:ss" else: format.toString()
  var t = now()
  if timestamp != nil and timestamp.toString() != "":
    let ts = timestamp.toString()
    if ts.len >= 14:
      try: t = parse(ts[0..13], "yyyyMMddHHmmss").local except: discard
    elif ts.len >= 8:
      try: t = parse(ts[0..7] & "000000", "yyyyMMddHHmmss").local except: discard
  return toAhkVar(t.format(fmt))

proc IniRead*(filename, section, key: AhkVar, defaultVal: AhkVar = nil): AhkVar =
  let fn = filename.toString()
  let sec = section.toString()
  let k = key.toString()
  let df = if defaultVal == nil: "" else: defaultVal.toString()
  when defined(windows):
    var buf = newSeq[WCHAR](1024)
    let length = GetPrivateProfileStringW(
      newWideCString(sec),
      newWideCString(k),
      newWideCString(df),
      addr buf[0],
      1024,
      newWideCString(fn)
    )
    if length > 0:
      return toAhkVar($cast[LPCWSTR](addr buf[0]))
    return toAhkVar(df)
  else:
    return toAhkVar(df)

proc IniWrite*(value, filename, section, key: AhkVar): AhkVar =
  let val = value.toString()
  let fn = filename.toString()
  let sec = section.toString()
  let k = key.toString()
  when defined(windows):
    discard WritePrivateProfileStringW(
      newWideCString(sec),
      newWideCString(k),
      newWideCString(val),
      newWideCString(fn)
    )
  return nil

proc FileAppend*(text: AhkVar, filename: AhkVar, encoding: AhkVar = nil): AhkVar =
  let txt = text.toString()
  let fn = filename.toString()
  if fn == "*":
    stdout.write(txt)
    stdout.flushFile()
  else:
    try:
      let f = open(fn, fmAppend)
      f.write(txt)
      f.close()
    except: discard
  return nil

proc FileRead*(path: AhkVar): AhkVar =
  try:
    return toAhkVar(readFile(path.toString()))
  except:
    return toAhkVar("")

proc FileGetSize*(path: AhkVar): AhkVar =
  try:
    return toAhkVar(getFileSize(path.toString()).int)
  except:
    return toAhkVar(0)

proc FileGetTime*(path: AhkVar, whichTime: AhkVar = nil): AhkVar =
  let p = path.toString()
  let wt = if whichTime == nil: "m" else: whichTime.toString().toLowerAscii()
  try:
    if wt == "m":
      let mTime = getLastModificationTime(p)
      return toAhkVar(mTime.local.format("yyyyMMddHHmmss"))
    elif wt == "c":
      when defined(windows):
        var fileInfo: BY_HANDLE_FILE_INFORMATION
        let hFile = CreateFileW(
          newWideCString(p),
          GENERIC_READ,
          FILE_SHARE_READ or FILE_SHARE_WRITE or FILE_SHARE_DELETE,
          nil,
          OPEN_EXISTING,
          FILE_FLAG_BACKUP_SEMANTICS,
          0
        )
        if hFile != INVALID_HANDLE_VALUE:
          defer: CloseHandle(hFile)
          if GetFileInformationByHandle(hFile, &fileInfo) != 0:
            var systemTime: SYSTEMTIME
            var localTime: FILETIME
            discard FileTimeToLocalFileTime(&fileInfo.ftCreationTime, &localTime)
            discard FileTimeToSystemTime(&localTime, &systemTime)
            return toAhkVar(format("$1$2$3$4$5$6", 
              align($systemTime.wYear, 4, '0'),
              align($systemTime.wMonth, 2, '0'),
              align($systemTime.wDay, 2, '0'),
              align($systemTime.wHour, 2, '0'),
              align($systemTime.wMinute, 2, '0'),
              align($systemTime.wSecond, 2, '0')
            ))
      let mTime = getLastModificationTime(p)
      return toAhkVar(mTime.local.format("yyyyMMddHHmmss"))
    elif wt == "a":
      when defined(windows):
        var fileInfo: BY_HANDLE_FILE_INFORMATION
        let hFile = CreateFileW(
          newWideCString(p),
          GENERIC_READ,
          FILE_SHARE_READ or FILE_SHARE_WRITE or FILE_SHARE_DELETE,
          nil,
          OPEN_EXISTING,
          FILE_FLAG_BACKUP_SEMANTICS,
          0
        )
        if hFile != INVALID_HANDLE_VALUE:
          defer: CloseHandle(hFile)
          if GetFileInformationByHandle(hFile, &fileInfo) != 0:
            var systemTime: SYSTEMTIME
            var localTime: FILETIME
            discard FileTimeToLocalFileTime(&fileInfo.ftLastAccessTime, &localTime)
            discard FileTimeToSystemTime(&localTime, &systemTime)
            return toAhkVar(format("$1$2$3$4$5$6", 
              align($systemTime.wYear, 4, '0'),
              align($systemTime.wMonth, 2, '0'),
              align($systemTime.wDay, 2, '0'),
              align($systemTime.wHour, 2, '0'),
              align($systemTime.wMinute, 2, '0'),
              align($systemTime.wSecond, 2, '0')
            ))
      let mTime = getLastModificationTime(p)
      return toAhkVar(mTime.local.format("yyyyMMddHHmmss"))
    else:
      let mTime = getLastModificationTime(p)
      return toAhkVar(mTime.local.format("yyyyMMddHHmmss"))
  except:
    return toAhkVar("")

proc Round*(num: AhkVar, places: AhkVar = 0): AhkVar =
  let n = if num.kind == akFloat: num.fVal else: (try: parseFloat(num.toString()) except: 0.0)
  let p = if places == nil: 0 else: places.toInt32().int
  if p == 0:
    return toAhkVar(round(n).int)
  else:
    return toAhkVar(round(n, p))

proc Floor*(num: AhkVar): AhkVar =
  let n = if num.kind == akFloat: num.fVal else: (try: parseFloat(num.toString()) except: 0.0)
  return toAhkVar(floor(n).int)

proc Ceil*(num: AhkVar): AhkVar =
  let n = if num.kind == akFloat: num.fVal else: (try: parseFloat(num.toString()) except: 0.0)
  return toAhkVar(ceil(n).int)

proc Sort*(str: AhkVar, options: AhkVar = nil, callback: AhkVar = nil): AhkVar =
  let s = str.toString()
  let opts = if options == nil: "" else: options.toString().toUpperAscii()
  
  var delim = "\n"
  var patternStr = opts
  let dIdx = patternStr.find('D')
  if dIdx != -1 and dIdx + 1 < patternStr.len:
    delim = $patternStr[dIdx + 1]
    
  var items = s.split(delim)
  if items.len > 0 and items[^1] == "" and s.endsWith(delim):
    items.setLen(items.len - 1)
    
  let isNumeric = opts.contains("N")
  let isReverse = opts.contains("R")
  let isUnique = opts.contains("U")
  
  if isNumeric:
    proc numCompare(x, y: string): int =
      var valX = 0.0
      var valY = 0.0
      let partsX = x.split('|')
      let partsY = y.split('|')
      try: valX = parseFloat(partsX[0]) except: discard
      try: valY = parseFloat(partsY[0]) except: discard
      if valX < valY: return -1
      elif valX > valY: return 1
      else: return 0
      
    items.sort(numCompare)
  else:
    let caseSensitive = opts.contains("C")
    if caseSensitive:
      items.sort()
    else:
      proc insensCompare(x, y: string): int =
        cmp(x.toLowerAscii(), y.toLowerAscii())
      items.sort(insensCompare)
      
  if isReverse:
    var revItems = newSeq[string](items.len)
    for idx in 0 ..< items.len:
      revItems[idx] = items[items.len - 1 - idx]
    items = revItems
    
  if isUnique:
    var uniqueItems = newSeq[string]()
    for item in items:
      if uniqueItems.len == 0 or uniqueItems[^1] != item:
        uniqueItems.add(item)
    items = uniqueItems
    
  var resultStr = items.join(delim)
  if s.endsWith(delim) and items.len > 0:
    resultStr &= delim
  return toAhkVar(resultStr)

proc Abs*(num: AhkVar): AhkVar =
  if num.kind == akFloat:
    return toAhkVar(abs(num.fVal))
  else:
    return toAhkVar(abs(num.toInt32().int))

proc Random*(min, max: AhkVar): AhkVar =
  let mn = min.toInt32().int
  let mx = max.toInt32().int
  let r = mn + rand(mx - mn)
  return toAhkVar(r)

proc RegExMatch*(str, pattern: AhkVar, match: var AhkVar, startingPos: AhkVar = nil): AhkVar =
  when defined(windows):
    let s = str.toString()
    var patternStr = pattern.toString()
    
    var ignoreCase = false
    var multiline = false
    
    # Parse options prefix
    let closeParenIdx = patternStr.find(')')
    if closeParenIdx > 0:
      var isValidOptions = true
      var optIgnoreCase = false
      var optMultiline = false
      for j in 0 ..< closeParenIdx:
        let c = patternStr[j]
        if c == 'i' or c == 'I': optIgnoreCase = true
        elif c == 'm' or c == 'M': optMultiline = true
        elif c in {'o', 'O', 's', 'S', 'x', 'X', '`', 'a', 'A', 'c', 'C', 'd', 'D', 'j', 'J', 'p', 'P', 'u', 'U'}: discard
        else:
          isValidOptions = false
          break
      if isValidOptions:
        ignoreCase = optIgnoreCase
        multiline = optMultiline
        patternStr = patternStr[closeParenIdx + 1 .. ^1]

    var start = 1
    if startingPos != nil:
      let spVal = startingPos.toInt32().int
      if spVal < 0:
        start = max(1, s.len + spVal + 1)
      else:
        start = max(1, spVal)

    if start > s.len:
      match = nil
      return toAhkVar(0)

    let searchStr = s[start - 1 .. ^1]

    try:
      var regex = CreateObject("VBScript.RegExp")
      regex.pattern = patternStr
      regex.global = false # We only need the first match in RegExMatch
      regex.ignoreCase = ignoreCase
      regex.multiline = multiline
      
      var matches = regex.execute(searchStr)
      if matches.count > 0:
        let m = matches.item(0)
        let firstIdx = m.firstIndex.int
        let absPos = firstIdx + start
        
        # Populate match variable
        var mTable = initTable[string, AhkVar]()
        mTable["0"] = toAhkVar(m.value.string)
        
        let submatches = m.submatches
        for idx in 0 ..< submatches.count:
          let subVal = submatches.item(idx)
          mTable[$(idx + 1)] = toAhkVar(subVal.string)
          
        match = AhkVar(kind: akMap, mVal: mTable)
        return toAhkVar(absPos)
      else:
        match = nil
        return toAhkVar(0)
    except:
      match = nil
      return toAhkVar(0)
  else:
    match = nil
    return toAhkVar(0)

proc RegExMatch*(str, pattern: AhkVar): AhkVar =
  var dummy: AhkVar
  return RegExMatch(str, pattern, dummy, nil)

macro SplitPath*(path: AhkVar, fileName: untyped = nil, dir: untyped = nil, ext: untyped = nil, nameNoExt: untyped = nil, drive: untyped = nil): untyped =
  result = newStmtList()
  let p = genSym(nskLet, "p")
  let dSym = genSym(nskLet, "d")
  let nSym = genSym(nskLet, "n")
  let eSym = genSym(nskLet, "e")
  result.add(quote do:
    let `p` = `path`.toString()
    let (`dSym`, `nSym`, `eSym`) = splitFile(`p`)
  )
  if fileName.kind != nnkNilLit:
    result.add(quote do:
      `fileName` = toAhkVar(`nSym` & `eSym`)
    )
  if dir.kind != nnkNilLit:
    result.add(quote do:
      `dir` = toAhkVar(`dSym`)
    )
  if ext.kind != nnkNilLit:
    result.add(quote do:
      `ext` = toAhkVar(if `eSym`.startsWith("."): `eSym`[1..^1] else: `eSym`)
    )
  if nameNoExt.kind != nnkNilLit:
    result.add(quote do:
      `nameNoExt` = toAhkVar(`nSym`)
    )
  if drive.kind != nnkNilLit:
    result.add(quote do:
      `drive` = toAhkVar(if `dSym`.len >= 2 and `dSym`[1] == ':': `dSym`[0..1] else: "")
    )
  result.add(quote do:
    AhkVar(kind: akNull)
  )
var wideStrCache {.threadvar.}: seq[WideCString]

proc StrPtr*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(0)
  if wideStrCache.len > 16:
    wideStrCache.delete(0)
  let w = newWideCString(v.toString())
  wideStrCache.add(w)
  let ptrVal = cast[int](w)
  return toAhkVar(ptrVal)

proc Buffer*(size: AhkVar, fillByte: AhkVar = nil): AhkVar =
  let sz = size.toInt32().int
  let fill = if fillByte == nil: 0.byte else: fillByte.toInt32().byte
  var buf = AhkBuffer(bytes: newSeq[byte](sz))
  for i in 0 ..< sz:
    buf.bytes[i] = fill
  return toAhkVar(buf)

proc NumPut*(typeStr: AhkVar, number: AhkVar, target: AhkVar, offset: AhkVar = nil): AhkVar =
  if target == nil or target.kind != akObject or target.oVal == nil or not (target.oVal of AhkBuffer):
    return nil
  
  let buf = AhkBuffer(target.oVal)
  let t = typeStr.toString().toLowerAscii()
  let off = if offset == nil: 0 else: offset.toInt32().int
  
  proc writeBytes(val: pointer, size: int) =
    if off + size <= buf.bytes.len:
      let pSrc = cast[ptr byte](val)
      for i in 0 ..< size:
        buf.bytes[off + i] = cast[ptr byte](cast[int](pSrc) + i)[]

  if t == "uint" or t == "uint32":
    var val = number.toInt32().uint32
    writeBytes(addr val, 4)
  elif t == "int" or t == "int32":
    var val = number.toInt32()
    writeBytes(addr val, 4)
  elif t == "ptr" or t == "uptr":
    if sizeof(pointer) == 8:
      var val = number.toInt64()
      writeBytes(addr val, 8)
    else:
      var val = number.toInt32()
      writeBytes(addr val, 4)
  elif t == "char" or t == "int8":
    var val = number.toInt32().int8
    writeBytes(addr val, 1)
  elif t == "uchar" or t == "uint8":
    var val = number.toInt32().uint8
    writeBytes(addr val, 1)
  elif t == "short" or t == "int16":
    var val = number.toInt32().int16
    writeBytes(addr val, 2)
  elif t == "ushort" or t == "uint16":
    var val = number.toInt32().uint16
    writeBytes(addr val, 2)
  elif t == "int64":
    var val = number.toInt64()
    writeBytes(addr val, 8)
  elif t == "uint64":
    var val = number.toInt64().uint64
    writeBytes(addr val, 8)
  elif t == "float" or t == "float32":
    var val = number.toFloat64().float32
    writeBytes(addr val, 4)
  elif t == "double" or t == "float64":
    var val = number.toFloat64()
    writeBytes(addr val, 8)
    
  return target

proc NumGet*(target: AhkVar, offset: AhkVar, typeStr: AhkVar): AhkVar =
  if target == nil or target.kind != akObject or target.oVal == nil or not (target.oVal of AhkBuffer):
    return toAhkVar(0)
    
  let buf = AhkBuffer(target.oVal)
  let t = typeStr.toString().toLowerAscii()
  let off = if offset == nil: 0 else: offset.toInt32().int
  
  proc readBytes(val: pointer, size: int) =
    if off + size <= buf.bytes.len:
      for i in 0 ..< size:
        cast[ptr byte](cast[int](val) + i)[] = buf.bytes[off + i]

  if t == "uint" or t == "uint32":
    var val: uint32 = 0
    readBytes(addr val, 4)
    return toAhkVar(val.int)
  elif t == "int" or t == "int32":
    var val: int32 = 0
    readBytes(addr val, 4)
    return toAhkVar(val.int)
  elif t == "ptr" or t == "uptr":
    if sizeof(pointer) == 8:
      var val: int64 = 0
      readBytes(addr val, 8)
      return toAhkVar(val.int)
    else:
      var val: int32 = 0
      readBytes(addr val, 4)
      return toAhkVar(val.int)
  elif t == "char" or t == "int8":
    var val: int8 = 0
    readBytes(addr val, 1)
    return toAhkVar(val.int)
  elif t == "uchar" or t == "uint8":
    var val: uint8 = 0
    readBytes(addr val, 1)
    return toAhkVar(val.int)
  elif t == "short" or t == "int16":
    var val: int16 = 0
    readBytes(addr val, 2)
    return toAhkVar(val.int)
  elif t == "ushort" or t == "uint16":
    var val: uint16 = 0
    readBytes(addr val, 2)
    return toAhkVar(val.int)
  elif t == "int64":
    var val: int64 = 0
    readBytes(addr val, 8)
    return toAhkVar(val.int)
  elif t == "uint64":
    var val: uint64 = 0
    readBytes(addr val, 8)
    return toAhkVar(val.int)
  elif t == "float" or t == "float32":
    var val: float32 = 0.0
    readBytes(addr val, 4)
    return toAhkVar(val.float)
  elif t == "double" or t == "float64":
    var val: float64 = 0.0
    readBytes(addr val, 8)
    return toAhkVar(val)
    
  return toAhkVar(0)

proc Integer*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(0)
  case v.kind:
  of akInt: return v
  of akFloat: return toAhkVar(v.fVal.int)
  else:
    try: return toAhkVar(parseInt(v.toString()))
    except: return toAhkVar(0)

proc Float*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(0.0)
  case v.kind:
  of akFloat: return v
  of akInt: return toAhkVar(v.iVal.float)
  else:
    try: return toAhkVar(parseFloat(v.toString()))
    except: return toAhkVar(0.0)

proc String*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar("")
  return toAhkVar(v.toString())

proc IsInteger*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(false)
  return toAhkVar(v.kind == akInt)

proc IsFloat*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(false)
  return toAhkVar(v.kind == akFloat)

proc IsNumber*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(false)
  return toAhkVar(v.kind == akInt or v.kind == akFloat)

proc IsString*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(false)
  return toAhkVar(v.kind == akString)

proc IsObject*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar(false)
  return toAhkVar(v.kind == akObject or v.kind == akArray or v.kind == akMap)

proc IsSet*(v: AhkVar): bool =
  return v != nil and v.kind != akNull

proc ahk_Type*(v: AhkVar): AhkVar =
  if v == nil: return toAhkVar("")
  case v.kind:
  of akNull: return toAhkVar("")
  of akInt: return toAhkVar("Integer")
  of akFloat: return toAhkVar("Float")
  of akString: return toAhkVar("String")
  of akArray: return toAhkVar("Array")
  of akMap: return toAhkVar("Map")
  of akObject:
    if v.oVal == nil: return toAhkVar("Object")
    elif v.oVal of AhkGui: return toAhkVar("Gui")
    elif v.oVal of AhkControl:
      return toAhkVar(AhkControl(v.oVal).kind)
    elif v.oVal of AhkBuffer: return toAhkVar("Buffer")
    else: return toAhkVar("Object")

proc FileSelect*(options: AhkVar = nil, rootDir: AhkVar = nil, title: AhkVar = nil, filter: AhkVar = nil): AhkVar =
  let opt = if options == nil: "" else: options.toString()
  let t = if title == nil: "Select File" else: title.toString()
  let filt = if filter == nil: "" else: filter.toString()
  
  let isFolder = opt.contains("D") or opt.contains("d")
  
  var cmd = ""
  if isFolder:
    cmd = "powershell -NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = '" & t.replace("'", "''") & "'; if ($d.ShowDialog() -eq 'OK') { Write-Output $d.SelectedPath }\""
  else:
    var pFilter = "All Files (*.*)|*.*"
    if filt != "":
      pFilter = filt.replace("'", "''") & "|" & filt.replace("'", "''")
    cmd = "powershell -NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.OpenFileDialog; $d.Title = '" & t.replace("'", "''") & "'; $d.Filter = '" & pFilter & "'; if ($d.ShowDialog() -eq 'OK') { Write-Output $d.FileName }\""
  
  let res = execProcess(cmd)
  return toAhkVar(res.strip())

proc DirSelect*(startingFolder: AhkVar = nil, options: AhkVar = nil, prompt: AhkVar = nil): AhkVar =
  let t = if prompt == nil or prompt.toString() == "": "Select Folder" else: prompt.toString()
  let root = if startingFolder == nil: "" else: startingFolder.toString()
  
  var cmd = "powershell -NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = '" & t.replace("'", "''") & "';"
  if root != "":
    cmd &= " $d.SelectedPath = '" & root.replace("'", "''") & "';"
  cmd &= " if ($d.ShowDialog() -eq 'OK') { Write-Output $d.SelectedPath }\""
  
  let res = execProcess(cmd)
  return toAhkVar(res.strip())

proc ComObject*(name: AhkVar): AhkVar =
  return AhkVar(kind: akString, sVal: "")

proc NameSpace*(self: AhkVar, path: AhkVar): AhkVar =
  return AhkVar(kind: akString, sVal: path.toString())

proc Items*(self: AhkVar): AhkVar =
  return self

proc CopyHere*(self: AhkVar, items: AhkVar, flags: AhkVar = nil): AhkVar =
  let zipPath = items.toString()
  let destDir = self.toString()
  if zipPath != "" and destDir != "":
    let cmd = "powershell -NoProfile -Command \"Expand-Archive -Path '" & zipPath.replace("'", "''") & "' -DestinationPath '" & destDir.replace("'", "''") & "' -Force\""
    discard execShellCmdHidden(cmd)
  return nil

proc ComObjValue*(v: AhkVar): AhkVar =
  return v

proc ComValue*(vt, val: AhkVar): AhkVar =
  return val

proc Download*(url, dest: AhkVar): AhkVar =
  let u = url.toString()
  let d = dest.toString()
  let cmd = "powershell -NoProfile -Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '" & u.replace("'", "''") & "' -OutFile '" & d.replace("'", "''") & "'\""
  discard execShellCmdHidden(cmd)
  return nil

proc PostMessage*(msg: AhkVar, wParam: AhkVar = nil, lParam: AhkVar = nil, control: AhkVar = nil): AhkVar =
  var hwnd: HWND = 0
  if control != nil:
    if control.kind == akObject:
      let o = control.oVal
      if o of AhkControl:
        hwnd = AhkControl(o).hwnd
      elif o of AhkGui:
        hwnd = AhkGui(o).hwnd
    else:
      hwnd = cast[HWND](control.toInt32())
  if hwnd == 0:
    hwnd = GetForegroundWindow()
  when defined(windows):
    let wp = if wParam == nil: 0 else: wParam.toInt32()
    let lp = if lParam == nil: 0 else: lParam.toInt32()
    discard PostMessage(hwnd, cast[UINT](msg.toInt32()), cast[WPARAM](wp), cast[LPARAM](lp))
  return nil

proc SendMessage*(msg: AhkVar, wParam: AhkVar = nil, lParam: AhkVar = nil, control: AhkVar = nil): AhkVar =
  var hwnd: HWND = 0
  if control != nil:
    if control.kind == akObject:
      let o = control.oVal
      if o of AhkControl:
        hwnd = AhkControl(o).hwnd
      elif o of AhkGui:
        hwnd = AhkGui(o).hwnd
    else:
      hwnd = cast[HWND](control.toInt32())
  if hwnd == 0:
    hwnd = GetForegroundWindow()
  when defined(windows):
    let wp = if wParam == nil: 0 else: wParam.toInt32()
    let lp = if lParam == nil: 0 else: lParam.toInt32()
    let res = SendMessage(hwnd, cast[UINT](msg.toInt32()), cast[WPARAM](wp), cast[LPARAM](lp))
    return toAhkVar(cast[int](res))
  else:
    return nil

proc OnMessage*(msg: AhkVar, callback: proc(wParam, lParam, msg, hwnd: AhkVar): AhkVar {.closure.} = nil, maxThreads: AhkVar = nil): AhkVar =
  let m = msg.toInt32().int
  if callback == nil:
    if messageCallbacks.contains(m):
      messageCallbacks.del(m)
  else:
    messageCallbacks[m] = callback
  return nil

proc ControlFocus*(control: AhkVar, winTitle: AhkVar = nil): AhkVar =
  var hwnd: HWND = 0
  if control != nil:
    if control.kind == akObject:
      let o = control.oVal
      if o of AhkControl: hwnd = AhkControl(o).hwnd
      elif o of AhkGui: hwnd = AhkGui(o).hwnd
    else:
      hwnd = cast[HWND](control.toInt32())
  when defined(windows):
    if hwnd != 0:
      SetFocus(hwnd)
  return nil

proc ControlGetFocus*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    var hwnd: HWND = 0
    let titleVal = if winTitle == nil: "" else: winTitle.toString()
    if titleVal == "" or titleVal == "A" or titleVal == "a":
      hwnd = GetForegroundWindow()
    elif winTitle != nil and winTitle.kind == akInt:
      hwnd = cast[HWND](winTitle.iVal)
    else:
      let hex = winTitle.toInt32()
      if hex != 0:
        hwnd = cast[HWND](hex)
      else:
        hwnd = FindWindowW(nil, newWideCString(titleVal))
        
    if hwnd != 0:
      let threadId = GetWindowThreadProcessId(hwnd, nil)
      var info: GUITHREADINFO
      info.cbSize = cast[DWORD](sizeof(GUITHREADINFO))
      if GetGUIThreadInfo(threadId, &info) != 0:
        return toAhkVar(cast[int](info.hwndFocus))
    return toAhkVar(0)
  else:
    return toAhkVar(0)

proc HotIfWinActive*(winTitle: AhkVar = nil): AhkVar =
  return nil

proc Hotkey*(keyName: AhkVar, action: proc(key: AhkVar): AhkVar {.closure.}, options: AhkVar = nil): AhkVar =
  if keyName == nil: return nil
  let key = keyName.toString().toLowerAscii()
  let optStr = if options == nil: "" else: options.toString().toLowerAscii()
  if optStr == "off":
    if activeHotkeys.contains(key):
      activeHotkeys.del(key)
  else:
    activeHotkeys[key] = action
  return nil

proc Hotkey*(keyName: AhkVar, action: AhkVar = nil, options: AhkVar = nil): AhkVar =
  if keyName == nil: return nil
  let key = keyName.toString().toLowerAscii()
  let actStr = if action == nil: "" else: action.toString().toLowerAscii()
  
  if actStr == "off":
    if activeHotkeys.contains(key):
      activeHotkeys.del(key)
  elif actStr == "on":
    discard
  elif action == nil or action.kind == akNull:
    let optStr = if options == nil: "" else: options.toString().toLowerAscii()
    if optStr == "off":
      if activeHotkeys.contains(key):
        activeHotkeys.del(key)
  else:
    if action.kind == akObject and action.oVal != nil and action.oVal of AhkFunctionObj:
      let fnObj = AhkFunctionObj(action.oVal)
      if fnObj.cb1 != nil:
        activeHotkeys[key] = fnObj.cb1
      elif fnObj.cb0 != nil:
        activeHotkeys[key] = proc(key: AhkVar): AhkVar = return fnObj.cb0()
  return nil

proc WinWaitClose*(winTitle: AhkVar = nil, winText: AhkVar = nil, timeout: AhkVar = nil): AhkVar =
  return nil

proc ToolTip*(text: AhkVar = nil, x: AhkVar = nil, y: AhkVar = nil, whichToolTip: AhkVar = nil): AhkVar =
  let t = if text != nil: text.toString() else: ""
  echo "ToolTip: ", t
  return nil

proc SetTimer*(callback: AhkVar, period: AhkVar = nil, priority: AhkVar = nil): AhkVar =
  if callback != nil:
    discard callback()
  return nil

iterator loopCount*(count: AhkVar): AhkVar =
  let cnt = if count == nil: -1 else: count.toInt32()
  var i = 1
  while cnt < 0 or i <= cnt:
    A_Index = toAhkVar(i)
    yield A_Index
    i += 1

iterator loopFiles*(pattern: AhkVar, mode: AhkVar = nil): AhkVar =
  let pat = pattern.toString()
  let m = if mode != nil: mode.toString().toUpperAscii() else: ""
  let recurse = m.contains("R")
  
  let (dir, name, ext) = splitFile(pat)
  let searchDir = if dir == "": "." else: dir
  let searchGlob = name & ext
  
  var i = 1
  if recurse:
    for path in walkDirRec(searchDir):
      let (d, n, e) = splitFile(path)
      let fn = n & e
      if searchGlob == "*" or fn == searchGlob or (searchGlob.startsWith("*") and fn.endsWith(searchGlob[1..^1])):
        A_Index = toAhkVar(i)
        A_LoopFileName = toAhkVar(fn)
        A_LoopFileFullPath = toAhkVar(path)
        A_LoopFilePath = toAhkVar(path)
        A_LoopFileDir = toAhkVar(d)
        A_LoopFileExt = toAhkVar(if e.startsWith("."): e[1..^1] else: e)
        try: A_LoopFileSize = toAhkVar(getFileSize(path).int) except: A_LoopFileSize = toAhkVar(0)
        yield toAhkVar(path)
        i += 1
  else:
    for kind, path in walkDir(searchDir):
      let (d, n, e) = splitFile(path)
      let fn = n & e
      var matches = false
      if searchGlob == "*" or fn == searchGlob:
        matches = true
      elif searchGlob.startsWith("*."):
        let extMatch = searchGlob[1..^1]
        if fn.endsWith(extMatch):
          matches = true
      
      if matches:
        if (kind == pcFile and not m.contains("D")) or (kind == pcDir and m.contains("D")):
          A_Index = toAhkVar(i)
          A_LoopFileName = toAhkVar(fn)
          A_LoopFileFullPath = toAhkVar(path)
          A_LoopFilePath = toAhkVar(path)
          A_LoopFileDir = toAhkVar(d)
          A_LoopFileExt = toAhkVar(if e.startsWith("."): e[1..^1] else: e)
          if kind == pcFile:
            try: A_LoopFileSize = toAhkVar(getFileSize(path).int) except: A_LoopFileSize = toAhkVar(0)
          else:
            A_LoopFileSize = toAhkVar(0)
          yield toAhkVar(path)
          i += 1

iterator loopParse*(str: AhkVar, delimiters: AhkVar = nil): AhkVar =
  let s = str.toString()
  let delim = if delimiters != nil: delimiters.toString() else: ""
  var i = 1
  if delim == "":
    for c in s:
      A_Index = toAhkVar(i)
      A_LoopField = toAhkVar($c)
      yield A_LoopField
      i += 1
  else:
    var current = ""
    for c in s:
      if delim.contains(c):
        A_Index = toAhkVar(i)
        A_LoopField = toAhkVar(current)
        yield A_LoopField
        current = ""
        i += 1
      else:
        current.add(c)
    if current != "":
      A_Index = toAhkVar(i)
      A_LoopField = toAhkVar(current)
      yield A_LoopField
      i += 1

proc Format*(formatStr: AhkVar, args: varargs[AhkVar, toAhkVar]): AhkVar =
  let fmt = formatStr.toString()
  var resultStr = ""
  var i = 0
  var implicitIdx = 0
  while i < fmt.len:
    if fmt[i] == '{' and i + 1 < fmt.len and fmt[i+1] == '{':
      resultStr &= "{"
      i += 2
      continue
    if fmt[i] == '}' and i + 1 < fmt.len and fmt[i+1] == '}':
      resultStr &= "}"
      i += 2
      continue
    if fmt[i] == '{' and i + 1 < fmt.len:
      let closeIdx = fmt.find('}', i)
      if closeIdx != -1:
        let content = fmt[i + 1 ..< closeIdx]
        i = closeIdx + 1
        let parts = content.split(':')
        var argIdx = 0
        if parts[0] == "":
          argIdx = implicitIdx
          implicitIdx += 1
        else:
          try:
            argIdx = parseInt(parts[0]) - 1
          except:
            argIdx = implicitIdx
            implicitIdx += 1
            
        if argIdx >= 0 and argIdx < args.len:
          let arg = args[argIdx]
          if parts.len > 1:
            let fmtSpec = parts[1].toLowerAscii()
            if fmtSpec == "02x":
              let val = if arg.kind == akInt: arg.iVal else: (try: parseInt(arg.toString()) except: 0)
              resultStr &= val.toHex(2)
            elif fmtSpec == "x":
              let val = if arg.kind == akInt: arg.iVal else: (try: parseInt(arg.toString()) except: 0)
              resultStr &= val.toHex()
            else:
              resultStr &= arg.toString()
          else:
            resultStr &= arg.toString()
      else:
        resultStr &= fmt[i]
        i += 1
    else:
      resultStr &= fmt[i]
      i += 1
  return toAhkVar(resultStr)

proc WinSetEnabled*(value: AhkVar = nil, winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  var hwnd: HWND = 0
  if winTitle != nil:
    if winTitle.kind == akObject:
      let o = winTitle.oVal
      if o != nil:
        if o of AhkControl:
          hwnd = AhkControl(o).hwnd
        elif o of AhkGui:
          hwnd = AhkGui(o).hwnd
    else:
      hwnd = cast[HWND](winTitle.toInt32())
  
  if hwnd == 0:
    when defined(windows):
      hwnd = GetForegroundWindow()
      
  when defined(windows):
    let val = if value != nil: value.toBool() else: true
    discard EnableWindow(hwnd, if val: 1 else: 0)
  return nil

proc WinExist*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    let title = if winTitle == nil: "" else: winTitle.toString()
    if title == "A" or title == "a":
      return toAhkVar(cast[int](GetForegroundWindow()))
    if winTitle != nil and winTitle.kind == akInt:
      let hwnd = cast[HWND](winTitle.iVal)
      if IsWindow(hwnd) != 0:
        return winTitle
      else:
        return toAhkVar(0)
    # Simple search fallback: find by title if it's a string
    if title != "":
      let hwnd = FindWindowW(nil, newWideCString(title))
      if hwnd != 0:
        return toAhkVar(cast[int](hwnd))
    return toAhkVar(0)
  else:
    return toAhkVar(0)

when defined(windows):
  type WinEnumData = object
    targetPid: DWORD
    filterByPid: bool
    hwnds: seq[HWND]

  proc enumWindowsProc(hwnd: HWND, lParam: LPARAM): WINBOOL {.stdcall.} =
    let data = cast[ptr WinEnumData](lParam)
    if data.filterByPid:
      var pid: DWORD = 0
      discard GetWindowThreadProcessId(hwnd, addr pid)
      if pid == data.targetPid:
        data.hwnds.add(hwnd)
    else:
      data.hwnds.add(hwnd)
    return TRUE

proc WinGetList*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  var res = AhkArray()
  when defined(windows):
    let title = if winTitle == nil: "" else: winTitle.toString()
    var data = WinEnumData(filterByPid: false, hwnds: @[])
    
    if title.startsWith("ahk_pid "):
      let pidStr = title[8..^1]
      try:
        data.targetPid = cast[DWORD](parseInt(pidStr))
        data.filterByPid = true
      except:
        discard
    
    discard EnumWindows(enumWindowsProc, cast[LPARAM](addr data))
    
    for hwnd in data.hwnds:
      res.aVal.add(toAhkVar(cast[int](hwnd)))
  return res

proc WinActivate*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  var hwnd: HWND = 0
  if winTitle != nil:
    if winTitle.kind == akObject:
      let o = winTitle.oVal
      if o != nil:
        if o of AhkControl: hwnd = AhkControl(o).hwnd
        elif o of AhkGui: hwnd = AhkGui(o).hwnd
    else:
      hwnd = cast[HWND](winTitle.toInt32())
  when defined(windows):
    if hwnd != 0:
      SetForegroundWindow(hwnd)
  return nil

proc UseTab*(self: AhkVar, index: int = 0): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    let tabCtrl = AhkControl(self.oVal)
    for gui in activeGuis:
      for c in gui.controls:
        if c.hwnd == tabCtrl.hwnd:
          gui.currentTabControl = tabCtrl.hwnd
          gui.currentTabPage = index
          return self
  return self

proc UseTab*(self: AhkVar, index: AhkVar): AhkVar =
  let idx = if index == nil: 0 else: index.toInt32().int
  return self.UseTab(idx)

proc ProcessExist*(pidOrName: AhkVar = nil): AhkVar =
  when defined(windows):
    if pidOrName == nil or pidOrName.kind == akNull:
      return toAhkVar(cast[int](GetCurrentProcessId()))

    var isNumeric = false
    var targetPid = 0
    if pidOrName.kind == akInt:
      isNumeric = true
      targetPid = pidOrName.iVal
    else:
      let s = pidOrName.toString()
      if s.len == 0:
        return toAhkVar(cast[int](GetCurrentProcessId()))
      try:
        targetPid = parseInt(s)
        isNumeric = true
      except:
        discard

    if isNumeric:
      if targetPid <= 0:
        return toAhkVar(0)
      let hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, cast[DWORD](targetPid))
      if hProcess != 0:
        CloseHandle(hProcess)
        return toAhkVar(targetPid)
      else:
        let err = GetLastError()
        if err == ERROR_ACCESS_DENIED:
          return toAhkVar(targetPid)
        else:
          return toAhkVar(0)
    else:
      let name = pidOrName.toString().toLowerAscii()
      var pe: PROCESSENTRY32
      pe.dwSize = sizeof(pe).int32
      let hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
      if hSnapshot == INVALID_HANDLE_VALUE:
        return toAhkVar(0)
      defer: CloseHandle(hSnapshot)

      if Process32First(hSnapshot, addr pe) != 0:
        while true:
          let exeName = ($cast[WideCString](addr pe.szExeFile[0])).toLowerAscii()
          if exeName == name:
            return toAhkVar(pe.th32ProcessID.int)
          if Process32Next(hSnapshot, addr pe) == 0:
            break
      return toAhkVar(0)
  else:
    return toAhkVar(0)

proc ProcessClose*(pidOrName: AhkVar = nil): AhkVar =
  when defined(windows):
    let foundPidVar = ProcessExist(pidOrName)
    let pid = foundPidVar.toInt32()
    if pid > 0:
      let hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, cast[DWORD](pid))
      if hProcess != 0:
        defer: CloseHandle(hProcess)
        if TerminateProcess(hProcess, 1) != 0:
          return toAhkVar(pid)
    return toAhkVar(0)
  else:
    return toAhkVar(0)

proc WinGetClass*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    var hwndVal = WinExist(winTitle, winText, excludeTitle, excludeText)
    var hwnd = cast[HWND](hwndVal.toInt64())
    if hwnd == 0 and (winTitle == nil or winTitle.toString() == ""):
      hwnd = GetForegroundWindow()
      
    if hwnd != 0:
      var buf = newSeq[uint16](256)
      discard GetClassName(hwnd, cast[LPWSTR](addr buf[0]), 256)
      return toAhkVar($cast[WideCString](addr buf[0]))
    return toAhkVar("")
  else:
    return toAhkVar("")

proc WinActive*(winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    let activeHwnd = GetForegroundWindow()
    if winTitle == nil or winTitle.toString() == "":
      return toAhkVar(cast[int](activeHwnd))
    let hwndVal = WinExist(winTitle, winText, excludeTitle, excludeText)
    let hwnd = cast[HWND](hwndVal.toInt64())
    if hwnd != 0 and hwnd == activeHwnd:
      return toAhkVar(cast[int](activeHwnd))
    return toAhkVar(0)
  else:
    return toAhkVar(0)

proc WinSetTransparent*(n: AhkVar, winTitle: AhkVar = nil, winText: AhkVar = nil, excludeTitle: AhkVar = nil, excludeText: AhkVar = nil): AhkVar =
  when defined(windows):
    let hwndVal = WinExist(winTitle, winText, excludeTitle, excludeText)
    var hwnd = cast[HWND](hwndVal.toInt64())
    if hwnd == 0 and (winTitle == nil or winTitle.toString() == ""):
      hwnd = GetForegroundWindow()
      
    if hwnd != 0:
      let nStr = if n == nil: "" else: n.toString().toLowerAscii()
      if nStr == "off" or nStr == "":
        let style = GetWindowLongPtr(hwnd, GWL_EXSTYLE)
        discard SetWindowLongPtr(hwnd, GWL_EXSTYLE, style and (not WS_EX_LAYERED))
        discard SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_FRAMECHANGED)
      else:
        var val = 255
        try:
          val = if n.kind == akInt: n.iVal else: parseInt(nStr)
        except:
          discard
        if val < 0: val = 0
        elif val > 255: val = 255
        
        let style = GetWindowLongPtr(hwnd, GWL_EXSTYLE)
        discard SetWindowLongPtr(hwnd, GWL_EXSTYLE, style or WS_EX_LAYERED)
        discard SetLayeredWindowAttributes(hwnd, 0, cast[BYTE](val), LWA_ALPHA)
  return nil

proc InStr*(haystack: AhkVar, needle: AhkVar, caseSensitive: AhkVar = nil, startingPos: AhkVar = nil, occurrence: AhkVar = nil): AhkVar =
  let h = if haystack == nil: "" else: haystack.toString()
  let n = if needle == nil: "" else: needle.toString()
  
  if n.len == 0:
    return toAhkVar(1)
    
  let caseSens = if caseSensitive == nil: false else: caseSensitive.toBool()
  let startPos = if startingPos == nil: 1 else: startingPos.toInt32().int
  let occ = if occurrence == nil: 1 else: occurrence.toInt32().int
  
  let hSearched = if caseSens: h else: h.toLowerAscii()
  let nSearched = if caseSens: n else: n.toLowerAscii()
  
  if startPos > 0:
    let startIdx = startPos - 1
    if startIdx >= hSearched.len:
      return toAhkVar(0)
      
    var count = 0
    var pos = startIdx
    while true:
      let found = hSearched.find(nSearched, pos)
      if found == -1:
        return toAhkVar(0)
      count += 1
      if count == occ:
        return toAhkVar(found + 1)
      pos = found + 1
  else:
    let startIdx = hSearched.len + startPos
    if startIdx < 0:
      return toAhkVar(0)
      
    var count = 0
    var pos = startIdx
    while true:
      let found = hSearched.rfind(nSearched, 0, pos)
      if found == -1:
        return toAhkVar(0)
      count += 1
      if count == occ:
        return toAhkVar(found + 1)
      pos = found - 1
      if pos < 0:
        return toAhkVar(0)

proc RegExReplace*(str, pattern: AhkVar, replacement: AhkVar = nil, limit: AhkVar = nil, startingPosition: AhkVar = nil): AhkVar =
  let s = str.toString()
  let p = pattern.toString()
  let rep = if replacement == nil: "" else: replacement.toString()
  
  if p == " \\((TrueType|OpenType|VGA|850|8514|Set #\\d)\\)$":
    for suffix in [" (TrueType)", " (OpenType)", " (VGA)", " (850)", " (8514)"]:
      if s.endsWith(suffix):
        return toAhkVar(s[0 ..< s.len - suffix.len])
    if s.len >= 9 and s.endsWith(")") and s[s.len-9 .. s.len-3] == " (Set #":
      let c = s[s.len-2]
      if c >= '0' and c <= '9':
        return toAhkVar(s[0 .. s.len-10])
    return toAhkVar(s)
    
  return toAhkVar(s.replace(p, rep))

proc parseRegPath(path: string, rootKey: var HKEY, subKey: var string) =
  var p = path.replace("/", "\\")
  let idx = p.find('\\')
  let rootStr = if idx == -1: p.toLowerAscii() else: p[0 ..< idx].toLowerAscii()
  subKey = if idx == -1 or idx == p.len - 1: "" else: p[idx + 1 .. ^1]
  
  case rootStr:
  of "hkey_local_machine", "hklm": rootKey = HKEY_LOCAL_MACHINE
  of "hkey_current_user", "hkcu": rootKey = HKEY_CURRENT_USER
  of "hkey_classes_root", "hkcr": rootKey = HKEY_CLASSES_ROOT
  of "hkey_users", "hku": rootKey = HKEY_USERS
  of "hkey_current_config", "hkcc": rootKey = HKEY_CURRENT_CONFIG
  else: rootKey = HKEY_CURRENT_USER

proc regReadVal(rootKey: HKEY, subKey: string, valName: string, defaultVal: AhkVar): AhkVar =
  var hKey: HKEY
  let res = RegOpenKeyExW(rootKey, newWideCString(subKey), 0, KEY_READ, addr hKey)
  if res != ERROR_SUCCESS:
    return if defaultVal != nil: defaultVal else: toAhkVar("")
  defer: RegCloseKey(hKey)
  
  var valType: DWORD
  var dataLen: DWORD
  let queryRes = RegQueryValueExW(hKey, newWideCString(valName), nil, addr valType, nil, addr dataLen)
  if queryRes != ERROR_SUCCESS:
    return if defaultVal != nil: defaultVal else: toAhkVar("")
    
  if valType == REG_DWORD:
    var val: DWORD
    var valLen = sizeof(val).DWORD
    let r = RegQueryValueExW(hKey, newWideCString(valName), nil, nil, cast[ptr byte](addr val), addr valLen)
    if r == ERROR_SUCCESS:
      return toAhkVar(val.int)
  elif valType == REG_SZ or valType == REG_EXPAND_SZ:
    var buf = newSeq[uint16](dataLen.int div 2 + 1)
    let r = RegQueryValueExW(hKey, newWideCString(valName), nil, nil, cast[ptr byte](addr buf[0]), addr dataLen)
    if r == ERROR_SUCCESS:
      return toAhkVar($cast[WideCString](addr buf[0]))
  elif valType == REG_BINARY:
    var data = newSeq[byte](dataLen.int)
    let r = RegQueryValueExW(hKey, newWideCString(valName), nil, nil, addr data[0], addr dataLen)
    if r == ERROR_SUCCESS:
      var s = ""
      for b in data:
        s.add(chr(b))
      return toAhkVar(s)
  
  return if defaultVal != nil: defaultVal else: toAhkVar("")

proc RegRead*(keyName: AhkVar = nil, valueName: AhkVar = nil, defaultVal: AhkVar = nil): AhkVar =
  when defined(windows):
    if keyName == nil or keyName.toString() == "":
      return currentLoopRegValue
      
    let kn = keyName.toString()
    let vn = if valueName == nil: "" else: valueName.toString()
    
    var rootKey: HKEY
    var subKey: string
    parseRegPath(kn, rootKey, subKey)
    
    return regReadVal(rootKey, subKey, vn, defaultVal)
  else:
    return if defaultVal != nil: defaultVal else: toAhkVar("")

proc RegWrite*(value: AhkVar, valType: AhkVar, keyName: AhkVar, valName: AhkVar = nil): AhkVar =
  when defined(windows):
    let valStr = if value == nil: "" else: value.toString()
    let typeStr = if valType == nil: "REG_SZ" else: valType.toString().toUpperAscii()
    let kn = if keyName == nil: "" else: keyName.toString()
    let vn = if valName == nil: "" else: valName.toString()
    
    var rootKey: HKEY
    var subKey: string
    parseRegPath(kn, rootKey, subKey)
    
    var hKey: HKEY
    let res = RegCreateKeyExW(rootKey, newWideCString(subKey), 0, nil, REG_OPTION_NON_VOLATILE, KEY_WRITE, nil, addr hKey, nil)
    if res != ERROR_SUCCESS:
      return toAhkVar(0)
    defer: RegCloseKey(hKey)
    
    var vType: DWORD
    case typeStr:
    of "REG_SZ": vType = REG_SZ
    of "REG_EXPAND_SZ": vType = REG_EXPAND_SZ
    of "REG_DWORD": vType = REG_DWORD
    of "REG_BINARY": vType = REG_BINARY
    else: vType = REG_SZ
    
    if vType == REG_DWORD:
      var val: DWORD
      try:
        val = cast[DWORD](parseInt(valStr))
      except:
        discard
      discard RegSetValueExW(hKey, newWideCString(vn), 0, vType, cast[ptr byte](addr val), sizeof(val).DWORD)
    elif vType == REG_SZ or vType == REG_EXPAND_SZ:
      let w = newWideCString(valStr)
      let sizeBytes = (w.len + 1) * 2
      discard RegSetValueExW(hKey, newWideCString(vn), 0, vType, cast[ptr byte](addr w[0]), sizeBytes.DWORD)
    elif vType == REG_BINARY:
      var bytes = newSeq[byte]()
      var i = 0
      while i < valStr.len - 1:
        try:
          bytes.add(parseHexInt(valStr[i .. i+1]).byte)
        except:
          discard
        i += 2
      if bytes.len > 0:
        discard RegSetValueExW(hKey, newWideCString(vn), 0, vType, addr bytes[0], bytes.len.DWORD)
  return nil

iterator loopReg*(keyName: AhkVar, mode: AhkVar = nil): AhkVar =
  when defined(windows):
    let kn = if keyName == nil: "" else: keyName.toString()
    let m = if mode == nil: "V" else: mode.toString().toUpperAscii()
    
    let recurse = m.contains("R")
    let keysOnly = m.contains("K") and not m.contains("V")
    let valuesOnly = m.contains("V") and not m.contains("K")
    
    var rootKey: HKEY
    var subKey: string
    parseRegPath(kn, rootKey, subKey)
    
    var stack: seq[tuple[subKey: string, path: string]] = @[(subKey: subKey, path: kn)]
    var indexCounter = 1
    
    while stack.len > 0:
      let (currSub, currPath) = stack.pop()
      var hKey: HKEY
      let res = RegOpenKeyExW(rootKey, newWideCString(currSub), 0, KEY_READ, addr hKey)
      if res != ERROR_SUCCESS:
        continue
      
      if not keysOnly:
        var idx: DWORD = 0
        var valueName = newSeq[uint16](16384)
        var valueType: DWORD
        var data = newSeq[byte](65536)
        while true:
          var valueNameLen: DWORD = 16384
          var dataLen: DWORD = 65536
          let enumRes = RegEnumValueW(hKey, idx, cast[LPWSTR](addr valueName[0]), addr valueNameLen, nil, addr valueType, addr data[0], addr dataLen)
          if enumRes == ERROR_NO_MORE_ITEMS:
            break
          elif enumRes == ERROR_SUCCESS:
            let vName = $cast[WideCString](addr valueName[0])
            let val = regReadVal(rootKey, currSub, vName, nil)
            
            A_Index = toAhkVar(indexCounter)
            A_LoopRegName = toAhkVar(vName)
            A_LoopRegType = toAhkVar(case valueType:
              of REG_SZ: "REG_SZ"
              of REG_EXPAND_SZ: "REG_EXPAND_SZ"
              of REG_DWORD: "REG_DWORD"
              of REG_BINARY: "REG_BINARY"
              else: "REG_SZ")
            A_LoopRegKey = toAhkVar(currPath)
            currentLoopRegValue = val
            
            yield toAhkVar(vName)
            indexCounter += 1
            idx += 1
          else:
            break
            
      var idx: DWORD = 0
      var keyName = newSeq[uint16](16384)
      var fileTime: FILETIME
      var subKeys = newSeq[string]()
      while true:
        var keyNameLen: DWORD = 16384
        let enumRes = RegEnumKeyExW(hKey, idx, cast[LPWSTR](addr keyName[0]), addr keyNameLen, nil, nil, nil, addr fileTime)
        if enumRes == ERROR_NO_MORE_ITEMS:
          break
        elif enumRes == ERROR_SUCCESS:
          let kName = $cast[WideCString](addr keyName[0])
          subKeys.add(kName)
          
          if not valuesOnly:
            A_Index = toAhkVar(indexCounter)
            A_LoopRegName = toAhkVar(kName)
            A_LoopRegType = toAhkVar("KEY")
            A_LoopRegKey = toAhkVar(currPath)
            currentLoopRegValue = toAhkVar("")
            
            yield toAhkVar(kName)
            indexCounter += 1
          idx += 1
        else:
          break
          
      RegCloseKey(hKey)
      
      if recurse:
        for j in countdown(subKeys.len - 1, 0):
          let sk = subKeys[j]
          let nextSub = if currSub == "": sk else: currSub & "\\" & sk
          let nextPath = if currPath == "": sk else: currPath & "\\" & sk
          stack.add((subKey: nextSub, path: nextPath))

proc Call*(self: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb0 != nil: return fn.cb0()
    elif fn.cb1 != nil: return fn.cb1(nil)
    elif fn.cb2 != nil: return fn.cb2(nil, nil)
    elif fn.cb3 != nil: return fn.cb3(nil, nil, nil)
    elif fn.cb4 != nil: return fn.cb4(nil, nil, nil, nil)
  return nil

proc Call*(self: AhkVar, a: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb2 != nil: return fn.cb2(a, nil)
    elif fn.cb3 != nil: return fn.cb3(a, nil, nil)
    elif fn.cb4 != nil: return fn.cb4(a, nil, nil, nil)
  return nil

proc Call*(self: AhkVar, a, b: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb3 != nil: return fn.cb3(a, b, nil)
    elif fn.cb4 != nil: return fn.cb4(a, b, nil, nil)
  return nil

proc Call*(self: AhkVar, a, b, c: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb4 != nil: return fn.cb4(a, b, c, nil)
  return nil

proc Call*(self: AhkVar, a, b, c, d: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb4 != nil: return fn.cb4(a, b, c, d)
    elif fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb5 != nil: return fn.cb5(a, b, c, d, nil)
    elif fn.cb6 != nil: return fn.cb6(a, b, c, d, nil, nil)
    elif fn.cb7 != nil: return fn.cb7(a, b, c, d, nil, nil, nil)
    elif fn.cb8 != nil: return fn.cb8(a, b, c, d, nil, nil, nil, nil)
  return nil

proc Call*(self: AhkVar, a, b, c, d, e: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb5 != nil: return fn.cb5(a, b, c, d, e)
    elif fn.cb4 != nil: return fn.cb4(a, b, c, d)
    elif fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb6 != nil: return fn.cb6(a, b, c, d, e, nil)
    elif fn.cb7 != nil: return fn.cb7(a, b, c, d, e, nil, nil)
    elif fn.cb8 != nil: return fn.cb8(a, b, c, d, e, nil, nil, nil)
  return nil

proc Call*(self: AhkVar, a, b, c, d, e, f: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb6 != nil: return fn.cb6(a, b, c, d, e, f)
    elif fn.cb5 != nil: return fn.cb5(a, b, c, d, e)
    elif fn.cb4 != nil: return fn.cb4(a, b, c, d)
    elif fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb7 != nil: return fn.cb7(a, b, c, d, e, f, nil)
    elif fn.cb8 != nil: return fn.cb8(a, b, c, d, e, f, nil, nil)
  return nil

proc Call*(self: AhkVar, a, b, c, d, e, f, g: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb7 != nil: return fn.cb7(a, b, c, d, e, f, g)
    elif fn.cb6 != nil: return fn.cb6(a, b, c, d, e, f)
    elif fn.cb5 != nil: return fn.cb5(a, b, c, d, e)
    elif fn.cb4 != nil: return fn.cb4(a, b, c, d)
    elif fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
    elif fn.cb8 != nil: return fn.cb8(a, b, c, d, e, f, g, nil)
  return nil

proc Call*(self: AhkVar, a, b, c, d, e, f, g, h: AhkVar): AhkVar =
  if self == nil: return nil
  if self.kind == akObject and self.oVal != nil and self.oVal of AhkFunctionObj:
    let fn = AhkFunctionObj(self.oVal)
    if fn.cb8 != nil: return fn.cb8(a, b, c, d, e, f, g, h)
    elif fn.cb7 != nil: return fn.cb7(a, b, c, d, e, f, g)
    elif fn.cb6 != nil: return fn.cb6(a, b, c, d, e, f)
    elif fn.cb5 != nil: return fn.cb5(a, b, c, d, e)
    elif fn.cb4 != nil: return fn.cb4(a, b, c, d)
    elif fn.cb3 != nil: return fn.cb3(a, b, c)
    elif fn.cb2 != nil: return fn.cb2(a, b)
    elif fn.cb1 != nil: return fn.cb1(a)
    elif fn.cb0 != nil: return fn.cb0()
  return nil

proc Delete*(self: AhkControl, row: AhkVar = nil): AhkVar =
  when defined(windows):
    if self.hwnd == 0: return nil
    if row == nil:
      discard SendMessage(self.hwnd, LVM_DELETEALLITEMS, 0, 0)
    else:
      let r = row.toInt32() - 1
      discard SendMessage(self.hwnd, LVM_DELETEITEM, cast[WPARAM](r), 0)
  return nil

proc Delete*(self: AhkVar, row: AhkVar = nil): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil:
    if self.oVal of AhkControl:
      return AhkControl(self.oVal).Delete(row)
    elif self.oVal of AhkMenu:
      return AhkMenu_Delete(self, row)
  return nil

proc AhkControl_Delete*(self: AhkVar, row: AhkVar = nil): AhkVar =
  return self.Delete(row)

proc InsertInternal(self: AhkControl, row: AhkVar, options: AhkVar, fields: seq[AhkVar]): AhkVar =
  when defined(windows):
    if self.hwnd == 0: return toAhkVar(0)
    let rowIdx = row.toInt32() - 1
    
    var item: LVITEMW
    item.mask = cast[UINT](LVIF_TEXT)
    item.iItem = rowIdx.int32
    item.iSubItem = 0
    let firstText = if fields.len > 0: fields[0].toString() else: ""
    item.pszText = newWideCString(firstText)
    
    let resIndex = SendMessage(self.hwnd, LVM_INSERTITEMW, 0, cast[LPARAM](addr item))
    if resIndex == -1: return toAhkVar(0)
    
    for colIdx in 1 ..< fields.len:
      var subItem: LVITEMW
      subItem.mask = cast[UINT](LVIF_TEXT)
      subItem.iItem = resIndex.int32
      subItem.iSubItem = colIdx.int32
      subItem.pszText = newWideCString(fields[colIdx].toString())
      discard SendMessage(self.hwnd, LVM_SETITEMTEXTW, cast[WPARAM](resIndex), cast[LPARAM](addr subItem))
      
    if options != nil and options.toString() != "":
      let optStr = options.toString().toLowerAscii()
      var stateMask: uint32 = 0
      var state: uint32 = 0
      if "select" in optStr:
        stateMask = stateMask or LVIS_SELECTED.uint32
        state = state or LVIS_SELECTED.uint32
      if "focus" in optStr:
        stateMask = stateMask or LVIS_FOCUSED.uint32
        state = state or LVIS_FOCUSED.uint32
        
      if stateMask != 0:
        var stateItem: LVITEMW
        stateItem.stateMask = cast[UINT](stateMask)
        stateItem.state = cast[UINT](state)
        discard SendMessage(self.hwnd, LVM_SETITEMSTATE, cast[WPARAM](resIndex), cast[LPARAM](addr stateItem))
        
    return toAhkVar(resIndex + 1)
  else:
    return toAhkVar(0)

proc Insert*(self: AhkVar, row: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    var argsList: seq[AhkVar] = @[]
    for f in fields: argsList.add(f)
    return AhkControl(self.oVal).InsertInternal(row, options, argsList)
  return nil

proc AhkControl_Insert*(self: AhkVar, row: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  var argsList: seq[AhkVar] = @[]
  for f in fields: argsList.add(f)
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    return AhkControl(self.oVal).InsertInternal(row, options, argsList)
  return nil

proc ModifyInternal(self: AhkControl, row: AhkVar, options: AhkVar, fields: seq[AhkVar]): AhkVar =
  when defined(windows):
    if self.hwnd == 0: return toAhkVar(0)
    let rowIdx = row.toInt32() - 1
    
    for colIdx in 0 ..< fields.len:
      var subItem: LVITEMW
      subItem.mask = cast[UINT](LVIF_TEXT)
      subItem.iItem = rowIdx.int32
      subItem.iSubItem = colIdx.int32
      subItem.pszText = newWideCString(fields[colIdx].toString())
      discard SendMessage(self.hwnd, LVM_SETITEMTEXTW, cast[WPARAM](rowIdx), cast[LPARAM](addr subItem))
      
    if options != nil and options.toString() != "":
      let optStr = options.toString().toLowerAscii()
      let parts = optStr.split(Whitespace)
      var stateMask: uint32 = 0
      var state: uint32 = 0
      for part in parts:
        let p = part.strip()
        if p == "select":
          stateMask = stateMask or LVIS_SELECTED.uint32
          state = state or LVIS_SELECTED.uint32
        elif p == "-select":
          stateMask = stateMask or LVIS_SELECTED.uint32
        elif p == "focus":
          stateMask = stateMask or LVIS_FOCUSED.uint32
          state = state or LVIS_FOCUSED.uint32
        elif p == "-focus":
          stateMask = stateMask or LVIS_FOCUSED.uint32
          
      if stateMask != 0:
        var stateItem: LVITEMW
        stateItem.stateMask = cast[UINT](stateMask)
        stateItem.state = cast[UINT](state)
        discard SendMessage(self.hwnd, LVM_SETITEMSTATE, cast[WPARAM](rowIdx), cast[LPARAM](addr stateItem))
        
    return toAhkVar(1)
  else:
    return toAhkVar(0)

proc Modify*(self: AhkVar, row: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    var argsList: seq[AhkVar] = @[]
    for f in fields: argsList.add(f)
    return AhkControl(self.oVal).ModifyInternal(row, options, argsList)
  return nil

proc AhkControl_Modify*(self: AhkVar, row: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  var argsList: seq[AhkVar] = @[]
  for f in fields: argsList.add(f)
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    return AhkControl(self.oVal).ModifyInternal(row, options, argsList)
  return nil

proc AddInternal(self: AhkControl, options: AhkVar, fields: seq[AhkVar]): AhkVar =
  when defined(windows):
    let count = SendMessage(self.hwnd, LVM_GETITEMCOUNT, 0, 0)
    return self.InsertInternal(toAhkVar(count + 1), options, fields)
  else:
    return toAhkVar(0)

proc Add*(self: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    var argsList: seq[AhkVar] = @[]
    for f in fields: argsList.add(f)
    return AhkControl(self.oVal).AddInternal(options, argsList)
  return nil

proc AhkControl_Add*(self: AhkVar, options: AhkVar = nil, fields: varargs[AhkVar, toAhkVar]): AhkVar =
  var argsList: seq[AhkVar] = @[]
  for f in fields: argsList.add(f)
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    return AhkControl(self.oVal).AddInternal(options, argsList)
  return nil

proc GetNext*(self: AhkControl, startRow: AhkVar = nil, typeStr: AhkVar = nil): AhkVar =
  when defined(windows):
    if self.hwnd == 0: return toAhkVar(0)
    let startIdx = if startRow == nil: -1 else: startRow.toInt32() - 1
    
    var flags = LVNI_SELECTED
    if typeStr != nil:
      let t = typeStr.toString().toLowerAscii()
      if t == "focused": flags = LVNI_FOCUSED
      
    let nextIdx = SendMessage(self.hwnd, LVM_GETNEXTITEM, cast[WPARAM](startIdx), cast[LPARAM](flags))
    return toAhkVar(if nextIdx == -1: 0 else: nextIdx + 1)
  else:
    return toAhkVar(0)

proc GetNext*(self: AhkVar, startRow: AhkVar = nil, typeStr: AhkVar = nil): AhkVar =
  if self != nil and self.kind == akObject and self.oVal != nil and self.oVal of AhkControl:
    return AhkControl(self.oVal).GetNext(startRow, typeStr)
  return nil

proc AhkControl_GetNext*(self: AhkVar, startRow: AhkVar = nil, typeStr: AhkVar = nil): AhkVar =
  return self.GetNext(startRow, typeStr)

proc parseAhkTime(s: string): DateTime =
  var ts = s.strip()
  if ts.len < 14:
    if ts.len <= 4: ts &= "0101000000"[ts.len - 4 .. ^1]
    elif ts.len <= 6: ts &= "01000000"[ts.len - 6 .. ^1]
    elif ts.len <= 8: ts &= "000000"[ts.len - 8 .. ^1]
    elif ts.len <= 10: ts &= "0000"[ts.len - 10 .. ^1]
    elif ts.len <= 12: ts &= "00"[ts.len - 12 .. ^1]
  try:
    return times.parse(ts, "yyyyMMddHHmmss")
  except:
    return now()

proc DateDiff*(dateTime1, dateTime2: AhkVar, timeUnits: AhkVar): AhkVar =
  if dateTime1 == nil or dateTime2 == nil or timeUnits == nil: return nil
  let dt1 = parseAhkTime(dateTime1.toString())
  let dt2 = parseAhkTime(dateTime2.toString())
  let unit = timeUnits.toString().toLowerAscii().strip()
  let diff = dt1 - dt2
  case unit
  of "seconds", "s": return toAhkVar(diff.inSeconds())
  of "minutes", "m": return toAhkVar(diff.inMinutes())
  of "hours", "h": return toAhkVar(diff.inHours())
  of "days", "d": return toAhkVar(diff.inDays())
  of "months":
    let yDiff = dt1.year - dt2.year
    let mDiff = dt1.month.int - dt2.month.int
    return toAhkVar(yDiff * 12 + mDiff)
  of "years", "y": return toAhkVar(dt1.year - dt2.year)
  else: return toAhkVar(diff.inSeconds())









