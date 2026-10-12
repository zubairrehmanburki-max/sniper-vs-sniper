import { PracticeGame } from './game.ts';

const game = new PracticeGame();
if (new URLSearchParams(location.search).has('debug')) {
  (window as unknown as { svs: PracticeGame }).svs = game;
}
