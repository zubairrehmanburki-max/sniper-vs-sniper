# @svs/server

Authoritative game server for online duels, built on Colyseus (`@colyseus/core` with the WebSocket
transport). There is one room type, `duel`. Rooms are matched by private code and map. An empty code means quick match, which only pairs players who picked the same map. Someone joining a private code plays the host's map.

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

Ratings:
- Quick match is ranked and private games are not. Ratings are Elo, starting at 1,200. K is 40 for
  a player's first 10 games and 24 after that (`packages/shared/src/elo.ts`). A forfeit counts as a
  loss.
- Until there are accounts, players are identified by a random id each browser keeps in
  localStorage. That makes ratings easy to reset or spoof, which is fine for playtesting but not
  for a public ladder.
- Ratings are stored in a JSON file named by `RATINGS_FILE`, or in memory if it isn't set.
  `GET /rating/:id` returns one player's rating.

Not here yet:
- Accounts, Postgres match history and Redis matchmaking. Matchmaking is in-memory on one process
  and doesn't yet band players by rating, which is enough for a single small instance.
- Hosting. The `Dockerfile` at the repo root builds one image that serves the client and runs the
  server (`CLIENT_DIR`, port 8080). Where it gets hosted is still to be decided.
