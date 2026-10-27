# SCOPE.md — Quadra API

State of the MVP on the `dev` branch, as of 2026-10-06. Reasons for each choice are in
`docs/DECISIONS.md`. The per-feature IN/OUT contracts this file used to hold are in
`docs/archive/SCOPE-2026-10-06-contratos-por-feature.md`.

Glossary: Partida = Match · Organizador = Organizer · Mensalista = Regular · Avulso = DropIn ·
Carta do Jogador = PlayerCard · Janela de Confirmação = ConfirmationWindow.

## Done

| Area | What exists | Main routes (`/api/v1`) |
| --- | --- | --- |
| Auth (FA.1, FA.3) | SMS OTP (6 digits) and Google login; the first valid login creates the user; own JWT + rotating refresh token; logout ends the session; export and delete the account (LGPD, DECISIONS #69) | `auth/login/sms-otp`, `auth/login/google`, `auth/refresh`, `auth/logout`, `auth/me`, `auth/me/export`, `DELETE auth/me` |
| Match creation (F1.1) | Recurring or one-off match with format, level, duration, prices, visibility (open / private by code / private by guests), confirmations open from creation (an explicit window or "opens N hours before" still accepted); optional cover photo (key from the profile-photo upload, read URL in the detail) | `POST matches`, `GET matches/mine`, `GET matches/{id}/detail` |
| Edit and cancel | The organizer edits the details (not type nor visibility) and cancels until the game starts; slots never below the confirmed players | `PUT matches/{id}`, `DELETE matches/{id}` |
| Next game of a recurring group | Generating a recurring game's summary creates the group's next game (next week day by frequency, same settings and invite code, same window distance), with everyone who had confirmed as Pending (DECISIONS #64); cancelling one creates it empty, and `endGroup=true` ends the group (DECISIONS #89) | `POST matches/{id}/summary`, `DELETE matches/{id}` |
| Presence (F1.2) | Confirm / decline, self-enrollment by confirming, Regular vs DropIn slots, waiting list, guests without an account, window opened and closed by the clock | `PUT matches/{id}/presences/me`, `matches/{id}/guests` |
| Teams (F1.3) | 2 to 4 teams, balanced or random draw, guests drawn in, manual adjustment | `matches/{id}/teams/draft`, `PUT matches/{id}/teams/{teamId}/members/{playerId}` |
| Scoreboard (F1.4) | Sets best of 3 or 5, pair of teams per set (rotation), end set early, undo last point, live updates over SignalR; a match without fixed teams (`freeTeams`, format `Free`) only starts and ends, with one roster of everyone confirmed (DECISIONS #86) | `matches/{id}/scoreboard/…`, hub `/hubs/match` |
| Player stats | The organizer records each player's cortadas, blocks, aces, salvadas and assistências, from the start of the game until the summary; participants read them (DECISIONS #84) | `GET/PUT matches/{id}/player-stats` |
| MVP vote (F1.5) | One vote each (who played, plus the organizer even without playing), no self-vote, opens when the game ends, 24 h deadline | `matches/{id}/mvp-voting/…` |
| Summary (F1.6) | Immutable summary; generating it is what records stats, history and ranking points | `matches/{id}/summary` |
| Map (F1.7) | Nearby matches by radius (PostGIS), including drafts; private matches never listed | `matches/nearby` |
| Arenas on the map | Arenas found on Google Places around the map, each a court, sand court or park (DECISIONS #87) (only the place id and coordinates kept, for up to 29 days), each with today's public games and the one being played now; name and address on tap via `places/{id}`. Partner showcase not done | `venues/nearby` |
| Address search | Proxy to Google Places or Photon, restricted to Brazil | `places/autocomplete`, `places/{id}` |
| Profile (F2.1) | Name, surname, unique `@handle`, birth date, single position, modality, declared level, derived skills (ACE/BLK/ATK/DEF/GERAL, moved by the recorded actions: DECISIONS #85), recorded actions (total and per match), stats, match history, photo by signed URL; level number from accumulated points (attendance +10, win +15, MVP +25; 10 × n × (n − 1) points for level n) | `profiles/me`, `profiles/handle-availability`, `profiles/me/match-history`, `profiles/me/photo/upload-url` |
| Player card (F2.2) | Card data after 3 matches; premium flag is a stub that always answers `false` | `profiles/me/card` |
| Card style | Finish, identity title and card photo chosen by the player, shown to everyone who sees the profile | `PUT profiles/me/card-style`, `cardStyle` on `profiles/me` and `profiles/{id}` |
| First-access tour | How the player left the app's tour (version, completed or skipped, last step seen, when); only the owner reads it (DECISIONS #97) | `PUT profiles/me/tour`, `tour` on `profiles/me` |
| Group ranking (F2.3) | Points per recurring match: attendance +10, +8 per game won (max 3), +4 per MVP vote, MVP +20 with 3+ votes, adding up over every game of the group (DECISIONS #64, #95) | `matches/{id}/ranking`, `rankings/mine` |
| Season ranking | Points from recurring matches played in a month (Brasília; any past month by `season`), among me and my network (who I follow and who follows me), inside one recurring group, or general (3+ games); optional court position filter; order by points, MVPs or games won; points split by reason, rank of 7 days ago, who to pass next; what one match gave me (DECISIONS #63, #95) | `rankings` (`scope`, `groupId`, `position`, `season`, `sort`), `rankings/groups`, `rankings/matches/{id}/me` |
| Feedback | Stores the app's feedback form, 20 per user per day | `POST feedback` |
| Update gate | Minimum and recommended app version from configuration (DECISIONS #74) | `GET app/version` |
| Notifications | In-app list, unread counter, mark read, clear one or all (DECISIONS #96); push through Expo Push by preference; sent for a waiting list spot, the reminder of a match within 24 h (confirm / see you), MVP voting open, summary ready (DECISIONS #77) | `notifications`, `notifications/unread-count`, `notifications/{id}/read`, `notifications/read-all`, `DELETE notifications`, `DELETE notifications/{id}`, `notifications/push-token`, `notifications/preferences` |
| Rede: feed | A post is a text and/or one photo, optionally tied to a finished match the author played or inviting to an open match still to come (organizer or confirmed player); likes; comments; the organizer of an open match still to come can share it as a card with no text (nothing is posted automatically); everyone's posts or only those of the players the caller follows; 20 posts per user per day | `GET posts` (`scope=following`), `POST posts`, `DELETE posts/{id}`, `PUT / DELETE posts/{id}/like`, `GET / POST posts/{id}/comments`, `DELETE posts/{id}/comments/{commentId}` |
| Rede: follows | One player follows another, with no approval; followers and following counts | `GET / PUT / DELETE profiles/{userId}/follow` |
| Rede: search | Find players by @handle prefix only, never by name, blocks respected, rate limited (DECISIONS #75) | `GET profiles/search` |
| Rede: safety | Report a post (leaves the reporter's feed); block a player (hides posts and comments both ways, ends follows, refuses new ones); admins by account id list reports and delete posts (DECISIONS #70) | `POST posts/{id}/report`, `PUT / DELETE profiles/{userId}/block`, `GET profiles/me/blocks`, `GET admin/reports`, `DELETE admin/posts/{id}`, `DELETE admin/reports/{postId}` |
| Friends' activity | What the players I follow did (started or finished an open match, MVP, new level number), recorded from the game events, 30 days, grouped "started", sharing switch on by default (DECISIONS #57 to #60) | `GET activities`, `PUT profiles/me/activity-sharing` |

## Pending

Needs configuration only (no code):

- **Twilio Verify**. Until then the hosted API accepts the fixed code for any phone number,
  so the app must not be shared outside the team.
- **Google login**: the OAuth client IDs.
- **Apple login**: code path exists, disabled until an Apple client ID is configured.

Needs code:

- **Push on the phone** needs the app side (`expo-notifications`, waiting for Johny's OK on the package) and FCM credentials in EAS; the API already stores tokens and sends through Expo Push (DECISIONS #77).
- **Summary of a game with rotating teams** does not record which pair played each set
  (DECISIONS #30).
- **A recurring group stops** when a game is never summarized: only a summary or a cancellation
  creates the next game (DECISIONS #64, #89).
- **Event retry**: a failed in-process handler is logged and not retried (no outbox).
- **Cleanup**: the `Aws:Sqs:*` settings are still required at startup although nothing is
  sent; the `StackExchange.Redis` package and the `Quadra.Workers.*` projects are unused.

## Out of the MVP

Human rulings (do not implement without a new decision):

- **The 3-match streak bonus (+20)**: not in the MVP. Do not ship dormant streak code. (The old
  ruling "recurring match = one game" ended on 2026-10-15: DECISIONS #64.)
- **`player_xp` lifetime accumulator**: cut (2026-07-03); it only served the city ranking. The
  level number (DECISIONS #57) is derived from the stats instead, with no new table.
- **One-off matches earn no ranking points**; the ranking endpoint answers `409` for them.
- **Premium**: no table or column; only the `IPremiumStatusReader` stub. No billing.
- **+5 "first match as DropIn" bonus**: not in the MVP.
- **Earning `Advanced` / `Elite` by playing** and the `frequency` stat: deferred until their
  formulas are defined. (Skill ratings grow with recorded actions since DECISIONS #85.)
- **Separate signup** (`POST auth/signup`), passwords, account linking between providers.

Rede (social feed, in scope since 2026-10-09, DECISIONS #40 to #49): each next step waits for
the owner's approval. Not built yet: followers / following lists, friendship with request,
a "moved up in the ranking" card, a match's posts, several photos,
video, posts on a profile, reporting a comment, notifications for likes, comments and follows.

Never part of the MVP (Layer 3):

- Courts and arenas (venues, photos, reviews), contacts sync
- Player search by name (search by @handle is in, DECISIONS #75), player rating, city ranking
- Achievements and badges, per-play stats input (each rally's action as it happens; per-match
  counts are in since DECISIONS #84)
- Payments, cost splitting, microtransactions, organizer premium plan
- Calendar, Instagram or WhatsApp integration; routes and directions
- More than 4 teams, balancing by position, partial confirmation, player-to-player substitution
