// The monsters on Overview's banner ("Dev it", the monsters mock's take A). One runner is always on the far shore, hiding in
// the pines: Sasquatch, small and black like a shadow, or Santa at Christmas ("1 on the screen at all times... he spawns from
// the same tree he hides behind"). The pines he is behind rustle, shake when he bursts out, and he runs to another clump and
// hides again ("Move the tree, the monster hides behind. Like shake it like he's hiding."); nothing of him shows through
// them. Now and then Nessie's neck rises out of the lake in the moon's path, she swims across, and her head turns down into
// the water with her neck following it like a snake's ("The head needs to bend in the water like a snake turns"). Drawn
// into the banner's own drawing (scene.js, RabbidLabScene), in its 3200 by 600 units.
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
  // Her neck, drawn each frame along a spine of SEG pieces from its foot, each turned from straight up toward where she
  // swims: thick at the foot, thin up the neck, the head a little fuller, then the snout.
  const SEG = 30;
  const LONG = 45;
  const REST = (s) => (s < 0.6 ? 3 + 9 * (s / 0.6) : s < 0.8 ? 12 + 92 * sm((s - 0.6) / 0.2) : 104 + 4 * ((s - 0.8) / 0.2)); // up, the head bent forward
  const UP = (s) => 2 + 6 * s; // coming up: straight, the head first
  const DOWN = (s) => 22 + 160 * s ** 1.25; // going down: curled over, the head into the water ahead of her
  const SWAY = (s, t) => 9 * s ** 1.3 * Math.sin((2 * Math.PI * t) / 4600 - 2.4 * s) + 6 * sm((s - 0.7) / 0.15) * Math.sin((2 * Math.PI * t) / 6600 + 0.8);
  const THICK = (s) => (s < 0.66 ? 3.4 - 1.9 * (s / 0.66) : s < 0.8 ? 1.5 + 0.45 * Math.sin(((s - 0.66) / 0.14) * (Math.PI / 2)) : 0.3 + 1.65 * (1 - ((s - 0.8) / 0.2) ** 2));
  // u: how far she has come up; d: how far into her dive, the head turning down first and the bend running down the neck
  // after it; t: the time, for the sway.
  const neck = (u, d, t) => {
    const P = [];
    let [x, y, prev] = [1.8, 0.6, null];
    for (let i = 0; i <= SEG; i++) {
      const s = i / SEG;
      const sway = SWAY(s, t);
      let a = lerp(UP(s) + sway * 0.4, REST(s) + sway, sm(u * 1.5 - s * 0.5));
      a = lerp(a, DOWN(s), sm(d * 1.6 - (1 - s) * 0.6));
      const r = (a * Math.PI) / 180;
      if (prev !== null) {
        const m = (prev + r) / 2;
        x += Math.sin(m) * (LONG / SEG);
        y -= Math.cos(m) * (LONG / SEG);
      }
      prev = r;
      P.push([x, y, r, THICK(s)]);
    }
    const pts = [...P.map(([px, py, r, w]) => [px - Math.cos(r) * w, py - Math.sin(r) * w]), ...P.map(([px, py, r, w]) => [px + Math.cos(r) * w, py + Math.sin(r) * w]).reverse()];
    let path = `M${f2(pts[0])}`;
    for (let i = 1; i < pts.length - 1; i++) path += `Q${f2(pts[i])} ${f2([(pts[i][0] + pts[i + 1][0]) / 2, (pts[i][1] + pts[i + 1][1]) / 2])}`;
    const [ex, ey, er] = P[Math.round(SEG * 0.87)];
    return { d: `${path}L${f2(pts[pts.length - 1])}Z`, eye: [ex - Math.cos(er) * 0.6, ey - Math.sin(er) * 0.6] };
  };
  const REST_NECK = neck(1, 0, 0);
  const NECK = (eye) => `<path class="ovm-neck" d="${REST_NECK.d}"/>${eye ? `<circle class="ovm-eye" cx="${REST_NECK.eye[0].toFixed(2)}" cy="${REST_NECK.eye[1].toFixed(2)}" r=".55"/>` : ''}`;
  const NESSIE = '<g class="ovm ovm-nessie"><defs><clipPath id="ovm-above"><rect x="-120" y="-80" width="240" height="80.4"/></clipPath>'
    + '<clipPath id="ovm-below"><rect x="-120" y=".4" width="240" height="80"/></clipPath></defs><g class="ovm-swim">'
    + `<g clip-path="url(#ovm-below)"><g class="ovm-rise"><g class="ovm-under" filter="url(#ovs-b1)">${BODY}${NECK(false)}</g></g></g>`
    + `<g clip-path="url(#ovm-above)"><g class="ovm-rise"><g class="ovm-dark">${BODY}${NECK(true)}</g></g></g></g></g>`;

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
  const OUTFITS = {
    sasquatch: { body: SHAGGY, cls: 'ovm-fur' }, // dark, the colour of the shore's pines a shade darker
    yeti: { body: SHAGGY, cls: 'ovm-snowfur' }, // the same in white, for when the banner shows the weather
    santa: { body: SANTA, cls: '' },
  };
  // Which outfit: Santa at Christmas; Sasquatch otherwise.
  const outfit = () => (S.holiday === 'christmas' ? 'santa' : 'sasquatch');
  // The far arm and leg behind, the near leg, the torso and head, the near arm in front; each joint turns about its own
  // point (overview.css), so these transforms only put the joints in place.
  const part = (p) => (p.startsWith('<') ? p : `<path d="${p}"/>`);
  const build = (m, cls) => {
    const leg = (i) => `<g class="ovm-leg${i ? ' ovm-b' : ''}" transform="translate(${m.hip[i][0]} ${m.hip[i][1]})"><g class="ovm-thigh">${part(m.thigh)}`
      + `<g transform="translate(0 ${m.knee})"><g class="ovm-shin">${part(m.shin)}</g></g></g></g>`;
    const arm = (i) => `<g class="ovm-arm${i ? ' ovm-b' : ''}" transform="translate(${m.shoulder[i][0]} ${m.shoulder[i][1]})"><g class="ovm-upper">${part(m.upper)}`
      + `<g transform="translate(0 ${m.elbow})"><g class="ovm-fore">${part(m.fore)}</g></g></g></g>`;
    return `<g class="${cls}">${arm(1)}${leg(1)}${leg(0)}${m.body}${arm(0)}</g>`;
  };
  const RUNNER = (name) => `<g class="ovm ovm-runner ovm-hiding"><g class="ovm-walk"><g class="ovm-bob">${build(OUTFITS[name].body, OUTFITS[name].cls)}</g></g></g>`;
  const PACE = 1.1; // the lope covers ground a little faster than 260 units in 8 seconds

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
  const HIDE = () => '<defs><mask id="ovm-hide" maskUnits="userSpaceOnUse" x="0" y="0" width="3200" height="600"><rect width="3200" height="600" fill="#fff"/>'
    + `<path fill="#000" d="${GROVES.map(hull).join('')}${(S.near.tall || []).map(([x, y, h]) => S.pine(x, y, h)).join('')}"/></mask></defs>`;
  const LEN = 24000; // Nessie's round (overview.css)

  const lay = (svg) => {
    svg.dataset.monsters = '';
    const later = (fn, ms) => setTimeout(() => { if (svg.isConnected) fn(); }, ms);
    svg.insertAdjacentHTML('beforeend', `<g class="ovm-layer">${HIDE()}<g mask="url(#ovm-hide)"><g>${RUNNER(outfit())}</g></g>${CLUMP}`
      + `<g transform="translate(2480 470) scale(1.75)">${NESSIE}</g></g>`);
    const q = (sel) => svg.querySelector(sel);
    if (still()) return;

    // The runner: always behind one clump or running to another.
    const runner = q('.ovm-runner');
    const walk = runner.querySelector('.ovm-walk');
    let spot = Math.floor(Math.random() * CLUMPS.length);
    const place = (x, left) => runner.parentNode.setAttribute('transform', `translate(${x} 434) scale(${left ? 0.75 : -0.75} .75)`);
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
      shake(spot, true); // he pushes out through the branches
      later(() => {
        place(CLUMPS[spot], CLUMPS[to] < CLUMPS[spot]);
        const dist = Math.abs(CLUMPS[to] - CLUMPS[spot]) / 0.75;
        const ms = ((dist / 260) * 8000) / PACE; // "Why is sasquatch running 900 miels an hour": about 260 units in 8 s
        walk.getAnimations().forEach((a) => a.cancel());
        runner.classList.remove('ovm-hiding');
        walk.animate([{ transform: 'translateX(0)' }, { transform: `translateX(${-dist}px)` }], { duration: ms, easing: 'linear', fill: 'forwards' });
        later(() => shake(to, true), ms - 700); // and into the next one
        later(() => { spot = to; wait(); }, ms);
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

    // Nessie, now and then: somewhere new on the lake in the moon's part of it each time, going one way or the other. Her
    // neck is drawn each frame while she's out, from where her round is.
    const ness = q('.ovm-nessie');
    const rise = ness.querySelector('.ovm-rise');
    const necks = ness.querySelectorAll('.ovm-neck');
    const eye = ness.querySelector('.ovm-eye');
    const frame = (now) => {
      if (!svg.isConnected || !ness.classList.contains('ovm-go')) return;
      const ms = Number(rise.getAnimations()[0]?.currentTime ?? 0);
      if (ms >= LEN) return;
      requestAnimationFrame(frame);
      const n = neck(Math.max(0, Math.min(1, ms / LEN / 0.12)), Math.max(0, Math.min(1, (ms / LEN - 0.8) / 0.13)), now);
      necks.forEach((p) => p.setAttribute('d', n.d));
      eye.setAttribute('cx', n.eye[0].toFixed(2));
      eye.setAttribute('cy', n.eye[1].toFixed(2));
    };
    const swim = () => {
      const left = Math.random() < 0.5;
      ness.parentNode.setAttribute('transform', `translate(${(2300 + Math.random() * 600).toFixed(0)} 470) scale(${left ? -1.75 : 1.75} 1.75)`);
      ness.classList.remove('ovm-go');
      void ness.getBoundingClientRect();
      ness.classList.add('ovm-go');
      requestAnimationFrame(frame);
      later(swim, LEN + 6000 + Math.random() * 14000);
    };
    later(swim, 3000 + Math.random() * 6000);
  };
  const fill = () => document.querySelectorAll('.ovs[data-size="tall"]:not([data-monsters])').forEach(lay);
  fill();
  document.addEventListener('rabbidlab:show', fill);
})();
