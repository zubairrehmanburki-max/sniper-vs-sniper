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
        /// <summary>Locked operators show a silhouette and say how to earn them.</summary>
        public string? Locked;
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

    /// <summary>A free battle pass reward.</summary>
    public readonly struct PassReward
    {
        public readonly int Silver, Gold;
        public PassReward(int silver, int gold) { Silver = silver; Gold = gold; }
        public string Value => (Silver > 0 ? Silver : Gold).ToString("#,0");
        public string Kind => Silver > 0 ? "SILVER" : "GOLD";
    }

    /// <summary>
    /// The fixed menu content that isn't in game.json: modes, operators, attachments, finishes,
    /// pass rewards, ranks and the bot's stand-in ratings.
    /// </summary>
    public static class Catalog
    {
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

        public static readonly string[] LoadoutNames = { "LONG RANGE", "ALL ROUND" };
        public static readonly string[] DefaultLoadoutRifles = { "mag338", "bolt65cm" };

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
            new OperatorInfo { Name = "UNKNOWN", Unit = 2, Bio = "A season operator, still classified.", Locked = "Reach Battle Pass tier 50 to unlock this operator." },
        };

        public static readonly string[] Outfits = { "Estuary standard", "Night ops", "Saltmarsh ghillie", "Season outfit" };
        /// <summary>The last outfit is a season reward.</summary>
        public const int LockedOutfit = 3;

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
        /// <summary>Finishes from here on are premium pass rewards.</summary>
        public const int FirstLockedFinish = 4;

        // ---- battle pass ---------------------------------------------------

        public const int PassTiers = 50;

        /// <summary>The free track: silver most tiers, gold every fifth.</summary>
        public static PassReward FreeReward(int tier) =>
            tier % 5 == 0 ? new PassReward(0, 50 + tier * 2) : new PassReward(250 + tier * 50, 0);
    }
}
