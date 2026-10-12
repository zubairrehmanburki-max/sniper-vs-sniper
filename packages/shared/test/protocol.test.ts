import { describe, expect, it } from 'vitest';
import { applyInput, createMatch, decodeView, encodeView, makeCode, resolveShot, thinOutcome, viewFor, type MatchState } from '../src/index.ts';
import { mapById } from '../src/maps.ts';

function match(): MatchState {
  return createMatch({ seed: 99, mapId: 'training-lane', players: { A: { name: 'A', rifleId: 'bolt308' }, B: { name: 'B', rifleId: 'bolt308' } }, time: 0 });
}

/** Both ready, A moves (ready), so B is in its shot phase. */
function bShooting(posA = { x: 0, y: 0, z: -40 }, stanceA: 'standing' | 'prone' = 'standing'): MatchState {
  let s = match();
  s = applyInput(s, { type: 'place', side: 'A', position: posA, stance: stanceA, time: 1 }).state;
  s = applyInput(s, { type: 'ready', side: 'A', time: 1 }).state;
  s = applyInput(s, { type: 'ready', side: 'B', time: 1 }).state;
  s = applyInput(s, { type: 'ready', side: 'A', time: 2 }).state;
  expect([s.phase, s.actor]).toEqual(['shot', 'B']);
  return s;
}

describe('player views', () => {
  it('never include the seed, and hide the enemy outside your shot phase', () => {
    const s = match();
    const v = viewFor(s, 'A', 0);
    expect(v.seed).toBe(0);
    expect(v.enemySeen).toBe(false);
    expect(v.players.B.position).toEqual({ x: 0, y: 0, z: 0 });
    expect(v.players.A.position).toEqual(s.players.A.position);
  });

  it('show the enemy to the shooter only with line of sight', () => {
    const s = bShooting();
    expect(viewFor(s, 'B', 2).enemySeen).toBe(true);
    expect(viewFor(s, 'B', 2).players.A.stance).toBe('standing');
    // The frozen player learns nothing about the shooter.
    expect(viewFor(s, 'A', 2).enemySeen).toBe(false);
    // Prone right behind a rock: hidden.
    const rock = mapById('training-lane').cover.find((c) => -c.min.z < 80)!;
    const behind = { x: (rock.min.x + rock.max.x) / 2, y: 0, z: rock.min.z + 2 };
    expect(viewFor(bShooting(behind, 'prone'), 'B', 2).enemySeen).toBe(false);
  });

  it('keep a revealed firing position visible', () => {
    let s = bShooting();
    s = applyInput(s, { type: 'fire', side: 'B', time: 3, input: { yaw: 0.3, pitch: 0, dialElevationMrad: 0, dialWindageMrad: 0 } }).state;
    const v = viewFor(s, 'A', 3);
    expect(v.enemySeen).toBe(false);
    expect(v.players.B.position).toEqual(s.players.B.revealed);
  });

  it('survive JSON (no Infinity) and thin the trajectory', () => {
    const s = { ...match(), phase: 'roundOver' as const, phaseEnd: Infinity };
    const wire = JSON.parse(JSON.stringify(encodeView(viewFor(s, 'A', 5))));
    expect(decodeView(wire).phaseEnd).toBe(Infinity);
    const map = mapById('training-lane');
    const o = resolveShot({
      map,
      conditions: s.conditions,
      rifleId: 'bolt308',
      shooter: { position: map.spots.A[0]!, stance: 'prone' },
      target: { id: 'B', side: 'B', position: map.spots.B[0]!, stance: 'prone', wounds: 0 },
      input: { yaw: 0, pitch: 0.003, dialElevationMrad: 3, dialWindageMrad: 0 },
      seed: 1,
      shotIndex: 1,
      fireTime: 0,
      coldBore: false,
    });
    const thin = thinOutcome(o);
    expect(thin.trajectory.points.length).toBeLessThan(o.trajectory.points.length / 4);
    expect(thin.trajectory.points.at(-1)).toEqual(o.trajectory.points.at(-1));
    expect(thin.seed).toBe(0);
  });

  it('makes readable codes', () => {
    expect(makeCode()).toMatch(/^[A-HJ-NP-Z]{4}$/);
  });
});

describe('elo', () => {
  it('rewards upsets more than expected wins, and settles after ten games', async () => {
    const { rateMatch, expectedScore, NEW_RATING } = await import('../src/elo.ts');
    expect(expectedScore(1200, 1200)).toBeCloseTo(0.5);
    const even = rateMatch(NEW_RATING, NEW_RATING);
    expect(even.winner.rating).toBe(1220);
    expect(even.loser.rating).toBe(1180);
    const upset = rateMatch({ rating: 1100, games: 30 }, { rating: 1400, games: 30 });
    const expected = rateMatch({ rating: 1400, games: 30 }, { rating: 1100, games: 30 });
    expect(upset.winner.rating - 1100).toBeGreaterThan(expected.winner.rating - 1400);
    expect(upset.winner.games).toBe(31);
  });
});
