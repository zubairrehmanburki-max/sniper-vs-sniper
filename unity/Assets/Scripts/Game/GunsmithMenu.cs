using System;
using SniperDuel.Game.Ui;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Gunsmith screen for one rifle: the six slot tabs, the four choices in the open
    /// slot (tap to inspect, EQUIP to fit; some need a weapon level), the effect panel, the
    /// weapon level bar and the finish swatches (tap to apply).
    /// </summary>
    public sealed class GunsmithMenu
    {
        const int Choices = 4;

        readonly UiScreen s;
        readonly GameConfig cfg;
        string rifle = "";
        int slot = 1, option;

        public GunsmithMenu(UiScreen screen, GameConfig cfg, Action back)
        {
            s = screen;
            this.cfg = cfg;
            s.On("back", back);
            for (int i = 0; i < Catalog.Slots.Length; i++)
            {
                int k = i;
                s.On("slot" + i, () => { slot = k; option = Profile.Part(rifle, k); Render(); });
            }
            for (int i = 0; i < Choices; i++)
            {
                int k = i;
                s.On("opt" + i, () => { option = k; Render(); });
            }
            s.On("equip", Equip);
            for (int i = 0; i < Catalog.Finishes.Length; i++)
            {
                int k = i;
                s.On("fin" + i, () => Finish(k));
            }
        }

        /// <summary>Opens on a rifle, with a slot selected.</summary>
        public void Open(string rifleId, int slotIndex)
        {
            rifle = rifleId;
            slot = Mathf.Clamp(slotIndex, 0, Catalog.Slots.Length - 1);
            option = Profile.Part(rifle, slot);
            Toast.Hide(s);
            Render();
        }

        PartInfo Option => Catalog.Slots[slot].Options[option];
        bool Unlocked(PartInfo p) => Profile.WeaponLevel(rifle) >= p.Level;

        void Equip()
        {
            var p = Option;
            if (!Unlocked(p))
            {
                Toast.Show(s, "LOCKED", $"{p.Name} needs weapon level {p.Level}. Win duels with this rifle to level it up.");
                return;
            }
            Profile.SetPart(rifle, slot, option);
            Render();
        }

        void Finish(int k)
        {
            if (k >= Catalog.FirstLockedFinish)
            {
                Toast.Show(s, Catalog.Finishes[k], "A premium Battle Pass finish. The premium track opens with the store.");
                return;
            }
            Profile.SetFinish(rifle, k);
            Toast.Show(s, "FINISH APPLIED", $"{Catalog.Finishes[k]} on the {RifleName()}.", 2f);
            Render();
        }

        string RifleName() => cfg.Rifles.Find(r => r.Id == rifle)?.Name ?? rifle;

        void Render()
        {
            var info = Catalog.Slots[slot];
            int fitted = Profile.Part(rifle, slot);
            s.Text("rifle", RifleName().Replace("Bolt-action 6.5", "6.5").ToUpperInvariant());
            s.Text("count", $"{Profile.PartTotals(rifle).fitted} / {Catalog.Slots.Length}");
            s.Text("slot", info.Name);

            for (int i = 0; i < Catalog.Slots.Length; i++)
            {
                if (!s.Has("slot" + i)) continue;
                bool on = i == slot;
                Skin.Card(s["slot" + i], on, new Color(10 / 255f, 12 / 255f, 11 / 255f, 0.66f));
                s.Text("slot" + i + "label", Catalog.Slots[i].Name);
                s.Color("slot" + i + "label", on ? Skin.Amber : Skin.Muted);
                s.Text("slot" + i + "name", Catalog.Slots[i].Options[Profile.Part(rifle, i)].Name);
            }

            for (int i = 0; i < Choices; i++)
            {
                bool exists = i < info.Options.Length;
                s.Show("opt" + i, exists);
                if (!exists || !s.Has("opt" + i)) continue;
                var p = info.Options[i];
                bool locked = !Unlocked(p), on = i == option, isFitted = i == fitted;
                var e = s["opt" + i];
                Skin.Border(e, on ? (isFitted ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.5f)) : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, locked ? 0.1f : 0.16f));
                e.style.backgroundColor = on ? (isFitted ? Skin.AmberTint : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.08f)) : Skin.Panel;
                s.Text("opt" + i + "name", p.Name);
                s.Text("opt" + i + "note", locked ? $"Weapon level {p.Level}" : isFitted ? "Equipped" : p.Note);
                s.Color("opt" + i + "name", locked ? Skin.Locked : Skin.Paper);
                s.Color("opt" + i + "note", locked ? Skin.Locked : Skin.Muted);
                s.Show("opt" + i + "check", isFitted);
                s.Show("opt" + i + "lock", locked);
            }

            // Effect panel for the option being looked at.
            var o = Option;
            s.Text("detail", o.Name);
            s.Text("detailText", o.Detail);
            Row(0, "Muzzle velocity", o.VelocityMps, " m/s");
            Row(1, "Wind resistance", o.WindPct, "%");
            Row(2, "Recoil recovery", o.RecoilPct, "%");
            s.Text("k3", "Weight");
            s.Text("v3", Mathf.Abs(o.WeightKg) < 0.05f ? "—" : $"{o.WeightKg:+0.0;−0.0} kg");
            s.Color("v3", o.WeightKg > 0.05f ? Skin.Red : o.WeightKg < -0.05f ? Skin.Amber : Skin.Muted);
            bool lockedOpt = !Unlocked(o), fittedOpt = option == fitted;
            s.Text("equip", lockedOpt ? $"LEVEL {o.Level}" : fittedOpt ? "EQUIPPED" : "EQUIP");
            if (s.Has("equip")) s["equip"].style.opacity = lockedOpt || fittedOpt ? 0.6f : 1f;

            // Weapon level.
            int level = Profile.WeaponLevel(rifle), xp = Profile.WeaponXp(rifle);
            s.Text("level", $"WEAPON LEVEL {level}");
            if (level >= Profile.MaxWeaponLevel)
            {
                s.Text("levelXp", "MAX");
                s.Fill("levelBar", 1);
            }
            else
            {
                int into = xp % Profile.XpPerWeaponLevel;
                s.Text("levelXp", $"{into:#,0} / {Profile.XpPerWeaponLevel:#,0} XP");
                s.Fill("levelBar", into / (float)Profile.XpPerWeaponLevel);
            }

            int finish = Profile.Finish(rifle);
            for (int i = 0; i < Catalog.Finishes.Length; i++)
            {
                if (!s.Has("fin" + i)) continue;
                bool on = i == finish;
                Skin.Border(s["fin" + i], on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, 0.2f), on ? 2 : 1);
            }
        }

        void Row(int i, string label, int value, string unit)
        {
            s.Text("k" + i, label);
            s.Text("v" + i, value == 0 ? "—" : $"{value:+0;−0}{unit}");
            s.Color("v" + i, value > 0 ? Skin.Amber : value < 0 ? Skin.Red : Skin.Muted);
        }
    }
}
