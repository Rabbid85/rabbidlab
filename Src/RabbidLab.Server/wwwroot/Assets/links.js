// Links: #links, the places in the lab in groups, each with its icon, detail and address, and a dot for whether it
// answers. Adding one reads the site for its name, icon and IP. console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, has: can } = R;
  const COLOURS = R.palette.slice(0, 9); // the grey at the end is left to roles
  let links = [];
  let states = {};
  let checks = { every: 60, downAfter: 30 }; // seconds: between checks, and unanswered before a link shows as down
  let revision = 0; // goes up when the server changes links by itself; a new one means read them again
  let groups = []; // their names, in the order they are shown
  let group = '';
  let lost = ''; // the group that was picked when it went away, in case it was only renamed
  const NO_GROUP = ' none'; // the filter's choice of links with no group: no group's name starts with a space
  let editing = null; // the link being changed, or null for a new one
  let icon = null; // the dialog's icon: a data: URL, or null for a letter
  let looked = null; // what the site said, for From the site
  let lookedUrl = ''; // the address that was read, so it isn't read twice
  let ip = ''; // the IP shown on the tile: looked up from the address, never typed
  let lookRun = 0; // the newest read: an older one's answer is dropped
  let timer = null;
  const stored = (key) => { try { return localStorage.getItem(key); } catch { return null; } };
  let view = stored('rabbidlab.links.view') === 'grid' ? 'grid' : 'cards'; // tiles, or a row to a link

  const colour = (name) => COLOURS[[...String(name)].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7) % COLOURS.length];
  const letter = (name) => esc((String(name).match(/[A-Za-z0-9]/) || ['?'])[0].toUpperCase());
  const NO_ICON = 'none'; // a link's icon when it's to show none at all; with no icon saved it shows its first letter
  const own = (l) => !!l.icon && l.icon !== NO_ICON; // it has a picture
  const pic = (l) => (own(l) ? `<img src="${esc(l.icon)}" alt="">` : `<b>${letter(l.name)}</b>`);
  const host = (url) => {
    try {
      const u = new URL(url);
      return u.host;
    } catch {
      return url;
    }
  };

  // --- The tiles, by group ------------------------------------------------------------------------------------------

  const dot = (l) => {
    const s = states[l.id];
    // The grid says it in words beside the dot (data-now), and keeps the column where a link isn't checked.
    if (!/^https?:/i.test(l.url)) return view === 'grid' ? '<i class="lstate none"></i>' : '';
    if (!s) return '<i class="lstate" title="Checking…" data-now="Checking…"></i>';
    if (s.up) return `<i class="lstate up" title="Up · answered in ${s.ms} ms" data-now="${s.ms} ms"></i>`;
    const since = s.since ? new Date(s.since).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '';
    // Amber until it has been quiet for as long as the settings say.
    return s.down ? `<i class="lstate down" title="Down · no answer${since ? ` since ${since}` : ''}" data-now="Down"></i>`
      : `<i class="lstate late" title="No answer just now · checking again, down after ${span(checks.downAfter)}" data-now="No answer"></i>`;
  };
  const span = (s) => (s < 60 ? `${s} seconds` : s === 60 ? 'a minute' : `${s / 60} minutes`);
  const tile = (l) => `<div class="ltile${states[l.id]?.down ? ' down' : ''}" data-id="${esc(l.id)}"${can('links.edit') ? ` draggable="true" data-drag="${esc(l.id)}"` : ''}><a class="lgo" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer" draggable="false">`
    + (l.icon === NO_ICON ? (view === 'grid' ? '<span class="lic gap"></span>' : '') : `<span class="lic${l.icon ? '' : ' letter'}" data-color="${colour(l.name)}">${pic(l)}</span>`)
    + `<span class="lt"><b>${esc(l.name)}</b>${l.description ? `<span>${esc(l.description)}</span>` : ''}`
    + `<code>${esc(l.address && l.address !== host(l.url) ? `${l.address} · ${host(l.url)}` : host(l.url))}</code></span></a>`
    + `${dot(l)}${can('links.edit') ? `<button type="button" class="icon ledit" data-edit="${esc(l.id)}" aria-label="Change ${esc(l.name)}">${svg('edit')}</button>` : ''}</div>`;

  const draw = () => {
    const find = $('ln-find').value.trim().toLowerCase();
    // The group picked may be gone after a change: back to all of them.
    if (group && group !== NO_GROUP && !groups.includes(group)) {
      lost = group;
      group = '';
    }
    // The filter narrows the page to one group, so it only shows when that would leave something out.
    const narrows = groups.length > 1 || (groups.length === 1 && links.some((l) => l.group !== groups[0]));
    if (!narrows || (group === NO_GROUP && links.every((l) => l.group))) group = '';
    filter(narrows, group === NO_GROUP ? 'No group' : group);
    const only = group === NO_GROUP ? '' : group; // the one group shown, as the links have it
    const shown = links.filter((l) => (!group || (l.group || '') === only)
      && (!find || [l.name, l.description, l.url, l.address, l.group].some((v) => String(v || '').toLowerCase().includes(find))));
    // Every group in its order, the empty ones too unless something is being looked for; links with no group go last.
    const by = (group ? [only] : [...groups, '']).map((g) => [g, shown.filter((l) => (l.group || '') === g)])
      .filter(([g, list]) => list.length || (g && !find));
    $('ln-list').innerHTML = by.map(([g, list]) => `<section class="lgroup" data-name="${esc(g)}">${groups.length ? tools.heading(g, list.length) : ''}`
      + (list.length ? `<div class="ltiles${view === 'grid' ? ' lrows' : ''}">${list.map(tile).join('')}</div>`
        : `<p class="hint lnone">No links in this group yet.${can('links.edit') ? ` <button type="button" class="link" data-addto="${esc(g)}">Add one</button>` : ''}</p>`)
      + '</section>').join('')
      || (links.length ? '<p class="hint">Nothing matches.</p>' : '');
    if (!find) $('ln-list').insertAdjacentHTML('beforeend', tools.out(by.some(([g]) => !g)));
    tools.settle();
    $('ln-view').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.view === view));
    $('ln-empty').hidden = links.length > 0 || groups.length > 0;
    $('ln-empty-text').textContent = can('links.edit')
      ? 'Put the sites in your lab here: the router, the NAS, the hypervisor, iDRACs. Paste an address and Rabbid Lab fills in the name, icon and IP.'
      : 'Nobody has put any here yet.';
    R.paint($('ln-list'));
    $('rail-links').textContent = links.length || '';
    $('links-dot').hidden = !links.some((l) => states[l.id] && states[l.id].down);
    drawHero();
  };

  // --- The hero: how the links are doing, and a square for every link ----------------------------------------------

  const hash = (text) => [...String(text)].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7) / 4294967295;
  const at = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '');
  const checked = (l) => /^https?:/i.test(l.url);
  // plain is a link that isn't a web address, so it's never checked.
  const how = (l) => (!checked(l) ? 'plain' : !states[l.id] ? 'new' : states[l.id].up ? 'up' : states[l.id].down ? 'down' : 'warn');
  const HOW = { plain: 'Not checked', new: 'Checking…', up: 'Up', down: 'Down', warn: 'No answer just now' };
  const squares = new Map(); // a link's square, kept between draws so its picture isn't loaded again
  const drawSquares = () => {
    const { slots, size } = R.places(links.length);
    const seen = new Set();
    [...links].sort((a, b) => a.id.localeCompare(b.id)).forEach((l, i) => {
      if (i >= slots.length) return;
      seen.add(l.id);
      let el = squares.get(l.id);
      if (!el) {
        el = document.createElement('a');
        el.target = '_blank';
        el.rel = 'noopener noreferrer';
        $('ln-hero').prepend(el);
        squares.set(l.id, el);
      }
      const state = how(l);
      const r = hash(l.id);
      const side = Math.round(state === 'down' ? size * 1.4 : size * (0.95 + r * 0.3));
      const face = own(l) ? l.icon : letter(l.name); // a link with no icon still gets its letter up here
      if (el.dataset.face !== face) {
        el.dataset.face = face;
        el.innerHTML = pic(l);
      }
      el.href = l.url;
      el.className = `tile sen lsq s-${state}${size < 20 ? ' tiny' : ''}`;
      el.title = `${l.name} · ${HOW[state]}`;
      el.setAttribute('aria-label', el.title);
      el.style.setProperty('--g', own(l) ? '#1E2132' : colour(l.name));
      el.style.setProperty('--t', `${(2.6 + r * 2.4).toFixed(2)}s`);
      el.style.setProperty('--d', `-${(hash(`${l.id}d`) * 4).toFixed(2)}s`);
      Object.assign(el.style, { left: `${(slots[i][0] + r * 1.2).toFixed(2)}%`, top: `${(slots[i][1] + hash(`${l.id}y`) * 4).toFixed(2)}%`, width: `${side}px`, height: `${side}px`,
        rotate: `${state === 'down' ? 8 : Math.round(hash(`${l.id}r`) * 24 - 12)}deg` });
    });
    squares.forEach((el, id) => {
      if (seen.has(id)) return;
      el.remove();
      squares.delete(id);
    });
  };
  const EVERY_SHORT = { 15: '15 s', 30: '30 s', 60: '1 min', 300: '5 min' };
  const drawHero = () => {
    const web = links.filter(checked);
    const known = web.filter((l) => states[l.id]);
    const up = known.filter((l) => states[l.id].up);
    const down = known.filter((l) => states[l.id].down);
    const late = known.filter((l) => !states[l.id].up && !states[l.id].down);
    const first = down[0];
    $('ln-hero').classList.toggle('bad', down.length > 0);
    $('ln-lead').textContent = !links.length ? 'Nothing here yet.' : first ? `${first.name} stopped answering${states[first.id].since ? ` at ${at(states[first.id].since)}` : ''}.`
      : !web.length ? 'The places in your lab, one click away.' : late.length ? `${late[0].name} didn't answer just now.`
        : known.length < web.length ? 'The first check is under way.' : 'Everything is answering.';
    $('ln-lead-more').textContent = !links.length ? (can('links.edit') ? 'Add the sites in your lab: the router, the NAS, the hypervisor, iDRACs.' : 'Nobody has put any here yet.')
      : down.length > 1 ? `So did ${down.slice(1, 4).map((l) => l.name).join(', ')}${down.length > 4 ? ` and ${down.length - 4} more` : ''}.`
        : !first && late.length ? `Trying again. It shows as down after ${span(checks.downAfter)} with no answer.` : '';
    $('ln-count').textContent = links.length;
    $('ln-answering').textContent = !web.length ? '0' : known.length ? `${up.length} of ${web.length}` : '…';
    $('ln-every').textContent = EVERY_SHORT[checks.every] || `${checks.every} s`;
    $('ln-card').hidden = !first;
    if (first) {
      $('ln-card-title').textContent = first.name;
      $('ln-card-text').textContent = `No answer from ${host(first.url)}${states[first.id].since ? ` since ${at(states[first.id].since)}` : ''}.`;
      $('ln-card-more').href = first.url;
    }
    drawSquares();
  };

  // Asks again when the server says to: sooner while a link that didn't answer is being tried again.
  const check = async () => {
    let wait = 60;
    try {
      const answer = await R.get('/api/links/states');
      states = answer.states || {};
      wait = answer.next || 60;
      if (answer.revision !== revision && !tools.busy()) take(await R.get('/api/links'));
      // Not under a drag or a name being typed: the dots catch up on the next check.
      if (!tools.busy()) draw();
    } catch {
      // The dots stay as they were.
    }
    clearTimeout(timer);
    if (R.where()[0] === 'links') timer = setTimeout(check, wait * 1000);
  };
  // What the server answers to a read or a change: the links and the groups, both in order, and the check times.
  const take = (answer) => {
    links = answer.links;
    groups = answer.groups || [];
    checks = answer.checks || checks;
    if (answer.revision != null) revision = answer.revision;
  };
  const refresh = async () => {
    try {
      take(await R.get('/api/links'));
      say('ln-problem', '');
    } catch (err) {
      say('ln-problem', err.message);
    }
    draw();
    check();
  };

  $('ln-find').addEventListener('input', draw);
  // Cards or a grid, remembered in this browser.
  $('ln-view').addEventListener('click', (e) => {
    const b = e.target.closest('[data-view]');
    if (!b) return;
    view = b.dataset.view;
    try { localStorage.setItem('rabbidlab.links.view', view); } catch { /* not kept, then */ }
    draw();
  });

  // --- The filter: a menu of the groups, to show one of them (console.js has it, for Sensors too). ------------------
  const filter = R.groupFilter('ln', {
    rows: () => {
      const loose = links.filter((l) => !l.group).length;
      return [['', 'All groups', links.length], ...groups.map((g) => [g, g, links.filter((l) => l.group === g).length]), ...(loose ? [[NO_GROUP, 'No group', loose]] : [])];
    },
    picked: () => group,
    pick: (g) => {
      group = g;
      draw();
    },
  });
  // Groups: renamed where they stand, dragged into order, deleted (console.js has the tools Sensors uses too).
  const tools = R.groupTools($('ln-list'), {
    api: '/api/links/groups',
    thing: 'link',
    problem: 'ln-problem',
    can: () => can('links.edit'),
    names: () => groups,
    order: (names) => { groups = names; },
    count: (name) => links.filter((l) => l.group === name).length,
    // Open all: every link in the group, each in a tab of its own.
    also: (g, n) => (n > 1 ? `<button type="button" class="link lopen" data-openall="${esc(g)}" title="Open every link in this group, each in a new tab">Open all</button>` : ''),
    places: true, // links keep the order they're dragged into
    // A link dragged to another place: its group's links in their new order. Shown straight away, then saved.
    move: async (id, to, ids) => {
      const l = links.find((x) => x.id === id);
      if (!l) return draw();
      // With a search on, the page shows only part of the group: the link joins the end of all of it.
      if ($('ln-find').value.trim()) ids = [...links.filter((x) => (x.group || '') === to && x.id !== id).map((x) => x.id), id];
      l.group = to || null;
      links = [...links.filter((x) => !ids.includes(x.id)), ...ids.map((x) => links.find((y) => y.id === x)).filter(Boolean)];
      draw();
      try {
        take(await R.send('/api/links/arrange', { group: to, ids }));
        say('ln-problem', '');
      } catch (err) {
        say('ln-problem', err.message);
      }
      draw();
    },
    take,
    draw,
    // The group being looked at keeps being looked at under its new name.
    renamed: (name, to) => {
      if (lost !== name) return;
      group = to;
      draw();
    },
  });
  $('ln-newgroup').addEventListener('click', tools.create);
  // Open all: every link in the group in a tab of its own. A browser lets one click open one tab until pop-ups are
  // allowed for this site, so the ones it stopped are kept, with the steps to allow them and a button to open the rest.
  let waiting = []; // the addresses the browser stopped
  const openTabs = (urls) => urls.filter((url) => {
    const tab = window.open(url, '_blank');
    if (tab) tab.opener = null;
    return !tab;
  });
  const openAll = (urls, of) => {
    waiting = openTabs(urls);
    if (!waiting.length) {
      if ($('lopen-dialog').open) $('lopen-dialog').close();
      return;
    }
    $('lopen-title').textContent = `Your browser opened ${of - waiting.length} of ${of}`;
    $('lopen-site').textContent = $('lopen-site2').textContent = location.origin;
    $('lopen-go').textContent = $('lopen-again').textContent = waiting.length === 1 ? 'Open the last one' : `Open the other ${waiting.length}`;
    if (!$('lopen-dialog').open) $('lopen-dialog').showModal();
  };
  $('ln-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-openall]');
    if (!b) return;
    const urls = [...b.closest('.lgroup').querySelectorAll('.lgo')].map((a) => a.href);
    openAll(urls, urls.length);
  });
  $('lopen-go').addEventListener('click', () => openAll(waiting, waiting.length));
  $('lopen-close').addEventListener('click', () => $('lopen-dialog').close());

  // --- Checks: how often, and how long with no answer before a link shows as down. Saved as it's changed. ------------
  const EVERY = [[15, '15 seconds'], [30, '30 seconds'], [60, 'Minute'], [300, '5 minutes']];
  const AFTER = [[0, 'Right away'], [15, '15 seconds'], [30, '30 seconds'], [60, 'Minute'], [300, '5 minutes']];
  const drawChecks = () => {
    const segs = (items, value) => items.map(([v, text]) => `<button type="button" data-value="${v}" aria-pressed="${v === value}">${text}</button>`).join('');
    $('lc-every').innerHTML = segs(EVERY, checks.every);
    $('lc-after').innerHTML = segs(AFTER, checks.downAfter);
    $('lc-known').checked = checks.describe !== false;
    $('lc-note').textContent = checks.downAfter
      ? `A link that doesn't answer turns amber and is tried again every few seconds. It turns red once it has gone ${span(checks.downAfter)} with no answer, so one slow reply doesn't show as down.`
      : 'A link turns red the first time it doesn\'t answer, even if it was only one slow reply.';
  };
  const saveChecks = async (change) => {
    try {
      take(await R.send('/api/links/checks', change));
      say('lc-problem', '');
    } catch (err) {
      say('lc-problem', err.message);
    }
    drawChecks();
    check();
  };
  const picked = (e) => e.target.closest('button[data-value]')?.dataset.value;
  $('lc-every').addEventListener('click', (e) => { if (picked(e) != null) saveChecks({ every: Number(picked(e)) }); });
  $('lc-after').addEventListener('click', (e) => { if (picked(e) != null) saveChecks({ downAfter: Number(picked(e)) }); });
  // Turned on, the links here get their descriptions first, under the flask.
  $('lc-known').addEventListener('change', async () => {
    const on = $('lc-known').checked;
    const form = $('lcheck-dialog').querySelector('.form');
    if (on) R.busy(form, 'Describing the links…');
    await saveChecks({ describe: on });
    R.busy(form, '');
    draw();
  });
  $('lc-close').addEventListener('click', () => $('lcheck-dialog').close());
  $('lc-sensors').addEventListener('click', () => $('lcheck-dialog').close());
  // Import: a file Export made, added beside what's here. The flask covers the settings while it goes in.
  const count = (n, thing) => `${n} ${thing}${n === 1 ? '' : 's'}`;
  $('lc-import-file').addEventListener('change', async () => {
    const f = $('lc-import-file').files[0];
    $('lc-import-file').value = '';
    if (!f) return;
    say('lc-done', '');
    say('lc-problem', '');
    const form = $('lcheck-dialog').querySelector('.form');
    R.busy(form, `Importing ${f.name}…`);
    try {
      let data = null;
      try {
        data = JSON.parse(await f.text());
      } catch {
        throw new Error("That file isn't a Links export from Rabbid Lab.");
      }
      const done = await R.send('/api/links/import', data);
      take(done.now);
      const parts = [done.links || done.groups ? `Imported ${count(done.links, 'link')} and ${count(done.groups, 'group')}.` : 'No new links or groups in that file.',
        done.skipped ? `${count(done.skipped, 'link')} already here ${done.skipped === 1 ? 'was' : 'were'} left as ${done.skipped === 1 ? 'it is' : 'they are'}.` : '',
        done.refused ? `${count(done.refused, 'link')} couldn't be read from the file.` : '',
        done.checks ? 'The check times were set from the file.' : ''];
      say('lc-done', parts.filter(Boolean).join(' '));
    } catch (err) {
      say('lc-problem', err.message);
    }
    R.busy(form, '');
    drawChecks();
    draw();
    check();
  });
  $('ln-checks').addEventListener('click', () => {
    say('lc-done', '');
    say('lc-problem', '');
    drawChecks();
    $('lcheck-dialog').showModal();
  });
  $('ln-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-addto]');
    if (b) open(null, b.dataset.addto);
  });

  // --- Adding and changing one: the form beside the tile as it will look; a list is read, then ticked before adding ----

  const NEW_GROUP = ' new'; // the group lists' last choice: no group's name starts with a space
  const full = (url) => (/^[a-z][a-z0-9+.-]*:\/\//i.test(url.trim()) ? url.trim() : `http://${url.trim()}`);
  const addresses = (n) => (n === 1 ? '1 address' : `${n} addresses`);
  let siteIcon = null; // the icon the site serves, kept while another is picked
  let uploaded = false; // the icon is a picture of the person's own
  const iconKind = () => (icon === NO_ICON ? 'none' : !icon ? 'letter' : uploaded ? 'upload' : 'site');
  const ICON_SAID = { site: 'site', letter: 'letter', upload: 'your picture', none: 'none' };
  // The tile as it will look on the page, and the four icons to pick from in a strip under it. None keeps an empty space, so nothing moves.
  const drawIcon = () => {
    const url = $('ln-url').value.trim();
    const name = $('ln-name').value.trim() || (url ? host(full(url)) : '');
    const empty = !url && !name;
    const shown = name || 'Name';
    const k = iconKind();
    const face = k === 'none' ? '<span class="ld-slot"></span>' : k === 'letter' ? `<span class="lic letter" data-color="${colour(shown)}"><b>${letter(shown)}</b></span>`
      : `<span class="lic"><img src="${esc(icon)}" alt=""></span>`;
    const where = !url ? 'address' : ip && ip !== host(full(url)) ? `${ip} · ${host(full(url))}` : host(full(url));
    const pick = (kind, title, inner, off) => `<button type="button" data-icon="${kind}" aria-pressed="${k === kind}" title="${title}"${off ? ' disabled' : ''}>${inner}</button>`;
    $('ln-preview').innerHTML = `<div class="ltiles"><div class="ltile${empty ? ' ld-ghost' : ''}"><div class="lgo">${face}`
      + `<span class="lt"><b>${esc(shown)}</b><span>${esc($('ln-about').value.trim() || (empty ? 'Detail' : ''))}</span><code>${esc(where)}</code></span></div></div></div>`
      + '<div class="ld-strip">'
      + pick('site', siteIcon ? "The site's own icon" : 'The site offers no icon', siteIcon ? `<span class="ld-face"><img src="${esc(siteIcon)}" alt=""></span>` : `<span class="ld-face off">${svg('globe')}</span>`, !siteIcon)
      + pick('letter', 'The first letter of the name', `<span class="ld-face letter" data-color="${colour(shown)}">${letter(shown)}</span>`)
      + pick('upload', 'A picture of your own', uploaded && icon ? `<span class="ld-face"><img src="${esc(icon)}" alt=""></span>` : `<span class="ld-face up">${svg('upload')}</span>`)
      + pick('none', 'No icon', '<span class="ld-face none"></span>')
      + `<span>Icon: ${ICON_SAID[k]}</span></div>`;
    R.paint($('ln-preview'));
  };
  // Under the address: what reading the site filled in, or why it couldn't, and a way to read it again.
  const foundLine = (ok, text, again) => {
    $('ln-looking').hidden = !text && !again;
    $('ln-looking').className = `ld-found wide${ok ? '' : ' warn'}`;
    $('ln-looking').innerHTML = (text ? `${svg(ok ? 'check' : 'alert')}<span>${esc(text)}</span>` : '')
      + (text || again ? `<button type="button" class="link" data-again>${text ? 'Read it again' : 'Read the site again'}</button>` : '');
  };
  const groupOf = (id) => ($(id).value === NEW_GROUP ? $(`${id}-new`).value : $(id).value);
  const pickGroup = (id) => {
    const fresh = $(id).value === NEW_GROUP;
    $(`${id}-new`).hidden = !fresh;
    if (fresh) $(`${id}-new`).focus();
  };

  // A new link is one address with its details, or a list: every address is read first, then the ones ticked are added.
  let many = false;
  let rows = null; // the list as read: { url, f, how, keep } for each address, or null before it's read
  let show = 'all';
  const drawSave = () => {
    const n = listed($('ln-many').value).length;
    $('ln-save').textContent = editing ? 'Save' : !many ? 'Add the link' : rows ? `Add ${count(rows.filter((r) => r.keep).length, 'link')}` : n ? `Read ${addresses(n)}` : 'Read them';
    $('ln-many-count').textContent = many && n ? addresses(n) : '';
  };
  const drawMode = () => {
    $('link-dialog').classList.toggle('many', many);
    $('ln-mode').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', (b.dataset.mode === 'many') === many));
    $('link-title').textContent = editing ? `Change ${editing.name}` : 'Add a link';
    $('ln-dlead').textContent = editing ? 'Change anything you like.' : many
      ? 'Paste the addresses, one a line or separated by commas. Rabbid Lab reads each site, then you tick the ones to add.'
      : 'Paste the address. Rabbid Lab reads the site for the rest.';
    drawSave();
  };
  // The addresses in a pasted list: split at line ends, commas and semicolons, quotes dropped, each one once.
  const same = (url) => String(url).trim().replace(/^[a-z][a-z0-9+.-]*:\/\//i, '').replace(/\/+$/, '').toLowerCase();
  const listed = (text) => {
    const seen = new Set();
    return String(text).split(/[\r\n,;]+/).map((a) => a.trim().replace(/^["'<]+|["'>]+$/g, '').trim()).filter((a) => a && !seen.has(same(a)) && seen.add(same(a)));
  };
  const READ_AS = { ok: ['Found', 'ok'], none: ['No answer', 'warn'], dup: ['On Links already', 'muted'] };
  // What the list turned out to be: filters by what each one said, and a tick on each to add it or leave it out.
  const drawFound = () => {
    $('ln-found-head').hidden = $('ln-found').hidden = !rows;
    if (!rows) return;
    const n = (h) => rows.filter((r) => r.how === h).length;
    if (show !== 'all' && !n(show)) show = 'all';
    const shown = rows.map((r, i) => [r, i]).filter(([r]) => show === 'all' || r.how === show);
    const chips = [['all', 'All', rows.length], ['ok', 'Found', n('ok')], ['none', 'No answer', n('none')], ['dup', 'On Links already', n('dup')]].filter(([k, , c]) => k === 'all' || c);
    $('ln-found').innerHTML = `<div class="seg sn-seg ld-chips">${chips.map(([k, label, c]) => `<button type="button" data-show="${k}" aria-pressed="${show === k}">${label}<small>${c}</small></button>`).join('')}</div>`
      + `<div class="table ld-scroll"><div class="tr ld-lr th"><span><input type="checkbox" data-all aria-label="Tick all of these"${shown.every(([r]) => r.keep) ? ' checked' : ''}></span>`
      + '<span></span><span>Link</span><span>Address</span><span>What it said</span></div>'
      + shown.map(([r, i]) => {
        const name = r.f?.name || host(full(r.url));
        const pic = r.f?.icon ? `<span class="lic"><img src="${esc(r.f.icon)}" alt=""></span>` : `<span class="lic letter" data-color="${colour(name)}"><b>${letter(name)}</b></span>`;
        const about = r.how === 'dup' ? 'Left out, so nothing is there twice' : r.how === 'none' ? 'Added with its address only' : r.f?.description || '';
        const where = r.f?.address && r.f.address !== host(full(r.url)) ? `${r.f.address} · ${host(full(r.url))}` : host(full(r.url));
        return `<div class="tr ld-lr"><span><input type="checkbox" data-row="${i}"${r.keep ? ' checked' : ''} aria-label="Add ${esc(name)}"></span>${pic}`
          + `<span><b>${esc(name)}</b><small>${esc(about)}</small></span><span><code>${esc(where)}</code></span><span class="ld-how ${READ_AS[r.how][1]}">${READ_AS[r.how][0]}</span></div>`;
      }).join('') + '</div>'
      + `<p class="hint">${rows.filter((r) => r.keep).length} of ${rows.length} will be added. Untick any to leave them out.</p>`;
    R.paint($('ln-found'));
  };
  // The flask covers what's being worked on while it reads, with how far along it is.
  const reading = (text, detail, part) => {
    const area = $('link-dialog').querySelector(many ? '.ld-list' : '.ld-grid');
    R.busy(area, text, detail);
    const cover = area.querySelector(':scope > .busy-cover');
    if (cover && part != null) {
      if (!cover.querySelector('.ld-bar')) cover.insertAdjacentHTML('beforeend', '<span class="ubar ld-bar"><i></i></span>');
      cover.querySelector('.ld-bar i').style.width = `${Math.round(part * 100)}%`;
    }
    $('ln-save').disabled = !!text;
  };
  // Every address is read like a single one, six at a time. Ones already on Links are left unticked.
  const readMany = async () => {
    const all = listed($('ln-many').value);
    say('ln-dproblem', '');
    if (!all.length) return say('ln-dproblem', 'Paste at least one address.');
    const have = new Set(links.map((l) => same(l.url)));
    const run = ++lookRun;
    rows = all.map((url) => ({ url, f: null, how: have.has(same(url)) ? 'dup' : 'new', keep: !have.has(same(url)) }));
    const todo = rows.filter((r) => r.how === 'new');
    const tally = () => `${rows.filter((r) => r.how === 'ok').length} found · ${rows.filter((r) => r.how === 'none').length} no answer · ${rows.filter((r) => r.how === 'dup').length} on Links already`;
    let next = 0;
    let done = 0;
    const read = async () => {
      while (next < todo.length && run === lookRun) {
        const r = todo[next++];
        try {
          r.f = await R.send('/api/links/look', { url: r.url });
        } catch {
          r.f = null; // added with its address for a name
        }
        r.how = r.f && !r.f.problem ? 'ok' : 'none';
        done++;
        if (run === lookRun) reading(`Reading ${done} of ${todo.length}`, tally(), done / todo.length);
      }
    };
    if (todo.length) reading(`Reading 0 of ${todo.length}`, tally(), 0);
    await Promise.all(Array.from({ length: Math.min(6, todo.length) }, read));
    if (run !== lookRun) return;
    reading('');
    show = 'all';
    drawFound();
    drawSave();
  };
  // The ticked ones are added in the order given; any that don't go in stay ticked, with why.
  const addMany = async () => {
    const keep = rows.filter((r) => r.keep);
    say('ln-dproblem', '');
    if (!keep.length) return say('ln-dproblem', 'Tick at least one link to add.');
    const group = groupOf('ln-many-group');
    const run = ++lookRun;
    const failed = [];
    for (let i = 0; i < keep.length && run === lookRun; i++) {
      const r = keep[i];
      const f = r.f || {};
      reading(`Adding ${i + 1} of ${keep.length}`, '', (i + 1) / keep.length);
      try {
        await R.send('/api/links', { url: r.url, name: (f.name || '').slice(0, 60), group, description: (f.description || '').slice(0, 200), address: (f.address || '').slice(0, 100), icon: f.icon || null });
        Object.assign(r, { keep: false, how: 'dup' });
      } catch (err) {
        failed.push([r, err.message]);
      }
    }
    if (run !== lookRun) return refresh();
    reading('');
    if (!failed.length) {
      $('link-dialog').close();
      return refresh();
    }
    const added = keep.length - failed.length;
    say('ln-dproblem', `${added ? `Added ${added}. ` : ''}${failed.length === 1 ? `${host(full(failed[0][0].url))} wasn't added` : `${failed.length} weren't added`}: ${failed[0][1]}`);
    drawFound();
    drawSave();
    refresh();
  };
  $('ln-mode').addEventListener('click', (e) => {
    const b = e.target.closest('[data-mode]');
    if (!b || editing) return;
    many = b.dataset.mode === 'many';
    say('ln-dproblem', '');
    drawMode();
    drawFound();
    (many ? $('ln-many') : $('ln-url')).focus();
  });
  $('ln-many').addEventListener('input', () => {
    // A changed list is read again before anything is added.
    if (rows) {
      rows = null;
      lookRun++;
      drawFound();
    }
    drawSave();
  });
  $('ln-found').addEventListener('click', (e) => {
    const b = e.target.closest('[data-show]');
    if (!b) return;
    show = show === b.dataset.show ? 'all' : b.dataset.show;
    drawFound();
  });
  $('ln-found').addEventListener('change', (e) => {
    if (e.target.dataset.row !== undefined) rows[Number(e.target.dataset.row)].keep = e.target.checked;
    else if (e.target.dataset.all !== undefined) rows.forEach((r) => { if (show === 'all' || r.how === show) r.keep = e.target.checked; });
    drawFound();
    drawSave();
  });
  // Several addresses pasted into the one address box go to the list instead.
  $('ln-url').addEventListener('paste', (e) => {
    const text = e.clipboardData?.getData('text') || '';
    const parts = listed(text);
    if (editing || $('ln-url').value.trim() || parts.length < 2 || !(/[\r\n]/.test(text.trim()) || parts.every((p) => /[.:]/.test(p)))) return;
    e.preventDefault();
    many = true;
    rows = null;
    $('ln-many').value = parts.join('\n');
    drawMode();
    drawFound();
    $('ln-many').focus();
  });
  // Closing the dialog stops a list that's still being read.
  $('link-dialog').addEventListener('close', () => { lookRun++; });
  // A link to change, or null for a new one, which starts in the group asked for or the one being looked at.
  const open = (l, into) => {
    editing = l;
    looked = null;
    lookedUrl = '';
    lookRun++;
    many = false;
    reading('');
    icon = l?.icon || null;
    siteIcon = l?.icon && l.icon !== NO_ICON ? l.icon : null;
    uploaded = false;
    rows = null;
    show = 'all';
    $('ln-mode').hidden = !!l;
    $('ln-many').value = '';
    $('ln-url').value = l?.url || '';
    $('ln-name').value = l?.name || '';
    $('ln-about').value = l?.description || '';
    const options = '<option value="">No group</option>' + groups.map((g) => `<option value="${esc(g)}">${esc(g)}</option>`).join('') + `<option value="${NEW_GROUP}">New group…</option>`;
    const start = l ? l.group || '' : into ?? (group === NO_GROUP ? '' : group);
    for (const id of ['ln-group', 'ln-many-group']) {
      $(id).innerHTML = options;
      $(id).value = start;
      $(`${id}-new`).value = '';
      pickGroup(id);
    }
    ip = l?.address || '';
    $('ln-delete').hidden = !l;
    say('ln-dproblem', '');
    foundLine(true, '', !!l);
    drawMode();
    drawFound();
    drawIcon();
    $('link-dialog').showModal();
    $('ln-url').focus();
  };
  // Reads the site; fills the empty fields, or all of them when asked to.
  const look = async (all) => {
    const url = $('ln-url').value.trim();
    // Pasting an address and then leaving the box both ask: the second time there's nothing new to read.
    if (!url || (!all && url === lookedUrl)) return;
    lookedUrl = url;
    const run = ++lookRun;
    say('ln-dproblem', '');
    reading(`Reading ${host(full(url))}…`);
    // Long enough to see, so a fast answer isn't a flicker.
    const seen = new Promise((done) => setTimeout(done, 600));
    let answer = null;
    let problem = '';
    try {
      answer = await R.send('/api/links/look', { url });
    } catch (err) {
      problem = err.message;
    }
    await seen;
    if (run !== lookRun) return;
    reading('');
    if (answer) {
      looked = answer;
      const fill = (id, value) => { if (value && (all || !$(id).value.trim())) $(id).value = value; };
      fill('ln-name', looked.name);
      fill('ln-about', looked.description);
      if (looked.address) ip = looked.address;
      if (looked.icon) siteIcon = looked.icon;
      if (looked.icon && (all || !icon)) {
        icon = looked.icon;
        uploaded = false;
      }
      const got = [looked.name && 'name', looked.description && 'detail', looked.icon && 'icon', looked.address && 'IP'].filter(Boolean);
      const said = got.length > 1 ? `${got.slice(0, -1).join(', ')} and ${got[got.length - 1]}` : got[0];
      foundLine(!looked.problem, looked.problem || (said ? `Read ${host(full(url))}: ${said} filled in. Change anything you like.` : `Read ${host(full(url))}, but it says nothing about itself. Fill it in yourself.`));
    } else {
      foundLine(false, problem);
    }
    drawIcon();
  };
  $('ln-add').addEventListener('click', () => open(null));
  $('ln-add-first').addEventListener('click', () => open(null));
  $('ln-group').addEventListener('change', () => pickGroup('ln-group'));
  $('ln-many-group').addEventListener('change', () => pickGroup('ln-many-group'));
  $('ln-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-edit]');
    if (b) open(links.find((l) => l.id === b.dataset.edit));
  });
  $('ln-url').addEventListener('change', () => {
    if (!editing) return look(false);
    // A link moved to another machine: its IP is looked up again, and what was typed stays.
    if (host(full($('ln-url').value)) === host(editing.url)) return;
    ip = '';
    look(false);
  });
  $('ln-url').addEventListener('paste', () => setTimeout(() => { if (!editing && !many) look(false); }, 0));
  $('ln-looking').addEventListener('click', (e) => { if (e.target.closest('[data-again]')) look(true); });
  ['ln-url', 'ln-name', 'ln-about'].forEach((id) => $(id).addEventListener('input', drawIcon));
  $('ln-preview').addEventListener('click', (e) => {
    const b = e.target.closest('[data-icon]');
    if (!b || b.disabled) return;
    if (b.dataset.icon === 'upload') return $('ln-icon-file').click();
    uploaded = false;
    icon = { site: siteIcon, letter: null, none: NO_ICON }[b.dataset.icon];
    drawIcon();
  });
  // An uploaded picture, made small: 128 pixels across at most, as a PNG (SVGs as they are).
  $('ln-icon-file').addEventListener('change', () => {
    const f = $('ln-icon-file').files[0];
    $('ln-icon-file').value = '';
    if (!f || !f.type.startsWith('image/')) return;
    const reader = new FileReader();
    const use = (picture) => {
      icon = picture;
      uploaded = true;
      drawIcon();
    };
    reader.onload = () => {
      if (f.type === 'image/svg+xml' && reader.result.length < 150000) return use(reader.result);
      const img = new Image();
      img.onload = () => {
        const scale = Math.min(1, 128 / Math.max(img.width, img.height));
        const c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(img.width * scale));
        c.height = Math.max(1, Math.round(img.height * scale));
        c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
        use(c.toDataURL('image/png'));
      };
      img.onerror = () => say('ln-dproblem', "That picture couldn't be read.");
      img.src = reader.result;
    };
    reader.readAsDataURL(f);
  });
  $('ln-cancel').addEventListener('click', () => $('link-dialog').close());
  $('ln-save').addEventListener('click', async () => {
    if (many && !editing) return rows ? addMany() : readMany();
    const body = { url: $('ln-url').value, name: $('ln-name').value, group: groupOf('ln-group'), description: $('ln-about').value, address: ip, icon };
    $('ln-save').disabled = true;
    try {
      await R.send(editing ? `/api/links/${encodeURIComponent(editing.id)}` : '/api/links', body);
      $('link-dialog').close();
      refresh();
    } catch (err) {
      say('ln-dproblem', err.message);
    } finally {
      $('ln-save').disabled = false;
    }
  });
  $('ln-delete').addEventListener('click', () => {
    if (!editing) return;
    const gone = editing;
    R.sure(`Take ${gone.name} off Links?`, 'The link is removed for everyone.', 'Delete', async () => {
      try {
        await R.send(`/api/links/${encodeURIComponent(gone.id)}/delete`, {});
        $('link-dialog').close();
        refresh();
      } catch (err) {
        say('ln-dproblem', err.message);
      }
    });
  });

  // Developer tools (Rabbid Lab Dev): a test link, one that's down, or a list of twelve, gone in 30 seconds.
  document.querySelector('#page-links .devstrip').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-test]');
    if (!b) return;
    try {
      take(await R.send('/api/links/test', { kind: b.dataset.test }));
      say('ln-problem', '');
      draw();
      check();
      setTimeout(refresh, 31000);
    } catch (err) {
      say('ln-problem', err.message);
    }
  });

  // --- Showing the page --------------------------------------------------------------------------------------------

  document.addEventListener('rabbidlab:show', (e) => {
    clearTimeout(timer);
    if (e.detail[0] !== 'links' || !can('links.view')) return;
    refresh(); // its check keeps itself going while the page is the one shown
  });
  // The count and the down dot in the rail, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => { if (can('links.view') && R.where()[0] !== 'links') refresh(); });
})();
