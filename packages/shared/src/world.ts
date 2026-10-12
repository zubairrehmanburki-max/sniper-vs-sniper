/**
 * World frame (matches Three.js): x = east/right across the map, y = up, z = south.
 * Player yaw 0 looks north (−z); positive yaw turns right (toward +x).
 *
 * The ballistics solver works in a per-shot frame (x downrange, y up, z right, origin at the scope).
 * These helpers convert between the two.
 */
import type { Vec3, WindField, WindZone, GustModel } from '@svs/ballistics';

export interface Aim {
  /** Rotation from north toward east, rad. */
  yaw: number;
  /** Elevation above horizontal, rad. */
  pitch: number;
}

export function forward(yaw: number): Vec3 {
  return { x: Math.sin(yaw), y: 0, z: -Math.cos(yaw) };
}
export function rightOf(yaw: number): Vec3 {
  return { x: Math.cos(yaw), y: 0, z: Math.sin(yaw) };
}

/** Shot-frame point to world, given the scope position and aim yaw. */
export function shotToWorld(p: { x: number; y: number; z: number }, eye: Vec3, yaw: number): Vec3 {
  const f = forward(yaw);
  const r = rightOf(yaw);
  return {
    x: eye.x + p.x * f.x + p.z * r.x,
    y: eye.y + p.y,
    z: eye.z + p.x * f.z + p.z * r.z,
  };
}

/** World point to shot frame. */
export function worldToShot(p: Vec3, eye: Vec3, yaw: number): Vec3 {
  const f = forward(yaw);
  const r = rightOf(yaw);
  const dx = p.x - eye.x;
  const dz = p.z - eye.z;
  return { x: dx * f.x + dz * f.z, y: p.y - eye.y, z: dx * r.x + dz * r.z };
}

/** World-frame vector (e.g. wind) expressed in the shot frame. */
export function vectorToShot(v: Vec3, yaw: number): Vec3 {
  const f = forward(yaw);
  const r = rightOf(yaw);
  return { x: v.x * f.x + v.z * f.z, y: v.y, z: v.x * r.x + v.z * r.z };
}

/**
 * Layered wind along a lane in world terms. The lane runs from `start` along `direction` (unit,
 * horizontal). Zone i covers lane distance [edges[i-1], edges[i]); the last zone runs on forever.
 */
export interface LaneWind {
  start: Vec3;
  direction: Vec3;
  /** Lane distances where zones end, ascending. zones.length = edges.length + 1. */
  edges: number[];
  /** World wind vector per zone, m/s. */
  zones: Vec3[];
  gust?: GustModel;
}

/** World wind at a point (no gust), for visual cues like grass and flags. */
export function laneWindAt(wind: LaneWind, p: Vec3): Vec3 {
  const s = (p.x - wind.start.x) * wind.direction.x + (p.z - wind.start.z) * wind.direction.z;
  let i = 0;
  while (i < wind.edges.length && s >= wind.edges[i]!) i++;
  return wind.zones[i]!;
}

/**
 * Converts lane wind to the solver's shot-frame WindField for a shot fired from `eye` with `yaw`.
 * Zone boundaries are mapped to downrange distances along the shot, so a shot fired from either
 * end of the lane sees the zones in the right order.
 */
export function shotWindField(wind: LaneWind, eye: Vec3, yaw: number): WindField {
  const f = forward(yaw);
  // Lane distance changes by `rate` per metre downrange.
  const rate = f.x * wind.direction.x + f.z * wind.direction.z;
  const s0 = (eye.x - wind.start.x) * wind.direction.x + (eye.z - wind.start.z) * wind.direction.z;
  const zoneAt = (s: number): number => {
    let i = 0;
    while (i < wind.edges.length && s >= wind.edges[i]!) i++;
    return i;
  };
  const zones: WindZone[] = [];
  if (Math.abs(rate) < 1e-6) {
    zones.push({ untilRange: Infinity, vector: vectorToShot(wind.zones[zoneAt(s0)]!, yaw) });
  } else {
    // Downrange distances where the shot crosses each edge, in flight order.
    const crossings = wind.edges
      .map((e) => (e - s0) / rate)
      .filter((d) => d > 0)
      .sort((a, b) => a - b);
    let prev = 0;
    for (const d of crossings) {
      const mid = s0 + rate * ((prev + d) / 2);
      zones.push({ untilRange: d, vector: vectorToShot(wind.zones[zoneAt(mid)]!, yaw) });
      prev = d;
    }
    zones.push({ untilRange: Infinity, vector: vectorToShot(wind.zones[zoneAt(s0 + rate * (prev + 1))]!, yaw) });
  }
  return wind.gust ? { zones, gust: wind.gust } : { zones };
}
