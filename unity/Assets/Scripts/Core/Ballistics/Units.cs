using System;

namespace SniperDuel.Ballistics
{
    /// <summary>Unit conversions and physical constants. Everything inside the solver is SI.</summary>
    public static class Units
    {
        public const double Gravity = 9.80665; // m/s²
        public const double EarthRotation = 7.2921159e-5; // rad/s

        public const double Inch = 0.0254; // m
        public const double Foot = 0.3048; // m
        public const double Yard = 0.9144; // m
        public const double Grain = 6.479891e-5; // kg
        public const double Mph = 0.44704; // m/s
        public const double Fps = 0.3048; // m/s

        /// <summary>Ballistic coefficient conversion: 1 lb/in² expressed in kg/m².</summary>
        public const double BcLbIn2 = 703.0696;

        public const double Mrad = 1e-3; // rad
        public const double Moa = Math.PI / (180 * 60); // rad
        public const double Deg = Math.PI / 180; // rad

        public static double MradToRad(double mrad) => mrad * Mrad;
        public static double RadToMrad(double rad) => rad / Mrad;
    }

    /// <summary>Plain double-precision vector. The solver avoids UnityEngine.Vector3 (float) on purpose.</summary>
    public struct Vec3
    {
        public double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3 Normalized => this * (1 / Length);
        public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####})";
    }

    public enum DragModel
    {
        G1,
        G7,
    }

    public sealed class Projectile
    {
        public DragModel DragModel;
        /// <summary>Ballistic coefficient against the chosen standard, lb/in².</summary>
        public double Bc;
        public double MassKg;
        public double DiameterM;
        public double LengthM;

        /// <summary>Kinetic energy in joules.</summary>
        public double KineticEnergy(double speed) => 0.5 * MassKg * speed * speed;
    }
}
