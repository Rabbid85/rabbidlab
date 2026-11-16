// The moving background on setup and sign-in: tiles in Rabbid Lab's colours drifting slowly, some filled, some outlined,
// with a few dots. It sizes itself to the screen: one tile per patch of screen, spread evenly with no gaps, a
// little bigger on big screens. It lays itself out again when the window changes size or moves to another monitor, and
// uses fewer tiles when the computer can't keep up. Still when the system asks for reduced motion.
(() => {
  const canvas = document.querySelector('canvas.tiles');
  if (!canvas || !canvas.getContext) return;
  const ctx = canvas.getContext('2d');
  const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  // The accent and the colours that go with it, then a pale grey (palette.js). They're CSS, which a canvas can't read, so
  // each is turned into a plain colour first.
  const probe = document.createElement('i');
  probe.hidden = true;
  document.body.append(probe);
  const COLOURS = window.RabbidLabColours.tiles.map((c) => {
    probe.style.color = c;
    return getComputedStyle(probe).color;
  });
  probe.remove();
  const PATCH = 260; // one tile for about every 260 × 260 pixels of screen
  let most = 200; // enough for a 4K screen; lowered when frames run slow
  let tiles = [];
  let width = 0;
  let height = 0;
  let scale = 1;
  let last = performance.now();
  let slow = 0;
  let queued = false;
  const born = last;

  const random = (min, max) => min + Math.random() * (max - min);

  const make = (x, y) => {
    const depth = Math.random(); // 0 far, 1 near: nearer tiles are bigger, brighter and a little faster
    const angle = random(0, Math.PI * 2);
    const speed = 12 + depth * 22; // pixels a second, before scaling
    return {
      x, y, base: 8 + depth * 18,
      kind: Math.random() < 0.18 ? 'dot' : Math.random() < 0.32 ? 'outline' : 'fill',
      colour: COLOURS[Math.floor(Math.random() * COLOURS.length)],
      vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed - 4,
      turn: random(-0.6, 0.6), rotation: random(0, Math.PI * 2),
      bob: random(0, Math.PI * 2), alpha: 0.7 + depth * 0.3,
    };
  };

  // New tiles on a jittered grid over the whole screen: one per cell, somewhere inside it, so they spread evenly.
  const seed = (count) => {
    if (count <= 0) return;
    const cols = Math.max(1, Math.round(Math.sqrt((count * width) / height)));
    const rows = Math.max(1, Math.ceil(count / cols));
    const cells = [...Array(cols * rows).keys()].sort(() => Math.random() - 0.5).slice(0, count);
    for (const c of cells) {
      tiles.push(make(((c % cols) + Math.random()) * (width / cols), (Math.floor(c / cols) + Math.random()) * (height / rows)));
    }
  };

  const layout = () => {
    queued = false;
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const oldWidth = width;
    const oldHeight = height;
    width = window.innerWidth;
    height = window.innerHeight;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    canvas.style.width = width + 'px';
    canvas.style.height = height + 'px';
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    // A little bigger on big screens, so a 4K monitor doesn't look like confetti.
    scale = Math.min(1.8, Math.max(0.85, Math.min(width, height) / 900));
    // Tiles already there keep their place on the screen, stretched to the new size, so nothing jumps.
    if (oldWidth && oldHeight) {
      for (const t of tiles) {
        t.x *= width / oldWidth;
        t.y *= height / oldHeight;
      }
    }
    const count = Math.min(most, Math.round((width * height) / (PATCH * PATCH * scale)));
    if (tiles.length > count) tiles.length = count;
    else seed(count - tiles.length);
  };
  const relayout = () => {
    if (!queued) {
      queued = true;
      requestAnimationFrame(layout);
    }
  };

  // Moved to a monitor with different scaling: lay out again so the canvas stays sharp there.
  const watchScaling = () => {
    window.matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`).addEventListener('change', () => {
      relayout();
      watchScaling();
    }, { once: true });
  };

  const rounded = (s, r) => {
    ctx.beginPath();
    ctx.moveTo(-s / 2 + r, -s / 2);
    ctx.arcTo(s / 2, -s / 2, s / 2, s / 2, r);
    ctx.arcTo(s / 2, s / 2, -s / 2, s / 2, r);
    ctx.arcTo(-s / 2, s / 2, -s / 2, -s / 2, r);
    ctx.arcTo(-s / 2, -s / 2, s / 2, -s / 2, r);
    ctx.closePath();
  };

  const draw = (now) => {
    const frame = now - last;
    const dt = Math.min(0.05, frame / 1000);
    last = now;
    // Slower than 40 frames a second for a while: fewer tiles, never fewer than 20.
    slow = frame > 25 && frame < 250 ? slow + 1 : Math.max(0, slow - 1);
    if (slow > 90 && tiles.length > 20) {
      most = Math.max(20, Math.round(tiles.length * 0.8));
      tiles.length = most;
      slow = 0;
    }
    const fade = Math.min(1, (now - born) / 1200); // fade in on load
    ctx.clearRect(0, 0, width, height);
    for (const t of tiles) {
      const size = t.base * scale;
      if (!still) {
        t.x += t.vx * scale * dt;
        t.y += t.vy * scale * dt;
        t.rotation += t.turn * dt;
        t.bob += dt * 1.2;
        const m = size * 2;
        if (t.x < -m) t.x = width + m;
        if (t.x > width + m) t.x = -m;
        if (t.y < -m) t.y = height + m;
        if (t.y > height + m) t.y = -m;
      }
      ctx.save();
      ctx.translate(t.x, t.y + Math.sin(t.bob) * 5 * scale);
      ctx.rotate(t.rotation);
      ctx.globalAlpha = t.alpha * fade;
      if (t.kind === 'dot') {
        ctx.beginPath();
        ctx.arc(0, 0, size * 0.22, 0, Math.PI * 2);
        ctx.fillStyle = t.colour;
        ctx.fill();
      } else {
        rounded(size, size * 0.24);
        if (t.kind === 'outline') {
          ctx.lineWidth = 1.5;
          ctx.strokeStyle = t.colour;
          ctx.stroke();
        } else {
          ctx.fillStyle = t.colour;
          ctx.fill();
        }
      }
      ctx.restore();
    }
    if (!still || fade < 1) requestAnimationFrame(draw);
  };

  window.addEventListener('resize', relayout);
  layout();
  watchScaling();
  requestAnimationFrame(draw);
})();
