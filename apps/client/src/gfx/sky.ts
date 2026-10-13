/**
 * Daylight from a photographic HDRI: the sky itself is the background, the same image lights
 * every surface through a prefiltered environment map, and the sun is a shadow-casting light on
 * the bearing of the brightest spot in the picture. Each map rotates the image so its sun sits
 * where the designer wants it relative to the firing line.
 */
import * as THREE from 'three';
import { assets, type SkyName } from './assets.ts';

export interface SkyOptions {
  sky: SkyName;
  /** Where the sun should be, degrees clockwise from due north (the enemy's direction, −z). */
  sunAzimuthDeg: number;
}

export interface Daylight {
  background: THREE.Texture;
  env: THREE.Texture;
  /** Rotation about y to apply to background and environment so the sun is on its bearing. */
  rotation: THREE.Euler;
  /** Unit vector towards the sun, in world space. */
  sunDir: THREE.Vector3;
  sunColor: THREE.Color;
  /** Approximate horizon colour, for fog. */
  horizon: THREE.Color;
  /** How high the sun stands, 0 at the horizon to 1 at the zenith. */
  elevation: number;
}

export function buildDaylight(renderer: THREE.WebGLRenderer | undefined, opts: SkyOptions): Daylight {
  const hdr = assets().sky(opts.sky);
  const want = THREE.MathUtils.degToRad(opts.sunAzimuthDeg);
  // World bearing: 0 = −z (north, towards the enemy), 90 = +x (east).
  const wantDir = new THREE.Vector3(Math.sin(want), 0, -Math.cos(want));
  const have = new THREE.Vector3(hdr.sunDir.x, 0, hdr.sunDir.z).normalize();
  // Rotating about y by `a` adds `a` to a bearing; three's background/environment rotation
  // applies the inverse to the lookup direction, so the same Euler serves both uses.
  const a = Math.atan2(wantDir.x, -wantDir.z) - Math.atan2(have.x, -have.z);
  const rotation = new THREE.Euler(0, a, 0);
  const sunDir = hdr.sunDir.clone().applyEuler(rotation).normalize();
  let env: THREE.Texture = hdr.texture;
  if (renderer) {
    const pmrem = new THREE.PMREMGenerator(renderer);
    env = pmrem.fromEquirectangular(hdr.texture).texture;
    pmrem.dispose();
  }
  return {
    background: hdr.texture,
    env,
    rotation,
    sunDir,
    sunColor: hdr.sunColor.clone(),
    horizon: hdr.horizonColor.clone(),
    elevation: Math.max(0, sunDir.y),
  };
}
