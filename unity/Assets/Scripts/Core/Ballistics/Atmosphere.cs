using System;

namespace SniperDuel.Ballistics
{
    /// <summary>Weather at the shooter.</summary>
    public sealed class Atmosphere
    {
        /// <summary>Shooter's altitude above sea level, m.</summary>
        public double AltitudeM;
        /// <summary>Station (unadjusted) pressure at the shooter, hPa.</summary>
        public double PressureHPa;
        public double TemperatureC;
        /// <summary>Relative humidity as a fraction 0..1.</summary>
        public double Humidity;

        public Atmosphere(double altitudeM, double pressureHPa, double temperatureC, double humidity)
        {
            AltitudeM = altitudeM;
            PressureHPa = pressureHPa;
            TemperatureC = temperatureC;
            Humidity = humidity;
        }

        /// <summary>ICAO standard atmosphere at sea level, dry air.</summary>
        public static Atmosphere Icao => new Atmosphere(0, 1013.25, 15, 0);
    }

    public struct AirState
    {
        public double Density; // kg/m³
        public double SpeedOfSound; // m/s
    }

    /// <summary>Air density and speed of sound from weather.</summary>
    public static class Air
    {
        const double Kelvin = 273.15;
        const double LapseRate = -0.0065; // K/m, troposphere
        const double PressureExponent = 5.255876; // g·M/(R·L)
        const double SpeedOfSoundCoeff = 20.0467; // m/s per sqrt(K)

        /// <summary>Standard station pressure for an altitude (hPa), for maps that only specify elevation.</summary>
        public static double StandardPressure(double altitudeM) =>
            1013.25 * Math.Pow(1 + LapseRate * altitudeM / (15 + Kelvin), PressureExponent);

        /// <summary>Speed of sound in air, m/s.</summary>
        public static double SpeedOfSound(double temperatureC) => SpeedOfSoundCoeff * Math.Sqrt(temperatureC + Kelvin);

        /// <summary>Moist-air density, kg/m³, from the CIPM-2007 equation.</summary>
        public static double Density(double temperatureC, double pressureHPa, double humidity)
        {
            const double R = 8.314472;
            const double Ma = 28.96546e-3;
            const double Mv = 18.01528e-3;
            double T = temperatureC + Kelvin;
            double p = pressureHPa * 100;
            double rh = Math.Min(1, Math.Max(0, humidity));

            double psv = Math.Exp(1.2378847e-5 * T * T - 1.9121316e-2 * T + 33.93711047 - 6.3431645e3 / T);
            double f = 1.00062 + 3.14e-8 * p + 5.6e-7 * temperatureC * temperatureC;
            double xv = rh * f * psv / p;
            double t = temperatureC;
            double Z = 1
                - p / T * (1.58123e-6 + -2.9331e-8 * t + 1.1043e-10 * t * t + (5.707e-6 + -2.051e-8 * t) * xv + (1.9898e-4 + -2.376e-6 * t) * xv * xv)
                + p / T * (p / T) * (1.83e-11 + -0.765e-8 * xv * xv);
            return p * Ma / (Z * R * T) * (1 - xv * (1 - Mv / Ma));
        }

        /// <summary>
        /// Air state at a height above the shooter, using the standard lapse rate. Uphill/downhill shots
        /// see thinner/thicker air along the path.
        /// </summary>
        public static Func<double, AirState> Model(Atmosphere atmo)
        {
            double t0 = atmo.TemperatureC + Kelvin;
            double rho0 = Density(atmo.TemperatureC, atmo.PressureHPa, atmo.Humidity);
            double c0 = SpeedOfSound(atmo.TemperatureC);
            return dh =>
            {
                if (Math.Abs(dh) < 1) return new AirState { Density = rho0, SpeedOfSound = c0 };
                double t = t0 + LapseRate * dh;
                double pRatio = Math.Pow(1 + LapseRate * dh / t0, PressureExponent);
                return new AirState { Density = rho0 * pRatio * (t0 / t), SpeedOfSound = SpeedOfSoundCoeff * Math.Sqrt(t) };
            };
        }
    }
}
