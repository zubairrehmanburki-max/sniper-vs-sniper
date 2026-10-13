using System;
using SniperDuel.Game.Ui;
using UnityEngine;

namespace SniperDuel.Game
{
    /// <summary>
    /// Drives the Battle Pass screen: the current tier and XP from Profile, a six-tier window of
    /// the free track around it (tap a reached tier to claim its silver or gold, saved), and the
    /// premium track and purchase buttons, which say the store isn't open yet.
    /// </summary>
    public sealed class PassMenu
    {
        const int Columns = 6;
        const string Cosmetic = "Cosmetic only. Nothing in the pass changes ballistics, sway or damage.";

        readonly UiScreen s;
        int first = 1;
        string? note;

        public PassMenu(UiScreen screen, Action back)
        {
            s = screen;
            s.On("back", back);
            for (int i = 0; i < Columns; i++)
            {
                int k = i;
                s.On("free" + i, () => Claim(first + k));
                s.On("prem" + i, () => Say("Premium rewards unlock with the premium track. The store opens with online play."));
            }
            s.On("buy", () => Say("Tiers can't be bought yet: every duel earns pass XP, 1,000 per tier."));
            s.On("premium", () => Say("The premium track goes on sale when the store opens with online play."));
        }

        public void Refresh()
        {
            note = null;
            // Start the window one before the first unclaimed tier (or the current one).
            int focus = Profile.FirstUnclaimed();
            if (focus == 0) focus = Profile.PassTier;
            first = Mathf.Clamp(focus - 1, 1, Catalog.PassTiers - Columns + 1);
            Render();
        }

        void Say(string text)
        {
            note = text;
            Render();
        }

        void Claim(int tier)
        {
            var r = Catalog.FreeReward(tier);
            if (Profile.Claimed(tier)) note = $"Tier {tier} is already claimed.";
            else if (tier > Profile.PassTier) note = $"Reach tier {tier} to claim {r.Value} {r.Kind.ToLowerInvariant()}. Each tier is 1,000 XP.";
            else if (Profile.Claim(tier)) note = $"Claimed {r.Value} {r.Kind.ToLowerInvariant()} from tier {tier}.";
            Render();
        }

        void Render()
        {
            int tier = Profile.PassTier, xp = Profile.Xp;
            s.Text("tier", tier.ToString());
            s.Text("tierLabel", $"TIER {tier}");
            if (tier >= Catalog.PassTiers)
            {
                s.Text("tierXp", "MAX TIER");
                s.Fill("tierBar", 1);
            }
            else
            {
                int into = xp % Profile.XpPerTier;
                s.Text("tierXp", $"{into:#,0} / {Profile.XpPerTier:#,0} XP");
                s.Fill("tierBar", into / (float)Profile.XpPerTier);
            }

            for (int i = 0; i < Columns; i++)
            {
                int t = first + i;
                bool reached = t <= tier, claimed = Profile.Claimed(t);
                s.Text("t" + i, t.ToString());
                s.Color("t" + i, t == tier ? Skin.Amber : reached ? Skin.Paper : Skin.Muted);
                var r = Catalog.FreeReward(t);
                s.Text("free" + i + "v", r.Value);
                s.Text("free" + i + "k", claimed ? r.Kind + " · CLAIMED" : reached ? "TAP TO CLAIM" : r.Kind);
                s.Color("free" + i + "k", reached && !claimed ? Skin.Amber : Skin.Muted);
                if (!s.Has("free" + i)) continue;
                var e = s["free" + i];
                Skin.Card(e, reached && !claimed, new Color(10 / 255f, 12 / 255f, 11 / 255f, 0.7f));
                e.style.opacity = claimed ? 0.6f : 1f;
            }
            s.Text("note", note ?? Cosmetic);
            s.Color("note", note != null ? Skin.Paper : Skin.Muted);
        }
    }
}
