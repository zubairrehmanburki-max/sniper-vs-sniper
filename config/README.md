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
