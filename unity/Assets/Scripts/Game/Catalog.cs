using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>What a match is played as. Every mode is against the bot until online play is hosted.</summary>
    public enum GameMode
    {
        Ranked,
        Quick,
        Private,
        Bot,
        Training,
    }

    /// <summary>One operator on the Operators screen.</summary>
    public sealed class OperatorInfo
    {
        public string Name = "";
        /// <summary>Index into Catalog.Units (1 or 2; 0 is "all").</summary>
        public int Unit;
        public string Bio = "";
    }

    /// <summary>One attachment choice in a gunsmith slot. Effects are shown on the stat panels.</summary>
    public sealed class PartInfo
    {
        public string Name = "";
        public string Note = "";
        public string Detail = "";
        public int VelocityMps;
        public int WindPct;
        public int RecoilPct;
        public float WeightKg;
        /// <summary>Weapon level needed to fit it (1: always).</summary>
        public int Level = 1;
    }

    /// <summary>A gunsmith slot (optic, muzzle...) and its choices.</summary>
    public sealed class SlotInfo
    {
        public string Name = "";
        public int Default;
        public PartInfo[] Options = System.Array.Empty<PartInfo>();
    }

    /// <summary>A free battle pass reward: silver, gold, or an item (a finish, an outfit, an operator).</summary>
    public readonly struct PassReward
    {
        public readonly int Silver, Gold;
        /// <summary>The item's name and kind ("ESTUARY CAMO", "FINISH"), or null for currency.</summary>
        public readonly string? Item, ItemKind;
        public PassReward(int silver, int gold) { Silver = silver; Gold = gold; Item = ItemKind = null; }
        public PassReward(string item, string kind) { Silver = Gold = 0; Item = item; ItemKind = kind; }
        public string Value => Item ?? (Silver > 0 ? Silver : Gold).ToString("#,0");
        public string Kind => ItemKind ?? (Silver > 0 ? "SILVER" : "GOLD");
        /// <summary>"750 silver", "the Estuary camo finish".</summary>
        public string Describe() => Item != null ? $"{Item} ({ItemKind!.ToLowerInvariant()})" : $"{Value} {Kind.ToLowerInvariant()}";
    }

    /// <summary>
    /// The fixed menu content that isn't in game.json: modes, operators, attachments, finishes,
    /// pass rewards, ranks and the bot's stand-in ratings.
    /// </summary>
    public static class Catalog
    {
        // ---- app ------------------------------------------------------------

        /// <summary>"v2.0 (build 58)": the build number matches TestFlight; local builds say "dev".</summary>
        public static string Version
        {
            get
            {
                var t = Resources.Load<TextAsset>("BuildInfo");
                string b = t != null ? t.text.Trim() : "";
                return b == "" || b == "dev" ? $"v{Application.version} (dev)" : $"v{Application.version} (build {b})";
            }
        }

        /// <summary>
        /// Who made the third-party assets in this build, from the CREDITS file the asset fetch writes:
        /// the CC-BY models by name and author, then the CC0 and OFL sources in one line.
        /// </summary>
        public static string Credits()
        {
            var lines = new System.Collections.Generic.List<string>();
            var t = Resources.Load<TextAsset>("TP/CREDITS");
            if (t != null)
                foreach (var row in t.text.Split('\n'))
                {
                    var c = row.Split('|');
                    // | Asset | Source | Author | Licence | Link |
                    if (c.Length < 6 || !c[4].Contains("CC-BY")) continue;
                    lines.Add($"{c[1].Trim()} by {c[3].Trim()} ({c[4].Trim()}, Sketchfab)");
                }
            if (lines.Count == 0) lines.Add("Soldier: Military tactical suit; rifle: Low-Poly L115A3 (CC-BY 4.0, Sketchfab)");
            lines.Add("Skies, textures and props: Poly Haven (CC0). Fonts: Google Fonts (OFL). Every sound is synthesised in the game.");
            return string.Join(". ", lines);
        }

        // ---- modes ---------------------------------------------------------

        public static readonly string[] ModeNames = { "RANKED DUEL", "QUICK DUEL", "PRIVATE DUEL", "VS BOT", "TRAINING" };
        public static readonly string[] DifficultyNames = { "EASY", "MEDIUM", "HARD" };

        /// <summary>Modes that will need a server; until then the bot plays them.</summary>
        public static bool Online(GameMode m) => m == GameMode.Ranked || m == GameMode.Quick || m == GameMode.Private;

        public const string OnlineSoon = "Online play opens soon – playing a bot match";

        /// <summary>The bot's stand-in rating for ranked results.</summary>
        public static int BotRating(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => 1100,
            BotDifficulty.Hard => 1500,
            _ => 1300,
        };

        public static string BotName(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => "RECRUIT",
            BotDifficulty.Hard => "GHOSTWIND",
            _ => "VIPER",
        };

        // ---- maps ----------------------------------------------------------

        /// <summary>The terrain word on a map tile ("ESTUARY") and what each side's position is called ("BRIDGE").</summary>
        public static (string region, string spot) MapInfo(string id) => id switch
        {
            "twin-bridges" => ("ESTUARY", "BRIDGE"),
            "watchtowers" => ("MARSH", "TOWER"),
            "oil-rigs" => ("OPEN SEA", "RIG"),
            "dry-valley" => ("DESERT", "RIDGE"),
            "harbor-rooftops" => ("URBAN", "ROOFTOP"),
            "alpine-ridge" => ("MOUNTAIN", "PEAK"),
            _ => ("RANGE", "FIRING POINT"),
        };

        public static string LightName(SkyKind? sky) => sky switch
        {
            SkyKind.Sunrise => "SUNRISE",
            SkyKind.Sunset => "SUNSET",
            _ => "CLEAR DAY",
        };

        // ---- ranks ---------------------------------------------------------

        public static readonly string[] Ranks =
        {
            "RECRUIT", "MARKSMAN III", "MARKSMAN II", "MARKSMAN I",
            "SHARPSHOOTER III", "SHARPSHOOTER II", "SHARPSHOOTER I", "GHOST",
        };

        public static int RankIndex(int rating) => rating < 1100 ? 0 : Mathf.Min(Ranks.Length - 1, 1 + (rating - 1100) / 150);
        public static int RankFloor(int index) => index == 0 ? 0 : 1100 + (index - 1) * 150;
        public static string Rank(int rating) => Ranks[RankIndex(rating)];

        // ---- loadouts ------------------------------------------------------

        public static readonly string[] LoadoutNames = { "LONG RANGE", "ALL ROUND", "LOADOUT 3", "LOADOUT 4" };
        public static readonly string[] DefaultLoadoutRifles = { "mag338", "bolt65cm", "mag338", "bolt65cm" };
        /// <summary>Loadouts every profile starts with; + NEW adds the rest.</summary>
        public const int StartingLoadouts = 2;

        /// <summary>The .50 opens at this player level; everything else is open from the start.</summary>
        public const int HeavyRifleLevel = 10;
        public const string HeavyRifle = "am50";

        public static int RifleUnlockLevel(string id) => id == HeavyRifle ? HeavyRifleLevel : 1;

        // ---- operators -----------------------------------------------------

        public static readonly string[] Units = { "ALL", "COASTAL RECON", "HIGHLAND UNIT" };

        public static readonly OperatorInfo[] Operators =
        {
            new OperatorInfo { Name = "VARGA", Unit = 1, Bio = "Former harbour pilot. Reads water like a chart and never takes the first shot when the tide is turning." },
            new OperatorInfo { Name = "HOLLIS", Unit = 2, Bio = "Mountain warfare instructor. Counts the seconds between flash and crack, then waits one more." },
            new OperatorInfo { Name = "KESTREL", Unit = 1, Bio = "Saltmarsh scout. Lies still for hours and fires only when the reeds stop moving." },
            new OperatorInfo { Name = "SHADE", Unit = 2, Bio = "Season 01's operator. Ex-coastguard marksman who learned to shoot from a pitching deck, so a steady bridge feels like cheating." },
        };

        /// <summary>The season operator, earned on the Battle Pass free track.</summary>
        public const int SeasonOperator = 3;

        /// <summary>Why an operator can't be picked yet, or null if it can.</summary>
        public static string? OperatorLock(int op) =>
            op == SeasonOperator && !Profile.Claimed(SeasonOperatorTier) ? $"Claim Battle Pass tier {SeasonOperatorTier} to unlock this operator." : null;

        /// <summary>The name to show for an operator: locked ones stay classified.</summary>
        public static string OperatorName(int op) => OperatorLock(op) != null ? "CLASSIFIED" : Operators[op].Name;

        public static readonly string[] Outfits = { "Estuary standard", "Night ops", "Saltmarsh ghillie", "Season 01 outfit" };
        /// <summary>The colours each outfit dresses the soldier in (the in-duel figure and the Operators portrait).</summary>
        public static readonly Color[] OutfitTints =
        {
            new Color(0.56f, 0.55f, 0.47f),
            new Color(0.24f, 0.26f, 0.3f),
            new Color(0.42f, 0.47f, 0.31f),
            new Color(0.5f, 0.38f, 0.26f),
        };

        /// <summary>The last outfit is a Battle Pass reward.</summary>
        public const int LockedOutfit = 3;

        public static string? OutfitLock(int outfit) =>
            outfit == LockedOutfit && !Profile.Claimed(SeasonOutfitTier) ? $"Claim Battle Pass tier {SeasonOutfitTier} to unlock the season outfit." : null;

        // ---- gunsmith ------------------------------------------------------

        public static readonly SlotInfo[] Slots =
        {
            new SlotInfo
            {
                Name = "OPTIC", Default = 0, Options = new[]
                {
                    new PartInfo { Name = "5–25× FFP MIL", Note = "Full zoom range", Detail = "First focal plane: the mil marks stay true at every zoom, so holds work at any magnification." },
                    new PartInfo { Name = "3–18× SFP", Note = "Wider view, lighter", Detail = "Second focal plane, easier to find the target with. Lighter than the long scope.", WeightKg = -0.3f },
                    new PartInfo { Name = "6–24× LIT", Note = "Glowing reticle", Detail = "An illuminated reticle that stays readable against dark water and dusk skies.", WeightKg = 0.1f },
                    new PartInfo { Name = "7–35× HIGH MAG", Note = "Weapon level 12", Detail = "More magnification for the longest lanes. Heavy, and the sway shows.", RecoilPct = -4, WeightKg = 0.4f, Level = 12 },
                },
            },
            new SlotInfo
            {
                Name = "MUZZLE", Default = 0, Options = new[]
                {
                    new PartInfo { Name = "3-PORT BRAKE", Note = "Cuts recoil", Detail = "Vents the gas sideways: the rifle settles faster and you see your own impact.", RecoilPct = 12, WeightKg = 0.2f },
                    new PartInfo { Name = "SUPPRESSOR · TITAN", Note = "Hides muzzle flash from the target", Detail = "Your shot gives no flash or dust signature. The target only learns your position after impact.", VelocityMps = -15, RecoilPct = 8, WeightKg = 0.6f },
                    new PartInfo { Name = "THREAD PROTECTOR", Note = "No effect, lighter", Detail = "A bare muzzle. Nothing gained, nothing to carry." },
                    new PartInfo { Name = "LINEAR COMP", Note = "Weapon level 14", Detail = "Throws the blast forward, away from you. Recoil drops further than with the brake.", RecoilPct = 16, WeightKg = 0.3f, Level = 14 },
                },
            },
            new SlotInfo
            {
                Name = "BARREL", Default = 1, Options = new[]
                {
                    new PartInfo { Name = "24\" STANDARD", Note = "Factory length", Detail = "The factory barrel. Balanced and quick to bring on target." },
                    new PartInfo { Name = "27\" FLUTED", Note = "More velocity", Detail = "Longer for more speed, fluted to keep the weight down.", VelocityMps = 15, WindPct = 2, WeightKg = 0.2f },
                    new PartInfo { Name = "30\" HEAVY", Note = "Fastest, heaviest", Detail = "The most muzzle velocity and the steadiest hold, at a weight you'll feel.", VelocityMps = 25, WindPct = 4, RecoilPct = 4, WeightKg = 0.9f },
                    new PartInfo { Name = "20\" CARBON", Note = "Weapon level 10", Detail = "Short and light. Gives up velocity for handling.", VelocityMps = -20, RecoilPct = -4, WeightKg = -0.8f, Level = 10 },
                },
            },
            new SlotInfo
            {
                Name = "STOCK", Default = 1, Options = new[]
                {
                    new PartInfo { Name = "FIXED SPORTER", Note = "Light, simple", Detail = "A plain fixed stock. Light, but nothing to adjust." },
                    new PartInfo { Name = "FOLDING CHASSIS", Note = "Adjustable cheek", Detail = "An aluminium chassis with an adjustable cheek riser for a consistent eye line.", RecoilPct = 6, WeightKg = 0.4f },
                    new PartInfo { Name = "PRECISION", Note = "Most adjustable", Detail = "Length, cheek and butt height all adjust. The rifle comes back on target fastest.", RecoilPct = 10, WeightKg = 0.7f },
                    new PartInfo { Name = "CARBON SKELETON", Note = "Weapon level 16", Detail = "Precision geometry in carbon fibre, without the weight.", RecoilPct = 8, WeightKg = -0.3f, Level = 16 },
                },
            },
            new SlotInfo
            {
                Name = "AMMO", Default = 1, Options = new[]
                {
                    new PartInfo { Name = "FACTORY BALL", Note = "Cheap and cheerful", Detail = "Plain factory ammunition. Fine inside the sweet spot." },
                    new PartInfo { Name = "MATCH GRADE", Note = "Tighter groups", Detail = "Weighed and sorted rounds with a better ballistic coefficient.", VelocityMps = 5, WindPct = 3 },
                    new PartInfo { Name = "MATCH HPBT", Note = "Holds wind better", Detail = "Hollow point boat tail: carries its speed further and drifts less.", VelocityMps = 10, WindPct = 5 },
                    new PartInfo { Name = "HAND LOAD", Note = "Weapon level 18", Detail = "Worked up for this barrel. The fastest, flattest round you can fit.", VelocityMps = 20, WindPct = 6, RecoilPct = -2, Level = 18 },
                },
            },
            new SlotInfo
            {
                Name = "BIPOD", Default = 0, Options = new[]
                {
                    new PartInfo { Name = "EMPTY", Note = "No bipod", Detail = "Shoot off a pack or the deck. Nothing to carry." },
                    new PartInfo { Name = "HARRIS 6–9\"", Note = "Steadier prone", Detail = "A spring bipod. Steadies the rifle and soaks up some recoil.", RecoilPct = 6, WeightKg = 0.4f },
                    new PartInfo { Name = "ATLAS V8", Note = "Cant and pan", Detail = "Lets the rifle cant and pan on the legs for quick level corrections.", RecoilPct = 9, WeightKg = 0.5f },
                    new PartInfo { Name = "SPIGOT RIG", Note = "Weapon level 12", Detail = "A tripod rest for the steadiest hold there is.", RecoilPct = 12, WeightKg = 0.7f, Level = 12 },
                },
            },
        };

        public static readonly string[] Finishes = { "CERAKOTE BLACK", "FLAT DARK EARTH", "RANGER GREEN", "SNIPER GREY", "ESTUARY CAMO", "GILT" };
        /// <summary>Finishes from here on are Battle Pass rewards.</summary>
        public const int FirstLockedFinish = 4;

        /// <summary>The pass tier that unlocks a finish, or 0 if it's open from the start.</summary>
        public static int FinishTier(int finish) => finish < FirstLockedFinish ? 0 : finish == FirstLockedFinish ? CamoTier : GiltTier;

        public static string? FinishLock(int finish)
        {
            int t = FinishTier(finish);
            return t > 0 && !Profile.Claimed(t) ? $"Claim Battle Pass tier {t} to unlock {Finishes[finish]}." : null;
        }

        // ---- battle pass ---------------------------------------------------

        public const int PassTiers = 50;
        public const int CamoTier = 20, SeasonOutfitTier = 30, GiltTier = 40, SeasonOperatorTier = 50;
        public const string SeasonName = "SEASON 01 · OPEN WATER";
        /// <summary>The season's last day (local time); the pass stays claimable until it ends.</summary>
        public static readonly System.DateTime SeasonEnd = new System.DateTime(2026, 12, 31, 23, 59, 59);

        /// <summary>The free track: silver most tiers, gold every fifth, and an item at tiers 20, 30, 40 and 50.</summary>
        public static PassReward FreeReward(int tier) => tier switch
        {
            CamoTier => new PassReward(Finishes[FirstLockedFinish], "FINISH"),
            SeasonOutfitTier => new PassReward("SEASON 01 OUTFIT", "OUTFIT"),
            GiltTier => new PassReward(Finishes[FirstLockedFinish + 1], "FINISH"),
            SeasonOperatorTier => new PassReward(Operators[SeasonOperator].Name, "OPERATOR"),
            _ => tier % 5 == 0 ? new PassReward(0, 50 + tier * 2) : new PassReward(250 + tier * 50, 0),
        };
    }
}
