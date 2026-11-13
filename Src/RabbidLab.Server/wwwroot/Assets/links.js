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
  let view = ['grid', 'links', 'icons'].includes(stored('rabbidlab.links.view')) ? stored('rabbidlab.links.view') : 'cards'; // tiles, rows, plain links or icons

  const colour = (name) => COLOURS[[...String(name)].reduce((n, c) => (n * 31 + c.charCodeAt(0)) >>> 0, 7) % COLOURS.length];
  const letter = (name) => esc((String(name).match(/[A-Za-z0-9]/) || ['?'])[0].toUpperCase());
  const NO_ICON = 'none'; // a link's icon when it's to show none at all; with no icon saved it shows its first letter
  const own = (l) => !!l.icon && l.icon !== NO_ICON; // it has a picture
  const pic = (l) => (own(l) ? `<img src="${esc(l.icon)}" alt="">` : `<b>${letter(l.name)}</b>`);
  // How many pixels wide a picture is, from its data: URL's header (PNG, GIF, the biggest in an ICO); an SVG is sharp at any size.
  const picPx = (icon) => {
    if (/^data:image\/svg/i.test(icon)) return Infinity;
    const m = /^data:image\/([a-z0-9+.-]+);base64,([A-Za-z0-9+/=]{0,1024})/i.exec(icon || '');
    if (!m) return null;
    let b = '';
    try { b = atob(m[2].slice(0, m[2].length - (m[2].length % 4))); } catch { return null; }
    const at = (i) => b.charCodeAt(i);
    const kind = m[1].toLowerCase();
    if (kind === 'png' && b.length >= 24) return ((at(16) << 24) | (at(17) << 16) | (at(18) << 8) | at(19)) >>> 0;
    if (kind === 'gif' && b.length >= 10) return at(6) | (at(7) << 8);
    if (kind.endsWith('icon') && b.length >= 22) {
      let most = 0;
      for (let k = 0, n = at(4) | (at(5) << 8); k < n && 6 + 16 * k < b.length; k++) most = Math.max(most, at(6 + 16 * k) || 256);
      return most || null;
    }
    return null;
  };
  // A picture is never drawn bigger than it is, so a 16 px favicon stays sharp instead of blown up.
  const capPic = (el) => {
    const img = el.querySelector('img');
    if (!img || /^data:image\/svg/i.test(img.src)) return;
    const cap = () => Object.assign(img.style, { maxWidth: `${img.naturalWidth}px`, maxHeight: `${img.naturalHeight}px` });
    if (img.complete && img.naturalWidth) cap();
    else img.addEventListener('load', cap, { once: true });
  };
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
    const why = s.why ? `: ${esc(s.why)}` : '';
    return s.down ? `<i class="lstate down" title="Down · no answer${since ? ` since ${since}` : ''}${why}" data-now="Down"></i>`
      : `<i class="lstate late" title="No answer just now${why} · checking again, down after ${span(checks.downAfter)}" data-now="No answer"></i>`;
  };
  const span = (s) => (s < 60 ? `${s} seconds` : s === 60 ? 'a minute' : `${s / 60} minutes`);
  // While links are being ticked to watch: a tick on each, ticked or not, and none on one that isn't a web address.
  const tick = (l) => (!watching ? '' : checked(l) ? `<span class="lw-tick">${svg('check')}</span>` : '');
  const tickClass = (l) => (!watching ? '' : !checked(l) ? ' lw-off' : ticked.has(l.id) ? ` lw-on${l.sensor ? ' lw-was' : ''}` : '');
  // A watched link: a small eye beside its dot.
  const eye = (l) => (l.sensor && !watching ? `<span class="lw-eye" title="Watched: it alerts when it's down">${svg('eye')}</span>` : '');
  const tile = (l) => `<div class="ltile${states[l.id]?.down ? ' down' : ''}${tickClass(l)}" data-id="${esc(l.id)}"${can('links.edit') && !watching ? ` draggable="true" data-drag="${esc(l.id)}"` : ''}>${tick(l)}<a class="lgo" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer" draggable="false">`
    + (l.icon === NO_ICON ? (view === 'grid' ? '<span class="lic gap"></span>' : '') : `<span class="lic${l.icon ? '' : ' letter'}" data-color="${colour(l.name)}">${pic(l)}</span>`)
    + `<span class="lt"><b>${esc(l.name)}</b>${l.description ? `<span>${esc(l.description)}</span>` : ''}`
    + `<code>${esc(l.address && l.address !== host(l.url) ? `${l.address} · ${host(l.url)}` : host(l.url))}</code></span></a>`
    + `${eye(l)}${dot(l)}${can('links.edit') && !watching ? `<button type="button" class="icon ledit" data-edit="${esc(l.id)}" aria-label="Change ${esc(l.name)}">${svg('edit')}</button>` : ''}</div>`;
  // The Links and Icons views: a dot only when a link isn't answering.
  const off = (l) => (states[l.id] && !states[l.id].up ? (states[l.id].down ? 'down' : 'late') : '');
  const face = (l, cls) => `<span class="${cls}${l.icon && l.icon !== NO_ICON ? '' : ' letter'}" data-color="${colour(l.name)}">${l.icon === NO_ICON ? '' : pic(l)}</span>`;
  // Dragged to another place or group, like the tiles, for people who may change links.
  const drag = (l) => (can('links.edit') ? ` draggable="true" data-drag="${esc(l.id)}"` : '');
  // Links: one line each, the name and then its address.
  const plain = (l) => `<a class="lx-link${off(l) ? ` ${off(l)}` : ''}" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer" title="${esc(l.description || l.name)}"${drag(l)}>`
    + `${face(l, 'lx-ic')}<span class="lx-name">${esc(l.name)}</span><span class="lx-host">${esc(host(l.url))}</span>${off(l) ? `<i class="lx-dot ${off(l)}"></i>` : ''}</a>`;
  // Icons: a square with the name under it, like a home screen.
  const launch = (l) => `<a class="lapp${off(l) ? ` ${off(l)}` : ''}" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer" title="${esc(`${l.name} · ${host(l.url)}`)}"${drag(l)}>`
    + `${face(l, 'lapp-sq')}${off(l) ? `<i class="lx-dot ${off(l)}"></i>` : ''}<span class="lapp-n">${esc(l.name)}</span></a>`;
  const body = (list) => (watching ? `<div class="ltiles${view === 'grid' ? ' lrows' : ''}">${list.map(tile).join('')}</div>` // ticking works on tiles
    : view === 'links' ? `<div class="lx">${list.map(plain).join('')}</div>`
    : view === 'icons' ? `<div class="lapps">${list.map(launch).join('')}</div>`
      : `<div class="ltiles${view === 'grid' ? ' lrows' : ''}">${list.map(tile).join('')}</div>`);

  // The count and the down dot beside Links in the rail.
  const rail = () => {
    $('rail-links').textContent = links.length || '';
    $('links-dot').hidden = !links.some((l) => states[l.id] && states[l.id].down);
  };
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
    $('ln-list').classList.toggle('lx-cols', view === 'links'); // the Links view flows its groups down columns
    $('ln-list').innerHTML = by.map(([g, list]) => `<section class="lgroup" data-name="${esc(g)}">${groups.length ? tools.heading(g, list.length) : ''}`
      + (list.length ? body(list)
        : `<p class="hint lnone">No links in this group yet.${can('links.edit') ? ` <button type="button" class="link" data-addto="${esc(g)}">Add one</button>` : ''}</p>`)
      + '</section>').join('')
      || (links.length ? '<p class="hint">Nothing matches.</p>' : '');
    if (!find) $('ln-list').insertAdjacentHTML('beforeend', tools.out(by.some(([g]) => !g)));
    tools.settle();
    $('ln-view').querySelectorAll('button').forEach((b) => b.setAttribute('aria-pressed', b.dataset.view === view));
    $('ln-empty').hidden = links.length > 0 || groups.length > 0;
    $('ln-empty-text').textContent = can('links.edit')
      ? `Put the sites in your lab here: the router, the NAS, the hypervisor, iDRACs. Paste an address and ${R.lab()} fills in the name, icon and IP.`
      : 'Nobody has put any here yet.';
    R.paint($('ln-list'));
    rail();
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
    const size = R.squareSize(links.length);
    // Where each sits on the page, for the layouts that go by group: its group's place, and its place in the group.
    const order = [...groups, ''];
    const inGroup = new Map();
    const placed = new Map(links.map((l) => {
      const g = order.indexOf(l.group || '');
      const k = inGroup.get(g) || 0;
      inGroup.set(g, k + 1);
      return [l.id, { g: g < 0 ? order.length : g, k }];
    }));
    const items = [...links].sort((a, b) => a.id.localeCompare(b.id)).map((l) => {
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
        capPic(el);
      }
      el.href = l.url;
      el.className = `tile sen lsq s-${state}${side < 20 ? ' tiny' : ''}`;
      el.title = `${l.name} · ${HOW[state]}${(state === 'down' || state === 'warn') && states[l.id].why ? `: ${states[l.id].why}` : ''}`;
      el.setAttribute('aria-label', el.title);
      el.style.setProperty('--g', own(l) ? '#1E2132' : colour(l.name));
      el.style.setProperty('--ring', own(l) ? 'var(--accent-soft)' : colour(l.name));
      R.ping(el, states[l.id]?.at, hash(`${l.id}p`) * 0.6); // the checks all land together, so the pulses spread over a moment
      Object.assign(el.style, { width: `${side}px`, height: `${side}px`, rotate: `${state === 'down' ? 8 : Math.round(hash(`${l.id}r`) * 24 - 12)}deg` });
      const px = own(l) ? picPx(l.icon) : Infinity; // a letter is sharp at any size
      return { el, id: l.id, group: l.group || '', sharp: px == null || px >= 48, ...placed.get(l.id) };
    });
    const seen = new Set(items.map((x) => x.id));
    squares.forEach((el, id) => {
      if (seen.has(id)) return;
      el.remove();
      squares.delete(id);
    });
    R.squares($('ln-hero'), items, $('ln-card'), true);
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
    const seen = links.filter((l) => l.sensor).length;
    $('ln-watched-wrap').hidden = !seen;
    $('ln-watched').textContent = seen;
    $('ln-answering').textContent = !web.length ? '0' : known.length ? `${up.length} of ${web.length}` : '…';
    $('ln-every').textContent = EVERY_SHORT[checks.every] || `${checks.every} s`;
    $('ln-card').hidden = !first;
    if (first) {
      $('ln-card-title').textContent = first.name;
      $('ln-card-text').textContent = `No answer from ${host(first.url)}${states[first.id].since ? ` since ${at(states[first.id].since)}` : ''}${states[first.id].why ? `: ${states[first.id].why}` : ''}.`;
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
      // Off the page only the rail's dot; on it, not under a drag or a name being typed: the dots catch up on the next check.
      if (R.where()[0] !== 'links') rail();
      else if (!tools.busy() && !refreshing) draw();
    } catch {
      // The dots stay as they were.
    }
    clearTimeout(timer);
    timer = setTimeout(check, wait * 1000); // on every page, so the rail's dot clears once a link is back
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
  // Names compared the way people read them: case ignored, esxi2 before esxi10.
  const byName = (a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
  // A link dragged to another place: its group's links in their new order. Shown straight away, then saved.
  // whole: ids is the whole group already (Sort A to Z), so a search on the page changes nothing.
  const arrange = async (id, to, ids, whole = false) => {
    const l = links.find((x) => x.id === id);
    if (!l) return draw();
    // With a search on, the page shows only part of the group: the link joins the end of all of it.
    if (!whole && $('ln-find').value.trim()) ids = [...links.filter((x) => (x.group || '') === to && x.id !== id).map((x) => x.id), id];
    l.group = to || null;
    links = [...links.filter((x) => !ids.includes(x.id)), ...ids.map((x) => links.find((y) => y.id === x)).filter(Boolean)];
    draw();
    // Each group's links in order, to tell whether the server's answer changes anything on the page.
    const shown = () => JSON.stringify([...groups, ''].map((g) => links.filter((x) => (x.group || '') === g).map((x) => x.id)));
    const was = shown();
    try {
      take(await R.send('/api/links/arrange', { group: to, ids }));
      say('ln-problem', '');
    } catch (err) {
      say('ln-problem', err.message);
    }
    if (shown() !== was) draw(); // drawn once already: again only when the server put things elsewhere
  };
  // Groups: renamed where they stand, dragged into order, sorted, deleted (console.js has the tools Sensors uses too).
  const tools = R.groupTools($('ln-list'), {
    api: '/api/links/groups',
    thing: 'link',
    problem: 'ln-problem',
    can: () => can('links.edit'),
    names: () => groups,
    order: (names) => { groups = names; },
    count: (name) => links.filter((l) => l.group === name).length,
    // Open all: every link in the group, each in a tab of its own.
    also: (g, n) => (watching ? tickAll(g) : n > 1 ? `<button type="button" class="link lopen" data-openall="${esc(g)}" title="Open every link in this group, each in a new tab">Open all</button>` : ''),
    places: true, // links keep the order they're dragged into
    move: arrange,
    // Sort: the group's links by name, A to Z or Z to A, saved like a drag.
    sort: (g, dir) => {
      const ids = links.filter((x) => (x.group || '') === g).sort((a, b) => byName(a, b) * (dir === 'za' ? -1 : 1)).map((x) => x.id);
      if (ids.length > 1) arrange(ids[0], g, ids, true);
    },
    // Which way the group is sorted now: 'az', 'za', or '' when it's in an order of its own.
    sorted: (g) => {
      const list = links.filter((x) => (x.group || '') === g);
      if (list.every((x, i) => !i || byName(list[i - 1], x) <= 0)) return 'az';
      return list.every((x, i) => !i || byName(list[i - 1], x) >= 0) ? 'za' : '';
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
    $('lc-known-label').textContent = checks.describe !== false ? 'On' : 'Off';
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
    const form = $('lc-form');
    if (on) R.busy(form, 'Describing the links…');
    await saveChecks({ describe: on });
    R.busy(form, '');
    draw();
  });
  // Import: a file Export made, added beside what's here. The flask covers the links while it goes in.
  const count = (n, thing) => `${n} ${thing}${n === 1 ? '' : 's'}`;
  // Refresh: every link with a web address has its site read again, four at a time. On Links each
  // tile being read shows the flask and takes what was found as soon as it's back, the way Plex refreshes metadata; in
  // Settings, Links the flask covers the section with how far along it is. One run at a time.
  let refreshing = false;
  // The banner squares follow the links as they come back, drawn once a frame however many land together.
  let squaresDue = false;
  const squaresSoon = () => {
    if (squaresDue) return;
    squaresDue = true;
    requestAnimationFrame(() => {
      squaresDue = false;
      drawSquares();
    });
  };
  const tileOf = (id) => $('ln-list').querySelector(`.ltile[data-id="${CSS.escape(id)}"]`);
  const refreshLinks = async (host, note) => {
    if (refreshing) return;
    const onPage = host === $('ln-list');
    // Every link with a web address: the server keeps what was set by hand and brings the rest up to date.
    const todo = links.filter(checked);
    ['lc-refresh-done', 'ln-done'].forEach((id) => say(id, ''));
    if (!todo.length) return say(note, 'No links with a web address to refresh.');
    refreshing = true;
    $('ln-refresh').disabled = true;
    const got = { icon: 0, description: 0, IP: 0 };
    let done = 0;
    let filled = 0;
    let quiet = 0;
    const progress = (name) => {
      // Once nothing is left to start, it says which it's waiting on.
      const waiting = next >= queue.length && inFlight.size ? ` waiting on ${[...inFlight].join(', ')}` : '';
      if (onPage) return R.toast(`Refreshing ${done} of ${todo.length}…`, waiting.trim(), { stay: true });
      R.busy(host, `Refreshing ${done} of ${todo.length}`, name ? `${name} · ${count(filled, 'link')} filled in so far` : '');
      const cover = host.querySelector(':scope > .busy-cover');
      if (cover && !cover.querySelector('.ld-bar')) cover.insertAdjacentHTML('beforeend', '<span class="ubar ld-bar"><i></i></span>');
      if (cover) cover.querySelector('.ld-bar i').style.width = `${Math.round((done / todo.length) * 100)}%`;
    };
    // Started top to bottom, as the page shows them, four at a time: a slow one only holds up its own place, not the group.
    const queues = [...groups, ''].map((g) => todo.filter((l) => (l.group || '') === g)).filter((q) => q.length);
    const rest = todo.filter((l) => !queues.some((q) => q.includes(l)));
    if (rest.length) queues.push(rest);
    const queue = queues.flat();
    const inFlight = new Set(); // the names being read just now
    let next = 0;
    const work = async () => {
      while (next < queue.length) {
        const l = queue[next++];
        inFlight.add(l.name);
        const card = onPage ? tileOf(l.id) : null;
        if (card) R.busy(card, 'Refreshing');
        let r = null;
        try {
          r = await R.send(`/api/links/${encodeURIComponent(l.id)}/refresh`, {});
          r.changed.forEach((c) => { got[c] += 1; });
          if (r.changed.length) filled++;
          else if (r.problem) quiet++;
        } catch {
          quiet++; // gone, or the server didn't answer: counted with the ones that didn't
        }
        inFlight.delete(l.name);
        done++;
        // The tile and its square show what was found straight away.
        const at = links.findIndex((x) => x.id === l.id);
        if (r?.link && at >= 0) links[at] = r.link;
        const now = onPage ? tileOf(l.id) : null;
        if (now && at >= 0) {
          now.outerHTML = tile(links[at]);
          const fresh = tileOf(l.id);
          R.paint(fresh);
          if (r?.changed.length) fresh.classList.add('lfresh');
        } else if (now) R.busy(now, '');
        if (r?.changed.length) squaresSoon();
        progress(l.name);
      }
    };
    progress('');
    await Promise.all(Array.from({ length: Math.min(4, queue.length) }, work));
    if (!onPage) R.busy(host, '');
    refreshing = false;
    $('ln-refresh').disabled = false;
    draw(); // each link was taken in as it came back: nothing to read again
    const what = [got.icon && count(got.icon, 'icon'), got.description && count(got.description, 'description'), got.IP && count(got.IP, 'IP')].filter(Boolean);
    // The result in a toast, with the activity log for what changed.
    const list = what.length > 1 ? `${what.slice(0, -1).join(', ')} and ${what[what.length - 1]}` : what[0];
    R.toast(filled ? `Refreshed ${list} on ${count(filled, 'link')}.` : `Refreshed ${count(todo.length, 'link')}.`,
      [filled ? '' : 'Nothing to change.', quiet ? `${count(quiet, 'link')} didn't answer.` : ''].filter(Boolean).join(' '),
      { tone: quiet ? 'warn' : '', action: filled || quiet ? { label: 'Details', href: '#settings/activity/lab' } : null });
  };
  $('lc-refresh').addEventListener('click', () => refreshLinks($('lc-form'), 'lc-refresh-done'));
  $('ln-refresh').addEventListener('click', () => refreshLinks($('ln-list'), 'ln-done'));
  // The cogwheel's menu shuts once a row in it is picked.
  $('ln-gear-menu').addEventListener('click', (e) => { if (e.target.closest('.am-link')) $('ln-gear-menu').hidePopover(); });
  // Export: the server sends the file to save.
  $('ln-export').addEventListener('click', () => {
    const a = document.createElement('a');
    a.href = '/api/links/export';
    a.download = '';
    a.click();
  });
  $('ln-import').addEventListener('click', () => $('ln-import-file').click());
  $('ln-import-file').addEventListener('change', async () => {
    const f = $('ln-import-file').files[0];
    $('ln-import-file').value = '';
    if (!f) return;
    const form = $('ln-list');
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
      R.toast(parts[0], parts.slice(1).filter(Boolean).join(' '));
    } catch (err) {
      R.toast("Couldn't import the file", err.message, { tone: 'red' });
    }
    R.busy(form, '');
    draw();
    check();
  });
  // Links settings, from the cogwheel's menu: drawn from a fresh read each time it shows.
  document.addEventListener('rabbidlab:show', async (e) => {
    if (e.detail[0] !== 'settings' || e.detail[1] !== 'links' || !can('links.edit')) return;
    say('lc-problem', '');
    try {
      take(await R.get('/api/links'));
    } catch (err) {
      say('lc-problem', err.message);
    }
    drawChecks();
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
  let siteFrom = null; // where on the site it came from
  let siteFresh = false; // read from the site while this dialog is open, so the site option needn't read it again
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
      + pick('site', siteIcon ? "The site's own icon" : "Find the site's icon", siteIcon ? `<span class="ld-face"><img src="${esc(siteIcon)}" alt=""></span>` : `<span class="ld-face up">${svg('globe')}</span>`)
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
      + ((text && $('ln-url').value.trim()) || again ? `<button type="button" class="link" data-again>${text ? 'Read it again' : 'Read the site again'}</button>` : '');
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
      ? `Paste the addresses, one a line or separated by commas. ${R.lab()} reads each site, then you tick the ones to add.`
      : `Paste the address. ${R.lab()} reads the site for the rest.`;
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
        await R.send('/api/links', { url: r.url, name: (f.name || '').slice(0, 60), group, description: (f.description || '').slice(0, 200), address: (f.address || '').slice(0, 100), icon: f.icon || null, iconFrom: f.iconFrom || null });
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
    uploaded = l?.iconFrom === 'own';
    siteIcon = !uploaded && l?.icon && l.icon !== NO_ICON ? l.icon : null;
    siteFrom = l?.iconFrom || null;
    siteFresh = false;
    rows = null;
    show = 'all';
    $('ln-mode').hidden = !!l;
    $('ln-dev').hidden = !!l;
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
    R.watchLink?.open(l); // Watch it: sensors.js fills it in
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
      if (looked.icon) {
        siteIcon = looked.icon;
        siteFrom = looked.iconFrom || null;
        siteFresh = true;
      }
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
  // The site's icon alone, for the site option before the site has been read: the name and detail stay as they are.
  const pullIcon = async () => {
    const url = $('ln-url').value.trim();
    if (!url) {
      foundLine(false, "Paste the address first, then this finds the site's icon.");
      return $('ln-url').focus();
    }
    const run = ++lookRun;
    say('ln-dproblem', '');
    reading(`Getting the icon from ${host(full(url))}…`);
    let got = null;
    try {
      got = await R.send('/api/links/look', { url });
    } catch {
      // Said below.
    }
    if (run !== lookRun) return;
    reading('');
    if (got?.icon) {
      siteIcon = got.icon;
      siteFrom = got.iconFrom || null;
      siteFresh = true;
      icon = got.icon;
      uploaded = false;
      foundLine(true, `Got the icon from ${host(full(url))}.`);
    } else {
      foundLine(false, `${host(full(url))} has no icon ${R.lab()} can read. Upload one instead.`);
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
    if (b.dataset.icon === 'site' && !siteFresh) return pullIcon(); // what the site serves now, not what was saved
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
  // Developer tools (Rabbid Lab Dev): test addresses, read as a site that answers or one that doesn't. What's added with them goes in 30 seconds.
  const testAddress = (how, n) => `https://${how}-${String(n).padStart(2, '0')}.rabbidlab.test`;
  $('ln-dev').addEventListener('click', (e) => {
    const b = e.target.closest('[data-fill]');
    if (!b) return;
    const kind = b.dataset.fill;
    if (kind === 'up' || kind === 'down') {
      $('ln-url').value = testAddress(kind, 1 + Math.floor(Math.random() * 99));
      $('ln-name').value = '';
      $('ln-about').value = '';
      icon = null;
      drawIcon();
      return look(true);
    }
    // A list: every ninth one doesn't answer, and the first link already on Links is in it too.
    const n = Number(kind);
    const lines = Array.from({ length: n }, (_, i) => testAddress(i % 9 === 8 ? 'down' : 'up', i + 1));
    if (links[0]) lines.splice(Math.min(2, lines.length), 0, links[0].url);
    $('ln-many').value = lines.join('\n');
    rows = null;
    drawFound();
    drawSave();
  });
  $('ln-cancel').addEventListener('click', () => $('link-dialog').close());
  $('ln-save').addEventListener('click', async () => {
    if (many && !editing) return rows ? addMany() : readMany();
    const body = { url: $('ln-url').value, name: $('ln-name').value, group: groupOf('ln-group'), description: $('ln-about').value, address: ip, icon,
      iconFrom: uploaded ? 'own' : icon && icon !== NO_ICON && icon === siteIcon ? siteFrom : null, watch: R.watchLink?.form() ?? undefined };
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
    R.sure(`Take ${gone.name} off Links?`, gone.sensor ? 'The link is removed for everyone, and so is the sensor that watches it.' : 'The link is removed for everyone.', 'Delete', async () => {
      try {
        await R.send(`/api/links/${encodeURIComponent(gone.id)}/delete`, {});
        $('link-dialog').close();
        refresh();
      } catch (err) {
        say('ln-dproblem', err.message);
      }
    });
  });

  // --- Watch links: tick the links to watch, pick when to alert and where, and each gets a sensor -------------------
  // Ticked to begin with are the ones watched already; unticking one stops watching it.

  let watching = false;
  let ticked = new Set();
  let lwTo = []; // the channels new ones alert
  let lwChans = null; // /api/channels, for people who may change alert settings
  const CH_NAME = { discord: 'Discord', teams: 'Teams', slack: 'Slack', ntfy: 'ntfy', webhook: 'Webhook' };
  const tickAll = (g) => {
    const mine = links.filter((l) => (l.group || '') === g && checked(l));
    if (!mine.length) return '';
    const all = mine.every((l) => ticked.has(l.id));
    return `<button type="button" class="link lw-all" data-tickall="${esc(g)}">${all ? `Untick all ${mine.length}` : `Tick all ${mine.length}`}</button>`;
  };
  const drawBar = () => {
    const was = new Set(links.filter((l) => l.sensor).map((l) => l.id));
    const adds = [...ticked].filter((id) => !was.has(id)).length;
    const stops = [...was].filter((id) => !ticked.has(id)).length;
    $('lw-n').textContent = ticked.size;
    $('lw-more').textContent = adds && stops ? `${adds} to watch, ${stops} to stop` : adds ? `${adds} new${was.size ? ` · ${was.size} watched already` : ''}` : stops ? `${stops} to stop watching` : 'Nothing changed yet';
    $('lw-go-text').textContent = adds && stops ? `Watch ${adds}, stop ${stops}` : adds ? `Watch ${adds} link${adds === 1 ? '' : 's'}` : stops ? `Stop watching ${stops}` : 'Watch';
    $('lw-go').disabled = !adds && !stops;
    $('lw-send-wrap').hidden = !lwChans;
    if (lwChans) {
      const all = [...(lwChans.email.ready ? [{ id: 'email', name: 'Email', kind: 'email' }] : []), ...lwChans.channels.map((c) => ({ id: c.id, name: c.name || CH_NAME[c.kind], kind: c.kind }))];
      $('lw-send').innerHTML = all.map((c) => `<button type="button" class="lw-chip${lwTo.includes(c.id) ? ' on' : ''}" data-lw-chan="${esc(c.id)}" aria-pressed="${lwTo.includes(c.id)}">${esc(c.name)}</button>`).join('')
        || '<span class="lw-none">No channels yet</span>';
    }
  };
  const setWatching = async (on) => {
    watching = on;
    $('page-links').classList.toggle('lw-mode', on);
    $('lw-bar').hidden = !on;
    if (on) {
      ticked = new Set(links.filter((l) => l.sensor).map((l) => l.id));
      // New ones start like new sensors: the alert time and channels from Settings, Sensors.
      const sensors = can('sensors.view') ? await R.get('/api/sensors').catch(() => null) : null;
      $('lw-after').value = String(sensors?.start?.after ?? 5);
      if ($('lw-after').value === '') $('lw-after').value = '5';
      lwChans = can('sensors.alerts') ? await R.get('/api/channels').catch(() => null) : null;
      lwTo = lwChans ? [...(lwChans.email.onForNew && lwChans.email.ready ? ['email'] : []), ...lwChans.channels.filter((c) => c.onForNew).map((c) => c.id)] : [];
      drawBar();
    }
    draw();
  };
  $('lw-cancel').addEventListener('click', () => setWatching(false));
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && watching && !document.querySelector('dialog[open]')) setWatching(false); });
  // While ticking, a click on a tile ticks it instead of opening it.
  $('ln-list').addEventListener('click', (e) => {
    if (!watching) return;
    const all = e.target.closest('[data-tickall]');
    const t = e.target.closest('.ltile');
    if (!all && !t) return;
    e.preventDefault();
    e.stopPropagation();
    if (all) {
      const mine = links.filter((l) => (l.group || '') === all.dataset.tickall && checked(l));
      const on = !mine.every((l) => ticked.has(l.id));
      mine.forEach((l) => (on ? ticked.add(l.id) : ticked.delete(l.id)));
    } else {
      const l = links.find((x) => x.id === t.dataset.id);
      if (!l || !checked(l)) return;
      if (ticked.has(l.id)) ticked.delete(l.id);
      else ticked.add(l.id);
    }
    drawBar();
    draw();
  }, true);
  $('lw-send').addEventListener('click', (e) => {
    const b = e.target.closest('[data-lw-chan]');
    if (!b) return;
    const id = b.dataset.lwChan;
    lwTo = lwTo.includes(id) ? lwTo.filter((x) => x !== id) : [...lwTo, id];
    drawBar();
  });
  $('lw-go').addEventListener('click', async () => {
    const was = new Set(links.filter((l) => l.sensor).map((l) => l.id));
    const watch = [...ticked].filter((id) => !was.has(id));
    const unwatch = [...was].filter((id) => !ticked.has(id));
    $('lw-go').disabled = true;
    try {
      const r = await R.send('/api/links/watch', { watch, unwatch, after: Number($('lw-after').value), channels: lwChans ? lwTo : null });
      const said = [r.watched ? `Watching ${r.watched} more link${r.watched === 1 ? '' : 's'}.` : '', r.stopped ? `Stopped watching ${r.stopped}.` : ''].filter(Boolean).join(' ');
      if (r.skipped.length) R.toast(said || 'Nothing changed', `Left out: ${r.skipped.join('; ')}`, { tone: 'warn' });
      else R.toast(said, r.watched ? "They're on Sensors too." : '', r.watched ? { action: { href: '#sensors', label: 'Open Sensors' } } : {});
      await setWatching(false);
      refresh();
    } catch (err) {
      R.toast("Couldn't change what's watched", err.message, { tone: 'warn' });
      $('lw-go').disabled = false;
    }
  });

  // --- Showing the page --------------------------------------------------------------------------------------------

  document.addEventListener('rabbidlab:show', (e) => {
    clearTimeout(timer);
    if (e.detail[0] !== 'links' || !can('links.view')) {
      if (watching) setWatching(false);
      if (can('links.view')) timer = setTimeout(check, 60000); // the rail's dot keeps up elsewhere
      return;
    }
    // #links/watch, from Pick links in Settings, Links: the page opens ticking. The address goes back to #links.
    const tick = e.detail[2][0] === 'watch' && can('links.edit') && can('sensors.edit');
    if (e.detail[2][0] === 'watch') history.replaceState(null, '', '#links');
    refresh().then(() => { if (tick && !watching) setWatching(true); }); // its check keeps itself going while the page is the one shown
  });
  // The count and the down dot in the rail, when the page itself hasn't asked already.
  document.addEventListener('rabbidlab:me', () => { if (can('links.view') && R.where()[0] !== 'links') refresh(); });
})();
