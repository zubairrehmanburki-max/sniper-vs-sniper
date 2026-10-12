/**
 * The 3D world: terrain, sky, water, structures, cover, figures and every wind cue (grass by zone,
 * flags, dust and clouds drifting downwind), all driven by the real wind field. Everything is
 * procedural: no asset files.
 */
import * as THREE from 'three';
import type { Vec3 } from '@svs/ballistics';
import type { Box, Ground, Palette, Silhouette, Stance, Structure } from '@svs/shared';
import { buildFigure } from './gfx/figure.ts';
import { buildDaylight } from './gfx/sky.ts';
import { addBridge, addTower, structureMaterials } from './gfx/structures.ts';
import { cloudTexture, concreteTexture, fbm, grainTexture, signTexture, softDotTexture, steelTexture, woodTexture } from './gfx/textures.ts';
import { buildWater, type WaterSurface } from './gfx/water.ts';

export interface WorldOptions {
  seed: number;
  /** Hit-test ground (platform tops included). */
  ground: Ground;
  /** Terrain as drawn; defaults to `ground`. */
  terrain?: Ground;
  /** Water level, if the map has open water. */
  water?: number;
  structures?: Structure[];
  /** Lane length (m) and wind-zone edges along it. */
  laneLength: number;
  laneEdges: number[];
  /** Steel plates (practice range). */
  steel?: Silhouette[];
  /** Rocks and other cover boxes. */
  cover?: Box[];
  /** Height of a raised firing bench at the origin, if any. */
  bench?: number;
  /** Lateral half-width of the lane, m. */
  laneHalfWidth?: number;
  flags?: Array<[number, number]>;
  palette?: Palette;
  /** Needed to bake the sky into reflections; without it materials use flat ambient light. */
  renderer?: THREE.WebGLRenderer;
}

export interface RangeView {
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  /** Per frame: time (s), wind lookup (world wind incl. gust at a world point). */
  update(time: number, dt: number, windAt: (p: Vec3) => Vec3): void;
  /** Dust puff (or a splash on water) at a world point. */
  dust(point: Vec3, size?: number): void;
  /** Plate hit: flash and swing. */
  ringPlate(id: string): void;
  /** Animate a tracer along world points starting now. */
  tracer(points: Array<{ t: number; p: Vec3 }>, until: number): void;
  /** Show or move a player figure (duel). */
  setFigure(id: string, figure: { position: Vec3; stance: Stance; facingYaw: number; dims: { heightM: number; widthM: number; headM: number } } | null): void;
}

const GRASS_COUNT = 9000;
const TREE_COUNT = 1800;

function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const smoothstep = (e0: number, e1: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - e0) / (e1 - e0)));
  return t * t * (3 - 2 * t);
};

export function buildWorld(opts: WorldOptions): RangeView {
  const { seed, ground, laneLength, laneEdges } = opts;
  const terrain = opts.terrain ?? ground;
  const targets = opts.steel ?? [];
  const halfWidth = opts.laneHalfWidth ?? 70;
  const rand = seededRandom(seed);
  const pal = opts.palette ?? {};
  const water = opts.water;
  const scene = new THREE.Scene();
  const centre = new THREE.Vector3(0, 0, -laneLength / 2);

  // ---- light and air ----------------------------------------------------------------------
  const daylight = buildDaylight(opts.renderer, { elevationDeg: 34, azimuthDeg: 52, turbidity: 3.2, rayleigh: 1.6 });
  if (daylight.background) scene.background = daylight.background;
  else {
    daylight.sky.name = 'sky';
    scene.add(daylight.sky);
  }
  if (daylight.env) {
    scene.environment = daylight.env;
    scene.environmentIntensity = 0.45;
  }
  const fogColor = pal.sky ? new THREE.Color(pal.sky).lerp(daylight.horizon, 0.5) : daylight.horizon;
  const fog = new THREE.Fog(fogColor, 700, 6500);
  scene.fog = fog;

  const camera = new THREE.PerspectiveCamera(60, 1, 0.05, 9000);
  camera.rotation.order = 'YXZ';

  const hemi = new THREE.HemisphereLight('#cfe0f5', '#6d6450', 0.55);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight('#fff1dc', 2.6);
  sun.name = 'sun';
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.camera.near = 1;
  sun.shadow.camera.far = 900;
  sun.shadow.camera.left = sun.shadow.camera.bottom = -90;
  sun.shadow.camera.right = sun.shadow.camera.top = 90;
  sun.shadow.bias = -0.0004;
  sun.shadow.normalBias = 0.6;
  scene.add(sun, sun.target);

  // ---- terrain ----------------------------------------------------------------------------
  const c = new THREE.Color();
  const groundColor = (x: number, z: number, y: number, out: THREE.Color): void => {
    const inLane = Math.abs(x) < halfWidth && z < 10 && z > -laneLength - 20;
    const n = rand();
    const [lh, ls, ll] = pal.lane ?? [0.2, 0.35, 0.42];
    const [oh, os, ol] = pal.outside ?? [0.17, 0.3, 0.36];
    if (water !== undefined && y < water - 0.3) {
      // Sea or river bed: dark, seen only through the shallows.
      const [wh, ws, wl] = pal.water ?? [0.55, 0.35, 0.3];
      out.setHSL(wh, ws * 0.6, wl * 0.35);
    } else if (water !== undefined && y < water + 1.5) {
      out.setHSL(0.11, 0.3, 0.42 + n * 0.05); // wet sand and mud at the shore
    } else if (inLane) out.setHSL(lh, ls, ll + n * 0.04);
    else out.setHSL(oh + n * 0.04, os + n * 0.1, ol + n * 0.08);
  };
  // Mountains ringing the far distance, past the playable terrain.
  const farRelief = (x: number, z: number): number => {
    const r = Math.hypot(x, z - centre.z);
    const k = smoothstep(1500, 2700, r);
    if (k <= 0) return 0;
    const a = Math.atan2(x, z - centre.z);
    const n = fbm(a / Math.PI, r / 9000, seed & 1023, 5, 3);
    return k * (120 + 520 * n * n);
  };
  const grain = grainTexture(seed);
  const terrainMat = new THREE.MeshStandardMaterial({ vertexColors: true, map: grain, roughness: 1, metalness: 0 });
  const snowAbove = pal.snowAbove;
  const hillColor = new THREE.Color(pal.hills ?? '#7d8a6a');
  const snow = new THREE.Color('#eef2f5');

  // Fine mesh over the playable lane, so what you see matches the ground the bullet hits.
  const laneW = 2 * (halfWidth + 80);
  const laneL = laneLength + 200;
  const fine = new THREE.PlaneGeometry(laneW, laneL, Math.ceil(laneW / 5), Math.ceil(laneL / 5));
  fine.rotateX(-Math.PI / 2);
  fine.translate(0, 0, -laneLength / 2);
  // Coarse surround to the horizon, sunk slightly where it overlaps the lane mesh.
  const coarse = new THREE.PlaneGeometry(7000, 7000, 128, 128);
  coarse.rotateX(-Math.PI / 2);
  coarse.translate(0, 0, -laneLength / 2);
  for (const [geo, inset, far] of [
    [fine, 0, false],
    [coarse, 0.4, true],
  ] as const) {
    const pos = geo.attributes.position!;
    const uv = geo.attributes.uv!;
    const colors: number[] = [];
    const scale = far ? 7000 / 9 : laneW / 9;
    for (let i = 0; i < pos.count; i++) {
      const x = pos.getX(i);
      const z = pos.getZ(i);
      let y = terrain(x, z) - inset;
      const relief = far ? farRelief(x, z) : 0;
      y += relief;
      groundColor(x, z, y, c);
      if (relief > 0) {
        c.lerp(hillColor, Math.min(1, relief / 400));
        if (snowAbove !== undefined && y > snowAbove) c.lerp(snow, Math.min(1, (y - snowAbove) / 120));
      }
      colors.push(c.r, c.g, c.b);
      pos.setY(i, y);
      uv.setXY(i, uv.getX(i) * scale, uv.getY(i) * scale);
    }
    geo.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
    geo.computeVertexNormals();
    const mesh = new THREE.Mesh(geo, terrainMat);
    mesh.name = far ? 'terrain-coarse' : 'terrain-fine';
    mesh.receiveShadow = true;
    scene.add(mesh);
  }

  // ---- water ------------------------------------------------------------------------------
  let waterSurface: WaterSurface | null = null;
  if (water !== undefined) {
    const [wh, ws, wl] = pal.water ?? [0.55, 0.35, 0.3];
    waterSurface = buildWater(
      water,
      9000,
      daylight.sunDir,
      {
        deep: new THREE.Color().setHSL(wh, ws, wl * 0.9),
        shallow: new THREE.Color().setHSL(wh - 0.03, ws * 0.9, wl * 1.5),
        horizon: fogColor.clone(),
        zenith: new THREE.Color().setHSL(0.6, 0.5, 0.5),
      },
      fog,
    );
    waterSurface.mesh.name = 'water';
    scene.add(waterSurface.mesh);
  }

  // ---- structures -------------------------------------------------------------------------
  const mats = structureMaterials();
  if (opts.structures?.length) {
    const group = new THREE.Group();
    group.name = 'structures';
    for (const st of opts.structures) {
      const facing = st.s < laneLength / 2 ? 1 : -1;
      if (st.kind === 'bridge') addBridge(group, st, terrain, mats, facing);
      else addTower(group, st, terrain, mats, facing);
    }
    scene.add(group);
  }

  if (opts.bench) {
    // Raised firing bench the shooter lies on.
    const bench = new THREE.Mesh(new THREE.BoxGeometry(6, opts.bench, 4), new THREE.MeshStandardMaterial({ map: woodTexture(), roughness: 0.9 }));
    bench.position.set(0, opts.bench / 2, 1.2);
    bench.castShadow = bench.receiveShadow = true;
    scene.add(bench);
  }

  if (targets.length) {
    // Earth berm behind the steel.
    const bermMat = new THREE.MeshStandardMaterial({ color: '#8b7350', roughness: 1, flatShading: true });
    const berm = new THREE.Mesh(new THREE.BoxGeometry(260, 14, 30), bermMat);
    berm.position.set(0, 7, -laneLength - 140);
    berm.receiveShadow = true;
    scene.add(berm);
  }

  // ---- cover: the boxes the hit test uses, drawn as boulders or the built things they are ----
  const rockTex = concreteTexture(seed + 3);
  rockTex.repeat.set(2, 2);
  const rockMat = new THREE.MeshStandardMaterial({ color: pal.rocks ?? '#8e8a80', map: rockTex, roughness: 0.95, flatShading: true });
  const builtMats: Record<string, THREE.Material> = {
    concrete: mats.concrete,
    metal: new THREE.MeshStandardMaterial({ map: steelTexture(), color: '#8a949c', roughness: 0.5, metalness: 0.6 }),
    wood: new THREE.MeshStandardMaterial({ map: woodTexture(), roughness: 0.85 }),
  };
  for (const box of opts.cover ?? []) {
    const w = box.max.x - box.min.x;
    const h = box.max.y - box.min.y;
    const d = box.max.z - box.min.z;
    let mesh: THREE.Mesh;
    if (box.look && box.look !== 'rock') {
      mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), builtMats[box.look]);
    } else {
      const geo = new THREE.BoxGeometry(w, h, d, 3, 2, 3);
      const p = geo.attributes.position!;
      for (let i = 0; i < p.count; i++) {
        // Roughen the top and sides a little, never beyond the box.
        const k = 0.88 + rand() * 0.12;
        p.setXYZ(i, p.getX(i) * k, p.getY(i) < h / 2 - 0.01 ? p.getY(i) : p.getY(i) * (0.92 + rand() * 0.08), p.getZ(i) * k);
      }
      geo.computeVertexNormals();
      mesh = new THREE.Mesh(geo, rockMat);
    }
    mesh.position.set((box.min.x + box.max.x) / 2, (box.min.y + box.max.y) / 2, (box.min.z + box.max.z) / 2);
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
  }

  // ---- player figures (duel) --------------------------------------------------------------
  const figures = new Map<string, THREE.Group>();
  const setFigure: RangeView['setFigure'] = (id, f) => {
    const old = figures.get(id);
    if (old) {
      scene.remove(old);
      figures.delete(id);
    }
    if (!f) return;
    const g = buildFigure(f.stance, f.dims);
    g.position.set(f.position.x, f.position.y, f.position.z);
    g.rotation.y = -f.facingYaw;
    scene.add(g);
    figures.set(id, g);
  };

  // ---- steel silhouettes on posts (practice) ----------------------------------------------
  const plates = new Map<string, { pivot: THREE.Group; mat: THREE.MeshStandardMaterial; swing: number; flash: number }>();
  const postMat = new THREE.MeshStandardMaterial({ color: '#3b3b3b', roughness: 0.6, metalness: 0.5 });
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
    const geo = new THREE.ExtrudeGeometry(shape, { depth: 0.012, bevelEnabled: false });
    geo.translate(0, -t.height, 0); // pivot at the top hanger
    const mat = new THREE.MeshStandardMaterial({ color: '#efe9d8', roughness: 0.5, metalness: 0.3, side: THREE.DoubleSide });
    const plate = new THREE.Mesh(geo, mat);
    plate.castShadow = true;
    const pivot = new THREE.Group();
    pivot.position.set(t.position.x, t.position.y + t.height, t.position.z);
    pivot.rotation.y = t.facingYaw - Math.PI;
    pivot.add(plate);
    scene.add(pivot);
    const frame = new THREE.Mesh(new THREE.BoxGeometry(t.width + 0.3, 0.05, 0.05), postMat);
    frame.position.set(t.position.x, t.position.y + t.height + 0.05, t.position.z - 0.05);
    scene.add(frame);
    for (const s of [-1, 1]) {
      const leg = new THREE.Mesh(new THREE.BoxGeometry(0.05, t.height + 0.1, 0.05), postMat);
      leg.position.set(t.position.x + s * (t.width / 2 + 0.15), t.position.y + (t.height + 0.1) / 2, t.position.z - 0.05);
      leg.castShadow = true;
      scene.add(leg);
    }
    const sign = new THREE.Mesh(new THREE.PlaneGeometry(1.2, 0.6), new THREE.MeshBasicMaterial({ map: signTexture(`${Math.round(-t.position.z)}`) }));
    sign.position.set(t.position.x + 1.6, 0.5, t.position.z);
    scene.add(sign);
    plates.set(t.id, { pivot, mat, swing: 0, flash: 0 });
  }

  // ---- vegetation --------------------------------------------------------------------------
  const onLand = (x: number, z: number): boolean => water === undefined || terrain(x, z) > water + 1.2;
  const onPlatform = (x: number, z: number): boolean => Math.abs(ground(x, z) - terrain(x, z)) > 0.01;

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
  const grassMat = new THREE.MeshStandardMaterial({ color: pal.grass ?? '#9aa45a', roughness: 1, side: THREE.DoubleSide });
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
  const up = new THREE.Vector3(0, 1, 0);
  let placed = 0;
  for (let tries = 0; placed < GRASS_COUNT && tries < GRASS_COUNT * 4; tries++) {
    // Tall grass in bands either side of the lane, where you read the wind; short mown grass in it.
    const z = -(15 + rand() * (laneLength + 100));
    const inLane = rand() < 0.3;
    const side = rand() < 0.5 ? -1 : 1;
    const x = inLane ? (rand() * 2 - 1) * (halfWidth - 40) : side * (halfWidth - 38 + rand() * 120);
    if (!onLand(x, z) || onPlatform(x, z)) continue;
    q.setFromAxisAngle(up, rand() * Math.PI);
    const s = inLane ? 0.15 + rand() * 0.2 : 0.8 + rand() * 0.9;
    m4.compose(new THREE.Vector3(x, terrain(x, z), z), q, new THREE.Vector3(s, s, s));
    grass.setMatrixAt(placed++, m4);
  }
  grass.count = placed;
  grass.name = 'grass';
  scene.add(grass);

  // Trees (or shrubs) on the land around the lane: three stacked cones on a trunk, instanced.
  const vegetation = pal.vegetation ?? 'pine';
  if (vegetation !== 'none') {
    const pine = vegetation === 'pine';
    const foliageGeo = pine
      ? (() => {
          const g = new THREE.ConeGeometry(2.2, 5, 7);
          const g2 = new THREE.ConeGeometry(1.7, 4.2, 7);
          const g3 = new THREE.ConeGeometry(1.1, 3.4, 7);
          g.translate(0, 4.5, 0);
          g2.translate(0, 7.2, 0);
          g3.translate(0, 9.6, 0);
          const merged = new THREE.BufferGeometry();
          const parts = [g, g2, g3];
          const positions: number[] = [];
          const normals: number[] = [];
          for (const p of parts) {
            const pg = p.toNonIndexed();
            positions.push(...(pg.attributes.position!.array as Float32Array));
            normals.push(...(pg.attributes.normal!.array as Float32Array));
          }
          merged.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
          merged.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
          return merged;
        })()
      : new THREE.IcosahedronGeometry(1.4, 1).translate(0, 1.1, 0);
    const foliageMat = new THREE.MeshStandardMaterial({ color: pine ? '#2f5a34' : '#5f6b3a', roughness: 1, flatShading: true });
    const trunkGeo = new THREE.CylinderGeometry(0.25, 0.4, 4, 6).translate(0, 2, 0);
    const trunkMat = new THREE.MeshStandardMaterial({ color: '#4a3a2a', roughness: 1 });
    const foliage = new THREE.InstancedMesh(foliageGeo, foliageMat, TREE_COUNT);
    const trunks = new THREE.InstancedMesh(trunkGeo, trunkMat, TREE_COUNT);
    let n = 0;
    for (let tries = 0; n < TREE_COUNT && tries < TREE_COUNT * 6; tries++) {
      const r = 60 + rand() * 2100;
      const a = rand() * Math.PI * 2;
      const x = Math.sin(a) * r;
      const z = centre.z + Math.cos(a) * r;
      const inLane = Math.abs(x) < halfWidth + 12 && z < 30 && z > -laneLength - 40;
      if (inLane || !onLand(x, z) || onPlatform(x, z)) continue;
      const y = terrain(x, z) + farRelief(x, z);
      if (snowAbove !== undefined && y > snowAbove - 40) continue;
      q.setFromAxisAngle(up, rand() * Math.PI * 2);
      const s = pine ? 0.7 + rand() * 1.1 : 0.6 + rand() * 0.9;
      m4.compose(new THREE.Vector3(x, y, z), q, new THREE.Vector3(s, s * (0.9 + rand() * 0.3), s));
      foliage.setMatrixAt(n, m4);
      trunks.setMatrixAt(n, m4);
      n++;
    }
    foliage.count = trunks.count = n;
    foliage.name = 'trees';
    trunks.name = 'trunks';
    foliage.castShadow = trunks.castShadow = true;
    scene.add(foliage, trunks);
  }

  // ---- wind flags beside the lane --------------------------------------------------------
  const flags: Array<{ group: THREE.Group; cloth: THREE.Mesh; pos: Vec3 }> = [];
  const poleMat = new THREE.MeshStandardMaterial({ color: '#d8d8d8', roughness: 0.4, metalness: 0.6 });
  const clothMat = new THREE.MeshStandardMaterial({ color: '#e0412b', roughness: 0.8, side: THREE.DoubleSide });
  for (const [x, z] of opts.flags ?? []) {
    const gy = ground(x, z);
    const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.04, 0.05, 6), poleMat);
    pole.position.set(x, gy + 3, z);
    pole.castShadow = true;
    scene.add(pole);
    if (water !== undefined && terrain(x, z) < water) {
      // A flag out on the water stands on a buoy.
      const buoy = new THREE.Mesh(new THREE.CylinderGeometry(1.2, 0.8, 1.6, 10), new THREE.MeshStandardMaterial({ color: '#d9a21b', roughness: 0.6 }));
      buoy.position.set(x, water + 0.4, z);
      scene.add(buoy);
      pole.position.y = water + 3.5;
    }
    const group = new THREE.Group();
    group.position.set(x, pole.position.y + 2.6, z);
    const clothGeo = new THREE.PlaneGeometry(1.6, 0.9, 8, 1);
    clothGeo.translate(0.8, -0.45, 0);
    const cloth = new THREE.Mesh(clothGeo, clothMat);
    group.add(cloth);
    scene.add(group);
    flags.push({ group, cloth, pos: { x, y: group.position.y, z } });
  }

  // ---- clouds ------------------------------------------------------------------------------
  const cloudTex = cloudTexture(seed);
  const clouds: THREE.Sprite[] = [];
  for (let i = 0; i < 16; i++) {
    const mat = new THREE.SpriteMaterial({ map: cloudTex, transparent: true, depthWrite: false, opacity: 0.85, fog: true });
    const sp = new THREE.Sprite(mat);
    const r = 900 + rand() * 2600;
    const a = rand() * Math.PI * 2;
    sp.position.set(Math.sin(a) * r, 520 + rand() * 420, centre.z + Math.cos(a) * r);
    const w = 380 + rand() * 520;
    sp.scale.set(w, w * (0.42 + rand() * 0.2), 1);
    scene.add(sp);
    clouds.push(sp);
  }

  // ---- dust, splashes and tracers ---------------------------------------------------------
  const dotTex = softDotTexture();
  const puffs: Array<{ sprite: THREE.Sprite; age: number; life: number; vel: THREE.Vector3; size: number; gravity: number }> = [];
  const dust = (point: Vec3, size = 1): void => {
    const splash = water !== undefined && point.y <= water + 0.3 && terrain(point.x, point.z) < water;
    const n = splash ? 14 : 9;
    for (let i = 0; i < n; i++) {
      const mat = new THREE.SpriteMaterial({ map: dotTex, color: splash ? '#eaf4ff' : '#b59a6c', transparent: true, depthWrite: false });
      const sprite = new THREE.Sprite(mat);
      sprite.position.set(point.x + (rand() - 0.5) * 0.3, point.y + 0.1 + rand() * 0.2, point.z + (rand() - 0.5) * 0.3);
      scene.add(sprite);
      puffs.push({
        sprite,
        age: 0,
        life: splash ? 1.1 + rand() * 0.6 : 1.6 + rand() * 1.4,
        vel: splash
          ? new THREE.Vector3((rand() - 0.5) * 2.5, 5 + rand() * 6, (rand() - 0.5) * 2.5)
          : new THREE.Vector3((rand() - 0.5) * 1.2, 1.2 + rand() * 2.2, (rand() - 0.5) * 1.2),
        size: size * (splash ? 0.35 + rand() * 0.5 : 0.5 + rand() * 0.6),
        gravity: splash ? 9.8 : 0,
      });
    }
  };

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

  // ---- per frame ----------------------------------------------------------------------------
  const flare = scene.getObjectsByProperty('name', 'flare') as THREE.Mesh[];
  const update = (time: number, dt: number, windAt: (p: Vec3) => Vec3): void => {
    clock = time;
    // The sky dome (if drawn live) and the shadow box follow the camera; the sun stays on its bearing.
    if (daylight.sky.parent) daylight.sky.position.copy(camera.position);
    sun.target.position.copy(camera.position);
    sun.position.copy(camera.position).addScaledVector(daylight.sunDir, 400);
    sun.target.updateMatrixWorld();

    grassUniforms.uTime.value = time;
    for (let i = 0; i < 3; i++) {
      const lane = i === 0 ? 50 : i === 1 ? (laneEdges[0] ?? 300) + 10 : (laneEdges[1] ?? 600) + 10;
      const w = windAt({ x: 0, y: 0, z: -lane });
      grassUniforms.uWind.value[i]!.set(w.x, w.z);
    }
    const midWind = windAt({ x: 0, y: 0, z: centre.z });
    waterSurface?.update(time, midWind, camera.position);
    for (const sp of clouds) {
      sp.position.x += midWind.x * dt * 0.6;
      sp.position.z += midWind.z * dt * 0.6;
      const dx = sp.position.x;
      const dz = sp.position.z - centre.z;
      if (Math.hypot(dx, dz) > 3800) {
        sp.position.x = -dx * 0.9;
        sp.position.z = centre.z - dz * 0.9;
      }
    }
    for (const f of flare) f.scale.setScalar(0.85 + 0.3 * Math.sin(time * 17) * Math.sin(time * 5.3));

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
      puff.vel.y -= puff.gravity * dt;
      puff.sprite.position.x += (puff.vel.x + w.x * 0.8) * dt;
      puff.sprite.position.y += puff.vel.y * dt;
      puff.sprite.position.z += (puff.vel.z + w.z * 0.8) * dt;
      const k = puff.age / puff.life;
      puff.sprite.scale.setScalar(puff.size * (0.6 + k * 2.2));
      (puff.sprite.material as THREE.SpriteMaterial).opacity = 0.8 * (1 - k);
      if (k >= 1 || (puff.gravity > 0 && water !== undefined && puff.sprite.position.y < water - 0.5)) {
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
      tr.sprite.scale.setScalar(0.08 + tr.sprite.position.distanceTo(camera.position) * 0.0012);
    }
  };

  return { scene, camera, update, dust, ringPlate, tracer, setFigure };
}
