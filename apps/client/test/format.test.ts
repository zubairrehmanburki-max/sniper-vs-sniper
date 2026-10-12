import { describe, expect, it } from 'vitest';
import { clickTurret, pxPerMrad, scopeFov, spotterCall, windClock } from '../src/format.ts';

describe('HUD helpers', () => {
  it('turrets move in whole 0.1 mrad clicks and stop at their travel', () => {
    expect(clickTurret(0, 3, 0.1, 30)).toBeCloseTo(0.3, 9);
    expect(clickTurret(0.3, -5, 0.1, 30)).toBeCloseTo(-0.2, 9);
    expect(clickTurret(29.95, 10, 0.1, 30)).toBe(30);
    expect(clickTurret(0, -400, 0.1, 30, -2)).toBe(-2);
  });
  it('reticle subtension is true at any zoom (first focal plane)', () => {
    // 1 mrad at 1000 m is 1 m; a 10° FOV on 1000 px shows ~174.6 mrad.
    const px = pxPerMrad(1000, 10);
    expect(1000 / px).toBeCloseTo(2 * Math.tan((5 * Math.PI) / 180) * 1000, 6);
    expect(pxPerMrad(1000, scopeFov(20, 30))).toBeCloseTo(2 * pxPerMrad(1000, scopeFov(10, 30)), 1);
  });
  it('reads wind as a clock direction', () => {
    expect(windClock({ x: 4, y: 0, z: 0 }, 0)).toEqual({ clock: 9, speed: 4 }); // blowing right = from the left
    expect(windClock({ x: -4, y: 0, z: 0 }, 0).clock).toBe(3);
    expect(windClock({ x: 0, y: 0, z: 4 }, 0).clock).toBe(12); // blowing toward the shooter
    expect(windClock({ x: 0, y: 0, z: -4 }, 0).clock).toBe(6);
  });
  it('spotter calls read naturally', () => {
    expect(spotterCall(-0.62, 0.31)).toBe('0.6 low, 0.3 right (mil)');
  });
});
