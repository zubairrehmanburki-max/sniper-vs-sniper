import { describe, expect, it } from 'vitest';
import {
  applyInput,
  botPlan,
  chestOf,
  config,
  createMatch,
  eyeOf,
  facingYaw,
  lineOfSight,
  mapById,
  moveBudget,
  other,
  silhouetteFor,
  type MatchInput,
  type MatchState,
  type Side,
} from '../src/index.ts';
import { solveFiringSolution } from '@svs/ballistics';
import { activeConditions } from '../src/match.ts';
import { shotWindField } from '../src/world.ts';
import { rifleById, rifleShot } from '../src/config.ts';

const R = config.rules;
const map = mapById('training-lane');

function newMatch(seed = 1): MatchState {
  return createMatch({
    seed,
    mapId: 'training-lane',
    players: { A: { name: 'Alex', rifleId: 'bolt308' }, B: { name: 'Bo', rifleId: 'mag338' } },
    time: 0,
  });
}

function step(s: MatchState, input: MatchInput) {
  const r = applyInput(s, input);
  if (r.error) throw new Error(r.error);
  return r;
}

/** A perfect shot from the actor at the frozen target (calculator solution, no sway). */
function perfectFire(s: MatchState, aimAt: 'head' | 'chest' = 'chest', time = s.phaseStart + 5): MatchInput {
  const side = s.actor!;
  const me = s.players[side];
  const enemy = s.players[other(side)];
  const eye = eyeOf(me.position, me.stance);
  const sil = silhouetteFor('t', enemy.position, enemy.stance, facingYaw(enemy.side));
  const aim = aimAt === 'chest' ? chestOf(sil) : eyeOf(enemy.position, enemy.stance);
  const dx = aim.x - eye.x;
  const dz = aim.z - eye.z;
  const horiz = Math.hypot(dx, dz);
  const yaw = Math.atan2(dx, -dz);
  const pitch = Math.atan2(aim.y - eye.y, horiz);
  const c = activeConditions(s);
  const sol = solveFiringSolution(
    { ...rifleShot(rifleById(me.rifleId)), losElevation: pitch },
    { atmosphere: c.atmosphere, wind: shotWindField(c.wind, eye, yaw) },
    Math.hypot(horiz, aim.y - eye.y),
    { fireTime: time },
  );
  const cold = me.coldBore ? rifleById(me.rifleId).coldBoreShiftMrad : { elevation: 0, windage: 0 };
  return {
    type: 'fire',
    side,
    time,
    input: { yaw, pitch, dialElevationMrad: sol.elevation * 1000 - cold.elevation, dialWindageMrad: sol.windage * 1000 - cold.windage },
  };
}

/** Put both players in the open, prone, facing each other with clear sight lines. */
function openField(s: MatchState): MatchState {
  s = step(s, { type: 'place', side: 'A', position: { x: 10, y: 0, z: -75 }, stance: 'standing', time: 1 }).state;
  s = step(s, { type: 'place', side: 'B', position: { x: -10, y: 0, z: -575 }, stance: 'standing', time: 1 }).state;
  s = step(s, { type: 'ready', side: 'A', time: 2 }).state;
  return step(s, { type: 'ready', side: 'B', time: 2 }).state;
}

describe('training lane map', () => {
  it('both zones see each other over the valley', () => {
    const a = eyeOf({ x: 10, y: map.ground(10, -75), z: -75 }, 'prone');
    const b = eyeOf({ x: -10, y: map.ground(-10, -575), z: -575 }, 'prone');
    expect(lineOfSight(a, b, map.ground, map.cover)).toBe(true);
  });
  it('every rock blocks the line straight through it', () => {
    for (const box of map.cover) {
      const cx = (box.min.x + box.max.x) / 2;
      const cz = (box.min.z + box.max.z) / 2;
      const y = (box.min.y + box.max.y) / 2;
      expect(lineOfSight({ x: cx, y, z: cz + 5 }, { x: cx, y, z: cz - 5 }, map.ground, map.cover)).toBe(false);
    }
  });
});

describe('turn cycle', () => {
  it('runs setup → A moves → B fires → B moves → A fires', () => {
    let s = newMatch();
    expect(s.phase).toBe('setup');
    s = openField(s);
    const seen: string[] = [];
    for (let i = 0; i < 5; i++) {
      seen.push(`${s.phase}:${s.actor}`);
      s = step(s, { type: 'ready', side: s.actor!, time: s.phaseStart + 1 }).state;
    }
    expect(seen).toEqual(['move:A', 'shot:B', 'move:B', 'shot:A', 'move:A']);
  });
  it('phases time out on the clock', () => {
    let s = openField(newMatch());
    const start = s.phaseStart;
    s = step(s, { type: 'tick', time: start + R.moveSeconds + 0.01 }).state;
    expect(`${s.phase}:${s.actor}`).toBe('shot:B');
    s = step(s, { type: 'tick', time: s.phaseStart + R.shotSeconds * 3 + 0.5 }).state;
    expect(`${s.phase}:${s.actor}`).toBe('move:A');
    expect(s.pair).toBe(2);
  });
  it('rejects out-of-turn actions', () => {
    const s = openField(newMatch());
    expect(applyInput(s, { type: 'fire', side: 'A', time: s.phaseStart + 1, input: { yaw: 0, pitch: 0, dialElevationMrad: 0, dialWindageMrad: 0 } }).error).toBeDefined();
    expect(applyInput(s, { type: 'place', side: 'B', position: { x: 0, y: 0, z: -600 }, stance: 'prone', time: s.phaseStart + 1 }).error).toBeDefined();
  });
  it('limits movement to the move budget and keeps players in their zone', () => {
    let s = openField(newMatch());
    const from = s.players.A.position;
    s = step(s, { type: 'place', side: 'A', position: { x: -500, y: 0, z: 300 }, stance: 'prone', time: s.phaseStart + 1 }).state;
    const p = s.players.A.position;
    expect(Math.hypot(p.x - from.x, p.z - from.z)).toBeLessThanOrEqual(moveBudget(s, 'A') + 0.5);
    expect(p.x).toBeGreaterThanOrEqual(map.zones.A.xMin);
    expect(-p.z).toBeGreaterThanOrEqual(map.zones.A.sMin);
    expect(-p.z).toBeLessThanOrEqual(map.zones.A.sMax);
  });
});

describe('shots', () => {
  it('a perfect head shot kills, ends the round and alternates the first mover', () => {
    let s = openField(newMatch(3));
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state; // A holds position
    const r = step(s, perfectFire(s, 'head'));
    expect(r.outcome!.verdict).toBe('kill');
    expect(r.state.phase).toBe('roundOver');
    expect(r.state.players.B.kills).toBe(1);
    const next = step(r.state, { type: 'nextRound', time: r.state.phaseStart + 3 }).state;
    expect(next.phase).toBe('setup');
    expect(next.round).toBe(2);
    expect(next.firstMover).toBe('B');
  });
  it('a long .308 chest hit only wounds, and a second wound puts the target down', () => {
    let s = createMatch({ seed: 9, mapId: 'training-lane', players: { A: { name: 'A', rifleId: 'bolt308' }, B: { name: 'B', rifleId: 'bolt308' } }, time: 0 });
    // 620 m apart: beyond the .308's chest-kill energy.
    s = step(s, { type: 'place', side: 'A', position: { x: 10, y: 0, z: -20 }, stance: 'standing', time: 1 }).state;
    s = step(s, { type: 'place', side: 'B', position: { x: -10, y: 0, z: -640 }, stance: 'standing', time: 1 }).state;
    s = step(s, { type: 'ready', side: 'A', time: 2 }).state;
    s = step(s, { type: 'ready', side: 'B', time: 2 }).state;
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state;
    let r = step(s, perfectFire(s, 'chest'));
    expect(r.outcome!.impact.energy).toBeLessThan(R.chestKillEnergyJ);
    expect(r.outcome!.verdict).toBe('wound');
    expect(r.state.players.A.wounds).toBe(1);
    s = r.state; // B moves, A shoots back and misses on purpose
    s = step(s, { type: 'ready', side: 'B', time: s.phaseStart + 1 }).state;
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state;
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state;
    expect(`${s.phase}:${s.actor}`).toBe('shot:B');
    r = step(s, perfectFire(s, 'chest'));
    expect(r.outcome!.verdict).toBe('kill');
  });
  it('firing reveals the shooter until they move', () => {
    let s = openField(newMatch(4));
    s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state;
    const bad = perfectFire(s);
    s = step(s, { ...bad, input: { ...(bad as Extract<MatchInput, { type: 'fire' }>).input, dialElevationMrad: 30 } } as MatchInput).state;
    expect(s.players.B.revealed).not.toBeNull();
    s = step(s, { type: 'place', side: 'B', position: { x: 30, y: 0, z: -600 }, stance: 'prone', time: s.phaseStart + 2 }).state;
    expect(s.players.B.revealed).toBeNull();
  });
  it('the same inputs and seed replay to the identical match', () => {
    const run = () => {
      let s = openField(newMatch(77));
      s = step(s, { type: 'ready', side: 'A', time: s.phaseStart + 1 }).state;
      return step(s, perfectFire(s, 'chest', s.phaseStart + 7.3));
    };
    const a = run();
    const b = run();
    expect(a.outcome!.impact).toEqual(b.outcome!.impact);
    expect(a.state.shots).toEqual(b.state.shots);
  });
});

describe('sudden death', () => {
  it('after the pair limit the wind calms and the zone shrinks toward the front', () => {
    let s = openField(newMatch(5));
    const fullWind = Math.hypot(s.conditions.wind.zones[1]!.x, s.conditions.wind.zones[1]!.z);
    s = step(s, { type: 'place', side: 'A', position: { x: 0, y: 0, z: -2 }, stance: 'prone', time: s.phaseStart + 1 }).state;
    let guard = 0;
    while (s.pair < R.suddenDeathAfterPairs + 2 && guard++ < 200) {
      s = step(s, { type: 'ready', side: s.actor!, time: s.phaseStart + 0.5 }).state;
    }
    expect(s.windScale).toBeLessThan(1);
    // A sat at the very back of the zone; the shrink pulled them forward.
    while (s.actor !== 'A' || s.phase !== 'move') s = step(s, { type: 'ready', side: s.actor!, time: s.phaseStart + 0.5 }).state;
    expect(-s.players.A.position.z).toBeGreaterThan(20);
    expect(fullWind).toBeGreaterThan(0);
  });
});

describe('forfeit', () => {
  it('two idle phases forfeit when enabled (online)', () => {
    let s = createMatch({ seed: 1, mapId: 'training-lane', players: { A: { name: 'A', rifleId: 'bolt308' }, B: { name: 'B', rifleId: 'bolt308' } }, time: 0, forfeitOnIdle: true });
    s = openField(s);
    s = step(s, { type: 'tick', time: 2 + R.moveSeconds + R.shotSeconds + R.moveSeconds + R.shotSeconds + 1 }).state;
    expect(s.phase).toBe('matchOver');
    expect(s.forfeit).toBe(true);
  });
});

/** Drive a whole match with bots on both sides, the way the hotseat client does. */
function playBots(seed: number, a: 'easy' | 'medium' | 'hard', b: 'easy' | 'medium' | 'hard') {
  let s = createMatch({
    seed,
    mapId: 'training-lane',
    players: { A: { name: 'BotA', rifleId: 'bolt65cm', bot: a }, B: { name: 'BotB', rifleId: 'mag338', bot: b } },
    time: 0,
  });
  let phases = 0;
  while (s.phase !== 'matchOver' && phases < 2000) {
    phases++;
    if (s.phase === 'roundOver') {
      s = step(s, { type: 'nextRound', time: s.phaseStart + 3 }).state;
      continue;
    }
    const sides: Side[] = s.phase === 'setup' ? ['A', 'B'] : [s.actor!];
    const plan = sides.flatMap((side) => botPlan(s, side)).sort((x, y) => x.time - y.time);
    const phaseId = `${s.round}:${s.pair}:${s.step}:${s.phase}`;
    for (const input of plan) {
      if (`${s.round}:${s.pair}:${s.step}:${s.phase}` !== phaseId) break;
      const r = applyInput(s, input);
      if (!r.error) s = r.state;
    }
    if (`${s.round}:${s.pair}:${s.step}:${s.phase}` === phaseId) s = step(s, { type: 'tick', time: s.phaseEnd + 0.01 }).state;
  }
  return s;
}

describe('bots', () => {
  it('a full bot-vs-bot match plays end to end', () => {
    const s = playBots(2024, 'medium', 'medium');
    expect(s.phase).toBe('matchOver');
    expect(s.winner).not.toBeNull();
    expect(s.players[s.winner!].kills).toBe(R.killsToWin);
    expect(s.shots.length).toBeGreaterThan(0);
  });
  it('hard beats easy more often than not', () => {
    let hardWins = 0;
    const n = 6;
    for (let i = 0; i < n; i++) if (playBots(100 + i, 'easy', 'hard').winner === 'B') hardWins++;
    expect(hardWins).toBeGreaterThanOrEqual(4);
  });
});
