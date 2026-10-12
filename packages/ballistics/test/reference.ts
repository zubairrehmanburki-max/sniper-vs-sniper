import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Atmosphere, Environment, Projectile, Shot } from '../src/index.ts';
import { GRAIN, INCH, uniformWind, windFrom } from '../src/index.ts';

interface RefLoad {
  table: 'G1' | 'G7';
  bc: number;
  grains: number;
  diameter_in: number;
  length_in: number;
  mv: number;
  twist_in: number;
}

export interface RefRow {
  range: number;
  time: number;
  velocity: number;
  height: number;
  windage: number;
}

export interface RefScenario {
  name: string;
  load: string;
  atmosphere: string;
  spinDrift: boolean;
  coriolis: { latitude: number; azimuth: number } | null;
  zeroElevationMrad: number;
  rows: RefRow[];
}

interface RefFile {
  generator: string;
  sightHeightMm: number;
  zeroRangeM: number;
  crosswindMps: number;
  loads: Record<string, RefLoad>;
  atmospheres: Record<string, Atmosphere>;
  scenarios: RefScenario[];
}

export const reference: RefFile = JSON.parse(
  readFileSync(fileURLToPath(new URL('../reference/reference-tables.json', import.meta.url)), 'utf8'),
);

export function projectileFor(load: string): Projectile {
  const l = reference.loads[load]!;
  return {
    dragModel: l.table,
    bc: l.bc,
    massKg: l.grains * GRAIN,
    diameterM: l.diameter_in * INCH,
    lengthM: l.length_in * INCH,
  };
}

/** Shot and environment for a reference scenario, zero elevation left for the caller to solve. */
export function setupFor(s: RefScenario): { shot: Omit<Shot, 'zeroElevation'>; env: Environment } {
  const l = reference.loads[s.load]!;
  const shot: Omit<Shot, 'zeroElevation'> = {
    projectile: projectileFor(s.load),
    muzzleVelocity: l.mv,
    sightHeight: reference.sightHeightMm / 1000,
    twist: s.spinDrift ? l.twist_in * INCH : 0,
    azimuth: s.coriolis ? (s.coriolis.azimuth * Math.PI) / 180 : undefined,
  };
  const env: Environment = {
    atmosphere: reference.atmospheres[s.atmosphere]!,
    wind: uniformWind(reference.crosswindMps, 90),
    latitude: s.coriolis?.latitude,
  };
  return { shot, env };
}

export { windFrom };
