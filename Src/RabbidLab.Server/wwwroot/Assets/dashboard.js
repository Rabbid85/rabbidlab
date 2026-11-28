// Overview's dashboard: cards of what the lab knows, under the banner. Each person arranges their own, and it is kept for
// them on the server (Dashboard.cs). Edit dashboard turns editing on: a card is taken hold of anywhere on it and the
// others fly out of its way; four pictures in its head set its size; its gear names it; its × hides it; Add a card, beside
// Done and as the last tile, lists the cards that are not showing. Done puts it all away. A card shows only what the
// person's roles show, and keeps up to date while Overview is open.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, get, send, toast, where, live, count } = R;
  const [GAP, ROW] = [12, 196]; // between cards, and a row's height (dashboard.css)
  const SIZES = [['Small', 1, 1], ['Wide', 2, 1], ['Tall', 1, 2], ['Large', 2, 2]];
  const date = (iso) => new Date(`${iso}T12:00:00`).toLocaleDateString([], { month: 'short', day: 'numeric' });
  const day = (iso) => { const d = new Date(iso); return d.toDateString() === new Date().toDateString() ? d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : d.toLocaleDateString([], { month: 'short', day: 'numeric' }); };
  const big = (v, label) => `<div class="dash-big"><b>${esc(v)}</b><span>${esc(label)}</span></div>`;
  const rows = (list) => `<ul class="dash-rows">${list.map(([tone, a, b]) => `<li><i class="dash-dot" data-tone="${tone}"></i><span>${esc(a)}</span><small>${esc(b ?? '')}</small></li>`).join('')}</ul>`;
  const wait = '<div class="dash-big"><b><i class="sk sk-num"></i></b></div><div class="sk-lines"><i class="sk w75"></i><i class="sk w45"></i></div>';
  const none = (text) => `<p class="dash-none">${esc(text)}</p>`;

  // The cards there are: name, icon, usual size (columns by rows), the page it opens, a line for Add a card, whose roles
  // show it, and how it draws from what was read (d) at w columns by h rows; nothing read yet draws as loading.
  const TYPES = {
    news: { name: 'What\'s new', icon: 'new', size: [2, 2], page: 'whats-new', desc: 'The newest versions, newest first, and what changed in each.', can: () => true,
      draw: (d, w, h) => `<div class="dash-news">${R.releases().slice(0, h > 1 ? 6 : 3).map((x) => `<a href="#whats-new" data-wn="${esc(x.v)}">`
        + `${x.img && w > 1 && h > 1 ? `<img src="Assets/news/${esc(x.img)}" alt="">` : ''}<span class="dash-when">${esc(x.d ? date(x.d) : '')}</span>${R.relTag(x.k)}<b>${esc(x.v)}</b>`
        + `<span class="dash-line">${esc(x.t || x.items[0])}</span></a>`).join('')}</div><a class="dash-more" href="#whats-new">All versions${svg('right')}</a>` },
    machine: { name: 'This machine', icon: 'server', size: [2, 1], desc: 'The server Rabbid Lab runs on: version, uptime, sign-in, the service account.', can: () => true,
      draw: () => '<div class="facts-two" id="st-facts"></div>', after: () => R.machine() },
    sensors: { name: 'Sensors', icon: 'activity', size: [1, 1], page: 'sensors', desc: 'How many sensors are up, and any that are not.', can: () => has('sensors.view'),
      draw: (d, w, h) => {
        if (!d.sensors) return d.read ? none('The sensors could not be read.') : wait;
        const on = d.sensors.sensors.filter((x) => x.status !== 'paused');
        const off = on.filter((x) => x.status === 'down').map((x) => ['down', x.name, 'Down']).concat(on.filter((x) => x.status === 'warn').map((x) => ['warn', x.name, 'Warning']));
        return big(on.length ? `${on.filter((x) => x.status === 'up' || x.status === 'warn').length} of ${on.length}` : 'None', 'Up')
          + rows(off.length ? off.slice(0, h > 1 ? 8 : 2) : on.length ? [['up', 'Nothing down', 'All answering']] : [['off', 'No sensors yet', '']]);
      } },
    links: { name: 'Links', icon: 'link', size: [1, 1], page: 'links', desc: 'How many links answer, and which are down.', can: () => has('links.view'),
      draw: (d, w, h) => {
        if (!d.links || !d.states) return d.read ? none('The links could not be read.') : wait;
        const known = d.links.links.filter((l) => d.states[l.id]);
        const off = known.filter((l) => d.states[l.id].down).map((l) => ['down', l.name || l.url, 'Down']).concat(known.filter((l) => !d.states[l.id].up && !d.states[l.id].down).map((l) => ['warn', l.name || l.url, 'Not responding']));
        return big(known.length ? `${known.filter((l) => d.states[l.id].up).length} of ${known.length}` : 'None', 'Responding')
          + rows(off.length ? off.slice(0, h > 1 ? 8 : 2) : known.length ? [['up', 'Nothing down', 'All responding']] : [['off', 'No links are checked yet', '']]);
      } },
    devices: { name: 'Devices', icon: 'monitor', size: [1, 1], page: 'seats', desc: 'Computers with the client: online, offline, waiting for approval.', can: () => has('seats.view'),
      draw: (d, w, h) => {
        if (!d.seats) return d.read ? none('The devices could not be read.') : wait;
        const [mine, waiting] = [d.seats.filter((c) => c.approved), d.seats.filter((c) => !c.approved)];
        const list = [...(waiting.length ? [['warn', 'Waiting for approval', count(waiting.length, 'computer')]] : []), ...mine.filter((c) => !c.online).map((c) => ['off', c.computer, 'Offline'])];
        return big(mine.length ? `${mine.filter((c) => c.online).length} of ${mine.length}` : 'None', 'Online')
          + rows(list.length ? list.slice(0, h > 1 ? 8 : 2) : mine.length ? [['up', 'Every computer is online', '']] : [['off', 'No computers have the client yet', '']]);
      } },
    alerts: { name: 'Alerts', icon: 'bell', size: [1, 1], page: 'alerts', desc: 'Alerts that went off and nobody has acknowledged.', can: () => has('sensors.alerts'),
      draw: (d, w, h) => {
        if (!d.brief?.alerts) return d.read ? none('The alerts could not be read.') : wait;
        const a = d.brief.alerts;
        return big(String(a.fresh), 'Not acknowledged') + rows(a.latest.length ? a.latest.slice(0, h > 1 ? 5 : 2).map((f) => [f.severity === 'critical' ? 'down' : 'warn', f.title, day(f.at)]) : [['up', 'Nothing waits for you', '']]);
      } },
    activity: { name: 'Activity', icon: 'history', size: [2, 1], page: 'settings/activity', desc: 'The latest changes made in Rabbid Lab, and by whom.', can: () => has('activity.view'),
      draw: (d, w, h) => {
        if (!d.activity) return d.read ? none('The activity log could not be read.') : wait;
        const list = d.activity.entries.slice(0, h > 1 ? 8 : 4);
        return list.length ? rows(list.map((e) => [e.result === 'failed' ? 'down' : 'up', `${e.by}: ${e.title}`, day(e.at)])) : none('Nothing has happened yet.');
      } },
    network: { name: 'Network', icon: 'globe', size: [1, 1], page: 'network', desc: 'What answers on the network, and anything new.', can: () => has('network.view'),
      draw: (d, w, h) => {
        if (!d.brief?.network) return d.read ? none('The network could not be read.') : wait;
        const n = d.brief.network;
        return big(String(n.all), 'Discovered') + rows(n.all ? [['up', 'Online now', String(n.online)], [n.fresh ? 'warn' : 'up', 'New', n.fresh ? count(n.fresh, 'device') : 'None'],
          ...(h > 1 ? n.latest.map((x) => ['warn', x.name, x.ip]) : [])] : [['off', 'No scan has run yet', '']]);
      } },
  };
  const USUAL = ['news', 'sensors', 'links', 'devices', 'alerts', 'machine', 'activity', 'network'];
  const may = (type) => !!TYPES[type]?.can();
  const make = (type, w, h, title) => ({ type, w: w || TYPES[type].size[0], h: h || TYPES[type].size[1], title: title || '' });

  const box = $('dash');
  const grid = $('dash-grid');
  let cards = null; // the arrangement, once read: [{ type, w, h, title }]
  let data = {}; // what the cards show, as last read
  let editing = false;

  // Where every card goes, worked out here and told to the page, never left to it: each card in turn takes the first free
  // place it fits, reading across and then down, so a smaller one fills a gap a bigger one left.
  const pack = (list, cols) => {
    const taken = [];
    const free = (c, r, w, h) => { for (let y = r; y < r + h; y++) for (let x = c; x < c + w; x++) if (taken[y]?.[x]) return false; return true; };
    const at = new Map();
    list.forEach((it) => {
      const w = Math.min(it.w, cols);
      for (let r = 0; !at.has(it.type); r++) {
        for (let c = 0; c + w <= cols && !at.has(it.type); c++) {
          if (!free(c, r, w, it.h)) continue;
          for (let y = r; y < r + it.h; y++) { taken[y] = taken[y] || []; for (let x = c; x < c + w; x++) taken[y][x] = true; }
          at.set(it.type, { col: c, row: r, w, h: it.h });
        }
      }
    });
    return at;
  };
  const els = () => [...grid.querySelectorAll(':scope > [data-dash], :scope > .dash-add')];
  const el = (type) => grid.querySelector(`:scope > [data-dash="${type}"]`);
  const cols = () => Number(getComputedStyle(grid).getPropertyValue('--dash-cols')) || 4;
  const missing = () => USUAL.filter((t) => may(t) && !cards.some((c) => c.type === t));
  const plus = () => (editing ? [{ type: '+', w: 1, h: 1 }] : []); // Add a card, the last tile
  const spots = (list = cards, C = cols()) => pack([...list, ...plus()], C);
  // The grid measured, once for all that is then worked out from it: where it is, its columns and how wide one is.
  const measure = () => { const g = grid.getBoundingClientRect(); const C = cols(); return { g, C, cw: (g.width - GAP * (C - 1)) / C }; };
  const rect = (p, m = measure()) => {
    const [left, top] = [m.g.left + p.col * (m.cw + GAP), m.g.top + p.row * (ROW + GAP)];
    return { left, top, right: left + p.w * m.cw + (p.w - 1) * GAP, bottom: top + p.h * ROW + (p.h - 1) * GAP };
  };
  const place = () => {
    const at = spots();
    els().forEach((e) => {
      const p = at.get(e.dataset.dash || '+');
      e.style.setProperty('grid-column', `${p.col + 1} / span ${p.w}`);
      e.style.setProperty('grid-row', `${p.row + 1} / span ${p.h}`);
    });
  };
  // What moves flies: where every card is before a change and after it, and each goes from the one to the other.
  const flip = (change) => {
    const was = new Map(els().map((e) => [e, e.getBoundingClientRect()]));
    change();
    els().forEach((e) => {
      const a = was.get(e);
      e.getAnimations().forEach((x) => { if (x.id === 'fly') x.cancel(); });
      if (!a) return;
      const b = e.getBoundingClientRect();
      if (Math.abs(a.left - b.left) < 1 && Math.abs(a.top - b.top) < 1 && Math.abs(a.width - b.width) < 1 && Math.abs(a.height - b.height) < 1) return;
      const from = `translate(${a.left - b.left}px, ${a.top - b.top}px) scale(${a.width / b.width}, ${a.height / b.height})`;
      e.animate([{ transform: from, transformOrigin: '0 0' }, { transform: 'none', transformOrigin: '0 0' }], { duration: 280, easing: 'cubic-bezier(.2, .8, .2, 1)' }).id = 'fly';
    });
  };
  const head = (c) => {
    const T = TYPES[c.type];
    const name = `${svg(T.icon)}<b>${esc(c.title || T.name)}</b>`;
    return `${T.page ? `<a class="dash-name" href="#${T.page}">${name}</a>` : `<span class="dash-name">${name}</span>`}<span class="dash-tools"><span class="dash-sizes">`
      + SIZES.map(([label, w, h]) => `<button type="button" data-dash-size="${w} ${h}" aria-pressed="${Math.min(2, c.w) === w && c.h === h}" aria-label="${label}" data-tip="${label}">`
        + `<svg viewBox="0 0 16 16" aria-hidden="true">${[[0, 0], [1, 0], [0, 1], [1, 1]].map(([x, y]) => `<rect x="${1 + x * 7.5}" y="${1 + y * 7.5}" width="6.5" height="6.5" rx="1.7"${x < w && y < h ? ' class="on"' : ''}/>`).join('')}</svg></button>`).join('')
      + `</span><button type="button" data-dash-name aria-label="Name this card" data-tip="Name">${svg('gear')}</button><button type="button" data-dash-hide aria-label="Hide this card" data-tip="Hide">${svg('x')}</button></span>`;
  };
  // A card's edge while editing: thin dashes, drawn over its own so they can march on the one pointed at.
  const ANTS = '<svg class="dash-ants" aria-hidden="true"><rect/></svg>';
  // One card's inside: its head when its size or name changed, and what it shows when that changed.
  const fill = (e, c) => {
    const frame = `${c.w} ${c.h} ${c.title}`;
    if (e.dataset.frame !== frame) {
      e.dataset.frame = frame;
      e.dataset.w = c.w;
      e.dataset.h = c.h;
      e.innerHTML = `<header class="dash-head">${head(c)}</header><div class="dash-body"></div>${ANTS}`;
      delete e.dataset.shows;
    }
    const html = TYPES[c.type].draw(data, c.w, c.h);
    if (e.dataset.shows === html) return;
    e.dataset.shows = html;
    e.querySelector('.dash-body').innerHTML = html;
    TYPES[c.type].after?.();
  };
  // The cards on the page made the same as the list: new ones in, gone ones out, each at its size and in its place.
  const sync = (fresh) => {
    const have = new Map([...grid.querySelectorAll(':scope > [data-dash]')].map((e) => [e.dataset.dash, e]));
    cards.forEach((c) => {
      let e = have.get(c.type);
      if (!e) {
        grid.insertAdjacentHTML('beforeend', `<section class="dash-card${c.type === fresh ? ' dash-arrive' : ''}" data-dash="${c.type}"></section>`);
        e = grid.lastElementChild;
      }
      have.delete(c.type);
      fill(e, c);
      grid.append(e);
    });
    have.forEach((e) => e.remove());
    const tile = grid.querySelector(':scope > .dash-add');
    if (!editing) tile?.remove();
    else if (tile) grid.append(tile);
    else grid.insertAdjacentHTML('beforeend', `<button type="button" class="dash-add" data-dash-add>${svg('plus')}<span>Add a card</span></button>`);
    box.classList.toggle('editing', editing);
    $('dash-edit').lastElementChild.textContent = editing ? 'Done' : 'Edit dashboard';
    $('dash-add').hidden = !editing;
    place();
  };

  // Kept for the person a moment after the last change, so a card carried through ten places is saved once.
  let saving = 0;
  const keep = () => {
    clearTimeout(saving);
    saving = setTimeout(async () => {
      try {
        await send('/api/me/dashboard', { cards: cards.map((c) => ({ type: c.type, w: c.w, h: c.h, title: c.title || null })) });
      } catch (e) {
        toast('Your dashboard was not saved.', e.message, { tone: 'bad' });
      }
    }, 500);
  };
  // What the cards show: what the console already keeps up to date where it does, and one light read each for the rest.
  const read = async () => {
    const on = (type) => cards.some((c) => c.type === type);
    const ask = (wanted, path) => (wanted ? get(path).catch(() => null) : null);
    const [sensors, links, states, seats, brief, activity] = await Promise.all([
      on('sensors') ? R.sensorsNow?.() || ask(true, '/api/sensors') : null,
      on('links') ? R.linksNow?.() || ask(true, '/api/links') : null,
      on('links') ? R.linkStatesNow?.() || ask(true, '/api/links/states').then((a) => a?.states || null) : null,
      ask(on('devices'), '/api/clients'),
      ask(on('alerts') || on('network'), '/api/overview/cards'),
      ask(on('activity'), '/api/activity/find?count=8'),
    ]);
    return { read: true, sensors, links, states, seats, brief, activity };
  };
  const show = (d) => { data = d; if (cards) cards.forEach((c) => { const e = el(c.type); if (e) fill(e, c); }); };
  const again = () => read().then(show, () => {});
  const changed = () => { keep(); again(); };

  const add = (type) => { cards.push(make(type)); flip(() => sync(type)); changed(); el(type).scrollIntoView({ block: 'nearest', behavior: 'smooth' }); };
  const hide = (type) => {
    const e = el(type);
    e.animate([{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(.9)' }], { duration: 170, easing: 'ease-in', fill: 'forwards' }).onfinish = () => {
      cards = cards.filter((c) => c.type !== type);
      flip(sync);
      keep();
    };
  };
  const size = (type, w, h) => { const c = cards.find((x) => x.type === type); if (c.w === w && c.h === h) return; [c.w, c.h] = [w, h]; flip(sync); keep(); };

  // Carrying a card. It stays in the grid as the slot where it will land, and a copy of it follows the pointer. Where the
  // slot goes is worked out from the pointer alone: of every place in the order the card could take, the one whose slot
  // the pointer is in, or else is nearest. The same pointer always gives the same answer, so nothing can flicker between
  // two places, and while the pointer stays in the slot nothing moves at all.
  let drag = null; // { c, f: its copy, x0, y0, left, top, x, y, rest: the other cards, from, at: its place in the order then and now }
  let press = null; // a pointer is down on a card: { c, x, y, id }
  let settle = null; // ends the last card's flight to its slot at once
  const far = (r, x, y) => Math.hypot(Math.max(r.left - x, 0, x - r.right), Math.max(r.top - y, 0, y - r.bottom));
  const order = (d, i) => [...d.rest.slice(0, i), d.c, ...d.rest.slice(i)];
  const spot = (x, y) => {
    const d = drag;
    const m = measure();
    const tries = Array.from({ length: d.rest.length + 1 }, (_, i) => ({ i, far: far(rect(spots(order(d, i), m.C).get(d.c.type), m), x, y) }));
    const now = tries[d.at];
    if (!now.far) return d.at;
    // Of several that would do, the one that moves the fewest others.
    const fewest = (list) => list.reduce((a, b) => (Math.abs(b.i - d.at) < Math.abs(a.i - d.at) ? b : a)).i;
    const under = tries.filter((t) => !t.far);
    if (under.length) return fewest(under);
    // Out in the open: the nearest slot, and it stays where it is unless another is clearly nearer.
    const least = Math.min(...tries.map((t) => t.far));
    return least > now.far - 8 ? d.at : fewest(tries.filter((t) => t.far <= least + 0.5));
  };
  const carry = (x, y) => {
    const d = drag;
    if (!d) return;
    [d.x, d.y] = [x, y];
    d.f.style.setProperty('transform', `translate(${x - d.x0}px, ${y - d.y0}px) scale(1.03)`);
    const at = spot(x, y);
    if (at === d.at) return;
    d.at = at;
    cards = order(d, at);
    flip(sync);
  };
  const scroller = () => { for (let p = box.parentElement; p; p = p.parentElement) { if (/auto|scroll/.test(getComputedStyle(p).overflowY) && p.scrollHeight > p.clientHeight) return p; } return document.scrollingElement; };
  // Held near the top or the foot of the window, the page scrolls under the card.
  const edge = () => {
    if (!press || !drag) return;
    const by = drag.y > innerHeight - 90 ? 14 : drag.y < 100 ? -14 : 0;
    if (by) scroller().scrollBy(0, by);
    requestAnimationFrame(edge);
  };
  const lift = (c, x, y) => {
    settle?.();
    const e = el(c.type);
    const r = e.getBoundingClientRect();
    const f = e.cloneNode(true);
    f.removeAttribute('data-dash');
    f.querySelectorAll('[id]').forEach((n) => n.removeAttribute('id'));
    f.classList.remove('dash-arrive');
    f.classList.add('dash-float');
    ['grid-column', 'grid-row'].forEach((p) => f.style.removeProperty(p));
    Object.entries({ left: r.left, top: r.top, width: r.width, height: r.height }).forEach(([k, v]) => f.style.setProperty(k, `${v}px`));
    document.body.append(f);
    e.classList.add('dash-slot');
    document.documentElement.classList.add('dash-carrying');
    // The grid keeps one height while a card is carried, with room under it for the card: a row coming or going would make
    // the page longer or shorter, and at its foot the whole page would shift under the pointer.
    grid.style.setProperty('min-height', `${grid.getBoundingClientRect().height + (c.h + 1) * (ROW + GAP)}px`);
    const from = cards.indexOf(c);
    drag = { c, f, x0: x, y0: y, left: r.left, top: r.top, x, y, rest: cards.filter((k) => k !== c), from, at: from };
    carry(x, y);
    requestAnimationFrame(edge);
  };
  const drop = (cancel) => {
    const d = drag;
    if (!d) return;
    drag = null;
    document.documentElement.classList.remove('dash-carrying');
    if (cancel && d.at !== d.from) { cards = order(d, d.from); flip(sync); }
    const e = el(d.c.type);
    const t = rect(spots().get(d.c.type));
    settle = () => { settle = null; d.f.remove(); e.classList.remove('dash-slot'); grid.style.removeProperty('min-height'); };
    const landed = () => { if (d.f.isConnected) settle?.(); };
    const a = d.f.animate([{ transform: d.f.style.transform }, { transform: `translate(${t.left - d.left}px, ${t.top - d.top}px)` }], { duration: 200, easing: 'cubic-bezier(.2, .8, .2, 1)', fill: 'forwards' });
    a.onfinish = landed;
    a.oncancel = landed;
    if (!cancel && d.at !== d.from) keep();
  };
  grid.addEventListener('pointerdown', (e) => {
    const h = e.target.closest('[data-dash]');
    if (e.button || !e.isPrimary || !editing || !h || press || e.target.closest('button, input')) return;
    press = { c: cards.find((c) => c.type === h.dataset.dash), x: e.clientX, y: e.clientY, id: e.pointerId };
  });
  const up = () => {
    if (press && drag) {
      drop(false);
      // The click that ends a drag opens nothing.
      const eat = (c) => { c.stopPropagation(); c.preventDefault(); };
      addEventListener('click', eat, { capture: true, once: true });
      setTimeout(() => removeEventListener('click', eat, { capture: true }), 0);
    }
    press = null;
  };
  addEventListener('pointermove', (e) => {
    if (!press || e.pointerId !== press.id) return;
    if (e.pointerType === 'mouse' && !e.buttons) { up(); return; } // let go where no pointerup reached the page
    if (!drag && Math.hypot(e.clientX - press.x, e.clientY - press.y) > 5) lift(press.c, press.x, press.y);
    if (!drag) return;
    e.preventDefault();
    carry(e.clientX, e.clientY);
  });
  ['pointerup', 'pointercancel', 'blur'].forEach((t) => addEventListener(t, up));
  addEventListener('keydown', (e) => { if (e.key === 'Escape' && drag) { drop(true); press = null; } });
  // Nothing on a card is picked up by the browser itself, or selected, while cards are being moved.
  ['selectstart', 'dragstart'].forEach((t) => addEventListener(t, (e) => { if (press || (editing && e.target.closest?.('#dash-grid'))) e.preventDefault(); }));
  document.addEventListener('scroll', () => { if (drag) carry(drag.x, drag.y); }, true);
  // A window made wider or narrower: the cards take their places in its two or four columns.
  new ResizeObserver(() => { if (!cards) return; if (drag) carry(drag.x, drag.y); else place(); }).observe(grid);

  const edit = (on) => { editing = on; if (drag) drop(true); press = null; flip(sync); };
  $('dash-edit').addEventListener('click', () => edit(!editing));
  // While editing a card opens nothing: its name and its rows are for taking hold of.
  grid.addEventListener('click', (e) => { if (editing && e.target.closest('a')) e.preventDefault(); }, true);
  grid.addEventListener('click', (e) => {
    const type = e.target.closest('[data-dash]')?.dataset.dash;
    const b = e.target.closest('button');
    if (!b) return;
    if (b.dataset.dashSize) { const [w, h] = b.dataset.dashSize.split(' ').map(Number); size(type, w, h); }
    if ('dashHide' in b.dataset) hide(type);
    if ('dashName' in b.dataset) name(type);
    if ('dashAdd' in b.dataset) pick();
  });
  $('dash-add').addEventListener('click', () => pick());

  // Add a card: the cards that are not showing, of those the person's roles show.
  const pick = () => {
    const free = missing();
    $('dash-pick-list').innerHTML = free.length ? free.map((t) => `<button type="button" data-dash-pick="${t}">${svg(TYPES[t].icon)}<b>${esc(TYPES[t].name)}</b><span>${esc(TYPES[t].desc)}</span></button>`).join('')
      : '<p class="dash-none">Every card is on your dashboard.</p>';
    $('dash-pick').showModal();
  };
  $('dash-pick').addEventListener('click', (e) => {
    const b = e.target.closest('[data-dash-pick]');
    if (e.target.closest('[data-dash-close]')) $('dash-pick').close();
    if (!b) return;
    $('dash-pick').close();
    add(b.dataset.dashPick);
    toast('Card added.', `${TYPES[b.dataset.dashPick].name} is on your dashboard.`);
  });
  // A card's own name; left empty, or as it was, it has its usual one.
  let naming = '';
  const name = (type) => {
    naming = type;
    const c = cards.find((x) => x.type === type);
    $('dash-name-title').textContent = TYPES[type].name;
    $('dash-name-box').value = c.title || TYPES[type].name;
    $('dash-name').showModal();
    $('dash-name-box').select();
  };
  $('dash-name').addEventListener('click', (e) => { if (e.target.closest('[data-dash-close]')) $('dash-name').close(); });
  $('dash-name-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const c = cards.find((x) => x.type === naming);
    const t = $('dash-name-box').value.trim();
    c.title = t && t !== TYPES[naming].name ? t : '';
    $('dash-name').close();
    sync();
    keep();
  });
  $('dash-usual').addEventListener('click', async () => {
    try {
      await send('/api/me/dashboard/reset', {});
      cards = USUAL.filter(may).map((t) => make(t));
      $('dash-pick').close();
      flip(sync);
      again();
      toast('Your dashboard is as it started.');
    } catch (e) {
      toast('Your dashboard was not reset.', e.message, { tone: 'bad' });
    }
  });

  // The arrangement is read once, the first time Overview shows; a card the person's roles no longer show is left out.
  const start = async () => {
    let kept = null;
    try { kept = (await get('/api/me/dashboard')).cards; } catch { /* the usual cards, this visit */ }
    cards = (kept ? kept.filter((c) => may(c.type)).map((c) => make(c.type, c.w, c.h, c.title)) : USUAL.filter(may).map((t) => make(t)));
    sync();
    again();
  };
  let started = false;
  const shown = () => {
    if (where()[0] !== 'overview') { if (editing) edit(false); return; }
    if (!started) { started = true; start(); } else if (cards) again();
  };
  document.addEventListener('rabbidlab:show', shown);
  live({ page: () => where()[0] === 'overview' && !!cards, every: 60000, read, now: () => data, draw: show, hold: () => !!drag });
  // The sidebar's own reads of sensors and links tell the cards as they change.
  ['rabbidlab:sensors', 'rabbidlab:links', 'rabbidlab:alerts'].forEach((t) => document.addEventListener(t, () => { if (cards && where()[0] === 'overview' && !drag) again(); }));
})();
