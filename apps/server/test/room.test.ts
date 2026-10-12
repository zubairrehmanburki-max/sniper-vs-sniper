import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { Client, type Room } from '@colyseus/sdk';
import type { Server } from '@colyseus/core';
import { decodeView, ROOM_NAME, type MatchView, type ServerMessage } from '@svs/shared';
import { createServer } from '../src/server.ts';
import { DuelRoom } from '../src/duelRoom.ts';

const PORT = 25670 + Math.floor(Math.random() * 100);
let server: Server;

beforeAll(async () => {
  DuelRoom.seed = () => 1234;
  server = createServer();
  await server.listen(PORT);
});
afterAll(async () => {
  await server.gracefullyShutdown(false);
});

/** A test player: keeps the latest view and every message it got. */
class Player {
  room!: Room;
  view: MatchView | null = null;
  messages: ServerMessage[] = [];
  readonly client = new Client(`ws://localhost:${PORT}`);
  constructor(readonly name: string) {}

  attach(room: Room): this {
    this.room = room;
    for (const type of ['update', 'waiting', 'error'] as const) {
      room.onMessage(type, (m: ServerMessage) => {
        this.messages.push(m);
        if (m.type === 'update') this.view = decodeView(m.view);
      });
    }
    return this;
  }
  async quick(mapId?: string): Promise<this> {
    return this.attach(await this.client.joinOrCreate(ROOM_NAME, { name: this.name, rifleId: 'bolt308', code: '', mapId, playerId: `test-${this.name}-id` }));
  }
  send(msg: object): void {
    this.room.send('input', msg);
  }
  async until(pred: (v: MatchView) => boolean, ms = 3000): Promise<MatchView> {
    const end = Date.now() + ms;
    while (Date.now() < end) {
      if (this.view && pred(this.view)) return this.view;
      await new Promise((r) => setTimeout(r, 20));
    }
    throw new Error(`${this.name}: timed out, last view ${this.view?.phase}/${this.view?.actor}`);
  }
}

async function startMatch(code?: string, mapId = 'training-lane'): Promise<[Player, Player]> {
  const a = new Player('Alice');
  const b = new Player('Bob');
  if (code) {
    // Tests that walk a move phase need an open-ground map; the default map has fixed positions.
    a.attach(await a.client.create(ROOM_NAME, { name: 'Alice', rifleId: 'bolt308', code, mapId }));
    b.attach(await b.client.join(ROOM_NAME, { name: 'Bob', rifleId: 'mag338', code }));
  } else {
    await a.quick();
    await b.quick();
  }
  await a.until((v) => v.phase === 'setup');
  await b.until((v) => v.phase === 'setup');
  return [a, b];
}

describe('duel room', () => {
  it('pairs two quick-match players and hides what each may not see', async () => {
    const [a, b] = await startMatch();
    expect(a.view!.you).toBe('A');
    expect(b.view!.you).toBe('B');
    expect(a.view!.seed).toBe(0);
    expect(a.view!.enemySeen).toBe(false);
    expect(a.view!.players.B.position).toEqual({ x: 0, y: 0, z: 0 });
    a.room.leave();
    b.room.leave();
  });

  it('runs setup, a move and a shot, with the verdict sent to both players', async () => {
    const [a, b] = await startMatch('TEST');
    expect(b.view!.players.B.rifleId).toBe('mag338');
    expect(b.view!.ranked).toBe(false); // private games are unrated
    a.send({ type: 'place', position: { x: 10, z: -40 }, stance: 'standing' });
    a.send({ type: 'ready' });
    b.send({ type: 'ready' });
    await a.until((v) => v.phase === 'move' && v.actor === 'A');
    // Out-of-turn input is rejected, not applied.
    b.send({ type: 'ready' });
    await new Promise((r) => setTimeout(r, 100));
    expect(b.messages.some((m) => m.type === 'error')).toBe(true);
    a.send({ type: 'ready' });
    const shot = await b.until((v) => v.phase === 'shot' && v.actor === 'B');
    // Bob is looking through the scope at a standing enemy in the open: he sees her.
    expect(shot.enemySeen).toBe(true);
    expect(shot.players.A.position.x).toBeCloseTo(10, 0);
    b.send({ type: 'fire', input: { yaw: 0.05, pitch: 0.2, dialElevationMrad: 0, dialWindageMrad: 0 } });
    await a.until((v) => v.shots.length === 1);
    await b.until((v) => v.shots.length === 1);
    const outcomes = [a, b].map((p) => p.messages.find((m) => m.type === 'update' && m.outcome));
    expect(outcomes.every(Boolean)).toBe(true);
    // The killcam trajectory is thinned for the wire.
    const o = (outcomes[0] as Extract<ServerMessage, { type: 'update' }>).outcome!;
    expect(o.trajectory.points.length).toBeLessThan(2000);
    expect(o.verdict).toBe('miss');
    a.room.leave();
    b.room.leave();
  });

  it("holds a dropped player's seat and lets them back in", async () => {
    const [a, b] = await startMatch('DROP');
    const token = b.room.reconnectionToken;
    b.room.reconnection.enabled = false;
    b.room.connection.close(3000, 'network gone'); // not a consented leave
    await a.until((v) => !v.opponentConnected);
    const b2 = new Player('Bob');
    b2.attach(await b2.client.reconnect(token));
    await a.until((v) => v.opponentConnected);
    b2.send({ type: 'ready' });
    a.send({ type: 'ready' });
    await b2.until((v) => v.phase === 'move');
    expect(b2.view!.you).toBe('B');
    a.room.leave();
    b2.room.leave();
  });

  it("plays the host's map in a private game, and matches quick games by map", async () => {
    const a = new Player('Alice');
    const b = new Player('Bob');
    a.attach(await a.client.create(ROOM_NAME, { name: 'Alice', rifleId: 'bolt308', code: 'MAPS', mapId: 'training-lane' }));
    b.attach(await b.client.join(ROOM_NAME, { name: 'Bob', rifleId: 'bolt308', code: 'MAPS' }));
    expect((await b.until((v) => v.phase === 'setup')).mapId).toBe('training-lane');
    a.room.leave();
    b.room.leave();
    const c = new Player('Cat');
    const d = new Player('Dan');
    c.attach(await c.client.joinOrCreate(ROOM_NAME, { name: 'Cat', rifleId: 'bolt308', code: '', mapId: 'training-lane' }));
    d.attach(await d.client.joinOrCreate(ROOM_NAME, { name: 'Dan', rifleId: 'bolt308', code: '', mapId: 'dry-valley' }));
    await new Promise((r) => setTimeout(r, 150));
    expect(c.messages.some((m) => m.type === 'update')).toBe(false); // different maps: both still waiting
    c.room.leave();
    d.room.leave();
  });

  it('rates quick matches (a forfeit counts) and serves the new rating', async () => {
    const a = await new Player('Rita').quick('training-lane');
    const b = await new Player('Ravi').quick('training-lane');
    await a.until((v) => v.phase === 'setup');
    expect(a.view!.ranked).toBe(true);
    expect(a.view!.ratings).toEqual({ A: { before: 1200 }, B: { before: 1200 } });
    b.room.leave();
    const v = await a.until((x) => x.phase === 'matchOver');
    expect(v.ratings).toEqual({ A: { before: 1200, after: 1220 }, B: { before: 1200, after: 1180 } });
    const res = await fetch(`http://localhost:${PORT}/rating/test-Rita-id`);
    expect(await res.json()).toEqual({ rating: 1220, games: 1 });
    a.room.leave();
  });

  it('awards the match when a player quits', async () => {
    const [a, b] = await startMatch('QUIT');
    b.room.leave();
    const v = await a.until((x) => x.phase === 'matchOver');
    expect(v.winner).toBe('A');
    expect(v.forfeit).toBe(true);
    a.room.leave();
  });
});
