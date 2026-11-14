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
  let limits = { cert: 21, space: 10 }; // what new sensors warn at
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
      if (!tools.busy() && !snGrid.busy()) draw();
    } catch {
      // The cards keep their kind's icon.
    }
  };
  const glyph = (s) => (s.kind === 'snmp' && (s.parts || []).some((p) => p.key === 'battery') ? 'power' : GLYPH[s.kind] || 'activity');

  // The last hour of reply times as a line. Checks with no reply leave a gap; the line keeps the reply colour while it's down.
  const line = (recent, status) => {
    const box = (inner) => `<svg class="sn-line" viewBox="0 0 300 40" preserveAspectRatio="none" aria-hidden="true">${inner}</svg>`;
    if (!recent.length || status === 'paused') return `<div class="sn-chart">${box('<path class="idle" d="M0 30H300"/>')}</div>`;
    const top = Math.max(...recent, 1) * 1.25;
    const y = (n) => 36 - (n / top) * 30;
    const step = 300 / Math.max(recent.length - 1, 1);
    let d = '';
    let open = false;
    recent.forEach((n, i) => {
      if (n < 0) {
        open = false;
        return;
      }
      d += `${open ? 'L' : 'M'}${(i * step).toFixed(1)} ${y(n).toFixed(1)}`;
      open = true;
    });
    const end = recent[recent.length - 1];
    const hue = status === 'down' || status === 'wait' ? 'up' : status;
    return `<div class="sn-chart s-${hue}">${box(`<path d="${d}"/>`)}${end >= 0 ? `<i data-top="${(y(end) / 40 * 100).toFixed(1)}"></i>` : ''}</div>`;
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
    // The title says Sensors, as Links says Links; how they're doing is the line under it.
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

  // --- The Rows view's cells, one per column (the columns are grid.js's) ------------------------------------------------
  const CELL = {
    state: (s) => `<span class="state ${s.status}">${s.status === 'down' && s.since ? `Down ${short(s.since)}` : s.status === 'paused' && s.pausedUntil ? `Until ${clock(s.pausedUntil)}` : WORD[s.status] || s.status}</span>`,
    name: (s) => `<div class="nm"><span class="lic">${faces[s.id] ? `<img src="${esc(faces[s.id])}" alt="">` : svg(glyph(s))}</span><b>${esc(s.name)}</b></div>`,
    check: (s) => `<span>${esc(check(s))}${s.waits ? ` · waits on ${esc(s.waits)}` : ''}</span>`,
    where: (s) => `<span class="dim" title="${esc(where(s))}">${esc(where(s))}</span>`,
    reply: (s) => `<span class="num">${esc(s.status === 'up' || s.status === 'warn' ? (s.kind === 'cert' ? days(s.note) : ['hw', 'nas', 'vmware', 'snmp', 'svc', 'disk'].includes(s.kind) ? s.note || 'All OK' : s.ms == null ? '' : ms(s.ms)) : '')}</span>`,
    line: (s) => line(s.recent, s.status),
    up: (s) => `<span class="num">${percent(s.up7)}</span>`,
    alerts: (s) => `<span class="dim">${esc(typeOf(s.type)?.name || "Don't alert")}</span>`,
  };
  // The Rows view is a data grid (grid.js): a table for each group, all sharing the same columns.
  const STATE_ORDER = { down: 0, warn: 1, wait: 2, new: 3, paused: 4, up: 5 };
  const snGrid = R.grid({
    el: $('sn-list'),
    key: 'sensors',
    cols: [
      { key: 'state', label: 'State', w: 110, on: true, get: (s) => STATE_ORDER[s.status] ?? 9, cell: CELL.state },
      { key: 'name', label: 'Sensor', w: 200, fixed: true, get: (s) => s.name, cell: CELL.name },
      { key: 'check', label: 'Check', w: 110, on: true, get: (s) => check(s), cell: CELL.check },
      { key: 'where', label: 'Address', w: 170, on: true, get: (s) => where(s), cell: CELL.where },
      { key: 'reply', label: 'Reply', w: 100, on: true, get: (s) => s.ms ?? '', cell: CELL.reply },
      { key: 'line', label: 'Last hour', w: 200, on: true, nosort: true, get: () => '', cell: CELL.line },
      { key: 'up', label: 'Up, 7 days', w: 90, on: true, get: (s) => s.up7 ?? '', cell: CELL.up },
      { key: 'alerts', label: 'Alert type', w: 110, on: true, get: (s) => typeOf(s.type)?.name || '', cell: CELL.alerts },
    ],
    row: (s) => ` data-open="${esc(s.id)}"`,
    rowClass: (s) => `sn s-${s.status}`,
    redraw: () => draw(),
  });

  // --- The sensors, in their groups ----------------------------------------------------------------------------------------

  // The card's one line: reply time and the week for a timed check that's up, else what the last check said.
  const said = (s) => (s.status === 'down' ? `${s.note || 'No reply'}${s.since ? ` for ${long(s.since)}` : ''}`
    : s.status === 'wait' ? `Waits on ${s.waits}`
    : s.status === 'paused' ? (s.pausedUntil ? `Paused until ${clock(s.pausedUntil)}` : 'Paused until you resume it')
    : s.status === 'new' ? 'Not checked yet'
    : TIMED.includes(s.kind) && s.status === 'up' && !s.invert ? [s.ms == null ? '' : ms(s.ms), s.up7 == null ? '' : `${percent(s.up7)} this week`].filter(Boolean).join(' · ') || 'Up'
    : s.note || WORD[s.status]);
  const quiet = (text) => `<span class="sn-quiet">${esc(text)}</span>`;
  // The last hour of replies, smoothed. Checks with no reply leave a gap.
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
    return `<svg class="sn-trace" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" aria-hidden="true">${d ? `<path d="${d}"/>` : ''}</svg>`;
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
        : view === 'grid' ? snGrid.table(list) : `<div class="ltiles sn-tiles">${list.map(card).join('')}</div>`)
      + '</section>').join('')
      || (sensors.length ? '<p class="hint">Nothing matches.</p>' : '');
    if (!find && group !== DOWN) $('sn-list').insertAdjacentHTML('beforeend', tools.out(by.some(([g]) => !g)));
    tools.settle();
    $('sn-cols').hidden = view !== 'grid';
    $('sn-empty').hidden = sensors.length > 0 || groups.length > 0;
    paint($('sn-list'));
  };

  // What the server answers to a read or a change: the sensors and groups in order, when new sensors alert, and gathering.
  const take = (answer) => {
    sensors = answer.sensors;
    groups = answer.groups || [];
    types = answer.types || types;
    startType = answer.startType || startType;
    limits = answer.limits || limits;
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
    if (!tools.busy() && !snGrid.busy()) draw();
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
  $('sn-cols').addEventListener('click', () => snGrid.menu($('sn-cols')));

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
    const a = s.alert;
    $('sv-title').textContent = s.name;
    $('sv-what').textContent = `${check(s)} · ${where(s)} · every ${{ 30: '30 seconds', 60: 'minute', 300: '5 minutes', 3600: 'hour' }[s.every] || `${s.every} s`} · ${a.after < 0 ? 'never alerts' : `${typeOf(s.type)?.name || 'alerts'}: ${alertShort(a).toLowerCase()}`}`;
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
    if (!b) return;
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
  const SEAT_HINT = () => `<p class="hint">Checked by the ${R.esc(R.app())} client on that computer, which runs as SYSTEM. No credentials needed.</p>`;
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
      + choice('warn', 'Warn when it has less than', [[14, '14 days'], [21, '21 days'], [30, '30 days'], [60, '60 days']], { value: limits.cert }),
    hw: () => field('target', 'iDRAC, iLO or BMC address', { wide: true, ph: '192.168.2.7' }) + signIn('An iDRAC or iLO user with the Read Only role is enough.'),
    svc: () => seatPick() + field('service', 'Service', { wide: true, max: 256, ph: 'Spooler or Print Spooler' }) + SEAT_HINT(),
    disk: () => seatPick() + field('drive', 'Drive', { max: 3, ph: 'C' })
      + choice('warnFree', 'Warn when free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: limits.space }) + SEAT_HINT(),
    snmp: () => field('target', 'Address', { wide: true, ph: '192.168.2.30 or ups.example.com' })
      + choice('value', 'Value to watch', [['uptime', 'Uptime: it answers'], ['ups', 'UPS battery and power'], ['iface', 'Interface up'], ['oid', 'Custom OID']], { value: 'uptime', wide: true })
      + (vals.value === 'iface' ? field('iface', 'Interface', { wide: true, max: 100, ph: 'Port 5 or eth0' }) + '<p class="hint">As the device names it. When the name is not found, Check now lists the names it has.</p>' : '')
      + (vals.value === 'oid' ? field('oid', 'OID', { wide: true, max: 300, ph: '1.3.6.1.2.1.1.3.0' }) : '')
      + picks('snmpVersion', 'Version', [['', 'v2c'], ['v3', 'v3']]) + signIn(''),
    vmware: () => field('target', 'ESXi or vCenter address', { wide: true, ph: 'vcenter.example.com' }) + signIn('A vSphere user with the Read-only role is enough.')
      + choice('warnFree', 'Warn when datastore free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: limits.space }),
    nas: () => picks('product', 'Product', [['', 'TrueNAS'], ['synology', 'Synology']])
      + field('target', 'Address', { wide: true, ph: vals.product === 'synology' ? 'backup.example.com (port 5001 unless you add another)' : 'truenas.example.com' })
      + (vals.product === 'synology' ? signIn('DSM only gives storage details to an account in the administrators group. Turn off 2-step verification for it.')
        : signIn('Make one in TrueNAS under Credentials, API Keys.'))
      + choice('warnFree', 'Warn when free space is below', [[5, '5 %'], [10, '10 %'], [20, '20 %']], { value: limits.space }),
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

  // Alert types: named ways to alert, set up in Settings, Alert rules (alerts.js). A sensor or a watched link picks one, or none.
  let types = [];
  let startType = '';
  const typeOf = (id) => types.find((t) => t.id === id);
  const mins = (m) => (m === 60 ? 'an hour' : m === 1 ? 'a minute' : `${m} minutes`);
  // An alert type in words: After 5 minutes down, once, and an all-clear when it's back, to Rabbid Lab Links.
  const typeWords = (id) => {
    const t = typeOf(id);
    if (!t) return 'It never alerts: it only shows here.';
    const to = can('sensors.alerts') && R.notes.list() ? R.notes.words(t.to) : '';
    return `${t.after ? `After ${mins(t.after)} down` : 'As soon as it\'s down'}, ${t.again ? `again every ${{ 60: 'hour', 360: '6 hours', 1440: 'day' }[t.again]} while it stays down` : 'once'}`
      + `${t.back ? ', and an all-clear when it\'s back' : ''}${to ? `, to ${to}` : ''}.${t.to.length ? '' : ' It has nowhere to send yet.'}`;
  };
  const alRow = (name, about, right) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">${right}</div></div>`;
  const NEW_TYPE = ' new';
  // Alert type: the types by name, Don't alert, and New alert type… for people who may set alerts up.
  const typeRow = (id) => alRow('Alert type', esc(typeWords(id)), `<select data-al="type" aria-label="Alert type">`
    + types.map((t) => `<option value="${esc(t.id)}"${t.id === id ? ' selected' : ''}>${esc(t.name)}</option>`).join('')
    + `<option value="none"${id === 'none' ? ' selected' : ''}>Don't alert</option>${can('sensors.alerts') ? `<option value="${NEW_TYPE}">New alert type…</option>` : ''}</select>`);
  // New alert type… opens its dialog; once it's added it's the one picked, and until then the old pick stays.
  const pickType = (e, current, set) => {
    if (e.target.value !== NEW_TYPE) return set(e.target.value);
    e.target.value = current;
    return R.alertTypes.open(null, async (id) => {
      take(await R.get('/api/sensors'));
      set(id);
    });
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
    warnDays: kind === 'cert' ? num(vals.warn ?? limits.cert) : null, login: signs() && vals.login !== NEW_LOGIN ? vals.login : null,
    newLogin: signs() && vals.login === NEW_LOGIN ? { name: vals.loginName || '', username: vals.username || '', password: vals.password || '', kind: loginKind(),
      auth: vals.auth || 'SHA-256', privacy: vals.privacy || 'AES-128', privacyPassword: vals.privacyPassword || '' } : null,
    value: kind === 'snmp' ? vals.value || 'uptime' : null, oid: kind === 'snmp' ? vals.oid || '' : null, interface: kind === 'snmp' ? vals.iface || '' : null,
    watch: signs() ? vals.watch || null : null, pin: signs() ? vals.pin || null : null,
    product: kind === 'nas' ? vals.product || '' : null, warnFree: ['nas', 'vmware', 'disk'].includes(kind) ? num(vals.warnFree ?? limits.space) : null, name: $('sd-name').value,
    group: $('sd-group').value === NEW_GROUP ? $('sd-group-new').value : $('sd-group').value, every: Number($('sd-every').value),
    rule: vals.rule, dependsOn: vals.dependsOn || '',
    tags: vals.tags || [], downAfter: Number($('sd-down').value), certDays: kind === 'web' ? vals.certDays || null : null,
    invert: ['ping', 'port', 'web'].includes(kind) && !!vals.invert, timeout: kind in WAIT ? vals.timeout || null : null });
  // A sensor to change, or null for a new one, which starts in the group asked for or the one being looked at.
  const edit = async (s, into) => {
    editing = s || null;
    Object.keys(vals).forEach((k) => delete vals[k]);
    if (s) Object.assign(vals, { target: s.target, port: s.port ?? '', server: s.server || '', expect: s.expect || '', text: s.text || '', address: s.address || '', warn: s.warnDays ?? 21,
      login: s.login || undefined, watch: s.watch || null, pin: s.pin || null, product: s.product === 'synology' ? 'synology' : '', warnFree: s.warnFree ?? 10, value: s.value || 'uptime', oid: s.oid || '', iface: s.interface || '', seat: s.seat || undefined, service: s.service || '', drive: s.drive || '' });
    // What More, Tags and Send to start with: a new web page warns 14 days before its certificate expires.
    Object.assign(vals, { tags: [...(s?.tags || [])], certDays: s ? s.certDays || 0 : 14, invert: !!s?.invert, timeout: s?.timeout || null });
    // Its alert type: a new one starts with the usual one.
    Object.assign(vals, { rule: s ? s.type : startType, dependsOn: s?.dependsOn || '' });
    logins = await R.get('/api/sensors/logins').catch(() => []);
    seats = await R.get('/api/sensors/seats').catch(() => []);
    if (can('sensors.alerts')) await R.notes.load();
    $('sd-find').value = '';
    $('sd-tag-in').value = '';
    drawTags();
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
    return `<p class="sd-pin">${svg('alert')}<span>It has a self-signed certificate. ${esc(R.app())} trusts this one, <code title="${esc(pin)}">SHA-256 ${esc(short)}</code>, and warns you if it changes.</span></p>`;
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
  // The rows: its alert type, and Depends on: any other sensor, but not one that depends on this one already.
  const drawAlerts = () => {
    const mine = new Set(editing ? [editing.id, ...behindOf(editing.id).map((x) => x.id)] : []);
    const above = sensors.filter((x) => !mine.has(x.id));
    if (vals.dependsOn && !above.some((x) => x.id === vals.dependsOn)) vals.dependsOn = '';
    $('sd-alerts').innerHTML = typeRow(vals.rule) + alRow('Depends on', 'While that one is down, this one waits instead of alerting.',
      `<select data-al="dep" aria-label="Depends on"><option value="">Nothing</option>${above.map((x) => `<option value="${esc(x.id)}"${x.id === vals.dependsOn ? ' selected' : ''}>${esc(x.name)} · ${esc(where(x))}</option>`).join('')}</select>`);
    drawSum();
  };
  const drawSum = () => {
    const dep = sensors.find((x) => x.id === vals.dependsOn);
    $('sd-alert-sum').innerHTML = `<p class="sd-sum">${svg('info')}<span>${esc(`${downText()}${dep ? ` Says nothing while ${dep.name} is down.` : ''}`)}</span></p>`;
  };
  $('sd-alerts').addEventListener('change', (e) => {
    if (e.target.dataset.al === 'type') {
      pickType(e, vals.rule, (id) => {
        vals.rule = id;
        drawAlerts();
      });
      return;
    }
    if (e.target.dataset.al !== 'dep') return;
    vals.dependsOn = e.target.value;
    drawAlerts();
  });

  // --- Settings, Sensors: the credentials sensors sign in with -----------------------------------------------------------------

  // The cogwheel's menu shuts once a row in it is picked.
  $('sn-gear-menu').addEventListener('click', (e) => { if (e.target.closest('.am-link')) $('sn-gear-menu').hidePopover(); });
  const KINDS = { user: 'Username and password', key: 'API key', community: 'SNMP v2c community', snmpv3: 'SNMPv3 user' };
  const drawCreds = (list) => {
    $('sn-creds').innerHTML = list.length ? list.map((l) => `<div class="ds-row"><div class="ds-l"><b>${esc(l.name)}</b><span>${esc([KINDS[l.kind] || 'Credentials', l.username].filter(Boolean).join(' · '))}</span></div>`
      + `<div class="ds-r"><span class="ds-v">${l.used} sensor${l.used === 1 ? '' : 's'}</span></div></div>`).join('') : '<p class="ds-empty">None yet. Credentials are saved when you add a sensor that signs in.</p>';
  };
  const loadCreds = async () => {
    try {
      drawCreds(await R.get('/api/sensors/logins'));
      say('sn-set-problem', '');
    } catch (err) {
      say('sn-set-problem', err.message);
      R.skeleton($('sn-creds'), '');
    }
  };
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'settings' && e.detail[1] === 'sensors' && can('sensors.edit')) loadCreds();
  });

  // --- Watch it, in Add a link: a sensor checks the link and alerts by the alert type picked (links.js asks for these) ----------

  let watching = null; // { on, type } for the link in the dialog; null when this person can't watch links
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
      `<input type="checkbox" class="switch" data-al="on" role="switch" aria-label="Watch it"${w.on ? ' checked' : ''}${web ? '' : ' disabled'}>`) + (w.on ? typeRow(w.type) : '');
  };
  $('ln-watch').addEventListener('change', (e) => {
    if (e.target.dataset.al === 'on') {
      watching.on = e.target.checked;
      drawWatch();
    } else if (e.target.dataset.al === 'type') {
      pickType(e, watching.type, (id) => {
        watching.type = id;
        drawWatch();
      });
    }
  });
  $('ln-url').addEventListener('input', () => { if (watching) drawWatch(); }); // an ssh:// link can't be watched
  R.watchLink = {
    // The Alerts part of the link dialog, for a link or a new one (null): on with its sensor's alert type, or off with the usual one.
    open: async (link) => {
      watching = null;
      $('ln-alerts').hidden = true;
      if (!can('sensors.edit')) return;
      try {
        if (can('sensors.view')) take(await R.get('/api/sensors'));
        if (can('sensors.alerts')) await R.notes.load();
      } catch {
        return; // without the sensors, the link is saved as it is
      }
      const s = link?.sensor ? sensors.find((x) => x.id === link.sensor) : null;
      watching = { on: !!s, type: s ? s.type : startType };
      $('ln-alerts').hidden = false;
      drawWatch();
    },
    // What to send with the link: whether it's watched and by which alert type, or null to leave it as it is.
    form: () => (!watching ? null : { on: watching.on, type: watching.type }),
    // For the bar on Links: the alert types to pick for the links ticked, and the usual one.
    types: () => types,
    start: () => startType,
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
