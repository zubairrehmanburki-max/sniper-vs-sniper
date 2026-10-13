/**
 * Writes ballistics.json: outputs of the TypeScript solver that the C# port must reproduce exactly
 * (RNG and gusts bit-for-bit, trajectories to floating-point noise). Run from the repo root:
 *   pnpm tsx unity/CoreTests/Golden/ballistics.gen.ts
 */
import { writeFileSync } from 'node:fs';
import { createRng, gustFactor, hashSeed, pointAtRange, simulate, solveFiringSolution, solveZero, valueNoise1D, GRAIN, INCH, type Environment, type Shot } from '../../../packages/ballistics/src/index.ts';

const rng = createRng(hashSeed(1234, 7));
const rngSeq = Array.from({ length: 8 }, () => rng.next());
const normals = Array.from({ length: 6 }, () => rng.normal());
const hashes = [hashSeed(0), hashSeed(1, 2, 3), hashSeed(4294967295, -5), hashSeed(0x6d76, 99)];
const noise = [0, 0.37, 1.5, -2.25, 17.9].map((t) => valueNoise1D(42, t));
const gusts = [0, 3.3, 10, 41.7].map((t) => gustFactor({ amplitude: 0.3, period: 4, seed: 77 }, t));

const projectile = { dragModel: 'G7' as const, bc: 0.322, massKg: 250 * GRAIN, diameterM: 0.338 * INCH, lengthM: 1.555 * INCH };
const base = { projectile, muzzleVelocity: 900, muzzleVelocitySd: 4, sightHeight: 0.045, twist: 10 * INCH };
const env: Environment = {
  atmosphere: { altitudeM: 300, pressureHPa: 980, temperatureC: 22, humidity: 0.6 },
  wind: {
    zones: [
      { untilRange: 300, vector: { x: 1, y: 0, z: 3 } },
      { untilRange: 700, vector: { x: -0.5, y: 0.2, z: 5.5 } },
      { untilRange: Infinity, vector: { x: 0, y: 0, z: 2 } },
    ],
    gust: { amplitude: 0.25, period: 5, seed: 99 },
  },
};
const zero = solveZero(base, env, 100);
const shot: Shot = { ...base, zeroElevation: zero, losElevation: 0.03, cant: 0.02, dialElevation: 0.0042, dialWindage: 0.0011 };
const traj = simulate(shot, env, {
  maxRange: 1300,
  seed: 31337,
  fireTime: 12.5,
  effects: { spinDrift: true, aerodynamicJump: true, transonicDispersion: true },
});
const samples = [100, 400, 640, 1000, 1300].map((r) => ({ range: r, ...pointAtRange(traj, r)! }));
const solution = solveFiringSolution({ ...shot, dialElevation: 0, dialWindage: 0 }, env, 640);

writeFileSync(
  new URL('./ballistics.json', import.meta.url),
  JSON.stringify({ rngSeq, normals, hashes, noise, gusts, zero, muzzleVelocity: traj.muzzleVelocity, aerodynamicJump: traj.aerodynamicJump, stability: traj.stability, samples, solution }, null, 1) + '\n',
);
console.log('wrote ballistics.json', samples.length, 'samples');
