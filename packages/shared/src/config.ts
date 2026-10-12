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
