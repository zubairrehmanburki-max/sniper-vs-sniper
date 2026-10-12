import { valueNoise1D } from './rng.ts';
import type { Vec3 } from './vec.ts';
import { DEG } from './units.ts';

/**
 * Wind in the shot frame: x = downrange (positive is a tailwind), y = up,
 * z = to the shooter's right (positive blows the bullet right).
 */
export interface WindZone {
  /** Downrange distance (m) where this zone ends. The last zone should be Infinity. */
  untilRange: number;
  vector: Vec3;
}

export interface GustModel {
  /** Peak fractional speed change, e.g. 0.3 for ±30%. */
  amplitude: number;
  /** Seconds between noise lattice points (gust period). */
  period: number;
  seed: number;
}

/** Layered wind along the lane: shooter, mid-range and target zones, with gusts over time. */
export interface WindField {
  zones: WindZone[];
  gust?: GustModel;
}

export const CALM: Readonly<WindField> = Object.freeze({ zones: [] });

/**
 * Wind vector from a speed and the direction it blows FROM, in degrees relative to the shot:
 * 0 = from behind the shooter, 90 = from the left (pushes right), 180 = headwind, 270 = from the right.
 */
export function windFrom(speed: number, fromDeg: number, vertical = 0): Vec3 {
  const a = fromDeg * DEG;
  return { x: speed * Math.cos(a), y: vertical, z: speed * Math.sin(a) };
}

/** Uniform wind over the whole lane. */
export function uniformWind(speed: number, fromDeg: number): WindField {
  return { zones: [{ untilRange: Infinity, vector: windFrom(speed, fromDeg) }] };
}

/** Gust multiplier at an absolute match time (1 = base wind). */
export function gustFactor(gust: GustModel | undefined, time: number): number {
  if (!gust || gust.amplitude === 0) return 1;
  return 1 + gust.amplitude * valueNoise1D(gust.seed, time / gust.period);
}

/** Wind at a downrange distance and absolute match time. */
export function windAt(field: WindField, range: number, time: number): Vec3 {
  const zones = field.zones;
  for (let i = 0; i < zones.length; i++) {
    const zone = zones[i]!;
    if (range < zone.untilRange || i === zones.length - 1) {
      const g = gustFactor(field.gust, time);
      return { x: zone.vector.x * g, y: zone.vector.y * g, z: zone.vector.z * g };
    }
  }
  return { x: 0, y: 0, z: 0 };
}
