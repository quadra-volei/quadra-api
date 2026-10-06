# PRODUCT.md — Quadra

> Product vision summary. For full detail, see the product document v1.0.

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
- No stat tracking
- No way to discover open nearby matches
- No progression / identity for amateur players

## Level system

| Level | Criteria | MVP status |
| --- | --- | --- |
| Beginner | < 10 matches | **Active** |
| Intermediate | 10+ matches and ≥ 1 MVP received | **Active** |
| Advanced | 30+ matches and > 60% vote average | **Deferred** — "vote average" is undefined and no event carries per-player MVP votes received. Not computed until the metric and its data source are defined. |
| Elite | 50+ matches, 10+ MVPs, top 10% of global ranking | **Deferred** — depends on a city-wide/global ranking, which is Layer 3 (out of MVP). Not computed until Layer 3 ranking exists. |

> **MVP scope (F2.1):** only **Beginner** and **Intermediate** are auto-calculated. A player who exceeds the Advanced/Elite thresholds stays labelled `Intermediate` until those tiers are activated. See `docs/specs/F2.1-player-profile.md`.

## Point system

- Confirmed attendance + showed up: +10
- Win: +15
- Voted MVP: +25
- 3 consecutive matches streak: bonus +20
- First match as DropIn in a new group: +5

## Monetization (validation only, no implementation in MVP)

Freemium with premium plan ~R$9.90/month. Billing implementation is NOT in the MVP — only the data structure that allows distinguishing free from premium users is prepared.
