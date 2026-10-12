/**
 * A duel: two humans passing the device (hotseat), a human against the bot, or an online match.
 * The match itself is @svs/shared's pure state machine; this file only renders it and feeds inputs.
 * Offline the reducer runs here; online the server runs it and sends this player's view.
 */
import { gustFactor, solveFiringSolution, type Vec3 } from '@svs/ballistics';
import {
  activeConditions,
  applyInput,
  botPlan,
  config,
  createMatch,
  DEFAULT_MAP,
  eyeOf,
  facingYaw,
  isSuddenDeath,
  laneWindAt,
  mapById,
  moveBudget,
  other,
  chestOf,
  rifleById,
  rifleShot,
  shotWindField,
  silhouetteFor,
  timeLeft,
  usableZone,
  zoneShrink,
  type BotDifficulty,
  type DuelMap,
  type MatchInput,
  type MatchState,
  type MatchView,
  type ShotOutcome,
  type Side,
  type Stance,
} from '@svs/shared';
import { playKillcam } from './killcam.ts';
import { playShotFeedback } from './practiceMode.ts';
import { spotterCall } from './format.ts';
import { buildWorld } from './scene.ts';
import type { OnlineLink } from './net.ts';
import { haptics } from './haptics.ts';
import { $, type Shooter } from './shooter.ts';

export interface DuelSetup {
  names: Record<Side, string>;
  rifles: Record<Side, string>;
  /** Bot difficulty for side B, or undefined for hotseat. */
  bot?: BotDifficulty;
  mapId?: string;
  /** Online match: the server link and its first view of the match. */
  online?: { link: OnlineLink; view: MatchView };
}

type Screen = 'map' | 'scope' | 'wait' | 'card';

export class DuelMode {
  private state: MatchState;
  private readonly map: DuelMap;
  private readonly humans: Side[];
  /** Whose eyes are on the screen right now. */
  private viewer: Side;
  private screen: Screen = 'map';
  private botQueue: MatchInput[] = [];
  private phaseKey = '';
  private setupTurn: Side = 'A';
  private pendingCard: (() => void) | null = null;
  private readonly online: OnlineLink | null;
  private connection: 'ok' | 'dropped' = 'ok';
  /** True while the killcam plays; phase changes wait until its result card is dismissed. */
  private cinematic = false;

  constructor(
    private readonly shooter: Shooter,
    private readonly setup: DuelSetup,
    private readonly onExit: () => void,
  ) {
    this.online = setup.online?.link ?? null;
    let seed: number;
    if (setup.online) {
      // The match seed stays on the server; scenery only needs some seed both players can share.
      const v = setup.online.view;
      seed = 0x5eed;
      this.state = v;
      this.humans = [v.you];
      this.viewer = v.you;
      shooter.time = v.now;
    } else {
      seed = (Date.now() ^ (Math.random() * 0x7fffffff)) >>> 0;
      this.humans = setup.bot ? ['A'] : ['A', 'B'];
      this.viewer = 'A';
      shooter.time = 0;
      this.state = createMatch({
        seed,
        mapId: setup.mapId ?? DEFAULT_MAP,
        players: {
          A: { name: setup.names.A, rifleId: setup.rifles.A },
          B: { name: setup.names.B, rifleId: setup.rifles.B, bot: setup.bot },
        },
        time: 0,
      });
    }
    this.map = mapById(this.state.mapId);
    const view = buildWorld({
      seed,
      ground: this.map.ground,
      laneLength: this.map.laneLength,
      laneEdges: this.map.windEdges,
      cover: this.map.cover,
      laneHalfWidth: this.map.look.laneHalfWidth,
      flags: this.map.look.flags.map(([x, s]) => [x, -s] as [number, number]),
      palette: this.map.look.palette,
    });
    shooter.attach(view, {
      eye: () => {
        const p = this.state.players[this.viewer];
        return eyeOf(p.position, p.stance);
      },
      windAt: (p) => this.windAt(p),
      canFire: () => this.state.phase === 'shot' && this.state.actor === this.viewer && this.screen === 'scope' && !this.state.acted,
      fire: (input, time) => this.input({ type: 'fire', side: this.viewer, input, time }),
      rangeables: () => {
        const e = this.state.players[other(this.viewer)];
        return {
          targets: this.enemyVisible() ? [silhouetteFor(e.side, e.position, e.stance, facingYaw(e.side))] : [],
          ground: this.map.ground,
          cover: this.map.cover,
        };
      },
      canChangeStance: () => false,
      swayPenalty: () => (this.state.players[this.viewer].wounds > 0 ? config.player.wound.swayFactor : 1),
      onFrame: () => this.frame(),
    });
    this.bindUi();
    this.online?.listen({
      update: (v, outcome) => this.remote(v, outcome),
      error: (message) => this.shooter.call(message),
      connection: (c) => {
        if (c === 'lost') {
          this.card('Connection lost', 'Could not get back to the game server. If you do not return in time the match is forfeited.', false, () => this.exit());
          $('card-go').textContent = 'Back to menu';
        }
        this.connection = c === 'dropped' ? 'dropped' : 'ok';
        this.renderBar();
      },
    });
    $('rifle').hidden = true;
    $('log').innerHTML = '';
    document.body.classList.add('duel');
    this.enterPhase();
  }

  // ---- inputs ------------------------------------------------------------

  private input(input: MatchInput): void {
    if (this.online) {
      // The server stamps the time and runs the reducer; its answer arrives as an update.
      if (input.type === 'place') this.online.send({ type: 'place', position: input.position, stance: input.stance });
      else if (input.type === 'ready') this.online.send({ type: 'ready' });
      else if (input.type === 'fire') this.online.send({ type: 'fire', input: input.input });
      return;
    }
    const r = applyInput(this.state, input);
    if (r.error) {
      if (this.humans.includes((input as { side?: Side }).side ?? 'A')) this.shooter.call(r.error);
      return;
    }
    const before = this.state;
    this.state = r.state;
    if (r.outcome) this.onShot(before, r.outcome);
    else this.checkPhase();
  }

  /** A new view from the server, with the shot that produced it if there was one. */
  private remote(view: MatchView, outcome?: ShotOutcome): void {
    if (Math.abs(this.shooter.time - view.now) > 0.25) this.shooter.time = view.now;
    const before = this.state;
    this.state = view;
    if (outcome) this.onShot(before, outcome);
    else if (view.phase === 'matchOver' && before.phase !== 'matchOver') this.matchOver(); // forfeit or idle timeout
    else this.checkPhase();
  }

  /** Offline both figures are known; online the server only sends the enemy when you can see it. */
  private enemyVisible(): boolean {
    return !this.online || (this.state as MatchView).enemySeen;
  }

  private bindUi(): void {
    $('ready').onclick = () => this.readyClicked();
    $('look').onclick = () => this.show(this.screen === 'scope' ? 'map' : 'scope');
    document.querySelectorAll<HTMLButtonElement>('#mapview [data-stance]').forEach((b) => {
      b.onclick = () => this.place(this.state.players[this.viewer].position, b.dataset.stance as Stance);
    });
    const canvas = $<HTMLCanvasElement>('zonemap');
    canvas.onpointerdown = (e) => {
      const rect = canvas.getBoundingClientRect();
      const world = this.mapToWorld(e.clientX - rect.left, e.clientY - rect.top, rect.width, rect.height);
      this.place(world, this.state.players[this.viewer].stance);
    };
    $('curtain-go').onclick = () => {
      $('curtain').hidden = true;
      this.shooter.running = true;
      this.showForPhase();
    };
    $('card-go').onclick = () => {
      $('card').hidden = true;
      const next = this.pendingCard;
      this.pendingCard = null;
      this.shooter.running = true;
      next?.();
    };
    $('quit').onclick = () => this.exit();
  }

  private place(p: Vec3, stance: Stance): void {
    const s = this.state;
    if (s.phase === 'setup' || (s.phase === 'move' && s.actor === this.viewer)) {
      this.input({ type: 'place', side: this.viewer, position: p, stance, time: this.shooter.time });
      this.drawMap();
    }
  }

  private readyClicked(): void {
    const s = this.state;
    if (s.phase === 'setup') {
      this.input({ type: 'ready', side: this.viewer, time: this.shooter.time });
      if (this.state.phase === 'setup' && this.humans.length === 2 && this.viewer === 'A') {
        this.setupTurn = 'B';
        this.curtain('B');
      } else if (this.state.phase === 'setup') {
        this.show('wait');
      }
      return;
    }
    if ((s.phase === 'move' || s.phase === 'shot') && s.actor === this.viewer) {
      this.input({ type: 'ready', side: this.viewer, time: this.shooter.time });
    }
  }

  // ---- phases ------------------------------------------------------------

  private frame(): void {
    if (!this.shooter.running) {
      this.renderBar();
      return;
    }
    if (this.online) {
      this.renderBar();
      return;
    }
    const t = this.shooter.time;
    while (this.botQueue.length && this.botQueue[0]!.time <= t) {
      const next = this.botQueue.shift()!;
      this.input(next);
      if (!this.shooter.running) break;
    }
    if (this.shooter.running && t >= this.state.phaseEnd) this.input({ type: 'tick', time: t });
    this.renderBar();
  }

  private checkPhase(): void {
    if (this.cinematic) return;
    const key = `${this.state.round}:${this.state.pair}:${this.state.step}:${this.state.phase}`;
    if (key !== this.phaseKey) this.enterPhase();
    else if (this.screen === 'map') this.drawMap();
  }

  private enterPhase(): void {
    const s = this.state;
    this.phaseKey = `${s.round}:${s.pair}:${s.step}:${s.phase}`;
    this.botQueue = [];
    for (const side of ['A', 'B'] as const) {
      if (s.players[side].bot) this.botQueue.push(...botPlan(s, side));
    }
    this.botQueue.sort((a, b) => a.time - b.time);
    this.renderLog();

    if (s.phase === 'roundOver' || s.phase === 'matchOver') return; // handled by the shot card
    if (s.phase === 'setup') {
      this.setupTurn = 'A';
      if (!this.online) this.viewer = 'A'; // online you are always your own side
      if (this.humans.length === 2) this.curtain('A');
      else this.showForPhase();
      return;
    }
    const actor = s.actor!;
    const human = this.humans.includes(actor);
    if (human && this.viewer !== actor && this.humans.length === 2) {
      this.curtain(actor);
      return;
    }
    if (human) this.viewer = actor;
    this.showForPhase();
  }

  /** Hotseat hand-over: hide everything and stop the clock until the next player taps. */
  private curtain(side: Side): void {
    this.viewer = side;
    this.shooter.running = false;
    this.show('wait');
    $('curtain-text').textContent = `Pass the device to ${this.state.players[side].name}.`;
    $('curtain').hidden = false;
  }

  private showForPhase(): void {
    const s = this.state;
    const me = s.players[this.viewer];
    this.shooter.setStance(me.stance);
    if (s.phase === 'setup') {
      this.show('map');
      return;
    }
    if (s.actor === this.viewer && this.humans.includes(this.viewer)) {
      if (s.phase === 'move') this.show('map');
      else {
        this.show('scope');
        this.shooter.call(`Your shot. ${this.state.players[other(this.viewer)].name} is frozen.`);
      }
      return;
    }
    this.show('wait');
  }

  private show(screen: Screen): void {
    this.screen = screen;
    const s = this.state;
    const scope = screen === 'scope';
    $('mapview').hidden = screen !== 'map';
    $('waitview').hidden = screen !== 'wait';
    this.shooter.inputEnabled = scope;
    this.shooter.setScoped(scope);
    document.body.classList.toggle('hud-off', !scope);
    // Show the enemy figure only when looking through the glass.
    const enemy = s.players[other(this.viewer)];
    this.shooter.view!.setFigure(
      'enemy',
      scope && this.enemyVisible()
        ? { position: enemy.position, stance: enemy.stance, facingYaw: facingYaw(enemy.side), dims: config.player.silhouette[enemy.stance] }
        : null,
    );
    if (scope) {
      const front = this.map.zones[other(this.viewer)];
      const look = { x: 0, y: this.map.ground(0, -(front.sMin + front.sMax) / 2) + 1, z: -(front.sMin + front.sMax) / 2 };
      if (s.phase === 'shot') this.shooter.lookAt(look);
    }
    const mine = s.phase === 'setup' || ((s.phase === 'move' || s.phase === 'shot') && s.actor === this.viewer);
    $('look').hidden = !mine || screen === 'wait';
    $('look').textContent = screen === 'scope' ? 'Map' : 'Look through scope';
    $('ready').hidden = !mine || screen === 'wait';
    $('ready').textContent = s.phase === 'setup' ? 'Ready' : s.phase === 'move' ? 'Done moving' : 'Hold fire';
    if (screen === 'map') this.drawMap();
    if (screen === 'wait') {
      const actor = s.actor ? s.players[s.actor] : null;
      $('wait-text').textContent =
        s.phase === 'setup'
          ? 'Waiting for the other player to set up.'
          : s.phase === 'shot'
            ? `You are frozen. ${actor?.name} is looking for you through the scope.`
            : `${actor?.name} is repositioning.`;
    }
    this.renderBar();
  }

  // ---- shots -------------------------------------------------------------

  private onShot(before: MatchState, outcome: ShotOutcome): void {
    const shooterSide = before.actor!;
    const shooter = before.players[shooterSide];
    const target = before.players[other(shooterSide)];
    const rifle = rifleById(shooter.rifleId);
    const humanShot = this.humans.includes(shooterSide);
    if (humanShot) playShotFeedback(this.shooter, outcome.trajectory, outcome.eye, outcome.yaw, outcome.impact, rifle.diameterM);
    else this.shooter.audio.shot(rifle.diameterM > 0.0085 ? 0.8 : 0.6);
    if (!humanShot && this.humans.includes(other(shooterSide))) {
      if (outcome.verdict !== 'miss') haptics.struck(outcome.verdict === 'kill');
      else if (outcome.nearMiss) haptics.snap();
    }

    const dist = Math.round(this.state.shots.at(-1)?.distance ?? 0);
    let headline: string;
    if (outcome.verdict === 'kill') headline = `${shooter.name} killed ${target.name}`;
    else if (outcome.verdict === 'wound') headline = `${shooter.name} wounded ${target.name}`;
    else headline = `${shooter.name} missed`;
    const detail: string[] = [];
    if (outcome.verdict !== 'miss') {
      detail.push(`${outcome.zone} hit at ${dist} m, ${outcome.impact.t.toFixed(2)} s flight, ${Math.round(outcome.impact.energy)} J.`);
    } else if (outcome.impact.kind === 'cover') {
      detail.push(`The bullet hit cover at ${dist} m.`);
    } else if (humanShot && outcome.call) {
      detail.push(`Spotter: ${spotterCall(outcome.call.upMrad, outcome.call.rightMrad)} at ${dist} m.`);
    } else if (humanShot) {
      const at = Math.round(Math.hypot(outcome.impact.point.x - outcome.eye.x, outcome.impact.point.z - outcome.eye.z));
      detail.push(`Spotter: dust at ${at} m, well short of the target at ${dist} m.`);
    }
    if (outcome.nearMiss) detail.push(`${target.name} heard the snap: it passed within a metre.`);
    if (!humanShot) detail.push(`The shot came from ${dist} m away. Its position is revealed until it moves.`);

    const after = this.state;
    const wait = humanShot ? outcome.impact.t * 1000 + 700 : 300;
    if (!this.online) this.shooter.running = false;
    const showCard = (): void =>
      this.card(headline, detail.join(' '), outcome.verdict !== 'miss', () => {
        if (after.phase === 'roundOver') this.roundOver();
        else if (after.phase === 'matchOver') this.matchOver();
        else this.checkPhase();
      });
    window.setTimeout(() => {
      if (outcome.verdict === 'miss') {
        showCard();
        return;
      }
      // Every hit gets the killcam, for the shooter and for the player who was hit.
      this.shooter.view!.setFigure('enemy', null);
      this.cinematic = true;
      void playKillcam(this.shooter, {
        trajectory: outcome.trajectory,
        eye: outcome.eye,
        yaw: outcome.yaw,
        impact: outcome.impact,
        target: { position: target.position, stance: target.stance, facingYaw: facingYaw(target.side) },
        title: headline,
        lethal: outcome.verdict === 'kill',
      }).then(() => {
        this.cinematic = false;
        this.show(this.screen);
        showCard();
      });
    }, wait);
  }

  private card(title: string, text: string, hit: boolean, next: () => void): void {
    if (!this.online) this.shooter.running = false; // offline the clock waits; online the server does not
    this.pendingCard = next;
    $('card-title').textContent = title;
    $('card-title').classList.toggle('hit', hit);
    $('card-text').textContent = text;
    $('card').hidden = false;
  }

  private score(): string {
    const { A, B } = this.state.players;
    return `${A.name} ${A.kills} – ${B.kills} ${B.name}`;
  }

  private roundOver(): void {
    const w = this.state.players[this.state.roundWinner!];
    const next = this.online ? 'The next round starts in a few seconds.' : 'Positions reset and the weather drifts.';
    this.card(`Round ${this.state.round} to ${w.name}`, `${this.score()}. First to ${config.rules.killsToWin} kills wins. ${next}`, false, () => {
      if (this.online) this.checkPhase();
      else this.input({ type: 'nextRound', time: this.shooter.time });
    });
  }

  private matchOver(): void {
    const w = this.state.players[this.state.winner!];
    const how = this.state.forfeit ? `${this.state.players[other(w.side)].name} forfeited.` : `${this.state.round} rounds, ${this.state.shots.length} shots fired.`;
    const r = (this.state as MatchView).ratings?.[this.viewer];
    const rating =
      r?.after !== undefined ? ` Your rating: ${r.before} → ${r.after} (${r.after >= r.before ? '+' : ''}${r.after - r.before}).` : '';
    this.card(`${w.name} wins the match`, `${this.score()}. ${how}${rating}`, true, () => this.exit());
    $('card-go').textContent = 'Back to menu';
  }

  private exit(): void {
    if (this.online) {
      if (this.state.phase !== 'matchOver') this.online.send({ type: 'forfeit' });
      this.online.handlers = null;
      this.online.leave();
    }
    this.shooter.running = true;
    this.shooter.inputEnabled = false;
    $('card').hidden = true;
    $('curtain').hidden = true;
    $('mapview').hidden = true;
    $('waitview').hidden = true;
    $('card-go').textContent = 'Continue';
    document.body.classList.remove('duel', 'hud-off');
    this.shooter.hooks = null;
    this.onExit();
  }

  /** Automation helper (?debug): aim at the enemy's chest and dial a perfect calculator solution. */
  debugSolve(): { elevMrad: number; windMrad: number; phase: string } {
    const s = this.state;
    const me = s.players[this.viewer];
    const enemy = s.players[other(this.viewer)];
    const eye = eyeOf(me.position, me.stance);
    const chest = chestOf(silhouetteFor('t', enemy.position, enemy.stance, facingYaw(enemy.side)));
    this.shooter.lookAt(chest);
    const c = activeConditions(s);
    const horiz = Math.hypot(chest.x - eye.x, chest.z - eye.z);
    const sol = solveFiringSolution(
      { ...rifleShot(rifleById(me.rifleId)), losElevation: this.shooter.pitch },
      { atmosphere: c.atmosphere, wind: shotWindField(c.wind, eye, this.shooter.yaw) },
      Math.hypot(horiz, chest.y - eye.y),
      { fireTime: this.shooter.time },
    );
    this.shooter.elevMrad = Math.round(sol.elevation * 10000) / 10;
    this.shooter.windMrad = Math.round(sol.windage * 10000) / 10;
    this.shooter.renderTurrets();
    return { elevMrad: this.shooter.elevMrad, windMrad: this.shooter.windMrad, phase: `${s.phase}:${s.actor}:${this.screen}` };
  }

  debugState(): string {
    const s = this.state;
    return `${s.phase}:${s.actor}:${this.screen}:viewer=${this.viewer}:A${s.players.A.kills}-B${s.players.B.kills}`;
  }

  // ---- rendering ---------------------------------------------------------

  private windAt(p: Vec3): Vec3 {
    const c = activeConditions(this.state);
    const w = laneWindAt(c.wind, p);
    const g = gustFactor(c.wind.gust, this.shooter.time);
    return { x: w.x * g, y: w.y * g, z: w.z * g };
  }

  private renderBar(): void {
    const s = this.state;
    const t = Math.ceil(timeLeft(s, this.shooter.time));
    let what = '';
    if (s.phase === 'setup') what = 'Setup: choose your start position';
    else if (s.phase === 'move') what = `${s.players[s.actor!].name} moves`;
    else if (s.phase === 'shot') what = `${s.players[s.actor!].name} shoots`;
    else if (s.phase === 'roundOver') what = 'Round over';
    else what = 'Match over';
    const sd = isSuddenDeath(s) ? ' · SUDDEN DEATH' : '';
    const clock = Number.isFinite(t) && (s.phase === 'setup' || s.phase === 'move' || s.phase === 'shot') ? ` · ${t}s` : '';
    $('phasebar').textContent = `Round ${s.round} · ${what}${clock}${sd} · ${this.score()}`;
    const a = activeConditions(s).atmosphere;
    $('weather').textContent = `${a.temperatureC.toFixed(0)} °C · ${a.pressureHPa.toFixed(0)} hPa · ${(a.humidity * 100).toFixed(0)}% RH`;
    const me = s.players[this.viewer];
    let status = me.wounds > 0 ? ' · wounded' : '';
    if (this.online && this.connection === 'dropped') status += ' · connection lost, reconnecting…';
    else if (this.online && !(s as MatchView).opponentConnected && s.phase !== 'matchOver') status += ' · opponent disconnected, holding their seat';
    const rt = (s as MatchView).ratings;
    if (rt) status += ` · ranked ${rt[this.viewer].before} vs ${rt[other(this.viewer)].before}`;
    $('me').textContent = `${me.name} · ${rifleById(me.rifleId).name} · ${me.stance}${status}`;
  }

  private renderLog(): void {
    const ol = $('log');
    ol.innerHTML = '';
    for (const shot of this.state.shots.slice().reverse().slice(0, 30)) {
      const li = document.createElement('li');
      li.value = shot.shotIndex;
      const who = this.state.players[shot.shooter].name;
      li.textContent = `R${shot.round} ${who}: ${shot.verdict}${shot.zone ? ` (${shot.zone})` : ''} at ${Math.round(shot.distance)} m`;
      if (shot.verdict !== 'miss') li.className = 'hit';
      ol.append(li);
    }
  }

  /** Top-down map of the viewer's own zone; the enemy is always "up". */
  private mapFrame(w: number, h: number) {
    const zone = this.map.zones[this.viewer];
    const pad = 14;
    const zw = zone.xMax - zone.xMin;
    const zd = zone.sMax - zone.sMin;
    const scale = Math.min((w - 2 * pad) / zw, (h - 2 * pad) / zd);
    const ox = (w - zw * scale) / 2;
    const oy = (h - zd * scale) / 2;
    const flip = this.viewer === 'B';
    const toScreen = (p: { x: number; z: number }) => {
      const s = -p.z;
      const u = flip ? zone.xMax - p.x : p.x - zone.xMin;
      const v = flip ? s - zone.sMin : zone.sMax - s;
      return { x: ox + u * scale, y: oy + v * scale };
    };
    const toWorld = (sx: number, sy: number): Vec3 => {
      const u = (sx - ox) / scale;
      const v = (sy - oy) / scale;
      const x = flip ? zone.xMax - u : zone.xMin + u;
      const s = flip ? zone.sMin + v : zone.sMax - v;
      return { x, y: 0, z: -s };
    };
    return { zone, scale, toScreen, toWorld };
  }

  private mapToWorld(sx: number, sy: number, w: number, h: number): Vec3 {
    return this.mapFrame(w, h).toWorld(sx, sy);
  }

  private drawMap(): void {
    const canvas = $<HTMLCanvasElement>('zonemap');
    const rect = canvas.getBoundingClientRect();
    const dpr = Math.min(2, window.devicePixelRatio);
    canvas.width = Math.max(1, rect.width * dpr);
    canvas.height = Math.max(1, rect.height * dpr);
    const ctx = canvas.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const w = rect.width;
    const h = rect.height;
    const s = this.state;
    const { zone, scale, toScreen } = this.mapFrame(w, h);
    const me = s.players[this.viewer];
    const [hue, sat, light] = this.map.look.palette?.lane ?? [0.21, 0.22, 0.4];
    ctx.clearRect(0, 0, w, h);

    // Terrain shading.
    const step = 4;
    for (let x = zone.xMin; x < zone.xMax; x += step) {
      for (let sd = zone.sMin; sd < zone.sMax; sd += step) {
        const g = this.map.ground(x + step / 2, -(sd + step / 2));
        const l = Math.min(80, light * 100 * 0.75 + Math.min(20, Math.max(0, g) * 3));
        ctx.fillStyle = `hsl(${hue * 360} ${sat * 100}% ${l}%)`;
        const a = toScreen({ x, z: -sd });
        const b = toScreen({ x: x + step, z: -(sd + step) });
        ctx.fillRect(Math.min(a.x, b.x), Math.min(a.y, b.y), Math.abs(b.x - a.x) + 0.5, Math.abs(b.y - a.y) + 0.5);
      }
    }
    // Sudden death: the unusable part of the zone is hatched.
    const shrink = zoneShrink(s);
    if (shrink > 0) {
      const u = usableZone(this.map, this.viewer, shrink, config.rules.minZoneDepthM);
      ctx.fillStyle = 'rgba(160, 30, 20, 0.35)';
      const keep0 = toScreen({ x: zone.xMin, z: -u.sMin });
      const keep1 = toScreen({ x: zone.xMax, z: -u.sMax });
      const top = Math.min(keep0.y, keep1.y);
      const bottom = Math.max(keep0.y, keep1.y);
      ctx.fillRect(0, bottom, w, h - bottom);
      ctx.fillRect(0, 0, w, top);
    }
    // Cover: hard is grey, soft (bullets of enough energy go through) is blue-grey and dashed.
    ctx.strokeStyle = '#2c2a26';
    for (const box of this.map.cover) {
      ctx.fillStyle = box.material === 'soft' ? '#8fa3b5' : '#9a958a';
      ctx.setLineDash(box.material === 'soft' ? [3, 2] : []);
      const a = toScreen({ x: box.min.x, z: box.min.z });
      const b = toScreen({ x: box.max.x, z: box.max.z });
      ctx.fillRect(Math.min(a.x, b.x), Math.min(a.y, b.y), Math.abs(b.x - a.x), Math.abs(b.y - a.y));
      ctx.strokeRect(Math.min(a.x, b.x), Math.min(a.y, b.y), Math.abs(b.x - a.x), Math.abs(b.y - a.y));
    }
    ctx.setLineDash([]);
    // Enemy direction.
    ctx.fillStyle = 'rgba(255,255,255,0.85)';
    ctx.font = '600 12px system-ui, sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(`▲ Enemy zone, about ${Math.round(this.map.zones.B.front - this.map.zones.A.front)} m`, w / 2, 14);

    // Reach circle in your move phase.
    const budget = moveBudget(s, this.viewer);
    const from = s.moveFrom ?? me.position;
    const reachesAll = [zone.xMin, zone.xMax].every((x) => [zone.sMin, zone.sMax].every((sd) => Math.hypot(x - from.x, -sd - from.z) <= budget));
    if (s.phase === 'move' && s.actor === this.viewer && s.moveFrom && !reachesAll) {
      const c = toScreen(s.moveFrom);
      const z0 = toScreen({ x: zone.xMin, z: -zone.sMin });
      const z1 = toScreen({ x: zone.xMax, z: -zone.sMax });
      ctx.save();
      ctx.beginPath();
      ctx.rect(Math.min(z0.x, z1.x), Math.min(z0.y, z1.y), Math.abs(z1.x - z0.x), Math.abs(z1.y - z0.y));
      ctx.clip();
      ctx.strokeStyle = 'rgba(255, 181, 71, 0.9)';
      ctx.setLineDash([5, 4]);
      ctx.beginPath();
      ctx.arc(c.x, c.y, budget * scale, 0, Math.PI * 2);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.restore();
    }
    const p = toScreen(me.position);
    ctx.fillStyle = me.revealed ? '#ff7b2e' : '#ffb547';
    ctx.beginPath();
    ctx.arc(p.x, p.y, 7, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#111';
    ctx.font = '700 9px system-ui, sans-serif';
    ctx.textBaseline = 'middle';
    ctx.fillText(me.stance[0]!.toUpperCase(), p.x, p.y + 0.5);
    ctx.textBaseline = 'alphabetic';

    $('map-hint').textContent =
      s.phase === 'setup'
        ? 'Tap the map to choose where you start, pick a stance, then Ready.'
        : me.revealed
          ? 'You fired from here, so the enemy knows this spot. Tap the map to move.'
          : reachesAll
            ? 'Tap anywhere in your zone to move, pick a stance, then Done moving.'
            : 'Tap inside the circle to move, pick a stance, then Done moving.';
    document.querySelectorAll<HTMLButtonElement>('#mapview [data-stance]').forEach((b) => b.classList.toggle('on', b.dataset.stance === me.stance));
    if (me.heardSnapFrom) {
      const dx = me.heardSnapFrom.x - me.position.x;
      const dz = me.heardSnapFrom.z - me.position.z;
      const bearing = Math.round(((Math.atan2(dx, -dz) * 180) / Math.PI + 360) % 360);
      $('map-hint').textContent += ` A bullet snapped past you from bearing ${bearing}°.`;
    }
  }
}
