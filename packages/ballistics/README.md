# @svs/ballistics

Pure TypeScript external-ballistics solver for Sniper vs Sniper. No DOM, no runtime dependencies,
no globals, no clock, no `Math.random`: `simulate(shot, environment, options)` is a pure function,
so the server's hit verdict and the client's preview/killcam replay the same flight from `{inputs, seed}`.

## Model

- 3-DOF point mass, RK4 at a fixed 0.5 ms step (semi-implicit Euler available for comparison).
- Drag from the standard G1/G7 Cd-vs-Mach tables, linearly interpolated, scaled by the ballistic coefficient.
- Wind enters only through velocity relative to the air, sampled per step from a layered field
  (zones along the lane) with seeded gusts over match time. Wind is never a sideways push.
- Air density from temperature, station pressure and humidity (CIPM-2007), varying with height along
  the path; speed of sound from temperature, so Mach shifts with weather.
- Litz spin drift (from the Miller stability factor), Litz aerodynamic jump (flag), Coriolis and
  Eötvös (flag, "full realism"), seeded transonic dispersion below Mach 1.2 (flag).
- Launch: seeded muzzle-velocity σ, sight height over bore, turret/hold dial, rifle cant (rotates the
  dial into lateral error), uphill/downhill line of sight.

## API

```ts
import { ICAO, solveZero, simulate, pointAtRange, dopeTable, solveFiringSolution, uniformWind } from '@svs/ballistics';

const rifle = { projectile, muzzleVelocity: 790, sightHeight: 0.045, twist: 0.2858 };
const env = { atmosphere: ICAO, wind: uniformWind(4, 90) };
const shot = { ...rifle, zeroElevation: solveZero(rifle, env, 100) };

dopeTable(shot, env, [300, 600, 900]);            // drop/drift corrections in mrad
solveFiringSolution(shot, env, 900);               // dial that hits the point of aim
simulate({ ...shot, dialElevation: 0.0112 }, env, { maxRange: 1000, seed: 1234, fireTime: 12.5 });
```

Shot frame: origin at the scope, x downrange, y up, z to the shooter's right. Angles are radians,
everything else is SI.

## Validation (Phase 0 exit gate)

`test/validation.test.ts` checks drop, wind drift, velocity and time of flight every 100 m against
reference tables from [py-ballisticcalc](https://github.com/o-murphy/py-ballisticcalc) 3.1.0, a
published open-source point-mass solver:

- .308 M118LR (175 gr SMK, G7 0.243) and .338 LM (250 gr Scenar, G7 0.322) to 1,000 m / 1,200 m,
  in ICAO and hot/high (1,200 m, 35 °C) air, plus a G1 table, spin drift and Coriolis cases.
- Tolerance is 1% (5 mm floor near the zero range). Actual agreement is better than 0.1% everywhere.

The tables are committed in `reference/reference-tables.json`; `reference/generate.py` regenerates
them. JBM's online calculator could not be reached from the build environment, so the reference is
py-ballisticcalc rather than JBM; both implement the same standard model.

Note: the plan's sanity figure of "9-10 mrad at 1,000 m" for .308 is closer to the 800 m value.
Published M118LR dope from a 100 m zero is about 14 mrad at 1,000 m, which is what this solver gives
(see `test/physics.test.ts`).

## Integrator benchmark

`pnpm --filter @svs/ballistics bench` (Node 22, one .308 shot to 1,000 m):

| Integrator | Step | Time per shot | Error at 1,000 m |
|---|---|---|---|
| RK4 | 0.5 ms | ~2.5 ms | < 0.001 mm |
| RK4 | 2 ms | ~0.9 ms | 0.005 mm |
| Semi-implicit Euler | 0.5 ms | ~1.6 ms | 14 mm |
| Semi-implicit Euler | 0.1 ms | ~11 ms | 2.7 mm |

RK4 is the default: far more accurate for the same cost.
