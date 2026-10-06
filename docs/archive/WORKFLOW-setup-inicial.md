# WORKFLOW.md — How to develop on Quadra

> Quick guide for you (human) to use the 4-agent system.

## Initial setup (one-time)

1. Make sure Claude Code is installed and .NET 10 SDK is on your machine
2. Make sure Docker is running (for Testcontainers in integration tests)
3. Copy every file from this package to your repository root
4. Confirm the structure looks like this:

```
your-repo/
├── CLAUDE.md
├── .claude/
│   ├── agents/
│   │   ├── spec-writer.md
│   │   ├── scope-guardian.md
│   │   ├── implementer.md
│   │   └── test-writer.md
│   ├── commands/
│   │   └── feature.md
│   └── hooks/
│       └── pre-edit-scope-check.sh
├── docs/
│   ├── PRODUCT.md
│   ├── SCOPE.md
│   ├── ARCHITECTURE.md
│   └── WORKFLOW.md
└── src/   (to create)
```

## First use: create the initial project structure

Before implementing features, ask Claude Code (without using `/feature` yet):

> Create the initial folder and project structure for Quadra per `docs/ARCHITECTURE.md`. Don't implement features yet — only the empty .csproj files, the solution, and a minimal `Program.cs` for the API. Add the NuGet packages listed in CLAUDE.md.

Review the result. After that, you're ready to use `/feature`.

## Implementing a feature

Example: implementing F1.1 (Match Creation):

```
/feature F1.1 — Match Creation (recurring and one-off)
```

Claude Code will:

1. Call the `spec-writer` → creates `docs/specs/001-match-creation.md`
2. **Pause and ask for your confirmation**
3. Call the `scope-guardian` → audits
4. If approved, **pause and ask for confirmation**
5. Call the `implementer` → writes C# code
6. Call the `test-writer` → writes and runs tests
7. Hand you the diff ready to review

## When something goes wrong

| Symptom | What to do |
| --- | --- |
| Guardian rejected the spec | Read the reason. Almost always the issue is that SCOPE.md is ambiguous OR the spec-writer inflated the feature. Adjust and re-run. |
| Spec feels "weak" | Tell the spec-writer what's missing. DO NOT skip to the implementer "fixing it manually". The workflow discipline is what keeps quality. |
| Implementer gets stuck with a question | This is desired behavior. Answer the question and it continues. DON'T ask it to "try and see". |
| Build fails | The implementer shouldn't deliver with a broken build. If it happens, it's a sign of an ignored rule — ask it to review. |
| Test fails | The test-writer returns it to the implementer. Let the cycle run. |
| You want to do something out of SCOPE | **Update SCOPE.md first**, manually. Then run `/feature`. Don't try to bypass the guardian. |

## Commit pattern

After `/feature` completes, review and commit manually. Suggested message:

```
feat(<module>): <feature title>

Refs: F<number> SCOPE.md
```

## Operational best practices

1. **One feature at a time.** Don't run multiple `/feature` in parallel on the same branch.
2. **Branch per feature.** `git checkout -b feature/F1.1-match-creation` before running `/feature`.
3. **Keep SCOPE.md alive.** When the product evolves, update SCOPE before running `/feature`. This file is the constitution.
4. **Always review the diff.** The system doesn't replace human review — it guarantees the diff is smaller and more focused, making review easier.
5. **When in doubt about stack or architecture**, edit `CLAUDE.md`. Agents read it every session.

## System evolution

When you start the frontend (React Native), you'll need to:

- Add a separate `frontend-spec-writer`
- Update `SCOPE.md` to describe screens
- Probably an `e2e-test-writer` instead of the current test-writer

But that's a future problem. For now, focus on the backend.

## Metrics to check if it's working

After 2-3 features implemented through the workflow, ask yourself:

- ❓ Has the `scope-guardian` rejected at least once? (If it never rejects, either it's weak, or the spec-writer is too conservative.)
- ❓ Are the specs in `docs/specs/` useful for understanding what was done without reading the code?
- ❓ Do the tests cover acceptance criteria or are they generic?
- ❓ How many times did you have to "fix manually" after the workflow? If a lot, adjust CLAUDE.md with the missing rule.

The system is alive. The `.md` files of the agents are editable — when you notice a recurring error pattern, add an explicit rule.
