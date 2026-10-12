/**
 * Elo ratings for ranked (quick match) duels. Pure maths; the server owns the stored ratings.
 */
export interface Rating {
  rating: number;
  games: number;
}

export const NEW_RATING: Rating = { rating: 1200, games: 0 };

/** Chance that a player rated `a` beats one rated `b`. */
export function expectedScore(a: number, b: number): number {
  return 1 / (1 + 10 ** ((b - a) / 400));
}

/** Provisional players move faster until their rating settles. */
export function kFactor(r: Rating): number {
  return r.games < 10 ? 40 : 24;
}

/** New ratings after a decided match. Rounded to whole points, as shown to players. */
export function rateMatch(winner: Rating, loser: Rating): { winner: Rating; loser: Rating } {
  const e = expectedScore(winner.rating, loser.rating);
  return {
    winner: { rating: Math.round(winner.rating + kFactor(winner) * (1 - e)), games: winner.games + 1 },
    loser: { rating: Math.round(loser.rating - kFactor(loser) * (1 - e)), games: loser.games + 1 },
  };
}
