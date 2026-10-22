import type { Atmosphere, Projectile, Shot } from '@svs/ballistics';
import { solveZero, standardPressure } from '@svs/ballistics';
import raw from '../../../config/game.json';

export type Stance = 'prone' | 'crouch' | 'standing';

export interface RifleConfig {
  id: string;
  name: string;
  class: string;
  cartridge: string;
  dragModel: 'G1' | 'G7';
  bc: number;
  massKg: number;
  diameterM: number;
  lengthM: number;
  muzzleVelocityMps: number;
  twistM: number;
  sweetSpotM: [number, number];
  coldBoreShiftMrad: { elevation: number; windage: number };
}

export interface GameConfig {
  ballistics: {
    dtSeconds: number;
    integrator: 'rk4' | 'euler';
    sightHeightM: number;
    zeroRangeM: number;
    muzzleVelocitySdMps: number;
    transonic: { machThreshold: number; sigmaRadPerSqrtS: number };
    effects: { spinDrift: boolean; aerodynamicJump: boolean; coriolis: boolean; transonicDispersion: boolean };
  };
  rifles: RifleConfig[];
  scope: {
    minZoom: number;
    maxZoom: number;
    fovAt1xDeg: number;
    clickMrad: number;
    maxElevationMrad: number;
    maxWindageMrad: number;
    eyeFovDeg: number;
  };
  sway: {
    amplitudeMrad: Record<Stance, number>;
    bipodFactor: number;
    periodSeconds: number;
    holdBreath: {
      steadySeconds: number;
      steadyFactor: number;
      recoverSeconds: number;
      strainGrowthPerSecond: number;
      maxStrainFactor: number;
    };
    eyeHeightM: Record<Stance, number>;
  };
  target: { widthM: number; heightM: number; headM: number };
  practiceRange: {
    targetsM: number[];
    laneWidthM: number;
    windZonesM: number[];
    wind: {
      baseSpeedMps: [number, number];
      directionJitterDeg: number;
      gustAmplitude: number;
      gustPeriodSeconds: number;
      zoneSpeedJitter: number;
    };
    atmosphere: { altitudeM: number; temperatureC: number; humidity: number };
    latitude: number;
    speedOfSoundForAudioMps: number;
    /** The firing point is a raised bench so prone shooters see over the grass. */
    firingPointHeightM: number;
  };
  softCover: { minEnergyJ: number; energyKeep: number; maxLayers: number };
  rules: {
    setupSeconds: number;
    moveSeconds: number;
    shotSeconds: number;
    killsToWin: number;
    suddenDeathAfterPairs: number;
    suddenDeathWindDecayPerPair: number;
    suddenDeathZoneShrinkPerPair: number;
    minZoneDepthM: number;
    chestKillEnergyJ: number;
    woundsToDown: number;
    nearMissRadiusM: number;
    forfeitAfterIdlePhases: number;
    /** Sudden death pairs after which nobody may lie prone, and after which everyone stands. */
    suddenDeathNoProneAfterPairs: number;
    suddenDeathStandingAfterPairs: number;
    /** Online: pause between a round's last shot and the next setup, s. */
    roundBreakSeconds: number;
    /** Online: how long a dropped player's seat is held before they forfeit, s. */
    reconnectGraceSeconds: number;
    roundWindDrift: number;
    /** Variable posts: trips to another structure each player may make per round. */
    relocationsPerRound: number;
  };
  player: {
    silhouette: Record<Stance, { heightM: number; widthM: number; headM: number }>;
    moveSpeedMps: number;
    wound: { swayFactor: number; moveSpeedFactor: number };
  };
  bot: { thinkSeconds: [number, number] } & Record<BotDifficulty, BotProfile>;
}

export type BotDifficulty = 'easy' | 'medium' | 'hard';

export interface BotProfile {
  rangeErrorFrac: number;
  /** half-station: holds half the wind at its own position; station: uses its station wind for the whole lane; layered: reads every zone. */
  windMode: 'half-station' | 'station' | 'layered';
  /** 1 = fires anywhere on the sway curve, 0 = fires exactly at the sway null. */
  swayFireFactor: number;
  spotChance: Record<Stance, number>;
  /** Number of fixed spots it rotates through (0 = uses the map's full position set). */
  positions: number;
  relocateAfterFiring: boolean;
}

export const config = raw as unknown as GameConfig;

export function rifleById(id: string, cfg: GameConfig = config): RifleConfig {
  const rifle = cfg.rifles.find((r) => r.id === id);
  if (!rifle) throw new Error(`Unknown rifle ${id}`);
  return rifle;
}

export function projectileOf(rifle: RifleConfig): Projectile {
  return {
    dragModel: rifle.dragModel,
    bc: rifle.bc,
    massKg: rifle.massKg,
    diameterM: rifle.diameterM,
    lengthM: rifle.lengthM,
  };
}

/** Atmosphere for a map: station pressure follows altitude unless given. */
export function atmosphereFor(a: { altitudeM: number; temperatureC: number; humidity: number; pressureHPa?: number }): Atmosphere {
  return {
    altitudeM: a.altitudeM,
    temperatureC: a.temperatureC,
    humidity: a.humidity,
    pressureHPa: a.pressureHPa ?? standardPressure(a.altitudeM),
  };
}

const zeroCache = new Map<string, number>();

/**
 * A rifle's shot setup, zeroed at the config zero range. Players zero in standard conditions on a
 * home range, so field weather shows up as changed dope, exactly like the real thing.
 */
export function rifleShot(rifle: RifleConfig, cfg: GameConfig = config): Shot {
  const base = {
    projectile: projectileOf(rifle),
    muzzleVelocity: rifle.muzzleVelocityMps,
    muzzleVelocitySd: cfg.ballistics.muzzleVelocitySdMps,
    sightHeight: cfg.ballistics.sightHeightM,
    twist: rifle.twistM,
  };
  const key = `${rifle.id}:${rifle.bc}:${rifle.muzzleVelocityMps}:${cfg.ballistics.zeroRangeM}`;
  let zero = zeroCache.get(key);
  if (zero === undefined) {
    zero = solveZero(base, { atmosphere: atmosphereFor({ altitudeM: 0, temperatureC: 15, humidity: 0.5 }) }, cfg.ballistics.zeroRangeM);
    zeroCache.set(key, zero);
  }
  return { ...base, zeroElevation: zero };
}
