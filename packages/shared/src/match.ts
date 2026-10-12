/**
 * The match state machine (GAME_PLAN "Core loop & turn system"). A pure reducer: the same inputs
 * and seed always produce the same match, so hotseat, bot games, the server and replays share it.
 *
 * Round: setup (both place) → move(first) → shot(second) → move(second) → shot(first) → repeat.
 * On a fixed-position map there are no move phases: shot(second) → shot(first) → repeat.
 * Every shot is taken at a frozen target. First to `killsToWin` kills takes the match.
 */
import { createRng, hashSeed, standardPressure, type Vec3 } from '@svs/ballistics';
import { config, type GameConfig, type Stance, type BotDifficulty } from './config.ts';
import { resolveShot, type Conditions, type FireInput, type ShotOutcome } from './combat.ts';
import { horizontalDistance } from './geometry.ts';
import { clampToZone, mapById, usableZone, type DuelMap, type Side } from './maps.ts';

export type PhaseKind = 'setup' | 'move' | 'shot' | 'roundOver' | 'matchOver';

export interface PlayerState {
  side: Side;
  name: string;
  rifleId: string;
  bot?: BotDifficulty;
  position: Vec3;
  stance: Stance;
  kills: number;
  wounds: number;
  /** Where this player last fired from, while they are still there (the enemy saw flash and dust). */
  revealed: Vec3 | null;
  coldBore: boolean;
  /** A supersonic near miss gave this player a rough bearing to the shooter. */
  heardSnapFrom: Vec3 | null;
  ready: boolean;
  idlePhases: number;
}

export interface ShotRecord {
  round: number;
  pair: number;
  shooter: Side;
  verdict: ShotOutcome['verdict'];
  zone?: ShotOutcome['zone'];
  impactKind: ShotOutcome['impact']['kind'];
  impactPoint: Vec3;
  energy: number;
  timeOfFlight: number;
  nearMiss: boolean;
  call?: ShotOutcome['call'];
  distance: number;
  input: FireInput;
  fireTime: number;
  shotIndex: number;
}

export interface MatchState {
  seed: number;
  mapId: string;
  round: number;
  /** Completed move+shot pairs this round (drives sudden death). */
  pair: number;
  phase: PhaseKind;
  /** Who acts in a move or shot phase. */
  actor: Side | null;
  /** Position at the start of the actor's move phase (movement budget is measured from here). */
  moveFrom: Vec3 | null;
  phaseStart: number;
  phaseEnd: number;
  acted: boolean;
  firstMover: Side;
  /** Phase order within a round after setup, indexes into the cycle. */
  step: number;
  players: Record<Side, PlayerState>;
  conditions: Conditions;
  windScale: number;
  shotCount: number;
  shots: ShotRecord[];
  roundWinner: Side | null;
  winner: Side | null;
  forfeit: boolean;
  forfeitOnIdle: boolean;
}

export type MatchInput =
  | { type: 'tick'; time: number }
  | { type: 'place'; side: Side; position: Vec3; stance: Stance; time: number }
  | { type: 'ready'; side: Side; time: number }
  | { type: 'fire'; side: Side; input: FireInput; time: number }
  | { type: 'nextRound'; time: number }
  /** A player quits or never comes back: the other side wins. */
  | { type: 'forfeit'; side: Side; time: number };

export interface StepResult {
  state: MatchState;
  outcome?: ShotOutcome;
  /** Input rejected (wrong phase, wrong player, out of reach...). State is unchanged. */
  error?: string;
}

export const other = (s: Side): Side => (s === 'A' ? 'B' : 'A');

/** Weather and layered wind for a round: rolled from the match seed, drifting a little each round. */
export function rollConditions(map: DuelMap, seed: number, round: number, cfg: GameConfig = config): Conditions {
  const c = map.climate;
  const base = createRng(hashSeed(seed, 0x636f6e64));
  const drift = createRng(hashSeed(seed, 0x6472, round));
  const between = (r: number, [lo, hi]: [number, number]): number => lo + (hi - lo) * r;
  const d = cfg.rules.roundWindDrift;
  const jitter = (): number => (round === 1 ? 0 : (drift.next() * 2 - 1) * d);
  const temperatureC = between(base.next(), c.temperatureC) + jitter() * 4;
  const humidity = Math.min(1, Math.max(0, between(base.next(), c.humidity) + jitter() * 0.1));
  const speed = between(base.next(), c.windSpeedMps) * (1 + jitter());
  const dir = c.windToward + (base.next() * 2 - 1) * c.windSpread + jitter() * 0.5;
  const zones: Vec3[] = [];
  for (let i = 0; i <= map.windEdges.length; i++) {
    const zs = speed * (0.75 + 0.5 * base.next()) * (1 + jitter() * 0.5);
    const zd = dir + (base.next() * 2 - 1) * 0.3;
    zones.push({ x: zs * Math.sin(zd), y: 0, z: -zs * Math.cos(zd) });
  }
  return {
    atmosphere: { altitudeM: c.altitudeM, pressureHPa: standardPressure(c.altitudeM), temperatureC, humidity },
    wind: {
      start: { x: 0, y: 0, z: 0 },
      direction: { x: 0, y: 0, z: -1 },
      edges: map.windEdges,
      zones,
      gust: { amplitude: c.gustAmplitude, period: c.gustPeriodSeconds, seed: hashSeed(seed, 0x67, round) },
    },
    latitude: map.latitude,
  };
}

function scaledConditions(c: Conditions, scale: number): Conditions {
  if (scale === 1) return c;
  return { ...c, wind: { ...c.wind, zones: c.wind.zones.map((v) => ({ x: v.x * scale, y: v.y * scale, z: v.z * scale })) } };
}

/** Wind and atmosphere actually in force (sudden death calms the wind). */
export function activeConditions(state: MatchState): Conditions {
  return scaledConditions(state.conditions, state.windScale);
}

function defaultPosition(map: DuelMap, side: Side): Vec3 {
  const spot = map.spots[side][0]!;
  return { ...spot };
}

export interface PlayerSetup {
  name: string;
  rifleId: string;
  bot?: BotDifficulty;
}

export function createMatch(
  opts: { seed: number; mapId: string; players: Record<Side, PlayerSetup>; time: number; forfeitOnIdle?: boolean },
  cfg: GameConfig = config,
): MatchState {
  const map = mapById(opts.mapId);
  const player = (side: Side): PlayerState => ({
    side,
    name: opts.players[side].name,
    rifleId: opts.players[side].rifleId,
    bot: opts.players[side].bot,
    position: defaultPosition(map, side),
    stance: 'prone',
    kills: 0,
    wounds: 0,
    revealed: null,
    coldBore: true,
    heardSnapFrom: null,
    ready: false,
    idlePhases: 0,
  });
  return {
    seed: opts.seed,
    mapId: opts.mapId,
    round: 1,
    pair: 0,
    phase: 'setup',
    actor: null,
    moveFrom: null,
    phaseStart: opts.time,
    phaseEnd: opts.time + cfg.rules.setupSeconds,
    acted: false,
    firstMover: 'A',
    step: 0,
    players: { A: player('A'), B: player('B') },
    conditions: rollConditions(map, opts.seed, 1, cfg),
    windScale: 1,
    shotCount: 0,
    shots: [],
    roundWinner: null,
    winner: null,
    forfeit: false,
    forfeitOnIdle: opts.forfeitOnIdle ?? false,
  };
}

/** Sudden-death shrink factor for the current pair count (0 = full zone). */
export function zoneShrink(state: MatchState, cfg: GameConfig = config): number {
  const over = state.pair - cfg.rules.suddenDeathAfterPairs;
  return over < 0 ? 0 : Math.min(1, (over + 1) * cfg.rules.suddenDeathZoneShrinkPerPair);
}

export function isSuddenDeath(state: MatchState, cfg: GameConfig = config): boolean {
  return state.pair >= cfg.rules.suddenDeathAfterPairs;
}

const STANCE_ORDER: Stance[] = ['prone', 'crouch', 'standing'];

/** Lowest stance allowed now: deep into sudden death you can no longer hide by lying flat. */
export function minStance(state: MatchState, cfg: GameConfig = config): Stance {
  const over = state.pair - cfg.rules.suddenDeathAfterPairs;
  if (over >= cfg.rules.suddenDeathStandingAfterPairs) return 'standing';
  if (over >= cfg.rules.suddenDeathNoProneAfterPairs) return 'crouch';
  return 'prone';
}

export function clampStance(state: MatchState, stance: Stance, cfg: GameConfig = config): Stance {
  const min = minStance(state, cfg);
  return STANCE_ORDER.indexOf(stance) < STANCE_ORDER.indexOf(min) ? min : stance;
}

/** Furthest a mover may end up from where their move phase started, m. */
export function moveBudget(state: MatchState, side: Side, cfg: GameConfig = config): number {
  const wounded = state.players[side].wounds > 0 ? cfg.player.wound.moveSpeedFactor : 1;
  return cfg.player.moveSpeedMps * cfg.rules.moveSeconds * wounded;
}

/**
 * The phase cycle after setup: [kind, actor] per step. On a fixed-position map nobody moves, so
 * the sides simply trade shots.
 */
function cycle(state: MatchState, step: number): { phase: 'move' | 'shot'; actor: Side } {
  const f = state.firstMover;
  const s = other(f);
  if (mapById(state.mapId).fixed) {
    return [
      { phase: 'shot' as const, actor: s },
      { phase: 'shot' as const, actor: f },
    ][step % 2]!;
  }
  return [
    { phase: 'move' as const, actor: f },
    { phase: 'shot' as const, actor: s },
    { phase: 'move' as const, actor: s },
    { phase: 'shot' as const, actor: f },
  ][step % 4]!;
}

const cycleLength = (state: MatchState): number => (mapById(state.mapId).fixed ? 2 : 4);

function clone(state: MatchState): MatchState {
  return {
    ...state,
    players: { A: { ...state.players.A }, B: { ...state.players.B } },
    shots: state.shots.slice(),
  };
}

function enterPhase(state: MatchState, step: number, time: number, cfg: GameConfig): void {
  const map = mapById(state.mapId);
  const next = cycle(state, step);
  state.step = step;
  state.phase = next.phase;
  state.actor = next.actor;
  state.acted = false;
  state.phaseStart = time;
  state.phaseEnd = time + (next.phase === 'move' ? cfg.rules.moveSeconds : cfg.rules.shotSeconds);
  for (const side of ['A', 'B'] as const) state.players[side].stance = clampStance(state, state.players[side].stance, cfg);
  if (next.phase === 'move') {
    const p = state.players[next.actor];
    // Sudden death: the usable zone shrinks toward the front, forcing exposure.
    const zone = usableZone(map, next.actor, zoneShrink(state, cfg), cfg.rules.minZoneDepthM);
    p.position = clampToZone(map, zone, p.position);
    state.moveFrom = { ...p.position };
  } else {
    state.moveFrom = null;
  }
}

function endPhase(state: MatchState, time: number, cfg: GameConfig): void {
  if (state.phase === 'setup') {
    state.players.A.ready = false;
    state.players.B.ready = false;
    enterPhase(state, 0, time, cfg);
    return;
  }
  if (state.phase !== 'move' && state.phase !== 'shot') return;
  const actor = state.players[state.actor!];
  if (!state.acted) {
    actor.idlePhases += 1;
    if (state.forfeitOnIdle && actor.idlePhases >= cfg.rules.forfeitAfterIdlePhases) {
      state.winner = other(actor.side);
      state.forfeit = true;
      state.phase = 'matchOver';
      state.actor = null;
      return;
    }
  } else {
    actor.idlePhases = 0;
  }
  if (state.phase === 'shot') {
    actor.heardSnapFrom = null; // the bearing hint lasts for one of your shot phases
    state.pair += 1;
    if (isSuddenDeath(state, cfg)) {
      state.windScale = Math.max(0, 1 - (state.pair - cfg.rules.suddenDeathAfterPairs + 1) * cfg.rules.suddenDeathWindDecayPerPair);
    }
  }
  enterPhase(state, (state.step + 1) % cycleLength(state), time, cfg);
}

function endRound(state: MatchState, winner: Side, time: number, cfg: GameConfig): void {
  state.players[winner].kills += 1;
  state.roundWinner = winner;
  state.actor = null;
  state.phaseStart = time;
  state.phaseEnd = Infinity;
  if (state.players[winner].kills >= cfg.rules.killsToWin) {
    state.winner = winner;
    state.phase = 'matchOver';
  } else {
    state.phase = 'roundOver';
  }
}

function startNextRound(state: MatchState, time: number, cfg: GameConfig): void {
  const map = mapById(state.mapId);
  state.round += 1;
  state.pair = 0;
  state.windScale = 1;
  state.conditions = rollConditions(map, state.seed, state.round, cfg);
  state.firstMover = other(state.firstMover);
  state.roundWinner = null;
  for (const side of ['A', 'B'] as const) {
    const p = state.players[side];
    p.wounds = 0;
    p.revealed = null;
    p.heardSnapFrom = null;
    p.ready = false;
    p.stance = 'prone';
    p.position = defaultPosition(map, side);
  }
  state.phase = 'setup';
  state.actor = null;
  state.acted = false;
  state.phaseStart = time;
  state.phaseEnd = time + cfg.rules.setupSeconds;
}

export function applyInput(prev: MatchState, input: MatchInput, cfg: GameConfig = config): StepResult {
  const fail = (error: string): StepResult => ({ state: prev, error });
  if (prev.phase === 'matchOver') return fail('Match is over');
  const state = clone(prev);
  const map = mapById(state.mapId);

  // Timers first: any input past the phase end closes the phase before it is considered.
  if (input.time >= state.phaseEnd && (state.phase === 'setup' || state.phase === 'move' || state.phase === 'shot')) {
    endPhase(state, state.phaseEnd, cfg);
    if (input.type === 'tick') {
      // Several phases may have elapsed (e.g. a long pause): keep closing them.
      return (state.phase as PhaseKind) === 'matchOver' ? { state } : applyInput(state, input, cfg);
    }
    if ((state.phase as PhaseKind) === 'matchOver') return { state };
  }

  switch (input.type) {
    case 'tick':
      return { state };

    case 'forfeit':
      state.winner = other(input.side);
      state.forfeit = true;
      state.phase = 'matchOver';
      state.actor = null;
      state.phaseStart = input.time;
      state.phaseEnd = Infinity;
      return { state };

    case 'nextRound':
      if (state.phase !== 'roundOver') return fail('No round to start');
      startNextRound(state, input.time, cfg);
      return { state };

    case 'place': {
      const p = state.players[input.side];
      if (state.phase === 'setup') {
        p.position = clampToZone(map, map.zones[input.side], input.position);
        p.stance = input.stance;
        return { state };
      }
      if (state.phase !== 'move' || state.actor !== input.side) return fail('Not your move phase');
      const zone = usableZone(map, input.side, zoneShrink(state, cfg), cfg.rules.minZoneDepthM);
      let target = clampToZone(map, zone, input.position);
      const from = state.moveFrom!;
      const budget = moveBudget(state, input.side, cfg);
      const d = horizontalDistance(from, target);
      if (d > budget) {
        const f = budget / d;
        target = clampToZone(map, zone, { x: from.x + (target.x - from.x) * f, y: 0, z: from.z + (target.z - from.z) * f });
      }
      if (horizontalDistance(p.position, target) > 0.5) p.revealed = null; // moved away from where they last fired
      p.position = target;
      p.stance = clampStance(state, input.stance, cfg);
      state.acted = true;
      return { state };
    }

    case 'ready': {
      if (state.phase === 'setup') {
        state.players[input.side].ready = true;
        if (state.players.A.ready && state.players.B.ready) endPhase(state, input.time, cfg);
        return { state };
      }
      if ((state.phase !== 'move' && state.phase !== 'shot') || state.actor !== input.side) return fail('Not your phase');
      state.acted = true;
      endPhase(state, input.time, cfg);
      return { state };
    }

    case 'fire': {
      if (state.phase !== 'shot' || state.actor !== input.side) return fail('Not your shot phase');
      if (state.acted) return fail('Already fired this phase');
      const shooter = state.players[input.side];
      const targetSide = other(input.side);
      const target = state.players[targetSide];
      state.shotCount += 1;
      const outcome = resolveShot(
        {
          map,
          conditions: activeConditions(state),
          rifleId: shooter.rifleId,
          shooter: { position: shooter.position, stance: shooter.stance },
          target: { id: targetSide, side: targetSide, position: target.position, stance: target.stance, wounds: target.wounds },
          input: input.input,
          seed: state.seed,
          shotIndex: state.shotCount,
          fireTime: input.time,
          coldBore: shooter.coldBore,
        },
        cfg,
      );
      shooter.coldBore = false;
      shooter.revealed = { ...shooter.position };
      state.acted = true;
      state.shots.push({
        round: state.round,
        pair: state.pair,
        shooter: input.side,
        verdict: outcome.verdict,
        zone: outcome.zone,
        impactKind: outcome.impact.kind,
        impactPoint: outcome.impact.point,
        energy: outcome.impact.energy,
        timeOfFlight: outcome.impact.t,
        nearMiss: outcome.nearMiss,
        call: outcome.call,
        distance: horizontalDistance(shooter.position, target.position),
        input: input.input,
        fireTime: input.time,
        shotIndex: state.shotCount,
      });
      if (outcome.nearMiss) target.heardSnapFrom = { ...shooter.position };
      if (outcome.verdict === 'kill') {
        endRound(state, input.side, input.time, cfg);
      } else {
        if (outcome.verdict === 'wound') target.wounds += 1;
        endPhase(state, input.time, cfg); // the shot phase ends on fire
      }
      return { state, outcome };
    }
  }
}

/** Seconds left in the current phase. */
export function timeLeft(state: MatchState, now: number): number {
  return Math.max(0, state.phaseEnd - now);
}
