// The weather on the banners (the sky window's Current weather, sky.js): what the US National Weather Service last read
// where the lab is (Weather.cs), drawn over every banner's scene and coloured into it. Each thing that moves is a drawing of
// its own that the graphics card moves whole, as the scene's own moving parts are (scene.js): each cloud, each sheet of
// rain or snow, the fog. Clouds are flat-bottomed banks, tall heaps and big domes mixed ("Ok this is my choice for
// clouds."), and none ever sits or blows across the moon ("NEVER put/keep one in front of the moon"). Rain is light, mid or
// heavy. The fog lies over the monsters and their pines too. There is no heat wave ("Heat wave remove").
(() => {
  const S = window.RabbidLabScene;
  const Sky = window.RabbidLabSky;
  if (!S || !Sky) return;
  const f1 = (n) => n.toFixed(1);
  const { rnd } = S;

  // How much cloud: how many clouds on the drawing's right (the left gets three fifths as many), and how dim the overcast.
  const COVER = { none: [0, 0], few: [3, 0], half: [5, 0.25], most: [8, 0.5], over: [7, 0.85] };
  const ORDER = ['none', 'few', 'half', 'most', 'over'];
  // Rain, a storm and snow come from cloud: at least this much shows with them, whatever the station's cloud reading said.
  const LEAST = { rain: 'half', snow: 'half', storm: 'most' };
  const coverOf = (wx) => ORDER[Math.max(ORDER.indexOf(wx.cover), ORDER.indexOf(LEAST[wx.kind] || 'none'), 0)];
  // The clouds' and the overcast's colours at each time of day, and how faint the stars are under each kind of weather.
  const CLOUDY = { night: ['#3e3e66', '#10101f'], dawn: ['#c8a0b0', '#4a3a52'], day: ['#f2f5f9', '#8e9cb0'], dusk: ['#b07a80', '#3a2440'] };
  const STARS = { rain: '.2', storm: '0', snow: '.3', fog: '.15', ice: '.8' };
  // Ice: everything in the colours of ice, at every time of day ("Just make it ice colroed").
  const ICE = {
    night: { sky0: '#02060f', sky1: '#081a33', sky2: '#1a3c66', l1: '#3c5f8c', l2: '#2c4a72', l3: '#21395c', l4: '#8aa6cc', l5: '#c2d4ec', pines3: '#17294a', pines5: '#10203a',
      fog: '#a8c8f0', mist: '#cfe4ff', rim: '#eef8ff', glint: '#f4fbff', water0: '#6d93c4', water1: '#16284a' },
    dusk: { sky0: '#0e1a33', sky1: '#2f4d7a', sky2: '#8fb4de', l1: '#5a77a3', l2: '#46618c', l3: '#364f76', l4: '#9db5d4', l5: '#c8d8ec', pines3: '#22385c', pines5: '#18294a',
      fog: '#b8d2f0', mist: '#d6e8fa', rim: '#f2f8ff', glint: '#f4faff', water0: '#86a6cc', water1: '#1c2e52' },
    day: { sky0: '#5d8fc4', sky1: '#a9cbea', sky2: '#e2f0fb', l1: '#9bb9d8', l2: '#7f9fc4', l3: '#6687b0', l4: '#c8dbef', l5: '#e6f0fa', pines3: '#3e5c82', pines5: '#2c4466',
      fog: '#eaf4ff', mist: '#f0f8ff', rim: '#ffffff', glint: '#ffffff', water0: '#bcd6ee', water1: '#6f8fb4' },
  };
  // What the weather adds to the scene's colours (scene.js paint): the clouds' and overcast's, dimmer stars, and under a
  // heavy overcast no halo or rays round the moon; in ice, the whole scene's.
  const tint = (wx, time) => {
    const pal = wx.kind === 'ice' ? { ...(ICE[time === 'dawn' ? 'dusk' : time] || ICE.night) } : {};
    const oc = COVER[coverOf(wx)][1];
    const [cloudy, occ] = CLOUDY[time] || CLOUDY.night;
    Object.assign(pal, { cloudy, occ, oc: String(oc) });
    if (STARS[wx.kind]) pal.star = STARS[wx.kind];
    if (oc > 0.5) Object.assign(pal, { halo: '.08', raysOp: '0' });
    return pal;
  };

  // What the layers are filled from, once for every banner, in a drawing that is always laid out (as the scene's own are).
  const blur = (id, sd) => `<filter id="${id}" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="${sd}"/></filter>`;
  const moonlit = (size) => { const F = S.fit(size); return `<radialGradient id="wx-cmoon-${size}" gradientUnits="userSpaceOnUse" cx="2680" cy="${f1(F.m(130))}" r="${f1(460 * (F.moonR / 28))}">`
    + '<stop offset="0" class="wx-cmA"/><stop offset=".35" class="wx-cmB"/><stop offset="1" class="wx-cmC"/></radialGradient>'; };
  document.body.insertAdjacentHTML('beforeend', `<svg class="ovs-defs" aria-hidden="true" focusable="false"><defs>${moonlit('tall')}${moonlit('low')}`
    + '<linearGradient id="wx-ocg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" class="wx-oc0"/><stop offset=".55" class="wx-oc0" stop-opacity=".6"/><stop offset=".85" class="wx-oc0" stop-opacity="0"/></linearGradient>'
    + '<radialGradient id="wx-ltsky"><stop offset="0" stop-color="#f1ecff" stop-opacity=".9"/><stop offset=".45" stop-color="#cfc4ff" stop-opacity=".35"/><stop offset="1" stop-color="#cfc4ff" stop-opacity="0"/></radialGradient>'
    + `${blur('wx-fg', 34)}${blur('wx-ltb', 4)}</defs></svg>`);

  // One layer: a drawing laid exactly as the scene is. data-d: a delay for its motion, set as --d once laid (the page
  // allows no style attributes).
  const layer = (size, H, cls, body, d) => `<svg class="ovs ${cls}" data-size="${size}"${d ? ` data-d="${d}"` : ''} viewBox="0 0 3200 ${H}" preserveAspectRatio="xMaxYMid slice" aria-hidden="true">${body}</svg>`;

  // A cloud: a flat base with round ends, and round bumps standing on its bottom line, so the bottom stays flat. heap: a
  // few big bumps, the tallest about the middle. dome: one big dome with a smaller one each side, short. wide: its length
  // against the usual; thick: the base's height against the length; rise: how far the tallest bump stands above the base,
  // in base heights; mix: how unlike each other the bumps are; tall: its whole height in base heights, at most.
  const BANKS = [{ wide: 0.85, thick: 0.075, rise: 2.3, mix: 0.7, tall: 3.3 }, { wide: 0.55, thick: 0.115, rise: 2.5, dome: true, tall: 3.5 }];
  const bank = (r, x, y, w, hb, B) => {
    const [left, right] = [x - w / 2, x + w / 2];
    let bumps = '';
    const base = (to) => `<rect x="${f1(left)}" y="${f1(y - hb)}" width="${f1(to - left)}" height="${f1(hb)}" rx="${f1(hb / 2)}"/>`;
    const bump = (cx, rho) => { bumps += `<circle cx="${f1(cx)}" cy="${f1(y - rho * hb)}" r="${f1(rho * hb)}"/>`; };
    const chord = (rho) => hb * Math.sqrt(2 * rho - 1); // half a bump's width where it leaves the base
    if (B.dome) {
      const big = (1 + B.rise) / 2; const cx = x + (r() - 0.5) * w * 0.08;
      [-1, 1].forEach((side) => { const rho = big * (0.5 + r() * 0.16); bump(cx + side * (chord(big) + chord(rho)) * 0.72, rho); });
      bump(cx, big);
      return base(right) + bumps;
    }
    // Each bump overlaps the one before it where they meet the base's top, so the top has no flat stretch.
    let [cx, last, end, tall] = [left + hb * 0.5, 0, left + hb, 0];
    for (let i = 0; i < 60; i++) {
      const t = Math.max(0, Math.min(1, (cx + hb - left) / w));
      const rho = 0.6 + ((1 + B.rise) / 2 - 0.6) * Math.sin(Math.PI * t) ** 0.9 * (1 - B.mix * r());
      const c = chord(rho);
      cx += (last + c) * 0.8;
      if (cx + c * 0.6 > right - hb * 0.5) break;
      bump(cx, rho);
      [last, end, tall] = [c, cx + c, rho];
    }
    // A big last bump is followed by a small one, and the base ends just past the last bump, so it tapers to a round end.
    if (tall > 0.95) { const c = chord(0.66); bump(end + c * 0.6, 0.66); end += c * 1.6; }
    return base(end + hb * 0.45) + bumps;
  };
  // A cloud that its sway, or in wind its run across the sky, could bring over the moon (or the sun) goes just above it or
  // just below it. y is the cloud's flat bottom; my and pad the moon's height and the room kept round it.
  const clearOfMoon = (x, y, w, tall, windy, my, pad) => {
    if (y <= my - pad || y - tall >= my + pad) return y;
    const [lo, hi] = windy ? [x - 500, x + 1900] : [x - 125, x + 125];
    if (hi + w / 2 < 2680 - pad || lo - w / 2 > 2680 + pad) return y;
    return y - tall / 2 < my && my - pad - tall >= 34 * (pad / 42) ? my - pad : my + pad + tall;
  };
  // n clouds across the drawing's right, and three fifths as many across its left for a wide window, each a drawing of its
  // own that sways slowly, or in wind is carried across (overview.css wx-cloud).
  const clouds = (n, windy, size) => {
    const F = S.fit(size);
    const s = F.moonR / 28; // on the lower banners everything in the sky is this much smaller
    const [my, pad] = [F.m(130), F.moonR + 14 * s];
    const set = (r, count, x0, span) => Array.from({ length: count }, (_, i) => {
      const depth = i % 3;
      const B = BANKS[Math.floor(r() * BANKS.length)];
      const w = [190, 260, 340][depth] * B.wide * (0.55 + r() * 0.75) * s; const hb = w * B.thick * (0.9 + r() * 0.2);
      const x = x0 + ((i + 0.5) / count) * span + (r() - 0.5) * 160; const tall = hb * B.tall;
      const y = clearOfMoon(x, F.m([95, 150, 200][depth] + (r() - 0.5) * 50) + tall * 0.35, w, tall, windy, my, pad);
      const body = `<g class="wx-cg" fill="url(#wx-cmoon-${size})">${bank(r, x, y, w, hb, B)}</g>`;
      return layer(size, F.H, `ovs-live wx-cloud wx-d${depth}${windy ? ' wx-windy' : ''}`, body, `${(-(i / count) * 70 - r() * 8).toFixed(1)}s`);
    }).join('');
    return set(rnd(13), n, 1380, 1720) + set(rnd(29), Math.round(n * 0.6), 20, 1000);
  };
  const overcast = (size) => { const { H } = S.fit(size); return layer(size, H, 'wx-oc', `<rect width="3200" height="${H}" fill="url(#wx-ocg)"/>`); };

  // Rain: three sheets of slanted streaks, the near ones longer, each sheet's streaks drawn twice one banner's height
  // apart, so the graphics card slides it down one height and starts again. n: how many streaks against mid's; len: how long.
  const RAIN = { light: { n: 0.4, len: 0.6 }, mid: { n: 1, len: 1 }, heavy: { n: 1.8, len: 1.2 } };
  const rain = (amount, size) => {
    const { H } = S.fit(size);
    const A = RAIN[amount] ? amount : 'mid';
    const k = H / 600;
    const r = rnd(9);
    return [[160, 16], [110, 24], [50, 34]].map(([n, len], i) => {
      const L = len * RAIN[A].len * (0.5 + 0.5 * k);
      let s = '';
      for (let j = 0; j < n * RAIN[A].n; j++) {
        const x = r() * 3400; const y = r() * H; const o = (0.25 + r() * 0.5).toFixed(2);
        const line = (yy, xx) => `<line x1="${f1(xx)}" y1="${f1(yy)}" x2="${f1(xx - L * 0.28)}" y2="${f1(yy + L)}" opacity="${o}"/>`;
        s += line(y, x) + line(y - H, x + 0.28 * H);
      }
      return layer(size, H, `ovs-live wx-rain wx-${A} wx-r${i + 1}`, s);
    }).join('');
  };
  // Snow: three sizes of flake, as the Christmas snow falls (overview.css ovs-snows).
  const SNOW = { light: 0.3, mid: 0.5, heavy: 0.85 };
  const snow = (amount, size) => {
    const { H } = S.fit(size);
    const r = rnd(5);
    return [[120, 1.4], [70, 2.2], [34, 3.2]].map(([n, big], i) => {
      let s = '';
      for (let j = 0; j < n * (SNOW[amount] || SNOW.mid); j++) {
        const [x, y, rr, o] = [f1(r() * 3200), r() * H, f1(big * (0.6 + r() * 0.8)), (0.35 + r() * 0.55).toFixed(2)];
        s += `<circle cx="${x}" cy="${f1(y)}" r="${rr}" opacity="${o}"/><circle cx="${x}" cy="${f1(y - H)}" r="${rr}" opacity="${o}"/>`;
      }
      return layer(size, H, `ovs-live ovs-snows ovs-f${i + 1}`, s);
    }).join('');
  };
  // Fog: soft banks lying in the valleys and over the water, across the whole drawing, drifting slowly as one.
  const FOG = [[2200, 330, 900, 70], [2700, 420, 800, 80], [1700, 470, 900, 70], [2400, 230, 700, 50], [600, 340, 800, 65], [900, 450, 750, 75], [300, 240, 600, 50]];
  const fog = (size) => {
    const { H, m } = S.fit(size);
    return layer(size, H, 'ovs-live wx-fog', `<g class="wx-fogbank" filter="url(#wx-fg)">${FOG.map(([cx, cy, rx, ry]) => `<ellipse cx="${cx}" cy="${f1(m(cy))}" rx="${rx}" ry="${f1(m(cy + ry) - m(cy))}"/>`).join('')}</g>`);
  };

  // Lightning in a thunderstorm: a forked bolt from the clouds to the hills, somewhere new every 3 to 8 seconds, mostly
  // where a normal window shows. It flickers (on, nearly off, on again), lights the sky round it, and fades.
  const lightning = (size) => { const { H } = S.fit(size); return layer(size, H, 'wx-lt', `<rect class="wx-lt-wash" width="3200" height="${H}"/><g class="wx-lt-g"></g>`); };
  const rand = (a, b) => a + Math.random() * (b - a);
  const channel = (x, y0, y1, step, wob, drift) => {
    const pts = [[x, y0]];
    let [cx, cy] = [x, y0];
    while (cy < y1) {
      cy = Math.min(y1, cy + step * rand(0.6, 1.4));
      cx += (Math.random() - 0.5) * wob + drift;
      pts.push([cx, cy]);
    }
    return pts;
  };
  const strike = (g, size) => {
    const { m } = S.fit(size);
    const d = (pts) => `M${pts.map(([a, b]) => `${f1(a)} ${f1(m(b))}`).join('L')}`;
    const main = channel(Math.random() < 0.72 ? rand(1550, 3120) : rand(250, 1500), rand(35, 80), rand(300, 370), 18, 26, (Math.random() - 0.5) * 6);
    const twigs = Array.from({ length: 2 + Math.floor(Math.random() * 3) }, () => {
      const at = main[1 + Math.floor(Math.random() * main.length * 0.6)];
      return channel(at[0], at[1], at[1] + rand(40, 130), 12, 18, (Math.random() < 0.5 ? -1 : 1) * rand(6, 14));
    });
    g.insertAdjacentHTML('beforeend', `<g><ellipse class="wx-lt-sky" cx="${f1(main[0][0])}" cy="${f1(m(main[0][1] + 60))}" rx="820" ry="${f1(m(440) - m(60))}" fill="url(#wx-ltsky)"/>`
      + `<g class="wx-lt-bolt"><path class="wx-lt-glow" d="${d(main)}${twigs.map(d).join('')}"/><path class="wx-lt-core" d="${d(main)}"/>${twigs.map((t) => `<path class="wx-lt-twig" d="${d(t)}"/>`).join('')}</g></g>`);
    const box = g.lastElementChild;
    box.querySelector('.wx-lt-bolt').animate([{ opacity: 0 }, { opacity: 1, offset: 0.04 }, { opacity: 0.12, offset: 0.12 }, { opacity: 1, offset: 0.2 }, { opacity: 0.85, offset: 0.36 }, { opacity: 0 }], { duration: 950, fill: 'forwards' });
    box.querySelector('.wx-lt-sky').animate([{ opacity: 0 }, { opacity: 0.6, offset: 0.04 }, { opacity: 0.1, offset: 0.12 }, { opacity: 0.5, offset: 0.2 }, { opacity: 0 }], { duration: 1200, fill: 'forwards' });
    g.ownerSVGElement.querySelector('.wx-lt-wash')?.animate([{ opacity: 0 }, { opacity: 0.1, offset: 0.05 }, { opacity: 0, offset: 0.14 }, { opacity: 0.08, offset: 0.22 }, { opacity: 0 }], { duration: 1000 });
    setTimeout(() => box.remove(), 1300);
  };
  const storm = (g, size) => {
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const next = (ms) => setTimeout(() => { if (!g.isConnected) return; strike(g, size); next(rand(3000, 8000)); }, ms);
    next(1100);
  };

  // The weather's layers on one banner: in a box over the scene's own moving layers and under the monsters, and the fog in a
  // box over the monsters too (overview.css wx-front). Laid again only when the weather or the banner's size changes.
  const lay = (box, size) => {
    const lives = box.querySelector(':scope > .ovs-lives');
    if (!lives) return;
    const wx = Sky.now().weather;
    const cover = wx ? coverOf(wx) : 'none';
    const key = wx ? `${wx.kind} ${wx.amount} ${cover} ${size}` : '';
    if ((box.dataset.wx || '') === key && (!key || box.querySelector(':scope > .wx-overs'))) return;
    box.querySelectorAll(':scope > .wx-overs').forEach((e) => e.remove());
    box.dataset.wx = key;
    if (!wx) return;
    const [n, oc] = COVER[cover];
    const back = (oc ? overcast(size) : '') + (n ? clouds(n, wx.kind === 'wind', size) : '')
      + (wx.kind === 'rain' ? rain(wx.amount, size) : wx.kind === 'storm' ? rain('heavy', size) + lightning(size) : wx.kind === 'snow' ? snow(wx.amount, size) : '');
    lives.insertAdjacentHTML('afterend', `<div class="wx-overs">${back}</div>${wx.kind === 'fog' ? `<div class="wx-overs wx-front">${fog(size)}</div>` : ''}`);
    box.querySelectorAll(':scope > .wx-overs [data-d]').forEach((e) => { e.style.setProperty('--d', e.dataset.d); e.removeAttribute('data-d'); });
    box.querySelectorAll(':scope > .wx-overs .wx-lt-g').forEach((g) => storm(g, size));
  };
  const fill = () => {
    document.querySelectorAll('[data-scene][data-fit]').forEach((box) => lay(box, 'tall'));
    document.querySelectorAll('.hero-art[data-fit]').forEach((box) => lay(box, 'low'));
  };
  window.RabbidLabWeather = { tint };
  // scene.js says when a banner was drawn and when the sky changed; it loaded first, so the colours are painted again now
  // that the weather's can be added.
  document.addEventListener('rabbidlab:scene', fill);
  window.dispatchEvent(new Event('rabbidlab:sky'));
})();
