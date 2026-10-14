---
name: security-audit
description: Security audit of Quadra (API, mobile app, landing), full or limited to one area such as auth, endpoints, data, uploads, business rules, realtime, infra, mobile or dependencies. Use when asked to audit or review security, check access to other users' data, investigate a reported vulnerability, threat-model a feature, or retest a security fix.
argument-hint: "[full | <area> | <feature> | SEC-xx]"
---

# Security audit — Quadra

Scope requested: **$ARGUMENTS**. With no argument, audit the area touched by the current change.
`full` covers every area listed in the threat model. `SEC-xx` retests one finding.

The everyday rules are in `CLAUDE.md`. This file is the procedure; do not copy either into a
report.

## Where the knowledge is

It lives outside git, in the workspace folder `../security/` next to the repos, because
`quadra-api` is public and the files describe open flaws. Start with `../security/README.md`.

| File | Use it for |
| --- | --- |
| `threat-model.md` | assets, actors, trust boundaries, threats per area, controls present and missing |
| `endpoint-access-matrix.md` | who may call each route, the ownership rule, the test, the status |
| `security-findings.md` | known findings `SEC-xx`: do not report them again as new |
| `remediation-plan.md` | fix order and dependencies |
| `audit-tests/` | the integration tests that prove each finding |

If `../security/` is missing (for example a cloud clone with only this repo), say so, work from
the code, and never write open findings into a repository.

## Procedure

1. **Scope.** State the area, the repos, the commit (`git rev-parse --short HEAD`) and what is out
   of scope.
2. **Read the existing knowledge first**: the matrix rows, the findings and the threat-model
   section for that area. Do not reread the whole project, the chat history or `docs/archive/`.
3. **Open only the files in the path**: controller, handler, repository, entity configuration;
   in the app, the screen, the hook in `src/features/<area>/api/`, the API client.
4. **Map the flow** between app, API, database and external services for each operation, and note
   where the caller's identity comes from.
5. **Compare the code with the expected rule** in the matrix and the threat model, including abuse
   of legitimate features and concurrency.
6. **Record evidence** as `file:line`.
7. **Classify each item** as confirmed (a test or an unambiguous code path), hypothesis (say what
   would prove it) or not verified (say why). No scanner alert is not proof of safety.
8. **Rate severity** (critical, high, medium, low, info) from impact, exposure and ease, with the
   preconditions and any combination with other findings.
9. **Recommend a fix that fits the architecture** (owning module, `Quadra.Shared` interfaces or
   events across modules, thin controllers, FluentValidation, no new package without asking).
10. **Propose the regression test**: an integration test with `WebApplicationFactory` and
    Testcontainers, two accounts (owner and stranger), asserting the secure behavior. Base it on
    `../security/audit-tests/AuditWebApplicationFactory.cs`.
11. **Update `../security/`**: new `SEC-xx` (next free id) or new evidence on an existing one,
    matrix rows, threat-model controls. A finding moves to `fixed` only when its test passes.
    Never write secrets, tokens or real personal data.
12. **Report** using the format below.

## Limits

- Read-only by default; fixes only when the user asks, in a branch from `dev`.
- Nothing against Render, Neon or any external system without explicit authorization; tests run
  locally with Testcontainers inside WSL.
- An audit test whose finding is still open never goes into the public repo.

## Report format

```
Scope: <area> · commit <sha> · out of scope: <...>
| Id | Title | Severity | Status | Evidence | Test |
Confirmed: ...
Hypotheses (how to prove): ...
Not verified (why): ...
Docs updated: <files in ../security/>
Checks run and their results: ...
```
