using System;
using System.Collections.Generic;
using System.Linq;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>One line of the XP breakdown on the results screen.</summary>
    public readonly struct XpLine
    {
        public readonly string Label;
        public readonly int Amount;
        public XpLine(string label, int amount) { Label = label; Amount = amount; }
    }

    /// <summary>What a finished match did to the profile, for the results screen.</summary>
    public sealed class MatchReport
    {
        public bool Won;
        public GameMode Mode;
        public readonly List<XpLine> Xp = new List<XpLine>();
        public int XpTotal;
        public int XpBefore;
        public bool Rated;
        public int RatingBefore, RatingAfter;
        public int Silver;
    }

    /// <summary>
    /// The player's progress and gear, saved in PlayerPrefs on this device until online accounts
    /// arrive: equipped loadout, operator, attachments and finishes, XP, currencies, the offline
    /// rating, career counters, medals, daily orders, pass claims and recent matches.
    /// </summary>
    public static class Profile
    {
        public const int XpPerLevel = 2000, XpPerTier = 1000, XpPerWeaponLevel = 1500, MaxWeaponLevel = 20;
        const int HistoryLength = 6, RatingHistoryLength = 30;

        // ---- gear ----------------------------------------------------------

        /// <summary>Index into Catalog.LoadoutNames: the loadout taken into a duel.</summary>
        public static int ActiveLoadout { get => GetInt("prof.loadout", 0, 0, Catalog.LoadoutNames.Length - 1); set => Set("prof.loadout", value); }

        public static string LoadoutRifle(int loadout) =>
            PlayerPrefs.GetString("prof.loadout" + loadout + ".rifle", Catalog.DefaultLoadoutRifles[Mathf.Clamp(loadout, 0, Catalog.DefaultLoadoutRifles.Length - 1)]);

        public static void SetLoadoutRifle(int loadout, string rifleId)
        {
            PlayerPrefs.SetString("prof.loadout" + loadout + ".rifle", rifleId);
            Save();
        }

        /// <summary>The rifle taken into a duel: the active loadout's, if the config has it and it's unlocked.</summary>
        public static string Rifle(GameConfig cfg)
        {
            string id = LoadoutRifle(ActiveLoadout);
            if (cfg.Rifles.Exists(r => r.Id == id) && Level >= Catalog.RifleUnlockLevel(id)) return id;
            return cfg.Rifles.Count > 0 ? cfg.Rifles[0].Id : "";
        }

        /// <summary>Index into Catalog.Operators.</summary>
        public static int Operator { get => GetInt("prof.operator", 0, 0, Catalog.Operators.Length - 1); set => Set("prof.operator", value); }
        public static int Outfit(int op) => GetInt("prof.outfit" + op, 0, 0, Catalog.Outfits.Length - 1);
        public static void SetOutfit(int op, int outfit) => Set("prof.outfit" + op, outfit);

        /// <summary>The fitted option (index into the slot's Options) for one rifle.</summary>
        public static int Part(string rifleId, int slot)
        {
            var s = Catalog.Slots[slot];
            return GetInt($"prof.part.{rifleId}.{slot}", s.Default, 0, s.Options.Length - 1);
        }

        public static void SetPart(string rifleId, int slot, int option) => Set($"prof.part.{rifleId}.{slot}", option);

        /// <summary>Index into Catalog.Finishes for one rifle.</summary>
        public static int Finish(string rifleId) => GetInt("prof.finish." + rifleId, 0, 0, Catalog.Finishes.Length - 1);
        public static void SetFinish(string rifleId, int finish) => Set("prof.finish." + rifleId, finish);

        /// <summary>The summed effect of a rifle's fitted parts.</summary>
        public static (int velocity, int wind, int recoil, float weight, int fitted) PartTotals(string rifleId)
        {
            int v = 0, w = 0, r = 0, n = 0;
            float kg = 0;
            for (int i = 0; i < Catalog.Slots.Length; i++)
            {
                var p = Catalog.Slots[i].Options[Part(rifleId, i)];
                v += p.VelocityMps; w += p.WindPct; r += p.RecoilPct; kg += p.WeightKg;
                if (p.Name != "EMPTY") n++;
            }
            return (v, w, r, kg, n);
        }

        // ---- the last menu choices ----------------------------------------

        public static GameMode Mode { get => (GameMode)GetInt("play.mode", 0, 0, 4); set => Set("play.mode", (int)value); }
        public static BotDifficulty Difficulty { get => (BotDifficulty)GetInt("play.difficulty", 1, 0, 2); set => Set("play.difficulty", (int)value); }
        public static string MapId { get => PlayerPrefs.GetString("play.map", Maps.DefaultMap); set { PlayerPrefs.SetString("play.map", value); Save(); } }
        /// <summary>Online modes pick a random map each match.</summary>
        public static bool MapRotation { get => GetInt("play.rotation", 0, 0, 1) == 1; set => Set("play.rotation", value ? 1 : 0); }

        // ---- progress ------------------------------------------------------

        public static int Xp => PlayerPrefs.GetInt("prog.xp", 0);
        public static int Level => LevelAt(Xp);
        public static int LevelAt(int xp) => 1 + xp / XpPerLevel;
        public static float LevelProgress(int xp) => xp % XpPerLevel / (float)XpPerLevel;

        public static int PassTier => PassTierAt(Xp);
        public static int PassTierAt(int xp) => Mathf.Min(Catalog.PassTiers, 1 + xp / XpPerTier);

        public static int Silver => PlayerPrefs.GetInt("prog.silver", 0);
        public static int Gold => PlayerPrefs.GetInt("prog.gold", 0);

        /// <summary>Offline rating from ranked-mode duels against the bot.</summary>
        public static int Rating => PlayerPrefs.GetInt("prog.rating", (int)Elo.NewRating.Value);
        public static int RatedGames => PlayerPrefs.GetInt("prog.rated", 0);

        public static int[] RatingHistory => Split(PlayerPrefs.GetString("prog.ratingHistory", "")).Select(v => int.TryParse(v, out var n) ? n : 0).ToArray();

        public static int WeaponXp(string rifleId) => PlayerPrefs.GetInt("prog.weapon." + rifleId, 0);
        public static int WeaponLevel(string rifleId) => Mathf.Min(MaxWeaponLevel, 1 + WeaponXp(rifleId) / XpPerWeaponLevel);

        public static bool Claimed(int tier) => PlayerPrefs.GetInt("pass.claimed." + tier, 0) != 0;

        /// <summary>Claims a reached, unclaimed free tier. False if it can't be claimed.</summary>
        public static bool Claim(int tier)
        {
            if (tier < 1 || tier > PassTier || Claimed(tier)) return false;
            var r = Catalog.FreeReward(tier);
            PlayerPrefs.SetInt("pass.claimed." + tier, 1);
            AddInt("prog.silver", r.Silver);
            AddInt("prog.gold", r.Gold);
            Save();
            return true;
        }

        /// <summary>The lowest reached tier not yet claimed, or 0.</summary>
        public static int FirstUnclaimed()
        {
            for (int t = 1; t <= PassTier; t++) if (!Claimed(t)) return t;
            return 0;
        }

        // ---- career --------------------------------------------------------

        /// <summary>A career counter ("all.wins", "ranked.hits", "rifle.mag338.shots", "map.oil-rigs.duels"...).</summary>
        public static int Count(string key) => PlayerPrefs.GetInt("stat." + key, 0);
        /// <summary>A career best in metres ("all.longest", "op0.longest").</summary>
        public static float Best(string key) => PlayerPrefs.GetFloat("best." + key, 0);

        public static readonly string[] MedalNames = { "KILOMETRE CLUB", "WIND WHISPERER", "ONE AND DONE" };
        public static readonly string[] MedalRules = { "Hit past 1,000 m", "Hit on a map with 8 m/s+ wind", "Win 2–0 without a missed shot" };
        public static bool Medal(int i) => PlayerPrefs.GetInt("medal." + i, 0) != 0;

        /// <summary>Recent matches, newest first: "W 2–1 · TWIN BRIDGES" and a detail column.</summary>
        public static List<(string text, string detail)> History =>
            Split(PlayerPrefs.GetString("prog.history", "")).Select(l =>
            {
                int bar = l.IndexOf('|');
                return bar < 0 ? (l, "") : (l.Substring(0, bar), l.Substring(bar + 1));
            }).ToList();

        // ---- daily orders --------------------------------------------------

        public static readonly string[] OrderNames = { "Win a duel past 800 m", "Land 3 first-round hits" };
        public static readonly int[] OrderGoals = { 1, 3 };
        const int OrderXp = 500;

        static string Today => DateTime.Now.ToString("yyyyMMdd");

        /// <summary>Today's progress on a daily order (resets at local midnight).</summary>
        public static int Order(int i) => PlayerPrefs.GetString("daily.date", "") == Today ? Mathf.Min(OrderGoals[i], PlayerPrefs.GetInt("daily." + i, 0)) : 0;

        public static TimeSpan OrdersResetIn => DateTime.Today.AddDays(1) - DateTime.Now;

        // ---- recording a match ---------------------------------------------

        /// <summary>
        /// Books a finished match: XP, silver, rating (ranked only), career counters (training
        /// doesn't count), medals, daily orders and the history line.
        /// </summary>
        public static MatchReport Record(MatchState s, GameMode mode, string rifleId, BotDifficulty bot)
        {
            var map = Maps.ById(s.MapId);
            bool won = s.Winner == Side.A;
            bool training = mode == GameMode.Training;
            var mine = s.Shots.Where(x => x.Shooter == Side.A).ToList();
            var hits = mine.Where(x => x.Verdict != Verdict.Miss).ToList();
            int heads = hits.Count(x => x.Zone == HitZone.Head);
            float longest = hits.Count > 0 ? (float)hits.Max(x => x.Distance) : 0;
            // Rounds where you fired, and those where your first shot of the round landed.
            var byRound = mine.GroupBy(x => x.Round).ToList();
            int rounds = byRound.Count;
            int firstHits = byRound.Count(g => g.OrderBy(x => x.ShotIndex).First().Verdict != Verdict.Miss);

            var report = new MatchReport { Won = won, Mode = mode, XpBefore = Xp, RatingBefore = Rating, RatingAfter = Rating };

            // XP.
            var lines = new List<XpLine> { new XpLine(won ? "Duel won" : s.Forfeit ? "Duel forfeited" : "Duel played", won ? 1000 : s.Forfeit ? 0 : 300) };
            if (firstHits > 0) lines.Add(new XpLine(firstHits == 1 ? "First-shot hit" : $"First-shot hits ×{firstHits}", 300 * firstHits));
            if (heads > 0) lines.Add(new XpLine(heads == 1 ? "Headshot" : $"Headshots ×{heads}", 250 * heads));

            // Daily orders.
            if (PlayerPrefs.GetString("daily.date", "") != Today)
            {
                PlayerPrefs.SetString("daily.date", Today);
                for (int i = 0; i < OrderGoals.Length; i++) PlayerPrefs.SetInt("daily." + i, 0);
            }
            int[] gain = { won && map.LaneLength >= 800 ? 1 : 0, hits.Count(x => x.Round == 0) };
            for (int i = 0; i < OrderGoals.Length; i++)
            {
                int before = PlayerPrefs.GetInt("daily." + i, 0);
                int after = Mathf.Min(OrderGoals[i], before + gain[i]);
                PlayerPrefs.SetInt("daily." + i, after);
                if (before < OrderGoals[i] && after >= OrderGoals[i]) lines.Add(new XpLine("Daily order: " + OrderNames[i].ToLowerInvariant(), OrderXp));
            }

            if (!training)
            {
                // Medals.
                bool[] earned =
                {
                    longest >= 1000,
                    hits.Count > 0 && map.Climate.WindSpeedMps[1] >= 8,
                    won && s.Players.B.Kills == 0 && mine.Count == hits.Count && mine.Count > 0,
                };
                for (int i = 0; i < earned.Length; i++)
                {
                    if (!earned[i] || Medal(i)) continue;
                    PlayerPrefs.SetInt("medal." + i, 1);
                    lines.Add(new XpLine("Medal: " + MedalNames[i], 500));
                }

                // Career.
                var scopes = mode == GameMode.Ranked ? new[] { "all", "ranked" } : new[] { "all" };
                foreach (var sc in scopes)
                {
                    AddInt($"stat.{sc}.duels", 1);
                    AddInt($"stat.{sc}." + (won ? "wins" : "losses"), 1);
                    AddInt($"stat.{sc}.shots", mine.Count);
                    AddInt($"stat.{sc}.hits", hits.Count);
                    AddInt($"stat.{sc}.heads", heads);
                    AddInt($"stat.{sc}.rounds", rounds);
                    AddInt($"stat.{sc}.firstHits", firstHits);
                    MaxFloat($"best.{sc}.longest", longest);
                }
                AddInt($"stat.rifle.{rifleId}.shots", mine.Count);
                AddInt($"stat.rifle.{rifleId}.hits", hits.Count);
                AddInt($"stat.map.{map.Id}.duels", 1);
                if (won) AddInt($"stat.map.{map.Id}.wins", 1);
                int op = Operator;
                AddInt($"stat.op{op}.duels", 1);
                if (won) AddInt($"stat.op{op}.wins", 1);
                MaxFloat($"best.op{op}.longest", longest);
            }

            // Results show four lines: fold any extras into the last.
            if (lines.Count > 4)
            {
                int extra = lines.Skip(3).Sum(l => l.Amount);
                lines.RemoveRange(3, lines.Count - 3);
                lines.Add(new XpLine("Orders and medals", extra));
            }
            if (training)
                for (int i = 0; i < lines.Count; i++) lines[i] = new XpLine(lines[i].Label + " (training ½)", lines[i].Amount / 2);
            report.Xp.AddRange(lines);
            report.XpTotal = lines.Sum(l => l.Amount);
            AddInt("prog.xp", report.XpTotal);
            AddInt("prog.weapon." + rifleId, report.XpTotal);
            report.Silver = training ? 0 : won ? 250 : 100;
            AddInt("prog.silver", report.Silver);

            // Rating: ranked only, against the bot's stand-in rating.
            if (mode == GameMode.Ranked)
            {
                var me = new Rating(Rating, RatedGames);
                var them = new Rating(Catalog.BotRating(bot), 30);
                var r = won ? Elo.RateMatch(me, them).Winner : Elo.RateMatch(them, me).Loser;
                report.Rated = true;
                report.RatingAfter = (int)r.Value;
                PlayerPrefs.SetInt("prog.rating", report.RatingAfter);
                PlayerPrefs.SetInt("prog.rated", r.Games);
                var hist = RatingHistory.ToList();
                if (hist.Count == 0) hist.Add(report.RatingBefore);
                hist.Add(report.RatingAfter);
                PlayerPrefs.SetString("prog.ratingHistory", string.Join("\n", hist.Skip(Math.Max(0, hist.Count - RatingHistoryLength))));
            }

            // History line.
            string result = $"{(won ? "W" : "L")} {s.Players.A.Kills}–{s.Players.B.Kills} · {map.Name.ToUpperInvariant()}";
            string detail = report.Rated ? $"{report.RatingAfter - report.RatingBefore:+0;−0;0}" : Catalog.ModeNames[(int)mode].Replace(" DUEL", "");
            var lines2 = new List<string> { result + "|" + detail };
            lines2.AddRange(Split(PlayerPrefs.GetString("prog.history", "")));
            PlayerPrefs.SetString("prog.history", string.Join("\n", lines2.Take(HistoryLength)));

            Save();
            return report;
        }

        // ---- storage -------------------------------------------------------

        static IEnumerable<string> Split(string s) => s.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        static int GetInt(string key, int def, int min, int max) => Mathf.Clamp(PlayerPrefs.GetInt(key, def), min, max);

        static void Set(string key, int v)
        {
            PlayerPrefs.SetInt(key, v);
            Save();
        }

        static void AddInt(string key, int n) => PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + n);

        static void MaxFloat(string key, float v)
        {
            if (v > PlayerPrefs.GetFloat(key, 0)) PlayerPrefs.SetFloat(key, v);
        }

        static void Save() => PlayerPrefs.Save();
    }
}
