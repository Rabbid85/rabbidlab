// The monsters on Overview's banner ("Dev it", the monsters mock's take A). One runner is always on the far shore, hiding in
// the pines: Sasquatch, small and black like a shadow, or Santa at Christmas ("1 on the screen at all times... he spawns from
// the same tree he hides behind"). The pines he is behind rustle, shake when he bursts out, and he runs to another clump and
// hides again ("Move the tree, the monster hides behind. Like shake it like he's hiding."); nothing of him shows through
// them. Now and then Nessie's neck rises out of the lake in the moon's path, she swims across, and her head turns down into
// the water with her neck following it like a snake's ("The head needs to bend in the water like a snake turns"). Laid on a
// stage over the banner's drawing (scene.js, RabbidLabScene), in its 3200 by 600 units. Everything that crosses the banner
// is a layer the graphics card moves, so it is as smooth as the screen and costs the page nothing: the runner's every limb,
// and Nessie's own small drawing; only her neck's shape is drawn by the page, 30 times a second while she is up.
(() => {
  const S = window.RabbidLabScene;
  if (!S) return;
  const f2 = (p) => `${p[0].toFixed(2)} ${p[1].toFixed(2)}`;
  const sm = (v) => { const c = Math.max(0, Math.min(1, v)); return c * c * (3 - 2 * c); };
  const lerp = (a, b, t) => a + (b - a) * t;
  const still = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

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
  // outfit moves the same way, a long lope (overview.css), and which he wears follows the banner (outfit below).
  // Shaggy ("This one."): tall, broad and hunched, a peaked head low on his shoulders with a heavy brow, in long fur that
  // hangs and trails behind him as he moves, a mane down his back, a fringe at his hips, locks off his arms and legs.
  const SHAGGY = {
    hip: [[-1.6, -13], [1.6, -13]], knee: 6.4, shoulder: [[-3.4, -28.4], [2.4, -29.2]], elbow: 7.4,
    thigh: 'M-2.2 -1L2.2 -1Q3.4 .8 3 2.8Q2.4 2.3 1.9 2.4Q3 4 2.5 5.6Q2 5.2 1.6 5.2L1.6 6.6L-1.6 6.6Z',
    shin: 'M-1.6 0L1.6 0Q2.8 1.6 2.4 3.6Q1.9 3.2 1.5 3.3Q2.6 4.6 2 5.6L1.3 5.6L-3.8 6.5L-3.6 5.2L-1.3 4.9Q-2.2 4.4 -2.4 3.6Q-1.8 3.8 -1.5 3.5Z',
    upper: 'M-1.8 -.8L1.8 -.8Q3.2 .6 2.9 2.6Q2.3 2.2 1.7 2.2Q3 3.6 2.6 5.4Q2 5 1.5 5L1.3 7.6L-1.3 7.6Z',
    fore: 'M-1.3 0L1.3 0Q2.8 1.2 2.6 3Q2 2.6 1.6 2.6Q3 3.8 2.6 5.6Q2 5 1.4 5Q2.2 6.4 1.1 6.8L-.2 8.4L-1.6 7.2Z',
    body: '<path d="M-4.2 -12.5C-6 -17 -7.6 -23 -6.4 -27.6C-5 -31 0 -32.4 4.4 -30.8Q6.2 -30.2 7.6 -28.2Q6.6 -29 6.2 -29.2Q8 -28.6 8.4 -25.5Q7.4 -26.2 7 -26.5'
      + 'Q8.8 -25.6 8.7 -22Q7.8 -22.6 7.3 -23Q9 -21.8 8.5 -18.5Q7.6 -19.1 7.1 -19.5Q8.6 -18 7.8 -15Q6.9 -15.6 6.4 -16Q7.6 -14.4 6.2 -11.4Q5.5 -12 5 -12.5'
      + 'Q4 -10.6 3 -11.6Q2 -10.4 1 -11.6Q0 -10.4 -1 -11.6Q-2 -10.6 -3 -11.8Q-3.8 -11 -4.2 -12.5Z"/>'
      + '<path d="M-7.2 -28.6C-8.6 -31.6 -7 -36 -3.8 -37.2C-1.8 -37.8 .2 -35.8 -.2 -33C-.6 -30.6 -3.2 -29 -7.2 -28.6Z"/><path d="M-8.6 -31.4L-6.2 -32.6L-6.6 -30.6Z"/>'
      + '<path d="M-3.8 -37.2Q-.6 -38.2 1.4 -35.4Q.6 -35.6 .2 -35.2Q2 -33.6 2.2 -31Q1.2 -31.8 .4 -31.6Q1.4 -30.4 1 -28.4Q-.2 -29.6 -1.2 -30.2Z"/>',
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
  // Each outfit's own way of moving (overview.css .ovm-g-*), and how fast it covers ground against 260 units in 8 seconds:
  // Sasquatch lopes, the yeti walks, Santa jogs (the walk items in the mock, "send to dev").
  const OUTFITS = {
    sasquatch: { body: SHAGGY, cls: 'ovm-fur', gait: 'lope', pace: 1.1 }, // dark, the colour of the shore's pines a shade darker
    yeti: { body: SHAGGY, cls: 'ovm-snowfur', gait: 'walk', pace: 0.5 }, // the same in white, for when the banner shows the weather
    santa: { body: SANTA, cls: '', gait: 'jog', pace: 0.8 },
  };
  // Which outfit: Santa at Christmas; the yeti while it snows where the lab is; Sasquatch otherwise.
  const outfit = () => (S.holiday === 'christmas' ? 'santa' : S.snowing ? 'yeti' : 'sasquatch');
  // The far arm and leg behind, the near leg, the torso and head, the near arm in front. Every piece is a small drawing of
  // its own with its joint at its middle, in a box placed at the joint (data-at, set as --x and --y once laid, since the
  // page allows no style attributes), so the graphics card turns each about its joint (overview.css): his limbs swing, he
  // bobs and he crosses the shore without the page doing any work.
  const part = (p) => (p.startsWith('<') ? p : `<path d="${p}"/>`);
  const piece = (p, cls) => `<svg class="ovm-piece ${cls}" viewBox="-12 -12 24 24" aria-hidden="true">${part(p)}</svg>`;
  const build = (m, cls) => {
    const limb = (name, at, upper, upperCls, down, lower, lowerCls) => `<div class="ovm-joint ${name}" data-at="${at.join(' ')}"><div class="ovm-turn ${upperCls}">${piece(upper, cls)}`
      + `<div class="ovm-joint" data-at="0 ${down}"><div class="ovm-turn ${lowerCls}">${piece(lower, cls)}</div></div></div></div>`;
    const leg = (i) => limb(`ovm-leg${i ? ' ovm-b' : ''}`, m.hip[i], m.thigh, 'ovm-thigh', m.knee, m.shin, 'ovm-shin');
    const arm = (i) => limb(`ovm-arm${i ? ' ovm-b' : ''}`, m.shoulder[i], m.upper, 'ovm-upper', m.elbow, m.fore, 'ovm-fore');
    return `${arm(1)}${leg(1)}${leg(0)}<svg class="ovm-piece ovm-torso ${cls}" viewBox="-20 -44 40 48" aria-hidden="true">${m.body}</svg>${arm(0)}`;
  };
  const RUNNER = (name) => `<div class="ovm-at"><div class="ovm ovm-runner ovm-g-${OUTFITS[name].gait} ovm-hiding"><div class="ovm-walk"><div class="ovm-bob">${build(OUTFITS[name].body, OUTFITS[name].cls)}</div></div></div></div>`;

  // Where he hides: four clumps of three to five pines of different heights on the far shore (base 436), the tallest near
  // the middle so he is hidden at its foot. Each pine sways from its foot when its clump rustles; each clump is mirrored in
  // the lake under it; at Christmas they wear the same lights as the big pines. [x, height] for each pine.
  const CLUMPS = [2440, 2635, 2830, 3025];
  const SHAPE = [[0, -1], [0.5, -0.58], [0.26, -0.58], [0.78, -0.24], [0.42, -0.24], [1, 0], [-1, 0], [-0.42, -0.24], [-0.78, -0.24], [-0.26, -0.58], [-0.5, -0.58]];
  const corners = (x, h) => SHAPE.map(([a, b]) => [x + a * h * 0.34, 436 + b * h]);
  const GROVES = (() => {
    const r = S.rnd(29);
    return CLUMPS.map((x) => {
      const n = 3 + Math.floor(r() * 3);
      const list = [[x + (r() - 0.5) * 5, 38 + r() * 10]];
      for (let k = 1; k < n; k++) list.push([x + (k % 2 ? -1 : 1) * (5 + r() * 11), 21 + r() * 18]);
      return list.sort((a, b) => a[0] - b[0]);
    });
  })();
  const CLUMP = GROVES.map((g, i) => `<g class="ovm-clump" id="ovm-cl-${i}" data-clump="${i}">${g.map(([px, h]) => `<g class="ovm-sway"><path d="${S.pine(px, 436, h)}"/>`
    + `<g class="ovm-lit">${S.lights(px, 436, h)}</g></g>`).join('')}</g><use href="#ovm-cl-${i}" class="ovm-refl" transform="translate(0 872) scale(1 -1)"/>`).join('');
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
  const BAND = [2400, 392, 670, 50];
  const hideMask = () => {
    const [x, y, w, h] = BAND;
    const box = `x="${x}" y="${y}" width="${w}" height="${h}"`;
    const near = (S.near.tall || []).filter(([px]) => px > x - 70 && px < x + w + 70).map(([px, py, ph]) => S.pine(px, py, ph)).join('');
    return `url("data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="${x} ${y} ${w} ${h}" preserveAspectRatio="none">`
      + `<mask id="m" maskUnits="userSpaceOnUse" ${box}><rect ${box} fill="#fff"/><path d="${GROVES.map(hull).join('')}${near}"/></mask><rect ${box} mask="url(#m)"/></svg>`)}")`;
  };
  const LEN = 24000; // Nessie's round (overview.css)

  const lay = (box) => {
    box.dataset.monsters = '';
    // A stage laid exactly as the drawing is, and on it: the strip the runner crosses, the clumps as one still drawing, and
    // Nessie's own small drawing.
    box.insertAdjacentHTML('beforeend', `<div class="ovm-layer"><div class="ovm-stage"><div class="ovm-band">${RUNNER(outfit())}</div>`
      + `<svg class="ovm-clumps" viewBox="0 0 3200 600" aria-hidden="true">${CLUMP}</svg>${NESSIE}</div></div>`);
    const layer = box.querySelector(':scope > .ovm-layer');
    const later = (fn, ms) => setTimeout(() => { if (layer.isConnected) fn(); }, ms);
    const q = (sel) => layer.querySelector(sel);
    layer.querySelectorAll('[data-at]').forEach((j) => { const [x, y] = j.dataset.at.split(' '); j.style.setProperty('--x', x); j.style.setProperty('--y', y); });
    // The big pines move with the window's width (scene.js draws the banner again), so what hides him is made again then.
    const hide = () => { if (!layer.isConnected) return; const m = hideMask(); ['mask-image', '-webkit-mask-image'].forEach((k) => q('.ovm-band').style.setProperty(k, m)); };
    hide();
    box.addEventListener('rabbidlab:scene', hide);
    if (still()) return;

    // The runner: always behind one clump or running to another.
    const runner = q('.ovm-runner');
    const { pace } = OUTFITS[outfit()];
    const walk = runner.querySelector('.ovm-walk');
    let spot = Math.floor(Math.random() * CLUMPS.length);
    let dash = null; // while he runs: from where to where, since when, for how long
    // Where his feet are across the drawing, and three quarters size, turned to face the way he goes.
    const place = (x, left) => { runner.parentNode.style.setProperty('--x', x); runner.parentNode.style.setProperty('--f', left ? 0.75 : -0.75); };
    const shake = (i, hard) => {
      const c = q(`[data-clump="${i}"]`);
      c.classList.remove('ovm-shake', 'ovm-hard');
      void c.getBoundingClientRect();
      c.classList.add('ovm-shake');
      c.classList.toggle('ovm-hard', hard);
    };
    place(CLUMPS[spot], true);
    runner.classList.add('ovm-here');
    const run = () => {
      let to = spot;
      while (to === spot) to = Math.floor(Math.random() * CLUMPS.length);
      // Walking, he only goes to the next clump along; a lope or a jog can cross several.
      if (pace < 0.7) to = spot === 0 ? 1 : spot === CLUMPS.length - 1 ? spot - 1 : spot + (Math.random() < 0.5 ? -1 : 1);
      shake(spot, true); // he pushes out through the branches
      later(() => {
        place(CLUMPS[spot], CLUMPS[to] < CLUMPS[spot]);
        const dist = Math.abs(CLUMPS[to] - CLUMPS[spot]) / 0.75;
        const ms = ((dist / 260) * 8000) / pace; // "Why is sasquatch running 900 miels an hour": about 260 units in 8 s, at his outfit's pace
        walk.getAnimations().forEach((a) => a.cancel());
        runner.classList.remove('ovm-hiding');
        walk.animate([{ transform: 'translateX(0)' }, { transform: `translateX(calc(${-dist} * var(--u)))` }], { duration: ms, easing: 'linear', fill: 'forwards' });
        later(() => shake(to, true), ms - 700); // and into the next one
        dash = { from: CLUMPS[spot], to: CLUMPS[to], at: performance.now(), ms };
        later(() => { spot = to; dash = null; wait(); }, ms);
      }, 520);
    };
    // Waiting behind the pines, standing still and upright so nothing of him leans out, the pines rustling once or twice
    // while he's there.
    const wait = () => {
      runner.classList.add('ovm-hiding');
      const long = 4000 + Math.random() * 7000;
      later(() => shake(spot, false), long * (0.25 + Math.random() * 0.2));
      if (Math.random() < 0.5) later(() => shake(spot, false), long * 0.7);
      later(run, long);
    };
    wait();

    // Nessie, now and then: somewhere new on the lake in the moon's part of it each time, going one way or the other. She
    // glides ("She also just needs to glide, not row"), and on the way looks at us and back over her shoulder, and dips her
    // head into the water twice ("Rowing, looking, dipping", less the rowing). Her neck, her rise out of the water and her
    // flippers are drawn 30 times a second on a timer while she's out, from where her round is (her drawing's own fade,
    // which the graphics card runs with her glide). Drawn once for every frame of the screen, a fast screen kept the page
    // busy a tenth of the time for her alone; each step is under a pixel.
    const ness = q('.ovm-nessie');
    const face = ness.querySelector('.ovm-face');
    const rises = ness.querySelectorAll('.ovm-rise');
    const paddles = [...ness.querySelectorAll('.ovm-paddle')];
    const necks = ness.querySelectorAll('.ovm-neck');
    const eyes = ness.querySelectorAll('.ovm-eye');
    const span = (p, a, b, c, e) => (p < a || p > e ? 0 : p < b ? sm((p - a) / (b - a)) : p <= c ? 1 : 1 - sm((p - c) / (e - c)));
    // Which way her head faces through her round: [part of her round, 1 forward / 0 at us / -1 back], eased between.
    const LOOK = [[0, 1], [0.3, 1], [0.35, 0], [0.4, 0], [0.46, -1], [0.52, -1], [0.57, 0], [0.62, 1], [1, 1]];
    const along = (keys, p) => {
      const i = Math.max(0, keys.findIndex(([v]) => v > p) - 1);
      const [[a, ha], [b, hb]] = [keys[i], keys[i + 1] || keys[i]];
      return b === a ? ha : lerp(ha, hb, sm((p - a) / (b - a)));
    };
    // When the runner bolts while she is up, she stops swimming and watches him until he is in the pines again ("She stops
    // to watch", the Nessie sees the monster mock): her head turns away from us toward him on the far shore and follows
    // him, forward when he is ahead of her, back over her shoulder when he is behind. While she is stopped her neck sways
    // slower ("floating around is a little too fast when stopped"). seen eases her in and out of watching.
    let [nessAt, nessDir, seen, aim, stopped, swayT, paddleT, last, round] = [2480, 1, 0, 0, false, 0, 0, null, null];
    const frame = () => {
      if (!layer.isConnected || !ness.classList.contains('ovm-go')) return;
      const now = performance.now();
      round = round || ness.getAnimations().find((a) => a.animationName === 'ovm-on');
      const ms = Number(round?.currentTime ?? 0);
      // Down again: nothing of hers is drawn and no timer runs until she next comes up.
      if (ms >= LEN) { ness.classList.remove('ovm-go'); later(swim, 6000 + Math.random() * 14000); return; }
      setTimeout(frame, 33);
      const p = ms / LEN;
      const [u, d] = [Math.max(0, Math.min(1, p / 0.12)), Math.max(0, Math.min(1, (p - 0.8) / 0.13))];
      const head = nessAt + nessDir * NS * (80 * p + 8);
      const rx = dash ? lerp(dash.from, dash.to, Math.min(1, Math.max(0, (now - dash.at) / dash.ms))) : null;
      if (rx !== null) aim = -Math.atan2(260, (rx - head) * nessDir);
      const dt = Math.min(100, now - (last ?? now));
      last = now;
      seen = lerp(seen, rx !== null && u > 0.9 && d === 0 ? 1 : 0, 1 - 0.93 ** (dt / 16.7));
      if ((seen > 0.5) !== stopped) {
        stopped = seen > 0.5;
        ness.getAnimations().forEach((a) => (stopped ? a.pause() : a.play()));
      }
      swayT += dt * lerp(1, 0.35, seen);
      if (!stopped) paddleT += dt;
      const turn = along(LOOK, p);
      const n = neck(u, d, swayT, { turn, theta: lerp(thetaOf(turn), aim, seen), dip: span(p, 0.14, 0.165, 0.185, 0.21) + span(p, 0.68, 0.705, 0.725, 0.75) });
      necks.forEach((g) => g.setAttribute('d', n.d));
      eyes.forEach((g, i) => {
        const [ex, ey, op] = n.eyes[i];
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
    // A round, and the next some time after it ends (a stop to watch makes it longer).
    const swim = () => {
      const left = Math.random() < 0.5;
      [nessAt, nessDir, seen, stopped, last, round] = [2300 + Math.random() * 600, left ? -1 : 1, 0, false, null, null];
      ness.style.setProperty('--x', nessAt.toFixed(0)); // where she comes up across the drawing
      ness.style.setProperty('--dir', nessDir); // which way the graphics card glides her
      face.setAttribute('transform', left ? 'scale(-1 1)' : '');
      ness.classList.remove('ovm-go');
      void ness.getBoundingClientRect();
      ness.classList.add('ovm-go');
      frame();
    };
    later(swim, 3000 + Math.random() * 6000);
  };
  const fill = () => document.querySelectorAll('[data-scene][data-fit]:not([data-monsters])').forEach(lay);
  fill();
  document.addEventListener('rabbidlab:show', fill);
  document.addEventListener('rabbidlab:scene', fill);
})();
