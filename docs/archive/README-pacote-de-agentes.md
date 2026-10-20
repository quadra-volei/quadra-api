# Package: Agent Workflow for Quadra Backend

This package contains everything needed to run the 4-agent workflow in Claude Code, tuned for the Quadra backend (.NET 10, modular monolith per the AWS architecture).

## What's inside

```
.
├── CLAUDE.md                          # Permanent instructions read every session
├── README.md                          # This file
├── .claude/
│   ├── agents/
│   │   ├── spec-writer.md             # Agent 1: produces technical contract
│   │   ├── scope-guardian.md          # Agent 2: audits scope (deliberately skeptical)
│   │   ├── implementer.md             # Agent 3: writes C# code
│   │   └── test-writer.md             # Agent 4: writes and runs tests
│   ├── commands/
│   │   └── feature.md                 # /feature slash command orchestrating the 4
│   └── hooks/
│       └── pre-edit-scope-check.sh    # Optional hook for extra warnings
└── docs/
    ├── PRODUCT.md                     # Product vision summary
    ├── SCOPE.md                       # ★ MVP CONSTITUTION — feature by feature
    ├── ARCHITECTURE.md                # Technical architecture (modules, infra)
    └── WORKFLOW.md                    # How to use the system day to day
```

## How to install

1. Copy everything to the Quadra repository root
2. Confirm Claude Code recognizes the agents:
   ```bash
   ls -la .claude/agents/
   ```
3. (Optional) To enable the hook:
   ```bash
   chmod +x .claude/hooks/pre-edit-scope-check.sh
   ```
   And add the config to `.claude/settings.json` as commented in the script.

## How to use

Open Claude Code in the repo and type:

```
/feature F1.1 — Match Creation
```

The workflow will guide you. Details in `docs/WORKFLOW.md`.

## Where to customize

| When you want to... | Edit... |
| --- | --- |
| Change stack, version, code standard | `CLAUDE.md` |
| Add/remove MVP features | `docs/SCOPE.md` |
| Refine a product rule | `docs/PRODUCT.md` |
| Adjust architecture (modules, infra) | `docs/ARCHITECTURE.md` |
| Change a specific agent's behavior | `.claude/agents/<name>.md` |
| Add a new step to the flow | `.claude/commands/feature.md` |

## Domain glossary

The product was designed in Portuguese. Code and docs use English. The mapping:

| Portuguese (product) | English (code) |
| --- | --- |
| Jogo / Partida | Match |
| Organizador | Organizer |
| Mensalista | Regular |
| Avulso | DropIn |
| Carta do Jogador | PlayerCard |
| Janela de Confirmação | ConfirmationWindow |

The app name **Quadra** is kept as-is (proper noun).

## Core principle

> **Hallucination drops drastically when the agent doesn't have to "remember" things — it has to read them.**

That's why the `.md` files in this package are the system's external memory. Keeping them updated matters more than sophisticated prompt engineering.
