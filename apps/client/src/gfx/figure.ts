/**
 * The sniper figure: a rigged, skinned soldier model posed per stance so the head sits where the
 * hit silhouette puts it (the hit test never touches the model). Poses are bone rotations over
 * the model's bind pose, with a slow breath on the chest.
 */
import * as THREE from 'three';
import { clone as cloneSkeleton } from 'three/examples/jsm/utils/SkeletonUtils.js';
import type { Stance } from '@svs/shared';
import { assets } from './assets.ts';

export interface FigureDims {
  heightM: number;
  widthM: number;
  headM: number;
}

export interface Figure {
  group: THREE.Group;
  update(dt: number): void;
}

const MODEL_HEIGHT = 1.83;

const gunMat = new THREE.MeshStandardMaterial({ color: '#1b1d1f', roughness: 0.45, metalness: 0.7 });
const stockMat = new THREE.MeshStandardMaterial({ color: '#3a3326', roughness: 0.7, metalness: 0.1 });
const glassMat = new THREE.MeshStandardMaterial({ color: '#0d1a26', roughness: 0.1, metalness: 0.8 });

function part(geo: THREE.BufferGeometry, mat: THREE.Material, x: number, y: number, z: number, rx = 0, ry = 0, rz = 0): THREE.Mesh {
  const m = new THREE.Mesh(geo, mat);
  m.position.set(x, y, z);
  m.rotation.set(rx, ry, rz);
  m.castShadow = true;
  m.receiveShadow = true;
  return m;
}

/** Bolt-action rifle with scope and bipod, barrel along −z, origin at the grip. */
export function rifle(bipod: boolean): THREE.Group {
  const g = new THREE.Group();
  g.add(part(new THREE.BoxGeometry(0.05, 0.08, 0.5), stockMat, 0, -0.02, 0.1)); // stock
  g.add(part(new THREE.BoxGeometry(0.055, 0.07, 0.4), gunMat, 0, 0.01, -0.25)); // receiver + forend
  g.add(part(new THREE.CylinderGeometry(0.013, 0.013, 0.62, 10), gunMat, 0, 0.025, -0.72, Math.PI / 2));
  g.add(part(new THREE.CylinderGeometry(0.03, 0.03, 0.08, 10), gunMat, 0, 0.025, -1.0, Math.PI / 2)); // brake
  g.add(part(new THREE.CylinderGeometry(0.024, 0.024, 0.3, 12), gunMat, 0, 0.085, -0.2, Math.PI / 2)); // scope tube
  g.add(part(new THREE.CylinderGeometry(0.03, 0.03, 0.08, 12), gunMat, 0, 0.085, -0.32, Math.PI / 2)); // objective bell
  g.add(part(new THREE.CylinderGeometry(0.026, 0.026, 0.01, 12), glassMat, 0, 0.085, -0.365, Math.PI / 2));
  g.add(part(new THREE.BoxGeometry(0.035, 0.09, 0.05), gunMat, 0, -0.07, -0.12)); // magazine
  g.add(part(new THREE.CylinderGeometry(0.008, 0.008, 0.06, 6), gunMat, 0.04, 0.03, 0.02, 0, 0, Math.PI / 2)); // bolt handle
  if (bipod) {
    for (const s of [-1, 1]) g.add(part(new THREE.CylinderGeometry(0.007, 0.007, 0.26, 6), gunMat, s * 0.07, -0.12, -0.5, 0, 0, s * 0.45));
  }
  return g;
}

interface Pose {
  /** Bone rotations in degrees, applied after the idle frame. */
  bones: Record<string, [number, number, number]>;
  /** Hips offset in the model's cm frame. */
  hips?: [number, number, number];
  /** Rotation of the whole model, radians about x (prone lies down). */
  lie?: number;
  /** Offset of the whole model from the figure origin, metres. */
  shift?: [number, number, number];
  /** Where the rifle goes, in figure metres, and whether it stands on its bipod. */
  rifleAt: [number, number, number];
  rifleTilt?: number;
  bipod: boolean;
}

const POSES: Record<Stance, Pose> = {
  standing: {
    bones: {
      RightArm: [85, 0, 35],
      RightForeArm: [0, 0, -55],
      LeftArm: [95, 0, 40],
      LeftForeArm: [0, 0, -30],
      Spine1: [4, 0, 0],
      Head: [-4, 0, 0],
    },
    rifleAt: [0.1, 1.35, -0.3],
    bipod: false,
  },
  crouch: {
    bones: {
      RightUpLeg: [95, 0, 8],
      LeftUpLeg: [95, 0, -8],
      RightLeg: [-100, 0, 0],
      LeftLeg: [-100, 0, 0],
      Spine: [20, 0, 0],
      Spine1: [8, 0, 0],
      Head: [-22, 0, 0],
      RightArm: [85, 0, 35],
      RightForeArm: [0, 0, -55],
      LeftArm: [95, 0, 40],
      LeftForeArm: [0, 0, -30],
    },
    hips: [0, 0, -58],
    shift: [0, 0, -0.25],
    rifleAt: [0.1, 0.8, -0.5],
    bipod: false,
  },
  prone: {
    bones: {
      RightArm: [80, 0, 60],
      RightForeArm: [55, 0, 0],
      LeftArm: [95, 0, 60],
      LeftForeArm: [30, 0, 0],
      RightUpLeg: [0, 0, 10],
      LeftUpLeg: [0, 0, -10],
      Spine: [-10, 0, 0],
      Spine1: [-10, 0, 0],
      Spine2: [-10, 0, 0],
      Neck: [-25, 0, 0],
      Head: [-30, 0, 0],
    },
    lie: -Math.PI / 2,
    rifleAt: [0.03, 0.27, -0.25],
    bipod: true,
  },
};

const bodyMat = new THREE.MeshStandardMaterial({ color: '#5a6049', roughness: 0.85, metalness: 0.05 });
const visorMat = new THREE.MeshStandardMaterial({ color: '#2a2f2a', roughness: 0.35, metalness: 0.4 });

/**
 * Build a figure facing −z; the caller rotates it to the facing yaw. The group origin is on the
 * ground under the figure's head for prone, under its feet otherwise.
 */
export function buildFigure(stance: Stance, dims: FigureDims, override?: Partial<Pose> & { raw?: boolean }): Figure {
  const src = assets().soldier;
  const model = cloneSkeleton(src.scene) as THREE.Group;
  const bones = new Map<string, THREE.Bone>();
  model.traverse((o) => {
    if ((o as THREE.Bone).isBone) bones.set(o.name.replace(/^mixamorig/, ''), o as THREE.Bone);
    const mesh = o as THREE.SkinnedMesh;
    if (mesh.isSkinnedMesh) {
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      mesh.frustumCulled = false;
      const m = mesh.material as THREE.MeshStandardMaterial;
      const tint = o.name.includes('visor') ? visorMat : bodyMat;
      const mat = tint.clone();
      mat.map = m.map;
      mat.normalMap = m.normalMap;
      mesh.material = mat;
    }
  });
  const pose: Pose = { ...POSES[stance], ...override, bones: override?.raw ? (override.bones ?? {}) : { ...POSES[stance].bones, ...override?.bones } };
  const rest = new Map<string, THREE.Quaternion>();
  for (const [name, b] of bones) rest.set(name, b.quaternion.clone());
  const hipsBone = bones.get('Hips');
  const hipsRest = hipsBone?.position.clone() ?? new THREE.Vector3();

  const applyPose = (): void => {
    for (const [name, deg] of Object.entries(pose.bones)) {
      const b = bones.get(name);
      if (!b) continue;
      const e = new THREE.Euler(THREE.MathUtils.degToRad(deg[0]), THREE.MathUtils.degToRad(deg[1]), THREE.MathUtils.degToRad(deg[2]));
      b.quaternion.copy(rest.get(name)!).multiply(new THREE.Quaternion().setFromEuler(e));
    }
  };

  const scale = dims.heightM / MODEL_HEIGHT;
  const group = new THREE.Group();
  const body = new THREE.Group();
  body.add(model);
  body.scale.setScalar(stance === 'standing' ? scale : 1.75 / MODEL_HEIGHT);
  if (pose.lie !== undefined) {
    // Lie down: rotate so the model's up (+y) points along +z (feet trailing behind the head).
    body.rotation.x = pose.lie;
    // Head at the origin: the model's head is ~1.7 m up its body, which now runs along +z.
    body.position.set(0, 0.22, 1.5);
  }
  if (pose.shift) body.position.add(new THREE.Vector3(...pose.shift));
  group.add(body);
  const r = rifle(pose.bipod);
  r.position.set(...pose.rifleAt);
  r.rotation.x = pose.rifleTilt ?? 0;
  group.add(r);

  // Keep the idle breathing for upright figures; the pose is re-applied over it every frame.
  let clock = 0;
  const update = (dt: number): void => {
    clock += dt;
    if (hipsBone) {
      hipsBone.position.copy(hipsRest);
      if (pose.hips) hipsBone.position.add(new THREE.Vector3(...pose.hips));
    }
    applyPose();
    // Breathing: the chest rises a touch, slowly.
    const breath = Math.sin(clock * 1.4) * THREE.MathUtils.degToRad(1.2);
    const spine2 = bones.get('Spine2');
    if (spine2) spine2.quaternion.multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), breath));
  };
  update(0);
  return { group, update };
}
