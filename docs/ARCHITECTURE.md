# ARCHITECTURE.md — Quadra Backend

> Architecture per the reference diagram. This is the architectural source of truth for the agents.

---

## Overview

> **Amendment — 2026-10-06 (Johny): no AWS-only infrastructure.** The backend now runs as a single
> container plus a PostgreSQL/PostGIS database (hosted test environment: Render + Neon).
> - **Events are handled in-process.** `IEventPublisher` is `InProcessEventPublisher`: when a module
>   publishes an event, the `IEventHandler<T>` implementations in `Quadra.Api/Events` run inside the
>   same request (profile provisioning on `UserRegistered`; player stats and group-ranking points on
>   `MatchSummaryGenerated`). There is no SQS and no separate Background Worker. Trade-off: a handler
>   that fails is logged and not retried (no outbox yet).
> - **Photo storage is optional and S3-compatible.** With no bucket configured the API serves
>   profiles without a photo and refuses uploads with 503; any S3-compatible service can be plugged
>   in through the `Aws:S3` settings (`ServiceUrl`, `AccessKeyId`, `SecretAccessKey`).
> - **Auth is the API's own** (see the Auth module below) — no Cognito.
> - Not implemented yet: push notifications (the Notification Worker) and the Redis SignalR backplane.
>
> The sections below still describe the original AWS design (ECS workers, SQS, S3) and are kept as
> the reference for a future scale-out; where they disagree with this note, this note wins.

Modular monolith in .NET 10 (CORE), with two separate workers running on ECS:

- **CORE**: ASP.NET Core API + SignalR Hub in the same process
- **Background Worker**: dedicated BackgroundService for heavy processing
- **Notification Worker**: dedicated BackgroundService for push notification delivery

## Infra layers

| Component | Responsibility |
| --- | --- |
| CloudFront | CDN for assets and static-response caching |
| ALB | HTTPS load balancer in front of CORE |
| CORE (ECS) | API + SignalR Hub |
| Background Worker (ECS) | SQS consumer for heavy work (stat calculation, XP distribution, card data generation) |
| Notification Worker (ECS) | Dedicated SQS consumer for push delivery (FCM/APNS) |
| PostgreSQL + PostGIS | Main database |
| Redis | SignalR backplane; light cache |
| SQS | Inter-module events and worker decoupling |
| S3 | Profile photos, generated assets |
| Twilio Verify | SMS OTP delivery and code check (called from the Auth module; swappable behind `IPhoneVerificationService`) |
| Google Identity | Issuer of the Google ID tokens the Auth module validates (JWKS) |

---

## CORE modules

Each module is a separate .NET project at `src/Quadra.Modules.<Name>/`. Boundaries are an **architectural rule**, validated by the `scope-guardian`.

### Auth
The source of truth for identity — there is no external identity provider (no AWS Cognito). Logs users in by SMS OTP (Twilio Verify) or Google ID token, creating the account on the first successful login; Apple is wired but not enabled. Issues its own JWT access tokens (HS256, short-lived) plus rotating refresh tokens stored hashed, and validates those JWTs on every other route. No request reaches other modules without passing through it.

**Own tables**: `users`, `refresh_tokens`

### Matches
Heart of the MVP. Manages the full lifecycle of a match — creation (recurring or one-off), confirmation window, waiting list, DropIn slot opening, closing. Publishes events to SQS when status changes.

**Own tables**: `matches`, `match_presences`, `waiting_list`
**Events published**: `MatchCreated`, `MatchStatusChanged`, `PresenceConfirmed`, `MatchClosed`

### InGame
Takes over when the match begins. Drafts teams balanced by level, allows manual adjustment by the organizer, controls set scoreboard in real time. Works alongside the Real-time Hub — every point scored becomes a WebSocket broadcast.

**Own tables**: `teams`, `team_members`, `scores`, `mvp_votes`
**Events published**: `MatchStarted`, `ScoreUpdated`, `MatchEnded`, `MvpAwarded`

### Profile
Everything about player identity, in the shape the mobile app uses: name and surname, unique `@handle`, birth date, single position (`LEV`…`COR`), preferred modality, self-declared level (then recalculated), and skill ratings derived from level + position. Photo (optional), automatically calculated level, match history, accumulated stats and the player card. Read-heavy — Background Worker writes, the app reads.

**Own tables**: `player_profiles`, `player_stats`, `player_match_history`, `player_cards`
**Events consumed**: every event related to a finished match

### Geo / Map
Answers location queries — "matches within 3km", "nearby courts", map pins. Backed by PostGIS. Owns nothing — only runs geographic queries over the matches tables.

**Own tables**: none (runs cross-module READ-ONLY queries)
**Exception to boundary rule**: this module has SELECT permission on `matches` (read-only, for geographic queries)

### Notifications
Exclusively IN-APP notifications — the bell, the counter, the listing and "mark as read". Doesn't send anything out. Just persists and serves records from the `notifications` table. The Notification Worker is the one knocking on the device.

**Own tables**: `notifications`
**Events consumed**: many (see Background Worker)

### Gamification
The rules for points, levels and achievements. Doesn't execute anything on its own — exposes rules and calculations. When the Background Worker needs to distribute XP or check whether a player unlocked an achievement, it calls this module. Keeps the group ranking up to date.

**Own tables**: `player_xp`, `group_rankings`, `point_transactions`

### Real-Time Hub
Not a business module — internal infrastructure. Manages active WebSocket connections, groups players by match room and broadcasts. Any module needing to notify clients in real time goes through it.

**Own tables**: none (connection state lives in Redis)

---

## Workers

### Notification Worker
Specialist in one thing only — delivering messages outside the app, to the device's OS. Runs separately on ECS and consumes a dedicated SQS queue (`notifications-queue`).

### Background Worker
Responsible for all heavy work that can't block the API. Runs as a separate process on ECS. Two operation modes:

1. **Event consumer**: consumes SQS events published by modules and triggers calculations (update stats, distribute XP, generate card data after 3rd match)
2. **Scheduled**: periodic jobs (recalculate daily ranking, close expired confirmation windows)

---

## Data flow (example: player voted MVP)

1. Organizer ends voting → `InGame` publishes `MvpAwarded` to SQS
2. `Background Worker` consumes the event
3. `Background Worker` calls `Gamification.DistributePoints(playerId, +25)`
4. `Gamification` updates `player_xp` and `group_rankings`
5. `Background Worker` calls `Profile.UpdateStats(playerId)`
6. `Profile` recalculates `MVPs received` and level
7. `Background Worker` publishes `NotificationRequested` for the player
8. `Notification Worker` consumes and dispatches push via OneSignal
9. `Notifications` records the event in the in-app table

None of these steps are synchronous in the organizer's HTTP request. Closing responds fast; everything else happens in background.

---

## Naming conventions

- .NET projects: `Quadra.<Layer>.<Name>` (e.g. `Quadra.Modules.Matches`)
- Namespaces: same as project name
- Tables: `snake_case_plural` (e.g. `match_presences`)
- Columns: `snake_case` (configure EF for snake_case naming)
- REST endpoints: `/api/v1/<resource>` (kebab-case if compound)
- SQS events: `<Entity><Action>` PascalCase (e.g. `MvpAwarded`)
- EF migrations: `<TimestampPrefix>_<ShortDescription>` (generated by the tool)
