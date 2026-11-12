// Roles and people: #people (everyone, or #people/people, /groups, /local) and #role/<id>/<tab> (one role's display,
// permissions and members). It asks /api/roles when the page opens and after each change. console.js shares its
// helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials, sure } = R;
  const SWATCHES = R.palette;
  const KIND = { group: 'Group', user: 'Person', local: 'Local account', everyone: 'Everyone', windows: 'Built in' };
  const ICON = { group: 'users', user: 'user', local: 'key', everyone: 'users', windows: 'monitor' };
  const BROWSE = { all: 'Everyone', people: 'People', groups: 'Groups', local: 'Local accounts' };
  let data = null; // what /api/roles said
  let list = []; // the rows drawn now
  let draft = null; // the role being changed, until Save
  let source = 'any';

  const problem = (text) => say('ppl-problem', text);
  const role = (id) => data.roles.find((r) => r.id === id);
  const mine = (key) => data.me.can.includes(key);
  const allParts = () => data.catalog.flatMap((p) => p.actions.map((a) => a.key));
  const same = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();
  const since = (iso) => {
    if (!iso) return '';
    const m = Math.floor((Date.now() - new Date(iso)) / 60000);
    const days = Math.floor(m / 1440);
    return m < 2 ? 'Signed in now' : m < 60 ? `Signed in ${m} min ago` : m < 120 ? 'Signed in an hour ago' : m < 1440 ? `Signed in ${Math.floor(m / 60)} hours ago`
      : days === 1 ? 'Signed in yesterday' : `Signed in ${days} days ago`;
  };
  const current = (el, on) => (on ? el.setAttribute('aria-current', 'page') : el.removeAttribute('aria-current'));
  const memberLabel = (m) => (m.kind === 'everyone' ? 'Everyone in Active Directory' : m.display || m.name);

  // --- Everyone, with the roles they get ---------------------------------------------------------------------------

  // Every group, person, local account and built-in member as a row, with each role they get: directly (removable),
  // from a group (via), or because everyone who signs in has User.
  const build = () => {
    const rows = new Map();
    const add = (kind, name) => {
      const k = `${kind}:${String(name).toLowerCase()}`;
      if (!rows.has(k)) rows.set(k, { kind, name, display: null, sub: '', roles: [], seen: null, source: kind === 'local' ? 'local' : 'ad' });
      return rows.get(k);
    };
    const give = (row, r, via, direct) => {
      const had = row.roles.find((x) => x.role.id === r.id);
      if (!had) row.roles.push({ role: r, via, direct });
      else if (direct) Object.assign(had, { via: null, direct: true });
    };
    const user = role('user');
    data.roles.forEach((r) => r.members.forEach((m) => {
      const row = add(m.kind, m.name);
      if (m.display) row.display = m.display;
      give(row, r, null, true);
    }));
    data.local.forEach((a) => add('local', a.username));
    if (data.windowsAdmins) {
      const row = add('windows', 'Windows administrators');
      row.sub = `Built in · of ${data.machine}`;
      give(row, role('admin'), null, false);
    }
    data.people.forEach((p) => {
      if (p.source === 'local') {
        add('local', p.username).seen = p.lastSignIn;
        return;
      }
      const row = add('user', p.username);
      Object.assign(row, { display: p.name, seen: p.lastSignIn, domain: p.domain, source: p.source });
      data.roles.forEach((r) => r.members.forEach((m) => {
        if (p.source === 'ad' && m.kind === 'group' && p.groups.some((g) => same(g, m.name))) give(row, r, m.name, false);
        if (p.source === 'ad' && m.kind === 'everyone') give(row, r, 'Everyone', false);
      }));
      if (p.source === 'windows') give(row, role('admin'), 'Windows administrators', false);
    });
    // Everyone signed in has User, whatever else they have.
    rows.forEach((row) => {
      if (['user', 'local'].includes(row.kind)) give(row, user, null, false);
      row.roles.sort((a, b) => data.roles.indexOf(a.role) - data.roles.indexOf(b.role));
      if (row.sub) return;
      row.sub = row.kind === 'group' ? `Group${data.domain ? ` · ${data.domain}` : ''}`
        : row.kind === 'everyone' ? `Anyone who can sign in to ${data.domain || 'Active Directory'}`
          : row.kind === 'local' ? 'Local account'
            : row.source === 'windows' ? `Windows administrator · ${row.name.split('\\')[0]}`
              : `${row.name}${row.domain ? ` · ${row.domain}` : ''}${row.seen ? '' : " · hasn't signed in yet"}`;
    });
    return [...rows.values()];
  };
  const shelf = (row) => (row.kind === 'user' ? 'people' : row.kind === 'local' ? 'local' : 'groups');
  const rank = (row) => (row.roles.length ? data.roles.indexOf(row.roles[0].role) : 999);
  const nameOf = (row) => (row.kind === 'everyone' ? 'Everyone in Active Directory' : row.display || row.name);
  // A row's own kind as a member, when it can be put in roles.
  const asMember = (row) => (['group', 'local', 'everyone'].includes(row.kind) || (row.kind === 'user' && row.source === 'ad') ? row.kind : null);
  // The roles someone could be given here: below the asker's own (manage), and not had directly already.
  const giveable = (row) => (mine('access.members') && asMember(row) ? data.roles.filter((r) => r.manage && !row.roles.some((x) => x.role.id === r.id && x.direct)) : []);

  const chip = (row, x) => {
    const removable = x.direct && x.role.manage && mine('access.members') && !(x.role.id === 'admin' && x.role.members.length === 1);
    const title = x.direct ? `In ${x.role.name}` : x.via ? `From ${x.via}` : x.role.id === 'user' ? 'Everyone who signs in has User' : `Built in`;
    return `<span class="rchip${x.via ? ' via' : ''}" data-color="${esc(x.role.color)}" title="${esc(title)}"><i class="dot"></i>${esc(x.role.name)}`
      + (x.via ? `<small>${svg('users')}${esc(x.via)}</small>` : '')
      + (removable ? `<button type="button" class="chip-x" data-take="${esc(x.role.id)}" aria-label="Take out of ${esc(x.role.name)}">${svg('x')}</button>` : '')
      + '</span>';
  };
  const pic = (row) => `<span class="pic ${row.kind}${row.source === 'windows' ? ' windows' : ''}">${row.kind === 'user' ? esc(initials(row.display || row.name)) : svg(ICON[row.kind])}</span>`;

  const parts = (r) => {
    if (r.id === 'admin') return ['Everything'];
    const found = data.catalog.filter((p) => p.actions.some((a) => r.permissions.includes(a.key))).map((p) => p.name);
    return found.length ? found : ['Nothing yet'];
  };
  const cards = () => data.roles.map((r) => {
    const p = parts(r);
    const who = r.members.map(memberLabel);
    return `<a class="rcard" href="#role/${encodeURIComponent(r.id)}" data-color="${esc(r.color)}"><b><i class="dot"></i>${esc(r.name)}${r.id === 'admin' ? svg('lock', 'lk') : ''}</b>`
      + `<p>${esc(r.about || '')}</p><div class="parts">${p.slice(0, 3).map((x) => `<span>${esc(x)}</span>`).join('')}${p.length > 3 ? `<span>+${p.length - 3}</span>` : ''}</div>`
      + `<div class="n">${svg('users')}${esc(who.length ? who.join(', ') : 'Nobody yet')}</div></a>`;
  }).join('');

  const drawList = (browse) => {
    const all = build();
    for (const k of Object.keys(BROWSE)) $(`b-${k}`).textContent = k === 'all' ? all.length : all.filter((row) => shelf(row) === k).length;
    const find = $('ppl-find').value.trim().toLowerCase();
    const only = $('ppl-role-filter').value;
    const week = $('ppl-week').checked;
    list = all.filter((row) => (browse === 'all' || shelf(row) === browse)
      && (source === 'any' || (source === 'local' ? row.kind === 'local' || row.kind === 'windows' || row.source === 'windows' : !['local', 'windows'].includes(row.kind) && row.source === 'ad'))
      && (!only || row.roles.some((x) => x.role.id === only))
      && (!week || (row.seen && Date.now() - new Date(row.seen) < 7 * 86400000))
      && (!find || [nameOf(row), row.name, row.sub, ...row.roles.map((x) => x.role.name)].some((t) => String(t).toLowerCase().includes(find))));
    const sort = $('ppl-sort').value;
    list.sort((a, b) => (sort === 'name' ? 0 : sort === 'seen' ? new Date(b.seen || 0) - new Date(a.seen || 0) : rank(a) - rank(b))
      || nameOf(a).localeCompare(nameOf(b)));
    $('ppl-title').textContent = BROWSE[browse];
    $('ppl-cards').hidden = browse !== 'all';
    $('ppl-cards-label').hidden = browse !== 'all';
    $('ppl-cards').innerHTML = cards();
    $('ppl-rows').innerHTML = list.length ? list.map((row, i) => `<div class="prow" data-row="${i}" tabindex="0"><div class="who">${pic(row)}<div><b>${esc(nameOf(row))}</b><span>${esc(row.sub)}</span></div></div>`
      + `<div class="chips-in">${row.roles.map((x) => chip(row, x)).join('')}${giveable(row).length ? `<button type="button" class="addrole" data-give aria-label="Give ${esc(nameOf(row))} a role">${svg('plus')}</button>` : ''}</div>`
      + `<div class="seen">${esc(row.seen ? since(row.seen) : '')}</div></div>`).join('')
      : '<div class="empty">Nobody here matches.</div>';
    document.querySelectorAll('.side a[data-browse]').forEach((a) => current(a, a.dataset.browse === browse));
    R.paint($('ppl-list'));
  };

  // --- The column on the left: browse, the roles in order (drag to move), and filters ------------------------------

  let dragging = null;
  const drawSide = (current) => {
    $('ppl-roles').innerHTML = data.roles.map((r) => {
      const movable = r.manage && !r.builtIn && mine('access.roles');
      return `<a class="role" href="#role/${encodeURIComponent(r.id)}" data-role="${esc(r.id)}" data-color="${esc(r.color)}"${movable ? ' draggable="true"' : ''}${r.id === current ? ' aria-current="page"' : ''}>`
        + `${movable ? svg('grip', 'grip') : '<span class="grip"></span>'}<i class="dot"></i><span class="nm">${esc(r.name)}</span>${r.id === 'admin' ? svg('lock', 'lk') : ''}<small>${r.members.length}</small></a>`;
    }).join('');
    const pick = $('ppl-role-filter');
    const was = pick.value;
    pick.innerHTML = '<option value="">Any role</option>' + data.roles.map((r) => `<option value="${esc(r.id)}">${esc(r.name)}</option>`).join('');
    pick.value = data.roles.some((r) => r.id === was) ? was : '';
    $('rail-roles').textContent = data.roles.length;
    R.paint($('ppl-roles'));
  };
  $('ppl-roles').addEventListener('dragstart', (e) => {
    dragging = e.target.closest('[draggable="true"]')?.dataset.role || null;
    if (dragging) e.dataTransfer.effectAllowed = 'move';
    else e.preventDefault();
  });
  $('ppl-roles').addEventListener('dragover', (e) => {
    const over = e.target.closest('[draggable="true"]');
    if (!dragging || !over || over.dataset.role === dragging) return;
    e.preventDefault();
    const below = e.clientY > over.getBoundingClientRect().top + over.offsetHeight / 2;
    document.querySelectorAll('#ppl-roles .drop-above, #ppl-roles .drop-below').forEach((a) => a.classList.remove('drop-above', 'drop-below'));
    over.classList.add(below ? 'drop-below' : 'drop-above');
  });
  $('ppl-roles').addEventListener('dragend', () => {
    dragging = null;
    document.querySelectorAll('#ppl-roles .drop-above, #ppl-roles .drop-below').forEach((a) => a.classList.remove('drop-above', 'drop-below'));
  });
  $('ppl-roles').addEventListener('drop', async (e) => {
    const over = e.target.closest('[draggable="true"]');
    if (!dragging || !over) return;
    e.preventDefault();
    const below = over.classList.contains('drop-below');
    const ids = data.roles.filter((r) => !r.builtIn).map((r) => r.id).filter((id) => id !== dragging);
    ids.splice(ids.indexOf(over.dataset.role) + (below ? 1 : 0), 0, dragging);
    dragging = null;
    try {
      await R.send('/api/roles/order', { ids });
      problem('');
    } catch (err) {
      problem(err.message);
    }
    refresh();
  });

  // --- A person's card, and giving or taking away roles ------------------------------------------------------------

  const closeCard = () => {
    $('pcard').hidden = true;
    $('rmenu').hidden = true;
  };
  const place = (box, near, dx = 0) => R.place(box, near, $('page-people'), dx);
  const allowedOf = (row) => {
    if (row.roles.some((x) => x.role.id === 'admin')) return null; // everything
    return new Set(row.roles.flatMap((x) => x.role.permissions));
  };
  const openCard = (i, el) => {
    const row = list[i];
    const can = allowedOf(row);
    const does = can === null ? '<li><b>Everything</b><span>Administrator allows it all.</span></li>'
      : data.catalog.map((p) => {
        const on = p.actions.filter((a) => can.has(a.key));
        return on.length ? `<li><b>${esc(p.name)}</b><span>${esc(on.map((a) => a.name.charAt(0).toLowerCase() + a.name.slice(1)).join(', '))}</span></li>` : '';
      }).join('') || '<li><span>Nothing yet.</span></li>';
    const acts = row.kind === 'local' && mine('access.local')
      ? `<div class="pcard-acts"><button type="button" class="small" data-password>New password</button><button type="button" class="small danger" data-remove-account>Remove account</button></div>` : '';
    $('pcard').innerHTML = `<div class="pcard-head">${pic(row)}<div><b>${esc(nameOf(row))}</b><span>${esc(row.sub)}${row.seen ? ` · ${esc(since(row.seen).toLowerCase())}` : ''}</span></div>`
      + `<button type="button" class="icon" data-close aria-label="Close">${svg('x')}</button></div>`
      + `<h4>Roles</h4><div class="chips-in">${row.roles.map((x) => chip(row, x)).join('') || '<span class="muted">None</span>'}${giveable(row).length ? `<button type="button" class="addrole" data-give aria-label="Give a role">${svg('plus')}</button>` : ''}</div>`
      + `<h4>${row.kind === 'group' || row.kind === 'everyone' ? 'Its roles allow' : 'Can do'}</h4><ul class="does">${does}</ul>${acts}`;
    $('pcard').dataset.row = i;
    R.paint($('pcard'));
    place($('pcard'), el, 180);
  };
  const openMenu = (i, el) => {
    const row = list[i];
    $('rmenu').innerHTML = `<p>Give ${esc(nameOf(row))}</p>` + giveable(row).map((r) => `<button type="button" data-give-role="${esc(r.id)}" data-color="${esc(r.color)}"><i class="dot"></i>${esc(r.name)}</button>`).join('');
    $('rmenu').dataset.row = i;
    R.paint($('rmenu'));
    place($('rmenu'), el);
  };
  const giveRole = async (row, id) => {
    try {
      await R.send(`/api/roles/${encodeURIComponent(id)}/members`, { kind: asMember(row), name: row.name, display: row.display });
      problem('');
    } catch (err) {
      problem(err.message);
    }
    closeCard();
    refresh();
  };
  const TAKE_OUT = 'They lose what only this role allows. Their other roles stay.';
  const takeRole = (row, id) => {
    const r = role(id);
    sure(`Take ${nameOf(row)} out of ${r.name}?`, TAKE_OUT, 'Take out', async () => {
      try {
        await R.send(`/api/roles/${encodeURIComponent(id)}/members/remove`, { kind: asMember(row), name: row.name });
        problem('');
      } catch (err) {
        problem(err.message);
      }
      closeCard();
      refresh();
    });
  };
  const onRowClick = (e, box) => {
    const host = e.target.closest('[data-row]');
    if (!host) return;
    const i = Number(host.dataset.row);
    const row = list[i];
    const take = e.target.closest('[data-take]');
    if (take) return takeRole(row, take.dataset.take);
    if (e.target.closest('[data-give]')) return openMenu(i, e.target.closest('[data-give]'));
    if (box === 'rows') openCard(i, host);
  };
  $('ppl-rows').addEventListener('click', (e) => onRowClick(e, 'rows'));
  $('ppl-rows').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-row]')) openCard(Number(e.target.dataset.row), e.target);
  });
  $('pcard').addEventListener('click', (e) => {
    const row = list[Number($('pcard').dataset.row)];
    if (e.target.closest('[data-close]')) return closeCard();
    const take = e.target.closest('[data-take]');
    if (take) return takeRole(row, take.dataset.take);
    if (e.target.closest('[data-give]')) return openMenu(Number($('pcard').dataset.row), e.target.closest('[data-give]'));
    if (e.target.closest('[data-password]')) {
      $('password-lead').textContent = `For the local account ${row.name}. It's signed out everywhere.`;
      $('pw-new').value = '';
      $('pw-problem').hidden = true;
      $('password-dialog').dataset.user = row.name;
      $('password-dialog').showModal();
    } else if (e.target.closest('[data-remove-account]')) {
      sure(`Remove the local account ${row.name}?`, "It's signed out straight away.", 'Remove account', async () => {
        try {
          await R.send(`/api/accounts/${encodeURIComponent(row.name)}/remove`);
          problem('');
        } catch (err) {
          problem(err.message);
        }
        closeCard();
        refresh();
      });
    }
  });
  $('rmenu').addEventListener('click', (e) => {
    const b = e.target.closest('[data-give-role]');
    if (b) giveRole(list[Number($('rmenu').dataset.row)], b.dataset.giveRole);
  });
  R.popups('#pcard, #rmenu, [data-row], [data-give]', closeCard);
  $('pw-cancel').addEventListener('click', () => $('password-dialog').close());
  $('pw-save').addEventListener('click', async () => {
    try {
      await R.send(`/api/accounts/${encodeURIComponent($('password-dialog').dataset.user)}/password`, { password: $('pw-new').value });
      $('password-dialog').close();
    } catch (err) {
      say('pw-problem', err.message);
    }
  });

  // --- One role: Display, Permissions and Members ------------------------------------------------------------------

  const editable = (r) => r.manage && mine('access.roles') && r.id !== 'admin';
  const dirty = (r) => draft && draft.id === r.id && (draft.name !== r.name || draft.about !== (r.about || '') || draft.color !== r.color
    || draft.permissions.size !== r.permissions.length || r.permissions.some((p) => !draft.permissions.has(p)));
  const fresh = (r) => ({ id: r.id, name: r.name, about: r.about || '', color: r.color, permissions: new Set(r.permissions) });
  const unsaved = (r) => { $('role-unsaved').hidden = !dirty(r); };
  const placeText = (r) => {
    const i = data.roles.indexOf(r);
    if (r.id === 'admin') return 'Administrator is always at the top, and allows everything.';
    if (r.id === 'user') return 'User is always at the bottom. Everyone who signs in has it, whatever else they have.';
    return `Below ${data.roles[i - 1].name} and above ${data.roles[i + 1].name}. Drag roles in the list on the left to move them. People can only change roles below their own highest one.`;
  };

  const drawRole = (r, tab) => {
    if (!draft || draft.id !== r.id) draft = fresh(r);
    const i = data.roles.indexOf(r);
    const total = allParts().length;
    const count = r.id === 'admin' ? total : draft.permissions.size;
    const where = r.id === 'admin' ? 'always at the top' : r.id === 'user' ? 'always at the bottom' : `below ${data.roles[i - 1].name}, above ${data.roles[i + 1].name}`;
    $('role-view').style.setProperty('--c', draft.color);
    $('role-name').textContent = draft.name || 'Untitled role';
    $('role-line').textContent = `${r.members.length} ${r.members.length === 1 ? 'member' : 'members'} · ${count} of ${total} permissions · ${where}`;
    $('role-viewas-text').textContent = `See ${R.lab()} as ${r.name}`;
    $('role-count').textContent = r.members.length;
    $('role-tabs').querySelectorAll('[data-tab]').forEach((a) => {
      a.href = `#role/${encodeURIComponent(r.id)}/${a.dataset.tab}`;
      current(a, a.dataset.tab === tab);
    });
    document.querySelectorAll('#role-view [data-pane]').forEach((p) => { p.hidden = p.dataset.pane !== tab; });
    const note = r.id === 'admin' ? "Administrator always allows everything, so only who's in it can change."
      : !r.manage ? `Only someone above ${r.name} can change it.` : !mine('access.roles') ? "Your roles don't let you change roles." : '';
    $('role-note').hidden = !note || tab === 'members';
    $('role-note').textContent = note;
    const can = editable(r);

    // Display: name, what it's for, colour, how its chip looks, where it sits.
    $('role-edit-name').value = draft.name;
    $('role-edit-name').disabled = !can || r.id === 'user';
    $('role-edit-about').value = draft.about;
    $('role-edit-about').disabled = !can;
    $('role-swatches').innerHTML = SWATCHES.map((c) => `<button type="button" role="radio" data-swatch="${c}" data-color="${c}" aria-label="${c}" aria-checked="${same(c, draft.color)}"${can ? '' : ' disabled'}></button>`).join('');
    const group = r.members.find((m) => m.kind === 'group')?.name || 'a group';
    $('role-preview').innerHTML = `<span class="rchip" data-color="${esc(draft.color)}"><i class="dot"></i>${esc(draft.name || 'Untitled role')}</span>`
      + `<span class="rchip via" data-color="${esc(draft.color)}"><i class="dot"></i>${esc(draft.name || 'Untitled role')}<small>${svg('users')}${esc(group)}</small></span>`
      + '<span class="hint">Dashed: from a group.</span>';
    $('role-where').textContent = placeText(r);
    $('role-delete-row').hidden = r.builtIn || !can;
    $('role-delete-title').textContent = `Delete ${r.name}`;

    // Permissions: a switch for each thing, by part. Nobody gives what their own roles don't allow.
    const find = $('perm-find').value.trim().toLowerCase();
    $('perm-list').innerHTML = data.catalog.map((p) => {
      const acts = p.actions.filter((a) => !find || `${p.name} ${a.name} ${a.about}`.toLowerCase().includes(find));
      if (!acts.length) return '';
      const on = p.actions.filter((a) => r.id === 'admin' || draft.permissions.has(a.key)).length;
      return `<div class="sec">${svg(p.icon)}<b>${esc(p.name)}</b><span>${on} of ${p.actions.length}</span></div><div class="rows">`
        + acts.map((a) => {
          const locked = !can || !mine(a.key);
          return `<label class="row still perm${locked ? ' locked' : ''}"><span class="t"><b>${esc(a.name)}</b><span>${esc(a.about)}${can && !mine(a.key) ? " Your roles don't allow this, so you can't give it." : ''}</span></span>`
            + `<input type="checkbox" class="switch" role="switch" data-perm="${esc(a.key)}" aria-label="${esc(a.name)}"${r.id === 'admin' || draft.permissions.has(a.key) ? ' checked' : ''}${locked ? ' disabled' : ''}></label>`;
        }).join('') + '</div>';
    }).join('') || '<div class="empty">No permission matches.</div>';
    $('perm-clear').hidden = !can;

    // Members: who's in it, and adding more.
    const takeable = r.manage && mine('access.members');
    const built = r.id === 'admin' && data.windowsAdmins
      ? `<div class="row still"><span class="pic windows">${svg('monitor')}</span><span class="t"><b>Windows administrators</b><span>Of ${esc(data.machine)}: they sign in as a local account, or with .\\ before their name. Built in.</span></span></div>` : '';
    const everyone = r.id === 'user' ? `<div class="row still"><span class="pic">${svg('users')}</span><span class="t"><b>Everyone signed in</b><span>Everyone has User, whatever's below. Groups and people below can sign in with just User.</span></span></div>` : '';
    $('member-list').innerHTML = everyone + built + (r.members.map((m) => `<div class="row still"><span class="pic ${m.kind}">${m.kind === 'user' ? esc(initials(m.display || m.name)) : svg(ICON[m.kind])}</span>`
      + `<span class="t"><b>${esc(memberLabel(m))}</b><span>${esc(KIND[m.kind])}${m.kind === 'user' && m.display ? ` · ${esc(m.name)}` : ''}${m.addedBy ? ` · added by ${esc(m.addedBy)}, ${esc(R.when(m.added))}` : ''}</span></span>`
      + (takeable && !(r.id === 'admin' && r.members.length === 1) ? `<button type="button" class="icon" data-take-member="${esc(m.kind)}" data-name="${esc(m.name)}" aria-label="Take ${esc(memberLabel(m))} out">${svg('x')}</button>` : '')
      + '</div>').join('') || (built || everyone ? '' : '<div class="empty">Nobody yet.</div>'));
    $('member-add').hidden = !takeable;

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
    try {
      await R.send(`/api/roles/${encodeURIComponent(draft.id)}`, { name: draft.name, about: draft.about, color: draft.color, permissions: [...draft.permissions] });
      draft = null;
      problem('');
    } catch (err) {
      problem(err.message);
      return;
    }
    refresh();
  });
  $('role-delete').addEventListener('click', () => {
    const r = role(draft.id);
    sure(`Delete ${r.name}?`, 'Everyone in it loses what it gives them.', 'Delete role', async () => {
      try {
        await R.send(`/api/roles/${encodeURIComponent(r.id)}/delete`);
        draft = null;
        await refresh(); // the list is drawn from what's loaded, so it's read before going back
        location.hash = '#people';
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
  $('member-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-take-member]');
    if (!b) return;
    const r = role(draft.id);
    const member = { kind: b.dataset.takeMember, name: b.dataset.name };
    sure(`Take ${b.closest('.row').querySelector('b').textContent} out of ${r.name}?`, TAKE_OUT, 'Take out', async () => {
      try {
        await R.send(`/api/roles/${encodeURIComponent(r.id)}/members/remove`, member);
        problem('');
      } catch (err) {
        problem(err.message);
      }
      refresh();
    });
  });

  // Searching groups, people and local accounts, for a role's Members and for Add people.
  const search = async (text) => {
    const found = await R.get(`/api/roles/search?q=${encodeURIComponent(text)}`);
    // When the domain can't be searched, what's typed can still be added as a group or a person.
    if (found.problem && text) {
      found.entries.push({ kind: 'group', name: text, display: null, description: `Add the group ${text} as typed` },
        { kind: 'user', name: text, display: null, description: `Add the account ${text} as typed` });
    }
    return found;
  };
  const entry = (e, attrs) => R.option(attrs, `<span class="pic ${e.kind}">${e.kind === 'user' && e.display ? esc(initials(e.display)) : svg(ICON[e.kind])}</span>`,
    e.display || e.name, e.kind === 'user' && e.display ? `${e.name}${e.description ? ` · ${e.description}` : ''}` : e.description || KIND[e.kind]);
  // A role's Members: who to add, leaving out who's in it already.
  R.dropdown($('member-find').closest('.adder'), {
    find: async (text) => {
      const found = await search(text);
      const r = role(draft.id);
      return { items: found.entries.filter((e) => !r.members.some((m) => m.kind === e.kind && same(m.name, e.name))), note: found.problem };
    },
    draw: (e, i) => entry(e, ` data-pick="${i}"`),
    pick: async (x) => {
      try {
        await R.send(`/api/roles/${encodeURIComponent(draft.id)}/members`, { kind: x.kind, name: x.name, display: x.display });
        problem('');
      } catch (err) {
        problem(err.message);
      }
      refresh();
    },
  });

  // New role: made just above User, then opened on Display to name it.
  $('role-new').addEventListener('click', async () => {
    let n = 1;
    while (data.roles.some((r) => same(r.name, n === 1 ? 'New role' : `New role ${n}`))) n++;
    try {
      const made = await R.send('/api/roles', { name: n === 1 ? 'New role' : `New role ${n}`, color: SWATCHES[(data.roles.length * 3) % SWATCHES.length] });
      draft = null;
      await refresh(); // so the new role is there to open
      location.hash = `#role/${encodeURIComponent(made.id)}/display`;
    } catch (err) {
      problem(err.message);
    }
  });

  // --- Add people: someone found, or a new local account, into roles ----------------------------------------------

  let picked = null;
  let pdFound = [];
  let pdTimer = null;
  const pdTab = (tab) => {
    $('people-tabs').querySelectorAll('[data-ptab]').forEach((a) => current(a, a.dataset.ptab === tab));
    document.querySelectorAll('#people-dialog [data-ppane]').forEach((p) => { p.hidden = p.dataset.ppane !== tab; });
    $('pd-go').textContent = tab === 'local' ? 'Make the account' : 'Add';
    $('people-dialog').dataset.tab = tab;
  };
  const pdList = async () => {
    try {
      const found = await search($('pd-find').value.trim());
      pdFound = found.entries;
      $('pd-results').innerHTML = (found.problem ? `<p class="drop-note">${esc(found.problem)}</p>` : '')
        + (pdFound.map((e, i) => entry(e, ` data-entry="${i}" aria-pressed="${!!picked && picked.kind === e.kind && same(picked.name, e.name)}"`)).join('')
          || '<p class="drop-note">Nothing found.</p>');
    } catch (err) {
      say('pd-problem', err.message);
    }
  };
  $('ppl-add').addEventListener('click', () => {
    picked = null;
    $('pd-find').value = '';
    $('pd-user').value = '';
    $('pd-password').value = '';
    $('pd-problem').hidden = true;
    $('pd-roles').innerHTML = data.roles.filter((r) => r.manage).map((r) => `<label class="role-pick" data-color="${esc(r.color)}"><input type="checkbox" value="${esc(r.id)}"${r.id === 'user' ? ' checked' : ''}><i class="dot"></i>${esc(r.name)}</label>`).join('');
    R.paint($('pd-roles'));
    const tabs = $('people-tabs').querySelectorAll('[data-ptab]');
    tabs.forEach((a) => a.classList.toggle('cant', !mine(a.dataset.ptab === 'local' ? 'access.local' : 'access.members')));
    pdTab(mine('access.members') ? 'find' : 'local');
    $('pd-roles').hidden = !mine('access.members');
    $('pd-roles').previousElementSibling.hidden = !mine('access.members');
    $('people-dialog').showModal();
    if (mine('access.members')) pdList();
  });
  $('people-tabs').addEventListener('click', (e) => {
    const a = e.target.closest('[data-ptab]');
    if (a) pdTab(a.dataset.ptab);
  });
  $('pd-find').addEventListener('input', () => {
    clearTimeout(pdTimer);
    pdTimer = setTimeout(pdList, 250);
  });
  $('pd-results').addEventListener('click', (e) => {
    const b = e.target.closest('[data-entry]');
    if (!b) return;
    picked = pdFound[Number(b.dataset.entry)];
    $('pd-results').querySelectorAll('[data-entry]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
  });
  $('pd-cancel').addEventListener('click', () => $('people-dialog').close());
  $('pd-go').addEventListener('click', async () => {
    const roles = [...$('pd-roles').querySelectorAll('input:checked')].map((c) => c.value);
    const tell = (text) => say('pd-problem', text);
    try {
      if ($('people-dialog').dataset.tab === 'local') {
        await R.send('/api/accounts', { username: $('pd-user').value, password: $('pd-password').value, roles: mine('access.members') ? roles : [] });
      } else {
        if (!picked) return tell('Pick a group, a person or a local account first.');
        if (!roles.length) return tell('Pick at least one role.');
        for (const id of roles) {
          // Local accounts have User already.
          if (!(picked.kind === 'local' && id === 'user')) await R.send(`/api/roles/${encodeURIComponent(id)}/members`, { kind: picked.kind, name: picked.name, display: picked.display });
        }
      }
      $('people-dialog').close();
      refresh();
    } catch (err) {
      tell(err.message);
      refresh();
    }
  });

  // --- Showing the page --------------------------------------------------------------------------------------------

  ['ppl-find', 'ppl-role-filter', 'ppl-week', 'ppl-sort'].forEach((id) => $(id).addEventListener(id === 'ppl-find' ? 'input' : 'change', () => route()));
  $('ppl-source').addEventListener('click', (e) => {
    const b = e.target.closest('[data-source]');
    if (!b) return;
    source = b.dataset.source;
    $('ppl-source').querySelectorAll('[data-source]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    route();
  });

  // Draws what the address asks for, from what's loaded.
  const route = () => {
    const [pageName, kind, rest] = R.where();
    if (pageName !== 'people' || !data) return;
    closeCard();
    const r = kind === 'role' ? role(decodeURIComponent(rest[0] || '')) : null;
    $('ppl-list').hidden = !!r;
    $('role-view').hidden = !r;
    if (!r) $('role-unsaved').hidden = true;
    drawSide(r?.id);
    if (r) drawRole(r, ['display', 'permissions', 'members'].includes(rest[1]) ? rest[1] : 'permissions');
    else drawList(BROWSE[rest[0]] ? rest[0] : 'all');
  };
  // Loads again, then draws.
  const refresh = async () => {
    try {
      data = await R.get('/api/roles');
      problem('');
    } catch (err) {
      problem(err.message);
      R.skeleton($('ppl-rows'), '');
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
    here = e.detail[0] === 'people' && R.has('access.view');
    if (!here) $('role-unsaved').hidden = true;
    else if (was && data) route();
    else refresh();
  });
  // The count in the rail, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => {
    if (R.has('access.view') && !here) refresh();
  });
})();
