// Sensors: #sensors, the things Rabbid Lab checks on a timer. The hero says how the lab is doing and its squares are the
// sensors; below, the sensors in their groups as cards or as a table whose columns can be resized and reordered.
// console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can } = R;
  const COLOURS = ['#FF8A3D', '#F2BE6B', '#B07CE8', '#6FA8FF', '#3CC7C7', '#F06BB4', '#7C83FF']; // a group's colour; green and red are left to up and down
  const KIND = { ping: 'Ping', port: 'Port', web: 'Web page', dns: 'DNS lookup', cert: 'Certificate', hw: 'Server hardware', nas: 'Storage', vmware: 'VMware', snmp: 'SNMP', svc: 'Windows service', disk: 'Disk space' };
  const WORD = { up: 'Up', down: 'Down', warn: 'Warning', paused: 'Paused', new: 'Checking', wait: 'Waiting' };
  // A sensor's icon when it doesn't watch a link with one, and the badge on its corner for how it is.
  const GLYPH = { ping: 'activity', port: 'plug', web: 'globe', dns: 'search', cert: 'lock', hw: 'server', nas: 'disk', vmware: 'monitor', snmp: 'org', svc: 'gear', disk: 'disk' };
  const BADGE = { up: 'check', warn: 'alert', down: 'x', paused: 'pause', wait: 'clock', new: 'clock' };
  const TAG = { warn: 'Warning', down: 'Down', paused: 'Paused', wait: 'Waiting', new: 'New' };
  const TIMED = ['ping', 'port', 'web', 'dns']; // kinds whose reply time is worth a line
  const DOWN = ' down'; // the filter's choice of everything that's down: no group's name starts with a space
  const NO_GROUP = ' none'; // and its choice of sensors with no group
  const NEW_GROUP = ' new';
  let sensors = [];
  let groups = [];
  let start = { after: 5, again: 0, back: true }; // when new sensors alert (Settings, Sensors)
  let gather = { on: false, secs: 60 }; // one alert for several
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
  // A time of day, with the day when it isn't today: 3:40 PM, Fri 8:00 AM.
  const clock = (at) => {
    const d = new Date(at);
    const time = d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    return d.toDateString() === new Date().toDateString() ? time : `${d.toLocaleDateString([], { weekday: 'short' })} ${time}`;
  };
  // When it alerts, in a few words: After 5 min, Right away, Never.
  const alertShort = (a) => (!a ? '' : a.after < 0 ? 'Never' : a.after === 0 ? 'Right away' : `After ${a.after === 60 ? '1 hour' : `${a.after} min`}`);
  // How it is now, in a few words for a card or a row.
  const now = (s) => (s.status === 'down' ? `Down ${s.since ? short(s.since) : ''}`.trim() : s.status === 'wait' ? 'Waiting'
    : s.status === 'paused' ? (s.pausedUntil ? `Until ${clock(s.pausedUntil)}` : 'Paused') : s.status === 'new' ? 'Checking…'
    : s.kind === 'cert' ? days(s.note) : s.status === 'warn' ? s.note || 'Warning' : ['hw', 'nas', 'vmware', 'snmp', 'svc', 'disk'].includes(s.kind) ? s.note || 'All OK' : s.ms == null ? 'Up' : ms(s.ms));
  const notAnswering = (s) => s.status === 'down' || s.status === 'wait';
  const percent = (p) => (p == null ? '' : `${p === 100 ? 100 : p.toFixed(p > 99.9 ? 2 : 1)}%`);
  const paint = (root) => {
    R.paint(root);
    root.querySelectorAll('[data-top]').forEach((el) => { el.style.top = `${el.dataset.top}%`; });
    root.querySelectorAll('[data-pct]').forEach((el) => { el.style.width = `${el.dataset.pct}%`; });
  };

  // The icon of the link a sensor watches, by sensor id, read from Links when the sensors change and every few minutes.
  let faces = {};
  let facesFor = '';
  let facesAt = 0;
  const loadFaces = async () => {
    const ids = sensors.map((s) => s.id).sort().join();
    if (!can('links.view') || (ids === facesFor && Date.now() - facesAt < 300000)) return;
    facesFor = ids;
    facesAt = Date.now();
    try {
      const got = await R.get('/api/links');
      faces = Object.fromEntries((got.links || []).filter((l) => l.sensor && /^data:image\//.test(l.icon || '')).map((l) => [l.sensor, l.icon]));
      if (!tools.busy() && !sizing && !moving) draw();
    } catch {
      // The cards keep their kind's icon.
    }
  };
  const glyph = (s) => (s.kind === 'snmp' && (s.parts || []).some((p) => p.key === 'battery') ? 'power' : GLYPH[s.kind] || 'activity');

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

  const squares = new Map(); // a sensor's square, kept between draws so it knows when its sensor was last checked
  const drawSquares = () => {
    const hero = $('sn-hero');
    const size = R.squareSize(sensors.length);
    const items = [...sensors].sort((a, b) => a.id.localeCompare(b.id)).map((s) => {
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
      const side = Math.round(down ? size * 1.4 : size * (0.85 + r * 0.35));
      el.className = `tile sen s-${s.status}${side < 20 ? ' tiny' : ''}`;
      el.title = `${s.name} · ${now(s)}`;
      const face = faces[s.id] || glyph(s);
      if (el.dataset.face !== face) {
        el.dataset.face = face;
        el.innerHTML = faces[s.id] ? `<img src="${esc(face)}" alt="">` : svg(face);
      }
      el.style.setProperty('--g', '#1E2132');
      el.style.setProperty('--ring', colour(s));
      R.ping(el, s.checked);
      Object.assign(el.style, { width: `${side}px`, height: `${side}px`, rotate: `${down ? 8 : Math.round(hash(`${s.id}r`) * 50 - 25)}deg` });
      return { el, id: s.id };
    });
    const seen = new Set(items.map((x) => x.id));
    squares.forEach((el, id) => {
      if (seen.has(id)) return;
      el.remove();
      squares.delete(id);
    });
    R.squares(hero, items, $('sn-card'));
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
    // What waits on the ones that are down says nothing until they're back.
    const waiting = live.filter((s) => s.status === 'wait').length;
    const behind = !down.length || !waiting ? '' : ` ${waiting} sensor${waiting === 1 ? '' : 's'} behind ${down.length === 1 ? 'it' : 'them'} ${waiting === 1 ? 'is' : 'are'} waiting instead of alerting.`;
    $('sn-lead-more').textContent = !sensors.length ? 'Add a sensor to watch a server, a service or a certificate.'
      : (down.length > 1 ? `So did ${down.slice(1, 4).map((s) => s.name).join(', ')}${down.length > 4 ? ` and ${down.length - 4} more` : ''}.` : '') + behind;
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

  const COLS = { state: ['State', 130], name: ['Sensor', 220], check: ['Check', 120], where: ['Address', 200], reply: ['Reply', 110], line: ['Last hour', 240], up: ['Up, 7 days', 100], alerts: ['Alerts', 120] };
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
  const CELL = {
    state: (s) => `<span class="state ${s.status}">${s.status === 'down' && s.since ? `Down ${short(s.since)}` : s.status === 'paused' && s.pausedUntil ? `Until ${clock(s.pausedUntil)}` : WORD[s.status] || s.status}</span>`,
    name: (s) => `<div class="nm"><span class="lic">${faces[s.id] ? `<img src="${esc(faces[s.id])}" alt="">` : svg(glyph(s))}</span><b>${esc(s.name)}</b></div>`,
    check: (s) => `<span>${esc(check(s))}${s.waits ? ` · waits on ${esc(s.waits)}` : ''}</span>`,
    where: (s) => `<span class="dim" title="${esc(where(s))}">${esc(where(s))}</span>`,
    reply: (s) => `<span class="num">${esc(s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : ['hw', 'nas', 'vmware', 'snmp', 'svc', 'disk'].includes(s.kind) ? s.note || 'All OK' : s.ms == null ? '' : ms(s.ms)) : '')}</span>`,
    line: (s) => line(s.recent, s.status),
    up: (s) => `<span class="num">${percent(s.up7)}</span>`,
    alerts: (s) => `<span class="dim">${esc(alertShort(s.alert))}</span>`,
  };
  const head = () => `<div class="tr th sn">${cols.order.map((k) => `<span class="sn-th" draggable="true" data-col="${k}">${COLS[k][0]}<i class="sn-size" data-size="${k}"></i></span>`).join('')}</div>`;
  const row = (s) => `<div class="tr sn s-${s.status}" data-open="${esc(s.id)}">${cols.order.map((k) => CELL[k](s)).join('')}</div>`;

  // --- The sensors, in their groups ----------------------------------------------------------------------------------------

  // The card's one line: reply time and the week for a timed check that's up, else what the last check said.
  const said = (s) => (s.status === 'down' ? `${s.note || 'No reply'}${s.since ? ` for ${long(s.since)}` : ''}`
    : s.status === 'wait' ? `Waits on ${s.waits}`
    : s.status === 'paused' ? (s.pausedUntil ? `Paused until ${clock(s.pausedUntil)}` : 'Paused until you resume it')
    : s.status === 'new' ? 'Not checked yet'
    : TIMED.includes(s.kind) && s.status === 'up' && !s.invert ? [s.ms == null ? '' : ms(s.ms), s.up7 == null ? '' : `${percent(s.up7)} this week`].filter(Boolean).join(' · ') || 'Up'
    : s.note || WORD[s.status]);
  const quiet = (text) => `<span class="sn-quiet">${esc(text)}</span>`;
  // The last hour of replies, smoothed. Checks with no reply are a red dash along the bottom.
  const smooth = (p) => p.map((q, i) => {
    if (!i) return `M${q[0].toFixed(1)} ${q[1].toFixed(1)}`;
    const a = p[i - 2] || p[i - 1];
    const b = p[i - 1];
    const d = p[i + 1] || q;
    return `C${(b[0] + (q[0] - a[0]) / 6).toFixed(1)} ${(b[1] + (q[1] - a[1]) / 6).toFixed(1)} ${(q[0] - (d[0] - b[0]) / 6).toFixed(1)} ${(q[1] - (d[1] - b[1]) / 6).toFixed(1)} ${q[0].toFixed(1)} ${q[1].toFixed(1)}`;
  }).join('');
  const trace = (recent) => {
    if (recent.length < 2) return quiet('First checks under way');
    const [w, h] = [300, 34];
    const got = recent.filter((n) => n >= 0);
    const top = Math.max(...got, 1) * 1.15;
    const bottom = got.length ? Math.min(...got) * 0.55 : 0;
    const step = w / (recent.length - 1);
    const at = recent.map((n, i) => [i * step, n < 0 ? null : h - 3 - ((n - bottom) / (top - bottom || 1)) * (h - 9)]);
    const runs = [[]];
    at.forEach((p) => (p[1] == null ? runs.push([]) : runs[runs.length - 1].push(p)));
    const d = runs.filter((r) => r.length > 1).map(smooth).join('');
    const gone = at.map((p) => (p[1] == null ? `M${Math.max(0, p[0] - step / 2).toFixed(1)} ${h - 1}H${Math.min(w, p[0] + step / 2).toFixed(1)}` : '')).join('');
    return `<svg class="sn-trace" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">${d ? `<path d="${d}"/>` : ''}${gone ? `<path class="gone" d="${gone}"/>` : ''}</svg>`;
  };
  // A server, pool, vCenter or UPS: up to four of its parts in a word or two, the full answer on hover.
  const LABEL = { health: 'Health', power: 'PSU', fans: 'Fans', temps: 'Temp', disks: 'Disks', pools: 'Pool', space: 'Free', hosts: 'Hosts', alarms: 'Alarms', uptime: 'Up', battery: 'Battery', iface: 'Port' };
  const POWER = { 'On line power': 'Line', 'On battery': 'Battery', 'On bypass': 'Bypass', 'No output': 'Off' };
  const word = (w) => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase();
  const brief = (s, p) => {
    const t = p.now || '';
    const of = /^(\d+) of (\d+)/.exec(t);
    const free = [...t.matchAll(/(\d+) % free/g)].pop();
    const pools = t.split(' · ');
    return of ? `${of[1]}/${of[2]}`
      : p.key === 'temps' ? (/\d+ °C/.exec(t) || [t])[0]
      : p.key === 'space' && free ? `${free[1]}%`
      : p.key === 'pools' ? (pools.length === 1 ? word(pools[0].split(' ').pop()) : `${pools.length} pools`)
      : p.key === 'power' && s.kind === 'snmp' ? POWER[t] || t
      : p.key === 'battery' ? t.replace(' %', '%')
      : p.key === 'uptime' ? t.replace(/^Up /, '') : t;
  };
  const cells = (s) => {
    const parts = s.parts.length > 1 ? s.parts.filter((p) => p.key !== 'health') : s.parts;
    return `<span class="sn-cells">${parts.slice(0, 4).map((p) => `<span class="${esc(p.state)}" title="${esc(`${p.name}: ${p.now}`)}"><small>${esc(p.key === 'power' && s.kind === 'snmp' ? 'Power' : LABEL[p.key] || p.name)}</small><b>${esc(brief(s, p))}</b></span>`).join('')}</span>`;
  };
  // What's left, as a bar: days on a certificate (of 90) or free space on a drive.
  const left = (s) => {
    const days = /(\d+) day/.exec(s.note || '');
    const free = /(\d+) % free/.exec(s.note || '');
    const pct = s.kind === 'cert' ? (days ? Math.min(100, (days[1] / 90) * 100) : /today/.test(s.note || '') ? 0 : null) : free ? Number(free[1]) : null;
    return pct == null ? null : `<span class="sn-meter"><i data-pct="${Math.max(2, Math.round(pct))}"></i></span>`;
  };
  const foot = (s) => (s.status === 'paused' ? quiet('No checks or alerts while paused')
    : s.status === 'wait' ? quiet(`Checked again once ${s.waits} answers`)
    : s.status === 'new' ? quiet('First check under way')
    : TIMED.includes(s.kind) ? trace(s.recent)
    : s.parts?.length ? cells(s)
    : (s.status !== 'down' && left(s)) || quiet(s.kind === 'svc' || s.kind === 'disk' ? `Checked by the client on ${s.target}` : where(s)));
  const card = (s) => `<div class="ltile sn-card s-${s.status}" data-open="${esc(s.id)}"${can('sensors.edit') ? ` draggable="true" data-drag="${esc(s.id)}"` : ''}>`
    + `<button type="button" class="lgo" aria-label="${esc(s.name)}" title="${esc(`${s.name} · ${check(s)} · ${where(s)}`)}">`
    + `<span class="sn-face${faces[s.id] ? '' : ' glyph'}">${faces[s.id] ? `<img src="${esc(faces[s.id])}" alt="">` : svg(glyph(s))}<i class="sn-badge">${svg(BADGE[s.status] || 'check')}</i></span>`
    + `<span class="sn-main"><span class="sn-title"><b>${esc(s.name)}</b>${TAG[s.status] ? `<em>${TAG[s.status]}</em>` : ''}</span>`
    + `<small>${esc(said(s))}</small><span class="sn-foot">${foot(s)}</span></span></button></div>`;

  const draw = () => {
    const find = $('sn-find').value.trim().toLowerCase();
    const down = sensors.filter(notAnswering); // down, and waiting on something that's down
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
    const shown = sensors.filter((s) => (group === DOWN ? notAnswering(s) : !group || (s.group || '') === only)
      && (!find || [s.name, s.target, s.group, KIND[s.kind], s.note, ...(s.tags || [])].some((v) => String(v || '').toLowerCase().includes(find))));
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

  // What the server answers to a read or a change: the sensors and groups in order, when new sensors alert, and gathering.
  const take = (answer) => {
    sensors = answer.sensors;
    groups = answer.groups || [];
    start = answer.start || start;
    gather = answer.gather || gather;
    lastCheck = answer.last;
    loadFaces();
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
        take(await R.send(`/api/sensors/${encodeURIComponent(id)}/move`, { group: to }));
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
      const down = sensors.filter(notAnswering).length;
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
  const EVENT = { down: ['Went down', 's-down'], up: ['Came back', 's-up'], warn: ['Warning', 's-warn'], email: ['Alert sent', 's-new'], problem: ['Alert not sent', 's-down'],
    pause: ['Paused', 's-paused'], resume: ['Started again', 's-new'] };
  // Names as a list: a, b and c.
  const and = (names) => (names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`);
  // What depends on a sensor, directly or through others.
  const behindOf = (id) => {
    const found = [];
    const next = [id];
    while (next.length) {
      const at = next.shift();
      sensors.filter((x) => x.dependsOn === at && x.id !== id && !found.includes(x)).forEach((x) => {
        found.push(x);
        next.push(x.id);
      });
    }
    return found;
  };
  const fill = (d) => {
    const s = d.sensor;
    showing = s;
    const a = s.alert || start;
    $('sv-title').textContent = s.name;
    $('sv-what').textContent = `${check(s)} · ${where(s)} · every ${{ 30: '30 seconds', 60: 'minute', 300: '5 minutes', 3600: 'hour' }[s.every] || `${s.every} s`} · ${a.after < 0 ? 'never alerts' : `alerts ${alertShort(a).toLowerCase()}`}`;
    // What it waits on, and what waits on it.
    const mine = sensors.filter((x) => x.dependsOn === s.id).map((x) => x.name);
    const above = sensors.find((x) => x.id === s.dependsOn);
    $('sv-deps').hidden = !mine.length && !above;
    $('sv-deps').innerHTML = (above ? `<p>${svg('link')}<span>It depends on <b>${esc(above.name)}</b>: while that's down, this one waits instead of alerting.</span></p>` : '')
      + (mine.length ? `<p>${svg('link')}<span><b>${esc(and(mine))}</b> depend${mine.length === 1 ? 's' : ''} on it: while it's down, ${mine.length === 1 ? 'that one waits' : 'they wait'} instead of alerting.</span></p>` : '');
    $('sv-now').className = `sv-now s-${s.status}`;
    $('sv-state').textContent = s.status === 'down' ? `Down for ${s.since ? long(s.since) : 'a moment'}` : s.status === 'wait' ? `Down, waiting on ${s.waits}`
      : s.status === 'up' ? `Up${s.since ? ` for ${long(s.since)}` : ''}` : s.status === 'warn' ? 'Needs attention'
        : s.status === 'paused' ? (s.pausedUntil ? `Paused until ${clock(s.pausedUntil)}` : 'Paused') : 'Not checked yet';
    const alerted = s.status !== 'down' ? '' : d.alerted ? `Alerted ${R.when(d.alerted)}` : a.after < 0 ? 'It never alerts' : a.after ? `Alerts after ${a.after === 60 ? 'an hour' : `${a.after} minutes`} down` : 'Alerts right away';
    $('sv-note').textContent = s.status === 'paused' ? `It isn't checked, and nobody is alerted.${s.pausedUntil ? ' It starts again on its own.' : ''}`
      : s.status === 'wait' ? `${s.note || 'No reply'}. No alert while ${s.waits} is down. If it's still down once ${s.waits} is back, it alerts then.`
        : `${[s.note, s.checked ? `Checked ${short(s.checked)} ago` : '', alerted].filter(Boolean).join('. ')}.`;
    $('sv-ms').textContent = s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : s.ms == null ? '-' : ms(s.ms)) : '-';
    $('sv-up24').textContent = percent(d.up24) || '-';
    $('sv-up7').textContent = percent(d.up7) || '-';
    $('sv-line').innerHTML = line(s.recent, s.status);
    $('sv-line').firstElementChild.className = `sn-chart s-${s.status}`;
    $('sv-events').hidden = !d.events.length;
    $('sv-events').innerHTML = d.events.slice(0, 40).map((e) => `<div class="row still ${(EVENT[e.what] || ['', ''])[1]}"><i></i><time>${esc(R.when(e.at))}</time>`
      + `<span class="t"><b>${esc((EVENT[e.what] || [e.what])[0])}</b><span>${esc(e.text)}</span></span></div>`).join('');
    $('sv-pause').innerHTML = s.paused ? 'Start again' : `Pause${svg('right')}`;
    $('sv-pause').classList.toggle('menu-btn', !s.paused);
    shut();
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
  // Pause asks for how long, with when each would end; what depends on it can go along. A paused one just starts again.
  const shut = () => {
    $('sv-menu').hidden = true;
    $('sv-pause').setAttribute('aria-expanded', 'false');
  };
  $('sv-pause').addEventListener('click', () => {
    if (showing.paused) return act('pause', { paused: false });
    if (!$('sv-menu').hidden) return shut();
    const t = Date.now();
    const eight = new Date();
    eight.setDate(eight.getDate() + 1);
    eight.setHours(8, 0, 0, 0);
    const deps = behindOf(showing.id).filter((x) => x.status !== 'paused');
    $('sv-menu').innerHTML = `<span class="sv-menu-h">Pause ${esc(showing.name)}</span>`
      + [[15, 'For 15 minutes'], [60, 'For an hour'], [240, 'For 4 hours'], [Math.ceil((eight - t) / 60000), 'Until 8:00 AM tomorrow', eight], [0, 'Until I start it again']]
        .map(([m, text, end]) => `<button type="button" role="menuitem" data-mins="${m}">${text}${m ? `<small>${esc(clock(end || t + m * 60000))}</small>` : ''}</button>`).join('')
      + (deps.length ? `<label class="sv-menu-all"><input type="checkbox" id="sv-menu-deps" checked> Pause the ${deps.length === 1 ? 'one' : deps.length} that depend${deps.length === 1 ? 's' : ''} on it too</label>` : '');
    $('sv-menu').hidden = false;
    $('sv-pause').setAttribute('aria-expanded', 'true');
    $('sv-menu').querySelector('button')?.focus();
  });
  $('sv-menu').addEventListener('click', (e) => {
    const b = e.target.closest('[data-mins]');
    if (!b) return;
    const minutes = Number(b.dataset.mins) || null;
    const dependents = !!$('sv-menu-deps')?.checked;
    shut();
    act('pause', { paused: true, minutes, dependents });
  });
  $('sview-dialog').addEventListener('click', (e) => { if (!$('sv-menu').hidden && !e.target.closest('.sv-pause-at')) shut(); });
  $('sview-dialog').addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || $('sv-menu').hidden) return;
    e.preventDefault(); // closes the menu, not the dialog
    shut();
    $('sv-pause').focus();
  });
  // Send a test alert: this sensor's real alert, marked as a test, to its channels. The toast says where it went.
  $('sv-test').addEventListener('click', async () => {
    const id = showing.id;
    $('sv-test').disabled = true;
    try {
      const r = await R.send(`/api/sensors/${encodeURIComponent(id)}/test`, {});
      if (r.went.length) R.toast('Sent a test alert', `to ${and(r.went)}.${r.failed.length ? ` ${r.failed.join(' ')}` : ''}`, { tone: r.failed.length ? 'warn' : '' });
      else R.toast("The test alert didn't go", r.failed.join(' '), { tone: 'warn' });
      if ($('sview-dialog').open && showing?.id === id) await open(id);
    } catch (err) {
      R.toast("The test alert didn't go", err.message, { tone: 'warn' });
    }
    $('sv-test').disabled = false;
  });
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
  const SEAT_HINT = () => `<p class="hint">Checked by the ${R.esc(R.lab())} client on that computer, which runs as SYSTEM. No credentials needed.</p>`;
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
    svc: () => seatPick() + field('service', 'Service', { wide: true, max: 256, ph: 'Spooler or Print Spooler' }) + SEAT_HINT(),
    disk: () => seatPick() + field('drive', 'Drive', { max: 3, ph: 'C' })
      + choice('warnFree', 'Warn when free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: 10 }) + SEAT_HINT(),
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
    drawMore();
  };
  // The kinds down the left, narrowed to what Find a check has.
  const drawTypes = () => {
    const q = $('sd-find').value.trim().toLowerCase();
    const hit = (x) => !q || `${KIND[x.id]} ${x.about}`.toLowerCase().includes(q);
    $('sd-type-list').innerHTML = GROUPS.map(([g, name]) => [name, TYPES.filter((x) => x.g === g && hit(x))]).filter(([, list]) => list.length)
      .map(([name, list]) => `<p class="rail-label">${name}</p>` + list.map((x) => `<a href="#" data-kind="${x.id}"${x.id === kind ? ' aria-current="true"' : ''}>${svg(x.icon)}<span>${KIND[x.id]}</span>${x.signs ? svg('key', 'sd-key') : ''}</a>`).join('')).join('')
      || '<p class="hint sd-none">No check like that.</p>';
  };
  $('sd-find').addEventListener('input', drawTypes);
  const setKind = (k) => {
    kind = TYPES.some((t) => t.id === k) ? k : 'ping';
    const t = TYPES.find((x) => x.id === kind);
    drawTypes();
    $('sd-kind-name').textContent = KIND[kind];
    $('sd-kind-about').textContent = t.about;
    // A certificate doesn't change by the minute, nor hardware. Checked hourly, one miss is enough to count as down.
    if (!editing) {
      $('sd-every').value = kind === 'cert' ? '3600' : (signs() && kind !== 'snmp') || kind === 'disk' ? '300' : '60';
      $('sd-down').value = $('sd-every').value === '3600' ? '1' : '2';
    }
    drawFields();
    drawAlerts();
  };
  // More, folded: alert when it answers, how long a check waits, and for a web page over https its certificate.
  const MORE = { web: 'More for web pages', ping: 'More for ping', port: 'More for ports', dns: 'More for DNS lookups' };
  const WAIT = { web: 10, ping: 5, port: 5, dns: 5 };
  const moreRow = (name, about, right) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">${right}</div></div>`;
  const drawMore = () => {
    $('sd-more').hidden = !(kind in MORE);
    if (!(kind in MORE)) return;
    $('sd-more-title').textContent = MORE[kind];
    const wait = vals.timeout || WAIT[kind];
    $('sd-more-rows').innerHTML = (kind === 'web' ? moreRow('Warn before the certificate expires', 'For an https address: a warning this many days before it expires.',
      `<select id="sd-cert" aria-label="Warn before the certificate expires">${[[7, '7 days'], [14, '14 days'], [30, '30 days'], [0, 'Never']]
        .map(([v, text]) => `<option value="${v}"${Number(vals.certDays || 0) === v ? ' selected' : ''}>${text}</option>`).join('')}</select>`) : '')
      + (kind === 'dns' ? '' : moreRow('Alert when it answers instead', 'For things that should be off, like a test server or a guest network.',
        `<input type="checkbox" class="switch" id="sd-invert" role="switch" aria-label="Alert when it answers instead"${vals.invert ? ' checked' : ''}>`))
      + moreRow('Give up waiting after', 'How long one check waits for an answer.',
        `<div class="seg">${[5, 10, 30].map((v) => `<button type="button" data-wait="${v}" aria-pressed="${wait === v}">${v} s</button>`).join('')}</div>`);
  };
  $('sd-more-rows').addEventListener('change', (e) => {
    if (e.target.id === 'sd-cert') vals.certDays = Number(e.target.value);
    if (e.target.id === 'sd-invert') vals.invert = e.target.checked;
  });
  $('sd-more-rows').addEventListener('click', (e) => {
    const b = e.target.closest('[data-wait]');
    if (!b) return;
    vals.timeout = Number(b.dataset.wait);
    drawMore();
  });
  // Tags as chips: Enter or a comma adds one, Backspace in the empty box takes the last off.
  const drawTags = () => {
    $('sd-tags').querySelectorAll('.sd-tag').forEach((t) => t.remove());
    $('sd-tag-in').insertAdjacentHTML('beforebegin', (vals.tags || []).map((t) => `<span class="sd-tag">${esc(t)}<button type="button" data-untag="${esc(t)}" aria-label="Take the tag ${esc(t)} off">×</button></span>`).join(''));
  };
  const addTag = () => {
    const t = $('sd-tag-in').value.replace(/,/g, '').trim().toLowerCase();
    $('sd-tag-in').value = '';
    vals.tags ||= [];
    if (t && !vals.tags.includes(t) && vals.tags.length < 8) vals.tags.push(t);
    drawTags();
  };
  $('sd-tag-in').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault();
      addTag();
    } else if (e.key === 'Backspace' && !e.target.value && vals.tags?.length) {
      vals.tags.pop();
      drawTags();
    }
  });
  $('sd-tag-in').addEventListener('blur', addTag);
  $('sd-tags').addEventListener('click', (e) => {
    const b = e.target.closest('[data-untag]');
    if (!b) return $('sd-tag-in').focus();
    vals.tags = vals.tags.filter((t) => t !== b.dataset.untag);
    drawTags();
  });
  // --- Alerts: when it alerts, reminders, the all-clear, and where alerts go. The same rows on a sensor, a watched link and in Settings --

  let chans = null; // /api/channels, when this person may read it
  const CH_KIND = { email: 'Email', discord: 'Discord', teams: 'Teams', slack: 'Slack', ntfy: 'ntfy', webhook: 'Webhook' };
  const CH_KINDS = ['discord', 'teams', 'slack', 'ntfy', 'webhook'];
  // Each kind's mark, small and in its colour: an envelope, Discord's face, Teams' T, Slack's hash, ntfy's bubble.
  const MARK = {
    email: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2.5"/><path d="m4 7.5 8 5.5 8-5.5"/></svg>',
    discord: '<svg viewBox="0 0 24 24" fill="currentColor" fill-rule="evenodd"><path d="M6.3 6.2C7.9 5.4 9.7 5 12 5s4.1.4 5.7 1.2c1.9 2.7 2.8 5.6 2.6 8.9-1.5 1.2-3 2-4.6 2.5l-1-1.6c-.9.3-1.8.4-2.7.4s-1.8-.1-2.7-.4l-1 1.6c-1.6-.5-3.1-1.3-4.6-2.5-.2-3.3.7-6.2 2.6-8.9zM9.3 10.6c-.9 0-1.5.8-1.5 1.8s.7 1.8 1.5 1.8 1.5-.8 1.5-1.8-.6-1.8-1.5-1.8zM14.7 10.6c-.9 0-1.5.8-1.5 1.8s.7 1.8 1.5 1.8 1.5-.8 1.5-1.8-.6-1.8-1.5-1.8z"/></svg>',
    teams: '<svg viewBox="0 0 24 24" fill="currentColor" fill-rule="evenodd"><circle cx="18" cy="6.8" r="2.3"/><path d="M16.5 10.2h4a1.5 1.5 0 0 1 1.5 1.5v3.6a3.4 3.4 0 0 1-5.5 2.7z"/><path d="M4.5 6h8A2.5 2.5 0 0 1 15 8.5v8a2.5 2.5 0 0 1-2.5 2.5h-8A2.5 2.5 0 0 1 2 16.5v-8A2.5 2.5 0 0 1 4.5 6zM5.3 9h6.4v1.8H9.4V16H7.6v-5.2H5.3z"/></svg>',
    slack: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"><path d="M9.5 4v16M14.5 4v16M4 9.5h16M4 14.5h16"/></svg>',
    ntfy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-8l-4 3v-3a2 2 0 0 1-2-2z"/><path d="m8 8.5 2.5 2-2.5 2M12.5 12.5h3.5"/></svg>',
  };
  const chIcon = (k) => MARK[k] || svg('link');
  const chCard = (id, k, name, sub, on) => `<button type="button" class="chan${on ? ' on' : ''}" data-chan="${esc(id)}" aria-pressed="${on}"><span class="chan-ic ${k}">${chIcon(k)}</span>`
    + `<span><b>${esc(name)}</b><small>${esc(sub)}</small></span><i class="chan-tick">${svg('check')}</i></button>`;
  const startChannels = () => (chans ? [...(chans.email.onForNew ? ['email'] : []), ...chans.channels.filter((c) => c.onForNew).map((c) => c.id)] : null);
  const chName = (id) => (id === 'email' ? 'Email' : (({ kind, name }) => `${CH_KIND[kind]}${name ? ` ${name}` : ''}`)(chans?.channels.find((c) => c.id === id) || { kind: 'webhook', name: '' }));
  // The kinds not set up yet: email with no mail server, and each kind of channel there isn't one of.
  const missing = () => (!chans ? [] : [...(chans.email.ready ? [] : ['email']), ...CH_KINDS.filter((k) => !chans.channels.some((c) => c.kind === k))]);
  const orList = (names) => (names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}`);
  // Send to: email and each channel that's set up, as a card to tick, then one line to add a channel (Add a channel picks the kind).
  const sendCards = (on) => {
    if (!chans) return '';
    const left = missing();
    return (chans.email.ready ? chCard('email', 'email', 'Email', 'To your alert list', on.includes('email')) : '')
      + chans.channels.map((c) => chCard(c.id, c.kind, CH_KIND[c.kind], c.name || c.where, on.includes(c.id))).join('')
      + `<button type="button" class="chan-addline" data-chan-add>${svg('plus')}<span><b>Add a channel</b>`
      + `<small>${left.length ? esc(orList(left.map((k) => (k === 'webhook' ? 'a webhook' : CH_KIND[k])))) : 'Another Discord, Teams, Slack, ntfy or webhook'}</small></span>`
      + `<i class="chan-minis">${left.map((k) => `<span class="chan-ic ${k}">${chIcon(k)}</span>`).join('')}</i></button>`;
  };
  // A click in Send to: a channel ticked or not, or Add a channel, and what's added is ticked. The list is changed in place.
  const sendClick = (e, list, redraw) => {
    const b = e.target.closest('[data-chan]');
    if (b) {
      const id = b.dataset.chan;
      const at = list.indexOf(id);
      if (at < 0) list.push(id);
      else list.splice(at, 1);
      return redraw();
    }
    if (e.target.closest('[data-chan-add]')) {
      openChannel(null, (id) => {
        if (id && !list.includes(id)) list.push(id);
        redraw();
      });
    }
  };
  // The three times as rows. x is { after, again, back }; each control says which with data-al.
  const AFTER = [[0, 'Right away'], [1, '1 minute'], [5, '5 minutes'], [10, '10 minutes'], [30, '30 minutes'], [60, 'An hour'], [-1, 'Never, only show it here']];
  const AGAIN = [[0, 'Alert once'], [60, 'Again every hour'], [360, 'Again every 6 hours'], [1440, 'Again every day']];
  const alRow = (name, about, right) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">${right}</div></div>`;
  const options = (items, on) => items.map(([v, t]) => `<option value="${v}"${v === on ? ' selected' : ''}>${t}</option>`).join('');
  const timeRows = (x) => alRow('Alert when it has been down for', 'Counted from the first miss.', `<select data-al="after" aria-label="Alert when it has been down for">${options(AFTER, x.after)}</select>`)
    + alRow('While it stays down', 'Reminders until it comes back.', `<select data-al="again" aria-label="While it stays down"${x.after < 0 ? ' disabled' : ''}>${options(AGAIN, x.again)}</select>`)
    + alRow('When it comes back', 'An all-clear, only after a down alert went.',
      `<input type="checkbox" class="switch" data-al="back" role="switch" aria-label="When it comes back"${x.back ? ' checked' : ''}${x.after < 0 ? ' disabled' : ''}>`);
  // A change in those rows, read into x. False when it wasn't one of them.
  const timeChange = (e, x) => {
    const k = e.target.dataset.al;
    if (k === 'after' || k === 'again') x[k] = Number(e.target.value);
    else if (k === 'back') x.back = e.target.checked;
    else return false;
    if (x.after < 0) Object.assign(x, { again: 0, back: false });
    return true;
  };
  // What will happen, in words: Alerts Email and Discord #homelab once it has been down 5 minutes, and again when it's back.
  const alertWords = (x, picked) => {
    if (x.after < 0) return 'Never alerts: it only shows here.';
    const to = picked && picked.filter((id) => id !== 'email' || chans?.email.ready); // email ticked with no mail server sends nothing
    if (to && !to.length) return 'Nobody is alerted yet: set up or tick where alerts go above.';
    const mins = (m) => (m === 60 ? 'an hour' : m === 1 ? 'a minute' : `${m} minutes`);
    return `Alerts${to ? ` ${and(to.map(chName))}` : ''} ${x.after ? `once it has been down ${mins(x.after)}` : 'as soon as it\'s down'}`
      + `${x.again ? `, again every ${{ 60: 'hour', 360: '6 hours', 1440: 'day' }[x.again]} while it stays down` : ''}${x.back ? `${x.again ? ',' : ''} and again when it's back` : ''}.`;
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
    group: $('sd-group').value === NEW_GROUP ? $('sd-group-new').value : $('sd-group').value, every: Number($('sd-every').value), rule: 'custom', custom: vals.alert, dependsOn: vals.dependsOn || '',
    tags: vals.tags || [], downAfter: Number($('sd-down').value), channels: chans ? vals.channels || [] : null, certDays: kind === 'web' ? vals.certDays || null : null,
    invert: ['ping', 'port', 'web'].includes(kind) && !!vals.invert, timeout: kind in WAIT ? vals.timeout || null : null });
  // A sensor to change, or null for a new one, which starts in the group asked for or the one being looked at.
  const edit = async (s, into) => {
    editing = s || null;
    Object.keys(vals).forEach((k) => delete vals[k]);
    if (s) Object.assign(vals, { target: s.target, port: s.port ?? '', server: s.server || '', expect: s.expect || '', text: s.text || '', address: s.address || '', warn: s.warnDays ?? 21,
      login: s.login || undefined, watch: s.watch || null, pin: s.pin || null, product: s.product === 'synology' ? 'synology' : '', warnFree: s.warnFree ?? 10, value: s.value || 'uptime', oid: s.oid || '', iface: s.interface || '', seat: s.seat || undefined, service: s.service || '', drive: s.drive || '' });
    // What More, Tags and Send to start with: a new web page warns 14 days before its certificate expires.
    Object.assign(vals, { tags: [...(s?.tags || [])], certDays: s ? s.certDays || 0 : 14, invert: !!s?.invert, timeout: s?.timeout || null });
    Object.assign(vals, { alert: { ...(s?.alert || start) }, dependsOn: s?.dependsOn || '' }); // its own alert times; a new one starts with Settings' ones
    logins = await R.get('/api/sensors/logins').catch(() => []);
    seats = await R.get('/api/sensors/seats').catch(() => []);
    chans = can('sensors.alerts') ? await R.get('/api/channels').catch(() => null) : null;
    vals.channels = s ? [...(s.channels || ['email'])] : startChannels();
    $('sd-find').value = '';
    $('sd-tag-in').value = '';
    drawTags();
    drawSend();
    if (s?.kind === 'snmp' && logins.find((l) => l.id === s.login)?.kind === 'snmpv3') vals.snmpVersion = 'v3';
    $('sd-title').textContent = s ? `Change ${s.name}` : 'Add a sensor';
    $('sd-name').value = s?.name || '';
    $('sd-group').innerHTML = '<option value="">No group</option>' + groups.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join('') + `<option value="${NEW_GROUP}">New group…</option>`;
    $('sd-group').value = s ? s.group || '' : into ?? (group === DOWN || group === NO_GROUP ? '' : group);
    $('sd-group-new').value = '';
    pickGroup();
    $('sd-every').value = String(s?.every || 60);
    $('sd-down').value = String(s?.downAfter ?? (s?.every >= 3600 ? 1 : 2));
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
  ['sd-down', 'sd-every'].forEach((id) => $(id).addEventListener('change', () => drawAlerts())); // the words under the alert setting follow them
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
    return `<p class="sd-pin">${svg('alert')}<span>It has a self-signed certificate. ${esc(R.lab())} trusts this one, <code title="${esc(pin)}">SHA-256 ${esc(short)}</code>, and warns you if it changes.</span></p>`;
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

  // --- A sensor's alerts: the times, what it depends on, where they go, and what that adds up to ----------------------------

  // Down after its misses, in words: Down after 2 misses in a row, about 2 minutes.
  const downText = () => {
    const n = Number($('sd-down').value) || 2;
    const secs = n * (Number($('sd-every').value) || 60);
    const about = secs < 90 ? `${secs} seconds` : secs < 3600 ? `${Math.round(secs / 60)} minutes` : secs === 3600 ? 'an hour' : `${Math.round(secs / 3600)} hours`;
    return `Down after ${n === 1 ? 'one miss' : `${n} misses in a row`}, about ${about}.`;
  };
  // The rows, with Depends on: any other sensor, but not one that depends on this one already.
  const drawAlerts = () => {
    const mine = new Set(editing ? [editing.id, ...behindOf(editing.id).map((x) => x.id)] : []);
    const above = sensors.filter((x) => !mine.has(x.id));
    if (vals.dependsOn && !above.some((x) => x.id === vals.dependsOn)) vals.dependsOn = '';
    $('sd-alerts').innerHTML = timeRows(vals.alert) + alRow('Depends on', 'While that one is down, this one waits instead of alerting.',
      `<select data-al="dep" aria-label="Depends on"><option value="">Nothing</option>${above.map((x) => `<option value="${esc(x.id)}"${x.id === vals.dependsOn ? ' selected' : ''}>${esc(x.name)} · ${esc(where(x))}</option>`).join('')}</select>`);
    drawSum();
  };
  const drawSum = () => {
    const dep = sensors.find((x) => x.id === vals.dependsOn);
    $('sd-alert-sum').innerHTML = `<p class="sd-sum">${svg('info')}<span>${esc(`${downText()} ${alertWords(vals.alert, chans ? vals.channels || [] : null)}${dep ? ` Says nothing while ${dep.name} is down.` : ''}`)}</span></p>`;
  };
  const drawSend = () => {
    $('sd-send-wrap').hidden = !chans;
    $('sd-send').innerHTML = sendCards(vals.channels || []);
    drawSum();
  };
  $('sd-send').addEventListener('click', (e) => {
    vals.channels ||= [];
    sendClick(e, vals.channels, drawSend);
  });
  $('sd-alerts').addEventListener('change', (e) => {
    if (e.target.dataset.al === 'dep') vals.dependsOn = e.target.value;
    else if (!timeChange(e, vals.alert)) return;
    drawAlerts();
  });
  $('sd-copy').addEventListener('click', () => openCopy({ alert: vals.alert, channels: chans ? vals.channels || [] : null, from: editing, group: $('sd-group').value }));

  // --- Use these alerts on other sensors: how many change at once, now that each sensor has its own --------------------------

  let copying = null; // { alert, channels, from, group, ids }
  const copyable = () => sensors.filter((x) => x.id !== copying.from?.id);
  const openCopy = ({ alert, channels, from = null, group: g = '', all = false }) => {
    copying = { alert: { ...alert }, channels: channels ? [...channels] : null, from, group: g, ids: new Set() };
    if (all) copyable().forEach((x) => copying.ids.add(x.id));
    $('cp-title').textContent = from ? `Use ${from.name}'s alerts on other sensors` : all ? 'Use these on every sensor' : 'Use these alerts on other sensors';
    $('cp-lead').textContent = `${alertWords(copying.alert, copying.channels)} What each one depends on stays as it is.`;
    $('cp-find').value = '';
    say('cp-problem', '');
    drawCopy();
    $('copy-dialog').showModal();
  };
  const drawCopyFoot = () => {
    const n = copying.ids.size;
    $('cp-go').textContent = n ? `Copy to ${n} sensor${n === 1 ? '' : 's'}` : 'Copy';
    $('cp-go').disabled = !n;
    const g = copying.group && copying.group !== NEW_GROUP ? copying.group : '';
    const inGroup = g ? copyable().filter((x) => x.group === g) : [];
    $('cp-all').textContent = inGroup.length && !inGroup.every((x) => copying.ids.has(x.id)) ? `Tick the ${g} group` : n === copyable().length && n ? 'Untick them all' : 'Tick them all';
  };
  const drawCopy = () => {
    const q = $('cp-find').value.trim().toLowerCase();
    const list = copyable().filter((x) => !q || [x.name, x.target, x.group, ...(x.tags || [])].some((v) => String(v || '').toLowerCase().includes(q)));
    $('cp-list').innerHTML = list.map((x) => `<label class="row sd-watch"><input type="checkbox" data-cp="${esc(x.id)}"${copying.ids.has(x.id) ? ' checked' : ''}>`
      + `<span class="t"><b>${esc(x.name)}</b><span>${esc(`${check(x)} · ${where(x)} · ${x.alert?.after < 0 ? 'never alerts' : `alerts ${alertShort(x.alert).toLowerCase()}`}`)}</span></span>`
      + `<span class="cp-g">${esc(x.group || '')}</span></label>`).join('') || `<p class="hint">${sensors.length > 1 ? 'Nothing matches.' : 'There are no other sensors yet.'}</p>`;
    drawCopyFoot();
  };
  $('cp-find').addEventListener('input', drawCopy);
  $('cp-list').addEventListener('change', (e) => {
    const id = e.target.dataset.cp;
    if (!id) return;
    if (e.target.checked) copying.ids.add(id);
    else copying.ids.delete(id);
    drawCopyFoot();
  });
  $('cp-all').addEventListener('click', () => {
    const word = $('cp-all').textContent;
    const g = copying.group;
    if (word.startsWith('Tick the ')) copyable().filter((x) => x.group === g).forEach((x) => copying.ids.add(x.id));
    else if (word.startsWith('Untick')) copying.ids.clear();
    else copyable().forEach((x) => copying.ids.add(x.id));
    drawCopy();
  });
  $('cp-cancel').addEventListener('click', () => $('copy-dialog').close());
  $('cp-go').addEventListener('click', async () => {
    $('cp-go').disabled = true;
    try {
      const r = await R.send('/api/sensors/copy', { ...copying.alert, channels: copying.channels, ids: [...copying.ids] });
      take(r.now);
      $('copy-dialog').close();
      draw();
      if (R.where()[0] === 'settings') drawStart();
      R.toast(`Changed the alerts on ${r.changed} sensor${r.changed === 1 ? '' : 's'}.`);
    } catch (err) {
      say('cp-problem', err.message);
      drawCopyFoot();
    }
  });

  // --- Settings, Sensors: when new sensors alert and where, and gathering alerts -----------------------------------------------

  const drawStart = () => {
    $('ss-rows').innerHTML = timeRows(start);
    $('ss-send').closest('.ds-row').hidden = !chans;
    $('ss-send').innerHTML = sendCards(startChannels() || []);
    $('ss-gather').checked = gather.on;
    $('ss-secs').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', Number(b.dataset.secs) === gather.secs));
  };
  $('ss-rows').addEventListener('change', async (e) => {
    const x = { ...start };
    if (!timeChange(e, x)) return;
    try {
      take(await R.send('/api/sensors/start', x));
      say('sn-set-problem', '');
    } catch (err) {
      say('sn-set-problem', err.message);
    }
    drawStart();
  });
  // A channel ticked here is one new sensors start with: its On for new sensors.
  $('ss-send').addEventListener('click', async (e) => {
    if (e.target.closest('[data-chan-add]')) return openChannel(null, () => drawStart()); // a new channel is on for new sensors unless switched off
    const b = e.target.closest('[data-chan]');
    if (!b) return;
    const id = b.dataset.chan;
    try {
      chans = await R.send(id === 'email' ? '/api/channels/email' : `/api/channels/${encodeURIComponent(id)}`, { onForNew: !startChannels().includes(id) });
      say('sn-set-problem', '');
    } catch (err) {
      say('sn-set-problem', err.message);
    }
    drawStart();
  });
  const setGather = async (change) => {
    try {
      take(await R.send('/api/sensors/gather', { ...gather, ...change }));
      say('sn-set-problem', '');
    } catch (err) {
      say('sn-set-problem', err.message);
    }
    drawStart();
  };
  $('ss-gather').addEventListener('change', () => setGather({ on: $('ss-gather').checked }));
  $('ss-secs').addEventListener('click', (e) => {
    const b = e.target.closest('[data-secs]');
    if (b) setGather({ secs: Number(b.dataset.secs) });
  });
  $('ss-all').addEventListener('click', () => openCopy({ alert: start, channels: startChannels(), all: true }));

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
    $('ml-body').querySelectorAll('[data-for]').forEach((el) => { el.hidden = !el.dataset.for.split(' ').includes(p); });
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
    $('ml-password').value = '';
    $('ml-secret').value = '';
    setProvider(provider);
  };
  // The mail server fields filled in with what's saved; false when it couldn't be read (said in problem).
  const fillMail = async (problem) => {
    try {
      kept = await R.get('/api/mail');
    } catch (err) {
      say(problem, err.message);
      return false;
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
    return true;
  };
  const openMail = async () => {
    say('ml-problem', '');
    say('ml-done', '');
    if (!await fillMail('sn-problem')) return;
    $('ml-done').before($('ml-body')); // back from Add a channel, if it was there
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
  // Settings: Notifications has the channels, email among them; Sensors has when new sensors alert, gathering and credentials.
  $('sn-mail').addEventListener('click', () => { location.hash = '#settings/notifications'; });
  const drawNotes = () => {
    drawChannels();
    if (can('sensors.alerts')) drawStart();
  };
  const KINDS = { user: 'Username and password', key: 'API key', community: 'SNMP v2c community', snmpv3: 'SNMPv3 user' };
  const drawCreds = (list) => {
    $('sn-creds').innerHTML = list.length ? list.map((l) => `<div class="ds-row"><div class="ds-l"><b>${esc(l.name)}</b><span>${esc([KINDS[l.kind] || 'Credentials', l.username].filter(Boolean).join(' · '))}</span></div>`
      + `<div class="ds-r"><span class="ds-v">${l.used} sensor${l.used === 1 ? '' : 's'}</span></div></div>`).join('') : '<p class="ds-empty">None yet. Credentials are saved when you add a sensor that signs in.</p>';
  };
  const loadNotes = async () => {
    try {
      if (can('sensors.alerts')) {
        kept = await R.get('/api/mail');
        chans = await R.get('/api/channels');
      }
      take(await R.get('/api/sensors'));
      if (can('sensors.edit')) drawCreds(await R.get('/api/sensors/logins'));
      say('nt-problem', '');
      say('sn-set-problem', '');
    } catch (err) {
      say(R.where()[1] === 'sensors' ? 'sn-set-problem' : 'nt-problem', err.message);
      R.skeleton($('sn-creds'), '');
    }
    drawNotes();
  };
  $('mail-dialog').addEventListener('close', () => { if (R.where()[0] === 'settings' && ['notifications', 'sensors'].includes(R.where()[1])) loadNotes(); });
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'settings' && ['notifications', 'sensors'].includes(e.detail[1]) && (can('sensors.alerts') || can('sensors.edit'))) loadNotes();
  });

  // --- Channels, in Settings, Notifications: where alerts go besides email, each set up once and picked on each sensor -----

  const CH_SUB = { discord: 'A channel webhook', teams: 'A Workflows webhook', slack: 'An incoming webhook', ntfy: 'Pushes to your phone', webhook: 'JSON to any address' };
  const CH_HELP = {
    discord: ['Webhook address', 'https://discord.com/api/webhooks/…', 'In Discord: channel settings, Integrations, Webhooks, New Webhook, Copy Webhook URL.'],
    teams: ['Webhook address', 'https://…', 'In Teams: the channel menu, Workflows, Post to a channel when a webhook request is received. Copy the address it gives you.'],
    slack: ['Webhook address', 'https://hooks.slack.com/services/…', 'In Slack: add the Incoming Webhooks app to a channel and copy its Webhook URL.'],
    ntfy: ['Topic address', 'https://ntfy.sh/lab-alerts', 'The ntfy server and a topic. Subscribe to the same topic in the ntfy app on your phone. Anyone who knows the topic can read it, so make it hard to guess.'],
    webhook: ['Address', 'https://example.com/hooks/lab', 'Alerts are posted as JSON: event (down, up, warn), subject, facts, sentBy and at.'],
  };
  const usedBy = (n) => (n ? `${n} sensor${n === 1 ? ' uses' : 's use'} it` : 'No sensor uses it yet');
  // The mail server in a few words: Microsoft 365 · from alerts@example.com to you@example.com.
  const mailWords = (m) => {
    const to = m.to?.length ? ` to ${m.to.join(', ')}` : ', nobody to send to yet';
    const from = m.from || m.username;
    return m.provider === 'm365' ? `Microsoft 365 · from ${from}${to}` : m.host === 'smtp.gmail.com' ? `Gmail · from ${from}${to}` : `${m.host}:${m.port || 587} · from ${from}${to}`;
  };
  const drawChannels = () => {
    if (!chans) return;
    const row = (id, k, name, sub, open, test) => `<div class="ds-row"><span class="chan-ic ${k}">${chIcon(k)}</span><div class="ds-l"><b>${esc(name)}</b><span>${esc(sub)}</span></div>`
      + `<div class="ds-r">${test ? `<button type="button" class="ds-btn" data-ch-test="${esc(id)}">Test</button>` : ''}<button type="button" class="ds-btn" ${open}>${test ? 'Edit' : 'Set up'}</button></div></div>`;
    const e = chans.email;
    const m = kept || {};
    const set = m.provider === 'm365' ? !!m.tenant : !!m.host;
    $('nt-ch-list').innerHTML = row('email', 'email', 'Email', set ? `${mailWords(m)} · ${usedBy(e.uses)}` : 'Not set up yet: add your mail server', 'data-ch-mail', set && e.ready)
      + chans.channels.map((c) => row(c.id, c.kind, `${CH_KIND[c.kind]}${c.name ? ` · ${c.name}` : ''}`, `${c.where} · ${usedBy(c.uses)}`, `data-ch-edit="${esc(c.id)}"`, true)).join('');
  };
  $('nt-ch-list').addEventListener('click', async (e) => {
    if (e.target.closest('[data-ch-mail]')) return openMail();
    const b = e.target.closest('[data-ch-edit]');
    if (b) return openChannel(chans.channels.find((c) => c.id === b.dataset.chEdit));
    // Test: a test message to that channel, or a test email to the alert list. The toast says how it went.
    const t = e.target.closest('[data-ch-test]');
    if (!t) return;
    const id = t.dataset.chTest;
    t.disabled = true;
    try {
      await R.send(id === 'email' ? '/api/mail/test' : `/api/channels/${encodeURIComponent(id)}/test`, {});
      R.toast(id === 'email' ? 'Sent a test email' : `Sent a test to ${chName(id)}`, id === 'email' ? `to ${(kept?.to || []).join(', ')}. Check the junk folder too.` : 'Look for it there.');
    } catch (err) {
      R.toast(id === 'email' ? "The test email didn't go" : `${chName(id)} didn't take the test`, err.message, { tone: 'warn' });
    }
    t.disabled = false;
  });
  $('nt-ch-add').addEventListener('click', () => openChannel(null));

  // Add a channel, or change one: its kind (only when it's new), the address, a name, and whether new sensors start with it.
  let chEditing = null;
  let chKind = 'discord';
  let chThen = null; // what to do with a new channel's id, when it was added from a sensor
  let mailFilled = false; // the mail server fields have what's saved, for Email in here
  const drawChKinds = () => {
    $('ch-kinds').hidden = !!chEditing;
    // Email is one of the kinds while there's no mail server: picked, the mail server fields show in here instead.
    const kinds = [...(chans && !chans.email.ready ? ['email'] : []), ...Object.keys(CH_HELP)];
    $('ch-kinds').innerHTML = kinds.map((k) => `<button type="button" class="chan${k === chKind ? ' on' : ''}" data-ch-kind="${k}" aria-pressed="${k === chKind}">`
      + `<span class="chan-ic ${k}">${chIcon(k)}</span><span><b>${CH_KIND[k]}</b><small>${k === 'email' ? 'Through your mail server' : CH_SUB[k]}</small></span></button>`).join('');
    const mail = chKind === 'email';
    $('ch-form').hidden = mail;
    $('ch-mail').hidden = !mail;
    if (mail) {
      if (!$('ch-mail').contains($('ml-body'))) $('ch-mail').append($('ml-body'));
      return;
    }
    const [label, ph, help] = CH_HELP[chKind];
    $('ch-url-label').textContent = label;
    $('ch-url').placeholder = chEditing ? 'Leave it empty to keep the saved one' : ph;
    $('ch-help').textContent = help;
  };
  // The mail server fields go home when the dialog closes, for the Email dialog.
  $('channel-dialog').addEventListener('close', () => $('ml-done').before($('ml-body')));
  const openChannel = (c, then = null) => {
    mailFilled = false;
    chEditing = c || null;
    chKind = c?.kind || missing().find((k) => k !== 'email') || 'discord'; // a new one starts on a kind there isn't one of yet
    chThen = then;
    $('ch-title').textContent = c ? `Change ${CH_KIND[c.kind]}${c.name ? ` · ${c.name}` : ''}` : 'Add a channel';
    $('ch-url').value = '';
    $('ch-name').value = c?.name || '';
    $('ch-new').checked = c ? c.onForNew : true;
    $('ch-delete').hidden = !c;
    $('ch-save').textContent = c ? 'Save' : 'Add the channel';
    say('ch-done', '');
    say('ch-problem', '');
    drawChKinds();
    $('channel-dialog').showModal();
    $('ch-url').focus();
  };
  $('ch-kinds').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-ch-kind]');
    if (!b) return;
    say('ch-done', '');
    say('ch-problem', '');
    chKind = b.dataset.chKind;
    if (chKind === 'email' && !mailFilled) mailFilled = await fillMail('ch-problem');
    drawChKinds();
    (chKind === 'email' ? [...$('ml-body').querySelectorAll('input')].find((i) => i.offsetParent) : $('ch-url'))?.focus();
  });
  const chForm = () => ({ kind: chKind, name: $('ch-name').value, url: $('ch-url').value, onForNew: $('ch-new').checked });
  $('ch-test').addEventListener('click', async () => {
    say('ch-done', '');
    say('ch-problem', '');
    $('ch-test').disabled = true;
    try {
      if (chKind === 'email') {
        // A test email goes to the addresses typed, so the mail server is saved first.
        await saveMail();
        await R.send('/api/mail/test', {});
        say('ch-done', `Sent to ${kept.to.join(', ')}. Check the inbox, and the junk folder.`);
      } else {
        await R.send(chEditing && !$('ch-url').value.trim() ? `/api/channels/${encodeURIComponent(chEditing.id)}/test` : '/api/channels/test', chForm());
        say('ch-done', 'Sent a test. Look for it there.');
      }
    } catch (err) {
      say('ch-problem', err.message);
    }
    $('ch-test').disabled = false;
  });
  $('ch-save').addEventListener('click', async () => {
    $('ch-save').disabled = true;
    try {
      if (chKind === 'email') {
        // Email: the mail server saved, and ticked where it was added from once it can send.
        await saveMail();
        chans = await R.get('/api/channels');
        chThen?.(chans.email.ready ? 'email' : null);
      } else if (chEditing) {
        chans = await R.send(`/api/channels/${encodeURIComponent(chEditing.id)}`, chForm());
      } else {
        const made = await R.send('/api/channels', chForm());
        chans = made.now;
        chThen?.(made.id);
      }
      $('channel-dialog').close();
      drawChannels();
      drawSend();
    } catch (err) {
      say('ch-problem', err.message);
    }
    $('ch-save').disabled = false;
  });
  $('ch-cancel').addEventListener('click', () => $('channel-dialog').close());
  $('ch-delete').addEventListener('click', () => {
    const c = chEditing;
    if (!c) return;
    R.sure(`Delete ${CH_KIND[c.kind]}${c.name ? ` · ${c.name}` : ''}?`, c.uses ? `${usedBy(c.uses)}. Those sensors stop sending to it.` : 'No sensor uses it.', 'Delete', async () => {
      try {
        chans = await R.send(`/api/channels/${encodeURIComponent(c.id)}/delete`, {});
        $('channel-dialog').close();
        drawChannels();
      } catch (err) {
        say('ch-problem', err.message);
      }
    });
  });

  // --- Watch it, in Add a link: a sensor checks the link and alerts, set with the same rows (links.js asks for these) -----------

  let watching = null; // { on, alert, channels } for the link in the dialog; null when this person can't watch links
  const webAddress = () => {
    const url = $('ln-url').value.trim();
    return !url || /^https?:\/\//i.test(url) || !/^[a-z][a-z0-9+.-]*:\/\//i.test(url); // typed without a scheme, it's http
  };
  const drawWatch = () => {
    if (!watching) return;
    const w = watching;
    const web = webAddress();
    if (!web) w.on = false;
    $('ln-watch').innerHTML = alRow('Watch it', web ? 'Checked every minute from this server, and it alerts when it\'s down. It shows on Sensors too.' : 'Only web addresses (http:// or https://) can be watched.',
      `<input type="checkbox" class="switch" data-al="on" role="switch" aria-label="Watch it"${w.on ? ' checked' : ''}${web ? '' : ' disabled'}>`) + (w.on ? timeRows(w.alert) : '');
    $('ln-send-wrap').hidden = !w.on || !chans;
    if (w.on && chans) $('ln-send').innerHTML = sendCards(w.channels);
  };
  $('ln-watch').addEventListener('change', (e) => {
    if (e.target.dataset.al === 'on') watching.on = e.target.checked;
    else if (!timeChange(e, watching.alert)) return;
    drawWatch();
  });
  $('ln-send').addEventListener('click', (e) => sendClick(e, watching.channels, drawWatch));
  $('ln-url').addEventListener('input', () => { if (watching) drawWatch(); }); // an ssh:// link can't be watched
  R.watchLink = {
    // The Alerts part of the link dialog, for a link or a new one (null): on with its sensor's alerts, or off with what new sensors start with.
    open: async (link) => {
      watching = null;
      $('ln-alerts').hidden = true;
      if (!can('sensors.edit')) return;
      try {
        if (can('sensors.view')) take(await R.get('/api/sensors'));
        chans = can('sensors.alerts') ? await R.get('/api/channels') : null;
      } catch {
        return; // without the sensors, the link is saved as it is
      }
      const s = link?.sensor ? sensors.find((x) => x.id === link.sensor) : null;
      watching = { on: !!s, alert: { ...(s?.alert || start) }, channels: [...(s ? s.channels : startChannels() || [])] };
      $('ln-alerts').hidden = false;
      drawWatch();
    },
    // What to send with the link: whether it's watched and how, or null to leave it as it is.
    form: () => (!watching ? null : { on: watching.on, ...watching.alert, channels: chans ? watching.channels : null }),
  };

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
