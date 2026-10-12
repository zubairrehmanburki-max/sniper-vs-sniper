/**
 * One duel: two clients, one authoritative MatchState. Clients send intents (place, ready, fire);
 * the room stamps them with its own clock, runs them through the shared reducer, and sends each
 * player their own filtered view. The match seed never leaves the server.
 */
import { Room, type Client } from '@colyseus/core';
import {
  applyInput,
  config,
  createMatch,
  DEFAULT_MAP,
  MAPS,
  encodeView,
  other,
  rifleById,
  thinOutcome,
  viewFor,
  type ClientMessage,
  type JoinOptions,
  type MatchInput,
  type MatchState,
  type ServerMessage,
  type ShotOutcome,
  type Side,
} from '@svs/shared';

interface Seat {
  side: Side;
  name: string;
  rifleId: string;
  client: Client;
  connected: boolean;
}

const TICK_MS = 250;

export class DuelRoom extends Room {
  override maxClients = 2;
  private code = '';
  private mapId = DEFAULT_MAP;
  private seats = new Map<string, Seat>();
  private match: MatchState | null = null;
  private startedAt = Date.now();
  /** Seed source; overridable so tests can replay a match. */
  static seed: () => number = () => (Math.random() * 0x7fffffff) >>> 0;

  private now(): number {
    return (Date.now() - this.startedAt) / 1000;
  }

  override onCreate(options: Partial<JoinOptions>): void {
    this.code = options.code ?? '';
    if (options.mapId && MAPS[options.mapId]) this.mapId = options.mapId;
    this.onMessage('input', (client: Client, msg: ClientMessage) => this.handle(client, msg));
    this.clock.setInterval(() => this.tick(), TICK_MS);
  }

  override onJoin(client: Client, options: Partial<JoinOptions>): void {
    const taken = new Set([...this.seats.values()].map((s) => s.side));
    const side: Side = taken.has('A') ? 'B' : 'A';
    const name = (options.name ?? '').trim().slice(0, 20) || (side === 'A' ? 'Player 1' : 'Player 2');
    let rifleId = options.rifleId ?? config.rifles[0]!.id;
    try {
      rifleById(rifleId);
    } catch {
      rifleId = config.rifles[0]!.id;
    }
    this.seats.set(client.sessionId, { side, name, rifleId, client, connected: true });
    if (this.seats.size < 2) {
      this.sendTo(client, { type: 'waiting', code: this.code || undefined });
      return;
    }
    void this.lock();
    const bySide = Object.fromEntries([...this.seats.values()].map((s) => [s.side, s])) as Record<Side, Seat>;
    this.startedAt = Date.now();
    this.match = createMatch({
      seed: DuelRoom.seed(),
      mapId: this.mapId,
      players: {
        A: { name: bySide.A.name, rifleId: bySide.A.rifleId },
        B: { name: bySide.B.name, rifleId: bySide.B.rifleId },
      },
      time: 0,
      forfeitOnIdle: true,
    });
    this.publish();
  }

  override async onDrop(client: Client): Promise<void> {
    const seat = this.seats.get(client.sessionId);
    if (!seat || !this.match || this.match.phase === 'matchOver') return;
    seat.connected = false;
    this.publish();
    // Hold the seat; the phase clock keeps running, so idling still forfeits by the normal rules.
    await this.allowReconnection(client, config.rules.reconnectGraceSeconds);
  }

  override onReconnect(client: Client): void {
    const seat = this.seats.get(client.sessionId);
    if (!seat) return;
    seat.client = client;
    seat.connected = true;
    this.publish();
  }

  override onLeave(client: Client): void {
    const seat = this.seats.get(client.sessionId);
    if (!seat) return;
    if (!this.match) {
      this.seats.delete(client.sessionId); // still waiting for an opponent: just free the seat
      return;
    }
    seat.connected = false;
    if (this.match.phase !== 'matchOver') this.apply({ type: 'forfeit', side: seat.side, time: this.now() });
    else this.publish();
  }

  private handle(client: Client, msg: ClientMessage): void {
    const seat = this.seats.get(client.sessionId);
    if (!seat || !this.match || typeof msg !== 'object' || msg === null) return;
    const time = this.now();
    const side = seat.side;
    let input: MatchInput;
    switch (msg.type) {
      case 'place':
        if (!isVec(msg.position) || !['standing', 'crouch', 'prone'].includes(msg.stance)) return;
        input = { type: 'place', side, position: { x: msg.position.x, y: 0, z: msg.position.z }, stance: msg.stance, time };
        break;
      case 'ready':
        input = { type: 'ready', side, time };
        break;
      case 'fire': {
        const f = msg.input;
        if (!f || ![f.yaw, f.pitch, f.dialElevationMrad, f.dialWindageMrad, f.cant ?? 0].every(Number.isFinite)) return;
        input = { type: 'fire', side, time, input: { yaw: f.yaw, pitch: f.pitch, dialElevationMrad: f.dialElevationMrad, dialWindageMrad: f.dialWindageMrad, cant: f.cant ?? 0 } };
        break;
      }
      case 'forfeit':
        input = { type: 'forfeit', side, time };
        break;
      default:
        return;
    }
    const error = this.apply(input);
    if (error) this.sendTo(client, { type: 'error', message: error });
  }

  private tick(): void {
    const m = this.match;
    if (!m || m.phase === 'matchOver') return;
    const now = this.now();
    if (m.phase === 'roundOver') {
      if (now - m.phaseStart >= config.rules.roundBreakSeconds) this.apply({ type: 'nextRound', time: now });
    } else if (now >= m.phaseEnd) {
      this.apply({ type: 'tick', time: now });
    }
  }

  /** Run one input through the reducer and publish the result. Returns the rejection, if any. */
  private apply(input: MatchInput): string | undefined {
    if (!this.match) return 'Match has not started';
    const r = applyInput(this.match, input);
    if (r.error) return r.error;
    const wasOver = this.match.phase === 'matchOver';
    this.match = r.state;
    this.publish(r.outcome);
    if (!wasOver && this.match.phase === 'matchOver') {
      // Leave the result up briefly, then close the room.
      this.clock.setTimeout(() => void this.disconnect(), 30_000);
    }
    return undefined;
  }

  private publish(outcome?: ShotOutcome): void {
    if (!this.match) return;
    const now = this.now();
    const wire = outcome ? thinOutcome(outcome) : undefined;
    for (const seat of this.seats.values()) {
      if (!seat.connected) continue;
      const opp = [...this.seats.values()].find((s) => s.side === other(seat.side));
      const view = encodeView(viewFor(this.match, seat.side, now, opp?.connected ?? false));
      this.sendTo(seat.client, { type: 'update', view, outcome: wire });
    }
  }

  private sendTo(client: Client, msg: ServerMessage): void {
    client.send(msg.type, msg);
  }

  /** For tests. */
  get state_(): MatchState | null {
    return this.match;
  }
}

function isVec(v: unknown): v is { x: number; z: number } {
  return typeof v === 'object' && v !== null && Number.isFinite((v as { x: number }).x) && Number.isFinite((v as { z: number }).z);
}
