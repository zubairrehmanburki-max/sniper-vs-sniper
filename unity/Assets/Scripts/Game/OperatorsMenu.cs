using System;
using SniperDuel.Game.Ui;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Operators screen: unit filter tabs (other units dim), the operator tiles (tap to
    /// select and equip), their career numbers, the outfit swatches (saved per operator) and the
    /// PREVIEW / EQUIPPED buttons.
    /// </summary>
    public sealed class OperatorsMenu
    {
        readonly UiScreen s;
        int unit;

        public OperatorsMenu(UiScreen screen, Action back)
        {
            s = screen;
            s.On("back", back);
            for (int i = 0; i < Catalog.Units.Length; i++)
            {
                int k = i;
                s.On("unit" + i, () => { unit = k; Render(); });
            }
            for (int i = 0; i < Catalog.Operators.Length; i++)
            {
                int k = i;
                s.On("op" + i, () => Select(k));
            }
            for (int i = 0; i < Catalog.Outfits.Length; i++)
            {
                int k = i;
                s.On("outfit" + i, () => Outfit(k));
            }
            s.On("preview", () => Toast.Show(s, "PREVIEW", "A turntable preview arrives with the final scanned operator models. Your choice already shows in the lobby and on the match cards."));
            s.On("equip", back);
        }

        public void Refresh()
        {
            unit = 0;
            Toast.Hide(s);
            Render();
        }

        void Select(int k)
        {
            var op = Catalog.Operators[k];
            if (op.Locked != null)
            {
                Toast.Show(s, "LOCKED", op.Locked);
                return;
            }
            Profile.Operator = k;
            Render();
        }

        void Outfit(int k)
        {
            if (k == Catalog.LockedOutfit)
            {
                Toast.Show(s, "LOCKED", "Season outfits come from the Battle Pass premium track, which opens with the store.");
                return;
            }
            Profile.SetOutfit(Profile.Operator, k);
            Toast.Show(s, "OUTFIT", $"{Catalog.Outfits[k]} for {Catalog.Operators[Profile.Operator].Name}.", 2f);
            Render();
        }

        void Render()
        {
            int cur = Profile.Operator;
            for (int i = 0; i < Catalog.Units.Length; i++)
                if (s.Has("unit" + i)) Skin.Tab(s["unit" + i], i == unit);

            for (int i = 0; i < Catalog.Operators.Length; i++)
            {
                if (!s.Has("op" + i)) continue;
                var e = s["op" + i];
                var op = Catalog.Operators[i];
                bool on = i == cur;
                Skin.Border(e, on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, op.Locked != null ? 0.12f : 0.18f), on ? 2 : 1);
                // The filter dims rather than hides, so the grid keeps its shape.
                e.style.opacity = unit == 0 || op.Unit == unit ? 1f : 0.3f;
            }

            var o = Catalog.Operators[cur];
            s.Text("unit", Catalog.Units[o.Unit]);
            s.Text("name", o.Name);
            s.Text("bio", o.Bio);
            int duels = Profile.Count($"op{cur}.duels"), wins = Profile.Count($"op{cur}.wins");
            float longest = Profile.Best($"op{cur}.longest");
            s.Text("duels", duels.ToString("#,0"));
            s.Text("winrate", duels > 0 ? $"{Mathf.RoundToInt(100f * wins / duels)}%" : "—");
            s.Text("longest", longest > 0 ? $"{longest:#,0} m" : "—");

            int outfit = Profile.Outfit(cur);
            for (int i = 0; i < Catalog.Outfits.Length; i++)
            {
                if (!s.Has("outfit" + i)) continue;
                bool on = i == outfit;
                Skin.Border(s["outfit" + i], on ? Skin.Amber : new Color(Skin.Paper.r, Skin.Paper.g, Skin.Paper.b, i == Catalog.LockedOutfit ? 0.12f : 0.2f), on ? 2 : 1);
            }
            s.Text("equip", "EQUIPPED");
        }
    }
}
