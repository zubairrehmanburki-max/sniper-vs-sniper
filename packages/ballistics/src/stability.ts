import type { Atmosphere } from './atmosphere.ts';
import type { Projectile } from './projectile.ts';
import { FPS, GRAIN, INCH, MOA, MPH } from './units.ts';

/**
 * Miller gyroscopic stability factor Sg, with velocity and atmosphere corrections.
 * twist is metres per turn (sign ignored).
 */
export function millerStability(
  projectile: Projectile,
  twist: number,
  muzzleVelocity: number,
  atmo: Atmosphere,
): number {
  if (twist === 0) return 0;
  const d = projectile.diameterM / INCH;
  const twistCal = Math.abs(twist) / INCH / d;
  const lengthCal = projectile.lengthM / INCH / d;
  const grains = projectile.massKg / GRAIN;
  const sg = (30 * grains) / (twistCal * twistCal * d * d * d * lengthCal * (1 + lengthCal * lengthCal));
  const fv = Math.cbrt(muzzleVelocity / FPS / 2800);
  const tempF = (atmo.temperatureC * 9) / 5 + 32;
  const pressureInHg = atmo.pressureHPa / 33.8639;
  const ftp = ((tempF + 460) / 519) * (29.92 / pressureInHg);
  return sg * fv * ftp;
}

/**
 * Litz spin-drift approximation, metres to the right (left for a left-hand twist):
 * drift = 1.25·(Sg + 1.2)·t^1.83 inches.
 */
export function spinDrift(sg: number, twist: number, time: number): number {
  if (sg === 0 || twist === 0 || time <= 0) return 0;
  return Math.sign(twist) * 1.25 * (sg + 1.2) * Math.pow(time, 1.83) * INCH;
}

/**
 * Litz aerodynamic-jump estimate: vertical launch deflection (rad) caused by crosswind at the muzzle.
 * Y [MOA per mph] = 0.01·Sg − 0.0024·L + 0.032 (L = bullet length in calibres).
 * For a right-hand twist a wind blowing left-to-right (positive z) jumps the bullet up.
 */
export function aerodynamicJump(projectile: Projectile, sg: number, twist: number, crosswind: number): number {
  if (sg === 0 || twist === 0) return 0;
  const lengthCal = projectile.lengthM / projectile.diameterM;
  const moaPerMph = 0.01 * sg - 0.0024 * lengthCal + 0.032;
  return Math.sign(twist) * moaPerMph * (crosswind / MPH) * MOA;
}
