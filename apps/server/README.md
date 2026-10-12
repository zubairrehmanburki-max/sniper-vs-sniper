# @svs/server

Authoritative game server for online duels, built on Colyseus (`@colyseus/core` with the WebSocket
transport). There is one room type, `duel`, matched by private code; an empty code is quick match.

```sh
pnpm server            # from the repo root: ws://localhost:2567 (PORT overrides the port)
pnpm --filter @svs/server test
```

How a room works:

- Two clients join. The room creates the match with a seed that never leaves the server.
- Clients send intents (`place`, `ready`, `fire`, `forfeit`). The room stamps each one with its own
  clock and runs it through the same `@svs/shared` reducer that hotseat and bot games use. It also
  advances the phase timers and starts the next round after a short break.
- Each player gets only `viewFor(state, side)`. That view has no seed, and it includes the enemy's
  position and stance only when that player could see them: in their own shot phase, with line of
  sight to the enemy's head or chest. A position revealed by a shot is always included. Shot
  results go to both players, with the trajectory thinned for the killcam.
- A dropped connection keeps its seat for 60 s (`rules.reconnectGraceSeconds`). The client SDK
  retries on its own, and a reloaded tab resumes from a token kept in sessionStorage. Quitting, or
  running out the grace period, forfeits the match. Idling through two phases forfeits it too.

Not here yet:
- Accounts, ELO, Postgres match history and Redis matchmaking. Matchmaking is in-memory on one
  process, which is enough for a single small instance.
- Deployment: a Fly.io or Render config.
