import { G1_TABLE, G7_TABLE } from './dragTables.ts';
import { BC_LB_IN2 } from './units.ts';

export type DragModel = 'G1' | 'G7';

interface PreparedTable {
  mach: Float64Array;
  cd: Float64Array;
}

function prepare(table: ReadonlyArray<readonly [number, number]>): PreparedTable {
  return {
    mach: Float64Array.from(table, (r) => r[0]),
    cd: Float64Array.from(table, (r) => r[1]),
  };
}

const TABLES: Record<DragModel, PreparedTable> = {
  G1: prepare(G1_TABLE),
  G7: prepare(G7_TABLE),
};

/** Standard-projectile drag coefficient at a Mach number, linearly interpolated. */
export function dragCoefficient(model: DragModel, mach: number): number {
  const { mach: m, cd } = TABLES[model];
  const n = m.length;
  if (mach <= m[0]!) return cd[0]!;
  if (mach >= m[n - 1]!) return cd[n - 1]!;
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (m[mid]! <= mach) lo = mid;
    else hi = mid;
  }
  const f = (mach - m[lo]!) / (m[hi]! - m[lo]!);
  return cd[lo]! + f * (cd[hi]! - cd[lo]!);
}

/**
 * Drag factor k such that the drag deceleration vector is a = -k·|v_r|·v_r.
 *
 * F = ½ρv²·Cd_bullet·A with Cd_bullet = Cd_std·(SD/BC), which simplifies to
 * k = ρ·Cd_std·π / (8·BC) with BC in kg/m². Mass and diameter cancel out.
 */
export function dragFactor(model: DragModel, bcLbIn2: number, mach: number, density: number): number {
  return (density * dragCoefficient(model, mach) * Math.PI) / (8 * bcLbIn2 * BC_LB_IN2);
}
