/**
 * The practice range scene: placeholder low-poly art, but every wind cue is driven by the real
 * wind field (grass by zone, flags, dust drifting downwind).
 */
import * as THREE from 'three';
import type { Vec3 } from '@svs/ballistics';
import type { Silhouette } from '@svs/shared';

export interface RangeView {
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  /** Per frame: time (s), wind lookup (world wind incl. gust at a world point). */
  update(time: number, dt: number, windAt: (p: Vec3) => Vec3): void;
  /** Dust puff at a world point. */
  dust(point: Vec3, size?: number): void;
  /** Plate hit: flash and swing. */
  ringPlate(id: string): void;
  /** Animate a tracer along world points starting now. */
  tracer(points: Array<{ t: number; p: Vec3 }>, until: number): void;
}

const GRASS_COUNT = 9000;

function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function softDotTexture(): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d')!;
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.4, 'rgba(255,255,255,0.6)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

function signTexture(text: string): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 128;
  const g = c.getContext('2d')!;
  g.fillStyle = '#f4f1e6';
  g.fillRect(0, 0, 256, 128);
  g.strokeStyle = '#222';
  g.lineWidth = 8;
  g.strokeRect(4, 4, 248, 120);
  g.fillStyle = '#222';
  g.font = 'bold 72px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(text, 128, 68);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function buildRange(targets: Silhouette[], laneEdges: number[], seed: number, firingPointHeight: number): RangeView {
  const rand = seededRandom(seed);
  const scene = new THREE.Scene();
  const sky = new THREE.Color('#a9cbe6');
  scene.background = sky;
  scene.fog = new THREE.Fog(sky, 1500, 5200);

  const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 8000);
  camera.rotation.order = 'YXZ';

  scene.add(new THREE.HemisphereLight('#dfefff', '#8a7a5a', 1.4));
  const sun = new THREE.DirectionalLight('#fff4dc', 2.2);
  sun.position.set(-300, 500, 200);
  scene.add(sun);

  // Ground: a big plane with vertex-colour variation, a mown lane in the middle.
  const groundGeo = new THREE.PlaneGeometry(6000, 6000, 120, 120);
  groundGeo.rotateX(-Math.PI / 2);
  const colors: number[] = [];
  const pos = groundGeo.attributes.position!;
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    const x = pos.getX(i);
    const z = pos.getZ(i);
    const inLane = Math.abs(x) < 70 && z < 10 && z > -1000;
    const n = rand();
    c.setHSL(inLane ? 0.2 : 0.17 + n * 0.04, inLane ? 0.35 : 0.3 + n * 0.1, inLane ? 0.42 + n * 0.04 : 0.36 + n * 0.08);
    colors.push(c.r, c.g, c.b);
    // Rolling terrain outside the lane, dead flat inside it (the range is flat for Phase 1).
    if (!(Math.abs(x) < 120 && z < 60 && z > -1100)) {
      const d = Math.min(1, (Math.abs(x) - 120) / 300 + Math.max(0, -z - 1100) / 300 + Math.max(0, z - 60) / 300);
      pos.setY(i, Math.max(0, d) * (Math.sin(x * 0.004) * 18 + Math.cos(z * 0.003) * 14 + 16));
    }
  }
  groundGeo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  groundGeo.computeVertexNormals();
  scene.add(new THREE.Mesh(groundGeo, new THREE.MeshLambertMaterial({ vertexColors: true })));

  // Raised firing bench the shooter lies on.
  const bench = new THREE.Mesh(new THREE.BoxGeometry(6, firingPointHeight, 4), new THREE.MeshLambertMaterial({ color: '#7a6a52' }));
  bench.position.set(0, firingPointHeight / 2, 1.2);
  scene.add(bench);

  // Distant hills for a horizon.
  const hillMat = new THREE.MeshLambertMaterial({ color: '#7d8a6a', flatShading: true });
  for (let i = 0; i < 26; i++) {
    const hill = new THREE.Mesh(new THREE.IcosahedronGeometry(1, 1), hillMat);
    const a = (i / 26) * Math.PI * 2;
    const r = 2400 + rand() * 900;
    hill.position.set(Math.sin(a) * r, -40, -Math.cos(a) * r);
    hill.scale.set(300 + rand() * 400, 120 + rand() * 220, 300 + rand() * 300);
    scene.add(hill);
  }

  // Earth berm behind the steel.
  const bermMat = new THREE.MeshLambertMaterial({ color: '#8b7350', flatShading: true });
  const berm = new THREE.Mesh(new THREE.BoxGeometry(260, 14, 30), bermMat);
  berm.position.set(0, 7, -1040);
  scene.add(berm);

  // Steel silhouettes on posts.
  const plates = new Map<string, { pivot: THREE.Group; mat: THREE.MeshLambertMaterial; swing: number; flash: number }>();
  for (const t of targets) {
    const shape = new THREE.Shape();
    const hw = t.width / 2;
    const body = t.height - t.head;
    const headW = t.width * 0.2;
    shape.moveTo(-hw, 0);
    shape.lineTo(hw, 0);
    shape.lineTo(hw, body * 0.85);
    shape.lineTo(hw * 0.75, body);
    shape.lineTo(headW, body);
    shape.lineTo(headW, t.height);
    shape.lineTo(-headW, t.height);
    shape.lineTo(-headW, body);
    shape.lineTo(-hw * 0.75, body);
    shape.lineTo(-hw, body * 0.85);
    shape.closePath();
    const geo = new THREE.ShapeGeometry(shape);
    geo.translate(0, -t.height, 0); // pivot at the top hanger
    const mat = new THREE.MeshLambertMaterial({ color: '#efe9d8', side: THREE.DoubleSide });
    const plate = new THREE.Mesh(geo, mat);
    const pivot = new THREE.Group();
    pivot.position.set(t.position.x, t.position.y + t.height, t.position.z);
    pivot.rotation.y = t.facingYaw - Math.PI;
    pivot.add(plate);
    scene.add(pivot);
    const postMat = new THREE.MeshLambertMaterial({ color: '#3b3b3b' });
    const frame = new THREE.Mesh(new THREE.BoxGeometry(t.width + 0.3, 0.05, 0.05), postMat);
    frame.position.set(t.position.x, t.position.y + t.height + 0.05, t.position.z - 0.05);
    scene.add(frame);
    for (const s of [-1, 1]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.05, t.height + 0.1, 0.05), postMat);
      leg.position.set(t.position.x + s * (t.width / 2 + 0.15), t.position.y + (t.height + 0.1) / 2, t.position.z - 0.05);
      scene.add(leg);
    }
    const sign = new THREE.Mesh(
      new THREE.PlaneGeometry(1.2, 0.6),
      new THREE.MeshBasicMaterial({ map: signTexture(`${Math.round(-t.position.z)}`) }),
    );
    sign.position.set(t.position.x + 1.6, 0.5, t.position.z);
    scene.add(sign);
    plates.set(t.id, { pivot, mat, swing: 0, flash: 0 });
  }

  // Grass tufts, swaying with the wind of the zone they stand in (shader reads per-zone wind).
  const tuft = new THREE.BufferGeometry();
  const tv: number[] = [];
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI;
    const dx = Math.cos(a) * 0.18;
    const dz = Math.sin(a) * 0.18;
    tv.push(-dx, 0, -dz, dx, 0, dz, 0, 0.6, 0);
  }
  tuft.setAttribute('position', new THREE.Float32BufferAttribute(tv, 3));
  tuft.computeVertexNormals();
  const grassUniforms = {
    uTime: { value: 0 },
    uWind: { value: [new THREE.Vector2(), new THREE.Vector2(), new THREE.Vector2()] },
    uEdges: { value: new THREE.Vector2(laneEdges[0] ?? 1e9, laneEdges[1] ?? 1e9) },
  };
  const grassMat = new THREE.MeshLambertMaterial({ color: '#9aa45a', side: THREE.DoubleSide });
  grassMat.onBeforeCompile = (shader) => {
    Object.assign(shader.uniforms, grassUniforms);
    shader.vertexShader = shader.vertexShader
      .replace(
        '#include <common>',
        `#include <common>
         uniform float uTime; uniform vec2 uWind[3]; uniform vec2 uEdges;`,
      )
      .replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
         vec4 ip = instanceMatrix * vec4(0.0, 0.0, 0.0, 1.0);
         float lane = -ip.z;
         vec2 w = lane < uEdges.x ? uWind[0] : (lane < uEdges.y ? uWind[1] : uWind[2]);
         float speed = length(w);
         float h = position.y * position.y;
         float flutter = 0.6 + 0.4 * sin(uTime * (2.0 + speed * 0.6) + ip.x * 0.7 + ip.z * 0.31);
         transformed.xz += w * h * 0.11 * flutter;
         transformed.y -= h * min(speed * 0.04, 0.35) * flutter;`,
      );
  };
  const grass = new THREE.InstancedMesh(tuft, grassMat, GRASS_COUNT);
  const m4 = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  for (let i = 0; i < GRASS_COUNT; i++) {
    // Tall grass in bands either side of the lane, where you read the wind; short mown grass in it.
    const z = -(15 + rand() * 1000);
    const inLane = rand() < 0.3;
    const side = rand() < 0.5 ? -1 : 1;
    const x = inLane ? (rand() * 2 - 1) * 30 : side * (32 + rand() * 120);
    q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), rand() * Math.PI);
    const s = inLane ? 0.3 + rand() * 0.25 : 0.8 + rand() * 0.9;
    m4.compose(new THREE.Vector3(x, 0, z), q, new THREE.Vector3(s, s, s));
    grass.setMatrixAt(i, m4);
  }
  scene.add(grass);

  // Wind flags beside the lane.
  const flags: Array<{ group: THREE.Group; cloth: THREE.Mesh; pos: Vec3 }> = [];
  const poleMat = new THREE.MeshLambertMaterial({ color: '#cccccc' });
  const clothMat = new THREE.MeshLambertMaterial({ color: '#e0412b', side: THREE.DoubleSide });
  for (const [x, z] of [
    [-22, -150],
    [26, -450],
    [-30, -750],
    [20, -880],
  ] as const) {
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.05, 6), poleMat);
    pole.position.set(x, 3, z);
    scene.add(pole);
    const group = new THREE.Group();
    group.position.set(x, 5.6, z);
    const clothGeo = new THREE.PlaneGeometry(1.6, 0.9, 8, 1);
    clothGeo.translate(0.8, -0.45, 0);
    const cloth = new THREE.Mesh(clothGeo, clothMat);
    group.add(cloth);
    scene.add(group);
    flags.push({ group, cloth, pos: { x, y: 5.6, z } });
  }

  // Dust puffs.
  const dotTex = softDotTexture();
  const puffs: Array<{ sprite: THREE.Sprite; age: number; life: number; vel: THREE.Vector3; size: number }> = [];
  const dust = (point: Vec3, size = 1): void => {
    for (let i = 0; i < 9; i++) {
      const mat = new THREE.SpriteMaterial({ map: dotTex, color: '#b59a6c', transparent: true, depthWrite: false });
      const sprite = new THREE.Sprite(mat);
      sprite.position.set(point.x + (rand() - 0.5) * 0.3, point.y + 0.1 + rand() * 0.2, point.z + (rand() - 0.5) * 0.3);
      scene.add(sprite);
      puffs.push({
        sprite,
        age: 0,
        life: 1.6 + rand() * 1.4,
        vel: new THREE.Vector3((rand() - 0.5) * 1.2, 1.2 + rand() * 2.2, (rand() - 0.5) * 1.2),
        size: size * (0.5 + rand() * 0.6),
      });
    }
  };

  // Tracers.
  const tracers: Array<{ sprite: THREE.Sprite; points: Array<{ t: number; p: Vec3 }>; start: number; until: number }> = [];
  let clock = 0;
  const tracer = (points: Array<{ t: number; p: Vec3 }>, until: number): void => {
    const mat = new THREE.SpriteMaterial({ map: dotTex, color: '#fff7c0', transparent: true, depthWrite: false, opacity: 0.85 });
    const sprite = new THREE.Sprite(mat);
    sprite.scale.setScalar(0.25);
    scene.add(sprite);
    tracers.push({ sprite, points, start: clock, until });
  };

  const ringPlate = (id: string): void => {
    const p = plates.get(id);
    if (!p) return;
    p.swing = 0.5;
    p.flash = 1;
  };

  const update = (time: number, dt: number, windAt: (p: Vec3) => Vec3): void => {
    clock = time;
    grassUniforms.uTime.value = time;
    for (let i = 0; i < 3; i++) {
      const lane = i === 0 ? 50 : i === 1 ? (laneEdges[0] ?? 300) + 10 : (laneEdges[1] ?? 600) + 10;
      const w = windAt({ x: 0, y: 0, z: -lane });
      grassUniforms.uWind.value[i]!.set(w.x, w.z);
    }
    for (const f of flags) {
      const w = windAt(f.pos);
      const speed = Math.hypot(w.x, w.z);
      // The cloth extends downwind (+x of the flag group points along the wind).
      f.group.rotation.order = 'YZX';
      f.group.rotation.y = Math.atan2(-w.z, w.x);
      f.group.rotation.z = -Math.max(0, 1.35 - speed * 0.2);
      const geo = f.cloth.geometry as THREE.PlaneGeometry;
      const pos = geo.attributes.position!;
      for (let i = 0; i < pos.count; i++) {
        const u = pos.getX(i);
        pos.setZ(i, Math.sin(u * 4 - time * (3 + speed)) * 0.08 * u * Math.min(1, speed / 3));
      }
      pos.needsUpdate = true;
    }
    for (const [, p] of plates) {
      if (p.swing > 0.001 || Math.abs(p.pivot.rotation.x) > 0.001) {
        p.pivot.rotation.x = p.swing * Math.sin(time * 9);
        p.swing *= Math.exp(-dt * 1.8);
      }
      if (p.flash > 0) {
        p.flash = Math.max(0, p.flash - dt * 1.5);
        p.mat.color.set('#efe9d8').lerp(new THREE.Color('#ff7b2e'), p.flash);
      }
    }
    for (let i = puffs.length - 1; i >= 0; i--) {
      const puff = puffs[i]!;
      puff.age += dt;
      const w = windAt({ x: puff.sprite.position.x, y: 0, z: puff.sprite.position.z });
      puff.vel.multiplyScalar(Math.exp(-dt * 1.5));
      puff.sprite.position.x += (puff.vel.x + w.x * 0.8) * dt;
      puff.sprite.position.y += puff.vel.y * dt;
      puff.sprite.position.z += (puff.vel.z + w.z * 0.8) * dt;
      const k = puff.age / puff.life;
      puff.sprite.scale.setScalar(puff.size * (0.6 + k * 2.2));
      (puff.sprite.material as THREE.SpriteMaterial).opacity = 0.8 * (1 - k);
      if (k >= 1) {
        scene.remove(puff.sprite);
        puff.sprite.material.dispose();
        puffs.splice(i, 1);
      }
    }
    for (let i = tracers.length - 1; i >= 0; i--) {
      const tr = tracers[i]!;
      const tf = time - tr.start;
      if (tf >= tr.until) {
        scene.remove(tr.sprite);
        tr.sprite.material.dispose();
        tracers.splice(i, 1);
        continue;
      }
      let j = 1;
      while (j < tr.points.length - 1 && tr.points[j]!.t < tf) j++;
      const a = tr.points[j - 1]!;
      const b = tr.points[j]!;
      const f = Math.min(1, Math.max(0, (tf - a.t) / (b.t - a.t || 1)));
      tr.sprite.position.set(a.p.x + (b.p.x - a.p.x) * f, a.p.y + (b.p.y - a.p.y) * f, a.p.z + (b.p.z - a.p.z) * f);
      // Keep it visible at range: grow with distance from the shooter.
      tr.sprite.scale.setScalar(0.08 + tr.sprite.position.length() * 0.0012);
    }
  };

  return { scene, camera, update, dust, ringPlate, tracer };
}
