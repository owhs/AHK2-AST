# AHK2 AST Verification Tests

This folder contains the automated verification test suite for AHK2 AST. The suite validates parser behaviors, emitter output, code formatting (Beautifier), minification transformations, AST optimizations, and scope-aware local variable renaming.

## Test Cases Summary

The suite runs **37 validation tests**:
1. **Multiline string formatting**: Validates Continuation section brackets/parentheses formatting.
2. **Switch/Case multiple values**: Verifies multi-value case expressions format (e.g. `case 'A', 'B':`).
3. **Logic Shaking default preservation**: Checks class method / nested function preservation when shaking options are disabled.
4. **Nested function logic shaking**: Checks unused nested functions within class methods are pruned while active ones are preserved.
5. **Diamond include resolution**: Resolves diamond inclusions correctly without circular reference warnings or duplicate code.
6. **Expression branch pruning**: Checks conditional statements context-based optimization (e.g., `x := 5` evaluates to `5`).
7. **HotIf call parsing**: Checks dynamic `HotIf (*) => ...` parses correctly as a function Call, not definition.
8. **Chained declarations**: Verifies multiple declarations output correctly (e.g. `global a, b := 1`).
9. **Safe Emitter robustness**: Ensures the emitter handles corrupted/empty expression nodes gracefully without crashing.
10. **Property accessors**: Confirms property `get`/`set` short-body and fat-arrow block generation.
11. **Stripping Optional include paths**: Ensures optional include directories evaluate smoothly.
12. **Static line resolution**: Checks A_LineFile resolution dynamically in parses.
13. **Space concatenation**: Validates space-based text concatenation formatting.
14. **Local static preservation**: Confirms that local `static` variables inside methods are not tree-shaken.
15. **Meta-methods preservation**: Checks `__Call` and other class meta-methods are protected from shaking.
16. **String-based method reference**: Confirms methods referenced dynamically as strings (e.g., in `ObjBindMethod`) are not shaken out.
17. **Expression constant folding**: Folds basic expressions at compile time.
18. **Conditional compilation**: Processes preprocessor flags (e.g., `;AST-IF`) correctly.
19. **Beautify block spacing**: Emits appropriate custom vertical gaps between classes/functions.
20. **Minifier escape & whitespace**: Compresses spacing, minifies block indent, and inline/folds.
21. **Minifier concat spacing**: Ensures explicit spaces between adjacent string literals are preserved.
22. **Class field shaking**: Prunes unused static/instance fields.
23. **Class field minification**: Preserves static field declarations.
24. **Bracket Indentation**: Validates OTB bracket style formatting.
25. **Math constant folding**: Evaluates dynamic math operations (e.g., `60 * 1000 * 1000`).
26. **Directive de-duplication**: Prunes duplicate `#Requires` and `#SingleInstance` statements.
27. **If-to-Switch compilation**: Compiles if-else chains into optimized `switch` blocks.
28. **Short-circuit folding & dead branches**: Prunes branches like `true || x` and eliminates dead code.
29. **Local variable shaking**: Prunes unused variables/assignments within local scopes.
30. **Minifier renaming & mangling**: Renames user classes, methods, globals, and parameters safely.
31. **Diagnostics logger comment blocks**: Injects diagnostic flow comments in emitted code.
32. **Aggressive tree-shaking & minification**: Runs aggressive shaking followed by minifier.
33. **Comment stripping**: Prunes comment lines.
34. **Try/Catch empty blocks**: Generates valid `{}` fallback brackets for try/catch blocks.
35. **Advanced local renaming**: Renames loop index variables, compound assignments, and increment/decrement variables safely.
36. **Dynamic property renaming**: Renames percent-wrapped dynamic properties safely (e.g., `obj.%col%`) while preserving system keywords (e.g., `Close`).
37. **Assume-Global & Nested Class isolation**: Isolates variables in nested classes from the outer scope, and prevents global variables in assume-global methods from being renamed as local variables.

## How to Compile & Run

### 1. Build AST Engine
Compile `AstEngine.dll` from the workspace root:
```powershell
.\build.ps1 -Force
```

### 2. Compile Verification Tests
From the workspace root, run the C# compiler to compile `VerifyTest.cs`:
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /out:build\VerifyTest.exe /reference:build\AstEngine.dll tests\VerifyTest.cs
```

### 3. Copy Test Dependencies
Copy helper `.ahk` scripts to the build output folder:
```powershell
Copy-Item tests\test_*.ahk build\
```

### 4. Run Tests
Execute `VerifyTest.exe`:
```powershell
& build\VerifyTest.exe
```

A clean run should output the minification tests and end with:
```
SUCCESS: All tests passed!
```
