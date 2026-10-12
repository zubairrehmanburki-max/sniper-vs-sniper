import { airModel, type Atmosphere } from './atmosphere.ts';
import { dragFactor } from './drag.ts';
import { kineticEnergy, type Projectile } from './projectile.ts';
import { createRng, hashSeed } from './rng.ts';
import { aerodynamicJump, millerStability, spinDrift } from './stability.ts';
import { DEG, EARTH_ROTATION, GRAVITY } from './units.ts';
import type { Vec3 } from './vec.ts';
import { windAt, type WindField } from './wind.ts';

/**
 * Everything about the rifle and the trigger press. Angles are radians.
 *
 * Shot frame: the scope's eye is the origin, x points downrange (horizontal, along the line of
 * sight's azimuth), y is up, z is to the shooter's right.
 */
export interface Shot {
  projectile: Projectile;
  /** Nominal muzzle velocity, m/s. */
  muzzleVelocity: number;
  /** Shot-to-shot muzzle velocity standard deviation, m/s. Applied only when a seed is given. */
  muzzleVelocitySd?: number;
  /** Scope centre height over the bore, m. */
  sightHeight: number;
  /** Barrel twist, metres per turn. Positive = right-hand, negative = left-hand, 0 = ignore spin. */
  twist: number;
  /** Bore elevation above the scope axis from zeroing (see solveZero). */
  zeroElevation: number;
  /** Bore windage relative to the scope axis from zeroing, usually 0. */
  zeroWindage?: number;
  /** Line-of-sight elevation above horizontal (uphill positive). */
  losElevation?: number;
  /** Compass azimuth of the line of sight (0 = north, π/2 = east). Only used for Coriolis. */
  azimuth?: number;
  /** Elevation dialled on the turret or held on the reticle (positive = up). */
  dialElevation?: number;
  /** Windage dialled or held (positive = right). */
  dialWindage?: number;
  /** Rifle cant, positive = rotated clockwise as the shooter sees it. */
  cant?: number;
}

export interface Environment {
  atmosphere: Atmosphere;
  wind?: WindField;
  /** Latitude in degrees, needed for Coriolis/Eötvös. */
  latitude?: number;
}

export interface Effects {
  /** Litz spin drift (needs twist and bullet length). Default on. */
  spinDrift: boolean;
  /** Coriolis and Eötvös (needs latitude and azimuth). Default off: "full realism" toggle. */
  coriolis: boolean;
  /** Vertical jump from muzzle crosswind. Default off (flag-gated). */
  aerodynamicJump: boolean;
  /** Seeded dispersion growth below the transonic threshold. Default off; needs a seed. */
  transonicDispersion: boolean;
}

export const DEFAULT_EFFECTS: Readonly<Effects> = Object.freeze({
  spinDrift: true,
  coriolis: false,
  aerodynamicJump: false,
  transonicDispersion: false,
});

export interface TransonicConfig {
  /** Mach number below which the bullet starts to destabilise. */
  machThreshold: number;
  /** Angular random-walk intensity of the velocity direction, rad/√s. */
  sigma: number;
}

export const DEFAULT_TRANSONIC: Readonly<TransonicConfig> = Object.freeze({ machThreshold: 1.2, sigma: 0.004 });

export interface SolveOptions {
  /** Stop once the bullet passes this horizontal downrange distance, m. */
  maxRange: number;
  /** Hard stop on time of flight, s. Default 8. */
  maxTime?: number;
  /** Integration step, s. Default 0.5 ms. */
  dt?: number;
  integrator?: 'rk4' | 'euler';
  /** Match seed. Drives muzzle-velocity variation and transonic dispersion. */
  seed?: number;
  /** Match clock at the trigger press, s. Sets the gust phase. Default 0. */
  fireTime?: number;
  effects?: Partial<Effects>;
  transonic?: TransonicConfig;
  /** Stop if the bullet drops this far below the shooter, m. Default 2000. */
  maxDrop?: number;
  /** Stop below this speed, m/s. Default 30. */
  minSpeed?: number;
}

export interface TrajectoryPoint {
  /** Time of flight, s. */
  t: number;
  /** Position in the shot frame, m. Spin drift is included in z. */
  x: number;
  y: number;
  z: number;
  vx: number;
  vy: number;
  vz: number;
  speed: number;
  mach: number;
  /** Kinetic energy, J. */
  energy: number;
}

export interface Trajectory {
  points: TrajectoryPoint[];
  /** Muzzle velocity actually used (nominal plus seeded variation), m/s. */
  muzzleVelocity: number;
  /** Miller stability factor (0 if spin is ignored). */
  stability: number;
  /** Aerodynamic jump applied at launch, rad (positive = up). */
  aerodynamicJump: number;
  losElevation: number;
  /** Why integration stopped. */
  termination: 'range' | 'time' | 'drop' | 'speed';
}

/**
 * Integrates one bullet's flight. A pure function of (shot, environment, options): no globals,
 * no clock, no Math.random. The server runs it for the verdict, the client for previews and killcams.
 */
export function simulate(shot: Shot, env: Environment, options: SolveOptions): Trajectory {
  const effects: Effects = { ...DEFAULT_EFFECTS, ...options.effects };
  const dt = options.dt ?? 0.0005;
  const maxTime = options.maxTime ?? 8;
  const maxDrop = options.maxDrop ?? 2000;
  const minSpeed = options.minSpeed ?? 30;
  const fireTime = options.fireTime ?? 0;
  const transonic = options.transonic ?? DEFAULT_TRANSONIC;
  const wind = env.wind;
  const air = airModel(env.atmosphere);
  const { projectile } = shot;
  const seeded = options.seed !== undefined;

  // --- launch conditions -------------------------------------------------
  let mv = shot.muzzleVelocity;
  if (seeded && shot.muzzleVelocitySd) {
    mv += shot.muzzleVelocitySd * createRng(hashSeed(options.seed!, 0x6d76)).normal();
  }

  const sg = effects.spinDrift || effects.aerodynamicJump ? millerStability(projectile, shot.twist, mv, env.atmosphere) : 0;

  const losEl = shot.losElevation ?? 0;
  const cant = shot.cant ?? 0;
  // Unit vectors of the scope: line of sight l, scope-up u, scope-right r (cant rotates u/r about l).
  const ce = Math.cos(losEl);
  const se = Math.sin(losEl);
  const cc = Math.cos(cant);
  const sc = Math.sin(cant);
  const l: Vec3 = { x: ce, y: se, z: 0 };
  const up0: Vec3 = { x: -se, y: ce, z: 0 };
  const u: Vec3 = { x: cc * up0.x, y: cc * up0.y, z: sc };
  const r: Vec3 = { x: -sc * up0.x, y: -sc * up0.y, z: cc };

  const elev = shot.zeroElevation + (shot.dialElevation ?? 0);
  const wnd = (shot.zeroWindage ?? 0) + (shot.dialWindage ?? 0);
  const te = Math.tan(elev);
  const tw = Math.tan(wnd);
  let dx = l.x + te * u.x + tw * r.x;
  let dy = l.y + te * u.y + tw * r.y;
  let dz = l.z + te * u.z + tw * r.z;

  let jump = 0;
  if (effects.aerodynamicJump && wind) {
    jump = aerodynamicJump(projectile, sg, shot.twist, windAt(wind, 0, fireTime).z);
    const tj = Math.tan(jump);
    dx += tj * up0.x;
    dy += tj * up0.y;
  }
  const dn = Math.hypot(dx, dy, dz);

  // State: position (bore exit is sightHeight below the scope along scope-up) and velocity.
  let x = -shot.sightHeight * u.x;
  let y = -shot.sightHeight * u.y;
  let z = -shot.sightHeight * u.z;
  let vx = (mv * dx) / dn;
  let vy = (mv * dy) / dn;
  let vz = (mv * dz) / dn;

  // Earth rotation in the shot frame (x along the azimuth, y up, z right).
  let ox = 0;
  let oy = 0;
  let oz = 0;
  if (effects.coriolis && env.latitude !== undefined && shot.azimuth !== undefined) {
    const lat = env.latitude * DEG;
    const az = shot.azimuth;
    ox = EARTH_ROTATION * Math.cos(lat) * Math.cos(az);
    oy = EARTH_ROTATION * Math.sin(lat);
    oz = -EARTH_ROTATION * Math.cos(lat) * Math.sin(az);
  }
  const useCoriolis = ox !== 0 || oy !== 0 || oz !== 0;

  const bc = projectile.bc;
  const model = projectile.dragModel;

  // Acceleration at a state; writes into acc[0..2] and returns Mach.
  const acc = new Float64Array(3);
  const accel = (t: number, px: number, py: number, vxx: number, vyy: number, vzz: number): number => {
    let wx = 0;
    let wy = 0;
    let wz = 0;
    if (wind) {
      const w = windAt(wind, px, fireTime + t);
      wx = w.x;
      wy = w.y;
      wz = w.z;
    }
    const rx = vxx - wx;
    const ry = vyy - wy;
    const rz = vzz - wz;
    const vr = Math.sqrt(rx * rx + ry * ry + rz * rz);
    const a = air(py);
    const mach = vr / a.speedOfSound;
    const k = dragFactor(model, bc, mach, a.density) * vr;
    acc[0] = -k * rx;
    acc[1] = -k * ry - GRAVITY;
    acc[2] = -k * rz;
    if (useCoriolis) {
      // a = -2 Ω × v
      acc[0] += -2 * (oy * vzz - oz * vyy);
      acc[1] += -2 * (oz * vxx - ox * vzz);
      acc[2] += -2 * (ox * vyy - oy * vxx);
    }
    return mach;
  };

  const points: TrajectoryPoint[] = [];
  const record = (t: number, mach: number): void => {
    const speed = Math.sqrt(vx * vx + vy * vy + vz * vz);
    points.push({
      t,
      x,
      y,
      z: z + (effects.spinDrift ? spinDrift(sg, shot.twist, t) : 0),
      vx,
      vy,
      vz,
      speed,
      mach,
      energy: kineticEnergy(projectile, speed),
    });
  };

  const dispersionRng = seeded && effects.transonicDispersion ? createRng(hashSeed(options.seed!, 0x7472)) : null;
  const sqrtDt = Math.sqrt(dt);

  let t = 0;
  let mach = accel(0, x, y, vx, vy, vz);
  record(0, mach);
  let termination: Trajectory['termination'] = 'time';

  while (true) {
    if (options.integrator === 'euler') {
      // Semi-implicit (symplectic) Euler.
      accel(t, x, y, vx, vy, vz);
      vx += acc[0]! * dt;
      vy += acc[1]! * dt;
      vz += acc[2]! * dt;
      x += vx * dt;
      y += vy * dt;
      z += vz * dt;
    } else {
      accel(t, x, y, vx, vy, vz);
      const k1x = vx, k1y = vy, k1z = vz;
      const a1x = acc[0]!, a1y = acc[1]!, a1z = acc[2]!;
      const h2 = dt / 2;
      accel(t + h2, x + k1x * h2, y + k1y * h2, vx + a1x * h2, vy + a1y * h2, vz + a1z * h2);
      const k2x = vx + a1x * h2, k2y = vy + a1y * h2, k2z = vz + a1z * h2;
      const a2x = acc[0]!, a2y = acc[1]!, a2z = acc[2]!;
      accel(t + h2, x + k2x * h2, y + k2y * h2, vx + a2x * h2, vy + a2y * h2, vz + a2z * h2);
      const k3x = vx + a2x * h2, k3y = vy + a2y * h2, k3z = vz + a2z * h2;
      const a3x = acc[0]!, a3y = acc[1]!, a3z = acc[2]!;
      accel(t + dt, x + k3x * dt, y + k3y * dt, vx + a3x * dt, vy + a3y * dt, vz + a3z * dt);
      const k4x = vx + a3x * dt, k4y = vy + a3y * dt, k4z = vz + a3z * dt;
      const a4x = acc[0]!, a4y = acc[1]!, a4z = acc[2]!;
      const s = dt / 6;
      x += s * (k1x + 2 * k2x + 2 * k3x + k4x);
      y += s * (k1y + 2 * k2y + 2 * k3y + k4y);
      z += s * (k1z + 2 * k2z + 2 * k3z + k4z);
      vx += s * (a1x + 2 * a2x + 2 * a3x + a4x);
      vy += s * (a1y + 2 * a2y + 2 * a3y + a4y);
      vz += s * (a1z + 2 * a2z + 2 * a3z + a4z);
    }
    t += dt;
    mach = accel(t, x, y, vx, vy, vz);

    if (dispersionRng && mach < transonic.machThreshold) {
      // Random walk of the velocity direction in the two axes perpendicular to flight.
      const speed = Math.sqrt(vx * vx + vy * vy + vz * vz);
      const h = Math.hypot(vx, vz);
      const dPitch = transonic.sigma * sqrtDt * dispersionRng.normal();
      const dYaw = transonic.sigma * sqrtDt * dispersionRng.normal();
      // pitch axis: perpendicular to v, in the vertical plane; yaw axis: horizontal, perpendicular to v.
      const px = (-vx * vy) / (speed * h);
      const py = h / speed;
      const pz = (-vz * vy) / (speed * h);
      const yx = -vz / h;
      const yz = vx / h;
      vx += speed * (dPitch * px + dYaw * yx);
      vy += speed * dPitch * py;
      vz += speed * (dPitch * pz + dYaw * yz);
    }

    record(t, mach);

    if (x >= options.maxRange) {
      termination = 'range';
      break;
    }
    if (t >= maxTime) {
      termination = 'time';
      break;
    }
    if (y < -maxDrop) {
      termination = 'drop';
      break;
    }
    if (points[points.length - 1]!.speed < minSpeed) {
      termination = 'speed';
      break;
    }
  }

  return { points, muzzleVelocity: mv, stability: sg, aerodynamicJump: jump, losElevation: losEl, termination };
}

const lerp = (a: number, b: number, f: number): number => a + (b - a) * f;

function interpolate(a: TrajectoryPoint, b: TrajectoryPoint, f: number): TrajectoryPoint {
  return {
    t: lerp(a.t, b.t, f),
    x: lerp(a.x, b.x, f),
    y: lerp(a.y, b.y, f),
    z: lerp(a.z, b.z, f),
    vx: lerp(a.vx, b.vx, f),
    vy: lerp(a.vy, b.vy, f),
    vz: lerp(a.vz, b.vz, f),
    speed: lerp(a.speed, b.speed, f),
    mach: lerp(a.mach, b.mach, f),
    energy: lerp(a.energy, b.energy, f),
  };
}

/** Bullet state where it crosses a horizontal downrange distance, or undefined if it never got there. */
export function pointAtRange(trajectory: Trajectory, range: number): TrajectoryPoint | undefined {
  const pts = trajectory.points;
  if (pts.length === 0 || pts[pts.length - 1]!.x < range) return undefined;
  let lo = 0;
  let hi = pts.length - 1;
  if (pts[0]!.x >= range) return pts[0];
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (pts[mid]!.x < range) lo = mid;
    else hi = mid;
  }
  const a = pts[lo]!;
  const b = pts[hi]!;
  return interpolate(a, b, (range - a.x) / (b.x - a.x));
}

/** Bullet state at a time of flight, or undefined if the flight ended earlier. */
export function pointAtTime(trajectory: Trajectory, time: number): TrajectoryPoint | undefined {
  const pts = trajectory.points;
  if (pts.length === 0 || time > pts[pts.length - 1]!.t) return undefined;
  if (time <= 0) return pts[0];
  let lo = 0;
  let hi = pts.length - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (pts[mid]!.t < time) lo = mid;
    else hi = mid;
  }
  const a = pts[lo]!;
  const b = pts[hi]!;
  return interpolate(a, b, (time - a.t) / (b.t - a.t));
}

/** Position relative to the line of sight: distance along it, height above it, lateral offset. */
export function relativeToLos(p: Pick<TrajectoryPoint, 'x' | 'y' | 'z'>, losElevation: number) {
  const ce = Math.cos(losElevation);
  const se = Math.sin(losElevation);
  return { along: p.x * ce + p.y * se, height: -p.x * se + p.y * ce, lateral: p.z };
}
