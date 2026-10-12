/**
 * The rifle and scope: aiming, zoom, turrets, sway, breath and the trigger. Modes (practice range,
 * duel) plug in where the shooter is, what they can range, and what happens when they fire.
 */
import * as THREE from 'three';
import type { Vec3 } from '@svs/ballistics';
import {
  config,
  holdBreath,
  initialBreath,
  rangefind,
  releaseBreath,
  swayOffset,
  type BreathState,
  type FireInput,
  type Stance,
} from '@svs/shared';
import { RangeAudio } from './audio.ts';
import { clickTurret, formatMil, pxPerMrad, scopeFov, windClock } from './format.ts';
import { drawReticle } from './reticle.ts';
import type { RangeView } from './scene.ts';
import { haptics } from './haptics.ts';
import type { Box, Ground, Silhouette } from '@svs/shared';

const STANCES: Stance[] = ['prone', 'crouch', 'standing'];
export const CYCLE_SECONDS = 1.4;

export const $ = <T extends HTMLElement>(id: string): T => document.getElementById(id) as T;

export interface ShooterHooks {
  /** Scope position in the world. */
  eye(): Vec3;
  /** World wind (with gust) at a point, for cues and the wind meter. */
  windAt(p: Vec3): Vec3;
  /** Whether the trigger is live right now (e.g. only in your shot phase). */
  canFire(): boolean;
  fire(input: FireInput, time: number): void;
  /** Things the laser can hit. */
  rangeables(): { targets: Silhouette[]; ground: Ground; cover: Box[] };
  /** Whether the player may change stance here (practice: yes; duel: only while moving). */
  canChangeStance(): boolean;
  onStance?(stance: Stance): void;
  /** Sway multiplier (wounds). */
  swayPenalty?(): number;
  /** Called every frame with the frame delta. */
  onFrame?(dt: number): void;
}

export class Shooter {
  readonly renderer: THREE.WebGLRenderer;
  readonly audio = new RangeAudio();
  private readonly reticleCtx: CanvasRenderingContext2D;
  view: RangeView | null = null;
  hooks: ShooterHooks | null = null;

  yaw = 0;
  pitch = 0;
  scoped = false;
  /** While set, a cinematic (the killcam) drives the camera instead of the player's aim. */
  director: ((cam: THREE.PerspectiveCamera, dt: number) => void) | null = null;
  zoom = 10;
  elevMrad = 0;
  windMrad = 0;
  stance: Stance = 'prone';
  bipod = true;
  breath: BreathState = initialBreath();
  /** Clock used for sway, gusts and the fire time (match time in a duel). */
  time = 0;
  /** When false the clock does not advance (hotseat curtain). */
  running = true;
  private lastShotAt = -10;
  private recoil = 0;
  private lastFrame = performance.now();
  private callTimer = 0;
  private dragging: { id: number; x: number; y: number } | null = null;
  private swaySeed = (Math.random() * 0x7fffffff) | 0;
  inputEnabled = false;

  constructor() {
    const canvas = $<HTMLCanvasElement>('view');
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(2, window.devicePixelRatio));
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 0.85;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.reticleCtx = $<HTMLCanvasElement>('reticle').getContext('2d')!;
    this.bindInput();
    window.addEventListener('resize', () => this.resize());
    this.resize();
    requestAnimationFrame((t) => this.frame(t));
  }

  attach(view: RangeView, hooks: ShooterHooks): void {
    this.view = view;
    this.hooks = hooks;
    this.resize();
  }

  lookAt(p: Vec3): void {
    const eye = this.hooks!.eye();
    this.yaw = Math.atan2(p.x - eye.x, -(p.z - eye.z));
    this.pitch = Math.atan2(p.y - eye.y, Math.hypot(p.x - eye.x, p.z - eye.z));
  }

  setStance(stance: Stance): void {
    this.stance = stance;
    $('stance').textContent = stance[0]!.toUpperCase() + stance.slice(1);
  }

  // ---- input -------------------------------------------------------------

  private bindInput(): void {
    const canvas = $<HTMLCanvasElement>('view');
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    canvas.addEventListener('mousedown', (e) => {
      if (!this.inputEnabled) return;
      if (e.button === 2) {
        this.setScoped(!this.scoped);
        return;
      }
      if (document.pointerLockElement === canvas) {
        if (e.button === 0) this.fire();
      } else {
        void (canvas.requestPointerLock?.() as Promise<void> | undefined)?.catch?.(() => undefined);
      }
    });
    document.addEventListener('mousemove', (e) => {
      if (this.inputEnabled && document.pointerLockElement === canvas) this.look(e.movementX, e.movementY);
    });
    canvas.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        if (this.inputEnabled) this.setZoom(this.zoom + (e.deltaY < 0 ? 1 : -1));
      },
      { passive: false },
    );
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
            if (this.inputEnabled) this.look((t.clientX - this.dragging.x) * 0.45, (t.clientY - this.dragging.y) * 0.45);
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

    document.querySelectorAll<HTMLButtonElement>('#turrets button[data-act], #actions button[data-act]').forEach((b) => {
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
    const zoom = $<HTMLInputElement>('zoom');
    zoom.min = String(config.scope.minZoom);
    zoom.max = String(config.scope.maxZoom);
    zoom.addEventListener('input', () => this.setZoom(Number(zoom.value)));

    window.addEventListener('keydown', (e) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === 'TEXTAREA' || tag === 'INPUT' || !this.inputEnabled) return;
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

  action(act: string): void {
    const s = config.scope;
    switch (act) {
      case 'elev-up':
      case 'elev-down':
        this.elevMrad = clickTurret(this.elevMrad, act === 'elev-up' ? 1 : -1, s.clickMrad, s.maxElevationMrad, -2);
        this.audio.click();
        haptics.click();
        break;
      case 'wind-left':
      case 'wind-right':
        this.windMrad = clickTurret(this.windMrad, act === 'wind-right' ? 1 : -1, s.clickMrad, s.maxWindageMrad);
        this.audio.click();
        haptics.click();
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
      case 'stance':
        if (this.hooks?.canChangeStance()) {
          this.setStance(STANCES[(STANCES.indexOf(this.stance) + 1) % STANCES.length]!);
          this.hooks.onStance?.(this.stance);
        } else this.call('Stance is locked until your move phase');
        break;
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

  setScoped(on: boolean): void {
    this.scoped = on;
    $('app').classList.toggle('scoped', on);
    $('scope').classList.toggle('on', on);
  }

  setZoom(z: number): void {
    this.zoom = Math.min(config.scope.maxZoom, Math.max(config.scope.minZoom, Math.round(z)));
    $<HTMLInputElement>('zoom').value = String(this.zoom);
    $('zoomv').textContent = `${this.zoom}×`;
  }

  renderTurrets(): void {
    $('elev').textContent = this.elevMrad === 0 ? '0.0' : `${this.elevMrad > 0 ? '↑' : '↓'}${Math.abs(this.elevMrad).toFixed(1)}`;
    $('wind').textContent = formatMil(this.windMrad, 'R', 'L');
  }

  // ---- aiming ------------------------------------------------------------

  fov(): number {
    return this.scoped ? scopeFov(this.zoom, config.scope.fovAt1xDeg) : config.scope.eyeFovDeg;
  }

  private look(dx: number, dy: number): void {
    const radPerPx = ((this.fov() * Math.PI) / 180 / Math.max(1, window.innerHeight)) * 0.9;
    this.yaw += dx * radPerPx;
    this.pitch = Math.max(-0.3, Math.min(0.3, this.pitch - dy * radPerPx));
  }

  /** The true muzzle axis right now: where you point plus sway and recoil. */
  trueAim(): { yaw: number; pitch: number } {
    const s = swayOffset(
      { stance: this.stance, bipod: this.bipod, breath: this.breath, seed: this.swaySeed, penalty: this.hooks?.swayPenalty?.() ?? 1 },
      this.time,
    );
    return { yaw: this.yaw + s.right / 1000, pitch: this.pitch + s.up / 1000 + this.recoil };
  }

  private laser(): void {
    if (!this.hooks) return;
    const r = this.hooks.rangeables();
    const d = rangefind(this.hooks.eye(), this.trueAim(), r.targets, r.ground, 3000, r.cover);
    this.call(d === undefined ? 'Laser: no return' : `Laser: ${d.toFixed(0)} m`);
  }

  fire(): void {
    if (!this.hooks || !this.inputEnabled) return;
    if (!this.hooks.canFire()) {
      this.call('You can only fire in your shot phase');
      return;
    }
    if (this.time - this.lastShotAt < CYCLE_SECONDS) return;
    this.lastShotAt = this.time;
    const aim = this.trueAim();
    this.recoil = this.stance === 'prone' && this.bipod ? 0.004 : 0.009;
    haptics.recoil();
    this.hooks.fire({ yaw: aim.yaw, pitch: aim.pitch, dialElevationMrad: this.elevMrad, dialWindageMrad: this.windMrad }, this.time);
  }

  call(text: string, hit = false): void {
    const el = $('call');
    el.textContent = text;
    el.classList.toggle('hit', hit);
    el.classList.add('show');
    this.callTimer = 4;
  }

  // ---- frame -------------------------------------------------------------

  resize(): void {
    const w = window.innerWidth;
    const h = window.innerHeight;
    this.renderer.setSize(w, h, false);
    if (this.view) {
      this.view.camera.aspect = w / h;
      this.view.camera.updateProjectionMatrix();
    }
    const rc = this.reticleCtx.canvas;
    const dpr = Math.min(2, window.devicePixelRatio);
    rc.width = w * dpr;
    rc.height = h * dpr;
  }

  /** Longest simulated step per frame; raised only by the debug harness on machines without a GPU. */
  maxStep = 0.1;

  private frame(now: number): void {
    const dt = Math.min(this.maxStep, (now - this.lastFrame) / 1000);
    this.lastFrame = now;
    if (this.running) this.time += dt;
    this.recoil *= Math.exp(-dt * 6);
    if (this.callTimer > 0) {
      this.callTimer -= dt;
      if (this.callTimer <= 0) $('call').classList.remove('show');
    }
    this.hooks?.onFrame?.(dt);
    if (this.view && this.hooks && this.director) {
      this.director(this.view.camera, dt);
      this.view.update(this.time, dt, (p) => this.hooks!.windAt(p));
      this.renderer.render(this.view.scene, this.view.camera);
    } else if (this.view && this.hooks) {
      const cam = this.view.camera;
      const aim = this.trueAim();
      const eye = this.hooks.eye();
      cam.position.set(eye.x, eye.y, eye.z);
      cam.rotation.set(aim.pitch, -aim.yaw, 0);
      const fov = this.fov();
      if (cam.fov !== fov) {
        cam.fov = fov;
        cam.updateProjectionMatrix();
      }
      const station = this.hooks.windAt(eye);
      const wc = windClock(station, this.yaw);
      $('windmeter').textContent = `Wind ${wc.speed.toFixed(1)} m/s from ${wc.clock} o'clock`;
      this.audio.setWind(this.running ? wc.speed : 0);
      this.view.update(this.time, dt, (p) => this.hooks!.windAt(p));
      this.renderer.render(this.view.scene, cam);
      if (this.scoped) {
        drawReticle(this.reticleCtx, window.innerWidth, window.innerHeight, pxPerMrad(window.innerHeight, fov));
      }
      $('fire').toggleAttribute('disabled', !this.hooks.canFire() || this.time - this.lastShotAt < CYCLE_SECONDS);
    }
    requestAnimationFrame((t) => this.frame(t));
  }
}
