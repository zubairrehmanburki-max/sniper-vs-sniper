/**
 * Synthesised audio: no asset files. The muzzle report, the delayed steel ring (time of flight plus
 * sound travel back from the target) and wind ambience that follows the station wind.
 */
export class RangeAudio {
  private ctx: AudioContext | null = null;
  private windGain: GainNode | null = null;
  private windFilter: BiquadFilterNode | null = null;

  /** Must be called from a user gesture. */
  unlock(): void {
    if (this.ctx) {
      void this.ctx.resume();
      return;
    }
    const AC = window.AudioContext ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC();
    const noise = this.noiseSource(4, true);
    this.windFilter = this.ctx.createBiquadFilter();
    this.windFilter.type = 'lowpass';
    this.windFilter.frequency.value = 400;
    this.windGain = this.ctx.createGain();
    this.windGain.gain.value = 0;
    noise.connect(this.windFilter).connect(this.windGain).connect(this.ctx.destination);
    noise.start();
  }

  private noiseSource(seconds: number, loop = false): AudioBufferSourceNode {
    const ctx = this.ctx!;
    const buf = ctx.createBuffer(1, ctx.sampleRate * seconds, ctx.sampleRate);
    const data = buf.getChannelData(0);
    let b = 0;
    for (let i = 0; i < data.length; i++) {
      b = 0.97 * b + 0.03 * (Math.random() * 2 - 1); // brown-ish noise (audio only, never physics)
      data[i] = b * 6;
    }
    const src = ctx.createBufferSource();
    src.buffer = buf;
    src.loop = loop;
    return src;
  }

  setWind(speedMps: number): void {
    if (!this.ctx || !this.windGain || !this.windFilter) return;
    const t = this.ctx.currentTime;
    this.windGain.gain.setTargetAtTime(Math.min(0.35, speedMps * 0.035), t, 0.4);
    this.windFilter.frequency.setTargetAtTime(250 + speedMps * 60, t, 0.4);
  }

  shot(loudness = 1): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime;
    const src = this.noiseSource(0.6);
    const f = ctx.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.setValueAtTime(5000, t);
    f.frequency.exponentialRampToValueAtTime(300, t + 0.4);
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.9 * loudness, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.55);
    src.connect(f).connect(g).connect(ctx.destination);
    src.start(t);
    // Low thump.
    const o = ctx.createOscillator();
    o.frequency.setValueAtTime(90, t);
    o.frequency.exponentialRampToValueAtTime(40, t + 0.25);
    const og = ctx.createGain();
    og.gain.setValueAtTime(0.6 * loudness, t);
    og.gain.exponentialRampToValueAtTime(0.001, t + 0.3);
    o.connect(og).connect(ctx.destination);
    o.start(t);
    o.stop(t + 0.35);
  }

  /** Steel ring, heard after `delay` seconds. */
  ping(delay: number, distance: number): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime + delay;
    const level = Math.max(0.05, 0.5 * (300 / Math.max(300, distance)));
    for (const [freq, decay] of [
      [1180, 0.9],
      [2390, 0.5],
      [3570, 0.3],
    ] as const) {
      const o = ctx.createOscillator();
      o.frequency.value = freq;
      const g = ctx.createGain();
      g.gain.setValueAtTime(0.0001, t);
      g.gain.exponentialRampToValueAtTime(level, t + 0.005);
      g.gain.exponentialRampToValueAtTime(0.0001, t + decay);
      o.connect(g).connect(ctx.destination);
      o.start(t);
      o.stop(t + decay + 0.05);
    }
  }

  /** Dull dirt impact, heard after `delay` seconds. */
  thud(delay: number, distance: number): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime + delay;
    const src = this.noiseSource(0.3);
    const f = ctx.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.value = 500;
    const g = ctx.createGain();
    const level = Math.max(0.03, 0.25 * (300 / Math.max(300, distance)));
    g.gain.setValueAtTime(level, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.25);
    src.connect(f).connect(g).connect(ctx.destination);
    src.start(t);
  }

  click(): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime;
    const o = ctx.createOscillator();
    o.type = 'square';
    o.frequency.value = 2400;
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.05, t);
    g.gain.exponentialRampToValueAtTime(0.0001, t + 0.02);
    o.connect(g).connect(ctx.destination);
    o.start(t);
    o.stop(t + 0.03);
  }
}
