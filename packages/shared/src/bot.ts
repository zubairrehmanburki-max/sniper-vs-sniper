/**
 * The PvE sniper. One brain, three noise profiles: each turn it asks the real solver for the true
 * firing solution and perturbs its INPUTS (range, wind read, fire timing). The bullet flies honestly.
 */
import { createRng, hashSeed, solveFiringSolution, uniformWind, windAt, type Vec3, type WindField } from '@svs/ballistics';
import { config, rifleById, rifleShot, type BotDifficulty, type GameConfig, type Stance } from './config.ts';
import { activeConditions, clampStance, other, type MatchInput, type MatchState } from './match.ts';
import { chestOf, eyeOf, facingYaw, silhouetteFor, type FireInput } from './combat.ts';
import { horizontalDistance } from './geometry.ts';
import { laneWindAt, shotWindField, vectorToShot } from './world.ts';
import { mapById, type Side } from './maps.ts';
import { swayOffset, initialBreath, holdBreath } from './sway.ts';
import { lineOfSight } from './visibility.ts';

export interface BotAction {
  /** Seconds after the phase starts. */
  at: number;
  input: MatchInput;
}

const rngFor = (state: MatchState, side: Side, salt: number) =>
  createRng(hashSeed(state.seed, 0x626f74, state.round, state.pair, state.step, side === 'A' ? 1 : 2, salt));

/** Where the bot goes in a setup or move phase. */
export function botMove(state: MatchState, side: Side, cfg: GameConfig = config): BotAction[] {
  const me = state.players[side];
  const profile = cfg.bot[me.bot ?? 'medium'];
  const map = mapById(state.mapId);
  const rng = rngFor(state, side, 1);
  const spots = map.spots[side];
  const pool = profile.positions > 0 ? spots.slice(0, profile.positions) : spots;
  let target: Vec3;
  if (pool.length === 1) {
    target = pool[0]!; // a fixed-position map: nowhere else to go
  } else if (profile.positions > 0) {
    // Predictable: walks a fixed rotation.
    target = pool[(state.round + state.pair) % pool.length]!;
  } else {
    const current = pool.findIndex((p) => horizontalDistance(p, me.position) < 1);
    const mustMove = profile.relocateAfterFiring && me.revealed !== null;
    // Punish-aware: avoid staying where it was revealed, and otherwise relocate now and then.
    if (current >= 0 && !mustMove && rng.next() < 0.4) target = pool[current]!;
    else {
      const choices = pool.filter((_, i) => i !== current);
      target = choices[Math.floor(rng.next() * choices.length)]!;
    }
  }
  // Lowest stance from which it can see out over its cover toward the enemy zone.
  const ez = map.zones[other(side)];
  const lx = (ez.xMin + ez.xMax) / 2;
  const lz = -(ez.sMin + ez.sMax) / 2;
  const look = { x: lx, y: map.ground(lx, lz) + 0.6, z: lz };
  const stances: Stance[] = ['prone', 'crouch', 'standing'];
  const sees = stances.find((st) => lineOfSight(eyeOf(target, st, cfg), look, map.ground, map.cover));
  const stance: Stance = clampStance(state, sees ?? 'prone', cfg);
  const at = state.phase === 'setup' ? 1 + rng.next() * 3 : 3 + rng.next() * 8;
  return [
    { at, input: { type: 'place', side, position: target, stance, time: 0 } },
    { at: at + 1 + rng.next() * 4, input: { type: 'ready', side, time: 0 } },
  ];
}

/** Does the bot see its target this phase? Line of sight plus a stance-based spotting roll. */
export function botSpots(state: MatchState, side: Side, cfg: GameConfig = config): boolean {
  const me = state.players[side];
  const enemy = state.players[other(side)];
  const map = mapById(state.mapId);
  const eye = eyeOf(me.position, me.stance, cfg);
  const chest = chestOf(silhouetteFor('t', enemy.position, enemy.stance, facingYaw(enemy.side), cfg));
  const head = eyeOf(enemy.position, enemy.stance, cfg);
  if (!lineOfSight(eye, chest, map.ground, map.cover) && !lineOfSight(eye, head, map.ground, map.cover)) return false;
  if (enemy.revealed) return true; // it saw the flash and dust
  const profile = cfg.bot[me.bot ?? 'medium'];
  return rngFor(state, side, 2).next() < profile.spotChance[enemy.stance];
}

/** The bot's shot phase: aim, solve with noisy inputs, fire on its sway timing (or hold fire). */
export function botShot(state: MatchState, side: Side, cfg: GameConfig = config): BotAction[] {
  const me = state.players[side];
  const difficulty: BotDifficulty = me.bot ?? 'medium';
  const profile = cfg.bot[difficulty];
  const rng = rngFor(state, side, 3);
  const [tLo, tHi] = cfg.bot.thinkSeconds;
  const think = Math.min(cfg.rules.shotSeconds - 1, tLo + (tHi - tLo) * rng.next());
  if (!botSpots(state, side, cfg)) return [{ at: 4 + rng.next() * 6, input: { type: 'ready', side, time: 0 } }];

  const enemy = state.players[other(side)];
  const map = mapById(state.mapId);
  const eye = eyeOf(me.position, me.stance, cfg);
  const chest = chestOf(silhouetteFor('t', enemy.position, enemy.stance, facingYaw(enemy.side), cfg));
  const dx = chest.x - eye.x;
  const dz = chest.z - eye.z;
  const horiz = Math.hypot(dx, dz);
  const yaw = Math.atan2(dx, -dz);
  const pitch = Math.atan2(chest.y - eye.y, horiz);
  const trueRange = Math.hypot(horiz, chest.y - eye.y);
  const rangeEstimate = trueRange * (1 + (rng.next() * 2 - 1) * profile.rangeErrorFrac);

  // The bot's wind read: what it believes, not what is.
  const conditions = activeConditions(state);
  const fireAt = state.phaseStart + think;
  let wind: WindField;
  if (profile.windMode === 'layered') {
    // Full layered read, but its gust timing is slightly off.
    wind = { ...shotWindField(conditions.wind, eye, yaw), gust: undefined };
    const g = conditions.wind.gust;
    const mistimed = g ? windAt({ zones: [{ untilRange: Infinity, vector: { x: 1, y: 0, z: 0 } }], gust: g }, 0, fireAt + (rng.next() * 2 - 1) * 3).x : 1;
    wind = { zones: wind.zones.map((z) => ({ ...z, vector: { x: z.vector.x * mistimed, y: 0, z: z.vector.z * mistimed } })) };
  } else {
    const station = vectorToShot(laneWindAt(conditions.wind, me.position), yaw);
    const scale = profile.windMode === 'half-station' ? 0.5 : 1;
    const speed = Math.hypot(station.x, station.z) * scale;
    const from = (Math.atan2(station.z, station.x) * 180) / Math.PI;
    wind = uniformWind(speed, from);
  }

  const rifle = rifleById(me.rifleId, cfg);
  const shot = { ...rifleShot(rifle, cfg), losElevation: pitch };
  let elevation = 0;
  let windage = 0;
  try {
    const sol = solveFiringSolution(shot, { atmosphere: conditions.atmosphere, wind }, rangeEstimate, {
      effects: { spinDrift: cfg.ballistics.effects.spinDrift },
    });
    elevation = sol.elevation * 1000;
    windage = sol.windage * 1000;
  } catch {
    return [{ at: think, input: { type: 'ready', side, time: 0 } }];
  }
  const click = cfg.scope.clickMrad;
  // Fire timing on the sway curve: a scaled sample of its own sway at the moment it fires.
  const breath = holdBreath(initialBreath(), fireAt - 2);
  const sway = swayOffset(
    { stance: me.stance, bipod: true, breath, seed: hashSeed(state.seed, 0x737779), penalty: me.wounds > 0 ? cfg.player.wound.swayFactor : 1 },
    fireAt,
    cfg,
  );
  const input: FireInput = {
    yaw: yaw + (sway.right * profile.swayFireFactor) / 1000,
    pitch: pitch + (sway.up * profile.swayFireFactor) / 1000,
    dialElevationMrad: Math.round(elevation / click) * click,
    dialWindageMrad: Math.round(windage / click) * click,
  };
  return [{ at: think, input: { type: 'fire', side, input, time: 0 } }];
}

/** All bot actions for the current phase, with absolute times filled in. */
export function botPlan(state: MatchState, side: Side, cfg: GameConfig = config): MatchInput[] {
  let actions: BotAction[] = [];
  if (state.phase === 'setup') actions = botMove(state, side, cfg);
  else if (state.phase === 'move' && state.actor === side) actions = botMove(state, side, cfg);
  else if (state.phase === 'shot' && state.actor === side) actions = botShot(state, side, cfg);
  return actions.map((a) => ({ ...a.input, time: state.phaseStart + a.at }) as MatchInput);
}
