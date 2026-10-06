# ARCHITECTURE.md — Quadra API

How the backend is actually built and hosted. The original AWS design (ECS workers, SQS,
Cognito, Redis) is in `docs/archive/ARCHITECTURE-aws-original.md` for reference only.

## Shape

One ASP.NET Core process (API + SignalR hub) and one PostgreSQL/PostGIS database.
Hosted test environment: a Docker container on Render (free plan, `Staging`) plus Neon.
The free plan sleeps when idle, so the first request after a pause can take up to a minute.

External services, all behind interfaces and all optional at startup except the database:

| Service | Used for | Interface / setting |
| --- | --- | --- |
| Twilio Verify | SMS code | `IPhoneVerificationService`, `Auth:PhoneVerification` |
| Google Identity | Validating Google ID tokens | `Auth:Google` |
| Cloudflare R2 (S3-compatible) | Profile photos by signed URL | `Aws:S3` |
| Google Places / Photon | Address search | `IPlaceSearchService`, `Places` |

## Modules

Each module is a project `src/Quadra.Modules.<Name>/` with its own DbContext and migrations.

| Module | Responsibility | Tables |
| --- | --- | --- |
| Auth | Login, JWT issuance and validation, refresh tokens | `users`, `refresh_tokens` |
| Matches | Match lifecycle, presence, waiting list, guests, summary | `matches`, `match_presences`, `waiting_list`, `match_guests`, `match_summaries`, `match_summary_players`, `match_summary_sets` |
| InGame | Teams, scoreboard, MVP vote | `teams`, `team_members`, `scoreboards`, `scoreboard_sets`, `mvp_votings`, `mvp_votes` |
| Profile | Player identity, stats, history, card, feedback | `player_profiles`, `player_stats`, `player_match_history`, `player_cards`, `feedback` |
| Gamification | Points and group ranking | `point_transactions`, `group_rankings` |
| Geo | Nearby matches and address search | none (reads `matches` read-only) |
| Realtime | SignalR hub `/hubs/match` | none (groups in memory) |
| Notifications | Not implemented | — |

## Boundaries

- A module never reads another module's tables; no cross-module JOINs. Geo's read-only access
  to `matches` is the only exception.
- Modules talk through interfaces in `Quadra.Shared` (for example `IPlayerSummaryReader`) or
  through events.

## Events

`IEventPublisher` is `InProcessEventPublisher`: publishing runs the `IEventHandler<T>`
implementations in `src/Quadra.Api/Events` inside the same request.

| Event | Handler effect |
| --- | --- |
| `UserRegistered` | Creates the empty player profile |
| `MatchSummaryGenerated` | Updates player stats and history; awards ranking points |

Trade-off: a handler that fails is logged and not retried (no outbox). Nothing runs in the
background except the confirmation-window sweeper (`Matches:WindowSweepSeconds`); matches are
also synced on every request that touches them, because the host sleeps.

## Real-time

Clients call `JoinMatchRoom` / `LeaveMatchRoom` and receive `ScoreboardUpdated` /
`PresenceUpdated`. The message is only a signal: clients re-read the state over REST.
Running more than one instance would require a backplane first.

## Naming

- Projects and namespaces: `Quadra.<Layer>.<Name>`
- Tables `snake_case_plural`, columns `snake_case`
- Routes `/api/v1/<resource>`, kebab-case when compound
- Events `<Entity><Action>` in PascalCase
