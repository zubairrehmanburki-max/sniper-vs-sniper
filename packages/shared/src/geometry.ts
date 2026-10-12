import type { Vec3 } from '@svs/ballistics';

/** Axis-aligned box in world space. Hard cover stops every bullet; soft cover is for Phase 4 penetration. */
export interface Box {
  id: string;
  min: Vec3;
  max: Vec3;
  material: 'hard' | 'soft';
}

/**
 * Fraction t ∈ [0, 1] along the segment a→b where it first enters the box, or undefined.
 * Slab method.
 */
export function segmentBoxEntry(a: Vec3, b: Vec3, box: Box): number | undefined {
  let t0 = 0;
  let t1 = 1;
  for (const axis of ['x', 'y', 'z'] as const) {
    const d = b[axis] - a[axis];
    if (Math.abs(d) < 1e-12) {
      if (a[axis] < box.min[axis] || a[axis] > box.max[axis]) return undefined;
      continue;
    }
    let ta = (box.min[axis] - a[axis]) / d;
    let tb = (box.max[axis] - a[axis]) / d;
    if (ta > tb) [ta, tb] = [tb, ta];
    t0 = Math.max(t0, ta);
    t1 = Math.min(t1, tb);
    if (t0 > t1) return undefined;
  }
  return t0;
}

export function pointInBox(p: Vec3, box: Box, margin = 0): boolean {
  return (
    p.x >= box.min.x - margin &&
    p.x <= box.max.x + margin &&
    p.y >= box.min.y - margin &&
    p.y <= box.max.y + margin &&
    p.z >= box.min.z - margin &&
    p.z <= box.max.z + margin
  );
}

export const distance = (a: Vec3, b: Vec3): number => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
export const horizontalDistance = (a: Vec3, b: Vec3): number => Math.hypot(a.x - b.x, a.z - b.z);
