/**
 * Trees as crossed billboards: two quads at right angles, each cut from a painted atlas (four
 * pines, two shrubs), instanced by the thousand. They cast shadows through the alpha test and
 * sway a little in the wind.
 */
import * as THREE from 'three';
import { assets } from './assets.ts';

const CELLS = 6;
const PINES = 4;

export interface TreeField {
  mesh: THREE.InstancedMesh;
  update(time: number, wind: { x: number; z: number }): void;
}

/**
 * `place(i)` yields position, scale and a random number per tree, or null to stop. Pines when
 * `kind` is 'pine', shrubs otherwise.
 */
export function buildTrees(kind: 'pine' | 'shrub', count: number, place: (rand: () => number) => { x: number; y: number; z: number; scale: number } | null, rand: () => number): TreeField {
  const atlas = assets().texture('trees');
  atlas.wrapS = atlas.wrapT = THREE.ClampToEdgeWrapping;
  // Two crossed quads, 1 m wide and 2 m tall, pivot at the ground. uv x is remapped per instance.
  const quad = (angle: number): THREE.BufferGeometry => {
    const g = new THREE.PlaneGeometry(1, 2);
    g.translate(0, 1, 0);
    g.rotateY(angle);
    return g;
  };
  const a = quad(0);
  const b = quad(Math.PI / 2);
  const pos = [...(a.attributes.position!.array as Float32Array), ...(b.attributes.position!.array as Float32Array)];
  const uv = [...(a.attributes.uv!.array as Float32Array), ...(b.attributes.uv!.array as Float32Array)];
  const idx = [...(a.index!.array as Uint16Array), ...Array.from(b.index!.array as Uint16Array, (i) => i + 4)];
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  // Light the cards as if they were a rounded crown: normals tilted up.
  geo.setAttribute('normal', new THREE.Float32BufferAttribute(new Array(8).fill([0, 0.8, 0.6]).flat(), 3));
  geo.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  geo.setIndex(idx);
  const cell = new Float32Array(count);
  geo.setAttribute('cell', new THREE.InstancedBufferAttribute(cell, 1));

  const uniforms = { uTime: { value: 0 }, uWind: { value: new THREE.Vector2(0, 0) } };
  const mat = new THREE.MeshStandardMaterial({ map: atlas, alphaTest: 0.5, side: THREE.DoubleSide, roughness: 1, metalness: 0 });
  mat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\nattribute float cell; uniform float uTime; uniform vec2 uWind;`)
      .replace('#include <uv_vertex>', `#include <uv_vertex>\nvMapUv.x = (cell + vMapUv.x) / ${CELLS.toFixed(1)};`)
      .replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
         vec4 ip = instanceMatrix * vec4(0.0, 0.0, 0.0, 1.0);
         float h = position.y * 0.5;
         float sway = sin(uTime * 1.3 + ip.x * 0.05 + ip.z * 0.07) * 0.5 + 0.5;
         transformed.xz += uWind * h * h * 0.03 * sway;`,
      );
  };
  const mesh = new THREE.InstancedMesh(geo, mat, count);
  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const up = new THREE.Vector3(0, 1, 0);
  let n = 0;
  for (let tries = 0; n < count && tries < count * 6; tries++) {
    const p = place(rand);
    if (!p) break;
    q.setFromAxisAngle(up, rand() * Math.PI);
    m4.compose(new THREE.Vector3(p.x, p.y, p.z), q, new THREE.Vector3(p.scale, p.scale, p.scale));
    mesh.setMatrixAt(n, m4);
    cell[n] = kind === 'pine' ? Math.floor(rand() * PINES) : PINES + Math.floor(rand() * (CELLS - PINES));
    n++;
  }
  mesh.count = n;
  mesh.castShadow = true;
  mesh.receiveShadow = false;
  mesh.customDepthMaterial = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking, map: atlas, alphaTest: 0.5 });
  (mesh.customDepthMaterial as THREE.MeshDepthMaterial).onBeforeCompile = (shader) => {
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', `#include <common>\nattribute float cell;`)
      .replace('#include <uv_vertex>', `#include <uv_vertex>\nvMapUv.x = (cell + vMapUv.x) / ${CELLS.toFixed(1)};`);
  };
  return {
    mesh,
    update(time, wind) {
      uniforms.uTime.value = time;
      uniforms.uWind.value.set(wind.x, wind.z);
    },
  };
}
