/**
 * Duel maps. A map is two opposing zones with no-man's-land between. Lane coordinate s runs from
 * player A's rear (s = 0, world z = 0) toward player B (world z = −s). A looks north (yaw 0), B south.
 */
import type { Vec3 } from '@svs/ballistics';
import type { Box } from './geometry.ts';
import type { Ground } from './hits.ts';

export type Side = 'A' | 'B';

export interface Zone {
  /** Lateral extent, world x. */
  xMin: number;
  xMax: number;
  /** Lane-distance extent (s = −z). */
  sMin: number;
  sMax: number;
  /** Lane distance of the edge facing the enemy. */
  front: number;
}

export interface DuelMap {
  id: string;
  name: string;
  laneLength: number;
  zones: Record<Side, Zone>;
  ground: Ground;
  cover: Box[];
  /** Named firing positions per side (bots use these; humans may stand anywhere in the zone). */
  spots: Record<Side, Vec3[]>;
  /** Lane distances where the wind zones change (shooter / mid / target thirds). */
  windEdges: number[];
  climate: {
    altitudeM: number;
    temperatureC: [number, number];
    humidity: [number, number];
    windSpeedMps: [number, number];
    /** Mean direction the wind blows toward, rad from north toward east, and its spread. */
    windToward: number;
    windSpread: number;
    gustAmplitude: number;
    gustPeriodSeconds: number;
  };
  latitude: number;
}

const smoothstep = (e0: number, e1: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - e0) / (e1 - e0)));
  return t * t * (3 - 2 * t);
};

/**
 * Training Lane: 650 m, A on a 3 m rise, B on a 5 m rise, a shallow valley with rolling dead ground
 * between. Placeholder geometry for the Phase 2 hotseat; Dry Valley replaces it in Phase 4.
 */
function trainingLaneGround(x: number, z: number): number {
  const s = -z;
  const a = 3 * (1 - smoothstep(85, 170, s));
  const b = 5 * smoothstep(470, 565, s);
  const valley = smoothstep(150, 220, s) * (1 - smoothstep(440, 500, s));
  const ripples = valley * (0.9 * Math.sin(x * 0.045 + 1.3) * Math.sin(s * 0.028) + 0.4 * Math.sin(s * 0.09 + x * 0.02));
  const outside = smoothstep(70, 220, Math.abs(x));
  const hills = outside * (10 + 8 * Math.sin(x * 0.01 + s * 0.006) + 6 * Math.cos(s * 0.012));
  return a + b + ripples + hills;
}

function rock(id: string, x: number, s: number, w: number, h: number, d: number, ground: Ground): Box {
  const y = ground(x, -s);
  return { id, min: { x: x - w / 2, y: y - 0.2, z: -s - d / 2 }, max: { x: x + w / 2, y: y + h, z: -s + d / 2 }, material: 'hard' };
}

function makeTrainingLane(): DuelMap {
  const ground = trainingLaneGround;
  const cover: Box[] = [];
  const spots: Record<Side, Vec3[]> = { A: [], B: [] };
  // Five positions per side, staggered so no single spot sees everything.
  const layoutA: Array<[number, number, number]> = [
    [-38, 70, 1.3],
    [-16, 78, 0.9],
    [4, 64, 1.4],
    [24, 76, 1.0],
    [42, 66, 1.2],
  ];
  layoutA.forEach(([x, s, h], i) => {
    cover.push(rock(`A-rock-${i}`, x, s, 2.2, h, 1.4, ground));
    spots.A.push({ x: x + (i % 2 === 0 ? 1.6 : -1.6), y: ground(x, -(s - 1.6)), z: -(s - 1.6) });
  });
  const layoutB: Array<[number, number, number]> = [
    [-42, 582, 1.2],
    [-20, 574, 1.4],
    [0, 588, 1.0],
    [18, 572, 1.3],
    [40, 584, 0.9],
  ];
  layoutB.forEach(([x, s, h], i) => {
    cover.push(rock(`B-rock-${i}`, x, s, 2.2, h, 1.4, ground));
    spots.B.push({ x: x + (i % 2 === 0 ? -1.6 : 1.6), y: ground(x, -(s + 1.6)), z: -(s + 1.6) });
  });
  return {
    id: 'training-lane',
    name: 'Training Lane',
    laneLength: 650,
    zones: {
      A: { xMin: -55, xMax: 55, sMin: 0, sMax: 80, front: 80 },
      B: { xMin: -55, xMax: 55, sMin: 570, sMax: 650, front: 570 },
    },
    ground,
    cover,
    spots,
    windEdges: [217, 433],
    climate: {
      altitudeM: 400,
      temperatureC: [8, 30],
      humidity: [0.25, 0.75],
      windSpeedMps: [1.5, 7],
      windToward: Math.PI / 2,
      windSpread: 0.6,
      gustAmplitude: 0.3,
      gustPeriodSeconds: 10,
    },
    latitude: 38,
  };
}

export const MAPS: Record<string, DuelMap> = { 'training-lane': makeTrainingLane() };

export function mapById(id: string): DuelMap {
  const m = MAPS[id];
  if (!m) throw new Error(`Unknown map ${id}`);
  return m;
}

export const laneDistance = (p: { z: number }): number => -p.z;

/** The usable part of a side's zone: during sudden death it shrinks toward the front edge. */
export function usableZone(map: DuelMap, side: Side, shrink: number, minDepth: number): Zone {
  const z = map.zones[side];
  const depth = Math.max(minDepth, (z.sMax - z.sMin) * (1 - shrink));
  return side === 'A' ? { ...z, sMin: z.front - depth, sMax: z.front } : { ...z, sMin: z.front, sMax: z.front + depth };
}

/** Clamp a world position into a zone and onto the ground, outside any cover box. */
export function clampToZone(map: DuelMap, zone: Zone, p: Vec3): Vec3 {
  let x = Math.min(zone.xMax, Math.max(zone.xMin, p.x));
  let s = Math.min(zone.sMax, Math.max(zone.sMin, -p.z));
  for (const box of map.cover) {
    const inside = x > box.min.x && x < box.max.x && -s > box.min.z && -s < box.max.z;
    if (inside) {
      // Step out the short way, toward the player's own rear.
      const toLeft = x - box.min.x;
      const toRight = box.max.x - x;
      const dz = Math.min(-s - box.min.z, box.max.z + s);
      if (dz <= Math.min(toLeft, toRight)) {
        const rear = zone.front === zone.sMax ? box.max.z + 0.3 : box.min.z - 0.3; // A's rear is +z
        s = -rear;
      } else {
        x = toLeft < toRight ? box.min.x - 0.3 : box.max.x + 0.3;
      }
    }
  }
  s = Math.min(zone.sMax, Math.max(zone.sMin, s));
  return { x, y: map.ground(x, -s), z: -s };
}
