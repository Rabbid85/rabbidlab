// The Activity report (Reports.cs, Activity.cs on the server), in three parts: the Summary over Settings, Activity log, by
// user and by area, following what its bar picks; Report beside Export, a print preview of it (#report); and Settings,
// Reports, where it's emailed on a schedule, with every report sent. Rows open the side panel.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, say, panel, toast, get, send, where, initials } = R;
  const KINDS = R.activityKinds;
  const ORDER = Object.keys(KINDS);
  const num = (n) => Number(n || 0).toLocaleString();
  const plural = (n, one, many) => `${num(n)} ${n === 1 ? one : many}`;
  const and = (names) => (names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`);
  const time = (d) => new Date(d).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const day = (d, year = true) => new Date(d).toLocaleDateString([], { month: 'short', day: 'numeric', year: year ? 'numeric' : undefined });
  // A date and time on one line: Today 2:04 PM, or Nov 21, 2:04 PM.
  const stamp = (iso) => (new Date(iso).toDateString() === new Date().toDateString() ? `Today ${time(iso)}`
    : `${day(iso, new Date(iso).getFullYear() !== new Date().getFullYear())}, ${time(iso)}`);
  // Two days as people say them: Nov 15 to Nov 22, 2026.
  const span = (a, b) => (new Date(a).toDateString() === new Date(b).toDateString() ? day(b)
    : `${day(a, new Date(a).getFullYear() !== new Date(b).getFullYear())} to ${day(b)}`);
  const RANGE = { 1: 'today', 7: 'in the last 7 days', 30: 'in the last 30 days', 0: 'in total' };
  const RESULT = { ok: ['Completed', 'green'], warning: ['Warning', 'amber'], failed: ['Failed', 'red'] };
  const badge = (r) => { const [t, c] = RESULT[r] || RESULT.failed; return `<span class="ubadge ${c}">${t}</span>`; };
  // Rabbid Lab itself, a client, or someone not signed in: an icon, not initials.
  const machine = (name) => name.startsWith(R.app()) || name.startsWith('The client on ') || name.startsWith('Unknown user from ');
  const pic = (name, cls = '') => (machine(name) ? `<span class="pic${cls}">${svg(name.startsWith('The client on ') ? 'laptop' : 'server')}</span>`
    : `<span class="pic user${cls}">${esc(initials(name))}</span>`);
  const kindPic = (k, cls = '') => `<span class="${cls || 'al-ic'}" data-color="${KINDS[k][2]}">${svg(KINDS[k][1])}</span>`;
  const head = (p, title, sub) => `<div class="dw-head">${p}<div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>`
    + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const strip = (pairs) => `<div class="dw-strip">${pairs.map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const section = (title, body) => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3></div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const pct = (n, total) => (total ? Math.round((n / total) * 100) : 0);
  const share = (n, total) => `<span class="rp-share"><progress max="100" value="${pct(n, total)}" aria-label="${pct(n, total)}% of entries"></progress><small>${pct(n, total)}%</small></span>`;
  const red = (n) => (n ? `<b class="rp-red">${num(n)}</b>` : '<span class="muted">0</span>');
  const cellNum = (n) => `<span class="ell">${num(n)}</span>`;
  // An entry in a side panel's list; a user's own panel leaves their name out.
  const entryRow = (e, by = true) => {
    const k = R.activityKind(e.area);
    return `<div class="mrow">${kindPic(k)}<div class="two"><b>${esc(e.title)}</b><small>${esc([stamp(e.at), by && e.by, e.detail].filter(Boolean).join(' · '))}</small></div>${e.result !== 'ok' ? badge(e.result) : ''}</div>`;
  };

  // Ticks on a grid: each row's box and Select all (shift-click ticks a run), and the count on Actions and in the bar.
  const tickLead = (keyOf) => ({ w: 18, head: '<label class="tk"><input type="checkbox" data-tick-all aria-label="Select all"></label>',
    cell: (x) => `<label class="tk"><input type="checkbox" data-tick="${esc(keyOf(x))}" aria-label="Select"></label>` });
  const ticker = (o) => {
    const picked = new Set();
    let last = null;
    const ticked = () => o.rows().filter((x) => picked.has(o.keyOf(x)));
    const draw = () => {
      const keys = new Set(o.rows().map(o.keyOf));
      [...picked].forEach((k) => { if (!keys.has(k)) picked.delete(k); }); // gone from the list
      const boxes = [...o.table.querySelectorAll('[data-tick]')];
      boxes.forEach((b) => { b.checked = picked.has(b.dataset.tick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
      const all = o.table.querySelector('[data-tick-all]');
      const on = boxes.filter((b) => b.checked).length;
      if (all) {
        all.checked = !!boxes.length && on === boxes.length;
        all.indeterminate = on > 0 && on < boxes.length;
      }
      const n = picked.size;
      o.act.disabled = !n;
      o.act.querySelector('.cnt').hidden = !n;
      o.act.querySelector('.cnt').textContent = num(n);
      const shown = keys.size;
      o.count.textContent = o.loading() ? '' : n ? `${num(n)} of ${plural(shown, 'item', 'items')} selected` : plural(shown, 'item', 'items');
    };
    o.table.addEventListener('click', (e) => {
      const box = e.target.closest('.tk')?.querySelector('input');
      if (!box || e.target !== box) return; // the label's own click comes through the box
      const boxes = [...o.table.querySelectorAll('[data-tick]')];
      const set = (b) => (box.checked ? picked.add(b.dataset.tick) : picked.delete(b.dataset.tick));
      if (box.dataset.tickAll !== undefined) boxes.forEach(set);
      else {
        const i = boxes.indexOf(box);
        const [a, b] = e.shiftKey && last !== null ? [Math.min(last, i), Math.max(last, i)] : [i, i];
        boxes.slice(a, b + 1).forEach(set);
        last = i;
      }
      draw();
    });
    return { picked, ticked, draw, clear: () => { picked.clear(); draw(); } };
  };
  // A row of a grid that opens the side panel, unless the click was on its tick or a heading.
  const rowOf = (e) => (e.target.closest('.tk, .th, button, a, input') ? null : e.target.closest('[data-rp-open]'));
  // A summary row's key, user:<name> or area:<kind>, as its two halves.
  const halves = (key) => [key.slice(0, key.indexOf(':')), key.slice(key.indexOf(':') + 1)];
  const markRows = (owner, root) => root.querySelectorAll('[data-rp-open]').forEach((r) => r.classList.toggle('dw-on', panel.shows(owner, r.dataset.rpOpen)));

  // --- The Summary over the Activity log ------------------------------------------------------------------------------
  // detail: what the log's bar picks, from console.js (rabbidlab:activity). data: /api/activity/summary for it.
  const sum = { detail: null, data: null, key: '', seq: 0, problem: '', open: '' };
  const userRows = () => sum.data?.users || [];
  const areaRows = () => sum.data?.areas || [];
  const users = R.grid({
    el: $('al-users'), store: 'rabbidlab.activity', key: 'users', sort: { key: 'entries', dir: -1 }, lead: tickLead((u) => u.name),
    cols: [
      { key: 'name', label: 'User', w: 200, fixed: true, get: (u) => u.name, cell: (u) => `<div class="who">${pic(u.name, ' small')}<b class="ell">${esc(u.name)}</b></div>` },
      { key: 'entries', label: 'Entries', w: 80, on: true, get: (u) => u.entries, cell: (u) => cellNum(u.entries) },
      { key: 'signIns', label: 'Sign-ins', w: 80, on: true, get: (u) => u.signIns, cell: (u) => cellNum(u.signIns) },
      { key: 'changes', label: 'Changes', w: 80, on: true, get: (u) => u.changes, cell: (u) => cellNum(u.changes) },
      { key: 'problems', label: 'Problems', w: 80, on: true, get: (u) => u.problems, cell: (u) => red(u.problems), text: (u) => u.problems },
      { key: 'share', label: 'Share of entries', w: 160, on: true, get: (u) => u.entries, cell: (u) => share(u.entries, sum.data.total), text: (u) => `${pct(u.entries, sum.data.total)}%` },
    ],
    row: (u) => ` data-rp-open="user:${esc(u.name)}" tabindex="0"`, empty: () => sum.problem || 'No entries.', loading: () => !sum.data && !sum.problem, redraw: () => drawSummary(),
  });
  const areas = R.grid({
    el: $('al-areas'), store: 'rabbidlab.activity', key: 'areas', sort: { key: 'entries', dir: -1 }, lead: tickLead((a) => a.kind),
    cols: [
      { key: 'name', label: 'Area', w: 200, fixed: true, get: (a) => a.name, cell: (a) => `<div class="who">${kindPic(a.kind)}<b class="ell">${esc(a.name)}</b></div>` },
      { key: 'entries', label: 'Entries', w: 80, on: true, get: (a) => a.entries, cell: (a) => cellNum(a.entries) },
      { key: 'problems', label: 'Problems', w: 80, on: true, get: (a) => a.problems, cell: (a) => red(a.problems), text: (a) => a.problems },
      { key: 'share', label: 'Share of entries', w: 160, on: true, get: (a) => a.entries, cell: (a) => share(a.entries, sum.data.total), text: (a) => `${pct(a.entries, sum.data.total)}%` },
    ],
    row: (a) => ` data-rp-open="area:${esc(a.kind)}" tabindex="0"`, empty: () => sum.problem || 'No entries.', loading: () => !sum.data && !sum.problem, redraw: () => drawSummary(),
  });
  const uTicks = ticker({ table: $('al-users'), act: $('al-u-act'), count: $('al-u-count'), rows: userRows, keyOf: (u) => u.name, loading: () => !sum.data });
  const aTicks = ticker({ table: $('al-areas'), act: $('al-a-act'), count: $('al-a-count'), rows: areaRows, keyOf: (a) => a.kind, loading: () => !sum.data });

  // The line over the grids: 40 entries in the last 7 days: 15 sign-ins, 25 changes, 5 problems.
  const line = (d, s) => {
    const scope = `${RANGE[s.days]}${s.by ? ` by ${s.by}` : ''}${s.kind ? ` in ${KINDS[s.kind][0]}` : ''}${s.q ? ` matching "${s.q}"` : ''}`;
    if (!d.total) return `No ${s.problems ? 'problems' : 'entries'} ${scope}.`;
    return s.problems ? `${plural(d.total, 'problem', 'problems')} ${scope}: ${plural(d.signIns, 'sign-in', 'sign-ins')}, ${plural(d.changes, 'change', 'changes')}.`
      : `${plural(d.total, 'entry', 'entries')} ${scope}: ${plural(d.signIns, 'sign-in', 'sign-ins')}, ${plural(d.changes, 'change', 'changes')}, ${plural(d.problems, 'problem', 'problems')}.`;
  };
  const drawSummary = () => {
    if (sum.data || sum.problem) $('al-sum-lead').textContent = sum.problem && !sum.data ? sum.problem : line(sum.data, sum.detail); // else its placeholder stays
    users.draw(userRows());
    areas.draw(areaRows());
    R.paint($('al-areas'));
    uTicks.draw();
    aTicks.draw();
    markRows('activity-sum', $('al-sum'));
  };
  drawSummary();

  // Each time the log is drawn from a new answer, the summary for the same picks; only when what's picked or found changed.
  document.addEventListener('rabbidlab:activity', async (e) => {
    const d = e.detail;
    sum.detail = d;
    const key = JSON.stringify([d.kind, d.by, d.q, d.days, d.problems, d.page.total, d.page.problems, d.page.entries[0]?.at]);
    if (key === sum.key) return;
    sum.key = key;
    const mine = ++sum.seq;
    try {
      const got = await get(`/api/activity/summary?${d.query}`);
      if (mine !== sum.seq) return;
      sum.data = got;
      sum.problem = '';
    } catch (err) {
      if (mine !== sum.seq) return;
      sum.problem = err.message;
      sum.key = ''; // read again next time
    }
    drawSummary();
    if (panel.is('activity-sum') && sum.open) openSum(sum.open, true);
  });

  // A user's or an area's side panel: its counts, and its latest entries for the same picks.
  const openSum = async (key, again = false) => {
    const [what, id] = halves(key);
    const x = what === 'user' ? userRows().find((u) => u.name === id) : areaRows().find((a) => a.kind === id);
    if (!x) {
      if (again) panel.close();
      return;
    }
    sum.open = key;
    const words = `Activity ${RANGE[sum.detail.days]}`;
    panel.body.innerHTML = (what === 'user'
      ? head(pic(x.name, ' dw-pic'), x.name, words)
        + `<div class="dw-bar">${qa(`data-rp-only="${esc(key)}"`, 'filter', 'Show only this user')}${qa(`data-rp-report="${esc(key)}"`, 'printer', 'Report')}</div>`
        + strip([['Entries', num(x.entries)], ['Sign-ins', num(x.signIns)], ['Changes', num(x.changes)], ['Problems', num(x.problems)]])
      : head(kindPic(x.kind, 'pic dw-pic rp-kind'), x.name, words)
        + `<div class="dw-bar">${qa(`data-rp-only="${esc(key)}"`, 'filter', 'Show only this area')}${qa(`data-rp-report="${esc(key)}"`, 'printer', 'Report')}</div>`
        + strip([['Entries', num(x.entries)], ['Problems', num(x.problems)], ['Share of entries', `${pct(x.entries, sum.data.total)}%`]]))
      + section('Latest entries', '<div class="dw-list" id="rp-latest"></div>');
    R.paint(panel.body);
    R.skeleton($('rp-latest'), 'rows', 3);
    if (!again) panel.open('activity-sum', key);
    markRows('activity-sum', $('al-sum'));
    const p = new URLSearchParams(sum.detail.query);
    if (what === 'user') p.set('by', x.name);
    else p.set('kind', x.kind);
    p.set('count', '10');
    let html;
    try {
      const page = await get(`/api/activity/find?${p}`);
      html = page.entries.map((e) => entryRow(e, what !== 'user')).join('') || none('No entries.');
    } catch (err) {
      html = none(err.message);
    }
    const box = $('rp-latest');
    if (sum.open !== key || !panel.is('activity-sum') || !box) return;
    box.innerHTML = html;
    R.paint(box);
  };
  $('al-sum').addEventListener('click', (e) => {
    const row = rowOf(e);
    if (!row) return;
    if (panel.shows('activity-sum', row.dataset.rpOpen)) panel.close();
    else openSum(row.dataset.rpOpen);
  });
  $('al-sum').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-rp-open]')) openSum(e.target.dataset.rpOpen);
  });
  panel.el.addEventListener('close', () => { markRows('activity-sum', $('al-sum')); markRows('reports', $('rp-sent')); });

  // Show only: the log's own User, or its sub-menu's area.
  const only = (key) => {
    const [what, id] = halves(key);
    panel.close();
    if (what === 'area') {
      location.hash = `#settings/activity/${id}`;
      return;
    }
    const sel = $('al-who');
    if (![...sel.options].some((o) => o.value === id)) sel.add(new Option(id, id));
    sel.value = id;
    sel.dispatchEvent(new Event('change'));
  };
  panel.body.addEventListener('click', (e) => {
    if (!panel.is('activity-sum')) return;
    const o = e.target.closest('[data-rp-only]');
    const r = e.target.closest('[data-rp-report]');
    if (o) only(o.dataset.rpOnly);
    else if (r) {
      const [what, id] = halves(r.dataset.rpReport);
      report(what === 'user' ? { who: id } : { kinds: [id] });
    }
  });
  $('al-u-act').addEventListener('click', () => {
    const rows = uTicks.ticked();
    R.picker.menu($('al-u-act'), [
      rows.length === 1 && { label: 'Show only this user', icon: 'filter', run: () => only(`user:${rows[0].name}`) },
      rows.length === 1 && { label: 'Report on this user', icon: 'printer', run: () => report({ who: rows[0].name }) },
      { label: 'Export selected', icon: 'csv', run: () => users.csv(rows, 'activity-by-user') },
      '-',
      { label: 'Clear selection', icon: 'x', run: () => uTicks.clear() },
    ], { key: 'rp.users' });
  });
  $('al-a-act').addEventListener('click', () => {
    const rows = aTicks.ticked();
    R.picker.menu($('al-a-act'), [
      rows.length === 1 && { label: 'Show only this area', icon: 'filter', run: () => only(`area:${rows[0].kind}`) },
      { label: rows.length === 1 ? 'Report on this area' : 'Report on these areas', icon: 'printer', run: () => report({ kinds: rows.map((a) => a.kind) }) },
      { label: 'Export selected', icon: 'csv', run: () => areas.csv(rows, 'activity-by-area') },
      '-',
      { label: 'Clear selection', icon: 'x', run: () => aTicks.clear() },
    ], { key: 'rp.areas' });
  });

  // --- Report: the print preview -----------------------------------------------------------------------------------
  // r: the period (1 today, 7, 30, 'month', 0 all, 'custom' from and until, as yyyy-mm-dd). kinds: the areas in it.
  const pv = { r: 7, from: '', until: '', who: '', kinds: new Set(ORDER), problems: false, sum: null, page: null, problem: '', seq: 0 };
  const ymd = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  // Report from the log (or a row of its summary): what its bar picks, with o over it.
  const report = (o = {}) => {
    const d = sum.detail || {};
    pv.r = [1, 7, 30, 0].includes(d.days) ? d.days : 7;
    pv.who = o.who ?? d.by ?? '';
    pv.kinds = new Set(o.kinds || (d.kind ? [d.kind] : ORDER));
    pv.problems = !!d.problems;
    pv.page = null;
    pv.sum = null;
    panel.close();
    location.hash = '#report';
  };
  $('al-report').addEventListener('click', () => report());

  const pvRange = () => {
    const now = new Date();
    if (pv.r === 'custom') {
      let [a, b] = [pv.from, pv.until].map((v) => new Date(`${v}T00:00`));
      if (a > b) [a, b] = [b, a];
      const end = new Date(b);
      end.setDate(end.getDate() + 1);
      return { since: a, until: end, words: span(a, b) };
    }
    if (pv.r === 'month') {
      const f = new Date(now.getFullYear(), now.getMonth(), 1);
      return { since: f, words: span(f, now) };
    }
    if (pv.r === 1) {
      const f = new Date(now);
      f.setHours(0, 0, 0, 0);
      return { since: f, words: day(now) };
    }
    if (pv.r) {
      const f = new Date(now);
      f.setDate(f.getDate() - pv.r);
      return { since: f, words: span(f, now) };
    }
    return { words: `All entries up to ${day(now)}` };
  };
  const pvQuery = () => {
    const g = pvRange();
    const p = new URLSearchParams();
    if (pv.kinds.size < ORDER.length) p.set('kind', ORDER.filter((k) => pv.kinds.has(k)).join(','));
    if (pv.who) p.set('by', pv.who);
    if (g.since) p.set('since', g.since.toISOString());
    if (g.until) p.set('until', g.until.toISOString());
    if (pv.problems) p.set('problems', 'true');
    return p.toString();
  };
  const drawPicks = () => {
    document.querySelectorAll('#rp-range [data-r]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.r === String(pv.r))));
    $('rp-dates').hidden = pv.r !== 'custom';
    $('rp-from').value = pv.from;
    $('rp-until').value = pv.until;
    const people = [...new Set([...(pv.page?.people || []), ...(pv.who ? [pv.who] : [])])];
    $('rp-who').innerHTML = `<option value="">All users</option>${people.map((p) => `<option${p === pv.who ? ' selected' : ''}>${esc(p)}</option>`).join('')}`;
    $('rp-areas').innerHTML = ORDER.map((k) => `<button type="button" class="fchip${pv.kinds.has(k) ? ' set' : ''}" data-rp-area="${k}" aria-pressed="${pv.kinds.has(k)}">${svg(pv.kinds.has(k) ? 'check' : 'plus')}${esc(KINDS[k][0])}</button>`).join('');
    $('rp-probs').checked = pv.problems;
  };

  // The pages: letter size, the same on screen as on paper. Each table row is one unit of a page's room; a table that
  // doesn't fit goes on over the page with its headings again.
  const ROOM = 41;
  const COLS = {
    users: [['User', 'w-name'], ['Entries', 'w-n'], ['Sign-ins', 'w-n'], ['Changes', 'w-n'], ['Problems', 'w-n']],
    areas: [['Area', 'w-name'], ['Entries', 'w-n'], ['Problems', 'w-n']],
    log: [['When', 'w-when'], ['User', 'w-user'], ['Area', 'w-area'], ['Activity', 'w-act'], ['Details', 'w-det'], ['Result', 'w-res']],
  };
  const td = (v, cls = '') => `<td${cls ? ` class="${cls}"` : ''}>${esc(v)}</td>`;
  const table = (cols, rows) => `<table class="rp-tbl"><colgroup>${cols.map(([, c]) => `<col class="${c}">`).join('')}</colgroup>`
    + `<thead><tr>${cols.map(([l]) => `<th>${esc(l)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table>`;
  const paginate = (blocks) => {
    const pages = [];
    let html = '';
    let room = ROOM - 7; // the first page's heading
    const turn = () => {
      pages.push(html);
      html = '';
      room = ROOM - 2;
    };
    blocks.forEach((b) => {
      if (!b.rows) {
        if (room < b.cost) turn();
        html += b.html;
        room -= b.cost;
        return;
      }
      if (room < 3) turn(); // its heading, the table's and a row at least
      html += `<h2 class="rp-h">${esc(b.title)}</h2>`;
      room -= 1.7;
      if (!b.rows.length) {
        html += `<p class="rp-none">${esc(b.empty)}</p>`;
        room -= 1.2;
        return;
      }
      for (let i = 0; i < b.rows.length;) {
        if (room < 2) turn();
        const part = b.rows.slice(i, i + Math.max(1, Math.floor(room - 1)));
        html += table(b.cols, part);
        room -= part.length + 1;
        i += part.length;
      }
    });
    pages.push(html);
    return pages;
  };
  const brandMark = () => {
    const img = document.getElementById('brand-img');
    if (img && !img.hidden && img.src) return `<img class="rp-mark" src="${esc(img.src)}" alt="">`;
    return document.querySelector('.brand svg.mark')?.outerHTML.replace('class="mark"', 'class="rp-mark"') || '';
  };
  const drawPaper = () => {
    const stage = $('rp-stage');
    if (pv.problem) {
      stage.innerHTML = `<p class="rp-problem">${esc(pv.problem)}</p>`;
      $('rp-pages').textContent = '';
      return;
    }
    if (!pv.sum || !pv.page) return;
    const s = pv.sum;
    const g = pvRange();
    const entries = pv.page.entries;
    const strip2 = `<table class="rp-sum"><tr><th>Entries</th><th>Sign-ins</th><th>Changes</th><th>Problems</th></tr>`
      + `<tr><td>${num(s.total)}</td><td>${num(s.signIns)}</td><td>${num(s.changes)}</td><td class="${s.problems ? 'bad' : ''}">${num(s.problems)}</td></tr></table>`;
    const cut = pv.page.total > entries.length ? `<p class="rp-none">The newest ${num(entries.length)} of ${num(pv.page.total)} entries are shown. Export in the Activity log has all of them.</p>` : '';
    const pages = paginate([
      { html: strip2, cost: 3 },
      { title: 'By user', cols: COLS.users, empty: 'No entries.', rows: s.users.map((u) => `<tr>${td(u.name)}${td(num(u.entries))}${td(num(u.signIns))}${td(num(u.changes))}${td(num(u.problems), u.problems ? 'bad' : '')}</tr>`) },
      { title: 'By area', cols: COLS.areas, empty: 'No entries.', rows: s.areas.map((a) => `<tr>${td(a.name)}${td(num(a.entries))}${td(num(a.problems), a.problems ? 'bad' : '')}</tr>`) },
      { title: 'Entries', cols: COLS.log, empty: 'No entries match these settings.', rows: entries.map((e) => `<tr class="${e.result !== 'ok' ? 'bad' : ''}">${td(`${day(e.at, false)}, ${time(e.at)}`)}${td(e.by)}${td(KINDS[R.activityKind(e.area)][0])}${td(e.title)}${td(e.detail || '')}${td(RESULT[e.result]?.[0] || 'Failed')}</tr>`) },
      ...(cut ? [{ html: cut, cost: 2 }] : []),
    ]);
    const lab = $('brand-name').textContent.trim();
    const me = R.me();
    const made = `Generated ${day(new Date())}, ${time(new Date())}${me ? ` by ${me.name || me.username}` : ''}`;
    const areasWord = pv.kinds.size === ORDER.length ? 'All areas' : ORDER.filter((k) => pv.kinds.has(k)).map((k) => KINDS[k][0]).join(', ');
    stage.innerHTML = pages.map((html, i) => '<article class="rp-paper">'
      + (i === 0 ? `<header class="rp-ph">${brandMark()}<div><b>${esc(lab)}</b>${me?.domain ? `<span>${esc(me.domain)}</span>` : ''}</div><span class="grow"></span><span class="rp-made">${esc(made)}</span></header>`
        + `<h1>Activity report</h1><table class="rp-meta"><tr><th>Period</th><td>${esc(g.words)}</td><th>User</th><td>${esc(pv.who || 'All users')}</td></tr>`
        + `<tr><th>Areas</th><td>${esc(areasWord)}</td><th>Results</th><td>${pv.problems ? 'Warnings and failures' : 'All'}</td></tr></table>`
        : `<header class="rp-ph small"><b>Activity report</b><span>${esc(g.words)}</span></header>`)
      + `<div class="rp-body">${html}</div><footer class="rp-pf"><span>${esc(lab)} · Activity report</span><span>Page ${i + 1} of ${pages.length}</span></footer></article>`).join('');
    $('rp-pages').textContent = `${plural(pv.page.total, 'entry', 'entries')} · ${plural(pages.length, 'page', 'pages')}`;
  };
  const pvLoad = async () => {
    const mine = ++pv.seq;
    drawPicks();
    if (!pv.kinds.size) {
      pv.problem = 'Select at least one area.';
      drawPaper();
      return;
    }
    if (!pv.page) {
      $('rp-pages').textContent = '';
      R.skeleton($('rp-stage'), 'rows', 6);
    }
    try {
      const q = pvQuery();
      const [s, page] = await Promise.all([get(`/api/activity/summary?${q}`), get(`/api/activity/find?${q}&count=5000`)]);
      if (mine !== pv.seq) return;
      pv.sum = s;
      pv.page = page;
      pv.problem = '';
    } catch (err) {
      if (mine !== pv.seq) return;
      pv.problem = err.message;
    }
    drawPicks();
    drawPaper();
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'report' && has('activity.view')) pvLoad(); });
  $('rp-range').addEventListener('click', (e) => {
    const b = e.target.closest('[data-r]');
    if (!b) return;
    pv.r = /^\d+$/.test(b.dataset.r) ? Number(b.dataset.r) : b.dataset.r;
    if (pv.r === 'custom' && !pv.from) {
      const a = new Date();
      a.setDate(a.getDate() - 7);
      [pv.from, pv.until] = [ymd(a), ymd(new Date())];
    }
    pvLoad();
  });
  ['rp-from', 'rp-until'].forEach((id) => $(id).addEventListener('change', () => {
    if (!$(id).value) return;
    pv[id === 'rp-from' ? 'from' : 'until'] = $(id).value;
    pvLoad();
  }));
  $('rp-who').addEventListener('change', () => { pv.who = $('rp-who').value; pvLoad(); });
  $('rp-probs').addEventListener('change', () => { pv.problems = $('rp-probs').checked; pvLoad(); });
  $('rp-areas').addEventListener('click', (e) => {
    const b = e.target.closest('[data-rp-area]');
    if (!b) return;
    if (pv.kinds.has(b.dataset.rpArea)) pv.kinds.delete(b.dataset.rpArea);
    else pv.kinds.add(b.dataset.rpArea);
    pvLoad();
  });
  // Printing: the pages alone, one to a sheet, from a copy beside the app (report.css hides the rest).
  $('rp-print').addEventListener('click', () => window.print());
  addEventListener('beforeprint', () => {
    if (where()[0] !== 'report' || !$('rp-stage').querySelector('.rp-paper')) return;
    let box = document.getElementById('rp-sheets');
    if (!box) {
      box = document.createElement('div');
      box.id = 'rp-sheets';
      document.body.append(box);
    }
    box.innerHTML = $('rp-stage').innerHTML;
    document.documentElement.classList.add('rp-printing');
  });
  addEventListener('afterprint', () => {
    document.documentElement.classList.remove('rp-printing');
    document.getElementById('rp-sheets')?.replaceChildren();
  });
  // While it's open: the summary again every 30 seconds, and the pages once it changed.
  R.live({ page: () => where()[0] === 'report' && has('activity.view'), every: 30000, hold: () => !pv.page, read: () => get(`/api/activity/summary?${pvQuery()}`), now: () => pv.sum, draw: () => pvLoad() });

  // --- Settings, Reports: the schedule, and every report sent ------------------------------------------------------
  let rp = null; // /api/reports
  let rpTo = []; // the email notifications under Send to, as picked
  const canChange = () => has('sensors.alerts') && has('activity.view');
  const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  const COVERS = { daily: 1, weekly: 7, monthly: 30 };
  const ordinal = (n) => `${n}${[11, 12, 13].includes(n) ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' })[n % 10] || 'th'}`;
  const OUT = { sent: ['Sent', 'green'], partial: ['Partially sent', 'amber'], failed: ['Failed', 'red'] };
  const TYPE = { scheduled: 'Scheduled', test: 'Test', again: 'Sent again' };
  const outBadge = (o) => `<span class="ubadge ${OUT[o]?.[1] || 'red'}">${OUT[o]?.[0] || 'Failed'}</span>`;
  $('rp-hour').innerHTML = Array.from({ length: 24 }, (_, h) => `<option value="${h}">${new Date(2026, 0, 1, h).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</option>`).join('');
  // The choices wait, empty, until the schedule is read.
  ['rp-every', 'rp-day', 'rp-hour', 'rp-covers'].forEach((id) => { $(id).selectedIndex = -1; });
  const sentRows = () => rp?.sent || [];
  const sentGrid = R.grid({
    el: $('rp-sent'), store: 'rabbidlab.reports', key: 'sent', sort: { key: 'at', dir: -1 }, lead: tickLead((r) => r.id),
    cols: [
      { key: 'at', label: 'Sent', w: 140, fixed: true, get: (r) => r.at, cell: (r) => `<span class="ell">${esc(stamp(r.at))}</span>`, text: (r) => new Date(r.at).toLocaleString() },
      { key: 'period', label: 'Period', w: 160, on: true, get: (r) => r.from, cell: (r) => `<span class="ell">${esc(r.period)}</span>`, text: (r) => r.period },
      { key: 'to', label: 'Sent to', w: 110, on: true, get: (r) => r.to.join(', ') },
      { key: 'entries', label: 'Entries', w: 70, on: true, get: (r) => r.entries, cell: (r) => cellNum(r.entries) },
      { key: 'type', label: 'Type', w: 90, on: true, get: (r) => TYPE[r.type] || '' },
      { key: 'outcome', label: 'Result', w: 120, on: true, get: (r) => r.outcome, cell: (r) => outBadge(r.outcome), word: (r) => OUT[r.outcome]?.[0] || '' },
    ],
    row: (r) => ` data-rp-open="${esc(r.id)}" tabindex="0"`, empty: 'No reports have been sent yet.', loading: () => !rp, redraw: () => drawSent(),
  });
  const sTicks = ticker({ table: $('rp-sent'), act: $('rp-act'), count: $('rp-count'), rows: sentRows, keyOf: (r) => r.id, loading: () => !rp });
  const drawSent = () => {
    sentGrid.draw(sentRows());
    sTicks.draw();
    markRows('reports', $('rp-sent'));
  };
  drawSent();
  const noteChip = (name) => `<span class="np-chip"><span class="chan-ic email">${R.notes.mark('email')}</span>${esc(name)}</span>`;
  const rpDraw = () => {
    const a = rp.activity;
    const may = canChange();
    $('rp-on').checked = a.on;
    $('rp-on-label').textContent = a.on ? 'On' : 'Off';
    $('rp-every').value = a.every;
    $('rp-day-row').hidden = a.every === 'daily';
    if ($('rp-day').dataset.every !== a.every) {
      $('rp-day').dataset.every = a.every;
      $('rp-day').innerHTML = (a.every === 'monthly' ? Array.from({ length: 28 }, (_, i) => [i + 1, `The ${ordinal(i + 1)}`]) : DAYS.map((d, i) => [i + 1, d]))
        .map(([v, t]) => `<option value="${v}">${t}</option>`).join('');
    }
    $('rp-day').value = String(a.day);
    $('rp-day-help').textContent = a.every === 'monthly' ? 'The day of the month. Days after the 28th are not listed, so that the report is sent every month.' : 'The day of the week.';
    $('rp-hour').value = String(a.hour);
    $('rp-covers').value = String(a.covers);
    // Off, the schedule shows as it was left, dimmed; turning it on is the first step.
    $('rp-on').disabled = !may;
    ['rp-every', 'rp-day', 'rp-hour', 'rp-covers'].forEach((id) => { $(id).disabled = !may || !a.on; });
    rpTo = [...a.to];
    if (may && a.on && R.notes.list()) R.notes.pick($('rp-to'), rpTo, () => save({ to: rpTo }), 'email');
    else $('rp-to').innerHTML = `<span class="np-btn">${rpTo.map((id) => noteChip(rp.emails.find((x) => x.id === id)?.name || 'Deleted notification')).join('') || '<span class="np-none">None selected</span>'}</span>`;
    $('rp-to').classList.toggle('off', !a.on);
    $('rp-next').textContent = a.next || 'Not scheduled';
    $('rp-next-help').textContent = !a.on ? 'Turn on Email the report to schedule it.' : !a.to.length ? 'Select an email notification under Send to.' : `${a.words}, using the server's time zone.`;
    $('rp-test').disabled = !may || !rpTo.length;
    $('rp-cant').hidden = may;
    drawSent();
    if (panel.is('reports') && sentOpen) openSent(sentOpen, true);
  };
  const save = async (change) => {
    const a = rp.activity;
    try {
      rp = await send('/api/reports/activity', { on: a.on, every: a.every, day: a.day, hour: a.hour, covers: a.covers, to: rpTo, ...change });
      say('rp-problem', '');
    } catch (err) {
      say('rp-problem', err.message);
    }
    rpDraw();
  };
  const rpLoad = async () => {
    try {
      const [got] = await Promise.all([get('/api/reports'), canChange() && !R.notes.list() ? R.notes.load() : null]);
      rp = got;
      say('rp-problem', '');
    } catch (err) {
      say('rp-problem', err.message);
      if (!rp) return;
    }
    rpDraw();
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'settings' && e.detail[1] === 'reports' && has('activity.view')) rpLoad(); });
  $('rp-on').addEventListener('change', () => save({ on: $('rp-on').checked }));
  // A new How often starts with its own usual period: a day, a week or 30 days.
  $('rp-every').addEventListener('change', () => {
    const every = $('rp-every').value;
    const d = rp.activity.day;
    save({ every, day: every === 'weekly' && d > 7 ? 1 : d, covers: COVERS[every] });
  });
  $('rp-day').addEventListener('change', () => save({ day: Number($('rp-day').value) }));
  $('rp-hour').addEventListener('change', () => save({ hour: Number($('rp-hour').value) }));
  $('rp-covers').addEventListener('change', () => save({ covers: Number($('rp-covers').value) }));

  // Sending: a test from the form, or a report sent before, again. Work shows in a toast that turns into how it went.
  const sentWords = (s) => (s.outcome === 'partial' ? s.why : `Sent to ${and(s.to)}. Check the junk folder too.`);
  $('rp-test').addEventListener('click', async () => {
    $('rp-test').disabled = true;
    toast('Sending a test report…', '', { stay: true });
    try {
      const r = await send('/api/reports/activity/test', { to: rpTo, covers: rp.activity.covers });
      rp = r.now;
      toast(r.sent.outcome === 'partial' ? 'Test report partially sent.' : 'Test report sent.', sentWords(r.sent), r.sent.outcome === 'partial' ? { tone: 'warn' } : {});
    } catch (err) {
      if (err.answer?.now) rp = err.answer.now;
      toast('Could not send the test report.', err.message, { tone: 'warn' });
    }
    rpDraw();
  });
  const again = async (rows) => {
    toast(rows.length === 1 ? 'Sending the report again…' : `Sending ${rows.length} reports again…`, '', { stay: true });
    const bad = [];
    for (const r of rows) {
      try {
        const a = await send(`/api/reports/sent/${encodeURIComponent(r.id)}/again`, {});
        rp = a.now;
        if (a.sent.outcome !== 'sent') bad.push(a.sent.why);
      } catch (err) {
        if (err.answer?.now) rp = err.answer.now;
        bad.push(err.message);
      }
    }
    if (!bad.length) toast(rows.length === 1 ? 'Report sent again.' : `${rows.length} reports sent again.`, rows.length === 1 ? `Sent to ${and(rows[0].to)}.` : '');
    else toast(rows.length === 1 ? 'Could not send the report again.' : `Could not send ${bad.length} of ${rows.length} reports again.`, bad[0], { tone: 'warn' });
    rpDraw();
  };

  // A sent report's side panel: when, what it covered, where it went and why not.
  let sentOpen = '';
  const openSent = (id, again2 = false) => {
    const r = sentRows().find((x) => x.id === id);
    if (!r) {
      if (again2) panel.close();
      return;
    }
    sentOpen = id;
    panel.body.innerHTML = head(`<span class="pic dw-pic">${svg('message')}</span>`, 'Activity report', r.period)
      + (canChange() ? `<div class="dw-bar">${qa('data-rp-again', 'refresh', 'Send again')}</div>` : '')
      + strip([['Sent', esc(stamp(r.at))], ['Entries', num(r.entries)], ['Type', esc(TYPE[r.type] || '')], ['Result', outBadge(r.outcome)]])
      + section(`Sent to <small>${r.to.length}</small>`, `<div class="dw-list">${r.to.map((n) => `<div class="mrow"><span class="chan-ic email">${R.notes.mark('email')}</span><div class="two"><b>${esc(n)}</b></div></div>`).join('')}</div>`)
      + (r.why ? section('What went wrong', `<p class="rp-why">${esc(r.why)}</p>`) : '')
      + section('Sent by', `<p class="rp-by">${esc(r.by)}</p>`);
    if (!again2) panel.open('reports', id);
    markRows('reports', $('rp-sent'));
  };
  $('rp-sent').addEventListener('click', (e) => {
    const row = rowOf(e);
    if (!row) return;
    if (panel.shows('reports', row.dataset.rpOpen)) panel.close();
    else openSent(row.dataset.rpOpen);
  });
  $('rp-sent').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-rp-open]')) openSent(e.target.dataset.rpOpen);
  });
  panel.body.addEventListener('click', (e) => {
    if (panel.is('reports') && e.target.closest('[data-rp-again]')) {
      const r = sentRows().find((x) => x.id === sentOpen);
      if (r) again([r]);
    }
  });
  $('rp-act').addEventListener('click', () => {
    const rows = sTicks.ticked();
    R.picker.menu($('rp-act'), [
      canChange() && { label: rows.length === 1 ? 'Send again' : `Send ${rows.length} again`, icon: 'refresh', run: () => again(rows) },
      { label: 'Export selected', icon: 'csv', run: () => sentGrid.csv(rows, 'activity-reports-sent') },
      '-',
      { label: 'Clear selection', icon: 'x', run: () => sTicks.clear() },
    ], { key: 'rp.sent' });
  });
  // While it's open: read again every 30 seconds, so a scheduled report shows up when it goes.
  R.live({ page: () => where()[0] === 'settings' && where()[1] === 'reports' && has('activity.view'), every: 30000, read: () => get('/api/reports'), now: () => rp,
    draw: (got) => { rp = got; rpDraw(); }, areas: () => [$('rp-sent')] });
})();
