/**
 * The platforms players stand on: cable-stayed road bridges, concrete watchtowers and offshore
 * rigs, all built from primitives with procedural textures. The hit model only knows their tops;
 * railings and furniture are dressing and never stop a bullet.
 */
import * as THREE from 'three';
import type { Ground, Structure } from '@svs/shared';
import { concreteTexture, helipadTexture, roadTexture, rustTexture, steelTexture } from './textures.ts';

export interface StructureMaterials {
  concrete: THREE.MeshStandardMaterial;
  road: THREE.MeshStandardMaterial;
  steel: THREE.MeshStandardMaterial;
  paint: THREE.MeshStandardMaterial;
  rust: THREE.MeshStandardMaterial;
  dark: THREE.MeshStandardMaterial;
  helipad: THREE.MeshStandardMaterial;
}

export function structureMaterials(): StructureMaterials {
  const concreteMap = concreteTexture();
  concreteMap.repeat.set(4, 4);
  const steelMap = steelTexture();
  const rustMap = rustTexture();
  rustMap.repeat.set(3, 3);
  return {
    concrete: new THREE.MeshStandardMaterial({ map: concreteMap, roughness: 0.92, metalness: 0 }),
    road: new THREE.MeshStandardMaterial({ map: roadTexture(), roughness: 0.95, metalness: 0 }),
    steel: new THREE.MeshStandardMaterial({ map: steelMap, color: '#9aa3ab', roughness: 0.45, metalness: 0.75 }),
    paint: new THREE.MeshStandardMaterial({ color: '#3f5a4a', roughness: 0.55, metalness: 0.35 }),
    rust: new THREE.MeshStandardMaterial({ map: rustMap, roughness: 0.8, metalness: 0.35 }),
    dark: new THREE.MeshStandardMaterial({ color: '#2a2d30', roughness: 0.7, metalness: 0.4 }),
    helipad: new THREE.MeshStandardMaterial({ map: helipadTexture(), roughness: 0.9, metalness: 0 }),
  };
}

function box(w: number, h: number, d: number, mat: THREE.Material, x: number, y: number, z: number, shadow = true): THREE.Mesh {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
  m.position.set(x, y, z);
  m.castShadow = shadow;
  m.receiveShadow = true;
  return m;
}

function cylinder(r: number, h: number, mat: THREE.Material, x: number, y: number, z: number, segments = 12): THREE.Mesh {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, h, segments), mat);
  m.position.set(x, y, z);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
}

/** A thin cylinder from a to b (cables, braces). */
function strut(a: THREE.Vector3, b: THREE.Vector3, r: number, mat: THREE.Material): THREE.Mesh {
  const d = new THREE.Vector3().subVectors(b, a);
  const len = d.length();
  const m = new THREE.Mesh(new THREE.CylinderGeometry(r, r, len, 6), mat);
  m.position.copy(a).addScaledVector(d, 0.5);
  m.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), d.normalize());
  m.castShadow = true;
  return m;
}

/**
 * Railing along a straight run between two points, posts every ~2.5 m, two rails. `gap` leaves a
 * stretch open around a point (the shooter's loophole), so nothing crosses the sight line.
 */
function railing(group: THREE.Group, a: THREE.Vector3, b: THREE.Vector3, mat: THREE.Material, height = 1.1, gap?: { at: THREE.Vector3; half: number }): void {
  const d = new THREE.Vector3().subVectors(b, a);
  if (gap) {
    const len = d.length();
    const t = d.dot(new THREE.Vector3().subVectors(gap.at, a)) / (len * len);
    if (t > 0 && t < 1) {
      const t0 = Math.max(0, t - gap.half / len);
      const t1 = Math.min(1, t + gap.half / len);
      if (t0 > 0.01) railing(group, a, a.clone().addScaledVector(d, t0), mat, height);
      if (t1 < 0.99) railing(group, a.clone().addScaledVector(d, t1), b, mat, height);
      return;
    }
  }
  const len = d.length();
  const n = Math.max(1, Math.round(len / 2.5));
  const post = new THREE.BoxGeometry(0.08, height, 0.08);
  const posts = new THREE.InstancedMesh(post, mat, n + 1);
  const m4 = new THREE.Matrix4();
  for (let i = 0; i <= n; i++) {
    const p = a.clone().addScaledVector(d, i / n);
    m4.makeTranslation(p.x, p.y + height / 2, p.z);
    posts.setMatrixAt(i, m4);
  }
  posts.castShadow = true;
  group.add(posts);
  for (const h of [height, height * 0.55]) {
    group.add(strut(a.clone().add(new THREE.Vector3(0, h, 0)), b.clone().add(new THREE.Vector3(0, h, 0)), 0.03, mat));
  }
}

function lampPost(group: THREE.Group, x: number, y: number, z: number, mats: StructureMaterials): void {
  group.add(cylinder(0.08, 7, mats.dark, x, y + 3.5, z, 8));
  const arm = box(1.6, 0.1, 0.1, mats.dark, x + 0.7, y + 7, z);
  group.add(arm);
  const head = box(0.6, 0.18, 0.3, mats.steel, x + 1.4, y + 6.95, z);
  group.add(head);
}

export function addBridge(group: THREE.Group, spec: Extract<Structure, { kind: 'bridge' }>, terrain: Ground, mats: StructureMaterials, facing: 1 | -1): void {
  const z = -spec.s;
  const span = spec.halfSpan * 2;
  const width = spec.halfWidth * 2;
  const deckThick = 1.4;
  // Deck: road on top, concrete sides and underside.
  const road = mats.road.clone();
  road.map = mats.road.map!.clone();
  road.map.repeat.set(span / 12, 1);
  road.map.needsUpdate = true;
  const deckGeo = new THREE.BoxGeometry(span, deckThick, width);
  const deck = new THREE.Mesh(deckGeo, [mats.concrete, mats.concrete, road, mats.concrete, mats.concrete, mats.concrete]);
  deck.position.set(0, spec.deckY - deckThick / 2, z);
  deck.castShadow = true;
  deck.receiveShadow = true;
  group.add(deck);
  // Edge girders and a kerb either side.
  for (const side of [-1, 1]) {
    group.add(box(span, 2.2, 0.8, mats.paint, 0, spec.deckY - deckThick - 1.1, z + side * (spec.halfWidth - 0.4)));
    const kz = z + side * (spec.halfWidth - 0.25);
    const rz = z + side * (spec.halfWidth - 0.3);
    if (side === -facing) {
      // The edge facing the enemy: kerb and railing are broken open at mid-span, where the shooter lies.
      const half = 4;
      group.add(box(spec.halfSpan - half, 0.3, 0.5, mats.concrete, -(spec.halfSpan + half) / 2, spec.deckY + 0.15, kz));
      group.add(box(spec.halfSpan - half, 0.3, 0.5, mats.concrete, (spec.halfSpan + half) / 2, spec.deckY + 0.15, kz));
      railing(group, new THREE.Vector3(-spec.halfSpan, spec.deckY + 0.3, rz), new THREE.Vector3(spec.halfSpan, spec.deckY + 0.3, rz), mats.dark, 1.1, { at: new THREE.Vector3(0, spec.deckY, rz), half });
    } else {
      group.add(box(span, 0.3, 0.5, mats.concrete, 0, spec.deckY + 0.15, kz));
      railing(group, new THREE.Vector3(-spec.halfSpan, spec.deckY + 0.3, rz), new THREE.Vector3(spec.halfSpan, spec.deckY + 0.3, rz), mats.dark);
    }
  }
  // Piers at the abutments and under each pylon, down into the water.
  const pierXs = [-spec.halfSpan + 6, spec.halfSpan - 6, ...spec.pylonsX];
  for (const x of pierXs) {
    const base = Math.min(terrain(x, z), -2) - 6;
    const h = spec.deckY - deckThick - base;
    const pier = box(4, h, width + 2, mats.concrete, x, base + h / 2, z);
    group.add(pier);
    // Cutwater at the water line.
    group.add(box(5.5, 3, width + 4, mats.concrete, x, 0.6, z));
  }
  // Cable-stayed towers above each pylon, with stays fanning out to the deck.
  const towerH = 42;
  for (const x of spec.pylonsX) {
    for (const side of [-1, 1]) {
      const lz = z + side * (spec.halfWidth + 0.8);
      group.add(box(2.4, towerH, 1.8, mats.concrete, x, spec.deckY + towerH / 2, lz));
    }
    group.add(box(2.4, 2, width + 3.4, mats.concrete, x, spec.deckY + towerH - 1, z));
    group.add(box(2.4, 1.6, width + 3.4, mats.concrete, x, spec.deckY + towerH * 0.55, z));
    for (const side of [-1, 1]) {
      const lz = z + side * (spec.halfWidth + 0.8);
      for (let i = 1; i <= 6; i++) {
        const dx = 14 + i * 13;
        const top = new THREE.Vector3(x, spec.deckY + towerH - 2 - i * 1.6, lz);
        for (const dir of [-1, 1]) {
          const ax = x + dir * dx;
          if (Math.abs(ax) > spec.halfSpan - 4) continue;
          group.add(strut(top, new THREE.Vector3(ax, spec.deckY + 0.2, lz), 0.07, mats.steel));
        }
      }
    }
  }
  // Lamp posts, alternating sides.
  for (let x = -spec.halfSpan + 20, i = 0; x < spec.halfSpan - 10; x += 32, i++) {
    lampPost(group, x, spec.deckY, z + (i % 2 ? 1 : -1) * (spec.halfWidth - 0.9), mats);
  }
  // Road continuing onto the banks.
  for (const dir of [-1, 1]) {
    const x0 = dir * spec.halfSpan;
    const roadOn = box(160, 0.4, width, road, x0 + dir * 80, spec.deckY - 0.2, z, false);
    group.add(roadOn);
  }
}

export function addTower(group: THREE.Group, spec: Extract<Structure, { kind: 'tower' }>, terrain: Ground, mats: StructureMaterials, facing: 1 | -1): void {
  const z = -spec.s;
  if (spec.style === 'concrete') {
    const base = Math.min(terrain(spec.x, z), -1) - 4;
    const shaftR = 2.3;
    const shaftH = spec.topY - 0.5 - base;
    group.add(cylinder(shaftR, shaftH, mats.concrete, spec.x, base + shaftH / 2, z, 20));
    // A wider head under the platform, with a dark window band.
    group.add(cylinder(shaftR + 0.9, 3.2, mats.concrete, spec.x, spec.topY - 2.1, z, 20));
    group.add(cylinder(shaftR + 0.95, 0.9, mats.dark, spec.x, spec.topY - 2.6, z, 20));
    const size = spec.halfSize * 2 + 1.2;
    const slab = box(size, 0.5, size, mats.concrete, spec.x, spec.topY - 0.25, z);
    group.add(slab);
    // Railing all round, with a gap at the stair head behind the shooter.
    const h = spec.halfSize + 0.55;
    const c = [
      new THREE.Vector3(spec.x - h, spec.topY, z - h),
      new THREE.Vector3(spec.x + h, spec.topY, z - h),
      new THREE.Vector3(spec.x + h, spec.topY, z + h),
      new THREE.Vector3(spec.x - h, spec.topY, z + h),
    ];
    const loophole = { at: new THREE.Vector3(spec.x, spec.topY, z - facing * h), half: 1.6 };
    for (let i = 0; i < 4; i++) railing(group, c[i]!, c[(i + 1) % 4]!, mats.dark, 1.0, loophole);
    // Ladder down the back face.
    const bz = z + facing * (shaftR + 0.25);
    for (let y = base + 6; y < spec.topY - 3; y += 0.4) group.add(box(0.5, 0.05, 0.05, mats.dark, spec.x, y, bz, false));
    for (const dx of [-0.25, 0.25]) group.add(box(0.05, spec.topY - 3 - base - 6, 0.05, mats.dark, spec.x + dx, (base + 6 + spec.topY - 3) / 2, bz, false));
    // Searchlight housing behind the shooter.
    group.add(box(1.2, 1.0, 1.0, mats.steel, spec.x + 1.6, spec.topY + 0.5, z + facing * (spec.halfSize - 0.6)));
    return;
  }
  // Offshore rig: a steel jacket on four legs, a two-level deck, helideck in front and the
  // process plant, crane and flare stack behind the shooter.
  const seabed = Math.min(terrain(spec.x, z), -10);
  const legR = 1.8;
  const legOff = spec.halfSize - 3;
  const deckBottom = spec.topY - 6;
  const legs: THREE.Vector3[] = [];
  for (const sx of [-1, 1]) {
    for (const sz of [-1, 1]) {
      const lx = spec.x + sx * legOff;
      const lz = z + sz * legOff;
      const h = deckBottom - seabed;
      group.add(cylinder(legR, h, mats.rust, lx, seabed + h / 2, lz, 14));
      legs.push(new THREE.Vector3(lx, 0, lz));
    }
  }
  // Cross-bracing between neighbouring legs at three levels.
  const pairs: Array<[number, number]> = [
    [0, 1],
    [1, 3],
    [3, 2],
    [2, 0],
  ];
  for (let level = 0; level < 3; level++) {
    const y0 = 4 + level * 9;
    const y1 = y0 + 9;
    for (const [i, j] of pairs) {
      const a = legs[i]!;
      const b = legs[j]!;
      group.add(strut(new THREE.Vector3(a.x, y0, a.z), new THREE.Vector3(b.x, y1, b.z), 0.5, mats.rust));
      group.add(strut(new THREE.Vector3(a.x, y1, a.z), new THREE.Vector3(b.x, y0, b.z), 0.5, mats.rust));
      group.add(strut(new THREE.Vector3(a.x, y1, a.z), new THREE.Vector3(b.x, y1, b.z), 0.5, mats.rust));
    }
  }
  const size = spec.halfSize * 2;
  // Lower deck (cellar) and main deck.
  group.add(box(size + 2, 1.2, size + 2, mats.steel, spec.x, deckBottom + 0.6, z));
  group.add(box(size, 1.5, size, mats.paint, spec.x, spec.topY - 0.75, z));
  // Helideck: a raised octagon in the half facing the enemy, where the shooter lies.
  const hz = z - facing * (spec.halfSize - 10);
  const pad = new THREE.Mesh(new THREE.CylinderGeometry(9.5, 9.5, 0.3, 8), mats.helipad);
  pad.position.set(spec.x, spec.topY - 0.15, hz);
  pad.rotation.y = Math.PI / 8;
  pad.receiveShadow = true;
  group.add(pad);
  // Railing round the main deck, open along the helideck's seaward edge.
  const h = spec.halfSize - 0.3;
  const corners = [
    new THREE.Vector3(spec.x - h, spec.topY, z - h),
    new THREE.Vector3(spec.x + h, spec.topY, z - h),
    new THREE.Vector3(spec.x + h, spec.topY, z + h),
    new THREE.Vector3(spec.x - h, spec.topY, z + h),
  ];
  const loophole = { at: new THREE.Vector3(spec.x, spec.topY, z - facing * h), half: 5 };
  for (let i = 0; i < 4; i++) railing(group, corners[i]!, corners[(i + 1) % 4]!, mats.dark, 1.0, loophole);
  // Behind the shooter: accommodation block, process modules, crane and flare boom.
  const bz = z + facing * (spec.halfSize - 7);
  group.add(box(14, 9, 8, mats.steel, spec.x - 8, spec.topY + 4.5, bz));
  group.add(box(5, 2.2, 5, mats.dark, spec.x - 8, spec.topY + 10.1, bz));
  group.add(box(8, 6, 7, mats.rust, spec.x + 7, spec.topY + 3, bz));
  group.add(cylinder(1.6, 7, mats.steel, spec.x + 12.5, spec.topY + 3.5, bz, 14));
  group.add(cylinder(1.6, 7, mats.steel, spec.x + 12.5, spec.topY + 3.5, bz + facing * 4, 14));
  // Crane.
  const craneBase = new THREE.Vector3(spec.x + spec.halfSize - 3, spec.topY, bz - facing * 3);
  group.add(cylinder(1.2, 6, mats.rust, craneBase.x, craneBase.y + 3, craneBase.z, 10));
  group.add(box(3, 2.4, 2.4, mats.rust, craneBase.x, craneBase.y + 7.2, craneBase.z));
  group.add(strut(new THREE.Vector3(craneBase.x, craneBase.y + 7, craneBase.z), new THREE.Vector3(craneBase.x - 10, craneBase.y + 20, craneBase.z + facing * 12), 0.35, mats.rust));
  // Flare boom reaching out over the sea, away from the enemy.
  const flareBase = new THREE.Vector3(spec.x - spec.halfSize + 2, spec.topY + 1, bz + facing * 5);
  const flareTip = new THREE.Vector3(spec.x - spec.halfSize - 24, spec.topY + 16, bz + facing * 22);
  group.add(strut(flareBase, flareTip, 0.45, mats.rust));
  group.add(strut(new THREE.Vector3(flareBase.x + 6, flareBase.y, flareBase.z), flareTip, 0.25, mats.rust));
  const flame = new THREE.Mesh(new THREE.SphereGeometry(1.6, 10, 8), new THREE.MeshBasicMaterial({ color: '#ff8a2a', transparent: true, opacity: 0.85 }));
  flame.position.copy(flareTip).add(new THREE.Vector3(0, 1.4, 0));
  flame.name = 'flare';
  group.add(flame);
  const glow = new THREE.PointLight('#ff9a3a', 60, 80, 1.6);
  glow.position.copy(flame.position);
  group.add(glow);
}
