/** Unit conversions and physical constants. Everything inside the solver is SI. */

export const GRAVITY = 9.80665; // m/s²
export const EARTH_ROTATION = 7.2921159e-5; // rad/s

export const INCH = 0.0254; // m
export const FOOT = 0.3048; // m
export const YARD = 0.9144; // m
export const GRAIN = 6.479891e-5; // kg
export const MPH = 0.44704; // m/s
export const FPS = 0.3048; // m/s

/** Ballistic coefficient conversion: 1 lb/in² expressed in kg/m². */
export const BC_LB_IN2 = 703.0696;

export const MRAD = 1e-3; // rad
export const MOA = Math.PI / (180 * 60); // rad
export const DEG = Math.PI / 180; // rad

export const mradToRad = (mrad: number): number => mrad * MRAD;
export const radToMrad = (rad: number): number => rad / MRAD;
