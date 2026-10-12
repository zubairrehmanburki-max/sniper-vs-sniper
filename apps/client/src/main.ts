import { config, type BotDifficulty } from '@svs/shared';
import { DuelMode } from './duelMode.ts';
import { PracticeMode } from './practiceMode.ts';
import { $, Shooter } from './shooter.ts';

const shooter = new Shooter();
const debug = new URLSearchParams(location.search).has('debug');

for (const id of ['rifle-a', 'rifle-b']) {
  const select = $<HTMLSelectElement>(id);
  for (const r of config.rifles) {
    const o = document.createElement('option');
    o.value = r.id;
    o.textContent = r.name;
    select.append(o);
  }
}
$<HTMLSelectElement>('rifle-b').value = 'bolt65cm';

function menu(): void {
  $('menu').hidden = false;
  shooter.inputEnabled = false;
  shooter.setScoped(false);
}

function begin(): void {
  shooter.audio.unlock();
  $('menu').hidden = true;
}

let current: unknown = null;
$('play-range').onclick = () => {
  begin();
  current = new PracticeMode(shooter);
  shooter.inputEnabled = true;
  shooter.setScoped(true);
  if (debug) (window as unknown as { svs: unknown }).svs = current;
};

function duel(bot?: BotDifficulty): void {
  begin();
  const nameA = $<HTMLInputElement>('name-a').value.trim() || 'Player 1';
  const nameB = bot ? `Bot (${bot})` : $<HTMLInputElement>('name-b').value.trim() || 'Player 2';
  current = new DuelMode(
    shooter,
    {
      names: { A: nameA, B: nameB },
      rifles: { A: $<HTMLSelectElement>('rifle-a').value, B: $<HTMLSelectElement>('rifle-b').value },
      bot,
    },
    menu,
  );
  if (debug) (window as unknown as { svs: unknown }).svs = current;
}
$('play-hotseat').onclick = () => duel();
$('play-bot').onclick = () => duel($<HTMLSelectElement>('bot-level').value as BotDifficulty);

menu();
