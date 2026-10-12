/**
 * Small deterministic PRNG utilities. The solver never touches Math.random or the clock:
 * every random draw comes from a match seed so the server verdict and the client killcam agree.
 */

export interface Rng {
  /** Uniform float in [0, 1). */
  next(): number;
  /** Standard normal draw (mean 0, sd 1). */
  normal(): number;
}

/** Mixes any number of integers into one 32-bit seed (murmur3 finalizer). */
export function hashSeed(...parts: number[]): number {
  let h = 0x9e3779b9;
  for (const p of parts) {
    let k = Math.imul(p | 0, 0xcc9e2d51);
    k = (k << 15) | (k >>> 17);
    k = Math.imul(k, 0x1b873593);
    h ^= k;
    h = (h << 13) | (h >>> 19);
    h = (Math.imul(h, 5) + 0xe6546b64) | 0;
  }
  h ^= h >>> 16;
  h = Math.imul(h, 0x85ebca6b);
  h ^= h >>> 13;
  h = Math.imul(h, 0xc2b2ae35);
  h ^= h >>> 16;
  return h >>> 0;
}

/** mulberry32: fast, well-distributed 32-bit generator. */
export function createRng(seed: number): Rng {
  let a = seed >>> 0;
  let spare: number | null = null;
  const next = (): number => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  const normal = (): number => {
    if (spare !== null) {
      const s = spare;
      spare = null;
      return s;
    }
    let u = 0;
    while (u === 0) u = next();
    const v = next();
    const r = Math.sqrt(-2 * Math.log(u));
    spare = r * Math.sin(2 * Math.PI * v);
    return r * Math.cos(2 * Math.PI * v);
  };
  return { next, normal };
}

/** Hash-based value in [-1, 1] for integer lattice point i (used by smooth noise). */
export function latticeValue(seed: number, i: number): number {
  return (hashSeed(seed, i) / 4294967295) * 2 - 1;
}

/** Smooth 1-D value noise in [-1, 1], continuous with continuous first derivative. */
export function valueNoise1D(seed: number, t: number): number {
  const i = Math.floor(t);
  const f = t - i;
  const u = f * f * f * (f * (f * 6 - 15) + 10); // quintic fade
  const a = latticeValue(seed, i);
  const b = latticeValue(seed, i + 1);
  return a + (b - a) * u;
}
