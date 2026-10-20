# CLAUDE.md — Quadra API

Backend of **Quadra**, a volleyball app (match organization, live game, ranking). The mobile
client lives in the sibling repo `quadra-mobile` and already consumes this API.

## Where things are

| Need | Read |
| --- | --- |
| What is done, pending, out of the MVP | `docs/SCOPE.md` |
| Why something is the way it is | `docs/DECISIONS.md` (source of truth for decisions) |
| Modules, tables, events, hosting | `docs/ARCHITECTURE.md` |
| Product vision, levels, points | `docs/PRODUCT.md` |

`docs/archive/` is history only (old specs, the AWS design, the four-agent workflow) — never
treat it as current and do not read it by default.

## Stack (do not change without asking)

- .NET 10 / C# 14, ASP.NET Core with **Controllers** (no Minimal APIs)
- EF Core 10 on **PostgreSQL + PostGIS** (Neon in the hosted environment)
- Auth: **own JWT** (HS256 access token + rotating refresh token stored hashed), Google ID
  token validated server-side, SMS OTP behind `IPhoneVerificationService` (Twilio Verify over
  `HttpClient`; fake provider with a fixed 6-digit code until Twilio is configured, refused in
  Production)
- Events: **in-process** (`IEventPublisher` → `InProcessEventPublisher` → `IEventHandler<T>` in
  `src/Quadra.Api/Events`). No queue, no worker process
- Real-time: SignalR hub `/hubs/match`, single instance, no backplane
- Photos: S3-compatible storage through signed URLs (Cloudflare R2), optional — with no bucket
  configured the API runs without photos
- Address search: Geo proxy `/api/v1/places` (Google Places when a key is set, Photon otherwise)
- FluentValidation, manual DTO mapping (no AutoMapper), Serilog JSON
- Tests: xUnit + FluentAssertions + NSubstitute; integration with WebApplicationFactory +
  Testcontainers (real Postgres)
- Hosting: one Docker container on Render (`Dockerfile` + `render.yaml`), environment
  `Staging`, redeployed on every push to `main`; migrations run on startup

## Layout

```
src/Quadra.Api/                 host, Program.cs, Events/ (cross-module event handlers)
src/Quadra.Modules.<Name>/      Auth, Matches, InGame, Profile, Geo, Gamification, Realtime
src/Quadra.Modules.Notifications/  in-app notifications + Expo Push (one file, Notifications.cs)
src/Quadra.Workers.*/           empty placeholders, not deployed
src/Quadra.Shared/              events and cross-module interfaces
src/Quadra.Infrastructure/      EF helpers, event publisher, photo storage
tests/Quadra.UnitTests/  tests/Quadra.IntegrationTests/
```

## Commands

```bash
dotnet build                                   # warnings are errors
dotnet test tests/Quadra.UnitTests
dotnet test tests/Quadra.IntegrationTests      # needs Docker (Testcontainers)
dotnet run --project src/Quadra.Api            # http://localhost:5075
docker compose up -d postgres                  # local database
dotnet format

# from inside the module that owns the entity
dotnet ef migrations add <Name> --startup-project ../Quadra.Api
dotnet ef database update --startup-project ../Quadra.Api
```

## Branches

- Start every change from `dev` and merge it back into `dev`. Never commit straight to `dev`
  or `main`.
- `dev` collects the merged changes; a PR from `dev` into `main` releases them. A push to
  `main` redeploys the hosted API. Build and tests must pass before merging.
- Commit messages in Portuguese, `tipo(modulo): resumo` (as in `git log`).

## Conventions

- **Module boundary**: a module never reads another module's tables. Use the interfaces in
  `Quadra.Shared` or an event; handlers that coordinate modules live in `Quadra.Api/Events`.
  Only exception: Geo reads `matches` read-only.
- Each module registers itself through `Add<Module>Module(IServiceCollection, …)`.
- Nullable enabled everywhere. Async for all I/O, `CancellationToken` on every public async
  method; never `.Result` / `.Wait()`.
- Records for DTOs, classes for EF entities. Thin controllers.
- Tables `snake_case_plural`, columns `snake_case`, routes `/api/v1/<resource>`.
- A change that alters behavior the app relies on gets a line in `docs/DECISIONS.md` and, if
  it changes what is done or pending, in `docs/SCOPE.md`.

## Security (every change)

Security is part of done. Knowledge (threat model, endpoint matrix, findings, fix plan) lives
outside git in the workspace folder `../security/` because this repo is public. The
`security-audit` skill (procedure) and the `security-auditor` agent (independent review) live in
the workspace root `.claude/`, also outside git: start Claude from the workspace root to use them.

- Validate every input on the server (FluentValidation); never trust the app.
- The caller's id comes from the token, never from the body or the route.
- Every read or write of a resource by id checks ownership or membership (owner, organizer,
  participant, or public). Being logged in is not permission; private matches stay private.
- New controllers keep `[Authorize]`; `[AllowAnonymous]` only for login, refresh, logout and the
  app version gate (`GET app/version`), whitelisted in `MatchVisibilityAuditTests`.
- Least privilege: return only the fields the caller may see; no extra DB or service rights.
- No tokens, passwords, phone numbers or personal data in logs, errors or responses to others;
  errors go out as ProblemDetails without internals.
- Reviewing a dependency or config change (NuGet, `render.yaml`, `Dockerfile`, auth options) is
  part of the change.
- A sensitive operation gets a negative test: a stranger (and a non-organizer) is refused.
- Never disable, skip or weaken a security control or test to make something pass.
- Run the relevant tests before calling a sensitive change done, and report which checks ran,
  their results and what was not verified.
- Never run destructive or load tests against Render or Neon without Johny's explicit OK.
- Never put open security findings or their failing tests in this repo.

## Do NOT

- Do not write secrets, keys, passwords or connection strings in any file. They live in the
  Render dashboard.
- Do not bring back AWS-only pieces (Cognito, SQS, ECS workers) or add Redis, MongoDB or a
  queue. The `Aws:Sqs:*` settings and the Redis packages still present are leftovers, not a
  direction.
- Do not add a NuGet package, change the stack or add a feature listed as out of the MVP
  without asking.
- Do not generate more than the next game of a recurring group: it is created only when the
  previous game's summary is generated (DECISIONS #64), never a calendar of future games.
- No generic repositories, no service locator outside the composition root, no
  `catch (Exception)` that swallows.
- When unsure about scope or stack, stop and ask instead of inventing a decision.
