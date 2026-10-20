---
description: Full implementation workflow for a Quadra feature (4 agents in sequence)
argument-hint: <SCOPE feature ID> <optional description>
---

# Command: /feature

Use this command to implement a Quadra MVP feature from scratch. It orchestrates the 4 subagents in sequence, with human checkpoints.

## Arguments received

$ARGUMENTS

## Prerequisites

Before proceeding, confirm these exist:
- `CLAUDE.md` at the root
- `docs/SCOPE.md`
- `docs/ARCHITECTURE.md`
- `docs/PRODUCT.md`

If any are missing, stop and warn.

## Execution sequence

Run each step below in order. **Don't skip steps. Don't run them in parallel.**

### Step 1 — Spec

Delegate to the `spec-writer` subagent:

> Create the technical spec for feature `$ARGUMENTS`. Strictly follow the template in your system prompt. Save it in `docs/specs/`.

When spec-writer finishes:
- Show the path of the created file
- Show the 3-line summary
- **STOP and ask the human**: "Spec is ready at [path]. Review and confirm with 'ok spec' to pass to guardian, or describe adjustments."

⚠️ Wait for the human response. Do not proceed automatically.

### Step 2 — Scope audit

Once the human confirms, delegate to the `scope-guardian` subagent:

> Audit the spec at `docs/specs/<file>`. Apply the full checklist from your system prompt.

If the guardian **rejects**:
- Show the full output to the human
- Return to Step 1 with the feedback
- Don't try to "fix it" yourself

If the guardian **approves**:
- Confirm approval to the human
- Ask: "Spec approved. May I proceed to implementation?"
- Wait for confirmation.

### Step 3 — Implementation

Once the human confirms, delegate to the `implementer` subagent:

> Implement the feature per the approved spec at `docs/specs/<file>`. Strictly follow the anti-hallucination rules in your system prompt.

When implementer finishes:
- Show the list of created/modified files
- Show the result of `dotnet build`
- **Don't declare the feature done yet** — tests are missing

### Step 4 — Tests

Right after step 3, delegate to the `test-writer` subagent:

> Write and run tests for the feature implemented per `docs/specs/<file>`. Cover every acceptance criterion. Don't declare done if any criterion lacks a test.

If a test **fails due to an implementation bug**:
- The test-writer returns it to the implementer
- Repeat step 3 with the feedback
- Then return to step 4

If everything passes:
- Show the final output to the human
- Suggest: `git diff` for manual review

### Step 5 — Delivery to the human

Present:

```
✅ Feature implemented and tested: <title>

Diff summary:
- Files created: <N>
- Files modified: <N>
- Tests added: <N>
- Migrations: <list>

Manual next steps:
1. Run `git diff` and review
2. Run `dotnet test` locally
3. If you approve: `git add . && git commit -m "<suggested message>"`

Suggested commit message:
feat(<module>): <feature title>

Refs: F<number> SCOPE.md
```

## Workflow principles

- **Each agent has isolated context** — the implementer doesn't see prior guardian conversations, and that's intentional.
- **The human is a checkpoint, not a bottleneck** — you pause at critical points (after spec, after approval) but don't ask for confirmation on every file written.
- **Failures go back, not forward** — if the guardian rejects, we don't skip to the implementer. If a test fails due to a bug, we don't skip to delivery.
- **No "try to fix and see"** — any non-trivial failure becomes a question for the human.
