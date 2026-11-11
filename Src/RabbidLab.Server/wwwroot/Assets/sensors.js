// Sensors: #sensors, the things Rabbid Lab checks on a timer. The hero says how the lab is doing and its squares are the
// sensors; below, the sensors in their groups as cards or as a table whose columns can be resized and reordered.
// console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can } = R;
  const COLOURS = ['#FF8A3D', '#F2BE6B', '#B07CE8', '#6FA8FF', '#3CC7C7', '#F06BB4', '#7C83FF']; // a group's colour; green and red are left to up and down
  const KIND = { ping: 'Ping', port: 'Port', web: 'Web page', dns: 'DNS lookup', cert: 'Certificate', hw: 'Server hardware' };
  const WORD = { up: 'Up', down: 'Down', warn: 'Warning', paused: 'Paused', new: 'Checking' };
  const DOWN = ' down'; // the filter's choice of everything that's down: no group's name starts with a space
  const NO_GROUP = ' none'; // and its choice of sensors with no group
  const NEW_GROUP = ' new';
  let sensors = [];
  let groups = [];
  let rules = [];
  let mailReady = false;
  let lastCheck = null;
  let group = ''; // the group being looked at, DOWN, or '' for all of them
  let lost = ''; // the group that was picked when it went away, in case it was only renamed
  let timer = null;

  const stored = (key) => { try { return localStorage.getItem(key); } catch { return null; } };
  const store = (key, value) => { try { localStorage.setItem(key, value); } catch { /* private browsing: it lasts until the page closes */ } };
  let view = stored('rabbidlab.sensors.view') === 'grid' ? 'grid' : 'cards';

  // --- Words ----------------------------------------------------------------------------------------------------------

  const hash = (text) => [...String(text)].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7) / 4294967295;
  const short = (iso) => {
    const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
    return s < 90 ? `${Math.round(s)} s` : s < 5400 ? `${Math.round(s / 60)} min` : s < 129600 ? `${Math.round(s / 3600)} h` : `${Math.round(s / 86400)} d`;
  };
  const long = (iso) => {
    const s = Math.max(1, (Date.now() - new Date(iso).getTime()) / 1000);
    const say1 = (n, unit) => `${n} ${unit}${n === 1 ? '' : 's'}`;
    return s < 90 ? say1(Math.round(s), 'second') : s < 5400 ? say1(Math.round(s / 60), 'minute') : s < 129600 ? say1(Math.round(s / 3600), 'hour') : say1(Math.round(s / 86400), 'day');
  };
  const colour = (s) => (s.group && groups.includes(s.group) ? COLOURS[groups.indexOf(s.group) % COLOURS.length] : '#C9CCDA');
  const check = (s) => (s.kind === 'port' ? `Port ${s.port}` : KIND[s.kind] || s.kind);
  const where = (s) => (s.kind === 'web' ? s.target.replace(/^https?:\/\//i, '').replace(/\/$/, '')
    : s.kind === 'cert' && s.port && s.port !== 443 ? `${s.target}:${s.port}` : s.kind === 'dns' && s.server ? `${s.target} on ${s.server}` : s.target);
  const ms = (n) => (n === 0 ? 'under 1 ms' : `${n} ms`);
  const days = (note) => (/(\d+) day/.exec(note || '') ? `${/(\d+) day/.exec(note)[1]} days left` : note || '');
  // How it is now, in a few words for a card or a row.
  const now = (s) => (s.status === 'down' ? `Down ${s.since ? short(s.since) : ''}`.trim() : s.status === 'paused' ? 'Paused' : s.status === 'new' ? 'Checking…'
    : s.kind === 'cert' ? days(s.note) : s.status === 'warn' ? s.note || 'Warning' : s.kind === 'hw' ? s.note || 'All OK' : s.ms == null ? 'Up' : ms(s.ms));
  const percent = (p) => (p == null ? '' : `${p === 100 ? 100 : p.toFixed(p > 99.9 ? 2 : 1)}%`);
  const letters = (name) => {
    const words = String(name).split(/[\s._-]+/).filter(Boolean);
    const digit = /(\d)\s*$/.exec(name);
    return ((words[0] || '?')[0] + (digit ? digit[1] : (words[1] || '')[0] || '')).toUpperCase();
  };
  const paint = (root) => {
    R.paint(root);
    root.querySelectorAll('[data-top]').forEach((el) => { el.style.top = `${el.dataset.top}%`; });
  };

  // The last hour of reply times as a line. Checks with no reply are a red dashed stretch along the bottom.
  const line = (recent, status) => {
    const box = (inner) => `<svg class="sn-line" viewBox="0 0 300 40" preserveAspectRatio="none" aria-hidden="true">${inner}</svg>`;
    if (!recent.length || status === 'paused') return `<div class="sn-chart">${box('<path class="idle" d="M0 30H300"/>')}</div>`;
    const top = Math.max(...recent, 1) * 1.25;
    const y = (n) => 36 - (n / top) * 30;
    const step = 300 / Math.max(recent.length - 1, 1);
    let d = '';
    let gone = '';
    let open = false;
    recent.forEach((n, i) => {
      const x = i * step;
      if (n < 0) {
        gone += `M${Math.max(0, x - step / 2).toFixed(1)} 37H${Math.min(300, x + step / 2).toFixed(1)}`;
        open = false;
      } else {
        d += `${open ? 'L' : 'M'}${x.toFixed(1)} ${y(n).toFixed(1)}`;
        open = true;
      }
    });
    const end = recent[recent.length - 1];
    return `<div class="sn-chart">${box(`<path d="${d}"/>${gone ? `<path class="gone" d="${gone}"/>` : ''}`)}${end >= 0 ? `<i data-top="${(y(end) / 40 * 100).toFixed(1)}"></i>` : ''}</div>`;
  };

  // --- The hero: how the lab is doing, and a square for every sensor -------------------------------------------------------

  const places = R.places; // where the squares go; Links puts its own there the same way
  const squares = new Map(); // a sensor's square, kept between draws so its beat isn't started over
  const drawSquares = () => {
    const hero = $('sn-hero');
    const { slots, size } = places(sensors.length);
    const seen = new Set();
    [...sensors].sort((a, b) => a.id.localeCompare(b.id)).forEach((s, i) => {
      if (i >= slots.length) return;
      seen.add(s.id);
      let el = squares.get(s.id);
      if (!el) {
        el = document.createElement('button');
        el.type = 'button';
        el.dataset.open = s.id;
        hero.prepend(el);
        squares.set(s.id, el);
      }
      const r = hash(s.id);
      const down = s.status === 'down';
      const side = Math.round((down ? size * 1.4 : size * (0.85 + r * 0.35)));
      el.className = `tile sen s-${s.status}${size < 20 ? ' tiny' : ''}`;
      el.title = `${s.name} · ${now(s)}`;
      el.textContent = letters(s.name);
      el.style.setProperty('--g', colour(s));
      el.style.setProperty('--t', `${(2.2 + r * 2.4).toFixed(2)}s`);
      el.style.setProperty('--d', `-${(hash(`${s.id}d`) * 4).toFixed(2)}s`);
      Object.assign(el.style, { left: `${(slots[i][0] + r * 1.2).toFixed(2)}%`, top: `${(slots[i][1] + hash(`${s.id}y`) * 4).toFixed(2)}%`, width: `${side}px`, height: `${side}px`,
        rotate: `${down ? 8 : Math.round(hash(`${s.id}r`) * 50 - 25)}deg` });
    });
    squares.forEach((el, id) => {
      if (seen.has(id)) return;
      el.remove();
      squares.delete(id);
    });
  };
  const drawHero = () => {
    const live = sensors.filter((s) => s.status !== 'paused');
    const down = live.filter((s) => s.status === 'down');
    const warn = live.filter((s) => s.status === 'warn');
    const fresh = live.length > 0 && live.every((s) => s.status === 'new');
    $('sn-hero').classList.toggle('bad', down.length > 0);
    $('sn-title').textContent = !sensors.length ? 'No sensors yet' : down.length ? `${down.length} down` : warn.length ? `${warn.length} warning${warn.length === 1 ? '' : 's'}`
      : fresh ? 'Checking…' : live.length ? 'All up' : 'All paused';
    const first = down[0] || warn[0];
    $('sn-lead').textContent = !sensors.length ? 'Nothing is being checked.' : down.length ? `${first.name} stopped answering ${first.since ? `${long(first.since)} ago` : 'just now'}.`
      : warn.length ? `${first.name}: ${first.note || 'needs attention'}.` : fresh ? 'The first checks are under way.' : live.length ? 'Everything is answering.' : 'Nothing is being checked.';
    $('sn-lead-more').textContent = !sensors.length ? 'Add a sensor to watch a server, a service or a certificate.'
      : down.length > 1 ? `So did ${down.slice(1, 4).map((s) => s.name).join(', ')}${down.length > 4 ? ` and ${down.length - 4} more` : ''}.`
        : down.length && !mailReady ? 'Nobody is emailed until Email settings are filled in.' : '';
    $('sn-answering').textContent = sensors.length ? `${live.filter((s) => s.status === 'up' || s.status === 'warn').length} of ${live.length}` : '0';
    const week = sensors.map((s) => s.up7).filter((p) => p != null);
    $('sn-week').textContent = week.length ? percent(Math.round(week.reduce((a, b) => a + b, 0) / week.length * 100) / 100) : '…';
    $('sn-last').textContent = lastCheck ? `${short(lastCheck)} ago` : '…';
    $('sn-card').hidden = !first;
    if (first) {
      $('sn-card').className = `hero-new ${down.length ? 'bad' : 'warn'}`;
      $('sn-card-kind').textContent = down.length ? 'Down now' : 'Needs attention';
      $('sn-card-title').textContent = first.name;
      $('sn-card-text').textContent = down.length ? `${check(first)} to ${where(first)}: ${(first.note || 'no reply').toLowerCase()}${first.since ? ` since ${R.when(first.since)}` : ''}.`
        : `${check(first)} for ${where(first)}: ${first.note || ''}.`;
      $('sn-card-more').dataset.open = first.id;
    }
    drawSquares();
    $('sensors-dot').hidden = !down.length;
  };

  // --- The grid's columns: an order and widths of the person's own, kept in the browser -----------------------------------

  const COLS = { state: ['State', 130], name: ['Sensor', 220], check: ['Check', 120], where: ['Address', 200], reply: ['Reply', 110], line: ['Last hour', 240], up: ['Up, 7 days', 100], rule: ['Alert rule', 120] };
  const plain = () => ({ order: Object.keys(COLS), w: Object.fromEntries(Object.entries(COLS).map(([k, c]) => [k, c[1]])) });
  const readCols = () => {
    try {
      const kept = JSON.parse(stored('rabbidlab.sensors.columns') || 'null');
      const start = plain();
      if (!kept) return start;
      // A column added since it was saved goes at the end; one that's gone is dropped.
      const order = [...kept.order.filter((k) => k in COLS), ...start.order.filter((k) => !kept.order.includes(k))];
      return { order, w: Object.fromEntries(order.map((k) => [k, Math.min(900, Math.max(60, Number(kept.w?.[k]) || start.w[k]))])) };
    } catch {
      return plain();
    }
  };
  let cols = readCols();
  let sizing = false; // a column's edge is being dragged
  let moving = null; // the column whose heading is being dragged
  const applyCols = () => {
    const list = $('sn-list');
    list.style.setProperty('--sn-cols', cols.order.map((k) => `minmax(${cols.w[k]}px, ${cols.w[k]}fr)`).join(' '));
    list.style.setProperty('--sn-min', `${cols.order.reduce((n, k) => n + cols.w[k], 0) + 12 * (cols.order.length - 1) + 32}px`);
    $('sn-cols-reset').hidden = view !== 'grid' || JSON.stringify(cols) === JSON.stringify(plain());
  };
  const keepCols = () => {
    store('rabbidlab.sensors.columns', JSON.stringify(cols));
    applyCols();
  };
  const rule = (id) => rules.find((r) => r.id === id) || rules[1] || { name: id, color: '#8A8FA8' };
  const CELL = {
    state: (s) => `<span class="state ${s.status}">${s.status === 'down' && s.since ? `Down ${short(s.since)}` : WORD[s.status] || s.status}</span>`,
    name: (s) => `<div class="nm"><span class="lic letter" data-color="${colour(s)}"><b>${esc(letters(s.name)[0])}</b></span><b>${esc(s.name)}</b></div>`,
    check: (s) => `<span>${esc(check(s))}</span>`,
    where: (s) => `<span class="dim" title="${esc(where(s))}">${esc(where(s))}</span>`,
    reply: (s) => `<span class="num">${esc(s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : s.kind === 'hw' ? s.note || 'All OK' : s.ms == null ? '' : ms(s.ms)) : '')}</span>`,
    line: (s) => line(s.recent, s.status),
    up: (s) => `<span class="num">${percent(s.up7)}</span>`,
    rule: (s) => `<span><span class="rchip" data-color="${rule(s.rule).color}"><i class="dot"></i>${esc(rule(s.rule).name)}</span></span>`,
  };
  const head = () => `<div class="tr th sn">${cols.order.map((k) => `<span class="sn-th" draggable="true" data-col="${k}">${COLS[k][0]}<i class="sn-size" data-size="${k}"></i></span>`).join('')}</div>`;
  const row = (s) => `<div class="tr sn s-${s.status}" data-open="${esc(s.id)}">${cols.order.map((k) => CELL[k](s)).join('')}</div>`;

  // --- The sensors, in their groups ----------------------------------------------------------------------------------------

  const card = (s) => `<div class="ltile sn-tile s-${s.status}" data-open="${esc(s.id)}"${can('sensors.edit') ? ` draggable="true" data-drag="${esc(s.id)}"` : ''}><button type="button" class="lgo" aria-label="${esc(s.name)}">`
    + `<span class="lic letter" data-color="${colour(s)}"><b>${esc(letters(s.name)[0])}</b></span>`
    + `<span class="lt"><b>${esc(s.name)}</b><span>${esc(check(s))}</span><code>${esc(where(s))}</code></span></button>`
    + `<i class="lstate ${s.status === 'paused' || s.status === 'new' ? '' : s.status}"></i><span class="sn-now">${esc(now(s))}</span>${line(s.recent, s.status)}</div>`;

  const draw = () => {
    const find = $('sn-find').value.trim().toLowerCase();
    const down = sensors.filter((s) => s.status === 'down');
    // The group picked may be gone after a change, and nothing may be down any more: back to all of them.
    if (group === DOWN ? !down.length : group && group !== NO_GROUP && !groups.includes(group)) {
      lost = group;
      group = '';
    }
    // The filter narrows the page to one group or to what's down, so it only shows when that would leave something out.
    const loose = sensors.some((s) => !s.group);
    const narrows = groups.length > 1 || (groups.length === 1 && loose) || (down.length > 0 && down.length < sensors.length);
    if (!narrows || (group === NO_GROUP && !loose)) group = '';
    drawHero();
    $('sn-tools').hidden = !sensors.length && !groups.length;
    filter(narrows, group === DOWN ? 'Down' : group === NO_GROUP ? 'No group' : group);
    $('sn-view').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.view === view));
    const only = group === NO_GROUP ? '' : group; // the one group shown, as the sensors have it
    const shown = sensors.filter((s) => (group === DOWN ? s.status === 'down' : !group || (s.group || '') === only)
      && (!find || [s.name, s.target, s.group, KIND[s.kind], s.note].some((v) => String(v || '').toLowerCase().includes(find))));
    // Every group in its order, the empty ones too unless something is being looked for; sensors with no group go last.
    const by = (group && group !== DOWN ? [only] : [...groups, '']).map((g) => [g, shown.filter((s) => (s.group || '') === g)])
      .filter(([g, list]) => list.length || (g && !find && group !== DOWN));
    $('sn-list').innerHTML = by.map(([g, list]) => `<section class="lgroup" data-name="${esc(g)}">${groups.length ? tools.heading(g, list.length) : ''}`
      + (!list.length ? `<p class="hint lnone">No sensors in this group yet.${can('sensors.edit') ? ` <button type="button" class="link" data-addto="${esc(g)}">Add one</button>` : ''}</p>`
        : view === 'grid' ? `<div class="table">${head()}${list.map(row).join('')}</div>` : `<div class="ltiles sn-tiles">${list.map(card).join('')}</div>`)
      + '</section>').join('')
      || (sensors.length ? '<p class="hint">Nothing matches.</p>' : '');
    if (!find && group !== DOWN) $('sn-list').insertAdjacentHTML('beforeend', tools.out(by.some(([g]) => !g)));
    tools.settle();
    applyCols();
    $('sn-empty').hidden = sensors.length > 0 || groups.length > 0;
    paint($('sn-list'));
  };

  // What the server answers to a read or a change: the sensors and groups in order, the rules, and whether email is set up.
  const take = (answer) => {
    sensors = answer.sensors;
    groups = answer.groups || [];
    rules = answer.rules || [];
    mailReady = !!answer.mail;
    lastCheck = answer.last;
  };
  const refresh = async () => {
    try {
      take(await R.get('/api/sensors'));
      say('sn-problem', '');
    } catch (err) {
      say('sn-problem', err.message);
    }
    // Not under a drag, a column being sized or a name being typed: it catches up on the next read.
    if (!tools.busy() && !sizing && !moving) draw();
  };
  const tools = R.groupTools($('sn-list'), {
    api: '/api/sensors/groups',
    thing: 'sensor',
    problem: 'sn-problem',
    can: () => can('sensors.edit'),
    names: () => groups,
    order: (names) => { groups = names; },
    count: (name) => sensors.filter((s) => s.group === name).length,
    // A sensor dragged into another group, or out of them all: shown there straight away, then saved.
    move: async (id, to) => {
      const s = sensors.find((x) => x.id === id);
      if (!s) return draw();
      s.group = to || null;
      draw();
      try {
        take(await R.send(`/api/sensors/${encodeURIComponent(id)}`, { kind: s.kind, target: s.target, port: s.port ?? null, server: s.server || '', name: s.name, group: to, every: s.every, rule: s.rule }));
        say('sn-problem', '');
      } catch (err) {
        say('sn-problem', err.message);
      }
      draw();
    },
    take,
    draw,
    renamed: (name, to) => {
      if (lost !== name) return;
      group = to;
      draw();
    },
  });

  $('sn-find').addEventListener('input', draw);
  // The filter: a menu of what's down and the groups, each with its colour, to show one of them.
  const filter = R.groupFilter('sn', {
    rows: () => {
      const down = sensors.filter((s) => s.status === 'down').length;
      const loose = sensors.filter((s) => !s.group).length;
      return [['', 'All groups', sensors.length], ...(down ? [[DOWN, 'Down', down, 'var(--red)']] : []),
        ...groups.map((g, i) => [g, g, sensors.filter((s) => s.group === g).length, COLOURS[i % COLOURS.length]]), ...(loose ? [[NO_GROUP, 'No group', loose, '#C9CCDA']] : [])];
    },
    picked: () => group,
    pick: (g) => {
      group = g;
      draw();
    },
  });
  $('sn-newgroup').addEventListener('click', tools.create);
  $('sn-view').addEventListener('click', (e) => {
    const b = e.target.closest('[data-view]');
    if (!b) return;
    view = b.dataset.view;
    store('rabbidlab.sensors.view', view);
    draw();
  });
  $('sn-cols-reset').addEventListener('click', () => {
    cols = plain();
    keepCols();
    draw();
  });

  // A column's right edge, dragged: its share of the width follows the pointer.
  $('sn-list').addEventListener('pointerdown', (e) => {
    const handle = e.target.closest('.sn-size');
    if (!handle) return;
    e.preventDefault();
    const key = handle.dataset.size;
    const from = e.clientX;
    const wide = handle.parentElement.getBoundingClientRect().width;
    const was = cols.w[key];
    sizing = true;
    document.body.classList.add('sn-sizing');
    const move = (m) => {
      cols.w[key] = Math.min(900, Math.max(60, Math.round(was * (wide + m.clientX - from) / wide)));
      applyCols();
    };
    const done = () => {
      window.removeEventListener('pointermove', move);
      sizing = false;
      document.body.classList.remove('sn-sizing');
      keepCols();
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', done, { once: true });
  });
  // A column's heading, dragged onto another: it lands on the side of that one the pointer is over.
  const unmark = () => $('sn-list').querySelectorAll('.drop-left, .drop-right').forEach((el) => el.classList.remove('drop-left', 'drop-right'));
  $('sn-list').addEventListener('dragstart', (e) => {
    const th = e.target.closest?.('.sn-th');
    if (!th) return;
    if (sizing) {
      e.preventDefault();
      return;
    }
    moving = th.dataset.col;
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', moving);
  });
  $('sn-list').addEventListener('dragover', (e) => {
    const th = e.target.closest('.sn-th');
    if (!moving || !th) return;
    e.preventDefault();
    unmark();
    if (th.dataset.col === moving) return;
    const box = th.getBoundingClientRect();
    th.classList.add(e.clientX < box.left + box.width / 2 ? 'drop-left' : 'drop-right');
  });
  $('sn-list').addEventListener('drop', (e) => {
    const th = e.target.closest('.sn-th');
    if (!moving || !th || th.dataset.col === moving) return;
    e.preventDefault();
    const order = cols.order.filter((k) => k !== moving);
    order.splice(order.indexOf(th.dataset.col) + (th.classList.contains('drop-right') ? 1 : 0), 0, moving);
    cols.order = order;
    moving = null;
    keepCols();
    draw();
  });
  $('sn-list').addEventListener('dragend', () => {
    if (!moving) return;
    moving = null;
    unmark();
  });

  // --- One sensor: how it is and what happened to it -------------------------------------------------------------------------

  let showing = null; // the sensor in the dialog
  const EVENT = { down: ['Went down', 's-down'], up: ['Came back', 's-up'], warn: ['Warning', 's-warn'], email: ['Email', 's-new'], problem: ['Email not sent', 's-down'] };
  const fill = (d) => {
    const s = d.sensor;
    showing = s;
    $('sv-title').textContent = s.name;
    $('sv-what').textContent = `${check(s)} · ${where(s)} · every ${{ 30: '30 seconds', 60: 'minute', 300: '5 minutes', 3600: 'hour' }[s.every] || `${s.every} s`} · ${rule(s.rule).name}`;
    $('sv-now').className = `sv-now s-${s.status}`;
    $('sv-state').textContent = s.status === 'down' ? `Down for ${s.since ? long(s.since) : 'a moment'}` : s.status === 'up' ? `Up${s.since ? ` for ${long(s.since)}` : ''}`
      : s.status === 'warn' ? 'Needs attention' : s.status === 'paused' ? 'Paused' : 'Not checked yet';
    const r = rule(s.rule);
    const mailed = s.status !== 'down' ? '' : d.alerted ? `Emailed ${R.when(d.alerted)}` : !mailReady ? 'Email is not set up'
      : `${r.name} ${r.after < 0 ? 'never emails' : r.after ? `emails after ${r.after} minutes` : 'emails right away'}`;
    $('sv-note').textContent = s.status === 'paused' ? 'It is not checked, and nobody is emailed about it.'
      : `${[s.note, s.checked ? `Checked ${short(s.checked)} ago` : '', mailed].filter(Boolean).join('. ')}.`;
    $('sv-ms').textContent = s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : s.ms == null ? '-' : ms(s.ms)) : '-';
    $('sv-up24').textContent = percent(d.up24) || '-';
    $('sv-up7').textContent = percent(d.up7) || '-';
    $('sv-line').innerHTML = line(s.recent, s.status);
    $('sv-line').firstElementChild.className = `sn-chart s-${s.status}`;
    $('sv-events').hidden = !d.events.length;
    $('sv-events').innerHTML = d.events.slice(0, 40).map((e) => `<div class="row still ${(EVENT[e.what] || ['', ''])[1]}"><i></i><time>${esc(R.when(e.at))}</time>`
      + `<span class="t"><b>${esc((EVENT[e.what] || [e.what])[0])}</b><span>${esc(e.text)}</span></span></div>`).join('');
    $('sv-pause').textContent = s.paused ? 'Start again' : 'Pause';
    paint($('sv-line'));
  };
  const open = async (id) => {
    say('sv-problem', '');
    try {
      fill(await R.get(`/api/sensors/${encodeURIComponent(id)}`));
      if (!$('sview-dialog').open) $('sview-dialog').showModal();
    } catch (err) {
      say('sn-problem', err.message);
    }
  };
  // Pause and Check now answer with every sensor; the dialog then reads its own again.
  const act = async (path, body) => {
    if (!showing) return;
    const id = showing.id;
    try {
      take(await R.send(`/api/sensors/${encodeURIComponent(id)}/${path}`, body));
      draw();
      await open(id);
    } catch (err) {
      say('sv-problem', err.message);
    }
  };
  document.addEventListener('click', (e) => {
    const b = e.target.closest('#page-sensors [data-open]');
    if (!b || e.target.closest('.sn-size')) return;
    e.preventDefault();
    open(b.dataset.open);
  });
  $('sv-close').addEventListener('click', () => $('sview-dialog').close());
  $('sv-pause').addEventListener('click', () => act('pause', { paused: !showing.paused }));
  $('sv-check').addEventListener('click', async () => {
    $('sv-check').disabled = true;
    await act('check', {});
    $('sv-check').disabled = false;
  });
  $('sv-change').addEventListener('click', () => {
    $('sview-dialog').close();
    edit(showing);
  });

  // --- Adding and changing one: the kinds of check down the left, the open one's fields beside them -------------------------

  let editing = null; // the sensor being changed, or null for a new one
  let kind = 'ping';
  const vals = {}; // what has been typed, kept while the kind changes
  const GROUPS = [['answer', 'Is it answering'], ['health', 'Health']];
  const TYPES = [
    { id: 'ping', g: 'answer', icon: 'activity', about: 'Answers ping.' },
    { id: 'port', g: 'answer', icon: 'plug', about: 'A TCP port is open.' },
    { id: 'web', g: 'answer', icon: 'globe', about: 'The page answers, with what you expect on it.' },
    { id: 'dns', g: 'answer', icon: 'search', about: 'A name resolves, to the address it should.' },
    { id: 'cert', g: 'answer', icon: 'lock', about: 'Days until a certificate expires.' },
    { id: 'hw', g: 'health', icon: 'server', about: 'iDRAC, iLO or Supermicro over Redfish: power supplies, fans, temperature and disks.', signs: true },
  ];
  // The fields, in the console's form markup; each keeps its value in vals under its key.
  const wide = (on) => (on ? ' class="wide"' : '');
  const field = (key, label, o = {}) => `<div${wide(o.wide)}><label for="sd-f-${key}">${label}</label><input id="sd-f-${key}" data-f="${key}"`
    + `${o.type ? ` type="${o.type}"` : ''}${o.num ? ' inputmode="numeric"' : ''} maxlength="${o.max || 300}" placeholder="${esc(o.ph || '')}" value="${esc(vals[key] ?? '')}" autocomplete="off" spellcheck="false"></div>`;
  const choice = (key, label, items, o = {}) => `<div${wide(o.wide)}><label for="sd-f-${key}">${label}</label><select id="sd-f-${key}" data-f="${key}">`
    + items.map(([v, text]) => `<option value="${esc(v)}"${String(vals[key] ?? o.value) === String(v) ? ' selected' : ''}>${esc(text)}</option>`).join('') + '</select></div>';
  const picks = (key, label, items) => `<div class="wide"><label>${label}</label><div class="seg sn-seg">`
    + items.map(([v, text]) => `<button type="button" data-f="${key}" data-v="${esc(v)}" aria-pressed="${(vals[key] || '') === v}">${esc(text)}</button>`).join('') + '</div></div>';
  // Credentials: a saved set picked, or a new one typed and saved with the sensor.
  const NEW_LOGIN = ' new';
  let logins = [];
  const signIn = (role) => {
    const opts = [...logins.map((l) => [l.id, `${l.name}${l.username && l.username !== l.name ? ` (${l.username})` : ''} · ${l.used} sensor${l.used === 1 ? '' : 's'}`]), [NEW_LOGIN, 'New credentials…']];
    if (!opts.some(([v]) => v === vals.login)) vals.login = logins[0]?.id || NEW_LOGIN;
    return '<div class="sd-div wide"><b>Sign in</b><i></i></div>' + choice('login', 'Credentials', opts, { wide: true })
      + (vals.login === NEW_LOGIN ? field('username', 'Username', { max: 120, ph: 'rabbidlab-ro' }) + field('password', 'Password', { type: 'password', max: 500 })
        + field('loginName', 'Save them as', { wide: true, max: 60, ph: 'iDRAC read-only' }) : '')
      + `<p class="hint">Stored encrypted on this server and only sent to this address. ${role}</p>`;
  };
  const FIELDS = {
    ping: () => field('target', 'Address', { wide: true, ph: '192.168.2.23 or nas.example.com' }),
    port: () => field('target', 'Host', { ph: '192.168.2.23 or nas.example.com' }) + field('port', 'Port', { num: true, max: 5, ph: '445, 636, 3389…' }),
    web: () => field('target', 'Address', { wide: true, ph: 'https://192.168.2.23' })
      + picks('expect', 'Counts as up when', [['', 'It answers'], ['ok', 'Status 200 to 399'], ['text', 'The page has this text']])
      + (vals.expect === 'text' ? field('text', 'Text on the page', { wide: true, max: 200, ph: 'Words the page shows when it works' }) : ''),
    dns: () => field('target', 'Name to look up', { ph: 'nas.example.com' }) + field('address', 'Should resolve to', { ph: 'Any address' })
      + field('server', 'DNS server to ask', { wide: true, max: 250, ph: 'The one this server uses' }),
    cert: () => field('target', 'Host', { ph: 'nas.example.com' }) + field('port', 'Port', { num: true, max: 5, ph: '443' })
      + choice('warn', 'Warn when it has less than', [[14, '14 days'], [21, '21 days'], [30, '30 days'], [60, '60 days']], { value: 21 }),
    hw: () => field('target', 'iDRAC, iLO or BMC address', { wide: true, ph: '192.168.2.7' }) + signIn('An iDRAC or iLO user with the Read Only role is enough.'),
  };
  const signs = () => TYPES.find((t) => t.id === kind)?.signs;
  const drawFields = () => {
    $('sd-fields').innerHTML = FIELDS[kind]();
    $('sd-out').innerHTML = signs() ? '<p class="hint">Check now signs in and shows what it can watch.</p>' : '';
  };
  const setKind = (k) => {
    kind = TYPES.some((t) => t.id === k) ? k : 'ping';
    const t = TYPES.find((x) => x.id === kind);
    $('sd-types').innerHTML = GROUPS.map(([g, name]) => `<p class="rail-label">${name}</p>` + TYPES.filter((x) => x.g === g)
      .map((x) => `<a href="#" data-kind="${x.id}"${x.id === kind ? ' aria-current="true"' : ''}>${svg(x.icon)}<span>${KIND[x.id]}</span>${x.signs ? svg('key', 'sd-key') : ''}</a>`).join('')).join('');
    $('sd-kind-name').textContent = KIND[kind];
    $('sd-kind-about').textContent = t.about;
    // A certificate doesn't change by the minute, nor hardware.
    if (!editing) $('sd-every').value = kind === 'cert' ? '3600' : kind === 'hw' ? '300' : '60';
    drawFields();
  };
  const pickGroup = () => {
    const fresh = $('sd-group').value === NEW_GROUP;
    $('sd-group-new').hidden = !fresh;
    if (fresh) $('sd-group-new').focus();
  };
  const num = (v) => (String(v ?? '').trim() ? Number(v) : null);
  const form = () => ({ kind, target: vals.target || '', port: kind === 'port' || kind === 'cert' ? num(vals.port) : null, server: kind === 'dns' ? vals.server || '' : '',
    expect: kind === 'web' ? vals.expect || '' : '', text: kind === 'web' && vals.expect === 'text' ? vals.text || '' : '', address: kind === 'dns' ? vals.address || '' : '',
    warnDays: kind === 'cert' ? num(vals.warn ?? 21) : null, login: signs() && vals.login !== NEW_LOGIN ? vals.login : null,
    newLogin: signs() && vals.login === NEW_LOGIN ? { name: vals.loginName || '', username: vals.username || '', password: vals.password || '' } : null,
    watch: kind === 'hw' ? vals.watch || null : null, pin: signs() ? vals.pin || null : null, name: $('sd-name').value,
    group: $('sd-group').value === NEW_GROUP ? $('sd-group-new').value : $('sd-group').value, every: Number($('sd-every').value), rule: $('sd-rule').value });
  // A sensor to change, or null for a new one, which starts in the group asked for or the one being looked at.
  const edit = async (s, into) => {
    editing = s || null;
    Object.keys(vals).forEach((k) => delete vals[k]);
    if (s) Object.assign(vals, { target: s.target, port: s.port ?? '', server: s.server || '', expect: s.expect || '', text: s.text || '', address: s.address || '', warn: s.warnDays ?? 21,
      login: s.login || undefined, watch: s.watch || null, pin: s.pin || null });
    logins = await R.get('/api/sensors/logins').catch(() => []);
    $('sd-title').textContent = s ? `Change ${s.name}` : 'Add a sensor';
    $('sd-name').value = s?.name || '';
    $('sd-group').innerHTML = '<option value="">No group</option>' + groups.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join('') + `<option value="${NEW_GROUP}">New group…</option>`;
    $('sd-group').value = s ? s.group || '' : into ?? (group === DOWN || group === NO_GROUP ? '' : group);
    $('sd-group-new').value = '';
    pickGroup();
    $('sd-rule').innerHTML = rules.map((r) => `<option value="${esc(r.id)}">${esc(r.name)}: ${r.after < 0 ? 'never emails' : r.after ? `emails after ${r.after} minutes` : 'emails right away'}</option>`).join('');
    $('sd-rule').value = s?.rule || 'normal';
    $('sd-every').value = String(s?.every || 60);
    setKind(s?.kind || 'ping');
    $('sd-delete').hidden = !s;
    $('sd-save').textContent = s ? 'Save' : 'Add the sensor';
    say('sd-problem', '');
    $('sensor-dialog').showModal();
    $('sd-fields').querySelector('input')?.focus();
  };
  $('sd-types').addEventListener('click', (e) => {
    const a = e.target.closest('[data-kind]');
    if (!a) return;
    e.preventDefault();
    setKind(a.dataset.kind);
    $('sd-fields').querySelector('input')?.focus();
  });
  const keep = (e) => { if (e.target.dataset.f) vals[e.target.dataset.f] = e.target.value; };
  $('sd-fields').addEventListener('input', keep);
  $('sd-fields').addEventListener('change', (e) => {
    keep(e);
    if (e.target.dataset.f === 'login') drawFields();
  });
  $('sd-fields').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-v]');
    if (!b) return;
    vals[b.dataset.f] = b.dataset.v;
    drawFields();
  });
  $('sd-group').addEventListener('change', pickGroup);
  $('sd-cancel').addEventListener('click', () => $('sensor-dialog').close());
  // What Check now found, as a status card: what answered and how, or what went wrong.
  const found = (f) => {
    const answered = f.ms == null ? '' : `Answered in ${ms(f.ms)}`;
    const title = !f.ok ? f.note || 'No answer' : kind === 'port' ? `Port ${vals.port} is open` : kind === 'dns' || kind === 'cert' || kind === 'hw' ? f.note || 'It answered' : answered || 'It answered';
    const line = !f.ok ? [f.detail, 'You can still add it.'].filter(Boolean).join(' · ')
      : [kind === 'web' ? f.note : kind === 'port' || kind === 'dns' ? answered : '', f.detail].filter(Boolean).join(' · ');
    const look = !f.ok ? ' bad' : f.warn ? ' ember' : '';
    return `<div class="ustatus"><span class="big-ic${look}">${svg(f.ok && !f.warn ? 'check' : 'alert')}</span><div class="ustatus-text"><b>${esc(title)}</b><span>${esc(line)}</span></div></div>`;
  };
  const STATE = { ok: 'OK', warn: 'Warning', bad: 'Critical' };
  const watchRows = (parts) => `<div class="rows">${parts.map((p) => `<label class="row sd-watch"><input type="checkbox" data-watch="${esc(p.key)}"${!vals.watch || vals.watch.includes(p.key) ? ' checked' : ''}>`
    + `<span class="t"><b>${esc(p.name)}</b><span>${esc(p.now)}</span></span><span class="sd-now ${p.state}">${STATE[p.state] || ''}</span></label>`).join('')}</div>`;
  const pinNote = (pin) => {
    const bytes = pin.split(':');
    const short = bytes.length > 6 ? `${bytes.slice(0, 4).join(':')}:…:${bytes.slice(-2).join(':')}` : pin;
    return `<p class="sd-pin">${svg('alert')}<span>It has a self-signed certificate. Rabbid Lab trusts this one, <code title="${esc(pin)}">SHA-256 ${esc(short)}</code>, and warns you if it changes.</span></p>`;
  };
  const foundAll = (f) => {
    if (signs()) vals.pin = f.pin || null;
    return (f.ok && f.title ? found({ ...f, note: f.title }) : found(f)) + (f.parts?.length ? watchRows(f.parts) : '') + (f.pin ? pinNote(f.pin) : '');
  };
  $('sd-out').addEventListener('change', (e) => {
    if (!e.target.dataset.watch) return;
    vals.watch = [...$('sd-out').querySelectorAll('[data-watch]')].filter((x) => x.checked).map((x) => x.dataset.watch);
  });
  $('sd-try').addEventListener('click', async () => {
    say('sd-problem', '');
    $('sd-out').innerHTML = '';
    R.busy($('sd-pane'), 'Checking…');
    $('sd-try').disabled = $('sd-save').disabled = true;
    try {
      $('sd-out').innerHTML = foundAll(await R.send('/api/sensors/try', form()));
    } catch (err) {
      say('sd-problem', err.message);
    } finally {
      R.busy($('sd-pane'), '');
      $('sd-try').disabled = $('sd-save').disabled = false;
    }
  });
  $('sd-save').addEventListener('click', async () => {
    $('sd-save').disabled = true;
    try {
      take(await R.send(editing ? `/api/sensors/${encodeURIComponent(editing.id)}` : '/api/sensors', form()));
      $('sensor-dialog').close();
      draw();
    } catch (err) {
      say('sd-problem', err.message);
    } finally {
      $('sd-save').disabled = false;
    }
  });
  $('sd-delete').addEventListener('click', () => {
    if (!editing) return;
    const gone = editing;
    R.sure(`Delete the sensor ${gone.name}?`, 'Its history goes with it.', 'Delete', async () => {
      try {
        take(await R.send(`/api/sensors/${encodeURIComponent(gone.id)}/delete`, {}));
        $('sensor-dialog').close();
        draw();
      } catch (err) {
        say('sd-problem', err.message);
      }
    });
  });

  $('sn-add').addEventListener('click', () => edit(null));
  $('sn-add-first').addEventListener('click', () => edit(null));
  $('sn-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-addto]');
    if (b) edit(null, b.dataset.addto);
  });

  // --- Alert rules: a change is saved as it's made -------------------------------------------------------------------------------

  let ruleId = 'normal';
  const AFTER = [[0, 'Right away'], [1, '1 min'], [5, '5 min'], [10, '10 min'], [30, '30 min'], [60, '1 hour'], [-1, 'Never']];
  const AGAIN = [[0, 'Email once'], [60, 'Again every hour'], [360, 'Again every 6 hours'], [1440, 'Again every day']];
  const BACK = [[true, 'Email'], [false, 'No email']];
  const drawRules = () => {
    const r = rule(ruleId);
    const about = (x) => (x.after < 0 ? 'Never emails. Shows on the page only.' : `Emails ${x.after ? `after ${x.after === 60 ? 'an hour' : `${x.after} minutes`} down` : 'right away'}${x.again ? `, and again every ${x.again === 60 ? 'hour' : x.again === 360 ? '6 hours' : 'day'}` : ''}.`);
    $('rl-cards').innerHTML = rules.map((x) => `<button type="button" class="rcard" data-rule="${esc(x.id)}" data-color="${esc(x.color)}" aria-pressed="${x.id === ruleId}">`
      + `<b><i></i>${esc(x.name)}</b><p>${about(x)}</p><span class="n">${svg('activity')}${x.used} sensor${x.used === 1 ? '' : 's'}</span></button>`).join('');
    const segs = (items, value) => items.map(([v, text]) => `<button type="button" data-value="${v}" aria-pressed="${v === value}"${r.after < 0 && items !== AFTER ? ' disabled' : ''}>${text}</button>`).join('');
    $('rl-after').innerHTML = segs(AFTER, r.after);
    $('rl-again').innerHTML = segs(AGAIN, r.again);
    $('rl-back').innerHTML = segs(BACK, r.back);
    $('rl-note').textContent = `A sensor turns red on the page after 2 failed checks in a row. The email waits for the time above, so something that comes straight back emails nobody.${mailReady ? '' : ' Nobody is emailed until Email settings are filled in.'}`;
    paint($('rl-cards'));
  };
  const saveRule = async (change) => {
    const r = rule(ruleId);
    try {
      take(await R.send(`/api/sensors/rules/${encodeURIComponent(ruleId)}`, { after: r.after, again: r.again, back: r.back, ...change }));
      say('rl-problem', '');
    } catch (err) {
      say('rl-problem', err.message);
    }
    drawRules();
    draw();
  };
  const value = (e) => e.target.closest('button[data-value]')?.dataset.value;
  $('rl-cards').addEventListener('click', (e) => {
    const b = e.target.closest('[data-rule]');
    if (!b) return;
    ruleId = b.dataset.rule;
    drawRules();
  });
  $('rl-after').addEventListener('click', (e) => { if (value(e) != null) saveRule({ after: Number(value(e)) }); });
  $('rl-again').addEventListener('click', (e) => { if (value(e) != null) saveRule({ again: Number(value(e)) }); });
  $('rl-back').addEventListener('click', (e) => { if (value(e) != null) saveRule({ back: value(e) === 'true' }); });
  $('rl-close').addEventListener('click', () => $('rules-dialog').close());
  $('sn-rules').addEventListener('click', () => {
    say('rl-problem', '');
    drawRules();
    $('rules-dialog').showModal();
  });

  // --- Email: the mail server and who gets the alerts ---------------------------------------------------------------------------

  let provider = 'gmail';
  let kept = null; // what's saved, for whether a password is stored
  const HELP = {
    gmail: 'Gmail needs an app password, not your usual one: Google Account, Security, 2-Step Verification, App passwords.',
    m365: 'In Microsoft Entra, register an app, give it the Mail.Send application permission with admin consent, and make a client secret. From has to be a mailbox in that tenant.',
    smtp: 'Any mail server or relay that takes STARTTLS on port 587: SendGrid, Mailgun, Amazon SES, Brevo, or one of your own. A personal Outlook.com account can\'t be used: Microsoft only lets those send after a sign-in in a browser.',
  };
  const setProvider = (p) => {
    provider = p;
    $('ml-provider').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.provider === p));
    $('mail-dialog').querySelectorAll('[data-for]').forEach((el) => { el.hidden = !el.dataset.for.split(' ').includes(p); });
    $('ml-user-label').textContent = p === 'gmail' ? 'Gmail address' : 'Username';
    $('ml-password-label').textContent = p === 'gmail' ? 'App password' : 'Password';
    $('ml-password').placeholder = kept?.hasPassword ? 'Stored. Type a new one to change it.' : '';
    $('ml-secret').placeholder = kept?.hasSecret ? 'Stored. Type a new one to change it.' : '';
    $('ml-help').textContent = HELP[p];
  };
  const mailForm = () => {
    const to = $('ml-to').value.split(/[,;\s]+/).filter(Boolean);
    if (provider === 'm365') return { provider: 'm365', tenant: $('ml-tenant').value, clientId: $('ml-client').value, clientSecret: $('ml-secret').value, from: $('ml-from').value, to };
    if (provider === 'gmail') return { provider: 'smtp', host: 'smtp.gmail.com', port: 587, security: 'starttls', username: $('ml-user').value, password: $('ml-password').value, from: $('ml-user').value, to };
    return { provider: 'smtp', host: $('ml-host').value, port: Number($('ml-port').value) || 587, security: $('ml-security').value, username: $('ml-user').value, password: $('ml-password').value, from: $('ml-from').value, to };
  };
  const saveMail = async () => {
    kept = await R.send('/api/mail', mailForm());
    mailReady = !!kept.ready;
    $('ml-password').value = '';
    $('ml-secret').value = '';
    setProvider(provider);
  };
  const openMail = async () => {
    say('ml-problem', '');
    say('ml-done', '');
    try {
      kept = await R.get('/api/mail');
    } catch (err) {
      say('sn-problem', err.message);
      return;
    }
    $('ml-host').value = kept.host || '';
    $('ml-port').value = kept.port || 587;
    $('ml-security').value = kept.security || 'starttls';
    $('ml-user').value = kept.username || '';
    $('ml-password').value = '';
    $('ml-tenant').value = kept.tenant || '';
    $('ml-client').value = kept.clientId || '';
    $('ml-secret').value = '';
    $('ml-from').value = kept.from || '';
    $('ml-to').value = (kept.to || []).join(', ');
    setProvider(kept.provider === 'm365' ? 'm365' : !kept.host || kept.host === 'smtp.gmail.com' ? 'gmail' : 'smtp');
    if (!$('mail-dialog').open) $('mail-dialog').showModal();
  };
  $('ml-provider').addEventListener('click', (e) => {
    const b = e.target.closest('[data-provider]');
    if (b) setProvider(b.dataset.provider);
  });
  $('ml-cancel').addEventListener('click', () => $('mail-dialog').close());
  $('ml-save').addEventListener('click', async () => {
    say('ml-done', '');
    try {
      await saveMail();
      say('ml-problem', '');
      $('mail-dialog').close();
      draw();
    } catch (err) {
      say('ml-problem', err.message);
    }
  });
  // The test goes to the addresses in the box, so it saves first.
  $('ml-test').addEventListener('click', async () => {
    say('ml-done', '');
    say('ml-problem', '');
    R.busy($('mail-dialog').querySelector('.form'), 'Sending a test email…');
    $('ml-test').disabled = $('ml-save').disabled = true;
    try {
      await saveMail();
      await R.send('/api/mail/test', {});
      say('ml-done', `Sent to ${kept.to.join(', ')}. Check the inbox, and the junk folder.`);
    } catch (err) {
      say('ml-problem', err.message);
    } finally {
      R.busy($('mail-dialog').querySelector('.form'), '');
      $('ml-test').disabled = $('ml-save').disabled = false;
    }
  });
  $('sn-mail').addEventListener('click', openMail);
  $('rl-mail').addEventListener('click', () => {
    $('rules-dialog').close();
    openMail();
  });

  // --- Showing the page --------------------------------------------------------------------------------------------------------------

  document.addEventListener('rabbidlab:show', (e) => {
    clearInterval(timer);
    if (!can('sensors.view')) return;
    const here = e.detail[0] === 'sensors';
    if (here) refresh();
    // On the page it keeps up with the checks; elsewhere it only needs the rail's dot.
    timer = setInterval(refresh, here ? 10000 : 60000);
  });
  document.addEventListener('rabbidlab:me', () => { if (can('sensors.view') && R.where()[0] !== 'sensors') refresh(); });
})();
