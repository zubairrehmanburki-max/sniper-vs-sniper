/**
 * Killcam: a slow-motion follow camera along the authoritative trajectory, with the drop and wind
 * drift read out as the bullet flies. Shown to both players on every hit, so each death teaches.
 */
import * as THREE from 'three';
import { relativeToLos, type Trajectory, type Vec3 } from '@svs/ballistics';
import { config, shotToWorld, type Impact, type Stance } from '@svs/shared';
import { $, type Shooter } from './shooter.ts';

export interface KillcamShot {
  trajectory: Trajectory;
  eye: Vec3;
  yaw: number;
  impact: Impact;
  /** Where the target stood, and how it was standing. */
  target: { position: Vec3; stance: Stance; facingYaw: number };
  title: string;
  lethal: boolean;
}

/** Seconds of screen time for the flight, and the hold on the target afterwards. */
const FLIGHT_SECONDS = 3.2;
const HOLD_SECONDS = 1.6;

/** Screen progress (0–1) → flight progress: brisk off the muzzle, lingering on the arrival. */
function ease(u: number): number {
  return 1 - (1 - u) ** 2.2 * (1 - 0.35 * u);
}

export function playKillcam(shooter: Shooter, shot: KillcamShot): Promise<void> {
  const view = shooter.view!;
  const pts = shot.trajectory.points.filter((p) => p.t <= shot.impact.t);
  if (pts.length < 2) return Promise.resolve();
  const world = pts.map((p) => shotToWorld(p, shot.eye, shot.yaw));
  world.push(shot.impact.point);
  const times = pts.map((p) => p.t);
  times.push(shot.impact.t);

  const wasScoped = shooter.scoped;
  shooter.setScoped(false);
  document.body.classList.add('killcam-on');
  const panel = $('killcam');
  panel.hidden = false;
  $('killcam-title').textContent = shot.title;

  // The bullet, its trail, and the target figure for the replay.
  const group = new THREE.Group();
  const bullet = new THREE.Mesh(new THREE.SphereGeometry(0.03, 10, 8), new THREE.MeshBasicMaterial({ color: '#ffd27a' }));
  group.add(bullet);
  const trailGeo = new THREE.BufferGeometry();
  const trailPos = new Float32Array(world.length * 3);
  trailGeo.setAttribute('position', new THREE.BufferAttribute(trailPos, 3));
  const trail = new THREE.Line(trailGeo, new THREE.LineBasicMaterial({ color: '#ffb547' }));
  trail.frustumCulled = false;
  group.add(trail);
  // Where it struck, shown once the bullet arrives.
  const hit = new THREE.Mesh(new THREE.SphereGeometry(0.07, 12, 10), new THREE.MeshBasicMaterial({ color: shot.lethal ? '#ff3b2e' : '#ffb547' }));
  hit.position.set(shot.impact.point.x, shot.impact.point.y, shot.impact.point.z);
  hit.visible = false;
  group.add(hit);
  view.scene.add(group);
  view.setFigure('killcam', {
    position: shot.target.position,
    stance: shot.target.stance,
    facingYaw: shot.target.facingYaw,
    dims: config.player.silhouette[shot.target.stance],
  });

  const at = (t: number): { p: Vec3; i: number; f: number } => {
    let i = 0;
    while (i < times.length - 2 && times[i + 1]! < t) i++;
    const f = Math.min(1, Math.max(0, (t - times[i]!) / (times[i + 1]! - times[i]!)));
    const a = world[i]!;
    const b = world[i + 1]!;
    return { p: { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f, z: a.z + (b.z - a.z) * f }, i, f };
  };

  let clock = 0;
  return new Promise((resolve) => {
    const finish = (): void => {
      shooter.director = null;
      view.scene.remove(group);
      trailGeo.dispose();
      view.setFigure('killcam', null);
      panel.hidden = true;
      document.body.classList.remove('killcam-on');
      shooter.setScoped(wasScoped);
      resolve();
    };
    $('killcam-skip').onclick = finish;
    shooter.director = (cam, dt) => {
      clock += dt;
      const u = Math.min(1, clock / FLIGHT_SECONDS);
      const t = ease(u) * shot.impact.t;
      const { p, i } = at(t);
      bullet.position.set(p.x, p.y, p.z);
      hit.visible = u >= 1;
      bullet.visible = u < 1;
      for (let k = 0; k <= i; k++) trailPos.set([world[k]!.x, world[k]!.y, world[k]!.z], k * 3);
      trailPos.set([p.x, p.y, p.z], (i + 1) * 3);
      trailGeo.setDrawRange(0, i + 2);
      trailGeo.attributes.position!.needsUpdate = true;

      // Chase from behind and a little above and to the right; swing round to face the target at the end.
      const ahead = at(Math.min(shot.impact.t, t + 0.02)).p;
      const dir = new THREE.Vector3(ahead.x - p.x, ahead.y - p.y, ahead.z - p.z);
      if (dir.lengthSq() < 1e-9) dir.set(Math.sin(shot.yaw), 0, -Math.cos(shot.yaw));
      dir.normalize();
      const right = new THREE.Vector3().crossVectors(dir, new THREE.Vector3(0, 1, 0)).normalize();
      const hold = Math.max(0, clock - FLIGHT_SECONDS) / HOLD_SECONDS;
      const swing = Math.min(1, hold * 1.5);
      const back = 2.2 + 1.5 * swing;
      cam.position.set(
        p.x - dir.x * back + right.x * (0.5 + 2.5 * swing),
        p.y - dir.y * back + 0.35 + 0.6 * swing,
        p.z - dir.z * back + right.z * (0.5 + 2.5 * swing),
      );
      cam.lookAt(p.x + dir.x * 4 * (1 - swing), p.y + dir.y * 4 * (1 - swing), p.z + dir.z * 4 * (1 - swing));
      if (cam.fov !== 50) {
        cam.fov = 50;
        cam.updateProjectionMatrix();
      }

      const sample = pts[Math.min(i, pts.length - 1)]!;
      const rel = relativeToLos(sample, shot.trajectory.losElevation);
      const range = Math.max(1, rel.along);
      $('killcam-stats').innerHTML =
        `<span><b>${Math.round(rel.along)} m</b> range</span>` +
        `<span><b>${t.toFixed(2)} s</b> flight</span>` +
        `<span><b>${Math.round(sample.speed)} m/s</b> Mach ${sample.mach.toFixed(2)}</span>` +
        `<span><b>${Math.abs(rel.height).toFixed(2)} m ${rel.height >= 0 ? 'above' : 'below'}</b> line of sight (${((Math.abs(rel.height) / range) * 1000).toFixed(1)} mil)</span>` +
        `<span><b>${Math.abs(rel.lateral).toFixed(2)} m ${rel.lateral >= 0 ? 'right' : 'left'}</b> of it after wind and spin</span>`;
      if (clock >= FLIGHT_SECONDS + HOLD_SECONDS) finish();
    };
  });
}
