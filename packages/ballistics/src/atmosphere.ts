/** Air density and speed of sound from weather. */

export interface Atmosphere {
  /** Shooter's altitude above sea level, m. */
  altitudeM: number;
  /** Station (unadjusted) pressure at the shooter, hPa. */
  pressureHPa: number;
  temperatureC: number;
  /** Relative humidity as a fraction 0..1. */
  humidity: number;
}

/** ICAO standard atmosphere at sea level, dry air. */
export const ICAO: Readonly<Atmosphere> = Object.freeze({
  altitudeM: 0,
  pressureHPa: 1013.25,
  temperatureC: 15,
  humidity: 0,
});

const KELVIN = 273.15;
const LAPSE_RATE = -0.0065; // K/m, troposphere
const PRESSURE_EXPONENT = 5.255876; // g·M/(R·L)
const SPEED_OF_SOUND_COEFF = 20.0467; // m/s per sqrt(K)

/** Standard station pressure for an altitude (hPa), for maps that only specify elevation. */
export function standardPressure(altitudeM: number): number {
  return 1013.25 * Math.pow(1 + (LAPSE_RATE * altitudeM) / (15 + KELVIN), PRESSURE_EXPONENT);
}

/** Speed of sound in air, m/s. */
export function speedOfSound(temperatureC: number): number {
  return SPEED_OF_SOUND_COEFF * Math.sqrt(temperatureC + KELVIN);
}

/**
 * Moist-air density, kg/m³, from the CIPM-2007 equation
 * (ideal gas with compressibility and water-vapour corrections).
 */
export function airDensity(temperatureC: number, pressureHPa: number, humidity: number): number {
  const R = 8.314472;
  const Ma = 28.96546e-3;
  const Mv = 18.01528e-3;
  const T = temperatureC + KELVIN;
  const p = pressureHPa * 100;
  const rh = Math.min(1, Math.max(0, humidity));

  const psv = Math.exp(1.2378847e-5 * T * T - 1.9121316e-2 * T + 33.93711047 - 6.3431645e3 / T);
  const f = 1.00062 + 3.14e-8 * p + 5.6e-7 * temperatureC * temperatureC;
  const xv = (rh * f * psv) / p;
  const t = temperatureC;
  const Z =
    1 -
    (p / T) *
      (1.58123e-6 +
        -2.9331e-8 * t +
        1.1043e-10 * t * t +
        (5.707e-6 + -2.051e-8 * t) * xv +
        (1.9898e-4 + -2.376e-6 * t) * xv * xv) +
    (p / T) * (p / T) * (1.83e-11 + -0.765e-8 * xv * xv);
  return ((p * Ma) / (Z * R * T)) * (1 - xv * (1 - Mv / Ma));
}

export interface AirState {
  density: number; // kg/m³
  speedOfSound: number; // m/s
}

/**
 * Returns a function giving air state at a height above the shooter, using the standard
 * lapse rate. Uphill/downhill shots on the alpine map see thinner/thicker air along the path.
 */
export function airModel(atmo: Atmosphere): (heightAboveShooterM: number) => AirState {
  const t0 = atmo.temperatureC + KELVIN;
  const rho0 = airDensity(atmo.temperatureC, atmo.pressureHPa, atmo.humidity);
  const c0 = speedOfSound(atmo.temperatureC);
  return (dh: number): AirState => {
    if (Math.abs(dh) < 1) return { density: rho0, speedOfSound: c0 };
    const t = t0 + LAPSE_RATE * dh;
    const pRatio = Math.pow(1 + (LAPSE_RATE * dh) / t0, PRESSURE_EXPONENT);
    return {
      density: rho0 * pRatio * (t0 / t),
      speedOfSound: SPEED_OF_SOUND_COEFF * Math.sqrt(t),
    };
  };
}
