# PRODUCT.md — Quadra

> Product vision summary.

## What it is

Mobile app for volleyball players that centralizes:
1. Organization of recurring and one-off matches
2. Data-driven gamification (levels, points, ranking, player card)
3. Player community (nearby matches map)

## User profiles (MVP)

- **Organizer**: creates and manages recurring or one-off matches
- **Regular** (`Mensalista`): fixed player in a recurring match, with priority on confirmation
- **DropIn** (`Avulso`): player without fixed bond, fills remaining slots (secondary feature in MVP)

## Pains the MVP solves

- Chaotic confirmation via WhatsApp
- Unbalanced teams formed without criteria
- No stat tracking (now: the organizer's stats sheet, ratings that follow it)
- No way to discover open nearby matches
- No progression / identity for amateur players

## Level system

A player declares a level at onboarding (`Beginner | Intermediate | Advanced`). It is then
recalculated from recorded matches and never drops below the declared one.

| Level | Criteria | MVP status |
| --- | --- | --- |
| Beginner | < 10 matches | Active |
| Intermediate | 10+ matches and ≥ 1 MVP received | Active |
| Advanced | 30+ matches and > 60% vote average | Only by declaring it; earning it is deferred ("vote average" is undefined) |
| Elite | 50+ matches, 10+ MVPs, top 10% of global ranking | Deferred (depends on a global ranking, out of the MVP) |

Besides that band, a player has a **level number** from accumulated points (below): reaching
level n takes 10 × n × (n − 1) points (level 2 = 20, level 5 = 200, level 16 = 2400). It only
goes up and is shown next to the player in the friends' activity.

## Matches and teams

A match is the day's session: the organizer starts it, it runs game after game, and only the
organizer ends it. The organizer chooses at creation (and can change it until the game starts):

- **Times fixos** (default): on the day the organizer draws 2 to 4 teams (random or balanced) and
  adjusts them by hand. Each game is to the points chosen at creation; with more than two teams
  the organizer picks who plays each game. Teams are named and colored from the brand palette, in
  this order: Time Azul (#1A1AFF), Time Limão (#AADD00), Time Laranja (#FF6B00), Time Roxo
  (#6B1AFF); tight spots use the short form (Azul, Limão…).
- **Sem times fixos**: teams form and change on court. The players are one list of confirmed
  players; the match is only started and ended, with no score and no winner. MVP vote, summary
  and stats work as usual; in the history the result is "Sem placar". The teams screen also offers
  "Jogar sem times fixos" next to "Sortear times", for any match.

## Player stats and ratings

**What is recorded.** Per player and match, five counts: **cortadas**, **blocks**, **aces**,
**salvadas** and **assistências**. Only the organizer fills the match's stats sheet, from the
start of the game until the summary is generated; then it is fixed. Players never record their own
numbers. A player left out of the sheet, or an action nobody in the match has, counts as **not
recorded** — never as zero. No attempts or errors are recorded, so there are no efficiency rates.
Works the same in one-off and recurring matches (each game of a group has its own sheet).

**Ratings.** Every player has ATK, DEF, BLK and ACE (1–99) and GERAL:

- They start at a baseline from what was declared at onboarding: 50 / 60 / 70 by level, plus a
  per-position adjustment. With nothing recorded, that is all.
- Each rating then follows a **rate per standard game** (6x6, about 45 points played): ATK from
  cortadas + half the assistências (the point starts at the set), DEF from salvadas, BLK from
  blocks, ACE from aces. Games are counted from the points the player's team was on court for (or,
  with no fixed teams, the planned time); 2x2 counts 3×, 4x4 1.5×, because each player touches the
  ball more.
- The rate becomes a value on a curve: 40 at zero, 75 at a good amateur rate, never above 99. The
  rating is the average of that value and the baseline, the baseline weighing as 12 games and a
  single match at most 6 games: one great match moves a rating at most a third of the way, and
  playing more only makes the rate weigh more — it never raises a rating by itself.
- **GERAL** = the baseline average + how much each rating moved, weighted by position (a libero's
  DEF, a middle's BLK, a setter's ATK with the assists…), so a setter or a libero is not dragged
  down by attacking or blocking little.

The volume (total of each action) and the average per match show on the profile and the card;
GERAL shows on the card, the profile and the ranking. Every number, weight and limit, with worked
examples: `docs/DECISIONS.md` #84 to #86 and `PlayerSkillCalculator`.

## Point system

- Ranking points per recurring match (DECISIONS #95): attendance +10, +8 per game won (at most 3 counted), +4 per MVP vote received, MVP of the day +20 (only with 3+ votes). Stats from the sheet do not count.
- The level number (DECISIONS #57) still uses attendance +10, win +15, MVP +25 over the whole history.
- 3 consecutive matches streak: bonus +20 — **not in the MVP** (see `SCOPE.md`)
- First match as DropIn in a new group: +5 — **not in the MVP**

## Monetization (validation only, no implementation in MVP)

Freemium with premium plan ~R$9.90/month. Billing is NOT in the MVP. The free/premium distinction exists only as a stub interface that always answers "free".
