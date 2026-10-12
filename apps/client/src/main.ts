import { config, makeCode, type BotDifficulty, type MatchView } from '@svs/shared';
import { DuelMode } from './duelMode.ts';
import { OnlineLink, serverUrl } from './net.ts';
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

// ---- online -----------------------------------------------------------------

let pendingLink: OnlineLink | null = null;

function onlineStatus(html: string | null, cancellable = false): void {
  $('online-status').hidden = html === null;
  $('online-status').innerHTML = html ?? '';
  $('online-cancel').hidden = !cancellable;
  for (const id of ['play-quick', 'play-create', 'play-join']) $<HTMLButtonElement>(id).disabled = cancellable;
}

function startOnline(link: OnlineLink, view: MatchView): void {
  pendingLink = null;
  onlineStatus(null);
  begin();
  current = new DuelMode(shooter, { names: { A: '', B: '' }, rifles: { A: '', B: '' }, online: { link, view } }, menu);
  if (debug) (window as unknown as { svs: unknown }).svs = current;
}

function watch(link: OnlineLink): void {
  pendingLink = link;
  link.listen({
    update: (view) => startOnline(link, view),
    waiting: (code) =>
      onlineStatus(code ? `Share this code with your opponent: <b>${code}</b><br>Waiting for them to join…` : 'Looking for an opponent…', true),
    error: (message) => onlineStatus(message),
    connection: (c) => {
      if (c === 'lost') onlineStatus('Lost the connection to the game server.');
    },
  });
}

async function online(kind: 'quick' | 'create' | 'join'): Promise<void> {
  shooter.audio.unlock();
  const opts = { name: $<HTMLInputElement>('name-a').value.trim() || 'Player 1', rifleId: $<HTMLSelectElement>('rifle-a').value };
  const code = $<HTMLInputElement>('join-code').value.trim().toUpperCase();
  if (kind === 'join' && !/^[A-Z]{4}$/.test(code)) {
    onlineStatus('Enter the 4-letter code your opponent shared.');
    return;
  }
  onlineStatus('Connecting…', true);
  try {
    const link =
      kind === 'quick'
        ? await OnlineLink.quick(opts)
        : kind === 'create'
          ? await OnlineLink.create({ ...opts, code: makeCode() })
          : await OnlineLink.join({ ...opts, code });
    watch(link);
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    onlineStatus(
      kind === 'join' && /not found|no rooms|locked|full/i.test(msg)
        ? `No open game with code ${code}.`
        : `Could not reach the game server at ${serverUrl()}.`,
    );
  }
}

$('play-quick').onclick = () => void online('quick');
$('play-create').onclick = () => void online('create');
$('play-join').onclick = () => void online('join');
$('online-cancel').onclick = () => {
  pendingLink?.leave();
  pendingLink = null;
  onlineStatus(null);
};

// A reload in the middle of an online match drops straight back into it.
void OnlineLink.resume().then((link) => {
  if (link) {
    onlineStatus('Rejoining your match…');
    watch(link);
  }
});
$('play-bot').onclick = () => duel($<HTMLSelectElement>('bot-level').value as BotDifficulty);

menu();
