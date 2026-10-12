import { Server } from '@colyseus/core';
import { WebSocketTransport } from '@colyseus/ws-transport';
import { ROOM_NAME } from '@svs/shared';
import { DuelRoom } from './duelRoom.ts';

/** Build the game server: one room type, matched by private code ('' = quick match). */
export function createServer(): Server {
  const server = new Server({ transport: new WebSocketTransport({ pingInterval: 5000, pingMaxRetries: 3 }) });
  server.define(ROOM_NAME, DuelRoom).filterBy(['code']);
  return server;
}
