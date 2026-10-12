/**
 * Daylight: a physical sky (Preetham, via three's Sky) with the sun placed per map, baked once
 * into an environment map so metals and wet surfaces reflect something.
 */
import * as THREE from 'three';
import { Sky } from 'three/examples/jsm/objects/Sky.js';

export interface SkyOptions {
  /** Sun elevation and azimuth, degrees. Azimuth 0 is due south (behind player A), 90 is east. */
  elevationDeg: number;
  azimuthDeg: number;
  /** Haze: 2 is a crisp day, 10 is milky. */
  turbidity: number;
  rayleigh: number;
}

export interface Daylight {
  /** The sky dome mesh; only added to the scene when it could not be baked to `background`. */
  sky: Sky;
  sunDir: THREE.Vector3;
  env?: THREE.Texture;
  /** The sky rendered once into a cubemap: far cheaper per frame than the analytic shader. */
  background?: THREE.CubeTexture;
  /** Approximate horizon colour, for fog. */
  horizon: THREE.Color;
}

export function buildDaylight(renderer: THREE.WebGLRenderer | undefined, opts: SkyOptions): Daylight {
  const sky = new Sky();
  sky.scale.setScalar(7000);
  const u = sky.material.uniforms;
  u.turbidity!.value = opts.turbidity;
  u.rayleigh!.value = opts.rayleigh;
  u.mieCoefficient!.value = 0.004;
  u.mieDirectionalG!.value = 0.8;
  const phi = THREE.MathUtils.degToRad(90 - opts.elevationDeg);
  const theta = THREE.MathUtils.degToRad(opts.azimuthDeg);
  const sunDir = new THREE.Vector3().setFromSphericalCoords(1, phi, theta);
  (u.sunPosition!.value as THREE.Vector3).copy(sunDir);

  let env: THREE.Texture | undefined;
  let background: THREE.CubeTexture | undefined;
  if (renderer) {
    const envScene = new THREE.Scene();
    envScene.add(sky);
    const target = new THREE.WebGLCubeRenderTarget(512, { generateMipmaps: true, minFilter: THREE.LinearMipmapLinearFilter });
    const cubeCamera = new THREE.CubeCamera(1, 20000, target);
    cubeCamera.update(renderer, envScene);
    background = target.texture;
    const pmrem = new THREE.PMREMGenerator(renderer);
    env = pmrem.fromCubemap(background).texture;
    pmrem.dispose();
    envScene.remove(sky);
  }
  // The horizon of a hazy sky reads as a pale blue-grey; warmer when the sun is low.
  const low = 1 - Math.min(1, opts.elevationDeg / 40);
  const horizon = new THREE.Color().setHSL(0.58 - low * 0.05, 0.25 + low * 0.2, 0.78 - low * 0.08);
  return { sky, sunDir, env, background, horizon };
}
