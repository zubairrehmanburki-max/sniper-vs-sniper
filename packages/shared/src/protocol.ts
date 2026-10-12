/**
 * Online protocol: what clients send to a duel room and what the room sends back. The room holds
 * the full MatchState; each player only ever receives `viewFor(state, side)`, which strips what
 * they are not entitled to see (the seed, and the enemy's position unless they can see it).
 */
import type { Vec3 } from '@svs/ballistics';
import type { Stance } from './config.ts';
import { chestOf, eyeOf, facingYaw, silhouetteFor, type FireInput, type ShotOutcome } from './combat.ts';
import { mapById, type Side } from './maps.ts';
import { other, type MatchState } from './match.ts';
import { lineOfSight } from './visibility.ts';

export const ROOM_NAME = 'duel';

/** Options a client passes when joining or creating a duel room. */
export interface JoinOptions {
  name: string;
  rifleId: string;
  /** Private game code; omit for quick match. */
  code?: string;
}

export type ClientMessage =
  | { type: 'place'; position: Vec3; stance: Stance }
  | { type: 'ready' }
  | { type: 'fire'; input: FireInput }
  | { type: 'forfeit' };

/** A player's view of the match: the MatchState shape, with hidden information removed. */
export interface MatchView extends MatchState {
  you: Side;
  /** True when the enemy's real position and stance are included. */
  enemySeen: boolean;
  /** Room clock when the view was made, s. */
  now: number;
  /** The opponent's connection, for "reconnecting" notices. */
  opponentConnected: boolean;
}

export type ServerMessage =
  | { type: 'update'; view: MatchView; outcome?: ShotOutcome }
  | { type: 'waiting'; code?: string }
  | { type: 'error'; message: string };

const HIDDEN: Vec3 = { x: 0, y: 0, z: 0 };

/** Can `side` see its enemy right now? Only while looking through the scope in its own shot phase. */
export function canSeeEnemy(state: MatchState, side: Side): boolean {
  if (state.phase === 'roundOver' || state.phase === 'matchOver') return true;
  if (state.phase !== 'shot' || state.actor !== side) return false;
  const map = mapById(state.mapId);
  const me = state.players[side];
  const enemy = state.players[other(side)];
  const eye = eyeOf(me.position, me.stance);
  const target = silhouetteFor('enemy', enemy.position, enemy.stance, facingYaw(enemy.side));
  const chest = chestOf(target);
  const head = eyeOf(enemy.position, enemy.stance);
  return lineOfSight(eye, chest, map.ground, map.cover) || lineOfSight(eye, head, map.ground, map.cover);
}

export function viewFor(state: MatchState, side: Side, now: number, opponentConnected = true): MatchView {
  const enemySide = other(side);
  const seen = canSeeEnemy(state, side);
  const enemy = state.players[enemySide];
  const hiddenEnemy = seen
    ? { ...enemy, heardSnapFrom: null }
    : { ...enemy, position: enemy.revealed ? { ...enemy.revealed } : HIDDEN, stance: 'prone' as Stance, heardSnapFrom: null };
  return {
    ...state,
    seed: 0,
    moveFrom: state.actor === side ? state.moveFrom : null,
    players: { ...state.players, [enemySide]: hiddenEnemy } as MatchState['players'],
    you: side,
    enemySeen: seen,
    now,
    opponentConnected,
  };
}

/** JSON has no Infinity: phase ends are sent as -1 when the phase has no timer. */
export function encodeView(view: MatchView): MatchView {
  return Number.isFinite(view.phaseEnd) ? view : { ...view, phaseEnd: -1 };
}

export function decodeView(view: MatchView): MatchView {
  return view.phaseEnd === -1 || view.phaseEnd === null ? { ...view, phaseEnd: Infinity } : view;
}

/** Shrink a shot outcome for the wire: the trajectory keeps one point every `stepS` plus the last. */
export function thinOutcome(outcome: ShotOutcome, stepS = 0.004): ShotOutcome {
  const pts = outcome.trajectory.points;
  const kept = pts.filter((p, i) => i === 0 || i === pts.length - 1 || Math.floor(p.t / stepS) !== Math.floor(pts[i - 1]!.t / stepS));
  return { ...outcome, seed: 0, trajectory: { ...outcome.trajectory, points: kept } };
}

/** Readable 4-letter private game code (no I/O to avoid confusion). */
export function makeCode(random: () => number = Math.random): string {
  const letters = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  let s = '';
  for (let i = 0; i < 4; i++) s += letters[Math.floor(random() * letters.length)];
  return s;
}

