# AHK2 AST test harness

A corpus-driven, sandboxed test loop for the AHK2 AST engine. It takes every AutoHotkey v2 script it can
find on this machine, pushes each one through every workflow the engine offers (round-trip, beautify,
optimise, tree-shake, minify, trace, …) and asks **AutoHotkey itself** whether the result is still a valid
program that means the same thing.

Nothing it runs can pop up on your screen, steal focus, touch your clipboard or kill your own scripts.

```powershell
.\harness\harness.ps1 selftest          # prove the sandbox works (16 checks, ~5s)
.\harness\harness.ps1 cases             # regression cases (GitHub issues + runtime semantics)
.\harness\harness.ps1 scan              # discover + dedupe scripts -> harness\state\corpus.json
.\harness\harness.ps1 run               # full sweep of the core tier, report in harness\out\<run>\summary.md
.\harness\harness.ps1 run --failing     # re-test only what failed last time (fast inner loop)
.\harness\harness.ps1 check <file>      # one script through every flow, verbose
.\harness\harness.ps1 reduce <file> --flow minify     # shrink a failure to a minimal repro
.\harness\harness.ps1 ast <file> --flow treeshake-safe  # print what one flow makes of a file
.\harness\harness.ps1 shot <file>       # screenshot AstWorkbench (built from src) invisibly
.\harness\harness.ps1 shot <file> --tour  # the Workbench walks its main screens itself: one PNG per screen
.\harness\harness.ps1 bench [--phases]  # engine speed on corpus\bench.txt (CPU ms, best of N; --phases = breakdown)
```

`harness.ps1` rebuilds what it needs from the working tree on every call (hash-checked, ~2s when something
changed). It compiles its **own** `harness\bin\AstEngine.dll`, so it never touches `build\` and doesn't care
whether AstWorkbench is running and locking the real DLL.

---

## How a script is judged

```
original.ahk ──► AutoHotkey /Validate ──► baseline  (rejected? → skipped: v1 script, broken lib, …)
     │
     ├─► engine Parse ─────────────────► any Error node on code AHK accepts = parser false positive
     │
     └─► for each flow × mode:
           engine ExecuteFlow ──► output.ahk ──► AutoHotkey /Validate ──► must still load
                  │                                     └─► warnings diffed against baseline
                  ├─ crash / hang / exception / plugin error / empty output
                  ├─ idempotence: flow(flow(x)) == flow(x)          (formatting flows)
                  ├─ AST equivalence: parse(emit(parse(x))) ≡ parse(x)   (round-trip)
                  ├─ token fidelity: lex(emit(parse(x))) ≡ lex(x) minus comments/layout/case   (round-trip)
                  ├─ hotkey oracle: remaps, hotkeys, hotstrings unchanged
                  └─ runtime (cases / allow-listed only): run both, compare dialogs + stdout + exit
```

The key idea: **the original script is the oracle.** If AutoHotkey accepts the original but rejects the
engine's output, that's an engine bug, no human judgement needed. Scripts AutoHotkey itself rejects
(v1 code, fragments, libraries that only work when included) are skipped rather than counted, so
"failing lib files" never pollute the score.

### Issue kinds

| kind | severity | meaning |
|---|---|---|
| `validate-fail` | hard | AHK rejects the flow's output (message + emitted context + first failing step) |
| `parse-false-error` | hard | parser emitted an `Error` node for code AHK accepts |
| `engine-crash` / `engine-hang` / `engine-exception` | hard | stack overflow, infinite loop (timeout), or exception out of the API |
| `plugin-error` | hard | a pipeline step threw (the engine swallows it and emits nothing) |
| `hotkey-diff` | hard | a remap target / hotstring text / hotkey label was lost or changed |
| `runtime-diff` | hard | original and output behave differently when run (dialogs, stdout, exit, runtime errors) |
| `empty-output` | hard (soft if the flow tree-shakes) | output is empty |
| `new-warnings` | soft | output triggers AHK warnings the original didn't (VarUnset, Unreachable) |
| `not-idempotent` | soft | running the flow twice changes the code again |
| `ast-diff` | soft | re-parsing the emitted code gives a different tree |
| `token-diff` | soft | the 1:1 emit doesn't lex to the original's tokens (ignoring comments, line breaks, letter case, trailing commas) — anything the parser dropped or misread |
| `not-applicable` | info | the flow doesn't apply to this script (`Compatibility: entry` on a library file) |
| `parse-unknown` | soft | parser produced an `Unknown` node |

Every hard failure writes `harness\out\<run>\fail\<id>_<flow>-<mode>\` with `original`, `output`,
`issue.txt` and a one-line repro command.

---

## The sandbox

AutoHotkey reports load errors on stderr with `/ErrorStdOut`, but `#Warn` warnings, runtime errors and
every `MsgBox` are dialogs. The harness never lets those near the user:

- **Hidden desktop.** Every process starts on a private desktop (`CreateDesktop` + `STARTUPINFO.lpDesktop`).
  Its windows exist and paint, but are never shown on the screen, can't take focus and can't receive
  or inject real keyboard input.
- **Watcher thread.** A thread attached to that desktop polls for new windows, reads them
  (`WM_GETTEXT` on every control), classifies them (AHK error / AHK warning / MsgBox / GUI), screenshots
  them (`PrintWindow`) and acts: warnings get **Continue**, MsgBoxes get their default button, error
  dialogs kill the job.
- **Job objects.** Every launch runs in its own kill-on-close job with a 2 GB memory cap and UI limits
  (no clipboard read/write, no `SystemParametersInfo`, no display changes, no logoff, no desktop
  switching). Timeouts and cleanup use `TerminateJobObject` on *that job only*. The harness never
  kills anything by name, so your own running scripts are safe (the selftest checks this).
- **Relocation.** Outputs are validated from a temp folder. `Ahk.Prepare` rewrites `#Include`/`#DllLoad`
  (relative, `%A_ScriptDir%`, `<Lib>`, directory includes) to absolute paths so a copy loads exactly as
  the original would, and forces `#Warn VarUnset/Unreachable, StdOut` so warnings arrive as text.
- **Worker processes.** The engine runs in child `AstHarness.exe worker` processes with a JSON-lines
  protocol. A stack overflow or infinite loop in the parser kills one worker, gets recorded as
  `engine-crash`/`engine-hang`, and a fresh worker takes over.

`/Validate` loads a script without running any of it, so the whole corpus is tested without executing
anyone's code. Actual execution only happens for `harness\cases\*.ahk` marked `; @run` and for scripts
you list in `harness\corpus\runnable.txt` (used with `run --exec`).

---

## Corpus

`harness\corpus\roots.txt` lists scan roots by tier, `exclude.txt` filters paths. `scan` dedupes by content
hash (the same lib copied into ten projects is tested once), never follows junctions, and classifies
each file as `entry` or `lib` (inside a `Lib` folder, or `#Include`d by another corpus file).

| tier | what | use |
|---|---|---|
| `core` | your projects, apps, downloads, AutoHotkey's own UX scripts | default loop target |
| `converter` | AHK-v2-script-converter's expected `.ah2` outputs: hundreds of small v2 edge cases | second tier |
| `history` | VS Code / Antigravity local-history snapshots and `~backup` | occasional broad sweeps |

## Flows

`harness\flows\*.json` are ordinary engine flow files (the same format the Pipeline Builder saves) plus a
`Harness` block:

```json
"Harness": { "Modes": ["local", "inline"], "Idempotent": true, "AstEquiv": true, "Validate": true,
             "Compatibility": "all", "Renames": false, "Risky": false }
```

- `Modes`: `inline` follows and inlines `#Include`s (the Workbench default); `local` keeps them.
- `Compatibility` — what the flow promises, so options that can't work for every script are still tested honestly:
  - `all` (default): every failure is an engine bug.
  - `entry`: not applicable to library files (aggressive tree-shaking a file with no entry point removes
    everything by design); those items are reported as `not-applicable`.
  - `best-effort`: a knowingly incompatible option (Extreme minify). Failures are still reported, with a
    `[best-effort]` prefix, but count as soft, not hard. Combine: `"entry,best-effort"`.
- `Renames`: the flow renames identifiers, so new-warning detection compares warning kinds, not names.
- `Risky` flows only run with `--risky` or when named in `--flows`.
- Minify levels: `08-minify-safe` (Safe, `all`), `09-minify-aggressive` (Aggressive, `all`),
  `10-minify-extreme` (Extreme, `best-effort`) — see `MinifyLevel` in the engine for what each promises.
- `12-compress.json` is a copy of your `build\Flows\optimise.json` (aggressive shake + property renaming:
  `entry,best-effort`). Drop more flows in and they're picked up.

## Cases

`harness\cases\*.ahk` are small, self-contained, safe scripts. Header annotations:

```ahk
; @issue 4            links the case to a GitHub issue
; @run                also execute original + every output and compare behaviour
; @expect msgbox: Helloworld     the ORIGINAL's transcript must contain this (keeps the case honest)
; @flows minify, compress        restrict to some flows (default: all)
```

Transcripts are line-based (`msgbox: …`, `out| …` for stdout lines, `runtime error: …`, `window: …`,
`exit N` / `still running`) and runtime diffs are printed as `- expected / + got`.

## Caching and speed

Baselines are cached per (content, include files + mtimes, AHK version, harness version); flow results per
(content, includes, flow, engine hash, harness hash). Change the engine and everything is re-checked;
change nothing and a re-run is nearly free. `--no-cache` forces everything. A cold full core run takes a
few minutes on 8 workers.

## Layout

```
harness\
  harness.ps1          entry point: build + dispatch
  src\                 C# 5 / .NET Framework 4 (same csc as build.ahk)
    Sandbox.cs         hidden desktop, job objects, watcher, screenshots
    Ahk.cs             /Validate + exec wrappers, message parsing, include relocation
    Worker.cs          engine worker process + crash-isolated pool
    Runner.cs          run / check / cases, all the oracles
    HotOracle.cs       engine-independent hotkey / remap / hotstring comparison
    Reducer.cs         ddmin reducer + Workbench screenshots
    Corpus.cs Report.cs SelfTest.cs Util.cs Native.cs Program.cs
  flows\  cases\  corpus\           checked in
  bin\  state\  out\                generated (git-ignored)
```
