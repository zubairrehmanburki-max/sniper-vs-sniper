using System.Collections.Generic;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>What the player takes into one duel, with the gunsmith's parts already worked in.</summary>
    public sealed class Kit
    {
        /// <summary>The game config for this duel: the fitted rifle added and the optic's zoom range.</summary>
        public GameConfig Config = null!;
        /// <summary>The fitted rifle's id in Config (the base id plus "+fit").</summary>
        public string RifleId = "";
        /// <summary>Recoil settles this many times faster than bare (1 = no change).</summary>
        public double RecoilRecovery = 1;
        public bool Suppressed;
        public Color Finish;
        public Color Outfit;
        /// <summary>The soldier model the outfit dresses the operator in.</summary>
        public string Soldier = "";
    }

    /// <summary>
    /// Turns the saved loadout into a duel kit: muzzle velocity and ballistic coefficient from the
    /// barrel, ammo and muzzle parts; the optic's zoom range; recoil recovery; the suppressor; the
    /// rifle's finish and the operator's outfit.
    /// </summary>
    public static class Gear
    {
        /// <summary>Zoom range per optic choice (Catalog.Slots[0]).</summary>
        static readonly (int min, int max)[] OpticZoom = { (5, 25), (3, 18), (6, 24), (7, 35) };
        const int OpticSlot = 0, MuzzleSlot = 1, SuppressorOption = 1;

        /// <summary>Furniture colour per finish (Catalog.Finishes).</summary>
        public static readonly Color[] FinishColors =
        {
            new Color(0.09f, 0.1f, 0.09f),
            new Color(0.48f, 0.41f, 0.31f),
            new Color(0.31f, 0.33f, 0.23f),
            new Color(0.33f, 0.35f, 0.35f),
            new Color(0.3f, 0.33f, 0.24f),
            new Color(0.62f, 0.48f, 0.19f),
        };

        public static Kit ForMatch(GameConfig cfg, string rifleId)
        {
            var baseRifle = cfg.RifleById(rifleId);
            var (dv, windPct, recoilPct, _, _) = Profile.PartTotals(rifleId);
            var fitted = new RifleConfig
            {
                Id = rifleId + "+fit",
                Name = baseRifle.Name,
                Class = baseRifle.Class,
                Cartridge = baseRifle.Cartridge,
                DragModel = baseRifle.DragModel,
                // Wind resistance is the round holding its speed: a better ballistic coefficient.
                Bc = baseRifle.Bc * (1 + windPct / 100.0),
                MassKg = baseRifle.MassKg,
                DiameterM = baseRifle.DiameterM,
                LengthM = baseRifle.LengthM,
                MuzzleVelocityMps = baseRifle.MuzzleVelocityMps + dv,
                TwistM = baseRifle.TwistM,
                SweetSpotM = baseRifle.SweetSpotM,
                ColdBoreShiftMrad = baseRifle.ColdBoreShiftMrad,
            };
            var (zMin, zMax) = OpticZoom[Mathf.Clamp(Profile.Part(rifleId, OpticSlot), 0, OpticZoom.Length - 1)];
            var b = cfg.Scope;
            var config = new GameConfig
            {
                Ballistics = cfg.Ballistics,
                Rifles = new List<RifleConfig>(cfg.Rifles) { fitted },
                Scope = new ScopeConfig
                {
                    MinZoom = zMin,
                    MaxZoom = zMax,
                    FovAt1xDeg = b.FovAt1xDeg,
                    ClickMrad = b.ClickMrad,
                    MaxElevationMrad = b.MaxElevationMrad,
                    MaxWindageMrad = b.MaxWindageMrad,
                    EyeFovDeg = b.EyeFovDeg,
                },
                Sway = cfg.Sway,
                Target = cfg.Target,
                PracticeRange = cfg.PracticeRange,
                SoftCover = cfg.SoftCover,
                Rules = cfg.Rules,
                Player = cfg.Player,
                Bot = cfg.Bot,
            };
            return new Kit
            {
                Config = config,
                RifleId = fitted.Id,
                RecoilRecovery = Mathf.Max(0.5f, 1 + recoilPct / 100f),
                Suppressed = Profile.Part(rifleId, MuzzleSlot) == SuppressorOption,
                Finish = FinishColors[Mathf.Clamp(Profile.Finish(rifleId), 0, FinishColors.Length - 1)],
                Outfit = Catalog.OutfitTints[Profile.Outfit(Profile.Operator)],
                Soldier = Catalog.OutfitSoldiers[Profile.Outfit(Profile.Operator)],
            };
        }
    }
}
