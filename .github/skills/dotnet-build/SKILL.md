---
name: dotnet-build
description: >
  Runs the .NET project build and interprets compiler errors.
  Use this after any C# code change, when build errors appear, or after completing
  any implementation task. Equivalent to TypeScript's typecheck — catches type
  errors, missing references, and broken contracts at compile time.
---

# .NET Build Skill

## When to Use This Skill

Load this skill when:
- Implementing any C# code change
- A compiler error or warning is reported
- Asked to "build", "compile", "check types", or "verify the project"
- After completing any task in the todo list during implementation

## How to Build

### Standard build (no restore)
```bash
dotnet build --no-restore
```

Use `--no-restore` during active development — packages are already restored.
Use without the flag only if you added a new NuGet package.

### Build a specific project
```bash
dotnet build src/MyProject/MyProject.csproj --no-restore
```

### Build in Release mode (for final verification)
```bash
dotnet build --configuration Release --no-restore
```

### Check which projects exist
```bash
find . -name "*.csproj" | sort
```

Or check the `.sln` file for the full project list.

## Interpreting Results

### Build succeeded, 0 Warning(s), 0 Error(s)
→ Continue to the next task. Mark the current task `[x]` in `plan.md`.

### Errors you introduced
→ Fix them before proceeding. Do not move to the next task with a broken build.
→ If fixing requires a design decision (e.g., an interface signature is fundamentally wrong), stop and flag it explicitly rather than patching with a cast or nullable workaround.

### Pre-existing errors (not introduced by your changes)
→ Do not fix them silently.
→ Add a note in `plan.md` under `## Observations`: `Pre-existing build error in <Project>/<File>.cs: <brief description>`
→ Continue with your task.

### Warnings
Treat warnings as errors unless explicitly told otherwise.
Common warnings that must be fixed:
- CS8600 / CS8601 / CS8602 / CS8618 — nullable reference violations
- CS0168 / CS0219 — unused variables
- CS8625 — null literal assigned to non-nullable type

Do not suppress warnings with `#pragma warning disable` without explicit instruction.

## Nullable Reference Types

This project assumes `<Nullable>enable</Nullable>` in all `.csproj` files.

Rules:
- Never use `!` (null-forgiving operator) to silence a nullable warning you introduced
- Model nullable state correctly: if something can be null, its type must be `T?`
- Prefer `ArgumentNullException.ThrowIfNull()` at method boundaries over nullable propagation
- Use `required` properties on records and classes where the value must always be set

```csharp
// Wrong — silencing the compiler
var name = GetName()!;

// Correct — model the nullability
string? name = GetName();
if (name is null) throw new InvalidOperationException("Name is required.");
```

## Analyzers

If the project uses Roslyn analyzers (e.g., via `Microsoft.CodeAnalysis.NetAnalyzers`
or `SonarAnalyzer.CSharp`), analyzer warnings are treated with the same weight as
compiler warnings. Do not suppress them without explicit instruction.

## Rules

- Never use `object` or `dynamic` as a type escape hatch
- Never use `#pragma warning disable` without explicit instruction
- Never use the null-forgiving operator `!` to suppress a warning you introduced
- Never leave unused `using` directives — they are noise and may indicate a wrong dependency
- Run build after every logical chunk of changes, not just at the end of the session
