---
description: "Annotation cycle. Address inline notes added to plan.md and update the document. Invoke with: /annotate"
agent: Architect
tools:
  - read
  - edit/editFiles
  - search/codebase
---

# Annotation Cycle

You are entering annotation mode.
The reviewer has added inline notes directly inside `plan.md`.
Your job is to read every note, address it, update the document, and remove the note once resolved.

## How to Process Notes

Notes may appear as HTML comments, blockquotes, or bold inline callouts. Examples:
- `<!-- NOTE: use base64 encoding, not plain JSON -->`
- `> NOTE: this should be a PATCH, not a PUT`
- `**NOTE:** remove this section entirely`

For each note:
1. Understand what is being asked — correction, rejection, domain constraint, or scope cut.
2. Apply the change to the relevant section of the document.
3. Remove the note marker once the change has been made.
4. Check if the change has downstream effects on other sections or the todo list — update those too.

## Note Types and How to Handle Each

**Two-word corrections** (`"not optional"`, `"wrong method"`)
→ Apply precisely where the note appears. No interpretation needed.

**Approach rejections** (`"remove this section, we don't need caching here"`)
→ Remove the section cleanly. Update any section that depended on it. Remove related todo items.

**Domain knowledge** (`"use drizzle:generate for migrations, not raw SQL"`)
→ Update the affected section. Propagate the constraint to the todo list and migration strategy.

**Structural redirections** (`"this is wrong, restructure the schema section entirely"`)
→ Rethink the affected section from scratch. Do not patch the wording — redesign the section.

**Scope cuts** (`"remove the download feature from the plan"`)
→ Remove cleanly everywhere: the implementation section, the files list, and the todo list.

## Hard Rules

- **Do not implement anything.** You are updating a document, not writing code.
- Do not resolve a note by rewriting it to sound like it agrees — either apply the change or flag a conflict.
- Do not add scope while resolving notes. Address exactly what is asked.
- Remove notes after resolving them. Do not leave resolved markers in the document.
- If a note is contradictory or unclear, flag it explicitly rather than guessing.

## When You Are Done

After processing all notes, confirm:
- All note markers have been removed
- The todo list reflects the updated plan
- No section references a removed or changed approach without being updated itself

End your response with: "All notes addressed. Ready for review or next annotation round."
Do not implement. Wait for explicit approval before any code is written.
