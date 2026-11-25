// The night scene behind every banner: stars, the moon, layered hills with fog between them, a treeline, and a lake with the
// moon's light on it. One drawing for every page ("This is the same banner on all pages, just size changes right?"): Overview's,
// and on the lower banners the same one drawn again for their height, with the same hills, moon and lake in the same places
// across and the heights squeezed to fit. Drawn from fixed seeds, so it looks the same every time. The colours come from
// overview.css, mixed from the accent, so the scene follows the lab's colour.
(() => {
  const rnd = (seed) => { let s = seed; return () => (s = (s * 16807) % 2147483647) / 2147483647; };
  const f1 = (n) => n.toFixed(1);
  const pine = (x, y, h) => {
    const w = h * 0.34;
    const p = [[0, -1], [0.5, -0.58], [0.26, -0.58], [0.78, -0.24], [0.42, -0.24], [1, 0], [-1, 0], [-0.42, -0.24], [-0.78, -0.24], [-0.26, -0.58], [-0.5, -0.58]];
    return `M${p.map(([a, b]) => `${f1(x + a * w)} ${f1(y + b * h)}`).join('L')}Z`;
  };
  // Soft shafts of light falling from the moon.
  const rays = (x, y, n, r, len) => {
    let s = '';
    for (let i = 0; i < n; i++) {
      const a = (-0.35 + (i / (n - 1)) * 0.7) + (r() - 0.5) * 0.08 + Math.PI / 2;
      const w = 0.03 + r() * 0.05;
      s += `M${x} ${f1(y)}L${f1(x + Math.cos(a - w) * len)} ${f1(y + Math.sin(a - w) * len)}L${f1(x + Math.cos(a + w) * len)} ${f1(y + Math.sin(a + w) * len)}Z`;
    }
    return `<path class="ovs-rays" d="${s}" filter="url(#ovs-b24)"/>`;
  };
  // How strong the haze round the moon is ("This moon glow is WAYYYY too much", then "less bright, the clouds + the moon haze").
  const HAZE = 0.2;
  // The moon: evenly lit with its edge a touch darker and softened by a little light, a close glow, and broad faint patches
  // where the real moon's seas are, blurred so they never read as spots ("The moon looks odd", of crisp ones and an outline).
  // [x, y, rx, ry, turn] as parts of the radius. Its outline for the seas is in the shared defs, one for each size.
  const SEAS = [[-0.42, -0.1, 0.36, 0.5, 15], [-0.2, -0.42, 0.24, 0.18, -10], [0.3, -0.25, 0.24, 0.17, 25], [0.45, 0.08, 0.2, 0.2, 0], [-0.1, 0.38, 0.16, 0.1, -15]];
  const moon = (x, y, rad) => `<circle cx="${x}" cy="${f1(y)}" r="${rad * 2.4}" fill="url(#ovs-glow)" opacity="${HAZE}"/>`
    + `<g transform="translate(${x} ${f1(y)})"><circle r="${rad + 1.2}" fill="#efeaff" opacity=".35" filter="url(#ovs-b05)"/><circle r="${rad}" fill="url(#ovs-moonfill)"/>`
    + `<g clip-path="url(#ovs-mc${rad})" fill="#9C94C6" opacity=".16"><g filter="url(#ovs-b2)">${SEAS.map(([a, b, rx, ry, t]) => `<ellipse transform="rotate(${t} ${f1(a * rad)} ${f1(b * rad)})" cx="${f1(a * rad)}" cy="${f1(b * rad)}" rx="${f1(rx * rad)}" ry="${f1(ry * rad)}"/>`).join('')}</g></g></g>`;
  // A shooting star: a streak that crosses its path and fades as it goes, so it never stops on screen. It carries the band of
  // sky it may cross and where the moon is, for wander() to put it somewhere new each time.
  const meteor = (x, y, deg, [y0, y1], [mx, my, mr]) => `<g class="ovs-mpos" data-sky="${f1(y0)} ${f1(y1)}" data-moon="${mx} ${f1(my)} ${mr}" transform="translate(${x} ${f1(y)}) rotate(${deg})"><g class="ovs-meteor">`
    + '<rect x="-110" y="-.9" width="110" height="1.8" rx=".9" fill="url(#ovs-tail)"/><circle r="1.7" fill="#fff"/><circle r="5" fill="#fff" opacity=".35" filter="url(#ovs-b3)"/></g></g>';
  // A scene twice as wide as it is drawn: the drawing on the right, and on its left its mirror image without the moon and its
  // light, so a wide banner shows more land and sky instead of zooming in and lifting the moon out of sight ("When dev is
  // full screen the moon goes way up"). Each layer is [where, svg]: 'all' drawn once across the whole width, 'right' on the
  // drawing only, 'both' on both sides overlapping by one unit so solid land shows no seam, 'soft' on both without the
  // overlap, for what can be seen through.
  const twice = (W, layers) => layers.map(([where, s]) => (where === 'all' ? s
    : `<g transform="translate(${W} 0)">${s}</g>${where === 'right' ? '' : `<g transform="translate(${where === 'both' ? W + 1 : W} 0) scale(-1 1)">${s}</g>`}`)).join('');

  // Halloween, October 15 to 31: a purple night with an orange moon and orange mist (overview.css .ovs-halloween), and three
  // tiny bats flitting past the moon ("Holloween is the bats."). ?banner=halloween shows it on any day.
  const holiday = new URLSearchParams(location.search).get('banner') === 'halloween' || (new Date().getMonth() === 9 && new Date().getDate() >= 15) ? 'halloween' : '';
  if (holiday) document.documentElement.classList.add(`ovs-${holiday}`);
  // A bat: a body, a head with ears, and two scalloped wings that flap from the shoulders, bobbing as it flies.
  const WING = 'M2 -1C9 -8 18 -11 30 -6C27 -3 26 0 25 3C22 0 19 0 16 3C14 0 11 0 8 4C6 2 4 2 2 3Z';
  const BAT = `<g class="ovs-bob"><g class="ovs-wing"><path d="${WING}"/></g><g transform="scale(-1 1)"><g class="ovs-wing"><path d="${WING}"/></g></g>`
    + '<ellipse cy="2" rx="3.2" ry="6.2"/><circle cy="-5" r="3.1"/><path d="M-2.8 -6.4L-2.2 -11.2L-0.6 -7.2ZM2.8 -6.4L2.2 -11.2L0.6 -7.2Z"/></g>';
  // Three, each on its own wandering path past the moon at (mx, my): in from one side, three turns near the moon, out the
  // other, at its own size, speed and bob. The path goes in data-v; fill() sets it as the bat's own properties, since the
  // page allows no style attributes.
  const bats = (mx, m, sc) => {
    const r = rnd(53);
    let s = '';
    for (let i = 0; i < 3; i++) {
      const back = r() < 0.5;
      const xs = [back ? mx - 300 : mx + 300, mx + (r() - 0.5) * 520, mx + (r() - 0.5) * 520, mx + (r() - 0.5) * 520, back ? mx + 310 : mx - 310];
      const ys = xs.map(() => m(70 + r() * 170));
      const v = [...xs.map((x, k) => `x${k}:${f1(x)}px;y${k}:${f1(ys[k])}px`), `s:${((0.3 + r() * 0.22) * sc).toFixed(2)}`, `t:${(6 + r() * 6).toFixed(1)}s`,
        `d:${(-i * 4 - r() * 1.5).toFixed(1)}s`, `b:${(0.3 + r() * 0.3).toFixed(2)}s`].join(';');
      s += `<g class="ovs-bat" data-v="${v}">${BAT}</g>`;
    }
    return s;
  };

  // How the drawing is fitted to a banner. h: its height in its own units (a 270 px banner shows 300); map: Overview's heights
  // to this one's, squeezing the sky most and the hills least so they keep their shape; the sizes of the trees, the moon and
  // its light, so nothing is squashed.
  const SIZES = {
    tall: { h: 600, map: [[0, 0], [600, 600]], tree: 1, near: 1, moonR: 28, glowR: 240, rayLen: 520, glint: 1, sheen: [80, 56, 110] },
    low: { h: 300, map: [[0, 0], [130, 44], [230, 60], [350, 116], [436, 166], [600, 300]], tree: 0.8, near: 0.55, moonR: 22, glowR: 170, rayLen: 200, glint: 0.75, sheen: [46, 44, 60] },
  };
  const draw = (size) => {
    const W = 1600;
    const k = SIZES[size];
    const H = k.h;
    const m = (y) => {
      for (let i = 1; i < k.map.length; i++) {
        const [a, A] = k.map[i - 1];
        const [b, B] = k.map[i];
        if (y <= b || i === k.map.length - 1) return A + ((y - a) * (B - A)) / (b - a);
      }
      return y;
    };
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
        return [x, m(v)];
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
    // About one pine in seven on the far hills and the far shore stands about twice as tall ("Some of these trees, on ALL
    // banners, should be taller"). Those take their own numbers, so every tree stays where it was.
    const tall = rnd(31);
    const lift = () => (tall() < 0.15 ? 1.8 + tall() * 0.8 : 1);
    const forest = (r, pts, from, to, hMin, hMax, gap, s, some = false) => {
      let d = '';
      for (let x = from; x < to; x += gap * (0.5 + r())) d += pine(x, yAt(pts, x) + 4 * s, (hMin + r() * (hMax - hMin)) * s * (some ? lift() : 1));
      return d;
    };
    const stars = (r, n, maxY) => {
      let s = '';
      for (let i = 0; i < n; i++) {
        const tw = r() < 0.25 ? ` ovs-tw ovs-d${1 + Math.floor(r() * 4)}` : '';
        s += `<circle class="ovs-star${tw}" cx="${f1(r() * W)}" cy="${f1(m(r() * maxY))}" r="${f1(0.5 + r() * r() * 1.5)}" opacity="${(0.25 + r() * 0.75).toFixed(2)}"/>`;
      }
      return s;
    };
    const fog = (y, h, op) => `<rect class="ovs-fog" x="0" y="${f1(m(y))}" width="${2 * W}" height="${f1(m(y + h) - m(y))}" opacity="${op}"/>`;

    const r = rnd(7);
    const [mx, my] = [1080, m(130)];
    const L1 = ridge(r, 350, 80, 0.55, [[700, 90, 150], [1400, 120, 160]]);
    const L2 = ridge(r, 395, 70, 0.55, [[540, 50, 160], [980, 60, 180], [1400, 90, 160]]);
    const L3 = ridge(r, 440, 50, 0.5, [[280, 36, 200], [1230, 44, 220]]);
    const sky = stars(r, 190, 320);
    const beams = rays(mx, my, 7, r, k.rayLen);
    const trees = forest(r, L3, 0, W, 9, 20, 15, k.tree, true);
    // The lake: the far shore with its pines (rising where it meets the hills at 1240), the water, the shore upside down in it,
    // and the moon's light breaking across it in rows of short strokes with gaps, wider toward the viewer ("the light on the
    // river here looks a bit too much": two thirds as bright).
    const q = rnd(19);
    const SH = m(436);
    const shore = ridge(q, 436 - 9, 10, 0.5, [[1240, 16, 170]]);
    const band = `M0 ${f1(SH)}L${shore.map(([a, b]) => `${f1(a)} ${f1(Math.min(b, SH))}`).join('L')}L${W} ${f1(SH)}Z${forest(q, shore, 0, W, 10, 28, 13, k.tree * 0.7 + k.near * 0.3, true)}`;
    // Every other row is drawn, a little blurred and gently shimmering, with no sharp copy on top ("This needs to be softened,
    // the light looks... odd", then "this is sexy" of the fewer strokes). They fade to nothing halfway down the water, so no
    // light glows above the strip ("what is this glow above the bar, get rid of this").
    let glints = '';
    for (let i = 0; i < 40; i++) {
      const row = i % 2 === 0;
      const t = (i + q() * 0.8) / 40;
      const y = SH + 3 + t * (H - SH - 6);
      const span = (10 + t * 96) * k.glint;
      const fade = Math.max(0, 1 - t * 2.1) ** 1.3;
      let gx = mx - span / 2 + (q() - 0.5) * (6 + t * 26) * k.glint;
      const end = gx + span;
      while (gx < end) {
        const w = Math.min(end - gx, 2 + q() * span * 0.4);
        const stroke = `<rect class="ovs-d${1 + Math.floor(q() * 4)}" x="${f1(gx)}" y="${f1(y)}" width="${f1(w)}" height="${f1((0.6 + t * 1.2) * k.glint)}" rx=".6" opacity="${((0.14 + q() * 0.46 * (1 - t * 0.45)) * 0.65 * fade).toFixed(2)}"/>`;
        if (row && fade > 0) glints += stroke;
        gx += w + 2 + q() * span * 0.2;
      }
    }
    const near = ridge(q, 660, 20, 0.5, [[0, 175, 170], [W, 185, 200]]);
    const [sy, srx, sry] = k.sheen;
    return `<svg class="ovs" viewBox="0 0 ${2 * W} ${H}" preserveAspectRatio="xMaxYMid slice" aria-hidden="true">` + twice(W, [
      ['all', `<rect width="${2 * W}" height="${H}" fill="url(#ovs-sky)"/>`],
      ['soft', `<g>${sky}</g>`],
      ['right', `${meteor(300, m(90), 14, [m(30), m(220)], [mx, my, k.moonR * 5])}<circle cx="${mx}" cy="${f1(my)}" r="${k.glowR}" fill="url(#ovs-glow)" opacity="${HAZE}"/>${beams}${moon(mx, my, k.moonR)}`
        + (holiday === 'halloween' ? bats(mx, m, k.moonR / 28) : '')],
      ['both', land(L1, 'ovs-l1', 0.5)],
      // No cloud under the moon ("Just remove this fucking cloud.").
      ['all', fog(250, 170, 0.55)],
      ['both', land(L2, 'ovs-l2', 0.4)], ['all', fog(320, 150, 0.5)],
      ['both', land(L3, 'ovs-l3', 0.3) + `<path class="ovs-l3" d="${trees}"/>`], ['all', fog(395, 130, 0.42)],
      ['soft', `<ellipse class="ovs-mist" cx="700" cy="${f1(m(455))}" rx="420" ry="${f1(m(481) - m(455))}" filter="url(#ovs-b24)"/>`],
      ['all', `<rect x="0" y="${f1(SH)}" width="${2 * W}" height="${f1(H - SH)}" fill="url(#ovs-water)"/>`],
      // The far shore upside down in the water, faint ("TOO bright"), and the moon's sheen kept up by the far shore. No lines
      // across the water ("What are these lines in the water. no.").
      ['soft', `<g transform="translate(0 ${f1(2 * SH)}) scale(1 -1)" opacity=".26" filter="url(#ovs-b1)"><path class="ovs-l4" d="${band}"/></g>`],
      ['right', `<ellipse class="ovs-sheen" cx="${mx}" cy="${f1(SH + sy * 0.35)}" rx="${srx}" ry="${f1(sry * 0.45)}" filter="url(#ovs-b24)"/>`],
      ['right', `<g class="ovs-glint ovs-shim" filter="url(#ovs-b1)" opacity=".7">${glints}</g>`],
      ['both', `<path class="ovs-l4" d="${band}"/>`],
      ['both', land(near, 'ovs-l5') + `<path class="ovs-l5" d="${forest(q, near, 0, 380, 60, 125, 26, k.near)}${forest(q, near, 1240, W, 60, 125, 26, k.near)}"/>`],
    ]) + '</svg>';
  };

  // The gradients, blurs and the moon's outlines, once for every scene, in a drawing that is always laid out: a scene on a
  // hidden page can't lend its own to the one on the page that's open.
  const blur = (id, sd) => `<filter id="${id}" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="${sd}"/></filter>`;
  document.body.insertAdjacentHTML('beforeend', '<svg class="ovs-defs" aria-hidden="true" focusable="false"><defs>'
    + '<linearGradient id="ovs-sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-sky0"/><stop offset=".62" class="ovs-sky1"/><stop offset="1" class="ovs-sky2"/></linearGradient>'
    + '<linearGradient id="ovs-fogg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-fogc" stop-opacity="0"/><stop offset="1" class="ovs-fogc" stop-opacity="1"/></linearGradient>'
    + '<linearGradient id="ovs-water" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="ovs-water0"/><stop offset="1" class="ovs-water1"/></linearGradient>'
    + '<radialGradient id="ovs-glow"><stop offset="0" class="ovs-glowc" stop-opacity=".55"/><stop offset=".35" class="ovs-glowc" stop-opacity=".15"/><stop offset=".7" class="ovs-glowc" stop-opacity=".03"/><stop offset="1" class="ovs-glowc" stop-opacity="0"/></radialGradient>'
    + '<radialGradient id="ovs-moonfill"><stop offset="0" stop-color="#fbfaff"/><stop offset=".75" stop-color="#f0ecff"/><stop offset="1" stop-color="#d9d2f4"/></radialGradient>'
    + '<linearGradient id="ovs-tail" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset="1" stop-color="#fff" stop-opacity=".85"/></linearGradient>'
    + '<clipPath id="ovs-mc28"><circle r="28"/></clipPath><clipPath id="ovs-mc22"><circle r="22"/></clipPath>'
    + blur('ovs-b05', 0.8) + blur('ovs-b1', 1.2) + blur('ovs-b2', 2) + blur('ovs-b3', 3) + blur('ovs-b24', 24)
    + '</defs></svg>');

  // The shooting star starts somewhere new in the sky every time it comes, well away from where it last was and from the
  // moon, mostly falling to the right, now and then to the left ("Never in the same place").
  const wander = (g) => {
    const [y0, y1] = g.dataset.sky.split(' ').map(Number);
    const [mx, my, mr] = g.dataset.moon.split(' ').map(Number);
    const lx = Number(g.dataset.last ?? -9999);
    for (let i = 0; i < 40; i++) {
      const x = 150 + Math.random() * 1350;
      const y = y0 + Math.random() * (y1 - y0);
      if (Math.abs(x - lx) < 420 || Math.hypot(x - mx, y - my) < mr) continue;
      const deg = Math.random() < 0.3 ? 180 - (10 + Math.random() * 22) : 8 + Math.random() * 26;
      g.setAttribute('transform', `translate(${x.toFixed(0)} ${y.toFixed(0)}) rotate(${deg.toFixed(0)})`);
      g.dataset.last = x.toFixed(0);
      return;
    }
  };
  document.addEventListener('animationiteration', (e) => { if (e.target.classList?.contains('ovs-meteor')) wander(e.target.parentNode); });

  // Overview's now; another page's banner the first time its page opens, so pages never opened never draw one. Every
  // banner shares the one low drawing, made once; each starts its shooting star somewhere of its own.
  let lowScene = '';
  const fill = () => {
    document.querySelectorAll('[data-scene]:empty').forEach((el) => { el.innerHTML = draw('tall'); });
    document.querySelectorAll('.page:not([hidden]) .hero').forEach((hero) => {
      if (hero.querySelector(':scope > .hero-art')) return;
      lowScene ||= draw('low');
      hero.insertAdjacentHTML('afterbegin', `<div class="hero-art">${lowScene}</div>`);
    });
    document.querySelectorAll('.ovs-mpos:not([data-last])').forEach(wander);
    document.querySelectorAll('.ovs-bat[data-v]').forEach((b) => {
      b.dataset.v.split(';').forEach((p) => { const [k, v] = p.split(':'); b.style.setProperty(`--${k}`, v); });
      b.removeAttribute('data-v');
    });
  };
  fill();
  document.addEventListener('rabbidlab:show', fill);
})();
