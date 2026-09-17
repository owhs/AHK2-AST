# AGENTS.md: running the fix loop

You are working on a hand-written AutoHotkey v2 parser/emitter/optimiser in C# (`src\`). This folder is
its test harness. The goal of the loop: **zero hard failures across the corpus for every non-risky flow,
with no regressions**, fixing the engine one root cause at a time. Read `README.md` once for how the
harness works; this file is the protocol.

## Ground rules

- **Never modify, move or "fix" corpus scripts.** They are the user's files and the ground truth.
- **Never kill processes by name** (`taskkill /im AutoHotkey*`, `Stop-Process -Name` …). The user runs
  their own AHK scripts. The harness only ever terminates its own job objects; if something is stuck,
  kill the `AstHarness.exe` process tree you started, nothing else.
- **Never execute corpus scripts** outside the harness, and never add scripts to
  `corpus\runnable.txt` yourself. Only the user decides what may run. `/Validate` (the default) runs no
  script code; `cases\*.ahk` are safe to run.
- **No visible windows.** Everything goes through the sandbox desktop. Don't launch AutoHotkey or
  AstWorkbench directly. Use `validate`, `exec`, `check`, `shot`.
- **Don't weaken an oracle to make a failure go away.** If the harness itself is wrong (a false
  positive), fix the harness, prove it with a selftest check or a case, and say so in the log.
- Don't touch `build\` (the harness builds its own engine in `harness\bin`). Commit only when asked.
- Engine and harness compile with the .NET Framework 4 `csc` (**C# 5**): no `$"..."`, `?.`, `=>`
  bodied properties, `nameof`, or auto-property initialisers.

## One iteration

```powershell
# 0. health (only needed after harness changes)
.\harness\harness.ps1 selftest

# 1. where are we?
.\harness\harness.ps1 cases                    # fast: GitHub issue cases + runtime semantics
.\harness\harness.ps1 run                      # core tier; cached, so only changed work is redone
.\harness\harness.ps1 report                   # re-print latest summary.md
```

2. **Pick one target.** In `summary.md`: anything under *Changes since … / newly failing* first
   (regressions), then the hard signature affecting the **most articles**. Prefer `parse-false-error` and
   `roundtrip` failures over plugin failures: parser/emitter bugs cascade into every flow.
3. **Reproduce.** Open the artifact's `issue.txt`, then
   `.\harness\harness.ps1 check "<article>" --flows <flow>`.
4. **Reduce.** `.\harness\harness.ps1 reduce "<article>" --flow <flow>` gives a minimal script in
   `harness\out\reduced\`. Use `--flow parse` for parser false errors and `--signature <text>` to pin a
   specific issue. For `runtime-diff` in a case, add `--exec --match "<a line from the diff>"` so the
   reduction keeps *that* behavioural difference (`--exec` is refused for anything outside `cases\` /
   `runnable.txt`).
5. **Lock it in.** Make the repro self-contained and save it as `harness\cases\<topic>.ahk` with
   `; @flows …` and, when behaviour matters, `; @run` plus `; @expect …` lines taken from the *original's*
   real output (`.\harness\harness.ps1 exec <case>`). Confirm the case fails before fixing.
6. **Fix the root cause** in `src\`:

   | symptom | usually lives in |
   |---|---|
   | `parse-false-error`, `ast-diff`, `(parse)` crash/hang | `src\ast\AhkLexer.cs`, `src\ast\AhkParser.cs` |
   | `roundtrip` validate-fail / not-idempotent | `src\ast\AstEmitter.cs` (or a misparse upstream) |
   | fails only in one flow | that plugin in `src\plugins\` (`first failing step:` names it for multi-step flows) |
   | `hotkey-diff` | hotkey/hotstring parsing, or the plugin rewriting them |
   | `runtime-diff` | a semantic transform: constant folding, inlining, renaming, tree-shaking |

   Keep the change minimal and in the style of the surrounding code.
7. **Verify, no regressions:**
   ```powershell
   .\harness\harness.ps1 cases            # new case passes, all others still pass
   .\harness\harness.ps1 verify           # legacy tests\VerifyTest.cs suite (37 tests) still passes
   .\harness\harness.ps1 run              # engine hash changed => everything re-checked
   ```
   `summary.md` must show **newly failing: 0**. If not, fix or revert before moving on.
8. **Log it.** Append one entry to `harness\LOOP-LOG.md`: date, signature, root cause (one line),
   files touched, case added, hard-fail count before → after.

Then start the next iteration. When the core tier is clean: `run --tier converter`, then
`run --tier history`, then `run --risky`.

## Triage notes

- A hard failure in `roundtrip` explains most failures of other flows on the same article. Fix it first
  and re-run before chasing those.
- `validate-fail` details show AHK's message, the emitted lines around it, and, for multi-step flows,
  the first step whose output AHK rejects.
- `runtime-diff` details are a line diff: `-` is what the original did, `+` is what the output did.
  `out|` lines are stdout, `msgbox:` are dialogs, `runtime error:` are AHK error dialogs.
- `new-warnings` with `VarUnset` after renaming or tree-shaking usually means a reference was renamed or
  removed while its definition wasn't (or the reverse). Treat it as a real bug unless the flow is risky.
- Articles skipped as `baseline-fail` are scripts AHK itself rejects (v1 code, include-only fragments).
  They are not engine failures. If a *v2* script shows up there with "#Include file … cannot be opened",
  that's a relocation gap in `Ahk.Prepare`: fix the harness.
- Engine crashes and hangs are isolated per worker; the `Crash` text carries the stderr tail (stack overflow
  shows as `0xC00000FD`).

## Known open issues (GitHub)

| # | case | status |
|---|---|---|
| 1 operator precedence raising | `cases\issue1_operator_precedence.ahk` | **fixed** (parser; minify inliner only inlines literal, list-statement, same-scope assignments) |
| 2 remaps | `cases\issue2_remap.ahk` | **fixed** (Remap node, AHK-verified key rule) |
| 3 hotstring X option | `cases\issue3_hotstring_x.ahk` | **fixed** (minify emitter kept only the trigger and dropped the action) |
| 4 continuation join | `cases\issue4_join*.ahk`, `cases\continuation_sections.ahk` | **fixed** (ContinuationJoiner) |

Update this table as issues close.

## AHK rules worth knowing (verified with /Validate)

- **Statement heads.** A line starting with a bare name or a plain `a.b` chain followed by an operator, comma or
  operand is a function-call statement: `x && f()` is `x(&& f())` (syntax error), `a.b ?? c` likewise, `x "s"` is
  `x("s")`. Only ` ?` / ` ??` may follow a bare name; chains ending in `]` / `)` are fine. A line starting with a
  literal (`0, y := 1`, `[1].Length`) is no statement at all. Emitters parenthesise such heads
  (`src\ast\StatementHead.cs`), so transforms may drop "redundant" parens freely.
- **Constant folding** must follow `src\plugins\AhkConst.cs`: `/` always yields a Float, `1.0` stays a Float, `==`
  is case-sensitive and `=` only folds ASCII, numeric-looking strings may compare numerically (not folded),
  `a || b` / `a && b` return an operand (only a constant *left* side folds outside conditions).
- `case x:` can't be followed by `{` on the same line; a block comment may run to the end of the file;
  `Goto Label` names a label (Goto node); stacked hotkeys (`~*Shift::` above `~*Ctrl::f()`) share one action.
- `until` (after loop/for — not while) and a loop's `else` (loop/for/while … else) must directly follow the loop — they are loop children.
  Inserting a line in between makes AutoHotkey 2.0.19 crash (0xC0000005) instead of reporting an error.
- `Name(...)` followed by a line `{` is a function definition; a bare block after such a call must be flattened.
- `-5 // 2` is `-2` (truncates); `"0.0"` and `"0x0"` are false; `[a, b,]` has 2 items; a nested static function
  may declare `local this`.
- `#HotIf expr` is code: the condition is parsed into the Directive's child (renamed/shaken like any code).
  `#HotIf`, `#InputLevel`, `#HotString`, `#UseHook`, … are positional — never deduplicated.
- A class may hold an instance `__new(p)` *and* a `static __new()` (and same-named static/instance members).
- Names may contain any non-ASCII character; `:::` / `+:::` / `` `;:: `` are hotkeys on `:` / `;`; hotstring
  options may contain blanks (`: :btw::`); `::btw::` + `{` on the *next* line is a function body (on the same
  line `{` is replacement text); `[ , , x]` has omitted items; `if (x)()` calls the condition's value.
- A hotkey line followed by a function definition defines a global function; a function defined inside a
  top-level `if { }` is global; one inside a hotkey's `{ }` body is local to it.
- Statement heads written in the source are kept verbatim (AHK accepts `x = 2 ? a : b`, rejects `x < 2 ? a : b`);
  only heads a transform produced get parenthesised.
- Use the other tiers as an unseen test set: `run --tier converter --flows roundtrip`, then `--tier history`.

## Compatibility levels (not every option has to fit every script)

Some transforms can't be correct for every script — AHK can build names at runtime (`%"Pre_" name%`,
`obj.%prop%`, `Type(x)`). The engine states what each level promises and the harness scores accordingly:

| minify level | promise | harness flow / compatibility |
|---|---|---|
| Safe | behaves identically for every script | `minify-safe` / `all` |
| Aggressive | + globals/functions/classes renamed, one-lining; names reachable at runtime are detected and kept | `minify-aggressive` / `all` |
| Extreme | + properties/methods renamed, risky renames; not every script | `minify-extreme` / `best-effort` |

Aggressive tree-shaking and the user's compress flow need an entry point: `Compatibility: entry` marks library
files `not-applicable` instead of counting "everything was shaken away" as a bug. A `[best-effort]` failure is
worth fixing when the construct is ordinary AHK (a real bug), and is the documented incompatibility when it
depends on runtime name construction.
- `ast <file> --flow <name>` prints what one flow produces — the quickest way to look at a transform's output.

## Useful commands

```powershell
.\harness\harness.ps1 validate <file>             # sandboxed AHK /Validate (warnings as text)
.\harness\harness.ps1 exec <case> --shots         # run a SAFE script, dialogs/windows/stdout + screenshots
.\harness\harness.ps1 run --filter <substr> --flows minify,compress --verbose
.\harness\harness.ps1 run --failing               # only articles that had issues last run
.\harness\harness.ps1 run --no-cache              # ignore cached verdicts
.\harness\harness.ps1 shot <file> --ms 8000       # AstWorkbench screenshot (only when UI changed)
.\harness\harness.ps1 shot <file> --tour           # scripted tour: script, palette, flow output, compare, jump, live error, flow editor, light theme
.\harness\harness.ps1 corpus                      # corpus stats; edit corpus\roots.txt + scan to change it
.\harness\harness.ps1 bench --reps 5              # engine speed (CPU ms, best of N) on corpus\bench.txt
.\harness\harness.ps1 bench --phases              # where the time goes (engine Prof timers, off otherwise)
.\harness\harness.ps1 ranges [--deep]             # source ranges on the corpus: nested, token boundaries, pieces re-parse, edit round-trips
.\harness\harness.ps1 host [--install]            # build AstHost.exe + test its JSON protocol (--install: build\ and AxStudio)
.\harness\harness.ps1 host --corpus               # binary (AXT1) parse = JSON parse, node for node, on the whole corpus
.\harness\harness.ps1 builtins --docs <docs dir>  # regenerate src\ast\AhkBuiltins.cs from the AutoHotkey help files
```

Any parser change must keep `ranges` clean (SourceRanges + the parse functions' `Stamp` calls): the AxStudio host
edits users' files by these ranges. `selftest` runs `ranges --deep` on the cases.

Perf work: the machine is usually busy, so compare CPU time (what `bench` reports), never wall clock, and A/B two
builds interleaved in the same minutes. Check any emitter change byte-for-byte against the previous build's output
(`ast <file> --flow roundtrip|minify-aggressive|beautify` on bench.txt + cases) before and after.

Screenshots are PNGs; view them with your image-reading tool rather than opening windows.
