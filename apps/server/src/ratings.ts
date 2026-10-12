/**
 * Where ranked ratings live. A JSON file is enough for one small instance; the interface leaves room
 * for the Postgres store the plan calls for once there are accounts.
 */
import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { NEW_RATING, type Rating } from '@svs/shared';

export interface StoredRating extends Rating {
  name: string;
}

export interface RatingStore {
  get(playerId: string): StoredRating | undefined;
  set(playerId: string, rating: StoredRating): void;
}

export class MemoryRatingStore implements RatingStore {
  protected readonly map = new Map<string, StoredRating>();
  get(id: string): StoredRating | undefined {
    return this.map.get(id);
  }
  set(id: string, r: StoredRating): void {
    this.map.set(id, r);
  }
}

/** Keeps every rating in memory and rewrites the file after each change (atomic rename). */
export class FileRatingStore extends MemoryRatingStore {
  constructor(private readonly path: string) {
    super();
    if (existsSync(path)) {
      const data = JSON.parse(readFileSync(path, 'utf8')) as Record<string, StoredRating>;
      for (const [id, r] of Object.entries(data)) this.map.set(id, r);
    }
  }
  override set(id: string, r: StoredRating): void {
    super.set(id, r);
    const tmp = `${this.path}.tmp`;
    writeFileSync(tmp, JSON.stringify(Object.fromEntries(this.map), null, 1));
    renameSync(tmp, this.path);
  }
}

export function ratingOf(store: RatingStore, id: string | undefined): Rating {
  const r = id ? store.get(id) : undefined;
  return r ? { rating: r.rating, games: r.games } : { ...NEW_RATING };
}

/** Player ids are client-made random tokens until there are accounts; reject anything else. */
export const validPlayerId = (id: unknown): id is string => typeof id === 'string' && /^[A-Za-z0-9_-]{8,64}$/.test(id);
