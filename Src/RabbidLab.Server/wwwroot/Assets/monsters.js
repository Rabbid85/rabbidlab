// The monsters on Overview's banner ("Dev it", the monsters mock's take A). One runner is always on the far shore, hiding in
// the pines: Sasquatch, small and black like a shadow, or Santa at Christmas ("1 on the screen at all times... he spawns from
// the same tree he hides behind"). The pines he is behind rustle, shake when he bursts out, and he runs to another clump and
// hides again ("Move the tree, the monster hides behind. Like shake it like he's hiding."); nothing of him shows through
// them. Now and then Nessie's neck rises out of the lake in the moon's path, she swims across, and her head turns down into
// the water with her neck following it like a snake's ("The head needs to bend in the water like a snake turns"). Laid on a
// stage over the banner's drawing (scene.js, RabbidLabScene, which says how everything on a banner moves), in its 3200 by
// 600 units. Both are stepped on the engine's beat, 30 times a second while they move: the runner's every limb is a small
// drawing turned about its joint, and Nessie's own small drawing glides and fades with her neck drawn inside it, the one
// shape on the banner the page draws. Each pine that shakes is a small drawing the graphics card turns for that second.
(() => {
  const S = window.RabbidLabScene;
  if (!S) return;
  const f2 = (p) => `${p[0].toFixed(2)} ${p[1].toFixed(2)}`;
  const sm = (v) => { const c = Math.max(0, Math.min(1, v)); return c * c * (3 - 2 * c); };
  const lerp = (a, b, t) => a + (b - a) * t;

  // Each monster's options, kept in this browser as the sky's are, and set in Settings, Banner ("Nessie should be rare.",
  // "All monsters need frequency options.", then "there needs to be an options page for the banner. There we can show
  // monster times etc."): how often it comes, and at which times of day. A visit is Overview opening. odds: the chance of
  // coming on a visit, and first: how soon then. open: how long until it comes when the visit goes on without it. gap:
  // how long from one time to the next. idle: the pines rustle while he waits.
  const KEY = 'rabbidlab.monsters';
  const MIN = 60000;
  const TIMES = [['dawn', 'Dawn', 'sunrise'], ['day', 'Day', 'sun'], ['dusk', 'Dusk', 'sunset'], ['night', 'Night', 'moon']]; // and its icon
  const OFTEN = [['never', 'Never'], ['rarely', 'Rarely'], ['sometimes', 'Sometimes'], ['often', 'Often']];
  const RULES = {
    rarely: { odds: 0.1, first: [3000, 15000], open: [20 * MIN, 40 * MIN], gap: [20 * MIN, 40 * MIN] },
    sometimes: { odds: 0, open: [MIN, 3 * MIN], gap: [3 * MIN, 8 * MIN] },
    // As they were until 1.1.438: Nessie on the lake about two thirds of the time, the runner bolting every few seconds.
    often: { nessie: { odds: 1, first: [3000, 9000], gap: [6000, 20000] }, runner: { odds: 1, first: [4000, 11000], gap: [4000, 11000], idle: true } },
  };
  // Each one's name, what it is, and how often it comes until someone says otherwise: rarely ("This should be default.").
  const WHO = {
    nessie: ['Nessie', 'Swims in the lake.', 'rarely'],
    sasquatch: ['Sasquatch', 'Runs along the far shore.', 'rarely'],
    yeti: ['Yeti', 'Replaces Sasquatch when it is snowing.', 'rarely'],
    santa: ['Santa', 'Replaces Sasquatch at Christmas.', 'rarely'],
  };
  // Only what this file knows is taken from what was kept.
  const clean = (o) => Object.fromEntries(Object.entries(WHO).map(([k, [, , usual]]) => [k, {
    often: OFTEN.some(([v]) => v === o?.[k]?.often) ? o[k].often : usual,
    times: Array.isArray(o?.[k]?.times) ? TIMES.map(([v]) => v).filter((v) => o[k].times.includes(v)) : TIMES.map(([v]) => v),
  }]));
  let opts = clean(null);
  try {
    opts = clean(JSON.parse(localStorage.getItem(KEY) || 'null'));
  } catch {
    // As they usually are, this visit.
  }
  const keep = () => {
    try {
      localStorage.setItem(KEY, JSON.stringify(opts));
    } catch {
      // This visit only.
    }
    window.dispatchEvent(new Event('rabbidlab:monsters'));
  };
  const between = ([a, b]) => a + Math.random() * (b - a);
  // The rule a monster comes by, or null when it never does.
  const rule = (k) => {
    const o = opts[k];
    return o.often === 'never' || !o.times.length ? null : o.often === 'often' ? RULES.often[k === 'nessie' ? k : 'runner'] : RULES[o.often];
  };
  // How long into a visit it first comes: within the first seconds when the visit is one it comes on, or after its wait.
  const first = (r) => (Math.random() < r.odds ? between(r.first) : between(r.open));
  // Whether it comes at the time of day the banner shows now (scene.js keeps that on the page).
  const allowed = (k) => opts[k].times.includes(document.documentElement.dataset.sky || 'night');

  // The options as Settings, Banner shows them: a line a monster. Under its name what it is and how often it shows, always
  // one short sentence; at the right how often as a drop-down and the four times of day as buttons, their names as hints.
  const about = (k) => {
    const o = opts[k];
    const how = o.often === 'never' ? 'Turned off.' : !o.times.length ? 'No times of day selected.'
      : { rarely: 'Appears on about 1 in 10 visits.', sometimes: 'Appears every few minutes.', often: k === 'nessie' ? 'Appears on every visit.' : 'Appears every few seconds.' }[o.often];
    return `${WHO[k][1]} ${how}`;
  };
  const form = () => Object.entries(WHO).map(([k, [name]]) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about(k)}</span></div><div class="ds-r">`
    + `<select data-mon-often="${k}" aria-label="How often ${name} appears">${OFTEN.map(([v, l]) => `<option value="${v}"${opts[k].often === v ? ' selected' : ''}>${l}</option>`).join('')}</select>`
    + `<div class="mon-times" data-mon-times="${k}" role="group" aria-label="Times of day ${name} appears">${TIMES.map(([v, l, icon]) => `<button type="button" data-v="${v}" data-tip="${l}" aria-label="${l}" aria-pressed="${opts[k].times.includes(v)}"${opts[k].often === 'never' ? ' disabled' : ''}><svg><use href="#i-${icon}"/></svg></button>`).join('')}</div>`
    + '</div></div>').join('');
  // Drawn into Settings, Banner; a change is kept at once and the banner goes by it from then. reset: its Reset button.
  const mount = (el, reset) => {
    const draw = () => {
      // The control that was used keeps the keyboard's place when the lines are drawn again.
      const at = el.contains(document.activeElement) ? document.activeElement : null;
      const where = !at ? null : at.dataset.monOften ? `[data-mon-often="${at.dataset.monOften}"]` : `[data-mon-times="${at.parentElement.dataset.monTimes}"] > [data-v="${at.dataset.v}"]`;
      el.innerHTML = form();
      if (where) el.querySelector(where)?.focus();
    };
    el.addEventListener('change', (e) => {
      const often = e.target.dataset.monOften;
      if (!often) return;
      opts[often].often = e.target.value;
      keep();
      draw();
    });
    el.addEventListener('click', (e) => {
      const b = e.target.closest('.mon-times > button');
      if (!b) return;
      const k = b.parentElement.dataset.monTimes;
      const had = opts[k].times;
      opts[k].times = TIMES.map(([v]) => v).filter((v) => (v === b.dataset.v) !== had.includes(v));
      keep();
      draw();
    });
    reset?.addEventListener('click', () => { opts = clean(null); keep(); draw(); window.RabbidLab?.toast('Monster settings were reset to the defaults.'); });
    draw();
  };
  const page = document.getElementById('set-monsters');
  if (page) mount(page, document.getElementById('mon-reset'));

  // Nessie: a plesiosaur low in the water, a sliver of her back above the water line (y 0), her body and flippers faint
  // through the water under it.
  const BODY = '<path d="M6 0C6 -2.2 3 -3.4 -2 -3.6C-10 -4 -18 -3.2 -23 -1C-30 1.5 -38 3.5 -46 5.5C-38 6.2 -30 6.4 -23 6C-18 9 -8 10.5 0 9.2C4.5 8.2 6.5 4 6 0Z"/>'
    + '<g class="ovm-paddle"><path d="M1 5C-1 9 -4 13 -9 15.5C-6 12 -3 8.5 -2 5.5Z"/></g><g class="ovm-paddle ovm-b"><path d="M-17 6C-20 9 -24 12 -29 13.5C-26 10.5 -22 8 -20 5.5Z"/></g>';
  // Her size on the lake: 1.75 was "rather fucking large".
  const NS = 1.1;
  // Her neck, drawn each frame along a spine of SEG pieces from its foot, each turned from straight up toward where she
  // swims. Her shape is the user's cartoon ("Nesse look"): a thick neck rising a little back, then arching right over, her
  // head hanging at the front, spines down her back. The head stands out from the neck ("Head needs to be more pronounced",
  // with a dragon's head): a pinch at the throat, a skull wider than the neck, a long snout to a round nose, swept-back horns.
  const SEG = 30;
  const LONG = 50;
  const REST = (s) => (s < 0.42 ? -10 + 8 * (s / 0.42) : s < 0.8 ? -2 + 140 * ((s - 0.42) / 0.38) : 138 + 8 * ((s - 0.8) / 0.2));
  const THICK = (s) => (s < 0.74 ? 3.5 - 1.6 * (s / 0.74) : s < 0.84 ? 1.9 + 1 * sm((s - 0.74) / 0.1) : s < 0.95 ? 2.9 - 1.4 * ((s - 0.84) / 0.11) : 1.5 + 0.25 * Math.sin(((s - 0.95) / 0.05) * Math.PI));
  const UP = (s) => 2 + 6 * s; // coming up: straight, the head first
  const DOWN = (s) => 22 + 160 * s ** 1.25; // going down: curled over, the head into the water ahead of her
  const SWAY = (s, t) => 9 * s ** 1.3 * Math.sin((2 * Math.PI * t) / 4600 - 2.4 * s) + 6 * sm((s - 0.7) / 0.15) * Math.sin((2 * Math.PI * t) / 6600 + 0.8);
  // Where the arch starts: the top of her turns from here when she looks round.
  const TURN = 0.42;
  // Looking round, her head turns as an owl's does ("just turn the fucking head like an owl"): the neck stays, the arch and
  // head turn about their own upright. turn: 1 forward, 0 at us (three-quarter on), -1 back over her shoulder.
  const thetaOf = (h) => (h >= 0 ? (1 - h) * 1.08 : 1.08 + -h * (Math.PI - 1.08));
  const area = (q) => q.reduce((s, p, i) => { const n = q[(i + 1) % q.length]; return s + p[0] * n[1] - n[0] * p[1]; }, 0);
  const wound = (q) => `M${(area(q) < 0 ? q.reverse() : q).map(f2).join('L')}Z`;
  const ring = ([cx, cy], w) => `M${(cx - w).toFixed(2)} ${cy.toFixed(2)}A${w.toFixed(2)} ${w.toFixed(2)} 0 1 1 ${(cx + w).toFixed(2)} ${cy.toFixed(2)}A${w.toFixed(2)} ${w.toFixed(2)} 0 1 1 ${(cx - w).toFixed(2)} ${cy.toFixed(2)}Z`;
  const join = ([x1, y1], r1, [x2, y2], r2) => {
    const dd = Math.hypot(x2 - x1, y2 - y1);
    if (dd <= Math.abs(r1 - r2) + 0.001) return '';
    const [phi, al] = [Math.atan2(y2 - y1, x2 - x1), Math.acos((r1 - r2) / dd)];
    const at = (x, y, w, an) => [x + w * Math.cos(an), y + w * Math.sin(an)];
    return wound([at(x1, y1, r1, phi + al), at(x2, y2, r2, phi + al), at(x2, y2, r2, phi - al), at(x1, y1, r1, phi - al)]);
  };
  // u: how far she has come up; d: how far into her dive; t: the time, for the sway. o.turn: which way her head faces;
  // o.dip: how far her head is down in the water; o.theta: a turn given as the angle itself, below 0 turned away from us
  // toward the far shore. Going down, she dips her head first and her neck follows it in ("she
  // should dip her head to go down too").
  const neck = (u, d, t, o = {}) => {
    const P = [];
    let [x, y, prev] = [1.8, 0.6, null];
    for (let i = 0; i <= SEG; i++) {
      const s = i / SEG;
      const sway = SWAY(s, t);
      let a = lerp(UP(s) + sway * 0.4, REST(s) + sway, sm(u * 1.5 - s * 0.5));
      if (o.dip) a = lerp(a, DOWN(s), sm(o.dip * 1.8 - (1 - s) * 1.6));
      a = lerp(a, DOWN(s), sm(d * 2.6 - (1 - s) * 1.6));
      const r = (a * Math.PI) / 180;
      if (prev !== null) {
        const m = (prev + r) / 2;
        x += Math.sin(m) * (LONG / SEG);
        y -= Math.cos(m) * (LONG / SEG);
      }
      prev = r;
      P.push([x, y, r, THICK(s)]);
    }
    const th = o.theta ?? thetaOf(o.turn ?? 1);
    const [c, sn] = [Math.cos(th), Math.sin(th)];
    const x0 = P[Math.round(SEG * TURN)][0];
    const k = (i) => sm((i / SEG - TURN + 0.04) / 0.1);
    const turned = ([px, py], kk) => [x0 + (px - x0) * (1 - kk + kk * c), py];
    // The neck up to the arch as an outline; the arch and head as round slices along it, each joined to the next, so the
    // head keeps its shape however far it has turned ("her shape goes away when looking"). All wound the same way.
    const CUT = Math.round(SEG * TURN);
    const low = P.slice(0, CUT + 1);
    const pts = [...low.map(([px, py, r, w]) => [px - Math.cos(r) * w, py - Math.sin(r) * w]), ...low.map(([px, py, r, w]) => [px + Math.cos(r) * w, py + Math.sin(r) * w]).reverse()];
    const side = area(pts) < 0 ? pts.reverse() : pts;
    let path = `M${f2(side[0])}`;
    for (let i = 1; i < side.length - 1; i++) path += `Q${f2(side[i])} ${f2([(side[i][0] + side[i + 1][0]) / 2, (side[i][1] + side[i + 1][1]) / 2])}`;
    path += `L${f2(side[side.length - 1])}Z`;
    const Q = P.map((p, i) => turned(p, k(i)));
    for (let i = CUT - 2; i <= SEG; i++) {
      path += ring(Q[i], P[i][3]);
      if (i < SEG) path += join(Q[i], P[i][3], Q[i + 1], P[i + 1][3]);
    }
    // Spines down her back, on the outside of the arch, each leaning a little toward her head; then the horns at the back of
    // her head, swept back along her neck.
    const spike = (i, a, b) => {
      const [px, py, r, w] = P[i];
      const at = (o1, o2) => turned([px - Math.cos(r) * o1 + Math.sin(r) * o2, py - Math.sin(r) * o1 - Math.cos(r) * o2], k(i));
      return wound([at(w - 0.35, -0.55), at(w - 0.35, 0.55), at(w + a, b)]);
    };
    for (let i = Math.round(SEG * 0.22); i <= Math.round(SEG * 0.72); i += 2) path += spike(i, 1.05, 0.3);
    [0.8, 0.84, 0.88].forEach((s) => { path += spike(Math.round(SEG * s), 1.7, -1.7); });
    // Her eyes, one each side of her head up near the snout, a little above its line: each shows while it faces us, so side
    // on the near one, turned to us both, looking back the other, turned away to the far shore neither.
    const [ex, ey, er] = P[Math.round(SEG * 0.93)];
    const up = -Math.sin(er) <= 0 ? [-Math.cos(er), -Math.sin(er)] : [Math.cos(er), Math.sin(er)];
    const [X, Y] = [ex + up[0] * 0.42 - x0, ey + up[1] * 0.42];
    const eye = (z) => [x0 + X * c - z * sn, Y, sm((0.7 * sn + 0.71 * Math.sign(z) * c) / 0.3)];
    return { d: path, eyes: [eye(0.62), eye(-0.62)] };
  };
  const REST_NECK = neck(1, 0, 0);
  // Her eyes are small and pale, up by the snout where a reptile's are ("Small, by the snout", the eyes mock).
  const EYE = (op) => `<g class="ovm-eye" opacity="${op}"><circle r=".3"/></g>`;
  const NECK = (eyes) => `<path class="ovm-neck" d="${REST_NECK.d}"/>${eyes ? EYE(1) + EYE(0) : ''}`;
  // A drawing of her own, the size of what can be seen of her (240 by 160 of her units, the water line across its middle).
  // The graphics card glides it across and fades it in and out (overview.css), so only this small drawing is painted as her
  // neck moves, not the whole banner. ovm-face turns her to face the way she goes.
  const NESSIE = '<svg class="ovm ovm-nessie" viewBox="-120 -80 240 160" aria-hidden="true"><defs><clipPath id="ovm-above"><rect x="-120" y="-80" width="240" height="80.4"/></clipPath>'
    + '<clipPath id="ovm-below"><rect x="-120" y=".4" width="240" height="80"/></clipPath></defs><g class="ovm-face">'
    + `<g clip-path="url(#ovm-below)"><g class="ovm-rise" transform="translate(0 40)"><g class="ovm-under" filter="url(#ovs-b1)">${BODY}${NECK(false)}</g></g></g>`
    + `<g clip-path="url(#ovm-above)"><g class="ovm-rise" transform="translate(0 40)"><g class="ovm-dark">${BODY}${NECK(true)}</g></g></g></g></svg>`;
  // Her flippers turn about their top right corners, and how far under the water she is through her round: up in the first
  // tenth, bobbing, down in the last. Both are drawn with her neck.
  const PADDLE = [[1, 5], [-17, 5.5]];
  const RISE = [[0, 40], [0.1, 0], [0.3, -0.7], [0.55, 0.5], [0.89, 0], [1, 40]];

  // The runner: one body on joints, facing left with his feet on y 0, and the outfits it wears ("Basically the yet monster
  // just changes outfits/styles lol. Engine would be amazing."). A body gives each limb's pieces drawn from their joint at
  // (0 0) down, where the joints are (hip and shoulder: [near, far]; knee and elbow: how far down the thigh and upper arm),
  // and the torso and head. A piece is a path's d, or its own markup when it has colours of its own (Santa's suit). Every
  // outfit moves in its own gait (GAITS below), and which he wears follows the banner (outfit below).
  // Upright and broad ("Looks like a hunchback lol." of the one before, then a Bigfoot in silhouette, "More like this lol";
  // the shaggier of the two, "All these are my choices."): his head up on his shoulders with a crest, a brow, a nose and a
  // beard in profile, a deep chest and a straight back with long locks down it, long thick arms that reach his knees and end
  // in big hands with fingers, thick thighs with fur at the back, and big feet.
  const WOODS = {
    hip: [[-1.4, -13.5], [1.6, -13.5]], knee: 7, shoulder: [[-1.2, -28.2], [2.6, -28.8]], elbow: 8.6,
    thigh: 'M-2.6 -1.2L2.6 -1.2L3 1.4L4.2 2.6L2.8 3L2.6 4.8L3.8 6L2.2 6.4L1.9 7.2L-1.9 7.2L-2.3 3Z',
    shin: 'M-1.9 0L1.9 0L2.2 1.8L3.4 3L2 3.4L1.8 5L2.6 6.5L-4.6 6.5Q-5.3 5.6 -4.2 5.2L-1.7 4.6Z',
    upper: 'M-2.3 -1.4Q0 -2.6 2.3 -1.4L2.7 1.4L4 2.8L2.5 3.2L2.6 5.2L3.8 6.8L2.1 7.2L1.6 8.7L-1.6 8.7L-2.1 4Z',
    fore: 'M-1.6 0L1.6 0L2 2.2L3.4 3.6L1.9 4L1.6 6.6L2.2 8.4L1.7 10.6L1 10.8L.9 9.2L.5 11.2L-.2 11.3L-.3 9.4L-.8 11L-1.5 10.9L-1.4 9L-2 9.8L-2.5 9.4L-1.9 7.4L-1.8 3.6Z',
    body: '<path d="M-3.6 -12.6C-4.8 -16 -5.6 -20 -5.4 -24C-5.3 -26 -5 -27.6 -4.2 -29L-3.6 -30.4C-4.8 -30.6 -5.4 -31.8 -4.6 -32.6L-5.3 -33.6L-4.5 -34.6L-4.9 -35.6'
      + 'C-4.6 -37.6 -2.6 -39.2 -.8 -39.4L-.2 -40.6L.6 -39.6L1.6 -40.4L2 -39C2.8 -37.6 3 -36 2.8 -34.6L4.2 -34L3.2 -32.6C5 -32.2 6.6 -30.8 7 -28.8L8.8 -28.4L7.5 -26.6L9 -25.4L7.6 -24.2'
      + 'C7.7 -22.6 7.4 -21 7 -19.6L8.6 -18.6L6.8 -17.6L7.8 -16L6 -15.4C5.6 -14.2 5 -13.2 4.4 -12.4L3.6 -10.6L2.4 -12L1.4 -10.4L0 -12L-1.2 -10.6L-2.4 -12.2Z"/>',
  };
  // Santa on the same joints: red trousers and black boots, red sleeves with white cuffs and black mittens; his round suit
  // with its white hem, black belt and gold buckle, his beard, his hat flying back, his sack high on his back.
  const RED = '#c8202a';
  const SANTA = {
    hip: [[-1, -10.8], [1, -10.8]], knee: 5.4, shoulder: [[-3.2, -24.6], [1.2, -24.6]], elbow: 6.2,
    thigh: `<path d="M-1.8 -.9L1.8 -.9L1.4 5.6L-1.4 5.6Z" fill="${RED}"/>`,
    shin: `<path d="M-1.4 0L1.4 0L1.2 3.2L-1.2 3.2Z" fill="${RED}"/><path d="M-1.5 3L1.5 3L1.3 5L-3.2 5.4L-3 4.2L-1.3 4.2Z" fill="#1a1018"/>`,
    upper: `<path d="M-1.4 -.6L1.4 -.6L1.1 6.4L-1.1 6.4Z" fill="${RED}"/>`,
    fore: `<path d="M-1.1 0L1.1 0L.9 4.6L-.9 4.6Z" fill="${RED}"/><path d="M-1.2 4.4H1.2V5.6H-1.2Z" fill="#f4f0ea"/><circle cy="6.6" r="1.2" fill="#1a1018"/>`,
    body: '<g transform="translate(0 -3.4)"><ellipse cx="5" cy="-21.8" rx="3.5" ry="4.1" fill="#8a6640"/><path d="M3.4 -25.4Q5 -26.9 6.6 -25.6" fill="none" stroke="#5a4028" stroke-width=".8"/>'
      + `<path d="M-7 -21C-10 -16 -10 -11 -7.4 -7.4L5 -7.4C7 -12 7 -18 4 -22C1 -24.5 -4 -24.5 -7 -21Z" fill="${RED}"/>`
      + '<path d="M-8.4 -9H6.2V-6.8H-7.8Z" fill="#f4f0ea"/><path d="M-9.4 -13.8H6.6V-11.6H-9.4Z" fill="#1a1018"/><rect x="-3.2" y="-14.1" width="2.8" height="2.8" fill="none" stroke="#e8c050" stroke-width=".7"/>'
      + '<circle cx="-3" cy="-25.6" r="3" fill="#f0c9a8"/><path d="M-6.6 -25.4C-7.6 -21.4 -5.4 -18.6 -2.6 -19C0 -19.4 .6 -22.4 .2 -24.6C-1.6 -23.4 -4.6 -23.6 -6.6 -25.4Z" fill="#f8f6f2"/>'
      + `<path d="M-6.4 -27.2C-5.6 -31.4 -1 -32.4 2.4 -30.2L6.6 -26.6L4.7 -25.9L1 -27.8Z" fill="${RED}"/>`
      + '<path d="M-6.9 -27.4Q-3 -28.8 1.4 -27.3" fill="none" stroke="#f8f6f2" stroke-width="1.7" stroke-linecap="round"/><circle cx="6.4" cy="-26.3" r="1.35" fill="#f8f6f2"/></g>',
  };
  // The gaits. t: how long a stride is, in seconds; lean: how far he leans in, in degrees; and for each joint how far it
  // is turned through a stride, [part of the stride, degrees], eased from each to the next. The body stays level ("He just
  // fucking bvounces when running?") and the elbows straighten as the arms come forward, so a hand never reaches the face
  // ("Arm in his mouth lol").
  // A real stride ("All these are picked now.", Sasquatch and the yeti): the heel lands with the leg out in front, the knee
  // takes the weight, the leg passes under him and pushes off, the shin folds up as it swings through; the arms pump
  // against the legs, bent at the elbow.
  // The jolly jog: short quick steps, each foot landing under him and springing off, the knee folding as it swings through.
  const GAITS = {
    stride: { t: 0.5, lean: -10, thigh: [[0, 36], [0.18, 20], [0.4, -22], [0.52, -34], [0.66, -12], [0.84, 30], [1, 36]],
      shin: [[0, -6], [0.18, -22], [0.4, -14], [0.52, -38], [0.66, -110], [0.84, -44], [1, -6]], upper: [[0, 34], [0.5, -40], [1, 34]], fore: [[0, 44], [0.5, 72], [1, 44]] },
    jog: { t: 0.46, lean: -4, thigh: [[0, 30], [0.2, 14], [0.45, -24], [0.55, -20], [0.7, 4], [0.85, 26], [1, 30]],
      shin: [[0, -10], [0.2, -24], [0.45, -20], [0.55, -50], [0.7, -95], [0.85, -40], [1, -10]], upper: [[0, 26], [0.5, -30], [1, 26]], fore: [[0, 60], [0.5, 92], [1, 60]] },
  };
  // A value through a list of [part, value], eased from each to the next.
  const along = (keys, p) => {
    const i = Math.max(0, keys.findIndex(([v]) => v > p) - 1);
    const [[a, ha], [b, hb]] = [keys[i], keys[i + 1] || keys[i]];
    return b === a ? ha : lerp(ha, hb, sm((p - a) / (b - a)));
  };
  // Each outfit's own gait, and how fast it covers ground against 260 units in 8 seconds: Sasquatch and the yeti stride,
  // Santa jogs (the walk items in the mock).
  const OUTFITS = {
    sasquatch: { body: WOODS, cls: 'ovm-fur', gait: 'stride', pace: 1 }, // dark, the colour of the shore's pines a shade darker
    yeti: { body: WOODS, cls: 'ovm-snowfur', gait: 'stride', pace: 1 }, // the same in white, while it snows where the lab is
    santa: { body: SANTA, cls: '', gait: 'jog', pace: 0.8 },
  };
  // Which outfit: Santa at Christmas; the yeti while it snows where the lab is; Sasquatch otherwise.
  const outfit = () => (S.holiday === 'christmas' ? 'santa' : S.snowing ? 'yeti' : 'sasquatch');
  // The far arm and leg behind, the near leg, the torso and head, the near arm in front. Every piece is a small drawing of
  // its own with its joint at its middle, in a box placed at the joint (its --x and --y) that turns about it, so a step
  // of his run turns boxes and draws nothing again.
  const part = (p) => (p.startsWith('<') ? p : `<path d="${p}"/>`);
  const piece = (p, cls) => `<svg class="ovm-piece ${cls}" viewBox="-12 -12 24 24" aria-hidden="true">${part(p)}</svg>`;
  const build = (m, cls) => {
    const limb = (name, at, upper, upperCls, down, lower, lowerCls) => `<div class="ovm-joint ${name}" data-vars="x:${at[0]};y:${at[1]}"><div class="ovm-turn ${upperCls}">${piece(upper, cls)}`
      + `<div class="ovm-joint" data-vars="x:0;y:${down}"><div class="ovm-turn ${lowerCls}">${piece(lower, cls)}</div></div></div></div>`;
    const leg = (i) => limb(`ovm-leg${i ? ' ovm-b' : ''}`, m.hip[i], m.thigh, 'ovm-thigh', m.knee, m.shin, 'ovm-shin');
    const arm = (i) => limb(`ovm-arm${i ? ' ovm-b' : ''}`, m.shoulder[i], m.upper, 'ovm-upper', m.elbow, m.fore, 'ovm-fore');
    return `${arm(1)}${leg(1)}${leg(0)}<svg class="ovm-piece ovm-torso ${cls}" viewBox="-20 -44 40 48" aria-hidden="true">${m.body}</svg>${arm(0)}`;
  };
  const RUNNER = (name) => `<div class="ovm-at"><div class="ovm ovm-runner"><div class="ovm-walk"><div class="ovm-bob">${build(OUTFITS[name].body, OUTFITS[name].cls)}</div></div></div></div>`;

  // Where he hides: four clumps of three to five pines of different heights on the far shore (base 436), the tallest near
  // the middle so he is hidden at its foot. Each pine sways from its foot when its clump rustles; each clump is mirrored in
  // the lake under it; at Christmas they wear the same lights as the big pines. [x, height] for each pine. A pine is a
  // sprite with its foot at the bottom middle, and a clump is there twice: its pines, and the same again blurred for
  // their mirror image (overview.css turns that one over about the water line).
  const CLUMPS = [2440, 2635, 2830, 3025];
  const corners = (x, h) => S.corners(x, 436, h);
  const GROVES = (() => {
    const r = S.rnd(29);
    return CLUMPS.map((x) => {
      const n = 3 + Math.floor(r() * 3);
      const list = [[x + (r() - 0.5) * 5, 38 + r() * 10]];
      for (let k = 1; k < n; k++) list.push([x + (k % 2 ? -1 : 1) * (5 + r() * 11), 21 + r() * 18]);
      return list.sort((a, b) => a[0] - b[0]);
    });
  })();
  const pines = (g, soft) => g.map(([px, h]) => S.sprite('ovm-sway', [px - h * 0.34 - 2, 434 - h, h * 0.68 + 4, h + 2],
    `<g${soft ? ' filter="url(#ovs-b1)"' : ''}><path d="${S.pine(px, 436, h)}"/><g class="ovm-lit">${S.lights(px, 436, h)}</g></g>`)).join('');
  const CLUMP = GROVES.map((g, i) => `<div class="ovm-clump" data-clump="${i}">${pines(g, false)}</div><div class="ovm-clump ovm-refl" data-clump="${i}">${pines(g, true)}</div>`).join('');
  // What hides him: the outline round each clump's pines, gaps and all, a little wider, and the big pines in the corners.
  const around = (pts) => {
    const p = pts.slice().sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    const turn = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    const half = (list) => list.reduce((h, q) => { while (h.length >= 2 && turn(h[h.length - 2], h[h.length - 1], q) <= 0) h.pop(); h.push(q); return h; }, []);
    return half(p).slice(0, -1).concat(half(p.slice().reverse()).slice(0, -1));
  };
  const hull = (g) => {
    const all = g.flatMap(([x, h]) => corners(x, h));
    const xs = all.map(([a]) => a);
    const pts = around(all.concat([[Math.min(...xs), 441], [Math.max(...xs), 441]]));
    const cx = pts.reduce((s, [a]) => s + a, 0) / pts.length;
    const cy = pts.reduce((s, [, b]) => s + b, 0) / pts.length;
    return `M${pts.map(([a, b]) => { const d = Math.hypot(a - cx, b - cy) || 1; return `${(a + ((a - cx) / d) * 1.5).toFixed(1)} ${(b + ((b - cy) / d) * 1.5).toFixed(1)}`; }).join('L')}Z`;
  };
  // The strip of far shore he crosses, [x, y, width, height] in the drawing's units. His pieces are layers, so what hides
  // him is a still picture laid over the strip as its mask: clear where the clumps and the big front pines are.
  // In wind the big pines lean, in a drawing in front of him (weather.js, data-gale), so they hide him themselves.
  const BAND = [2400, 392, 670, 50];
  const hideMask = (windy) => {
    const [x, y, w, h] = BAND;
    const box = `x="${x}" y="${y}" width="${w}" height="${h}"`;
    const near = windy ? '' : (S.near.tall || []).filter(([px]) => px > x - 70 && px < x + w + 70).map(([px, py, ph]) => S.pine(px, py, ph)).join('');
    return `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="${x} ${y} ${w} ${h}" preserveAspectRatio="none">`
      + `<mask id="m" maskUnits="userSpaceOnUse" ${box}><rect ${box} fill="#fff"/><path d="${GROVES.map(hull).join('')}${near}"/></mask><rect ${box} mask="url(#m)"/></svg>`)}")`;
  };
  const LEN = 24000; // Nessie's round (overview.css)

  const lay = (box) => {
    box.dataset.monsters = '';
    // A stage laid exactly as the drawing is, and on it: the strip the runner crosses, the clumps' pines, and Nessie's own
    // small drawing.
    box.insertAdjacentHTML('beforeend', `<div class="ovm-layer"><div class="ovm-stage"><div class="ovm-band">${RUNNER(outfit())}</div>`
      + `<div class="ovm-clumps">${CLUMP}</div>${NESSIE}</div></div>`);
    const layer = box.querySelector(':scope > .ovm-layer');
    // Their timers and listeners end with the layer, and they rest while the banner can't be seen (scene.js, life).
    const { later, on, showing, rest } = S.life(layer);
    const q = (sel) => layer.querySelector(sel);
    S.vars(layer);
    // The big pines move with the window's width (scene.js draws the banner again), so what hides him is made again then,
    // and when the wind starts or stops.
    const hide = () => { const m = hideMask(box.hasAttribute('data-gale')); ['mask-image', '-webkit-mask-image'].forEach((k) => q('.ovm-band').style.setProperty(k, m)); };
    hide();
    on(box, 'rabbidlab:scene', hide);
    on(box, 'rabbidlab:weather', hide);
    if (S.still()) return;

    // The runner: always behind one clump or running to another.
    const runner = q('.ovm-runner');
    const { pace, gait: way } = OUTFITS[outfit()];
    const gait = GAITS[way];
    const walk = runner.querySelector('.ovm-walk');
    const bob = runner.querySelector('.ovm-bob');
    // Each joint with how it turns through a stride; the far leg and the near arm run half a stride behind.
    const joints = ['thigh', 'shin', 'upper', 'fore'].flatMap((part) => [...runner.querySelectorAll(`.ovm-${part}`)]
      .map((el) => [el, gait[part], el.closest('.ovm-leg.ovm-b, .ovm-arm:not(.ovm-b)') ? 0.5 : 0]));
    let spot = Math.floor(Math.random() * CLUMPS.length);
    let dash = null; // while he runs: from where to where across the drawing, how far in pixels, since when, for how long
    // A step of his run, on the beat: how far he has come, and where in a stride each joint is.
    const stride = (now) => {
      if (!dash) return;
      const ms = now - dash.at;
      walk.style.transform = `translateX(${(dash.far * Math.min(1, ms / dash.ms)).toFixed(1)}px)`;
      joints.forEach(([el, keys, behind]) => { el.style.transform = `rotate(${along(keys, (ms / (gait.t * 1000) + behind) % 1).toFixed(1)}deg)`; });
    };
    // Standing still and upright where his run ended, so nothing of him leans out of the pines.
    const stand = () => {
      if (dash) walk.style.transform = `translateX(${dash.far.toFixed(1)}px)`;
      dash = null;
      bob.style.transform = '';
      joints.forEach(([el]) => { el.style.transform = ''; });
    };
    // Where his feet are across the drawing, and three quarters size, turned to face the way he goes.
    const place = (x, left) => { runner.parentNode.style.setProperty('--x', x); runner.parentNode.style.setProperty('--f', left ? 0.75 : -0.75); };
    // A clump's pines and their mirror image shake together.
    const shake = (i, hard) => layer.querySelectorAll(`[data-clump="${i}"]`).forEach((c) => {
      c.classList.remove('ovm-shake', 'ovm-hard');
      void c.getBoundingClientRect();
      c.classList.add('ovm-shake');
      c.classList.toggle('ovm-hard', hard);
    });
    place(CLUMPS[spot], true);
    runner.classList.add('ovm-here');
    const run = () => {
      let to = spot;
      while (to === spot) to = Math.floor(Math.random() * CLUMPS.length);
      // A slow gait only goes to the next clump along; a stride or a jog can cross several.
      if (pace < 0.7) to = spot === 0 ? 1 : spot === CLUMPS.length - 1 ? spot - 1 : spot + (Math.random() < 0.5 ? -1 : 1);
      shake(spot, true); // he pushes out through the branches
      later(() => {
        place(CLUMPS[spot], CLUMPS[to] < CLUMPS[spot]);
        const dist = Math.abs(CLUMPS[to] - CLUMPS[spot]) / 0.75;
        const ms = ((dist / 260) * 8000) / pace; // "Why is sasquatch running 900 miels an hour": about 260 units in 8 s, at his outfit's pace
        walk.style.transform = '';
        bob.style.transform = `rotate(${gait.lean}deg)`;
        later(() => shake(to, true), ms - 700); // and into the next one
        dash = { from: CLUMPS[spot], to: CLUMPS[to], far: (-dist * q('.ovm-stage').getBoundingClientRect().width) / 3200, at: performance.now(), ms };
        later(() => { spot = to; stand(); wait(false); }, ms);
      }, 520);
    };
    // One wait at a time for each thing waited for: a new visit, or a change of the options, starts it over.
    const waits = {};
    const hold = (k, fn, ms) => { clearTimeout(waits[k]); waits[k] = fn && later(fn, ms); };
    // Waiting behind the pines until he next bolts, by his options (monsters' options, above); opening: Overview has just
    // opened. Often, the pines rustle once or twice while he waits; otherwise they hold still until just before he goes.
    // With nobody to see him he stays put, and the banner coming back is a new visit.
    const who = outfit();
    const wait = (opening) => {
      const r = rule(who);
      ['runner', 'rustle', 'rustle2'].forEach((k) => hold(k));
      if (!r) return;
      const long = opening ? first(r) : between(r.gap);
      const rustle = () => { if (showing() && allowed(who)) shake(spot, false); };
      if (r.idle) {
        hold('rustle', rustle, long * (0.25 + Math.random() * 0.2));
        if (Math.random() < 0.5) hold('rustle2', rustle, long * 0.7);
      } else hold('rustle', rustle, Math.max(0, long - 1800));
      hold('runner', () => (!showing() ? rest(() => wait(true)) : allowed(who) ? run() : wait(false)), long);
    };
    wait(true);

    // Nessie, now and then: somewhere new on the lake in the moon's part of it each time, going one way or the other. She
    // glides ("She also just needs to glide, not row"), and on the way looks at us and back over her shoulder, and dips her
    // head into the water twice ("Rowing, looking, dipping", less the rowing). Her round is 24 seconds, stepped on the
    // engine's beat while she's out: where her drawing is and how much of it shows, and inside it her neck, her rise out
    // of the water and her flippers. Each step is under a pixel.
    const ness = q('.ovm-nessie');
    const face = ness.querySelector('.ovm-face');
    const rises = ness.querySelectorAll('.ovm-rise');
    const paddles = [...ness.querySelectorAll('.ovm-paddle')];
    const necks = ness.querySelectorAll('.ovm-neck');
    const eyes = ness.querySelectorAll('.ovm-eye');
    const span = (p, a, b, c, e) => (p < a || p > e ? 0 : p < b ? sm((p - a) / (b - a)) : p <= c ? 1 : 1 - sm((p - c) / (e - c)));
    // Which way her head faces through her round: [part of her round, 1 forward / 0 at us / -1 back], eased between.
    const LOOK = [[0, 1], [0.3, 1], [0.35, 0], [0.4, 0], [0.46, -1], [0.52, -1], [0.57, 0], [0.62, 1], [1, 1]];
    // When the runner bolts while she is up, she stops swimming and watches him until he is in the pines again ("She stops
    // to watch", the Nessie sees the monster mock): her head turns away from us toward him on the far shore and follows
    // him, forward when he is ahead of her, back over her shoulder when he is behind. While she is stopped her neck sways
    // slower ("floating around is a little too fast when stopped"). seen eases her in and out of watching.
    // at: how far into her round she is, in ms; out: whether she is up at all.
    let [nessAt, nessDir, seen, aim, stopped, swayT, paddleT, last, faced, at, out] = [2480, 1, 0, 0, false, 0, 0, null, 1, 0, false];
    // When she next comes up, by her options; opening: Overview has just opened.
    const due = (opening) => { const r = rule('nessie'); hold('nessie', r && swim, r ? (opening ? first(r) : between(r.gap)) : 0); };
    const frame = (now, box, n) => {
      if (!out) return;
      const p = at / LEN;
      const [u, d] = [Math.max(0, Math.min(1, p / 0.12)), Math.max(0, Math.min(1, (p - 0.8) / 0.13))];
      const turn = along(LOOK, p);
      const dip = span(p, 0.14, 0.165, 0.185, 0.21) + span(p, 0.68, 0.705, 0.725, 0.75);
      // Drawn 30 times a second while she comes up, goes down, turns her head, dips it or watches the runner; 15 while she
      // only glides and sways, when a step moves no part of her a pixel.
      if (n % 2 && u === 1 && d === 0 && dip === 0 && turn === faced && seen <= 0.01) return;
      faced = turn;
      const dt = Math.min(100, now - (last ?? now));
      last = now;
      const head = nessAt + nessDir * NS * (80 * p + 8);
      const rx = dash ? lerp(dash.from, dash.to, Math.min(1, Math.max(0, (now - dash.at) / dash.ms))) : null;
      if (rx !== null) aim = -Math.atan2(260, (rx - head) * nessDir);
      seen = lerp(seen, rx !== null && u > 0.9 && d === 0 ? 1 : 0, 1 - 0.93 ** (dt / 16.7));
      // While she watches she stays where she is: her round waits and her flippers hold still.
      stopped = seen > 0.5;
      swayT += dt * lerp(1, 0.35, seen);
      if (!stopped) { paddleT += dt; at += dt; }
      // Down again: nothing of hers shows or is drawn until she next comes up.
      if (at >= LEN) { out = false; ness.style.opacity = '0'; due(false); return; }
      // Her drawing fades in over the first 6% of her round and out over the last, and glides 88 units the way she faces.
      ness.style.opacity = Math.min(1, p / 0.06, (1 - p) / 0.06).toFixed(3);
      ness.style.transform = `translateX(${(nessDir * 88 * p * Math.max(box.width / 3200, box.height / 600)).toFixed(2)}px)`;
      const drawn = neck(u, d, swayT, { turn, theta: lerp(thetaOf(turn), aim, seen), dip });
      necks.forEach((g) => g.setAttribute('d', drawn.d));
      eyes.forEach((g, i) => {
        const [ex, ey, op] = drawn.eyes[i];
        g.setAttribute('transform', `translate(${ex.toFixed(2)} ${ey.toFixed(2)})`);
        g.setAttribute('opacity', op.toFixed(2));
      });
      const up = along(RISE, p).toFixed(2);
      rises.forEach((g) => g.setAttribute('transform', `translate(0 ${up})`));
      paddles.forEach((g) => {
        const b = g.classList.contains('ovm-b');
        g.setAttribute('transform', `rotate(${(1 + (b ? 13 : -13) * Math.cos((Math.PI * paddleT) / 1400)).toFixed(1)} ${PADDLE[b ? 1 : 0].join(' ')})`);
      });
    };
    // A round, and the next some time after it ends (a stop to watch makes it longer). None begins while the banner can't
    // be seen (its coming back is a new visit) or at a time of day she doesn't come at, and one under way waits where it
    // is until the banner is back.
    const swim = () => {
      if (!showing()) { rest(() => due(true)); return; }
      if (!allowed('nessie')) { due(false); return; }
      const left = Math.random() < 0.5;
      [nessAt, nessDir, seen, stopped, last, at, out] = [2300 + Math.random() * 600, left ? -1 : 1, 0, false, null, 0, true];
      ness.style.setProperty('--x', nessAt.toFixed(0)); // where she comes up across the drawing
      face.setAttribute('transform', left ? 'scale(-1 1)' : '');
    };
    S.beat(layer, (now, box, n) => { stride(now); frame(now, box, n); }, 1);
    due(true);
    // Overview opened again, or the options changed: a new visit for whoever isn't out.
    const visit = () => { if (!dash) wait(true); if (!out) due(true); };
    let was = showing();
    on(document, 'rabbidlab:show', () => { const now = showing(); if (now && !was) visit(); was = now; });
    on(window, 'rabbidlab:monsters', visit);
  };
  // None under a solid colour: nothing moves there (scene.js takes them away).
  const fill = () => { if (!S.solid) document.querySelectorAll('[data-scene][data-fit]:not([data-monsters])').forEach(lay); };
  fill();
  document.addEventListener('rabbidlab:show', fill);
  document.addEventListener('rabbidlab:scene', fill);
})();
