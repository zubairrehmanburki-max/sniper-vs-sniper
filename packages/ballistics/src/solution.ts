import { pointAtRange, relativeToLos, simulate, type Environment, type Shot, type SolveOptions } from './solver.ts';
import { radToMrad } from './units.ts';

const ZERO_OPTIONS: Partial<SolveOptions> = {
  effects: { spinDrift: false, coriolis: false, aerodynamicJump: false, transonicDispersion: false },
};

/**
 * Bore elevation (rad) that puts the bullet on the line of sight at zeroRange, in still air.
 * Zeroing ignores dial, cant, incline and random effects, like zeroing on a flat range.
 */
export function solveZero(
  shot: Omit<Shot, 'zeroElevation'>,
  env: Environment,
  zeroRange: number,
  options: Partial<SolveOptions> = {},
): number {
  const level: Shot = {
    ...shot,
    zeroElevation: 0,
    zeroWindage: 0,
    dialElevation: 0,
    dialWindage: 0,
    cant: 0,
    losElevation: 0,
  };
  const calm: Environment = { atmosphere: env.atmosphere };
  let elev = 0;
  for (let i = 0; i < 30; i++) {
    const traj = simulate({ ...level, zeroElevation: elev }, calm, {
      ...options,
      ...ZERO_OPTIONS,
      seed: undefined,
      maxRange: zeroRange,
    });
    const p = pointAtRange(traj, zeroRange);
    if (!p) throw new Error(`Cannot zero at ${zeroRange} m: bullet does not reach it`);
    const err = p.y / zeroRange;
    elev -= err;
    if (Math.abs(p.y) < 1e-7) break;
  }
  return elev;
}

export interface FiringSolution {
  /** Elevation to dial or hold, rad (positive = up). */
  elevation: number;
  /** Windage to dial or hold, rad (positive = right). */
  windage: number;
  timeOfFlight: number;
  impactVelocity: number;
  impactEnergy: number;
}

/**
 * The dial/hold that puts the bullet on the point of aim at a slant distance along the shot's
 * line of sight (shot.losElevation, cant and wind included). This is what the bot asks for each
 * turn before perturbing it, and what an in-game ballistic calculator shows.
 */
export function solveFiringSolution(
  shot: Shot,
  env: Environment,
  slantRange: number,
  options: Partial<SolveOptions> = {},
): FiringSolution {
  const losElevation = shot.losElevation ?? 0;
  const range = slantRange * Math.cos(losElevation);
  let elevation = 0;
  let windage = 0;
  for (let i = 0; i < 30; i++) {
    const traj = simulate({ ...shot, dialElevation: elevation, dialWindage: windage }, env, {
      ...options,
      maxRange: range,
    });
    const p = pointAtRange(traj, range);
    if (!p) throw new Error(`Target at ${slantRange.toFixed(0)} m is out of reach`);
    const rel = relativeToLos(p, losElevation);
    if (Math.abs(rel.height) < 1e-5 && Math.abs(rel.lateral) < 1e-5) {
      return { elevation, windage, timeOfFlight: p.t, impactVelocity: p.speed, impactEnergy: p.energy };
    }
    // With cant the dial axes are rotated; map the miss back into the reticle frame.
    const cant = shot.cant ?? 0;
    const up = rel.height / rel.along;
    const right = rel.lateral / rel.along;
    elevation -= up * Math.cos(cant) + right * Math.sin(cant);
    windage -= right * Math.cos(cant) - up * Math.sin(cant);
  }
  throw new Error('Firing solution did not converge');
}

export interface DopeRow {
  range: number;
  time: number;
  velocity: number;
  mach: number;
  energy: number;
  /** Bullet height relative to the line of sight, m (negative = below). */
  height: number;
  /** Lateral offset, m (positive = right). */
  lateral: number;
  /** Elevation correction to dial, mrad (positive = dial up). */
  elevationMrad: number;
  /** Windage correction to dial, mrad (positive = dial right). */
  windageMrad: number;
}

/** A dope card: corrections at each range for one shot setup (dial/hold should be zero). */
export function dopeTable(
  shot: Shot,
  env: Environment,
  ranges: number[],
  options: Partial<SolveOptions> = {},
): DopeRow[] {
  const maxRange = Math.max(...ranges);
  const traj = simulate(shot, env, { ...options, maxRange });
  return ranges.map((range) => {
    const p = pointAtRange(traj, range);
    if (!p) throw new Error(`Bullet does not reach ${range} m`);
    const rel = relativeToLos(p, traj.losElevation);
    return {
      range,
      time: p.t,
      velocity: p.speed,
      mach: p.mach,
      energy: p.energy,
      height: rel.height,
      lateral: rel.lateral,
      elevationMrad: radToMrad(-rel.height / rel.along),
      windageMrad: radToMrad(-rel.lateral / rel.along),
    };
  });
}
