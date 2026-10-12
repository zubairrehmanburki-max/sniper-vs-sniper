import type { DragModel } from './drag.ts';

export interface Projectile {
  dragModel: DragModel;
  /** Ballistic coefficient against the chosen standard, lb/in². */
  bc: number;
  massKg: number;
  diameterM: number;
  lengthM: number;
}

/** Kinetic energy in joules. */
export function kineticEnergy(projectile: Projectile, speed: number): number {
  return 0.5 * projectile.massKg * speed * speed;
}
