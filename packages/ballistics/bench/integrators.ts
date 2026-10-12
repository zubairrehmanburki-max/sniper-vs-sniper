/** Benchmarks RK4 against semi-implicit Euler (plan: "benchmark both"). Run: pnpm --filter @svs/ballistics bench */
import { ICAO, GRAIN, INCH, pointAtRange, simulate, solveZero, type Shot } from '../src/index.ts';

const base = {
  projectile: { dragModel: 'G7' as const, bc: 0.243, massKg: 175 * GRAIN, diameterM: 0.308 * INCH, lengthM: 1.24 * INCH },
  muzzleVelocity: 792.5,
  sightHeight: 0.045,
  twist: 11.25 * INCH,
};
const env = { atmosphere: ICAO };
const shot: Shot = { ...base, zeroElevation: solveZero(base, env, 100) };
const truth = pointAtRange(simulate(shot, env, { maxRange: 1000, dt: 0.00005 }), 1000)!.y;

for (const [integrator, dt] of [
  ['rk4', 0.002],
  ['rk4', 0.001],
  ['rk4', 0.0005],
  ['euler', 0.001],
  ['euler', 0.0005],
  ['euler', 0.0001],
] as const) {
  const n = 50;
  const start = performance.now();
  let y = 0;
  for (let i = 0; i < n; i++) y = pointAtRange(simulate(shot, env, { maxRange: 1000, dt, integrator }), 1000)!.y;
  const ms = (performance.now() - start) / n;
  console.log(`${integrator.padEnd(5)} dt=${(dt * 1000).toFixed(2)} ms  ${ms.toFixed(2)} ms/shot  error at 1000 m: ${((y - truth) * 1000).toFixed(3)} mm`);
}
