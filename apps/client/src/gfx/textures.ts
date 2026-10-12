/**
 * Procedural textures: everything is drawn on a canvas at start-up, so the game ships no image
 * files and every map still gets grain, concrete, asphalt, rust and cloud detail.
 */
import * as THREE from 'three';

function hash(ix: number, iy: number, seed: number): number {
  let h = (ix * 374761393 + iy * 668265263 + seed * 1442695041) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** Tileable value noise in [0, 1] over a period of `period` cells. */
function noise(x: number, y: number, period: number, seed: number): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const sx = fx * fx * (3 - 2 * fx);
  const sy = fy * fy * (3 - 2 * fy);
  const w = (a: number, b: number): number => hash(((a % period) + period) % period, ((b % period) + period) % period, seed);
  const n0 = w(ix, iy) + (w(ix + 1, iy) - w(ix, iy)) * sx;
  const n1 = w(ix, iy + 1) + (w(ix + 1, iy + 1) - w(ix, iy + 1)) * sx;
  return n0 + (n1 - n0) * sy;
}

/** Tileable fractal noise in [0, 1]. */
export function fbm(u: number, v: number, seed: number, octaves = 5, base = 4): number {
  let sum = 0;
  let amp = 0.5;
  let total = 0;
  let period = base;
  for (let o = 0; o < octaves; o++) {
    sum += amp * noise(u * period, v * period, period, seed + o * 17);
    total += amp;
    amp *= 0.5;
    period *= 2;
  }
  return sum / total;
}

function canvasTexture(size: number, paint: (g: CanvasRenderingContext2D, img: ImageData) => void, srgb = true): THREE.CanvasTexture {
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d')!;
  const img = g.createImageData(size, size);
  paint(g, img);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = srgb ? THREE.SRGBColorSpace : THREE.NoColorSpace;
  tex.anisotropy = 4;
  return tex;
}

/** Fill the pixel buffer from a per-pixel shade function returning [r, g, b] in 0–255. */
function shade(g: CanvasRenderingContext2D, img: ImageData, f: (u: number, v: number) => [number, number, number]): void {
  const size = img.width;
  const d = img.data;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const [r, gg, b] = f(x / size, y / size);
      const i = (y * size + x) * 4;
      d[i] = r;
      d[i + 1] = gg;
      d[i + 2] = b;
      d[i + 3] = 255;
    }
  }
  g.putImageData(img, 0, 0);
}

const clamp255 = (v: number): number => Math.max(0, Math.min(255, Math.round(v)));

/** Neutral grain that multiplies vertex colours: keeps the ground from looking like plastic. */
export function grainTexture(seed = 1): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const n = fbm(u, v, seed, 5, 6);
      const k = clamp255(170 + 85 * n);
      return [k, k, k];
    }),
  );
}

export function concreteTexture(seed = 2): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const n = fbm(u, v, seed, 5, 8);
      const streak = 0.06 * Math.sin(v * 60 + fbm(u, v, seed + 9, 2, 3) * 6);
      const speck = hash(Math.floor(u * 256), Math.floor(v * 256), seed) > 0.985 ? -40 : 0;
      const k = 150 + 50 * n + streak * 255 + speck;
      return [clamp255(k), clamp255(k - 2), clamp255(k - 6)];
    }),
  );
}

/** Asphalt with a dashed centre line and solid edge lines, tiled along u. */
export function roadTexture(seed = 3): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const n = fbm(u, v, seed, 4, 12);
      let k = 52 + 28 * n;
      const centre = Math.abs(v - 0.5) < 0.012 && u % 0.5 < 0.3;
      const edge = Math.abs(v - 0.06) < 0.01 || Math.abs(v - 0.94) < 0.01;
      if (centre || edge) k = 215 + 25 * n;
      return [clamp255(k), clamp255(k), clamp255(k - 4)];
    }),
  );
}

export function rustTexture(seed = 4): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const n = fbm(u, v, seed, 5, 5);
      const rust = Math.max(0, n - 0.55) * 2.2;
      const r = 140 + 60 * n - rust * 20;
      return [clamp255(r), clamp255(r * (0.9 - rust * 0.45)), clamp255(r * (0.85 - rust * 0.6))];
    }),
  );
}

export function steelTexture(seed = 5): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const n = fbm(u * 0.2, v, seed, 4, 16); // brushed: stretched along u
      const k = 120 + 70 * n;
      return [clamp255(k), clamp255(k + 3), clamp255(k + 8)];
    }),
  );
}

export function woodTexture(seed = 6): THREE.Texture {
  return canvasTexture(256, (g, img) =>
    shade(g, img, (u, v) => {
      const ring = Math.sin((v + fbm(u, v, seed, 3, 4) * 0.4) * 40);
      const n = fbm(u, v, seed + 1, 4, 6);
      const k = 95 + 45 * n + 18 * ring;
      return [clamp255(k * 1.1), clamp255(k * 0.78), clamp255(k * 0.5)];
    }),
  );
}

/** Soft cumulus blob with alpha, for sky billboards. */
export function cloudTexture(seed = 7): THREE.Texture {
  const size = 256;
  const c = document.createElement('canvas');
  c.width = c.height = size;
  const g = c.getContext('2d')!;
  const img = g.createImageData(size, size);
  const d = img.data;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const u = x / size - 0.5;
      const v = y / size - 0.5;
      const r = Math.hypot(u * 1.1, v * 1.8);
      const n = fbm(x / size, y / size, seed, 5, 3);
      const a = Math.max(0, Math.min(1, (0.55 - r) * 3 + (n - 0.5) * 1.4));
      const i = (y * size + x) * 4;
      const shadeK = 225 + 30 * n;
      d[i] = shadeK;
      d[i + 1] = shadeK;
      d[i + 2] = Math.min(255, shadeK + 8);
      d[i + 3] = Math.round(a * a * 255);
    }
  }
  g.putImageData(img, 0, 0);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function softDotTexture(): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d')!;
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.4, 'rgba(255,255,255,0.6)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

export function signTexture(text: string): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = 256;
  c.height = 128;
  const g = c.getContext('2d')!;
  g.fillStyle = '#f4f1e6';
  g.fillRect(0, 0, 256, 128);
  g.strokeStyle = '#222';
  g.lineWidth = 8;
  g.strokeRect(4, 4, 248, 120);
  g.fillStyle = '#222';
  g.font = 'bold 72px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(text, 128, 68);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** A helipad "H" in a circle, white on dark green. */
export function helipadTexture(): THREE.Texture {
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const g = c.getContext('2d')!;
  g.fillStyle = '#2f4a3a';
  g.fillRect(0, 0, 256, 256);
  g.strokeStyle = '#f0ede4';
  g.lineWidth = 10;
  g.beginPath();
  g.arc(128, 128, 110, 0, Math.PI * 2);
  g.stroke();
  g.fillStyle = '#f0ede4';
  g.font = 'bold 150px system-ui, sans-serif';
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText('H', 128, 134);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}
