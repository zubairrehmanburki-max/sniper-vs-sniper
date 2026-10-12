/** The practice range: one lane, steel silhouettes at fixed distances, seeded layered wind. */
import { createRng, hashSeed, type Atmosphere, type Vec3 } from '@svs/ballistics';
import { atmosphereFor, config, type GameConfig } from './config.ts';
import type { Silhouette } from './hits.ts';
import type { LaneWind } from './world.ts';

export interface PracticeRange {
  seed: number;
  targets: Silhouette[];
  wind: LaneWind;
  atmosphere: Atmosphere;
  latitude: number;
}

/**
 * Builds the range for a seed. The shooter sits at the origin looking north (−z); targets are
 * spread a little left and right so each needs its own windage and a real aim change.
 */
export function createPracticeRange(seed: number, cfg: GameConfig = config): PracticeRange {
  const pr = cfg.practiceRange;
  const rng = createRng(hashSeed(seed, 0x7067));
  const t = cfg.target;
  const targets: Silhouette[] = pr.targetsM.map((d, i) => ({
    id: `steel-${d}`,
    position: { x: (i % 2 === 0 ? -1 : 1) * (4 + 6 * i), y: 0, z: -d },
    facingYaw: Math.PI,
    width: t.widthM,
    height: t.heightM,
    head: t.headM,
  }));
  const [lo, hi] = pr.wind.baseSpeedMps;
  const baseSpeed = lo + (hi - lo) * rng.next();
  // Mostly a crosswind (the skill test), from either side.
  const side = rng.next() < 0.5 ? -1 : 1;
  const baseDir = side * (Math.PI / 2) + (rng.next() * 2 - 1) * (pr.wind.directionJitterDeg * Math.PI) / 180;
  const zones: Vec3[] = [];
  for (let i = 0; i <= pr.windZonesM.length; i++) {
    const speed = baseSpeed * (1 + (rng.next() * 2 - 1) * pr.wind.zoneSpeedJitter);
    const dir = baseDir + (rng.next() * 2 - 1) * 0.25;
    zones.push(windBlowingToward(speed, dir));
  }
  return {
    seed,
    targets,
    wind: {
      start: { x: 0, y: 0, z: 0 },
      direction: { x: 0, y: 0, z: -1 },
      edges: pr.windZonesM,
      zones,
      gust: { amplitude: pr.wind.gustAmplitude, period: pr.wind.gustPeriodSeconds, seed: hashSeed(seed, 0x67) },
    },
    atmosphere: atmosphereFor(pr.atmosphere),
    latitude: pr.latitude,
  };
}

/**
 * World wind vector for air moving toward compass-like direction `dir` measured from north (−z)
 * toward east (+x). E.g. dir = π/2 blows toward +x (a crosswind from the shooter's left).
 */
export function windBlowingToward(speed: number, dir: number): Vec3 {
  return { x: speed * Math.sin(dir), y: 0, z: -speed * Math.cos(dir) };
}
