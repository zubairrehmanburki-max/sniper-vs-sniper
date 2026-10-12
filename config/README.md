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
