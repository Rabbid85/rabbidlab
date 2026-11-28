// Windows accounts on Devices: a computer's Accounts tab (its local accounts and who's in its Administrators group), the
// Local admins list across computers, and this server as a row of Computers. console.js draws the page; this adds to it
// through RabbidLab.devices, as devices.js does. Changes go to /api/windows; users.js has the account and password windows.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, panel, toast, sure, get, send, chip, section, qa, none, day, listed: and, oops, closeX, below } = R;

  const NEEDS = '1.1.183'; // the first client that reads and changes Administrators
  const KIND = { local: 'Local account', localgroup: 'Local group', user: 'Domain user', group: 'Domain group', computer: 'Domain computer', wellknown: 'Windows group', unknown: 'Unknown account' };
  const ICON = { local: 'user', localgroup: 'users', user: 'user', group: 'users', computer: 'monitor', wellknown: 'users', unknown: 'user' };
  const fact = (label, value) => `<div class="dw-fact"><span class="l">${esc(label)}</span><span class="v">${value ? esc(value) : '<span class="muted">Not set</span>'}</span></div>`;
  const pic = (kind, cls = 'small') => `<span class="pic ${cls}">${svg(ICON[kind] || 'user')}</span>`;
  const x = (attr, label) => `<button type="button" class="at-more dw-x" ${attr} data-tip="Remove" aria-label="${esc(label)}">${svg('x')}</button>`;
  const del = (attr, label) => `<button type="button" class="at-more dw-x" ${attr} data-tip="Delete" aria-label="${esc(label)}">${svg('trash')}</button>`;
  const seen = (iso) => {
    if (!iso) return 'Never signed in';
    const d = Math.floor((Date.now() - new Date(iso)) / 86400000);
    return `Last sign-in ${d < 1 ? 'today' : d < 30 ? `${d} day${d === 1 ? '' : 's'} ago` : day(iso)}`;
  };
  const how = (p) => (p.direct && p.via.length ? `Direct member, also through ${and(p.via)}` : p.direct ? 'Direct member' : `Member through ${and(p.via)}`);
  const short = (name) => name.slice(name.indexOf('\\') + 1); // AMW\mwiles -> mwiles
  const prefix = (name) => (name.includes('\\') ? name.slice(0, name.indexOf('\\')) : '');

  // --- What's known -----------------------------------------------------------------------------------------------
  let adm = null; // /api/devices/admins: { computers: [{ id, computer, server, online, version, checked, admins, locals }], groups }
  let admAt = 0;
  const loadAdm = async () => {
    adm = await get('/api/devices/admins');
    admAt = Date.now();
  };
  const fresh = () => (Date.now() - admAt > 60000 ? loadAdm().catch(() => {}) : Promise.resolve());
  const known = (id) => adm?.computers.find((c) => c.id === id);
  const groupOf = (sid) => (sid && adm?.groups ? adm.groups[sid] : null);

  // The people a computer's Administrators come to: local accounts and domain users added directly, and the people in
  // its domain groups (read from AD on the server). Groups whose people aren't known stay as they are.
  const peopleOf = (admins, locals) => {
    const out = new Map();
    const add = (key, base, via) => {
      const p = out.get(key) || { ...base, key, direct: false, via: [] };
      if (via) p.via.push(via);
      else p.direct = true;
      out.set(key, p);
    };
    for (const m of admins || []) {
      const g = m.kind === 'group' ? groupOf(m.sid) : null;
      if (g) {
        for (const u of g.people) add(`${prefix(m.name)}\\${u.account}`.toUpperCase(), { kind: 'user', name: `${prefix(m.name)}\\${u.account}`, shown: u.name || u.account, enabled: u.enabled }, short(m.name));
      } else {
        const a = m.kind === 'local' ? (locals || []).find((y) => y.name.toUpperCase() === short(m.name).toUpperCase()) : null;
        add(m.name.toUpperCase(), { kind: m.kind, name: m.name, shown: m.kind === 'local' ? short(m.name) : m.name, enabled: a ? a.enabled : null, sid: m.sid, unread: m.kind === 'group' || m.kind === 'localgroup' });
      }
    }
    // Domain users who are also in a group get their name and state from AD.
    for (const p of out.values()) {
      if (p.kind === 'user' && p.shown === p.name) {
        const g = (admins || []).map((m) => groupOf(m.sid)).filter(Boolean).flatMap((y) => y.people).find((u) => u.account.toUpperCase() === short(p.name).toUpperCase());
        if (g) Object.assign(p, { shown: g.name || g.account, enabled: g.enabled });
      }
    }
    return [...out.values()].sort((a, b) => (a.kind === b.kind ? a.shown.localeCompare(b.shown) : a.kind === 'user' ? -1 : b.kind === 'user' ? 1 : a.kind.localeCompare(b.kind)));
  };

  // --- This server, as a row of Computers ---------------------------------------------------------------------------
  let server = null; // from /api/windows/computers
  let ready = {}; // computer id -> why it can't take changes now, or null
  const loadComputers = async () => {
    if (!has('windows.view')) return;
    try {
      const list = await get('/api/windows/computers');
      const s = list.find((c) => c.server);
      server = s ? { id: 'server', computer: s.name, os: s.os, server: true, approved: true, online: true, version: s.version, address: '::1', user: null } : null;
      ready = Object.fromEntries(list.map((c) => [c.id, c.ready ? null : c.why || 'This computer cannot be changed right now.']));
    } catch {
      // Without it there's no server row: the clients still list.
    }
    D.draw();
  };
  // Not when this server has the client too: its own row already lists it.
  D.hooks.extra = () => (server && has('windows.view') && !D.computers().some((c) => (c.computer || '').toUpperCase() === server.computer.toUpperCase()) ? [server] : []);
  let asked = false;
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'seats' || asked) return;
    asked = true;
    loadComputers();
  });

  // --- A computer's Accounts tab ------------------------------------------------------------------------------------
  const live = {}; // computer id -> { accounts, groups, admins } as it last answered, plus { error, loading }
  let at = null; // the computer open in the panel
  let open = null; // { kind: 'local' | 'user', name } opened inside the tab
  let view = 'members'; // Administrators as Windows lists it, or the people it comes to
  let showBuilt = false;
  let adding = false;
  const folded = new Set(); // domain groups opened in the list, by SID

  const loadLive = async (id) => {
    live[id] = { ...(live[id] || {}), loading: true, error: null };
    try {
      const got = await get(`/api/windows/${encodeURIComponent(id)}/accounts`);
      live[id] = { accounts: got.accounts, groups: got.groups, admins: got.admins };
    } catch (e) {
      live[id] = { ...(live[id] || {}), loading: false, error: e.message };
    }
    if (panel.shows('seats', id)) D.panelAgain();
  };

  const accountsSection = (c, l) => {
    const all = l.accounts || [];
    const list = all.filter((a) => showBuilt || !a.builtIn);
    const hidden = all.length - list.length;
    const adminNames = new Set((l.admins || []).filter((m) => m.kind === 'local').map((m) => short(m.name).toUpperCase()));
    const isAdmin = (a) => (l.admins ? adminNames.has(a.name.toUpperCase()) : (a.groups || []).some((g) => g === adminsName(l)));
    const rows = list.map((a) => `<div class="mrow acc-row" data-acc-open="local:${esc(a.name)}">${pic('local')}`
      + `<div class="two"><b>${esc(a.name)}</b><small>${esc(seen(a.lastSignIn))}${a.neverExpires ? ' · Password never expires' : ''}</small></div>`
      + `<span class="acc-chips">${isAdmin(a) ? chip('Administrator', 'ember', 'shield') : ''}${a.enabled ? '' : chip('Disabled', 'off', 'power')}${a.locked ? chip('Locked out', 'amber', 'lock') : ''}</span>`
      + `${has('windows.delete') && !a.builtIn && !ready[c.id] ? del(`data-acc-del="${esc(a.name)}"`, `Delete ${a.name}`) : ''}</div>`).join('');
    const more = hidden ? `<button type="button" class="linkish acc-more" data-acc-built>Show ${hidden} built-in account${hidden === 1 ? '' : 's'}</button>`
      : showBuilt && all.some((a) => a.builtIn) ? '<button type="button" class="linkish acc-more" data-acc-built>Hide built-in accounts</button>' : '';
    return section(`Local accounts <small>${list.length}</small>`, `${rows ? `<div class="dw-list">${rows}</div>` : none('No accounts other than the built-in accounts.')}${more}`,
      has('windows.create') && !ready[c.id] ? `<button type="button" class="small" data-acc-new>${svg('plus')}New account</button>` : '');
  };
  const adminsName = (l) => (l.groups || []).find((g) => g.sid === 'S-1-5-32-544')?.name || 'Administrators';

  const memberRow = (m, locals) => {
    const a = m.kind === 'local' ? (locals || []).find((y) => y.name.toUpperCase() === short(m.name).toUpperCase()) : null;
    const g = m.kind === 'group' ? groupOf(m.sid) : null;
    const second = m.kind === 'group' ? (g ? `Domain group · ${g.people.length}${g.more ? '+' : ''} ${g.people.length === 1 ? 'member' : 'members'}` : 'Domain group')
      : a?.builtIn ? 'Built-in account' : KIND[m.kind] || KIND.unknown;
    const isOpen = g && folded.has(m.sid);
    const sub = !isOpen ? '' : g.people.map((u) => `<div class="mrow acc-row acc-sub" data-acc-open="user:${esc(`${prefix(m.name)}\\${u.account}`)}">${pic('user')}`
      + `<div class="two"><b>${esc(u.name || u.account)}</b><small>${esc(`${prefix(m.name)}\\${u.account}`)}</small></div><span class="acc-chips">${u.enabled ? '' : chip('Disabled', 'off', 'power')}</span></div>`).join('');
    const opens = m.kind === 'local' ? `data-acc-open="local:${esc(short(m.name))}"` : m.kind === 'user' ? `data-acc-open="user:${esc(m.name)}"` : g ? `data-acc-fold="${esc(m.sid)}"` : '';
    return `<div class="mrow${opens ? ' acc-row' : ''}" ${opens}>${pic(m.kind)}<div class="two"><b>${esc(m.name)}</b><small>${esc(second)}</small></div>`
      + `<span class="acc-chips">${a && !a.enabled ? chip('Disabled', 'off', 'power') : ''}</span>`
      + `<span class="acc-chev${isOpen ? ' up' : ''}">${g ? svg('chevron-down') : ''}</span>`
      + `${has('windows.change') && m.sid ? x(`data-acc-out="${esc(m.sid)}"`, `Remove ${m.name} from Administrators`) : ''}</div>${sub}`;
  };

  const personRow = (p) => `<div class="mrow${p.kind === 'local' || p.kind === 'user' ? ' acc-row' : ''}"${p.kind === 'local' ? ` data-acc-open="local:${esc(short(p.name))}"` : p.kind === 'user' ? ` data-acc-open="user:${esc(p.name)}"` : ''}>`
    + `${pic(p.kind)}<div class="two"><b>${esc(p.shown)}</b><small>${esc(p.unread ? `${p.name} · Members not available` : `${p.name} · ${how(p)}`)}</small></div>`
    + `<span class="acc-chips">${p.enabled === false ? chip('Disabled', 'off', 'power') : ''}</span></div>`;

  const adminsSection = (c, admins, locals, note) => {
    if (!admins) {
      return section('Administrators', none(below(c.version, NEEDS) && !c.server ? `Requires client ${NEEDS} or later. The client updates automatically within a few minutes.` : 'Windows did not return the group members.'));
    }
    const people = peopleOf(admins, locals);
    const can = has('windows.change') && !ready[c.id] && !note;
    const right = `<div class="acc-right"><div class="seg"><button type="button" data-acc-view="members" aria-pressed="${view === 'members'}">Members</button>`
      + `<button type="button" data-acc-view="people" aria-pressed="${view === 'people'}">Effective</button></div>`
      + (can ? `<button type="button" class="small" data-acc-add aria-pressed="${adding}">${svg(adding ? 'x' : 'plus')}${adding ? 'Done' : 'Add'}</button>` : '') + '</div>';
    const lead = note || (view === 'members' ? `Members of this group are administrators of ${c.computer}.${admins.some((m) => groupOf(m.sid)) ? ' Click a domain group to see its members.' : ''}`
      : `Every account with administrator rights on ${c.computer}, including members of domain groups.${adm && !adm.groups ? ' Active Directory cannot be read from this server, so domain group members are not listed.' : ''}`);
    const add = !(adding && can) ? '' : '<div class="adder acc-add"><label class="find"><svg><use href="#i-search"/></svg>'
      + '<input type="search" id="acc-add-find" data-find placeholder="Local account or DOMAIN\\name" aria-label="Account to add" autocomplete="off" spellcheck="false"></label><div class="drop" data-drop hidden></div></div>';
    const rows = view === 'members' ? admins.map((m) => memberRow(m, locals)).join('') : people.map(personRow).join('');
    return section(`Administrators <small>${view === 'members' ? admins.length : people.length}</small>`, `<p class="dw-how acc-lead">${esc(lead)}</p>${add}<div class="dw-list">${rows || none('No members.')}</div>`, right);
  };

  const back = `<button type="button" class="linkish acc-back" data-acc-back>${svg('back')}All accounts</button>`;
  const detail = (c, l) => {
    if (open.kind === 'local') {
      const a = (l.accounts || []).find((y) => y.name.toUpperCase() === open.name.toUpperCase());
      if (!a) {
        open = null;
        return pane(c);
      }
      const gs = a.groups || [];
      const others = (l.groups || []).map((g) => g.name).filter((g) => !gs.includes(g));
      const change = has('windows.change') && !ready[c.id];
      const acts = (change ? qa('data-acc-act="password"', 'key', 'Set password') + qa('data-acc-act="power"', a.enabled ? 'user-off' : 'user-check', a.enabled ? 'Disable' : 'Enable') : '')
        + (has('windows.delete') && !a.builtIn && !ready[c.id] ? qa('data-acc-act="delete"', 'trash', 'Delete') : '');
      return `${back}<div class="acc-head">${pic('local', 'dw-pic')}<div><h3>${esc(a.name)}</h3><p>${a.builtIn ? 'Built-in account' : 'Local account'} on ${esc(c.computer)}</p></div></div>`
        + `<div class="dw-status acc-status">${gs.includes(adminsName(l)) ? chip('Administrator', 'ember', 'shield') : ''}${a.enabled ? chip('Enabled', 'ok') : chip('Disabled', 'off', 'power')}`
        + `${a.locked ? chip('Locked out', 'amber', 'lock') : ''}${a.builtIn ? chip('Built-in', 'muted') : ''}${a.neverExpires ? chip('Password never expires', 'muted') : ''}</div>`
        + (acts ? `<div class="dw-bar acc-bar">${acts}</div>` : '')
        + `<section class="dw-sec"><div class="dw-sec-head"><h3>Account</h3></div><div class="dw-facts">${fact('Full name', a.fullName)}${fact('Last sign-in', a.lastSignIn ? day(a.lastSignIn) : 'Never')}`
        + `${fact('Password set', a.passwordSet ? day(a.passwordSet) : '')}${fact('Description', a.description)}</div></section>`
        + section(`Groups <small>${gs.length}</small>`, gs.length ? `<div class="dw-list">${gs.map((g) => `<div class="mrow"><span class="pic small">${svg('users')}</span><div class="two"><b>${esc(g)}</b></div>`
          + `${change ? x(`data-acc-ungroup="${esc(g)}"`, `Remove from ${g}`) : ''}</div>`).join('')}</div>` : none('Not a member of any groups.'),
        change && others.length ? `<select class="gpick" data-acc-group aria-label="Add to a group"><option value="">Add to…</option>${others.map((g) => `<option>${esc(g)}</option>`).join('')}</select>` : '');
    }
    // A domain user: how they're an administrator here, and where else.
    const admins = l.admins || known(c.id)?.admins || [];
    const here = peopleOf(admins, l.accounts).find((p) => p.key === open.name.toUpperCase());
    const direct = admins.find((m) => m.kind === 'user' && m.name.toUpperCase() === open.name.toUpperCase());
    const elsewhere = (adm?.computers || []).filter((k) => k.id !== c.id).map((k) => [k, peopleOf(k.admins, k.locals).find((p) => p.key === open.name.toUpperCase())]).filter(([, p]) => p);
    return `${back}<div class="acc-head">${pic('user', 'dw-pic')}<div><h3>${esc(here?.shown || open.name)}</h3><p>${esc(open.name)} · Domain user</p></div></div>`
      + `<div class="dw-bar acc-bar">${has('ad.view') ? `<a class="dw-qa" href="#ad/users">${svg('users')}<span>Open Active Directory</span></a>` : ''}`
      + `${direct && has('windows.change') && !ready[c.id] ? qa(`data-acc-out="${esc(direct.sid)}"`, 'user-minus', 'Remove from Administrators') : ''}</div>`
      + section(`On ${esc(c.computer)}`, `<p class="dw-how">${here ? `${esc(how(here))}.` : `Not an administrator of ${esc(c.computer)}.`}${here && !direct ? ' To change this, remove the user from the group in Active Directory.' : ''}</p>`)
      + section(`Also an administrator of <small>${elsewhere.length}</small>`, elsewhere.length ? `<div class="dw-list">${elsewhere.map(([k, p]) => `<div class="mrow acc-row" data-acc-pc="${esc(k.id)}">`
        + `<span class="pic small">${svg(k.server ? 'server' : 'monitor')}</span><div class="two"><b>${esc(k.computer)}</b><small>${esc(how(p))}</small></div>${svg('right', 'acc-go')}</div>`).join('')}</div>`
        : none(adm ? 'No other computers.' : 'Loading…'));
  };

  // The tab: what the computer said just now, or what was last known when it can't answer.
  const pane = (c) => {
    const l = live[c.id];
    if (!l || (l.loading && !l.accounts)) {
      if (!l) loadLive(c.id);
      return '<div data-acc-skel></div>';
    }
    if (open && l.accounts) return detail(c, l);
    if (!l.accounts) {
      const k = known(c.id);
      const note = k?.checked ? `${c.computer} is unreachable. Showing members as of ${R.when(k.checked)}.` : null;
      return section('Local accounts', `<p class="dw-how warn">${esc(l.error || 'The computer is unreachable.')}</p>`, `<button type="button" class="small" data-acc-again>${svg('refresh')}Try again</button>`)
        + (k?.admins ? adminsSection(c, k.admins, k.locals, note) : '');
    }
    return accountsSection(c, l) + adminsSection(c, l.admins, l.accounts);
  };

  const H = D.hooks;
  const tabs0 = H.seatTabs;
  // Its count before the tab opens comes from the last fleet read (known), until the computer itself answers.
  const accCount = (c) => { const list = live[c.id]?.accounts || known(c.id)?.locals; return list ? list.filter((a) => !a.builtIn).length : ''; };
  H.seatTabs = (c) => [...(tabs0?.(c) || []), ...(has('windows.view') && c.approved ? [['accounts', 'Accounts', accCount(c)]] : [])];
  const pane0 = H.seatPane;
  H.seatPane = (c, tab) => {
    if (tab !== 'accounts') return pane0 ? pane0(c, tab) : null;
    // A box being typed in keeps what's typed when the panel is drawn again.
    const box = panel.body.querySelector('#acc-add-find');
    const keep = box ? { value: box.value, focus: document.activeElement === box } : null;
    const html = pane(c);
    queueMicrotask(() => {
      const skel = panel.body.querySelector('[data-acc-skel]');
      if (skel && !skel.firstChild) R.skeleton(skel, 'rows', 4);
      const again = panel.body.querySelector('#acc-add-find');
      if (keep && again) {
        again.value = keep.value;
        if (keep.focus) again.focus();
      }
    });
    return html;
  };
  const opened0 = H.seatOpened;
  H.seatOpened = (c) => {
    if (at !== c.id) {
      open = null;
      adding = false;
      folded.clear();
      delete live[c.id]; // asked again when its tab opens
    }
    at = c.id;
    opened0?.(c);
    if (has('windows.view')) {
      fresh().then(() => { if (panel.shows('seats', c.id)) D.panelAgain(); });
      loadComputers();
    }
  };
  const again = () => D.panelAgain();

  // After a change: the computer is asked again, and the Local admins list read again.
  const after = async (id) => {
    await loadLive(id);
    admAt = 0;
    await fresh();
    D.draw();
  };
  const change = async (id, path, body, said) => {
    const c = D.rows().find((y) => y.id === id);
    R.busy(panel.body, c?.server ? 'Applying changes on this server' : `Contacting ${c?.computer || 'the computer'}`);
    try {
      const answer = await send(path, body);
      toast(said, answer?.warning || '', answer?.warning ? { tone: 'warn' } : {});
    } catch (e) {
      oops(e);
    } finally {
      R.busy(panel.body, '');
    }
    await after(id);
  };

  // Who to add: this computer's own accounts as typed, and AD's users and groups when Active Directory is set up.
  R.dropdown(panel.body, {
    none: 'No matches. Type DOMAIN\\name to add an account by name.',
    find: async (q) => {
      const id = at;
      const l = live[id] || {};
      const have = new Set((l.admins || []).map((m) => m.name.toUpperCase()));
      const domain = (l.admins || []).map((m) => (m.kind === 'user' || m.kind === 'group' ? prefix(m.name) : '')).find(Boolean) || '';
      const text = q.toLowerCase();
      const found = (l.accounts || []).filter((a) => !have.has(`${D.rows().find((y) => y.id === id)?.computer || ''}\\${a.name}`.toUpperCase()) && a.name.toLowerCase().includes(text))
        .map((a) => ({ name: a.name, shown: a.name, kind: 'local', sub: a.builtIn ? 'Built-in account' : 'Local account' }));
      if (domain && has('ad.view')) {
        try {
          for (const m of await get(`/api/ad/find?q=${encodeURIComponent(q.includes('\\') ? short(q) : q)}`)) {
            if ((m.kind === 'user' || m.kind === 'group') && m.username && !have.has(`${domain}\\${m.username}`.toUpperCase())) {
              found.push({ name: `${domain}\\${m.username}`, shown: m.name, kind: m.kind, sub: `${domain}\\${m.username} · ${m.kind === 'group' ? 'Domain group' : 'Domain user'}` });
            }
          }
        } catch {
          // No service account: typed names still work.
        }
      }
      if (q.includes('\\') && !found.some((f) => f.name.toUpperCase() === q.toUpperCase())) found.push({ name: q.trim(), shown: q.trim(), kind: 'user', sub: 'Add by name' });
      return found.slice(0, 30);
    },
    draw: (f, i) => R.option(` data-pick="${i}"`, pic(f.kind), f.shown, f.sub),
    pick: (f) => {
      if (!panel.is('seats') || !at) return;
      const c = D.rows().find((y) => y.id === at);
      change(at, `/api/windows/${encodeURIComponent(at)}/admins`, { add: [f.name] }, `${f.name} added to Administrators on ${c?.computer}.`);
    },
  });

  panel.el.addEventListener('click', (e) => {
    if (!panel.is('seats') || panel.key !== at) return;
    const id = at;
    const c = D.rows().find((y) => y.id === id);
    if (!c) return;
    const on = (name) => e.target.closest(`[data-acc-${name}]`);
    const base = (name) => `/api/windows/${encodeURIComponent(id)}/accounts/${encodeURIComponent(name)}`;
    let b;
    if ((b = on('out'))) {
      e.stopPropagation();
      const sid = b.dataset.accOut;
      const m = (live[id]?.admins || []).find((y) => y.sid === sid);
      const who = m?.name || sid;
      sure(`Remove ${who} from Administrators?`, `${who} will no longer be an administrator of ${c.computer}.${m?.kind === 'group' ? ' Members who are administrators through another group keep that access.' : ''}`, 'Remove',
        () => change(id, `/api/windows/${encodeURIComponent(id)}/admins`, { remove: [sid] }, `${who} removed from Administrators on ${c.computer}.`));
    } else if ((b = on('del'))) {
      e.stopPropagation();
      const name = b.dataset.accDel;
      sure(`Delete ${name}?`, `${name} will be permanently deleted from ${c.computer}. Its profile folder and files are kept.`, `Delete ${name}`,
        () => change(id, `${base(name)}/delete`, {}, `${name} deleted.`));
    } else if ((b = on('fold'))) {
      const sid = b.dataset.accFold;
      if (folded.has(sid)) folded.delete(sid);
      else folded.add(sid);
      again();
    } else if ((b = on('open'))) {
      const v = b.dataset.accOpen;
      open = { kind: v.slice(0, v.indexOf(':')), name: v.slice(v.indexOf(':') + 1) };
      again();
    } else if (on('back')) {
      open = null;
      again();
    } else if (on('built')) {
      showBuilt = !showBuilt;
      again();
    } else if ((b = on('view'))) {
      view = b.dataset.accView;
      again();
    } else if (on('add')) {
      adding = !adding;
      again();
      queueMicrotask(() => panel.body.querySelector('#acc-add-find')?.focus());
    } else if (on('again')) {
      loadLive(id);
      again();
    } else if ((b = on('pc'))) {
      D.open(b.dataset.accPc, 'accounts');
    } else if (on('new')) {
      R.windowsAccounts.create({ id, name: c.computer, server: !!c.server, groups: live[id]?.groups || [], done: () => after(id) });
    } else if ((b = on('ungroup'))) {
      const g = b.dataset.accUngroup;
      change(id, `${base(open.name)}/groups`, { remove: [g] }, `${open.name} removed from ${g}.`);
    } else if ((b = on('act'))) {
      const name = open.name;
      const a = (live[id]?.accounts || []).find((y) => y.name === name);
      if (!a) return;
      if (b.dataset.accAct === 'password') {
        R.windowsAccounts.password(a.name, `${base(a.name)}/password`, `On ${c.computer}. Setting a password also unlocks the account if it is locked out.`, () => after(id));
      } else if (b.dataset.accAct === 'power') {
        change(id, `${base(a.name)}/enable`, { on: !a.enabled }, `${a.name} ${a.enabled ? 'disabled' : 'enabled'}.`);
      } else if (b.dataset.accAct === 'delete') {
        sure(`Delete ${a.name}?`, `${a.name} will be permanently deleted from ${c.computer}. Its profile folder and files are kept.`, `Delete ${a.name}`, async () => {
          open = null;
          await change(id, `${base(a.name)}/delete`, {}, `${a.name} deleted.`);
        });
      }
    }
  });
  panel.el.addEventListener('change', (e) => {
    if (!panel.is('seats') || panel.key !== at || !e.target.matches('[data-acc-group]') || !e.target.value || !open) return;
    change(at, `/api/windows/${encodeURIComponent(at)}/accounts/${encodeURIComponent(open.name)}/groups`, { add: [e.target.value] }, `${open.name} added to ${e.target.value}.`);
  });

  // --- Local admins: one list across the computers -------------------------------------------------------------------
  let tab = 'members';
  const typeOf = (m, c) => {
    const a = m.kind === 'local' ? (c.locals || []).find((y) => y.name.toUpperCase() === short(m.name).toUpperCase()) : null;
    return a?.builtIn ? 'Built-in account' : KIND[m.kind] || KIND.unknown;
  };
  const localOf = (r) => (r.kind === 'local' ? (r.pc.locals || []).find((y) => y.name.toUpperCase() === short(r.name).toUpperCase()) : null);
  const memberList = () => {
    const map = new Map();
    for (const c of adm?.computers || []) {
      for (const m of c.admins || []) {
        const key = m.kind === 'local' || m.kind === 'localgroup' ? `${c.id}|${m.name}`.toUpperCase() : (m.sid || m.name).toUpperCase();
        const r = map.get(key) || { key, kind: m.kind, name: m.name, sid: m.sid, pc: c, type: typeOf(m, c), on: [] };
        r.on.push(c);
        map.set(key, r);
      }
    }
    return [...map.values()];
  };
  const personList = () => {
    const map = new Map();
    for (const c of adm?.computers || []) {
      for (const p of peopleOf(c.admins, c.locals)) {
        const key = p.kind === 'local' ? `${c.id}|${p.key}` : p.key;
        const r = map.get(key) || { ...p, key, pc: c, on: [] };
        r.on.push({ c, direct: p.direct, via: p.via });
        map.set(key, r);
      }
    }
    return [...map.values()];
  };
  const stateText = (enabled) => (enabled === true ? 'Enabled' : enabled === false ? 'Disabled' : '');
  const stateCell = (enabled) => (enabled == null ? '<span class="muted">-</span>' : `<span class="state ${enabled ? 'online' : 'offline'}">${stateText(enabled)}</span>`);
  const memberState = (r) => (r.kind === 'local' ? localOf(r)?.enabled ?? null : r.kind === 'user' ? personList().find((p) => p.key === r.name.toUpperCase())?.enabled ?? null : null);
  const onText = (n) => `${n} computer${n === 1 ? '' : 's'}`;
  const howAll = (r) => {
    const d = r.on.some((o) => o.direct);
    const v = r.on.some((o) => o.via.length);
    return r.unread ? 'Members not available' : d && v ? 'Direct and through groups' : d ? 'Direct' : 'Through groups';
  };
  const who = (kind, text) => `<div class="who">${pic(kind, '')}<b class="ell">${esc(text)}</b></div>`;
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);
  // Ticks, like every list: what's ticked goes to Actions.
  const picked = new Set();
  const canTick = () => has('windows.change') || has('windows.delete');
  const lead = () => (canTick() ? { w: 18, head: '<label class="tk"><input type="checkbox" data-adm-tick-all aria-label="Select all"></label>',
    cell: (r) => `<label class="tk"><input type="checkbox" data-adm-tick="${esc(r.key)}" aria-label="Select ${esc(r.name)}"></label>` } : null);
  let membersG = null;
  const membersGrid = () => (membersG ||= R.grid({
    el: $('dv-admins'), key: 'admins', sort: { key: 'member', dir: 1 }, lead,
    cols: [
      { key: 'member', label: 'Member', w: 250, fixed: true, get: (r) => r.name, cell: (r) => who(r.kind, r.name) },
      { key: 'type', label: 'Type', w: 140, on: true, get: (r) => r.type },
      { key: 'on', label: 'Administrator of', w: 140, on: true, get: (r) => r.on.length, text: (r) => onText(r.on.length), cell: (r) => esc(onText(r.on.length)) },
      { key: 'state', label: 'State', w: 90, on: true, get: (r) => stateText(memberState(r)), cell: (r) => stateCell(memberState(r)) },
      { key: 'last', label: 'Last sign-in', w: 130, on: true, get: (r) => ms(localOf(r)?.lastSignIn), text: (r) => localOf(r)?.lastSignIn || '',
        cell: (r) => `<span class="muted">${r.kind !== 'local' ? '-' : localOf(r)?.lastSignIn ? esc(day(localOf(r).lastSignIn)) : 'Never'}</span>` },
      { key: 'sid', label: 'SID', w: 300, get: (r) => r.sid || '', cell: (r) => `<span class="mono muted ell">${esc(r.sid || '')}</span>` },
    ],
    row: (r) => ` data-acc-row="${esc(r.key)}"`,
    empty: () => D.more.admins.empty(), loading: () => !adm && !D.failed(),
    redraw: () => D.draw(),
  }));
  let peopleG = null;
  const peopleGrid = () => (peopleG ||= R.grid({
    el: $('dv-adminp'), key: 'admin-people', sort: { key: 'person', dir: 1 }, lead,
    cols: [
      { key: 'person', label: 'Name', w: 180, fixed: true, get: (r) => r.shown, cell: (r) => who(r.kind, r.shown) },
      { key: 'account', label: 'Account', w: 210, on: true, get: (r) => r.name, cell: (r) => `<span class="mono ell">${esc(r.name)}</span>` },
      { key: 'type', label: 'Type', w: 120, on: true, get: (r) => typeOf(r, r.pc) },
      { key: 'on', label: 'Administrator of', w: 140, on: true, get: (r) => r.on.length, text: (r) => onText(r.on.length), cell: (r) => esc(onText(r.on.length)) },
      { key: 'how', label: 'Membership', w: 180, on: true, get: howAll },
      { key: 'state', label: 'State', w: 90, on: true, get: (r) => stateText(r.enabled), cell: (r) => stateCell(r.enabled) },
    ],
    row: (r) => ` data-acc-row="${esc(r.key)}"`,
    empty: () => D.more.admins.empty(), loading: () => !adm && !D.failed(),
    redraw: () => D.draw(),
  }));
  const fld = (key, label, type, get1) => ({ key, label, type, get: get1 });
  const FIELDS = [['Administrator', [fld('name', 'Name', 'text', (r) => r.name), fld('type', 'Type', 'choice', (r) => (tab === 'people' ? typeOf(r, r.pc) : r.type)),
    fld('state', 'State', 'choice', (r) => stateText(tab === 'people' ? r.enabled : memberState(r))), fld('on', 'Administrator of (computers)', 'number', (r) => r.on.length)]]];

  D.lists.admins = ['Local admins', 'shield', 'Members of the Administrators group on each computer.'];
  D.more.admins = {
    // A tab beside Computers, not a list of its own down the left. Members and People are its views, where Computers' are.
    under: 'computers',
    tabs: (make) => make('#seats/admins', D.at() === 'admins', 'shield', 'Local admins', adm ? memberList().length : ''),
    views: () => [['members', 'Members', memberList], ['people', 'Effective members', personList]].map(([key, label, rows]) =>
      `<a href="#seats/admins" data-acc-tab="${key}"${tab === key ? ' aria-current="page"' : ''}><span class="nm">${label}</span><small>${adm ? rows().length : ''}</small></a>`).join(''),
    get table() { return tab === 'people' ? 'dv-adminp' : 'dv-admins'; },
    grid: () => (tab === 'people' ? peopleGrid() : membersGrid()),
    rows: () => (tab === 'people' ? personList() : memberList()),
    count: () => (adm ? memberList().length : ''),
    can: () => has('windows.view'),
    load: loadAdm,
    every: 30000, // this server's own Administrators group is read from Windows each time
    noun: 'administrators',
    fields: () => FIELDS,
    empty: () => (!adm ? 'Could not load the list.' : adm.computers.length ? `No computers have reported their Administrators group yet. Clients ${NEEDS} and later report it within 30 minutes.` : 'No computers yet.'),
    acts: { can: canTick, count: () => pickedRows().length, open: (button) => openActs(button) },
  };

  // --- What the ticks can do: take people out of Administrators, delete local accounts ------------------------------
  const pickedRows = () => (tab === 'people' ? personList() : memberList()).filter((r) => picked.has(r.key));
  // Each computer a ticked row was added to directly, with the SID it was added with.
  const outOf = (rows) => rows.flatMap((r) => (tab === 'people'
    ? r.on.filter((o) => o.direct && sidOn(o.c, r.name)).map((o) => ({ pc: o.c, sid: sidOn(o.c, r.name), name: r.name }))
    : r.sid ? r.on.map((c) => ({ pc: c, sid: r.sid, name: r.name })) : [])).filter((t) => !ready[t.pc.id]);
  const deletable = (rows) => rows.filter((r) => r.kind === 'local' && localOf(r) && !localOf(r).builtIn && !ready[r.pc.id]).map((r) => ({ pc: r.pc, name: short(r.name), full: r.name }));
  const drawTicks = () => {
    const table = $(tab === 'people' ? 'dv-adminp' : 'dv-admins');
    R.grid.ticks(table, 'admTick', picked);
    D.drawPicked?.();
  };
  // The Actions menu (actions.js): taking people out of Administrators, and deleting local accounts last.
  const openActs = (button) => {
    const rows = pickedRows();
    const out = has('windows.change') ? outOf(rows) : [];
    const gone = has('windows.delete') ? deletable(rows) : [];
    const left = rows.length - new Set([...out.map((t) => t.name), ...gone.map((t) => t.full)]).size;
    R.actions.open(button, {
      items: [...(out.length ? [{ act: 'out', label: 'Remove from Administrators…', hint: String(out.length) }] : []), { act: 'export', label: 'Export selected' }, { act: 'clear', label: 'Clear selection' },
        ...(gone.length ? ['-', { act: 'delete', label: 'Delete local accounts…', hint: String(gone.length), danger: true }] : [])],
      // Built-in accounts and computers still being read can't be changed: said when some ticked are those.
      note: (out.length || gone.length) && left > 0 ? `${left} of ${rows.length} selected cannot be changed: built-in accounts or computers still loading.` : '',
      pick: actPicked,
    });
  };
  // One change on each computer, then everything read again.
  const each = async (list, work, done) => {
    R.busy($('dv-lib'), `Contacting ${list.length === 1 ? list[0].pc.computer : `${new Set(list.map((t) => t.pc.id)).size} computers`}`);
    let failed = 0;
    for (const t of list) {
      try {
        await work(t);
      } catch {
        failed++;
      }
      delete live[t.pc.id];
    }
    R.busy($('dv-lib'), '');
    toast(failed ? `${failed} of ${list.length} changes failed.` : done, failed ? 'Computers that did not respond may be offline. The other changes were applied.' : '', failed ? { tone: 'warn' } : {});
    picked.clear();
    admAt = 0;
    await fresh();
    D.draw();
  };
  const pairs = (list) => list.map((t) => `${t.name} on ${t.pc.computer}`);
  function actPicked(act) {
    const rows = pickedRows();
    if (act === 'clear') {
      picked.clear();
      drawTicks();
    } else if (act === 'export') {
      const g = tab === 'people' ? peopleGrid() : membersGrid();
      g.csv(g.sorted(rows), 'local-admins-ticked');
    } else if (act === 'out') {
      const list = outOf(rows);
      sure(`Remove ${list.length === 1 ? list[0].name : `${list.length} memberships`} from Administrators?`, `${and(pairs(list).slice(0, 6))}${list.length > 6 ? ` and ${list.length - 6} more` : ''}. Members of domain groups stay administrators wherever those groups are added.`, 'Remove',
        () => each(list, (t) => send(`/api/windows/${encodeURIComponent(t.pc.id)}/admins`, { remove: [t.sid] }), `Removed from Administrators: ${list.length === 1 ? `${list[0].name} on ${list[0].pc.computer}` : `${list.length} memberships`}.`));
    } else if (act === 'delete') {
      const list = deletable(rows);
      sure(`Delete ${list.length === 1 ? list[0].full : `${list.length} local accounts`}?`, `${and(list.slice(0, 6).map((t) => t.full))}${list.length > 6 ? ` and ${list.length - 6} more` : ''}. This cannot be undone. Profile folders and their files are kept.`, 'Delete',
        () => each(list, (t) => send(`/api/windows/${encodeURIComponent(t.pc.id)}/accounts/${encodeURIComponent(t.name)}/delete`, {}), `Deleted ${list.length === 1 ? list[0].full : `${list.length} local accounts`}.`));
    }
  }

  const drawn0 = H.drawn;
  H.drawn = () => {
    drawn0?.();
    const on = D.at() === 'admins';
    $('dv-admins').hidden = !(on && tab === 'members');
    $('dv-adminp').hidden = !(on && tab === 'people');
    if (on) {
      markRow();
      drawTicks();
    } else if (panel.is('admins')) panel.close();
  };
  // Members or People, picked under Computers down the left (console.js draws them there with the tab).
  $('dv-views').addEventListener('click', (e) => {
    const a = e.target.closest('[data-acc-tab]');
    if (!a) return;
    e.preventDefault();
    if (tab === a.dataset.accTab) return;
    tab = a.dataset.accTab;
    picked.clear(); // a member and a person aren't the same row
    if (panel.is('admins')) panel.close();
    D.draw();
  });

  // A row of Local admins in the panel: which computers it's an administrator of, and how.
  const markRow = () => document.querySelectorAll('#dv-admins [data-acc-row], #dv-adminp [data-acc-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('admins', el.dataset.accRow)));
  panel.el.addEventListener('close', markRow);
  const head = (kind, title, sub) => `<div class="dw-head">${pic(kind, 'dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>`
    + `${closeX}</div>`;
  // A computer it's an administrator of, with a take-out button when it was added there directly.
  const pcRow = (c, second, sid = null, name = '') => `<div class="mrow acc-row" data-acc-pc="${esc(c.id)}"><span class="pic small">${svg(c.server ? 'server' : 'monitor')}</span>`
    + `<div class="two"><b>${esc(c.computer)}</b><small>${esc(second)}</small></div>`
    + `${sid && has('windows.change') && !ready[c.id] ? x(`data-adm-out="${esc(c.id)}" data-sid="${esc(sid)}" data-name="${esc(name)}"`, `Remove ${name} from Administrators on ${c.computer}`) : svg('right', 'acc-go')}</div>`;
  const openRow = (key) => {
    if (panel.shows('admins', key)) {
      panel.close();
      return;
    }
    const html = rowHtml(key);
    if (!html) return;
    panel.body.innerHTML = html;
    panel.open('admins', key);
    markRow();
  };
  const rowHtml = (key) => {
    let html = '';
    if (tab === 'members') {
      const r = memberList().find((y) => y.key === key);
      if (!r) return '';
      const a = localOf(r);
      const g = r.kind === 'group' ? groupOf(r.sid) : null;
      html = head(r.kind, r.name, r.type)
        + (a ? `<div class="dw-status acc-status">${a.enabled ? chip('Enabled', 'ok') : chip('Disabled', 'off', 'power')}${a.neverExpires ? chip('Password never expires', 'muted') : ''}</div>`
          + `<section class="dw-sec"><div class="dw-sec-head"><h3>Account</h3></div><div class="dw-facts">${fact('Last sign-in', a.lastSignIn ? day(a.lastSignIn) : 'Never')}${fact('Description', a.description)}</div></section>` : '')
        + (r.kind === 'group' ? section(`Members <small>${g ? g.people.length : ''}</small>`, g ? (g.people.length ? `<div class="dw-list">${g.people.map((u) => `<div class="mrow">${pic('user')}`
          + `<div class="two"><b>${esc(u.name || u.account)}</b><small>${esc(`${prefix(r.name)}\\${u.account}`)}</small></div><span class="acc-chips">${u.enabled ? '' : chip('Disabled', 'off', 'power')}</span></div>`).join('')}</div>` : none('No members.'))
          : none(adm?.groups ? 'This group is not in this domain, so its members are not available.' : 'Active Directory cannot be read from this server, so the members are not available.')) : '')
        + section(`Administrator of <small>${r.on.length}</small>`, `<div class="dw-list">${r.on.map((c) => pcRow(c, r.kind === 'local' ? 'Local account' : 'Member of Administrators', r.sid, r.name)).join('')}</div>`);
    } else {
      const r = personList().find((y) => y.key === key);
      if (!r) return '';
      html = head(r.kind, r.shown, r.name)
        + (r.kind === 'user' && has('ad.view') ? `<div class="dw-bar"><a class="dw-qa" href="#ad/users">${svg('users')}<span>Open Active Directory</span></a></div>` : '')
        + section(`Administrator of <small>${r.on.length}</small>`, `<div class="dw-list">${r.on.map((o) => pcRow(o.c, r.unread ? 'Members not available' : how(o), o.direct ? sidOn(o.c, r.name) : null, r.name)).join('')}</div>`);
    }
    return html;
  };
  // The SID a name was added to a computer's Administrators with.
  const sidOn = (c, name) => (c.admins || []).find((m) => m.name.toUpperCase() === name.toUpperCase())?.sid || null;
  for (const id of ['dv-admins', 'dv-adminp']) {
    $(id).addEventListener('click', (e) => {
      const ticked = R.grid.tick(e, $(id), 'admTick', picked);
      if (ticked !== null) {
        if (ticked) drawTicks();
        return;
      }
      const row = e.target.closest('[data-acc-row]');
      if (row) openRow(row.dataset.accRow);
    });
  }
  // A computer there opens its own panel, on its Accounts tab; its x takes the account out of Administrators there.
  panel.el.addEventListener('click', (e) => {
    const out = e.target.closest('[data-adm-out]');
    if (out && panel.is('admins')) {
      e.stopPropagation();
      const key = panel.key;
      const { admOut: pcId, sid, name } = out.dataset;
      const pc = (adm?.computers || []).find((k) => k.id === pcId);
      sure(`Remove ${name} from Administrators?`, `${name} will no longer be an administrator of ${pc?.computer || 'that computer'}.`, 'Remove', async () => {
        R.busy(panel.body, `Contacting ${pc?.computer || 'the computer'}`);
        try {
          const answer = await send(`/api/windows/${encodeURIComponent(pcId)}/admins`, { remove: [sid] });
          toast(`${name} removed from Administrators on ${pc?.computer}.`, answer?.warning || '', answer?.warning ? { tone: 'warn' } : {});
        } catch (err) {
          oops(err);
        } finally {
          R.busy(panel.body, '');
        }
        delete live[pcId];
        admAt = 0;
        await fresh();
        D.draw();
        if (panel.shows('admins', key)) {
          const html = rowHtml(key);
          if (html) panel.body.innerHTML = html;
          else panel.close();
        }
      });
      return;
    }
    const b = e.target.closest('[data-acc-pc]');
    if (b && panel.is('admins')) {
      view = tab === 'people' ? 'people' : 'members';
      D.open(b.dataset.accPc, 'accounts');
    }
  });
})();
