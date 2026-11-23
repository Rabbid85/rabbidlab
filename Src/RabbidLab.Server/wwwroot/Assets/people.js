// Roles and people, in Settings (take A): #settings/people with four tabs, Roles (a grid by tier), /assignments (every
// assignment: who, where in Active Directory and until when), /people and /local. A role, an assignment or a person opens
// in the side panel; a role's permissions are switched on and off there under Edit. A role is changed or given only by
// someone in a higher tier (Roles.cs). It asks /api/roles when the page opens and after each change.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials, sure } = R;
  const SWATCHES = R.palette;
  const KIND = { group: 'Group', user: 'User', local: 'Local account', everyone: 'Everyone', windows: 'Built-in', all: 'Built-in' };
  const ICON = { group: 'users', user: 'user', local: 'key', everyone: 'users', windows: 'monitor', all: 'users' };
  const TABS = ['roles', 'assignments', 'people', 'local'];
  const TIER_SHORT = ['Administrator only. Every permission, every role.', 'Changes the roles below it and the people who have them.', 'Uses the lab. Changes no roles.'];
  const TIER_ABOUT = [
    'Only Administrator. It has every permission and can change and give every other role.',
    'People with a tier 1 role can change and give tier 2 roles, when their permissions allow it. They can reset passwords and MFA only for people whose other roles are all in tier 2.',
    'People whose roles are all in tier 2 cannot change or give roles. User, which everyone has, is in tier 2.',
  ];
  const DAY = 86400000;
  const EXPAND = 'rabbidlab.people.expand';
  let data = null; // what /api/roles said
  let tab = 'roles';
  let expand = (() => { try { return localStorage.getItem(EXPAND) === '1'; } catch { return false; } })();
  const picked = new Set(); // ticked assignments
  const pickedRoles = new Set(); // ticked roles
  const came = new Map(); // assignment id -> when it was given here, for the row's arrival
  const done = new WeakSet();

  const problem = (text) => say('ppl-problem', text);
  const role = (id) => data.roles.find((r) => r.id === id);
  const mine = (key) => data.me.can.includes(key);
  const allParts = () => data.catalog.flatMap((p) => p.actions.map((a) => a.key));
  const permsOf = (r) => new Set(r.id === 'admin' ? allParts() : r.permissions);
  const same = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();
  const lower = (s) => s.charAt(0).toLowerCase() + s.slice(1);
  const tierName = (t) => `Tier ${t}`;
  const day = (iso) => new Date(iso).toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' });
  const daysLeft = (iso) => (new Date(iso) - Date.now()) / DAY;
  const ended = (m) => !!m?.ends && new Date(m.ends) <= Date.now();
  const since = (iso) => {
    if (!iso) return 'Never';
    const m = Math.floor((Date.now() - new Date(iso)) / 60000);
    const days = Math.floor(m / 1440);
    return m < 2 ? 'Just now' : m < 60 ? `${m} min ago` : m < 120 ? 'An hour ago' : m < 1440 ? `${Math.floor(m / 60)} hours ago` : days === 1 ? 'Yesterday' : `${days} days ago`;
  };
  const current = (el, on) => (on ? el.setAttribute('aria-current', 'page') : el.removeAttribute('aria-current'));
  const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
  const sec = (title, body) => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3></div>${body}</section>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const strip = (pairs) => `<div class="dw-strip">${pairs.map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const badge = (text, tone) => `<span class="ubadge ${tone}">${esc(text)}</span>`;
  const muted = (text) => `<span class="muted">${esc(text)}</span>`;
  const ell = (text) => `<span class="ell">${esc(text)}</span>`;
  const dot = (r) => `<span class="pp-role" data-color="${esc(r.color)}"><i class="dot"></i><span class="ell">${esc(r.name)}</span></span>`;
  const findText = () => $('ppl-find').value.trim().toLowerCase();
  const hit = (...t) => !findText() || t.some((x) => String(x || '').toLowerCase().includes(findText()));

  // --- Every assignment ----------------------------------------------------------------------------------------------

  // A role does something in Active Directory, so where it works matters.
  const scoped = (r) => r.id !== 'admin' && r.permissions.some((p) => data.scoped.includes(p));
  const aid = (r, m) => `${r.id}|${m.kind}|${String(m.name).toLowerCase()}`;
  // Every assignment as a row: each role's members, then the ones nobody gives or takes away (Windows administrators,
  // everyone's User, a local account's User).
  const assignments = () => {
    const out = [];
    data.roles.forEach((r) => r.members.forEach((m) => out.push({ id: aid(r, m), r, m, kind: m.kind, name: m.name, display: m.display })));
    if (data.windowsAdmins) out.push({ id: 'admin|windows', r: role('admin'), m: null, kind: 'windows', name: 'Windows administrators', built: `Members of the Administrators group on ${data.machine}. They sign in with a local account, or with .\\ before the username` });
    out.push({ id: 'user|all', r: role('user'), m: null, kind: 'all', name: 'All signed-in users', built: 'Everyone who signs in has User' });
    data.local.filter((a) => !out.some((x) => x.kind === 'local' && same(x.name, a.username)))
      .forEach((a) => out.push({ id: `user|local|${a.username.toLowerCase()}`, r: role('user'), m: null, kind: 'local', name: a.username, built: 'Local accounts always have User' }));
    return out;
  };
  // With People from groups on: under each group, everyone in it who has signed in (nested groups included).
  const reachOf = (x) => {
    if (x.kind === 'group') return data.people.filter((p) => p.source === 'ad' && p.groups.some((g) => same(g, x.name)));
    if (x.kind === 'everyone') return data.people.filter((p) => p.source === 'ad');
    if (x.kind === 'windows') return data.people.filter((p) => p.source === 'windows');
    return null;
  };
  const withPeople = (list) => list.flatMap((x) => [x, ...(expand ? (reachOf(x) || []).map((p) => ({ id: `${x.id}>${p.username.toLowerCase()}`, r: x.r, m: x.m, via: x, p, kind: 'user', name: p.username, display: p.name })) : [])]);
  const whoOf = (x) => (x.kind === 'everyone' ? 'Everyone in Active Directory' : x.display || x.name);
  const typeOf = (x) => (x.via ? 'User' : x.kind === 'user' && x.p?.source === 'windows' ? 'Built-in' : KIND[x.kind]);
  const pic = (x, cls = '') => `<span class="pic ${x.kind}${cls}">${x.kind === 'user' ? esc(initials(x.display || x.name)) : svg(ICON[x.kind])}</span>`;
  const whereText = (x) => {
    if (x.r.id === 'admin') return 'Everything';
    if (!scoped(x.r)) return '';
    const s = x.m?.scope?.length ? x.m.scope : x.r.scope;
    return s?.length ? s.map((o) => o.path).join(', ') : 'Entire domain';
  };
  const endsCell = (m) => (!m?.ends ? muted('Never') : ended(m) ? badge(`Ended ${day(m.ends)}`, 'grey') : daysLeft(m.ends) <= 14 ? badge(day(m.ends), 'amber') : ell(day(m.ends)));
  // Why an assignment can't be changed or removed here, or ''.
  const whyNot = (x) => {
    if (x.via) return `Comes from ${whoOf(x.via)}. Remove the group's assignment instead.`;
    if (x.built) return `${x.built}.`;
    if (!mine('access.members')) return 'Your roles do not include giving roles.';
    if (!x.r.manage) return `Only someone in a higher tier than ${x.r.name} can change who has it.`;
    return '';
  };
  const lastAdmin = (x) => x.r.id === 'admin' && x.r.members.filter((m) => !ended(m)).length <= 1 && !ended(x.m);

  // A person or local account: every role they get, how (directly, from a group, or everyone), and the assignment behind it.
  const personOf = (kind, name) => {
    const p = data.people.find((q) => (kind === 'local' ? q.source === 'local' : q.source !== 'local') && same(q.username, name));
    const got = [];
    const add = (r, how, m, via) => { if (!got.some((g) => g.r === r && g.m === m)) got.push({ r, how, m, via }); };
    data.roles.forEach((r) => r.members.forEach((m) => {
      if (ended(m)) return;
      if (m.kind === kind && same(m.name, name)) add(r, 'Given directly', m, null);
      else if (kind === 'user' && p?.source === 'ad' && m.kind === 'group' && p.groups.some((g) => same(g, m.name))) add(r, `From ${m.name}`, m, m.name);
      else if (kind === 'user' && p?.source === 'ad' && m.kind === 'everyone') add(r, 'Everyone in Active Directory', m, 'everyone');
    }));
    if (p?.source === 'windows') add(role('admin'), 'Windows administrators', null, 'windows');
    if (!got.some((g) => g.r.id === 'user')) add(role('user'), kind === 'local' ? 'Local accounts always have User' : 'Everyone has User', null, 'all');
    got.sort((a, b) => data.roles.indexOf(a.r) - data.roles.indexOf(b.r));
    const display = p?.name || data.roles.flatMap((r) => r.members).find((m) => m.kind === kind && same(m.name, name))?.display || null;
    const all = got.some((g) => g.r.id === 'admin');
    const can = all ? new Set(allParts()) : new Set(got.flatMap((g) => g.r.permissions));
    return { kind, name, display, p, got, can, top: Math.min(...got.map((g) => g.r.tier)), source: kind === 'local' ? 'local' : p?.source || 'ad' };
  };
  // How many of the people who have signed in have a role, by any way; worked out once for each time the lists are drawn.
  let counted = new Map();
  const peopleIn = (r) => {
    if (r.id === 'user') return null;
    if (!counted.has(r.id)) counted.set(r.id, data.people.filter((p) => personOf(p.source === 'local' ? 'local' : 'user', p.username).got.some((g) => g.r === r)).length);
    return counted.get(r.id);
  };

  // --- The four lists --------------------------------------------------------------------------------------------------

  const ticks = (key, set, head) => ({ w: 18, head: `<label class="tk"><input type="checkbox" data-${key}-all aria-label="Select all"></label>`,
    cell: (x) => (head(x) ? `<label class="tk"><input type="checkbox" data-${key}-tick="${esc(x.id)}" aria-label="${esc(`Select ${x.name}`)}"${set.has(x.id) ? ' checked' : ''}></label>` : '') });
  const rolesGrid = R.grid({
    el: $('pp-roles-table'), key: 'roles', sort: { key: 'tier', dir: 1 }, group: { on: true, keys: ['tier'] },
    lead: ticks('pr', pickedRoles, (r) => !r.builtIn && r.manage && mine('access.roles')),
    cols: [
      { key: 'name', label: 'Role', w: 220, fixed: true, get: (r) => r.name, cell: (r) => `<div class="who"><span class="pic pp-rpic" data-color="${esc(r.color)}">${svg('shield')}</span><b class="ell">${esc(r.name)}</b>${r.manage ? '' : `<i class="pp-lock" data-tip="${esc(`Only someone in a higher tier than ${r.name} can change it`)}">${svg('lock')}</i>`}</div>` },
      { key: 'tier', label: 'Tier', w: 80, on: true, get: (r) => r.tier, text: (r) => tierName(r.tier), cell: (r) => ell(tierName(r.tier)),
        group: { key: (r) => r.tier, head: (rs) => `<span class="pp-th"><span class="pp-tn">${esc(tierName(rs[0].tier))}</span><span class="pp-tdesc">${esc(TIER_SHORT[rs[0].tier] || '')}</span></span>` } },
      { key: 'can', label: 'Can', w: 90, on: true, get: (r) => permsOf(r).size, text: (r) => `${permsOf(r).size} of ${allParts().length}`, cell: (r) => ell(`${permsOf(r).size} of ${allParts().length}`) },
      { key: 'cant', label: 'Can\'t', w: 70, on: true, get: (r) => allParts().length - permsOf(r).size },
      { key: 'assign', label: 'Assignments', w: 110, on: true, get: (r) => assignments().filter((x) => x.r === r).length },
      { key: 'people', label: 'People', w: 90, on: true, get: (r) => peopleIn(r) ?? 1e6, text: (r) => (r.id === 'user' ? 'Everyone' : String(peopleIn(r))), cell: (r) => ell(r.id === 'user' ? 'Everyone' : String(peopleIn(r))) },
      { key: 'about', label: 'Description', w: 300, on: true, get: (r) => r.about || '' },
    ],
    row: (r) => ` data-pp-rrow="${esc(r.id)}" tabindex="0"`, rowClass: (r) => `${pickedRoles.has(r.id) ? 'picked' : ''}${R.panel.shows('people', `r:${r.id}`) ? ' dw-on' : ''}`,
    empty: () => 'No roles match.', loading: () => !data, redraw: () => drawTab(),
  });
  const ledger = R.grid({
    el: $('ppl-table'), key: 'assignments', sort: { key: 'role', dir: 1 },
    lead: ticks('pp', picked, (x) => !whyNot(x)),
    cols: [
      { key: 'who', label: 'Person or group', w: 240, fixed: true, get: whoOf, cell: (x) => `<div class="who">${pic(x)}<b class="ell">${esc(whoOf(x))}</b></div>` },
      { key: 'type', label: 'Type', w: 110, on: true, get: typeOf },
      { key: 'role', label: 'Role', w: 170, on: true, get: (x) => data.roles.indexOf(x.r), text: (x) => x.r.name, cell: (x) => dot(x.r) },
      { key: 'tier', label: 'Tier', w: 70, on: true, get: (x) => x.r.tier, text: (x) => tierName(x.r.tier), cell: (x) => ell(tierName(x.r.tier)) },
      { key: 'where', label: 'Where', w: 210, on: true, get: whereText, cell: (x) => (whereText(x) ? ell(whereText(x)) : muted('-')) },
      { key: 'ends', label: 'Ends', w: 130, on: true, get: (x) => (x.m?.ends ? +new Date(x.m.ends) : 9e15), text: (x) => (x.m?.ends ? day(x.m.ends) : 'Never'), cell: (x) => endsCell(x.m) },
      { key: 'from', label: 'From group', w: 170, on: true, get: (x) => (x.via ? whoOf(x.via) : ''), cell: (x) => (x.via ? ell(whoOf(x.via)) : muted('-')) },
      { key: 'by', label: 'Added by', w: 140, on: true, get: (x) => (x.via ? '' : x.m?.addedBy || ''), cell: (x) => (x.via || !x.m?.addedBy ? muted('-') : ell(x.m.addedBy)) },
      { key: 'added', label: 'Added', w: 120, get: (x) => (x.m?.added && !x.via ? +new Date(x.m.added) : ''), text: (x) => (x.m?.added && !x.via ? day(x.m.added) : ''), cell: (x) => (x.m?.added && !x.via ? ell(day(x.m.added)) : muted('-')) },
      { key: 'reason', label: 'Reason', w: 260, get: (x) => (x.via ? '' : x.m?.reason || '') },
      { key: 'seen', label: 'Last sign-in', w: 130, get: (x) => (x.p?.lastSignIn ? +new Date(x.p.lastSignIn) : ''), text: (x) => (x.p ? since(x.p.lastSignIn) : ''), cell: (x) => (x.p ? ell(since(x.p.lastSignIn)) : muted('-')) },
    ],
    row: (x) => ` data-pp-row="${esc(x.id)}" tabindex="0"`, rowClass: (x) => `${x.via ? 'pp-via' : ''}${ended(x.m) ? ' off' : ''}${picked.has(x.id) ? ' picked' : ''}`,
    empty: () => (findText() ? 'No assignments match.' : 'No assignments.'), loading: () => !data, redraw: () => drawTab(),
  });
  const mfaCell = (x) => (x.p?.mfa ? badge('Set up', 'green') : badge('Not set up', 'amber'));
  const peopleGrid = R.grid({
    el: $('pp-people-table'), key: 'people', sort: { key: 'seen', dir: -1 },
    cols: [
      { key: 'name', label: 'Name', w: 220, fixed: true, get: (x) => x.display || x.name, cell: (x) => `<div class="who">${pic({ kind: 'user', name: x.name, display: x.display })}<b class="ell">${esc(x.display || x.name)}</b></div>` },
      { key: 'logon', label: 'Logon name', w: 150, on: true, get: (x) => x.name, cell: (x) => `<span class="mono ell">${esc(x.name)}</span>` },
      { key: 'roles', label: 'Roles', w: 240, on: true, get: (x) => [...new Set(x.got.map((g) => g.r.name))].join(', ') },
      { key: 'tier', label: 'Highest tier', w: 100, on: true, get: (x) => x.top, text: (x) => tierName(x.top), cell: (x) => ell(tierName(x.top)) },
      { key: 'can', label: 'Can', w: 90, on: true, get: (x) => x.can.size, text: (x) => `${x.can.size} of ${allParts().length}`, cell: (x) => ell(`${x.can.size} of ${allParts().length}`) },
      { key: 'mfa', label: 'MFA', w: 110, on: true, get: (x) => (x.p?.mfa ? 0 : 1), text: (x) => (x.p?.mfa ? 'Set up' : 'Not set up'), cell: mfaCell },
      { key: 'seen', label: 'Last sign-in', w: 130, on: true, get: (x) => (x.p?.lastSignIn ? +new Date(x.p.lastSignIn) : ''), text: (x) => since(x.p?.lastSignIn), cell: (x) => ell(since(x.p?.lastSignIn)) },
    ],
    row: (x) => ` data-pp-person="${esc(`user:${x.name}`)}" tabindex="0"`, rowClass: (x) => (R.panel.shows('people', `p:user:${x.name}`) ? 'dw-on' : ''),
    empty: () => (findText() ? 'Nobody matches.' : 'Nobody has signed in with Active Directory yet.'), loading: () => !data, redraw: () => drawTab(),
  });
  const localGrid = R.grid({
    el: $('pp-local-table'), key: 'locals', sort: { key: 'name', dir: 1 },
    cols: [
      { key: 'name', label: 'Username', w: 200, fixed: true, get: (x) => x.name, cell: (x) => `<div class="who">${pic({ kind: 'local', name: x.name })}<b class="ell">${esc(x.name)}</b></div>` },
      { key: 'roles', label: 'Roles', w: 240, on: true, get: (x) => [...new Set(x.got.map((g) => g.r.name))].join(', ') },
      { key: 'mfa', label: 'MFA', w: 110, on: true, get: (x) => (x.p?.mfa ? 0 : 1), text: (x) => (x.p?.mfa ? 'Set up' : 'Not set up'), cell: mfaCell },
      { key: 'created', label: 'Created', w: 120, on: true, get: (x) => (x.created ? +new Date(x.created) : ''), text: (x) => (x.created ? day(x.created) : ''), cell: (x) => (x.created ? ell(day(x.created)) : muted('-')) },
      { key: 'seen', label: 'Last sign-in', w: 130, on: true, get: (x) => (x.p?.lastSignIn ? +new Date(x.p.lastSignIn) : ''), text: (x) => since(x.p?.lastSignIn), cell: (x) => ell(since(x.p?.lastSignIn)) },
    ],
    row: (x) => ` data-pp-person="${esc(`local:${x.name}`)}" tabindex="0"`, rowClass: (x) => (R.panel.shows('people', `p:local:${x.name}`) ? 'dw-on' : ''),
    empty: () => (findText() ? 'Nothing matches.' : 'No local accounts.'), loading: () => !data, redraw: () => drawTab(),
  });

  let drawnRows = new Map(); // assignment row id -> the row, as last drawn
  const actButton = (id, n, what) => {
    const act = $(id);
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n;
    if (n) delete act.dataset.tip;
    else act.dataset.tip = `Select ${what} first`;
  };
  const drawTicks = () => {
    $('ppl-table').querySelectorAll('[data-pp-tick]').forEach((b) => { b.checked = picked.has(b.dataset.ppTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    $('pp-roles-table').querySelectorAll('[data-pr-tick]').forEach((b) => { b.checked = pickedRoles.has(b.dataset.prTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    actButton('ppl-act', picked.size, 'assignments');
    actButton('pp-roles-act', pickedRoles.size, 'roles');
  };
  const markRows = () => document.querySelectorAll('#set-people [data-pp-row], #set-people [data-pp-rrow], #set-people [data-pp-person]').forEach((el) => {
    const key = el.dataset.ppRrow ? `r:${el.dataset.ppRrow}` : el.dataset.ppPerson ? `p:${el.dataset.ppPerson}` : keyOf(drawnRows.get(el.dataset.ppRow) || {});
    el.classList.toggle('dw-on', R.panel.is('people') && R.panel.key === key);
  });
  R.panel.el.addEventListener('close', markRows);
  const peopleRows = () => data.people.filter((p) => p.source !== 'local').map((p) => ({ id: `user:${p.username}`, ...personOf('user', p.username) }))
    .filter((x) => hit(x.name, x.display, ...x.got.map((g) => g.r.name)));
  const localRows = () => data.local.map((a) => ({ id: `local:${a.username}`, created: a.created, ...personOf('local', a.username) })).filter((x) => hit(x.name, ...x.got.map((g) => g.r.name)));
  const drawTab = () => {
    if (!data) return;
    counted = new Map();
    const all = assignments();
    $('pp-n-roles').textContent = data.roles.length;
    $('pp-n-assignments').textContent = all.length;
    $('pp-n-people').textContent = data.people.filter((p) => p.source !== 'local').length;
    $('pp-n-local').textContent = data.local.length;
    if (tab === 'roles') {
      const list = data.roles.filter((r) => hit(r.name, r.about));
      [...pickedRoles].forEach((id) => { if (!list.some((r) => r.id === id)) pickedRoles.delete(id); });
      rolesGrid.draw(list);
      rolesGrid.tools($('pp-roles-tools'), $('pp-roles-rows'));
      R.paint($('pp-roles-table'));
    } else if (tab === 'assignments') {
      const list = withPeople(all).filter((x) => hit(whoOf(x), x.name, x.r.name, whereText(x), x.m?.addedBy, x.m?.reason, x.via ? whoOf(x.via) : ''));
      [...picked].forEach((id) => { if (!list.some((x) => x.id === id && !whyNot(x))) picked.delete(id); });
      const rows = ledger.draw(list);
      drawnRows = new Map(rows.map((x) => [x.id, x]));
      ledger.tools($('ppl-tools'), $('ppl-rows'));
      R.paint($('ppl-table'));
      came.forEach((t, id) => {
        if (Date.now() - t > 2800) { came.delete(id); return; }
        const row = $('ppl-table').querySelector(`[data-pp-row="${CSS.escape(id)}"]`);
        if (row && !done.has(row)) { done.add(row); R.arrive(row, t); }
      });
    } else if (tab === 'people') {
      peopleGrid.draw(peopleRows());
      peopleGrid.tools($('pp-people-tools'), $('pp-people-rows'));
    } else {
      localGrid.draw(localRows());
      localGrid.tools($('pp-local-tools'), $('pp-local-rows'));
    }
    drawTicks();
    markRows();
  };

  // --- A role in the side panel: Can and Can't (switched under Edit), its assignments, and its name and tier -----------

  const rp = { id: null, tab: 'can', edit: null, form: null };
  const editWhy = (r) => (r.id === 'admin' ? 'Administrator always has every permission.' : !r.manage ? `Only someone in a higher tier than ${r.name} can change it.` : !mine('access.roles') ? 'Your roles do not include changing roles.' : '');
  const tierWhy = (r, t) => {
    if (r.id === 'admin') return 'Administrator is always tier 0.';
    if (r.id === 'user') return 'User is always tier 2.';
    if (t === 0) return 'Administrator is the only role in tier 0.';
    if (t <= data.me.tier) return `You can put a role only in a lower tier than your own (tier ${data.me.tier}).`;
    return '';
  };
  const formOf = (r) => ({ name: r.name, about: r.about || '', color: r.color, tier: r.tier });
  const formDirty = (r) => !!rp.form && (rp.form.name !== r.name || rp.form.about !== (r.about || '') || rp.form.color !== r.color || rp.form.tier !== r.tier);
  const editChanges = (r) => (rp.edit ? allParts().filter((k) => rp.edit.has(k) !== permsOf(r).has(k)).length : 0);
  const canTab = (r) => {
    const perms = permsOf(r);
    const edit = rp.edit;
    const has = (k) => (edit ? edit.has(k) : perms.has(k));
    const total = allParts().length;
    const changes = editChanges(r);
    // While editing, each line stays in the list it started in; a tag says what will change.
    const list = (side) => data.catalog.map((p) => {
      const acts = p.actions.filter((a) => (side === 'can' ? perms.has(a.key) : !perms.has(a.key)));
      if (!acts.length) return '';
      const n = p.actions.filter((a) => perms.has(a.key)).length;
      return `<div class="pp-area"><p class="pp-ah">${svg(p.icon)}<b>${esc(p.name)}</b><span>${n} of ${p.actions.length}</span></p>`
        + (p.key === 'ad' && side === 'can' && r.id !== 'admin' ? `<p class="pp-where">${svg('tree')}<span>Where these work is set on each assignment.</span></p>` : '')
        + `<ul>${acts.map((a) => {
          const now = has(a.key);
          const tag = edit && now !== perms.has(a.key) ? badge(now ? 'Will be added' : 'Will be removed', now ? 'green' : 'red') : '';
          const lock = edit && !mine(a.key);
          return `<li class="pp-li${edit ? ' editing' : ''}"><span class="pp-mk ${side === 'can' ? 'yes' : 'no'}">${svg(side === 'can' ? 'check' : 'x')}</span>`
            + `<span class="pp-two"><b>${esc(a.name)}</b><small>${esc(a.about)}${lock ? ' You cannot grant a permission your roles do not include.' : ''}</small></span>${tag}`
            + (edit ? `<input type="checkbox" class="switch" role="switch" data-pp-perm="${esc(a.key)}" aria-label="${esc(a.name)}"${now ? ' checked' : ''}${lock ? ' disabled' : ''}>` : '') + '</li>';
        }).join('')}</ul></div>`;
    }).join('');
    return (edit ? `<div class="pp-editbar">${svg('edit')}<span>${changes ? `You have ${plural(changes, 'unsaved change', 'unsaved changes')}.` : 'Turn permissions on or off, then save.'}</span>`
      + `<button type="button" class="link" data-pp-ecancel>Cancel</button><button type="button" class="primary small" data-pp-esave${changes ? '' : ' disabled'}>Save changes</button></div>` : '')
      + `<p class="dw-how pp-lede">${esc(r.id === 'admin' ? 'Administrator can do everything. It cannot be changed.' : `${r.name} can do ${perms.size} of the ${total} things.`)}</p>`
      + sec(`${svg('check')}Can <small>${perms.size}</small>`, list('can') || '<p class="muted dw-empty">Nothing yet.</p>')
      + sec(`${svg('x')}Can't <small>${total - perms.size}</small>`, list('cant') || `<p class="muted dw-empty">Nothing. ${esc(r.name)} has every permission.</p>`);
  };
  const whoTab = (r, as) => {
    const give = r.manage && mine('access.members');
    return `<div class="pp-ptools"><span class="grow"></span>${give ? `<button type="button" class="small primary" data-pp-rgive>${svg('user-plus')}Give this role</button>` : ''}</div>`
      + (as.length ? `<div class="dw-list">${as.map((x) => `<div class="mrow pp-mrow" data-pp-assign="${esc(x.id)}" tabindex="0">${pic(x)}<span class="pp-two"><b>${esc(whoOf(x))}</b><small>${esc(typeOf(x))}${x.m?.reason ? ` · ${esc(x.m.reason)}` : ''}</small></span>`
        + `<span class="pp-two pp-end">${scoped(r) ? ell(whereText(x)) : ''}<small>${x.m?.ends ? (ended(x.m) ? `Ended ${esc(day(x.m.ends))}` : `Until ${esc(day(x.m.ends))}`) : 'No end date'}</small></span></div>`).join('')}</div>`
        : '<p class="muted dw-empty">Nobody has this role.</p>');
  };
  const nameTab = (r) => {
    rp.form ||= formOf(r);
    const f = rp.form;
    const can = !editWhy(r);
    return `<div class="form pp-rform"><label for="pp-rname">Role name</label><input id="pp-rname" maxlength="40" autocomplete="off" value="${esc(f.name)}"${can && r.id !== 'user' ? '' : ' disabled'}>`
      + `<label for="pp-rabout">Description</label><input id="pp-rabout" maxlength="160" autocomplete="off" value="${esc(f.about)}"${can ? '' : ' disabled'}>`
      + `<p class="form-label">Tier</p><div class="seg pp-tier" role="radiogroup" aria-label="Tier">${[0, 1, 2].map((t) => {
        const no = can ? tierWhy(r, t) : editWhy(r);
        return `<button type="button" role="radio" data-pp-tier="${t}" aria-checked="${f.tier === t}" aria-pressed="${f.tier === t}"${no ? ` disabled data-tip="${esc(no)}"` : ''}>${esc(tierName(t))}</button>`;
      }).join('')}</div><p class="hint pp-tier-about">${esc(TIER_ABOUT[f.tier])}</p>`
      + `<p class="form-label">Colour</p><div class="swatches" role="radiogroup" aria-label="Colour">${SWATCHES.map((c) => `<button type="button" role="radio" data-pp-swatch="${c}" data-color="${c}" aria-label="${c}" aria-checked="${same(c, f.color)}"${can ? '' : ' disabled'}></button>`).join('')}</div></div>`
      + (can ? `<div class="pp-rsave"><button type="button" class="link" data-pp-freset${formDirty(r) ? '' : ' hidden'}>Reset</button><button type="button" class="primary small" data-pp-fsave${formDirty(r) ? '' : ' disabled'}>Save changes</button></div>` : '')
      + sec('Sign-in', '<p class="dw-how">MFA is required for every role. Everyone signs in with a password and a code from an authenticator app.</p>')
      + (!r.builtIn && can ? sec('Delete', `<p class="dw-how">Its assignments are removed. Permissions from other roles still apply.</p><button type="button" class="danger" data-pp-rdelete>${svg('trash')}Delete ${esc(r.name)}</button>`) : '');
  };
  const openRole = (r, again = false, tabName = null) => {
    if (!r) { R.panel.close(); return; }
    if (rp.id !== r.id) Object.assign(rp, { id: r.id, tab: 'can', edit: null, form: null });
    if (tabName) rp.tab = tabName;
    panelOn = { role: r.id };
    const total = allParts().length;
    const can = permsOf(r).size;
    const as = assignments().filter((x) => x.r === r);
    const why = editWhy(r);
    const y = R.panel.body.scrollTop;
    R.panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic pp-rpic" data-color="${esc(r.color)}">${svg('shield')}</span><div class="dw-name"><h2 id="dw-title">${esc(r.name)}</h2>`
      + `<p>${esc(r.about || `${tierName(r.tier)}. ${TIER_SHORT[r.tier]}`)}</p></div><button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${rp.edit ? '' : R.editButton('data-pp-redit', why)}${r.manage && mine('access.members') ? qa('data-pp-rgive', 'user-plus', 'Give this role') : ''}`
      + `${r.id === 'admin' ? '' : qa('data-pp-rpreview', 'eye', 'Preview as')}</div>`
      + strip([['Tier', esc(tierName(r.tier))], ['Can', `${can} of ${total}`], ['Can\'t', String(total - can)], ['Assignments', String(as.length)], ['People', r.id === 'user' ? 'Everyone' : String(peopleIn(r))]])
      + `<div class="tabs2 dw-tabs" role="tablist">${[['can', 'Can and Can\'t'], ['who', 'Assignments', as.length], ['name', 'Name and tier']].map(([k, l, n]) => `<a role="tab" data-pp-rtab="${k}"${rp.tab === k ? ' aria-current="true"' : ''}>${esc(l)}${n == null ? '' : `<small>${n}</small>`}</a>`).join('')}</div>`
      + `<div class="dw-panel">${rp.tab === 'who' ? whoTab(r, as) : rp.tab === 'name' ? nameTab(r) : canTab(r)}</div>`;
    R.paint(R.panel.body);
    if (!again) R.panel.open('people', `r:${r.id}`);
    else R.panel.body.scrollTop = y;
    markRows();
  };
  const saveRole = async (r, body, said) => {
    try {
      await R.send(`/api/roles/${encodeURIComponent(r.id)}`, { name: r.name, about: r.about || '', color: r.color, permissions: [...permsOf(r)].filter(() => r.id !== 'admin'), ...body });
      R.toast('Saved.', said);
      return true;
    } catch (err) {
      R.toast('Could not save the role.', err.message, { tone: 'warn' });
      return false;
    }
  };

  // --- An assignment, or a person, in the side panel ---------------------------------------------------------------

  const keyOf = (x) => (x.via || (x.kind === 'local' && x.built) ? `p:${x.kind}:${x.name}` : `a:${x.id}`);
  const head = (picHtml, title, sub) => `<div class="dw-head">${picHtml}<div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>`
    + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`;
  const gives = (r, where) => {
    if (r.id === 'admin') return '<ul class="does"><li><b>Everything</b><span>The Administrator role has every permission.</span></li></ul>';
    return `<ul class="does">${data.catalog.map((p) => {
      const on = p.actions.filter((a) => r.permissions.includes(a.key));
      return on.length ? `<li><b>${esc(p.name)}</b><span>${esc(on.map((a) => lower(a.name)).join(', '))}${p.key === 'ad' && where ? `. Where: ${esc(where)}` : ''}</span></li>` : '';
    }).join('') || '<li><span>No permissions.</span></li>'}</ul>`;
  };
  let panelOn = null; // what the side panel shows: { role }, { a } or { p }
  const openAssign = (x, again = false) => {
    const r = x.r;
    const m = x.m;
    const why = whyNot(x);
    const reach = reachOf(x);
    const person = ['user', 'local'].includes(x.kind);
    panelOn = { a: x.id };
    R.panel.body.innerHTML = head(pic(x, ' dw-pic'), whoOf(x), `${r.name} · ${tierName(r.tier)}`)
      + `<div class="dw-bar">${R.editButton('data-pp-edit', why)}${qa('data-pp-open-role', 'shield', `Open ${r.name}`)}`
      + (person ? qa('data-pp-open-person', 'user', `Open ${whoOf(x)}`) : '')
      + (why || lastAdmin(x) ? '' : qa('data-pp-remove', 'user-minus', 'Remove assignment')) + '</div>'
      + strip([['Type', esc(typeOf(x))], ['Role', esc(r.name)], ['Tier', esc(tierName(r.tier))], ['Where', esc(whereText(x) || 'Not used')], ['Ends', m?.ends ? (ended(m) ? badge(`Ended ${day(m.ends)}`, 'grey') : esc(day(m.ends))) : 'Never'],
        ['Added by', esc(m?.addedBy || 'Built-in')], ...(m?.added ? [['Added', esc(day(m.added))]] : [])])
      + (m?.reason ? sec('Reason', `<p class="dw-how">${esc(m.reason)}</p>`) : '')
      + (x.built ? sec('Built-in', `<p class="dw-how">${esc(x.built)}. It cannot be removed.</p>`) : '')
      + (ended(m) ? sec('Ended', `<p class="dw-how">This assignment ended on ${esc(day(m.ends))} and no longer gives ${esc(r.name)}. Edit it to set a new end date, or remove it.</p>`) : '')
      + (scoped(r) ? sec('Where', `<p class="dw-how">${esc(m?.scope?.length ? `Its Active Directory tasks work only on what is in ${m.scope.map((o) => o.path).join(', ')}, child OUs included.` : 'Its Active Directory tasks work on every user, group and computer the service account can reach.')}</p>`) : '')
      + sec(`What it gives <small>${r.id === 'admin' ? 'All' : r.permissions.length}</small>`, gives(r, whereText(x)))
      + (reach ? sec(`Who it reaches <small>${reach.length}</small>`, reach.length ? `<div class="dw-list">${reach.map((p) => `<div class="mrow pp-mrow" data-pp-person="user:${esc(p.username)}" tabindex="0">${pic({ kind: 'user', name: p.username, display: p.name })}`
        + `<span class="pp-two"><b>${esc(p.name || p.username)}</b><small>${esc(p.username)}</small></span><span class="muted">${esc(since(p.lastSignIn))}</span></div>`).join('')}</div>`
        + `<p class="dw-how">${esc(x.kind === 'group' ? `Members of ${whoOf(x)}, including members of nested groups, get ${r.name} when they sign in. People who have not signed in yet are not listed.` : 'People who have signed in.')}</p>`
        : '<p class="muted dw-empty">Nobody it reaches has signed in yet.</p>') : '');
    R.paint(R.panel.body);
    if (!again) R.panel.open('people', keyOf(x));
    markRows();
  };
  const openPerson = (kind, name, again = false) => {
    const x = personOf(kind, name);
    panelOn = { p: `${kind}:${name}` };
    const local = kind === 'local';
    const does = x.got.some((g) => g.r.id === 'admin') ? '<li><b>Everything</b><span>The Administrator role has every permission.</span></li>'
      : data.catalog.map((p) => {
        const on = p.actions.filter((a) => x.can.has(a.key));
        return on.length ? `<li><b>${esc(p.name)}</b><span>${esc(on.map((a) => lower(a.name)).join(', '))}</span></li>` : '';
      }).join('') || '<li><span>No permissions.</span></li>';
    const giveable = mine('access.members') && (local || x.source === 'ad');
    R.panel.body.innerHTML = head(pic({ kind: local ? 'local' : 'user', name, display: x.display }, ' dw-pic'), x.display || name,
      local ? 'Local account' : x.source === 'windows' ? `Windows administrator · ${name.split('\\')[0]}` : `${name}${x.p?.domain ? ` · ${x.p.domain}` : ''}`)
      + '<div class="dw-bar">'
      + (giveable ? qa('data-pp-give-to', 'user-plus', 'Give a role') : '')
      + (local && mine('access.local') ? qa('data-password', 'key', 'New password') + qa('data-remove-account', 'trash', 'Delete account') : '')
      + (x.p?.mfa && mine('access.mfa') ? qa('data-reset-mfa', 'refresh', 'Reset MFA') : '') + '</div>'
      + strip([['Signed in with', local ? 'Local account' : x.source === 'windows' ? 'Windows' : 'Active Directory'], ['Highest tier', esc(tierName(x.top))], ['Roles', String(x.got.length)],
        ['Can', `${x.can.size} of ${allParts().length}`], ['MFA', x.p?.mfa ? badge('Set up', 'green') : badge('Not set up', 'amber')], ['Last sign-in', esc(since(x.p?.lastSignIn))]])
      + sec(`Roles <small>${x.got.length}</small>`, `<div class="dw-list">${x.got.map((g) => `<div class="mrow pp-mrow"${g.m ? ` data-pp-assign="${esc(aid(g.r, g.m))}" tabindex="0"` : ''}>`
        + `<span class="pp-two">${dot(g.r)}<small>${esc(`${tierName(g.r.tier)} · ${g.how}`)}</small></span>`
        + `<span class="pp-two pp-end">${scoped(g.r) ? ell(whereText({ r: g.r, m: g.m })) : ''}<small>${g.m?.ends ? `Until ${esc(day(g.m.ends))}` : 'No end date'}</small></span></div>`).join('')}</div>`)
      + sec('MFA', `<p class="dw-how">${esc(x.p?.mfa ? `Set up since ${R.when(x.p.mfa)}.` : 'Not set up. Setup is required at the next sign-in.')}</p>`)
      + sec('Effective permissions', `<ul class="does">${does}</ul>`);
    R.paint(R.panel.body);
    if (!again) R.panel.open('people', `p:${kind}:${name}`);
    markRows();
  };
  const openRow = (x) => {
    if (R.panel.is('people') && R.panel.key === keyOf(x)) { R.panel.close(); return; }
    if (x.via) openPerson('user', x.name);
    else if (x.kind === 'local' && x.built) openPerson('local', x.name);
    else openAssign(x);
  };
  const assignOf = (id) => assignments().find((x) => x.id === id);
  // The side panel follows the lists: drawn again as it is now, or closed when what it shows has gone.
  const panelAgain = () => {
    if (!R.panel.is('people') || !panelOn) return;
    if (panelOn.role) {
      const r = role(panelOn.role);
      if (r) openRole(r, true);
      else R.panel.close();
    } else if (panelOn.a) {
      const x = assignOf(panelOn.a);
      if (x) openAssign(x, true);
      else R.panel.close();
    } else {
      const [kind, ...rest] = panelOn.p.split(':');
      openPerson(kind, rest.join(':'), true);
    }
  };

  // --- Removing assignments and roles ----------------------------------------------------------------------------------

  const TAKE_OUT = 'Permissions given only by these assignments are removed. Other roles are not affected.';
  const remove = (xs) => {
    const list = xs.filter((x) => !whyNot(x));
    if (!list.length) return;
    const one = list.length === 1 ? list[0] : null;
    sure(one ? `Remove ${whoOf(one)} from ${one.r.name}?` : `Remove ${list.length} assignments?`, TAKE_OUT, 'Remove', async () => {
      let n = 0;
      try {
        for (const x of list) {
          await R.send(`/api/roles/${encodeURIComponent(x.r.id)}/members/remove`, { kind: x.kind, name: x.name });
          n += 1;
          picked.delete(x.id);
        }
        R.toast('Removed.', one ? `${whoOf(one)} no longer has ${one.r.name}.` : `${n} assignments removed.`);
      } catch (err) {
        R.toast(n ? `Removed ${n} of ${list.length}.` : 'Could not remove the assignment.', err.message, { tone: 'warn' });
      }
      refresh();
    });
  };
  const deleteRoles = (rs) => {
    const list = rs.filter((r) => !r.builtIn && r.manage);
    if (!list.length) return;
    sure(list.length === 1 ? `Delete ${list[0].name}?` : `Delete ${list.length} roles?`, 'Their assignments are removed, and the people who had them lose what only these roles give.', 'Delete', async () => {
      try {
        for (const r of list) {
          await R.send(`/api/roles/${encodeURIComponent(r.id)}/delete`);
          pickedRoles.delete(r.id);
        }
        if (R.panel.is('people') && panelOn?.role && list.some((r) => r.id === panelOn.role)) R.panel.close();
        R.toast(list.length === 1 ? 'Role deleted.' : 'Roles deleted.', list.map((r) => r.name).join(', '));
      } catch (err) {
        R.toast('Could not delete the role.', err.message, { tone: 'warn' });
      }
      refresh();
    });
  };
  const copyRole = async (r) => {
    let n = 1;
    const base = `${r.name} copy`;
    while (data.roles.some((x) => same(x.name, n === 1 ? base : `${base} ${n}`))) n++;
    try {
      const made = await R.send('/api/roles', { name: (n === 1 ? base : `${base} ${n}`).slice(0, 40), color: r.color, copy: r.id });
      await refresh();
      openRole(role(made.id), false, 'name');
    } catch (err) {
      R.toast('Could not copy the role.', err.message, { tone: 'warn' });
    }
  };
  $('ppl-act').addEventListener('click', () => {
    const xs = [...picked].map(assignOf).filter(Boolean);
    R.actions.open($('ppl-act'), {
      items: [{ act: 'edit', label: 'Change where and when it ends…', disabled: xs.length !== 1 }, { act: 'clear', label: 'Clear selection' }, '-',
        { act: 'remove', label: 'Remove assignments…', hint: String(xs.length), danger: true }],
      pick: (act) => {
        if (act === 'edit' && xs.length === 1) openGive({ edit: xs[0] });
        else if (act === 'clear') { picked.clear(); drawTab(); }
        else if (act === 'remove') remove(xs);
      },
    });
  });
  $('pp-roles-act').addEventListener('click', () => {
    const rs = [...pickedRoles].map(role).filter(Boolean);
    R.actions.open($('pp-roles-act'), {
      items: [{ act: 'copy', label: 'Copy role', disabled: rs.length !== 1 }, { act: 'clear', label: 'Clear selection' }, '-', { act: 'delete', label: 'Delete roles…', hint: String(rs.length), danger: true }],
      pick: (act) => {
        if (act === 'copy' && rs.length === 1) copyRole(rs[0]);
        else if (act === 'clear') { pickedRoles.clear(); drawTab(); }
        else if (act === 'delete') deleteRoles(rs);
      },
    });
  });
  // Ticks and rows in each list.
  document.getElementById('set-people').addEventListener('change', (e) => {
    const t = e.target.closest('[data-pp-tick], [data-pr-tick]');
    if (t) {
      const [set, id] = t.dataset.ppTick != null ? [picked, t.dataset.ppTick] : [pickedRoles, t.dataset.prTick];
      if (t.checked) set.add(id);
      else set.delete(id);
      drawTicks();
      return;
    }
    if (e.target.closest('[data-pp-all]')) {
      [...drawnRows.values()].filter((x) => !whyNot(x)).forEach((x) => (e.target.checked ? picked.add(x.id) : picked.delete(x.id)));
      drawTicks();
    } else if (e.target.closest('[data-pr-all]')) {
      data.roles.filter((r) => !r.builtIn && r.manage && mine('access.roles') && hit(r.name, r.about)).forEach((r) => (e.target.checked ? pickedRoles.add(r.id) : pickedRoles.delete(r.id)));
      drawTicks();
    }
  });
  const rowOpen = (el) => {
    if (el.dataset.ppRow) { const x = drawnRows.get(el.dataset.ppRow); if (x) openRow(x); return; }
    if (el.dataset.ppRrow) {
      if (R.panel.shows('people', `r:${el.dataset.ppRrow}`)) R.panel.close();
      else openRole(role(el.dataset.ppRrow));
      return;
    }
    if (el.dataset.ppPerson) {
      if (R.panel.shows('people', `p:${el.dataset.ppPerson}`)) { R.panel.close(); return; }
      const [kind, ...n] = el.dataset.ppPerson.split(':');
      openPerson(kind, n.join(':'));
    }
  };
  document.getElementById('set-people').addEventListener('click', (e) => {
    if (e.target.closest('.tk, button, a, input')) return;
    const el = e.target.closest('[data-pp-row], [data-pp-rrow], [data-pp-person]');
    if (el) rowOpen(el);
  });
  document.getElementById('set-people').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-pp-row], [data-pp-rrow], [data-pp-person]')) rowOpen(e.target);
  });
  $('ppl-expand').checked = expand;
  $('ppl-expand').addEventListener('change', (e) => {
    expand = e.target.checked;
    try { localStorage.setItem(EXPAND, expand ? '1' : '0'); } catch { /* not kept, then */ }
    drawTab();
  });
  $('ppl-find').addEventListener('input', () => drawTab());

  // Clicks in the side panel.
  R.panel.el.addEventListener('click', async (e) => {
    if (!R.panel.is('people') || !panelOn) return;
    const on = (attr) => e.target.closest(`[${attr}]`);
    // A role.
    if (panelOn.role) {
      const r = role(panelOn.role);
      if (!r) return;
      if (on('data-pp-rtab')) { rp.tab = on('data-pp-rtab').dataset.ppRtab; openRole(r, true); return; }
      if (on('data-pp-redit') && !editWhy(r)) { rp.edit = new Set(permsOf(r)); rp.tab = 'can'; openRole(r, true); return; }
      if (on('data-pp-ecancel')) { rp.edit = null; openRole(r, true); return; }
      if (on('data-pp-esave') && rp.edit) {
        const n = editChanges(r);
        if (await saveRole(r, { permissions: [...rp.edit] }, `${r.name}: ${plural(n, 'permission', 'permissions')} changed.`)) { rp.edit = null; await refresh(); }
        return;
      }
      if (on('data-pp-rgive')) { openGive({ role: r.id }); return; }
      if (on('data-pp-rpreview')) {
        R.viewAs({ name: r.name }, [...new Set([...role('user').permissions, ...r.permissions])]);
        location.hash = '#overview';
        return;
      }
      const tier = on('data-pp-tier');
      if (tier && !tier.disabled) { rp.form.tier = Number(tier.dataset.ppTier); openRole(r, true); return; }
      const sw = on('data-pp-swatch');
      if (sw && !sw.disabled) { rp.form.color = sw.dataset.ppSwatch; openRole(r, true); return; }
      if (on('data-pp-freset')) { rp.form = null; openRole(r, true); return; }
      if (on('data-pp-fsave') && rp.form) {
        const f = rp.form;
        if (await saveRole(r, { name: f.name, about: f.about, color: f.color, ...(f.tier !== r.tier ? { tier: f.tier } : {}) }, `${f.name} is saved.`)) { rp.form = null; await refresh(); }
        return;
      }
      if (on('data-pp-rdelete')) { deleteRoles([r]); return; }
    }
    const x = panelOn.a ? assignOf(panelOn.a) : null;
    const [pk, ...pn] = panelOn.p ? panelOn.p.split(':') : [];
    const pname = pn.join(':');
    if (on('data-pp-edit') && x && !whyNot(x)) openGive({ edit: x });
    else if (on('data-pp-remove') && x) remove([x]);
    else if (on('data-pp-open-role') && x) openRole(x.r, false, 'can');
    else if (on('data-pp-open-person') && x) openPerson(x.kind, x.name);
    else if (on('data-pp-person')) { const [k, ...n] = on('data-pp-person').dataset.ppPerson.split(':'); openPerson(k, n.join(':')); }
    else if (on('data-pp-assign')) { const a = assignOf(on('data-pp-assign').dataset.ppAssign); if (a) openAssign(a); }
    else if (on('data-pp-give-to') && pk) {
      const who = personOf(pk, pname);
      openGive({ who: { kind: pk, name: pname, display: who.display } });
    } else if (on('data-reset-mfa') && pk) {
      const who = personOf(pk, pname);
      sure(`Reset MFA for ${who.display || pname}?`, 'Their current authenticator app stops working. You receive a one-time code to give them, which they enter with their password to set up MFA on a new device.', 'Reset MFA', async () => {
        try {
          const given = await R.send('/api/mfa/reset', { source: who.source, username: pname });
          problem('');
          refresh();
          // Shown once: the server keeps only its hash.
          sure(`One-time code for ${who.display || pname}`, `They enter this code after their password at the next sign-in to set up MFA again. The code works once, expires in ${given.hours} hours and is not shown again.`,
            'Done', null, { code: given.code, safe: true, info: true });
        } catch (err) {
          R.toast('Could not reset MFA.', err.message, { tone: 'warn' });
          refresh();
        }
      });
    } else if (on('data-password') && pk === 'local') {
      $('password-lead').textContent = `For the local account ${pname}. All of its sessions are signed out.`;
      $('pw-new').value = '';
      $('pw-problem').hidden = true;
      $('password-dialog').dataset.user = pname;
      $('password-dialog').showModal();
    } else if (on('data-remove-account') && pk === 'local') {
      sure(`Delete the local account ${pname}?`, 'All of its sessions are signed out immediately, and its assignments are removed.', 'Delete account', async () => {
        try {
          await R.send(`/api/accounts/${encodeURIComponent(pname)}/remove`);
          R.panel.close();
          R.toast('Account deleted.', `${pname} can no longer sign in.`);
        } catch (err) {
          R.toast('Could not delete the account.', err.message, { tone: 'warn' });
        }
        refresh();
      });
    }
  });
  R.panel.el.addEventListener('change', (e) => {
    if (!R.panel.is('people') || !panelOn?.role || !rp.edit) return;
    const s = e.target.closest('[data-pp-perm]');
    if (!s) return;
    if (s.checked) rp.edit.add(s.dataset.ppPerm);
    else rp.edit.delete(s.dataset.ppPerm);
    openRole(role(panelOn.role), true);
  });
  R.panel.el.addEventListener('input', (e) => {
    if (!R.panel.is('people') || !panelOn?.role || !rp.form) return;
    if (e.target.id === 'pp-rname') rp.form.name = e.target.value;
    else if (e.target.id === 'pp-rabout') rp.form.about = e.target.value;
    else return;
    const r = role(panelOn.role);
    const dirty = formDirty(r);
    R.panel.body.querySelector('[data-pp-fsave]')?.toggleAttribute('disabled', !dirty);
    R.panel.body.querySelector('[data-pp-freset]')?.toggleAttribute('hidden', !dirty);
  });
  R.panel.el.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' || !R.panel.is('people')) return;
    if (e.target.matches('[data-pp-person], [data-pp-assign]')) e.target.click();
  });
  $('pw-cancel').addEventListener('click', () => $('password-dialog').close());
  $('pw-save').addEventListener('click', async () => {
    try {
      await R.send(`/api/accounts/${encodeURIComponent($('password-dialog').dataset.user)}/password`, { password: $('pw-new').value });
      $('password-dialog').close();
      R.toast('Password changed.', 'All of its sessions are signed out.');
    } catch (err) {
      say('pw-problem', err.message);
    }
  });

  // --- Give a role, or change an assignment ------------------------------------------------------------------------

  // The OUs to pick from, once asked for, with the problem when they can't be listed.
  let ous = null;
  const askOus = async () => {
    if (ous) return;
    ous = { list: [], loading: true };
    try {
      ous = { list: mine('ad.view') ? await R.get('/api/ad/places') : [] };
    } catch (err) {
      ous = { list: [], problem: err.message };
    }
    if ($('give-dialog').open) drawGive();
  };
  const pathOf = (dn) => data.roles.flatMap((r) => [...r.scope, ...r.members.flatMap((m) => m.scope || [])]).find((s) => same(s.dn, dn))?.path
    || ous?.list?.find((p) => same(p.dn, dn))?.path
    || dn.split(/(?<!\\),/).filter((p) => !/^DC=/i.test(p)).map((p) => p.slice(p.indexOf('=') + 1)).reverse().join(' / ') || dn;
  const under = (dn, top) => same(dn, top) || dn.toLowerCase().endsWith(`,${top.toLowerCase()}`);
  const tidy = (dns) => dns.filter((dn, i) => !dns.some((o, k) => k !== i && under(dn, o) && !same(dn, o)) && dns.findIndex((o) => same(o, dn)) === i);
  const isoDay = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  let gv = null; // { edit, who, role, w, ous, e, date }
  const openGive = ({ who = null, role: rid = '', edit = null } = {}) => {
    if (!mine('access.members')) return;
    const m = edit?.m;
    gv = edit
      ? { edit, who: { kind: edit.kind, name: edit.name, display: edit.display }, role: edit.r.id, w: m.scope?.length ? 'ous' : 'all', ous: (m.scope || []).map((s) => s.dn),
        e: m.ends ? 'date' : 'never', date: isoDay(m.ends && !ended(m) ? new Date(m.ends) : new Date(Date.now() + 30 * DAY)) }
      : { edit: null, who, role: rid, w: 'all', ous: [], e: 'never', date: isoDay(new Date(Date.now() + 30 * DAY)) };
    $('give-title').textContent = edit ? 'Change the assignment' : 'Give a role';
    $('give-lead').textContent = edit ? `${whoOf(edit)} has ${edit.r.name}. Change where it works, when it ends and why.`
      : 'Choose who gets the role, which role, where in Active Directory it works, and when it ends.';
    $('gv-go').textContent = edit ? 'Save' : 'Give role';
    $('gv-who-row').hidden = !!edit;
    $('gv-role-row').hidden = !!edit;
    $('gv-reason').value = m?.reason || '';
    $('gv-find').value = '';
    $('gv-drop').hidden = true;
    $('gv-problem').hidden = true;
    drawGive();
    $('give-dialog').showModal();
    if (!edit && !who) $('gv-find').focus();
    if (mine('ad.view')) askOus();
  };
  const hasDirect = (r, who) => r.members.some((m) => m.kind === who.kind && same(m.name, who.name));
  const whyRole = (r) => {
    const who = gv.who;
    if (!r.manage) return `Only someone in a higher tier than ${r.name} can give it.`;
    if (who && hasDirect(r, who)) return `${whoOf(who)} already has ${r.name}.`;
    if (who?.kind === 'local' && r.id === 'user') return 'Local accounts always have User.';
    return '';
  };
  const drawGive = () => {
    const who = gv.who;
    $('gv-who').innerHTML = who ? `<div class="pp-picked">${pic(who)}<span class="pp-two"><b>${esc(whoOf(who))}</b><small>${esc(KIND[who.kind])}${who.kind === 'user' && who.display ? ` · ${esc(who.name)}` : ''}</small></span>`
      + `<button type="button" class="icon" data-gv-clear data-tip="Clear" aria-label="Clear">${svg('x')}</button></div>` : '';
    $('gv-adder').hidden = !!who;
    $('gv-roles').innerHTML = [0, 1, 2].map((t) => data.roles.filter((r) => r.tier === t).map((r) => {
      const off = whyRole(r);
      if (off && gv.role === r.id && !gv.edit) gv.role = '';
      return `<label class="pp-ropt${off ? ' off' : ''}"${off ? ` data-tip="${esc(off)}"` : ''} data-color="${esc(r.color)}"><input type="radio" name="gv-role" value="${esc(r.id)}"${gv.role === r.id ? ' checked' : ''}${off ? ' disabled' : ''}>`
        + `<i class="dot"></i><b>${esc(r.name)}</b><span class="pp-rtier">${esc(tierName(r.tier))}</span><span class="pp-rn">${r.id === 'admin' ? 'Every permission' : plural(r.permissions.length, 'permission', 'permissions')}</span></label>`;
    }).join('')).join('');
    const r = gv.role ? role(gv.role) : null;
    const left = (ous?.list || []).filter((p) => !gv.ous.some((dn) => same(dn, p.dn)));
    const root = (ous?.list || [])[0]?.dn.split(/(?<!\\),/).filter((p) => /^DC=/i.test(p)).join(',');
    $('gv-where').innerHTML = !r ? '<p class="pp-fh">Select a role first.</p>'
      : r.id === 'admin' ? '<p class="pp-fh">Administrator works on everything, so Where does not apply.</p>'
        : !scoped(r) ? `<p class="pp-fh">${esc(r.name)} has no Active Directory tasks, so Where does not apply.</p>`
          : `<div class="seg pp-seg"><button type="button" data-gv-w="all" aria-pressed="${gv.w === 'all'}">Entire domain</button><button type="button" data-gv-w="ous" aria-pressed="${gv.w === 'ous'}">Only these OUs</button></div>`
            + (gv.w === 'ous' ? `<div class="ou-row pp-ourow"><select id="gv-ou" aria-label="Add an OU"><option value="">${ous?.loading ? 'Reading OUs…' : 'Add an OU…'}</option>`
              + (root ? `<option value="${esc(root)}" data-fixed="Select an OU. For everything, select Entire domain.">${esc(root.split(',').map((p) => p.slice(3)).join('.'))}</option>` : '')
              + left.map((p) => `<option value="${esc(p.dn)}">${esc(p.path || p.name)}</option>`).join('')
              + `</select><button type="button" data-browse-ou="gv-ou"${left.length ? '' : ' disabled'}>${svg('folder')}Browse…</button></div>`
              + `<div class="scope-ous pp-ous">${gv.ous.map((dn) => `<span class="gchip" data-tip="${esc(dn)}">${svg('folder')}${esc(pathOf(dn))}<button type="button" class="chip-x" data-gv-oux="${esc(dn)}" aria-label="${esc(`Remove ${pathOf(dn)}`)}">${svg('x')}</button></span>`).join('')
                || '<span class="pp-fh">Add at least one OU. Child OUs are included.</span>'}</div>` : '')
            + (ous?.problem ? `<p class="pp-fh">Could not list OUs: ${esc(ous.problem)}</p>` : '')
            + `<p class="pp-fh">${gv.w === 'all' ? 'Its Active Directory tasks work on every user, group and computer the service account can reach.' : 'Its Active Directory tasks work only on what is in these OUs.'}</p>`;
    $('gv-ends').innerHTML = `<div class="seg pp-seg"><button type="button" data-gv-e="never" aria-pressed="${gv.e === 'never'}">Never</button><button type="button" data-gv-e="date" aria-pressed="${gv.e === 'date'}">On a date</button></div>`
      + (gv.e === 'date' ? `<input type="date" class="pp-input pp-date" id="gv-date" value="${gv.date}" min="${isoDay(new Date())}" max="${isoDay(new Date(Date.now() + 5 * 365 * DAY))}" aria-label="Ends on">` : '')
      + `<p class="pp-fh">${gv.e === 'date' ? 'The assignment stops working at the end of that day. The activity log records it.' : 'The assignment stays until someone removes it.'}</p>`;
    R.paint($('give-dialog'));
    sumGive();
  };
  const placesOf = (r) => (r && scoped(r) && gv.w === 'ous' ? [...gv.ous] : []);
  const endsOf = () => (gv.e === 'date' && gv.date ? new Date(`${gv.date}T23:59:59`).toISOString() : null);
  const sumGive = () => {
    const r = gv.role ? role(gv.role) : null;
    const okWhere = !r || !scoped(r) || gv.w === 'all' || gv.ous.length;
    $('gv-go').disabled = !(gv.who && r && okWhere && (gv.e === 'never' || gv.date));
    const box = $('gv-sum');
    box.hidden = !(gv.who && r);
    if (box.hidden) return;
    const where = scoped(r) ? ` in ${gv.w === 'all' ? 'the entire domain' : gv.ous.length ? gv.ous.map(pathOf).join(', ') : '…'}` : '';
    box.innerHTML = `${svg('info')}<span><b>${esc(whoOf(gv.who))}</b> ${gv.edit ? 'keeps' : 'gets'} <b>${esc(r.name)}</b>${esc(where)}${gv.e === 'date' && gv.date ? ` until ${esc(day(`${gv.date}T12:00`))}` : ''}.`
      + `${gv.who.kind === 'group' ? ` Everyone in ${esc(whoOf(gv.who))}, including nested groups, gets it when they sign in.` : ''}</span>`;
  };
  R.dropdown($('gv-adder'), {
    find: async (text) => {
      const found = await search(text);
      return { items: found.entries, note: found.problem };
    },
    draw: (e, i) => entry(e, ` data-pick="${i}"`),
    pick: (x) => {
      gv.who = { kind: x.kind, name: x.name, display: x.display };
      drawGive();
    },
  });
  $('give-dialog').addEventListener('click', (e) => {
    const on = (attr) => e.target.closest(`[${attr}]`);
    if (on('data-gv-clear')) { gv.who = null; drawGive(); $('gv-find').focus(); return; }
    if (on('data-gv-w')) { gv.w = on('data-gv-w').dataset.gvW; drawGive(); return; }
    if (on('data-gv-e')) { gv.e = on('data-gv-e').dataset.gvE; drawGive(); return; }
    if (on('data-gv-oux')) { gv.ous = gv.ous.filter((dn) => !same(dn, on('data-gv-oux').dataset.gvOux)); drawGive(); }
  });
  $('give-dialog').addEventListener('change', (e) => {
    if (e.target.name === 'gv-role') { gv.role = e.target.value; drawGive(); return; }
    if (e.target.id === 'gv-ou' && e.target.value) {
      const o = e.target.selectedOptions[0];
      if (!o?.dataset.fixed) gv.ous = tidy([...gv.ous, e.target.value]);
      drawGive();
      return;
    }
    if (e.target.id === 'gv-date') { gv.date = e.target.value; sumGive(); }
  });
  $('give-dialog').addEventListener('input', (e) => {
    if (e.target.id === 'gv-date') { gv.date = e.target.value; sumGive(); }
  });
  $('gv-cancel').addEventListener('click', () => $('give-dialog').close());
  $('gv-go').addEventListener('click', async () => {
    const r = role(gv.role);
    if (!r || !gv.who) return;
    const body = { kind: gv.who.kind, name: gv.who.name, display: gv.who.display, scope: placesOf(r), ends: endsOf(), reason: $('gv-reason').value.trim() || null };
    $('gv-go').disabled = true;
    try {
      await R.send(`/api/roles/${encodeURIComponent(r.id)}/members${gv.edit ? '/edit' : ''}`, body);
      $('give-dialog').close();
      const where = scoped(r) && body.scope.length ? ` in ${body.scope.map(pathOf).join(', ')}` : '';
      const until = body.ends ? ` until ${day(body.ends)}` : '';
      R.toast(gv.edit ? 'Assignment saved.' : 'Role given.', `${whoOf(gv.who)} has ${r.name}${where}${until}.`);
      const id = aid(r, { kind: gv.who.kind, name: gv.who.kind === 'everyone' ? 'ad' : gv.who.name });
      if (!gv.edit) came.set(id, Date.now());
      await refresh();
      const x = assignOf(id);
      if (x && (gv.edit || !R.panel.is('people'))) openAssign(x, R.panel.is('people') && R.panel.key === keyOf(x));
    } catch (err) {
      say('gv-problem', err.message);
      sumGive();
    }
  });
  $('ppl-give').addEventListener('click', () => openGive());

  // Searching groups, people and local accounts, for Give a role.
  const search = async (text) => {
    const found = await R.get(`/api/roles/search?q=${encodeURIComponent(text)}`);
    // When the domain can't be searched, what's typed can still be given as a group or a person.
    if (found.problem && text) {
      found.entries.push({ kind: 'group', name: text, display: null, description: `Add group ${text} as entered` },
        { kind: 'user', name: text, display: null, description: `Add user ${text} as entered` });
    }
    return found;
  };
  const entry = (e, attrs) => R.option(attrs, `<span class="pic ${e.kind}">${e.kind === 'user' && e.display ? esc(initials(e.display)) : svg(ICON[e.kind])}</span>`,
    e.display || e.name, e.kind === 'user' && e.display ? `${e.name}${e.description ? ` · ${e.description}` : ''}` : e.description || KIND[e.kind]);

  // --- New local account, new role, Preview as --------------------------------------------------------------------

  $('ppl-local').addEventListener('click', () => {
    $('pd-user').value = '';
    $('pd-password').value = '';
    $('pd-problem').hidden = true;
    $('local-dialog').showModal();
    $('pd-user').focus();
  });
  $('pd-cancel').addEventListener('click', () => $('local-dialog').close());
  $('pd-go').addEventListener('click', async () => {
    const name = $('pd-user').value.trim();
    try {
      await R.send('/api/accounts', { username: name, password: $('pd-password').value, roles: [] });
      $('local-dialog').close();
      R.toast('Account created.', `${name} can sign in with the User role. Give it more roles here.`);
      await refresh();
      openPerson('local', name);
    } catch (err) {
      say('pd-problem', err.message);
    }
  });
  // New role: made in the tier under the maker's own, then opened on Name and tier to name it.
  $('role-new').addEventListener('click', async () => {
    let n = 1;
    while (data.roles.some((r) => same(r.name, n === 1 ? 'New role' : `New role ${n}`))) n++;
    try {
      const made = await R.send('/api/roles', { name: n === 1 ? 'New role' : `New role ${n}`, color: SWATCHES[(data.roles.length * 3) % SWATCHES.length] });
      await refresh();
      if (tab !== 'roles') location.hash = '#settings/people';
      openRole(role(made.id), false, 'name');
    } catch (err) {
      problem(err.message);
    }
  });
  $('ppl-preview').addEventListener('click', () => {
    if (!data) return;
    R.actions.open($('ppl-preview'), {
      items: data.roles.filter((r) => r.id !== 'admin').map((r) => ({ act: r.id, label: r.name, hint: tierName(r.tier) })),
      pick: (id) => {
        const r = role(id);
        R.viewAs({ name: r.name }, [...new Set([...role('user').permissions, ...r.permissions])]);
        location.hash = '#overview';
      },
    });
  });

  // --- Showing the page --------------------------------------------------------------------------------------------

  // Draws what the address asks for, from what's loaded. Going to another tab closes the side panel; an old
  // #settings/people/role/<id> address opens that role in it.
  let lastTab = '';
  const route = () => {
    const [pageName, sub, rest] = R.where();
    if (pageName !== 'settings' || sub !== 'people' || !data) return;
    const asked = rest[0] === 'role' ? 'roles' : ['soon', 'groups'].includes(rest[0]) ? 'assignments' : TABS.includes(rest[0]) ? rest[0] : 'roles';
    tab = asked;
    if (lastTab && lastTab !== tab && R.panel.is('people')) R.panel.close();
    lastTab = tab;
    document.querySelectorAll('#set-people [data-pppane]').forEach((p) => { p.hidden = p.dataset.pppane !== tab; });
    $('pp-tabs').querySelectorAll('[data-pptab]').forEach((a) => current(a, a.dataset.pptab === tab));
    $('pp-rule').textContent = `You are in tier ${data.me.tier}. A role can be changed only by someone in a higher tier. The same goes for giving the role to someone, and for resetting the passwords and MFA of the people who have it.`;
    $('rail-roles').textContent = data.roles.length;
    drawTab();
    if (rest[0] === 'role' && role(decodeURIComponent(rest[1] || ''))) {
      openRole(role(decodeURIComponent(rest[1])), false, rest[2] === 'display' ? 'name' : rest[2] === 'members' ? 'who' : 'can');
      history.replaceState(null, '', '#settings/people');
    } else {
      panelAgain();
    }
  };
  // Loads again, then draws.
  let failed = false; // the last read didn't work, so the next one that does draws
  const refresh = async () => {
    try {
      data = await R.get('/api/roles');
      failed = false;
      problem('');
    } catch (err) {
      failed = true;
      problem(err.message);
      return;
    }
    $('rail-roles').textContent = data.roles.length;
    route();
  };
  // Opening the page reads the roles again; moving around inside it draws from what's loaded.
  let here = false;
  document.addEventListener('rabbidlab:show', (e) => {
    const was = here;
    here = e.detail[0] === 'settings' && e.detail[1] === 'people' && R.has('access.view');
    if (!here) return;
    if (was && data) route();
    else refresh();
  });
  // The count in the rail, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => {
    if (R.has('access.view') && !here) refresh();
  });
  // While it's open: every 10 seconds, but not over a role being edited, or a window being filled in.
  const editing = () => !!rp.edit || (!!rp.form && !!role(rp.id) && formDirty(role(rp.id)));
  R.live({
    page: () => here,
    hold: () => editing() || $('give-dialog').open || $('local-dialog').open,
    every: 10000,
    read: () => R.get('/api/roles'),
    now: () => (failed ? null : data),
    draw: (got) => {
      data = got;
      failed = false;
      problem('');
      $('rail-roles').textContent = data.roles.length;
      route();
    },
    areas: () => [$('pp-roles-table'), $('ppl-table'), $('pp-people-table'), $('pp-local-table')],
  });
})();
