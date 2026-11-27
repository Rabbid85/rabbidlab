// The night scene behind every banner: stars, the moon, layered hills with fog between them, a treeline, and a lake with the
// moon's light on it. One drawing for every page ("This is the same banner on all pages, just size changes right?"): Overview's,
// and on the lower banners the same one drawn again for their height, with the same hills, moon and lake in the same places
// across and the heights squeezed to fit. Drawn from fixed seeds, so it looks the same every time. The colours come from
// overview.css, mixed from the accent, so the scene follows the lab's colour.
(() => {
  const rnd = (seed) => { let s = seed; return () => (s = (s * 16807) % 2147483647) / 2147483647; };
  const f1 = (n) => n.toFixed(1);
  const PINE = [[0, -1], [0.5, -0.58], [0.26, -0.58], [0.78, -0.24], [0.42, -0.24], [1, 0], [-1, 0], [-0.42, -0.24], [-0.78, -0.24], [-0.26, -0.58], [-0.5, -0.58]];
  // A pine standing at (x, y), h tall: its corners, and its outline.
  const corners = (x, y, h) => PINE.map(([a, b]) => [x + a * h * 0.34, y + b * h]);
  const pine = (x, y, h) => `M${corners(x, y, h).map(([a, b]) => `${f1(a)} ${f1(b)}`).join('L')}Z`;
  // Whether a point is inside the pine standing at (x, y), h tall.
  const inPine = ([x, y, h], px, py) => {
    const [a, b] = [(px - x) / (h * 0.34), (py - y) / h];
    return PINE.reduce((hit, [x1, y1], i) => { const [x2, y2] = PINE[(i + 1) % PINE.length]; return (y1 > b) !== (y2 > b) && a < x1 + ((b - y1) * (x2 - x1)) / (y2 - y1) ? !hit : hit; }, false);
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
  const moon = (x, y, rad) => `<circle cx="${x}" cy="${f1(y)}" r="${rad * 2.4}" class="ovs-halo" fill="url(#ovs-glow)" opacity="${HAZE}"/>`
    + `<g transform="translate(${x} ${f1(y)})"><circle class="ovs-rimglow" r="${rad + 1.2}" fill="#efeaff" opacity=".35" filter="url(#ovs-b05)"/><circle r="${rad}" fill="url(#ovs-moonfill)"/>`
    + `<g class="ovs-seas" clip-path="url(#ovs-mc${rad})" fill="#9C94C6" opacity=".16"><g filter="url(#ovs-b2)">${SEAS.map(([a, b, rx, ry, t]) => `<ellipse transform="rotate(${t} ${f1(a * rad)} ${f1(b * rad)})" cx="${f1(a * rad)}" cy="${f1(b * rad)}" rx="${f1(rx * rad)}" ry="${f1(ry * rad)}"/>`).join('')}</g></g></g>`;
  // A shooting star: a streak that crosses its path and fades as it goes, so it never stops on screen. It carries the band of
  // sky it may cross and where the moon is, for wander() to put it somewhere new each time.
  const meteor = (x, y, deg, [y0, y1], [mx, my, mr]) => `<g class="ovs-mpos" data-sky="${f1(y0)} ${f1(y1)}" data-moon="${mx} ${f1(my)} ${mr}" transform="translate(${x} ${f1(y)}) rotate(${deg})"><g class="ovs-meteor">`
    + '<rect x="-110" y="-.9" width="110" height="1.8" rx=".9" fill="url(#ovs-tail)"/><circle r="1.7" fill="#fff"/><circle r="5" fill="#fff" opacity=".35" filter="url(#ovs-b3)"/></g></g>';
  // A scene twice as wide as it is drawn: the drawing on the right, and on its left its mirror image without the moon and its
  // light, so a wide banner shows more land and sky instead of zooming in and lifting the moon out of sight ("When dev is
  // full screen the moon goes way up"). Each layer is [where, svg]: 'all' drawn once across the whole width, 'right' on the
  // drawing only, 'both' on both sides overlapping by one unit so solid land shows no seam, 'soft' on both without the
  // overlap, for what can be seen through.
  // 'pair': the drawing's own and the mirror's own, for where they differ.
  // A layer whose key the left half draws for itself (own, leftHalf below) has that on the left in place of the mirror,
  // overlapping the drawing by one unit so no hairline shows where they meet.
  const twice = (W, layers, own = {}) => layers.map(([where, s, key]) => (own[key] != null
    ? `<g transform="translate(${W} 0)">${where === 'pair' ? own.nearRight ?? s[0] : s}</g><g transform="translate(1 0)">${own[key]}</g>`
    : where === 'all' ? s
      : where === 'pair' ? `<g transform="translate(${W} 0)">${s[0]}</g><g transform="translate(${W + 1} 0) scale(-1 1)">${s[1]}</g>`
        : `<g transform="translate(${W} 0)">${s}</g>${where === 'right' ? '' : `<g transform="translate(${where === 'both' ? W + 1 : W} 0) scale(-1 1)">${s}</g>`}`)).join('');

  // The engine: what every picture behind a banner, and everything laid on one (the weather, the monsters), shares.
  // Whether people asked for less motion.
  const still = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
  // The life of something laid on a banner, on its element el: it does nothing once it has left the page, and rests while
  // it can't be seen (its page is not the one open, or the tab is behind another), so nothing runs for a banner nobody
  // sees. later: a timer that only fires while el is on the page. on: a listener that goes when el does. showing: whether
  // it can be seen now. rest(fn): fn waits, and runs when the banner is next seen.
  const lives = new Set();
  const life = (el) => {
    const end = new AbortController();
    const resting = [];
    const showing = () => !document.hidden && el.getClientRects().length > 0;
    const me = { gone: () => { if (el.isConnected) return false; end.abort(); resting.length = 0; lives.delete(me); return true; } };
    const wake = () => { if (!me.gone() && showing()) resting.splice(0).forEach((fn) => fn()); };
    ['rabbidlab:show', 'visibilitychange'].forEach((type) => document.addEventListener(type, wake, { signal: end.signal }));
    lives.add(me);
    return {
      later: (fn, ms) => setTimeout(() => { if (!me.gone()) fn(); }, ms),
      on: (target, type, fn) => target.addEventListener(type, (e) => { if (!me.gone()) fn(e); }, { signal: end.signal }),
      showing,
      rest: (fn) => { resting.push(fn); },
    };
  };
  // What has left the page lets go of its listeners now, not when one of them next fires.
  const sweep = () => lives.forEach((l) => l.gone());

  // The holiday the banner wears (overview.css .ovs-halloween, .ovs-christmas). Halloween, October 15 to 31: a purple night
  // with an orange moon and mist, and three tiny bats flitting past the moon ("Holloween is the bats."). Christmas, from the
  // day after Thanksgiving (the fourth Thursday in November) to December 26: a cold blue night, snow on the shore and the
  // ground and falling, the pines dark, coloured lights on the big pines in the corners ("Christmas is done"); the
  // monsters' pines are lit too, and Santa runs between them (monsters.js). Which one is on today, and whether this person
  // shows holidays at all, is the sky's to say (sky.js); when that changes, every banner is drawn again (again, below).
  const Sky = window.RabbidLabSky;
  let holiday = Sky?.holiday() ?? '';
  if (holiday) document.documentElement.classList.add(`ovs-${holiday}`);

  // The time of day, from the clock: dawn, the day with the sun, dusk, night ("Day and night, dawn, dusk look amazing", then
  // "Write this to dev" of take B in the day-and-night mock): the same bright dawn, day and dusk in dark and light mode, with
  // dark words by day, and the night drawn from the accent. The colours go on <html> as --sk-* (overview.css falls back to the
  // night): at night a holiday's own, at other times its tint on the time's. ?time=dawn, day, dusk or night shows one.
  const rgb = (c) => [1, 3, 5].map((i) => parseInt(c.slice(i, i + 2), 16));
  const mix = (a, b, t) => { const [x, y] = [rgb(a), rgb(b)]; return `#${x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, '0')).join('')}`; };
  const toward = (pal, keys, c, t) => Object.fromEntries(keys.filter((k) => /^#[0-9a-f]{6}$/i.test(pal[k] || '')).map((k) => [k, mix(pal[k], c, t)]));
  // A sun in place of the moon: white, a warm edge, no seas, a wide soft halo, warm rays, no shooting star.
  const SUN = { moon0: '#ffffff', moon1: '#fffbe8', moon2: '#ffefb8', seas: '0', halo: '.6', rimglow: '#fff6d8', rimglowOp: '.9', raysOp: '.09', meteor: 'hidden' };
  const TIMES = {
    night: {},
    dawn: { sky0: '#2b2f6b', sky1: '#d8889b', sky2: '#f7c89e', l1: '#5c4b7c', l2: '#4a3d69', l3: '#3a3058', l4: '#2c2547', l5: '#1d1830', fog: '#f3b8a8', fogK: '.7',
      glow: '#ffd4aa', water0: '#c98b9b', water1: '#2b2447', rim: '#ffcdb5', mist: '#f6c4b4', star: '.25', glint: '#fff0e0', ...SUN, moon1: '#fff0dc', moon2: '#ffcf9e', halo: '.55' },
    day: { sky0: '#5f9fe9', sky1: '#a7ccf3', sky2: '#e4effb', l1: '#94b8d0', l2: '#77a18d', l3: '#5b8568', l4: '#446b53', l5: '#2f4d3c', pines3: '#3f6a4e', pines5: '#23402f',
      fog: '#dcebf8', fogK: '.5', glow: '#fff3c4', water0: '#86b6e2', water1: '#2c5d8e', rim: '#ffffff', mist: '#ffffff', mistK: '.6', star: '0', glint: '#ffffff',
      text: '#10213f', lead: '#26364f', eye: '#2c5d8e', shade: 'transparent', ...SUN }, // no shade behind the words by day ("No shade", the haze mock)
    dusk: { sky0: '#1d1a4a', sky1: '#a5487b', sky2: '#f69c5c', l1: '#4b305f', l2: '#3c2651', l3: '#2f1e43', l4: '#231735', l5: '#160f23', fog: '#f08c6c', fogK: '.7',
      glow: '#ffc58c', water0: '#a1517b', water1: '#1b1231', rim: '#ffb28c', mist: '#f3a080', star: '.5', glint: '#ffe0c4', ...SUN, moon1: '#ffe2c0', moon2: '#ffa868', halo: '.6' },
  };
  // A holiday's night, and how it leans the other times its way.
  const HILLS = ['l1', 'l2', 'l3', 'l4'];
  const HOLIDAYS = {
    halloween: {
      name: 'Halloween',
      night: { sky0: '#070208', sky1: '#1f0a25', sky2: '#47183c', l1: '#3d1a3a', l2: '#2d1331', l3: '#220e28', l4: '#180a1e', l5: '#0c0611', fog: '#ff8a3d', glow: '#ffae5c', rim: '#ffb35c',
        water0: '#4a2039', water1: '#0b0510', mist: '#ff9a4a', mistK: '.8', moon0: '#ffe4b5', moon1: '#ffb866', moon2: '#e2832b', glint: '#ffcf8a', seas: '.12', halo: '.35' },
      tint: (p) => ({ ...toward(p, ['sky1', 'sky2'], '#7a2a6a', 0.3), ...toward(p, ['fog', 'mist'], '#ff8a3d', 0.45), ...toward(p, HILLS, '#3d1a3a', 0.25) }),
    },
    christmas: {
      name: 'Christmas',
      night: { sky0: '#02040d', sky1: '#0b1532', sky2: '#1f3058', l1: '#34497a', l2: '#1d2d52', l3: '#162343', l4: '#8fa2c4', l5: '#d3deef', pines3: '#0b1424', pines4: '#0c1828',
        pines5: '#07101c', fog: '#aec2ea', glow: '#e6eeff', rim: '#eef3ff', water0: '#4a5f8c', water1: '#111b33', mist: '#d2e0fa', glint: '#eef4ff' },
      // Snow on the far hills, the shore and the ground in front, the pines on them still dark.
      tint: (p) => ({ ...toward(p, ['l1'], '#ffffff', 0.35), ...toward(p, ['l4'], '#ffffff', 0.7), ...toward(p, ['l5'], '#ffffff', 0.82),
        ...toward(p, ['water0'], '#d6e4f4', 0.3), rim: '#ffffff', pines4: p.pines4 || p.l4 || '#0c1828', pines5: p.pines5 || p.l5 || '#07101c' }),
    },
  };
  const NAMES = { dawn: 'Dawn', day: 'Day', dusk: 'Dusk', night: 'Night' };
  const asked = new URLSearchParams(location.search).get('time');
  const timeNow = () => {
    if (NAMES[asked]) return asked;
    const m = new Date().getHours() * 60 + new Date().getMinutes();
    return m >= 360 && m < 480 ? 'dawn' : m >= 480 && m < 1020 ? 'day' : m >= 1020 && m < 1170 ? 'dusk' : 'night';
  };

  // Christmas lights: along the lower edge of each of a pine's three tiers, from one branch tip to the other, 3, 4 and 5
  // plain coloured bulbs, the same colours in the same order on every pine ("Each tree needs the same light setup").
  const BULBS = ['#ff4b4b', '#ffd34d', '#46d46e', '#4fa8ff', '#ff8f3a'];
  const TIERS = [[-1, -0.58, 0, 0.5], [-0.58, -0.24, 0.26, 0.78], [-0.24, 0, 0.42, 1]]; // top, bottom, half widths (pine)
  const ROW = [3, 4, 5];
  // A pine's bulbs, standing at (cx, y), h tall. big: a soft glow round each, and some twinkle (r says which). lift takes a
  // twinkling bulb into its beat's own drawing, and says whether it did.
  const lights = (cx, y, h, big, r, lift) => {
    const w = h * 0.34;
    const rad = big ? Math.min(1.8, h * 0.016) : Math.max(0.6, h * 0.022);
    const at = [];
    TIERS.forEach(([top, bot, w0, w1], t) => {
      for (let j = 0; j < ROW[t]; j++) {
        const u = j / (ROW[t] - 1);
        const f = 0.86 + 0.08 * Math.sin(Math.PI * u); // along the tier's lower edge, drooping a little in the middle
        at.push([cx + (2 * u - 1) * (w0 + (w1 - w0) * f) * w * 0.84, y + (top + (bot - top) * f) * h, BULBS[(t * 2 + j) % BULBS.length]]);
      }
    });
    const glow = big ? `<g opacity=".4" filter="url(#ovs-b3)">${at.map(([x, yy, c]) => `<circle cx="${f1(x)}" cy="${f1(yy)}" r="${f1(rad * 2.6)}" fill="${c}"/>`).join('')}</g>` : '';
    return glow + at.map(([x, yy, c]) => {
      const beat = big && r() < 0.4 ? 1 + Math.floor(r() * 4) : 0;
      const bulb = `<g><circle cx="${f1(x)}" cy="${f1(yy)}" r="${f1(rad)}" fill="${c}"/>${big ? `<circle cx="${f1(x)}" cy="${f1(yy)}" r="${f1(rad * 0.38)}" fill="#fff" opacity=".7"/>` : ''}</g>`;
      return beat && lift?.(x, yy, rad, beat, bulb) ? '' : bulb;
    }).join('');
  };
  // What is laid on a banner needs from the engine: the seeded randoms, a number to one decimal, a blur, less motion or
  // not, and a life (above); a layer and the snow (below, Scene.layer and Scene.snow), and how a size is fitted (Scene.fit).
  // And from the picture: the holiday, a pine with its corners and its lights, where the big pines stand in each size
  // ([x, base, height] across the whole banner in drawing order, for the monsters and the wind), the far shore's pines in
  // each size ([the drawing's, its left half's], for the wind), and how far below the moon's place the sun or moon stands
  // now (disc). solid: a solid colour is shown in place of the picture.
  const blur = (id, sd) => `<filter id="${id}" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="${sd}"/></filter>`;
  const Scene = { holiday, snowing: false, solid: false, disc: 0, rnd, f1, blur, still, life, pine, corners, lights: (cx, y, h) => lights(cx, y, h, false, () => 1), near: {}, shore: {} };
  window.RabbidLabScene = Scene;
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
  // A height on Overview's drawing as it lies on a size's.
  const mapOf = (k) => (y) => {
    for (let i = 1; i < k.map.length; i++) {
      const [a, A] = k.map[i - 1];
      const [b, B] = k.map[i];
      if (y <= b || i === k.map.length - 1) return A + ((y - a) * (B - A)) / (b - a);
    }
    return y;
  };
  // For what is laid over a drawing (weather.js): its height in its own units, its heights, and how big its near things are.
  Scene.fit = (size) => ({ H: SIZES[size].h, m: mapOf(SIZES[size]), near: SIZES[size].near, moonR: SIZES[size].moonR });
  // One layer: a drawing laid exactly as the picture is, 3200 wide and its size's height, fitted the same way. d: a delay
  // for its motion, which whoever lays it sets as --d (the page allows no style attributes).
  const layer = (size, cls, body, d) => `<svg class="ovs${cls ? ` ${cls}` : ''}" data-size="${size}"${d ? ` data-d="${d}"` : ''} viewBox="0 0 3200 ${SIZES[size].h}" preserveAspectRatio="xMaxYMid slice" aria-hidden="true">${body}</svg>`;
  // Snow falling, at Christmas and when it snows where the lab is (weather.js): flakes in three sizes, each size a layer of
  // its own with its flakes twice, one banner's height above the other, which the graphics card slides down one height
  // and starts again (overview.css ovs-snows). counts: how many flakes of each size. A layer's flakes are four shapes, one
  // for each of four brightnesses (SOFT), not a shape to every flake: the page goes over every shape on a banner each
  // time anything on it changes, and with a shape to every flake or streak of rain that was half its work in a storm.
  const SOFT = [0.42, 0.56, 0.69, 0.83];
  const snow = (size, counts) => {
    const H = SIZES[size].h;
    const r = rnd(5);
    return [1.4, 2.2, 3.2].map((big, i) => {
      const d = SOFT.map(() => '');
      for (let j = 0; j < counts[i]; j++) {
        const [x, y, rr, b] = [r() * 3200, r() * H, f1(big * (0.6 + r() * 0.8)), Math.min(SOFT.length - 1, Math.floor(r() * SOFT.length))];
        const flake = (yy) => `M${f1(x - rr)} ${f1(yy)}a${rr} ${rr} 0 1 0 ${f1(2 * rr)} 0a${rr} ${rr} 0 1 0 ${f1(-2 * rr)} 0`;
        d[b] += flake(y) + flake(y - H);
      }
      return layer(size, `ovs-live ovs-snows ovs-f${i + 1}`, d.map((p, b) => (p ? `<path d="${p}" opacity="${SOFT[b]}"/>` : '')).join(''));
    }).join('');
  };
  Object.assign(Scene, { layer, snow });
  // A colour in place of the times of day (the sky's window, Colour): the same drawing with every part a shade of the one
  // colour, deep in dark mode and pale in light mode with dark words ("Can change color here, like the actual background
  // color"). It is the night's drawing, with the moon.
  const mono = (c, light) => {
    const d = (t) => mix(c, '#05040c', t);
    const l = (t) => mix(c, '#ffffff', t);
    return light
      ? { sky0: l(0.35), sky1: l(0.55), sky2: l(0.78), l1: l(0.45), l2: l(0.3), l3: l(0.15), l4: d(0.15), l5: d(0.35), pines3: d(0.05), pines5: d(0.45), water0: l(0.5), water1: d(0.2),
        fog: l(0.8), glow: l(0.92), rim: '#ffffff', mist: l(0.85), glint: '#ffffff', star: '0', moon0: '#ffffff', moon1: l(0.95), moon2: l(0.8), seas: '0', halo: '.5',
        text: d(0.75), lead: d(0.6), eye: d(0.4), shade: l(0.85) }
      : { sky0: d(0.92), sky1: d(0.8), sky2: d(0.55), l1: d(0.62), l2: d(0.7), l3: d(0.78), l4: d(0.84), l5: d(0.92), pines3: d(0.82), pines5: d(0.95), water0: d(0.6), water1: d(0.95),
        fog: l(0.25), glow: l(0.7), rim: l(0.5), mist: l(0.4), glint: l(0.8), moon0: '#ffffff', moon1: l(0.9), moon2: l(0.7) };
  };
  const root = document.documentElement;
  // What this person picked for the mode the console is in (sky.js): the clock's time of day or one time for good, with the
  // holiday's colours and the weather's over it (weather.js, which also lays what falls and drifts); the drawing in one
  // colour; or one solid colour in place of the drawing, with nothing moving (overview.css .ovs-flat).
  const look = () => {
    const sky = Sky?.now() ?? { time: 'clock' };
    if (sky.solid) return { time: 'night', pal: { flat: sky.solid, shade: 'transparent' }, sky };
    if (sky.colour) return { time: 'night', pal: mono(sky.colour, root.dataset.theme === 'light'), sky };
    const time = NAMES[asked] ? asked : NAMES[sky.time] ? sky.time : timeNow();
    const pal = { ...TIMES[time] };
    const hol = HOLIDAYS[holiday];
    if (hol) Object.assign(pal, time === 'night' ? hol.night : hol.tint(pal));
    const wx = sky.weather;
    if (wx) Object.assign(pal, window.RabbidLabWeather?.tint(wx, time) ?? {});
    return { time, pal, sky, hol, wx };
  };
  let painted = '';
  const apply = ({ time, pal, sky, hol, wx }) => {
    const key = `${time} ${JSON.stringify(pal)}`;
    if (key !== painted) {
      [...root.style].filter((p) => p.startsWith('--sk-')).forEach((p) => root.style.removeProperty(p));
      Object.entries(pal).forEach(([k, v]) => root.style.setProperty(`--sk-${k}`, v));
      root.dataset.sky = time;
      root.classList.toggle('ovs-flat', !!sky.solid);
      painted = key;
    }
    Scene.disc = DISC[time];
    // The chip at the top right of Overview's banner: the time of day and the clock, the weather, then the holiday; or the
    // colour's name. It opens the sky's window.
    const chip = document.getElementById('hero-sky');
    if (chip) {
      const what = sky.solid ? `${sky.name}, solid` : sky.colour ? sky.name : `${NAMES[time]}, ${new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
      chip.textContent = `${what}${wx ? ` · ${Sky.says(wx)}` : ''}${hol ? ` · ${hol.name}` : ''}`;
      chip.hidden = false;
    }
  };
  // The sun and the moon ("Sun drops, mooon rises, visa versa when time changes.", then "This one." of the take where the
  // sun also climbs). The disc, with its halo and rays, is a drawing of its own between the sky and the hills, which the
  // graphics card moves (overview.css .ovs-disc). When night gives way to a time with the sun, or the sun to the night, it
  // sets behind the far hills, its light on the water going out; out of sight it becomes the other and the sky turns; the
  // other rises. Through the day it is the same sun: low over the hills at dawn, high by day, low again at dusk.
  // DISC: how far below the moon's place it stands at each time, in Overview's units; DOWN: out of sight.
  const DISC = { night: 0, dawn: 150, day: -34, dusk: 150 };
  const DOWN = 270;
  const place = (y, ms = 0, ease = 'ease') => {
    const low = mapOf(SIZES.low);
    Object.entries({ '--disc': String(y), '--disc-low': f1(low(130 + y) - low(130)), '--disc-t': `${ms}ms`, '--disc-e': ease }).forEach(([k, v]) => root.style.setProperty(k, v));
  };
  let shown = null; // the time of day the colours are painted for
  let goal = null; // the time being turned to, while the disc sets and rises
  let sankAt = 0; // when the disc began to set, while it is down
  let turn = []; // what is still to come of a turn
  const paint = () => {
    const v = look();
    const later = (fn, ms) => turn.push(setTimeout(fn, ms));
    // At once: the first time, in a tab that is not showing, for people who asked for less motion, and a solid colour.
    if (shown === null || document.hidden || v.sky.solid || root.classList.contains('ovs-flat') || still()) {
      turn.forEach(clearTimeout);
      [turn, goal, sankAt, shown] = [[], null, 0, v.time];
      root.classList.remove('ovs-set');
      apply(v);
      place(DISC[v.time]);
      return;
    }
    if (v.time === shown && goal === null) { apply(v); return; }
    if (v.time === goal) return;
    turn.forEach(clearTimeout);
    [turn, goal] = [[], v.time];
    // The colours as they are when it lands, and what is laid on the drawings told (the clouds keep clear of the disc).
    const land = (told) => { const now = look(); apply(now); shown = now.time; if (told) document.dispatchEvent(new CustomEvent('rabbidlab:scene')); };
    if (sankAt || (shown === 'night') !== (v.time === 'night')) {
      if (!sankAt) {
        sankAt = performance.now();
        place(DOWN, 2400, 'cubic-bezier(.5, 0, .9, .6)');
        root.classList.add('ovs-set');
      }
      later(() => {
        land(true);
        later(() => { place(DISC[shown], 2800, 'cubic-bezier(.1, .5, .4, 1)'); root.classList.remove('ovs-set'); [sankAt, goal] = [0, null]; }, 600);
      }, Math.max(0, 2400 - (performance.now() - sankAt)));
    } else {
      land(false);
      place(DISC[shown], 3200, 'ease-in-out');
      goal = null;
    }
  };
  paint();
  Scene.solid = !!Sky?.now().solid;
  // The lake: the picture behind every banner. fit: on a window wide enough to show the middle of the drawing well in from
  // its left edge, how far in that edge is across the drawing (0 when all of it shows); null on a narrower one.
  const lake = (size, fit = null) => {
    const W = 1600;
    const k = SIZES[size];
    const H = k.h;
    const m = mapOf(k);
    // A ridge line: midpoint displacement for the rough edge, with bell-shaped peaks on top.
    const ridge = (r, base, amp, rough, peaks = []) => {
      const N = 256;
      const ys = new Array(N + 1).fill(0);
      ys[0] = (r() - 0.5) * amp;
      ys[N] = (r() - 0.5) * amp;
      for (let step = N, d = amp; step > 1; step /= 2, d *= rough) {
        for (let i = step / 2; i < N; i += step) ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 + (r() - 0.5) * d;
      }
      const at = (x, y) => peaks.reduce((v, [px, ph, pw]) => v - ph * Math.exp(-((x - px) ** 2) / (2 * pw * pw)), base + y);
      const pts = ys.map((y, i) => { const x = (i / N) * W; return [x, m(at(x, y))]; });
      pts.y0 = at(0, ys[0]); // where it starts, before fitting, for the left half's own ridge to end on
      return pts;
    };
    // A ridge over the left half that ends where the drawing's own begins (to), so the two meet.
    const join = (r, to, base, amp, rough, peaks) => {
      const N = 256;
      const pk = (x) => peaks.reduce((s, [px, ph, pw]) => s + ph * Math.exp(-((x - px) ** 2) / (2 * pw * pw)), 0);
      const ys = new Array(N + 1).fill(0);
      ys[0] = (r() - 0.5) * amp;
      ys[N] = to - base + pk(W);
      for (let step = N, d = amp; step > 1; step /= 2, d *= rough) {
        for (let i = step / 2; i < N; i += step) ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 + (r() - 0.5) * d;
      }
      return ys.map((y, i) => { const x = (i / N) * W; return [x, m(base + y - pk(x))]; });
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
    // keep: an array to note each pine in [x, base, height], for what stands on them (the Christmas lights, the monsters).
    const forest = (r, pts, from, to, hMin, hMax, gap, s, some = false, keep = null) => {
      let d = '';
      for (let x = from; x < to; x += gap * (0.5 + r())) {
        const [y, h] = [yAt(pts, x) + 4 * s, (hMin + r() * (hMax - hMin)) * s * (some ? lift() : 1)];
        keep?.push([x, y, h]);
        d += pine(x, y, h);
      }
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
    // The shore and its pines apart, so in snow the ground can be white and the pines dark.
    const shoreLand = `M0 ${f1(SH)}L${shore.map(([a, b]) => `${f1(a)} ${f1(Math.min(b, SH))}`).join('L')}L${W} ${f1(SH)}Z`;
    const shorePines = forest(q, shore, 0, W, 10, 28, 13, k.tree * 0.7 + k.near * 0.3, true);
    const band = `<path class="ovs-l4" d="${shoreLand}"/><path class="ovs-l4 ovs-pines ovs-shorep" d="${shorePines}"/>`;
    // Every other row is drawn, a little blurred and gently shimmering, with no sharp copy on top ("This needs to be softened,
    // the light looks... odd", then "this is sexy" of the fewer strokes). They fade to nothing halfway down the water, so no
    // light glows above the strip ("what is this glow above the bar, get rid of this").
    const BEATS = [1, 2, 3, 4]; // the four beats the stars twinkle and the water's light shimmers on (overview.css ovs-d1 to 4)
    const glints = BEATS.map(() => '');
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
        const beat = 1 + Math.floor(q() * 4);
        const stroke = `<rect x="${f1(gx)}" y="${f1(y)}" width="${f1(w)}" height="${f1((0.6 + t * 1.2) * k.glint)}" rx=".6" opacity="${((0.14 + q() * 0.46 * (1 - t * 0.45)) * 0.65 * fade).toFixed(2)}"/>`;
        if (row && fade > 0) glints[beat - 1] += stroke;
        gx += w + 2 + q() * span * 0.2;
      }
    }
    const near = ridge(q, 660, 20, 0.5, [[0, 175, 170], [W, 185, 200]]);
    // The big pines in the corners of the drawing. Where each stands is kept for the lights and the monsters (Scene.near).
    const [keepA, keepB] = [[], []];
    const pinesA = forest(q, near, 0, 380, 60, 125, 26, k.near, false, keepA);
    const pinesB = forest(q, near, 1240, W, 60, 125, 26, k.near, false, keepB);
    const [sy, srx, sry] = k.sheen;
    // The left half is land of its own, not the right half flipped over ("This spot looks mirrorred.... this looks bad",
    // then "Pines at both edges", "This is 100% my choice too"): hills, far trees and a far shore that carry on from where
    // the drawing's begin, their peaks placed so none sits where a mirror's would, big pines where the halves meet, and a
    // far-left wooded hill that moves fit units in, to stand at the window's left edge however wide the window is.
    const g = rnd(41);
    const A1 = join(g, L1.y0, 350, 80, 0.55, [[430, 105, 170], [1010, 62, 150]]);
    const A2 = join(g, L2.y0, 395, 70, 0.55, [[260, 70, 170], [770, 44, 150]]);
    const A3 = join(g, L3.y0, 440, 50, 0.5, [[620, 40, 210]]);
    const treesL = forest(g, A3, 2, W - 6, 9, 20, 15, k.tree, true);
    const q2 = rnd(43);
    const shoreL = join(q2, shore.y0, 436 - 9, 10, 0.5, [[480, 14, 160]]);
    const bandL = `<path class="ovs-l4" d="M0 ${f1(SH)}L${shoreL.map(([a, b]) => `${f1(a)} ${f1(Math.min(b, SH))}`).join('L')}L${W} ${f1(SH)}Z"/>`
      + `<path class="ovs-l4 ovs-pines ovs-shorep" d="${forest(q2, shoreL, 2, W - 6, 10, 28, 13, k.tree * 0.7 + k.near * 0.3, true)}"/>`;
    Scene.shore[size] = [shorePines, bandL.match(/ovs-shorep" d="([^"]*)"/)[1]];
    const n2 = rnd(47);
    const nearL = join(n2, near.y0, 660, 20, 0.5, [[W, 175, 170], [0, 185, 200]]);
    const [mid, edge] = [[], []];
    const pinesMid = forest(n2, nearL, W - 380, W - 12, 55, 115, 28, k.near, false, mid);
    const pinesEdge = forest(n2, nearL, 0, 380, 60, 125, 26, k.near, false, edge);
    const dx = fit ?? 0;
    // The front land cut where it lies below the banner's foot, so the far-left piece can move on its own.
    const cut = (a, b) => { const pts = nearL.filter(([x]) => x >= a && x <= b); return `M${f1(pts[0][0])} ${H}L${pts.map(([x, y]) => `${f1(x)} ${f1(y)}`).join('L')}L${f1(pts[pts.length - 1][0])} ${H}Z`; };
    const own = {
      l1: land(A1, 'ovs-l1', 0.5), l2: land(A2, 'ovs-l2', 0.4), l3: land(A3, 'ovs-l3', 0.3) + `<path class="ovs-l3 ovs-pines" d="${treesL}"/>`,
      reflect: `<g transform="translate(0 ${f1(2 * SH)}) scale(1 -1)" opacity=".26" filter="url(#ovs-b1)">${bandL}</g>`,
      band: bandL,
      near: `<path class="ovs-l5" d="${cut(600, W)}"/><path class="ovs-l5 ovs-pines ovs-big" d="${pinesMid}"/><g transform="translate(${dx} 0)"><path class="ovs-l5" d="${cut(0, 600)}"/><path class="ovs-l5 ovs-pines ovs-big" d="${pinesEdge}"/></g>`,
    };
    // The trees in the middle ("My pick.", On a wooded hill): where a window shows the middle, the big pines there stand on a
    // broad dark hill that rises behind them and slopes to the banner's foot on both sides, so no water shows under them.
    // The hill is wooded all over ("This needs to be filled out with trees. The white in the middle is stupid.", of its
    // bare ground showing white under the pines in snow): it is the pines' own colour with a line of pines along its crest,
    // it lies over the front land's own hump there, and this half's ground where the halves meet is wooded too.
    if (fit !== null) {
      const top = [];
      for (let x = -380; x <= 380; x += 20) top.push(`${f1(x * k.near)} ${f1(m(470 + 0.00038 * x * x))}`); // as wide as the pines are big
      const ps = (list) => list.map(([x, y, h]) => pine(x, y, h)).join('');
      const hr = rnd(71);
      let crest = '';
      for (let x = -372; x <= 372; x += 9 + hr() * 9) crest += pine(x * k.near, m(476 + 0.00038 * x * x), (20 + hr() * 26) * k.near);
      own.nearRight = `${land(near, 'ovs-l5')}<path class="ovs-l5 ovs-pines" d="M${f1(-380 * k.near)} ${H}L${top.join('L')}L${f1(380 * k.near)} ${H}Z${crest}"/><path class="ovs-l5 ovs-pines ovs-big" d="${ps(keepA)}${ps(keepB)}"/>`;
      own.near = own.near.replace('<path class="ovs-l5" d="', '<path class="ovs-l5 ovs-pines" d="');
    }
    // Where each big pine stands across the whole banner, in the order they are drawn, for the lights and the monsters.
    const standing = [...keepA, ...keepB].map(([x, y, h]) => [W + x, y, h]).concat(mid.map(([x, y, h]) => [x + 1, y, h]), edge.map(([x, y, h]) => [x + dx + 1, y, h]));
    Scene.near[size] = standing;
    // What moves is in drawings of its own on top (ovs-live), each one faded, stretched or moved whole by the graphics card,
    // so the page does no work per frame and the motion is as smooth as the screen. Moving inside the big drawing, every
    // twinkle painted all of it again, blurs and all, at the screen's rate. The stars that twinkle, clear of the hills: one
    // drawing for each of their four beats (the ones lower down hold still). The moon's light on the water the same. The
    // shooting star. At Christmas the bulbs that twinkle and the snow.
    const live = (cls, body) => layer(size, `ovs-live ${cls}`, body);
    const liveY = m(185);
    const split = (s) => {
      const live = BEATS.map(() => '');
      const still = s.replace(/<circle class="ovs-star ovs-tw ovs-d(\d)"[^>]*\/>/g, (c, d) => {
        const plain = c.replace(/ ovs-tw ovs-d\d/, '');
        if (Number(c.match(/cy="([\d.]+)"/)[1]) >= liveY) return plain;
        live[d - 1] += plain;
        return '';
      });
      return [still, live];
    };
    const [skyStill, skyLive] = split(sky);
    const [skyStillL, skyLiveL] = split(stars(rnd(53), 190, 320));
    own.stars = `<g>${skyStillL}</g>`;
    // Christmas: each big pine drawn again over its own with its lights, one at a time in drawing order, so a pine in front
    // hides the lights of the one behind. A bulb that twinkles goes in its beat's own drawing on top, unless a pine in
    // front covers any of it; then it holds still where it is.
    const lit = rnd(83);
    const twinkle = BEATS.map(() => '');
    const hidden = (i, x, y, rad) => standing.slice(i + 1).some((t) => [[0, 0], [rad, 0], [-rad, 0], [0, rad], [0, -rad]].some(([a, b]) => inPine(t, x + a, y + b)));
    const christmas = holiday !== 'christmas' ? '' : `<g>${standing.map(([x, y, h], i) => `<path class="ovs-l5 ovs-pines" d="${pine(x, y, h)}"/>`
      + lights(x, y, h, true, lit, (bx, by, rad, beat, bulb) => { if (hidden(i, bx, by, rad)) return false; twinkle[beat - 1] += bulb; return true; })).join('')}</g>`;
    const lives = BEATS.map((d) => live(`ovs-tw ovs-d${d}`, `<g transform="translate(${W} 0)">${skyLive[d - 1]}</g><g transform="translate(1 0)">${skyLiveL[d - 1]}</g>`)
      + live(`ovs-glint ovs-shim ovs-d${d}`, `<g transform="translate(${W} 0)" filter="url(#ovs-b1)" opacity=".7">${glints[d - 1]}</g>`)
      + (twinkle[d - 1] ? live(`ovs-twinkle ovs-d${d}`, twinkle[d - 1]) : '')).join('')
      + live('ovs-met', `<g transform="translate(${W} 0)">${meteor(300, m(90), 14, [m(30), m(220)], [mx, my, k.moonR * 5])}</g>`)
      + (holiday === 'christmas' ? snow(size, [42, 24, 12]) : '');
    // Three drawings, one over the other: the sky and its still stars; the moon or the sun with its halo and rays, a drawing
    // of its own that the graphics card moves, so it can set behind the hills and rise (paint, above); and the land and the
    // lake. At Halloween the bats, between the moon and the hills, in theirs.
    return layer(size, '', twice(W, [['all', `<rect width="${2 * W}" height="${H}" fill="url(#ovs-sky)"/>`], ['soft', `<g>${skyStill}</g>`, 'stars']], own))
      + live('ovs-disc', `<g transform="translate(${W} 0)"><circle cx="${mx}" cy="${f1(my)}" r="${k.glowR}" class="ovs-halo" fill="url(#ovs-glow)" opacity="${HAZE}"/>${beams}${moon(mx, my, k.moonR)}</g>`)
      + (holiday === 'halloween' ? live('ovs-bats', `<g transform="translate(${W} 0)">${bats(mx, m, k.moonR / 28)}</g>`) : '')
      + layer(size, '', twice(W, [
      ['both', land(L1, 'ovs-l1', 0.5), 'l1'],
      // No cloud under the moon ("Just remove this fucking cloud.").
      ['all', fog(250, 170, 0.55)],
      ['both', land(L2, 'ovs-l2', 0.4), 'l2'], ['all', fog(320, 150, 0.5)],
      ['both', land(L3, 'ovs-l3', 0.3) + `<path class="ovs-l3 ovs-pines" d="${trees}"/>`, 'l3'], ['all', fog(395, 130, 0.42)],
      ['soft', `<ellipse class="ovs-mist" cx="700" cy="${f1(m(455))}" rx="420" ry="${f1(m(481) - m(455))}" filter="url(#ovs-b24)"/>`],
      ['all', `<rect x="0" y="${f1(SH)}" width="${2 * W}" height="${f1(H - SH)}" fill="url(#ovs-water)"/>`],
      // The far shore upside down in the water, faint ("TOO bright"), and the moon's sheen kept up by the far shore. No lines
      // across the water ("What are these lines in the water. no.").
      ['soft', `<g transform="translate(0 ${f1(2 * SH)}) scale(1 -1)" opacity=".26" filter="url(#ovs-b1)">${band}</g>`, 'reflect'],
      ['right', `<ellipse class="ovs-sheen" cx="${mx}" cy="${f1(SH + sy * 0.35)}" rx="${srx}" ry="${f1(sry * 0.45)}" filter="url(#ovs-b24)"/>`],
      ['both', band, 'band'],
      ['pair', [land(near, 'ovs-l5') + `<path class="ovs-l5 ovs-pines ovs-big" d="${pinesA}${pinesB}"/>`, ''], 'near'],
      ['all', christmas],
    ], own)) + `<div class="ovs-lives">${lives}</div>`;
  };
  // The pictures a banner can show. One so far, the lake; another is one more entry here, drawn to the same sizes with the
  // same fit, and the engine, the sky, the weather and the holidays' colours work on it as they are.
  const PICTURES = { lake };
  const picture = 'lake';
  // The gradients, blurs and the moon's outlines, once for every scene, in a drawing that is always laid out: a scene on a
  // hidden page can't lend its own to the one on the page that's open.
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
      // Its own drawing flies the 420 units the streak now points along (overview.css ovs-fly).
      ['cos', 'sin'].forEach((f, n) => g.ownerSVGElement.style.setProperty(n ? '--fy' : '--fx', (420 * Math[f]((deg * Math.PI) / 180)).toFixed(1)));
      return;
    }
  };
  document.addEventListener('animationiteration', (e) => { if (e.target.classList?.contains('ovs-met')) wander(e.target.querySelector('.ovs-mpos')); });

  // How a banner's box shows the drawing (laid xMaxYMid slice, 3200 wide and H tall): on a window wide enough to show the
  // middle well in from its left edge, how far in that edge is across the drawing; null on a narrower one, and null when
  // the box is not laid out yet (a hidden page).
  const fitOf = (box, H) => {
    const { width: w, height: h } = box.getBoundingClientRect();
    if (!w || !h) return undefined;
    const left = Math.max(0, 3200 - w / Math.max(w / 3200, h / H));
    return 1600 - left > 500 ? Math.round(left) : null;
  };
  // The drawing in a box for how wide it is now, drawn again only when that changes; the monsters laid on it (monsters.js)
  // stay, and are told. The last drawing made for each size is kept, for the other banners of that size; a window dragged
  // through many widths makes many drawings, and none of the old ones is kept.
  const drawn = {};
  const lay = (box, size) => {
    const fit = fitOf(box, SIZES[size].h);
    if (fit === undefined || (box.dataset.fit === String(fit) && box.dataset.hol === holiday)) return;
    box.querySelectorAll(':scope > .ovs, :scope > .ovs-lives').forEach((e) => e.remove());
    const key = `${picture} ${fit} ${holiday}`;
    if (drawn[size]?.key !== key) drawn[size] = { key, html: PICTURES[picture](size, fit), near: Scene.near[size], shore: Scene.shore[size] };
    const made = drawn[size];
    [Scene.near[size], Scene.shore[size]] = [made.near, made.shore]; // where this width's pines stand, for the lights, the monsters and the wind
    box.insertAdjacentHTML('afterbegin', made.html);
    box.dataset.fit = String(fit);
    box.dataset.hol = holiday;
    box.dispatchEvent(new CustomEvent('rabbidlab:scene', { bubbles: true }));
  };
  // Overview's now; another page's banner the first time its page opens, so pages never opened never draw one. Every
  // banner of a size shares one drawing for a width; each starts its shooting star somewhere of its own.
  const fill = () => {
    document.querySelectorAll('[data-scene]').forEach((el) => lay(el, 'tall'));
    document.querySelectorAll('.page:not([hidden]) .hero').forEach((hero) => {
      if (!hero.querySelector(':scope > .hero-art')) hero.insertAdjacentHTML('afterbegin', '<div class="hero-art"></div>');
      lay(hero.querySelector(':scope > .hero-art'), 'low');
    });
    document.querySelectorAll('.ovs-mpos:not([data-last])').forEach(wander);
    document.querySelectorAll('.ovs-bat[data-v]').forEach((b) => {
      b.dataset.v.split(';').forEach((p) => { const [k, v] = p.split(':'); b.style.setProperty(`--${k}`, v); });
      b.removeAttribute('data-v');
    });
  };
  fill();
  document.addEventListener('rabbidlab:show', fill);
  // A window made wider or narrower shows more or less of the drawing: fitted again a moment after the resizing stops.
  let resized = 0;
  window.addEventListener('resize', () => { clearTimeout(resized); resized = setTimeout(fill, 200); });
  // The sky changed (sky.js: a pick in its window, a holiday starting or ending at midnight): painted again, and with
  // another holiday every banner is drawn again and the monsters laid on the new drawing (Santa at Christmas).
  // Snow where the lab is brings the yeti out in Sasquatch's place (monsters.js), so the monsters are laid again then too;
  // under a solid colour there are none, so they are taken away, and laid again when the drawing is back.
  const again = () => {
    const h = Sky?.holiday() ?? '';
    const now = Sky?.now();
    const [snowing, solid] = [now?.weather?.kind === 'snow', !!now?.solid];
    if (h !== holiday || snowing !== Scene.snowing || solid !== Scene.solid) {
      if (holiday) root.classList.remove(`ovs-${holiday}`);
      if (h) root.classList.add(`ovs-${h}`);
      [holiday, Scene.holiday, Scene.snowing, Scene.solid] = [h, h, snowing, solid];
      document.querySelectorAll('[data-monsters]').forEach((box) => { box.querySelector(':scope > .ovm-layer')?.remove(); delete box.dataset.monsters; });
      sweep();
    }
    paint();
    fill();
    // For what is laid on the drawings and follows the sky (the monsters, the weather), also when no drawing changed.
    document.dispatchEvent(new CustomEvent('rabbidlab:scene'));
  };
  window.addEventListener('rabbidlab:sky', again);
  window.addEventListener('rabbidlab:theme', again); // dark mode and light mode each have a sky of their own
  // The clock is looked at twice a minute, for the time of day and the chip; not while the tab is behind another, and at
  // once when it comes back in front.
  setInterval(() => { if (!document.hidden) again(); }, 30000);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) again(); });
})();
