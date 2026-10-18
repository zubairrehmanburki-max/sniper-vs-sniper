/*
 * Elo ratings for ranked (quick match) duels. Pure maths; the server owns the stored ratings.
 */
using System;

namespace SniperDuel.Rules
{
    public struct Rating
    {
        public double Value;
        public int Games;

        public Rating(double value, int games)
        {
            Value = value;
            Games = games;
        }
    }

    public struct RatedMatch
    {
        public Rating Winner;
        public Rating Loser;
    }

    public static class Elo
    {
        public static readonly Rating NewRating = new Rating(1200, 0);

        /// <summary>Chance that a player rated `a` beats one rated `b`.</summary>
        public static double ExpectedScore(double a, double b) => 1 / (1 + Math.Pow(10, (b - a) / 400));

        /// <summary>Provisional players move faster until their rating settles.</summary>
        public static int KFactor(Rating r) => r.Games < 10 ? 40 : 24;

        /// <summary>New ratings after a decided match. Rounded to whole points, as shown to players.</summary>
        public static RatedMatch RateMatch(Rating winner, Rating loser)
        {
            double e = ExpectedScore(winner.Value, loser.Value);
            return new RatedMatch
            {
                Winner = new Rating(JsMath.Round(winner.Value + KFactor(winner) * (1 - e)), winner.Games + 1),
                Loser = new Rating(JsMath.Round(loser.Value - KFactor(loser) * (1 - e)), loser.Games + 1),
            };
        }
    }
}
