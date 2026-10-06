# SCOPE.md — MVP Constitution

> This file defines what IS and what IS NOT in the MVP.
> The `scope-guardian` uses this document as the single source of truth.
> Any feature outside this list is REJECTED — no matter how useful it seems.

---

## Glossary (domain terms)

| Portuguese (product docs) | English (code) | Meaning |
| --- | --- | --- |
| Quadra | Quadra | App name (kept) |
| Jogo / Partida | Match | A volleyball session |
| Organizador | Organizer | Player who creates and manages matches |
| Mensalista | Regular | Fixed player in a recurring match |
| Avulso | DropIn | Player without fixed bond, fills open slots |
| Rede | Network | Community feed (Layer 3 — not in MVP) |
| Carta do Jogador | PlayerCard | FIFA-style stats card |
| Janela de Confirmação | ConfirmationWindow | Period when Regulars can confirm/decline |

---

## MVP principle

Layer 1 (Match Core) and Layer 2 (Retention) are in the MVP. Layer 3 (Growth) is NOT.

Frontend is NOT in this phase — backend only. Specs describe **API contracts**, not screens.

---

## Layer 1 — Match Core (MVP)

### F1.1 — Match Creation
- **Module**: `Matches`
- **IN**: create recurring or one-off match; fields `name`, `location`, `address`, `lat/lon`, `dateTime`, `maxPlayers`, `price` (optional), `type` (Recurring/OneOff), `frequency` (if recurring), `dayOfWeek` (if recurring)
- **IN**: confirmation window (`windowOpensAt`, `windowClosesAt`)
- **IN**: slots reserved for Regulars vs slots open to DropIns
- **OUT**: complex custom rules (only a free-text `description` field)
- **OUT**: payment / cost splitting between players
- **OUT**: external calendar integration

### F1.2 — Presence Management
- **Module**: `Matches`
- **IN**: list of Regulars with status `Confirmed | Declined | Pending`
- **IN**: push notification when window opens (delegated to `Notifications` module)
- **IN**: automatic waiting list when slots exceeded
- **IN**: automatic slot release to DropIns after `windowClosesAt`
- **OUT**: partial confirmation (e.g. "I'll arrive late")
- **OUT**: direct player-to-player substitution

### F1.3 — In-Game: Teams
- **Module**: `InGame`
- **IN**: team draft balanced by player level (queries `Profile` via interface)
- **IN**: manual team adjustment by the Organizer
- **OUT**: balancing by preferred position (push to v2)
- **OUT**: history of who played with whom

### F1.4 — In-Game: Scoreboard
- **Module**: `InGame`
- **IN**: set-by-set scoreboard (best of 3 or 5)
- **IN**: real-time point/set recording (via SignalR through `Realtime` module)
- **IN**: match state (`NotStarted | InProgress | Ended`)
- **OUT**: individual stats per play (serve, defense, attack) — these come in v2 (US 1.1 of PDF)
- **OUT**: point replay / rewind

### F1.5 — Post-Match: MVP
- **Module**: `InGame` (voting) + `Gamification` (point allocation)
- **IN**: each player votes for ONE name (cannot self-vote)
- **IN**: automatic MVP selection from votes
- **IN**: voting deadline (set by Organizer; default 24h)
- **OUT**: multiple award categories (best serve, best defense, etc.)

### F1.6 — Match Summary
- **Module**: `Matches` (orchestrator) + `InGame` + `Gamification`
- **IN**: endpoint returning final score, duration, MVP, team composition
- **IN**: immutable record (cannot be edited afterwards)
- **OUT**: shareable image generation (frontend)

### F1.7 — Matches Map
- **Module**: `Geo`
- **IN**: `GET /matches/nearby?lat&lon&radiusKm` endpoint — returns matches with open slots
- **IN**: filter by matches open to DropIns
- **IN**: PostGIS query (ST_DWithin on `geography` with SRID 4326)
- **OUT**: court/arena pins (Layer 3)
- **OUT**: routes / directions

---

## Layer 2 — Retention (MVP)

### F2.1 — Player Profile
- **Module**: `Profile`
- **IN**: photo (S3 object reference), name, primary position, secondary position — position catalog: `Setter | OutsideHitter | Opposite | MiddleBlocker | Libero`
- **IN**: automatically calculated level — MVP computes **`Beginner | Intermediate`** only (see `PRODUCT.md` level table). `Advanced` and `Elite` are **deferred**: Advanced's "vote average" is undefined and Elite depends on the Layer-3 global ranking
- **IN**: aggregated stats: matches played, wins, losses, draws, MVPs received (draws included because F1.6 permits a null-winner match)
- **IN**: match history with pagination (own table `player_match_history`)
- **OUT**: manual stat editing (everything derived from events)
- **OUT (deferred, not cut)**: `frequency` stat — no formula is defined in PRODUCT/SCOPE; excluded from the MVP until specified, re-added when defined
- **OUT (deferred, not cut)**: `Advanced` / `Elite` level tiers — see above

### F2.2 — Player Card (data)
- **Module**: `Profile` (data) + `Gamification` (premium check)
- **IN**: endpoint returning **card data** (not image) — JSON with stats, level, position, free/premium flag
- **IN**: generated after 3 recorded matches
- **OUT**: visual rendering (frontend)
- **OUT**: premium art variants (catalog comes later)
- **OUT (human ruling, 2026-07-03)**: no persistent premium data structure in the MVP. The free/premium flag is resolved via a stub (`IPremiumStatusReader` → always `false`) behind a stable interface. No `plan`/`is_premium` column or table is created until billing is specified. This narrows PRODUCT's "prepared data structure" to interface-only for now.

### F2.3 — Group Ranking
- **Module**: `Gamification`
- **IN**: endpoint returning accumulated point ranking per recurring match
- **IN**: scoring per product doc rules (attendance +10, win +15, MVP +25)
- **OUT**: city-wide ranking (Layer 3)
- **RULING (human, 2026-07-03) — grouping model**: for the MVP a "group" is the single recurring `matches` row; `GroupId = matchId`. Multi-occurrence accumulation is deferred together with F1.1's deferred "recurring match instance generation". The `group_id` key is kept distinct from `match_id` in the schema so a future occurrence model can repoint it with no migration.
- **OUT (human ruling, 2026-07-03) — 3-streak +20**: deferred. The "3 consecutive matches" rule is unreachable while a group is summarized at most once (F1.1 defers occurrence generation; F1.6 enforces `UNIQUE (match_id)`). It re-enters scope only when the recurring-occurrence model exists. F2.3 MUST NOT ship a dormant `StreakCalculator`, `Streak` point reason, `current_streak` column, or streak recompute — no dead code.
- **OUT (human ruling, 2026-07-03) — `player_xp` lifetime accumulator**: cut from the MVP. Its only consumer is the Layer-3 city-wide leaderboard (OUT). Reintroduce it alongside that feature. F2.3 maintains only `point_transactions` (ledger) and `group_rankings` (per-group standing).
- **OUT (human ruling, 2026-07-03) — OneOff match points**: a OneOff match has no group to accumulate into; the ranking endpoint returns `409` for a OneOff `matchId` and the worker awards it no points. Reconciles with PRODUCT's non-group-qualified point list.
- **Note (docs reconciliation)**: PRODUCT lists a 5th rule ("+5 first match as DropIn in a new group") that SCOPE F2.3 does not enumerate. SCOPE is source of truth; the +5 DropIn bonus is NOT in the MVP. Reconcile PRODUCT when convenient.

---

## Layer 3 — Growth (OUT OF MVP)

**Everything here is REJECTED automatically by the guardian.** Listed only for reference:

- Courts/Arenas Map (registered venues, photos, reviews)
- Social feed / Network (posts, stories)
- Player search by level/position/region
- Instagram/WhatsApp integration
- City-wide ranking
- Friendship system (US 5.1, 5.2 of PDF)
- Player rating (US 3.3 of PDF)
- GPS-based personalized recommendations (US 4.1)
- Contacts sync (US 5.2)
- Advanced stats like serve/block/attack/defense (numbers from the profile mockup)
- Unlockable achievements / badges (US 1.2)
- Cosmetic microtransactions
- Premium plan for organizers (multi-group analytics)

---

## Auth — Cross-cutting (MVP)

### FA.1 — JWT Validation Middleware
- **Module**: `Auth`
- **IN**: ASP.NET middleware that validates JWTs issued by AWS Cognito
- **IN**: extracts claims (sub, email, custom:role) into HttpContext.User
- **IN**: rejects expired or malformed tokens with 401
- **IN**: configuration via appsettings (Cognito User Pool ID, region, audience)
- **OUT**: token issuance (Cognito does that)
- **OUT**: refresh token logic (FA.3)
- **OUT**: any UI for login (frontend)

### FA.2 — User Signup
- **Module**: `Auth`
- **IN**: endpoint POST /api/v1/auth/signup that triggers Cognito SignUp
- **IN**: persists local user record (id, cognito_sub, email, created_at) in `users` table
- **IN**: returns user id and confirmation status
- **OUT**: email/SMS confirmation flow (handled by Cognito)
- **OUT**: profile data (that's the Profile module's F2.1)

### FA.3 — Login flows
- **Module**: `Auth`
- **IN**: endpoint POST /api/v1/auth/login/sms-otp (initiate + verify)
- **IN**: endpoint POST /api/v1/auth/login/google (exchange Google token for Cognito session)
- **IN**: endpoint POST /api/v1/auth/login/apple (idem)
- **IN**: endpoint POST /api/v1/auth/refresh
- **IN**: returns JWT + refresh token
- **OUT**: account linking (multiple providers same user) — push to v2
- **OUT**: password reset (Cognito-hosted flow only, not custom)

## Auth and supporting infra (cross-cutting, MVP)

These are not "user stories" but must exist for the MVP to work:

- **Auth**: SMS OTP signup, Google login (Cognito), Apple login (Cognito), JWT issuance and validation
- **In-app notifications**: `notifications` table, endpoints `GET /notifications`, `POST /notifications/{id}/mark-read`, unread counter
- **Notification Worker**: SQS consumer delivering push to devices (FCM/APNS via OneSignal or similar — provider choice outside this spec)
- **Background Worker**: SQS consumer to distribute XP, update rankings, generate card data
- **Real-time Hub**: SignalR for live scoreboard and presence updates

---

## Guardian evaluation rules

The `scope-guardian` rejects a spec if:

1. **Feature not in this list** → REJECT
2. **Feature from Layer 3** → REJECT
3. **Wrong module** (e.g. gamification logic inside Matches module) → REJECT
4. **Direct access to another module's tables** → REJECT
5. **New NuGet dependency without justification** → REJECT
6. **UI / screen details** → REJECT (backend only in this phase)
7. **Stack change** (ORM, framework, database) → REJECT and ask for human confirmation

If it passes all → APPROVE.
