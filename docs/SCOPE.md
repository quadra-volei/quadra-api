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
- **IN**: photo (S3 object reference), name, primary position, secondary position
- **IN**: automatically calculated level (`Beginner | Intermediate | Advanced | Elite`) using product doc criteria
- **IN**: aggregated stats: matches played, wins, losses, MVPs received, frequency
- **IN**: match history with pagination
- **OUT**: manual stat editing (everything derived from events)

### F2.2 — Player Card (data)
- **Module**: `Profile` (data) + `Gamification` (premium check)
- **IN**: endpoint returning **card data** (not image) — JSON with stats, level, position, free/premium flag
- **IN**: generated after 3 recorded matches
- **OUT**: visual rendering (frontend)
- **OUT**: premium art variants (catalog comes later)

### F2.3 — Group Ranking
- **Module**: `Gamification`
- **IN**: endpoint returning accumulated point ranking per recurring match
- **IN**: scoring per product doc rules (attendance +10, win +15, MVP +25, 3-streak +20)
- **OUT**: city-wide ranking (Layer 3)

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

> **Decision (2026-10-05, Johny)**: the backend no longer uses AWS Cognito. The Auth module is the
> identity source of truth: it issues and validates its own JWTs, verifies phone numbers through
> Twilio Verify, and validates Google ID tokens itself. FA.1–FA.3 below reflect that decision.

### FA.1 — JWT Validation Middleware
- **Module**: `Auth`
- **IN**: ASP.NET middleware that validates the JWTs issued by this API (HS256, signing key from configuration)
- **IN**: validates signature, issuer, audience and lifetime; extracts claims (sub = `users.id`, role) into HttpContext.User
- **IN**: rejects expired, malformed, unsigned or foreign-key tokens with 401
- **IN**: configuration via appsettings / environment (`Auth:Jwt`: issuer, audience, signing key, lifetimes); startup fails without a strong key
- **OUT**: token issuance and refresh token logic (FA.3)
- **OUT**: any UI for login (frontend)

### FA.2 — User Signup
- **Superseded — no separate signup.** There is no `POST /api/v1/auth/signup`. The local user record (`users`: id, provider, phone_number / external_subject, email, created_at) is created by FA.3 on the first successful login (first valid SMS OTP for a phone number, or first valid Google ID token for a Google account).
- **OUT**: profile data (that's the Profile module's F2.1)

### FA.3 — Login flows
- **Module**: `Auth`
- **IN**: endpoint POST /api/v1/auth/login/sms-otp (initiate + verify) — 6-digit code delivered and checked by Twilio Verify, behind `IPhoneVerificationService` (swappable provider; fixed-code fake for development/tests, refused in Production)
- **IN**: initiate accepts any valid phone number; the first valid OTP creates the user (no 404 for unknown phones)
- **IN**: endpoint POST /api/v1/auth/login/google (validate the Google ID token server-side, find or create the user, issue a session)
- **IN**: endpoint POST /api/v1/auth/login/apple (idem) — code path kept, **not enabled** until Apple is configured
- **IN**: endpoint POST /api/v1/auth/refresh — rotates the refresh token on every use
- **IN**: endpoint POST /api/v1/auth/logout — revokes the session's refresh token
- **IN**: endpoint GET /api/v1/auth/me — the account behind the access token
- **IN**: returns own JWT access token (short-lived) + opaque refresh token (stored only as a SHA-256 hash in `refresh_tokens`)
- **OUT**: account linking (multiple providers same user) — push to v2
- **OUT**: password login / password reset (there are no passwords)

## Auth and supporting infra (cross-cutting, MVP)

These are not "user stories" but must exist for the MVP to work:

- **Auth**: SMS OTP login (Twilio Verify), Google login, Apple login (not enabled yet), own JWT issuance and validation — no AWS Cognito
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
