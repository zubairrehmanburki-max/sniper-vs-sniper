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

/** A man-made platform players stand on: the hit model treats its top as ground. */
export type Structure =
  | { kind: 'bridge'; s: number; deckY: number; halfSpan: number; halfWidth: number; pylonsX: number[] }
  | { kind: 'tower'; x: number; s: number; topY: number; halfSize: number; style: 'concrete' | 'rig' };

export interface DuelMap {
  id: string;
  name: string;
  /** One-line pitch for the map picker. */
  blurb?: string;
  laneLength: number;
  /**
   * Fixed-position duel: each side has one spot, nothing to hide behind and no move phases, so the
   * whole game is the shot. Zones collapse to that spot.
   */
  fixed?: boolean;
  /** Water surface height, if the map has open water (shots falling in it splash). */
  water?: number;
  /** Terrain as drawn, when the hit-test ground also includes platform tops; defaults to `ground`. */
  terrain?: Ground;
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
  look: { flags: Array<[number, number]>; laneHalfWidth: number; palette?: Palette; structures?: Structure[] };
}

/** Colours for drawing a map; anything left out uses the client's default green range. */
export interface Palette {
  /** Which photographic sky lights the map, and where its sun sits (degrees clockwise from the enemy's direction). */
  sky?: 'sunrise' | 'clear' | 'sunset';
  sunAzimuthDeg?: number;
  /** Ground hue/saturation/lightness in the lane and around it (0–1 each). */
  lane?: [number, number, number];
  outside?: [number, number, number];
  /** Ground below sea level (a harbor channel) in this colour. */
  water?: [number, number, number];
  grass?: string;
  hills?: string;
  rocks?: string;
  /** Trees or bushes on the land around the lane. */
  vegetation?: 'pine' | 'shrub' | 'none';
  /** Snow line height for the far mountains, m (none if absent). */
  snowAbove?: number;
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
        sky: 'clear',
        sunAzimuthDeg: 105,
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
        sky: 'sunrise',
        sunAzimuthDeg: 250,
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
        sky: 'clear',
        sunAzimuthDeg: 80,
        lane: [0.3, 0.18, 0.5],
        outside: [0.6, 0.08, 0.78],
        grass: '#8fa06a',
        hills: '#dfe6ec',
        rocks: '#7d7f82',
      },
    },
  };
}

// ---- fixed-position maps -------------------------------------------------------------------
// Both players stand in the open on a platform with nothing but water between them. No moving, no
// hiding: the duel is the wind call, the range and the trigger.

/** Hit-test ground: the terrain, with every platform top as a hard plateau. */
function withPlatforms(terrain: Ground, structures: Structure[]): Ground {
  return (x, z) => {
    const s = -z;
    let y = terrain(x, z);
    for (const st of structures) {
      if (st.kind === 'bridge') {
        if (Math.abs(x) <= st.halfSpan && Math.abs(s - st.s) <= st.halfWidth) y = Math.max(y, st.deckY);
      } else if (Math.abs(x - st.x) <= st.halfSize && Math.abs(s - st.s) <= st.halfSize) y = Math.max(y, st.topY);
    }
    return y;
  };
}

function fixedZones(a: Vec3, b: Vec3): Record<Side, Zone> {
  return {
    A: { xMin: a.x, xMax: a.x, sMin: -a.z, sMax: -a.z, front: -a.z },
    B: { xMin: b.x, xMax: b.x, sMin: -b.z, sMax: -b.z, front: -b.z },
  };
}

/**
 * Twin Bridges: two road bridges 640 m apart across a wide, slow estuary. Each sniper stands at
 * mid-span, 24 m over the water, with the whole river between them. Sea level, a steady wind
 * along the valley: a crosswind with nothing to break it.
 */
function makeTwinBridges(): DuelMap {
  const DECK = 24;
  const terrain: Ground = (x, z) => {
    const s = -z;
    const ax = Math.abs(x);
    // River bed between the banks, then the banks climb to the road level and on into hills.
    const bed = -8 * (1 - smoothstep(180, 230, ax));
    const bank = DECK * smoothstep(200, 290, ax);
    const hills = smoothstep(300, 700, ax) * (30 + 25 * Math.sin(ax * 0.004 + s * 0.003) + 12 * Math.cos(s * 0.007 + x * 0.002));
    const roll = 2 * Math.sin(s * 0.01) * smoothstep(240, 400, ax);
    return bed + bank + hills + roll;
  };
  const structures: Structure[] = [
    { kind: 'bridge', s: 0, deckY: DECK, halfSpan: 210, halfWidth: 7, pylonsX: [-90, 90] },
    { kind: 'bridge', s: 640, deckY: DECK, halfSpan: 210, halfWidth: 7, pylonsX: [-90, 90] },
  ];
  const a = { x: 0, y: DECK, z: -2 };
  const b = { x: 0, y: DECK, z: -638 };
  return {
    id: 'twin-bridges',
    name: 'Twin Bridges',
    blurb: 'Mid-span to mid-span across an estuary, 640 m',
    laneLength: 640,
    fixed: true,
    water: 0,
    terrain,
    zones: fixedZones(a, b),
    ground: withPlatforms(terrain, structures),
    cover: [],
    spots: { A: [a], B: [b] },
    windEdges: [215, 425],
    climate: {
      altitudeM: 4,
      temperatureC: [9, 24],
      humidity: [0.55, 0.9],
      windSpeedMps: [3, 8],
      windToward: Math.PI / 2,
      windSpread: 0.35,
      gustAmplitude: 0.2,
      gustPeriodSeconds: 11,
    },
    latitude: 48,
    look: {
      flags: [
        [-26, 110],
        [26, 320],
        [-26, 530],
      ],
      laneHalfWidth: 180,
      structures,
      palette: {
        sky: 'sunrise',
        sunAzimuthDeg: 125,
        lane: [0.3, 0.3, 0.42],
        outside: [0.27, 0.32, 0.38],
        water: [0.55, 0.4, 0.3],
        grass: '#7f9a4f',
        hills: '#6f8a66',
        rocks: '#8a8580',
        vegetation: 'pine',
      },
    },
  };
}

/**
 * Watchtowers: two concrete watchtowers standing in a flooded marsh, 450 m apart, open platforms
 * 22 m up. Reeds and dead pines all round, the air damp and almost still, then a gust.
 */
function makeWatchtowers(): DuelMap {
  const TOP = 22;
  const terrain: Ground = (x, z) => {
    const s = -z;
    const dx = x;
    const ds = s - 225;
    const r = Math.hypot(dx * 0.75, ds);
    // Marsh floor under the water in the middle, reed islands, then wooded shores beyond 360 m.
    const floor = -3;
    const shore = 5 * smoothstep(340, 460, r) + smoothstep(420, 800, r) * (22 + 14 * Math.sin(x * 0.006 + s * 0.004) + 8 * Math.cos(s * 0.009));
    const islets = 0.9 * Math.max(0, Math.sin(x * 0.045) * Math.sin(s * 0.052) - 0.55) * 8 * (1 - smoothstep(300, 340, r));
    return floor + shore + islets;
  };
  const structures: Structure[] = [
    { kind: 'tower', x: 0, s: 0, topY: TOP, halfSize: 3, style: 'concrete' },
    { kind: 'tower', x: 0, s: 450, topY: TOP, halfSize: 3, style: 'concrete' },
  ];
  const a = { x: 0, y: TOP, z: -1.2 };
  const b = { x: 0, y: TOP, z: -448.8 };
  return {
    id: 'watchtowers',
    name: 'Watchtowers',
    blurb: 'Tower to tower over a drowned marsh, 450 m',
    laneLength: 450,
    fixed: true,
    water: 0,
    terrain,
    zones: fixedZones(a, b),
    ground: withPlatforms(terrain, structures),
    cover: [],
    spots: { A: [a], B: [b] },
    windEdges: [150, 300],
    climate: {
      altitudeM: 180,
      temperatureC: [4, 18],
      humidity: [0.7, 0.98],
      windSpeedMps: [0.5, 5],
      windToward: Math.PI / 2,
      windSpread: 1.2,
      gustAmplitude: 0.55,
      gustPeriodSeconds: 7,
    },
    latitude: 52,
    look: {
      flags: [
        [-30, 90],
        [34, 225],
        [-30, 360],
      ],
      laneHalfWidth: 160,
      structures,
      palette: {
        sky: 'clear',
        sunAzimuthDeg: 95,
        lane: [0.22, 0.28, 0.34],
        outside: [0.2, 0.3, 0.3],
        water: [0.46, 0.25, 0.22],
        grass: '#9a9b55',
        hills: '#55684f',
        rocks: '#7d7c74',
        vegetation: 'pine',
      },
    },
  };
}

/**
 * Oil Rigs: two production platforms 900 m apart in open sea, helidecks 34 m above the swell.
 * Nothing between them but wind: strong, steady and cold. The .338 and .50 map.
 */
function makeOilRigs(): DuelMap {
  const TOP = 34;
  const terrain: Ground = () => -40;
  const structures: Structure[] = [
    { kind: 'tower', x: 0, s: 0, topY: TOP, halfSize: 18, style: 'rig' },
    { kind: 'tower', x: 0, s: 900, topY: TOP, halfSize: 18, style: 'rig' },
  ];
  const a = { x: 0, y: TOP, z: -14 };
  const b = { x: 0, y: TOP, z: -886 };
  return {
    id: 'oil-rigs',
    name: 'Oil Rigs',
    blurb: 'Helideck to helideck in the open sea, 870 m',
    laneLength: 900,
    fixed: true,
    water: 0,
    terrain,
    zones: fixedZones(a, b),
    ground: withPlatforms(terrain, structures),
    cover: [],
    spots: { A: [a], B: [b] },
    windEdges: [300, 600],
    climate: {
      altitudeM: 0,
      temperatureC: [2, 14],
      humidity: [0.7, 0.95],
      windSpeedMps: [6, 13],
      windToward: Math.PI / 2,
      windSpread: 0.3,
      gustAmplitude: 0.18,
      gustPeriodSeconds: 16,
    },
    latitude: 58,
    look: {
      flags: [
        [-24, 150],
        [24, 450],
        [-24, 750],
      ],
      laneHalfWidth: 200,
      structures,
      palette: {
        sky: 'sunset',
        sunAzimuthDeg: 95,
        lane: [0.58, 0.3, 0.22],
        outside: [0.58, 0.3, 0.22],
        water: [0.58, 0.32, 0.2],
        grass: '#7f8f70',
        hills: '#6c7a84',
        rocks: '#777',
        vegetation: 'none',
      },
    },
  };
}

export const MAPS: Record<string, DuelMap> = {
  'twin-bridges': makeTwinBridges(),
  watchtowers: makeWatchtowers(),
  'oil-rigs': makeOilRigs(),
  'dry-valley': makeDryValley(),
  'harbor-rooftops': makeHarborRooftops(),
  'alpine-ridge': makeAlpineRidge(),
  'training-lane': makeTrainingLane(),
};
export const DEFAULT_MAP = 'twin-bridges';

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
