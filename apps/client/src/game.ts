/** Practice range controller: input, scope, firing through the real solver, feedback. */
import * as THREE from 'three';
import { hashSeed, simulate, gustFactor, solveFiringSolution, type Vec3 } from '@svs/ballistics';
import {
  config,
  createPracticeRange,
  flatGround,
  holdBreath,
  initialBreath,
  laneWindAt,
  missCall,
  rangefind,
  releaseBreath,
  resolveImpact,
  rifleById,
  rifleShot,
  shotToWorld,
  shotWindField,
  swayOffset,
  type BreathState,
  type Silhouette,
  type Stance,
} from '@svs/shared';
import { RangeAudio } from './audio.ts';
import { clickTurret, formatMil, pxPerMrad, scopeFov, spotterCall, windClock } from './format.ts';
import { drawReticle } from './reticle.ts';
import { buildRange } from './scene.ts';

const STANCES: Stance[] = ['prone', 'crouch', 'standing'];
const CYCLE_SECONDS = 1.4; // bolt cycle before the next shot

const $ = <T extends HTMLElement>(id: string): T => document.getElementById(id) as T;

function storage(): Storage | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export class PracticeGame {
  private readonly seed = (Date.now() ^ (Math.random() * 0x7fffffff)) >>> 0;
  private readonly range = createPracticeRange(this.seed);
  private readonly view = buildRange(
    this.range.targets,
    this.range.wind.edges,
    this.seed,
    config.practiceRange.firingPointHeightM,
  );
  private readonly renderer: THREE.WebGLRenderer;
  private readonly audio = new RangeAudio();
  private readonly reticleCtx: CanvasRenderingContext2D;

  private yaw = 0;
  private pitch = 0;
  private scoped = false;
  private zoom = 10;
  private elevMrad = 0;
  private windMrad = 0;
  private stance: Stance = 'prone';
  private bipod = true;
  private breath: BreathState = initialBreath();
  private rifleId = 'bolt308';
  private shotIndex = 0;
  private coldBore = true;
  private lastShotAt = -10;
  private recoil = 0;
  private time = 0;
  private lastFrame = performance.now();
  private started = false;
  private callTimer = 0;
  private dragging: { id: number; x: number; y: number } | null = null;

  constructor() {
    const canvas = $<HTMLCanvasElement>('view');
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(2, window.devicePixelRatio));
    this.reticleCtx = $<HTMLCanvasElement>('reticle').getContext('2d')!;
    // Start looking at the 300 m plate.
    const first = this.range.targets[0]!;
    this.yaw = Math.atan2(first.position.x, -first.position.z);
    this.pitch = Math.atan2(0.5 - this.eyeHeight(), -first.position.z);
    this.buildHud();
    this.bindInput();
    window.addEventListener('resize', () => this.resize());
    this.resize();
    requestAnimationFrame((t) => this.frame(t));
  }

  // ---- setup -------------------------------------------------------------

  private buildHud(): void {
    const select = $<HTMLSelectElement>('rifle');
    for (const r of config.rifles) {
      const o = document.createElement('option');
      o.value = r.id;
      o.textContent = r.name;
      select.append(o);
    }
    select.value = this.rifleId;
    select.addEventListener('change', () => {
      this.rifleId = select.value;
      this.coldBore = true;
      this.loadDope();
      select.blur();
    });
    const a = this.range.atmosphere;
    $('weather').textContent = `${a.temperatureC.toFixed(0)} °C · ${a.pressureHPa.toFixed(0)} hPa · ${(a.humidity * 100).toFixed(0)}% RH · ${a.altitudeM} m`;
    const zoom = $<HTMLInputElement>('zoom');
    zoom.min = String(config.scope.minZoom);
    zoom.max = String(config.scope.maxZoom);
    zoom.addEventListener('input', () => this.setZoom(Number(zoom.value)));
    this.loadDope();
    $<HTMLTextAreaElement>('dope').addEventListener('input', (e) => {
      storage()?.setItem(`dope:${this.rifleId}`, (e.target as HTMLTextAreaElement).value);
    });
    $('start').addEventListener('click', () => this.start());
    this.renderTurrets();
  }

  private loadDope(): void {
    $<HTMLTextAreaElement>('dope').value = storage()?.getItem(`dope:${this.rifleId}`) ?? '';
  }

  private start(): void {
    this.started = true;
    this.audio.unlock();
    $('intro').classList.add('hidden');
    this.setScoped(true);
  }

  private bindInput(): void {
    const canvas = $<HTMLCanvasElement>('view');
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());

    // Mouse: pointer lock on desktop, drag fallback.
    canvas.addEventListener('mousedown', (e) => {
      if (!this.started) return;
      if (e.button === 2) {
        this.setScoped(!this.scoped);
        return;
      }
      if (document.pointerLockElement === canvas) {
        if (e.button === 0) this.fire();
      } else {
        void canvas.requestPointerLock?.();
      }
    });
    document.addEventListener('mousemove', (e) => {
      if (document.pointerLockElement === canvas) this.look(e.movementX, e.movementY);
    });
    canvas.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        this.setZoom(this.zoom + (e.deltaY < 0 ? 1 : -1));
      },
      { passive: false },
    );

    // Touch: drag anywhere on the view to fine-aim.
    canvas.addEventListener('touchstart', (e) => {
      const t = e.changedTouches[0]!;
      if (!this.dragging) this.dragging = { id: t.identifier, x: t.clientX, y: t.clientY };
    });
    canvas.addEventListener(
      'touchmove',
      (e) => {
        e.preventDefault();
        for (const t of Array.from(e.changedTouches)) {
          if (this.dragging && t.identifier === this.dragging.id) {
            // Touch aim is slower than mouse: thumb precision.
            this.look((t.clientX - this.dragging.x) * 0.45, (t.clientY - this.dragging.y) * 0.45);
            this.dragging.x = t.clientX;
            this.dragging.y = t.clientY;
          }
        }
      },
      { passive: false },
    );
    const endTouch = (e: TouchEvent): void => {
      for (const t of Array.from(e.changedTouches)) if (this.dragging?.id === t.identifier) this.dragging = null;
    };
    canvas.addEventListener('touchend', endTouch);
    canvas.addEventListener('touchcancel', endTouch);

    // HUD buttons.
    document.querySelectorAll<HTMLButtonElement>('button[data-act]').forEach((b) => {
      const act = b.dataset.act!;
      if (act === 'breath') {
        const down = (e: Event): void => {
          e.preventDefault();
          this.setBreath(true);
        };
        const up = (e: Event): void => {
          e.preventDefault();
          this.setBreath(false);
        };
        b.addEventListener('pointerdown', down);
        b.addEventListener('pointerup', up);
        b.addEventListener('pointerleave', up);
        b.addEventListener('pointercancel', up);
        return;
      }
      b.addEventListener('click', (e) => {
        e.preventDefault();
        this.action(act);
        b.blur();
      });
    });

    window.addEventListener('keydown', (e) => {
      if ((e.target as HTMLElement).tagName === 'TEXTAREA') return;
      if (!this.started) {
        if (e.key === 'Enter' || e.key === ' ') this.start();
        return;
      }
      const map: Record<string, string> = {
        w: 'elev-up',
        s: 'elev-down',
        a: 'wind-left',
        d: 'wind-right',
        x: 'zero',
        z: 'scope',
        f: 'laser',
        c: 'stance',
        b: 'bipod',
        ' ': 'fire',
        q: 'zoom-out',
        e: 'zoom-in',
      };
      if (e.key === 'Shift') {
        this.setBreath(true);
        return;
      }
      const act = map[e.key.toLowerCase()];
      if (act) {
        e.preventDefault();
        if (act === 'fire' && e.repeat) return;
        this.action(act);
      }
    });
    window.addEventListener('keyup', (e) => {
      if (e.key === 'Shift') this.setBreath(false);
    });
  }

  private action(act: string): void {
    const s = config.scope;
    switch (act) {
      case 'elev-up':
      case 'elev-down':
        this.elevMrad = clickTurret(this.elevMrad, act === 'elev-up' ? 1 : -1, s.clickMrad, s.maxElevationMrad, -2);
        this.audio.click();
        break;
      case 'wind-left':
      case 'wind-right':
        this.windMrad = clickTurret(this.windMrad, act === 'wind-right' ? 1 : -1, s.clickMrad, s.maxWindageMrad);
        this.audio.click();
        break;
      case 'zero':
        this.elevMrad = 0;
        this.windMrad = 0;
        break;
      case 'scope':
        this.setScoped(!this.scoped);
        break;
      case 'zoom-in':
        this.setZoom(this.zoom + 1);
        break;
      case 'zoom-out':
        this.setZoom(this.zoom - 1);
        break;
      case 'stance': {
        this.stance = STANCES[(STANCES.indexOf(this.stance) + 1) % STANCES.length]!;
        $('stance').textContent = this.stance[0]!.toUpperCase() + this.stance.slice(1);
        break;
      }
      case 'bipod':
        this.bipod = !this.bipod;
        $('bipod').classList.toggle('on', this.bipod);
        break;
      case 'laser':
        this.laser();
        break;
      case 'fire':
        this.fire();
        break;
    }
    this.renderTurrets();
  }

  private setBreath(hold: boolean): void {
    this.breath = hold ? holdBreath(this.breath, this.time) : releaseBreath(this.breath, this.time);
    $('breath').classList.toggle('pressed', hold);
  }

  private setScoped(on: boolean): void {
    this.scoped = on;
    $('app').classList.toggle('scoped', on);
    $('scope').classList.toggle('on', on);
  }

  private setZoom(z: number): void {
    this.zoom = Math.min(config.scope.maxZoom, Math.max(config.scope.minZoom, Math.round(z)));
    $<HTMLInputElement>('zoom').value = String(this.zoom);
    $('zoomv').textContent = `${this.zoom}×`;
  }

  private renderTurrets(): void {
    $('elev').textContent = this.elevMrad === 0 ? '0.0' : `${this.elevMrad > 0 ? '↑' : '↓'}${Math.abs(this.elevMrad).toFixed(1)}`;
    $('wind').textContent = formatMil(this.windMrad, 'R', 'L');
  }

  // ---- aiming ------------------------------------------------------------

  private fov(): number {
    return this.scoped ? scopeFov(this.zoom, config.scope.fovAt1xDeg) : config.scope.eyeFovDeg;
  }

  private look(dx: number, dy: number): void {
    const radPerPx = ((this.fov() * Math.PI) / 180 / Math.max(1, window.innerHeight)) * 0.9;
    this.yaw += dx * radPerPx;
    this.pitch = Math.max(-0.3, Math.min(0.3, this.pitch - dy * radPerPx));
  }

  private eyeHeight(): number {
    return config.practiceRange.firingPointHeightM + config.sway.eyeHeightM[this.stance];
  }

  private eye(): Vec3 {
    return { x: 0, y: this.eyeHeight(), z: 0 };
  }

  /** The true muzzle axis right now: where you point plus sway and recoil. */
  private trueAim(): { yaw: number; pitch: number } {
    const s = swayOffset({ stance: this.stance, bipod: this.bipod, breath: this.breath, seed: this.seed }, this.time);
    return { yaw: this.yaw + s.right / 1000, pitch: this.pitch + s.up / 1000 + this.recoil };
  }

  private windWithGust(p: Vec3): Vec3 {
    const w = laneWindAt(this.range.wind, p);
    const g = gustFactor(this.range.wind.gust, this.time);
    return { x: w.x * g, y: w.y * g, z: w.z * g };
  }

  // ---- shooting ----------------------------------------------------------

  private laser(): void {
    const d = rangefind(this.eye(), this.trueAim(), this.range.targets, flatGround);
    this.call(d === undefined ? 'Laser: no return' : `Laser: ${d.toFixed(0)} m`);
  }

  private fire(): void {
    if (!this.started || this.time - this.lastShotAt < CYCLE_SECONDS) return;
    this.lastShotAt = this.time;
    const rifle = rifleById(this.rifleId);
    const aim = this.trueAim();
    const eye = this.eye();
    const b = config.ballistics;
    const cold = this.coldBore ? rifle.coldBoreShiftMrad : { elevation: 0, windage: 0 };
    this.coldBore = false;
    const shot = {
      ...rifleShot(rifle),
      losElevation: aim.pitch,
      azimuth: aim.yaw,
      dialElevation: (this.elevMrad + cold.elevation) / 1000,
      dialWindage: (this.windMrad + cold.windage) / 1000,
    };
    const env = {
      atmosphere: this.range.atmosphere,
      wind: shotWindField(this.range.wind, eye, aim.yaw),
      latitude: this.range.latitude,
    };
    const traj = simulate(shot, env, {
      maxRange: 1200,
      dt: b.dtSeconds,
      integrator: b.integrator,
      seed: hashSeed(this.seed, ++this.shotIndex),
      fireTime: this.time,
      effects: b.effects,
      transonic: { machThreshold: b.transonic.machThreshold, sigma: b.transonic.sigmaRadPerSqrtS },
      maxTime: 4,
    });
    const impact = resolveImpact(traj, eye, aim.yaw, this.range.targets, flatGround);

    // Feedback: report, recoil, tracer, then impact effects when the bullet actually arrives.
    this.audio.shot(rifle.massKg * rifle.muzzleVelocityMps > 30 ? 1.3 : 1);
    this.recoil = this.stance === 'prone' && this.bipod ? 0.004 : 0.009;
    const stride = Math.max(1, Math.floor(traj.points.length / 300));
    const path = traj.points
      .filter((_, i) => i % stride === 0)
      .filter((p) => p.t <= impact.t)
      .map((p) => ({ t: p.t, p: shotToWorld(p, eye, aim.yaw) }));
    path.push({ t: impact.t, p: impact.point });
    this.view.tracer(path, impact.t);

    const distance = Math.hypot(impact.point.x - eye.x, impact.point.z - eye.z);
    const target = this.nearestTarget(aim.yaw);
    const n = this.shotIndex;
    window.setTimeout(() => {
      if (impact.kind === 'target') {
        this.view.ringPlate(impact.targetId!);
        this.audio.ping(distance / config.practiceRange.speedOfSoundForAudioMps, distance);
      } else if (impact.kind === 'ground') {
        this.view.dust(impact.point, rifle.diameterM > 0.01 ? 1.8 : 1);
        this.audio.thud(distance / config.practiceRange.speedOfSoundForAudioMps, distance);
      }
    }, impact.t * 1000);

    const d = Math.round(-target.position.z);
    let text: string;
    let hit = false;
    if (impact.kind === 'target') {
      hit = true;
      const t = this.range.targets.find((x) => x.id === impact.targetId)!;
      text = `HIT ${Math.round(-t.position.z)} m · ${impact.zone} · ${impact.t.toFixed(2)} s · ${Math.round(impact.energy)} J`;
    } else {
      const callout = missCall(traj, eye, aim.yaw, target);
      text = callout ? `Miss at ${d} m: ${spotterCall(callout.upMrad, callout.rightMrad)}` : `Miss: fell short of ${d} m`;
    }
    window.setTimeout(() => this.call(text, hit), impact.t * 1000);
    this.log(n, text, hit);
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

  private call(text: string, hit = false): void {
    const el = $('call');
    el.textContent = text;
    el.classList.toggle('hit', hit);
    el.classList.add('show');
    this.callTimer = 4;
  }

  private log(n: number, text: string, hit: boolean): void {
    const li = document.createElement('li');
    li.value = n;
    li.textContent = text;
    if (hit) li.className = 'hit';
    const ol = $('log');
    ol.prepend(li);
    while (ol.children.length > 30) ol.lastChild!.remove();
  }

  /**
   * Playtest/automation helper (only exposed with ?debug): aims at a plate's chest and dials the
   * calculator's solution for the current wind, rounded to turret clicks, like a perfect spotter.
   */
  debugSolve(targetIndex: number): { elevMrad: number; windMrad: number } {
    const t = this.range.targets[targetIndex]!;
    const eye = this.eye();
    const chestY = t.position.y + t.height - t.head - 0.175;
    const horizontal = Math.hypot(t.position.x - eye.x, t.position.z - eye.z);
    this.yaw = Math.atan2(t.position.x - eye.x, -(t.position.z - eye.z));
    this.pitch = Math.atan2(chestY - eye.y, horizontal);
    const rifle = rifleById(this.rifleId);
    const shot = { ...rifleShot(rifle), losElevation: this.pitch };
    const env = { atmosphere: this.range.atmosphere, wind: shotWindField(this.range.wind, eye, this.yaw) };
    const sol = solveFiringSolution(shot, env, Math.hypot(horizontal, chestY - eye.y), {
      fireTime: this.time,
      effects: { spinDrift: config.ballistics.effects.spinDrift },
    });
    const click = config.scope.clickMrad;
    this.elevMrad = Math.round((sol.elevation * 1000) / click) * click;
    this.windMrad = Math.round((sol.windage * 1000) / click) * click;
    this.renderTurrets();
    return { elevMrad: this.elevMrad, windMrad: this.windMrad };
  }

  // ---- frame -------------------------------------------------------------

  private resize(): void {
    const w = window.innerWidth;
    const h = window.innerHeight;
    this.renderer.setSize(w, h, false);
    this.view.camera.aspect = w / h;
    this.view.camera.updateProjectionMatrix();
    const rc = this.reticleCtx.canvas;
    const dpr = Math.min(2, window.devicePixelRatio);
    rc.width = w * dpr;
    rc.height = h * dpr;
  }

  private frame(now: number): void {
    const dt = Math.min(0.1, (now - this.lastFrame) / 1000);
    this.lastFrame = now;
    this.time += dt;
    this.recoil *= Math.exp(-dt * 6);
    if (this.callTimer > 0) {
      this.callTimer -= dt;
      if (this.callTimer <= 0) $('call').classList.remove('show');
    }

    const cam = this.view.camera;
    const aim = this.trueAim();
    cam.position.set(0, this.eyeHeight(), 0);
    cam.rotation.set(aim.pitch, -aim.yaw, 0);
    const fov = this.fov();
    if (cam.fov !== fov) {
      cam.fov = fov;
      cam.updateProjectionMatrix();
    }

    // Station wind meter (exact wind at your position only) and wind audio.
    const station = this.windWithGust({ x: 0, y: 0, z: -1 });
    const wc = windClock(station, this.yaw);
    $('windmeter').textContent = `Wind ${wc.speed.toFixed(1)} m/s from ${wc.clock} o'clock`;
    this.audio.setWind(wc.speed);

    this.view.update(this.time, dt, (p) => this.windWithGust(p));
    this.renderer.render(this.view.scene, cam);
    if (this.scoped) {
      drawReticle(this.reticleCtx, window.innerWidth, window.innerHeight, pxPerMrad(window.innerHeight, fov));
    }
    const ready = this.time - this.lastShotAt >= CYCLE_SECONDS;
    $('fire').toggleAttribute('disabled', !ready);
    requestAnimationFrame((t) => this.frame(t));
  }
}
