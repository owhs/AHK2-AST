# AHK2 AST Parser, Optimizer, & Minifier

An optimizing compiler engine, parser, and minifier suite for **AutoHotkey v2** written in C#. This library parses AutoHotkey v2 source code into an Abstract Syntax Tree (AST), performs multi-pass tree shaking and code optimization, mangles variable and function names for size reduction, and formats/beautifies the generated code.

The workspace also contains a graphical workbench tool (**AstWorkbench**) to load, visualize, edit, optimize, and inspect AHK2 syntax structures side-by-side.

---

## Project Directory Structure

```
AHK2 AST/
├── build/                 # Build output directory (DLLs, EXEs)
├── src/                   # C# Source code
│   ├── ast/               # Core AST nodes, Lexer, Parser, Emitter, and AST engine
│   ├── jit/               # Reserved for future just-in-time code generator components
│   ├── plugins/           # AST transformations: TreeShaker, Optimise, Minify, Beautify
│   ├── ui/                # Windows Forms components for AstWorkbench UI
│   └── AstWorkbench.cs    # Entry point of the GUI workbench program
├── tests/                 # Automated verification test suite
│   ├── VerifyTest.cs      # Core test runner executing 37 verification scenarios
│   ├── README.md          # Comprehensive breakdown of all verification tests
│   └── test_*.ahk         # Dependency scripts for diamond-include parsing tests
├── build.ps1              # Main compilation script for AST Engine and Workbench UI
└── run_tests.ps1          # Compilation and execution helper script for the test suite
```

---

## How to Build the Project

You can compile the entire workspace (both the `AstEngine.dll` library and the `AstWorkbench.exe` GUI program) from the repository root:

```powershell
.\build.ps1 -Force
```

*Note: This script automatically detects `csc.exe` in the .NET Framework 4.x folder, gathers all engine source files, checks content hashes, and compiles output files under the `build\` folder.*

---

## Running Verification Tests

We maintain an automated verification suite verifying **37 test cases** including expression pruning, dynamic call parsing, constant folding, and collision-free minifier renaming.

To compile and execute the test suite in one step:

```powershell
.\run_tests.ps1
```

If the execution is successful, the terminal will print test logs and finish with:
```
SUCCESS: All tests passed!
```

> [!NOTE]
> If `AstWorkbench.exe` is currently running, `AstEngine.dll` might be locked. The `run_tests.ps1` script detects this lock and automatically falls back to compiling verification tests against the existing `build\AstEngine.dll` binary without crashing.

---

## Adding New Tests

To expand the test coverage:
1. Open [tests/VerifyTest.cs](file:///c:/projects/c%23/AHK2%20AST/tests/VerifyTest.cs).
2. Append a new numbered verification block to the `VerifyTest.Main` method.
3. If helper scripts are needed, save them in the `tests/` directory with a prefix of `test_` and update the test case to query them relative to the execution folder path.
4. Run `.\run_tests.ps1` to rebuild and execute the suite.
