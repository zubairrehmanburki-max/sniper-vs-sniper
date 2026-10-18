# Game config

`game.json` is the single home for every gameplay-visible number (ballistic coefficients, velocities,
thresholds, timers). Code reads it; nothing tunable is hard-coded.

## Rifle data sources and deviations from the plan

The plan's rifle table gives approximate figures. Where a published value exists it wins:

| Rifle | Plan | Config | Source |
|---|---|---|---|
| .308 175 gr | G7 ~0.26, 790 m/s | G7 0.243, 790 m/s | Litz-measured G7 BC for the 175 gr Sierra MatchKing (M118LR) |
| 6.5 CM 140 gr | G7 ~0.31, 825 m/s | G7 0.315, 825 m/s | Hornady 140 gr ELD-M, mid-range of published G7 figures |
| .338 LM 250 gr | G7 ~0.32, 900 m/s | G7 0.322, 900 m/s | Lapua 250 gr Scenar |
| .50 BMG 660 gr | G7-equiv ~0.36, 850 m/s | unchanged | Plan value (no single published G7 for 660 gr ball) |

Bullet lengths and twist rates (needed for spin drift) are typical published values for each bullet.
`coldBoreShiftMrad` values are placeholders to be tuned in playtest.

## Rule deviations from the plan

- `rules.chestKillEnergyJ` is 1,500 J, not the plan's 2,500 J. A 175 gr .308 carries about 2,200 J
  at 300 m and 1,300 J at 600 m, so 2,500 J would have made every .308 chest hit a wound. At 1,500 J
  the .308 and 6.5 CM kill with chest hits to roughly 500 m, the .338 to about 1,300 m and the .50
  beyond 1,500 m, which keeps the rifles distinct.
- The frozen player sees nothing of the enemy's shot phase live (the plan's recommendation); the
  result is shown to both players afterwards.
- Best of 3 kills stays as written until playtests say otherwise.
- Online, a round ends with an `rules.roundBreakSeconds` pause (8 s) before the next setup, instead
  of waiting for both players to tap Continue. A dropped player's seat is held for
  `rules.reconnectGraceSeconds` (60 s, as in the plan); the phase clock keeps running meanwhile, so
  the idle-forfeit rule still applies.

## Phase 4 simplifications

- **Soft cover** (`softCover`): a bullet carrying at least `minEnergyJ` (2,500 J) when it reaches a
  soft box (wood, sheet metal) goes through `maxLayers` (1) of them and keeps `energyKeep` (70%) of
  its energy, roughly the plan's ~30% loss. The path itself is not re-integrated after the exit: the
  remaining flight keeps the original trajectory and only the energy (and speed) at impact are scaled.
  At these ranges the extra drop and drift after a 1 m exit are a few centimetres. At sea level a .50
  carries enough to about 1,400 m, a .338 to about 750 m, and a .308 or 6.5 CM only inside 200 m,
  closer than any enemy zone, so in practice only the two big rifles punch through. Hard cover
  (rock, concrete, earth) stops everything.
- **Sudden death escalation** (not in the plan): after `rules.suddenDeathNoProneAfterPairs` (2)
  sudden-death pairs nobody may lie prone, and after `rules.suddenDeathStandingAfterPairs` (4) both
  players must stand. This ends the rare stalemate where both players hide fully behind cover.
  The Unity game turns this off (2026-10-17): stance is always the player's own choice.
- **Turn lock** (Unity game, 2026-10-17): you walk and change stance only in your own turn, and once
  you fire you stay on that spot in that stance until the enemy fires (`Match.CanMove`). Bots too.
- **Alpine Ridge** models thin, cold air through the map's altitude and temperature, and the shot
  angle through the real slope. Up- and down-slope thermals, and temperature changing with height
  along the path, are not modelled; the wind field is the same layered, gusty model as elsewhere.

## Fixed-position maps (Phase 6)

- A map with `fixed: true` has point zones: `clampToZone` always returns the one spot, so `place`
  can only change the stance, and the pair cycle is shot A, shot B with no move phases (`cycle()` in
  `match.ts`). Sudden-death stance escalation still applies, which is what ends a long stalemate of
  misses: both players end up standing.
- Platform tops (bridge decks, tower heads, helidecks) are part of the hit-test ground
  (`withPlatforms` in `maps.ts`), so a low shot hits the deck edge or the structure, not the water
  below it. Railings, lamp posts, cranes and the like are drawn but never hit-tested.
- The water surface is at `water` (0 m on all three maps). A shot that reaches it is reported as a
  splash; nothing floats, so there is no soft cover anywhere on these maps.
