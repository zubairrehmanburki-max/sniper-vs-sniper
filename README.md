# Sniper vs Sniper

A 1v1 turn-based sniper duel where every shot is a solved physics problem. The full design and build
plan is in [docs/GAME_PLAN.md](docs/GAME_PLAN.md).

## Layout

```
config/game.json       every gameplay-visible number (rifles, BCs, velocities, thresholds)
packages/ballistics    pure TypeScript ballistics solver + validation tests
packages/shared        game rules shared by client and server (match state machine, hits, maps, bot, protocol)
apps/client            Three.js browser client (practice range, hotseat, vs bot, online)
apps/server            Colyseus game server: authoritative duel rooms
```

## Develop

Requires Node 20+ and pnpm.

```sh
pnpm install
pnpm dev         # client at http://localhost:5173
pnpm server      # game server on ws://localhost:2567 (needed for online play only)
pnpm test        # all packages
pnpm typecheck
```

## Progress

- [x] Phase 0: ballistics solver, validated against published dope tables within 1% to 1,000 m
- [x] Phase 1: practice range (steel at 300/600/900 m, full scope UX, live wind cues)
- [x] Phase 2: turn system, hotseat, PvE bot
- [x] Phase 3: online play (quick match, private codes, reconnection)
- [ ] Phase 4: content and soft launch
  - [x] Dry Valley, Harbor Rooftops and Alpine Ridge maps, with a map picker
  - [x] Killcam
  - [x] Ranked quick match with Elo ratings
  - [x] Soft cover a big enough round can punch through
  - [ ] Hosting and soft launch
- [ ] Phase 5: mobile wrap
  - [x] Capacitor iOS and Android shells, icons and splash, haptics
  - [x] CI builds a debug Android APK
  - [ ] Store submission (needs Apple and Google developer accounts)
- [ ] Phase 6: polish
  - [x] Fixed-position maps: Twin Bridges, Watchtowers, Oil Rigs (default), with no move phases
  - [x] 3D look: photographic HDRI skies lighting everything, reflective water, real grass/concrete/asphalt textures, a rigged soldier model posed per stance, painted tree billboards, bloom and scope post-processing
