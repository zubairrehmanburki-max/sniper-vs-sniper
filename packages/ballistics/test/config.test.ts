/** The rifles in config/game.json fly sensibly through this solver. */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { dopeTable, ICAO, solveZero, type Projectile, type Shot } from '../src/index.ts';

interface RifleConfig {
  id: string;
  dragModel: 'G1' | 'G7';
  bc: number;
  massKg: number;
  diameterM: number;
  lengthM: number;
  muzzleVelocityMps: number;
  twistM: number;
  sweetSpotM: [number, number];
}

const config = JSON.parse(
  readFileSync(fileURLToPath(new URL('../../../config/game.json', import.meta.url)), 'utf8'),
) as { ballistics: { sightHeightM: number; zeroRangeM: number }; rifles: RifleConfig[] };

describe('config rifles', () => {
  for (const rifle of config.rifles) {
    it(`${rifle.id} is still supersonic near the far end of its sweet spot and needs a sensible dial`, () => {
      const projectile: Projectile = {
        dragModel: rifle.dragModel,
        bc: rifle.bc,
        massKg: rifle.massKg,
        diameterM: rifle.diameterM,
        lengthM: rifle.lengthM,
      };
      const base = { projectile, muzzleVelocity: rifle.muzzleVelocityMps, sightHeight: config.ballistics.sightHeightM, twist: rifle.twistM };
      const env = { atmosphere: ICAO };
      const shot: Shot = { ...base, zeroElevation: solveZero(base, env, config.ballistics.zeroRangeM) };
      const far = 0.9 * rifle.sweetSpotM[1];
      const [row] = dopeTable(shot, env, [far]);
      expect(row!.mach).toBeGreaterThan(1.0);
      expect(row!.elevationMrad).toBeGreaterThan(2);
      expect(row!.elevationMrad).toBeLessThan(20);
    });
  }
});
