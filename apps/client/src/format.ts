/** Small pure helpers for the HUD, kept DOM-free so they can be unit tested. */
import type { Vec3 } from '@svs/ballistics';
import { forward, rightOf } from '@svs/shared';

/** Clamp a turret value to its travel and snap it to whole clicks. */
export function clickTurret(valueMrad: number, clicks: number, clickMrad: number, maxMrad: number, min = -maxMrad): number {
  const v = Math.round(valueMrad / clickMrad + clicks) * clickMrad;
  return Math.min(maxMrad, Math.max(min, Number(v.toFixed(4))));
}

/** Pixels per milliradian on screen for a vertical field of view (first focal plane reticle). */
export function pxPerMrad(viewportHeightPx: number, verticalFovDeg: number): number {
  const half = (verticalFovDeg * Math.PI) / 360;
  return viewportHeightPx / 2 / Math.tan(half) / 1000;
}

/** Vertical FOV in degrees for a scope magnification. */
export function scopeFov(zoom: number, fovAt1xDeg: number): number {
  return fovAt1xDeg / zoom;
}

/**
 * Where wind comes FROM as a clock position relative to the shooter's facing (12 = headwind,
 * 3 = from the right, 9 = from the left), plus its full speed.
 */
export function windClock(windWorld: Vec3, yaw: number): { clock: number; speed: number } {
  const f = forward(yaw);
  const r = rightOf(yaw);
  const along = windWorld.x * f.x + windWorld.z * f.z; // + = blowing downrange (tailwind)
  const across = windWorld.x * r.x + windWorld.z * r.z; // + = blowing toward the right
  const speed = Math.hypot(along, across);
  // Direction the wind comes from, measured clockwise from straight ahead.
  const from = Math.atan2(-across, -along);
  let clock = Math.round(((from / (2 * Math.PI)) * 12 + 12) % 12);
  if (clock === 0) clock = 12;
  return { clock, speed };
}

export function formatMil(mrad: number, pos: string, neg: string): string {
  if (Math.abs(mrad) < 0.05) return '0.0';
  return `${Math.abs(mrad).toFixed(1)} ${mrad > 0 ? pos : neg}`;
}

export function spotterCall(upMrad: number, rightMrad: number): string {
  const parts: string[] = [];
  if (Math.abs(upMrad) >= 0.1) parts.push(`${Math.abs(upMrad).toFixed(1)} ${upMrad > 0 ? 'high' : 'low'}`);
  if (Math.abs(rightMrad) >= 0.1) parts.push(`${Math.abs(rightMrad).toFixed(1)} ${rightMrad > 0 ? 'right' : 'left'}`);
  return parts.length ? `${parts.join(', ')} (mil)` : 'just missed the edge';
}
