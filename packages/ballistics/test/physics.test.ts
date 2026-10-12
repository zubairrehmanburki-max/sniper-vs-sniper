import { describe, expect, it } from 'vitest';
import {
  airDensity,
  CALM,
  DEG,
  dopeTable,
  GRAVITY,
  gustFactor,
  ICAO,
  pointAtRange,
  pointAtTime,
  relativeToLos,
  simulate,
  solveFiringSolution,
  solveZero,
  speedOfSound,
  uniformWind,
  windFrom,
  type Environment,
  type Shot,
} from '../src/index.ts';
import { projectileFor } from './reference.ts';

const m118: Omit<Shot, 'zeroElevation'> = {
  projectile: projectileFor('m118lr'),
  muzzleVelocity: 792.5,
  sightHeight: 0.045,
  twist: 0,
};
const icao: Environment = { atmosphere: ICAO };
const zero = solveZero(m118, icao, 100);
const shot: Shot = { ...m118, zeroElevation: zero };

function at(s: Shot, env: Environment, range: number, opts = {}) {
  const traj = simulate(s, env, { maxRange: range, ...opts });
  return { traj, p: pointAtRange(traj, range)!, rel: relativeToLos(pointAtRange(traj, range)!, traj.losElevation) };
}

describe('atmosphere', () => {
  it('ICAO sea level is 1.225 kg/m³ and 340.3 m/s', () => {
    expect(airDensity(15, 1013.25, 0)).toBeCloseTo(1.225, 2);
    expect(speedOfSound(15)).toBeCloseTo(340.29, 1);
  });
  it('humid air is lighter than dry air at the same temperature and pressure', () => {
    expect(airDensity(30, 1013.25, 0.9)).toBeLessThan(airDensity(30, 1013.25, 0));
  });
  it('hot, high air shoots flatter than cold sea-level air', () => {
    const hot: Environment = { atmosphere: { altitudeM: 2400, pressureHPa: 750, temperatureC: 35, humidity: 0.1 } };
    const cold: Environment = { atmosphere: { altitudeM: 0, pressureHPa: 1020, temperatureC: 2, humidity: 0.8 } };
    expect(at(shot, hot, 800).rel.height).toBeGreaterThan(at(shot, cold, 800).rel.height + 0.5);
  });
});

describe('integration', () => {
  it('with negligible drag the drop is g·t²/2', () => {
    const vacuum: Shot = { ...shot, projectile: { ...shot.projectile, bc: 1e9 }, zeroElevation: 0, sightHeight: 0 };
    const p = pointAtTime(simulate(vacuum, icao, { maxRange: 2000 }), 1)!;
    expect(p.y).toBeCloseTo(-0.5 * GRAVITY, 5);
    expect(p.x).toBeCloseTo(792.5, 3);
  });
  it('RK4 is converged at the default 0.5 ms step', () => {
    const coarse = at(shot, icao, 1000).rel.height;
    const fine = at(shot, icao, 1000, { dt: 0.0001 }).rel.height;
    expect(Math.abs(coarse - fine)).toBeLessThan(1e-4);
  });
  it('semi-implicit Euler at 0.5 ms agrees with RK4 within 0.5%', () => {
    const rk4 = at(shot, icao, 1000).rel.height;
    const euler = at(shot, icao, 1000, { integrator: 'euler' }).rel.height;
    expect(Math.abs(euler - rk4) / Math.abs(rk4)).toBeLessThan(0.005);
  });
  it('reports energy as ½mv²', () => {
    for (const p of simulate(shot, icao, { maxRange: 600 }).points.filter((_, i) => i % 97 === 0)) {
      expect(p.energy).toBeCloseTo(0.5 * shot.projectile.massKg * p.speed * p.speed, 6);
    }
  });
  it('stops at the requested range', () => {
    const traj = simulate(shot, icao, { maxRange: 500 });
    expect(traj.termination).toBe('range');
    expect(traj.points.at(-1)!.x).toBeGreaterThanOrEqual(500);
  });
});

describe('sanity vs real-world dope', () => {
  it('.308 175 gr from a 100 m zero needs about 14 mrad at 1,000 m', () => {
    // The plan quoted "roughly 9-10 mrad", which is closer to the 800 m figure; published M118LR
    // dope (JBM / Applied Ballistics) is about 14 mrad at 1,000 m in ICAO conditions.
    const row = dopeTable(shot, icao, [800, 1000]);
    expect(row[0]!.elevationMrad).toBeGreaterThan(8.8);
    expect(row[0]!.elevationMrad).toBeLessThan(9.6);
    expect(row[1]!.elevationMrad).toBeGreaterThan(13.5);
    expect(row[1]!.elevationMrad).toBeLessThan(15);
  });
  it('.308 goes transonic past ~900 m', () => {
    const [r800, r1000] = dopeTable(shot, icao, [800, 1000]);
    expect(r800!.mach).toBeGreaterThan(1.05);
    expect(r1000!.mach).toBeLessThan(1);
  });
});

describe('wind', () => {
  it('is applied through relative velocity: a crosswind from the left drifts the bullet right', () => {
    expect(at(shot, { ...icao, wind: uniformWind(5, 90) }, 600).rel.lateral).toBeGreaterThan(0.5);
    expect(at(shot, { ...icao, wind: uniformWind(5, 270) }, 600).rel.lateral).toBeLessThan(-0.5);
  });
  it('a headwind drops the bullet more than a tailwind', () => {
    const head = at(shot, { ...icao, wind: uniformWind(8, 180) }, 800).rel.height;
    const tail = at(shot, { ...icao, wind: uniformWind(8, 0) }, 800).rel.height;
    expect(head).toBeLessThan(tail);
  });
  it('layered zones: each third of the lane contributes, and the sum equals a uniform wind', () => {
    const v = windFrom(4, 90);
    const zone = (from: number, to: number): Environment => ({
      ...icao,
      wind: {
        zones: [
          { untilRange: from, vector: { x: 0, y: 0, z: 0 } },
          { untilRange: to, vector: v },
          { untilRange: Infinity, vector: { x: 0, y: 0, z: 0 } },
        ],
      },
    });
    const near = at(shot, zone(0, 300), 900).rel.lateral;
    const mid = at(shot, zone(300, 600), 900).rel.lateral;
    const far = at(shot, zone(600, 900), 900).rel.lateral;
    const all = at(shot, { ...icao, wind: uniformWind(4, 90) }, 900).rel.lateral;
    expect(near).toBeGreaterThan(0);
    expect(mid).toBeGreaterThan(0);
    expect(far).toBeGreaterThan(0);
    expect(near + mid + far).toBeCloseTo(all, 2);
  });
  it('gusts are deterministic per seed, bounded by amplitude, and change with fire time', () => {
    const gust = { amplitude: 0.3, period: 10, seed: 42 };
    for (let t = 0; t < 120; t += 0.7) {
      const g = gustFactor(gust, t);
      expect(g).toBeGreaterThanOrEqual(0.7);
      expect(g).toBeLessThanOrEqual(1.3);
      expect(g).toBe(gustFactor(gust, t));
    }
    const env: Environment = { ...icao, wind: { ...uniformWind(5, 90), gust } };
    const a = at(shot, env, 700, { fireTime: 3 }).rel.lateral;
    const b = at(shot, env, 700, { fireTime: 17 }).rel.lateral;
    expect(a).not.toBeCloseTo(b, 3);
  });
});

describe('spin drift, aerodynamic jump and cant', () => {
  const spun: Shot = { ...shot, twist: 0.2858 };
  it('right-hand twist drifts right, left-hand twist drifts left', () => {
    expect(at(spun, icao, 1000).rel.lateral).toBeGreaterThan(0.2);
    expect(at({ ...spun, twist: -0.2858 }, icao, 1000).rel.lateral).toBeLessThan(-0.2);
  });
  it('is negligible at short range', () => {
    expect(Math.abs(at(spun, icao, 200).rel.lateral)).toBeLessThan(0.01);
  });
  it('aerodynamic jump: right twist with wind from the left lifts the impact', () => {
    const env: Environment = { ...icao, wind: uniformWind(5, 90) };
    const off = at(spun, env, 600, { effects: { aerodynamicJump: false } }).rel.height;
    const on = at(spun, env, 600, { effects: { aerodynamicJump: true } }).rel.height;
    expect(on).toBeGreaterThan(off);
    expect(on - off).toBeLessThan(0.2);
  });
  it('a clockwise cant rotates elevation into a miss to the right and low', () => {
    const dial = dopeTable(shot, icao, [800])[0]!.elevationMrad / 1000;
    const level = at({ ...shot, dialElevation: dial }, icao, 800).rel;
    const canted = at({ ...shot, dialElevation: dial, cant: 10 * DEG }, icao, 800).rel;
    expect(Math.abs(level.height)).toBeLessThan(0.01);
    const expectedRight = 800 * Math.tan(zero + dial) * Math.sin(10 * DEG);
    expect(canted.lateral).toBeGreaterThan(0.8 * expectedRight);
    expect(canted.lateral).toBeLessThan(1.2 * expectedRight);
    expect(canted.height).toBeLessThan(level.height);
  });
});

describe('Coriolis', () => {
  const env: Environment = { ...icao, latitude: 45 };
  it('is off unless enabled', () => {
    const a = at({ ...shot, azimuth: 0 }, env, 1000).rel;
    const b = at(shot, icao, 1000).rel;
    expect(a.lateral).toBe(b.lateral);
  });
  it('deflects right in the northern hemisphere and lifts eastward shots (Eötvös)', () => {
    const north = at({ ...shot, azimuth: 0 }, env, 1000, { effects: { coriolis: true } }).rel;
    const east = at({ ...shot, azimuth: 90 * DEG }, env, 1000, { effects: { coriolis: true } }).rel;
    const west = at({ ...shot, azimuth: 270 * DEG }, env, 1000, { effects: { coriolis: true } }).rel;
    const plain = at(shot, icao, 1000).rel;
    expect(north.lateral).toBeGreaterThan(plain.lateral + 0.01);
    expect(east.height).toBeGreaterThan(plain.height + 0.05);
    expect(west.height).toBeLessThan(plain.height - 0.05);
  });
});

describe('determinism and seeded variation', () => {
  const varied: Shot = { ...shot, muzzleVelocitySd: 4 };
  it('the same seed gives a bit-identical trajectory', () => {
    const a = simulate(varied, icao, { maxRange: 1000, seed: 7, effects: { transonicDispersion: true } });
    const b = simulate(varied, icao, { maxRange: 1000, seed: 7, effects: { transonicDispersion: true } });
    expect(a.points).toEqual(b.points);
  });
  it('without a seed the nominal muzzle velocity is used', () => {
    expect(simulate(varied, icao, { maxRange: 10 }).muzzleVelocity).toBe(792.5);
  });
  it('muzzle velocity spread across seeds matches the configured σ', () => {
    const n = 4000;
    const mvs = Array.from({ length: n }, (_, i) => simulate(varied, icao, { maxRange: 1, seed: i }).muzzleVelocity);
    const mean = mvs.reduce((s, v) => s + v, 0) / n;
    const sd = Math.sqrt(mvs.reduce((s, v) => s + (v - mean) ** 2, 0) / (n - 1));
    expect(mean).toBeCloseTo(792.5, 0);
    expect(sd).toBeGreaterThan(3.7);
    expect(sd).toBeLessThan(4.3);
  });
  it('transonic dispersion only acts once the bullet slows below the threshold', () => {
    const opts = { effects: { transonicDispersion: true } };
    // .308 at 700 m is still well above Mach 1.2: no dispersion yet.
    expect(at(shot, icao, 700, { ...opts, seed: 3 }).rel.height).toBe(at(shot, icao, 700).rel.height);
    // By 1,100 m it has been transonic for a while: seeded spread appears.
    const spread = [1, 2, 3, 4, 5, 6].map((seed) => at(shot, icao, 1100, { ...opts, seed }).rel.lateral);
    expect(Math.max(...spread) - Math.min(...spread)).toBeGreaterThan(0.01);
  });
});

describe('firing solutions', () => {
  const cases: Array<[string, Shot, Environment, number]> = [
    ['flat, calm, 600 m', shot, icao, 600],
    ['flat, 6 m/s crosswind, 900 m', shot, { ...icao, wind: uniformWind(6, 60) }, 900],
    ['15° uphill, 700 m', { ...shot, losElevation: 15 * DEG }, icao, 700],
    ['20° downhill with 5° cant and wind', { ...shot, losElevation: -20 * DEG, cant: 5 * DEG }, { ...icao, wind: uniformWind(4, 300) }, 650],
  ];
  for (const [name, s, env, range] of cases) {
    it(`dialling the solution hits the point of aim: ${name}`, () => {
      const sol = solveFiringSolution(s, env, range);
      const el = s.losElevation ?? 0;
      const traj = simulate({ ...s, dialElevation: sol.elevation, dialWindage: sol.windage }, env, {
        maxRange: range * Math.cos(el),
      });
      const rel = relativeToLos(pointAtRange(traj, range * Math.cos(el))!, el);
      expect(Math.abs(rel.height)).toBeLessThan(0.001);
      expect(Math.abs(rel.lateral)).toBeLessThan(0.001);
      expect(rel.along).toBeCloseTo(range, 0);
    });
  }
  it('incline shots need less elevation than the same distance on the flat', () => {
    const flat = solveFiringSolution(shot, icao, 800).elevation;
    const up = solveFiringSolution({ ...shot, losElevation: 25 * DEG }, icao, 800).elevation;
    const down = solveFiringSolution({ ...shot, losElevation: -25 * DEG }, icao, 800).elevation;
    expect(up).toBeLessThan(flat);
    expect(down).toBeLessThan(flat);
  });
  it('calm-air solution windage is zero without spin', () => {
    expect(solveFiringSolution(shot, { atmosphere: ICAO, wind: CALM }, 500).windage).toBeCloseTo(0, 9);
  });
});
