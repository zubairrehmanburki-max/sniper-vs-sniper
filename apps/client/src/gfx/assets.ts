/**
 * The art the renderer ships: photographic HDRI skies, PBR textures and a rigged soldier. Every
 * file is fetched once at start-up (`preloadAssets`) so the world builders stay synchronous.
 *
 * Files are imported through Vite, which emits them beside the bundle or, when the build is told
 * to inline them (the single-file playable), bakes them into the script as data: URLs. Both paths
 * go through `bytes()`, which never fetches a data: URL (the playable's sandbox forbids it).
 */
import * as THREE from 'three';
import { GLTFLoader, type GLTF } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { RGBELoader } from 'three/examples/jsm/loaders/RGBELoader.js';
import skySunrise from '../assets/blouberg_sunrise_2_1k.hdr?url';
import skyClear from '../assets/quarry_01_1k.hdr?url';
import skySunset from '../assets/venice_sunset_1k.hdr?url';
import soldierUrl from '../assets/Soldier.glb?url';
import grassUrl from '../assets/grass.jpg?url';
import grassNormalUrl from '../assets/grass_n.jpg?url';
import concreteUrl from '../assets/concrete.jpg?url';
import concreteNormalUrl from '../assets/concrete_n.jpg?url';
import asphaltUrl from '../assets/asphalt.jpg?url';
import asphaltNormalUrl from '../assets/asphalt_n.jpg?url';
import rustUrl from '../assets/rust.jpg?url';
import waterNormalsUrl from '../assets/waternormals.jpg?url';
import treesUrl from '../assets/trees.png?url';
import smokeUrl from '../assets/smoke.png?url';

export type SkyName = 'sunrise' | 'clear' | 'sunset';
const SKIES: Record<SkyName, string> = { sunrise: skySunrise, clear: skyClear, sunset: skySunset };

const TEXTURES = {
  grass: grassUrl,
  grassNormal: grassNormalUrl,
  concrete: concreteUrl,
  concreteNormal: concreteNormalUrl,
  asphalt: asphaltUrl,
  asphaltNormal: asphaltNormalUrl,
  rust: rustUrl,
  waterNormals: waterNormalsUrl,
  trees: treesUrl,
  smoke: smokeUrl,
} as const;
export type TextureName = keyof typeof TEXTURES;
/** Colour textures are sRGB; normal maps and the water normals are linear data. */
const LINEAR: ReadonlySet<TextureName> = new Set<TextureName>(['grassNormal', 'concreteNormal', 'asphaltNormal', 'waterNormals']);

/** Raw bytes of an asset URL, decoding data: URLs locally. */
export async function bytes(url: string): Promise<ArrayBuffer> {
  if (url.startsWith('data:')) {
    const comma = url.indexOf(',');
    const meta = url.slice(5, comma);
    const payload = url.slice(comma + 1);
    if (meta.endsWith(';base64')) {
      const bin = atob(payload);
      const out = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
      return out.buffer;
    }
    return new TextEncoder().encode(decodeURIComponent(payload)).buffer as ArrayBuffer;
  }
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Failed to load ${url}: ${res.status}`);
  return res.arrayBuffer();
}

export interface HdrImage {
  texture: THREE.DataTexture;
  /** Direction of the brightest spot (the sun), in the texture's own frame. */
  sunDir: THREE.Vector3;
  /** Hue of the sun and the average colour of the band just above the horizon. */
  sunColor: THREE.Color;
  horizonColor: THREE.Color;
}

async function loadHdr(url: string): Promise<HdrImage> {
  const loader = new RGBELoader();
  loader.type = THREE.HalfFloatType;
  const img = loader.parse(await bytes(url)) as { width: number; height: number; data: Uint16Array };
  const texture = new THREE.DataTexture(img.data, img.width, img.height, THREE.RGBAFormat, THREE.HalfFloatType);
  texture.colorSpace = THREE.LinearSRGBColorSpace;
  texture.minFilter = THREE.LinearFilter;
  texture.magFilter = THREE.LinearFilter;
  texture.generateMipmaps = false;
  texture.flipY = true;
  texture.mapping = THREE.EquirectangularReflectionMapping;
  texture.needsUpdate = true;

  // Find the sun: the brightest 4x4 block in the upper half. Row 0 is the zenith (the file's
  // first row is the top of the sky; flipY puts it at v = 1).
  const { width: w, height: h, data } = img;
  const px = (x: number, y: number): [number, number, number] => {
    const i = (y * w + x) * 4;
    return [THREE.DataUtils.fromHalfFloat(data[i]!), THREE.DataUtils.fromHalfFloat(data[i + 1]!), THREE.DataUtils.fromHalfFloat(data[i + 2]!)];
  };
  let best = -1;
  let bx = 0;
  let by = 0;
  for (let y = 0; y < h / 2; y += 4) {
    for (let x = 0; x < w; x += 4) {
      let s = 0;
      for (let dy = 0; dy < 4; dy++) {
        for (let dx = 0; dx < 4; dx++) {
          const [r, g, b] = px(Math.min(w - 1, x + dx), Math.min(h - 1, y + dy));
          s += r + g + b;
        }
      }
      if (s > best) {
        best = s;
        bx = x + 2;
        by = y + 2;
      }
    }
  }
  const [sr, sg, sb] = px(bx, by);
  const sm = Math.max(sr, sg, sb, 1e-3);
  const sunColor = new THREE.Color(sr / sm, sg / sm, sb / sm);
  // three's equirect lookup: u = atan2(dir.z, dir.x) / 2π + 0.5, v = asin(dir.y) / π + 0.5.
  const phi = (by / h) * Math.PI; // polar angle from the zenith
  const lon = (bx / w - 0.5) * Math.PI * 2;
  const sunDir = new THREE.Vector3(Math.cos(lon) * Math.sin(phi), Math.cos(phi), Math.sin(lon) * Math.sin(phi));
  // Haze out the horizon band and everything below it: the photo's own ground, buildings and
  // trees would otherwise poke up behind the map's hills. Rows near the horizon fade into their
  // own average (of the brighter pixels, so a dark building does not tint the sky); below the
  // horizon a plain ground tone remains for the reflections. The sun's disc itself is kept.
  const fadeTop = Math.floor(h * (70 / 180));
  const bandTop = Math.floor(h * (77 / 180));
  const horizonRow = Math.floor(h / 2);
  const rowAvg = (y: number): [number, number, number] => {
    const lum: number[] = [];
    for (let x = 0; x < w; x += 2) {
      const [r, g, b] = px(x, y);
      lum.push(Math.min(r, 6) + Math.min(g, 6) + Math.min(b, 6));
    }
    const cut = [...lum].sort((a, b) => a - b)[Math.floor(lum.length * 0.6)]! * 0.85;
    let r = 0;
    let g = 0;
    let b = 0;
    let n = 0;
    for (let x = 0, k = 0; x < w; x += 2, k++) {
      if (lum[k]! < cut) continue;
      const [pr, pg, pb] = px(x, y);
      r += Math.min(pr, 6);
      g += Math.min(pg, 6);
      b += Math.min(pb, 6);
      n++;
    }
    return [r / n, g / n, b / n];
  };
  const avgs = new Map<number, [number, number, number]>();
  for (let y = fadeTop; y <= horizonRow; y++) avgs.set(y, rowAvg(y));
  const haze = avgs.get(Math.floor(h * (86 / 180)))!;
  const ground: [number, number, number] = [haze[0] * 0.45, haze[1] * 0.45, haze[2] * 0.45];
  const toHalf = THREE.DataUtils.toHalfFloat;
  // Dark objects standing well above the haze band (a tall building, a tree at the edge of the
  // photo) are painted out with the row's own sky.
  for (let y = Math.floor(h * (50 / 180)); y < fadeTop; y++) {
    const t = rowAvg(y);
    const tl = t[0] + t[1] + t[2];
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      const [r, g, b] = px(x, y);
      const q = (r + g + b) / tl;
      if (q < 0.35) {
        const kk = Math.min(1, (0.35 - q) / 0.15);
        data[i] = toHalf(r + (t[0] - r) * kk);
        data[i + 1] = toHalf(g + (t[1] - g) * kk);
        data[i + 2] = toHalf(b + (t[2] - b) * kk);
      }
    }
  }
  for (let y = fadeTop; y < h; y++) {
    const k = y >= bandTop ? 1 : (y - fadeTop) / (bandTop - fadeTop);
    const kk = k * k * (3 - 2 * k);
    const t = y >= horizonRow ? ground : avgs.get(y)!;
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      const [r, g, b] = px(x, y);
      if (r + g + b > 60) continue; // the sun
      data[i] = toHalf(r + (t[0] - r) * kk);
      data[i + 1] = toHalf(g + (t[1] - g) * kk);
      data[i + 2] = toHalf(b + (t[2] - b) * kk);
    }
  }
  // Tame the glow around the sun (the disc stays): looking its way should dazzle, not white out.
  for (let i = 0; i < data.length; i += 4) {
    const r = THREE.DataUtils.fromHalfFloat(data[i]!);
    const g = THREE.DataUtils.fromHalfFloat(data[i + 1]!);
    const b = THREE.DataUtils.fromHalfFloat(data[i + 2]!);
    const m = Math.max(r, g, b);
    if (m > 10 && r + g + b <= 60) {
      const k = 10 / m;
      data[i] = toHalf(r * k);
      data[i + 1] = toHalf(g * k);
      data[i + 2] = toHalf(b * k);
    }
  }
  const horizonColor = new THREE.Color(...haze).multiplyScalar(0.9 / Math.max(...haze, 1e-3));
  texture.needsUpdate = true;
  return { texture, sunDir, sunColor, horizonColor };
}

async function loadTexture(url: string, linear: boolean): Promise<THREE.Texture> {
  const buf = await bytes(url);
  const bitmap = await createImageBitmap(new Blob([buf]), { imageOrientation: 'flipY', premultiplyAlpha: 'none', colorSpaceConversion: 'none' });
  const tex = new THREE.Texture(bitmap);
  tex.flipY = false;
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = linear ? THREE.NoColorSpace : THREE.SRGBColorSpace;
  tex.anisotropy = 8;
  tex.needsUpdate = true;
  return tex;
}

async function loadGltf(url: string): Promise<GLTF> {
  const buf = await bytes(url);
  return new Promise((resolve, reject) => new GLTFLoader().parse(buf, '', resolve, reject));
}

export interface Assets {
  sky(name: SkyName): HdrImage;
  texture(name: TextureName): THREE.Texture;
  soldier: GLTF;
}

let loaded: Assets | null = null;
let loading: Promise<Assets> | null = null;

/** Fetch and decode everything, once. Safe to call again: returns the same promise. */
export function preloadAssets(onProgress?: (done: number, total: number) => void): Promise<Assets> {
  if (loading) return loading;
  const total = Object.keys(SKIES).length + Object.keys(TEXTURES).length + 1;
  let done = 0;
  const tick = <T>(p: Promise<T>): Promise<T> =>
    p.then((v) => {
      onProgress?.(++done, total);
      return v;
    });
  loading = (async () => {
    const skyEntries = await Promise.all((Object.keys(SKIES) as SkyName[]).map(async (k) => [k, await tick(loadHdr(SKIES[k]))] as const));
    const texEntries = await Promise.all((Object.keys(TEXTURES) as TextureName[]).map(async (k) => [k, await tick(loadTexture(TEXTURES[k], LINEAR.has(k)))] as const));
    const soldier = await tick(loadGltf(soldierUrl));
    const skies = Object.fromEntries(skyEntries) as Record<SkyName, HdrImage>;
    const textures = Object.fromEntries(texEntries) as Record<TextureName, THREE.Texture>;
    loaded = { sky: (n) => skies[n], texture: (n) => textures[n], soldier };
    return loaded;
  })();
  return loading;
}

/** The loaded assets; throws if `preloadAssets` has not finished. */
export function assets(): Assets {
  if (!loaded) throw new Error('Assets are not loaded yet');
  return loaded;
}
