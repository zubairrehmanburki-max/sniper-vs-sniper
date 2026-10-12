/**
 * Aim wander. The crosshair drifts on a slow figure-8 whose size depends on stance, bipod, breath
 * and wounds. Firing is a timing read on this curve. Pure functions of time, so the bot and replays
 * can evaluate the same curve.
 */
import { valueNoise1D } from '@svs/ballistics';
import { config, type GameConfig, type Stance } from './config.ts';

export interface BreathState {
  holding: boolean;
  /** Match time the current hold (or recovery) started, s. */
  since: number;
  /** Amplitude factor at the moment the last hold was released (≥ 1 means strained). */
  strainAtRelease: number;
}

export const initialBreath = (): BreathState => ({ holding: false, since: -1e9, strainAtRelease: 1 });

/** Amplitude multiplier from breathing at time `now`. */
export function breathFactor(state: BreathState, now: number, cfg: GameConfig = config): number {
  const hb = cfg.sway.holdBreath;
  const dt = Math.max(0, now - state.since);
  if (state.holding) {
    const settle = 0.6;
    if (dt < settle) return 1 + (hb.steadyFactor - 1) * (dt / settle);
    if (dt < hb.steadySeconds) return hb.steadyFactor;
    return Math.min(hb.maxStrainFactor, hb.steadyFactor + hb.strainGrowthPerSecond * (dt - hb.steadySeconds));
  }
  const excess = Math.max(0, state.strainAtRelease - 1);
  return 1 + excess * Math.exp(-dt / hb.recoverSeconds);
}

export function holdBreath(state: BreathState, now: number, cfg: GameConfig = config): BreathState {
  if (state.holding) return state;
  // Can't get a clean hold until you've recovered from a strained one.
  return { holding: true, since: now - Math.max(0, breathFactor(state, now, cfg) - 1) * 0.3, strainAtRelease: 1 };
}

export function releaseBreath(state: BreathState, now: number, cfg: GameConfig = config): BreathState {
  if (!state.holding) return state;
  const f = breathFactor(state, now, cfg);
  return { holding: false, since: now, strainAtRelease: Math.max(1, f) };
}

export interface SwayInput {
  stance: Stance;
  bipod: boolean;
  breath: BreathState;
  /** Extra multiplier, e.g. 1.4 for a wounded player. */
  penalty?: number;
  seed: number;
}

/** Aim offset at time `now`, in mrad (right, up). */
export function swayOffset(input: SwayInput, now: number, cfg: GameConfig = config): { right: number; up: number } {
  const s = cfg.sway;
  const amp =
    s.amplitudeMrad[input.stance] *
    (input.bipod && input.stance === 'prone' ? s.bipodFactor : 1) *
    breathFactor(input.breath, now, cfg) *
    (input.penalty ?? 1);
  const w = (2 * Math.PI) / s.periodSeconds;
  // Figure-8 plus a little seeded drift so it never feels mechanical.
  const right = amp * (Math.sin(w * now) + 0.35 * valueNoise1D(input.seed, now * 0.4));
  const up = amp * (0.5 * Math.sin(2 * w * now) + 0.3 * valueNoise1D(input.seed + 1, now * 0.5));
  return { right, up };
}
