# AstHost.exe protocol

`AstHost.exe` is the AHK2 AST engine as one long-running helper process. It has no window (GUI subsystem, never a
console) and talks over its standard streams:

- **stdin**: one JSON object per line (UTF-8; `\uXXXX` escapes work too).
- **stdout**: one JSON object per line per request, in order. Replies are pure ASCII (non-ASCII as `\uXXXX`), so any
  pipe code page reads them.
- It **exits when stdin closes** (or on `{"cmd":"exit"}`). Nothing is written to stderr.

Every request may carry an `"id"` (any JSON value); the reply echoes it. A request that fails — bad JSON, a script the
engine chokes on, a missing file, a timeout — gets an error reply and the host keeps running:

```json
{"id":7,"ok":false,"error":{"code":"timeout","message":"the request took longer than 60000 ms and was stopped"}}
```

`code` is one of `bad-request`, `unknown-command`, `io`, `no-node`, `edit-failed`, `timeout`, `internal` (an engine
exception; `detail` has the stack). Each request runs on its own thread with a 256 MB stack and a time limit
(`"timeoutMs"`, default 60000).

Included files are parsed through the engine's `AstFileCache` (path + last-write time), so repeated parses with
`"includes": true` only re-read files that changed.

## Positions

A **position** is `{"line": 1-based, "col": 1-based, "offset": 0-based UTF-16 index}`. A **range** is `"start"` and
`"end"` positions; `end` is just past the last character, so `text.Substring(start.offset, end.offset - start.offset)`
(AHK: `SubStr(text, start.offset + 1, end.offset - start.offset)`) is the node's text. `file` is an index into the
reply's `files` array (0 = the parsed text; its entry is the path, or `null` for `text` without a path).

## Requests

### ping

`{"cmd":"ping"}` → `{"id":…,"ok":true,"version":"1.0","engine":"0.0.0.0"}`

### parse

`{"cmd":"parse", "path":"C:\\x\\main.ahk"?, "text":"…"?, "includes":false, "depth":N?}`

Give `text`, `path` or both (`text` wins, `path` then locates `#Include`s). `includes: true` follows `#Include` like
AutoHotkey (full paths; each file once). `depth` limits the levels returned (`childCount` marks cut-off nodes).

```json
{"id":1,"ok":true,
 "files":["C:\\x\\main.ahk","C:\\x\\lib\\JSON.ahk"],
 "nodeCount":1234,
 "tree":{"type":"Program","start":{"line":1,"col":1,"offset":0},"end":{"line":40,"col":1,"offset":812},"file":0,
   "children":[
     {"type":"BinaryExpr","value":":=","flags":{"assign":true},
      "start":{"line":1,"col":1,"offset":0},"end":{"line":1,"col":7,"offset":6},"file":0,
      "children":[{"type":"Identifier","value":"x",…},{"type":"Number","value":"1",…}]},
     {"type":"Include","value":"C:\\x\\lib\\JSON.ahk","flags":{"directive":"#Include lib\\JSON.ahk"},"meta":"#Include lib\\JSON.ahk",
      "start":{…the #Include line…},"end":{…},"file":0,
      "children":[{"type":"Class","value":"JSON",…,"file":1}]}
   ]}}
```

Node fields: `type`, `value` (omitted when empty), `flags` (omitted when none), `meta` (the raw Metadata string, omitted
when empty), `start`, `end` (`null` only for nodes a transform made — never in a parse reply), `file`, `children`
(omitted when none). Node types and flags: `NODES.md`.

Both forms also carry `"problems"` (as in the `errors` reply) and `"parseMs"`.

### parse, binary (`"format":"bin"`)

For big trees: the same tree (same nodes, types, ranges, flags, `depth`) as a compact binary AXT1 image that
AutoHotkey reads in place with `NumGet`/`StrGet` — nothing to parse. Where the bytes go:

| `"to"` | reply field | disk |
|---|---|---|
| `"file"` (default) | `"bin"`: the path. `"out"` names it (replaced if it exists); without it the host picks a temp file (`%TEMP%\AstHost\parse-<pid>-<n>.axt`, deleted when the host exits) | yes |
| `"shm"` | `"shm"`: the name of a shared memory section (`Local\AstHost-<pid>-<n>`, or `"name"` if given). Open it with `OpenFileMappingW` + `MapViewOfFile`. The host keeps it until `{"cmd":"release","shm":name}` (or `release` without `shm` = all) or exit; your mapped view keeps it readable after that | no |
| `"base64"` | `"base64"`: the bytes, inline in the reply (decode with `CryptStringToBinaryW`, flag 1) | no |

```json
{"cmd":"parse","path":"C:\\x\\main.ahk","includes":true,"format":"bin","to":"shm"}
{"id":…,"ok":true,"shm":"Local\\AstHost-4711-3","nodes":105077,"bytes":5630124,"files":[…],"problems":[…],"parseMs":179,"buildMs":52,"writeMs":2}
```

`src\host\AxtReader.ahk` (next to the exe in `studio\bin`) is a tested reader: `AxtTree.FromShm(name, bytes)`,
`.FromFile(path)`, `.FromBase64(b64)`, then `t.Type(i)`, `t.Value(i)`, `t.Start(i)`, `t.Children(i)`, `t.Has(i, "assign")`, …

**Layout** (little-endian; every offset in bytes from the start):

Header, 64 bytes:

| at | type | field |
|---|---|---|
| +0 | char[4] | `AXT1` |
| +4 | u32 | version = 1 |
| +8 | u32 | nodeCount |
| +12 | u32 | recordSize = 48 |
| +16 | u32 | nodesOffset (first node record) |
| +20 | u32 | stringsOffset |
| +24 | u32 | stringsBytes |
| +28 / +32 | u32 / u32 | typeCount / typeTableOffset (u32 string refs: node type names) |
| +36 / +40 | u32 / u32 | flagCount / flagTableOffset (u32 string refs: the name of flag bit i) |
| +44 / +48 | u32 / u32 | fileCount / fileTableOffset (u32 string refs: full paths; 0xFFFFFFFF = pasted text) |
| +52 | 12 bytes | reserved (0) |

Node record, 48 bytes, in **pre-order** (a node's subtree is the contiguous run after it); node 0 = Program:

| at | type | field |
|---|---|---|
| +0 | u16 | type (index into the type table) |
| +2 | u16 | file (index into the file table; same as JSON `file`) |
| +4 | u32 | flags: bit i = flag i of the flag table |
| +8 | u32 | parent (0xFFFFFFFF for the root) |
| +12 | u32 | nextSibling (0xFFFFFFFF if last) |
| +16 | u32 | childCount (the first child, if any, is this index + 1) |
| +20 | u32 | startOffset (UTF-16 index into that file's text, = JSON `start.offset`) |
| +24 | u32 | endOffset (exclusive) |
| +28 | u32 | startLine |
| +32 | u32 | endLine |
| +36 | u32 | value: string ref (source spelling), 0xFFFFFFFF if none |
| +40 | u32 | meta: string ref to the raw metadata text (= JSON `meta`), 0xFFFFFFFF if none |
| +44 | u32 | startCol |

The end column is not stored: `endOffset` covers it.

**String ref**: a byte offset into the string table; there, a u32 length in UTF-16 units, the UTF-16LE units, and padding
to 4 bytes. Each distinct string is stored once.

**Flags**: bit i is the boolean flag named by flag-table entry i: `truncated`, `attached`, `static`, `member`, `byref`,
`variadic`, `optional`, `command`, `inline`, `executes`, `comma`, `paren`, `space`, `implicit`, `bare`, `hotif`,
`recovery`, `duplicate`, `assign`, `block` (read the names from the table; new ones are appended). The JSON form's
non-boolean flags come from the node's value/meta the same way: Declaration `scope` = meta; Catch `var` = meta,
`classes` = value split on commas; Switch `caseSense` = meta; Hotstring `options`/`trigger`/`replacement` = the value
`:options:trigger::replacement`, `comment` = meta after `inline;`; Directive `name`/`args` = the value split at the first
blank, `comment` = meta after `hotif;`; String/Warning `raw` = meta after `raw:`; Include `directive` = meta.

**depth**: with `"depth"`, a cut-off node keeps its real `childCount`, has the `truncated` bit, and no records follow for
its children.

```ahk
buf := FileRead(path, "RAW")                       ; or: p := MapViewOfFile(...) and NumGet(p, off, …)
nodes := NumGet(buf, 16, "UInt"), strs := NumGet(buf, 20, "UInt")
str(ref) => ref = 0xFFFFFFFF ? "" : StrGet(buf.Ptr + strs + ref + 4, NumGet(buf, strs + ref, "UInt"), "UTF-16")
typeName(t) => str(NumGet(buf, NumGet(buf, 32, "UInt") + t * 4, "UInt"))
rec := nodes + i * 48
type := typeName(NumGet(buf, rec, "UShort")), start := NumGet(buf, rec + 20, "UInt"), end_ := NumGet(buf, rec + 24, "UInt")
value := str(NumGet(buf, rec + 36, "UInt"))
```

The harness keeps the two forms in step: `harness.ps1 host` compares bin with JSON on the cases (file, shm, base64,
depth), and `harness.ps1 host --corpus` on every corpus file with includes.

### outline

`{"cmd":"outline", "path"?, "text"?, "includes":false}`

```json
{"id":2,"ok":true,
 "functions":[{"name":"Add","static":false,
    "params":[{"name":"a","byref":false,"variadic":false,"optional":false,"start":…,"end":…,"file":0},
              {"name":"b","byref":false,"variadic":false,"optional":true,"default":"1","start":…,"end":…,"file":0}],
    "fatArrow":false,"start":…,"end":…,"file":0,"body":{"start":…,"end":…}}],
 "classes":[{"name":"Widget","static":false,"extends":"Gui","extendsRange":{"start":…,"end":…},
    "members":[
      {"kind":"var","name":"count","static":true,"init":"0","start":…,"end":…,"file":0},
      {"kind":"method","name":"__New","static":false,"params":[…],"start":…,"end":…,"file":0},
      {"kind":"property","name":"Name","static":false,"params":[],"accessors":["get","set"],"start":…,"end":…,"file":0},
      {"kind":"class","name":"Inner","static":false,"extends":null,"members":[…],"start":…,"end":…,"file":0}],
    "start":…,"end":…,"file":0}],
 "hotkeys":[{"trigger":"^!t","inline":true,"start":…,"end":…,"file":0},
            {"trigger":"CapsLock","remap":"Ctrl","start":…,"end":…,"file":0}],
 "hotstrings":[{"options":"","trigger":"btw","executes":false,"replacement":"by the way","start":…,"end":…,"file":0}],
 "globals":[{"name":"counter","kind":"assign","count":3,"start":…,"end":…,"file":0},
            {"name":"cfg","kind":"declaration","count":1,"start":…,"end":…,"file":0}],
 "labels":[{"name":"Start","start":…,"end":…,"file":0}],
 "includes":[{"path":"C:\\x\\lib\\JSON.ahk","directive":"#Include lib\\JSON.ahk","start":…,"end":…,"file":0}],
 "files":["C:\\x\\main.ahk", …]}
```

- `functions`: functions in global scope (top level and blocks of top-level if/loop/try; not those inside function or
  hotkey bodies). `default` / `init` are the source text (only for the main file).
- `globals`: first definition of each global name (top-level assignment `x := …` or `global x` at top level), with how
  many definitions there are.
- `hotkeys`: stacked hotkeys (`a::` above `b::x()`) are listed each.
- `includes` with `path: null` are `#Include` lines that were not followed (`includes: false`).

### errors

`{"cmd":"errors", "path"?, "text"?, "includes":false}`

```json
{"id":3,"ok":true,"files":[null],
 "problems":[{"kind":"Warning","message":"Expected RParen in grouped expression at line 3:1, got Identifier",
              "start":{"line":3,"col":1,"offset":17},"end":{"line":3,"col":2,"offset":18},"file":0}]}
```

`kind` is `Error` or `Warning`. A problem without a range of its own (an include that could not be read) is placed on
the `#Include` line in the file that includes it.

### edit

`{"cmd":"edit", "text":"…", "op":"replace|insertBefore|insertAfter|delete|replaceRange", "nodeRange":{"start":…,"end":…}, "nodeType":"Call"?, "newText":"…"}`

`nodeRange.start` / `.end` may be positions as the parse reply gives them (their `offset` is used; `{line, col}` works
too) or plain offsets. The node is found by its exact range (with `nodeType`, the node of that type; otherwise the
outermost one — the statement rather than the expression that is all of it). The new text is the old one with a single
splice; nothing is re-emitted, so every other byte stays as it was.

- `replace`: the node's text becomes `newText` exactly.
- `insertBefore` / `insertAfter`: `newText` (one or more lines) as new lines above / below the **statement** the node is
  in, indented like it (the text's own common indentation is replaced, relative indentation kept) and with the line
  break the file uses there. After a statement means after its line, trailing comment included.
- `delete`: a statement alone on its lines goes with those lines and its trailing comment; an item of a comma list
  (arguments, array items, object pairs, parameters) goes with its comma; anything else loses just its text.
- `replaceRange`: splice `newText` into `nodeRange` without looking for a node.

```json
{"id":4,"ok":true,"text":"…the new source…","changed":true,
 "written":{"start":{"line":3,"col":5,"offset":40},"end":{"line":3,"col":11,"offset":46}},
 "problems":[…every parse problem of the new text…],
 "newProblems":[…those the old text did not have…]}
```

`written` is what was inserted/replaced, as a range in the new text (empty for a delete). Problem entries look like the
`errors` reply's. A no-op edit returns the text byte for byte with `"changed":false`.

### flow

`{"cmd":"flow", "path":"C:\\x\\main.ahk", "text"?, "flowJson":{…} or "…", "includes":true, "log":false}`

Runs a flow (the same JSON the Workbench and the harness use; e.g. `harness\flows\minify-aggressive.json`) through
`ExecuteFlow`. With a `path`, `#Include`s are followed by default.

```json
{"id":5,"ok":true,"output":"…the flow's output (minified script, report, …)…","ms":412,"log":["…"]}
```

### nodeAt

`{"cmd":"nodeAt", "path"?, "text"?, "offset":N}` or `"line":L, "col":C`

```json
{"id":6,"ok":true,"offset":57,"nodes":[{"type":"Program",…},{"type":"If",…},{"type":"Block",…},{"type":"Call",…},{"type":"String",…}]}
```

The nodes containing the position, outermost first, without their children (`childCount` says how many they have).

### release

`{"cmd":"release","shm":"Local\\AstHost-4711-3"}` (or without `shm`: every section this host published) →
`{"id":…,"ok":true,"released":1}`. Views that readers still have mapped stay valid.

### exit

`{"cmd":"exit"}` → `{"id":…,"ok":true}`, then the process ends.

## Example (AutoHotkey v2)

```ahk
shell := ComObject("WScript.Shell")
exec := shell.Exec('"' A_ScriptDir '\bin\AstHost.exe"')   ; no window: AstHost is a GUI-subsystem exe
exec.StdIn.WriteLine('{"id":1,"cmd":"outline","path":"' StrReplace(file, "\", "\\") '"}')
reply := exec.StdOut.ReadLine()                          ; one line of ASCII JSON
```
