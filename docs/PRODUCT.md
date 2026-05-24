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

| Level | Criteria |
| --- | --- |
| Beginner | < 10 matches |
| Intermediate | 10+ matches and ≥ 1 MVP received |
| Advanced | 30+ matches and > 60% vote average |
| Elite | 50+ matches, 10+ MVPs, top 10% of global ranking |

## Point system

- Confirmed attendance + showed up: +10
- Win: +15
- Voted MVP: +25
- 3 consecutive matches streak: bonus +20
- First match as DropIn in a new group: +5

## Monetization (validation only, no implementation in MVP)

Freemium with premium plan ~R$9.90/month. Billing implementation is NOT in the MVP — only the data structure that allows distinguishing free from premium users is prepared.
