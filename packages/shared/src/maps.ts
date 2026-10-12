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
  /** How the map is drawn: wind flags (world x, lane distance s) and an optional palette. */
  look: { flags: Array<[number, number]>; laneHalfWidth: number; palette?: Palette };
}

/** Colours for drawing a map; anything left out uses the client's default green range. */
export interface Palette {
  sky?: string;
  /** Ground hue/saturation/lightness in the lane and around it (0–1 each). */
  lane?: [number, number, number];
  outside?: [number, number, number];
  /** Ground below sea level (a harbor channel) in this colour. */
  water?: [number, number, number];
  grass?: string;
  hills?: string;
  rocks?: string;
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
    look: {
      flags: [
        [-62, 120],
        [64, 325],
        [-66, 530],
      ],
      laneHalfWidth: 70,
    },
  };
}

/**
 * Dry Valley (GAME_PLAN launch map): 690–870 m across a dry wash at 1,200 m, hot and thin-aired,
 * with a strong, steady crosswind. A sits on a low bench, B on a higher one with a wash between.
 */
function dryValleyGround(x: number, z: number): number {
  const s = -z;
  const benchA = 6 * (1 - smoothstep(95, 180, s));
  const benchB = 11 * smoothstep(650, 770, s);
  // The wash: a shallow channel snaking across mid-lane, with gravel bars either side.
  const wash = -3.2 * Math.exp(-(((s - 430 - 25 * Math.sin(x * 0.02)) / 38) ** 2));
  const bars = 0.8 * Math.sin(s * 0.035 + x * 0.015) * smoothstep(180, 260, s) * (1 - smoothstep(600, 660, s));
  const outside = smoothstep(80, 240, Math.abs(x));
  const ridges = outside * (16 + 12 * Math.sin(x * 0.008 + s * 0.004) + 7 * Math.cos(s * 0.01));
  return benchA + benchB + wash + bars + ridges;
}

function makeDryValley(): DuelMap {
  const ground = dryValleyGround;
  const cover: Box[] = [];
  const spots: Record<Side, Vec3[]> = { A: [], B: [] };
  // Six boulders a side: two pairs close together (a short dash between them) and two loners.
  const layoutA: Array<[number, number, number, number]> = [
    [-46, 76, 1.4, 2.6],
    [-40, 64, 1.0, 2.0],
    [-12, 84, 1.2, 2.4],
    [14, 70, 1.6, 3.0],
    [20, 58, 1.0, 1.8],
    [44, 80, 1.3, 2.2],
  ];
  layoutA.forEach(([x, s, h, w], i) => {
    cover.push(rock(`A-rock-${i}`, x, s, w, h, 1.6, ground));
    spots.A.push({ x: x + (i % 2 === 0 ? 1.8 : -1.8), y: ground(x, -(s - 1.8)), z: -(s - 1.8) });
  });
  const layoutB: Array<[number, number, number, number]> = [
    [-44, 792, 1.3, 2.4],
    [-18, 784, 1.5, 2.8],
    [-12, 800, 1.0, 1.8],
    [10, 790, 1.2, 2.2],
    [36, 786, 1.6, 3.0],
    [42, 802, 1.0, 2.0],
  ];
  layoutB.forEach(([x, s, h, w], i) => {
    cover.push(rock(`B-rock-${i}`, x, s, w, h, 1.6, ground));
    spots.B.push({ x: x + (i % 2 === 0 ? -1.8 : 1.8), y: ground(x, -(s + 1.8)), z: -(s + 1.8) });
  });
  return {
    id: 'dry-valley',
    name: 'Dry Valley',
    laneLength: 870,
    zones: {
      A: { xMin: -60, xMax: 60, sMin: 0, sMax: 90, front: 90 },
      B: { xMin: -60, xMax: 60, sMin: 780, sMax: 870, front: 780 },
    },
    ground,
    cover,
    spots,
    windEdges: [290, 580],
    climate: {
      altitudeM: 1200,
      temperatureC: [26, 42],
      humidity: [0.08, 0.3],
      windSpeedMps: [5, 10],
      windToward: Math.PI / 2,
      windSpread: 0.25,
      gustAmplitude: 0.12,
      gustPeriodSeconds: 14,
    },
    latitude: 34,
    look: {
      flags: [
        [-70, 160],
        [72, 440],
        [-74, 700],
      ],
      laneHalfWidth: 75,
      palette: {
        sky: '#c7d6e2',
        lane: [0.09, 0.32, 0.56],
        outside: [0.08, 0.28, 0.5],
        grass: '#b9a66b',
        hills: '#a88d6c',
        rocks: '#9c8165',
      },
    },
  };
}

/** A box standing on the ground at (x, s): w wide (x), d deep (along the lane), h tall. */
function block(id: string, x: number, s: number, w: number, d: number, h: number, ground: Ground, material: Box['material'], look: Box['look'], base?: number): Box {
  const y = base ?? ground(x, -s);
  return { id, min: { x: x - w / 2, y: y - 0.05, z: -s - d / 2 }, max: { x: x + w / 2, y: y + h, z: -s + d / 2 }, material, look };
}

/**
 * Harbor Rooftops (GAME_PLAN launch map): two flat roofs facing each other across quays and a
 * channel, 330–440 m. A's roof is 12 m up, B's 16 m. Parapets hide a prone or crouching shooter;
 * air-conditioning units are sheet metal, so they hide you but a .338 or .50 goes straight through.
 * Light wind that swirls between the buildings.
 */
const ROOF_A = 12;
const ROOF_B = 16;
function harborGround(x: number, z: number): number {
  const s = -z;
  const inX = 1 - smoothstep(46, 48, Math.abs(x));
  const roofA = ROOF_A * inX * (1 - smoothstep(56, 58, s)) * smoothstep(-14, -12, s);
  const roofB = ROOF_B * inX * smoothstep(380, 382, s) * (1 - smoothstep(452, 454, s));
  const channel = -3 * smoothstep(150, 158, s) * (1 - smoothstep(282, 290, s));
  return Math.max(roofA, roofB) + channel;
}

function makeHarborRooftops(): DuelMap {
  const ground = harborGround;
  const cover: Box[] = [];
  const spots: Record<Side, Vec3[]> = { A: [], B: [] };
  const roof = (side: Side): number => (side === 'A' ? ROOF_A : ROOF_B);
  // Front parapets with gaps (s is the parapet line, dir points toward the enemy).
  const parapet = (side: Side, s: number): void => {
    const segs: Array<[number, number]> = [
      [-46, -30],
      [-26, -6],
      [-2, 18],
      [22, 46],
    ];
    segs.forEach(([x0, x1], i) =>
      cover.push(block(`${side}-parapet-${i}`, (x0 + x1) / 2, s, x1 - x0, 0.35, 1.05, ground, 'hard', 'concrete', roof(side))),
    );
  };
  parapet('A', 56.6);
  parapet('B', 381.4);
  // Roof furniture: stair housings (concrete) and AC units (sheet metal).
  const furniture: Array<[Side, string, number, number, number, number, number, Box['material'], Box['look']]> = [
    ['A', 'stairs', -30, 30, 3.2, 3.2, 2.8, 'hard', 'concrete'],
    ['A', 'ac', -8, 46, 2.2, 1.4, 1.5, 'soft', 'metal'],
    ['A', 'ac', 12, 38, 2.2, 1.4, 1.5, 'soft', 'metal'],
    ['A', 'stairs', 30, 22, 3.2, 3.2, 2.8, 'hard', 'concrete'],
    ['A', 'ac', 36, 48, 2.2, 1.4, 1.5, 'soft', 'metal'],
    ['B', 'stairs', 28, 408, 3.2, 3.2, 2.8, 'hard', 'concrete'],
    ['B', 'ac', 6, 392, 2.2, 1.4, 1.5, 'soft', 'metal'],
    ['B', 'ac', -14, 400, 2.2, 1.4, 1.5, 'soft', 'metal'],
    ['B', 'stairs', -32, 418, 3.2, 3.2, 2.8, 'hard', 'concrete'],
    ['B', 'ac', -36, 392, 2.2, 1.4, 1.5, 'soft', 'metal'],
  ];
  furniture.forEach(([side, kind, x, sd, w, d, h, material, look], i) => {
    cover.push(block(`${side}-${kind}-${i}`, x, sd, w, d, h, ground, material, look, roof(side)));
    // Firing spots: beside the AC units (peek round the metal), not behind the stairs (blind).
    if (kind === 'ac') spots[side].push({ x: x + w / 2 + 0.9, y: roof(side), z: -sd });
  });
  // And the gaps in the parapets, the best views and the most exposed.
  for (const x of [-28, -4, 20]) {
    spots.A.push({ x, y: ROOF_A, z: -55 });
    spots.B.push({ x, y: ROOF_B, z: -383 });
  }
  // Buildings on the quays break up the sight lines between the roofs.
  cover.push(block('warehouse', -24, 100, 30, 26, 10, ground, 'hard', 'concrete'));
  cover.push(block('crane-tower', 32, 128, 10, 10, 26, ground, 'hard', 'concrete'));
  cover.push(block('office', -30, 312, 22, 18, 25, ground, 'hard', 'concrete'));
  cover.push(block('shed', 22, 330, 18, 12, 7, ground, 'hard', 'concrete'));
  return {
    id: 'harbor-rooftops',
    name: 'Harbor Rooftops',
    laneLength: 450,
    zones: {
      A: { xMin: -42, xMax: 42, sMin: 4, sMax: 54, front: 54 },
      B: { xMin: -42, xMax: 42, sMin: 384, sMax: 434, front: 384 },
    },
    ground,
    cover,
    spots,
    windEdges: [150, 300],
    climate: {
      altitudeM: 5,
      temperatureC: [12, 26],
      humidity: [0.65, 0.95],
      windSpeedMps: [1, 4.5],
      windToward: Math.PI / 2,
      windSpread: 1.6,
      gustAmplitude: 0.5,
      gustPeriodSeconds: 6,
    },
    latitude: 44,
    look: {
      flags: [
        [-52, 60],
        [55, 230],
        [-52, 375],
      ],
      laneHalfWidth: 60,
      palette: {
        sky: '#b9c8d6',
        lane: [0.08, 0.05, 0.5],
        outside: [0.58, 0.25, 0.42],
        water: [0.56, 0.35, 0.32],
        grass: '#8f9b6b',
        hills: '#7f8a93',
        rocks: '#8d8a85',
      },
    },
  };
}

/**
 * Alpine Ridge (GAME_PLAN launch map): 900–1,100 m from a shoulder at 2,400 m, across a gully, up
 * to positions 110–150 m higher on the far slope. Cold, thin air shoots flat; the climb makes every shot an incline shot;
 * gusty wind. Log piles are wood: cover a .50 shoots through.
 */
function alpineGround(x: number, z: number): number {
  const s = -z;
  const shoulder = 6 * (1 - smoothstep(60, 160, s));
  const gully = -55 * smoothstep(150, 420, s) * (1 - smoothstep(560, 860, s));
  // A steady climb from the gully floor to the summit behind B, so B's zone faces A down the slope
  // instead of hiding behind a crest.
  const climb = Math.min(1, Math.max(0, (s - 560) / 560));
  const ridge = 150 * climb ** 1.15;
  const bumps = 1.2 * Math.sin(x * 0.05 + s * 0.02) * Math.cos(s * 0.013);
  const flanks = smoothstep(90, 260, Math.abs(x)) * (40 + 30 * Math.sin(x * 0.006 + s * 0.003));
  return shoulder + gully + ridge + bumps + flanks;
}

function makeAlpineRidge(): DuelMap {
  const ground = alpineGround;
  const cover: Box[] = [];
  const spots: Record<Side, Vec3[]> = { A: [], B: [] };
  const layoutA: Array<[number, number, number, number, Box['material']]> = [
    [-44, 82, 1.6, 2.8, 'hard'],
    [-20, 66, 1.1, 2.2, 'hard'],
    [0, 88, 1.0, 3.6, 'soft'],
    [22, 72, 1.5, 2.6, 'hard'],
    [46, 86, 1.2, 2.2, 'hard'],
  ];
  layoutA.forEach(([x, s, h, w, material], i) => {
    cover.push(block(`A-cover-${i}`, x, s, w, 1.6, h, ground, material, material === 'soft' ? 'wood' : 'rock'));
    spots.A.push({ x: x + (i % 2 === 0 ? 1.9 : -1.9), y: ground(x, -(s - 1.9)), z: -(s - 1.9) });
  });
  const layoutB: Array<[number, number, number, number, Box['material']]> = [
    [-42, 1012, 1.4, 2.6, 'hard'],
    [-16, 1026, 1.2, 2.4, 'hard'],
    [4, 1008, 1.0, 3.6, 'soft'],
    [24, 1020, 1.6, 2.8, 'hard'],
    [44, 1010, 1.1, 2.0, 'hard'],
  ];
  layoutB.forEach(([x, s, h, w, material], i) => {
    cover.push(block(`B-cover-${i}`, x, s, w, 1.6, h, ground, material, material === 'soft' ? 'wood' : 'rock'));
    spots.B.push({ x: x + (i % 2 === 0 ? -1.9 : 1.9), y: ground(x, -(s + 1.9)), z: -(s + 1.9) });
  });
  return {
    id: 'alpine-ridge',
    name: 'Alpine Ridge',
    laneLength: 1100,
    zones: {
      A: { xMin: -60, xMax: 60, sMin: 0, sMax: 95, front: 95 },
      B: { xMin: -60, xMax: 60, sMin: 1000, sMax: 1095, front: 1000 },
    },
    ground,
    cover,
    spots,
    windEdges: [370, 730],
    climate: {
      altitudeM: 2400,
      temperatureC: [-10, 8],
      humidity: [0.3, 0.7],
      windSpeedMps: [2, 9],
      windToward: Math.PI / 2,
      windSpread: 0.7,
      gustAmplitude: 0.45,
      gustPeriodSeconds: 8,
    },
    latitude: 46,
    look: {
      flags: [
        [-72, 200],
        [74, 560],
        [-74, 930],
      ],
      laneHalfWidth: 75,
      palette: {
        sky: '#9fc3e8',
        lane: [0.3, 0.18, 0.5],
        outside: [0.6, 0.08, 0.78],
        grass: '#8fa06a',
        hills: '#dfe6ec',
        rocks: '#7d7f82',
      },
    },
  };
}

export const MAPS: Record<string, DuelMap> = {
  'dry-valley': makeDryValley(),
  'harbor-rooftops': makeHarborRooftops(),
  'alpine-ridge': makeAlpineRidge(),
  'training-lane': makeTrainingLane(),
};
export const DEFAULT_MAP = 'dry-valley';

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
