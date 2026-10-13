using System;
using SniperDuel.Game.Ui;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Loadout screen: two loadout tabs, the rifle list (selection, lock, stats from
    /// GameConfig plus fitted parts), the six part tiles that open the gunsmith on that slot, and
    /// EQUIP, which saves the rifle to the loadout and makes it the one taken into duels.
    /// </summary>
    public sealed class LoadoutMenu
    {
        const int Rows = 4;

        readonly UiScreen s;
        readonly GameConfig cfg;
        int loadout;
        string selected = "";

        /// <param name="gunsmith">Opens the gunsmith on a rifle and slot.</param>
        public LoadoutMenu(UiScreen screen, GameConfig cfg, Action back, Action<string, int> gunsmith)
        {
            s = screen;
            this.cfg = cfg;
            s.On("back", back);
            for (int i = 0; i < Catalog.LoadoutNames.Length; i++)
            {
                int k = i;
                s.On("preset" + i, () => { loadout = k; selected = Profile.LoadoutRifle(k); Render(); });
            }
            s.On("preset-new", () => Toast.Show(s, "TWO LOADOUTS FOR NOW", "More loadout slots arrive with online accounts. Switch between LONG RANGE and ALL ROUND."));
            for (int i = 0; i < Rows; i++)
            {
                int k = i;
                s.On("rifle" + i, () =>
                {
                    if (k >= cfg.Rifles.Count) return;
                    selected = cfg.Rifles[k].Id;
                    Render();
                });
            }
            for (int i = 0; i < Catalog.Slots.Length; i++)
            {
                int k = i;
                s.On("part" + i, () => gunsmith(Current.Id, k));
            }
            s.On("equip", Equip);
        }

        RifleConfig Current => cfg.Rifles.Find(r => r.Id == selected) ?? cfg.Rifles[0];

        /// <summary>Called when the screen opens: show the active loadout and its rifle.</summary>
        public void Refresh()
        {
            loadout = Profile.ActiveLoadout;
            selected = Profile.LoadoutRifle(loadout);
            Render();
        }

        void Equip()
        {
            var r = Current;
            int need = Catalog.RifleUnlockLevel(r.Id);
            if (Profile.Level < need)
            {
                Toast.Show(s, "LOCKED", $"{r.Name} unlocks at level {need}. You're level {Profile.Level}: win duels to level up.");
                return;
            }
            Profile.SetLoadoutRifle(loadout, r.Id);
            Profile.ActiveLoadout = loadout;
            Toast.Show(s, "EQUIPPED", $"{Catalog.LoadoutNames[loadout]} · {r.Name}. You'll take it into your next duel.", 2.5f);
            Render();
        }

        void Render()
        {
            if (cfg.Rifles.Count == 0) return;
            for (int i = 0; i < Catalog.LoadoutNames.Length; i++)
                if (s.Has("preset" + i)) Skin.Tab(s["preset" + i], i == loadout);

            var cur = Current;
            string equipped = Profile.LoadoutRifle(loadout);
            for (int i = 0; i < Rows; i++)
            {
                s.Show("rifle" + i, i < cfg.Rifles.Count);
                if (i >= cfg.Rifles.Count || !s.Has("rifle" + i)) continue;
                var r = cfg.Rifles[i];
                bool on = r.Id == cur.Id, locked = Profile.Level < Catalog.RifleUnlockLevel(r.Id);
                Skin.Card(s["rifle" + i], on);
                s.Text("rifle" + i + "name", r.Name.Replace("Bolt-action 6.5", "6.5").ToUpperInvariant());
                s.Color("rifle" + i + "name", on ? Skin.Amber : locked ? Skin.Locked : Skin.Paper);
                s.Text("rifle" + i + "sub", locked ? $"Unlocks at level {Catalog.RifleUnlockLevel(r.Id)}"
                    : r.Id == equipped ? "Equipped · " + Class(r) : Class(r) + " · " + Bullet(r));
                s.Color("rifle" + i + "sub", on ? Skin.Paper : locked ? Skin.Locked : Skin.Muted);
                s.Show("rifle" + i + "lock", locked);
            }

            s.Text("title", cur.Name.Replace("Bolt-action 6.5", "6.5").ToUpperInvariant());
            string caliber = cur.Cartridge.Split('/')[0].Trim().ToUpperInvariant();
            s.Text("subtitle", $"{caliber} · {Bullet(cur)} · {cur.DragModel} {cur.Bc:0.000} · LEVEL {Profile.WeaponLevel(cur.Id)} / {Profile.MaxWeaponLevel}");

            // Parts fitted to this rifle.
            for (int i = 0; i < Catalog.Slots.Length; i++)
            {
                var slot = Catalog.Slots[i];
                var p = slot.Options[Profile.Part(cur.Id, i)];
                s.Text("part" + i + "label", slot.Name);
                s.Text("part" + i + "name", p.Name);
                bool empty = p.Name == "EMPTY";
                if (s.Has("part" + i)) Skin.Card(s["part" + i], false, new Color(10 / 255f, 12 / 255f, 11 / 255f, empty ? 0.4f : 0.66f));
                s.Color("part" + i + "name", empty ? Skin.Muted : Skin.Paper);
                s.Color("part" + i + "label", Skin.Muted);
            }

            // Ballistics: the rifle's numbers with the parts' effects.
            var (dv, wind, recoil, _, _) = Profile.PartTotals(cur.Id);
            double v = cur.MuzzleVelocityMps + dv;
            double energy = 0.5 * cur.MassKg * v * v;
            Stat(0, "MUZZLE VELOCITY", $"{v:0} m/s", (float)(v / 1000), dv != 0 ? dv : (int?)null);
            Stat(1, $"BALLISTIC COEF. {cur.DragModel}", $"{cur.Bc:0.000}", (float)(cur.Bc / 0.4), null);
            Stat(2, "MUZZLE ENERGY", $"{energy / 1000:0.0} kJ", (float)(energy / 16000), null);
            Stat(3, "WIND RESISTANCE", Pct(wind), 0.5f + wind / 20f, wind);
            Stat(4, "RECOIL RECOVERY", Pct(recoil), 0.4f + recoil / 50f, recoil);
            s.Text("sweet", $"{cur.SweetSpotM[0]:#,0}–{cur.SweetSpotM[1]:#,0} m");

            bool lockedCur = Profile.Level < Catalog.RifleUnlockLevel(cur.Id);
            bool isEquipped = cur.Id == equipped && loadout == Profile.ActiveLoadout;
            s.Text("equip", lockedCur ? $"LEVEL {Catalog.RifleUnlockLevel(cur.Id)}" : isEquipped ? "EQUIPPED" : "EQUIP");
            if (s.Has("equip")) s["equip"].style.opacity = lockedCur || isEquipped ? 0.6f : 1f;
        }

        void Stat(int i, string label, string value, float bar, int? delta)
        {
            s.Text("label" + i, label);
            s.Text("stat" + i, value);
            s.Color("stat" + i, delta is int d && d != 0 ? d > 0 ? Skin.Amber : Skin.Red : Skin.Paper);
            s.Fill("bar" + i, bar);
        }

        static string Pct(int v) => v == 0 ? "±0%" : $"{v:+0;−0}%";

        static string Class(RifleConfig r) => r.Class switch
        {
            "starter" => "Starter",
            "precision" => "Precision",
            "magnum" => "Magnum",
            "anti-materiel" => "Anti-materiel",
            _ => r.Class,
        };

        /// <summary>"175 gr match" from "7.62x51 / 175 gr match (M118LR class)".</summary>
        static string Bullet(RifleConfig r)
        {
            string c = r.Cartridge;
            int slash = c.IndexOf('/');
            if (slash >= 0) c = c.Substring(slash + 1).Trim();
            int paren = c.IndexOf('(');
            if (paren >= 0) c = c.Substring(0, paren).Trim();
            return c;
        }
    }
}
