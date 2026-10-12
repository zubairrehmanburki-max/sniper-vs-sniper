import { describe, expect, it } from 'vitest';
import { pointAtRange, simulate, solveFiringSolution, type Environment } from '@svs/ballistics';
import {
  breathFactor,
  config,
  createPracticeRange,
  forward,
  holdBreath,
  initialBreath,
  laneWindAt,
  missCall,
  rangefind,
  releaseBreath,
  resolveImpact,
  rifleById,
  rifleShot,
  shotToWorld,
  shotWindField,
  swayOffset,
  vectorToShot,
  worldToShot,
  type LaneWind,
  type Silhouette,
} from '../src/index.ts';

const eye = { x: 0, y: 0.3, z: 0 };

describe('world and shot frames', () => {
  it('round-trips points for any yaw', () => {
    for (const yaw of [0, 0.3, -1.2, Math.PI]) {
      const p = { x: 412, y: -3.1, z: 7.5 };
      const w = shotToWorld(p, { x: 5, y: 1, z: -20 }, yaw);
      const back = worldToShot(w, { x: 5, y: 1, z: -20 }, yaw);
      expect(back.x).toBeCloseTo(p.x, 9);
      expect(back.y).toBeCloseTo(p.y, 9);
      expect(back.z).toBeCloseTo(p.z, 9);
    }
  });
  it('yaw 0 looks north (−z) with +x on the right', () => {
    expect(shotToWorld({ x: 100, y: 0, z: 2 }, { x: 0, y: 0, z: 0 }, 0)).toEqual({ x: 2, y: 0, z: -100 });
  });
  it('wind toward +x is a crosswind pushing right when looking north, left when looking south', () => {
    expect(vectorToShot({ x: 5, y: 0, z: 0 }, 0).z).toBeCloseTo(5);
    expect(vectorToShot({ x: 5, y: 0, z: 0 }, Math.PI).z).toBeCloseTo(-5);
  });
});

describe('lane wind', () => {
  const lane: LaneWind = {
    start: { x: 0, y: 0, z: 0 },
    direction: { x: 0, y: 0, z: -1 },
    edges: [300, 600],
    zones: [
      { x: 1, y: 0, z: 0 },
      { x: 2, y: 0, z: 0 },
      { x: 3, y: 0, z: 0 },
    ],
  };
  it('samples the zone a world point is in', () => {
    expect(laneWindAt(lane, { x: 0, y: 0, z: -100 }).x).toBe(1);
    expect(laneWindAt(lane, { x: 0, y: 0, z: -450 }).x).toBe(2);
    expect(laneWindAt(lane, { x: 0, y: 0, z: -800 }).x).toBe(3);
  });
  it('maps zones to downrange distances for a shot from the start', () => {
    const f = shotWindField(lane, { x: 0, y: 0, z: 0 }, 0);
    expect(f.zones.map((z) => z.untilRange)).toEqual([300, 600, Infinity]);
    expect(f.zones.map((z) => Math.round(z.vector.z))).toEqual([1, 2, 3]);
  });
  it('reverses the zone order for a shot from the far end', () => {
    const f = shotWindField(lane, { x: 0, y: 0, z: -900 }, Math.PI);
    expect(f.zones.map((z) => Math.round(z.untilRange))).toEqual([300, 600, Infinity]);
    // Looking south, wind toward +x pushes the bullet left (negative z in the shot frame).
    expect(f.zones.map((z) => Math.round(z.vector.z))).toEqual([-3, -2, -1]);
  });
});

describe('hit resolution', () => {
  const rifle = rifleById('bolt308');
  const shot = rifleShot(rifle);
  const env: Environment = { atmosphere: { altitudeM: 0, pressureHPa: 1013.25, temperatureC: 15, humidity: 0.5 } };
  const target = (d: number): Silhouette => ({
    id: `t${d}`,
    position: { x: 0, y: 0, z: -d },
    facingYaw: Math.PI,
    width: config.target.widthM,
    height: config.target.heightM,
    head: config.target.headM,
  });

  function fireAt(d: number, upMrad = 0, rightMrad = 0) {
    const t = target(d);
    const chestY = t.height - t.head - 0.175;
    const dy = chestY - eye.y;
    const pitch = Math.atan2(dy, d);
    const slant = Math.hypot(d, dy);
    const s = { ...shot, losElevation: pitch };
    const sol = solveFiringSolution(s, env, slant);
    const traj = simulate(
      { ...s, dialElevation: sol.elevation + upMrad / 1000, dialWindage: sol.windage + rightMrad / 1000 },
      env,
      { maxRange: d + 50 },
    );
    return { t, traj };
  }

  for (const d of [300, 600, 900]) {
    it(`a correct solution hits the chest at ${d} m`, () => {
      const { t, traj } = fireAt(d);
      const impact = resolveImpact(traj, eye, 0, [t]);
      expect(impact.kind).toBe('target');
      expect(impact.zone).toBe('chest');
      expect(Math.abs(impact.local!.u)).toBeLessThan(0.01);
      expect(Math.abs(impact.local!.v)).toBeLessThan(0.01);
    });
  }
  it('dialling 0.4 mrad up at 600 m moves the impact into the head', () => {
    const { t, traj } = fireAt(600, 0.45);
    expect(resolveImpact(traj, eye, 0, [t]).zone).toBe('head');
  });
  it('a big low miss hits the ground short of the target, and the spotter calls it low', () => {
    const { t, traj } = fireAt(600, -3);
    const impact = resolveImpact(traj, eye, 0, [t]);
    expect(impact.kind).toBe('ground');
    expect(-impact.point.z).toBeLessThan(600);
  });
  it('the spotter call reports the miss in mrad', () => {
    const { t, traj } = fireAt(600, 0, 1.5);
    const call = missCall(traj, eye, 0, t)!;
    expect(call.rightMrad).toBeCloseTo(1.5, 1);
    expect(Math.abs(call.upMrad)).toBeLessThan(0.1);
    expect(resolveImpact(traj, eye, 0, [t]).kind).not.toBe('target');
  });
  it('impact energy matches the trajectory at that range', () => {
    const { t, traj } = fireAt(600);
    const impact = resolveImpact(traj, eye, 0, [t]);
    expect(impact.energy).toBeCloseTo(pointAtRange(traj, 600)!.energy, -1);
  });
  it('the rangefinder reads the plate distance, or the ground', () => {
    const t = target(600);
    const onPlate = rangefind(eye, { yaw: 0, pitch: Math.atan2(0.5 - 0.3, 600) }, [t])!;
    expect(onPlate).toBeCloseTo(600, 0);
    const onGround = rangefind(eye, { yaw: 0.2, pitch: -0.3 / 250 }, [t])!;
    expect(onGround).toBeGreaterThan(240);
    expect(onGround).toBeLessThan(260);
  });
});

describe('practice range', () => {
  it('is deterministic per seed and differs between seeds', () => {
    expect(createPracticeRange(5)).toEqual(createPracticeRange(5));
    expect(createPracticeRange(5).wind.zones).not.toEqual(createPracticeRange(6).wind.zones);
  });
  it('puts steel at the configured distances, facing the shooter', () => {
    const r = createPracticeRange(1);
    expect(r.targets.map((t) => -t.position.z)).toEqual(config.practiceRange.targetsM);
    for (const t of r.targets) expect(forward(t.facingYaw).z).toBeCloseTo(1);
  });
  it('wind speeds stay in a playable band', () => {
    for (let s = 0; s < 50; s++) {
      for (const z of createPracticeRange(s).wind.zones) {
        expect(Math.hypot(z.x, z.z)).toBeLessThan(10);
      }
    }
  });
});

describe('sway and breath', () => {
  const amp = (stance: 'prone' | 'standing', bipod = false, breath = initialBreath(), now = 10) => {
    let max = 0;
    for (let t = now; t < now + 5; t += 0.05) {
      const o = swayOffset({ stance, bipod, breath, seed: 1 }, t);
      max = Math.max(max, Math.hypot(o.right, o.up));
    }
    return max;
  };
  it('prone is steadier than standing, and a bipod halves it', () => {
    expect(amp('prone')).toBeLessThan(amp('standing') / 3);
    expect(amp('prone', true)).toBeCloseTo(amp('prone') / 2, 5);
  });
  it('holding breath steadies for a few seconds, then strains', () => {
    const b = holdBreath(initialBreath(), 0);
    expect(breathFactor(b, 2)).toBeCloseTo(config.sway.holdBreath.steadyFactor);
    expect(breathFactor(b, 9)).toBeGreaterThan(1.5);
    const released = releaseBreath(b, 9);
    expect(breathFactor(released, 9.01)).toBeGreaterThan(1.5);
    expect(breathFactor(released, 30)).toBeLessThan(1.02);
  });
});
