# Copilot Instructions

This file is the entry point for all Copilot customization in this repository.
It is always active. Read it first, then follow the references.

## Always-On Standards

Coding standards, typing rules, architecture constraints, and quality gates are defined in:
[coding-standards](.github/instructions/coding-standards.instructions.md)

These rules apply to every file, every suggestion, every interaction. No exceptions.

## Workflow Phases

This repository follows a structured AI-assisted development workflow.
Each phase has a dedicated prompt file you invoke via `/` in Copilot Chat:

| Phase | Command | Purpose |
|---|---|---|
| Research | `/research` | Deep-read a target area, produce `research.md` |
| Planning | `/plan` | Produce `plan.md` from `research.md` |
| Annotation | `/annotate` | Address inline notes in `plan.md`, do not implement |
| Implementation | `/implement` | Execute `plan.md` completely |

Never skip phases. Never implement before the plan is approved.

## Agents

Use the `Architect` agent for sessions that span multiple phases.
It orchestrates the workflow, enforces phase boundaries, and maintains the plan as the source of truth.

## Skills

The following skills are available and will be loaded automatically when relevant:

- `dotnet-build` — runs the project typecheck and interprets errors
- `efcore-migrations` — handles schema changes using Entity Framework Core migrations
- `dotnet-tests` — runs tests and reports results

Do not write raw SQL for schema changes. Do not skip typecheck.
