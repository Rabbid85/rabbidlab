// Ctrl+K search: one window in the middle of the screen that finds pages, Active Directory users, groups and computers,
// devices, network devices, links, sensors, settings and actions. Ctrl+K or / opens it, and so does Search in the top bar;
// arrows move, Enter opens, Esc closes. G then a letter goes to a page. Each person can change those letters in Settings ›
// Keyboard shortcuts; they're kept on the server for them (UserKeys.cs), and Reset puts the defaults back.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, get, send, toast } = R;
  const can = (perms) => !perms || perms.split(/\s+/).some((p) => has(p));

  // Every page that has a shortcut: its key by default, where it is in the sidebar, and who can see it.
  const PAGES = [
    { id: 'overview', label: 'Overview', icon: 'overview', where: 'Lab', key: 'o', hash: '#overview', can: 'overview.view' },
    { id: 'links', label: 'Links', icon: 'link', where: 'Lab', key: 'l', hash: '#links', can: 'links.view' },
    { id: 'sensors', label: 'Sensors', icon: 'activity', where: 'Lab', key: 's', hash: '#sensors', can: 'sensors.view' },
    { id: 'devices', label: 'Devices', icon: 'monitor', where: 'Lab', key: 'd', hash: '#seats', can: 'seats.view sessions.view' },
    { id: 'network', label: 'Network', icon: 'radar', where: 'Lab', key: 'n', hash: '#network', can: 'network.view' },
    { id: 'users', label: 'Users', icon: 'user', where: 'Active Directory', key: 'u', hash: '#ad/users', can: 'ad.view ad.connect' },
    { id: 'groups', label: 'Groups', icon: 'users', where: 'Active Directory', key: 'g', hash: '#ad/groups', can: 'ad.view ad.connect' },
    { id: 'computers', label: 'Computers', icon: 'laptop', where: 'Active Directory', key: 'c', hash: '#ad/computers', can: 'ad.view ad.connect' },
    { id: 'gpo', label: 'Group Policy', icon: 'policy', where: 'Directory', key: 'p', hash: '#gpo', can: 'gpo.view' },
    { id: 'domain', label: 'Domain health', icon: 'shield', where: 'Directory', key: 'h', hash: '#ad/domain', can: 'ad.view ad.connect' },
    { id: 'sync', label: 'Sync', icon: 'refresh', where: 'Directory', key: 'y', hash: '#sync', can: 'ad.view' },
    { id: 'apps', label: 'Connected apps', icon: 'plug', where: 'Apps', key: '', hash: '#apps', can: 'apps.view' },
    { id: 'alerts', label: 'Alerts', icon: 'bell', where: 'Alerts', key: 'a', hash: '#alerts', can: 'sensors.alerts' },
    { id: 'workflows', label: 'Workflows', icon: 'org', where: 'Automation', key: 'f', hash: '#workflows', can: 'ad.view' },
    { id: 'lifecycle', label: 'Lifecycle', icon: 'calendar', where: 'Automation', key: '', hash: '#lifecycle', can: 'ad.view' },
    { id: 'people', label: 'Roles and people', icon: 'shield', where: 'Settings', key: 'r', hash: '#settings/people', can: 'access.view' },
    { id: 'wiki', label: 'Wiki', icon: 'book', where: 'Help', key: 'w', hash: '#wiki' },
    { id: 'settings', label: 'Settings', icon: 'gear', where: 'Help', key: 't', hash: '#settings' },
    { id: 'whats-new', label: 'What’s new', icon: 'new', where: 'Help', key: '', hash: '#whats-new' },
  ];
  const GROUPS = ['Recent', 'Pages', 'Users', 'Groups', 'Computers', 'Devices', 'Network', 'Links', 'Sensors', 'Settings', 'Actions'];

  // --- Your keys: the defaults, with yours over them ------------------------------------------------------------------
  let mine = {}; // page id -> letter, or '-' for none (/api/me/keys)
  const keyOf = (p) => {
    const k = mine[p.id] ?? p.key;
    return k && k !== '-' ? k : '';
  };
  const loadKeys = () => get('/api/me/keys').then((r) => { mine = r.keys || {}; drawKeys(); }).catch(() => {});

  // --- Doing what an item says ------------------------------------------------------------------------------------------
  // Goes to a page, then waits for something on it (a button, a list) before doing the rest.
  const after = (hash, find, then) => {
    if (location.hash !== hash) location.hash = hash;
    const started = Date.now();
    const look = () => {
      const el = find();
      if (el && !el.disabled) then(el);
      else if (Date.now() - started < 8000) setTimeout(look, 150);
    };
    setTimeout(look, 60);
  };
  const openPc = (id) => after('#seats', () => (R.devices.computers().some((c) => c.id === id) ? document.body : null), () => R.devices.open(id));
  // What a result does, as data, so a recent one can do it again after a reload.
  const run = (go) => {
    if (go.t === 'hash') location.hash = go.h;
    else if (go.t === 'ad') R.adOpen(go.page, go.key);
    else if (go.t === 'pc') openPc(go.id);
    else if (go.t === 'net') R.openNetwork(go.id);
    else if (go.t === 'url') window.open(go.u, '_blank', 'noopener');
    else if (go.t === 'act') ACTIONS.find((a) => a.id === go.id)?.run();
  };
  const ACTIONS = [
    { id: 'scan', label: 'Scan the network', icon: 'radar', hint: 'Network', words: 'ping sweep discover find devices', can: 'network.scan', run: () => after('#network', () => $('net-scan'), (b) => b.click()) },
    { id: 'sync', label: 'Sync domain controllers', icon: 'refresh', hint: 'Sync', words: 'replicate replication repadmin', can: 'ad.change', run: () => after('#sync', () => document.querySelector('[data-sy-run="dcs"]'), (b) => b.click()) },
    { id: 'updates', label: 'Check for updates', icon: 'updates', hint: 'Updates', words: 'update upgrade version', can: 'updates.check', run: () => $('hero-check').click() },
    { id: 'link', label: 'Add a link', icon: 'plus', hint: 'Links', words: 'new bookmark site', can: 'links.edit', run: () => after('#links', () => $('ln-add'), (b) => b.click()) },
    { id: 'sensor', label: 'Add a sensor', icon: 'plus', hint: 'Sensors', words: 'new check monitor ping', can: 'sensors.edit', run: () => after('#sensors', () => $('sn-add'), (b) => b.click()) },
    { id: 'alert', label: 'New alert', icon: 'bell', hint: 'Alerts', words: 'notify notification rule', can: 'sensors.alerts', run: () => after('#alerts', () => $('ea-new'), (b) => b.click()) },
    { id: 'keys', label: 'Keyboard shortcuts', icon: 'grip', hint: 'Settings', words: 'keys hotkeys shortcut', run: () => { location.hash = '#settings/keys'; } },
    { id: 'signout', label: 'Sign out', icon: 'logout', hint: 'Your account', words: 'log off logout', run: () => $('signout').click() },
  ];

  // --- What there is to find ------------------------------------------------------------------------------------------
  let sensors = null; // /api/sensors, read when the window opens
  let net = null; // /api/network, when Network hasn't read it
  const fresh = () => {
    if (can('sensors.view')) get('/api/sensors').then((r) => { sensors = r.sensors || []; if (open) draw(); }).catch(() => {});
    if (can('network.view') && !R.networkNow?.()) get('/api/network').then((r) => { net = r; if (open) draw(); }).catch(() => {});
  };
  // Settings' pages and their parts, as the Settings sidebar lists them for you; one that's also under Pages shows there only.
  const nameOf = (a) => (a?.querySelector('span') || a)?.textContent.trim() || ''; // without a count beside it
  const settingsItems = () => [...document.querySelectorAll('#page-settings nav a[data-sub]')].filter((a) => !a.hidden && !a.classList.contains('dev-only')
    && can(a.dataset.can) && can(document.querySelector(`#page-settings nav a[data-sub="${a.dataset.sub}"]:not([data-sec])`)?.dataset.can)
    && !PAGES.some((p) => p.hash === a.getAttribute('href'))).map((a) => {
    const sec = a.dataset.sec;
    const main = nameOf(a);
    const parent = sec ? nameOf(document.querySelector(`#page-settings nav a[data-sub="${a.dataset.sub}"]:not([data-sec])`)) : '';
    return { ref: `set:${a.dataset.sub}:${sec || ''}`, group: 'Settings', icon: 'gear', label: sec ? `${parent} › ${main}` : main, hint: 'Settings', go: { t: 'hash', h: a.getAttribute('href') } };
  });
  const everything = () => {
    const out = PAGES.filter((p) => can(p.can)).map((p) => ({ ref: `page:${p.id}`, group: 'Pages', icon: p.icon, label: p.label, hint: p.where, key: keyOf(p), go: { t: 'hash', h: p.hash } }));
    if (has('seats.view')) {
      R.devices.computers().forEach((c) => out.push({ ref: `pc:${c.id}`, group: 'Devices', icon: c.server ? 'server' : 'monitor', label: c.computer, hint: c.os || '', go: { t: 'pc', id: c.id } }));
    }
    const nd = R.networkNow?.() || net;
    (nd?.devices || []).forEach((d) => out.push({ ref: `net:${d.id}`, group: 'Network', icon: 'radar', label: d.name || d.ip, hint: d.name ? d.ip : d.vendor || '', words: `${d.ip} ${d.vendor || ''} ${d.mac || ''}`, go: { t: 'net', id: d.id } }));
    (R.linksNow?.()?.links || []).forEach((l) => out.push({ ref: `link:${l.id}`, group: 'Links', icon: 'link', label: l.name || l.url, hint: String(l.url || '').replace(/^https?:\/\//, ''), words: l.url, go: { t: 'url', u: l.url } }));
    (sensors || []).forEach((s) => out.push({ ref: `sensor:${s.id}`, group: 'Sensors', icon: 'activity', label: s.name, hint: s.target || s.type || '', words: `${s.type || ''} ${s.target || ''}`, go: { t: 'hash', h: '#sensors' } }));
    out.push(...settingsItems());
    ACTIONS.filter((a) => can(a.can)).forEach((a) => out.push({ ref: `act:${a.id}`, group: 'Actions', icon: a.icon, label: a.label, hint: a.hint, words: a.words, go: { t: 'act', id: a.id } }));
    return out;
  };
  // Active Directory is asked as you type: users, groups and computers whose names match (/api/ad/find).
  const AD_GROUP = { user: 'Users', group: 'Groups', computer: 'Computers' };
  const AD_PAGE = { user: 'users', group: 'groups', computer: 'computers' };
  const AD_ICON = { user: 'user', group: 'users', computer: 'laptop' };
  const adFound = new Map(); // query -> results
  let adAsking = '';
  let adTimer = 0;
  const askAd = (q) => {
    clearTimeout(adTimer);
    if (!has('ad.view') || q.length < 2 || adFound.has(q)) return;
    adTimer = setTimeout(async () => {
      adAsking = q;
      draw();
      try {
        const rows = await get(`/api/ad/find?q=${encodeURIComponent(q)}`);
        adFound.set(q, rows.map((m) => ({ ref: `ad:${m.kind}:${m.dn}`, group: AD_GROUP[m.kind] || 'Users', icon: AD_ICON[m.kind] || 'user', label: m.name,
          hint: m.kind === 'user' ? m.username || '' : m.where || '', words: `${m.username || ''} ${m.where || ''}`, go: { t: 'ad', page: AD_PAGE[m.kind] || 'users', key: m.kind === 'group' ? m.dn : m.kind === 'computer' ? m.name : m.username } })));
      } catch {
        adFound.set(q, []);
      }
      if (adAsking === q) adAsking = '';
      if (open) draw();
    }, 220);
  };

  // --- Recent: the last few things opened from here, in this browser ---------------------------------------------------
  const recentKey = () => `rabbidlab.search.recent.${R.me()?.username || ''}`;
  const recent = () => { try { return JSON.parse(localStorage.getItem(recentKey()) || '[]'); } catch { return []; } };
  const remember = (x) => {
    if (x.group === 'Pages' || x.group === 'Actions' || x.group === 'Settings') return;
    const item = { ref: x.ref, kind: x.group.replace(/s$/, ''), icon: x.icon, label: x.label, hint: x.hint, go: x.go };
    try { localStorage.setItem(recentKey(), JSON.stringify([item, ...recent().filter((r) => r.ref !== x.ref)].slice(0, 6))); } catch { /* not kept, then */ }
  };

  // --- Matching -----------------------------------------------------------------------------------------------------------
  const words = (q) => q.toLowerCase().split(/\s+/).filter(Boolean);
  const hits = (list, q) => {
    const w = words(q);
    const text = (x) => `${x.label} ${x.hint || ''} ${x.words || ''} ${x.group}`.toLowerCase();
    const starts = (x) => (x.label.toLowerCase().startsWith(w[0]) ? 0 : 1);
    return list.filter((x) => w.every((t) => text(x).includes(t))).sort((a, b) => starts(a) - starts(b));
  };
  const mark = (label, q) => {
    const w = words(q).sort((a, b) => b.length - a.length)[0];
    const i = w ? label.toLowerCase().indexOf(w) : -1;
    return i < 0 ? esc(label) : `${esc(label.slice(0, i))}<mark>${esc(label.slice(i, i + w.length))}</mark>${esc(label.slice(i + w.length))}`;
  };

  // --- The window -------------------------------------------------------------------------------------------------------
  const layer = $('search');
  const input = $('search-q');
  const list = $('search-list');
  let open = false;
  let items = [];
  let at = 0;
  const kbd = (k) => `<kbd class="kbd">${esc(k)}</kbd>`;
  const keysOf = (k) => (k ? `<span class="srch-keys">${kbd('G')}${kbd(k.toUpperCase())}</span>` : '<span class="srch-keys"></span>');
  const pick = () => {
    const q = input.value.trim();
    if (!q) {
      const all = everything();
      const back = recent().map((r) => ({ ...r, group: 'Recent' })).slice(0, 3);
      return [...back, ...all.filter((x) => x.group === 'Pages'), ...all.filter((x) => x.group === 'Actions')];
    }
    askAd(q.toLowerCase());
    const found = hits([...everything(), ...(adFound.get(q.toLowerCase()) || [])], q);
    return GROUPS.flatMap((g) => found.filter((x) => x.group === g).slice(0, g === 'Pages' ? 8 : 6));
  };
  const draw = () => {
    const q = input.value.trim();
    items = pick();
    at = Math.min(at, Math.max(0, items.length - 1));
    let g = '';
    const waiting = q && adAsking === q.toLowerCase() ? '<p class="srch-wait">Searching Active Directory…</p>' : '';
    list.innerHTML = (items.length ? items.map((x, i) => {
      const head = x.group !== g ? `<p class="srch-g">${esc((g = x.group))}</p>` : '';
      return `${head}<div class="srch-it" data-si="${i}" role="option" id="srch-${i}"><span class="pic small">${svg(x.icon)}</span><span class="srch-l">${mark(x.label, q)}</span>`
        + `<span class="srch-h">${esc(x.group === 'Recent' ? x.kind : x.hint || '')}</span>${keysOf(x.key)}</div>`;
    }).join('') : waiting ? '' : `<p class="srch-none">No results for <b>${esc(q)}</b>. Try a name, a computer, an address or a page.</p>`) + waiting;
    marked();
  };
  const marked = () => list.querySelectorAll('[data-si]').forEach((el) => {
    const on = Number(el.dataset.si) === at;
    el.classList.toggle('on', on);
    el.setAttribute('aria-selected', String(on));
    if (on) {
      el.scrollIntoView({ block: 'nearest' });
      input.setAttribute('aria-activedescendant', el.id);
    }
  });
  const show = () => {
    if (!R.me()) return;
    open = true;
    layer.hidden = false;
    input.value = '';
    at = 0;
    fresh();
    draw();
    input.focus();
  };
  const hide = () => {
    if (!open) return;
    open = false;
    layer.hidden = true;
    input.blur();
  };
  const choose = (i = at) => {
    const x = items[i];
    if (!x) return;
    hide();
    remember(x);
    run(x.go);
  };
  input.addEventListener('input', () => { at = 0; draw(); });
  list.addEventListener('click', (e) => { const it = e.target.closest('[data-si]'); if (it) choose(Number(it.dataset.si)); });
  list.addEventListener('mousemove', (e) => { const it = e.target.closest('[data-si]'); if (it && Number(it.dataset.si) !== at) { at = Number(it.dataset.si); marked(); } });
  layer.querySelector('[data-search-shut]').addEventListener('click', hide);
  $('top-search').addEventListener('click', show);

  // --- The keys: Ctrl+K and / anywhere, arrows, Enter and Esc while it's open, G then a letter for a page ------------------
  const chord = $('search-chord');
  let waitG = 0;
  let chordTimer = 0;
  const chordShow = (on) => {
    clearTimeout(chordTimer);
    chord.hidden = !on;
    if (!on) return;
    const pages = PAGES.filter((p) => can(p.can) && keyOf(p));
    chord.innerHTML = `${kbd('G')}<span>then ${pages.slice(0, 8).map((p) => `<b>${esc(keyOf(p).toUpperCase())}</b> ${esc(p.label)}`).join(', ')}${pages.length > 8 ? ', …' : ''}</span>`;
    chordTimer = setTimeout(() => { waitG = 0; chord.hidden = true; }, 1600);
  };
  window.addEventListener('keydown', (e) => {
    const k = String(e.key || '').toLowerCase();
    const ctrl = e.ctrlKey || e.metaKey;
    if (ctrl && k === 'k' && !e.altKey && !e.shiftKey) {
      if (document.querySelector('dialog[open]:modal') || !R.me()) return;
      e.preventDefault();
      if (open) hide(); else show();
      return;
    }
    if (open) {
      if (k === 'arrowdown' || k === 'arrowup') { e.preventDefault(); if (items.length) { at = (at + (k === 'arrowdown' ? 1 : -1) + items.length) % items.length; marked(); } }
      else if (k === 'enter') { e.preventDefault(); choose(); }
      else if (k === 'escape') { e.preventDefault(); e.stopPropagation(); hide(); }
      else if (k === 'tab') e.preventDefault(); // focus stays in the box
      return;
    }
    const typing = e.target instanceof Element && e.target.closest('input, textarea, select, [contenteditable="true"], [contenteditable=""]');
    if (typing || ctrl || e.altKey || !R.me() || document.querySelector('dialog[open]:modal')) return;
    if (waitG) {
      waitG = 0;
      chordShow(false);
      const to = PAGES.find((p) => can(p.can) && keyOf(p) === k);
      if (to) { e.preventDefault(); location.hash = to.hash; }
      return;
    }
    if (k === 'g' && !e.shiftKey) { waitG = Date.now(); chordShow(true); }
    else if (k === '/') { e.preventDefault(); show(); }
  }, true);

  // --- Settings › Keyboard shortcuts: a letter for each page you can see, kept for you ----------------------------------
  const drawKeys = () => {
    const box = $('keys-list');
    if (!box || box.contains(document.activeElement)) return;
    box.innerHTML = PAGES.filter((p) => can(p.can)).map((p) => `<div class="ds-row"><div class="ds-l"><b>${esc(p.label)}</b><span>${esc(p.where)}${mine[p.id] && mine[p.id] !== p.key ? ` · Default ${p.key ? `G then ${p.key.toUpperCase()}` : 'none'}` : ''}</span></div>`
      + `<div class="ds-r keys-r">${kbd('G')}<span class="keys-then">then</span><input class="ds-input keys-in" data-key-page="${p.id}" value="${esc(keyOf(p).toUpperCase())}" maxlength="1" autocomplete="off" spellcheck="false" aria-label="Key after G for ${esc(p.label)}" placeholder="–"></div></div>`).join('');
    $('keys-reset').disabled = !Object.keys(mine).length;
  };
  const saveKeys = async (page, value) => {
    const k = value.trim().toLowerCase();
    const next = Object.fromEntries(PAGES.map((p) => [p.id, keyOf(p) || '-']));
    next[page] = k || '-';
    const twice = k && PAGES.find((p) => p.id !== page && next[p.id] === k);
    if (twice) {
      R.say('keys-problem', `G then ${k.toUpperCase()} already goes to ${twice.label}. Change that one first, or pick another letter.`);
      drawKeys();
      return;
    }
    try {
      mine = (await send('/api/me/keys', { keys: next })).keys || {};
      R.say('keys-problem', '');
      const p = PAGES.find((x) => x.id === page);
      toast('Shortcut saved.', k ? `G then ${k.toUpperCase()} goes to ${p.label}.` : `${p.label} has no shortcut.`);
    } catch (e) {
      if (!e.cancelled) R.say('keys-problem', e.message);
    }
    drawKeys();
  };
  $('keys-list').addEventListener('change', (e) => { const i = e.target.closest('[data-key-page]'); if (i) saveKeys(i.dataset.keyPage, i.value); });
  $('keys-list').addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.matches('[data-key-page]')) e.target.blur(); });
  $('keys-list').addEventListener('focusout', () => setTimeout(drawKeys, 0));
  $('keys-reset').addEventListener('click', async () => {
    try {
      mine = (await send('/api/me/keys/reset', {})).keys || {};
      R.say('keys-problem', '');
      toast('Shortcuts reset.', 'Every page has its default letter again.');
    } catch (e) {
      if (!e.cancelled) R.say('keys-problem', e.message);
    }
    drawKeys();
  });
  document.addEventListener('rabbidlab:me', loadKeys);
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'settings' && e.detail[1] === 'keys') drawKeys(); });
})();
