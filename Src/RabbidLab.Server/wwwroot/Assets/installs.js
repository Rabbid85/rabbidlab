// Installs on Devices: the apps that can be put on computers with winget, for everyone on them. The list with the
// computers that have each app, an app in the side panel, Install (a job like any other, DeviceApps.cs), and adding an
// app by its winget ID. Installing confirms who's asking and shows under Jobs.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, panel, toast, sure, get, send, say, when } = R;

  const BRAKE = 10; // more computers than this, and how many has to be typed
  const CATEGORIES = ['Browsers', 'Developer tools', 'Admin tools', 'Utilities', 'Office', 'Media', 'Runtimes', 'Other'];
  const can = () => has('seats.installs');
  const many = (n) => `${n.toLocaleString()} computer${n === 1 ? '' : 's'}`;
  const names = (list) => (list.length <= 4 ? list.map((c) => c.computer).join(', ') : many(list.length));
  const appNames = (list) => (list.length <= 3 ? list.map((a) => a.name).join(', ') : `${list.length} apps`);
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const oops = (e) => { if (!e.cancelled) toast("That didn't work.", e.message, { tone: 'warn' }); };
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);
  const byName = (a, b) => a.name.localeCompare(b.name);
  const source = (a) => (a.builtIn ? 'Built-in' : a.by || '');
  const onText = (n) => (n ? many(n) : 'None');

  let apps = null; // /api/devices/apps
  const appOf = (id) => (apps || []).find((a) => a.id === id);
  const load = async () => { apps = await get('/api/devices/apps'); };
  // Workflows name their install steps by these (devices.js).
  D.apps = () => apps || [];
  D.loadApps = load;

  // --- The list -------------------------------------------------------------------------------------------------
  const picked = new Set(); // the ticked apps, by id
  const lead = { w: 18, head: '<label class="tk"><input type="checkbox" data-app-tick-all aria-label="Select all"></label>',
    cell: (a) => `<label class="tk"><input type="checkbox" data-app-tick="${esc(a.id)}" aria-label="Select ${esc(a.name)}"></label>` };
  let G = null;
  const grid = () => (G ||= R.grid({
    el: $('dv-installs'), key: 'installs', sort: { key: 'name', dir: 1 }, lead,
    cols: [
      { key: 'name', label: 'App', w: 230, fixed: true, get: (a) => a.name, cell: (a) => `<div class="who"><span class="pic">${svg('box')}</span><b class="ell">${esc(a.name)}</b></div>` },
      { key: 'publisher', label: 'Publisher', w: 150, on: true, get: (a) => a.publisher || '', cell: (a) => `<span class="ell">${esc(a.publisher || '')}</span>` },
      { key: 'category', label: 'Category', w: 130, on: true, get: (a) => a.category },
      { key: 'on', label: 'Installed on', w: 115, on: true, get: (a) => a.on.length, text: (a) => onText(a.on.length), cell: (a) => `<span${a.on.length ? '' : ' class="muted"'}>${esc(onText(a.on.length))}</span>` },
      { key: 'about', label: 'Description', w: 320, on: true, nofilter: true, get: (a) => a.about || '', cell: (a) => `<span class="ell muted">${esc(a.about || '')}</span>` },
      { key: 'winget', label: 'winget ID', w: 230, nofilter: true, get: (a) => a.winget, cell: (a) => `<span class="mono muted ell">${esc(a.winget)}</span>` },
      { key: 'by', label: 'Added by', w: 120, get: source },
      { key: 'changed', label: 'Changed', w: 150, nofilter: true, get: (a) => ms(a.changed), text: (a) => a.changed || '', cell: (a) => `<span class="muted">${esc(when(a.changed))}</span>` },
    ],
    row: (a) => ` data-row="app:${esc(a.id)}"`,
    empty: () => D.more.installs.empty(), loading: () => !apps,
    redraw: () => D.draw(),
  }));
  const fld = (key, label, type, value) => ({ key, label, type, get: value });
  const FIELDS = [['App', [fld('name', 'Name', 'text', (a) => a.name), fld('publisher', 'Publisher', 'choice', (a) => a.publisher || ''), fld('category', 'Category', 'choice', (a) => a.category),
    fld('on', 'Installed on (computers)', 'number', (a) => a.on.length), fld('by', 'Added by', 'choice', source), fld('winget', 'winget ID', 'text', (a) => a.winget)]]];
  const pickedRows = () => (apps || []).filter((a) => picked.has(a.id));

  D.lists.installs = ['Installs', 'box', 'Apps to install on computers with winget, for everyone on the computer. Tick apps or open one, then Install.'];
  D.more.installs = { table: 'dv-installs', grid, rows: () => apps || [], count: () => apps?.length, can, noun: 'apps', fields: () => FIELDS, load,
    empty: () => 'No apps like that.',
    acts: { can, count: () => pickedRows().length, open: (button) => openActs(button) },
    add: { can, label: 'Add an app', open: () => openEdit(null) } };

  // Ticks, like every list: what's ticked goes to Actions.
  const drawTicks = () => {
    const boxes = [...$('dv-installs').querySelectorAll('[data-app-tick]')];
    boxes.forEach((b) => {
      b.checked = picked.has(b.dataset.appTick);
      b.closest('.tr')?.classList.toggle('picked', b.checked);
    });
    const all = $('dv-installs').querySelector('[data-app-tick-all]');
    if (all) {
      const on = boxes.filter((b) => b.checked).length;
      all.checked = !!boxes.length && on === boxes.length;
      all.indeterminate = on > 0 && on < boxes.length;
    }
    D.drawPicked?.();
  };
  let lastTick = null;
  const tick = (box, shift) => {
    const boxes = [...$('dv-installs').querySelectorAll('[data-app-tick]')];
    const set = (b) => (box.checked ? picked.add(b.dataset.appTick) : picked.delete(b.dataset.appTick));
    if (box.dataset.appTickAll !== undefined) boxes.forEach(set);
    else {
      const i = boxes.indexOf(box);
      const from = shift && lastTick !== null ? Math.min(lastTick, i) : i;
      const to = shift && lastTick !== null ? Math.max(lastTick, i) : i;
      boxes.slice(from, to + 1).forEach(set);
      lastTick = i;
    }
    drawTicks();
  };
  $('dv-installs').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target === box) tick(box, e.shiftKey); // the label's own click comes through the box
      return;
    }
    const row = e.target.closest('[data-row]');
    if (!row) return;
    const key = row.dataset.row;
    if (panel.shows('devices', key)) panel.close();
    else if (appOf(key.slice(4))) openApp(appOf(key.slice(4)));
  });

  // What Actions does with the ticked apps.
  const menu = $('dv-act-menu');
  const openActs = (button) => {
    const rows = pickedRows();
    const mine = rows.filter((a) => !a.builtIn);
    const item = (act, icon, label, hint = '', danger = false) => `<button type="button" role="menuitem" class="am-link${danger ? ' out' : ''}" data-app-act="${act}">${svg(icon)}<span>${esc(label)}</span>${hint ? `<small>${esc(hint)}</small>` : ''}</button>`;
    menu.innerHTML = `<div class="am-links">${item('install', 'download', 'Install on computers…', String(rows.length))}${item('export', 'csv', 'Export these')}${item('clear', 'x', 'Clear the ticks')}</div>`
      + (mine.length ? `<div class="am-links">${item('delete', 'trash', 'Delete…', String(mine.length), true)}</div>` : '');
    const r = button.getBoundingClientRect();
    menu.style.cssText = `position:fixed;inset:auto;width:270px;margin:0;left:${Math.max(8, r.right - 270)}px;top:${r.bottom + 6}px`;
    menu.showPopover();
  };
  menu.addEventListener('click', (e) => {
    const b = e.target.closest('[data-app-act]');
    if (!b) return;
    menu.hidePopover();
    const rows = pickedRows();
    const act = b.dataset.appAct;
    if (act === 'clear') {
      picked.clear();
      drawTicks();
    } else if (act === 'export') grid().csv(grid().sorted(rows), 'installs-ticked');
    else if (act === 'install') openInstall({ apps: rows.sort(byName) });
    else if (act === 'delete') remove(rows.filter((a) => !a.builtIn));
  });

  // --- An app in the side panel ---------------------------------------------------------------------------------
  let shown = null; // the app that's open
  const scripts = {}; // app id -> what installing it runs, once asked for
  const openApp = (a) => {
    shown = a;
    const ran = scripts[a.id];
    const strip = [['Category', esc(a.category)], ['Installed on', esc(onText(a.on.length))], ['Added by', esc(source(a) || 'Unknown')]]
      .map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${v}</span></div>`).join('');
    const extras = a.extensions.length ? `<p class="dw-how">It also adds ${esc(a.extensions.map((x) => x.name).join(', '))} to VS Code, for each person who uses the computer. Whoever is signed in gets it straight away, and everyone else the first time they sign in. It's added once per person, so one they remove later stays removed.</p>`
      + `<pre class="code-box">${a.extensions.map((x) => `code --install-extension ${esc(x.id)}`).join('\n')}</pre>` : '';
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('box')}</span><div class="dw-name"><h2 id="dw-title">${esc(a.name)}</h2><p>${esc([a.publisher, a.winget].filter(Boolean).join(' · '))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${qa('data-app-install', 'download', 'Install…')}${a.builtIn ? '' : qa('data-app-edit', 'edit', 'Edit') + qa('data-app-del', 'trash', 'Delete')}</div>`
      + `<div class="dw-strip">${strip}</div>`
      + section('About', `<p class="dw-how">${esc(a.about || 'No description.')}</p><p class="dw-how">winget installs it for everyone on the computer, as SYSTEM. One that's already there is brought up to date.</p>${extras}`)
      + section(`Installed on <small>${a.on.length}</small>`, a.on.length ? `<div class="dw-list">${a.on.map((x) => `<div class="mrow acc-row" data-app-pc="${esc(x.id)}"><span class="pic small">${svg('monitor')}</span>`
        + `<div class="two"><b>${esc(x.computer)}</b><small>${esc(x.version ? `Version ${x.version}` : '')}</small></div>${svg('right', 'acc-go')}</div>`).join('')}</div>`
        : none(has('seats.view') ? "No computer lists it under Installed apps yet. A computer's list updates a minute or two after an install." : ''))
      + section('What runs', ran ? `<details class="job-what"><summary><b>Install ${esc(a.name)}</b><small>PowerShell · as SYSTEM · time limit ${ran.minutes} min</small></summary><pre class="code-box">${esc(ran.text)}</pre></details>` : '<div data-app-skel></div>')
      + (a.workflows.length ? section(`Workflows that install it <small>${a.workflows.length}</small>`, `<div class="dw-list">${a.workflows.map((w) => `<div class="mrow"><span class="pic small">${svg('org')}</span><div class="two"><b>${esc(w)}</b></div></div>`).join('')}</div>`) : '')
      + (a.builtIn ? '' : section('Changed', `<ul class="does"><li><b>When</b><span>${esc(when(a.changed))}</span></li>${a.by ? `<li><b>By</b><span>${esc(a.by)}</span></li>` : ''}</ul>`));
    panel.open('devices', `app:${a.id}`);
    D.mark?.();
    if (ran) return;
    const skel = panel.body.querySelector('[data-app-skel]');
    if (skel) R.skeleton(skel, 'rows', 2);
    get(`/api/devices/apps/${encodeURIComponent(a.id)}/script`).then((got) => {
      scripts[a.id] = got;
      if (shown?.id === a.id && panel.shows('devices', `app:${a.id}`)) openApp(appOf(a.id) || a);
    }).catch(() => {});
  };
  const remove = (list) => {
    if (!list.length) return;
    const one = list.length === 1;
    sure(one ? `Delete ${list[0].name}?` : `Delete ${list.length} apps?`, `${one ? 'It goes' : `${appNames(list)}. They go`} from this page. Nothing is uninstalled from any computer.`, 'Delete', async () => {
      let failed = '';
      for (const a of list) {
        try {
          await D.confirmed(() => send(`/api/devices/apps/${encodeURIComponent(a.id)}/delete`));
          picked.delete(a.id);
          delete scripts[a.id];
        } catch (e) {
          if (e.cancelled) break;
          failed ||= e.message;
        }
      }
      if (panel.is('devices') && String(panel.key).startsWith('app:')) panel.close();
      await load().catch(() => {});
      D.draw();
      if (failed) toast(one ? `${list[0].name} wasn't deleted.` : "Some weren't deleted.", failed, { tone: 'warn' });
      else toast(one ? 'App deleted.' : `Deleted ${list.length} apps.`, one ? list[0].name : '');
    });
  };
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('devices') || !String(panel.key).startsWith('app:') || !shown) return;
    const on = (attr) => e.target.closest(`[${attr}]`);
    if (on('data-app-install')) openInstall({ apps: [shown] });
    else if (on('data-app-edit')) openEdit(shown);
    else if (on('data-app-del')) remove([shown]);
    else if (on('data-app-pc')) D.open(on('data-app-pc').dataset.appPc, 'software');
  });

  // --- Install: which apps, on which computers ------------------------------------------------------------------
  // From this page the apps are given and the computers are picked; from Computers' Actions it's the other way round.
  let ins = null; // { apps, on } while the window is open
  const approved = () => D.computers().filter((c) => c.approved).sort((a, b) => a.computer.localeCompare(b.computer));
  const targets = () => {
    if (ins.on) return ins.on;
    const v = $('ins-on').value;
    if (v === 'ticked') return approved().filter((c) => D.picked.has(c.id));
    if (v === 'online') return approved().filter((c) => D.stateOf(c) === 'online');
    if (v === 'all') return approved();
    return approved().filter((c) => `c:${c.id}` === v);
  };
  const chosen = () => ins.apps || [appOf($('ins-app').value)].filter(Boolean);
  const drawIns = () => {
    const on = targets();
    const list = chosen();
    const n = on.length;
    for (const id of ['ins-count', 'ins-count-label']) $(id).hidden = n <= BRAKE;
    $('ins-count-label').textContent = `That's ${n} computers. Type ${n} to go ahead`;
    $('ins-go').disabled = !n || !list.length;
    const off = on.filter((c) => D.stateOf(c) === 'offline').length;
    const extras = list.filter((a) => a.extensions.length).map((a) => ` ${a.name} also gets ${a.extensions.map((x) => x.name).join(', ')} in VS Code, for each person who uses the computer.`).join('');
    $('ins-note').textContent = `winget installs ${list.length > 1 ? 'them' : 'it'} for everyone on the computer, as SYSTEM. An app that's already there is brought up to date.${extras}`
      + (!off ? '' : off < n ? ` ${off} ${off === 1 ? 'is offline and gets' : 'are offline and get'} it once back, within a day.` : n === 1 ? " It's offline and gets it once it's back, within a day." : " They're offline and get it once back, within a day.")
      + ' Who ran it and what winget wrote are kept under Jobs.';
  };
  const openInstall = async ({ apps: given = null, on = null }) => {
    if (!apps) {
      try {
        await load();
      } catch (e) {
        oops(e);
        return;
      }
    }
    const pcs = on ? on.filter((c) => c.approved && !c.server) : null;
    if (pcs && !pcs.length) {
      toast('Nothing to install it on.', 'Pick computers that are approved.', { tone: 'warn' });
      return;
    }
    ins = { apps: given, on: pcs };
    $('ins-title').textContent = !given ? 'Install an app' : given.length === 1 ? `Install ${given[0].name}` : `Install ${given.length} apps`;
    const lead = [given && given.length > 1 ? `${given.map((a) => a.name).join(', ')}.` : '', pcs ? `On ${names(pcs)}.` : ''].filter(Boolean).join(' ');
    $('ins-lead').textContent = lead;
    $('ins-lead').hidden = !lead;
    for (const id of ['ins-app', 'ins-app-label']) $(id).hidden = !!given;
    if (!given) {
      const group = (label, list) => (list.length ? `<optgroup label="${esc(label)}">${list.sort(byName).map((a) => `<option value="${esc(a.id)}">${esc(a.name)}</option>`).join('')}</optgroup>` : '');
      $('ins-app').innerHTML = '<option value="" selected disabled>Pick an app</option>' + CATEGORIES.map((c) => group(c, apps.filter((a) => a.category === c))).join('');
    }
    for (const id of ['ins-on', 'ins-on-label']) $(id).hidden = !!pcs;
    if (!pcs) {
      const all = approved();
      const ticked = all.filter((c) => D.picked.has(c.id)).length;
      const online = all.filter((c) => D.stateOf(c) === 'online').length;
      $('ins-on').innerHTML = `<option value="" disabled${ticked ? '' : ' selected'}>Pick the computers</option>`
        + (ticked ? `<option value="ticked" selected>Ticked computers (${ticked})</option>` : '')
        + `<option value="online">Every online computer (${online})</option><option value="all">Every approved computer (${all.length})</option>`
        + (all.length ? `<optgroup label="One computer">${all.map((c) => `<option value="c:${esc(c.id)}">${esc(c.computer)}${D.stateOf(c) === 'online' ? '' : ' (offline)'}</option>`).join('')}</optgroup>` : '');
    }
    $('ins-count').value = '';
    say('ins-problem', '');
    drawIns();
    $('ins-dialog').showModal();
  };
  for (const id of ['ins-app', 'ins-on']) $(id).addEventListener('change', drawIns);
  $('ins-cancel').addEventListener('click', () => $('ins-dialog').close());
  $('ins-go').addEventListener('click', async () => {
    const on = targets();
    const list = chosen();
    const body = { kind: 'install', apps: list.map((a) => a.id), clients: on.map((c) => c.id) };
    if (on.length > BRAKE) body.count = Number($('ins-count').value) || 0;
    $('ins-go').disabled = true;
    try {
      const made = await D.confirmed(() => send('/api/devices/run', body));
      $('ins-dialog').close();
      picked.clear();
      drawTicks();
      toast('Install sent.', `${appNames(list)} on ${names(on)}. It's under Jobs.`);
      D.openJob(made.id);
      if (D.at() === 'jobs') D.reload();
    } catch (e) {
      if (!e.cancelled) say('ins-problem', e.message);
    }
    $('ins-go').disabled = false;
  });

  // --- Add an app, or change one of your own --------------------------------------------------------------------
  let editing = null; // its id, or null for a new one
  const openEdit = (a) => {
    editing = a?.id || null;
    $('app-title').textContent = a ? 'Edit app' : 'Add an app';
    for (const [id, value] of [['app-name', a?.name], ['app-winget', a?.winget], ['app-publisher', a?.publisher], ['app-about', a?.about], ['app-shows', a?.shows]]) $(id).value = value || '';
    $('app-category').innerHTML = CATEGORIES.map((c) => `<option${c === (a?.category || 'Other') ? ' selected' : ''}>${esc(c)}</option>`).join('');
    say('app-problem', '');
    $('app-dialog').showModal();
    $('app-name').focus();
  };
  $('app-cancel').addEventListener('click', () => $('app-dialog').close());
  $('app-save').addEventListener('click', async () => {
    const v = (id) => $(id).value.trim();
    const form = { id: editing, name: v('app-name'), winget: v('app-winget'), publisher: v('app-publisher'), about: v('app-about'), category: $('app-category').value, shows: v('app-shows') };
    $('app-save').disabled = true;
    try {
      const made = await D.confirmed(() => send('/api/devices/apps', form));
      $('app-dialog').close();
      delete scripts[made.id];
      await load();
      D.draw();
      toast(editing ? 'App saved.' : 'App added.', form.name);
      if (panel.is('devices') && String(panel.key).startsWith('app:') && appOf(made.id)) openApp(appOf(made.id));
    } catch (e) {
      if (!e.cancelled) say('app-problem', e.message);
    }
    $('app-save').disabled = false;
  });

  // In Computers' Actions, for the ticked computers, and in a computer's own Actions.
  const H = D.hooks;
  const items0 = H.actItems;
  H.actItems = (item) => (items0?.(item) || '') + (can() ? item('install', 'box', 'Install an app…') : '');
  const act0 = H.act;
  H.act = (act, on) => (act === 'install' ? (openInstall({ on }), true) : act0?.(act, on) || false);
  const drawn0 = H.drawn;
  H.drawn = () => {
    drawn0?.();
    if (D.at() === 'installs') drawTicks();
  };
})();
