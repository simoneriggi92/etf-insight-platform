---
applyTo: "**"
---

# Coding Standards

These rules apply to every interaction, every file, every suggestion. No exceptions.

## Core Principles

- **Correctness over speed.** Never suggest a solution that works in isolation but breaks the surrounding system. Read the context before writing anything.
- **Fit the codebase.** Before introducing a new pattern, verify whether an equivalent already exists. Duplication is a defect.
- **Simplicity is a constraint, not a default.** Choose the simpler option only when it satisfies all requirements. Never simplify by omitting correctness.
- **Explicit is better than implicit.** Name things for what they do. Avoid magic, side effects without documentation, or behavior that surprises the reader.


## Code Style

- **No unnecessary comments.** If a comment explains *what* the code does, rewrite the code so it does not need explaining. Comments are reserved for *why* — non-obvious decisions, constraints from external systems, intentional trade-offs.
- **No JSDoc unless the function is a public API surface.** Internal utilities do not need documentation blocks.
- **No `TODO` comments left in committed code.** If something is deferred, it belongs in the issue tracker, not the source.
- **No dead code.** Do not leave commented-out blocks, unused imports, or unreachable branches.
- **Functions do one thing.** If a function needs a comment to separate two sections, it should be two functions.

## Architecture & Patterns

- **Read before writing.** Before creating a new utility, service, hook, or module, verify that an equivalent does not already exist.
- **Follow the existing layering.** Respect the established boundaries between layers (e.g., data access, domain logic, presentation). Do not reach across layers directly.
- **Do not reinvent infrastructure.** If the codebase uses a queue, cache, ORM, or retry mechanism, use it. Do not build a parallel one unless explicitly asked and justified.
- **Side effects belong at the edges.** Pure logic should be separated from I/O, mutation, and external calls.
- **Migrations follow the ORM conventions.** Never write raw SQL for schema changes unless the ORM cannot express it. Always use the project's migration tooling.

## Error Handling

- **Errors must be typed.** Do not throw or return untyped `Error` objects where a discriminated result type is feasible.
- **Do not swallow errors silently.** Every `catch` block must either re-throw, log with context, or return a typed error — never an empty block.
- **Fail loudly in development, gracefully in production.** Use assertions for invariants that should never be violated.

## HTTP & APIs

- **Use the correct HTTP semantics.** `POST` creates, `PUT` replaces, `PATCH` updates partially, `DELETE` removes. Do not use `POST` as a catch-all.
- **Validate inputs at the boundary.** Parsing and validation happen at the entry point (controller/handler), not inside domain logic.
- **Do not expose internal identifiers or implementation details in API responses.**

## Testing

- **Tests describe behavior, not implementation.** Test what the code does, not how it does it. Avoid testing internal state directly.
- **Each test has one clear failure reason.** A test that can fail for five different reasons is not a test — it is a guess.
- **No logic in tests.** Conditionals and loops in test bodies are a sign the test is doing too much.
- **Test names are full sentences.** `"returns empty array when user has no subscriptions"` is a test name. `"test1"` is not.

## General

- **Do not expand scope without explicit instruction.** If you notice an adjacent issue, flag it — do not fix it silently.
- **Do not change function or method signatures without explicit approval** if those signatures are part of a public or shared interface.
- **Prefer reversible decisions.** When two approaches are equivalent, choose the one that is easier to undo.
