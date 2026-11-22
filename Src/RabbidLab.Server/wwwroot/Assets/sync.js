// Sync (Directory): every domain controller with every other, then Microsoft 365 through Entra Connect (SyncRun.cs).
// The page: Sync all, Sync DCs and Sync Microsoft 365 with Delta or Full, each DC and Microsoft 365 with the flask and
// its line, then every copy seen, newest first. Every row opens the side panel, where a failure says why and can retry.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, panel, toast, get, send, when, where } = R;

  const ICON = { user: 'user', group: 'users', computer: 'laptop', contact: 'user', ou: 'folder', other: 'list' };
  const FILTERS = [['all', 'Everything'], ['user', 'Users'], ['group', 'Groups'], ['computer', 'Computers'], ['bad', 'Problems']];
  const FLASK_IN = 'M10 3.6v5.8L4.6 18.4A1.7 1.7 0 0 0 6.1 21h11.8a1.7 1.7 0 0 0 1.5-2.6L14 9.4V3.6z';
  const FLASK = 'M9 3h6M10 3v6.2L4.6 18.4A1.7 1.7 0 0 0 6.1 21h11.8a1.7 1.7 0 0 0 1.5-2.6L14 9.2V3';

  let data = null; // /api/sync
  let type = 'delta';
  let filter = 'all';
  let timer = null;
  let wasRunning = false;
  let clip = 0;

  const time = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '');
  const at = (iso) => (iso && new Date(iso).toDateString() === new Date().toDateString() ? time(iso) : when(iso));
  const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;
  const section = (title, body) => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3></div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const strip = (pairs) => `<div class="dw-strip">${pairs.map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${esc(v)}</span></div>`).join('')}</div>`;
  const head = (icon, title, sub) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>`
    + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`;
  const m365Label = (m) => (m.label || '').replace('{at}', time(m.at));
  const dcNames = () => (data?.dcs || []).map((d) => d.name);
  const missingSomewhere = (c) => dcNames().some((n) => c.places[n] === 'pending');

  // The flask fills with the part's progress, then gives way to a tick or a cross; the line along the row's foot does the same.
  const flask = () => {
    clip++;
    return `<span class="sy-ind"><svg class="fk-svg" viewBox="0 0 24 24"><defs><clipPath id="sy-fk-${clip}"><path d="${FLASK_IN}"/></clipPath></defs><g clip-path="url(#sy-fk-${clip})">`
      + `<rect class="fk-liq" x="0" y="0" width="24" height="24"/><circle class="fk-b" cx="10.6" cy="19" r="1"/><circle class="fk-b b2" cx="13.6" cy="19.4" r=".8"/></g><path class="fk-glass" d="${FLASK}"/></svg>`
      + `${svg('check', 'sy-ok')}${svg('x', 'sy-bad')}</span>`;
  };
  // A sync can finish between two answers, so the flask creeps up on its own while a row waits or runs, and a row that
  // has just finished fills to the top before the tick takes its place.
  const paint = (el, part, label) => {
    clearTimeout(Number(el.dataset.done || 0));
    const was = el.dataset.s;
    const going = part.s === 'wait' || part.s === 'run';
    const creep = going ? Math.min(part.s === 'run' ? 0.85 : 0.3, Number(el.dataset.creep || 0) + 0.12) : 0;
    el.dataset.creep = String(creep);
    // Only forward is animated: back to empty, or a new pass starting lower, jumps there.
    const show = (s, f) => {
      const snap = f < Number(el.dataset.f || 0) - 0.001 || (el.dataset.s === 'ok' && s !== 'ok');
      el.classList.toggle('snap', snap);
      el.dataset.s = s;
      el.dataset.f = String(f);
      el.querySelector('.fk-liq').style.transform = `scaleY(${f})`;
      el.querySelector('.sy-ln i').style.width = `${s === 'ok' ? 100 : s === 'idle' ? 0 : f * 100}%`;
      if (snap) requestAnimationFrame(() => requestAnimationFrame(() => el.classList.remove('snap')));
    };
    el.querySelector('.sy-lab').textContent = label;
    if (part.s === 'ok' && (was === 'wait' || was === 'run')) {
      show('run', 1);
      el.dataset.done = String(setTimeout(() => show('ok', 1), 1000));
      return;
    }
    show(part.s, going ? Math.max(part.s === 'run' ? part.f : 0, creep) : ['ok', 'bad'].includes(part.s) ? part.f : 0);
  };

  // --- Drawing ------------------------------------------------------------------------------------------------------
  const drawHero = () => {
    const changes = data.changes || [];
    const waiting = changes.filter(missingSomewhere).length;
    const reachable = data.dcs.filter((d) => d.s !== 'bad').length;
    $('sync-state').textContent = data.running ? 'Syncing now.' : waiting ? `${plural(waiting, 'change')} not on every domain controller yet.` : 'Every change is on every domain controller.';
    $('sync-n-waiting').textContent = String(waiting);
    $('sync-n-dcs').textContent = `${reachable} of ${data.dcs.length}`;
    $('sync-n-m365').textContent = data.m365?.at ? at(data.m365.at) : data.m365 ? 'Not yet' : 'Not set up';
    $('rail-sync-dot').hidden = !data.dcs.some((d) => d.s === 'bad');
  };

  const actions = () => {
    const may = has('ad.change') && data.canSync;
    const why = !has('ad.change') ? 'Your roles can look but not sync.' : !data.canSync ? 'Set up the service account first.' : '';
    const dcWhy = why || (!data.windows ? 'Syncing domain controllers needs Rabbid Lab on Windows.' : '');
    const btn = (run, cls, icon, label, off) => `<button type="button" class="${cls}" data-sy-run="${run}"${off ? ` disabled data-tip="${esc(off)}"` : ''}>${svg(icon)}${label}</button>`;
    return `<div class="sy-acts">${btn('all', 'primary sy-btn', 'refresh', 'Sync all', data.running ? 'A sync is running.' : dcWhy)}`
      + `${btn('dcs', 'sy-btn sy-btn2', 'server', 'Sync DCs', data.running ? 'A sync is running.' : dcWhy)}`
      + `${data.m365 ? btn('m365', 'sy-btn sy-btn2', 'cloud', 'Sync Microsoft 365', data.running ? 'A sync is running.' : why) : ''}<span class="grow"></span>`
      + (data.m365 ? `<span class="sy-type"><span class="sy-typel">Microsoft 365</span><span class="sy-seg" role="group" aria-label="Microsoft 365 sync type">`
        + `<button type="button" data-sy-type="delta" aria-pressed="${type === 'delta'}" data-tip="Only what changed. Under a minute.">Delta</button>`
        + `<button type="button" data-sy-type="full" aria-pressed="${type === 'full'}" data-tip="Rereads every object. Minutes to hours.">Full</button></span></span>` : '')
      + '</div>'
      + (type === 'full' && data.m365 ? `<p class="sy-info">${svg('alert')}<span><b>Full</b> rereads every object in AD and Entra ID and runs every sync rule. Minutes on a small domain, hours on a big one, and nothing else syncs until it's done. Use it after changing what syncs, or when something is stuck.</span></p>` : '');
  };

  const statusRow = (key, title, sub) => `<div class="sy-row" data-sy-open="${esc(key)}" data-s="idle">${flask()}<span class="sy-name"><b>${esc(title)}</b><small>${esc(sub)}</small></span>`
    + '<span class="sy-st"><span class="sy-lab"></span></span><span class="sy-ln"><i></i></span></div>';

  const feedLine = (e) => {
    const word = e.s === 'bad' ? 'Failed' : e.s === 'notyet' ? 'Not on every DC yet' : e.m ? 'Sent' : 'Copied';
    return `<div class="sy-fl" data-sy-open="feed:${esc(e.id)}" data-s="${esc(e.s)}"><small>${esc(at(e.at))}</small><span class="sy-flr">${esc(e.from)} → ${esc(e.to)}</span>`
      + `<span class="sy-obj">${svg(ICON[e.kind] || 'list')}<span><b>${esc(e.name)}</b><small>${esc([e.what, ...(e.details || [])].join(' · '))}</small></span></span>`
      + `<span class="sy-cell" data-s="${esc(e.s)}">${svg(e.s === 'bad' ? 'x' : e.s === 'notyet' ? 'alert' : 'check')}<span>${word}</span></span></div>`;
  };

  // Every change since the last sync, whether or not anything had to be copied: what it is, where it was made, how many DCs have it.
  const has1 = (p) => p === 'origin' || p === 'ok';
  const changeLine = (c) => {
    const names = dcNames();
    const n = names.filter((x) => has1(c.places[x])).length;
    const s = n === names.length ? 'ok' : names.some((x) => c.places[x] === 'pending') ? 'pending' : 'unknown';
    return `<div class="sy-fl" data-sy-open="chg:${esc(c.id)}" data-s="${s}"><small>${esc(at(c.at))}</small><span class="sy-flr">Made on ${esc(c.on || 'an unknown DC')}</span>`
      + `<span class="sy-obj">${svg(ICON[c.kind] || 'list')}<span><b>${esc(c.name)}</b><small>${esc([c.what, ...(c.details || [])].join(' · '))}</small></span></span>`
      + `<span class="sy-cell" data-s="${s}">${svg(s === 'ok' ? 'check' : s === 'pending' ? 'history' : 'alert')}<span>${n === names.length ? `On all ${names.length} DCs` : `On ${n} of ${names.length} DCs`}</span></span></div>`;
  };

  const drawFeed = () => {
    const box = $('sy-feed');
    if (!box) return;
    // The copies made from here, and each change that reached every DC without one, newest first.
    const copied = new Set((data.feed || []).map((e) => e.change));
    const lines = [...(data.feed || []).map((e) => ({ at: e.at, kind: e.kind, bad: e.s !== 'ok', html: () => feedLine(e) })),
      ...(data.changes || []).filter((c) => !copied.has(c.id)).map((c) => ({ at: c.at, kind: c.kind, bad: dcNames().some((n) => c.places[n] === 'pending'), html: () => changeLine(c) }))]
      .filter((x) => filter === 'all' || (filter === 'bad' ? x.bad : x.kind === filter)).sort((a, b) => (a.at < b.at ? 1 : -1));
    box.innerHTML = lines.map((x) => x.html()).join('')
      || `<p class="muted sy-none">${filter === 'all' ? `Nothing has changed since ${esc(at(data.since))}.` : 'Nothing here.'}</p>`;
  };

  // The rows stay put while a sync runs, so the flask and line move smoothly; only a different set of DCs draws them again.
  const drawRows = () => {
    const grid = $('sy-rows');
    const keys = [...data.dcs.map((d) => `dc:${d.name}`), ...(data.m365 ? ['m365'] : [])];
    if (grid.dataset.keys !== keys.join('|')) {
      grid.dataset.keys = keys.join('|');
      grid.innerHTML = data.dcs.map((d) => statusRow(`dc:${d.name}`, d.name, [d.site, d.readOnly ? 'read-only' : ''].filter(Boolean).join(', ') || d.host)).join('')
        + (data.m365 ? statusRow('m365', 'Microsoft 365', `Entra Connect on ${data.m365.server}`) : '');
    }
    for (const d of data.dcs) paint(grid.querySelector(`[data-sy-open="dc:${CSS.escape(d.name)}"]`), d, d.label);
    if (data.m365) paint(grid.querySelector('[data-sy-open="m365"]'), data.m365, m365Label(data.m365));
  };

  const draw = () => {
    const body = $('sync-body');
    R.skeleton(body, '');
    const changes = data.changes || [];
    const waiting = changes.filter(missingSomewhere).length;
    if (!body.querySelector('#sy-rows')) {
      body.innerHTML = '<div class="gcard sy-card"><div class="sy-head"><h2 id="sy-title"></h2><p class="muted" id="sy-sub"></p></div><div id="sy-acts"></div><div class="sy-th sy-th-rows"><span></span><span>Domain controller</span><span>State</span></div><div class="sy-rows" id="sy-rows"></div></div>'
        + '<div class="gcard"><div class="gbar"><span class="sy-gtitle" id="sy-ftitle">Every change, newest first</span><span class="grow"></span><span class="sy-seg" id="sy-filters"></span></div>'
        + '<div class="sy-th sy-th-fl"><span>When</span><span>Where</span><span>Change</span><span>Result</span></div><div class="sy-feed" id="sy-feed"></div></div>';
    }
    $('sy-title').textContent = waiting ? `${plural(waiting, 'change')} not everywhere yet` : 'Every change is everywhere';
    $('sy-sub').textContent = `${plural(changes.length, 'change')} since ${at(data.since)}${data.more ? ', the newest 300 shown' : ''}. The newest is first.`;
    $('sy-acts').innerHTML = actions();
    $('sy-filters').innerHTML = FILTERS.map(([v, t]) => `<button type="button" data-sy-filter="${v}" aria-pressed="${filter === v}">${t}</button>`).join('');
    drawRows();
    drawFeed();
    drawHero();
    if (panel.is('sync') && openKey) openPanel(openKey, true);
    else mark('');
  };

  // --- The side panel -----------------------------------------------------------------------------------------------
  let openKey = '';
  const changeList = (list, place) => `<div class="dw-list">${list.map((c) => `<div class="mrow"><span class="pic small">${svg(ICON[c.kind] || 'list')}</span>`
    + `<div class="two"><b>${esc(c.name)}</b><small>${esc(`${c.what}, made on ${c.on || 'an unknown DC'} at ${at(c.at)}`)}</small></div>`
    + `<span class="sy-cell" data-s="${esc(c.places[place])}">${svg(c.places[place] === 'notyet' ? 'alert' : 'history')}<span>${c.places[place] === 'notyet' ? 'Not on every DC then' : 'Waiting'}</span></span></div>`).join('')}</div>`;
  const eventList = (list, word) => `<div class="dw-list">${list.map((e) => `<div class="mrow"><span class="pic small">${svg(ICON[e.kind] || 'list')}</span>`
    + `<div class="two"><b>${esc(e.name)}</b><small>${esc(`${e.what}, ${word(e)} ${at(e.at)}`)}</small></div></div>`).join('')}</div>`;
  const trouble = (error, advice) => section('What went wrong', `<div class="sy-err"><b>${esc(error)}</b>${advice ? `<p>${esc(advice)}</p>` : ''}</div>`);
  const details = (list) => `<ul class="does">${list.map((d) => {
    const i = d.indexOf(': ');
    return i > 0 ? `<li><b>${esc(d.slice(0, i))}</b><span>${esc(d.slice(i + 2))}</span></li>` : `<li><b>${esc(d)}</b><span></span></li>`;
  }).join('')}</ul>`;

  const dcPanel = (d) => {
    const missing = (data.changes || []).filter((c) => c.places[d.name] === 'pending');
    const copied = (data.feed || []).filter((e) => e.to === d.name && e.s === 'ok').slice(0, 8);
    const may = has('ad.change') && data.canSync && data.windows && !data.running;
    return head('server', d.name, [d.host, d.site, d.readOnly ? 'Read-only' : ''].filter(Boolean).join(' · '))
      + (d.s === 'bad' && may ? `<div class="dw-bar">${qa('data-sy-run="dcs"', 'refresh', 'Retry')}</div>` : '')
      + strip([['State', d.label], ['Site', d.site || 'Unknown'], ['Missing', String(missing.length)]])
      + (d.error ? trouble(d.error, d.advice) : '')
      + section(`Missing here <small>${missing.length}</small>`, missing.length ? changeList(missing, d.name) : none('Nothing. It has every change.'))
      + (copied.length ? section('Last copied here', eventList(copied, (e) => `from ${e.from},`)) : '');
  };
  const m365Panel = () => {
    const m = data.m365;
    const open = (data.changes || []).filter((c) => ['pending', 'notyet'].includes(c.places.M365));
    const sent = (data.feed || []).filter((e) => e.m && e.s === 'ok').slice(0, 8);
    const may = has('ad.change') && data.canSync && !data.running;
    return head('cloud', 'Microsoft 365', `Entra Connect on ${m.server}`)
      + (may && m.client && m.online ? `<div class="dw-bar">${qa('data-sy-run="m365"', 'refresh', m.s === 'bad' ? 'Retry' : 'Sync Microsoft 365')}</div>` : '')
      + strip([['State', m365Label(m)], ['Last sync from here', m.at ? at(m.at) : 'None yet'], ['Next on its own', m.next ? at(m.next) : 'Every 30 minutes']])
      + (m.error ? trouble(m.error, m.advice) : '')
      + section(`Not sent from here yet <small>${open.length}</small>`, (open.length ? changeList(open, 'M365') : none('Nothing. Every change it syncs has been sent.'))
        + '<p class="dw-how">Entra Connect also sends changes by itself every 30 minutes, so some of these may be there already. Computers and OUs aren\'t synced to Microsoft 365.</p>')
      + (sent.length ? section('Last sent', eventList(sent, () => 'sent')) : '');
  };
  const feedPanel = (e) => {
    const word = e.s === 'bad' ? 'Failed' : e.s === 'notyet' ? 'Not on every DC yet' : e.m ? 'Sent' : 'Copied';
    const run = e.s === 'bad' ? 'dcs' : e.s === 'notyet' ? 'all' : '';
    const may = has('ad.change') && data.canSync && !data.running && (run !== 'dcs' || data.windows);
    return head(ICON[e.kind] || 'list', `${e.from} → ${e.to}`, `${at(e.at)} · ${e.name}, ${e.what.toLowerCase()}`)
      + (run && may ? `<div class="dw-bar">${qa(`data-sy-run="${run}"`, 'refresh', run === 'dcs' ? 'Retry' : 'Sync all')}</div>` : '')
      + strip([['Result', word], ['Change', e.name], ['Kind', e.kind === 'ou' ? 'OU' : e.kind.charAt(0).toUpperCase() + e.kind.slice(1)]])
      + (e.s !== 'ok' && e.why ? trouble(e.why, e.s === 'bad' ? (data.dcs.find((d) => d.name === e.to)?.advice || '') : 'Sync all copies it to every domain controller first, then sends it.') : '')
      + section('What changed', e.details?.length ? details(e.details) : none(e.what));
  };
  // One change: where it is on each domain controller and in Microsoft 365, and what changed.
  const PLACE = { origin: 'Made here', ok: 'Has it', pending: 'Missing', unknown: 'Couldn\'t be read' };
  const M365_PLACE = { ok: 'Sent from here', pending: 'Not sent from here yet', notyet: 'Not on every DC when it synced', na: 'Not synced to Microsoft 365' };
  const changePanel = (c) => {
    const missing = dcNames().some((n) => c.places[n] === 'pending');
    const may = has('ad.change') && data.canSync && data.windows && !data.running;
    const where = data.dcs.map((d) => `<div class="mrow"><span class="pic small">${svg('server')}</span><div class="two"><b>${esc(d.name)}</b><small>${esc(d.site || d.host)}</small></div>`
      + `<span class="sy-cell" data-s="${esc(c.places[d.name])}">${svg(has1(c.places[d.name]) ? 'check' : c.places[d.name] === 'pending' ? 'history' : 'alert')}<span>${PLACE[c.places[d.name]] || 'Unknown'}</span></span></div>`).join('')
      + (data.m365 ? `<div class="mrow"><span class="pic small">${svg('cloud')}</span><div class="two"><b>Microsoft 365</b><small>Entra Connect on ${esc(data.m365.server)}</small></div>`
        + `<span class="sy-cell" data-s="${esc(c.places.M365)}">${svg(c.places.M365 === 'ok' ? 'check' : c.places.M365 === 'na' ? 'minus' : 'history')}<span>${M365_PLACE[c.places.M365] || 'Unknown'}</span></span></div>` : '');
    return head(ICON[c.kind] || 'list', c.name, [c.what, c.sub].filter(Boolean).join(' · '))
      + (missing && may ? `<div class="dw-bar">${qa('data-sy-run="dcs"', 'refresh', 'Sync DCs')}</div>` : '')
      + strip([['Made on', c.on || 'Unknown'], ['When', at(c.at)], ['Kind', c.kind === 'ou' ? 'OU' : c.kind.charAt(0).toUpperCase() + c.kind.slice(1)]])
      + section('Where it is', `<div class="dw-list">${where}</div>`)
      + section('What changed', c.details?.length ? details(c.details) : none(c.what))
      + '<p class="dw-how">Active Directory keeps only the new values, so what it was before isn\'t shown.</p>';
  };
  const openPanel = (key, again = false) => {
    const d = key.startsWith('dc:') ? data.dcs.find((x) => x.name === key.slice(3)) : null;
    const e = key.startsWith('feed:') ? (data.feed || []).find((x) => x.id === key.slice(5)) : null;
    const c = key.startsWith('chg:') ? (data.changes || []).find((x) => x.id === key.slice(4)) : null;
    const html = d ? dcPanel(d) : key === 'm365' && data.m365 ? m365Panel() : e ? feedPanel(e) : c ? changePanel(c) : '';
    if (!html) {
      if (again) panel.close();
      return;
    }
    openKey = key;
    panel.body.innerHTML = html;
    if (!again) panel.open('sync', key);
    mark(key);
  };
  const mark = (key) => document.querySelectorAll('#page-sync [data-sy-open]').forEach((r) => r.classList.toggle('on', r.dataset.syOpen === key));

  // --- Reading and syncing ------------------------------------------------------------------------------------------
  const stopPolling = () => { clearTimeout(timer); timer = null; };
  const load = async (fresh = false) => {
    stopPolling();
    try {
      data = await get(`/api/sync${fresh ? '?fresh=true' : ''}`);
    } catch (err) {
      R.skeleton($('sync-body'), '');
      $('sync-body').innerHTML = `<div class="gcard sy-card"><p class="sy-problem">${esc(err.message)}</p></div>`;
      $('sync-state').textContent = "Sync can't read Active Directory.";
      if (!data) for (const id of ['sync-n-waiting', 'sync-n-dcs', 'sync-n-m365']) $(id).textContent = '—';
      return;
    }
    draw();
    if (wasRunning && !data.running) finished();
    wasRunning = data.running;
    // While one runs, asked every second; the page shows each step as it lands.
    if (data.running && where()[0] === 'sync' && !document.hidden) timer = setTimeout(() => load(), 1000);
  };
  const finished = () => {
    const bad = data.dcs.filter((d) => d.s === 'bad').map((d) => d.name);
    const m = data.m365?.s === 'bad';
    if (data.problem) toast('The sync stopped.', data.problem, { tone: 'warn' });
    else if (bad.length || m) toast('Sync done, with problems.', `${[...bad, ...(m ? ['Microsoft 365'] : [])].join(', ')} didn't sync. Click a row to see why.`, { tone: 'warn' });
    else toast('Sync done.', 'Every domain controller has every change.');
  };
  const run = async (what) => {
    try {
      data = await send('/api/sync', { what, full: type === 'full' });
      wasRunning = true;
      draw();
      timer = setTimeout(() => load(), 800);
    } catch (err) {
      toast("That didn't work.", err.message, { tone: 'warn' });
    }
  };

  $('page-sync').addEventListener('click', (e) => {
    const t = e.target.closest('[data-sy-run], [data-sy-type], [data-sy-filter], [data-sy-open]');
    if (!t || t.disabled) return;
    if (t.dataset.syRun) run(t.dataset.syRun);
    else if (t.dataset.syType) { type = t.dataset.syType; $('sy-acts').innerHTML = actions(); }
    else if (t.dataset.syFilter) { filter = t.dataset.syFilter; draw(); }
    else if (panel.shows('sync', t.dataset.syOpen)) { panel.close(); mark(''); }
    else openPanel(t.dataset.syOpen);
  });
  panel.body.addEventListener('click', (e) => {
    const t = e.target.closest('[data-sy-run]');
    if (t && panel.is('sync')) run(t.dataset.syRun);
  });
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'sync') { stopPolling(); return; }
    if (!data) R.skeleton($('sync-body'), 'rows', 6);
    load(true);
  });
  document.addEventListener('visibilitychange', () => { if (!document.hidden && where()[0] === 'sync' && data?.running) load(); });
})();
