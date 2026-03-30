---
description: "Implementation phase. Execute plan.md completely. Invoke with: /implement"
agent: Architect
tools:
  - search/codebase
  - read
  - edit/editFiles
  - vscode/runCommand
  - search
  - todo
---

# Implementation Phase

You are entering implementation mode.
The plan has been reviewed and approved. Execute it completely, without stopping.

## Prerequisites

1. Read `plan.md` in full before writing a single line of code.
2. Confirm the todo list exists and all items are unchecked.
3. If `plan.md` is missing or the todo list is absent, stop and ask. Do not implement from memory.

## Execution Modes

Implement every task in the `plan.md` todo list in strict order, without skipping.
Your execution behavior depends entirely on how this prompt was invoked:

### 1. Default Mode: Step-by-Step (Triggered by `/implement`)

If NO flags (`--auto` or `--chat`) are present:

- Implement **ONE** task only using your file editing tools.
- Immediately mark it as `[x]` in `plan.md` (do not batch updates).
- Fix any type errors you introduced during this task.
- **STOP** execution and wait for my next `/implement` command to proceed. Do not write code for the next task.

### 2. Auto Mode: Continuous (Triggered by `/implement --auto`)

If the `--auto` flag IS present:

- **CRITICAL:** You must complete **ALL** tasks in a single, continuous response using file editing tools. Do not yield or stop to ask for my input between tasks.
- After completing a task, immediately proceed to write the code for the next task.
- Fix any type errors you introduced before moving to the next task.
- **DO NOT** update `plan.md` one by one if it breaks your execution flow. You MUST **batch** all the `[x]` updates and apply them to `plan.md` at the very end of the implementation phase.

### 3. Chat Mode: Draft in Chat (Triggered by `/implement --chat`)

If the `--chat` flag IS present:

- **CRITICAL:** DO NOT use tools to edit or modify project files directly. Output the code for **ONE** task only directly here in the chat response.
- Provide clear instructions on where this code belongs (e.g., file paths and line numbers or methods to replace).
- Provide explanations, mermaid diagrams, or any other context necessary to understand the code and its placement.
- Do not attempt to update `plan.md` using file tools.
- **STOP** execution and wait for me to review, apply the code manually, and issue my next `/implement --chat` command to proceed to the next task.

## Code Quality

Follow all rules in `.github/instructions/dotnet-coding-standards.instructions.md`:

- No `any` or `unknown` as escape hatches
- No unnecessary comments or JSDoc on internal functions
- No dead code, unused imports, or TODOs
- No raw SQL for schema changes — use the migration tooling (see `efcore-migrations` skill)
- Do not modify function signatures that are not listed in the plan as targets

## Scope

Implement exactly what is in the plan. Nothing more.

If you notice an adjacent issue during implementation:

- Do not fix it silently
- Add a note at the bottom of `plan.md` under `## Observations`
- Continue with the planned task

If you encounter a task that is genuinely ambiguous — underspecified, not just unfamiliar:

- Stop at that specific task
- Flag the ambiguity precisely: what is unclear and what decision is needed
- Do not make an arbitrary choice and continue

## Definition of Done

Implementation is complete when:

- Every item in the todo list is marked `[x]`
- Typecheck passes with zero new errors
- No dead code, unused imports, or unplanned scope changes were introduced
- Every file listed in "Files to Modify" and "Files to Create" has been addressed
- If any deviation from the plan was necessary, it is documented under `## Deviations` in `plan.md`
