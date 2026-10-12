import type { Trajectory, TrajectoryPoint, Vec3 } from '@svs/ballistics';
import { forward, rightOf, shotToWorld, type Aim } from './world.ts';

export type HitZone = 'head' | 'chest' | 'body';

/** A human-size silhouette (steel plate on the range, the frozen player in a duel). */
export interface Silhouette {
  id: string;
  /** Centre of the base, on the ground, world frame. */
  position: Vec3;
  /** Yaw the silhouette faces (its front looks along forward(facingYaw)). */
  facingYaw: number;
  width: number;
  height: number;
  /** Head height; the head is 40% of the body width. */
  head: number;
}

/** Ground height at a world (x, z). */
export type Ground = (x: number, z: number) => number;
export const flatGround: Ground = () => 0;

export interface Impact {
  kind: 'target' | 'ground' | 'none';
  targetId?: string;
  zone?: HitZone;
  /** World point of impact (last integrated point for 'none'). */
  point: Vec3;
  /** Time of flight, s. */
  t: number;
  speed: number;
  energy: number;
  /** For target hits: offset from the plate centre in the plate plane, m (right, up). */
  local?: { u: number; v: number };
}

const CHEST_DEPTH = 0.35; // m of torso below the head that counts as chest

/** Which zone of a silhouette a local (u right, h up from base) point falls in, if any. */
export function silhouetteZone(s: Silhouette, u: number, h: number): HitZone | undefined {
  if (h < 0 || h > s.height) return undefined;
  const headBase = s.height - s.head;
  if (h >= headBase) return Math.abs(u) <= 0.2 * s.width ? 'head' : undefined;
  if (Math.abs(u) > s.width / 2) return undefined;
  return h >= headBase - CHEST_DEPTH ? 'chest' : 'body';
}

function lerpPoint(a: Vec3, b: Vec3, f: number): Vec3 {
  return { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f, z: a.z + (b.z - a.z) * f };
}

/**
 * Walks the trajectory and returns the first thing the bullet meets: a silhouette or the ground.
 * This is the authoritative hit test (server) and the preview/killcam hit test (client).
 */
export function resolveImpact(
  traj: Trajectory,
  eye: Vec3,
  yaw: number,
  targets: Silhouette[],
  ground: Ground = flatGround,
): Impact {
  const pts = traj.points;
  const world = (p: TrajectoryPoint): Vec3 => shotToWorld(p, eye, yaw);
  const frames = targets.map((s) => ({ s, n: forward(s.facingYaw), r: rightOf(s.facingYaw) }));
  let prev = world(pts[0]!);
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1]!;
    const b = pts[i]!;
    const cur = world(b);
    let best: { f: number; impact: Impact } | undefined;

    for (const { s, n, r } of frames) {
      const da = (prev.x - s.position.x) * n.x + (prev.z - s.position.z) * n.z;
      const db = (cur.x - s.position.x) * n.x + (cur.z - s.position.z) * n.z;
      if (da > 0 && db <= 0) {
        const f = da / (da - db);
        const p = lerpPoint(prev, cur, f);
        const u = (p.x - s.position.x) * r.x + (p.z - s.position.z) * r.z;
        const h = p.y - s.position.y;
        const zone = silhouetteZone(s, -u, h); // facing the shooter, the plate's right is the shooter's left
        if (zone && (!best || f < best.f)) {
          best = {
            f,
            impact: {
              kind: 'target',
              targetId: s.id,
              zone,
              point: p,
              t: a.t + (b.t - a.t) * f,
              speed: a.speed + (b.speed - a.speed) * f,
              energy: a.energy + (b.energy - a.energy) * f,
              local: { u: -u, v: h - (s.height - s.head - CHEST_DEPTH / 2) },
            },
          };
        }
      }
    }

    const ga = prev.y - ground(prev.x, prev.z);
    const gb = cur.y - ground(cur.x, cur.z);
    if (ga > 0 && gb <= 0) {
      const f = ga / (ga - gb);
      if (!best || f < best.f) {
        best = {
          f,
          impact: {
            kind: 'ground',
            point: lerpPoint(prev, cur, f),
            t: a.t + (b.t - a.t) * f,
            speed: a.speed + (b.speed - a.speed) * f,
            energy: a.energy + (b.energy - a.energy) * f,
          },
        };
      }
    }
    if (best) return best.impact;
    prev = cur;
  }
  const last = pts[pts.length - 1]!;
  return { kind: 'none', point: prev, t: last.t, speed: last.speed, energy: last.energy };
}

/**
 * Where the bullet passed a silhouette's plane relative to its chest centre, as the shooter sees it,
 * in mrad (positive = high / right). The spotter's call after a shot. Undefined if it never got there.
 */
export function missCall(traj: Trajectory, eye: Vec3, yaw: number, s: Silhouette): { upMrad: number; rightMrad: number } | undefined {
  const n = forward(s.facingYaw);
  const r = rightOf(s.facingYaw);
  const pts = traj.points;
  let prev = shotToWorld(pts[0]!, eye, yaw);
  for (let i = 1; i < pts.length; i++) {
    const cur = shotToWorld(pts[i]!, eye, yaw);
    const da = (prev.x - s.position.x) * n.x + (prev.z - s.position.z) * n.z;
    const db = (cur.x - s.position.x) * n.x + (cur.z - s.position.z) * n.z;
    if (da > 0 && db <= 0) {
      const p = lerpPoint(prev, cur, da / (da - db));
      const centreY = s.position.y + s.height - s.head - CHEST_DEPTH / 2;
      const dist = Math.hypot(s.position.x - eye.x, centreY - eye.y, s.position.z - eye.z);
      const u = -((p.x - s.position.x) * r.x + (p.z - s.position.z) * r.z);
      return { upMrad: ((p.y - centreY) / dist) * 1000, rightMrad: (u / dist) * 1000 };
    }
    prev = cur;
  }
  return undefined;
}

/** Laser rangefinder: distance along the aim ray to the first silhouette or the ground. */
export function rangefind(eye: Vec3, aim: Aim, targets: Silhouette[], ground: Ground = flatGround, maxRange = 3000): number | undefined {
  const f = forward(aim.yaw);
  const cp = Math.cos(aim.pitch);
  const dir = { x: f.x * cp, y: Math.sin(aim.pitch), z: f.z * cp };
  let best: number | undefined;
  for (const s of targets) {
    const n = forward(s.facingYaw);
    const denom = dir.x * n.x + dir.z * n.z;
    if (denom >= 0) continue;
    const d = ((s.position.x - eye.x) * n.x + (s.position.z - eye.z) * n.z) / denom;
    if (d <= 0) continue;
    const p = { x: eye.x + dir.x * d, y: eye.y + dir.y * d, z: eye.z + dir.z * d };
    const r = rightOf(s.facingYaw);
    const u = -((p.x - s.position.x) * r.x + (p.z - s.position.z) * r.z);
    if (silhouetteZone(s, u, p.y - s.position.y) && (best === undefined || d < best)) best = d;
  }
  // March the ground.
  const step = 0.5;
  for (let d = step; d < (best ?? maxRange); d += step) {
    const x = eye.x + dir.x * d;
    const z = eye.z + dir.z * d;
    if (eye.y + dir.y * d <= ground(x, z)) return d;
  }
  return best;
}
