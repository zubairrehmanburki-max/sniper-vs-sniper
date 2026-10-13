/**
 * Open water: three's reflective Water (planar reflection of the sky, the structures and the far
 * shore, distorted by a scrolling normal map, with the sun's glitter). The reflection pass skips
 * the grass and the tree billboards, which cost most and would never be noticed in the ripples.
 */
import * as THREE from 'three';
import { Water } from 'three/examples/jsm/objects/Water.js';
import { assets } from './assets.ts';

export interface WaterSurface {
  mesh: THREE.Mesh;
  update(time: number, wind: { x: number; z: number }, cameraPos: THREE.Vector3): void;
}

export interface WaterOptions {
  level: number;
  size: number;
  sunDir: THREE.Vector3;
  sunColor: THREE.Color;
  color: THREE.Color;
  /** Objects hidden while the reflection is rendered. */
  skipInReflection: () => THREE.Object3D[];
  /** Reflection texture size; 512 suits phones. */
  textureSize?: number;
}

export function buildWater(opts: WaterOptions): WaterSurface {
  const normals = assets().texture('waterNormals');
  const geo = new THREE.PlaneGeometry(opts.size, opts.size);
  const water = new Water(geo, {
    textureWidth: opts.textureSize ?? 512,
    textureHeight: opts.textureSize ?? 512,
    waterNormals: normals,
    sunDirection: opts.sunDir.clone(),
    sunColor: opts.sunColor.clone(),
    waterColor: opts.color.clone(),
    distortionScale: 2.4,
    fog: true,
    clipBias: 0.02,
  });
  water.rotation.x = -Math.PI / 2;
  water.position.y = opts.level;
  water.frustumCulled = false;
  const uniforms = (water.material as THREE.ShaderMaterial).uniforms;
  uniforms.size!.value = 6.0;
  const inner = water.onBeforeRender;
  water.onBeforeRender = (renderer, scene, camera, geometry, material, group) => {
    const hidden = opts.skipInReflection().filter((o) => o.visible);
    for (const o of hidden) o.visible = false;
    inner.call(water, renderer, scene, camera, geometry, material, group);
    for (const o of hidden) o.visible = true;
  };
  return {
    mesh: water,
    update(time, wind, cameraPos) {
      const speed = Math.hypot(wind.x, wind.z);
      uniforms.time!.value = time * (0.5 + Math.min(1.2, speed * 0.1));
      uniforms.distortionScale!.value = 1.5 + Math.min(3, speed * 0.3);
      water.position.x = cameraPos.x;
      water.position.z = cameraPos.z;
    },
  };
}
