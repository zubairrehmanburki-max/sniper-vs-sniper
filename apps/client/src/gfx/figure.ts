/**
 * A sniper figure built from primitives, posed to match the hitbox silhouette of each stance:
 * head at the silhouette's top, body behind it for prone. Nothing here is hit-tested.
 */
import * as THREE from 'three';
import type { Stance } from '@svs/shared';

export interface FigureDims {
  heightM: number;
  widthM: number;
  headM: number;
}

const uniform = new THREE.MeshStandardMaterial({ color: '#4d5a3c', roughness: 0.9 });
const uniformDark = new THREE.MeshStandardMaterial({ color: '#3c4631', roughness: 0.9 });
const skin = new THREE.MeshStandardMaterial({ color: '#b98d6a', roughness: 0.7 });
const helmet = new THREE.MeshStandardMaterial({ color: '#3a4030', roughness: 0.6 });
const gun = new THREE.MeshStandardMaterial({ color: '#1f2224', roughness: 0.4, metalness: 0.6 });
const glass = new THREE.MeshStandardMaterial({ color: '#223344', roughness: 0.2, metalness: 0.6 });

function part(geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number, rx = 0, ry = 0, rz = 0): THREE.Mesh {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(x, y, z);
  m.rotation.set(rx, ry, rz);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
}

const bx = (w: number, h: number, d: number): THREE.BoxGeometry => new THREE.BoxGeometry(w, h, d);

/** Rifle with scope, barrel along −z (forward). */
function rifle(): THREE.Group {
  const g = new THREE.Group();
  g.add(part(bx(0.05, 0.07, 0.55), gun, 0, 0, -0.1)); // receiver + stock
  g.add(part(new THREE.CylinderGeometry(0.014, 0.014, 0.6, 8), gun, 0, 0.02, -0.65, Math.PI / 2));
  g.add(part(new THREE.CylinderGeometry(0.022, 0.022, 0.26, 10), glass, 0, 0.075, -0.18, Math.PI / 2));
  g.add(part(bx(0.04, 0.08, 0.05), uniformDark, 0, -0.06, -0.02)); // magazine
  return g;
}

/**
 * Build a figure facing −z; the caller rotates it to the facing yaw. Head centre is at
 * y = height − head/2 so the model sits exactly on the silhouette the hit test uses.
 */
export function buildFigure(stance: Stance, dims: FigureDims): THREE.Group {
  const g = new THREE.Group();
  const H = dims.heightM;
  const hd = dims.headM;
  const headY = H - hd / 2;
  const head = part(new THREE.SphereGeometry(hd * 0.46, 12, 10), skin, 0, headY, 0);
  const hat = part(new THREE.SphereGeometry(hd * 0.52, 12, 10, 0, Math.PI * 2, 0, Math.PI * 0.55), helmet, 0, headY + 0.01, 0);
  g.add(head, hat);

  if (stance === 'prone') {
    // Lying flat, legs trailing behind (+z), rifle on a bipod out in front.
    const torso = part(bx(dims.widthM * 0.9, 0.24, 0.85), uniform, 0, 0.14, 0.5);
    const hips = part(bx(dims.widthM * 0.7, 0.2, 0.4), uniformDark, 0, 0.11, 1.05);
    const legL = part(bx(0.16, 0.16, 0.75), uniformDark, -0.11, 0.09, 1.6);
    const legR = part(bx(0.16, 0.16, 0.75), uniformDark, 0.12, 0.09, 1.62, 0, 0.08);
    const armL = part(bx(0.11, 0.11, 0.5), uniform, -0.2, 0.22, -0.05, 0.3);
    const armR = part(bx(0.11, 0.11, 0.45), uniform, 0.18, 0.2, 0.02, 0.4);
    const r = rifle();
    r.position.set(0.02, headY - 0.05, -0.2);
    const bipod = new THREE.Group();
    for (const s of [-1, 1]) bipod.add(part(new THREE.CylinderGeometry(0.008, 0.008, 0.3, 6), gun, s * 0.09, 0.15, 0, 0, 0, s * 0.35));
    bipod.position.set(0.02, 0, -0.7);
    g.add(torso, hips, legL, legR, armL, armR, r, bipod);
    return g;
  }

  const standing = stance === 'standing';
  const legH = standing ? H * 0.46 : H * 0.3;
  const torsoH = standing ? H * 0.33 : H * 0.4;
  const torsoY = legH + torsoH / 2;
  // Legs: straight when standing, folded when crouched.
  for (const s of [-1, 1]) {
    if (standing) g.add(part(bx(0.17, legH, 0.2), uniformDark, s * 0.11, legH / 2, 0));
    else {
      g.add(part(bx(0.17, 0.18, 0.5), uniformDark, s * 0.13, 0.1, 0.1)); // shin along the ground
      g.add(part(bx(0.17, legH, 0.2), uniformDark, s * 0.13, legH / 2 + 0.05, -0.1, -0.5));
    }
  }
  g.add(part(bx(dims.widthM * 0.9, torsoH, 0.28), uniform, 0, torsoY, 0, standing ? 0 : 0.15));
  g.add(part(bx(dims.widthM * 1.05, 0.12, 0.3), uniformDark, 0, torsoY + torsoH / 2 - 0.02, 0)); // shoulders / vest
  // Arms raised to the rifle, which sits at shoulder height, barrel forward.
  const shoulderY = torsoY + torsoH / 2 - 0.08;
  g.add(part(bx(0.1, 0.1, 0.42), uniform, -0.2, shoulderY - 0.1, -0.18, 0.2));
  g.add(part(bx(0.1, 0.1, 0.36), uniform, 0.2, shoulderY - 0.12, -0.08, 0.3));
  const r = rifle();
  r.position.set(0.06, shoulderY, -0.15);
  g.add(r);
  return g;
}
