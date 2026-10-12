# @svs/client

Browser client (Three.js + Vite). Phase 1 is the practice range: steel at 300, 600 and 900 m,
full scope UX, live layered wind with grass and flag cues.

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
a perfect calculator solution on the 900 m plate).
