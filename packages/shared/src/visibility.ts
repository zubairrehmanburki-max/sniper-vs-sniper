import type { Vec3 } from '@svs/ballistics';
import { segmentBoxEntry, type Box } from './geometry.ts';
import type { Ground } from './hits.ts';

/** True when nothing (hard cover or terrain) blocks the straight line between two points. */
export function lineOfSight(from: Vec3, to: Vec3, ground: Ground, cover: Box[], step = 2): boolean {
  for (const box of cover) {
    const f = segmentBoxEntry(from, to, box);
    if (f !== undefined && f > 1e-6 && f < 1 - 1e-6) return false;
  }
  const len = Math.hypot(to.x - from.x, to.y - from.y, to.z - from.z);
  const n = Math.max(2, Math.ceil(len / step));
  for (let i = 1; i < n; i++) {
    const f = i / n;
    const x = from.x + (to.x - from.x) * f;
    const y = from.y + (to.y - from.y) * f;
    const z = from.z + (to.z - from.z) * f;
    if (y < ground(x, z)) return false;
  }
  return true;
}
