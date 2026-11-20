// Group Policy: the domain's GPOs, where they're linked, WMI filters and backups (/api/gpo), with a GPO's side panel
// (details, links, settings, who it applies to) and its Actions menu. On Devices, a Group Policy tab on each computer
// and a Group Policy list: what applied, what didn't and why, errors, and the local policy it ended up with.
// Changes are made as Rabbid Lab's AD account; when it isn't allowed, a domain admin's login is asked for (kept in this
// tab for 15 minutes, never saved).
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, get, send, toast, sure, panel } = R;

  const day = (iso) => (iso ? new Date(iso).toLocaleDateString([], { dateStyle: 'medium' }) : '');
  const stamp = (iso) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : '');
  const since = (iso) => {
    if (!iso) return '';
    const m = Math.max(0, Math.round((Date.now() - new Date(iso)) / 60000));
    return m < 1 ? 'Just now' : m < 60 ? `${m} min ago` : m < 1440 ? `${Math.round(m / 60)} hours ago` : m < 2880 ? 'Yesterday' : `${Math.round(m / 1440)} days ago`;
  };
  const badge = (text, tone) => `<span class="ubadge ${tone}">${esc(text)}</span>`;
  const muted = (text) => `<span class="muted">${esc(text)}</span>`;
  const facts = (rows) => `<ul class="does">${rows.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${v}</span></li>`).join('')}</ul>`;
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const rows2 = (items) => `<div class="dw-list seat-list">${items.map(([a, b, right = '', attr = '']) => `<div class="mrow${attr ? ' acc-row' : ''}" ${attr}><div class="two"><b>${esc(a)}</b><small>${esc(b)}</small></div>${right}</div>`).join('')}</div>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const plural = (n, one, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;
  const oops = (e) => { if (!e.cancelled) toast("That didn't work.", e.message, { tone: 'warn' }); };
  const WEEK = 7 * 86400000;

  // --- What's known --------------------------------------------------------------------------------------------------
  let gp = null; // /api/gpo: { domain, account, gpmc, gpos, places, filters, backups }
  let ad = null; // /api/ad: { domain, state }
  let problem = '';
  let loading = null;
  const detail = {}; // GPO id -> { data } | { error } | { loading }
  const load = () => (loading ||= (async () => {
    try {
      ad = await R.adInfo();
      if (ad?.state) {
        gp = await get('/api/gpo');
        problem = '';
      }
    } catch (e) {
      problem = e.message;
    } finally {
      loading = null;
    }
    draw();
    if (panel.is('gpo')) drawPanel();
  })());
  const gpoOf = (id) => gp?.gpos.find((g) => g.id === id);
  const placeOf = (dn) => gp?.places.find((p) => p.dn.toLowerCase() === String(dn).toLowerCase());
  const filterOf = (id) => gp?.filters.find((w) => w.id === id);
  const linksOf = (id) => (gp?.places || []).flatMap((p) => p.links.filter((l) => l.gpo === id).map((l) => ({ ...l, place: p })));
  const usedBy = (w) => (gp?.gpos || []).filter((g) => g.wmi === w.id);
  const nameOf = (id) => gpoOf(id)?.name || (id === 'LOCALGPO' ? 'Local Group Policy' : id);
  const STATUS = [['Enabled', 'green'], ['User settings off', 'grey'], ['Computer settings off', 'grey'], ['All settings off', 'amber']];
  const statusOf = (g) => STATUS[g.status] || STATUS[0];
  const computerOn = (g) => (g.status & 2) === 0;
  const userOn = (g) => (g.status & 1) === 0;

  // --- The admin login, for a change the service account isn't allowed to make ---------------------------------------
  // Typed for one action and sent with its requests. The password is never kept: it's gone when the action ends.
  let login = null; // { username, password } while the action that asked for it runs
  let lastUser = ''; // the username typed last, to fill it in again
  let acting = 0; // actions under way
  // Several changes as one action (the ticked GPOs, say): the login is asked for once, and forgotten after the last.
  const together = async (work) => {
    acting++;
    try {
      return await work();
    } finally {
      if (--acting === 0) login = null;
    }
  };
  let asking = null;
  const askLogin = (why) => new Promise((ok, no) => {
    asking = { ok, no };
    $('gpo-l-lead').textContent = why;
    $('gpo-l-problem').hidden = true;
    $('gpo-l-user').value = lastUser;
    $('gpo-l-password').value = '';
    $('gpo-login-dialog').showModal();
    ($('gpo-l-user').value ? $('gpo-l-password') : $('gpo-l-user')).focus();
  });
  const loginGo = () => {
    const username = $('gpo-l-user').value.trim();
    const password = $('gpo-l-password').value;
    if (!username || !password) {
      $('gpo-l-problem').textContent = 'Type the username and password.';
      $('gpo-l-problem').hidden = false;
      return;
    }
    login = { username, password };
    lastUser = username;
    const { ok } = asking;
    asking = null;
    $('gpo-login-dialog').close();
    ok();
  };
  $('gpo-l-go').addEventListener('click', loginGo);
  $('gpo-l-cancel').addEventListener('click', () => $('gpo-login-dialog').close());
  for (const id of ['gpo-l-user', 'gpo-l-password']) $(id).addEventListener('keydown', (e) => { if (e.key === 'Enter') loginGo(); });
  $('gpo-login-dialog').addEventListener('close', () => {
    $('gpo-l-password').value = ''; // not left in the page
    if (!asking) return;
    const { no } = asking;
    asking = null;
    no(Object.assign(new Error('Cancelled.'), { cancelled: true }));
  });
  // A change: confirmed it's you (as Devices asks), then as the service account, or with the login when it isn't allowed.
  const change = (path, body = {}, where = $('gpo-lib')) => together(async () => {
    const attempt = async () => {
      const l = login;
      try {
        return await send(path, { ...body, ...(l ? { username: l.username, password: l.password } : {}) });
      } catch (e) {
        if (!e.answer?.login) throw e;
        login = null; // that login didn't work
        await askLogin(e.message);
        return attempt();
      }
    };
    R.busy(where, 'Changing Group Policy');
    try {
      return await D.confirmed(attempt);
    } finally {
      R.busy(where, '');
    }
  });
  R.gpoChange = change; // Devices, Deploy the client (deploy.js)
  // Group Policy Management on this server: the command shows first, then runs as SYSTEM while the page asks how it's
  // going. The Group Policy page and Devices, Deploy the client both offer it (rabbidlab:gpmc when it's done).
  const installGpmc = async (where) => {
    let s;
    try {
      s = await get('/api/gpo/gpmc');
    } catch (e) {
      return oops(e);
    }
    const download = s.command.startsWith('Add-WindowsCapability') ? ' It downloads from Windows Update.' : '';
    sure('Install Group Policy Management?', `This runs on ${s.machine}, the server ${R.app()} runs on, as SYSTEM. It can take a few minutes.${download}`, 'Install', async () => {
      try {
        await D.confirmed(() => send('/api/gpo/gpmc/install', {}));
      } catch (e) {
        return oops(e);
      }
      R.busy(where, 'Installing Group Policy Management', 'Starting');
      const poll = async () => {
        let now = null;
        try {
          now = await get('/api/gpo/gpmc');
        } catch {
          // Asked again below.
        }
        if (!now || now.running) {
          // How far Windows says it is, under the flask.
          if (now?.percent != null) R.busy(where, 'Installing Group Policy Management', `${now.percent}%`);
          return setTimeout(poll, 2000);
        }
        R.busy(where, '');
        gpmcWaits = !!now.restart;
        if (now.here) toast('Group Policy Management is installed.', 'Everything on the Group Policy pages works now.');
        else if (now.restart) toast('Group Policy Management is installed.', 'Windows needs a restart to finish. Restart this server and the Group Policy pages work.', { stay: true });
        else toast("Group Policy Management didn't install.", (now.output || '').split('\n').filter(Boolean).pop() || 'See the activity log.', { tone: 'warn', stay: true });
        document.dispatchEvent(new CustomEvent('rabbidlab:gpmc'));
      };
      setTimeout(poll, 2000);
    }, { code: s.command, safe: true });
  };
  R.installGpmc = installGpmc;
  // Installed, with Windows waiting for a restart: asked once when a page finds it missing, and known after an install.
  let gpmcWaits = null;
  const gpmcWaiting = () => {
    if (gpmcWaits === null) {
      gpmcWaits = false;
      get('/api/gpo/gpmc').then((s) => {
        if (!s.restart) return;
        gpmcWaits = true;
        document.dispatchEvent(new CustomEvent('rabbidlab:gpmc'));
      }).catch(() => { /* the note says it's missing */ });
    }
    return gpmcWaits;
  };
  R.gpmcWaiting = gpmcWaiting; // deploy.js
  document.addEventListener('click', (e) => { if (e.target.closest('[data-install-gpmc]')) installGpmc(e.target.closest('.lib') || document.body); });
  document.addEventListener('rabbidlab:gpmc', () => {
    gp = null;
    problem = '';
    if (R.where()[0] === 'gpo') load();
  });

  // --- The page: four lists down the left, one grid ----------------------------------------------------------------
  const VIEWS = [['all', 'All GPOs', () => true], ['linked', 'Linked', (g) => linksOf(g.id).length > 0], ['unlinked', 'Not linked', (g) => !linksOf(g.id).length, 'amber'],
    ['off', 'Settings turned off', (g) => g.status !== 0], ['recent', 'Changed this week', (g) => g.changed && Date.now() - new Date(g.changed) < WEEK]];
  const place = (p) => `<div class="who"><span class="pic">${svg(p.domain ? 'globe' : 'folder')}</span><b class="ell">${esc(p.name)}</b></div>`;
  const LISTS = {
    gpos: {
      name: 'Group Policy Objects', icon: 'policy', lead: 'Every GPO in the domain, where it is linked and what it sets.', noun: 'GPOs', make: () => has('gpo.change') && 'New GPO',
      rows: () => (gp?.gpos || []).filter((VIEWS.find((v) => v[0] === view) || VIEWS[0])[2]), sort: { key: 'name', dir: 1 }, key: (g) => g.id,
      cols: [
        { key: 'name', label: 'GPO', w: 240, fixed: true, get: (g) => g.name, cell: (g) => `<div class="who"><span class="pic">${svg('policy')}</span><b class="ell">${esc(g.name)}</b></div>` },
        { key: 'status', label: 'Status', w: 175, on: true, get: (g) => statusOf(g)[0], cell: (g) => badge(...statusOf(g)) },
        { key: 'links', label: 'Linked to', w: 110, on: true, get: (g) => linksOf(g.id).length, text: (g) => linksOf(g.id).map((l) => l.place.path).join('; '),
          cell: (g) => (linksOf(g.id).length ? esc(plural(linksOf(g.id).length, 'place')) : badge('Not linked', 'amber')) },
        { key: 'wmi', label: 'WMI filter', w: 150, on: true, get: (g) => filterOf(g.wmi)?.name || '', cell: (g) => (g.wmi ? `<span class="ell">${esc(filterOf(g.wmi)?.name || 'Missing filter')}</span>` : muted('None')) },
        { key: 'changed', label: 'Changed', w: 120, on: true, get: (g) => (g.changed ? new Date(g.changed).getTime() : 0), text: (g) => g.changed || '', cell: (g) => esc(since(g.changed)) },
        { key: 'created', label: 'Created', w: 120, get: (g) => (g.created ? new Date(g.created).getTime() : 0), text: (g) => g.created || '', cell: (g) => esc(day(g.created)) },
        { key: 'cv', label: 'Computer version', w: 130, get: (g) => g.computerVersion },
        { key: 'uv', label: 'User version', w: 110, get: (g) => g.userVersion },
        { key: 'id', label: 'Unique ID', w: 310, get: (g) => g.id, cell: (g) => `<span class="mono muted ell">${esc(g.id)}</span>` },
      ],
    },
    linked: {
      name: 'Where linked', icon: 'tree', lead: 'The domain and each OU, with the GPOs linked to it in the order they apply.', noun: 'places', make: () => has('gpo.link') && 'Link a GPO',
      rows: () => gp?.places || [], sort: null, key: (p) => p.dn,
      cols: [
        { key: 'name', label: 'Domain or OU', w: 220, fixed: true, get: (p) => p.name, cell: place },
        { key: 'path', label: 'Path', w: 260, on: true, get: (p) => p.path, cell: (p) => `<span class="muted ell">${esc(p.path)}</span>` },
        { key: 'count', label: 'GPOs linked', w: 110, on: true, get: (p) => p.links.length },
        { key: 'blocked', label: 'Inheritance', w: 130, on: true, get: (p) => (p.blocked ? 'Blocked' : 'Inherits'), cell: (p) => (p.blocked ? badge('Blocked', 'amber') : muted('Inherits')) },
        { key: 'dn', label: 'Distinguished name', w: 320, get: (p) => p.dn, cell: (p) => `<span class="mono muted ell">${esc(p.dn)}</span>` },
      ],
    },
    wmi: {
      name: 'WMI filters', icon: 'filter', lead: 'Queries a GPO can be limited by: it applies only where the query finds something.', noun: 'filters', make: () => has('gpo.change') && 'New WMI filter',
      rows: () => gp?.filters || [], sort: { key: 'name', dir: 1 }, key: (w) => w.id,
      cols: [
        { key: 'name', label: 'Filter', w: 200, fixed: true, get: (w) => w.name, cell: (w) => `<div class="who"><span class="pic">${svg('filter')}</span><b class="ell">${esc(w.name)}</b></div>` },
        { key: 'query', label: 'Query', w: 420, on: true, get: (w) => w.query, cell: (w) => `<span class="mono muted ell">${esc(w.query)}</span>` },
        { key: 'used', label: 'Used by', w: 100, on: true, get: (w) => usedBy(w).length, text: (w) => usedBy(w).map((g) => g.name).join('; '), cell: (w) => esc(plural(usedBy(w).length, 'GPO')) },
        { key: 'changed', label: 'Changed', w: 120, on: true, get: (w) => (w.changed ? new Date(w.changed).getTime() : 0), text: (w) => w.changed || '', cell: (w) => esc(day(w.changed)) },
        { key: 'space', label: 'Namespace', w: 130, get: (w) => w.space },
        { key: 'author', label: 'Author', w: 160, get: (w) => w.author || '' },
      ],
    },
    backups: {
      name: 'Backups', icon: 'history', lead: 'Copies of GPOs kept on this server, to put one back the way it was. Deleting a GPO backs it up first.', noun: 'backups', make: () => has('gpo.change') && 'Back up all GPOs',
      rows: () => gp?.backups || [], sort: { key: 'at', dir: -1 }, key: (b) => b.id,
      cols: [
        { key: 'name', label: 'GPO', w: 230, fixed: true, get: (b) => b.name, cell: (b) => `<div class="who"><span class="pic">${svg('policy')}</span><b class="ell">${esc(b.name)}</b></div>` },
        { key: 'at', label: 'Backed up', w: 170, on: true, get: (b) => new Date(b.at).getTime(), text: (b) => b.at, cell: (b) => esc(stamp(b.at)) },
        { key: 'by', label: 'By', w: 120, on: true, get: (b) => b.by || '' },
        { key: 'comment', label: 'Comment', w: 280, on: true, get: (b) => b.comment || '' },
        { key: 'now', label: 'The GPO now', w: 130, on: true, get: (b) => (gpoOf(b.gpo) ? 'In the domain' : 'Deleted'), cell: (b) => (gpoOf(b.gpo) ? muted('In the domain') : badge('Deleted', 'amber')) },
        { key: 'id', label: 'Backup ID', w: 310, get: (b) => b.id, cell: (b) => `<span class="mono muted ell">${esc(b.id)}</span>` },
      ],
    },
  };
  let list = 'gpos';
  let view = 'all';
  const L = () => LISTS[list];
  const picked = new Set(); // ticked rows of the list showing
  const canTick = () => (list === 'gpos' ? has('gpo.change') || has('gpo.link') || has('gpo.delete') || has('seats.actions')
    : list === 'linked' ? has('gpo.link') : list === 'wmi' ? has('gpo.change') : true);
  const g0 = R.grid({
    el: $('gpo-table'), store: 'rabbidlab.gpo', key: () => list, cols: () => L().cols, sort: () => L().sort,
    lead: () => (canTick() ? { w: 18, head: '<label class="tk"><input type="checkbox" data-gpo-tick-all aria-label="Select all"></label>',
      cell: (x) => `<label class="tk"><input type="checkbox" data-gpo-tick="${esc(L().key(x))}" aria-label="Select"></label>` } : null),
    row: (x) => ` data-gpo-row="${esc(L().key(x))}"`, empty: () => (problem ? '' : gp ? 'Nothing here.' : 'Reading Group Policy.'), redraw: () => draw(),
  });
  // The filter bar over the list (filter.js): a search, a chip for each column worth filtering by, and saved filters.
  const gpoBar = R.filter.bar({ el: $('gpo-fbar'), store: 'rabbidlab.gpo', key: () => list, grid: () => g0, noun: () => L().noun, rows: () => L().rows(), redraw: () => draw() });
  const shown = () => L().rows().filter(gpoBar.test());
  const stat = (num, icon, label) => `<div><b>${num}</b><span>${svg(icon)}${esc(label)}</span></div>`;
  let tiled = false;
  let drawnCmds = null; // what the bar under the banner has, so a redraw keeps its buttons
  const draw = () => {
    if (!tiled) {
      R.scatter?.($('gpo-hero'), 20261116);
      tiled = true;
    }
    const setup = ad && !ad.state;
    $('gpo-setup').hidden = !setup;
    if (setup) R.adSetupCard?.($('gpo-setup'), ad);
    const l = L();
    $('gpo-eyebrow').textContent = gp?.domain ? `${gp.domain} · Directory` : 'Directory';
    $('gpo-state').textContent = `${l.name}.`;
    $('gpo-lead').textContent = l.lead;
    const all = gp?.gpos || [];
    $('gpo-stats').innerHTML = stat(all.length, 'policy', 'GPOs') + stat(all.filter((g) => linksOf(g.id).length).length, 'link', 'Linked')
      + stat(all.filter((g) => !linksOf(g.id).length).length, 'alert', 'Not linked') + stat(all.filter(VIEWS[4][2]).length, 'clock', 'Changed this week');
    document.querySelectorAll('#gpo-rail a[data-gpo]').forEach((a) => (a.dataset.gpo === list ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    $('gpo-views').innerHTML = list !== 'gpos' ? '' : VIEWS.map((v) => {
      const count = all.filter(v[2]).length;
      return `<a href="#gpo/gpos/${v[0]}"${v[0] === view ? ' aria-current="page"' : ''}><span class="nm">${esc(v[1])}</span><small class="${count && v[3] ? `tone ${v[3]}` : ''}">${gp ? count : ''}</small></a>`;
    }).join('');
    $('gpo-n-gpos').textContent = gp ? all.length : '';
    $('gpo-n-linked').textContent = gp ? gp.places.length : '';
    $('gpo-n-wmi').textContent = gp ? gp.filters.length : '';
    $('gpo-n-backups').textContent = gp ? gp.backups.length : '';
    const rows = shown();
    $('gpo-tabs').innerHTML = `<a aria-current="true">${svg(l.icon)}${esc(l.name)} <small>${gp ? rows.length : ''}</small></a>`;
    // The bar under the banner: New makes what the open list holds, its arrow has what every list makes; both wait until
    // Group Policy has been read.
    const makes = Object.entries(LISTS).filter(([, x]) => x.make());
    const here = makes.find(([k]) => k === list) || makes[0];
    const off = gp ? '' : ' disabled';
    const cmds = !here ? '' : `<div class="cmd-pair"><button type="button" class="cmd" data-make="${here[0]}" data-tip="${esc(here[1].make())}"${off}>${svg('plus')}<span>New</span></button><i class="cmd-rule"></i>`
      + `<button type="button" class="cmd cmd-arrow" data-make-more aria-haspopup="menu" data-tip="New GPO, link, WMI filter or backup" aria-label="Everything Group Policy makes"${off}>${svg('chevron-down', 'chev')}</button></div>`;
    if (cmds !== drawnCmds) $('gpo-cmd').innerHTML = drawnCmds = cmds;
    $('gpo-cmdbar').hidden = !!setup;
    // The filter bar, and the grid's part of the list's bar (Group by, the count, Compact or Relaxed).
    gpoBar.draw();
    g0.tools($('gpo-tools'), $('gpo-rows'));
    $('gpo-problem').hidden = !problem;
    $('gpo-problem').textContent = problem;
    $('gpo-note').hidden = !(gp && !gp.gpmc && list !== 'wmi' && list !== 'linked');
    const waits = gp && !gp.gpmc && gpmcWaiting();
    $('gpo-note').innerHTML = waits ? esc('Group Policy Management is installed, and Windows needs a restart to finish. Restart this server and GPO settings, new GPOs, copies, deletes and backups work.')
      : esc('Group Policy Management isn\'t on this server, so GPO settings, who a GPO applies to, new GPOs, copies, deletes and backups aren\'t here yet. Links, names, status and WMI filters work without it.')
      + (has('gpo.change') ? ' <button type="button" class="linkish" data-install-gpmc>Install it…</button>' : '');
    if (gp || problem) g0.draw(rows);
    else R.skeleton($('gpo-table'), 'rows', 6);
    drawTicks();
    document.querySelectorAll('#gpo-table [data-gpo-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('gpo', `${list}:${el.dataset.gpoRow}`)));
  };
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'gpo') return;
    const [l, v] = e.detail[2] || [];
    const next = LISTS[l] ? l : 'gpos';
    if (next !== list) {
      if (panel.is('gpo')) panel.close();
      picked.clear();
    }
    list = next;
    view = list === 'gpos' && VIEWS.some((x) => x[0] === v) ? v : 'all';
    draw();
    if (!gp && !loading && !problem) load();
  });
  // Set up, disconnected, or Not now on the card: read again.
  document.addEventListener('rabbidlab:ad', () => {
    ad = null;
    gp = null;
    problem = '';
    if (R.where()[0] === 'gpo') load();
  });
  $('gpo-export').addEventListener('click', () => g0.csv(g0.sorted(shown()), `group-policy-${list}`));
  $('gpo-refresh').addEventListener('click', () => R.refresh($('gpo-refresh'), $('gpo-lib'), 'Reading Group Policy again…', () => {
    Object.keys(detail).forEach((k) => delete detail[k]);
    return load();
  }));
  const makeIn = (k) => (k === 'gpos' ? newGpo() : k === 'linked' ? linkTo(null, null) : k === 'wmi' ? editFilter(null) : backupAll());
  $('gpo-cmd').addEventListener('click', (e) => {
    if (!gp) return;
    const b = e.target.closest('[data-make]');
    if (b) makeIn(b.dataset.make);
    else if (e.target.closest('[data-make-more]')) {
      const made = Object.entries(LISTS).filter(([, x]) => x.make()).map(([k, x]) => ({ label: x.make(), icon: x.icon, run: () => makeIn(k) }));
      R.picker.menu(e.target.closest('.cmd-pair'), made.flatMap((m, i) => (i && m.label === LISTS.backups.make() ? ['-', m] : [m])), { key: 'gpo.new' });
    }
  });

  // --- Ticks, and what Actions does with them --------------------------------------------------------------------
  const tickedRows = () => L().rows().filter((x) => picked.has(L().key(x)));
  const drawTicks = () => {
    const boxes = [...$('gpo-table').querySelectorAll('[data-gpo-tick]')];
    boxes.forEach((b) => {
      b.checked = picked.has(b.dataset.gpoTick);
      b.closest('.tr')?.classList.toggle('picked', b.checked);
    });
    const all = $('gpo-table').querySelector('[data-gpo-tick-all]');
    if (all) {
      const on = boxes.filter((b) => b.checked).length;
      all.checked = !!boxes.length && on === boxes.length;
      all.indeterminate = on > 0 && on < boxes.length;
    }
    const n = tickedRows().length;
    const act = $('gpo-act');
    act.hidden = !canTick() || !gp;
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n.toLocaleString();
    act.dataset.tip = n ? `${n} ticked` : `Tick ${L().noun} first`;
  };
  let lastTick = null;
  $('gpo-table').addEventListener('click', (e) => {
    const box = e.target.closest('.tk')?.querySelector('input');
    if (box) {
      if (e.target !== box) return; // the label's own click comes through the box
      const boxes = [...$('gpo-table').querySelectorAll('[data-gpo-tick]')];
      const set = (b) => (box.checked ? picked.add(b.dataset.gpoTick) : picked.delete(b.dataset.gpoTick));
      if (box.dataset.gpoTickAll !== undefined) boxes.forEach(set);
      else {
        const i = boxes.indexOf(box);
        const from = e.shiftKey && lastTick !== null ? Math.min(lastTick, i) : i;
        const to = e.shiftKey && lastTick !== null ? Math.max(lastTick, i) : i;
        boxes.slice(from, to + 1).forEach(set);
        lastTick = i;
      }
      drawTicks();
      return;
    }
    const row = e.target.closest('[data-gpo-row]');
    if (row && !e.target.closest('.th')) openRow(list, row.dataset.gpoRow);
  });

  const menu = $('gpo-menu');
  const item = (act, icon, label, hint = '', danger = false) => `<button type="button" role="menuitem" class="am-link${danger ? ' out' : ''}" data-gpo-act="${act}">${svg(icon)}<span>${esc(label)}</span>${hint ? `<small>${esc(hint)}</small>` : ''}</button>`;
  const named = (name, items) => (items ? `<div class="am-group"><p class="am-head">${name}</p>${items}</div>` : '');
  let menuFor = null; // { kind: 'ticked' | 'gpo' | 'place' | 'filter' | 'backup' | 'link', ... }
  const showMenu = (button, html, width) => {
    menu.innerHTML = html;
    const r = button.getBoundingClientRect();
    menu.style.cssText = `position:fixed;inset:auto;width:${width}px;margin:0;left:${Math.max(8, Math.min(r.left, innerWidth - width - 8))}px;top:${r.bottom + 6}px`;
    menu.showPopover();
    if (r.bottom + 6 + menu.offsetHeight > innerHeight - 8 && r.top > innerHeight - r.bottom) menu.style.top = `${Math.max(8, r.top - 6 - menu.offsetHeight)}px`;
  };
  $('gpo-act').addEventListener('click', () => {
    const rows = tickedRows();
    menuFor = { kind: 'ticked', list, rows };
    const sel = named('Selection', item('export', 'csv', 'Export these') + item('clear', 'x', 'Clear the ticks'));
    let html = '';
    if (list === 'gpos') {
      html = `<div class="am-links">${named('GPOs', (has('gpo.link') ? item('link-many', 'link', 'Link to an OU…') : '') + (has('gpo.change') && gp?.gpmc ? item('backup-many', 'history', 'Back up', String(rows.length)) : '')
        + (has('seats.actions') ? item('gpupdate-many', 'refresh', 'Run gpupdate where they apply…') : ''))}${sel}</div>`
        + (has('gpo.delete') && gp?.gpmc ? `<div class="am-links">${item('delete-many', 'trash', 'Delete GPOs…', String(rows.length), true)}</div>` : '');
    } else if (list === 'linked') {
      html = `<div class="am-links">${named('Places', item('link-here-many', 'link', 'Link a GPO…') + item('block-many', 'lock', 'Block inheritance') + item('inherit-many', 'unlock', 'Inherit again'))}${sel}</div>`;
    } else if (list === 'wmi') {
      html = `<div class="am-links">${sel}</div><div class="am-links">${item('delete-filters', 'trash', 'Delete filters…', String(rows.length), true)}</div>`;
    } else html = `<div class="am-links">${sel}</div>`;
    showMenu($('gpo-act'), html, 270);
    const r = $('gpo-act').getBoundingClientRect();
    menu.style.left = `${Math.max(8, Math.min(r.left, innerWidth - 278))}px`; // under the button, from its left edge
  });

  // --- A row's side panel: a GPO, a place, a filter or a backup --------------------------------------------------
  let open = null; // { kind, id }
  let tab = 'details';
  const head = (icon, title, sub) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(title)}</h2><p>${esc(sub)}</p></div>`
    + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`;
  const actsButton = (show = true) => (show ? `<div class="dw-bar"><button type="button" class="small dw-acts" data-gpo-menu aria-haspopup="menu">${svg('menu')}Actions${svg('chevron-down', 'chev')}</button></div>` : '');
  const tabs = (list2) => `<div class="tabs2 dw-tabs" role="tablist">${list2.map(([k, label, count = '']) => `<a role="tab" data-gpo-tab="${k}"${tab === k ? ' aria-current="true"' : ''}>${label}${count === '' || count === 0 ? '' : ` <small>${count}</small>`}</a>`).join('')}</div>`;
  const panelGrid = (id, key, cols, rows, sort, tail = null) => {
    const el = panel.body.querySelector(`#${id}`);
    if (el) R.grid({ el, key, cols, sort, tail, headButton: true, row: (x) => (x.rowAttr ? ` ${x.rowAttr}` : ''), empty: 'Nothing here.', redraw: () => drawPanel() }).draw(rows);
  };
  const openRow = (kind, key) => {
    const pk = `${kind}:${key}`;
    if (panel.shows('gpo', pk)) {
      panel.close();
      return;
    }
    open = { kind, id: key };
    tab = 'details';
    if (!drawPanel()) return;
    panel.open('gpo', pk);
    draw();
  };
  const openGpo = (id) => {
    if (location.hash.split('/')[0] !== '#gpo') location.hash = '#gpo';
    list = 'gpos';
    open = { kind: 'gpos', id };
    tab = 'details';
    if (drawPanel()) panel.open('gpo', `gpos:${id}`);
    draw();
  };
  const loadDetail = async (id) => {
    if (detail[id]?.data || detail[id]?.loading || !gp?.gpmc) return;
    detail[id] = { loading: true };
    try {
      detail[id] = { data: await get(`/api/gpo/${encodeURIComponent(id)}`) };
    } catch (e) {
      detail[id] = { error: e.message };
    }
    if (panel.shows('gpo', `gpos:${id}`)) drawPanel();
  };
  // Draws the open row's panel; false when it isn't there any more.
  const drawPanel = () => {
    if (!open || !gp) return false;
    if (open.kind === 'linked') return drawPlace();
    if (open.kind === 'wmi') return drawFilter();
    if (open.kind === 'backups') return drawBackup();
    const g = gpoOf(open.id);
    if (!g) {
      if (panel.is('gpo')) panel.close();
      return false;
    }
    loadDetail(g.id);
    const d = detail[g.id];
    const links = linksOf(g.id);
    const where = links.length ? `Linked to ${plural(links.length, 'place')}` : 'Not linked anywhere';
    const waitOr = (body) => (!gp.gpmc ? none('Group Policy Management isn\'t on this server, so this can\'t be read here.')
      : d?.error ? `<p class="hint problem">${esc(d.error)}</p>` : !d?.data ? '<div data-gpo-skel></div>' : body(d.data));
    const versions = (ad2, sys) => esc(sys == null ? `${ad2}` : ad2 === sys ? `${ad2} (AD and SYSVOL match)` : `AD ${ad2}, SYSVOL ${sys}: not in step yet`);
    let pane;
    if (tab === 'links') {
      pane = section(`Links <small>${links.length}</small>`, links.length ? '<div class="table" id="gpo-p-links"></div>' : none('It isn\'t linked anywhere, so it applies to nothing.'),
        has('gpo.link') ? `<button type="button" class="small" data-gpo-do="link">${svg('plus')}Link to an OU</button>` : '');
    } else if (tab === 'settings') {
      pane = waitOr((x) => section(`Computer configuration <small>${x.computer.length}</small>`, x.computer.length ? '<div class="table" id="gpo-p-comp"></div>' : none('No computer settings.'),
        computerOn(g) ? '' : badge('Turned off', 'amber'))
        + section(`User configuration <small>${x.user.length}</small>`, x.user.length ? '<div class="table" id="gpo-p-user"></div>' : none('No user settings.'), userOn(g) ? '' : badge('Turned off', 'amber'))
        + `<p class="dw-how">From the GPO's settings report. Settings are changed in the Group Policy editor (gpmc.msc). <a href="/api/gpo/${encodeURIComponent(g.id)}/report">Save the full report</a></p>`);
    } else if (tab === 'who') {
      pane = waitOr((x) => {
        const applies = x.who.filter((t) => t.access === 'apply' && !t.denied);
        const changes = x.who.filter((t) => ['edit', 'full', 'custom'].includes(t.access) || t.denied);
        const reads = x.who.filter((t) => t.access === 'read' && !t.denied);
        const add = has('gpo.change') ? '<div class="adder gpo-add"><label class="find"><svg><use href="#i-search"/></svg><input type="search" id="gpo-who-find" data-find placeholder="A group, user or computer to apply it to" aria-label="Who to apply it to" autocomplete="off" spellcheck="false"></label><div class="drop" data-drop hidden></div></div>' : '';
        const out = (t) => (has('gpo.change') ? `<button type="button" class="at-more dw-x" data-gpo-unapply="${esc(t.name)}" data-tip="Take out" aria-label="Stop it applying to ${esc(t.name)}">${svg('x')}</button>` : '');
        return section(`Applies to <small>${applies.length}</small>`, `<p class="dw-how">Security filtering: it applies only to these groups, users and computers, in the places it's linked.</p>${add}`
          + (applies.length ? rows2(applies.map((t) => [t.name, 'Read and apply', out(t)])) : none('Nobody: it applies to nothing.')))
          + (reads.length ? section(`Can read it <small>${reads.length}</small>`, rows2(reads.map((t) => [t.name, 'Read only']))) : '')
          + section(`Who can change it <small>${changes.length}</small>`, rows2(changes.map((t) => [t.name, t.denied ? 'Denied' : { edit: 'Edit settings', full: 'Edit settings, delete, change security', custom: 'Custom' }[t.access]])))
          + `<p class="dw-how">Who can change it is set in Group Policy Management, on the GPO's Delegation tab.${x.owner ? ` Owner: ${esc(x.owner)}.` : ''}</p>`;
      });
    } else {
      const x = d?.data;
      pane = section('Details', facts([['Status', badge(...statusOf(g))], ['Linked to', esc(where)], ['WMI filter', g.wmi ? esc(filterOf(g.wmi)?.name || 'A filter that isn\'t there any more') : muted('None')],
        x?.owner ? ['Owner', esc(x.owner)] : null, ['Created', esc(stamp(g.created))], ['Changed', esc(`${stamp(g.changed)} (${since(g.changed)})`)],
        ['Computer version', versions(g.computerVersion, x?.sysvolComputer)], ['User version', versions(g.userVersion, x?.sysvolUser)],
        ['Computer settings', esc(g.hasComputer ? (computerOn(g) ? 'Some, on' : 'Some, turned off') : 'None')], ['User settings', esc(g.hasUser ? (userOn(g) ? 'Some, on' : 'Some, turned off') : 'None')],
        ['Unique ID', `<span class="mono">${esc(g.id)}</span>`]]));
    }
    const count = (k) => (d?.data ? d.data[k].length : '');
    // The facts looked at first, in a row under the name.
    const strip = `<div class="dw-strip">${[['Status', badge(...statusOf(g))], ['Linked to', links.length ? esc(plural(links.length, 'place')) : badge('Not linked', 'amber')],
      ['WMI filter', g.wmi ? esc(filterOf(g.wmi)?.name || 'Missing filter') : muted('None')], ['Changed', esc(since(g.changed))]].map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${v}</span></div>`).join('')}</div>`;
    panel.body.innerHTML = head('policy', g.name, where) + actsButton(has('gpo.change') || has('gpo.link') || has('gpo.delete') || has('seats.actions')) + strip
      + tabs([['details', 'Details'], ['links', 'Links', links.length], ['settings', 'Settings', d?.data ? count('computer') + count('user') : ''], ['who', 'Delegation']]) + pane;
    const skel = panel.body.querySelector('[data-gpo-skel]');
    if (skel) R.skeleton(skel, 'rows', 4);
    if (tab === 'links' && links.length) {
      panelGrid('gpo-p-links', 'gpo-links', [
        { key: 'where', label: 'Linked to', w: 150, fixed: true, get: (l) => l.place.path, cell: (l) => `<span class="ell" title="${esc(l.place.path)}">${esc(l.place.name)}</span>` },
        { key: 'order', label: 'Order', w: 55, on: true, get: (l) => l.order },
        { key: 'enforced', label: 'Enforced', w: 80, on: true, get: (l) => (l.enforced ? 1 : 0), cell: (l) => (l.enforced ? badge('Enforced', 'amber') : muted('No')) },
        { key: 'on', label: 'Link', w: 60, on: true, get: (l) => (l.enabled ? 1 : 0), cell: (l) => (l.enabled ? badge('On', 'green') : badge('Off', 'grey')) },
      ], links.map((l) => ({ ...l, rowAttr: `data-gpo-link="${esc(g.id)}|${esc(l.place.dn)}"` })), { key: 'where', dir: 1 }, linkTail());
    }
    if (tab === 'settings' && d?.data) {
      const cols = [{ key: 'setting', label: 'Setting', w: 230, fixed: true, get: (s) => s.name, cell: (s) => `<span class="ell" title="${esc(s.name)}">${esc(s.name)}</span>` },
        { key: 'value', label: 'Value', w: 180, on: true, get: (s) => s.value, cell: (s) => `<span class="ell" title="${esc(s.value)}">${esc(s.value)}</span>` },
        { key: 'area', label: 'Where', w: 280, get: (s) => s.area, cell: (s) => `<span class="muted ell" title="${esc(s.area)}">${esc(s.area)}</span>` }];
      panelGrid('gpo-p-comp', 'gpo-settings', cols, d.data.computer, null);
      panelGrid('gpo-p-user', 'gpo-settings', cols, d.data.user, null);
    }
    return true;
  };
  const linkTail = () => (has('gpo.link') ? { w: 34, cell: () => `<button type="button" class="at-more dw-x" data-gpo-link-menu data-tip="Change the link" aria-label="Change the link">${svg('dots')}</button>` } : null);
  const drawPlace = () => {
    const p = placeOf(open.id);
    if (!p) {
      if (panel.is('gpo')) panel.close();
      return false;
    }
    panel.body.innerHTML = head(p.domain ? 'globe' : 'folder', p.name, p.path) + actsButton(has('gpo.link'))
      + section(`Linked here <small>${p.links.length}</small>`, p.links.length ? '<div class="table" id="gpo-p-here"></div>' : none('No GPO is linked here.'))
      + section('Inheritance', `<p class="dw-how">${p.domain ? 'GPOs linked to the domain apply to everything in it, unless an OU blocks them and they aren\'t enforced.'
        : p.blocked ? 'Blocked: GPOs linked higher up don\'t apply here, unless they\'re enforced.' : 'GPOs linked higher up apply here too, before the ones linked here, so these win.'}</p>`);
    if (p.links.length) {
      panelGrid('gpo-p-here', 'gpo-here', [
        { key: 'order', label: 'Order', w: 55, fixed: true, get: (l) => l.order },
        { key: 'gpo', label: 'GPO', w: 160, on: true, get: (l) => nameOf(l.gpo), cell: (l) => `<span class="ell">${esc(nameOf(l.gpo))}</span>` },
        { key: 'enforced', label: 'Enforced', w: 80, on: true, get: (l) => (l.enforced ? 1 : 0), cell: (l) => (l.enforced ? badge('Enforced', 'amber') : muted('No')) },
        { key: 'on', label: 'Link', w: 60, on: true, get: (l) => (l.enabled ? 1 : 0), cell: (l) => (l.enabled ? badge('On', 'green') : badge('Off', 'grey')) },
      ], p.links.map((l) => ({ ...l, place: p, rowAttr: `data-gpo-link="${esc(l.gpo)}|${esc(p.dn)}"` })), { key: 'order', dir: 1 }, linkTail());
    }
    return true;
  };
  const drawFilter = () => {
    const w = filterOf(open.id);
    if (!w) {
      if (panel.is('gpo')) panel.close();
      return false;
    }
    const users = usedBy(w);
    panel.body.innerHTML = head('filter', w.name, users.length ? `Used by ${plural(users.length, 'GPO')}` : 'Not used by any GPO') + actsButton(has('gpo.change'))
      + section('Details', facts([['Namespace', esc(w.space)], ['Query', `<span class="mono">${esc(w.query)}</span>`], w.queries > 1 ? ['Queries', esc(`${w.queries}; editing keeps the first`)] : null,
        ['Description', w.description ? esc(w.description) : muted('None')], ['Author', w.author ? esc(w.author) : muted('Not said')], ['Changed', esc(stamp(w.changed))], ['ID', `<span class="mono">${esc(w.id)}</span>`]]))
      + section(`Used by <small>${users.length}</small>`, users.length ? rows2(users.map((g) => [g.name, statusOf(g)[0], svg('right', 'acc-go'), `data-gpo-open="${esc(g.id)}"`])) : none('No GPO uses it.'));
    return true;
  };
  const drawBackup = () => {
    const b = gp.backups.find((x) => x.id === open.id);
    if (!b) {
      if (panel.is('gpo')) panel.close();
      return false;
    }
    const now = gpoOf(b.gpo);
    panel.body.innerHTML = head('history', b.name, `Backed up ${stamp(b.at)}`) + actsButton(has('gpo.change') && gp.gpmc)
      + section('Details', facts([['GPO', esc(b.name)], ['Backed up', esc(stamp(b.at))], ['By', b.by ? esc(b.by) : muted('Not said')], ['Comment', b.comment ? esc(b.comment) : muted('None')],
        ['The GPO now', now ? esc(`In the domain as ${now.name}`) : badge('Deleted', 'amber')], ['GPO ID', `<span class="mono">${esc(b.gpo)}</span>`], ['Backup ID', `<span class="mono">${esc(b.id)}</span>`]]))
      + `<p class="dw-how">Restoring puts its settings back, and brings it back with the same ID when it was deleted. Links aren't part of a backup: they stay as they are.</p>`;
    return true;
  };
  panel.el.addEventListener('close', () => { if (R.where()[0] === 'gpo') draw(); });

  // The panel's Actions menu, and a link's own menu.
  const openPanelMenu = (button) => {
    if (open.kind === 'gpos') {
      const g = gpoOf(open.id);
      const c = has('gpo.change');
      const gm = c && gp.gpmc;
      const left = named('Links', has('gpo.link') ? item('link', 'link', 'Link to an OU…') : '')
        + named('Settings', c ? item('comp', 'monitor', computerOn(g) ? 'Turn computer settings off…' : 'Turn computer settings on…') + item('user', 'user', userOn(g) ? 'Turn user settings off…' : 'Turn user settings on…') + item('wmi', 'filter', 'WMI filter…') : '');
      const right = named('GPO', (c ? item('rename', 'edit', 'Rename…') : '') + (gm ? item('copy', 'copy', 'Copy…') + item('backup', 'history', 'Back up…') + item('restore', 'refresh', 'Restore from a backup…') : ''))
        + named('Report', (gp.gpmc ? item('report', 'download', 'Save settings report') : '') + (has('seats.actions') ? item('gpupdate', 'refresh', 'Run gpupdate…') : ''));
      const cols = [left, right].filter(Boolean);
      menuFor = { kind: 'gpo', id: g.id };
      showMenu(button, (cols.length ? `<div class="am-cols">${cols.map((x) => `<div class="am-links">${x}</div>`).join('')}</div>` : '')
        + (has('gpo.delete') && gp.gpmc ? `<div class="am-links">${item('delete', 'trash', 'Delete GPO…', '', true)}</div>` : ''), cols.length > 1 ? 500 : 260);
    } else if (open.kind === 'linked') {
      const p = placeOf(open.id);
      menuFor = { kind: 'place', dn: p.dn };
      showMenu(button, `<div class="am-links">${item('link-here', 'link', 'Link a GPO…')}${p.domain ? '' : item('block', p.blocked ? 'unlock' : 'lock', p.blocked ? 'Inherit again' : 'Block inheritance')}</div>`, 250);
    } else if (open.kind === 'wmi') {
      menuFor = { kind: 'filter', id: open.id };
      showMenu(button, `<div class="am-links">${item('edit-filter', 'edit', 'Edit…')}</div><div class="am-links">${item('delete-filter', 'trash', 'Delete…', '', true)}</div>`, 220);
    } else if (open.kind === 'backups') {
      menuFor = { kind: 'backup', id: open.id };
      showMenu(button, `<div class="am-links">${item('restore-this', 'refresh', 'Restore…')}</div>`, 220);
    }
  };
  const openLinkMenu = (button, gpo, dn) => {
    const p = placeOf(dn);
    const l = p?.links.find((x) => x.gpo === gpo);
    if (!l) return;
    menuFor = { kind: 'link', gpo, dn };
    showMenu(button, `<div class="am-links">${item('link-on', l.enabled ? 'power' : 'check', l.enabled ? 'Turn the link off' : 'Turn the link on')}${item('link-enforce', 'lock', l.enforced ? 'Stop enforcing' : 'Enforce')}`
      + `${l.order > 1 ? item('link-up', 'chevron-up', 'Move up', `to ${l.order - 1}`) : ''}${l.order < p.links.length ? item('link-down', 'chevron-down', 'Move down', `to ${l.order + 1}`) : ''}</div>`
      + `<div class="am-links">${item('unlink', 'x', 'Remove the link…', '', true)}</div>`, 250);
  };
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('gpo')) return;
    const t = e.target.closest('[data-gpo-tab]');
    if (t) {
      tab = t.dataset.gpoTab;
      drawPanel();
      return;
    }
    if (e.target.closest('[data-gpo-menu]')) {
      openPanelMenu(e.target.closest('[data-gpo-menu]'));
      return;
    }
    const lm = e.target.closest('[data-gpo-link]');
    if (lm && has('gpo.link') && !e.target.closest('.th')) {
      const [gpo, dn] = lm.dataset.gpoLink.split('|');
      openLinkMenu(e.target.closest('[data-gpo-link-menu]') || lm, gpo, dn);
      return;
    }
    const o = e.target.closest('[data-gpo-open]');
    if (o) {
      openGpo(o.dataset.gpoOpen);
      return;
    }
    const un = e.target.closest('[data-gpo-unapply]');
    if (un) {
      const who = un.dataset.gpoUnapply;
      const g = gpoOf(open.id);
      sure(`Stop ${g.name} applying to ${who}?`, `${who} keeps reading it, so computers can still read it, but its settings stop applying there at the next refresh.`, 'Stop applying', () => apply(g, who, false));
      return;
    }
    const d = e.target.closest('[data-gpo-do]')?.dataset.gpoDo;
    if (d === 'link') linkTo(gpoOf(open.id), null);
  });

  // Who to apply it to: groups, users and computers from Active Directory, or a name typed as DOMAIN\name.
  R.dropdown(panel.body, {
    none: 'Nobody matches. Type DOMAIN\\name to add someone by name.',
    find: async (q) => {
      if (!panel.is('gpo') || open?.kind !== 'gpos') return [];
      const netbios = (detail[open.id]?.data?.who || []).map((t) => (t.name.includes('\\') ? t.name.slice(0, t.name.indexOf('\\')) : '')).find((p) => p && !/^(NT AUTHORITY|BUILTIN)$/i.test(p)) || '';
      const found = [];
      try {
        for (const m of await get(`/api/ad/find?q=${encodeURIComponent(q.includes('\\') ? q.slice(q.indexOf('\\') + 1) : q)}`)) {
          if (m.username && ['user', 'group', 'computer'].includes(m.kind)) {
            const name = netbios ? `${netbios}\\${m.username}` : m.username;
            found.push({ name, shown: m.name, kind: m.kind, sub: `${name} · ${{ user: 'User', group: 'Group', computer: 'Computer' }[m.kind]}` });
          }
        }
      } catch {
        // Typed names still work.
      }
      if (q.includes('\\') && !found.some((f) => f.name.toUpperCase() === q.trim().toUpperCase())) found.push({ name: q.trim(), shown: q.trim(), kind: 'group', sub: 'Add by name' });
      return found.slice(0, 30);
    },
    draw: (f, i) => R.option(` data-pick="${i}"`, `<span class="pic small">${svg(f.kind === 'group' ? 'users' : f.kind === 'computer' ? 'laptop' : 'user')}</span>`, f.shown, f.sub),
    pick: (f) => {
      const g = open?.kind === 'gpos' && gpoOf(open.id);
      if (g) apply(g, f.name, true);
    },
  });
  const apply = async (g, who, on) => {
    try {
      await change(`/api/gpo/${encodeURIComponent(g.id)}/apply`, { trustee: who, apply: on }, panel.body);
      delete detail[g.id];
      toast(on ? `${g.name} applies to ${who} now.` : `${g.name} doesn't apply to ${who} any more.`, 'At their next refresh, within 90 minutes, or Run gpupdate now.');
      drawPanel();
    } catch (e) {
      oops(e);
    }
  };

  // --- Doing things ----------------------------------------------------------------------------------------------
  const after = async (said, more = '') => {
    if (said) toast(said, more);
    await load();
  };
  menu.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-gpo-act]');
    if (!b || !menuFor) return;
    menu.hidePopover();
    const act = b.dataset.gpoAct;
    const m = menuFor;
    try {
      if (m.kind === 'ticked') await doTicked(act, m.rows);
      else if (m.kind === 'gpo') await doGpo(act, gpoOf(m.id));
      else if (m.kind === 'place') {
        const p = placeOf(m.dn);
        if (act === 'link-here') linkTo(null, p);
        else if (act === 'block') await block([p], !p.blocked);
      } else if (m.kind === 'filter') {
        if (act === 'edit-filter') editFilter(filterOf(m.id));
        else deleteFilters([filterOf(m.id)]);
      } else if (m.kind === 'backup') restore(gp.backups.find((x) => x.id === m.id));
      else if (m.kind === 'link') await doLink(act, m.gpo, m.dn);
    } catch (err) {
      oops(err);
    }
  });
  const doGpo = async (act, g) => {
    if (act === 'link') linkTo(g, null);
    else if (act === 'comp' || act === 'user') {
      const [c, u] = act === 'comp' ? [!computerOn(g), userOn(g)] : [computerOn(g), !userOn(g)];
      const what = act === 'comp' ? `computer settings ${c ? 'on' : 'off'}` : `user settings ${u ? 'on' : 'off'}`;
      sure(`Turn ${g.name}'s ${what}?`, 'It changes on every computer it applies to at their next refresh, within 90 minutes, or Run gpupdate now.', 'Change', async () => {
        try {
          await change(`/api/gpo/${encodeURIComponent(g.id)}/status`, { computer: c, user: u }, panel.body);
          await after(`${g.name}: ${what}.`);
        } catch (err) {
          oops(err);
        }
      });
    } else if (act === 'rename') ask('Rename the GPO', 'The new name shows in Group Policy Management too. Links and settings stay as they are.', `<label for="gpo-f-name">Name</label><input id="gpo-f-name" maxlength="200" value="${esc(g.name)}" autocomplete="off" spellcheck="false">`, async () => {
      const name = $('gpo-f-name').value.trim();
      await change(`/api/gpo/${encodeURIComponent(g.id)}/rename`, { name }, $('gpo-dialog'));
      await after(`Renamed to ${name}.`);
    }, 'Rename');
    else if (act === 'copy') ask('Copy the GPO', `A new GPO with ${g.name}'s settings, not linked anywhere. It gets the permissions a new GPO gets.`,
      `<label for="gpo-f-name">Name</label><input id="gpo-f-name" maxlength="200" value="${esc(`Copy of ${g.name}`)}" autocomplete="off" spellcheck="false">`, async () => {
        const name = $('gpo-f-name').value.trim();
        const r = await change('/api/gpo', { name, from: g.id }, $('gpo-dialog'));
        await after(`Copied as ${name}.`, r.warning || '');
      }, 'Copy');
    else if (act === 'backup') ask('Back up the GPO', `${g.name}'s settings and permissions, kept on this server under Backups.`,
      '<label for="gpo-f-comment">Comment</label><input id="gpo-f-comment" maxlength="200" placeholder="Before the firewall change" autocomplete="off">', async () => {
        await change(`/api/gpo/${encodeURIComponent(g.id)}/backup`, { comment: $('gpo-f-comment').value.trim() }, $('gpo-dialog'));
        await after(`Backed up ${g.name}.`, 'It shows under Backups.');
      }, 'Back up');
    else if (act === 'restore') {
      const mine = gp.backups.filter((x) => x.gpo === g.id);
      if (!mine.length) {
        toast(`${g.name} has no backup yet.`, 'Back it up first.', { tone: 'warn' });
        return;
      }
      ask(`Restore ${g.name}`, 'Its settings go back to the backup picked. Links stay as they are. What it has now is backed up first.',
        `<label for="gpo-f-backup">Backup</label><select id="gpo-f-backup">${mine.map((x) => `<option value="${esc(x.id)}">${esc(`${stamp(x.at)}${x.comment ? ` · ${x.comment}` : ''}`)}</option>`).join('')}</select>`, async () => {
          await change('/api/gpo/restore', { backup: $('gpo-f-backup').value }, $('gpo-dialog'));
          await after(`Restored ${g.name}.`, 'Computers get it at their next refresh, within 90 minutes.');
        }, 'Restore');
    } else if (act === 'wmi') ask('WMI filter', `${g.name} applies only where the filter's query finds something.`,
      `<label for="gpo-f-wmi">Filter</label><select id="gpo-f-wmi"><option value="">None</option>${gp.filters.map((w) => `<option value="${esc(w.id)}"${w.id === g.wmi ? ' selected' : ''}>${esc(w.name)}</option>`).join('')}</select>`, async () => {
        const id = $('gpo-f-wmi').value;
        await change(`/api/gpo/${encodeURIComponent(g.id)}/wmi`, { filter: id }, $('gpo-dialog'));
        await after(id ? `${g.name} uses ${filterOf(id)?.name}.` : `${g.name} has no WMI filter.`);
      }, 'Save');
    else if (act === 'report') location.href = `/api/gpo/${encodeURIComponent(g.id)}/report`;
    else if (act === 'gpupdate') gpupdate([g]);
    else if (act === 'delete') {
      const links = linksOf(g.id);
      sure(`Delete ${g.name}?`, `${links.length ? `It's linked to ${plural(links.length, 'place')}; those links go too. ` : 'It isn\'t linked anywhere. '}It's backed up first, so it can be restored from Backups.`, 'Delete', async () => {
        try {
          await change(`/api/gpo/${encodeURIComponent(g.id)}/delete`, {}, panel.body);
          panel.close();
          await after(`Deleted ${g.name}.`, 'A backup is under Backups.');
        } catch (err) {
          oops(err);
        }
      });
    }
  };
  const doLink = async (act, gpo, dn) => {
    const p = placeOf(dn);
    const l = p?.links.find((x) => x.gpo === gpo);
    if (!l) return;
    const body = { gpo, target: dn };
    if (act === 'unlink') {
      sure(`Unlink ${nameOf(gpo)} from ${p.name}?`, 'It stops applying there at the next refresh. The GPO itself stays.', 'Unlink', async () => {
        try {
          await change('/api/gpo/link', { ...body, remove: true }, panel.body);
          await after(`Unlinked ${nameOf(gpo)} from ${p.name}.`);
        } catch (err) {
          oops(err);
        }
      });
      return;
    }
    const more = act === 'link-on' ? { enabled: !l.enabled } : act === 'link-enforce' ? { enforced: !l.enforced } : { order: l.order + (act === 'link-up' ? -1 : 1) };
    await change('/api/gpo/link', { ...body, ...more }, panel.body);
    await after(act === 'link-on' ? `The link is ${l.enabled ? 'off' : 'on'}.` : act === 'link-enforce' ? (l.enforced ? 'No longer enforced.' : 'Enforced.') : `Moved to ${more.order}.`);
  };
  const doTicked = async (act, rows) => {
    if (act === 'clear') {
      picked.clear();
      drawTicks();
    } else if (act === 'export') g0.csv(g0.sorted(rows), `group-policy-${list}-ticked`);
    else if (act === 'link-many') linkTo(null, null, rows);
    else if (act === 'link-here-many') linkTo(null, null, null, rows);
    else if (act === 'block-many' || act === 'inherit-many') await block(rows.filter((p) => !p.domain), act === 'block-many');
    else if (act === 'gpupdate-many') gpupdate(rows);
    else if (act === 'backup-many') {
      const failed = [];
      R.busy($('gpo-lib'), 'Backing up');
      try {
        await together(async () => {
          for (const g of rows) {
            try {
              await change(`/api/gpo/${encodeURIComponent(g.id)}/backup`, { comment: '' });
            } catch (err) {
              if (err.cancelled) throw err;
              failed.push(g.name);
            }
          }
        });
      } finally {
        R.busy($('gpo-lib'), '');
      }
      await after(failed.length ? `${failed.length} of ${rows.length} weren't backed up.` : `Backed up ${plural(rows.length, 'GPO')}.`, failed.join(', '));
    } else if (act === 'delete-many') {
      const builtIn = rows.filter((g) => ['{31B2F340-016D-11D2-945F-00C04FB984F9}', '{6AC1786C-016F-11D2-945F-00C04FB984F9}'].includes(g.id));
      const list2 = rows.filter((g) => !builtIn.includes(g));
      if (!list2.length) {
        toast('Windows needs those two.', 'The Default Domain Policy and Default Domain Controllers Policy can\'t be deleted.', { tone: 'warn' });
        return;
      }
      sure(`Delete ${plural(list2.length, 'GPO')}?`, `${list2.slice(0, 6).map((g) => g.name).join(', ')}${list2.length > 6 ? ` and ${list2.length - 6} more` : ''}. Their links go too. Each is backed up first.${builtIn.length ? ' The two default GPOs stay: Windows needs them.' : ''}`, 'Delete', async () => {
        const failed = [];
        await together(async () => {
          for (const g of list2) {
            try {
              await change(`/api/gpo/${encodeURIComponent(g.id)}/delete`, {});
            } catch (err) {
              if (err.cancelled) break;
              failed.push(g.name);
            }
          }
        });
        picked.clear();
        await after(failed.length ? `${failed.length} of ${list2.length} weren't deleted.` : `Deleted ${plural(list2.length, 'GPO')}.`, failed.length ? failed.join(', ') : 'Backups are under Backups.');
      });
    } else if (act === 'delete-filters') deleteFilters(rows);
  };
  const block = async (places, on) => {
    const failed = [];
    for (const p of places) {
      try {
        await change('/api/gpo/block', { target: p.dn, blocked: on }, panel.is('gpo') ? panel.body : $('gpo-lib'));
      } catch (err) {
        if (err.cancelled) return;
        failed.push(p.name);
      }
    }
    await after(failed.length ? `${failed.length} of ${places.length} didn't change.` : on ? `Inheritance blocked on ${places.length === 1 ? places[0].name : plural(places.length, 'OU')}.`
      : `${places.length === 1 ? places[0].name : plural(places.length, 'OU')} inherit${places.length === 1 ? 's' : ''} again.`, failed.join(', '));
  };
  const deleteFilters = (filters) => {
    const busy = filters.filter((w) => usedBy(w).length);
    const free = filters.filter((w) => !usedBy(w).length);
    if (!free.length) {
      toast(`${busy.length === 1 ? busy[0].name : 'Those filters'} ${busy.length === 1 ? 'is' : 'are'} in use.`, 'Take them off their GPOs first.', { tone: 'warn' });
      return;
    }
    sure(`Delete ${free.length === 1 ? free[0].name : plural(free.length, 'WMI filter')}?`, `Gone from the domain.${busy.length ? ` ${busy.map((w) => w.name).join(', ')} ${busy.length === 1 ? 'stays: it\'s' : 'stay: they\'re'} in use.` : ''}`, 'Delete', async () => {
      const failed = [];
      for (const w of free) {
        try {
          await change(`/api/gpo/wmi-filters/${encodeURIComponent(w.id)}/delete`, { name: w.name }, panel.is('gpo') ? panel.body : $('gpo-lib'));
        } catch (err) {
          if (err.cancelled) return;
          failed.push(w.name);
        }
      }
      if (panel.is('gpo') && open?.kind === 'wmi') panel.close();
      picked.clear();
      await after(failed.length ? `${failed.length} of ${free.length} weren't deleted.` : `Deleted ${free.length === 1 ? free[0].name : plural(free.length, 'WMI filter')}.`, failed.join(', '));
    });
  };
  const restore = (b) => {
    if (!b) return;
    sure(`Restore ${b.name}?`, `Its settings go back to the backup from ${stamp(b.at)}${b.comment ? ` (${b.comment})` : ''}. ${gpoOf(b.gpo) ? 'Links stay as they are. What it has now is backed up first.' : 'It was deleted, so it comes back with its ID, not linked anywhere.'}`, 'Restore', async () => {
      try {
        await change('/api/gpo/restore', { backup: b.id }, panel.is('gpo') ? panel.body : $('gpo-lib'));
        await after(`Restored ${b.name}.`, 'Computers get it at their next refresh, within 90 minutes.');
      } catch (err) {
        oops(err);
      }
    });
  };
  const backupAll = () => ask('Back up all GPOs', `Every GPO in ${gp.domain}, kept on this server under Backups.`,
    '<label for="gpo-f-comment">Comment</label><input id="gpo-f-comment" maxlength="200" placeholder="Before the upgrade" autocomplete="off">', async () => {
      const r = await change('/api/gpo/backup-all', { comment: $('gpo-f-comment').value.trim() }, $('gpo-dialog'));
      await after(r.failed.length ? `Backed up ${r.done}; ${r.failed.length} weren't.` : `Backed up ${plural(r.done, 'GPO')}.`, r.failed.join(', '));
    }, 'Back up');
  // Run gpupdate on the computers in Devices a GPO reaches: in its results, or in a place it's linked.
  const gpupdate = async (gpos) => {
    if (!D.computers().length) await D.reload?.();
    const paths = gpos.flatMap((g) => linksOf(g.id).filter((l) => l.enabled).map((l) => l.place.path.toLowerCase()));
    const ids = new Set(gpos.map((g) => g.id));
    const on = D.computers().filter((c) => c.approved && !c.server && c.inventory?.gp && ((c.inventory.gp.gpos || []).some((id) => ids.has(id))
      || paths.some((p) => `${String(c.inventory.gp.som || '').toLowerCase()}/`.startsWith(`${p}/`))));
    if (!on.length) {
      toast('No computer in Devices gets it yet.', 'Computers send their Group Policy results every two hours, from client 1.1.211 on.', { tone: 'warn' });
      return;
    }
    D.run('gpupdate', on);
  };

  // --- Windows: New GPO, Link, Rename, Copy, Back up, Restore, WMI filters --------------------------------------
  let onGo = null;
  const ask = (title, lead, form, go, label = 'OK') => {
    $('gpo-d-title').textContent = title;
    $('gpo-d-lead').textContent = lead;
    $('gpo-d-form').innerHTML = form;
    $('gpo-d-problem').hidden = true;
    $('gpo-d-go').textContent = label;
    $('gpo-d-go').disabled = false;
    onGo = go;
    $('gpo-dialog').showModal();
    $('gpo-d-form').querySelector('input:not([type=checkbox]), select, textarea')?.focus();
  };
  $('gpo-d-cancel').addEventListener('click', () => $('gpo-dialog').close());
  $('gpo-d-go').addEventListener('click', async () => {
    if (!onGo) return;
    $('gpo-d-go').disabled = true;
    try {
      await onGo();
      $('gpo-dialog').close();
    } catch (e) {
      if (!e.cancelled) {
        $('gpo-d-problem').textContent = e.message;
        $('gpo-d-problem').hidden = false;
      }
    } finally {
      $('gpo-d-go').disabled = false;
    }
  });
  $('gpo-d-form').addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.matches('input')) $('gpo-d-go').click(); });
  // The OUs as Pick an OU reads them: the domain first, then each OU by its path.
  const ouOptions = (sel, blank = '') => (blank ? `<option value="">${esc(blank)}</option>` : '')
    + gp.places.map((p) => `<option value="${esc(p.dn)}"${sel && p.dn.toLowerCase() === sel.toLowerCase() ? ' selected' : ''}>${esc(p.domain ? p.name : p.path.slice(gp.domain.length + 1).split('/').join(' / '))}</option>`).join('');
  const ouField = (label, sel, blank = '') => `<label for="gpo-f-ou">${esc(label)}</label><div class="ou-row"><select id="gpo-f-ou">${ouOptions(sel, blank)}</select>`
    + `<button type="button" data-browse-ou="gpo-f-ou">${svg('folder')}Browse…</button></div>`;
  const newGpo = () => ask('New GPO', gp.gpmc ? 'An empty GPO, or a copy of another. Its settings are then set in the Group Policy editor (gpmc.msc).' : 'This needs Group Policy Management on this server.',
    '<label for="gpo-f-name">Name</label><input id="gpo-f-name" maxlength="200" placeholder="C-Screen lock" autocomplete="off" spellcheck="false">'
    + `<label for="gpo-f-from">Start from</label><select id="gpo-f-from"><option value="">Empty</option>${gp.gpos.map((g) => `<option value="${esc(g.id)}">${esc(g.name)}</option>`).join('')}</select>`
    + (has('gpo.link') ? ouField('Link it to', '', 'Not linked yet') : ''), async () => {
      const name = $('gpo-f-name').value.trim();
      if (!name) throw new Error('Give the GPO a name.');
      const link = $('gpo-f-ou')?.value || '';
      const r = await change('/api/gpo', { name, from: $('gpo-f-from').value, link }, $('gpo-dialog'));
      await after(`Made ${name}.`, r.warning || (link ? `Linked to ${placeOf(link)?.path || 'the OU picked'}.` : 'Not linked yet.'));
      openGpo(r.id);
    }, 'Make it');
  // A GPO to an OU, a GPO picked to an OU, several GPOs to one OU, or one GPO to several OUs.
  const linkTo = (g, p, gpos = null, places = null) => {
    const many = gpos || (g ? [g] : null);
    ask(many ? `Link ${many.length === 1 ? many[0].name : plural(many.length, 'GPO')}` : `Link a GPO to ${places ? (places.length === 1 ? places[0].name : plural(places.length, 'place')) : p ? p.name : 'an OU'}`,
      'It applies to the users and computers in there, and in OUs under it, at their next refresh. A new link goes last, so the GPOs linked there already win.',
      (many ? '' : `<label for="gpo-f-gpo">GPO</label><select id="gpo-f-gpo">${gp.gpos.map((x) => `<option value="${esc(x.id)}">${esc(x.name)}</option>`).join('')}</select>`)
      + (places ? '' : ouField('Link to', p?.dn || gp.places.find((x) => !x.domain)?.dn || ''))
      + '<span></span><label class="check"><input type="checkbox" id="gpo-f-enf"> Enforced: applies even where inheritance is blocked, and wins over GPOs linked lower down</label>', async () => {
        const ids = many ? many.map((x) => x.id) : [$('gpo-f-gpo').value];
        const targets = places ? places.map((x) => x.dn) : [$('gpo-f-ou').value];
        const enforced = $('gpo-f-enf').checked;
        const failed = [];
        let done = 0;
        await together(async () => {
          for (const id of ids) {
            for (const target of targets) {
              if (placeOf(target)?.links.some((l) => l.gpo === id)) continue;
              try {
                await change('/api/gpo/link', { gpo: id, target, add: true, enforced }, $('gpo-dialog'));
                done++;
              } catch (err) {
                if (err.cancelled) throw err;
                failed.push(`${nameOf(id)}: ${err.message}`);
              }
            }
          }
        });
        if (!done && failed.length) throw new Error(failed[0]);
        if (!done) throw new Error(ids.length === 1 && targets.length === 1 ? 'It\'s already linked there.' : 'They\'re already linked there.');
        await after(`Linked ${ids.length === 1 ? nameOf(ids[0]) : plural(ids.length, 'GPO')} to ${targets.length === 1 ? placeOf(targets[0])?.name || 'the OU' : plural(targets.length, 'place')}.`, failed.join('; '));
      }, 'Link');
  };
  const editFilter = (w) => ask(w ? `Edit ${w.name}` : 'New WMI filter', 'A GPO that uses it applies only on computers where the query finds something. Test the query under Devices, WMI query, first.',
    `<label for="gpo-f-name">Name</label><input id="gpo-f-name" maxlength="200" value="${esc(w?.name || '')}" placeholder="Windows 11 only" autocomplete="off" spellcheck="false">`
    + `<label for="gpo-f-desc">Description</label><input id="gpo-f-desc" maxlength="400" value="${esc(w?.description || '')}" autocomplete="off">`
    + `<label for="gpo-f-space">Namespace</label><input id="gpo-f-space" value="${esc(w?.space || 'root\\CIMv2')}" autocomplete="off" spellcheck="false">`
    + `<label for="gpo-f-query">Query</label><textarea id="gpo-f-query" rows="4" spellcheck="false" placeholder="SELECT * FROM Win32_OperatingSystem WHERE Version LIKE '10.0.2%' AND ProductType = 1">${esc(w?.query || '')}</textarea>`, async () => {
      const name = $('gpo-f-name').value.trim();
      await change('/api/gpo/wmi-filters', { id: w?.id || '', name, description: $('gpo-f-desc').value.trim(), space: $('gpo-f-space').value.trim(), query: $('gpo-f-query').value.trim() }, $('gpo-dialog'));
      await after(w ? `Saved ${name}.` : `Made ${name}.`);
    }, w ? 'Save' : 'Make it');

  // --- Devices: a Group Policy tab on each computer, and the Group Policy list --------------------------------------
  const NEEDS = '1.1.211';
  const WHY = { security: 'Denied by security filtering', wmi: 'Its WMI filter found nothing here', disabled: 'That half of the GPO is turned off', link: 'Its link is turned off', empty: 'No settings for this side' };
  const inv = {}; // computer id -> { data } | { error } | { loading }
  const loadInv = async (id) => {
    inv[id] = { loading: true };
    try {
      inv[id] = { data: (await get(`/api/clients/${encodeURIComponent(id)}/inventory`)).inventory?.groupPolicy || null, at: Date.now() };
    } catch (e) {
      inv[id] = { error: e.message };
    }
    if (panel.shows('seats', id)) D.panelAgain();
  };
  // GPO names from the domain when they're known (a filtered-out GPO comes back from gpresult as its ID).
  const gpoName = (x) => (gp ? gpoOf(x.id)?.name : null) || (x.name && !/^\{[0-9A-F-]+\}$/i.test(x.name) ? x.name : x.id === 'LOCALGPO' ? 'Local Group Policy' : x.name || x.id);
  const linkWords = (x) => (x.link === 'Local' ? 'This computer\'s own policy' : x.link ? `Linked to ${x.link}` : '');
  const gpRows = (list2, why) => rows2(list2.map((x) => [gpoName(x), why ? WHY[x.why] || 'Not applied' : linkWords(x), gp && gpoOf(x.id) ? svg('right', 'acc-go') : '', gp && gpoOf(x.id) && has('gpo.view') ? `data-gpo-open-pc="${esc(x.id)}"` : '']));
  const tabs0 = D.hooks.seatTabs;
  // Its count is how many GPOs applied, to the computer and to whoever was signed in.
  const appliedCount = (p) => (p ? (p.computer?.applied.length || 0) + (p.user?.applied.length || 0) : '');
  D.hooks.seatTabs = (c) => [...(tabs0?.(c) || []), ...(c.approved && !c.server ? [['gp', 'Group Policy', appliedCount(inv[c.id]?.data)]] : [])];
  const pane0 = D.hooks.seatPane;
  D.hooks.seatPane = (c, which) => {
    if (which !== 'gp') return pane0?.(c, which) ?? null;
    const got = inv[c.id];
    if (!got || (got.loading && !got.data)) {
      if (!got) loadInv(c.id);
      queueMicrotask(() => { const s = panel.body.querySelector('[data-gp-skel]'); if (s && !s.firstChild) R.skeleton(s, 'rows', 4); });
      return '<div data-gp-skel></div>';
    }
    if (got.error) return `<p class="hint problem">${esc(got.error)}</p>`;
    const p = got.data;
    const run = has('seats.actions') ? `<button type="button" class="small" data-gp-update>${svg('refresh')}Run gpupdate</button>` : '';
    if (!p) {
      const old = c.version && c.version.localeCompare(NEEDS, undefined, { numeric: true }) < 0;
      return section('Group Policy', none(old ? `Shows once its client is ${NEEDS} or later. It updates itself within a few minutes.` : 'Its client reads Group Policy every two hours, and right after gpupdate. Nothing has come in yet.'), run);
    }
    const comp = p.computer;
    const user = p.user;
    const l = p.local || {};
    const errs = p.errors || [];
    const yes = (b) => (b == null ? '' : b ? 'On' : 'Off');
    return section('Last refresh', facts([['When', esc(comp?.refreshed ? `${since(comp.refreshed)}${comp.dc ? `, from ${comp.dc.split('.')[0].toUpperCase()}` : ''}` : 'Not known')],
      comp?.took ? ['Took', esc(`${comp.took} seconds${comp.slow ? ', over a slow link' : ''}`)] : null,
      ['Computer', esc(comp ? `${plural(comp.applied.length, 'GPO')} applied` : 'Not read')], ['User', esc(user ? `${plural(user.applied.length, 'GPO')} applied to ${user.who}` : 'Nobody signed in when it was read')],
      comp?.som ? ['In', esc(comp.som)] : null, ['Read', esc(since(p.checked))]]), run)
      + (errs.length ? section(`Errors <small>${errs.length}</small>`, `<div class="dw-list seat-list err-list">${errs.map((e) => `<div class="mrow"><div class="two"><b>${esc(e.source)}${e.warning ? ' (warning)' : ''}</b><small>${esc(e.text)}</small></div><span class="err-when">${esc(since(e.when))}</span></div>`).join('')}</div>`) : '')
      + (comp ? section(`Applied to the computer <small>${comp.applied.length}</small>`, comp.applied.length ? gpRows(comp.applied, false) : none('Nothing.')) : '')
      + (user ? section(`Applied to ${esc(user.who)} <small>${user.applied.length}</small>`, user.applied.length ? gpRows(user.applied, false) : none('Nothing.')) : '')
      + section(`Not applied <small>${(comp?.denied.length || 0) + (user?.denied.length || 0)}</small>`, (comp?.denied.length || user?.denied.length)
        ? gpRows([...(comp?.denied || []), ...(user?.denied || [])], true) : none('Everything in reach applied.'))
      + section('Local policy it ended up with', facts([['Minimum password length', l.minLength == null ? '' : esc(plural(l.minLength, 'character'))],
        ['Maximum password age', l.maxAge == null ? '' : esc(l.maxAge === 0 ? 'Never expires' : plural(l.maxAge, 'day'))],
        ['Lockout after', l.lockout == null ? '' : esc(l.lockout === 0 ? 'Never locks out' : `${plural(l.lockout, 'wrong password')}${l.lockoutMinutes ? `, for ${l.lockoutMinutes} min` : ''}`)],
        ['Windows Firewall', esc(l.firewall || '')], ['Remote Desktop', esc(l.rdp || '')], ['User Account Control', esc(yes(l.uac))], ['SMBv1', esc(yes(l.smb1))], ['Audit sign-ins', esc(l.auditLogon || '')]]
        .filter(([, v]) => v)));
  };
  const opened0 = D.hooks.seatOpened;
  D.hooks.seatOpened = (c) => {
    if (!inv[c.id]?.at || Date.now() - inv[c.id].at > 60000) delete inv[c.id];
    if (has('gpo.view') && !gp && !loading) load(); // for GPO names
    opened0?.(c);
  };
  // The inventory the panel reads as it opens: the tab's error count shows before the tab is opened.
  const inv0 = D.hooks.seatInventory;
  D.hooks.seatInventory = (c, got) => {
    if (!inv[c.id]?.data) inv[c.id] = { data: got?.inventory?.groupPolicy || null, at: Date.now() };
    inv0?.(c, got);
  };
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('seats')) return;
    if (e.target.closest('[data-gp-update]')) {
      const c = D.rows().find((y) => y.id === panel.key);
      if (c) D.run('gpupdate', [c]);
    }
    const o = e.target.closest('[data-gpo-open-pc]');
    if (o) openGpo(o.dataset.gpoOpenPc);
  });

  // The Group Policy list: one row per computer, with what Group Policy did there.
  const G = (c) => c.inventory?.gp || null;
  const pcs = () => D.rows().filter((c) => c.approved && !c.server);
  const dash = '<span class="muted">-</span>';
  let localG = null;
  const ticks = new Set();
  const localGrid = () => (localG ||= R.grid({
    el: $('dv-gplocal'), key: 'gp-local', sort: { key: 'computer', dir: 1 },
    rowClass: (c) => (c.approved && !c.online ? 'off' : ''),
    lead: () => (has('seats.actions') ? { w: 18, head: '<label class="tk"><input type="checkbox" data-gpl-tick-all aria-label="Select all"></label>',
      cell: (c) => `<label class="tk"><input type="checkbox" data-gpl-tick="${esc(c.id)}" aria-label="Select ${esc(c.computer)}"></label>` } : null),
    cols: [
      { key: 'computer', label: 'Computer', w: 165, fixed: true, get: (c) => c.computer, cell: (c) => `<div class="who"><span class="pic">${svg('monitor')}</span><b class="ell">${esc(c.computer)}</b></div>` },
      { key: 'refreshed', label: 'Last refresh', w: 120, on: true, get: (c) => (G(c)?.refreshed ? new Date(G(c).refreshed).getTime() : 0), text: (c) => G(c)?.refreshed || '',
        cell: (c) => (G(c) ? esc(since(G(c).refreshed) || 'Not known') : muted('Not sent yet')) },
      { key: 'cgpos', label: 'Computer GPOs', w: 120, on: true, get: (c) => G(c)?.computer ?? '', cell: (c) => (G(c)?.computer == null ? dash : esc(G(c).computer)) },
      { key: 'ugpos', label: 'User GPOs', w: 100, on: true, get: (c) => G(c)?.user ?? '', cell: (c) => (G(c)?.user == null ? dash : esc(G(c).user)) },
      { key: 'who', label: 'Signed in', w: 140, on: true, get: (c) => G(c)?.who || '' },
      { key: 'errors', label: 'Errors', w: 90, on: true, get: (c) => G(c)?.errors ?? 0, cell: (c) => (G(c)?.errors ? badge(plural(G(c).errors, 'error'), 'red') : dash) },
      { key: 'minLength', label: 'Min password', w: 110, on: true, get: (c) => G(c)?.minLength ?? '' },
      { key: 'firewall', label: 'Firewall', w: 120, on: true, get: (c) => G(c)?.firewall || '' },
      { key: 'rdp', label: 'Remote Desktop', w: 130, get: (c) => G(c)?.rdp || '' },
      { key: 'took', label: 'Took', w: 80, get: (c) => G(c)?.took ?? '', cell: (c) => (G(c)?.took == null ? dash : esc(`${G(c).took} s`)) },
      { key: 'som', label: 'In', w: 240, get: (c) => G(c)?.som || '' },
    ],
    row: (c) => ` data-gpl-pc="${esc(c.id)}"`, empty: () => 'No computers yet.', redraw: () => D.draw(),
  }));
  const drawLocalTicks = () => {
    const boxes = [...$('dv-gplocal').querySelectorAll('[data-gpl-tick]')];
    boxes.forEach((b) => {
      b.checked = ticks.has(b.dataset.gplTick);
      b.closest('.tr')?.classList.toggle('picked', b.checked);
    });
    const all = $('dv-gplocal').querySelector('[data-gpl-tick-all]');
    if (all) {
      const on = boxes.filter((b) => b.checked).length;
      all.checked = !!boxes.length && on === boxes.length;
      all.indeterminate = on > 0 && on < boxes.length;
    }
    D.drawPicked?.();
  };
  D.lists.gplocal = ['Group Policy', 'policy', 'What Group Policy applied on each computer, and any errors.'];
  D.more.gplocal = {
    table: 'dv-gplocal', grid: localGrid, rows: pcs, count: () => pcs().filter((c) => G(c)).length || '', can: () => has('seats.view'), noun: 'computers',
    load: async () => { if (has('gpo.view') && !gp && !loading) load(); },
    empty: () => '',
    acts: {
      can: () => has('seats.actions'),
      count: () => pcs().filter((c) => ticks.has(c.id)).length,
      open: (button) => {
        const on = pcs().filter((c) => ticks.has(c.id));
        R.actions.open(button, {
          items: [{ act: 'gpupdate', label: 'Run gpupdate…', hint: String(on.length) }, { act: 'clear', label: 'Clear the ticks' }],
          pick: (act) => {
            if (act === 'gpupdate') D.run('gpupdate', pcs().filter((c) => ticks.has(c.id)));
            else {
              ticks.clear();
              drawLocalTicks();
            }
          },
        });
      },
    },
  };
  let lastLocal = null;
  $('dv-gplocal').addEventListener('click', (e) => {
    const box = e.target.closest('.tk')?.querySelector('input');
    if (box) {
      if (e.target !== box) return;
      const boxes = [...$('dv-gplocal').querySelectorAll('[data-gpl-tick]')];
      const set = (b) => (box.checked ? ticks.add(b.dataset.gplTick) : ticks.delete(b.dataset.gplTick));
      if (box.dataset.gplTickAll !== undefined) boxes.forEach(set);
      else {
        const i = boxes.indexOf(box);
        const from = e.shiftKey && lastLocal !== null ? Math.min(lastLocal, i) : i;
        const to = e.shiftKey && lastLocal !== null ? Math.max(lastLocal, i) : i;
        boxes.slice(from, to + 1).forEach(set);
        lastLocal = i;
      }
      drawLocalTicks();
      return;
    }
    const row = e.target.closest('[data-gpl-pc]');
    if (row && !e.target.closest('.th')) D.open(row.dataset.gplPc, 'gp');
  });
  const drawn0 = D.hooks.drawn;
  D.hooks.drawn = () => {
    drawn0?.();
    if (D.at() === 'gplocal') drawLocalTicks();
  };
})();
