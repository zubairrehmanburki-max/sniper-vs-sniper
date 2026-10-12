/** Turning a fire input into a verdict: the authoritative resolution shared by hotseat, bot and server. */
import { hashSeed, simulate, type Atmosphere, type Trajectory, type Vec3 } from '@svs/ballistics';
import { config, rifleById, rifleShot, type GameConfig, type Stance } from './config.ts';
import { resolveImpact, missCall, type HitZone, type Impact, type Silhouette } from './hits.ts';
import type { DuelMap, Side } from './maps.ts';
import { shotWindField, type LaneWind } from './world.ts';

/** What a client sends when it fires. Never the result: the referee computes that. */
export interface FireInput {
  /** True muzzle axis (sway included), rad. */
  yaw: number;
  pitch: number;
  dialElevationMrad: number;
  dialWindageMrad: number;
  cant?: number;
}

export interface Conditions {
  atmosphere: Atmosphere;
  wind: LaneWind;
  latitude: number;
}

export function silhouetteFor(id: string, position: Vec3, stance: Stance, facingYaw: number, cfg: GameConfig = config): Silhouette {
  const s = cfg.player.silhouette[stance];
  return { id, position, facingYaw, width: s.widthM, height: s.heightM, head: s.headM };
}

/** Eye (scope) position for a player standing at `position` in a stance. */
export function eyeOf(position: Vec3, stance: Stance, cfg: GameConfig = config): Vec3 {
  const s = cfg.player.silhouette[stance];
  return { x: position.x, y: position.y + s.heightM - s.headM / 2, z: position.z };
}

/** Chest centre of a silhouette (what a careful shooter aims at). */
export function chestOf(s: Silhouette): Vec3 {
  return { x: s.position.x, y: s.position.y + s.height - s.head - 0.175, z: s.position.z };
}

export const facingYaw = (side: Side): number => (side === 'A' ? 0 : Math.PI);

export type Verdict = 'kill' | 'wound' | 'miss';

export interface ShotOutcome {
  verdict: Verdict;
  zone?: HitZone;
  impact: Impact;
  /** Bullet passed within the near-miss radius while supersonic: the target hears the snap. */
  nearMiss: boolean;
  /** Where the bullet passed the target plane relative to its chest, mrad from the shooter (miss diagnostics). */
  call?: { upMrad: number; rightMrad: number };
  trajectory: Trajectory;
  eye: Vec3;
  yaw: number;
  seed: number;
}

export interface ShotRequest {
  map: DuelMap;
  conditions: Conditions;
  rifleId: string;
  shooter: { position: Vec3; stance: Stance };
  target: { id: string; side: Side; position: Vec3; stance: Stance; wounds: number };
  input: FireInput;
  /** Match seed and shot counter, for muzzle-velocity and transonic variation. */
  seed: number;
  shotIndex: number;
  /** Match clock at the trigger press (gust phase). */
  fireTime: number;
  coldBore: boolean;
}

export function resolveShot(req: ShotRequest, cfg: GameConfig = config): ShotOutcome {
  const rifle = rifleById(req.rifleId, cfg);
  const b = cfg.ballistics;
  const eye = eyeOf(req.shooter.position, req.shooter.stance, cfg);
  const cold = req.coldBore ? rifle.coldBoreShiftMrad : { elevation: 0, windage: 0 };
  const shot = {
    ...rifleShot(rifle, cfg),
    losElevation: req.input.pitch,
    azimuth: req.input.yaw,
    cant: req.input.cant ?? 0,
    dialElevation: (req.input.dialElevationMrad + cold.elevation) / 1000,
    dialWindage: (req.input.dialWindageMrad + cold.windage) / 1000,
  };
  const env = {
    atmosphere: req.conditions.atmosphere,
    wind: shotWindField(req.conditions.wind, eye, req.input.yaw),
    latitude: req.conditions.latitude,
  };
  const seed = hashSeed(req.seed, req.shotIndex);
  const trajectory = simulate(shot, env, {
    maxRange: req.map.laneLength + 300,
    dt: b.dtSeconds,
    integrator: b.integrator,
    seed,
    fireTime: req.fireTime,
    effects: b.effects,
    transonic: { machThreshold: b.transonic.machThreshold, sigma: b.transonic.sigmaRadPerSqrtS },
    maxTime: 5,
  });
  const target = silhouetteFor(req.target.id, req.target.position, req.target.stance, facingYaw(req.target.side), cfg);
  const impact = resolveImpact(trajectory, eye, req.input.yaw, [target], req.map.ground, req.map.cover);
  const call = missCall(trajectory, eye, req.input.yaw, target);

  let verdict: Verdict = 'miss';
  let zone: HitZone | undefined;
  if (impact.kind === 'target') {
    zone = impact.zone!;
    const lethal = zone === 'head' || (zone === 'chest' && impact.energy >= cfg.rules.chestKillEnergyJ);
    verdict = lethal || req.target.wounds + 1 >= cfg.rules.woundsToDown ? 'kill' : 'wound';
  }

  let nearMiss = false;
  const chest = chestOf(target);
  const d = Math.hypot(chest.x - eye.x, chest.y - eye.y, chest.z - eye.z);
  const reachedTarget = Math.hypot(impact.point.x - eye.x, impact.point.z - eye.z) >= Math.hypot(chest.x - eye.x, chest.z - eye.z) - 0.5;
  if (verdict === 'miss' && call && reachedTarget) {
    const offsetM = (Math.hypot(call.upMrad, call.rightMrad) / 1000) * d;
    const passing = trajectory.points.find((p) => p.x >= d * 0.98);
    nearMiss = offsetM <= cfg.rules.nearMissRadiusM && (passing?.mach ?? 0) > 1;
  }
  return { verdict, zone, impact, nearMiss, call, trajectory, eye, yaw: req.input.yaw, seed };
}
