using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SniperDuel.Game.Ui;
using SniperDuel.Ballistics;
using SniperDuel.Game.World;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game
{
    /// <summary>
    /// Moves between screens: title, lobby, map select, matchmaking, the versus card, the duel and
    /// the results, with the side screens (settings, loadout, career...) one tap from the lobby.
    /// Every mode is played against the bot until the online server is hosted; the online modes
    /// say so. Finished matches are booked into Profile.
    /// </summary>
    public sealed class Flow : MonoBehaviour
    {
        const string TrainingMap = "training-lane";
        const int MapTiles = 6;
        static readonly string[] ModeTabs = { "tab-ranked", "tab-quick", "tab-private", "tab-bot", "tab-training" };

        GameConfig cfg = null!;
        UiHost ui = null!;
        CameraRig rig = null!;
        Arena? arena;
        Duel? duel;
        string mapId = Maps.DefaultMap;
        GameMode mode;
        SettingsMenu settings = null!;
        LoadoutMenu loadout = null!;
        GunsmithMenu gunsmith = null!;
        OperatorsMenu operators = null!;
        HudLayoutMenu hudLayout = null!;
        CareerMenu career = null!;
        PassMenu pass = null!;
        readonly Dictionary<string, Action> refresh = new Dictionary<string, Action>();

        // The match in progress or just finished.
        GameMode matchMode;
        BotDifficulty matchBot = BotDifficulty.Medium;
        string matchRifle = "";
        ShotOutcome? replay;
        string replayWho = "";

        static string PlayerName => Settings.Callsign;

        public static Flow Create(GameConfig cfg, UiHost ui, CameraRig rig, Arena? arena)
        {
            var f = new GameObject("Flow").AddComponent<Flow>();
            f.cfg = cfg;
            f.ui = ui;
            f.rig = rig;
            f.arena = arena;
            f.mapId = Array.IndexOf(Maps.Ids, Profile.MapId) is int i && i >= 0 && i < MapTiles ? Profile.MapId : Maps.DefaultMap;
            f.mode = Profile.Mode;
            f.Wire();
            return f;
        }

        /// <summary>The bot the current mode plays: your pick in VS BOT and TRAINING, matched to your rating otherwise.</summary>
        BotDifficulty Bot => Catalog.Online(mode)
            ? Profile.Rating < 1200 ? BotDifficulty.Easy : Profile.Rating < 1400 ? BotDifficulty.Medium : BotDifficulty.Hard
            : Profile.Difficulty;

        string MatchMap => mode == GameMode.Training ? TrainingMap : mapId;

        void Wire()
        {
            var title = ui.Get("Title");
            title.On("deploy", () => Go("Lobby"));
            title.On("settings", () => Go("Settings"));
            title.On("account", () => { settings.OpenAccount(); Go("Settings", false); });
            title.Text("version", Catalog.Version + " · OFFLINE");
            title.Show("loading", false);

            WireLobby();
            WirePlay();

            settings = new SettingsMenu(ui.Get("Settings"), () => Go("Lobby"));
            refresh["Settings"] = settings.Refresh;
            loadout = new LoadoutMenu(ui.Get("Loadout"), cfg, () => Go("Lobby"), OpenGunsmith);
            refresh["Loadout"] = loadout.Refresh;
            gunsmith = new GunsmithMenu(ui.Get("Gunsmith"), cfg, () => Go("Loadout", false));
            operators = new OperatorsMenu(ui.Get("Operators"), () => Go("Lobby"));
            refresh["Operators"] = operators.Refresh;
            hudLayout = new HudLayoutMenu(ui.Get("HudLayout"), () => Go("Lobby"));
            refresh["HudLayout"] = hudLayout.Refresh;
            career = new CareerMenu(ui.Get("Career"), cfg, () => Go("Lobby"));
            refresh["Career"] = career.Refresh;
            pass = new PassMenu(ui.Get("Pass"), () => Go("Lobby"));
            refresh["Pass"] = pass.Refresh;

            var results = ui.Get("Results");
            results.On("continue", () => Go("Lobby"));
            results.On("rematch", () => StartCoroutine(Versus()));
            results.On("bulletcam", Replay);
        }

        /// <summary>Fills a screen with live data without opening it (the CI screenshot walk).</summary>
        public void Preview(string screen)
        {
            if (screen == "Gunsmith") gunsmith.Open(Profile.Rifle(cfg), 1);
            if (screen == "Versus" && arena != null)
            {
                matchMode = mode;
                matchBot = Bot;
                matchRifle = Profile.Rifle(cfg);
                ShowVersus(arena.Map, 0x5eed);
                ui.Get("Versus").Text("countdown", "3");
            }
            if (refresh.TryGetValue(screen, out var r)) r();
        }

        void Go(string screen, bool refreshScreen = true)
        {
            if (duel != null && screen != "Hud" && screen != "Scope")
            {
                Destroy(duel.gameObject);
                duel = null;
            }
            rig.Current = CameraRig.Mode.Menu;
            Settings.SetInMenus(true);
            if (refreshScreen && refresh.TryGetValue(screen, out var r)) r();
            ui.Show(screen);
        }

        void OpenGunsmith(string rifleId, int slot)
        {
            gunsmith.Open(rifleId, slot);
            Go("Gunsmith");
        }

        // ---- lobby ---------------------------------------------------------

        void WireLobby()
        {
            var lobby = ui.Get("Lobby");
            refresh["Lobby"] = RenderLobby;
            lobby.On("play", () => Go("Play"));
            lobby.On("mode", () => Go("Play"));
            lobby.On("quickplay", StartMatch);
            lobby.On("loadout", () => Go("Loadout"));
            lobby.On("operators", () => Go("Operators"));
            lobby.On("pass", () => Go("Pass"));
            lobby.On("career", () => Go("Career"));
            lobby.On("hud", () => Go("HudLayout"));
            lobby.On("settings", () => Go("Settings"));
            // Gold is earned on the Battle Pass free track.
            lobby.On("gold", () => Go("Pass"));
            lobby.On("mail", () =>
            {
                var mail = Profile.Mail;
                int unread = Profile.MailUnread;
                Profile.MailRead();
                lobby.Show("mailDot", false);
                // The new messages (or the latest one), newest first.
                var shown = mail.Take(Mathf.Clamp(unread, 1, 3)).ToList();
                if (shown.Count == 0) return;
                string title = unread > 0 ? $"MAIL · {unread} NEW" : "MAIL";
                string text = string.Join("\n", shown.Select(m => $"{m.title}: {m.text}"));
                Toast.Show(lobby, title, text, 4f + 2f * shown.Count);
            });
            // Daily orders count down to midnight.
            lobby.Root.schedule.Execute(() =>
            {
                if (lobby.Root.style.display == DisplayStyle.Flex) lobby.Text("dailyClock", Clock(Profile.OrdersResetIn));
            }).Every(1000);
        }

        void RenderLobby()
        {
            var l = ui.Get("Lobby");
            Toast.Hide(l);
            int xp = Profile.Xp, rating = Profile.Rating;
            l.Text("name", PlayerName);
            l.Text("level", Profile.Level.ToString());
            l.Fill("xp", Profile.LevelProgress(xp));
            l.Text("rank", Catalog.Rank(rating));
            l.Text("rating", rating.ToString("#,0"));
            l.Text("silver", Profile.Silver.ToString("#,0"));
            l.Text("gold", Profile.Gold.ToString("#,0"));
            l.Show("mailDot", Profile.Mail.Count > 0 && Profile.MailUnread > 0);

            l.Text("modeName", Catalog.ModeNames[(int)mode]);
            var map = Maps.ById(MatchMap);
            string tail = mode == GameMode.Training ? "solo targets" : Catalog.Online(mode) ? "Best of 3" : $"{Catalog.DifficultyNames[(int)Profile.Difficulty].ToLowerInvariant()} bot";
            l.Text("mode", $"{map.Name} · {tail}");

            int tier = Profile.PassTier, unclaimed = Profile.FirstUnclaimed();
            l.Show("passNew", unclaimed > 0);
            l.Text("passTier", $"TIER {tier} / {Catalog.PassTiers}");
            l.Fill("passBar", (tier - 1 + Profile.Xp % Profile.XpPerTier / (float)Profile.XpPerTier) / Catalog.PassTiers);
            if (unclaimed > 0)
            {
                var r = Catalog.FreeReward(unclaimed);
                l.Text("passNext", $"Ready to claim: {r.Describe()} (tier {unclaimed})");
            }
            else if (tier < Catalog.PassTiers)
            {
                var r = Catalog.FreeReward(tier + 1);
                l.Text("passNext", $"Next: {r.Describe()} at tier {tier + 1}");
            }
            else l.Text("passNext", "Free track complete");

            l.Text("dailyClock", Clock(Profile.OrdersResetIn));
            for (int i = 0; i < Profile.OrderNames.Length; i++)
            {
                int n = Profile.Order(i), goal = Profile.OrderGoals[i];
                l.Text("order" + i, Profile.OrderNames[i]);
                l.Text("order" + i + "v", $"{n}/{goal}");
                l.Color("order" + i + "v", n >= goal ? Skin.Amber : Skin.Muted);
                l.Fill("order" + i + "bar", n / (float)goal);
            }
        }

        static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

        // ---- play ----------------------------------------------------------

        void WirePlay()
        {
            var play = ui.Get("Play");
            refresh["Play"] = RenderPlay;
            play.On("back", () => Go("Lobby"));
            play.On("find", StartMatch);
            for (int i = 0; i < MapTiles && i < Maps.Ids.Length; i++)
            {
                string id = Maps.Ids[i];
                play.On("map" + i, () =>
                {
                    // Picking a map leaves the fixed training lane for a bot duel there.
                    if (mode == GameMode.Training) SetMode(GameMode.Bot);
                    mapId = id;
                    Profile.MapId = id;
                    RenderPlay();
                });
            }
            for (int i = 0; i < ModeTabs.Length; i++)
            {
                var m = (GameMode)i;
                play.On(ModeTabs[i], () => SetMode(m));
            }
            for (int i = 0; i < 3; i++)
            {
                var d = (BotDifficulty)i;
                play.On("diff-" + i, () => { Profile.Difficulty = d; RenderPlay(); });
            }
            play.On("rotation", () => { Profile.MapRotation = !Profile.MapRotation; RenderPlay(); });
            RenderPlay();
        }

        void SetMode(GameMode m)
        {
            mode = m;
            Profile.Mode = m;
            RenderPlay();
        }

        void RenderPlay()
        {
            var play = ui.Get("Play");
            bool online = Catalog.Online(mode), training = mode == GameMode.Training;
            for (int i = 0; i < ModeTabs.Length; i++)
            {
                if (!play.Has(ModeTabs[i])) continue;
                var e = play[ModeTabs[i]];
                bool on = i == (int)mode;
                Skin.Border(e, on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.2f));
                e.style.backgroundColor = on ? new Color(Skin.Amber.r, Skin.Amber.g, Skin.Amber.b, 0.16f) : new Color(10 / 255f, 12 / 255f, 11 / 255f, 0.5f);
                Skin.Text(e, on ? Skin.Amber : Skin.Paper);
            }
            play.Text("online", online ? Catalog.OnlineSoon.ToUpperInvariant()
                : training ? "OFFLINE · SOLO PRACTICE · NO RATING" : $"OFFLINE · VS BOT · {Catalog.DifficultyNames[(int)Profile.Difficulty]}");
            play.Color("online", online ? Skin.Amber : Skin.Muted);

            for (int i = 0; i < MapTiles; i++)
            {
                if (!play.Has("map" + i)) continue;
                if (i < Maps.Ids.Length)
                {
                    var m = Maps.ById(Maps.Ids[i]);
                    play.Text("map" + i, m.Name.ToUpperInvariant());
                    play.Text("sub" + i, Catalog.MapInfo(m.Id).region);
                }
                bool on = !training && i < Maps.Ids.Length && Maps.Ids[i] == mapId;
                Skin.Border(play["map" + i], on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.2f), on ? 2 : 1);
                play["map" + i].style.opacity = training ? 0.5f : 1f;
                play.Show("check" + i, on);
                play.Color("sub" + i, on ? Skin.Amber : Skin.Muted);
            }

            var map = Maps.ById(MatchMap);
            play.Text("title", map.Name.ToUpperInvariant());
            play.Text("blurb", training ? "Steel plates down a range lane in layered wind. Just you and the targets, no bot and no clock. Nothing here touches your rating or career." : map.Blurb ?? "");
            play.Text("range", Catalog.MapInfo(map.Id).region);
            var c = map.Climate;
            play.Text("wind", $"{c.WindSpeedMps[0]:0}–{c.WindSpeedMps[1]:0} m/s");
            play.Text("light", Catalog.LightName(map.Look.Palette?.Sky));
            play.Text("format", training ? "SOLO" : "BEST OF 3");

            // Online modes may rotate maps; bot modes pick the bot instead.
            play.Show("rotationRow", online);
            play.Show("diffRow", !online && !training);
            Skin.Toggle(play, "rotation", Profile.MapRotation);
            for (int i = 0; i < 3; i++)
                if (play.Has("diff-" + i)) Skin.Segment(play["diff-" + i], i == (int)Profile.Difficulty);
            play.Text("find", online ? "FIND MATCH" : training ? "START TRAINING" : "START DUEL");
        }

        // ---- match ---------------------------------------------------------

        void StartMatch()
        {
            // Online modes rotate maps if asked. Nobody is online until the server is hosted, so every
            // mode goes straight to a bot duel rather than pretending to search.
            if (Catalog.Online(mode) && Profile.MapRotation) mapId = Maps.Ids[UnityEngine.Random.Range(0, Mathf.Min(MapTiles, Maps.Ids.Length))];
            if (mode == GameMode.Training) StartPractice();
            else StartCoroutine(Versus());
        }

        /// <summary>Solo practice: straight onto the range lane with the plates out, no bot and no versus card.</summary>
        void StartPractice()
        {
            matchMode = mode;
            matchRifle = Profile.Rifle(cfg);
            EnsureArena();
            duel = Duel.Begin(Gear.ForMatch(cfg, matchRifle), ui, rig, arena!, PlayerName, matchRifle, Profile.Difficulty, Duel.NewSeed(), solo: true);
            Settings.SetInMenus(false);
            duel.Left += () => Go("Play");
        }

        void EnsureArena()
        {
            string id = MatchMap;
            if (arena != null && arena.Map.Id == id) return;
            if (arena != null) Destroy(arena.Root.gameObject);
            arena = Arena.Build(Maps.ById(id));
            rig.MenuFocus = Library.ToUnity(arena.Map.Spots.A[0]);
        }

        IEnumerator Versus()
        {
            Go("Versus");
            EnsureArena();
            var map = arena!.Map;
            matchMode = mode;
            matchBot = Bot;
            matchRifle = Profile.Rifle(cfg);
            uint seed = Duel.NewSeed();
            ShowVersus(map, seed);
            var v = ui.Get("Versus");
            for (int n = 3; n > 0; n--)
            {
                v.Text("countdown", n.ToString());
                yield return new WaitForSeconds(1);
            }
            duel = Duel.Begin(Gear.ForMatch(cfg, matchRifle), ui, rig, arena, PlayerName, matchRifle, matchBot, seed);
            Settings.SetInMenus(false);
            duel.Finished += ShowResults;
        }

        /// <summary>The versus card, from the match about to be played: both kits, who shoots first, the round 1 wind and air.</summary>
        void ShowVersus(DuelMap map, uint seed)
        {
            var v = ui.Get("Versus");
            string botRifle = Duel.BotRifle(cfg, matchBot, matchRifle);
            var preview = Match.CreateMatch(seed, map.Id, new PerSide<PlayerSetup>(
                new PlayerSetup(PlayerName, matchRifle), new PlayerSetup(Catalog.BotName(matchBot), botRifle, matchBot)), 0, cfg, turnLock: true);
            string RifleName(string id) => cfg.Rifles.Find(r => r.Id == id)?.Name.Replace("Bolt-action 6.5", "6.5") ?? id;

            string spot = Catalog.MapInfo(map.Id).spot;
            v.Text("youSpot", $"{spot} A · YOU");
            v.Text("enemySpot", $"{spot} B · ENEMY");
            int rating = Profile.Rating;
            v.Text("name", PlayerName);
            v.Text("kit", $"{Catalog.Rank(rating)} · {rating:#,0} · {RifleName(matchRifle)}");
            v.Text("enemy", Catalog.BotName(matchBot));
            string botLevel = Catalog.DifficultyNames[(int)matchBot] + " BOT";
            v.Text("enemyKit", matchMode == GameMode.Ranked
                ? $"{botLevel} · {Catalog.BotRating(matchBot):#,0} · {RifleName(botRifle)}"
                : $"{botLevel} · {RifleName(botRifle)}");
            bool meFirst = Match.FirstShooter(preview) == Side.A;
            v.Text("first", meFirst ? "YOU SHOOT FIRST" : "ENEMY SHOOTS FIRST");
            v.Color("first", Skin.Ink);
            if (v.Has("first")) v["first"].style.backgroundColor = meFirst ? Skin.Amber : Skin.Red;

            v.Text("map", map.Name.ToUpperInvariant());
            // Round 1's mid-lane wind, as the clock face a spotter calls it: where it blows from.
            var zones = preview.Conditions.Wind.Zones;
            var mid = zones.Count > 0 ? zones[zones.Count / 2] : new Vec3(0, 0, 0);
            var u = Library.ToUnity(mid);
            double speed = Math.Sqrt(mid.X * mid.X + mid.Z * mid.Z);
            int clock = (int)Math.Round(Mathf.Repeat(Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg + 180, 360) / 30) % 12;
            v.Text("wind", speed < 0.3 ? "Calm" : $"{speed:0.0} m/s · {(clock == 0 ? 12 : clock)} o'clock");
            var air = preview.Conditions.Atmosphere;
            v.Text("air", $"{air.TemperatureC:0} °C · {air.PressureHPa:0} hPa");
            v.Text("light", Catalog.LightName(map.Look.Palette?.Sky));
        }

        // ---- results -------------------------------------------------------

        void ShowResults(MatchState s)
        {
            replay = duel?.LastHit ?? duel?.LastShot;
            replayWho = duel?.LastHit != null ? duel.LastHitWho : duel?.LastShotWho ?? "";
            var report = Profile.Record(s, matchMode, matchRifle, matchBot);

            bool won = s.Winner == Side.A;
            var r = ui.Get("Results");
            r.Text("title", s.Forfeit && !won ? "FORFEIT" : won ? "VICTORY" : "DEFEAT");
            r.Color("title", won ? Skin.Amber : Skin.Red);
            r.Text("score", $"{s.Players.A.Kills} – {s.Players.B.Kills}");
            r.Text("versus", $"VS {s.Players.B.Name} · {Maps.ById(s.MapId).Name.ToUpperInvariant()}");
            var mine = s.Shots.Where(x => x.Shooter == Side.A).ToList();
            int hits = mine.Count(x => x.Verdict != Verdict.Miss);
            double energy = mine.Where(x => x.Verdict != Verdict.Miss).Select(x => x.Energy).DefaultIfEmpty(0).Max();
            r.Text("accuracy", $"{hits} / {mine.Count}");
            r.Text("impact", energy > 0 ? $"{energy / 1000:0.0} kJ" : "—");
            // How far off sideways your misses were: the spotter's windage call.
            var calls = mine.Where(x => x.Verdict == Verdict.Miss && x.Call.HasValue).Select(x => Math.Abs(x.Call!.Value.RightMrad)).ToList();
            r.Text("windRead", calls.Count > 0 ? $"±{calls.Average():0.0} mil" : hits > 0 ? "±0.0 mil" : "—");
            // One line per round: how it was decided.
            for (int i = 0; i < 3; i++)
            {
                var decisive = s.Shots.FindLast(x => x.Round == i && x.Verdict == Verdict.Kill);
                r.Show("row" + i, decisive != null);
                if (decisive == null) continue;
                bool me = decisive.Shooter == Side.A;
                int nth = s.Shots.Count(x => x.Round == i && x.Shooter == decisive.Shooter && x.ShotIndex <= decisive.ShotIndex);
                string shot = nth == 1 ? "first shot" : $"shot {nth}";
                r.Text("row" + i + "round", "R" + (i + 1));
                r.Text("row" + i + "text", me ? $"{decisive.Zone} hit · {shot}" : $"Hit by enemy · {shot}");
                r.Text("row" + i + "value", $"{decisive.Energy / 1000:0.0} kJ");
                if (r.Has("row" + i)) r["row" + i].style.borderLeftColor = me ? Skin.Amber : Skin.Red;
            }

            // XP.
            for (int i = 0; i < 4; i++)
            {
                bool has = i < report.Xp.Count;
                r.Show("xp" + i, has);
                if (!has) continue;
                r.Text("xp" + i + "k", report.Xp[i].Label);
                r.Text("xp" + i + "v", $"+{report.Xp[i].Amount:#,0}");
            }
            r.Text("xpTotal", $"+{report.XpTotal:#,0}");
            int before = Profile.LevelAt(report.XpBefore), after = Profile.Level;
            if (after > before)
            {
                r.Text("lvl", $"LEVEL {after} · LEVEL UP");
                r.Text("lvlNext", $"LEVEL {after + 1}");
                r.Fill("lvlBase", 0);
                r.Fill("lvlGain", Profile.LevelProgress(Profile.Xp));
            }
            else
            {
                float baseF = Profile.LevelProgress(report.XpBefore);
                r.Text("lvl", $"LEVEL {before}");
                r.Text("lvlNext", $"LEVEL {before + 1}");
                r.Fill("lvlBase", baseF);
                r.Fill("lvlGain", Mathf.Min(1 - baseF, report.XpTotal / (float)Profile.XpPerLevel));
            }

            // Rating: ranked moves it; other modes just show where you stand.
            int rating = report.RatingAfter, rank = Catalog.RankIndex(rating);
            r.Text("rankMode", report.Rated ? "RANKED" : Catalog.ModeNames[(int)report.Mode] + " · UNRATED");
            r.Text("rank", Catalog.Ranks[rank]);
            r.Text("rating", rating.ToString("#,0"));
            int delta = report.RatingAfter - report.RatingBefore;
            r.Text("delta", report.Rated ? $"{delta:+0;−0;±0}" : "");
            r.Color("delta", delta >= 0 ? Skin.Amber : Skin.Red);
            if (rank + 1 < Catalog.Ranks.Length)
            {
                int lo = Catalog.RankFloor(rank), hi = Catalog.RankFloor(rank + 1);
                r.Fill("rankBar", rank == 0 ? rating / (float)hi : (rating - lo) / (float)(hi - lo));
                r.Text("rankNext", $"{hi - rating} to {Catalog.Ranks[rank + 1]}");
            }
            else
            {
                r.Fill("rankBar", 1);
                r.Text("rankNext", "Top rank");
            }
            if (!report.Rated) r.Text("rankNext", Catalog.Online(report.Mode) ? "Only ranked duels move rating" : $"Unrated · +{report.Silver} silver");

            r.Show("bulletcam", replay != null);
            r.Text("bulletcam", duel?.LastHit != null ? "WATCH BULLET CAM" : "WATCH LAST SHOT");
            Go("Results");
        }

        /// <summary>Replays your last hit (or last shot) from behind the bullet, then returns to the results.</summary>
        void Replay()
        {
            if (replay == null || arena == null)
            {
                Go("Lobby");
                return;
            }
            Duel.Replay(ui, rig, arena, replay, replayWho, () => Go("Results", false));
        }
    }
}
