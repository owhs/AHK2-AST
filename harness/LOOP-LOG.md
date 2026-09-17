# Loop log

One entry per fix iteration (newest last). Format:

```
## YYYY-MM-DD — <signature>
root cause: <one line>
files: <src files touched>   case: harness\cases\<name>.ahk
core hard fails: <before> → <after>   newly failing: 0
```

## 2026-09-11 — baseline (harness created, no engine changes)
first full run `out\20260911-000209-core`: 1360 unique core scripts, 981 accepted by AHK and tested, 19,620 checks.
core hard fails: **2120** (309 articles, 31 signatures) · soft: 2363 · cases: 2/8 clean.
Top signatures: minify/compress "Syntax error." (208 articles) · Nim transpiler IndexOutOfRange (81) ·
minify "does not contain a recognized action" (75) · tree-shake "&" requires a variable (38) ·
`for k, v, in in` emission (30) · parser false errors on valid code (20 articles).
(Later harness fix: nested `<Lib>` includes now resolve via a Lib junction, so a few previously skipped
scripts will start being tested; expect the tested count to rise slightly on the next run.)

## 2026-09-11 — output rejected by AHK: Unexpected reserved word (`for k, v, in x` → `for k, v, in in x`)
root cause: ParseFor took any identifier after a comma as the next loop var, so the reserved word `in` became var #3.
fix (structural): for-var list parsed as comma-separated slots (`[&]name` | `*` | omitted) terminated by the reserved
word `in`; omitted slots kept as `Omitted` nodes (a trailing `k, v,` still passes 3 vars to __Enum, verified in AHK);
emitter renders omitted slots as `k, v,` / `, v`; Nim emitter skips them. Analyses already filter Identifier children.
files: src\ast\AhkParser.cs (ParseFor, IsForInKeyword), src\ast\AstEmitter.cs (ForVars), src\plugins\NimTranspilerPlugin.cs
case: harness\cases\for_var_slots.ahk (still fails minify-rename/compress: separate closure-capture rename bug, tier 2)
tier-1 sweep (parse+roundtrip+beautify): 87 checks fixed, newly failing: 0 → 167 hard fails left in tier-1 flows

## 2026-09-11 — command-style call statements were parsed as concatenations (structural)
root cause: `MsgBox "hi"` was Concat(Identifier MsgBox, "hi"), `MyFunc 1, 2` a MultiStatement, `WinGetPos ,, &W` lost its
call entirely and emitted as the invalid `WinGetPos, , &W`. Every analysis saw a variable read instead of a call.
fix: TryParseCallStatement builds Call(callee, Arguments{command}) for `Name …`, `a.b.c …` and bare `Name`, using AHK's
exact rule measured with 40 probes: after the callee + whitespace, anything but a binary/assignment operator starts
the args (`f - 1` = f(-1), `y ++` is a call, `f ? a : b` / `f . x` are not; `(f) 5` / `%"f"% 5` are concatenations);
omitted args kept (`f ,, 3`); a line ending in a comma continues on the next line. Emitters keep the paren-less form;
the minifier switches to parens only inside one-line comma chains. `Goto label` excluded (label name, not expr).
AutoFix legacy-MsgBox rewrite now emits explicit parens. Plugins with Concat{space}-as-call hacks (Nim, AutoFix)
keep working through their Call paths.
files: AhkParser.cs (TryParseCallStatement, IsExpressionContinuation, Adjacent), AstEmitter.cs (Arguments),
TransformAndFormatPlugins.cs (minify Arguments + chain flag), AutoFixPlugin.cs   case: cases\call_statements.ahk

## 2026-09-11 — remaps parsed as hotkey bodies (issue #2, structural)
root cause: `a::b` was Hotkey{body: b} (a call now), `XButton2::^LButton` a parse error; minify dropped the `^`, trace
wrapped remaps into code.
fix: new Remap node (Value = origin, child KeyName = destination text), recognised when the same-line tokens are
contiguous and spell one key with optional ^!+#<> modifiers. Rule verified against AHK's own ListHotkeys output
(remaps appear as *key / *key up): Return/Pause/Joy1../vkNNscNNN are NOT destinations; Sleep/Help/Enter/+/1/ä are.
Shared rule in src\ast\AhkKeyNames.cs. Harness HotOracle corrected with the same ground truth (it wrongly treated
`#Left::Return` as a remap — earlier trace "hotkey-diff" reports on those were false positives).
files: AhkParser.cs (TryParseRemap), AhkKeyNames.cs (new), AstEmitter.cs + minify emitter (Remap)
case: cases\issue2_remap.ahk now clean on all 20 checks
full sweep vs full baseline: newly failing 0, fixed 506 → core hard fails 2120 → 1632

## 2026-09-11 — parser false errors batch (A–F)
- `global Counter += delta`: declaration initialisers accept any assignment operator → BinaryExpr(op, name, rhs)
  (still exactly "the value assigned"); emitters write it back 1:1 via AstEmitter.IsCompoundDeclarationInit.
- `<<=` `>>=` were lexed as TokenType.Assign, `>>>=` split in two: new token types + TokenKinds.IsAssignment as the
  single source of truth (six drifted copies of the operator list replaced).
- `"Class: " class`: implicit-concatenation operand check now accepts keywords that can only be variable names there
  (class/extends/new/global/local/static; not else/until/catch).
- `static Name(params) {` inside a function body: static nested function, not a declaration.
- Comma continuation lines (incl. across comment lines) after every list item: one AtListComma helper shared by
  comma sequences and case value lists.
files: Token.cs, AhkLexer.cs, AhkParser.cs, AstEmitter.cs, TransformAndFormatPlugins.cs   case: cases\parser_misc_valid.ahk
open: expression continuation sections whose lines are joined as text (GuiEnhancerKit, VisualDiff, issue #4 variant)
need a lexer-level design (join raw lines per section options, then lex) — next structural item.
harness: "Changes since" now compares every check with its latest known status (state\latest-status-<tier>.json),
so runs with fewer flows can't hide regressions; `ast` command added (AST dump + emit).

## 2026-09-11 — continuation sections are text (issue #4, structural)
root cause: the lexer emulated sections ad hoc: quoted sections became one raw String token, sections after an
operator were lexed line by line as code. Wrong for strings spanning lines in expression sections (parse errors),
for `"First, ; c"` + section (comment stripped inside quotes; the string really spans the join) and for option /
indentation semantics (minify runtime diffs). Probed AHK 2.0.19 with ~25 forms: sections are merged as TEXT into the
preceding code line (no separator; its comment stripped, not quote-aware), lines indentation-trimmed by the FIRST
line's indent (relative indent kept), joined with `n / Join, rest of the `)` line appended; quotes of an open string
are literal inside the section; `(` starts a section only if the line has no `)`; `2 *` + section `1 + 1` = 3, i.e.
joining, NOT grouping (the previous Grouped model would give 4).
fix: new src\ast\ContinuationJoiner.cs runs before lexing (virtual line feed U+E000 = whitespace in code, real LF in
strings), with a SourceMap back to the original so strings that span a join keep `raw:` metadata → comment children
and 1:1 emission (your test 64 design) still work. AstEmitter.MultilineStringLiteral regenerates a section with the
exact options needed (LTrim0/RTrim0) or a `n one-liner when a line starts with `)`. Hotstring/hotkey sections keep the
old path. Also ;-comment/%deref%/hotkey scans stop at joined line feeds.
tests: tests\VerifyTest.cs #65 updated — it asserted the Grouped model; now asserts the comment node survives
(evidence in the test comment). case: cases\continuation_sections.ahk (s1–s4, options, quotes, precedence), clean.
issue #4: both issue cases now clean on all 20 checks.
harness: shutdown can no longer hang (job-close kills workers, Environment.Exit at the end).

## 2026-09-11 — regression caught + batch 3 (names, headers, includes)
- REGRESSION from the joiner (11 checks, Seek_(SearchTheStartMenu).ahk): `MsgBox` ⏎ `(` ⏎ `"text"` ⏎ `)` joined to
  `MsgBox"text"`. AHK probes: a space is inserted when the join point is in code (not inside an open string) and the
  preceding line ends with a word character (`y`+`z` → `y z`, `f`+`"x"` → f("x")); `"p"`+`"q"`, `(y)`+`y` and text
  inside a string get nothing. Rule added to ContinuationJoiner; case lines added. Fixed.
- Dynamic variable names `b%A_Index%`, `pre%n%post`, `%n%x` were Concat of separate variables (wrong reads, wrong
  assignment targets, foldable as strings). Lexer now emits ONE Identifier. Consumers updated to the real semantics:
  tree-shaker (exact dynamic prefixes of any length keep matching globals; dynamic assignment targets never pruned;
  inner names are reads), minifier (inner names renamed consistently; variables reachable by prefix — or everything
  for `%a%b` / plain `%x%` reads — excluded from renaming and single-use inlining).
- `static types := Map(), types.CaseSense := false`: chain items parsed as `name(.name)* := expr` / bare name /
  expression (ParseClassVarChainItem, shared by both class-var branches); chained bare names no longer re-emit
  `static`; tree-shaker never prunes dotted items (initializer statements); renamer renames dotted paths per segment
  and treats chained items as class members.
- `for (i, v in list)`: parenthesised header (Metadata "paren", emitted 1:1).
- Engine #Include resolution now expands %A_ScriptDir%, %A_LineFile% (as the including file), shell folders, A_Space
  /A_Tab — RichIDE's 22 %A_ScriptDir% includes were silently dropped when inlining.
- harness: comment-only / block-comment-only files may legitimately produce empty output (3 false positives).
files: AhkLexer.cs, AhkParser.cs, AstEmitter.cs, AhkAstEngine.cs, ContinuationJoiner.cs, LogicShakerPlugin.cs,
TransformAndFormatPlugins.cs   case: cases\names_and_headers.ahk (clean on all 20 checks)

## 2026-09-11 — PAUSED (by user)
State: core hard fails 1,479 (last completed full sweep 20260911-014823-core). The sweep for batch 3 was stopped
mid-run, so batch 3 is verified by cases + VerifyTest only (all pass), not yet by a full sweep.
Written but NOT yet built/verified: #Include inside blocks (AhkAstEngine.ProcessIncludesIn) + case
cases\include_in_block.ahk; `this.x.Method args` / `super.Method args` call statements (AhkParser.TryParseCallStatement)
+ extra lines in cases\call_statements.ahk.
Resume: .\harness\harness.ps1 verify; .\harness\harness.ps1 cases; .\harness\harness.ps1 run  (expect newly failing 0),
then continue tier 1 (≈15 scripts left), then tier 2 (minify inlining/renaming, optimise folding, Nim).

## 2026-09-11 — "fix them all in one go" (no loop)
Process: per fix → cases + targeted `check`/`--flows`; full sweeps only as gates.
Sweep 20260911-042530-core: 1,479 → **105** hard fails (30 articles); 17 newly failing (2 causes, both fixed below).
Structural rules added (each AHK-verified with /Validate probes):
- **StatementHead** (src\ast\StatementHead.cs, both emitters): a line starting `name <op>`, `a.b <op>`, `name "s"`
  or a literal is a call statement / no action to AHK → the leading operand is parenthesised; a group that opens
  a line is written on one line (a lone `(` line is a continuation section). Transforms may now drop parens freely.
- **AhkConst** (src\plugins\AhkConst.cs): one AHK-accurate constant model (Integer/Float/String, truthiness,
  `/` → Float, `==` case-sensitive, `=` ASCII-only, numeric-looking strings unknown, `a||b` returns an operand)
  used by the optimise folder, the tree-shaker folder and its dead-branch evaluator. Condition context may also
  fold a constant right side (`x && false`).
- Loops own their tail: `until` and `loop/for/while … else` are children of the loop (ParseLoopTail); emitters,
  trace, AutoFix, Nim pick the body as the last non-Until/Else child. A standalone `until` statement still parses.
- A bare block after `Name(...)` is written as its statements (else AHK reads a function definition); optimise
  splices pruned-branch blocks.
Fixes: minify (case/default `{` placement, stacked hotkeys, hotstring actions = issue #3, goto not comma-chained,
local rename vs nested-lambda params, classes not renamed when Type()/__Class is used, composite member derefs
renamed, InheritsFromCSModule cycle = stack overflow); shaker (`(_ := unset)` kept, side-effect-free comma items
dropped, `%x%` / `obj.On%x%` derefs read their names + keep members by prefix, `%fn%()` reads fn, bare
global/local/static never pruned, assume-global functions not local-shaken); parser (`Goto Label` node,
`default:` label outside switch, `default:` not last, unterminated block comment to EOF); trace (stacked hotkeys,
bare global/static first, never a line before until/else — AHK 2.0.19 *crashes* on that, 1-line capped snippets);
Nim (base-class cycles). Harness: `#Include` inside continuation strings ignored; `#include *i` isn't code;
`ast <file> --flow <name>` prints a flow's output.
Cases added: statement_heads, hotkeys_stacked, nested_class_self_base. VerifyTest #61 now expects `(x) == y && …`.
Final gate 20260911-053454-core: **0 hard fails** (984 tested articles × 20 checks), newly failing 0, soft 2,122.
Late fixes: loop-tail lookahead now rolls back skipped comments (comments were duplicated/moved = +150
not-idempotent); optimise PruneDeadBranches drops statements after an unconditional return/throw (up to a label).
Open (soft only): not-idempotent comment/blank-line drift (roundtrip 813, beautify 590), ast-diff 173,
tree-shake-everything empty outputs (185, mostly library files with no entry point), VarUnset warnings after
aggressive shaking/minify renaming (~260), trace-inserted lines after a `return` (40), 2 Unknown-node parses.

## 2026-09-11 — large root-cause batch (fidelity oracles, minify levels, compatibility contract)
User asked for one large patch rather than small tweaks, plus honest classification of options that can't fit
every script. Harness first, so the data is trustworthy:
- **token-diff oracle** (roundtrip/local): the 1:1 emit must lex to the original's tokens (minus comments,
  line breaks, case, trailing commas). **Compatibility** per flow (`all` / `entry` / `best-effort`), **Renames**
  (warnings compared by kind), reducer can target soft issues with `--match`, same-mode idempotence, corpus
  role = lib for files with no auto-execute code / hotkeys, `runtime_language.ahk` conformance case (dynamic
  names, VarRefs, closures, meta-functions, statics, exceptions, loop/switch variants, #HotIf).
- **Minify levels** (MinifyLevel Safe/Aggressive/Extreme/Custom, flags flip to Custom only on change): Safe = always
  identical, Aggressive = + globals/functions/classes + one-lining with runtime-reachable names kept (names
  spelled as strings, dynamic names, Type()/__Class), Extreme = + properties (best-effort). Old ad-hoc
  minify flows replaced by 08-minify-safe / 09-minify-aggressive / 10-minify-extreme.
Root causes fixed (engine):
- Parser trivia: optional-continuation look-aheads (else/catch/finally/until) buffered comments and moved them
  in front of the statement → non-consuming `AtContinuation`; Block/Class/Switch/Object/Include/multi-line
  string+comment EndLine recorded (phantom blank lines); trailing comments joined on the statement's last line
  too; engine marker warnings re-emitted verbatim; only column-1 markers rebuild includes.
- Parser gaps: `#HotIf expr` parsed into the directive (was opaque text: renamers/shakers broke it);
  `global a, b := 1` stays one statement; class-body `#DllLoad`, class var chains continued after comment lines;
  `Loop Parse,` comma and same-line comments kept; `x => e` kept bare; implicit precedence groups written as
  the source had them; leading `.member` continuation after a trailing comment was read as concatenation
  (lexer now defers such comments to the logical line end); comment inside a parenthesised concatenation;
  statement heads written in the source are kept verbatim (AHK's own rule has quirks: `x = 2 ? a : b` ok).
- Emitter: multi-line groups never start a line with an unclosed `(` (was silently a continuation section).
- Shaker: property bodies and lambdas are function scopes; same-named members (`__new` + `static __new`) both
  tracked; `local this`; hotkey bodies are function scopes (nested functions local).
- Minify: `%expr%` derefs renamed by parsing the expression (was regex on text: `%this.valRef%` → `%b%`);
  functions defined under a hotkey or inside a top-level block are global; nested classes never renamed as
  globals (`class Error extends Error`); optimiser no longer dedupes positional directives (#HotIf…).
- Trace: helpers inserted after the leading directives (not after a final `return`); no line trace between a
  label and its loop.
Results: core 0 hard; soft 2,122 → 756 → (see gate below); token-diff 42 → 1; ast-diff 173 → 1; all three minify
levels clean on the core corpus.
Follow-up (same day): converter + history tiers swept for the first time (roundtrip): found colon-key hotkeys
(`:::`, `+:::`), `` `;:: ``, spaced hotstring options (`: :btw::`), hotstring `{` body kept on its own line,
non-ASCII names (`★a★b★c:`), omitted array elements, case-list trailing comma, `Loop Files.Length`, `if (x)()`;
all fixed (cases\parser_edges_v2.ahk). Trace: hotkey + function definition traced as a function. Beautify: include
markers like the emitter; compact mode formats inlined files inline. Idempotence: include EndLine, string/comment
EndLine, same-mode second pass.
**Final gate:** core 977 tested → 0 hard, 3 soft (2 data-only scripts shaken empty = correct; 1 marker rebuild,
by design); converter 659 → 0 hard, 0 soft; history 1,566 → 0 hard, 0 soft. Cases 19/19 (2 documented Extreme
incompatibilities). VerifyTest passes. (7 AxStudio.* files left the tested set: AHK now rejects the originals.)

## 2026-09-11 — user report: AHK2-ActiveX-Gui Showcase.ahk broken after the engine
1. No steps (inline) → page unstyled. Root cause: inlining moves an included file's code into the output, so
   `A_LineFile` there became the output's path; the library resolves its CSS/HTML/icons with
   `static LibDir := SubStr(A_LineFile, ...)`. Fix: the include inliner pins every `A_LineFile` read in an inlined
   file to that file's original path (AhkAstEngine.PinLineFile); Optimise's self-run-guard patch recognises the
   pinned form. Verified with a sandboxed probe (ReadLib("themes\base.css") 11,699 bytes before and after inlining).
2. Tree-shake (ShakeLibraryDeclarations) → "Unknown method: AddRow". Root cause: components register themselves
   in a static initializer (`class AxRow { static _reg := AxRich.Register(...) }`); nothing names AxRow, so the
   shaker deleted the class and the registration. Fix: classes whose static initializers have load-time effects
   (a call to anything but a pure built-in; lambdas excluded) are entry points, with those initializers.
   Verified: shaken Showcase runs in the sandbox without the error.
Why the harness missed both: they only show when the script runs (/Validate loads fine), and corpus scripts are
not executed. Case added: cases\linefile_and_selfregistration.ahk (@run). Harness: exec --shots now also takes a
final screenshot at the deadline (the IE/ActiveX control still paints nothing on the hidden desktop).

## 2026-09-11 — engine speed (the four suggested fixes, measured)
Tooling: `bench` (CPU ms, best of N, corpus\bench.txt: Showcase, AxStudio.App, vscode, ImagePut, AHK2ColorfulGUI,
WebViewToo) and `bench --phases` (engine `Prof` timers, off unless the harness turns them on). The machine is
busy, so wall clock was useless; every verdict below is CPU time, A/B interleaved in the same minutes.
1. `AstNode.ChildNodes` copy → cached snapshot: **no measurable gain** (13.3 s vs 13.4 s A/B). Reverted.
2. Emitter: expression nodes (the bulk of a tree) now append into one StringBuilder instead of returning strings
   for the parent to concatenate. Emit **~15 % faster** (186 vs 216–233 ms, old/new engines A/B); flows overall
   unchanged within noise (parse and analysis dominate). Output byte-identical: roundtrip, minify-aggressive and
   beautify of bench.txt + all cases, 78 files, compared with the previous build.
3. Repeated whole-tree walks: the **big win**. node-diagram per-line file lookup, member-suffix index and edge
   dictionaries instead of linear scans (AxStudio.App ~9 s → ~1.1 s); statement-head + leading-group scans merged
   into one walk; GetMaxLine memoised per emit (emit −15 %); minify fold + flatten in one pass.
4. Regexes built in hot paths: hoisted to static fields (number parsing in constant folding, identifier-string
   scan in rename, deref-name scans in the shaker, A_LineFile check). .NET already caches static Regex calls, so
   **no measurable gain** — kept because it's free.
Removed afterwards: the temporary emit A/B switch and the Thread.Suspend sampling profiler (it deadlocked).
Gate: cases 20/20, VerifyTest passes, core sweep 977 tested → 0 hard, 3 soft (unchanged), newly failing 0.

## 2026-09-11 — Workbench rebuilt around the real workflow
Audit findings that made it "barely usable": the editor was a RichTextBox recoloured by a timer (full-text copies
on every paint); every parse replaced the editor text with inlined includes (Save then wrote the inlined code;
there was no Save at all); flows ran on the UI thread; the parse result replaced the form's engine (missing-plugin
prompt lost); warnings at 0:0; flow tabs restored from the layout were dead; ~40 swallowed exceptions.
New shell (src\ui): FastColoredTextBox editors with an AHK v2 line-state highlighter (AhkSyntax.cs), folding,
find/replace/go-to, squiggles + line tint + gutter dot; file tabs that keep encoding/line endings, dirty prompts,
reload on external change, session + layout restore; live background parse on one engine worker (parses and flows
serialised: PipelineLogger is static); Problems (real file/line: "at line X:Y" parsed, include warnings pinned to
the #Include), Outline, AST (code preview per node, caret sync, search, hide comments); flows from built-in
presets (BuiltinFlows.cs, generated from harness\flows) + user flows, output tab with honest size stats and
Save/Compare/Validate/Run/Open/Re-run; command palette (@symbol, :line, flows); layout-preserving theme switch;
crash.log appends and tells the user. Old partials kept verbatim in ui_v1_backup\ (they had uncommitted edits).
Harness: `shot <file> --tour` (the Workbench screenshots its own screens on the hidden desktop). Bugs found by the
tour: FCTB 16-style limit, shared font disposed by zoom, outline FullPath on detached nodes, BeginInvoke during
teardown. Engine untouched; cases 20/20.
## 2026-09-12 — AxStudio back end: source ranges, text-preserving edits, AstHost.exe, docs-generated built-ins
1. **Ranges.** Tokens carry original-source offsets (the ContinuationJoiner map now also covers section content and
   records each section's `(`/`)`); every parse function stamps the node it returns with the tokens it consumed
   (`AhkParser.Stamp`), a few constructs set theirs explicitly (else/until/finally/case/default keywords, command
   arguments, omitted items, error recovery, Extends, remaps), and `SourceRanges` fills the rest from anchor token +
   children, closes brackets, and treats a joined section as one piece. AstNode: StartOffset/EndOffset +
   RangeStart/End line/col (Line/Column/EndLine untouched — the emitter's layout is unchanged). Warning/Error nodes
   have real ranges. Comments never widen a parent (the parser hands some to nodes they are not in: `(` adopts
   comments of earlier lines, a one-line statement's comment is held back into the next block) — flagged `attached`.
   `harness.ps1 ranges`: every node nested, on token boundaries, line/col = offsets; statements and outermost
   expressions of cleanly parsing files re-parse from their own text to the same subtree; insert/delete round-trips.
   Corpus: 1361 files, 3.23M nodes, 782k re-parsed, 7747 edits: clean. `selftest` runs it (--deep) on the cases.
2. **SourceEdit**: ReplaceNodeText / ReplaceRange / InsertBefore / InsertAfter (statement level, the statement's
   indentation and line break) / DeleteNode (own lines + trailing comment; comma-list items with their comma), each
   a single splice + re-parse reporting new problems.
3. **AstHost.exe** (src\host, GUI subsystem, engine compiled in): parse / outline / errors / edit / flow / nodeAt /
   ping / exit over stdin/stdout JSON lines; per-request thread (256 MB stack) with a timeout; protocol in
   src\host\PROTOCOL.md, nodes in NODES.md. `harness.ps1 host [--install]`: 35 protocol checks (incl. garbage input,
   a 3000-deep expression, a timeout, exit on stdin close). Installed to build\AstHost.exe and studio\bin.
4. **AhkBuiltins** (generated: `harness.ps1 builtins --docs`) from the 2.0.26 help: 365 functions with argument counts,
   136 variables, 66 classes with bases; EvalAnalysis, the minifier's reserved names and the Workbench highlighter use
   it. MismatchedArgs ignores `static __New` (AxInspector "expects 0-0"). Old hand table was wrong for RegExMatch (max
   5, is 4) and SendMessage. AxStudio's 114 files: 12 false reports gone (A_ScreenDPI, MethodError, …), none new.
Also: #Include de-duplicates by full path only (two JSON.ahk were one); AstNode.Clone dropped SourceHeadFollower
(cached include trees lost their source heads). The report that ProcessIncludes mutates cached trees: checked —
AstFileCache hands out clones on both paths; nothing to fix there.
Gate (each step): cases 20/20, VerifyTest ok, core 977 tested → 18303 pass, 20 hard (all harness-error: the deleted
Downloads\AutoHotkeyDocs-2 folder, same as before), 3 soft, newly failing 0.

## 2026-09-12 — AstHost: binary parse output (AXT1)
`parse` with `"format":"bin"`: the tree as fixed 48-byte pre-order records + string/type/flag/file tables
(src\host\TreeBin.cs, layout in PROTOCOL.md), delivered as a file (`"out"` or a temp file removed on exit), named shared
memory (`"to":"shm"`, released with `release`) or inline base64 — the last two never touch disk. Tested AutoHotkey
reader: src\host\AxtReader.ahk (copied next to the exe), run in the sandbox over all three routes. `harness.ps1 host`
compares bin with JSON (cases, depth, all routes; 44 checks); `host --corpus`: 1361 files identical. Showcase.ahk with
includes: 105k nodes, 5.4 MB bin in ~205 ms round trip vs 14 MB JSON in ~555 ms; 60,000 lines: 280k nodes, 12.8 MB in
~860 ms vs 37 MB JSON in ~1.4 s. Parse replies (both forms) now also carry "problems" and "parseMs".
