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
| Match creation (F1.1) | Recurring or one-off match with format, level, duration, prices, visibility (open / private by code / private by guests), confirmations open from creation (an explicit window or "opens N hours before" still accepted); optional cover photo (key from the profile-photo upload, read URL in the detail) | `POST matches`, `GET matches/mine`, `GET matches/{id}/detail` |
| Edit and cancel | The organizer edits the details (not type nor visibility) and cancels until the game starts; slots never below the confirmed players | `PUT matches/{id}`, `DELETE matches/{id}` |
| Presence (F1.2) | Confirm / decline, self-enrollment by confirming, Regular vs DropIn slots, waiting list, guests without an account, window opened and closed by the clock | `PUT matches/{id}/presences/me`, `matches/{id}/guests` |
| Teams (F1.3) | 2 to 4 teams, balanced or random draw, guests drawn in, manual adjustment | `matches/{id}/teams/draft`, `PUT matches/{id}/teams/{teamId}/members/{playerId}` |
| Scoreboard (F1.4) | Sets best of 3 or 5, pair of teams per set (rotation), end set early, undo last point, live updates over SignalR | `matches/{id}/scoreboard/…`, hub `/hubs/match` |
| MVP vote (F1.5) | One vote each (who played, plus the organizer even without playing), no self-vote, opens when the game ends, 24 h deadline | `matches/{id}/mvp-voting/…` |
| Summary (F1.6) | Immutable summary; generating it is what records stats, history and ranking points | `matches/{id}/summary` |
| Map (F1.7) | Nearby matches by radius (PostGIS), including drafts; private matches never listed | `matches/nearby` |
| Address search | Proxy to Google Places or Photon, restricted to Brazil | `places/autocomplete`, `places/{id}` |
| Profile (F2.1) | Name, surname, unique `@handle`, birth date, single position, modality, declared level, derived skills (ACE/BLK/ATA/DEF/GERAL), stats, match history, photo by signed URL; level number from accumulated points (attendance +10, win +15, MVP +25; 10 × n × (n − 1) points for level n) | `profiles/me`, `profiles/handle-availability`, `profiles/me/match-history`, `profiles/me/photo/upload-url` |
| Player card (F2.2) | Card data after 3 matches; premium flag is a stub that always answers `false` | `profiles/me/card` |
| Group ranking (F2.3) | Points per recurring match: attendance +10, win +15, MVP +25 | `matches/{id}/ranking`, `rankings/mine` |
| Season ranking | Points from recurring matches played this month (Brasília), among me and who I follow, inside one recurring group, or general (3+ games); optional court position filter; ties share a rank (DECISIONS #63) | `rankings` (`scope`, `groupId`, `position`), `rankings/groups` |
| Feedback | Stores the app's feedback form, 20 per user per day | `POST feedback` |
| Rede: feed | A post is a text and/or one photo, optionally tied to a finished match the author played or inviting to an open match still to come (organizer or confirmed player); likes; comments; the organizer of an open match still to come can share it as a card with no text (nothing is posted automatically); everyone's posts or only those of the players the caller follows; 20 posts per user per day | `GET posts` (`scope=following`), `POST posts`, `DELETE posts/{id}`, `PUT / DELETE posts/{id}/like`, `GET / POST posts/{id}/comments`, `DELETE posts/{id}/comments/{commentId}` |
| Rede: follows | One player follows another, with no approval; followers and following counts | `GET / PUT / DELETE profiles/{userId}/follow` |
| Friends' activity | What the players I follow did (started or finished an open match, MVP, new level number), recorded from the game events, 30 days, grouped "started", sharing switch on by default (DECISIONS #57 to #60) | `GET activities`, `PUT profiles/me/activity-sharing` |

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
- **Summary of a game with rotating teams** does not record which pair played each set
  (DECISIONS #30).
- **Per-player stats**: the score is recorded by team; the app shows zeros in "Meu desempenho".
  (Level number and points exist since DECISIONS #57.)
- **Event retry**: a failed in-process handler is logged and not retried (no outbox).
- **Cleanup**: the `Aws:Sqs:*` settings are still required at startup although nothing is
  sent; the Redis packages and the `Quadra.Workers.*` projects are unused.

## Out of the MVP

Human rulings (do not implement without a new decision):

- **Recurring match = one game** (2026-07-03, confirmed 2026-10-06). A group is the single
  recurring `matches` row and no future occurrences are generated. So a ranking holds the
  points of one game, and the following stay out until an occurrence model exists: the
  3-match streak bonus (+20) and any multi-week accumulation. Do not ship dormant streak code.
- **`player_xp` lifetime accumulator**: cut (2026-07-03); it only served the city ranking. The
  level number (DECISIONS #57) is derived from the stats instead, with no new table.
- **One-off matches earn no ranking points**; the ranking endpoint answers `409` for them.
- **Premium**: no table or column; only the `IPremiumStatusReader` stub. No billing.
- **+5 "first match as DropIn" bonus**: not in the MVP.
- **Earning `Advanced` / `Elite` by playing**, the `frequency` stat and skill ratings that
  grow with play: deferred until their formulas are defined.
- **Separate signup** (`POST auth/signup`), passwords, account linking between providers.

Rede (social feed, in scope since 2026-10-09, DECISIONS #40 to #49): each next step waits for
the owner's approval. Not built yet: followers / following lists, friendship with request,
blocking, a "moved up in the ranking" card, a match's posts, several photos,
video, posts on a profile, reporting a post, notifications for likes, comments and follows.

Never part of the MVP (Layer 3):

- Courts and arenas (venues, photos, reviews), contacts sync
- Player search, player rating, city ranking
- Achievements and badges, per-play stats input (serve, block, attack, defense)
- Payments, cost splitting, microtransactions, organizer premium plan
- Calendar, Instagram or WhatsApp integration; routes and directions
- More than 4 teams, balancing by position, partial confirmation, player-to-player substitution
