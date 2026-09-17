# AST node types

What the parser produces (`AhkAstEngine.Parse`) and what `AstHost.exe` sends as JSON (see `src\host\PROTOCOL.md`).
Every node has a **type**, an optional **value**, **flags** (the node's `Metadata` split into named fields; the raw
string is also sent as `meta`), a source **range** and its **children** in source order.

## Ranges

- `start` / `end` = `{line, col, offset}`: the whole text the node spans in the file it was parsed from, from its
  leftmost child to its closing bracket/brace. `offset` counts UTF-16 characters from the start of the file
  (`source.Substring(start.offset, end.offset - start.offset)` is the node's text); `line`/`col` are 1-based and `end` is
  just past the last character.
- Ranges refer to the **original** text. Inside and after a joined continuation section they point at the real lines;
  a node that reaches into a section runs through its closing `)` (text inside a section is one piece).
- `file` = index into the reply's `files` (0 = the parsed text). The children of an `Include` the engine followed are
  that file's code, so their ranges are in that file; the Include's own range is its `#Include` line.
- Every parsed node has a range. `Warning`/`Error` nodes have one too (the token they are about); the message may
  still say "at line X:Y".
- Children nest inside their parent, with one exception: a **comment the parser attached to an expression** (a `(`
  keeps comments of the lines above it; a continued line's comment moves to the end of the logical line) keeps its own
  range, which may lie outside the parent's. Such comments have `flags.attached = true` and never widen the parent.
- Comments between statements are children of the block, in order, before the statement they precede. A comment
  written *inside* a multi-line statement is hoisted the same way: its range then lies inside that statement's range.
- Empty ranges (`start == end`): `Omitted` items, an empty `Arguments` of a command call, an empty `CaseBody`.

The node's `Line`/`Column` in C# are its anchor token's position (the `(` of a call, the operator of a binary
expression, the name of a member) and `EndLine` is a layout hint for the emitter; use the range fields instead.

## Statements and declarations

| type | value | children | flags / notes |
|---|---|---|---|
| `Program` | | statements | `inlinedRanges` in meta when the text has `; --- begin:` markers |
| `Method` | name | `Parameters`, then `Block` or `FatArrowBody` | a **function** (top level, nested or class method). `static`. `get`/`set` accessors are Methods named `get`/`set` |
| `Parameters` | | `Parameter`… | range includes the `(` `)` (or `[` `]` of an indexed property) |
| `Parameter` | name (`*` for a bare variadic) | default value expression | `byref`, `variadic`, `optional`; range covers `&name := default*?` |
| `FatArrowBody` | | expression | `Name() => expr` |
| `Class` | name | `Extends`?, members | `static` (nested static class). Members: `Method`, `Property`, `StaticAssign`, `Declaration`, `Class`, `Comment` |
| `Extends` | base name (`WebView2.Base`) | | range = the base name only |
| `Property` | name | `Parameters`? (indexed), then `Block` of get/set `Method`s, or an expression (`Prop => expr`) | `static` |
| `StaticAssign` | name (`a.b` path) | value, then more `StaticAssign`/`Declaration`/expressions for `, x := 1` | class variable `[static] x := v`; `static` |
| `Declaration` | name | initializer (the value for `:=`; `BinaryExpr` for `+=` etc.), more `Declaration`s for `a, b` | statement: `scope` = `global` / `local` / `static`. In a class: bare `x` / `static x`, `member`, `static` |
| `Directive` | text of the line (`#Include x.ahk`) | `#HotIf`: the condition expression | `name` (`#Include`), `args`, `hotif`, `comment` |
| `Include` | full path | the included file's statements | engine-followed `#Include` (C#: `ChildFile` set). `duplicate` (already included: no children), `directive` = original text |
| `Hotkey` | trigger (`^!t`, `~*Ctrl`) | `Block` or one statement; a stacked hotkey is the child of the one above | `inline` (action on the same line) |
| `Remap` | origin key | `KeyName` (value = destination) | `a::b` |
| `Hotstring` | `:opts:trigger::replacement` (with X: `:opts:trigger::`) | with X / an action: the statement or `Block` | `options`, `trigger`, `replacement` (text hotstrings), `executes`, `inline`, `comment` |
| `Label` | name | | `Name:` |
| `Goto` | label name | | `Goto Label` (a `Goto(expr)` call stays a Call) |
| `Block` | | statements | `{ … }` |
| `If` | | condition, then-statement/Block, `Else`? | |
| `Else` | | statement/Block | range starts at `else` |
| `While` | | condition, body, `Until`?, `Else`? | |
| `Loop` | variant (`Parse`/`Files`/`Reg`/`Read`) or empty | count/args (`Omitted` for skipped), body, `Until`?, `Else`? | `comma` (`Loop Parse, x`) |
| `For` | | `ForVars`, collection, body, `Until`?, `Else`? | `paren` (`for (k, v in x)`) |
| `ForVars` | | `Identifier`s (`byref`, `variadic` `*`), `Omitted` | range = the variable list |
| `Until` | | condition | child of the loop it closes |
| `Try` | | body, `Catch`…, `Else`?, `Finally`? | |
| `Catch` | class list (`TypeError, ValueError`) | body | `var` (the `as` variable), `classes` |
| `Finally` | | body | |
| `Switch` | | subject?, `Case`…, `Default`? | `caseSense` |
| `Case` | | values…, `CaseBody` | range from `case` |
| `Default` | | `DefaultBody` | |
| `CaseBody`, `DefaultBody` | | statements | |
| `Return`, `Throw` | | expression? | |
| `Break`, `Continue` | | label (`Identifier`/`Number`)? | |
| `MultiStatement` | | expressions | `a := 1, b := 2` |
| `Comment` | the comment text (`; x` or `/* … */`) | | `block` for `/* */`, `attached` (see above) |
| `Warning` | message | | parser warnings (`Expected RBrace in block at line 9:1, got EOF`), unknown constructs, `; WARNING:` comments (`raw`) |
| `Error` | message | | `recovery` (the statement the parser skipped: range = the skipped text), include failures (range = the #Include line) |
| `Unknown` | token text | | an unrecognised class member (a `Warning` points at it) |

## Expressions

| type | value | children | flags / notes |
|---|---|---|---|
| `BinaryExpr` | operator | left, right | **assignments are BinaryExpr** `:=` `+=` `.=` `??=` … with `assign`; also `.` concatenation with blanks, `&&`, `is`, `~=`, … |
| `Concat` | `" "` or `""` | left, right | implicit concatenation `a b` / `a"x"`; `space` |
| `UnaryExpr` | operator (`-` `!` `~` `&` `++` `--` `not`) | operand | |
| `PostfixExpr` | `++` / `--` | operand | |
| `Ternary` | | condition, then, else | |
| `Grouped` | | expression (+ attached comments) | `( … )`; `implicit` = AutoHotkey's grouping with no parentheses in the source (`a && b := 1`) |
| `Sequence` | | expressions | `(a, b)` |
| `Call` | | callee, `Arguments` | `command` = paren-less `MsgBox "x"`: its Arguments has `command` too and a range of just the arguments |
| `Arguments` | | argument expressions, `Omitted` | range includes `(` `)` |
| `Member` | member name | object | `a.b` (`obj.%name%` has the deref as value) |
| `Index` | | object, index expressions | `a[i, j]` |
| `Variadic` | | expression | `f(args*)` |
| `UnsetModifier` | | expression | `f(x?)` |
| `FatArrow` | | `Parameters`, body expression | `(a) => a + 1`; `bare` for `x => …` |
| `Array` | | items, `Omitted` | `[1, , 3]` |
| `Object` | | `KeyValue`… | `{a: 1}` |
| `KeyValue` | | key (`Identifier` or expression), value | |
| `Identifier` | name | | variables, function names; `%x%` / `pre%x%` dynamic names keep their text |
| `Number` | literal text (`0x1F`, `1.5e3`) | | |
| `String` | literal with its quotes | comments found in a continuation section's raw text | `raw` = the original source of a continuation-section string |
| `This`, `Super` | | | |
| `Omitted` | | | an empty list item (empty range) |
| `KeyName` | destination key | | in `Remap` |

Names are case-insensitive in AutoHotkey; the values keep the source's spelling.

## Built-in names

`AhkBuiltins` (src\ast\AhkBuiltins.cs, generated from the AutoHotkey docs with `harness.ps1 builtins --docs <dir>`) lists
the built-in functions with their argument counts, the built-in variables and the built-in classes with their bases.
