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

`docs/specs/` holds the contracts written when each feature was first built; several were
later changed by `docs/DECISIONS.md`. Where they disagree, the code and DECISIONS win.
`docs/archive/` is history only — never treat it as current, do not read it by default.

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
  `Staging`, redeployed on every push to `dev`; migrations run on startup

## Layout

```
src/Quadra.Api/                 host, Program.cs, Events/ (cross-module event handlers)
src/Quadra.Modules.<Name>/      Auth, Matches, InGame, Profile, Geo, Gamification, Realtime
src/Quadra.Modules.Notifications/  empty (not implemented)
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
- A push to `dev` redeploys the hosted API. Build and tests must pass before merging.
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

## Do NOT

- Do not write secrets, keys, passwords or connection strings in any file. They live in the
  Render dashboard.
- Do not bring back AWS-only pieces (Cognito, SQS, ECS workers) or add Redis, MongoDB or a
  queue. The `Aws:Sqs:*` settings and the Redis packages still present are leftovers, not a
  direction.
- Do not add a NuGet package, change the stack or add a feature listed as out of the MVP
  without asking.
- Do not generate future occurrences of a recurring match (ruling of 2026-07-03).
- No generic repositories, no service locator outside the composition root, no
  `catch (Exception)` that swallows.
- When unsure about scope or stack, stop and ask instead of inventing a decision.
