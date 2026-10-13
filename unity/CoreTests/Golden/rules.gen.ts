/**
 * Writes rules.json: outputs of the TypeScript game rules (packages/shared) that the C# port in
 * unity/Assets/Scripts/Core/Rules must reproduce. Run from the repo root:
 *   node_modules/.bin/tsx unity/CoreTests/Golden/rules.gen.ts
 *
 * Optional fields are written as explicit nulls (never omitted) so the C# side can build the same
 * JSON shape and compare it structurally. Infinity becomes null, as JSON.stringify does.
 */
import { writeFileSync } from 'node:fs';
import { solveFiringSolution, type Vec3 } from '../../../packages/ballistics/src/index.ts';
import {
  MAPS,
  activeConditions,
  applyInput,
  botPlan,
  breathFactor,
  chestOf,
  clampToZone,
  config,
  createMatch,
  createPracticeRange,
  eyeOf,
  facingYaw,
  holdBreath,
  initialBreath,
  lineOfSight,
  mapById,
  other,
  rangefind,
  rateMatch,
  releaseBreath,
  resolveShot,
  rifleById,
  rifleShot,
  rollConditions,
  shotWindField,
  silhouetteFor,
  swayOffset,
  usableZone,
  expectedScore,
  type BotDifficulty,
  type Conditions,
  type FireInput,
  type LaneWind,
  type MatchInput,
  type MatchState,
  type ShotOutcome,
  type Side,
  type Stance,
} from '../../../packages/shared/src/index.ts';

const v = (p: Vec3 | null | undefined): number[] | null => (p ? [p.x, p.y, p.z] : null);
const SIDES: Side[] = ['A', 'B'];
const STANCES: Stance[] = ['prone', 'crouch', 'standing'];
const RIFLES = config.rifles.map((r) => r.id);

function conditionsSummary(c: Conditions) {
  return {
    atmosphere: [c.atmosphere.altitudeM, c.atmosphere.pressureHPa, c.atmosphere.temperatureC, c.atmosphere.humidity],
    zones: c.wind.zones.map(v),
    edges: c.wind.edges,
    gust: c.wind.gust ? [c.wind.gust.amplitude, c.wind.gust.period, c.wind.gust.seed] : null,
    latitude: c.latitude,
  };
}

function stateSummary(s: MatchState) {
  return {
    round: s.round,
    pair: s.pair,
    phase: s.phase,
    actor: s.actor,
    moveFrom: v(s.moveFrom),
    phaseStart: s.phaseStart,
    phaseEnd: s.phaseEnd,
    acted: s.acted,
    firstMover: s.firstMover,
    step: s.step,
    windScale: s.windScale,
    shotCount: s.shotCount,
    roundWinner: s.roundWinner,
    winner: s.winner,
    forfeit: s.forfeit,
    players: SIDES.map((side) => {
      const p = s.players[side];
      return [v(p.position), p.stance, p.kills, p.wounds, v(p.revealed), p.coldBore, v(p.heardSnapFrom), p.ready, p.idlePhases];
    }),
  };
}

function outcomeSummary(o: ShotOutcome | undefined) {
  if (!o) return null;
  const i = o.impact;
  return {
    verdict: o.verdict,
    zone: o.zone ?? null,
    kind: i.kind,
    targetId: i.targetId ?? null,
    coverId: i.coverId ?? null,
    point: v(i.point),
    t: i.t,
    speed: i.speed,
    energy: i.energy,
    local: i.local ? [i.local.u, i.local.v] : null,
    penetrated: i.penetrated ?? null,
    nearMiss: o.nearMiss,
    call: o.call ? [o.call.upMrad, o.call.rightMrad] : null,
    eye: v(o.eye),
    seed: o.seed,
  };
}

function inputSummary(i: MatchInput) {
  return {
    type: i.type,
    time: i.time,
    side: 'side' in i ? i.side : null,
    position: i.type === 'place' ? v(i.position) : null,
    stance: i.type === 'place' ? i.stance : null,
    fire: i.type === 'fire' ? [i.input.yaw, i.input.pitch, i.input.dialElevationMrad, i.input.dialWindageMrad, i.input.cant ?? 0] : null,
  };
}

// ---- maps ----------------------------------------------------------------------------------
const maps = Object.values(MAPS).map((m) => {
  const groundSamples: number[][] = [];
  for (const x of [-150, -47, -12.5, 0, 3.3, 46, 210]) {
    for (const s of [-5, 0, 2, 55, 130, 225, 431, 640, 800, 1010]) groundSamples.push([x, -s, m.ground(x, -s)]);
  }
  const clamps: number[][] = [];
  for (const side of SIDES) {
    const z = m.zones[side];
    for (const [x, s] of [[0, z.sMin], [-500, z.front], [7.3, (z.sMin + z.sMax) / 2], [-38, 70.4], [-16, 78], [100, 1e4], [-8, 46], [28, 408]]) {
      const p = clampToZone(m, z, { x: x!, y: 0, z: -s! });
      clamps.push([x!, s!, p.x, p.y, p.z]);
    }
    for (const shrink of [0, 0.3, 1]) {
      const u = usableZone(m, side, shrink, config.rules.minZoneDepthM);
      clamps.push([shrink, u.xMin, u.xMax, u.sMin, u.sMax, u.front]);
    }
  }
  const los: boolean[] = [];
  for (const a of m.spots.A) for (const b of m.spots.B) for (const st of STANCES) los.push(lineOfSight(eyeOf(a, st), eyeOf(b, st), m.ground, m.cover));
  return {
    id: m.id,
    laneLength: m.laneLength,
    fixed: m.fixed ?? false,
    zones: SIDES.map((s) => [m.zones[s].xMin, m.zones[s].xMax, m.zones[s].sMin, m.zones[s].sMax, m.zones[s].front]),
    cover: m.cover.map((b) => ({ id: b.id, min: v(b.min), max: v(b.max), material: b.material, look: b.look ?? null })),
    spots: SIDES.map((s) => m.spots[s].map(v)),
    groundSamples,
    clamps,
    los,
    conditions: [1, 2, 3].map((round) => conditionsSummary(rollConditions(m, 4242, round))),
  };
});

// ---- wind fields ---------------------------------------------------------------------------
const lane: LaneWind = {
  start: { x: 0, y: 0, z: 0 },
  direction: { x: 0, y: 0, z: -1 },
  edges: [300, 600],
  zones: [
    { x: 1, y: 0, z: 0.5 },
    { x: 2, y: 0, z: -0.3 },
    { x: 3, y: 0.1, z: 0 },
  ],
  gust: { amplitude: 0.3, period: 7, seed: 5 },
};
const windFields = [
  [0, 0, 0],
  [0, -900, Math.PI],
  [12, -450, 0.2],
  [-30, -200, Math.PI - 0.15],
  [5, -350, Math.PI / 2],
].map(([x, z, yaw]) => {
  const f = shotWindField(lane, { x: x!, y: 1, z: z! }, yaw!);
  return { x, z, yaw, zones: f.zones.map((zn) => [zn.untilRange, ...v(zn.vector)!]) };
});

// ---- sway ----------------------------------------------------------------------------------
const sway: number[][] = [];
for (const stance of STANCES) {
  for (const bipod of [false, true]) {
    for (const t of [0, 0.37, 2.5, 10.1, 33.3]) {
      const o = swayOffset({ stance, bipod, breath: initialBreath(), seed: 1234567, penalty: bipod ? 1 : 1.4 }, t);
      sway.push([STANCES.indexOf(stance), bipod ? 1 : 0, t, o.right, o.up]);
    }
  }
}
const held = holdBreath(initialBreath(), 3);
const released = releaseBreath(held, 12);
const rehold = holdBreath(released, 13);
const breath = {
  held: [held.since, held.strainAtRelease],
  released: [released.since, released.strainAtRelease],
  rehold: [rehold.since, rehold.strainAtRelease],
  factors: [0, 3.2, 3.6, 5, 8.1, 9, 12, 14, 30].map((t) => [breathFactor(held, t), breathFactor(released, t), breathFactor(rehold, t)]),
  maxSeed: [swayOffset({ stance: 'crouch', bipod: false, breath: initialBreath(), seed: 4294967295 }, 7.7)].map((o) => [o.right, o.up])[0],
};

// ---- shots per map -------------------------------------------------------------------------
function perfectInput(rifleId: string, eye: Vec3, aim: Vec3, c: Conditions, time: number): FireInput {
  const dx = aim.x - eye.x;
  const dz = aim.z - eye.z;
  const horiz = Math.hypot(dx, dz);
  const yaw = Math.atan2(dx, -dz);
  const pitch = Math.atan2(aim.y - eye.y, horiz);
  const sol = solveFiringSolution(
    { ...rifleShot(rifleById(rifleId)), losElevation: pitch },
    { atmosphere: c.atmosphere, wind: shotWindField(c.wind, eye, yaw) },
    Math.hypot(horiz, aim.y - eye.y),
    { fireTime: time },
  );
  return { yaw, pitch, dialElevationMrad: sol.elevation * 1000, dialWindageMrad: sol.windage * 1000 };
}

const OFFSETS: Array<[string, number, number]> = [
  ['chest', 0, 0],
  ['head', 0, 0],
  ['high', 0.5, 0],
  ['low', -1, 0],
  ['right', 0, 1],
  ['short', -3, 0],
  ['wide', 2, -2],
  ['near', 0.25, 0.2],
];
const shots = Object.values(MAPS).flatMap((m) => {
  const c = rollConditions(m, 99, 1);
  return SIDES.flatMap((side) => {
    const shooterPos = m.spots[side][0]!;
    const targetSide = other(side);
    const targetPos = m.spots[targetSide][m.spots[targetSide].length - 1]!;
    return OFFSETS.map(([name, up, right], i) => {
      const rifleId = RIFLES[i % RIFLES.length]!;
      const shooterStance: Stance = STANCES[i % 3]!;
      const targetStance: Stance = STANCES[(i + 1) % 3]!;
      const eye = eyeOf(shooterPos, shooterStance);
      const sil = silhouetteFor(targetSide, targetPos, targetStance, facingYaw(targetSide));
      const aim = name === 'head' ? eyeOf(targetPos, targetStance) : chestOf(sil);
      const fireTime = 10 + i * 3.7;
      const base = perfectInput(rifleId, eye, aim, c, fireTime);
      const input = { ...base, dialElevationMrad: base.dialElevationMrad + up, dialWindageMrad: base.dialWindageMrad + right, cant: i === 7 ? 0.01 : undefined };
      const outcome = resolveShot({
        map: m,
        conditions: c,
        rifleId,
        shooter: { position: shooterPos, stance: shooterStance },
        target: { id: targetSide, side: targetSide, position: targetPos, stance: targetStance, wounds: i % 2 },
        input,
        seed: 99,
        shotIndex: i + 1,
        fireTime,
        coldBore: i % 3 === 0,
      });
      const ranges = [0, -0.002, -0.01, -0.05].map((dp) => rangefind(eye, { yaw: base.yaw, pitch: base.pitch + dp }, [sil], m.ground, 3000, m.cover) ?? null);
      return {
        map: m.id,
        side,
        name,
        rifleId,
        shooterStance,
        targetStance,
        wounds: i % 2,
        coldBore: i % 3 === 0,
        fireTime,
        shotIndex: i + 1,
        input: [input.yaw, input.pitch, input.dialElevationMrad, input.dialWindageMrad, input.cant ?? 0],
        outcome: outcomeSummary(outcome),
        ranges,
      };
    });
  });
});

// ---- practice range and Elo ----------------------------------------------------------------
const practice = [0, 1, 5, 6, 123456789].map((seed) => {
  const r = createPracticeRange(seed);
  return {
    seed,
    targets: r.targets.map((t) => ({ id: t.id, position: v(t.position), facingYaw: t.facingYaw, width: t.width, height: t.height, head: t.head })),
    zones: r.wind.zones.map(v),
    gust: [r.wind.gust!.amplitude, r.wind.gust!.period, r.wind.gust!.seed],
    atmosphere: [r.atmosphere.altitudeM, r.atmosphere.pressureHPa, r.atmosphere.temperatureC, r.atmosphere.humidity],
    latitude: r.latitude,
  };
});
const elo = [
  [1200, 0, 1200, 0],
  [1500, 30, 1300, 12],
  [1100, 9, 1650, 40],
  [1337, 10, 1337, 3],
  [2000, 100, 800, 100],
].map(([wr, wg, lr, lg]) => {
  const r = rateMatch({ rating: wr!, games: wg! }, { rating: lr!, games: lg! });
  return { input: [wr, wg, lr, lg], expected: expectedScore(wr!, lr!), winner: [r.winner.rating, r.winner.games], loser: [r.loser.rating, r.loser.games] };
});

// ---- scripted matches through the reducer --------------------------------------------------
interface Script {
  name: string;
  seed: number;
  mapId: string;
  rifles: [string, string];
  forfeitOnIdle: boolean;
  steps: unknown[];
}

function scripted(name: string, seed: number, mapId: string, rifles: [string, string], forfeitOnIdle: boolean, run: (s: MatchState, step: (s: MatchState, i: MatchInput) => MatchState) => void): Script {
  const steps: unknown[] = [];
  const step = (s: MatchState, input: MatchInput): MatchState => {
    const r = applyInput(s, input);
    steps.push({ input: inputSummary(input), error: r.error ?? null, outcome: outcomeSummary(r.outcome), state: stateSummary(r.state) });
    return r.state;
  };
  const s = createMatch({ seed, mapId, players: { A: { name: 'A', rifleId: rifles[0] }, B: { name: 'B', rifleId: rifles[1] } }, time: 0, forfeitOnIdle });
  steps.push({ input: null, error: null, outcome: null, state: stateSummary(s), conditions: conditionsSummary(s.conditions) });
  run(s, step);
  return { name, seed, mapId, rifles, forfeitOnIdle, steps };
}

function perfectFire(s: MatchState, aimAt: 'head' | 'chest' = 'chest', time = s.phaseStart + 5, upMrad = 0): MatchInput {
  const side = s.actor!;
  const me = s.players[side];
  const enemy = s.players[other(side)];
  const eye = eyeOf(me.position, me.stance);
  const sil = silhouetteFor('t', enemy.position, enemy.stance, facingYaw(enemy.side));
  const aim = aimAt === 'chest' ? chestOf(sil) : eyeOf(enemy.position, enemy.stance);
  const base = perfectInput(me.rifleId, eye, aim, activeConditions(s), time);
  const cold = me.coldBore ? rifleById(me.rifleId).coldBoreShiftMrad : { elevation: 0, windage: 0 };
  return { type: 'fire', side, time, input: { ...base, dialElevationMrad: base.dialElevationMrad - cold.elevation + upMrad, dialWindageMrad: base.dialWindageMrad - cold.windage } };
}

const openField = (s: MatchState, step: (s: MatchState, i: MatchInput) => MatchState, a: Vec3 = { x: 10, y: 0, z: -75 }, b: Vec3 = { x: -10, y: 0, z: -575 }): MatchState => {
  s = step(s, { type: 'place', side: 'A', position: a, stance: 'standing', time: 1 });
  s = step(s, { type: 'place', side: 'B', position: b, stance: 'standing', time: 1 });
  s = step(s, { type: 'ready', side: 'A', time: 2 });
  return step(s, { type: 'ready', side: 'B', time: 2 });
};

const scripts: Script[] = [
  scripted('turn cycle and errors', 1, 'training-lane', ['bolt308', 'mag338'], false, (s, step) => {
    s = step(s, { type: 'fire', side: 'A', time: 0.5, input: { yaw: 0, pitch: 0, dialElevationMrad: 0, dialWindageMrad: 0 } });
    s = openField(s, step);
    s = step(s, { type: 'place', side: 'B', position: { x: 0, y: 0, z: -600 }, stance: 'prone', time: s.phaseStart + 1 });
    s = step(s, { type: 'place', side: 'A', position: { x: -500, y: 0, z: 300 }, stance: 'prone', time: s.phaseStart + 1 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 2 });
    s = step(s, { type: 'nextRound', time: s.phaseStart + 2 });
    s = step(s, { type: 'tick', time: s.phaseStart + config.rules.shotSeconds + 0.01 });
    s = step(s, { type: 'place', side: 'B', position: { x: 30, y: 0, z: -620 }, stance: 'crouch', time: s.phaseStart + 3 });
    s = step(s, { type: 'tick', time: s.phaseStart + config.rules.moveSeconds * 3 + 0.5 });
    s = step(s, { type: 'forfeit', side: 'B', time: s.phaseStart + 1 });
    step(s, { type: 'tick', time: s.phaseStart + 1 });
  }),
  scripted('head shot, next round', 3, 'training-lane', ['bolt308', 'mag338'], false, (s, step) => {
    s = openField(s, step);
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'head'));
    s = step(s, { type: 'nextRound', time: s.phaseStart + 3 });
    s = openField(s, step, { x: -20, y: 0, z: -40 }, { x: 25, y: 0, z: -600 });
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 9.25));
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'head', s.phaseStart + 3.5));
  }),
  scripted('wounds and second wound', 9, 'training-lane', ['bolt308', 'bolt308'], false, (s, step) => {
    s = openField(s, step, { x: 10, y: 0, z: -20 }, { x: -10, y: 0, z: -640 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'chest'));
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 1 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    step(s, perfectFire(s, 'chest'));
  }),
  scripted('reveal and near misses', 4, 'training-lane', ['bolt308', 'mag338'], false, (s, step) => {
    s = openField(s, step);
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 5, 30));
    s = step(s, { type: 'place', side: 'B', position: { x: 30, y: 0, z: -600 }, stance: 'prone', time: s.phaseStart + 2 });
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 3 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 4, 1.2));
    s = step(s, { type: 'place', side: 'A', position: { x: 12, y: 0, z: -76 }, stance: 'crouch', time: s.phaseStart + 2 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 3 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 6, -0.9));
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 1 });
    step(s, perfectFire(s, 'chest', s.phaseStart + 2, 0.6));
  }),
  scripted('sudden death', 5, 'training-lane', ['bolt65cm', 'am50'], false, (s, step) => {
    s = openField(s, step);
    s = step(s, { type: 'place', side: 'A', position: { x: 0, y: 0, z: -2 }, stance: 'prone', time: s.phaseStart + 1 });
    let guard = 0;
    while (s.pair < config.rules.suddenDeathAfterPairs + 6 && guard++ < 200) {
      s = step(s, { type: 'ready', side: s.actor!, time: s.phaseStart + 0.5 });
    }
    s = step(s, { type: 'place', side: s.actor!, position: { x: 0, y: 0, z: 0 }, stance: 'prone', time: s.phaseStart + 1 });
    step(s, { type: 'tick', time: s.phaseStart + 1000 });
  }),
  scripted('idle forfeit', 1, 'training-lane', ['bolt308', 'bolt308'], true, (s, step) => {
    s = openField(s, step);
    step(s, { type: 'tick', time: 2 + 2 * config.rules.moveSeconds + 2 * config.rules.shotSeconds + 1 });
  }),
  scripted('fixed map shots', 21, 'oil-rigs', ['am50', 'mag338'], false, (s, step) => {
    s = step(s, { type: 'place', side: 'A', position: { x: 30, y: 0, z: -50 }, stance: 'crouch', time: 1 });
    s = step(s, { type: 'ready', side: 'A', time: 2 });
    s = step(s, { type: 'ready', side: 'B', time: 3 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 5, 0.8));
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 7));
    s = step(s, { type: 'nextRound', time: s.phaseStart + 8 });
    s = step(s, { type: 'tick', time: s.phaseEnd + 0.01 });
    s = step(s, perfectFire(s, 'head', s.phaseStart + 12));
  }),
  scripted('soft cover', 8, 'harbor-rooftops', ['am50', 'bolt308'], false, (s, step) => {
    // B crouches behind an AC unit (sheet metal): the .50 goes through it.
    s = step(s, { type: 'place', side: 'A', position: { x: 3, y: 0, z: -50 }, stance: 'crouch', time: 1 });
    s = step(s, { type: 'place', side: 'B', position: { x: 6, y: 0, z: -393.2 }, stance: 'crouch', time: 1 });
    s = step(s, { type: 'ready', side: 'A', time: 2 });
    s = step(s, { type: 'ready', side: 'B', time: 2 });
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 });
    s = step(s, perfectFire(s, 'chest', s.phaseStart + 4));
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 1 });
    step(s, perfectFire(s, 'chest', s.phaseStart + 4));
  }),
];

// ---- seeded bot turns ----------------------------------------------------------------------
const botTurns = (['easy', 'medium', 'hard'] as BotDifficulty[]).flatMap((bot) =>
  ['dry-valley', 'training-lane', 'watchtowers'].map((mapId) => {
    let s = createMatch({ seed: 11, mapId, players: { A: { name: 'A', rifleId: 'bolt65cm' }, B: { name: 'B', rifleId: 'mag338', bot } }, time: 0 });
    const setup = botPlan(s, 'B').map(inputSummary);
    const go = (i: MatchInput) => {
      const r = applyInput(s, i);
      if (r.error) throw new Error(r.error);
      s = r.state;
    };
    if (!mapById(mapId).fixed) {
      go({ type: 'place', side: 'A', position: { x: 10, y: 0, z: -75 }, stance: 'standing', time: 1 });
      go({ type: 'place', side: 'B', position: { x: -10, y: 0, z: -(mapById(mapId).zones.B.sMin + 5) }, stance: 'prone', time: 1 });
    }
    go({ type: 'ready', side: 'A', time: 2 });
    go({ type: 'ready', side: 'B', time: 2 });
    if (s.phase === 'move') go({ type: 'ready', side: 'A', time: s.phaseStart + 1 });
    const shot = botPlan(s, 'B').map(inputSummary);
    return { bot, mapId, setup, shot };
  }),
);

// ---- full bot-vs-bot matches ---------------------------------------------------------------
function playBots(seed: number, a: BotDifficulty, b: BotDifficulty, mapId: string) {
  let s = createMatch({ seed, mapId, players: { A: { name: 'BotA', rifleId: 'bolt65cm', bot: a }, B: { name: 'BotB', rifleId: 'mag338', bot: b } }, time: 0 });
  const steps: unknown[] = [];
  const apply = (input: MatchInput) => {
    const r = applyInput(s, input);
    steps.push({ input: inputSummary(input), error: r.error ?? null, outcome: outcomeSummary(r.outcome), state: stateSummary(r.state) });
    if (!r.error) s = r.state;
  };
  let phases = 0;
  while (s.phase !== 'matchOver' && phases < 2000) {
    phases++;
    if (s.phase === 'roundOver') {
      apply({ type: 'nextRound', time: s.phaseStart + 3 });
      continue;
    }
    const sides: Side[] = s.phase === 'setup' ? ['A', 'B'] : [s.actor!];
    const plan = sides.flatMap((side) => botPlan(s, side)).sort((x, y) => x.time - y.time);
    const phaseId = `${s.round}:${s.pair}:${s.step}:${s.phase}`;
    for (const input of plan) {
      if (`${s.round}:${s.pair}:${s.step}:${s.phase}` !== phaseId) break;
      apply(input);
    }
    if (`${s.round}:${s.pair}:${s.step}:${s.phase}` === phaseId) apply({ type: 'tick', time: s.phaseEnd + 0.01 });
  }
  return { seed, a, b, mapId, steps };
}

const botMatches = [
  ...Object.keys(MAPS).map((mapId) => playBots(77, 'medium', 'medium', mapId)),
  playBots(2024, 'medium', 'medium', 'training-lane'),
  playBots(101, 'easy', 'hard', 'training-lane'),
  playBots(102, 'hard', 'easy', 'dry-valley'),
  playBots(103, 'hard', 'hard', 'alpine-ridge'),
];

writeFileSync(
  new URL('./rules.json', import.meta.url),
  JSON.stringify({ maps, windFields, sway, breath, shots, practice, elo, scripts, botTurns, botMatches }) + '\n',
);
console.log(
  'wrote rules.json:',
  shots.length,
  'shots,',
  scripts.length,
  'scripts,',
  botMatches.map((m) => m.steps.length).join('/'),
  'bot match steps',
);
