// Links > Admin portals (#links/portals): the catalogue (portal-catalog.js) laid out like Active Directory, its groups down
// the left and its portals in a grid; the star adds one as a link only you see. And Add from the catalogue, the window behind
// Add a link's arrow: groups, search and ticks, then the group they go in and who sees them. links.js shares R.links.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can, colour, letter, count: plural, section: sec, strip, closeX } = R;
  const CAT = R.portalCatalog || [];
  const ALL = CAT.flatMap((g) => g.portals);
  const NEW = ' new'; // Add to: a new Admin portals group
  const PORTALS = 'Admin portals';
  const groupName = (id) => CAT.find((g) => g.id === id)?.name || '';
  const host = (url) => { try { return new URL(url).host; } catch { return url; } };
  const maker = (p) => (p.group === 'community' ? 'Community' : /apple\.com/.test(p.url) ? 'Apple' : /google\.com/.test(p.url) ? 'Google' : /samsung/.test(p.url) ? 'Samsung' : 'Microsoft');
  const face = (p, cls = 'lx-ic') => `<span class="${cls} letter" data-color="${colour(p.name)}"><b>${letter(p.name)}</b></span>`;
  const now = () => R.links?.now() || { links: [], groups: [], roles: [] };
  // The link a portal is in Links as, for this person: theirs, or one for everyone or a role they see.
  const linkOf = (p) => now().links.find((l) => l.catalog === p.key) || null;

  // --- The tab: groups down the left, the portals in a grid, a filter by publisher ------------------------------------

  let g = 'all';
  let makerF = '';
  const picked = new Set();
  const shown = () => {
    const q = $('lp-find').value.trim().toLowerCase();
    return ALL.filter((p) => (g === 'all' || (g === 'mine' ? !!linkOf(p) : p.group === g)) && (!makerF || maker(p) === makerF)
      && (!q || `${p.name} ${p.what} ${host(p.url)} ${groupName(p.group)}`.toLowerCase().includes(q)));
  };
  // The star: added for you alone with Add links only you see (or for everyone with Manage links alone); a portal someone
  // added for everyone stays on, since taking it away is Edit's job.
  const starBtn = (p) => {
    const l = linkOf(p);
    const tip = l ? (l.owner ? 'Remove from your links' : 'In Links for everyone. Change it from Your links.') : can('links.own') || can('links.edit') ? 'Add to your links' : 'Your roles do not include adding links.';
    return `<button type="button" class="icon lp-star${l ? ' on' : ''}" data-lp-star="${esc(p.key)}" data-tip="${esc(tip)}" aria-pressed="${!!l}" aria-label="${esc(`${l ? 'Remove' : 'Add'} ${p.name}`)}"${l && !l.owner ? ' aria-disabled="true"' : ''}>${svg('star')}</button>`;
  };
  const grid = R.grid({
    el: $('lp-table'), key: 'portals', sort: { key: 'name', dir: 1 },
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-lp-all aria-label="Select all"></label>',
      cell: (p) => `<label class="tk"><input type="checkbox" data-lp-tick="${esc(p.key)}" aria-label="${esc(`Select ${p.name}`)}"${picked.has(p.key) ? ' checked' : ''}></label>` },
    cols: [
      { key: 'star', label: 'In your links', w: 110, on: true, get: (p) => (linkOf(p) ? 0 : 1), text: (p) => (linkOf(p) ? 'Yes' : 'No'), cell: starBtn },
      { key: 'name', label: 'Portal', w: 280, fixed: true, get: (p) => p.name, cell: (p) => `<div class="who">${face(p)}<b class="ell">${esc(p.name)}</b></div>` },
      { key: 'what', label: 'Description', w: 380, on: true, get: (p) => p.what },
      { key: 'host', label: 'Address', w: 230, on: true, get: (p) => host(p.url), cell: (p) => `<span class="mono ell">${esc(host(p.url))}</span>` },
      { key: 'group', label: 'Group', w: 180, on: true, get: (p) => CAT.findIndex((x) => x.id === p.group), text: (p) => groupName(p.group), cell: (p) => `<span class="ell">${esc(groupName(p.group))}</span>` },
      { key: 'maker', label: 'Publisher', w: 120, on: true, get: maker },
    ],
    row: (p) => ` data-lp-row="${esc(p.key)}" tabindex="0"`, rowClass: (p) => `${picked.has(p.key) ? 'picked' : ''}${R.panel.shows('portal', p.key) ? ' dw-on' : ''}`,
    empty: () => 'No portals match.', redraw: () => drawList(), picked: () => picked.size,
  });
  const drawNav = () => {
    const a = (k, label, icon, n) => `<a href="#links/portals" data-lp-g="${k}"${g === k ? ' aria-current="page"' : ''}>${svg(icon)}<span>${esc(label)}</span><small>${n}</small></a>`;
    $('lp-nav').innerHTML = `<p class="rail-label">Browse</p>${a('mine', 'In your links', 'star', ALL.filter(linkOf).length)}${a('all', 'All portals', 'globe', ALL.length)}`
      + `<p class="rail-label">Groups</p><div class="dkids lp-kids">${CAT.map((x) => `<a href="#links/portals" data-lp-g="${x.id}"${g === x.id ? ' aria-current="page"' : ''}><span class="nm">${esc(x.name)}</span><small>${x.portals.length}</small></a>`).join('')}</div>`;
  };
  const drawTicks = () => {
    $('lp-table').querySelectorAll('[data-lp-tick]').forEach((b) => { b.checked = picked.has(b.dataset.lpTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const n = picked.size;
    const act = $('lp-act');
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n;
    if (n) delete act.dataset.tip;
    else act.dataset.tip = 'Select portals first';
  };
  const drawList = () => {
    const list = shown();
    [...picked].forEach((k) => { if (!list.some((p) => p.key === k)) picked.delete(k); });
    grid.draw(list);
    grid.tools($('lp-tools'), $('lp-rows'));
    R.paint($('lp-table'));
    drawTicks();
  };
  const drawAll = () => {
    $('ln-tab-portals').textContent = ALL.length;
    $('lp-maker').classList.toggle('set', !!makerF);
    $('lp-maker-v').hidden = !makerF;
    $('lp-maker-v').textContent = makerF;
    drawNav();
    drawList();
    if (R.panel.is('portal')) openPortal(ALL.find((p) => p.key === R.panel.key), true);
  };

  // Adding portals as links: one by one, so a refusal names the one it was. Who: 'all', 'me', or a role's id.
  const addLinks = async (list, group, who) => {
    let done = 0;
    for (const p of list) {
      await R.send('/api/links', { url: p.url, name: p.name.slice(0, 60), description: p.what.slice(0, 200), group, catalog: p.key,
        who: who === 'all' || who === 'me' ? who : 'roles', roles: who === 'all' || who === 'me' ? null : [who] });
      done += 1;
    }
    return done;
  };
  const star = async (p) => {
    const l = linkOf(p);
    try {
      if (l?.owner) {
        await R.send(`/api/links/${encodeURIComponent(l.id)}/delete`, {});
        R.toast(`${p.name} removed from your links.`, '');
      } else if (!l) {
        // Yours alone when you may; a group that's there already, or none, so nobody else gets an empty group.
        const mine = can('links.own');
        if (!mine && !can('links.edit')) return;
        await addLinks([p], now().groups.includes(PORTALS) ? PORTALS : mine ? '' : PORTALS, mine ? 'me' : 'all');
        R.toast(`${p.name} added to your links.`, mine ? 'Only you see it.' : 'Everyone sees it.', { action: { label: 'Show', run: () => { location.hash = '#links'; } } });
      }
    } catch (err) {
      R.toast('Could not change your links.', err.message, { tone: 'warn' });
    }
    await R.links?.refresh();
    drawAll();
  };

  // --- One portal in the side panel ---------------------------------------------------------------------------------

  const openPortal = (p, again = false) => {
    if (!p) { R.panel.close(); return; }
    const l = linkOf(p);
    R.panel.body.innerHTML = `<div class="dw-head">${face(p, 'pic dw-pic lp-pic')}<div class="dw-name"><h2 id="dw-title">${esc(p.name)}</h2><p>${esc(host(p.url))}</p></div>`
      + `${closeX}</div>`
      + `<div class="dw-bar"><a class="dw-qa" href="${esc(p.url)}" target="_blank" rel="noopener noreferrer">${svg('out')}<span>Open</span></a>`
      + (!l && (can('links.own') || can('links.edit')) ? `<button type="button" class="dw-qa" data-lp-star="${esc(p.key)}">${svg('star')}<span>Add to your links</span></button>` : '')
      + (l?.owner ? `<button type="button" class="dw-qa" data-lp-star="${esc(p.key)}">${svg('star')}<span>Remove from your links</span></button>` : '')
      + `<button type="button" class="dw-qa" data-lp-copy="${esc(p.url)}">${svg('copy')}<span>Copy link</span></button></div>`
      + strip([['Group', esc(groupName(p.group))], ['Publisher', esc(maker(p))], ['In your links', l ? (l.owner ? 'Yes, only you' : 'Yes') : 'No']])
      + sec('Description', `<p class="dw-how">${esc(p.what)}</p>`)
      + sec('Address', `<p class="dw-how"><span class="mono">${esc(p.url)}</span></p>`)
      + sec('Checks', `<p class="dw-how">${esc(l ? 'Checked every minute like any other link, while you have it showing.' : 'Not checked. Once it is in your links, it is checked like any other link.')}</p>`);
    R.paint(R.panel.body);
    if (!again) R.panel.open('portal', p.key);
    $('lp-table').querySelectorAll('[data-lp-row]').forEach((el) => el.classList.toggle('dw-on', R.panel.shows('portal', el.dataset.lpRow)));
  };
  R.panel.el.addEventListener('close', () => $('lp-table').querySelectorAll('.dw-on').forEach((el) => el.classList.remove('dw-on')));
  R.panel.el.addEventListener('click', (e) => {
    if (!R.panel.is('portal')) return;
    const s = e.target.closest('[data-lp-star]');
    if (s) { star(ALL.find((p) => p.key === s.dataset.lpStar)); return; }
    const c = e.target.closest('[data-lp-copy]');
    if (c) navigator.clipboard?.writeText(c.dataset.lpCopy).then(() => R.toast('Link copied.', ''), () => R.toast('Could not copy the link.', '', { tone: 'warn' }));
  });

  // --- The tab's clicks -------------------------------------------------------------------------------------------------

  $('lp-nav').addEventListener('click', (e) => {
    const a = e.target.closest('[data-lp-g]');
    if (!a) return;
    e.preventDefault();
    g = a.dataset.lpG;
    drawAll();
  });
  $('lp-find').addEventListener('input', () => drawList());
  $('lp-maker').addEventListener('click', () => {
    const n = new Map();
    ALL.forEach((p) => n.set(maker(p), (n.get(maker(p)) || 0) + 1));
    R.actions.open($('lp-maker'), {
      items: [{ act: '', label: 'Any publisher' }, '-', ...[...n].map(([v, c]) => ({ act: v, label: v, hint: String(c) }))],
      pick: (act) => { makerF = act; drawAll(); },
    });
  });
  $('lp-table').addEventListener('click', (e) => {
    const s = e.target.closest('[data-lp-star]');
    if (s) { if (s.getAttribute('aria-disabled') !== 'true') star(ALL.find((p) => p.key === s.dataset.lpStar)); return; }
    if (e.target.closest('.tk, button, a, input')) return;
    const row = e.target.closest('[data-lp-row]');
    if (!row) return;
    if (R.panel.shows('portal', row.dataset.lpRow)) R.panel.close();
    else openPortal(ALL.find((p) => p.key === row.dataset.lpRow));
  });
  $('lp-table').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('[data-lp-row]')) openPortal(ALL.find((p) => p.key === e.target.dataset.lpRow));
  });
  $('lp-table').addEventListener('change', (e) => {
    const t = e.target.closest('[data-lp-tick]');
    if (t) { if (t.checked) picked.add(t.dataset.lpTick); else picked.delete(t.dataset.lpTick); drawTicks(); return; }
    if (e.target.closest('[data-lp-all]')) { shown().forEach((p) => (e.target.checked ? picked.add(p.key) : picked.delete(p.key))); drawTicks(); }
  });
  $('lp-act').addEventListener('click', () => {
    const list = ALL.filter((p) => picked.has(p.key));
    const fresh = list.filter((p) => !linkOf(p));
    R.actions.open($('lp-act'), {
      items: [{ act: 'add', label: 'Add from the catalogue…', hint: String(fresh.length), disabled: !fresh.length || !(can('links.own') || can('links.edit')) },
        { act: 'open', label: `Open ${list.length > 1 ? `${list.length} in new tabs` : 'in a new tab'}` }, { act: 'copy', label: `Copy ${list.length > 1 ? `${list.length} links` : 'link'}` }, '-', { act: 'clear', label: 'Clear selection' }],
      pick: (act) => {
        if (act === 'add') openCatalogue(fresh.map((p) => p.key));
        else if (act === 'open') list.forEach((p) => window.open(p.url, '_blank', 'noopener'));
        else if (act === 'copy') navigator.clipboard?.writeText(list.map((p) => p.url).join('\n')).then(() => R.toast(`${plural(list.length, 'link')} copied.`, ''));
        else if (act === 'clear') { picked.clear(); drawList(); }
      },
    });
  });

  // --- Add from the catalogue ------------------------------------------------------------------------------------------

  const cat = { g: 'all', ticked: new Set() };
  const catShown = () => {
    const q = $('cat-find').value.trim().toLowerCase();
    const pool = cat.g === 'all' ? ALL : cat.g === 'sel' ? ALL.filter((p) => cat.ticked.has(p.key)) : ALL.filter((p) => p.group === cat.g);
    return q ? pool.filter((p) => `${p.name} ${p.what} ${host(p.url)}`.toLowerCase().includes(q)) : pool;
  };
  const drawCatGroups = () => {
    const n = (id) => ALL.filter((p) => p.group === id && cat.ticked.has(p.key)).length;
    const row = (id, label, count, ticked) => `<button type="button" data-cat-g="${id}"${cat.g === id ? ' aria-current="true"' : ''}><span>${esc(label)}</span>${ticked ? `<b>${ticked}</b>` : ''}<small>${count}</small></button>`;
    $('cat-groups').innerHTML = row('all', 'All portals', ALL.length, 0) + row('sel', 'Selected', cat.ticked.size, 0) + '<i></i>' + CAT.map((x) => row(x.id, x.name, x.portals.length, n(x.id))).join('');
  };
  const drawCatRows = () => {
    const list = catShown();
    const q = $('cat-find').value.trim();
    let last = '';
    $('cat-rows').innerHTML = list.map((p) => {
      const head = cat.g === 'all' && !q && p.group !== last ? `<p class="lp-ghead">${esc(groupName((last = p.group)))}</p>` : '';
      const had = !!linkOf(p);
      return `${head}<label class="pk-row lp-row${had ? ' lp-had' : ''}"><input type="checkbox" data-cat-tick="${esc(p.key)}"${had || cat.ticked.has(p.key) ? ' checked' : ''}${had ? ' disabled' : ''}>`
        + `${face(p)}<span class="lp-name">${esc(p.name)}</span><span class="lp-what">${esc(p.what)}</span>${had ? '<span class="lp-tag">In Links</span>' : `<code class="lp-host">${esc(host(p.url))}</code>`}</label>`;
    }).join('') || `<p class="lp-none">No portals match "${esc(q)}".</p>`;
    R.paint($('cat-rows'));
  };
  const catCount = () => {
    const n = cat.ticked.size;
    $('cat-n').textContent = n ? `${n} selected` : 'None selected';
    $('cat-go').disabled = !n;
    $('cat-go').textContent = n ? `Add ${plural(n, 'link')}` : 'Add links';
  };
  // Who sees them: Everyone and each role with Manage links, Only you with Add links only you see.
  const openCatalogue = (keys = []) => {
    if (!can('links.edit') && !can('links.own')) return;
    const { groups, roles } = now();
    cat.g = keys.length ? 'sel' : 'all';
    cat.ticked = new Set(keys.filter((k) => !linkOf(ALL.find((p) => p.key === k))));
    $('cat-find').value = '';
    $('cat-to').innerHTML = (can('links.edit') && !groups.includes(PORTALS) ? `<option value="${NEW}">New group: ${PORTALS}</option>` : '')
      + groups.map((x) => `<option value="${esc(x)}">${esc(x)}</option>`).join('') + '<option value="">No group</option>';
    $('cat-to').value = groups.includes(PORTALS) ? PORTALS : can('links.edit') ? NEW : '';
    $('cat-who').innerHTML = (can('links.edit') ? '<option value="all">Everyone</option>' + roles.map((r) => `<option value="${esc(r.id)}">${esc(r.name)}</option>`).join('') : '')
      + (can('links.own') ? '<option value="me">Only you</option>' : '');
    $('cat-who').value = can('links.edit') ? 'all' : 'me';
    say('cat-problem', '');
    drawCatGroups();
    drawCatRows();
    catCount();
    $('cat-dialog').showModal();
    $('cat-find').focus();
  };
  R.portals = { add: () => openCatalogue() };
  $('cat-groups').addEventListener('click', (e) => {
    const b = e.target.closest('[data-cat-g]');
    if (!b) return;
    cat.g = b.dataset.catG;
    drawCatGroups();
    drawCatRows();
  });
  $('cat-find').addEventListener('input', drawCatRows);
  $('cat-all').addEventListener('click', () => {
    catShown().forEach((p) => { if (!linkOf(p)) cat.ticked.add(p.key); });
    drawCatGroups();
    drawCatRows();
    catCount();
  });
  $('cat-rows').addEventListener('change', (e) => {
    const t = e.target.closest('[data-cat-tick]');
    if (!t) return;
    if (t.checked) cat.ticked.add(t.dataset.catTick);
    else cat.ticked.delete(t.dataset.catTick);
    drawCatGroups();
    catCount();
  });
  $('cat-cancel').addEventListener('click', () => $('cat-dialog').close());
  $('cat-go').addEventListener('click', async () => {
    const list = ALL.filter((p) => cat.ticked.has(p.key));
    const to = $('cat-to').value === NEW ? PORTALS : $('cat-to').value;
    const who = $('cat-who').value;
    $('cat-go').disabled = true;
    let done = 0;
    try {
      done = await addLinks(list, to, who);
      $('cat-dialog').close();
      const whom = who === 'all' ? 'Everyone sees them.' : who === 'me' ? 'Only you see them.' : `${now().roles.find((r) => r.id === who)?.name || 'That role'} sees them.`;
      R.toast(`${plural(done, 'link')} added${to ? ` to ${to}` : ''}.`, whom, { action: { label: 'Show', run: () => { location.hash = '#links'; } } });
    } catch (err) {
      say('cat-problem', `${done ? `Added ${done} of ${list.length}. ` : ''}${err.message}`);
      catCount();
    }
    await R.links?.refresh();
    if (R.where()[2]?.[0] === 'portals') drawAll();
  });

  // --- Which tab shows -------------------------------------------------------------------------------------------------

  // The links read again (here or by anyone): the stars follow.
  document.addEventListener('rabbidlab:links', () => { if (!$('ln-portals').hidden) drawAll(); });

  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'links') return;
    const tab = e.detail[2]?.[0] === 'portals' ? 'portals' : 'yours';
    $('ln-yours').hidden = tab !== 'yours';
    $('ln-portals').hidden = tab !== 'portals';
    $('ln-tabs').querySelectorAll('[data-ltab]').forEach((a) => (a.dataset.ltab === tab ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    if (tab !== 'portals' && R.panel.is('portal')) R.panel.close();
    $('ln-tab-portals').textContent = ALL.length;
    if (tab === 'portals') drawAll();
  });
})();
