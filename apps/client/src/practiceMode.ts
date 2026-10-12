/** Practice range: steel at 300/600/900 m, every shot flown by the solver, spotter calls on misses. */
import { gustFactor, hashSeed, simulate, solveFiringSolution, type Vec3 } from '@svs/ballistics';
import {
  config,
  createPracticeRange,
  flatGround,
  laneWindAt,
  missCall,
  resolveImpact,
  rifleById,
  rifleShot,
  shotToWorld,
  shotWindField,
  type FireInput,
  type Silhouette,
} from '@svs/shared';
import { spotterCall } from './format.ts';
import { buildWorld } from './scene.ts';
import { haptics } from './haptics.ts';
import { $, type Shooter } from './shooter.ts';

function storage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export class PracticeMode {
  private readonly seed = (Date.now() ^ (Math.random() * 0x7fffffff)) >>> 0;
  private readonly range = createPracticeRange(this.seed);
  private rifleId = 'bolt308';
  private shotIndex = 0;
  private coldBore = true;

  constructor(private readonly shooter: Shooter) {
    const view = buildWorld({
      seed: this.seed,
      ground: flatGround,
      laneLength: 900,
      laneEdges: this.range.wind.edges,
      steel: this.range.targets,
      bench: config.practiceRange.firingPointHeightM,
      flags: [
        [-22, -150],
        [26, -450],
        [-30, -750],
        [20, -880],
      ],
    });
    shooter.attach(view, {
      eye: () => this.eye(),
      windAt: (p) => this.windWithGust(p),
      canFire: () => true,
      fire: (input, time) => this.fire(input, time),
      rangeables: () => ({ targets: this.range.targets, ground: flatGround, cover: [] }),
      canChangeStance: () => true,
    });
    shooter.lookAt({ ...this.range.targets[0]!.position, y: 0.5 });
    this.setupHud();
  }

  private setupHud(): void {
    const select = $<HTMLSelectElement>('rifle');
    select.innerHTML = '';
    for (const r of config.rifles) {
      const o = document.createElement('option');
      o.value = r.id;
      o.textContent = r.name;
      select.append(o);
    }
    select.value = this.rifleId;
    select.hidden = false;
    select.onchange = () => {
      this.rifleId = select.value;
      this.coldBore = true;
      this.loadDope();
      select.blur();
    };
    const a = this.range.atmosphere;
    $('weather').textContent = `${a.temperatureC.toFixed(0)} °C · ${a.pressureHPa.toFixed(0)} hPa · ${(a.humidity * 100).toFixed(0)}% RH · ${a.altitudeM} m`;
    this.loadDope();
    $<HTMLTextAreaElement>('dope').oninput = (e) => {
      try {
        storage()?.setItem(`dope:${this.rifleId}`, (e.target as HTMLTextAreaElement).value);
      } catch {
        /* storage unavailable */
      }
    };
  }

  private loadDope(): void {
    let v = '';
    try {
      v = storage()?.getItem(`dope:${this.rifleId}`) ?? '';
    } catch {
      /* storage unavailable */
    }
    $<HTMLTextAreaElement>('dope').value = v;
  }

  private eye(): Vec3 {
    return { x: 0, y: config.practiceRange.firingPointHeightM + config.sway.eyeHeightM[this.shooter.stance], z: 0 };
  }

  private windWithGust(p: Vec3): Vec3 {
    const w = laneWindAt(this.range.wind, p);
    const g = gustFactor(this.range.wind.gust, this.shooter.time);
    return { x: w.x * g, y: w.y * g, z: w.z * g };
  }

  private fire(input: FireInput, time: number): void {
    const rifle = rifleById(this.rifleId);
    const eye = this.eye();
    const b = config.ballistics;
    const cold = this.coldBore ? rifle.coldBoreShiftMrad : { elevation: 0, windage: 0 };
    this.coldBore = false;
    const shot = {
      ...rifleShot(rifle),
      losElevation: input.pitch,
      azimuth: input.yaw,
      dialElevation: (input.dialElevationMrad + cold.elevation) / 1000,
      dialWindage: (input.dialWindageMrad + cold.windage) / 1000,
    };
    const env = { atmosphere: this.range.atmosphere, wind: shotWindField(this.range.wind, eye, input.yaw), latitude: this.range.latitude };
    const traj = simulate(shot, env, {
      maxRange: 1200,
      dt: b.dtSeconds,
      integrator: b.integrator,
      seed: hashSeed(this.seed, ++this.shotIndex),
      fireTime: time,
      effects: b.effects,
      transonic: { machThreshold: b.transonic.machThreshold, sigma: b.transonic.sigmaRadPerSqrtS },
      maxTime: 4,
    });
    const impact = resolveImpact(traj, eye, input.yaw, this.range.targets, flatGround);
    playShotFeedback(this.shooter, traj, eye, input.yaw, impact, rifle.diameterM);

    const target = this.nearestTarget(input.yaw);
    const d = Math.round(-target.position.z);
    let text: string;
    let hit = false;
    if (impact.kind === 'target') {
      hit = true;
      const t = this.range.targets.find((x) => x.id === impact.targetId)!;
      text = `HIT ${Math.round(-t.position.z)} m · ${impact.zone} · ${impact.t.toFixed(2)} s · ${Math.round(impact.energy)} J`;
    } else {
      const callout = missCall(traj, eye, input.yaw, target);
      text = callout ? `Miss at ${d} m: ${spotterCall(callout.upMrad, callout.rightMrad)}` : `Miss: fell short of ${d} m`;
    }
    const n = this.shotIndex;
    window.setTimeout(() => this.shooter.call(text, hit), impact.t * 1000);
    const li = document.createElement('li');
    li.value = n;
    li.textContent = text;
    if (hit) li.className = 'hit';
    const ol = $('log');
    ol.prepend(li);
    while (ol.children.length > 30) ol.lastChild!.remove();
  }

  private nearestTarget(yaw: number): Silhouette {
    let best = this.range.targets[0]!;
    let bestAngle = Infinity;
    for (const t of this.range.targets) {
      const a = Math.abs(Math.atan2(t.position.x, -t.position.z) - yaw);
      if (a < bestAngle) {
        bestAngle = a;
        best = t;
      }
    }
    return best;
  }

  /**
   * Playtest/automation helper (exposed with ?debug): aims at a plate's chest and dials the
   * calculator's solution for the current wind, rounded to turret clicks, like a perfect spotter.
   */
  debugSolve(targetIndex: number): { elevMrad: number; windMrad: number } {
    const s = this.shooter;
    const t = this.range.targets[targetIndex]!;
    const eye = this.eye();
    const chestY = t.position.y + t.height - t.head - 0.175;
    const horizontal = Math.hypot(t.position.x - eye.x, t.position.z - eye.z);
    s.yaw = Math.atan2(t.position.x - eye.x, -(t.position.z - eye.z));
    s.pitch = Math.atan2(chestY - eye.y, horizontal);
    const shot = { ...rifleShot(rifleById(this.rifleId)), losElevation: s.pitch };
    const env = { atmosphere: this.range.atmosphere, wind: shotWindField(this.range.wind, eye, s.yaw) };
    const sol = solveFiringSolution(shot, env, Math.hypot(horizontal, chestY - eye.y), {
      fireTime: s.time,
      effects: { spinDrift: config.ballistics.effects.spinDrift },
    });
    const click = config.scope.clickMrad;
    s.elevMrad = Math.round((sol.elevation * 1000) / click) * click;
    s.windMrad = Math.round((sol.windage * 1000) / click) * click;
    s.renderTurrets();
    return { elevMrad: s.elevMrad, windMrad: s.windMrad };
  }
}

/** Report, tracer, then impact effects and sound when the bullet actually arrives. */
export function playShotFeedback(
  shooter: Shooter,
  traj: import('@svs/ballistics').Trajectory,
  eye: Vec3,
  yaw: number,
  impact: import('@svs/shared').Impact,
  caliberM: number,
): void {
  shooter.audio.shot(caliberM > 0.0085 ? 1.3 : 1);
  const stride = Math.max(1, Math.floor(traj.points.length / 300));
  const path = traj.points
    .filter((_, i) => i % stride === 0)
    .filter((p) => p.t <= impact.t)
    .map((p) => ({ t: p.t, p: shotToWorld(p, eye, yaw) }));
  path.push({ t: impact.t, p: impact.point });
  shooter.view!.tracer(path, impact.t);
  const distance = Math.hypot(impact.point.x - eye.x, impact.point.z - eye.z);
  const sound = config.practiceRange.speedOfSoundForAudioMps;
  window.setTimeout(() => {
    if (impact.kind === 'target') {
      shooter.view!.ringPlate(impact.targetId!);
      shooter.audio.ping(distance / sound, distance);
      haptics.hit(distance / sound);
    } else if (impact.kind === 'ground' || impact.kind === 'cover') {
      shooter.view!.dust(impact.point, caliberM > 0.01 ? 1.8 : 1);
      shooter.audio.thud(distance / sound, distance);
    }
  }, impact.t * 1000);
}
