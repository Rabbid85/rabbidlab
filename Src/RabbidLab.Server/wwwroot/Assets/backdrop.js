// What moves behind the sign-in card (Settings, Appearance, Sign-in page): none, today's squares, or one of the others, all
// in the accent and the colours that go with it. The same code draws the soft lights on the sign-in page's Clock side and the
// preview in Appearance. One frame and no movement when the system asks for reduced motion.
(() => {
  const KINDS = [['none', 'None'], ['squares', 'Squares'], ['bubbles', 'Bubbles'], ['glass', 'Glass'], ['net', 'Network'], ['rain', 'Data rain'],
    ['fireflies', 'Fireflies'], ['drops', 'Drops'], ['fizz', 'Fizz'], ['lava', 'Lava lamp'], ['liquid', 'Liquid'], ['steam', 'Steam'], ['molecules', 'Molecules']];
  const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const BLUE = [61, 127, 230];
  const VIOLET = [142, 82, 214];
  const PALE = [242, 243, 250];
  const rnd = (a, b) => a + Math.random() * (b - a);
  const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
  // CSS colours (the accent changes them) turned into numbers a canvas can use. Through one pixel, since a mixed colour
  // comes back from CSS as color(srgb 0.5 0.1 0.9) rather than rgb().
  const pixel = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
  const colours = () => {
    const probe = document.createElement('i');
    probe.hidden = true;
    document.body.append(probe);
    const read = (v) => {
      probe.style.color = v;
      pixel.clearRect(0, 0, 1, 1);
      pixel.fillStyle = '#000';
      pixel.fillStyle = getComputedStyle(probe).color;
      pixel.fillRect(0, 0, 1, 1);
      return [...pixel.getImageData(0, 0, 1, 1).data.slice(0, 3)];
    };
    const c = { A: read('var(--accent)'), L: read('var(--accent-line)'), tiles: (window.RabbidLabColours?.tiles || ['var(--accent)']).map((v) => rgba(read(v), 1)) };
    probe.remove();
    return c;
  };

  // Draws one kind on a canvas until stopped. width: draw as if the canvas were that wide, so a small preview looks like the
  // page scaled down. Returns stop().
  const run = (canvas, kind, { width = 0 } = {}) => {
    const ctx = canvas.getContext('2d');
    const { A, L, tiles: TILES } = colours();
    let w = 0;
    let h = 0;
    let state = {};

    const KIND = {
      // Squares: today's tiles in the lab's colours drifting slowly, some filled, some outlined, with a few dots. One tile for
      // about every 260 × 260 pixels, a little bigger on big screens, fewer when the computer can't keep up.
      squares: {
        setup: () => {
          state.tiles = [];
          state.most = 200;
          state.slow = 0;
          state.born = performance.now();
          squaresFill();
        },
        resize: (ow, oh) => {
          for (const t of state.tiles) {
            t.x *= w / ow;
            t.y *= h / oh;
          }
          squaresFill();
        },
        frame: (t, dt, ms) => {
          state.slow = ms > 25 && ms < 250 ? state.slow + 1 : Math.max(0, state.slow - 1);
          if (state.slow > 90 && state.tiles.length > 20) {
            state.most = Math.max(20, Math.round(state.tiles.length * 0.8));
            state.tiles.length = state.most;
            state.slow = 0;
          }
          const fade = still ? 1 : Math.min(1, (performance.now() - state.born) / 1200);
          const scale = state.scale;
          for (const q of state.tiles) {
            const size = q.base * scale;
            if (!still) {
              q.x += q.vx * scale * dt;
              q.y += q.vy * scale * dt;
              q.rotation += q.turn * dt;
              q.bob += dt * 1.2;
              const m = size * 2;
              if (q.x < -m) q.x = w + m;
              if (q.x > w + m) q.x = -m;
              if (q.y < -m) q.y = h + m;
              if (q.y > h + m) q.y = -m;
            }
            ctx.save();
            ctx.translate(q.x, q.y + Math.sin(q.bob) * 5 * scale);
            ctx.rotate(q.rotation);
            ctx.globalAlpha = q.alpha * fade;
            ctx.beginPath();
            if (q.kind === 'dot') {
              ctx.arc(0, 0, size * 0.22, 0, Math.PI * 2);
              ctx.fillStyle = q.colour;
              ctx.fill();
            } else {
              ctx.roundRect(-size / 2, -size / 2, size, size, size * 0.24);
              if (q.kind === 'outline') {
                ctx.lineWidth = 1.5;
                ctx.strokeStyle = q.colour;
                ctx.stroke();
              } else {
                ctx.fillStyle = q.colour;
                ctx.fill();
              }
            }
            ctx.restore();
          }
        },
      },
      // Bubbles: the flask's bubbles rising from the bottom, wobbling, some outlined.
      bubbles: {
        setup: () => { state.b = Array.from({ length: Math.round(w / 48) }, () => ({ x: rnd(0, w), y: rnd(0, h), r: rnd(3, 16), v: rnd(10, 40), p: rnd(0, 6), c: Math.random() < .25 ? L : A, o: Math.random() < .4 })); },
        frame: (t, dt) => {
          for (const b of state.b) {
            if (!still) {
              b.y -= b.v * dt;
              if (b.y < -20) { b.y = h + 20; b.x = rnd(0, w); }
            }
            const x = b.x + Math.sin(t * 1.3 + b.p) * 6;
            const a = .15 + .5 * (b.y / h);
            ctx.beginPath();
            ctx.arc(x, b.y, b.r, 0, Math.PI * 2);
            if (b.o) { ctx.strokeStyle = rgba(b.c, a); ctx.lineWidth = 1.5; ctx.stroke(); } else { ctx.fillStyle = rgba(b.c, a * .6); ctx.fill(); }
          }
        },
      },
      // Glass: big soft lights in the accent, blue and violet, drifting behind frosted dark.
      glass: {
        setup: () => { state.blobs = [A, BLUE, VIOLET, L, A].map((c, i) => ({ c, x: rnd(0, w), y: rnd(0, h), r: rnd(.28, .45) * Math.max(w, h), sx: rnd(-1, 1), sy: rnd(-1, 1), p: i })); },
        frame: (t) => {
          ctx.globalCompositeOperation = 'lighter';
          for (const b of state.blobs) {
            const x = b.x + Math.sin(t * .05 + b.p) * w * .18 * b.sx;
            const y = b.y + Math.cos(t * .04 + b.p * 2) * h * .18 * b.sy;
            const g = ctx.createRadialGradient(x, y, 0, x, y, b.r);
            g.addColorStop(0, rgba(b.c, .22));
            g.addColorStop(1, rgba(b.c, 0));
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, w, h);
          }
          ctx.globalCompositeOperation = 'source-over';
        },
      },
      // Network: the lab's machines as points drifting, near ones linked, a pulse running along a link now and then.
      net: {
        setup: () => {
          state.dots = Array.from({ length: Math.round((w * h) / 26000) }, () => ({ x: rnd(0, w), y: rnd(0, h), vx: rnd(-.15, .15), vy: rnd(-.15, .15), c: Math.random() < .2 ? BLUE : A }));
          state.pulses = [];
        },
        frame: (t, dt) => {
          const near = 150;
          const d = state.dots;
          if (!still) {
            for (const p of d) {
              p.x = (p.x + p.vx * dt * 60 + w) % w;
              p.y = (p.y + p.vy * dt * 60 + h) % h;
            }
          }
          ctx.lineWidth = 1;
          for (let i = 0; i < d.length; i++) {
            for (let j = i + 1; j < d.length; j++) {
              const r = Math.hypot(d[i].x - d[j].x, d[i].y - d[j].y);
              if (r > near) continue;
              ctx.strokeStyle = rgba(A, (1 - r / near) * .22);
              ctx.beginPath();
              ctx.moveTo(d[i].x, d[i].y);
              ctx.lineTo(d[j].x, d[j].y);
              ctx.stroke();
              if (!still && Math.random() < .0004) state.pulses.push({ a: d[i], b: d[j], k: 0 });
            }
          }
          state.pulses = state.pulses.filter((p) => (p.k += dt * .8) < 1);
          for (const p of state.pulses) {
            ctx.fillStyle = rgba(L, 1 - p.k * .6);
            ctx.beginPath();
            ctx.arc(p.a.x + (p.b.x - p.a.x) * p.k, p.a.y + (p.b.y - p.a.y) * p.k, 2.5, 0, Math.PI * 2);
            ctx.fill();
          }
          for (const p of d) {
            ctx.fillStyle = rgba(p.c, .7);
            ctx.beginPath();
            ctx.roundRect(p.x - 2.5, p.y - 2.5, 5, 5, 1.5);
            ctx.fill();
          }
        },
      },
      // Data rain: short lines falling down columns at their own speeds, brighter at the head.
      rain: {
        setup: () => { state.cols = Array.from({ length: Math.round(w / 34) }, (_, i) => ({ x: i * 34 + 17, y: rnd(-h, h), v: rnd(60, 160), len: rnd(60, 180), c: Math.random() < .2 ? BLUE : A })); },
        frame: (t, dt) => {
          for (const c of state.cols) {
            if (!still) { c.y += c.v * dt; if (c.y - c.len > h) { c.y = rnd(-200, 0); c.v = rnd(60, 160); } }
            const g = ctx.createLinearGradient(0, c.y - c.len, 0, c.y);
            g.addColorStop(0, rgba(c.c, 0));
            g.addColorStop(1, rgba(c.c, .55));
            ctx.fillStyle = g;
            ctx.fillRect(c.x - 1, c.y - c.len, 2, c.len);
            ctx.fillStyle = rgba(L, .9);
            ctx.fillRect(c.x - 1.5, c.y - 3, 3, 3);
          }
        },
      },
      // Fireflies: soft glowing dots drifting and blinking slowly.
      fireflies: {
        setup: () => { state.f = Array.from({ length: Math.round((w * h) / 30000) }, () => ({ x: rnd(0, w), y: rnd(0, h), a: rnd(0, 6), v: rnd(.2, .6), p: rnd(0, 6), c: Math.random() < .25 ? BLUE : Math.random() < .5 ? L : A })); },
        frame: (t, dt) => {
          ctx.globalCompositeOperation = 'lighter';
          for (const f of state.f) {
            if (!still) {
              f.a += Math.sin(t * .3 + f.p) * dt * .8;
              f.x = (f.x + Math.cos(f.a) * f.v * dt * 30 + w) % w;
              f.y = (f.y + Math.sin(f.a) * f.v * dt * 30 + h) % h;
            }
            const glow = .25 + .75 * Math.max(0, Math.sin(t * .8 + f.p));
            const g = ctx.createRadialGradient(f.x, f.y, 0, f.x, f.y, 18);
            g.addColorStop(0, rgba(f.c, .55 * glow));
            g.addColorStop(1, rgba(f.c, 0));
            ctx.fillStyle = g;
            ctx.fillRect(f.x - 18, f.y - 18, 36, 36);
          }
          ctx.globalCompositeOperation = 'source-over';
        },
      },
      // Drops: data rain as liquid, round drops falling faster as they go, splashing into a pool along the bottom.
      drops: {
        setup: () => {
          state.drop = (y) => ({ x: rnd(0, w), y, v: rnd(0, 40), r: rnd(3, 7), c: Math.random() < .22 ? BLUE : Math.random() < .3 ? L : A, wait: rnd(0, 2.5) });
          state.d = Array.from({ length: Math.round(w / 26) }, () => state.drop(rnd(-h * .6, h * .8)));
          state.bits = []; // droplets thrown up by a landing
          state.rings = []; // ripples on the pool
          state.floor = h - 22;
        },
        frame: (t, dt) => {
          const G = 380; // pixels per second, per second; drops stop speeding up at 560
          const floor = state.floor;
          const pool = ctx.createLinearGradient(0, floor - 4, 0, h);
          pool.addColorStop(0, rgba(L, .16));
          pool.addColorStop(1, rgba(A, .06));
          ctx.fillStyle = pool;
          ctx.beginPath();
          ctx.moveTo(0, h);
          for (let x = 0; x <= w; x += 12) ctx.lineTo(x, floor + Math.sin(x * .02 + t * 1.4) * 1.5);
          ctx.lineTo(w, h);
          ctx.closePath();
          ctx.fill();
          for (const d of state.d) {
            if (!still) {
              if (d.wait > 0) { d.wait -= dt; continue; }
              d.v = Math.min(560, d.v + G * dt * (.6 + d.r * .08));
              d.y += d.v * dt;
              if (d.y + d.r > floor) {
                state.rings.push({ x: d.x, c: d.c, k: 0, size: d.r });
                for (let i = 0; i < 3 + Math.round(d.r); i++) state.bits.push({ x: d.x, y: floor - 1, vx: rnd(-90, 90), vy: -rnd(90, 210) * (d.r / 5), r: rnd(.8, 1.8), c: d.c, life: 1 });
                Object.assign(d, state.drop(rnd(-80, -10)));
                continue;
              }
            }
            // The drop, longer the faster it falls, with a shine on it.
            const stretch = 1 + Math.min(1.6, d.v / 420);
            ctx.save();
            ctx.translate(d.x, d.y);
            const body = ctx.createLinearGradient(0, -d.r * 2 * stretch, 0, d.r);
            body.addColorStop(0, rgba(L, .55));
            body.addColorStop(1, rgba(d.c, .9));
            ctx.fillStyle = body;
            ctx.beginPath();
            ctx.moveTo(0, -d.r * 2.4 * stretch);
            ctx.bezierCurveTo(d.r * .5, -d.r * 1.2 * stretch, d.r * 1.05, -d.r * .2, d.r, d.r * .15);
            ctx.arc(0, d.r * .15, d.r, 0, Math.PI);
            ctx.bezierCurveTo(-d.r * 1.05, -d.r * .2, -d.r * .5, -d.r * 1.2 * stretch, 0, -d.r * 2.4 * stretch);
            ctx.fill();
            ctx.fillStyle = 'rgba(255,255,255,.55)';
            ctx.beginPath();
            ctx.ellipse(-d.r * .35, -d.r * .2, d.r * .22, d.r * .38, -.3, 0, Math.PI * 2);
            ctx.fill();
            ctx.restore();
          }
          state.bits = state.bits.filter((b) => b.life > 0 && b.y < floor + 2);
          for (const b of state.bits) {
            if (!still) { b.vy += G * dt; b.x += b.vx * dt; b.y += b.vy * dt; b.life -= dt * .9; }
            ctx.fillStyle = rgba(b.c, Math.max(0, b.life) * .85);
            ctx.beginPath();
            ctx.arc(b.x, b.y, b.r, 0, Math.PI * 2);
            ctx.fill();
          }
          state.rings = state.rings.filter((s) => (s.k += dt * 1.1) < 1);
          ctx.lineWidth = 1.4;
          for (const s of state.rings) {
            ctx.strokeStyle = rgba(s.c, (1 - s.k) * .55);
            for (const lag of [0, .25]) {
              const k = Math.max(0, s.k - lag);
              if (!k) continue;
              ctx.beginPath();
              ctx.ellipse(s.x, floor + 2, s.size * 2 + k * s.size * 9, 1.2 + k * 3.5, 0, 0, Math.PI * 2);
              ctx.stroke();
            }
          }
        },
      },
      // Fizz: streams of tiny bubbles rising from the bottom like soda, now and then a bigger one.
      fizz: {
        setup: () => {
          state.streams = Array.from({ length: Math.round(w / 38) }, () => ({ x: rnd(0, w), rate: rnd(4, 12), acc: 0 }));
          // Already under way, so the first frame isn't empty.
          state.b = Array.from({ length: Math.round(w / 6) }, () => ({ x: rnd(0, w), y: rnd(0, h), r: Math.random() < .06 ? rnd(4, 7) : rnd(1, 2.6), v: rnd(70, 140), p: rnd(0, 6), c: Math.random() < .15 ? L : A }));
        },
        frame: (t, dt) => {
          if (!still) {
            for (const s of state.streams) {
              s.acc += s.rate * dt;
              while (s.acc > 1) { s.acc -= 1; state.b.push({ x: s.x + rnd(-3, 3), y: h + 4, r: Math.random() < .06 ? rnd(4, 7) : rnd(1, 2.6), v: rnd(70, 140), p: rnd(0, 6), c: Math.random() < .15 ? L : A }); }
            }
          }
          ctx.lineWidth = 1.2;
          for (const b of state.b) {
            if (!still) b.y -= b.v * dt * (1 + b.r * .05);
            ctx.strokeStyle = rgba(b.c, Math.min(1, b.y / h) * .8);
            ctx.beginPath();
            ctx.arc(b.x + Math.sin(t * 3 + b.p) * 2, b.y, b.r, 0, Math.PI * 2);
            ctx.stroke();
          }
          state.b = state.b.filter((b) => b.y > -10);
        },
      },
      // Lava lamp: soft blobs of liquid rising, sinking and running into each other.
      lava: {
        setup: () => { state.l = Array.from({ length: 9 }, (_, i) => ({ x: rnd(.1, .9), y: rnd(0, 1), r: rnd(.07, .14), v: rnd(.015, .04) * (i % 2 ? 1 : -1), p: rnd(0, 6), c: i % 3 === 0 ? BLUE : i % 3 === 1 ? A : VIOLET })); },
        frame: (t) => {
          ctx.save();
          ctx.filter = `blur(${Math.round(26 * (state.zoom || 1))}px)`;
          for (const b of state.l) {
            const y = (((b.y + t * b.v) % 1.3) + 1.3) % 1.3 - .15;
            const x = b.x + Math.sin(t * .2 + b.p) * .05;
            const r = b.r * Math.min(w, h) * (1 + Math.sin(t * .5 + b.p) * .15);
            ctx.fillStyle = rgba(b.c, .55);
            ctx.beginPath();
            ctx.arc(x * w, y * h, r, 0, Math.PI * 2);
            ctx.fill();
          }
          ctx.restore();
        },
      },
      // Liquid: the bottom of the screen filled like the flask, its surface moving, bubbles rising through it.
      liquid: {
        setup: () => { state.b = Array.from({ length: Math.round(w / 30) }, () => ({ x: rnd(0, w), y: rnd(h * .7, h), r: rnd(2, 6), v: rnd(15, 45), p: rnd(0, 6) })); },
        frame: (t, dt) => {
          const top = (x) => h * .74 + Math.sin(x * .006 + t * .8) * 14 + Math.sin(x * .013 - t * .5) * 8;
          const g = ctx.createLinearGradient(0, h * .68, 0, h);
          g.addColorStop(0, rgba(L, .32));
          g.addColorStop(1, rgba(A, .14));
          ctx.fillStyle = g;
          ctx.beginPath();
          ctx.moveTo(0, h);
          for (let x = 0; x <= w; x += 10) ctx.lineTo(x, top(x));
          ctx.lineTo(w, h);
          ctx.closePath();
          ctx.fill();
          ctx.strokeStyle = rgba(L, .5);
          ctx.lineWidth = 2;
          ctx.beginPath();
          for (let x = 0; x <= w; x += 10) ctx.lineTo(x, top(x));
          ctx.stroke();
          ctx.fillStyle = 'rgba(255,255,255,.18)';
          for (const b of state.b) {
            if (!still) { b.y -= b.v * dt; if (b.y < top(b.x) + 4) { b.y = h + rnd(0, 30); b.x = rnd(0, w); } }
            ctx.beginPath();
            ctx.arc(b.x + Math.sin(t * 2 + b.p) * 3, b.y, b.r, 0, Math.PI * 2);
            ctx.fill();
          }
        },
      },
      // Steam: soft wisps rising from the bottom and fading, like the flask on its burner.
      steam: {
        setup: () => { state.s = Array.from({ length: 14 }, () => ({ x: rnd(0, 1), p: rnd(0, 1), sp: rnd(.04, .08), wob: rnd(0, 6), c: Math.random() < .3 ? A : PALE })); },
        frame: (t) => {
          ctx.lineCap = 'round';
          for (const s of state.s) {
            const k = (s.p + t * s.sp) % 1;
            const x = s.x * w + Math.sin(t * .4 + s.wob) * 40;
            const y = h - k * h * 1.1;
            ctx.strokeStyle = rgba(s.c, Math.sin(k * Math.PI) * (s.c === A ? .16 : .09));
            ctx.lineWidth = 18 + k * 50;
            ctx.beginPath();
            ctx.moveTo(x, y + 60);
            ctx.bezierCurveTo(x + 40 * Math.sin(t * .6 + s.wob), y + 20, x - 40 * Math.cos(t * .5 + s.wob), y - 20, x + 20 * Math.sin(t * .7), y - 60);
            ctx.stroke();
          }
        },
      },
      // Molecules: atoms joined by bonds, each molecule turning slowly as it drifts.
      molecules: {
        setup: () => {
          const shapes = [[[0, 0], [1, 0]], [[0, 0], [1, 0], [-.5, .87]], [[0, 0], [1, 0], [1.5, .87], [1, 1.73], [0, 1.73], [-.5, .87]], [[0, 0], [1, .3], [2, 0], [1, 1.3]]];
          state.m = Array.from({ length: Math.round((w * h) / 120000) + 4 }, () => ({ x: rnd(0, w), y: rnd(0, h), a: rnd(0, 6), va: rnd(-.15, .15), vx: rnd(-6, 6), vy: rnd(-6, 6), s: rnd(18, 34), atoms: shapes[Math.floor(rnd(0, shapes.length))], c: Math.random() < .3 ? BLUE : A }));
        },
        frame: (t, dt) => {
          ctx.lineWidth = 2;
          for (const m of state.m) {
            if (!still) { m.a += m.va * dt; m.x = (m.x + m.vx * dt + w) % w; m.y = (m.y + m.vy * dt + h) % h; }
            const pts = m.atoms.map(([px, py]) => [m.x + (px * Math.cos(m.a) - py * Math.sin(m.a)) * m.s, m.y + (px * Math.sin(m.a) + py * Math.cos(m.a)) * m.s]);
            ctx.strokeStyle = rgba(m.c, .3);
            for (let i = 1; i < pts.length; i++) { ctx.beginPath(); ctx.moveTo(...pts[i - 1]); ctx.lineTo(...pts[i]); ctx.stroke(); }
            if (pts.length === 6) { ctx.beginPath(); ctx.moveTo(...pts[5]); ctx.lineTo(...pts[0]); ctx.stroke(); }
            pts.forEach(([px, py], i) => {
              ctx.fillStyle = rgba(i === 0 ? L : m.c, .75);
              ctx.beginPath();
              ctx.arc(px, py, i === 0 ? 6 : 4.5, 0, Math.PI * 2);
              ctx.fill();
            });
          }
        },
      },
      // Lights: the sign-in page's Clock side, three soft lights drifting slowly behind the time.
      lights: {
        setup: () => {},
        frame: (t) => {
          ctx.globalCompositeOperation = 'lighter';
          [[A, .2, .25, 0], [BLUE, .75, .7, 2], [VIOLET, .4, .85, 4]].forEach(([c, fx, fy, p]) => {
            const x = w * fx + Math.sin(t * .07 + p) * w * .15;
            const y = h * fy + Math.cos(t * .05 + p) * h * .12;
            const g = ctx.createRadialGradient(x, y, 0, x, y, Math.max(w, h) * .55);
            g.addColorStop(0, rgba(c, .28));
            g.addColorStop(1, rgba(c, 0));
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, w, h);
          });
          ctx.globalCompositeOperation = 'source-over';
        },
      },
    };
    // Squares: as many as fit the size, spread evenly on a jittered grid, a little bigger on big screens.
    const squaresFill = () => {
      const make = (x, y) => {
        const depth = Math.random(); // 0 far, 1 near: nearer tiles are bigger, brighter and a little faster
        const angle = rnd(0, Math.PI * 2);
        const speed = 12 + depth * 22;
        return {
          x, y, base: 8 + depth * 18, kind: Math.random() < 0.18 ? 'dot' : Math.random() < 0.32 ? 'outline' : 'fill',
          colour: TILES[Math.floor(Math.random() * TILES.length)], vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed - 4,
          turn: rnd(-0.6, 0.6), rotation: rnd(0, Math.PI * 2), bob: rnd(0, Math.PI * 2), alpha: 0.7 + depth * 0.3,
        };
      };
      state.scale = Math.min(1.8, Math.max(0.85, Math.min(w, h) / 900));
      const count = Math.min(state.most, Math.round((w * h) / (260 * 260 * state.scale)));
      if (state.tiles.length > count) {
        state.tiles.length = count;
        return;
      }
      const more = count - state.tiles.length;
      if (more <= 0) return;
      const cols = Math.max(1, Math.round(Math.sqrt((more * w) / h)));
      const rows = Math.max(1, Math.ceil(more / cols));
      const cells = [...Array(cols * rows).keys()].sort(() => Math.random() - 0.5).slice(0, more);
      for (const c of cells) state.tiles.push(make(((c % cols) + Math.random()) * (w / cols), (Math.floor(c / cols) + Math.random()) * (h / rows)));
    };

    const k = KIND[kind];
    let raf = 0;
    let alive = true;
    let ready = false; // set up at a real size: a canvas that's hidden waits until it shows
    let seen = true; // off screen (Settings closed, say), nothing is drawn
    // The canvas's own size, sharp on high-DPI screens; laid out again when it changes size.
    const size = () => {
      const ratio = Math.min(devicePixelRatio || 1, 2);
      const cw = canvas.clientWidth;
      const ch = canvas.clientHeight;
      const zoom = width && cw ? cw / width : 1;
      const was = [w, h];
      w = cw / zoom;
      h = ch / zoom;
      state.zoom = zoom;
      canvas.width = Math.round(cw * ratio);
      canvas.height = Math.round(ch * ratio);
      ctx.setTransform(ratio * zoom, 0, 0, ratio * zoom, 0, 0);
      return was;
    };
    const draw = (t, dt, ms) => {
      ctx.clearRect(0, 0, w, h);
      if (ready) k.frame(t, dt, ms);
    };
    size();
    if (k && w && h) {
      k.setup();
      ready = true;
    }
    draw(8, 0, 16);
    let last = performance.now();
    const loop = (now) => {
      raf = 0;
      if (!alive || !seen) return;
      const ms = now - last;
      last = now;
      draw(now / 1000, Math.min(.05, ms / 1000), ms);
      raf = requestAnimationFrame(loop);
    };
    const go = () => {
      if (!k || still || raf) return;
      last = performance.now();
      raf = requestAnimationFrame(loop);
    };
    const resized = new ResizeObserver(() => {
      if (!alive) return;
      const [ow, oh] = size();
      if (!k || !w || !h) return;
      if (k.resize && ready && ow && oh) k.resize(ow, oh);
      else {
        state = { zoom: state.zoom };
        k.setup();
        ready = true;
      }
      draw(8, 0, 16);
    });
    resized.observe(canvas);
    const watch = new IntersectionObserver(([e]) => {
      seen = e.isIntersecting;
      if (seen) go();
    });
    watch.observe(canvas);
    go();
    return () => {
      alive = false;
      cancelAnimationFrame(raf);
      resized.disconnect();
      watch.disconnect();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
    };
  };

  // One kind on a canvas at a time: show() again with another kind (or after the accent changed) swaps it.
  const on = (canvas, opts) => {
    let stop = null;
    let shown = '';
    return (kind) => {
      const key = `${kind}|${getComputedStyle(document.documentElement).getPropertyValue('--accent')}`;
      if (key === shown) return;
      shown = key;
      stop?.();
      stop = run(canvas, kind, opts);
    };
  };

  window.RabbidLabBackdrop = { KINDS, run, on };
})();
