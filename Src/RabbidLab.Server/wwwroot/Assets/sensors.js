// Sensors: #sensors, the things Rabbid Lab checks on a timer. The hero says how the lab is doing and its squares are the
// sensors; below, the sensors in their groups as cards or as a table whose columns can be resized and reordered.
// console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can } = R;
  const COLOURS = ['#FF8A3D', '#F2BE6B', '#B07CE8', '#6FA8FF', '#3CC7C7', '#F06BB4', '#7C83FF']; // a group's colour; green and red are left to up and down
  const KIND = { ping: 'Ping', port: 'Port', web: 'Web page', dns: 'DNS lookup', cert: 'Certificate', hw: 'Server hardware', nas: 'Storage', vmware: 'VMware', snmp: 'SNMP', svc: 'Windows service', disk: 'Disk space' };
  const WORD = { up: 'Up', down: 'Down', warn: 'Warning', paused: 'Paused', new: 'Checking' };
  const DOWN = ' down'; // the filter's choice of everything that's down: no group's name starts with a space
  const NO_GROUP = ' none'; // and its choice of sensors with no group
  const NEW_GROUP = ' new';
  let sensors = [];
  let groups = [];
  let rules = [];
  let defaultRule = 'normal'; // the saved alert settings new sensors start with
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
    : s.kind === 'cert' ? days(s.note) : s.status === 'warn' ? s.note || 'Warning' : ['hw', 'nas', 'vmware', 'snmp', 'svc', 'disk'].includes(s.kind) ? s.note || 'All OK' : s.ms == null ? 'Up' : ms(s.ms));
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
        : down.length && !mailReady ? 'Nobody is emailed until a mail server is set up in Settings, Notifications.' : '';
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
  const rule = (id) => (id === 'custom' ? { name: 'Custom', color: '#8A8FA8' }
    : rules.find((r) => r.id === id) || rules.find((r) => r.id === defaultRule) || { name: id, color: '#8A8FA8' });
  const CELL = {
    state: (s) => `<span class="state ${s.status}">${s.status === 'down' && s.since ? `Down ${short(s.since)}` : WORD[s.status] || s.status}</span>`,
    name: (s) => `<div class="nm"><span class="lic letter" data-color="${colour(s)}"><b>${esc(letters(s.name)[0])}</b></span><b>${esc(s.name)}</b></div>`,
    check: (s) => `<span>${esc(check(s))}</span>`,
    where: (s) => `<span class="dim" title="${esc(where(s))}">${esc(where(s))}</span>`,
    reply: (s) => `<span class="num">${esc(s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : ['hw', 'nas', 'vmware', 'snmp', 'svc', 'disk'].includes(s.kind) ? s.note || 'All OK' : s.ms == null ? '' : ms(s.ms)) : '')}</span>`,
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
    defaultRule = answer.default || 'normal';
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
  const GROUPS = [['answer', 'Is it answering'], ['health', 'Health'], ['seat', 'On a seat']];
  const TYPES = [
    { id: 'ping', g: 'answer', icon: 'activity', about: 'Answers ping.' },
    { id: 'port', g: 'answer', icon: 'plug', about: 'A TCP port is open.' },
    { id: 'web', g: 'answer', icon: 'globe', about: 'The page answers, with what you expect on it.' },
    { id: 'dns', g: 'answer', icon: 'search', about: 'A name resolves, to the address it should.' },
    { id: 'cert', g: 'answer', icon: 'lock', about: 'Days until a certificate expires.' },
    { id: 'hw', g: 'health', icon: 'server', about: 'iDRAC, iLO or Supermicro over Redfish: power supplies, fans, temperature and disks.', signs: true },
    { id: 'nas', g: 'health', icon: 'disk', about: 'TrueNAS or Synology: pool health, disks and free space.', signs: true },
    { id: 'vmware', g: 'health', icon: 'monitor', about: 'ESXi or vCenter: hosts connected, datastore free space and triggered alarms.', signs: true },
    { id: 'snmp', g: 'health', icon: 'org', about: 'Switches, UPS units and printers: one value you pick, such as a UPS battery or a switch port.', signs: true },
    { id: 'svc', g: 'seat', icon: 'gear', about: 'A Windows service is running, checked by the client on that computer.' },
    { id: 'disk', g: 'seat', icon: 'disk', about: 'Free space on a drive of a computer in Seats, read by its client.' },
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
  // The kind of credentials this sensor takes: an API key for TrueNAS, a community or v3 user for SNMP, else a username and password.
  const loginKind = () => (kind === 'nas' && vals.product !== 'synology' ? 'key' : kind === 'snmp' ? (vals.snmpVersion === 'v3' ? 'snmpv3' : 'community') : 'user');
  const NEW_NAME = { user: 'New credentials…', key: 'New API key…', community: 'New community…', snmpv3: 'New SNMPv3 user…' };
  const signIn = (role) => {
    const k = loginKind();
    const opts = [...logins.filter((l) => l.kind === k).map((l) => [l.id, `${l.name}${l.username && l.username !== l.name ? ` (${l.username})` : ''} · ${l.used} sensor${l.used === 1 ? '' : 's'}`]), [NEW_LOGIN, NEW_NAME[k]]];
    if (!opts.some(([v]) => v === vals.login)) vals.login = opts[0][0];
    const save = (ph) => field('loginName', k === 'user' || k === 'snmpv3' ? 'Save them as' : 'Save it as', { wide: true, max: 60, ph });
    const typed = {
      user: () => field('username', 'Username', { max: 120, ph: 'rabbidlab-ro' }) + field('password', 'Password', { type: 'password', max: 500 })
        + save({ nas: 'DSM monitor', vmware: 'vSphere read-only' }[kind] || 'iDRAC read-only'),
      key: () => field('password', 'API key', { type: 'password', wide: true, max: 500 }) + save('TrueNAS API key'),
      community: () => field('password', 'Community', { type: 'password', wide: true, max: 500 }) + save('SNMP community'),
      snmpv3: () => field('username', 'Username', { max: 120, ph: 'rabbidlab' }) + choice('auth', 'Authentication', [['SHA-256', 'SHA-256'], ['SHA-1', 'SHA-1']], { value: 'SHA-256' })
        + field('password', 'Auth password', { type: 'password', max: 500 }) + choice('privacy', 'Privacy', [['AES-128', 'AES-128'], ['None', 'None']], { value: 'AES-128' })
        + (vals.privacy === 'None' ? '' : field('privacyPassword', 'Privacy password', { type: 'password', wide: true, max: 500 })) + save('SNMPv3 monitor'),
    };
    return '<div class="sd-div wide"><b>Sign in</b><i></i></div>' + choice('login', 'Credentials', opts, { wide: true })
      + (vals.login === NEW_LOGIN ? typed[k]() : '')
      + `<p class="hint">${k === 'community' ? 'SNMP v2c sends the community in clear text. Use v3 where the device has it. ' : ''}Stored encrypted on this server and only sent to this address.${role ? ` ${role}` : ''}</p>`;
  };
  // The computers in Seats, for the checks their client makes.
  let seats = [];
  const SEAT_HINT = '<p class="hint">Checked by the Rabbid Lab client on that computer, which runs as SYSTEM. No credentials needed.</p>';
  const seatPick = () => {
    if (!seats.length) return '<p class="hint">No computers in Seats yet. Add one under Seats, then pick it here.</p>';
    if (!seats.some((c) => c.id === vals.seat)) vals.seat = seats[0].id;
    return choice('seat', 'Computer', seats.map((c) => [c.id, `${c.computer}${c.connected ? '' : ' (client not connected)'}`]), { wide: true });
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
    svc: () => seatPick() + field('service', 'Service', { wide: true, max: 256, ph: 'Spooler or Print Spooler' }) + SEAT_HINT,
    disk: () => seatPick() + field('drive', 'Drive', { max: 3, ph: 'C' })
      + choice('warnFree', 'Warn when free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: 10 }) + SEAT_HINT,
    snmp: () => field('target', 'Address', { wide: true, ph: '192.168.2.30 or ups.example.com' })
      + choice('value', 'Value to watch', [['uptime', 'Uptime: it answers'], ['ups', 'UPS battery and power'], ['iface', 'Interface up'], ['oid', 'Custom OID']], { value: 'uptime', wide: true })
      + (vals.value === 'iface' ? field('iface', 'Interface', { wide: true, max: 100, ph: 'Port 5 or eth0' }) + '<p class="hint">As the device names it. When the name is not found, Check now lists the names it has.</p>' : '')
      + (vals.value === 'oid' ? field('oid', 'OID', { wide: true, max: 300, ph: '1.3.6.1.2.1.1.3.0' }) : '')
      + picks('snmpVersion', 'Version', [['', 'v2c'], ['v3', 'v3']]) + signIn(''),
    vmware: () => field('target', 'ESXi or vCenter address', { wide: true, ph: 'vcenter.example.com' }) + signIn('A vSphere user with the Read-only role is enough.')
      + choice('warnFree', 'Warn when datastore free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: 10 }),
    nas: () => picks('product', 'Product', [['', 'TrueNAS'], ['synology', 'Synology']])
      + field('target', 'Address', { wide: true, ph: vals.product === 'synology' ? 'backup.example.com (port 5001 unless you add another)' : 'truenas.example.com' })
      + (vals.product === 'synology' ? signIn('DSM only gives storage details to an account in the administrators group. Turn off 2-step verification for it.')
        : signIn('Make one in TrueNAS under Credentials, API Keys.'))
      + choice('warnFree', 'Warn when free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: 10 }),
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
    if (!editing) $('sd-every').value = kind === 'cert' ? '3600' : (signs() && kind !== 'snmp') || kind === 'disk' ? '300' : '60';
    drawFields();
  };
  const pickGroup = () => {
    const fresh = $('sd-group').value === NEW_GROUP;
    $('sd-group-new').hidden = !fresh;
    if (fresh) $('sd-group-new').focus();
  };
  const num = (v) => (String(v ?? '').trim() ? Number(v) : null);
  const onSeat = () => kind === 'svc' || kind === 'disk';
  const form = () => ({ kind, target: onSeat() ? seats.find((c) => c.id === vals.seat)?.computer || '' : vals.target || '',
    seat: onSeat() ? vals.seat || '' : null, service: kind === 'svc' ? vals.service || '' : null, drive: kind === 'disk' ? vals.drive || '' : null, port: kind === 'port' || kind === 'cert' ? num(vals.port) : null, server: kind === 'dns' ? vals.server || '' : '',
    expect: kind === 'web' ? vals.expect || '' : '', text: kind === 'web' && vals.expect === 'text' ? vals.text || '' : '', address: kind === 'dns' ? vals.address || '' : '',
    warnDays: kind === 'cert' ? num(vals.warn ?? 21) : null, login: signs() && vals.login !== NEW_LOGIN ? vals.login : null,
    newLogin: signs() && vals.login === NEW_LOGIN ? { name: vals.loginName || '', username: vals.username || '', password: vals.password || '', kind: loginKind(),
      auth: vals.auth || 'SHA-256', privacy: vals.privacy || 'AES-128', privacyPassword: vals.privacyPassword || '' } : null,
    value: kind === 'snmp' ? vals.value || 'uptime' : null, oid: kind === 'snmp' ? vals.oid || '' : null, interface: kind === 'snmp' ? vals.iface || '' : null,
    watch: signs() ? vals.watch || null : null, pin: signs() ? vals.pin || null : null,
    product: kind === 'nas' ? vals.product || '' : null, warnFree: ['nas', 'vmware', 'disk'].includes(kind) ? num(vals.warnFree ?? 10) : null, name: $('sd-name').value,
    group: $('sd-group').value === NEW_GROUP ? $('sd-group-new').value : $('sd-group').value, every: Number($('sd-every').value), rule: vals.rule || defaultRule, custom: vals.rule === 'custom' ? vals.custom : null });
  // A sensor to change, or null for a new one, which starts in the group asked for or the one being looked at.
  const edit = async (s, into) => {
    editing = s || null;
    Object.keys(vals).forEach((k) => delete vals[k]);
    if (s) Object.assign(vals, { target: s.target, port: s.port ?? '', server: s.server || '', expect: s.expect || '', text: s.text || '', address: s.address || '', warn: s.warnDays ?? 21,
      login: s.login || undefined, watch: s.watch || null, pin: s.pin || null, product: s.product === 'synology' ? 'synology' : '', warnFree: s.warnFree ?? 10, value: s.value || 'uptime', oid: s.oid || '', iface: s.interface || '', seat: s.seat || undefined, service: s.service || '', drive: s.drive || '' });
    logins = await R.get('/api/sensors/logins').catch(() => []);
    seats = await R.get('/api/sensors/seats').catch(() => []);
    if (s?.kind === 'snmp' && logins.find((l) => l.id === s.login)?.kind === 'snmpv3') vals.snmpVersion = 'v3';
    $('sd-title').textContent = s ? `Change ${s.name}` : 'Add a sensor';
    $('sd-name').value = s?.name || '';
    $('sd-group').innerHTML = '<option value="">No group</option>' + groups.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join('') + `<option value="${NEW_GROUP}">New group…</option>`;
    $('sd-group').value = s ? s.group || '' : into ?? (group === DOWN || group === NO_GROUP ? '' : group);
    $('sd-group-new').value = '';
    pickGroup();
    vals.rule = s ? s.rule : defaultRule;
    vals.custom = s?.custom ? { after: s.custom.after, again: s.custom.again, back: s.custom.back } : { after: 10, again: 0, back: true };
    drawAlerts();
    $('sd-every').value = String(s?.every || 60);
    setKind(s?.kind || 'ping');
    $('sd-delete').hidden = !s;
    $('sd-dev').hidden = !!s;
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
    if (['login', 'value', 'privacy'].includes(e.target.dataset.f)) drawFields();
  });
  $('sd-fields').addEventListener('click', (e) => {
    const b = e.target.closest('button[data-v]');
    if (!b) return;
    vals[b.dataset.f] = b.dataset.v;
    drawFields();
  });
  $('sd-group').addEventListener('change', pickGroup);
  // Developer tools (Rabbid Lab Dev): a test address for a ping sensor, which answers or goes down. What's added with it goes in 30 seconds.
  $('sd-dev').addEventListener('click', (e) => {
    const b = e.target.closest('[data-fill]');
    if (!b) return;
    setKind('ping');
    vals.target = `${b.dataset.fill}-${String(1 + Math.floor(Math.random() * 99)).padStart(2, '0')}.rabbidlab.test`;
    $('sd-name').value = b.dataset.fill === 'down' ? 'Test sensor that goes down' : 'Test sensor';
    drawFields();
  });
  $('sd-cancel').addEventListener('click', () => $('sensor-dialog').close());
  // What Check now found, as a status card: what answered and how, or what went wrong.
  const found = (f) => {
    const answered = f.ms == null ? '' : `Answered in ${ms(f.ms)}`;
    const title = !f.ok ? f.note || 'No answer' : kind === 'port' ? `Port ${vals.port} is open` : kind === 'dns' || kind === 'cert' || signs() || onSeat() ? f.note || 'It answered' : answered || 'It answered';
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
    return (f.title ? found({ ...f, note: f.title }) : found(f)) + (f.parts?.length ? watchRows(f.parts) : '') + (f.pin ? pinNote(f.pin) : '');
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

  // --- Alert settings: saved ones by name, one of them the default. A sensor picks one, or Custom for its own -------------

  const TIMES = {
    after: [[0, 'Right away'], [1, '1 min'], [5, '5 min'], [10, '10 min'], [30, '30 min'], [60, '1 hour'], [-1, 'Never']],
    again: [[0, 'Email once'], [60, 'Again every hour'], [360, 'Again every 6 hours'], [1440, 'Again every day']],
    back: [[true, 'Email'], [false, 'No email']],
  };
  const TIME_LABEL = { after: 'Email when it has been down for', again: 'While it is still down', back: 'When it comes back' };
  const STAR = '<svg class="rstar" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3 6.4 20.2l1.1-6.2L3 9.6l6.2-.9z"/></svg>';
  const aboutRule = (x) => (x.after < 0 ? 'Never emails; it shows on the page only.'
    : `Emails ${x.after ? `after ${x.after === 60 ? 'an hour' : `${x.after} minute${x.after === 1 ? '' : 's'}`} down` : 'right away'}`
      + `${x.again ? `, again every ${x.again === 60 ? 'hour' : x.again === 360 ? '6 hours' : 'day'} while it stays down` : ''}${x.back ? ', and when it comes back' : ''}.`);
  // The three times as rows of buttons; scope says whose they are.
  const timings = (scope, x) => Object.entries(TIMES).map(([key, items]) => `<div><label>${TIME_LABEL[key]}</label><div class="seg sn-seg">`
    + items.map(([v, text]) => `<button type="button" data-scope="${scope}" data-t="${key}" data-v="${v}" aria-pressed="${x[key] === v}"${key !== 'after' && x.after < 0 ? ' disabled' : ''}>${text}</button>`).join('')
    + '</div></div>').join('');
  const ruleButtons = (scope, current, extra = '') => `<div class="seg sn-seg">${rules.map((r) => `<button type="button" data-scope="${scope}" data-rule="${esc(r.id)}" aria-pressed="${r.id === current}"`
    + `${r.id === defaultRule ? ' title="The default: new sensors start with it"' : ''}>${esc(r.name)}${r.id === defaultRule ? STAR : ''}</button>`).join('')}${extra}</div>`;
  const timed = (b) => (b.dataset.t === 'back' ? b.dataset.v === 'true' : Number(b.dataset.v));

  // On a sensor: one of the saved settings, or Custom with times of its own, which can be saved by name for other sensors.
  const drawAlerts = () => {
    const custom = vals.rule === 'custom';
    $('sd-rules').innerHTML = ruleButtons('sensor', vals.rule, `<button type="button" data-rule="custom" aria-pressed="${custom}">Custom</button>`);
    if (!custom) {
      $('sd-alert-more').innerHTML = `<p class="hint">${esc(aboutRule(rules.find((x) => x.id === vals.rule) || vals.custom))}</p>`;
      return;
    }
    $('sd-alert-more').innerHTML = timings('custom', vals.custom) + (!can('sensors.alerts') ? '' : `<div class="sd-saveas">${vals.naming
      ? '<input id="sd-saveas-name" maxlength="40" placeholder="Name for these alert settings" autocomplete="off"><button type="button" class="small" data-saveas-go>Save</button><button type="button" class="link" data-saveas-cancel>Cancel</button>'
      : '<button type="button" class="link" data-saveas>Save these as…</button>'}</div>`);
  };
  $('sd-rules').addEventListener('click', (e) => {
    const b = e.target.closest('[data-rule]');
    if (!b) return;
    vals.rule = b.dataset.rule;
    vals.naming = false;
    drawAlerts();
  });
  $('sd-alert-more').addEventListener('click', async (e) => {
    const t = e.target.closest('[data-t]');
    if (t) {
      vals.custom[t.dataset.t] = timed(t);
      if (vals.custom.after < 0) Object.assign(vals.custom, { again: 0, back: false });
      drawAlerts();
    } else if (e.target.closest('[data-saveas]') || e.target.closest('[data-saveas-cancel]')) {
      vals.naming = !!e.target.closest('[data-saveas]');
      drawAlerts();
      $('sd-saveas-name')?.focus();
    } else if (e.target.closest('[data-saveas-go]')) {
      try {
        const made = await R.send('/api/sensors/rules', { name: $('sd-saveas-name').value, ...vals.custom });
        take(made.now);
        Object.assign(vals, { rule: made.id, naming: false });
        say('sd-problem', '');
        drawAlerts();
        draw();
      } catch (err) {
        say('sd-problem', err.message);
      }
    }
  });

  // In Email settings: the saved settings, a new one, the default, deleting one. Saved as they're changed.
  let mailRule = null;
  const drawMailRules = () => {
    if (!rules.some((r) => r.id === mailRule)) mailRule = defaultRule;
    const r = rules.find((x) => x.id === mailRule) || rules[0];
    if (!r) return;
    mailRule = r.id;
    $('ml-rules').innerHTML = ruleButtons('mail', r.id, '<button type="button" class="ml-new" data-rule-new>+ New</button>');
    if (document.activeElement !== $('ml-rule-name')) $('ml-rule-name').value = r.name;
    $('ml-rule-times').innerHTML = timings('mail', r);
    $('ml-rule-note').textContent = `${r.used} sensor${r.used === 1 ? ' uses' : 's use'} ${r.name}.${r.id === defaultRule ? ' It is the default: new sensors start with it.' : ''} Saved as you change it.`;
    $('ml-rule-default').hidden = $('ml-rule-delete').hidden = r.id === defaultRule;
  };
  const changeRule = async (change) => {
    const r = rules.find((x) => x.id === mailRule);
    try {
      take(await R.send(`/api/sensors/rules/${encodeURIComponent(r.id)}`, { after: r.after, again: r.again, back: r.back, ...change }));
      say('ml-problem', '');
    } catch (err) {
      say('ml-problem', err.message);
    }
    drawMailRules();
    draw();
  };
  $('ml-rules').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-rule]');
    if (b) {
      mailRule = b.dataset.rule;
      drawMailRules();
    } else if (e.target.closest('[data-rule-new]')) {
      let name = 'New alert settings';
      for (let i = 2; rules.some((r) => r.name.toLowerCase() === name.toLowerCase()); i++) name = `New alert settings ${i}`;
      try {
        const made = await R.send('/api/sensors/rules', { name, after: 5, again: 0, back: true });
        take(made.now);
        mailRule = made.id;
        drawMailRules();
        $('ml-rule-name').select();
      } catch (err) {
        say('ml-problem', err.message);
      }
    }
  });
  $('ml-rule-times').addEventListener('click', (e) => {
    const t = e.target.closest('[data-t]');
    if (t) changeRule({ [t.dataset.t]: timed(t) });
  });
  $('ml-rule-name').addEventListener('change', () => changeRule({ name: $('ml-rule-name').value }));
  $('ml-rule-default').addEventListener('click', async () => {
    try {
      take(await R.send(`/api/sensors/rules/${encodeURIComponent(mailRule)}/default`, {}));
    } catch (err) {
      say('ml-problem', err.message);
    }
    drawMailRules();
    draw();
  });
  $('ml-rule-delete').addEventListener('click', () => {
    const r = rules.find((x) => x.id === mailRule);
    R.sure(`Delete the alert settings ${r.name}?`, r.used ? `The ${r.used} sensor${r.used === 1 ? '' : 's'} on them move to ${rule(defaultRule).name}.` : 'No sensor uses them.', 'Delete', async () => {
      try {
        take(await R.send(`/api/sensors/rules/${encodeURIComponent(r.id)}/delete`, {}));
        mailRule = defaultRule;
      } catch (err) {
        say('ml-problem', err.message);
      }
      drawMailRules();
      draw();
    });
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
    mailRule = defaultRule;
    drawMailRules();
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
  // Settings, Notifications: the mail server and the saved alert settings, each changed in Email settings.
  $('sn-mail').addEventListener('click', () => { location.hash = '#settings/notifications'; });
  const drawNotes = () => {
    const m = kept || {};
    const set = m.provider === 'm365' ? !!m.tenant : !!m.host;
    const gmail = m.host === 'smtp.gmail.com';
    const from = m.from || m.username;
    $('nt-server').textContent = !set ? 'Not set up yet' : m.provider === 'm365' ? `Microsoft 365 · ${m.tenant}` : gmail ? 'Gmail' : `${m.host}:${m.port || 587}`;
    $('nt-from').textContent = from || '–';
    $('nt-change').textContent = set ? 'Edit' : 'Set up';
    $('nt-to').textContent = m.to?.length ? m.to.join(', ') : 'Nobody yet.';
    $('nt-test').disabled = !set || !m.to?.length;
    $('nt-rules').innerHTML = rules.map((r) => `<div class="ds-row"><div class="ds-l"><b><i class="ds-dot" data-color="${esc(r.color || '#8A8FA8')}"></i>${esc(r.name)}${r.id === defaultRule ? STAR.replace('rstar', 'ds-star') : ''}</b>`
      + `<span>${r.id === defaultRule ? 'The default for new sensors. ' : ''}${esc(aboutRule(r))}</span></div>`
      + `<div class="ds-r"><span class="ds-v">${r.used} sensor${r.used === 1 ? '' : 's'}</span><button type="button" class="ds-btn" data-edit-rule="${esc(r.id)}">Edit</button></div></div>`).join('');
    R.paint($('nt-rules'));
  };
  const KINDS = { user: 'Username and password', key: 'API key', community: 'SNMP v2c community', snmpv3: 'SNMPv3 user' };
  const drawCreds = (list) => {
    $('sn-creds').innerHTML = list.length ? list.map((l) => `<div class="ds-row"><div class="ds-l"><b>${esc(l.name)}</b><span>${esc([KINDS[l.kind] || 'Credentials', l.username].filter(Boolean).join(' · '))}</span></div>`
      + `<div class="ds-r"><span class="ds-v">${l.used} sensor${l.used === 1 ? '' : 's'}</span></div></div>`).join('') : '<p class="ds-empty">None yet. Credentials are saved when you add a sensor that signs in.</p>';
  };
  const loadNotes = async () => {
    try {
      if (can('sensors.alerts')) kept = await R.get('/api/mail');
      take(await R.get('/api/sensors'));
      if (can('sensors.edit')) drawCreds(await R.get('/api/sensors/logins'));
      say('nt-problem', '');
      say('sn-set-problem', '');
    } catch (err) {
      say(R.where()[1] === 'sensors' ? 'sn-set-problem' : 'nt-problem', err.message);
    }
    drawNotes();
  };
  $('nt-change').addEventListener('click', openMail);
  $('nt-change-to').addEventListener('click', async () => {
    await openMail();
    $('ml-to').focus();
  });
  $('nt-rules').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-edit-rule]');
    if (!b) return;
    await openMail();
    mailRule = b.dataset.editRule;
    drawMailRules();
    $('ml-rule-name').scrollIntoView({ block: 'center' });
  });
  $('nt-add').addEventListener('click', async () => {
    await openMail();
    $('ml-rules').querySelector('[data-rule-new]')?.click();
    $('ml-rule-name').scrollIntoView({ block: 'center' });
  });
  $('nt-test').addEventListener('click', async () => {
    say('nt-done', '');
    say('nt-problem', '');
    R.busy($('nt-mail'), 'Sending a test email…');
    try {
      await R.send('/api/mail/test', {});
      say('nt-done', `Sent to ${(kept?.to || []).join(', ')}. Check the inbox, and the junk folder.`);
    } catch (err) {
      say('nt-problem', err.message);
    }
    R.busy($('nt-mail'), '');
  });
  $('mail-dialog').addEventListener('close', () => { if (R.where()[0] === 'settings' && ['notifications', 'sensors'].includes(R.where()[1])) loadNotes(); });
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'settings' && ['notifications', 'sensors'].includes(e.detail[1]) && (can('sensors.alerts') || can('sensors.edit'))) loadNotes();
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
