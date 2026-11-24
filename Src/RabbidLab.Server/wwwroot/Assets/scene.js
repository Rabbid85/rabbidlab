// The night scene behind Overview's banner: stars, the moon, layered hills with fog between them, and a lake with the moon's
// light on it. Drawn once into each [data-scene] from fixed seeds, so it looks the same every time. The colours come from
// overview.css, mixed from the accent, so the scene follows the lab's colour.
(() => {
  const W = 1600;
  const H = 600;
  const MOON = [1080, 130]; // above the words, which sit at the bottom left
  const SX = 1240; // where the lake's far shore rises
  const rnd = (seed) => { let s = seed; return () => (s = (s * 16807) % 2147483647) / 2147483647; };
  const f1 = (n) => n.toFixed(1);

  // A ridge line: midpoint displacement for the rough edge, with bell-shaped peaks on top.
  const ridge = (r, base, amp, rough, peaks = []) => {
    const N = 256;
    const ys = new Array(N + 1).fill(0);
    ys[0] = (r() - 0.5) * amp;
    ys[N] = (r() - 0.5) * amp;
    for (let step = N, d = amp; step > 1; step /= 2, d *= rough) {
      for (let i = step / 2; i < N; i += step) ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 + (r() - 0.5) * d;
    }
    return ys.map((y, i) => {
      const x = (i / N) * W;
      let v = base + y;
      peaks.forEach(([px, ph, pw]) => { v -= ph * Math.exp(-((x - px) ** 2) / (2 * pw * pw)); });
      return [x, v];
    });
  };
  const yAt = (pts, x) => {
    const i = Math.max(0, Math.min(pts.length - 2, Math.floor((x / W) * (pts.length - 1))));
    const [x1, y1] = pts[i];
    const [x2, y2] = pts[i + 1];
    return y1 + ((y2 - y1) * (x - x1)) / (x2 - x1);
  };
  // A layer of land, and on far layers a thin moonlit line along its ridge.
  const land = (pts, cls, rim = 0) => `<path class="${cls}" d="M0 ${H}L${pts.map(([x, y]) => `${f1(x)} ${f1(y)}`).join('L')}L${W} ${H}Z"/>`
    + (rim ? `<polyline class="ovs-rim" opacity="${rim}" points="${pts.map(([x, y]) => `${f1(x)},${f1(y)}`).join(' ')}"/>` : '');
  const pine = (x, y, h) => {
    const w = h * 0.34;
    const p = [[0, -1], [0.5, -0.58], [0.26, -0.58], [0.78, -0.24], [0.42, -0.24], [1, 0], [-1, 0], [-0.42, -0.24], [-0.78, -0.24], [-0.26, -0.58], [-0.5, -0.58]];
    return `M${p.map(([a, b]) => `${f1(x + a * w)} ${f1(y + b * h)}`).join('L')}Z`;
  };
  const forest = (r, pts, from, to, hMin, hMax, gap) => {
    let d = '';
    for (let x = from; x < to; x += gap * (0.5 + r())) d += pine(x, yAt(pts, x) + 4, hMin + r() * (hMax - hMin));
    return d;
  };
  const stars = (r, n, maxY) => {
    let s = '';
    for (let i = 0; i < n; i++) {
      const tw = r() < 0.25 ? ` ovs-tw ovs-d${1 + Math.floor(r() * 4)}` : '';
      s += `<circle class="ovs-star${tw}" cx="${f1(r() * W)}" cy="${f1(r() * maxY)}" r="${f1(0.5 + r() * r() * 1.5)}" opacity="${(0.25 + r() * 0.75).toFixed(2)}"/>`;
    }
    return s;
  };
  const fog = (y, h, op) => `<rect class="ovs-fog" x="0" y="${y}" width="${W}" height="${h}" opacity="${op}"/>`;
  // Soft shafts of light falling from the moon.
  const rays = (x, y, n, r) => {
    let s = '';
    for (let i = 0; i < n; i++) {
      const a = (-0.35 + (i / (n - 1)) * 0.7) + (r() - 0.5) * 0.08 + Math.PI / 2;
      const w = 0.03 + r() * 0.05;
      s += `M${x} ${y}L${f1(x + Math.cos(a - w) * 520)} ${f1(y + Math.sin(a - w) * 520)}L${f1(x + Math.cos(a + w) * 520)} ${f1(y + Math.sin(a + w) * 520)}Z`;
    }
    return `<path class="ovs-rays" d="${s}" filter="url(#ovs-b24)"/>`;
  };
  // The moon: evenly lit with its edge a touch darker and softened by a little light, a close glow, and broad faint patches
  // where the real moon's seas are, blurred so they never read as spots ("The moon looks odd", of crisp ones and an outline).
  // [x, y, rx, ry, turn] as parts of the radius.
  const SEAS = [[-0.42, -0.1, 0.36, 0.5, 15], [-0.2, -0.42, 0.24, 0.18, -10], [0.3, -0.25, 0.24, 0.17, 25], [0.45, 0.08, 0.2, 0.2, 0], [-0.1, 0.38, 0.16, 0.1, -15]];
  const moon = (x, y, rad) => `<circle cx="${x}" cy="${y}" r="${rad * 2.4}" fill="url(#ovs-glow)" opacity=".4"/>`
    + `<g transform="translate(${x} ${y})"><clipPath id="ovs-moonclip"><circle r="${rad}"/></clipPath>`
    + `<circle r="${rad + 1.2}" fill="#efeaff" opacity=".35" filter="url(#ovs-b05)"/><circle r="${rad}" fill="url(#ovs-moonfill)"/>`
    + `<g clip-path="url(#ovs-moonclip)" fill="#9C94C6" opacity=".16"><g filter="url(#ovs-b2)">${SEAS.map(([a, b, rx, ry, t]) => `<ellipse transform="rotate(${t} ${f1(a * rad)} ${f1(b * rad)})" cx="${f1(a * rad)}" cy="${f1(b * rad)}" rx="${f1(rx * rad)}" ry="${f1(ry * rad)}"/>`).join('')}</g></g></g>`;
  // A thin cloud lit by the moon: a few soft overlapping puffs.
  const cloud = (x, y, w) => [[-0.4, 0.3, 0.28, 0.45], [-0.12, -0.15, 0.34, 0.7], [0.18, 0.08, 0.3, 0.55], [0.42, 0.3, 0.2, 0.35]]
    .map(([a, b, rx, ry]) => `<ellipse class="ovs-wisp" cx="${f1(x + a * w)}" cy="${f1(y + b * 12)}" rx="${f1(rx * w)}" ry="${f1(ry * 14)}"/>`).join('');
  // A shooting star: a streak that crosses its path and fades as it goes, so it never stops on screen.
  const meteor = (x, y, deg) => `<g transform="translate(${x} ${y}) rotate(${deg})"><g class="ovs-meteor">`
    + '<rect x="-110" y="-.9" width="110" height="1.8" rx=".9" fill="url(#ovs-tail)"/><circle r="1.7" fill="#fff"/><circle r="5" fill="#fff" opacity=".35" filter="url(#ovs-b3)"/></g></g>';

  // The lake: the far shore with its pines, the water, the shore upside down in it, and the moon's light breaking across it
  // in rows of short strokes with gaps, wider toward the viewer.
  const lake = (r, mx) => {
    const SH = 436;
    const shore = ridge(r, SH - 9, 10, 0.5, [[SX, 16, 170]]);
    const band = `M0 ${SH}L${shore.map(([a, b]) => `${f1(a)} ${f1(Math.min(b, SH))}`).join('L')}L${W} ${SH}Z${forest(r, shore, 0, W, 10, 28, 13)}`;
    let glints = '';
    for (let i = 0; i < 40; i++) {
      const t = (i + r() * 0.8) / 40;
      const y = SH + 3 + t * (H - SH - 6);
      const span = 10 + t * 96;
      let gx = mx - span / 2 + (r() - 0.5) * (6 + t * 26);
      const end = gx + span;
      while (gx < end) {
        const w = Math.min(end - gx, 2 + r() * span * 0.4);
        glints += `<rect class="ovs-d${1 + Math.floor(r() * 4)}" x="${f1(gx)}" y="${f1(y)}" width="${f1(w)}" height="${f1(0.6 + t * 1.2)}" rx=".6" opacity="${(0.14 + r() * 0.46 * (1 - t * 0.45)).toFixed(2)}"/>`;
        gx += w + 2 + r() * span * 0.2;
      }
    }
    let ripples = '';
    for (let i = 0; i < 11; i++) ripples += `M0 ${f1(SH + 6 + (i ** 1.5) * 4.6)}H${W}`;
    const near = ridge(r, 660, 20, 0.5, [[0, 175, 170], [W, 185, 200]]);
    return `<rect x="0" y="${SH}" width="${W}" height="${H - SH}" fill="url(#ovs-water)"/>`
      + `<g transform="translate(0 ${2 * SH}) scale(1 -1)" opacity=".55" filter="url(#ovs-b1)"><path class="ovs-l4" d="${band}"/></g>`
      + `<ellipse class="ovs-sheen" cx="${mx}" cy="${SH + 80}" rx="56" ry="110" filter="url(#ovs-b24)"/>`
      + `<path class="ovs-ripple" d="${ripples}"/>`
      // The light on the water once soft and still, and once sharp and shimmering.
      + `<g class="ovs-glint" filter="url(#ovs-b1)">${glints}</g><g class="ovs-glint ovs-shim" opacity=".7">${glints}</g>`
      + `<path class="ovs-l4" d="${band}"/>`
      + land(near, 'ovs-l5') + `<path class="ovs-l5" d="${forest(r, near, 0, 380, 60, 125, 26)}${forest(r, near, 1240, W, 60, 125, 26)}"/>`;
  };

  const blur = (id, sd) => `<filter id="${id}" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="${sd}"/></filter>`;
  const DEFS = '<defs>'
    + '<linearGradient id="ovs-sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-sky0"/><stop offset=".62" class="ovs-sky1"/><stop offset="1" class="ovs-sky2"/></linearGradient>'
    + '<linearGradient id="ovs-fogg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-fogc" stop-opacity="0"/><stop offset="1" class="ovs-fogc" stop-opacity="1"/></linearGradient>'
    + '<linearGradient id="ovs-water" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-water0"/><stop offset="1" class="ovs-water1"/></linearGradient>'
    + '<radialGradient id="ovs-glow"><stop offset="0" class="ovs-glowc" stop-opacity=".55"/><stop offset=".35" class="ovs-glowc" stop-opacity=".15"/><stop offset=".7" class="ovs-glowc" stop-opacity=".03"/><stop offset="1" class="ovs-glowc" stop-opacity="0"/></radialGradient>'
    + '<radialGradient id="ovs-moonfill"><stop offset="0" stop-color="#fbfaff"/><stop offset=".75" stop-color="#f0ecff"/><stop offset="1" stop-color="#d9d2f4"/></radialGradient>'
    + '<linearGradient id="ovs-tail" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset="1" stop-color="#fff" stop-opacity=".85"/></linearGradient>'
    + blur('ovs-b05', 0.8) + blur('ovs-b1', 1.2) + blur('ovs-b2', 2) + blur('ovs-b3', 3) + blur('ovs-b6', 6) + blur('ovs-b24', 24)
    + '</defs>';

  const scene = () => {
    const r = rnd(7);
    const [mx, my] = MOON;
    const L1 = ridge(r, 350, 80, 0.55, [[700, 90, 150], [1400, 120, 160]]);
    const L2 = ridge(r, 395, 70, 0.55, [[540, 50, 160], [980, 60, 180], [1400, 90, 160]]);
    const L3 = ridge(r, 440, 50, 0.5, [[280, 36, 200], [1230, 44, 220]]);
    return `<svg class="ovs" viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMaxYMid slice" aria-hidden="true">${DEFS}`
      + `<rect width="${W}" height="${H}" fill="url(#ovs-sky)"/><g>${stars(r, 190, 320)}</g>${meteor(300, 90, 14)}`
      + `<circle cx="${mx}" cy="${my}" r="240" fill="url(#ovs-glow)" opacity=".4"/>${rays(mx, my, 7, r)}${moon(mx, my, 28)}`
      + `<g class="ovs-drift" filter="url(#ovs-b6)">${cloud(mx - 70, my + 30, 250)}${cloud(mx + 150, my - 34, 150)}</g>`
      + land(L1, 'ovs-l1', 0.5) + fog(250, 170, 0.55)
      + land(L2, 'ovs-l2', 0.4) + fog(320, 150, 0.5)
      + land(L3, 'ovs-l3', 0.3) + `<path class="ovs-l3" d="${forest(r, L3, 0, W, 9, 20, 15)}"/>` + fog(395, 130, 0.42)
      + `<ellipse class="ovs-mist" cx="700" cy="455" rx="420" ry="26" filter="url(#ovs-b24)"/>`
      + lake(rnd(19), mx)
      + '</svg>';
  };

  document.querySelectorAll('[data-scene]').forEach((el) => { el.innerHTML = scene(); });
})();
