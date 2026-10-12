import express from 'express';
import { Server } from '@colyseus/core';
import { WebSocketTransport } from '@colyseus/ws-transport';
import { ROOM_NAME } from '@svs/shared';
import { DuelRoom } from './duelRoom.ts';

/**
 * Build the game server: one room type, matched by private code ('' = quick match).
 * With `clientDir` it also serves the built browser client, so one process hosts the whole game.
 */
export function createServer(opts: { clientDir?: string } = {}): Server {
  const server = new Server({
    transport: new WebSocketTransport({ pingInterval: 5000, pingMaxRetries: 3 }),
    express: (app) => {
      app.get('/health', (_req, res) => {
        res.json({ ok: true });
      });
      if (opts.clientDir) app.use(express.static(opts.clientDir));
    },
  });
  server.define(ROOM_NAME, DuelRoom).filterBy(['code', 'mapId']);
  return server;
}
