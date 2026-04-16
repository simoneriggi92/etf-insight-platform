---
description: "Implementation phase. Execute plan.md completely. Invoke with: /implement"
agent: Architect
tools:
  - search
  - read
  - edit
  - search
  - todo
  - agent
  - browser
  - vscode.mermaid-chat-features/renderMermaidDiagram
  - vscode
  - web
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

If NO flags (`--auto`, `--auto-phase`, `--chat`, `--chat-phase`, `--batch`, or `--chat-batch`) are present:

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

### 3. Auto-Phase Mode: Execute Entire Phase (Triggered by `/implement --auto-phase`)

If the `--auto-phase` flag IS present:

- Identify the current active **Phase** (or logical group of tasks) in `plan.md`.
- **CRITICAL:** Complete **ALL** incomplete tasks within that specific Phase in a single, continuous response using your file editing tools.
- Do not stop to ask for input between tasks within this Phase.
- **DO NOT** update `plan.md` one by one. You MUST **batch** all `[x]` updates for this Phase and apply them to `plan.md` at the very end of the Phase.
- **STOP** execution when the Phase is complete and wait for my next command.

### 4. Batch Mode: Execute N Tasks (Triggered by `/implement --batch <N>`)

If the `--batch <N>` flag IS present (e.g., `/implement --batch 3`):

- **CRITICAL:** Implement exactly **<N>** consecutive incomplete tasks from `plan.md` using your file editing tools in a single, continuous response.
- Do not stop to ask for input between these <N> tasks.
- Fix any type errors you introduced before moving to the next task in the batch.
- **DO NOT** update `plan.md` one by one. You MUST **batch** all `[x]` updates for these <N> tasks and apply them to `plan.md` at the very end of this batch.
- **STOP** execution when the <N> tasks are complete and wait for my next command.

### 5. Chat Mode: Draft in Chat (Triggered by `/implement --chat`)

If the `--chat` flag IS present:

- **CRITICAL:** DO NOT use tools to edit or modify project files directly. Output the code for **ONE** task only directly here in the chat response.
- Provide clear instructions on where this code belongs (e.g., file paths and line numbers or methods to replace).
- Provide explanations, mermaid diagrams, or any other context necessary to understand the code and its placement.
- Do not attempt to update `plan.md` using file tools.
- **STOP** execution and wait for me to review, apply the code manually, and issue my next `/implement --chat` command to proceed to the next task.

### 6. Chat-Phase Mode: Draft Entire Phase in Chat (Triggered by `/implement --chat-phase`)

If the `--chat-phase` flag IS present:

- **CRITICAL:** DO NOT use tools to edit or modify project files directly.
- Identify the current active **Phase** (or logical group of tasks) in `plan.md`.
- Output the code for **ALL** incomplete tasks within that specific Phase in a single chat response.
- Group the code clearly by task and provide exact file paths for each snippet.
- Provide explanations, mermaid diagrams, or any other context necessary to understand how the tasks in this Phase interact with each other.
- Do not attempt to update `plan.md` using file tools.
- **STOP** execution when the Phase is complete. Wait for me to review, apply the code, and manually update `plan.md`.

### 7. Chat-Batch Mode: Draft N Tasks in Chat (Triggered by `/implement --chat-batch <N>`)

If the `--chat-batch <N>` flag IS present:

- **CRITICAL:** DO NOT use tools to edit or modify project files directly.
- Output the code for exactly **<N>** consecutive incomplete tasks directly here in the chat response.
- **ANTI-LAZINESS OVERRIDE:** You MUST provide the FULL, complete ```code block for EVERY SINGLE TASK. Do not summarize, do not skip, and do not omit any code. Do not use placeholders like `// implementation here`. I need the exact code for all <N> tasks.
- Group the code clearly by task, provide exact file paths, explanations, and mermaid diagrams if necessary.
- Do not attempt to update `plan.md` using file tools.
- **STOP** execution when the <N> tasks are complete. Wait for me to review, apply the code, and manually update `plan.md`.

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
