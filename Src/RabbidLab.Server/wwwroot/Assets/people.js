// Roles and people, in Settings: #settings/people (every assignment, or /soon, /groups, /local) and
// #settings/people/role/<id>/<tab> (one role's assignments, permissions, and name and tier). Each assignment says where in
// Active Directory it works, when it ends and why; a role is changed or given only by someone in a higher tier (Roles.cs).
// It asks /api/roles when the page opens and after each change. console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials, sure } = R;
  const SWATCHES = R.palette;
  const KIND = { group: 'Group', user: 'User', local: 'Local account', everyone: 'Everyone', windows: 'Built-in', all: 'Built-in' };
  const ICON = { group: 'users', user: 'user', local: 'key', everyone: 'users', windows: 'monitor', all: 'users' };
  const VIEWS = { all: 'All assignments', soon: 'Ending in 30 days', groups: 'Given to groups', local: 'Local accounts' };
  const TABS = ['assignments', 'permissions', 'display'];
  const TIER_ABOUT = [
    'Only Administrator. It has every permission and can change and give every other role.',
    'People with a tier 1 role can change and give tier 2 roles, when their permissions allow it. They can reset passwords and MFA only for people whose other roles are all in tier 2.',
    'People whose roles are all in tier 2 cannot change or give roles. User, which everyone has, is in tier 2.',
  ];
  const DAY = 86400000;
  const EXPAND = 'rabbidlab.people.expand';
  let data = null; // what /api/roles said
  let draft = null; // the role being changed, until Save
  let expand = (() => { try { return localStorage.getItem(EXPAND) === '1'; } catch { return false; } })();
  const picked = new Set(); // ticked assignments
  const came = new Map(); // assignment id -> when it was given here, for the row's arrival
  const done = new WeakSet();

  const problem = (text) => say('ppl-problem', text);
  const role = (id) => data.roles.find((r) => r.id === id);
  const mine = (key) => data.me.can.includes(key);
  const allParts = () => data.catalog.flatMap((p) => p.actions.map((a) => a.key));
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
  const dot = (r) => `<span class="pp-role" data-color="${esc(r.color)}"><i class="dot"></i><span class="ell">${esc(r.name)}</span></span>`;

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
  const endsCell = (m) => (!m?.ends ? muted('Never') : ended(m) ? badge(`Ended ${day(m.ends)}`, 'grey') : daysLeft(m.ends) <= 14 ? badge(day(m.ends), 'amber') : `<span class="ell">${esc(day(m.ends))}</span>`);
  // Why an assignment can't be changed or removed here, or ''.
  const whyNot = (x) => {
    if (x.via) return `Comes from ${whoOf(x.via)}. Remove the group's assignment instead.`;
    if (x.built) return `${x.built}.`;
    if (!mine('access.members')) return 'Your roles do not include giving roles.';
    if (!x.r.manage) return `Only someone in a higher tier than ${x.r.name} can change who has it.`;
    return '';
  };
  const lastAdmin = (x) => x.r.id === 'admin' && x.r.members.filter((m) => !ended(m)).length <= 1 && !ended(x.m);

  // What the left and the address pick: a view, or one role.
  let view = 'all';
  let viewRole = null;
  const shown = () => {
    const q = $('ppl-find').value.trim().toLowerCase();
    const base = assignments().filter((x) => (viewRole ? x.r === viewRole
      : view === 'soon' ? x.m?.ends && !ended(x.m) && daysLeft(x.m.ends) <= 30
        : view === 'groups' ? x.kind === 'group'
          : view === 'local' ? x.kind === 'local' : true));
    return withPeople(base).filter((x) => !q || [whoOf(x), x.name, x.r.name, whereText(x), x.m?.addedBy, x.m?.reason, x.via ? whoOf(x.via) : ''].some((t) => String(t || '').toLowerCase().includes(q)));
  };
  const grid = R.grid({
    el: $('ppl-table'), key: 'assignments', sort: { key: 'role', dir: 1 },
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-pp-tick-all aria-label="Select all"></label>',
      cell: (x) => (whyNot(x) ? '' : `<label class="tk"><input type="checkbox" data-pp-tick="${esc(x.id)}" aria-label="${esc(`Select ${whoOf(x)}`)}"${picked.has(x.id) ? ' checked' : ''}></label>`) },
    cols: [
      { key: 'who', label: 'Person or group', w: 240, fixed: true, get: whoOf, cell: (x) => `<div class="who">${pic(x)}<b class="ell">${esc(whoOf(x))}</b></div>` },
      { key: 'type', label: 'Type', w: 110, on: true, get: typeOf },
      { key: 'role', label: 'Role', w: 170, on: true, get: (x) => data.roles.indexOf(x.r), text: (x) => x.r.name, cell: (x) => dot(x.r) },
      { key: 'tier', label: 'Tier', w: 70, on: true, get: (x) => x.r.tier, text: (x) => tierName(x.r.tier), cell: (x) => `<span class="ell">${esc(tierName(x.r.tier))}</span>` },
      { key: 'where', label: 'Where', w: 210, on: true, get: whereText, cell: (x) => (whereText(x) ? `<span class="ell">${esc(whereText(x))}</span>` : muted('-')) },
      { key: 'ends', label: 'Ends', w: 130, on: true, get: (x) => (x.m?.ends ? +new Date(x.m.ends) : 9e15), text: (x) => (x.m?.ends ? day(x.m.ends) : 'Never'), cell: (x) => endsCell(x.m) },
      { key: 'from', label: 'From group', w: 170, on: true, get: (x) => (x.via ? whoOf(x.via) : ''), cell: (x) => (x.via ? `<span class="ell">${esc(whoOf(x.via))}</span>` : muted('-')) },
      { key: 'by', label: 'Added by', w: 140, on: true, get: (x) => (x.via ? '' : x.m?.addedBy || ''), cell: (x) => (x.via || !x.m?.addedBy ? muted('-') : `<span class="ell">${esc(x.m.addedBy)}</span>`) },
      { key: 'added', label: 'Added', w: 120, get: (x) => (x.m?.added && !x.via ? +new Date(x.m.added) : ''), text: (x) => (x.m?.added && !x.via ? day(x.m.added) : ''), cell: (x) => (x.m?.added && !x.via ? `<span class="ell">${esc(day(x.m.added))}</span>` : muted('-')) },
      { key: 'reason', label: 'Reason', w: 260, get: (x) => (x.via ? '' : x.m?.reason || '') },
      { key: 'seen', label: 'Last sign-in', w: 130, get: (x) => (x.p?.lastSignIn ? +new Date(x.p.lastSignIn) : ''), text: (x) => (x.p ? since(x.p.lastSignIn) : ''), cell: (x) => (x.p ? `<span class="ell">${esc(since(x.p.lastSignIn))}</span>` : muted('-')) },
    ],
    row: (x) => ` data-pp-row="${esc(x.id)}" tabindex="0"`, rowClass: (x) => `${x.via ? 'pp-via' : ''}${ended(x.m) ? ' off' : ''}${picked.has(x.id) ? ' picked' : ''}`,
    empty: () => ($('ppl-find').value.trim() ? 'No assignments match.' : viewRole ? `Nobody has ${viewRole.name} yet.` : 'No assignments.'), loading: () => !data, redraw: () => drawList(), picked: () => picked.size,
  });
  let drawnRows = new Map(); // row id -> the row, as last drawn
  const drawTicks = () => {
    $('ppl-table').querySelectorAll('[data-pp-tick]').forEach((b) => { b.checked = picked.has(b.dataset.ppTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const all = $('ppl-table').querySelector('[data-pp-tick-all]');
    const can = [...drawnRows.values()].filter((x) => !whyNot(x));
    if (all) { all.checked = !!can.length && can.every((x) => picked.has(x.id)); all.indeterminate = !all.checked && can.some((x) => picked.has(x.id)); }
    const n = picked.size;
    const act = $('ppl-act');
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n;
    if (n) delete act.dataset.tip;
    else act.dataset.tip = 'Select assignments first';
  };
  const markRow = () => $('ppl-table').querySelectorAll('[data-pp-row]').forEach((el) => {
    const x = drawnRows.get(el.dataset.ppRow);
    el.classList.toggle('dw-on', !!x && R.panel.is('people') && R.panel.key === keyOf(x));
  });
  R.panel.el.addEventListener('close', markRow);
  const drawList = () => {
    const list = shown();
    [...picked].forEach((id) => { if (!list.some((x) => x.id === id && !whyNot(x))) picked.delete(id); });
    const rows = grid.draw(list);
    drawnRows = new Map(rows.map((x) => [x.id, x]));
    grid.tools($('ppl-tools'), $('ppl-rows'));
    R.paint($('ppl-table'));
    came.forEach((t, id) => {
      if (Date.now() - t > 2800) { came.delete(id); return; }
      const row = $('ppl-table').querySelector(`[data-pp-row="${CSS.escape(id)}"]`);
      if (row && !done.has(row)) { done.add(row); R.arrive(row, t); }
    });
    drawTicks();
    markRow();
  };

  // --- The left: the views, then the roles under their tiers -------------------------------------------------------

  const drawSide = () => {
    const all = assignments();
    const n = { all: all.length, soon: all.filter((x) => x.m?.ends && !ended(x.m) && daysLeft(x.m.ends) <= 30).length, groups: all.filter((x) => x.kind === 'group').length, local: all.filter((x) => x.kind === 'local').length };
    Object.keys(VIEWS).forEach((k) => { $(`b-${k}`).textContent = n[k]; });
    document.querySelectorAll('#set-people .side a[data-view]').forEach((a) => current(a, !viewRole && a.dataset.view === view));
    $('ppl-roles').innerHTML = [0, 1, 2].map((t) => {
      const rs = data.roles.filter((r) => r.tier === t);
      return rs.length ? `<p class="side-label">${esc(tierName(t))}${t === data.me.tier ? '<span>Yours</span>' : ''}</p>` + rs.map((r) => `<a class="role" href="#settings/people/role/${encodeURIComponent(r.id)}" data-role="${esc(r.id)}" data-color="${esc(r.color)}"${r === viewRole ? ' aria-current="page"' : ''}>`
        + `<i class="dot"></i><span class="nm">${esc(r.name)}</span>${r.manage ? '' : svg('lock', 'lk')}<small>${all.filter((x) => x.r === r).length}</small></a>`).join('') : '';
    }).join('');
    $('rail-roles').textContent = data.roles.length;
    $('pp-rule').textContent = `You are in tier ${data.me.tier}. A role can be changed only by someone in a higher tier. The same goes for giving the role to someone, and for resetting the passwords and MFA of the people who have it.`;
    R.paint($('ppl-roles'));
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
  let panelOn = null; // what the side panel shows: { a } or { p }
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
        : `<p class="muted dw-empty">Nobody it reaches has signed in yet.</p>`) : '');
    R.paint(R.panel.body);
    if (!again) R.panel.open('people', keyOf(x));
    markRow();
  };
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
    return { kind, name, display, p, got, source: kind === 'local' ? 'local' : p?.source || 'ad' };
  };
  const openPerson = (kind, name, again = false) => {
    const x = personOf(kind, name);
    panelOn = { p: `${kind}:${name}` };
    const all = x.got.some((g) => g.r.id === 'admin');
    const can = new Set(x.got.flatMap((g) => g.r.permissions));
    const top = Math.min(...x.got.map((g) => g.r.tier));
    const local = kind === 'local';
    const does = all ? '<li><b>Everything</b><span>The Administrator role has every permission.</span></li>'
      : data.catalog.map((p) => {
        const on = p.actions.filter((a) => can.has(a.key));
        return on.length ? `<li><b>${esc(p.name)}</b><span>${esc(on.map((a) => lower(a.name)).join(', '))}</span></li>` : '';
      }).join('') || '<li><span>No permissions.</span></li>';
    const giveable = mine('access.members') && (local || x.source === 'ad');
    R.panel.body.innerHTML = head(pic({ kind: local ? 'local' : 'user', name, display: x.display }, ' dw-pic'), x.display || name,
      local ? 'Local account' : x.source === 'windows' ? `Windows administrator · ${name.split('\\')[0]}` : `${name}${x.p?.domain ? ` · ${x.p.domain}` : ''}`)
      + '<div class="dw-bar">'
      + (giveable ? qa('data-pp-give-to', 'user-plus', 'Give a role') : '')
      + (local && mine('access.local') ? qa('data-password', 'key', 'New password') + qa('data-remove-account', 'trash', 'Delete account') : '')
      + (x.p?.mfa && mine('access.mfa') ? qa('data-reset-mfa', 'refresh', 'Reset MFA') : '') + '</div>'
      + strip([['Signed in with', local ? 'Local account' : x.source === 'windows' ? 'Windows' : 'Active Directory'], ['Highest tier', esc(tierName(top))], ['Roles', String(x.got.length)],
        ['MFA', x.p?.mfa ? badge('Set up', 'green') : badge('Not set up', 'amber')], ['Last sign-in', esc(since(x.p?.lastSignIn))]])
      + sec(`Roles <small>${x.got.length}</small>`, `<div class="dw-list">${x.got.map((g) => `<div class="mrow pp-mrow"${g.m ? ` data-pp-assign="${esc(aid(g.r, g.m))}" tabindex="0"` : ''}>`
        + `<span class="pp-two">${dot(g.r)}<small>${esc(`${tierName(g.r.tier)} · ${g.how}`)}</small></span>`
        + `<span class="pp-two pp-end">${scoped(g.r) ? `<span class="ell">${esc(whereText({ r: g.r, m: g.m }))}</span>` : ''}<small>${g.m?.ends ? `Until ${esc(day(g.m.ends))}` : 'No end date'}</small></span></div>`).join('')}</div>`)
      + sec('MFA', `<p class="dw-how">${esc(x.p?.mfa ? `Set up since ${R.when(x.p.mfa)}.` : 'Not set up. Setup is required at the next sign-in.')}</p>`)
      + sec('Effective permissions', `<ul class="does">${does}</ul>`);
    R.paint(R.panel.body);
    if (!again) R.panel.open('people', `p:${kind}:${name}`);
    markRow();
  };
  const openRow = (x) => {
    if (R.panel.is('people') && R.panel.key === keyOf(x)) { R.panel.close(); return; }
    if (x.via) openPerson('user', x.name);
    else if (x.kind === 'local' && x.built) openPerson('local', x.name);
    else openAssign(x);
  };
  const assignOf = (id) => assignments().find((x) => x.id === id);
  // The side panel follows the list: drawn again as it is now, or closed when what it shows has gone.
  const panelAgain = () => {
    if (!R.panel.is('people') || !panelOn) return;
    if (panelOn.a) {
      const x = assignOf(panelOn.a);
      if (x) openAssign(x, true);
      else R.panel.close();
    } else {
      const [kind, ...rest] = panelOn.p.split(':');
      openPerson(kind, rest.join(':'), true);
    }
  };

  // --- Removing assignments --------------------------------------------------------------------------------------

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
  $('ppl-act').addEventListener('click', () => {
    const xs = [...picked].map(assignOf).filter(Boolean);
    R.actions.open($('ppl-act'), {
      items: [{ act: 'edit', label: 'Change where and when it ends…', disabled: xs.length !== 1 }, { act: 'clear', label: 'Clear selection' }, '-',
        { act: 'remove', label: 'Remove assignments…', hint: String(xs.length), danger: true }],
      pick: (act) => {
        if (act === 'edit' && xs.length === 1) openGive({ edit: xs[0] });
        else if (act === 'clear') { picked.clear(); drawList(); }
        else if (act === 'remove') remove(xs);
      },
    });
  });
  $('ppl-table').addEventListener('change', (e) => {
    const t = e.target.closest('[data-pp-tick]');
    if (t) {
      if (t.checked) picked.add(t.dataset.ppTick);
      else picked.delete(t.dataset.ppTick);
      drawTicks();
      return;
    }
    if (e.target.closest('[data-pp-tick-all]')) {
      const can = [...drawnRows.values()].filter((x) => !whyNot(x));
      if (e.target.checked) can.forEach((x) => picked.add(x.id));
      else can.forEach((x) => picked.delete(x.id));
      drawTicks();
    }
  });
  $('ppl-table').addEventListener('click', (e) => {
    if (e.target.closest('.tk, button, a, input')) return;
    const row = e.target.closest('[data-pp-row]');
    const x = row && drawnRows.get(row.dataset.ppRow);
    if (x) openRow(x);
  });
  $('ppl-table').addEventListener('keydown', (e) => {
    const row = e.key === 'Enter' && e.target.matches('[data-pp-row]') ? drawnRows.get(e.target.dataset.ppRow) : null;
    if (row) openRow(row);
  });
  $('ppl-expand').checked = expand;
  $('ppl-expand').addEventListener('change', (e) => {
    expand = e.target.checked;
    try { localStorage.setItem(EXPAND, expand ? '1' : '0'); } catch { /* not kept, then */ }
    drawList();
  });

  // Clicks in the side panel.
  R.panel.el.addEventListener('click', (e) => {
    if (!R.panel.is('people') || !panelOn) return;
    const on = (attr) => e.target.closest(`[${attr}]`);
    const x = panelOn.a ? assignOf(panelOn.a) : null;
    const [pk, ...pn] = panelOn.p ? panelOn.p.split(':') : [];
    const pname = pn.join(':');
    if (on('data-pp-edit') && x && !whyNot(x)) openGive({ edit: x });
    else if (on('data-pp-remove') && x) remove([x]);
    else if (on('data-pp-open-role') && x) location.hash = `#settings/people/role/${encodeURIComponent(x.r.id)}`;
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
  $('role-give').addEventListener('click', () => { if (viewRole) openGive({ role: viewRole.id }); });

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

  // --- A new local account -----------------------------------------------------------------------------------------

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
      came.set(`user|local|${name.toLowerCase()}`, Date.now());
      await refresh();
      openPerson('local', name);
    } catch (err) {
      say('pd-problem', err.message);
    }
  });

  // --- One role: Assignments, Permissions, and Name and tier ---------------------------------------------------------

  const editable = (r) => r.manage && mine('access.roles') && r.id !== 'admin';
  const dirty = (r) => draft && draft.id === r.id && (draft.name !== r.name || draft.about !== (r.about || '') || draft.color !== r.color || draft.tier !== r.tier
    || draft.permissions.size !== r.permissions.length || r.permissions.some((p) => !draft.permissions.has(p)));
  const fresh = (r) => ({ id: r.id, name: r.name, about: r.about || '', color: r.color, tier: r.tier, permissions: new Set(r.permissions) });
  const unsaved = (r) => { $('role-unsaved').hidden = !dirty(r); };
  const tierWhy = (r, t) => {
    if (r.id === 'admin') return 'Administrator is always tier 0.';
    if (r.id === 'user') return 'User is always tier 2.';
    if (t === 0) return 'Administrator is the only role in tier 0.';
    if (t <= data.me.tier) return `You can put a role only in a lower tier than your own (tier ${data.me.tier}).`;
    return '';
  };

  const drawRole = (r, tab) => {
    if (!draft || draft.id !== r.id) draft = fresh(r);
    const total = allParts().length;
    const count = r.id === 'admin' ? total : draft.permissions.size;
    const n = assignments().filter((x) => x.r === r).length;
    $('role-head').style.setProperty('--c', draft.color);
    $('role-view').style.setProperty('--c', draft.color);
    $('role-name').textContent = draft.name || 'Untitled role';
    $('role-line').textContent = `${tierName(r.tier)} · ${plural(n, 'assignment', 'assignments')} · ${count} of ${total} permissions`;
    $('role-viewas-text').textContent = `Preview as ${r.name}`;
    $('role-count').textContent = n;
    $('role-give').disabled = !r.manage;
    if (r.manage) delete $('role-give').dataset.tip;
    else $('role-give').dataset.tip = `Only someone in a higher tier than ${r.name} can give it.`;
    $('role-tabs').querySelectorAll('[data-tab]').forEach((a) => {
      a.href = `#settings/people/role/${encodeURIComponent(r.id)}/${a.dataset.tab}`;
      current(a, a.dataset.tab === tab);
    });
    document.querySelectorAll('#role-view [data-pane]').forEach((p) => { p.hidden = p.dataset.pane !== tab; });
    const note = r.id === 'admin' ? 'Administrator always has every permission. Only someone with Administrator can give it.'
      : !r.manage ? `Only someone in a higher tier than ${r.name} can change this role or give it.` : !mine('access.roles') ? 'Your roles do not include changing roles.' : '';
    $('role-note').hidden = !note || tab === 'assignments';
    $('role-note').textContent = note;
    const can = editable(r);

    // Name and tier: name, what it's for, tier, colour, how its chip looks.
    $('role-edit-name').value = draft.name;
    $('role-edit-name').disabled = !can || r.id === 'user';
    $('role-edit-about').value = draft.about;
    $('role-edit-about').disabled = !can;
    $('role-tier').innerHTML = [0, 1, 2].map((t) => {
      const no = can ? tierWhy(r, t) : 'You cannot change this role.';
      return `<button type="button" role="radio" data-tier="${t}" aria-checked="${draft.tier === t}" aria-pressed="${draft.tier === t}"${no && draft.tier !== t ? ` disabled data-tip="${esc(no)}"` : no ? ' disabled' : ''}>${esc(tierName(t))}</button>`;
    }).join('');
    $('role-tier-about').textContent = TIER_ABOUT[draft.tier];
    $('role-swatches').innerHTML = SWATCHES.map((c) => `<button type="button" role="radio" data-swatch="${c}" data-color="${c}" aria-label="${c}" aria-checked="${same(c, draft.color)}"${can ? '' : ' disabled'}></button>`).join('');
    const group = r.members.find((m) => m.kind === 'group')?.name || 'a group';
    $('role-preview').innerHTML = `<span class="rchip" data-color="${esc(draft.color)}"><i class="dot"></i>${esc(draft.name || 'Untitled role')}</span>`
      + `<span class="rchip via" data-color="${esc(draft.color)}"><i class="dot"></i>${esc(draft.name || 'Untitled role')}<small>${svg('users')}${esc(group)}</small></span>`
      + '<span class="hint">Dashed: given through a group.</span>';
    $('role-delete-row').hidden = r.builtIn || !can;
    $('role-delete-title').textContent = `Delete ${r.name}`;

    // Permissions: a switch for each thing, by part. Nobody gives what their own roles don't allow.
    const find = $('perm-find').value.trim().toLowerCase();
    $('perm-list').innerHTML = data.catalog.map((p) => {
      const acts = p.actions.filter((a) => !find || `${p.name} ${a.name} ${a.about}`.toLowerCase().includes(find));
      if (!acts.length) return '';
      const on = p.actions.filter((a) => r.id === 'admin' || draft.permissions.has(a.key)).length;
      return `<div class="sec">${svg(p.icon)}<b>${esc(p.name)}</b><span>${on} of ${p.actions.length}</span></div>`
        + (p.key === 'ad' && r.id !== 'admin' ? `<p class="hint pp-where">${svg('tree')}<span>Where these work is set on each assignment, in Give a role.</span></p>` : '')
        + '<div class="rows">' + acts.map((a) => {
          const locked = !can || !mine(a.key);
          return `<label class="row still perm${locked ? ' locked' : ''}"><span class="t"><b>${esc(a.name)}</b><span>${esc(a.about)}${can && !mine(a.key) ? ' You cannot grant a permission your roles do not include.' : ''}</span></span>`
            + `<input type="checkbox" class="switch" role="switch" data-perm="${esc(a.key)}" aria-label="${esc(a.name)}"${r.id === 'admin' || draft.permissions.has(a.key) ? ' checked' : ''}${locked ? ' disabled' : ''}></label>`;
        }).join('') + '</div>';
    }).join('') || '<div class="empty">No matching permissions.</div>';
    $('perm-clear').hidden = !can;

    R.paint($('role-head'));
    R.paint($('role-view'));
    unsaved(r);
  };

  $('role-edit-name').addEventListener('input', (e) => {
    draft.name = e.target.value;
    $('role-name').textContent = draft.name || 'Untitled role';
    document.querySelectorAll('#role-preview .rchip').forEach((c) => { c.childNodes[1].textContent = draft.name || 'Untitled role'; });
    unsaved(role(draft.id));
  });
  $('role-edit-about').addEventListener('input', (e) => {
    draft.about = e.target.value;
    unsaved(role(draft.id));
  });
  $('role-tier').addEventListener('click', (e) => {
    const b = e.target.closest('[data-tier]');
    if (!b || b.disabled) return;
    draft.tier = Number(b.dataset.tier);
    drawRole(role(draft.id), 'display');
  });
  $('role-swatches').addEventListener('click', (e) => {
    const b = e.target.closest('[data-swatch]');
    if (!b || b.disabled) return;
    draft.color = b.dataset.swatch;
    drawRole(role(draft.id), 'display');
  });
  $('perm-list').addEventListener('change', (e) => {
    if (!e.target.matches('[data-perm]')) return;
    if (e.target.checked) draft.permissions.add(e.target.dataset.perm);
    else draft.permissions.delete(e.target.dataset.perm);
    drawRole(role(draft.id), 'permissions');
  });
  $('perm-find').addEventListener('input', () => drawRole(role(draft.id), 'permissions'));
  $('perm-clear').addEventListener('click', () => {
    draft.permissions = new Set([...draft.permissions].filter((p) => !mine(p)));
    drawRole(role(draft.id), 'permissions');
  });
  $('role-reset').addEventListener('click', () => {
    draft = null;
    route();
  });
  $('role-save').addEventListener('click', async () => {
    const r = role(draft.id);
    try {
      await R.send(`/api/roles/${encodeURIComponent(draft.id)}`, { name: draft.name, about: draft.about, color: draft.color, permissions: [...draft.permissions], ...(draft.tier !== r.tier ? { tier: draft.tier } : {}) });
      draft = null;
      problem('');
      R.toast('Saved.', `${r.name} is saved.`);
    } catch (err) {
      problem(err.message);
      return;
    }
    refresh();
  });
  $('role-delete').addEventListener('click', () => {
    const r = role(draft.id);
    sure(`Delete ${r.name}?`, 'Its assignments are removed, and the people who had it lose what only this role gives.', 'Delete role', async () => {
      try {
        await R.send(`/api/roles/${encodeURIComponent(r.id)}/delete`);
        draft = null;
        await refresh(); // the list is drawn from what's loaded, so it's read before going back
        location.hash = '#settings/people';
      } catch (err) {
        problem(err.message);
      }
    });
  });
  $('role-viewas').addEventListener('click', () => {
    const r = role(draft.id);
    const perms = r.id === 'admin' ? allParts() : [...new Set([...role('user').permissions, ...r.permissions])];
    R.viewAs({ name: r.name }, perms);
    location.hash = '#overview';
  });

  // New role: made in the tier under the maker's own, then opened on Name and tier.
  $('role-new').addEventListener('click', async () => {
    let n = 1;
    while (data.roles.some((r) => same(r.name, n === 1 ? 'New role' : `New role ${n}`))) n++;
    try {
      const made = await R.send('/api/roles', { name: n === 1 ? 'New role' : `New role ${n}`, color: SWATCHES[(data.roles.length * 3) % SWATCHES.length] });
      draft = null;
      await refresh(); // so the new role is there to open
      location.hash = `#settings/people/role/${encodeURIComponent(made.id)}/display`;
    } catch (err) {
      problem(err.message);
    }
  });

  // --- Showing the page --------------------------------------------------------------------------------------------

  $('ppl-find').addEventListener('input', () => drawList());

  // Draws what the address asks for, from what's loaded. Going to another view or role closes the side panel, which would cover it.
  let lastAt = '';
  const route = () => {
    const [pageName, sub, rest] = R.where();
    if (pageName !== 'settings' || sub !== 'people' || !data) return;
    const r = rest[0] === 'role' ? role(decodeURIComponent(rest[1] || '')) : null;
    const tab = rest[2] === 'members' ? 'assignments' : TABS.includes(rest[2]) ? rest[2] : 'assignments';
    viewRole = r;
    view = !r && VIEWS[rest[0]] ? rest[0] : 'all';
    const at = `${r?.id || ''}|${view}|${r ? tab : ''}`;
    if (lastAt && at !== lastAt && R.panel.is('people')) R.panel.close();
    lastAt = at;
    $('role-head').hidden = !r;
    $('ppl-list').hidden = !!r && tab !== 'assignments';
    $('role-view').hidden = !r || tab === 'assignments';
    $('ppl-list').querySelector('.lib-bar').hidden = !!r;
    $('ppl-title').textContent = VIEWS[view];
    if (!r) { $('role-unsaved').hidden = true; draft = null; }
    drawSide();
    if (r) drawRole(r, tab);
    if (!r || tab === 'assignments') drawList();
    panelAgain();
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
      for (const id of ['ppl-table', 'ppl-roles']) R.skeleton($(id), '');
      for (const k of Object.keys(VIEWS)) if ($(`b-${k}`).querySelector('.sk')) $(`b-${k}`).textContent = '';
      return;
    }
    if (draft && !role(draft.id)) draft = null;
    $('rail-roles').textContent = data.roles.length;
    route();
  };
  // Opening the page reads the roles again; moving around inside it draws from what's loaded.
  let here = false;
  document.addEventListener('rabbidlab:show', (e) => {
    const was = here;
    here = e.detail[0] === 'settings' && e.detail[1] === 'people' && R.has('access.view');
    if (!here) $('role-unsaved').hidden = true;
    else if (was && data) route();
    else refresh();
  });
  // The count in the rail, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => {
    if (R.has('access.view') && !here) refresh();
  });
  // While it's open: every 10 seconds, but not over a role with changes that aren't saved, or a window being filled in.
  const unsavedRole = () => !!draft && !!role(draft.id) && dirty(role(draft.id));
  R.live({
    page: () => here,
    hold: () => unsavedRole() || $('give-dialog').open || $('local-dialog').open,
    every: 10000,
    read: () => R.get('/api/roles'),
    now: () => (failed ? null : data),
    draw: (got) => {
      data = got;
      failed = false;
      draft = null; // nothing unsaved: the role open shows what it is now
      problem('');
      $('rail-roles').textContent = data.roles.length;
      route();
    },
    areas: () => [$('ppl-table'), $('role-view')],
  });
})();
