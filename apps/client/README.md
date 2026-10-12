# @svs/client

Browser client (Three.js + Vite). The menu offers three modes:

- **Practice range** (Phase 1): steel at 300, 600 and 900 m, full scope UX, live layered wind.
- **Hotseat duel** (Phase 2): two players on one device. A curtain hides the screen between turns
  and pauses the clock.
- **Duel vs bot** (Phase 2): easy, medium or hard. The bot gets the same physics as you; only its
  inputs (range estimate, wind read, sway timing, spotting) are noisy.

A duel runs setup, then move and shot phases alternating between players, best of 3 kills, with
sudden death after 16 pairs. The rules live in `@svs/shared` (`match.ts`) as a pure reducer, so the
online server in Phase 3 will run the same code.

```sh
pnpm dev          # from the repo root, then open the printed URL (also reachable from a phone on the LAN)
pnpm build        # static build in apps/client/dist
```

Controls: mouse or drag to aim, right-click or Z for the scope, wheel or Q/E to zoom, W/S and A/D
for elevation and windage clicks (0.1 mil), Shift to hold breath, Space or click to fire, F laser
rangefinder, C stance, B bipod, X zero turrets. On touch every control has a button.

Every shot goes through `@svs/ballistics` with the match seed (muzzle-velocity spread, gusts,
transonic dispersion), and hits are resolved by `@svs/shared` exactly as the server will later.
Adding `?debug` to the URL exposes `window.svs` for automated playtests (`svs.debugSolve(2)` dials
a perfect calculator solution on the 900 m plate; in a duel `svs.debugSolve()` aims at the enemy
and `svs.debugState()` returns a one-line state summary).
