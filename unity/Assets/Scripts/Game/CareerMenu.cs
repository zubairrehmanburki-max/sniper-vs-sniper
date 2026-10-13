using System;
using System.Collections.Generic;
using System.Linq;
using SniperDuel.Game.Ui;
using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Career screen from Profile: RANKED / ALL MODES stat tiles, a MEDALS view, the
    /// rating chart (or the match history list), hit rates by rifle, win rates on the most played
    /// maps and the medal list.
    /// </summary>
    public sealed class CareerMenu
    {
        const int Bars = 30;

        readonly UiScreen s;
        readonly GameConfig cfg;
        readonly List<VisualElement> bars = new List<VisualElement>();
        int tab;
        bool history;

        public CareerMenu(UiScreen screen, GameConfig cfg, Action back)
        {
            s = screen;
            this.cfg = cfg;
            s.On("back", back);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                s.On("tab" + i, () => { tab = k; Render(); });
            }
            s.On("history", () => { history = !history; Render(); });
            if (s.Has("chart"))
            {
                var chart = s["chart"];
                for (int i = 0; i < Bars; i++)
                {
                    var b = new VisualElement { name = "bar", pickingMode = PickingMode.Ignore };
                    b.style.flexGrow = 1;
                    b.style.height = 0;
                    if (i > 0) b.style.marginLeft = 2;
                    chart.Add(b);
                    bars.Add(b);
                }
            }
        }

        public void Refresh()
        {
            history = false;
            Render();
        }

        void Render()
        {
            for (int i = 0; i < 3; i++)
                if (s.Has("tab" + i)) Skin.Tab(s["tab" + i], i == tab);

            if (tab == 2) Medals();
            else Stats(tab == 0 ? "ranked" : "all");
            Chart();
            Rifles();
            MapRows();
            for (int i = 0; i < Profile.MedalNames.Length; i++)
            {
                bool got = Profile.Medal(i);
                if (s.Has("medal" + i)) s["medal" + i].style.opacity = got ? 1f : 0.45f;
                s.Text("medal" + i + "text", got ? Profile.MedalRules[i] : "Locked · " + Profile.MedalRules[i]);
            }
        }

        void Stats(string sc)
        {
            int duels = Profile.Count(sc + ".duels"), wins = Profile.Count(sc + ".wins"), losses = Profile.Count(sc + ".losses");
            int shots = Profile.Count(sc + ".shots"), hits = Profile.Count(sc + ".hits");
            int rounds = Profile.Count(sc + ".rounds"), first = Profile.Count(sc + ".firstHits");
            float longest = Profile.Best(sc + ".longest");
            Tile(0, "WINS – LOSSES", $"{wins} – {losses}");
            Tile(1, "WIN RATE", duels > 0 ? Pct(wins, duels) : "—");
            Tile(2, "LONGEST HIT", longest > 0 ? $"{longest:#,0} m" : "—", longest > 0);
            Tile(3, "FIRST-SHOT HITS", rounds > 0 ? Pct(first, rounds) : "—");
            Tile(4, "ACCURACY", shots > 0 ? Pct(hits, shots) : "—");
            Tile(5, "HEADSHOTS", Profile.Count(sc + ".heads").ToString("#,0"));
        }

        void Medals()
        {
            int got = Enumerable.Range(0, Profile.MedalNames.Length).Count(Profile.Medal);
            float longest = Profile.Best("all.longest");
            Tile(0, "MEDALS", $"{got} / {Profile.MedalNames.Length}", got > 0);
            Tile(1, "KILOMETRE CLUB", Profile.Medal(0) ? "EARNED" : $"{Mathf.Min(longest, 1000):#,0} m", Profile.Medal(0));
            Tile(2, "WIND WHISPERER", Profile.Medal(1) ? "EARNED" : "—", Profile.Medal(1));
            Tile(3, "ONE AND DONE", Profile.Medal(2) ? "EARNED" : "—", Profile.Medal(2));
            Tile(4, "DUELS", Profile.Count("all.duels").ToString("#,0"));
            Tile(5, "LEVEL", Profile.Level.ToString());
        }

        void Tile(int i, string label, string value, bool highlight = false)
        {
            s.Text("k" + i, label);
            s.Text("v" + i, value);
            s.Color("v" + i, highlight ? Skin.Amber : Skin.Paper);
        }

        void Chart()
        {
            s.Show("chart", !history);
            s.Show("history", history);
            s.Text("historyBtn", history ? "RATING CHART" : "MATCH HISTORY");
            if (history)
            {
                var h = Profile.History;
                s.Text("chartTitle", "RECENT MATCHES");
                s.Text("chartValue", h.Count > 0 ? $"{h.Count} SHOWN" : "");
                for (int i = 0; i < 6; i++)
                {
                    s.Text("hist" + i, i < h.Count ? h[i].text : i == 0 ? "No duels yet. Play one from the lobby." : "");
                    s.Text("hist" + i + "v", i < h.Count ? h[i].detail : "");
                    s.Color("hist" + i, i < h.Count && h[i].text.StartsWith("W") ? Skin.Paper : Skin.Muted);
                }
                return;
            }
            var r = Profile.RatingHistory;
            s.Text("chartTitle", $"RATING · LAST {Mathf.Max(0, r.Length - 1)} RANKED DUELS");
            int peak = r.Length > 0 ? r.Max() : Profile.Rating;
            s.Text("chartValue", $"{Profile.Rating:#,0} · PEAK {Mathf.Max(peak, Profile.Rating):#,0}");
            int lo = r.Length > 0 ? r.Min() - 20 : 0, hi = r.Length > 0 ? r.Max() + 20 : 1;
            for (int i = 0; i < bars.Count; i++)
            {
                // Newest on the right.
                int idx = r.Length - bars.Count + i;
                var b = bars[i];
                if (idx < 0 || r.Length < 2) { b.style.height = 2; b.style.backgroundColor = Skin.Line; continue; }
                float f = Mathf.Clamp01((r[idx] - lo) / (float)Mathf.Max(1, hi - lo));
                b.style.height = Length.Percent(8 + 92 * f);
                b.style.backgroundColor = idx == r.Length - 1 ? Skin.Amber : new Color(Skin.Amber.r, Skin.Amber.g, Skin.Amber.b, 0.45f);
            }
        }

        void Rifles()
        {
            for (int i = 0; i < 4; i++)
            {
                if (i >= cfg.Rifles.Count) { s.Text("rifle" + i, ""); s.Text("rifle" + i + "v", ""); s.Fill("rifle" + i + "bar", 0); continue; }
                var rf = cfg.Rifles[i];
                int shots = Profile.Count($"rifle.{rf.Id}.shots"), hits = Profile.Count($"rifle.{rf.Id}.hits");
                s.Text("rifle" + i, rf.Name.Replace("Bolt-action 6.5", "6.5"));
                s.Text("rifle" + i + "v", shots > 0 ? Pct(hits, shots) : "—");
                s.Fill("rifle" + i + "bar", shots > 0 ? hits / (float)shots : 0);
                if (s.Has("rifle" + i + "bar")) s["rifle" + i + "bar"].style.backgroundColor = rf.Id == Profile.Rifle(cfg) ? Skin.Amber : Skin.Paper;
            }
        }

        void MapRows()
        {
            // The four most played maps (picker order breaks ties).
            var ids = Maps.Ids.Where(id => id != "training-lane")
                .Select((id, n) => (id, n, duels: Profile.Count($"map.{id}.duels")))
                .OrderByDescending(m => m.duels).ThenBy(m => m.n).Take(4).ToList();
            for (int i = 0; i < 4; i++)
            {
                if (i >= ids.Count) { s.Text("map" + i, ""); s.Text("map" + i + "v", ""); continue; }
                var (id, _, duels) = ids[i];
                s.Text("map" + i, Maps.ById(id).Name);
                s.Text("map" + i + "v", duels > 0 ? Pct(Profile.Count($"map.{id}.wins"), duels) : "—");
            }
        }

        static string Pct(int n, int of) => $"{Mathf.RoundToInt(100f * n / Mathf.Max(1, of))}%";
    }
}
