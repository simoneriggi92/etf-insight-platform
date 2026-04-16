---
description: "Research phase. Deep-read a target area of the codebase and produce research.md. Invoke with: /research <target-folder-or-feature>"
agent: Architect
tools:
  - search/codebase
  - edit/editFiles
  - read
  - search
---

# Research Phase

You are entering research mode. Your only deliverable is a written document.
You will not propose solutions, write plans, or suggest any code changes.

## Your Task

Read the target provided by the user deeply and thoroughly.
When done, write everything you learned into `research.md` at the project root.

## How to Read

Do not skim. For every file in scope:
- Read the full implementation, not just signatures
- Trace every call chain end to end
- Identify what external systems, libraries, queues, caches, or databases are involved
- Look for existing utilities, patterns, or abstractions that already solve similar problems
- Note every non-obvious behavior, edge case, or implicit assumption

The words "deeply" and "in detail" are not decorative. Surface-level reading is not acceptable.

## research.md Structure

Produce the document with exactly this structure:

```
# Research: [Target Name]

## Overview
What this system does and its role in the broader application.

## Entry Points
How the system is triggered: API routes, events, cron jobs, UI actions.

## Core Data Flow
Step-by-step trace from input to output across the full call chain.

## Key Components
For each significant file or module:
- What it does
- What it depends on
- What depends on it
- Non-obvious behavior or edge cases

## External Dependencies
Queues, caches, databases, ORMs, third-party services.
How each is used and what assumptions the code makes about them.

## Existing Patterns & Conventions
Naming, error handling, typing, layering decisions already in place.

## Potential Issues
Bugs, fragile assumptions, inconsistencies, missing validations.
Cite file names and approximate line references where possible.

## Open Questions
Anything unclear that needs clarification before planning begins.
```

## Hard Rules

- Do not propose solutions or fixes. Document only.
- Do not write any code.
- Do not trust function names — read the implementation.
- Do not stop early. Research is complete when every relevant file has been read.
- Write the output to `research.md`. Chat summaries are not a valid deliverable.
