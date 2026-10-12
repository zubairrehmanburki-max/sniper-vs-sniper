# Sniper vs Sniper

A 1v1 turn-based sniper duel where every shot is a solved physics problem. The full design and build
plan is in [docs/GAME_PLAN.md](docs/GAME_PLAN.md).

## Layout

```
config/game.json       every gameplay-visible number (rifles, BCs, velocities, thresholds)
packages/ballistics    pure TypeScript ballistics solver + validation tests
```

## Develop

Requires Node 20+ and pnpm.

```sh
pnpm install
pnpm test        # all packages
pnpm typecheck
```

## Progress

- [x] Phase 0: ballistics solver, validated against published dope tables within 1% to 1,000 m
- [ ] Phase 1: practice range
- [ ] Phase 2: turn system, hotseat, PvE bot
- [ ] Phase 3: online play
- [ ] Phase 4: content and soft launch
- [ ] Phase 5: mobile wrap
