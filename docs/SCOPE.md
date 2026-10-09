# SCOPE.md — Quadra API

State of the MVP on the `dev` branch, as of 2026-10-06. Reasons for each choice are in
`docs/DECISIONS.md`. The per-feature IN/OUT contracts this file used to hold are in
`docs/archive/SCOPE-2026-10-06-contratos-por-feature.md`.

Glossary: Partida = Match · Organizador = Organizer · Mensalista = Regular · Avulso = DropIn ·
Carta do Jogador = PlayerCard · Janela de Confirmação = ConfirmationWindow.

## Done

| Area | What exists | Main routes (`/api/v1`) |
| --- | --- | --- |
| Auth (FA.1, FA.3) | SMS OTP (6 digits) and Google login; the first valid login creates the user; own JWT + rotating refresh token; logout | `auth/login/sms-otp`, `auth/login/google`, `auth/refresh`, `auth/logout`, `auth/me` |
| Match creation (F1.1) | Recurring or one-off match with format, level, duration, prices, visibility (open / private by code / private by guests), confirmations open from creation (an explicit window or "opens N hours before" still accepted) | `POST matches`, `GET matches/mine`, `GET matches/{id}/detail` |
| Presence (F1.2) | Confirm / decline, self-enrollment by confirming, Regular vs DropIn slots, waiting list, guests without an account, window opened and closed by the clock | `PUT matches/{id}/presences/me`, `matches/{id}/guests` |
| Teams (F1.3) | 2 to 4 teams, balanced or random draw, guests drawn in, manual adjustment | `matches/{id}/teams/draft`, `PUT matches/{id}/teams/{teamId}/members/{playerId}` |
| Scoreboard (F1.4) | Sets best of 3 or 5, pair of teams per set (rotation), end set early, undo last point, live updates over SignalR | `matches/{id}/scoreboard/…`, hub `/hubs/match` |
| MVP vote (F1.5) | One vote each (who played, plus the organizer even without playing), no self-vote, opens when the game ends, 24 h deadline | `matches/{id}/mvp-voting/…` |
| Summary (F1.6) | Immutable summary; generating it is what records stats, history and ranking points | `matches/{id}/summary` |
| Map (F1.7) | Nearby matches by radius (PostGIS), including drafts; private matches never listed | `matches/nearby` |
| Address search | Proxy to Google Places or Photon, restricted to Brazil | `places/autocomplete`, `places/{id}` |
| Profile (F2.1) | Name, surname, unique `@handle`, birth date, single position, modality, declared level, derived skills (ACE/BLK/ATA/DEF/GERAL), stats, match history, photo by signed URL | `profiles/me`, `profiles/handle-availability`, `profiles/me/match-history`, `profiles/me/photo/upload-url` |
| Player card (F2.2) | Card data after 3 matches; premium flag is a stub that always answers `false` | `profiles/me/card` |
| Group ranking (F2.3) | Points per recurring match: attendance +10, win +15, MVP +25 | `matches/{id}/ranking`, `rankings/mine` |
| Feedback | Stores the app's feedback form, 20 per user per day | `POST feedback` |

## Pending

Needs configuration only (no code):

- **Twilio Verify**. Until then the hosted API accepts the fixed code for any phone number,
  so the app must not be shared outside the team.
- **Google login**: the OAuth client IDs.
- **Apple login**: code path exists, disabled until an Apple client ID is configured.

Needs code:

- **In-app notifications** (`notifications` table, list, mark as read, unread counter): the
  module is empty.
- **Push notifications** (planned: Expo Push): nothing implemented.
- **Match cover image**: not in the create contract.
- **Summary of a game with rotating teams** does not record which pair played each set
  (DECISIONS #30).
- **Per-player stats and XP**: the score is recorded by team; the app shows zeros in
  "Meu desempenho" and a fixed Level/XP bar.
- **Event retry**: a failed in-process handler is logged and not retried (no outbox).
- **Cleanup**: the `Aws:Sqs:*` settings are still required at startup although nothing is
  sent; the Redis packages and the `Quadra.Workers.*` projects are unused.

## Out of the MVP

Human rulings (do not implement without a new decision):

- **Recurring match = one game** (2026-07-03, confirmed 2026-10-06). A group is the single
  recurring `matches` row and no future occurrences are generated. So a ranking holds the
  points of one game, and the following stay out until an occurrence model exists: the
  3-match streak bonus (+20) and any multi-week accumulation. Do not ship dormant streak code.
- **`player_xp` lifetime accumulator**: cut (2026-07-03); it only serves the city ranking.
- **One-off matches earn no ranking points**; the ranking endpoint answers `409` for them.
- **Premium**: no table or column; only the `IPremiumStatusReader` stub. No billing.
- **+5 "first match as DropIn" bonus**: not in the MVP.
- **Earning `Advanced` / `Elite` by playing**, the `frequency` stat and skill ratings that
  grow with play: deferred until their formulas are defined.
- **Separate signup** (`POST auth/signup`), passwords, account linking between providers.

Never part of the MVP (Layer 3):

- Courts and arenas (venues, photos, reviews), social feed, friends, contacts sync
- Player search, player rating, city or global ranking
- Achievements and badges, per-play stats input (serve, block, attack, defense)
- Payments, cost splitting, microtransactions, organizer premium plan
- Calendar, Instagram or WhatsApp integration; routes and directions
- More than 4 teams, balancing by position, partial confirmation, player-to-player substitution
