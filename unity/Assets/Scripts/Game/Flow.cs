using System.Collections;
using SniperDuel.Game.Ui;
using SniperDuel.Game.World;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Moves between screens: title, lobby, map select, matchmaking, the versus card, the duel and
    /// the results, with the side screens (settings, loadout, career...) one tap from the lobby.
    /// Matches are against the bot until the online server is hosted.
    /// </summary>
    public sealed class Flow : MonoBehaviour
    {
        GameConfig cfg = null!;
        UiHost ui = null!;
        CameraRig rig = null!;
        Arena? arena;
        Duel? duel;
        string mapId = "twin-bridges";
        BotDifficulty difficulty = BotDifficulty.Medium;
        string rifleId = "";
        const string PlayerName = "YOU";

        public static Flow Create(GameConfig cfg, UiHost ui, CameraRig rig, Arena? arena)
        {
            var f = new GameObject("Flow").AddComponent<Flow>();
            f.cfg = cfg;
            f.ui = ui;
            f.rig = rig;
            f.arena = arena;
            if (arena != null) f.mapId = arena.Map.Id;
            f.rifleId = cfg.Rifles.Count > 0 ? cfg.Rifles[0].Id : "";
            f.Wire();
            return f;
        }

        void Wire()
        {
            var title = ui.Get("Title");
            title.On("deploy", () => Go("Lobby"));
            title.On("settings", () => Go("Settings"));
            title.On("account", () => Go("Career"));
            title.Text("version", "v" + Application.version);
            title.Show("loading", false);

            var lobby = ui.Get("Lobby");
            lobby.On("play", () => Go("Play"));
            lobby.On("quickplay", () => StartCoroutine(Matchmake()));
            lobby.On("loadout", () => Go("Loadout"));
            lobby.On("operators", () => Go("Operators"));
            lobby.On("pass", () => Go("Pass"));
            lobby.On("career", () => Go("Career"));
            lobby.On("settings", () => Go("Settings"));
            foreach (var soon in new[] { "friends", "mail", "store", "invite", "mode" })
                lobby.On(soon, () => { });

            var play = ui.Get("Play");
            play.On("back", () => Go("Lobby"));
            play.On("find", () => StartCoroutine(Matchmake()));
            for (int i = 0; i < 6 && i < Maps.Ids.Length; i++)
            {
                string id = Maps.Ids[i];
                play.On("map" + i, () => SelectMap(id));
            }
            foreach (var tab in new[] { "tab-ranked", "tab-quick", "tab-private", "tab-bot", "tab-training" })
                play.On(tab, () => { });
            SelectMap(mapId);

            var mm = ui.Get("Matchmaking");
            mm.On("cancel", () => { StopAllCoroutines(); Go("Play"); });
            mm.On("bot", () => { StopAllCoroutines(); StartCoroutine(Versus()); });

            var loadout = ui.Get("Loadout");
            loadout.On("back", () => Go("Lobby"));
            loadout.On("equip", () => Go("Lobby"));
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                loadout.On("rifle" + i, () => { if (k < cfg.Rifles.Count) rifleId = cfg.Rifles[k].Id; });
            }

            foreach (var side in new[] { "Settings", "Operators", "Career", "Pass", "Gunsmith", "HudLayout" })
                ui.Get(side).On("back", () => Go("Lobby"));

            var results = ui.Get("Results");
            results.On("continue", () => Go("Lobby"));
            results.On("rematch", () => StartCoroutine(Versus()));
            results.On("bulletcam", () => { });
        }

        void Go(string screen)
        {
            if (duel != null && screen != "Hud" && screen != "Scope")
            {
                Destroy(duel.gameObject);
                duel = null;
            }
            rig.Current = CameraRig.Mode.Menu;
            ui.Show(screen);
        }

        void SelectMap(string id)
        {
            mapId = id;
            var map = Maps.ById(id);
            var play = ui.Get("Play");
            play.Text("title", map.Name.ToUpperInvariant());
            play.Text("blurb", map.Blurb ?? "");
            play.Text("range", $"{map.LaneLength:0} m");
            var c = map.Climate;
            play.Text("wind", $"{c.WindSpeedMps[0]:0}–{c.WindSpeedMps[1]:0} m/s");
        }

        void EnsureArena()
        {
            if (arena != null && arena.Map.Id == mapId) return;
            if (arena != null) Destroy(arena.Root.gameObject);
            arena = Arena.Build(Maps.ById(mapId));
            rig.MenuFocus = Library.ToUnity(arena.Map.Spots.A[0]);
        }

        IEnumerator Matchmake()
        {
            Go("Matchmaking");
            var mm = ui.Get("Matchmaking");
            mm.Text("headline", "FINDING OPPONENT");
            float t = 0;
            while (t < 3.5f)
            {
                t += Time.deltaTime;
                mm.Text("wait", $"0:{Mathf.FloorToInt(t):00}");
                if (mm.Has("radar")) mm["radar"].style.rotate = new UnityEngine.UIElements.Rotate(UnityEngine.UIElements.Angle.Degrees(t * 120));
                yield return null;
            }
            // Nobody online yet: the bot takes the slot.
            yield return Versus();
        }

        IEnumerator Versus()
        {
            Go("Versus");
            EnsureArena();
            var map = arena!.Map;
            var v = ui.Get("Versus");
            v.Text("name", PlayerName);
            v.Text("enemy", difficulty == BotDifficulty.Hard ? "GHOSTWIND" : difficulty == BotDifficulty.Easy ? "RECRUIT" : "VIPER");
            v.Text("map", map.Name.ToUpperInvariant());
            v.Text("range", $"{map.LaneLength:0} m");
            v.Text("wind", $"{map.Climate.WindSpeedMps[0]:0}–{map.Climate.WindSpeedMps[1]:0} m/s");
            for (int n = 3; n > 0; n--)
            {
                v.Text("countdown", n.ToString());
                yield return new WaitForSeconds(1);
            }
            duel = Duel.Begin(cfg, ui, rig, arena, PlayerName, rifleId, difficulty);
            duel.Finished += ShowResults;
        }

        void ShowResults(MatchState s)
        {
            bool won = s.Winner == Side.A;
            var r = ui.Get("Results");
            r.Text("title", s.Forfeit && !won ? "FORFEIT" : won ? "VICTORY" : "DEFEAT");
            r.Color("title", won ? new Color(0.95f, 0.70f, 0.24f) : new Color(0.90f, 0.28f, 0.23f));
            r.Text("score", $"{s.Players.A.Kills} – {s.Players.B.Kills}");
            r.Text("versus", $"VS {s.Players.B.Name} · {Maps.ById(s.MapId).Name.ToUpperInvariant()}");
            int mine = 0, hits = 0;
            double energy = 0;
            foreach (var shot in s.Shots)
            {
                if (shot.Shooter != Side.A) continue;
                mine++;
                if (shot.Verdict != Verdict.Miss) { hits++; energy = System.Math.Max(energy, shot.Energy); }
            }
            r.Text("accuracy", $"{hits} / {mine}");
            r.Text("impact", energy > 0 ? $"{energy / 1000:0.0} kJ" : "—");
            r.Text("windRead", "—");
            // One line per round: how it was decided.
            for (int i = 0; i < 3; i++)
            {
                var decisive = s.Shots.FindLast(x => x.Round == i && x.Verdict == Verdict.Kill);
                r.Show("row" + i, decisive != null);
                if (decisive == null) continue;
                bool me = decisive.Shooter == Side.A;
                r.Text("row" + i + "round", "R" + (i + 1));
                r.Text("row" + i + "text", me ? $"{decisive.Zone} hit · shot {decisive.ShotIndex + 1}" : $"Hit by enemy · shot {decisive.ShotIndex + 1}");
                r.Text("row" + i + "value", $"{decisive.Distance:0} m");
            }
            Go("Results");
        }
    }
}
