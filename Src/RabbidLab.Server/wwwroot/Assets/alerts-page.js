// Alerts (EventAlerts.cs): an alert for anything that can happen. Its own page in the sidebar: Everything that can happen
// (a switch on each), Rules (the AD security alerts to start with, and every one added), Fired and Where alerts go. New
// alert and a rule's side panel take its name, how often (so many times, or so many different accounts, addresses or
// computers, in so many minutes, for each of something), conditions of our own, and then; unusual sign-ins have the
// learning period. A user's Actions › Alert me about this user, and their Alerts tab (users.js), are about one person.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, panel, toast, sure, get, send } = R;
  const may = () => has('sensors.alerts');
  const count = (n, one, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const list = (xs) => (xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`);
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const oops = (e) => { if (!e.cancelled) toast('The operation failed.', e.message, { tone: 'warn' }); };
  const same = (a, b) => !!a && !!b && a.toLowerCase() === b.toLowerCase();
  const SEV = { critical: ['Critical', 'red'], warning: ['Warning', 'amber'], info: ['Info', 'grey'] };
  const AREA_ICON = { 'Domain sign-ins': 'lock', 'Active Directory': 'users', Devices: 'monitor', 'Sensors and links': 'activity', Printers: 'printer', 'Connected apps': 'plug', 'Rabbid Lab': 'shield' };
  const SENT = { sent: 'Sent', partial: 'Partly sent', failed: 'Not sent', held: 'Held for quiet hours', nobody: 'No notifications' };
  const COMPUTERS = { '': 'Any computer', servers: 'Servers', workstations: 'Workstations' };
  const FIELD = { account: 'Account', address: 'Address', computer: 'Computer', reason: 'Reason', group: 'Group', object: 'Object', kind: 'Kind of object', by: 'Changed by', hour: 'Hour', day: 'Day' };
  const OPS = { is: 'is', isnot: 'is not', oneof: 'is one of', noneof: 'is none of', contains: 'contains', starts: 'starts with', between: 'is between' };
  const PER = { account: 'account', address: 'address', computer: 'computer', group: 'group', all: 'of them together' };
  const DISTINCT = { '': 'times', account: 'different accounts', address: 'different addresses', computer: 'different computers' };
  const HINT = { oneof: 'Values, separated by commas', noneof: 'Values, separated by commas', between: 'Hours, such as 9-17' };

  let data = null; // /api/event-alerts
  let tab = 'rules';
  const picked = new Set();
  const ev = (id) => data?.events.find((e) => e.id === id);
  const ruleOf = (id) => data?.rules.find((r) => r.id === id);
  const firedOf = (id) => data?.fired.find((f) => f.id === id);
  const typeName = (id) => data?.types.find((t) => t.id === id)?.name || 'a deleted alert type';
  const fresh = () => (data?.fired || []).filter((f) => f.state === 'new').length;
  const week = (r) => (data?.fired || []).filter((f) => f.rule === r.id && Date.now() - new Date(f.at) < 7 * 86400000).length;
  const listRule = (e) => data?.rules.find((r) => r.event === e.id && !r.about && r.from !== 'builtin') || data?.rules.find((r) => r.event === e.id && !r.about);
  const nameOf = (r) => r.name || ev(r.event)?.name || r.event;
  const sevBadge = (sev) => { const [w, tone] = SEV[sev] || SEV.warning; return `<span class="ubadge ${tone}">${w}</span>`; };
  const sevPic = (sev, cls = 'pic') => `<span class="${cls} ea-sev ${SEV[sev]?.[1] || 'amber'}">${svg(sev === 'info' ? 'info' : 'alert')}</span>`;
  const areaPic = (e) => `<span class="pic">${svg(AREA_ICON[e?.area] || 'bell')}</span>`;
  const learns = (r) => ['ad.signin.new', 'ad.signin.hours'].includes(r.event);
  const audited = (e) => !!e && (e.area === 'Domain sign-ins' || /AD audit/.test(e.note || ''));

  // A rule in words: how often, its conditions, its fixed settings; and the whole of it as a sentence.
  const often = (r) => (r.times > 1 ? `${r.times} ${DISTINCT[r.distinct || '']} in ${r.minutes} minutes${r.per === 'all' ? '' : `, for each ${PER[r.per]}`}` : '');
  const condWords = (c) => `${FIELD[c.field] || c.field} ${OPS[c.op] || c.op} ${c.value}`;
  const conds = (r) => {
    const fixed = ev(r.event)?.fixed || [];
    return [often(r), ...r.conditions.map(condWords), fixed.includes('computers') && r.computers ? COMPUTERS[r.computers] : '',
      fixed.includes('after') ? `after ${r.after} minutes` : '', fixed.includes('percent') ? `${r.percent}% full` : ''].filter(Boolean);
  };
  const sendTo = (r) => list(r.types.map(typeName)) || 'nobody yet';
  const say = (r) => {
    const bits = [r.aboutName || r.about, ...conds(r)].filter(Boolean);
    return `When: ${ev(r.event)?.name || r.event}${bits.length ? ` (${bits.join('; ')})` : ''}. Then: ${r.severity === 'info' ? 'an' : 'a'} ${SEV[r.severity]?.[0] || ''} alert to ${sendTo(r)}${r.quiet ? ', even in quiet hours' : ''}.`;
  };

  const load = async () => {
    if (!may()) return;
    try {
      const was = JSON.stringify(data);
      data = await get('/api/event-alerts');
      R.say('ea-problem', '');
      if (JSON.stringify(data) !== was) changed();
    } catch (e) {
      R.say('ea-problem', e.message);
    }
  };
  const changed = () => {
    draw();
    drawRail();
    document.dispatchEvent(new CustomEvent('rabbidlab:alerts'));
  };
  const took = (r) => { if (r?.now) { data = r.now; changed(); } return r; };
  const drawRail = () => {
    const n = fresh();
    $('rail-alerts-n').textContent = n ? n.toLocaleString() : '';
    $('rail-alerts-n').classList.toggle('tone', n > 0);
  };

  // --- The page --------------------------------------------------------------------------------------------------
  const TABS = [['catalog', 'Everything that can happen', 'list'], ['rules', 'Rules', 'bell'], ['fired', 'Fired', 'history'], ['types', 'Where alerts go', 'message']];
  const rowsOf = (t) => (t === 'catalog' ? data?.events || [] : t === 'rules' ? data?.rules || [] : t === 'fired' ? data?.fired || [] : data?.types || []);
  const areaOrder = (e) => [...new Set((data?.events || []).map((x) => x.area))].indexOf(e?.area);
  const more = (id, label) => `<button type="button" class="at-more" data-ea-more="${esc(id)}" data-tip="Actions" aria-label="${esc(`Actions for ${label}`)}">${svg('dots')}</button>`;
  const tick = (id, label) => `<label class="tk"><input type="checkbox" data-ea-tick="${esc(id)}" aria-label="Select ${esc(label)}"${picked.has(id) ? ' checked' : ''}></label>`;
  const lead = { w: 18, head: '<label class="tk"><input type="checkbox" data-ea-tick-all aria-label="Select all"></label>' };
  const G = {};
  const grids = {
    catalog: () => R.grid({
      el: $('ea-list'), key: 'ealist', sort: { key: 'area', dir: 1 },
      cols: [
        { key: 'name', label: 'What can happen', w: 340, fixed: true, get: (e) => e.name, cell: (e) => `<div class="who">${areaPic(e)}<b class="ell">${esc(e.name)}</b></div>` },
        { key: 'area', label: 'Area', w: 150, on: true, get: (e) => data.events.indexOf(e), text: (e) => e.area, cell: (e) => `<span class="ell">${esc(e.area)}</span>` },
        { key: 'sev', label: 'Severity', w: 100, on: true, get: (e) => (listRule(e) || e).severity, cell: (e) => (listRule(e) ? sevBadge(listRule(e).severity) : `<span class="muted">${SEV[e.severity][0]}</span>`) },
        { key: 'to', label: 'Send to', w: 160, on: true, get: (e) => (listRule(e) ? sendTo(listRule(e)) : ''), cell: (e) => (listRule(e) ? `<span class="ell">${esc(sendTo(listRule(e)))}</span>` : '<span class="muted">-</span>') },
        { key: 'on', label: 'On', w: 140, on: true, get: (e) => (e.notYet ? 2 : listRule(e)?.on ? 0 : 1), text: (e) => (e.notYet ? 'Not available yet' : listRule(e)?.on ? 'On' : 'Off'),
          cell: (e) => (e.notYet ? `<span class="muted ea-notyet" data-tip="${esc(e.notYet)}">Not available yet</span>`
            : `<input type="checkbox" class="switch" data-ea-on="${esc(e.id)}"${listRule(e)?.on ? ' checked' : ''} aria-label="Alert when ${esc(e.name)}">`) },
      ],
      row: (e) => ` data-ea-row="${esc(e.id)}"${e.notYet ? ' data-ea-notyet' : ''}`, empty: () => 'Nothing to list.', loading: () => !data, redraw: () => draw(),
    }),
    rules: () => R.grid({
      el: $('ea-list'), key: 'earules2', sort: { key: 'area', dir: 1 },
      lead: { ...lead, cell: (r) => tick(r.id, nameOf(r)) },
      cols: [
        { key: 'name', label: 'Alert', w: 250, fixed: true, get: nameOf, cell: (r) => `<div class="who">${areaPic(ev(r.event))}<b class="ell">${esc(nameOf(r))}</b></div>` },
        { key: 'area', label: 'Area', w: 140, on: true, get: (r) => areaOrder(ev(r.event)) * 1000 + data.rules.indexOf(r), text: (r) => ev(r.event)?.area || '', cell: (r) => `<span class="ell">${esc(ev(r.event)?.area || '')}</span>` },
        { key: 'when', label: 'Only when', w: 240, on: true, get: (r) => conds(r).join('; ') || 'Each time', cell: (r) => `<span class="ell">${esc([r.aboutName || r.about, ...conds(r)].filter(Boolean).join('; ') || 'Each time')}</span>` },
        { key: 'sev', label: 'Severity', w: 100, on: true, get: (r) => Object.keys(SEV).indexOf(r.severity), text: (r) => SEV[r.severity][0], cell: (r) => sevBadge(r.severity) },
        { key: 'to', label: 'Send to', w: 150, on: true, get: (r) => sendTo(r), cell: (r) => `<span class="ell${r.types.length ? '' : ' muted'}">${esc(r.types.length ? sendTo(r) : 'Nobody yet')}</span>` },
        { key: 'fired', label: 'Last 7 days', w: 100, on: true, get: week, cell: (r) => (week(r) ? `<span class="num">${week(r)}</span>` : '<span class="muted">0</span>') },
        { key: 'on', label: 'On', w: 60, on: true, get: (r) => (r.on ? 0 : 1), text: (r) => (r.on ? 'On' : 'Off'), cell: (r) => `<input type="checkbox" class="switch" data-ea-ron="${esc(r.id)}"${r.on ? ' checked' : ''} aria-label="On">` },
        { key: 'by', label: 'Added by', w: 120, get: (r) => r.by },
      ],
      tail: { w: 30, cell: (r) => more(r.id, nameOf(r)) },
      row: (r) => ` data-ea-row="${esc(r.id)}"`, empty: () => 'No alerts yet. Select New alert, or turn one on in Everything that can happen.', loading: () => !data, redraw: () => draw(),
    }),
    fired: () => R.grid({
      el: $('ea-list'), key: 'eafired', sort: { key: 'at', dir: -1 },
      lead: { ...lead, cell: (f) => tick(f.id, f.title) },
      cols: [
        { key: 'at', label: 'When', w: 140, fixed: true, get: (f) => new Date(f.at).getTime(), text: (f) => R.when(f.at), cell: (f) => `<span class="ell">${esc(R.when(f.at))}</span>` },
        { key: 'what', label: 'What happened', w: 360, on: true, get: (f) => f.title, cell: (f) => `<div class="who">${sevPic(f.severity)}<b class="ell">${esc(f.title)}</b></div>` },
        { key: 'alert', label: 'Alert', w: 220, on: true, get: (f) => (ruleOf(f.rule) ? nameOf(ruleOf(f.rule)) : ev(f.event)?.name || f.event) },
        { key: 'sent', label: 'Sent', w: 150, on: true, get: (f) => SENT[f.sent] || f.sent, cell: (f) => `<span class="ell${f.sent === 'sent' ? '' : ' muted'}">${esc(SENT[f.sent] || f.sent)}</span>` },
        { key: 'state', label: 'State', w: 160, on: true, get: (f) => (f.approval === 'waiting' ? 0 : f.state === 'new' ? 1 : 2),
          text: (f) => (f.approval === 'waiting' ? 'Workflow waiting' : f.state === 'new' ? 'New' : 'Acknowledged'),
          cell: (f) => (f.approval === 'waiting' ? '<span class="ubadge amber">Workflow waiting</span>' : f.state === 'new' ? '<span class="ubadge amber">New</span>' : '<span class="ubadge grey">Acknowledged</span>') },
      ],
      tail: { w: 30, cell: (f) => more(f.id, f.title) },
      row: (f) => ` data-ea-row="${esc(f.id)}"`, empty: () => 'Nothing has fired yet.', loading: () => !data, redraw: () => draw(),
    }),
    types: () => R.grid({
      el: $('ea-list'), key: 'eatypes', sort: { key: 'name', dir: 1 },
      cols: [
        { key: 'name', label: 'Alert type', w: 220, fixed: true, get: (t) => t.name, cell: (t) => `<div class="who"><span class="pic">${svg('message')}</span><b class="ell">${esc(t.name)}</b></div>` },
        { key: 'to', label: 'Notifications', w: 160, on: true, get: (t) => t.to, text: (t) => (t.to ? count(t.to, 'notification') : 'None'), cell: (t) => `<span class="${t.to ? '' : 'muted'}">${esc(t.to ? count(t.to, 'notification') : 'None')}</span>` },
        { key: 'used', label: 'Used by', w: 140, on: true, get: (t) => data.rules.filter((r) => r.types.includes(t.id)).length, text: (t) => count(data.rules.filter((r) => r.types.includes(t.id)).length, 'alert') },
      ],
      row: (t) => ` data-ea-row="${esc(t.id)}"`, empty: () => 'No alert types.', loading: () => !data, redraw: () => draw(),
    }),
  };
  const LEAD = {
    catalog: 'Everything Rabbid Lab can alert on. Turn one on and it alerts with the usual settings; select it to change them.',
    rules: 'The AD security alerts are here to start with. Change any of them, add conditions of your own, or select New alert. Sensors, links and certificates alert through Settings, Alert rules.',
    fired: 'Each alert that went off stays New until someone acknowledges it.',
    types: 'Alerts go through alert types. Their notifications, times and quiet hours are set in Settings, Alert rules and Notifications.',
  };
  // Whether the domain controllers' Security logs are being read: what the sign-in and directory alerts need.
  const drawStatus = () => {
    const dcs = data?.dcs || [];
    const el = $('ea-status');
    const wants = (data?.rules || []).some((r) => r.on && audited(ev(r.event)));
    el.hidden = !data || !wants;
    if (el.hidden) return;
    const read = dcs.filter((d) => d.state === 'read');
    const bad = dcs.filter((d) => d.state !== 'read');
    const last = read.map((d) => d.last).filter(Boolean).sort().pop();
    el.classList.toggle('bad', !read.length || bad.length > 0);
    el.innerHTML = !dcs.length ? `${svg('alert')}<span><b>The domain controllers' Security logs aren't read yet.</b> Sign-in and directory alerts need the AD audit: connect Active Directory with a service account that is in Event Log Readers.</span>`
      : `${svg(bad.length ? 'alert' : 'activity')}<span><b>${esc(read.length ? `Reading the Security logs on ${list(read.map((d) => d.name.split('.')[0].toUpperCase()))}.` : 'No domain controller\'s Security log can be read.')}</b> `
        + `${esc([last ? `The last event arrived ${R.when(last)}.` : '', ...bad.map((d) => d.problem || `${d.name} can't be read.`)].filter(Boolean).join(' '))}</span>`;
  };
  const draw = () => {
    if (R.where()[0] !== 'alerts') return;
    const rules = data?.rules || [];
    $('ea-state').textContent = !data ? '' : fresh() ? `${count(fresh(), 'alert needs', 'alerts need')} a look.` : `${count(rules.filter((r) => r.on).length, 'alert is', 'alerts are')} on.`;
    if (data) {
      $('ea-n-on').textContent = rules.filter((r) => r.on).length;
      $('ea-n-fired').textContent = data.fired.filter((f) => Date.now() - new Date(f.at) < 7 * 86400000).length;
      $('ea-n-new').textContent = fresh();
      $('ea-n-can').textContent = data.events.filter((e) => !e.notYet).length;
    }
    $('ea-tabs').innerHTML = TABS.map(([k, l, i]) => `<a role="tab" data-ea-tab="${k}"${tab === k ? ' aria-current="true"' : ''}>${svg(i)}${esc(l)} <small${k === 'fired' && fresh() ? ' class="tone amber"' : ''}>${data ? (k === 'fired' && fresh() ? fresh() : rowsOf(k).length) : ''}</small></a>`).join('');
    $('ea-lead').textContent = LEAD[tab];
    drawStatus();
    $('ea-new').hidden = !may();
    $('ea-acts').hidden = tab === 'catalog' || tab === 'types';
    $('ea-ackall').hidden = !fresh();
    const rows = rowsOf(tab);
    [...picked].forEach((id) => { if (!rows.some((x) => x.id === id)) picked.delete(id); });
    (G[tab] ||= grids[tab]()).draw(rows);
    $('ea-count').textContent = data ? count(rows.length, tab === 'catalog' ? 'thing' : tab === 'types' ? 'alert type' : 'alert') : '';
    drawTicks();
    if (panel.is('earule')) { if (ruleOf(panel.key)) openRule(ruleOf(panel.key), true); else panel.close(); }
    if (panel.is('eafired')) { if (firedOf(panel.key)) openFired(firedOf(panel.key), true); else panel.close(); }
  };
  const drawTicks = () => {
    [...$('ea-list').querySelectorAll('[data-ea-tick]')].forEach((b) => { b.checked = picked.has(b.dataset.eaTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const n = picked.size;
    $('ea-acts').disabled = !n;
    $('ea-acts').querySelector('.cnt').hidden = !n;
    $('ea-acts').querySelector('.cnt').textContent = n;
  };
  $('ea-tabs').addEventListener('click', (e) => {
    const a = e.target.closest('[data-ea-tab]');
    if (!a || a.dataset.eaTab === tab) return;
    tab = a.dataset.eaTab;
    picked.clear();
    if (panel.is('earule') || panel.is('eafired')) panel.close();
    draw();
  });
  $('ea-list').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return;
      if (box.dataset.eaTickAll !== undefined) rowsOf(tab).forEach((x) => (box.checked ? picked.add(x.id) : picked.delete(x.id)));
      else if (box.checked) picked.add(box.dataset.eaTick);
      else picked.delete(box.dataset.eaTick);
      drawTicks();
      return;
    }
    const m = e.target.closest('[data-ea-more]');
    if (m) { rowMenu(m, m.dataset.eaMore); return; }
    if (e.target.closest('input, button, a')) return;
    const row = e.target.closest('[data-ea-row]');
    if (!row) return;
    const id = row.dataset.eaRow;
    if (tab === 'rules') { if (panel.shows('earule', id)) panel.close(); else openRule(ruleOf(id)); }
    else if (tab === 'fired') { if (panel.shows('eafired', id)) panel.close(); else openFired(firedOf(id)); }
    else if (tab === 'catalog') {
      const e2 = ev(id);
      if (e2?.notYet) toast(`${e2.name} can't be alerted on yet.`, e2.notYet);
      else if (listRule(e2)) openRule(listRule(e2));
      else turnOn(e2);
    } else location.hash = '#settings/alerts';
  });
  $('ea-list').addEventListener('change', async (e) => {
    const t = e.target;
    if (t.dataset.eaOn) {
      const e2 = ev(t.dataset.eaOn);
      const r = listRule(e2);
      if (r) await saveRule(r, { on: t.checked }, t.checked ? 'Alert on.' : 'Alert off.');
      else if (t.checked) await turnOn(e2);
    } else if (t.dataset.eaRon) {
      const r = ruleOf(t.dataset.eaRon);
      if (r) await saveRule(r, { on: t.checked }, t.checked ? 'Alert on.' : 'Alert off.');
    }
  });
  // On from the list: the usual settings (its own severity and counting, the alert type new things start with), and its panel to change them.
  const turnOn = async (e) => {
    try {
      const r = took(await send('/api/event-alerts/rules', { event: e.id, types: data.start ? [data.start] : [], from: 'list' }));
      toast('Alert on.', say(ruleOf(r.id)));
      openRule(ruleOf(r.id));
    } catch (err) {
      oops(err);
      draw();
    }
  };
  const saveRule = async (r, change, said = 'Saved.') => {
    try {
      took(await send(`/api/event-alerts/rules/${r.id}`, change));
      toast(said, say(ruleOf(r.id) || r));
      return true;
    } catch (err) {
      oops(err);
      draw();
      return false;
    }
  };
  // A row's ⋯: an alert's Edit, test, on or off and delete; a fired alert's panel, its alert's Edit, acknowledge, and the workflow waiting.
  const rowMenu = (anchor, id) => {
    if (tab === 'rules') {
      const r = ruleOf(id);
      if (!r) return;
      R.actions.open(anchor, {
        items: [{ act: 'edit', label: 'Edit…' }, { act: 'open', label: 'Open in the side panel' }, '-', { act: 'test', label: 'Send test' }, { act: 'turn', label: r.on ? 'Turn off' : 'Turn on' }, '-', { act: 'delete', label: 'Delete…', danger: true }],
        pick: (act) => (act === 'edit' ? openBuild(r) : act === 'open' ? openRule(r) : act === 'test' ? test(r) : act === 'turn' ? saveRule(r, { on: !r.on }, r.on ? 'Alert off.' : 'Alert on.') : remove([r])),
      });
      return;
    }
    const f = firedOf(id);
    if (!f) return;
    const r = ruleOf(f.rule);
    R.actions.open(anchor, {
      items: [{ act: 'open', label: 'Open in the side panel' }, ...(r ? [{ act: 'edit', label: 'Edit the alert…' }] : []), '-', ...(f.state === 'new' ? [{ act: 'ack', label: 'Acknowledge' }] : []),
        ...(f.approval === 'waiting' ? [{ act: 'approve', label: 'Approve and run the workflow' }, { act: 'dismiss', label: 'Dismiss the workflow' }] : [])],
      pick: async (act) => {
        if (act === 'open') openFired(f);
        else if (act === 'edit') openBuild(r);
        else if (act === 'ack') ack([f.id]);
        else if (act === 'approve') { openFired(f); approve(f); } else if (act === 'dismiss') {
          try { took(await send(`/api/event-alerts/fired/${f.id}/approval`, { state: 'dismissed' })); toast('Workflow dismissed.', f.title); } catch (err) { oops(err); }
        }
      },
    });
  };
  const test = async (r) => {
    try {
      await send(`/api/event-alerts/rules/${r.id}/test`);
      toast('Test sent.', `To ${sendTo(r)}.`);
    } catch (err) {
      oops(err);
    }
  };
  $('ea-acts').addEventListener('click', () => {
    const ids = [...picked];
    if (tab === 'fired') {
      R.actions.open($('ea-acts'), { items: [{ act: 'ack', label: 'Acknowledge' }], pick: () => ack(ids) });
      return;
    }
    const rs = ids.map(ruleOf).filter(Boolean);
    R.actions.open($('ea-acts'), {
      items: [{ act: 'on', label: 'Turn on' }, { act: 'off', label: 'Turn off' }, '-', { act: 'delete', label: 'Delete…', hint: String(rs.length), danger: true }],
      pick: async (act) => {
        if (act === 'delete') { remove(rs); return; }
        try {
          for (const r of rs) took(await send(`/api/event-alerts/rules/${r.id}`, { on: act === 'on' }));
          toast(act === 'on' ? 'Alerts on.' : 'Alerts off.', count(rs.length, 'alert'));
          picked.clear();
          draw();
        } catch (err) {
          oops(err);
        }
      },
    });
  });
  const remove = (rs) => sure(rs.length === 1 ? `Delete ${nameOf(rs[0])}?` : `Delete ${rs.length} alerts?`, 'Nothing more is sent for them. What they already sent stays in Fired.', 'Delete', async () => {
    try {
      for (const r of rs) took(await send(`/api/event-alerts/rules/${r.id}/delete`));
      toast(rs.length === 1 ? 'Alert deleted.' : `${rs.length} alerts deleted.`, rs.map(nameOf).join(', '));
      picked.clear();
      if (panel.is('earule')) panel.close();
    } catch (err) {
      oops(err);
    }
  });
  const ack = async (ids) => {
    try {
      const r = took(await send('/api/event-alerts/ack', { ids }));
      toast(r.acknowledged === 1 ? 'Alert acknowledged.' : `${count(r.acknowledged, 'alert')} acknowledged.`, '');
      picked.clear();
      draw();
    } catch (err) {
      oops(err);
    }
  };
  $('ea-ackall').addEventListener('click', () => ack(null));
  $('ea-new').addEventListener('click', () => openBuild());
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'alerts') return;
    if (!data) R.skeleton($('ea-list'), 'grid', 6, { cols: 5 });
    draw();
    load();
  });
  // Every 30 seconds while open (and for the count in the sidebar on any page), and as soon as the tab shows again.
  setInterval(() => { if (document.visibilityState === 'visible') load(); }, 30000);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') load(); });
  document.addEventListener('rabbidlab:me', () => load());

  // --- What a rule waits for: its name, how often, its conditions and fixed settings; then where it goes ------------
  // pre names the data attributes: ea-r in a rule's panel, ea-b in New alert. d is the rule or the draft.
  const often$ = (d, e, pre) => {
    const per = Object.entries(PER).filter(([k]) => k === 'all' || e.fields.includes(k) || (k === 'computer' && e.fields.includes('address')));
    const distinct = Object.entries(DISTINCT).filter(([k]) => !k || e.fields.includes(k) || (k === 'computer' && e.fields.includes('address')));
    const sel = (attr, opts, v) => `<select data-${pre}-${attr}>${opts.map(([k, l]) => `<option value="${esc(k)}"${k === v ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select>`;
    return `<div class="seg ea-oftenseg" role="group" aria-label="How often"><button type="button" data-${pre}-often="1" aria-pressed="${d.times <= 1}">Each time</button><button type="button" data-${pre}-often="n" aria-pressed="${d.times > 1}">More than once</button></div>`
      + (d.times > 1 ? `<p class="ea-often">At least <input class="ds-input" type="number" min="2" max="1000" value="${d.times}" data-${pre}-times aria-label="How many"> ${sel('distinct', distinct, d.distinct || '')}`
        + ` in <input class="ds-input" type="number" min="1" max="1440" value="${d.minutes}" data-${pre}-minutes aria-label="Minutes"> minutes, for each ${sel('per', per, d.per)}</p>` : '');
  };
  const fixed$ = (d, e, pre) => {
    const sel = (attr, opts, v) => `<label><span>${attr === 'computers' ? 'Computers' : attr === 'after' ? 'For more than' : 'When it is'}</span><select data-${pre}-${attr}>${opts.map(([k, l]) => `<option value="${esc(k)}"${String(k) === String(v) ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select></label>`;
    const out = [];
    if (e.fixed.includes('computers')) out.push(sel('computers', Object.entries(COMPUTERS), d.computers));
    if (e.fixed.includes('after')) out.push(sel('after', [5, 15, 30, 60].map((m) => [m, `${m} minutes`]), d.after));
    if (e.fixed.includes('percent')) out.push(sel('percent', [80, 90, 95].map((p) => [p, `${p}% full`]), d.percent));
    return out.length ? `<div class="ea-conds">${out.join('')}</div>` : '';
  };
  const conds$ = (list2, e, pre) => {
    const fields = e.fields;
    return `<div class="ea-clist">${list2.map((c, i) => `<div class="ea-cond"><select data-${pre}-cf="${i}" aria-label="Field">${fields.map((f) => `<option value="${f}"${f === c.field ? ' selected' : ''}>${esc(FIELD[f] || f)}</option>`).join('')}</select>`
      + `<select data-${pre}-co="${i}" aria-label="How to compare">${Object.entries(OPS).filter(([k]) => k !== 'between' || c.field === 'hour').map(([k, l]) => `<option value="${k}"${k === c.op ? ' selected' : ''}>${esc(l)}</option>`).join('')}</select>`
      + `<input class="ds-input" data-${pre}-cv="${i}" value="${esc(c.value)}" placeholder="${esc(HINT[c.op] || (c.field === 'hour' ? '0 to 23' : c.field === 'day' ? 'Monday' : 'Value'))}"${c.field === 'group' ? ' list="ea-groups"' : ''} autocomplete="off" spellcheck="false" aria-label="Value">`
      + `<button type="button" class="icon" data-${pre}-cx="${i}" data-tip="Remove" aria-label="Remove condition">${svg('x')}</button></div>`).join('')}</div>`
      + (fields.length ? `<button type="button" class="small ea-cadd" data-${pre}-cadd>${svg('plus')}Add a condition</button>` : '');
  };
  const then$ = (r, e, pre) => `<div class="ea-then"><span>Severity</span><div class="seg" role="group" aria-label="Severity">${Object.entries(SEV).map(([k, [w]]) => `<button type="button" data-${pre}-sev="${k}" aria-pressed="${r.severity === k}">${w}</button>`).join('')}</div>`
    + `<span>Send to</span><div class="ea-chips">${data.types.map((t) => `<button type="button" class="ea-chip" data-${pre}-to="${esc(t.id)}" aria-pressed="${r.types.includes(t.id)}">${svg(r.types.includes(t.id) ? 'check' : 'plus')}${esc(t.name)}</button>`).join('') || '<span class="muted">No alert types yet.</span>'}</div>`
    + (pre === 'ea-m' ? '' : `<span>Quiet hours</span><label class="ea-sw"><input type="checkbox" class="switch" data-${pre}-quiet${r.quiet ? ' checked' : ''}><span>${r.quiet ? 'Sends even in quiet hours' : 'Waits until quiet hours end'}</span></label>`)
    + (e?.user && pre !== 'ea-m' ? `<span>Also</span><select data-${pre}-flow aria-label="Also run a workflow"><option value="">Do nothing else</option>${data.workflows.map((w) => `<option value="${esc(w.id)}"${w.id === r.workflow ? ' selected' : ''}>${esc(`Run ${w.name} on the user, after someone approves`)}</option>`).join('')}</select>` : '')
    + '</div>';
  const learning$ = () => {
    const l = data.learning;
    return `<div class="ea-then"><span>Learning period</span><div class="seg" role="group" aria-label="Learning period">${[7, 14, 30].map((d) => `<button type="button" data-ea-learn="${d}" aria-pressed="${l.days === d}">${d} days</button>`).join('')}</div></div>`
      + `<p class="ea-text muted">${esc(!l.accounts ? 'Nothing learned yet. Sign-ins are learned as the Security logs are read.'
        : `${count(l.accounts, 'account')} known${l.since ? ` since ${new Date(l.since).toLocaleDateString([], { dateStyle: 'medium' })}` : ''}, ${l.learned} learned for ${l.days} days. An account alerts once it has been learned for the whole period.`)}</p>`;
  };
  // The conditions being changed, kept here until each has a value (an empty one isn't saved).
  let pc = { id: '', list: [] };
  const conditionsOf = (r) => { if (pc.id !== r.id) pc = { id: r.id, list: r.conditions.map((c) => ({ ...c })) }; return pc.list; };
  const complete = (cs) => cs.every((c) => c.value.trim());
  const openRule = (r, again = false) => {
    if (!r) return;
    const e = ev(r.event);
    const cs = conditionsOf(r);
    const fired = data.fired.filter((f) => f.rule === r.id).slice(0, 10);
    const box = panel.body.querySelector('.dw-scroll, .dw-panel');
    const top = panel.body.scrollTop;
    panel.body.innerHTML = `<div class="dw-head">${areaPic(e).replace('class="pic"', 'class="pic dw-pic"')}<div class="dw-name"><h2 id="dw-title">${esc(nameOf(r))}</h2><p>${esc(say(r))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${R.editButton('data-ea-edit', '')}<button type="button" class="dw-qa" data-ea-test>${svg('bell')}<span>Send test</span></button><button type="button" class="dw-qa" data-ea-turn>${svg(r.on ? 'pause' : 'play')}<span>${r.on ? 'Turn off' : 'Turn on'}</span></button>`
      + `<button type="button" class="dw-qa" data-ea-del>${svg('trash')}<span>Delete</span></button></div>`
      + `<div class="dw-strip"><div><span class="l">State</span><span class="v">${r.on ? '<span class="ubadge green">On</span>' : '<span class="ubadge grey">Off</span>'}</span></div><div><span class="l">Severity</span><span class="v">${sevBadge(r.severity)}</span></div>`
      + `<div><span class="l">Send to</span><span class="v">${esc(sendTo(r))}</span></div><div><span class="l">Last 7 days</span><span class="v">${week(r)}</span></div><div><span class="l">Added by</span><span class="v">${esc(r.from === 'builtin' ? 'There to start with' : r.by)}</span></div></div>`
      + section('What happens', `<div class="mrow ea-what">${areaPic(e)}<div class="two"><b>${esc(e?.name || r.event)}</b><small>${esc([e?.area, r.aboutName || r.about ? `Only for ${r.aboutName || r.about}` : '', e?.note].filter(Boolean).join(' · '))}</small></div></div>`
        + `<label class="ea-name"><span>Name</span><input class="ds-input" data-ea-r-name value="${esc(r.name)}" placeholder="${esc(e?.name || '')}" maxlength="60" autocomplete="off"></label>`)
      + section('How often', often$(r, e, 'ea-r'))
      + section(`Only when <small>${cs.length || ''}</small>`, (fixed$(r, e, 'ea-r') + conds$(cs, e, 'ea-r')) || none('It alerts each time this happens.'))
      + (learns(r) ? section('Learning', learning$()) : '')
      + section('Then', then$(r, e, 'ea-r'))
      + section(`Fired <small>${fired.length}</small>`, fired.length ? `<div class="dw-list">${fired.map((f) => `<div class="mrow">${sevPic(f.severity)}<div class="two"><b>${esc(f.title)}</b><small>${esc(`${R.when(f.at)} · ${SENT[f.sent] || f.sent}${f.state === 'new' ? ' · New' : ''}`)}</small></div></div>`).join('')}</div>` : none('It has not fired yet.'));
    if (!again) panel.open('earule', r.id);
    else if (box) panel.body.scrollTop = top;
  };
  const openFired = (f, again = false) => {
    if (!f) return;
    const r = ruleOf(f.rule);
    const flow = r?.workflow && data.workflows.find((w) => w.id === r.workflow);
    const who = f.subjectName || f.subject;
    const approval = f.approval === 'waiting' ? section('Workflow waiting', `<p class="ea-ask">${esc(flow ? `Run ${flow.name} on ${who}?` : 'The workflow for this alert was deleted or turned off.')}</p>`
      + `<div class="ea-ask-do">${flow ? `<button type="button" class="primary small" data-ea-approve>${svg('play')}Approve and run</button>` : ''}<button type="button" class="small" data-ea-dismiss>Dismiss</button></div>`)
      : f.approval ? section('Workflow', `<p class="ea-ask">${esc(f.approval === 'approved' ? `Approved and run by ${f.approvalBy}.` : `Dismissed by ${f.approvalBy}.`)}</p>`) : '';
    panel.body.innerHTML = `<div class="dw-head">${sevPic(f.severity, 'pic dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(f.title)}</h2><p>${esc(`${R.when(f.at)} · ${r ? nameOf(r) : ev(f.event)?.name || f.event}`)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${r ? R.editButton('data-ea-editrule', '') : ''}${f.state === 'new' ? `<button type="button" class="dw-qa" data-ea-ack>${svg('check')}<span>Acknowledge</span></button>` : ''}${r ? `<button type="button" class="dw-qa" data-ea-torule>${svg('bell')}<span>Open the alert</span></button>` : ''}</div>`
      + `<div class="dw-strip"><div><span class="l">State</span><span class="v">${f.state === 'new' ? '<span class="ubadge amber">New</span>' : '<span class="ubadge grey">Acknowledged</span>'}</span></div><div><span class="l">Severity</span><span class="v">${sevBadge(f.severity)}</span></div>`
      + `<div><span class="l">Sent</span><span class="v">${esc(SENT[f.sent] || f.sent)}</span></div><div><span class="l">When</span><span class="v">${esc(R.when(f.at))}</span></div>${f.ackBy ? `<div><span class="l">Acknowledged by</span><span class="v">${esc(f.ackBy)}</span></div>` : ''}</div>`
      + approval
      + section('What happened', `<p class="ea-text">${esc(f.text || f.title)}</p>${f.why ? `<p class="ea-text muted">${esc(f.why)}</p>` : ''}${r ? `<p class="ea-say">${esc(say(r))}</p>` : ''}`);
    if (!again) panel.open('eafired', f.id);
  };
  // A condition row changed (field, how, value), added or removed, in the panel or in New alert.
  const condChange = (cs, t, pre) => {
    const k = (s) => t.getAttribute(`data-${pre}-${s}`);
    if (k('cf') !== null) { const c = cs[k('cf')]; c.field = t.value; if (c.op === 'between' && c.field !== 'hour') c.op = 'is'; return 'draw'; }
    if (k('co') !== null) { cs[k('co')].op = t.value; return 'draw'; }
    if (k('cv') !== null) { cs[k('cv')].value = t.value; return 'value'; }
    return '';
  };
  panel.el.addEventListener('click', async (e) => {
    if (panel.is('earule')) {
      const r = ruleOf(panel.key);
      if (!r) return;
      let b;
      if (e.target.closest('[data-ea-edit]')) openBuild(r);
      else if (e.target.closest('[data-ea-test]')) test(r);
      else if (e.target.closest('[data-ea-turn]')) saveRule(r, { on: !r.on }, r.on ? 'Alert off.' : 'Alert on.');
      else if (e.target.closest('[data-ea-del]')) remove([r]);
      else if ((b = e.target.closest('[data-ea-r-sev]'))) { if (b.dataset.eaRSev !== r.severity) saveRule(r, { severity: b.dataset.eaRSev }); }
      else if ((b = e.target.closest('[data-ea-r-to]'))) { const id = b.dataset.eaRTo; saveRule(r, { types: r.types.includes(id) ? r.types.filter((x) => x !== id) : [...r.types, id] }); }
      else if ((b = e.target.closest('[data-ea-r-often]'))) {
        const ev2 = ev(r.event);
        if (b.dataset.eaROften === '1' && r.times > 1) saveRule(r, { times: 1 });
        else if (b.dataset.eaROften === 'n' && r.times <= 1) saveRule(r, { times: Math.max(2, ev2.times), minutes: ev2.minutes || 10, per: ev2.per, distinct: ev2.distinct });
      } else if (e.target.closest('[data-ea-r-cadd]')) { conditionsOf(r).push({ field: ev(r.event).fields[0], op: 'is', value: '' }); openRule(r, true); panel.body.querySelector('.ea-cond:last-child input')?.focus(); }
      else if ((b = e.target.closest('[data-ea-r-cx]'))) {
        const cs = conditionsOf(r);
        cs.splice(Number(b.dataset.eaRCx), 1);
        if (complete(cs)) saveRule(r, { conditions: cs }); else openRule(r, true);
      } else if ((b = e.target.closest('[data-ea-learn]'))) {
        try { took(await send('/api/event-alerts/learning', { days: Number(b.dataset.eaLearn) })); toast('Saved.', `Unusual sign-ins alert after ${b.dataset.eaLearn} days of learning.`); } catch (err) { oops(err); }
      }
    } else if (panel.is('eafired')) {
      const f = firedOf(panel.key);
      if (!f) return;
      if (e.target.closest('[data-ea-editrule]')) openBuild(ruleOf(f.rule));
      else if (e.target.closest('[data-ea-ack]')) ack([f.id]);
      else if (e.target.closest('[data-ea-torule]')) { tab = 'rules'; draw(); openRule(ruleOf(f.rule)); }
      else if (e.target.closest('[data-ea-dismiss]')) {
        try { took(await send(`/api/event-alerts/fired/${f.id}/approval`, { state: 'dismissed' })); toast('Workflow dismissed.', f.title); } catch (err) { oops(err); }
      } else if (e.target.closest('[data-ea-approve]')) approve(f);
    }
  });
  panel.el.addEventListener('change', async (e) => {
    if (!panel.is('earule')) return;
    const r = ruleOf(panel.key);
    const t = e.target;
    if (!r) return;
    const cs = conditionsOf(r);
    const did = condChange(cs, t, 'ea-r');
    if (did) {
      if (complete(cs)) { if (!(await saveRule(r, { conditions: cs }))) pc.id = ''; } else if (did === 'draw') openRule(r, true);
      return;
    }
    if (t.matches('[data-ea-r-name]')) saveRule(r, { name: t.value });
    else if (t.matches('[data-ea-r-times], [data-ea-r-minutes]')) saveRule(r, { [t.matches('[data-ea-r-times]') ? 'times' : 'minutes']: Number(t.value) });
    else if (t.matches('[data-ea-r-per]')) saveRule(r, { per: t.value });
    else if (t.matches('[data-ea-r-distinct]')) saveRule(r, { distinct: t.value });
    else if (t.matches('[data-ea-r-computers]')) saveRule(r, { computers: t.value });
    else if (t.matches('[data-ea-r-after], [data-ea-r-percent]')) saveRule(r, { [t.matches('[data-ea-r-after]') ? 'after' : 'percent']: Number(t.value) });
    else if (t.matches('[data-ea-r-quiet]')) saveRule(r, { quiet: t.checked });
    else if (t.matches('[data-ea-r-flow]')) saveRule(r, { workflow: t.value });
  });
  // Approve: the workflow runs on the user now, within the approver's roles (Confirm it's you when a step needs it).
  const approve = async (f) => {
    const r = ruleOf(f.rule);
    const flow = data.workflows.find((w) => w.id === r?.workflow);
    if (!flow) return;
    R.busy(panel.body, `Running ${flow.name}`, f.subjectName || f.subject);
    try {
      const run = await send(`/api/ad/workflows/${flow.id}/run`, { user: f.subject || f.subjectName, values: {} });
      took(await send(`/api/event-alerts/fired/${f.id}/approval`, { state: 'approved', run: run.id }));
      const bad = run.steps.find((s) => s.state === 'failed');
      if (bad) toast(`${flow.name} stopped at ${bad.label}.`, bad.detail, { tone: 'warn' });
      else toast(`${flow.name} completed.`, `${count(run.steps.length, 'step')} for ${run.userName || f.subject}.`);
      document.dispatchEvent(new CustomEvent('rabbidlab:undone'));
    } catch (err) {
      oops(err);
    } finally {
      R.busy(panel.body, '');
    }
  };

  // --- New alert: what happens (everything, found by a word), only when, then --------------------------------------
  let draft = null;
  let find = '';
  const blank = () => ({ event: '', name: '', times: 1, minutes: 10, per: 'account', distinct: '', conditions: [], computers: '', after: 15, percent: 90, severity: 'warning',
    types: data?.start ? [data.start] : [], quiet: false, workflow: '' });
  const drawBuild = (part = 'all') => {
    const d = draft;
    const e = ev(d.event);
    if (part === 'all' || part === 'events') {
      const q = find.trim().toLowerCase();
      const areas = [...new Set(data.events.map((x) => x.area))];
      $('ea-build-events').innerHTML = areas.map((a) => {
        const xs = data.events.filter((x) => x.area === a && (!q || `${x.name} ${a}`.toLowerCase().includes(q)));
        return xs.length ? `<p class="ea-area">${svg(AREA_ICON[a] || 'bell')}${esc(a)}</p>${xs.map((x) => `<label class="ea-ev${d.event === x.id ? ' on' : ''}${x.notYet ? ' off' : ''}"><input type="radio" name="ea-ev" value="${esc(x.id)}"${d.event === x.id ? ' checked' : ''}${x.notYet ? ' disabled' : ''}>`
          + `<span>${esc(x.name)}${x.notYet ? `<small>${esc(x.notYet)}</small>` : ''}</span></label>`).join('')}` : '';
      }).join('') || '<p class="muted ea-none">Nothing matches. Try another word.</p>';
    }
    $('ea-build-conds').innerHTML = !e ? '<p class="muted ea-none">Pick what happens first.</p>'
      : `<label class="ea-name"><span>Name</span><input class="ds-input" data-ea-b-name value="${esc(d.name)}" placeholder="${esc(e.name)}" maxlength="60" autocomplete="off"></label>`
        + `<p class="ea-sub">How often</p>${often$(d, e, 'ea-b')}${(fixed$(d, e, 'ea-b') + conds$(d.conditions, e, 'ea-b')) ? `<p class="ea-sub">Only when</p>${fixed$(d, e, 'ea-b')}${conds$(d.conditions, e, 'ea-b')}` : ''}`;
    $('ea-build-then').innerHTML = then$(d, e, 'ea-b');
    $('ea-build-say').textContent = e ? say(d) : 'When: pick what happens. Then: choose who hears about it.';
    $('ea-build-save').disabled = !e || !complete(d.conditions);
  };
  // New alert, or an alert opened to edit (r): the same window, filled in from it, saved back to it.
  let editing = '';
  const openBuild = async (r = null) => {
    if (!data) await load();
    if (!data) return;
    editing = r?.id || '';
    draft = r ? { ...blank(), ...JSON.parse(JSON.stringify(r)), workflow: r.workflow || '' } : blank();
    find = '';
    $('ea-build-find').value = '';
    $('ea-build-title').textContent = r ? `Edit ${nameOf(r)}` : 'New alert';
    $('ea-build-save').textContent = r ? 'Save' : 'Save alert';
    R.say('ea-build-problem', '');
    drawBuild();
    $('ea-build').showModal();
    $('ea-build-find').focus();
  };
  $('ea-build-find').addEventListener('input', (e) => { find = e.target.value; drawBuild('events'); });
  $('ea-build').addEventListener('input', (e) => {
    if (!draft) return;
    if (e.target.matches('[data-ea-b-cv]')) { condChange(draft.conditions, e.target, 'ea-b'); $('ea-build-save').disabled = !complete(draft.conditions); $('ea-build-say').textContent = say(draft); }
    else if (e.target.matches('[data-ea-b-name]')) draft.name = e.target.value;
  });
  $('ea-build').addEventListener('change', (e) => {
    const t = e.target;
    if (!draft) return;
    if (t.name === 'ea-ev') {
      const x = ev(t.value);
      Object.assign(draft, { event: t.value, severity: x.severity, times: x.times, minutes: x.minutes, per: x.per, distinct: x.distinct, conditions: [], computers: '' });
      if (!x.user) draft.workflow = '';
      drawBuild();
      return;
    }
    const did = condChange(draft.conditions, t, 'ea-b');
    if (did === 'value') return;
    if (!did) {
      if (t.matches('[data-ea-b-times], [data-ea-b-minutes]')) draft[t.matches('[data-ea-b-times]') ? 'times' : 'minutes'] = Math.max(1, Number(t.value) || 1);
      else if (t.matches('[data-ea-b-per]')) draft.per = t.value;
      else if (t.matches('[data-ea-b-distinct]')) draft.distinct = t.value;
      else if (t.matches('[data-ea-b-computers]')) draft.computers = t.value;
      else if (t.matches('[data-ea-b-after], [data-ea-b-percent]')) draft[t.matches('[data-ea-b-after]') ? 'after' : 'percent'] = Number(t.value);
      else if (t.matches('[data-ea-b-quiet]')) draft.quiet = t.checked;
      else if (t.matches('[data-ea-b-flow]')) draft.workflow = t.value;
      else return;
    }
    drawBuild('rest');
  });
  $('ea-build').addEventListener('click', async (e) => {
    let b;
    if (!draft) return;
    if ((b = e.target.closest('[data-ea-b-sev]'))) { draft.severity = b.dataset.eaBSev; drawBuild('rest'); return; }
    if ((b = e.target.closest('[data-ea-b-to]'))) { const id = b.dataset.eaBTo; draft.types = draft.types.includes(id) ? draft.types.filter((x) => x !== id) : [...draft.types, id]; drawBuild('rest'); return; }
    if ((b = e.target.closest('[data-ea-b-often]'))) {
      const x = ev(draft.event);
      if (b.dataset.eaBOften === '1') draft.times = 1;
      else if (draft.times <= 1) Object.assign(draft, { times: Math.max(2, x.times), minutes: x.minutes || 10, per: x.per, distinct: x.distinct });
      drawBuild('rest');
      return;
    }
    if (e.target.closest('[data-ea-b-cadd]')) { draft.conditions.push({ field: ev(draft.event).fields[0], op: 'is', value: '' }); drawBuild('rest'); $('ea-build-conds').querySelector('.ea-cond:last-child input')?.focus(); return; }
    if ((b = e.target.closest('[data-ea-b-cx]'))) { draft.conditions.splice(Number(b.dataset.eaBCx), 1); drawBuild('rest'); return; }
    if (e.target.closest('#ea-build-cancel')) { $('ea-build').close(); return; }
    if (!e.target.closest('#ea-build-save') || !draft.event) return;
    R.busy($('ea-build'), 'Saving the alert', draft.name || ev(draft.event).name);
    try {
      const { event, name, times, minutes, per, distinct, conditions, computers, after, percent, severity, types, quiet, workflow } = draft;
      const body = { event, name, times, minutes, per, distinct, conditions, computers, after, percent, severity, types, quiet, workflow };
      const r = took(await send(editing ? `/api/event-alerts/rules/${editing}` : '/api/event-alerts/rules', editing ? body : { ...body, from: 'new' }));
      $('ea-build').close();
      pc.id = ''; // the panel reads its conditions afresh
      toast('Alert saved.', say(ruleOf(r.id)));
      if (R.where()[0] !== 'alerts') location.hash = '#alerts';
      tab = 'rules';
      draw();
      openRule(ruleOf(r.id));
    } catch (err) {
      R.say('ea-build-problem', err.message);
    } finally {
      R.busy($('ea-build'), '');
    }
  });

  // --- Alert me about one user (their Actions in Active Directory), and their Alerts tab ---------------------------
  let me = null; // { user, picks, severity, types }
  const about = (x) => (data?.rules || []).filter((r) => same(r.about, x.username) || same(r.aboutName, x.name));
  const drawMe = () => {
    const have = new Set(about(me.user).map((r) => r.event));
    const evs = data.events.filter((e) => e.user && !e.notYet);
    $('ea-me-body').innerHTML = `<div class="pk-list ea-me-list">${evs.map((e) => `<label class="pk-row${have.has(e.id) ? ' ea-have' : ''}"><input type="checkbox" value="${esc(e.id)}"${have.has(e.id) || me.picks.has(e.id) ? ' checked' : ''}${have.has(e.id) ? ' disabled' : ''}>`
      + `${areaPic(e)}<span class="pk-text"><b>${esc(e.user)}</b><small>${esc(have.has(e.id) ? 'Already on' : e.area)}</small></span></label>`).join('')}</div>`
      + `<div class="ea-me-then">${then$({ severity: me.severity, types: me.types, quiet: false }, null, 'ea-m')}</div>`;
    $('ea-me-save').disabled = !me.picks.size;
    $('ea-me-save').textContent = me.picks.size > 1 ? `Add ${me.picks.size} alerts` : 'Add alert';
  };
  const openMe = async (user) => {
    if (!data) await load();
    if (!data) return;
    me = { user, picks: new Set(), severity: 'warning', types: data.start ? [data.start] : [] };
    $('ea-me-title').textContent = `Alert me about ${user.name || user.username}`;
    $('ea-me-lead').textContent = `Pick what to hear about. Alerts about ${user.name || user.username} are listed on the Alerts tab of their panel and in Alerts.`;
    R.say('ea-me-problem', '');
    drawMe();
    $('ea-me').showModal();
  };
  $('ea-me').addEventListener('change', (e) => {
    if (!me || !e.target.matches('.ea-me-list input')) return;
    if (e.target.checked) me.picks.add(e.target.value); else me.picks.delete(e.target.value);
    drawMe();
  });
  $('ea-me').addEventListener('click', async (e) => {
    let b;
    if (!me) return;
    if ((b = e.target.closest('[data-ea-m-sev]'))) { me.severity = b.dataset.eaMSev; drawMe(); return; }
    if ((b = e.target.closest('[data-ea-m-to]'))) { const id = b.dataset.eaMTo; me.types = me.types.includes(id) ? me.types.filter((x) => x !== id) : [...me.types, id]; drawMe(); return; }
    if (e.target.closest('#ea-me-cancel')) { $('ea-me').close(); return; }
    if (!e.target.closest('#ea-me-save') || !me.picks.size) return;
    const name = me.user.name || me.user.username;
    R.busy($('ea-me'), 'Adding the alerts', name);
    try {
      for (const id of me.picks) took(await send('/api/event-alerts/rules', { event: id, about: me.user.username, aboutName: me.user.name || '', severity: me.severity, types: me.types, from: 'user' }));
      $('ea-me').close();
      toast(me.picks.size === 1 ? 'Alert added.' : `${me.picks.size} alerts added.`, `You hear when ${name} ${list([...me.picks].map((id) => ev(id).user.replace(/^./, (c) => c.toLowerCase())))}.`, { action: { label: 'View', run: () => { location.hash = '#alerts'; tab = 'rules'; } } });
    } catch (err) {
      R.say('ea-me-problem', err.message);
    } finally {
      R.busy($('ea-me'), '');
    }
  });
  // The Alerts tab on a user's panel (users.js).
  const pane = (x) => {
    const rs = about(x);
    return section(`Alerts about ${esc(x.name || x.username)} <small>${rs.length}</small>`, rs.length ? `<div class="dw-list">${rs.map((r) => `<div class="mrow">${areaPic(ev(r.event))}<div class="two"><b>${esc(ev(r.event)?.user || nameOf(r))}</b>`
      + `<small>${esc(`${SEV[r.severity][0]} · ${sendTo(r)}`)}</small></div>${r.on ? '<span class="ubadge green">On</span>' : '<span class="ubadge grey">Off</span>'}</div>`).join('')}</div>` : none(`Nobody is alerted about ${x.name || x.username} yet.`),
    `<span class="dw-sec-do"><button type="button" class="small" data-ea-me="${esc(x.username)}" data-ea-me-name="${esc(x.name || '')}">${svg('bell')}Alert me…</button></span>`);
  };
  document.addEventListener('click', (e) => {
    const b = e.target.closest('#dw-body [data-ea-me]');
    if (b) openMe({ username: b.dataset.eaMe, name: b.dataset.eaMeName });
  });

  R.alerts = { can: may, me: openMe, about, pane };
})();
