/**
 * Connection to the game server. The server is authoritative: the client sends intents and renders
 * the filtered views it gets back. A dropped socket is retried by the SDK; a reloaded page resumes
 * from the reconnection token kept in sessionStorage.
 */
import { Client, type Room } from '@colyseus/sdk';
import { decodeView, ROOM_NAME, type ClientMessage, type JoinOptions, type MatchView, type ServerMessage, type ShotOutcome } from '@svs/shared';

const TOKEN_KEY = 'svs.reconnect';
const PLAYER_KEY = 'svs.playerId';

/** This browser's stable player id (no accounts yet). Ratings follow it. */
export function playerId(): string | undefined {
  try {
    let id = localStorage.getItem(PLAYER_KEY);
    if (!id) {
      id = crypto.randomUUID().replace(/-/g, '');
      localStorage.setItem(PLAYER_KEY, id);
    }
    return id;
  } catch {
    return undefined; // storage blocked: play unrated
  }
}

/** Ranked rating for this browser, or null when the server can't be reached. */
export async function fetchRating(): Promise<{ rating: number; games: number } | null> {
  const id = playerId();
  if (!id) return null;
  try {
    const res = await fetch(`${serverUrl().replace(/^ws/, 'http')}/rating/${id}`);
    return res.ok ? ((await res.json()) as { rating: number; games: number }) : null;
  } catch {
    return null;
  }
}

export function serverUrl(): string {
  const param = new URLSearchParams(location.search).get('server');
  if (param) return param;
  const env = import.meta.env.VITE_SERVER_URL as string | undefined;
  // A build served by the game server itself (the hosted deploy) talks to its own origin.
  if (env === 'same-origin') return `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}`;
  if (env) return env;
  return `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.hostname || 'localhost'}:2567`;
}

export interface LinkHandlers {
  update(view: MatchView, outcome?: ShotOutcome): void;
  waiting?(code?: string): void;
  error?(message: string): void;
  /** Connection state: 'dropped' while the SDK retries, 'back' when it reconnects, 'lost' when it gives up. */
  connection?(state: 'dropped' | 'back' | 'lost'): void;
}

export class OnlineLink {
  handlers: LinkHandlers | null = null;
  private closing = false;

  private constructor(readonly room: Room) {
    for (const type of ['update', 'waiting', 'error'] as const) {
      room.onMessage(type, (m: ServerMessage) => this.dispatch(m));
    }
    room.onDrop(() => this.handlers?.connection?.('dropped'));
    room.onReconnect(() => {
      this.saveToken();
      this.handlers?.connection?.('back');
    });
    room.onLeave(() => {
      forgetToken();
      if (!this.closing) this.handlers?.connection?.('lost');
    });
    this.saveToken();
  }

  /** Last message seen before any handler was attached (the first view arrives quickly). */
  pending: ServerMessage[] = [];

  private dispatch(m: ServerMessage): void {
    const h = this.handlers;
    if (!h) {
      this.pending.push(m);
      return;
    }
    if (m.type === 'update') h.update(decodeView(m.view), m.outcome);
    else if (m.type === 'waiting') h.waiting?.(m.code);
    else h.error?.(m.message);
  }

  listen(handlers: LinkHandlers): void {
    this.handlers = handlers;
    const queued = this.pending;
    this.pending = [];
    for (const m of queued) this.dispatch(m);
  }

  send(msg: ClientMessage): void {
    this.room.send('input', msg);
  }

  leave(): void {
    this.closing = true;
    forgetToken();
    void this.room.leave(true);
  }

  private saveToken(): void {
    try {
      sessionStorage.setItem(TOKEN_KEY, this.room.reconnectionToken);
    } catch {
      /* storage blocked: reloading the page will forfeit instead of resuming */
    }
  }

  static async quick(opts: JoinOptions): Promise<OnlineLink> {
    return new OnlineLink(await new Client(serverUrl()).joinOrCreate(ROOM_NAME, { ...opts, code: '' }));
  }

  static async create(opts: JoinOptions & { code: string }): Promise<OnlineLink> {
    return new OnlineLink(await new Client(serverUrl()).create(ROOM_NAME, opts));
  }

  static async join(opts: JoinOptions & { code: string }): Promise<OnlineLink> {
    return new OnlineLink(await new Client(serverUrl()).join(ROOM_NAME, opts));
  }

  /** Resume a match after a page reload, if this tab was in one. */
  static async resume(): Promise<OnlineLink | null> {
    let token: string | null = null;
    try {
      token = sessionStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
    if (!token) return null;
    try {
      return new OnlineLink(await new Client(serverUrl()).reconnect(token));
    } catch {
      forgetToken();
      return null;
    }
  }
}

function forgetToken(): void {
  try {
    sessionStorage.removeItem(TOKEN_KEY);
  } catch {
    /* ignore */
  }
}
