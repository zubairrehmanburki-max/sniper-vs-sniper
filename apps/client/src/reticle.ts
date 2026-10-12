/** Mil-based "tree" reticle drawn on a 2D canvas over the scope view (first focal plane). */
export function drawReticle(ctx: CanvasRenderingContext2D, w: number, h: number, pxPerMrad: number): void {
  const dpr = ctx.canvas.width / w;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  const cx = w / 2;
  const cy = h / 2;
  const radius = Math.min(w, h) * 0.48;

  // Scope tube: everything outside the circle is black.
  ctx.fillStyle = '#000';
  ctx.beginPath();
  ctx.rect(0, 0, w, h);
  ctx.arc(cx, cy, radius, 0, Math.PI * 2, true);
  ctx.fill('evenodd');
  const vignette = ctx.createRadialGradient(cx, cy, radius * 0.75, cx, cy, radius);
  vignette.addColorStop(0, 'rgba(0,0,0,0)');
  vignette.addColorStop(1, 'rgba(0,0,0,0.55)');
  ctx.fillStyle = vignette;
  ctx.beginPath();
  ctx.arc(cx, cy, radius, 0, Math.PI * 2);
  ctx.fill();

  ctx.save();
  ctx.beginPath();
  ctx.arc(cx, cy, radius, 0, Math.PI * 2);
  ctx.clip();
  ctx.strokeStyle = '#0b0b0b';
  ctx.fillStyle = '#0b0b0b';
  const m = pxPerMrad;
  const fine = Math.max(0.6, Math.min(1.2, m * 0.012));

  // Thick outer posts beyond 10 mil.
  ctx.lineWidth = Math.max(3, m * 0.12);
  ctx.beginPath();
  ctx.moveTo(cx - 10 * m, cy);
  ctx.lineTo(cx - radius, cy);
  ctx.moveTo(cx + 10 * m, cy);
  ctx.lineTo(cx + radius, cy);
  ctx.moveTo(cx, cy - 10 * m);
  ctx.lineTo(cx, cy - radius);
  ctx.moveTo(cx, cy + 12 * m);
  ctx.lineTo(cx, cy + radius);
  ctx.stroke();

  // Fine crosshair.
  ctx.lineWidth = fine;
  ctx.beginPath();
  ctx.moveTo(cx - 10 * m, cy);
  ctx.lineTo(cx + 10 * m, cy);
  ctx.moveTo(cx, cy - 10 * m);
  ctx.lineTo(cx, cy + 12 * m);
  ctx.stroke();

  // Hash marks: every mil (longer every 5), half-mil marks between. Vertical marks only above
  // centre; below centre is the holdover tree.
  const showTenths = m > 25;
  ctx.beginPath();
  for (let i = 1; i <= 10; i++) {
    const len = (i % 5 === 0 ? 0.35 : 0.22) * m;
    const half = 0.12 * m;
    for (const s of [-1, 1]) {
      ctx.moveTo(cx + s * i * m, cy - len);
      ctx.lineTo(cx + s * i * m, cy + len);
      ctx.moveTo(cx + s * (i - 0.5) * m, cy - half);
      ctx.lineTo(cx + s * (i - 0.5) * m, cy + half);
    }
    ctx.moveTo(cx - len, cy - i * m);
    ctx.lineTo(cx + len, cy - i * m);
    ctx.moveTo(cx - half, cy - (i - 0.5) * m);
    ctx.lineTo(cx + half, cy - (i - 0.5) * m);
  }
  if (showTenths) {
    for (let t = -20; t <= 20; t++) {
      if (t % 5 === 0) continue;
      const l = 0.07 * m;
      ctx.moveTo(cx + t * 0.2 * m, cy - l);
      ctx.lineTo(cx + t * 0.2 * m, cy + l);
      ctx.moveTo(cx - l, cy + t * 0.2 * m);
      ctx.lineTo(cx + l, cy + t * 0.2 * m);
    }
  }
  ctx.stroke();

  // Tree: below centre, a row every mil with dots at each mil of windage hold.
  const dot = Math.max(1, Math.min(3, m * 0.05));
  for (let row = 1; row <= 12; row++) {
    const y = cy + row * m;
    ctx.beginPath();
    ctx.moveTo(cx - 0.3 * m, y);
    ctx.lineTo(cx + 0.3 * m, y);
    ctx.stroke();
    const width = Math.min(row, 8);
    for (let k = 1; k <= width; k++) {
      for (const s of [-1, 1]) {
        ctx.beginPath();
        ctx.arc(cx + s * k * m, y, dot, 0, Math.PI * 2);
        ctx.fill();
      }
    }
  }

  // Numbers every 2 mil.
  if (m > 8) {
    ctx.font = `${Math.max(9, Math.min(14, m * 0.28))}px system-ui, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    for (let i = 2; i <= 10; i += 2) {
      ctx.fillText(String(i), cx + i * m, cy - 0.7 * m);
      ctx.fillText(String(i), cx - i * m, cy - 0.7 * m);
      ctx.fillText(String(i), cx + 8.8 * m, cy + i * m);
    }
  }
  ctx.restore();
}
