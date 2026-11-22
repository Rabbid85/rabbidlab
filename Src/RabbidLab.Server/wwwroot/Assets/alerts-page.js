// Alerts (EventAlerts.cs): an alert for anything that can happen. Its own page in the sidebar: Everything that can happen
// (a switch on each), Rules, Fired and Where alerts go. New alert asks what happens, only when, and then; a rule or a fired
// alert opens in the side panel and changes in place; a user's Actions › Alert me about this user, and their Alerts tab
// (users.js), add and list alerts about one person.
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
  const AREA_ICON = { 'Sign-ins': 'lock', 'Active Directory': 'users', Devices: 'monitor', 'Sensors and links': 'activity', Printers: 'printer', 'Connected apps': 'plug', 'Rabbid Lab': 'shield' };
  const SENT = { sent: 'Sent', partial: 'Partly sent', failed: 'Not sent', held: 'Held for quiet hours', nobody: 'No notifications' };
  const WHAT = { '': 'Anything', user: 'Users', group: 'Groups', computer: 'Computers', ou: 'OUs' };
  const COMPUTERS = { '': 'Any computer', servers: 'Servers', workstations: 'Workstations' };
  const GROUPS = ['Domain Admins', 'Enterprise Admins', 'Schema Admins', 'Administrators', 'Account Operators', 'Backup Operators', 'Group Policy Creator Owners'];

  let data = null; // /api/event-alerts
  let tab = 'rules';
  const picked = new Set();
  const ev = (id) => data?.events.find((e) => e.id === id);
  const ruleOf = (id) => data?.rules.find((r) => r.id === id);
  const firedOf = (id) => data?.fired.find((f) => f.id === id);
  const typeName = (id) => data?.types.find((t) => t.id === id)?.name || 'a deleted alert type';
  const fresh = () => (data?.fired || []).filter((f) => f.state === 'new').length;
  const week = (r) => (data?.fired || []).filter((f) => f.rule === r.id && Date.now() - new Date(f.at) < 7 * 86400000).length;
  const listRule = (e) => data?.rules.find((r) => r.event === e.id && !r.about);
  const sevBadge = (sev) => { const [w, tone] = SEV[sev] || SEV.warning; return `<span class="ubadge ${tone}">${w}</span>`; };
  const sevPic = (sev, cls = 'pic') => `<span class="${cls} ea-sev ${SEV[sev]?.[1] || 'amber'}">${svg(sev === 'info' ? 'info' : 'alert')}</span>`;
  const areaPic = (e) => `<span class="pic">${svg(AREA_ICON[e?.area] || 'bell')}</span>`;

  // What a rule waits for, in words (only its conditions that aren't "always"), and the whole rule as a sentence.
  const conds = (r) => {
    const f = ev(r.event)?.fields || [];
    return [f.includes('count') && r.count ? data.counts[r.count] : '', f.includes('group') && r.group ? r.group : '', f.includes('what') && r.what ? WHAT[r.what] : '',
      f.includes('computers') && r.computers ? COMPUTERS[r.computers] : '', f.includes('after') ? `${r.after} minutes` : '', f.includes('percent') ? `${r.percent}% full` : ''].filter(Boolean);
  };
  const sendTo = (r) => list(r.types.map(typeName)) || 'nobody yet';
  const say = (r) => {
    const bits = [r.aboutName || r.about, ...conds(r)].filter(Boolean);
    return `When: ${ev(r.event)?.name || r.event}${bits.length ? ` (${bits.join(', ')})` : ''}. Then: ${r.severity === 'info' ? 'an' : 'a'} ${SEV[r.severity]?.[0] || ''} alert to ${sendTo(r)}${r.quiet ? ', even in quiet hours' : ''}.`;
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
  const tick = (id, label) => `<label class="tk"><input type="checkbox" data-ea-tick="${esc(id)}" aria-label="Select ${esc(label)}"${picked.has(id) ? ' checked' : ''}></label>`;
  const lead = { w: 18, head: '<label class="tk"><input type="checkbox" data-ea-tick-all aria-label="Select all"></label>' };
  const G = {};
  const grids = {
    catalog: () => R.grid({
      el: $('ea-list'), key: 'ealist', sort: { key: 'area', dir: 1 },
      cols: [
        { key: 'name', label: 'What can happen', w: 330, fixed: true, get: (e) => e.name, cell: (e) => `<div class="who">${areaPic(e)}<b class="ell">${esc(e.name)}</b></div>` },
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
      el: $('ea-list'), key: 'earules', sort: { key: 'name', dir: 1 },
      lead: { ...lead, cell: (r) => tick(r.id, ev(r.event)?.name || r.event) },
      cols: [
        { key: 'name', label: 'Alert', w: 260, fixed: true, get: (r) => ev(r.event)?.name || r.event, cell: (r) => `<div class="who">${areaPic(ev(r.event))}<b class="ell">${esc(ev(r.event)?.name || r.event)}</b></div>` },
        { key: 'about', label: 'Watches', w: 130, on: true, get: (r) => r.aboutName || r.about || 'Everything' },
        { key: 'when', label: 'Only when', w: 190, on: true, get: (r) => conds(r).join(', ') || 'Always' },
        { key: 'sev', label: 'Severity', w: 100, on: true, get: (r) => Object.keys(SEV).indexOf(r.severity), text: (r) => SEV[r.severity][0], cell: (r) => sevBadge(r.severity) },
        { key: 'to', label: 'Send to', w: 170, on: true, get: (r) => sendTo(r), cell: (r) => `<span class="ell${r.types.length ? '' : ' muted'}">${esc(r.types.length ? sendTo(r) : 'Nobody yet')}</span>` },
        { key: 'fired', label: 'Fired (7 days)', w: 110, on: true, get: week, cell: (r) => (week(r) ? `<span class="num">${week(r)}</span>` : '<span class="muted">0</span>') },
        { key: 'on', label: 'On', w: 60, on: true, get: (r) => (r.on ? 0 : 1), text: (r) => (r.on ? 'On' : 'Off'), cell: (r) => `<input type="checkbox" class="switch" data-ea-ron="${esc(r.id)}"${r.on ? ' checked' : ''} aria-label="On">` },
        { key: 'by', label: 'Added by', w: 120, get: (r) => r.by },
      ],
      row: (r) => ` data-ea-row="${esc(r.id)}"`, empty: () => 'No alerts yet. Select New alert, or turn one on in Everything that can happen.', loading: () => !data, redraw: () => draw(),
    }),
    fired: () => R.grid({
      el: $('ea-list'), key: 'eafired', sort: { key: 'at', dir: -1 },
      lead: { ...lead, cell: (f) => tick(f.id, f.title) },
      cols: [
        { key: 'at', label: 'When', w: 140, fixed: true, get: (f) => new Date(f.at).getTime(), text: (f) => R.when(f.at), cell: (f) => `<span class="ell">${esc(R.when(f.at))}</span>` },
        { key: 'what', label: 'What happened', w: 360, on: true, get: (f) => f.title, cell: (f) => `<div class="who">${sevPic(f.severity)}<b class="ell">${esc(f.title)}</b></div>` },
        { key: 'alert', label: 'Alert', w: 220, on: true, get: (f) => ev(f.event)?.name || f.event },
        { key: 'sent', label: 'Sent', w: 150, on: true, get: (f) => SENT[f.sent] || f.sent, cell: (f) => `<span class="ell${f.sent === 'sent' ? '' : ' muted'}">${esc(SENT[f.sent] || f.sent)}</span>` },
        { key: 'state', label: 'State', w: 160, on: true, get: (f) => (f.approval === 'waiting' ? 0 : f.state === 'new' ? 1 : 2),
          text: (f) => (f.approval === 'waiting' ? 'Workflow waiting' : f.state === 'new' ? 'New' : 'Acknowledged'),
          cell: (f) => (f.approval === 'waiting' ? '<span class="ubadge amber">Workflow waiting</span>' : f.state === 'new' ? '<span class="ubadge amber">New</span>' : '<span class="ubadge grey">Acknowledged</span>') },
      ],
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
    rules: 'Sensors, links, certificates, disk space, updates and failing sign-ins also alert through Settings, Alert rules.',
    fired: 'Each alert that went off stays New until someone acknowledges it.',
    types: 'Alerts go through alert types. Their notifications, times and quiet hours are set in Settings, Alert rules and Notifications.',
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
    $('ea-new').hidden = !may();
    $('ea-acts').hidden = tab === 'catalog' || tab === 'types';
    $('ea-ackall').hidden = !fresh();
    const rows = rowsOf(tab);
    [...picked].forEach((id) => { if (!rows.some((x) => x.id === id)) picked.delete(id); });
    (G[tab] ||= grids[tab]()).draw(rows);
    $('ea-count').textContent = data ? count(rows.length, tab === 'catalog' ? 'thing' : tab === 'rules' ? 'alert' : tab === 'fired' ? 'alert' : 'alert type', tab === 'catalog' ? 'things' : undefined) : '';
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
  // On from the list: the usual settings (its own severity, the alert type new things start with), and its panel to change them.
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
    } catch (err) {
      oops(err);
      draw();
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
  const remove = (rs) => sure(rs.length === 1 ? 'Delete this alert?' : `Delete ${rs.length} alerts?`, 'Nothing more is sent for them. What they already sent stays in Fired.', 'Delete', async () => {
    try {
      for (const r of rs) took(await send(`/api/event-alerts/rules/${r.id}/delete`));
      toast(rs.length === 1 ? 'Alert deleted.' : `${rs.length} alerts deleted.`, rs.map((r) => ev(r.event)?.name || r.event).join(', '));
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

  // --- A rule in the side panel: what happens, only when and then, changed in place -----------------------------
  const condInputs = (r, e, attr) => e.fields.map((f) => {
    const sel = (opts, v) => opts.map(([k, l]) => `<option value="${esc(k)}"${String(k) === String(v) ? ' selected' : ''}>${esc(l)}</option>`).join('');
    if (f === 'count') return `<label><span>How often</span><select ${attr}="count">${sel(data.counts.map((w, i) => [i, w]), r.count)}</select></label>`;
    if (f === 'group') return `<label><span>Group</span><input class="ds-input" ${attr}="group" value="${esc(r.group)}" placeholder="Any group" list="ea-groups" autocomplete="off" spellcheck="false"></label>`;
    if (f === 'what') return `<label><span>Objects</span><select ${attr}="what">${sel(Object.entries(WHAT), r.what)}</select></label>`;
    if (f === 'computers') return `<label><span>Computers</span><select ${attr}="computers">${sel(Object.entries(COMPUTERS), r.computers)}</select></label>`;
    if (f === 'after') return `<label><span>For more than</span><select ${attr}="after">${sel([5, 15, 30, 60].map((m) => [m, `${m} minutes`]), r.after)}</select></label>`;
    if (f === 'percent') return `<label><span>When it is</span><select ${attr}="percent">${sel([80, 90, 95].map((p) => [p, `${p}% full`]), r.percent)}</select></label>`;
    return '';
  }).join('');
  const thenRows = (r, e, pre) => `<div class="ea-then"><span>Severity</span><div class="seg" role="group" aria-label="Severity">${Object.entries(SEV).map(([k, [w]]) => `<button type="button" data-${pre}-sev="${k}" aria-pressed="${r.severity === k}">${w}</button>`).join('')}</div>`
    + `<span>Send to</span><div class="ea-chips">${data.types.map((t) => `<button type="button" class="ea-chip" data-${pre}-to="${esc(t.id)}" aria-pressed="${r.types.includes(t.id)}">${svg(r.types.includes(t.id) ? 'check' : 'plus')}${esc(t.name)}</button>`).join('') || '<span class="muted">No alert types yet.</span>'}</div>`
    + `<span>Quiet hours</span><label class="ea-sw"><input type="checkbox" class="switch" data-${pre}-quiet${r.quiet ? ' checked' : ''}><span>${r.quiet ? 'Sends even in quiet hours' : 'Waits until quiet hours end'}</span></label>`
    + (e?.user ? `<span>Also</span><select data-${pre}-flow aria-label="Also run a workflow"><option value="">Do nothing else</option>${data.workflows.map((w) => `<option value="${esc(w.id)}"${w.id === r.workflow ? ' selected' : ''}>${esc(`Run ${w.name} on the user, after someone approves`)}</option>`).join('')}</select>` : '')
    + '</div>';
  const openRule = (r, again = false) => {
    if (!r) return;
    const e = ev(r.event);
    const fired = data.fired.filter((f) => f.rule === r.id).slice(0, 10);
    panel.body.innerHTML = `<div class="dw-head">${areaPic(e).replace('class="pic"', 'class="pic dw-pic"')}<div class="dw-name"><h2 id="dw-title">${esc(e?.name || r.event)}</h2><p>${esc(say(r))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar"><button type="button" class="dw-qa" data-ea-test>${svg('bell')}<span>Send test</span></button><button type="button" class="dw-qa" data-ea-turn>${svg(r.on ? 'pause' : 'play')}<span>${r.on ? 'Turn off' : 'Turn on'}</span></button>`
      + `<button type="button" class="dw-qa" data-ea-del>${svg('trash')}<span>Delete</span></button></div>`
      + `<div class="dw-strip"><div><span class="l">State</span><span class="v">${r.on ? '<span class="ubadge green">On</span>' : '<span class="ubadge grey">Off</span>'}</span></div><div><span class="l">Severity</span><span class="v">${sevBadge(r.severity)}</span></div>`
      + `<div><span class="l">Send to</span><span class="v">${esc(sendTo(r))}</span></div><div><span class="l">Fired (7 days)</span><span class="v">${week(r)}</span></div><div><span class="l">Added by</span><span class="v">${esc(r.by)}</span></div></div>`
      + section('What happens', `<div class="mrow ea-what">${areaPic(e)}<div class="two"><b>${esc(e?.name || r.event)}</b><small>${esc([e?.area, r.aboutName || r.about ? `Only for ${r.aboutName || r.about}` : '', e?.note].filter(Boolean).join(' · '))}</small></div></div>`
        + (e?.fields.length ? `<div class="ea-conds">${condInputs(r, e, 'data-ea-rc')}</div>` : none('It alerts each time this happens.')))
      + section('Then', thenRows(r, e, 'ea-r'))
      + section(`Fired <small>${fired.length}</small>`, fired.length ? `<div class="dw-list">${fired.map((f) => `<div class="mrow">${sevPic(f.severity)}<div class="two"><b>${esc(f.title)}</b><small>${esc(`${R.when(f.at)} · ${SENT[f.sent] || f.sent}${f.state === 'new' ? ' · New' : ''}`)}</small></div></div>`).join('')}</div>` : none('It has not fired yet.'));
    if (!again) panel.open('earule', r.id);
  };
  const openFired = (f, again = false) => {
    if (!f) return;
    const r = ruleOf(f.rule);
    const flow = r?.workflow && data.workflows.find((w) => w.id === r.workflow);
    const who = f.subjectName || f.subject;
    const approval = f.approval === 'waiting' ? section('Workflow waiting', `<p class="ea-ask">${esc(flow ? `Run ${flow.name} on ${who}?` : `The workflow for this alert was deleted or turned off.`)}</p>`
      + `<div class="ea-ask-do">${flow ? `<button type="button" class="primary small" data-ea-approve>${svg('play')}Approve and run</button>` : ''}<button type="button" class="small" data-ea-dismiss>Dismiss</button></div>`)
      : f.approval ? section('Workflow', `<p class="ea-ask">${esc(f.approval === 'approved' ? `Approved and run by ${f.approvalBy}.` : `Dismissed by ${f.approvalBy}.`)}</p>`) : '';
    panel.body.innerHTML = `<div class="dw-head">${sevPic(f.severity, 'pic dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(f.title)}</h2><p>${esc(`${R.when(f.at)} · ${ev(f.event)?.name || f.event}`)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${f.state === 'new' ? `<button type="button" class="dw-qa" data-ea-ack>${svg('check')}<span>Acknowledge</span></button>` : ''}${r ? `<button type="button" class="dw-qa" data-ea-torule>${svg('bell')}<span>Open the alert</span></button>` : ''}</div>`
      + `<div class="dw-strip"><div><span class="l">State</span><span class="v">${f.state === 'new' ? '<span class="ubadge amber">New</span>' : '<span class="ubadge grey">Acknowledged</span>'}</span></div><div><span class="l">Severity</span><span class="v">${sevBadge(f.severity)}</span></div>`
      + `<div><span class="l">Sent</span><span class="v">${esc(SENT[f.sent] || f.sent)}</span></div><div><span class="l">When</span><span class="v">${esc(R.when(f.at))}</span></div>${f.ackBy ? `<div><span class="l">Acknowledged by</span><span class="v">${esc(f.ackBy)}</span></div>` : ''}</div>`
      + approval
      + section('What happened', `<p class="ea-text">${esc(f.text || f.title)}</p>${f.why ? `<p class="ea-text muted">${esc(f.why)}</p>` : ''}${r ? `<p class="ea-say">${esc(say(r))}</p>` : ''}`);
    if (!again) panel.open('eafired', f.id);
  };
  panel.el.addEventListener('click', async (e) => {
    if (panel.is('earule')) {
      const r = ruleOf(panel.key);
      if (!r) return;
      let b;
      if (e.target.closest('[data-ea-test]')) {
        try {
          await send(`/api/event-alerts/rules/${r.id}/test`);
          toast('Test sent.', `To ${sendTo(r)}.`);
        } catch (err) {
          oops(err);
        }
      } else if (e.target.closest('[data-ea-turn]')) saveRule(r, { on: !r.on }, r.on ? 'Alert off.' : 'Alert on.');
      else if (e.target.closest('[data-ea-del]')) remove([r]);
      else if ((b = e.target.closest('[data-ea-r-sev]'))) { if (b.dataset.eaRSev !== r.severity) saveRule(r, { severity: b.dataset.eaRSev }); }
      else if ((b = e.target.closest('[data-ea-r-to]'))) { const id = b.dataset.eaRTo; saveRule(r, { types: r.types.includes(id) ? r.types.filter((x) => x !== id) : [...r.types, id] }); }
    } else if (panel.is('eafired')) {
      const f = firedOf(panel.key);
      if (!f) return;
      if (e.target.closest('[data-ea-ack]')) ack([f.id]);
      else if (e.target.closest('[data-ea-torule]')) { tab = 'rules'; draw(); openRule(ruleOf(f.rule)); }
      else if (e.target.closest('[data-ea-dismiss]')) {
        try { took(await send(`/api/event-alerts/fired/${f.id}/approval`, { state: 'dismissed' })); toast('Workflow dismissed.', f.title); } catch (err) { oops(err); }
      } else if (e.target.closest('[data-ea-approve]')) approve(f);
    }
  });
  panel.el.addEventListener('change', (e) => {
    if (!panel.is('earule')) return;
    const r = ruleOf(panel.key);
    const t = e.target;
    if (!r) return;
    if (t.dataset.eaRc) saveRule(r, { [t.dataset.eaRc]: ['count', 'after', 'percent'].includes(t.dataset.eaRc) ? Number(t.value) : t.value.trim() });
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
  const blank = () => ({ event: '', count: 0, group: '', what: '', computers: '', after: 15, percent: 90, severity: 'warning', types: data?.start ? [data.start] : [], quiet: false, workflow: '' });
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
    $('ea-build-conds').innerHTML = !e ? '<p class="muted ea-none">Pick what happens first.</p>' : e.fields.length ? `<div class="ea-conds">${condInputs(d, e, 'data-ea-bc')}</div>` : '<p class="muted ea-none">There is nothing to narrow down. It alerts each time.</p>';
    $('ea-build-then').innerHTML = thenRows(d, e, 'ea-b');
    $('ea-build-say').textContent = e ? say(d) : 'When: pick what happens. Then: choose who hears about it.';
    $('ea-build-save').disabled = !e;
  };
  const openBuild = async () => {
    if (!data) await load();
    if (!data) return;
    draft = blank();
    find = '';
    $('ea-build-find').value = '';
    R.say('ea-build-problem', '');
    drawBuild();
    $('ea-build').showModal();
    $('ea-build-find').focus();
  };
  $('ea-build-find').addEventListener('input', (e) => { find = e.target.value; drawBuild('events'); });
  $('ea-build').addEventListener('change', (e) => {
    const t = e.target;
    if (!draft) return;
    if (t.name === 'ea-ev') { const x = ev(t.value); Object.assign(draft, { event: t.value, severity: x.severity, count: 0, group: '', what: '', computers: '' }); if (!x.user) draft.workflow = ''; drawBuild(); return; }
    if (t.dataset.eaBc) draft[t.dataset.eaBc] = ['count', 'after', 'percent'].includes(t.dataset.eaBc) ? Number(t.value) : t.value.trim();
    else if (t.matches('[data-ea-b-quiet]')) draft.quiet = t.checked;
    else if (t.matches('[data-ea-b-flow]')) draft.workflow = t.value;
    drawBuild('rest');
  });
  $('ea-build').addEventListener('click', async (e) => {
    let b;
    if (!draft) return;
    if ((b = e.target.closest('[data-ea-b-sev]'))) { draft.severity = b.dataset.eaBSev; drawBuild('rest'); return; }
    if ((b = e.target.closest('[data-ea-b-to]'))) { const id = b.dataset.eaBTo; draft.types = draft.types.includes(id) ? draft.types.filter((x) => x !== id) : [...draft.types, id]; drawBuild('rest'); return; }
    if (e.target.closest('#ea-build-cancel')) { $('ea-build').close(); return; }
    if (!e.target.closest('#ea-build-save') || !draft.event) return;
    R.busy($('ea-build'), 'Saving the alert', ev(draft.event).name);
    try {
      const r = took(await send('/api/event-alerts/rules', { ...draft, from: 'new' }));
      $('ea-build').close();
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
      + `<div class="ea-me-then">${thenRows({ severity: me.severity, types: me.types, quiet: false }, null, 'ea-m').replace(/<span>Quiet hours<\/span>[\s\S]*?<\/label>/, '')}</div>`;
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
  $('ea-me-cancel').addEventListener('click', () => $('ea-me').close());
  // The Alerts tab on a user's panel (users.js).
  const pane = (x) => {
    const rs = about(x);
    return section(`Alerts about ${esc(x.name || x.username)} <small>${rs.length}</small>`, rs.length ? `<div class="dw-list">${rs.map((r) => `<div class="mrow">${areaPic(ev(r.event))}<div class="two"><b>${esc(ev(r.event)?.user || ev(r.event)?.name || r.event)}</b>`
      + `<small>${esc(`${SEV[r.severity][0]} · ${sendTo(r)}`)}</small></div>${r.on ? '<span class="ubadge green">On</span>' : '<span class="ubadge grey">Off</span>'}</div>`).join('')}</div>` : none(`Nobody is alerted about ${x.name || x.username} yet.`),
    `<span class="dw-sec-do"><button type="button" class="small" data-ea-me="${esc(x.username)}" data-ea-me-name="${esc(x.name || '')}">${svg('bell')}Alert me…</button></span>`);
  };
  document.addEventListener('click', (e) => {
    const b = e.target.closest('#dw-body [data-ea-me]');
    if (b) openMe({ username: b.dataset.eaMe, name: b.dataset.eaMeName });
  });

  R.alerts = { can: may, me: openMe, about, pane };
})();
