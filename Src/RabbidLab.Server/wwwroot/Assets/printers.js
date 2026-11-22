// Printers on Devices: a computer's Printers tab, the Printers list across computers, and Add a printer (for one computer,
// or the ticked ones from Actions). What a computer has comes from its client's inventory; adding, removing and test pages
// are jobs (DevicePrinters.cs), so they confirm who's asking and show under Jobs like any other run.
(() => {
  const R = window.RabbidLab;
  const D = R.devices;
  const { $, esc, svg, has, panel, toast, sure, get, send, say } = R;

  const NEEDS = '1.1.190'; // the first client that reports its printers
  const BRAKE = 10;
  const KIND = { tcpip: 'TCP/IP', lpr: 'LPR', ipp: 'IPP', wsd: 'WSD', share: 'Shared', universal: 'Universal Print', virtual: 'Virtual', local: 'Local' };
  // Windows' own drivers, there to pick even when no computer reported them yet.
  const CLASS = ['Microsoft PCL6 Class Driver', 'Microsoft PS Class Driver', 'Microsoft IPP Class Driver', 'Generic / Text Only'];
  const many = (n) => `${n.toLocaleString()} computer${n === 1 ? '' : 's'}`;
  const names = (list) => (list.length <= 4 ? list.map((c) => c.computer).join(', ') : many(list.length));
  const chip = (text, cls = '', icon = '') => `<span class="uchip ${cls}">${icon ? svg(icon) : ''}${esc(text)}</span>`;
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
  const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
  const where = (p) => [KIND[p.kind] || p.kind, p.address].filter(Boolean).join(' · ');
  const can = () => has('seats.actions');
  const oops = (e) => { if (!e.cancelled) toast('The operation failed.', e.message, { tone: 'warn' }); };
  const below = (version, than) => {
    const [a, b] = [version || '0', than].map((v) => v.split('.').map(Number));
    for (let i = 0; i < 3; i++) if ((a[i] || 0) !== b[i]) return (a[i] || 0) < b[i];
    return false;
  };

  // --- Running one: add, remove or a test page, as a job on the computers given ---------------------------------
  const runPrinter = async (on, printer, count) => {
    const made = await D.confirmed(() => send('/api/devices/run', { kind: 'printer', clients: on.map((c) => c.id), printer, ...(count ? { count } : {}) }));
    D.openJob(made.id);
    if (D.at() === 'jobs') D.reload();
    return made;
  };

  // --- A computer's Printers tab -------------------------------------------------------------------------------
  const inv = {}; // computer id -> { printers, drivers } from its inventory, or { error }
  const keepInv = (id, got) => { inv[id] = { printers: got?.printers ?? null, drivers: got?.printerDrivers || [], sent: !!got }; };
  const loadInv = async (id) => {
    try {
      keepInv(id, (await get(`/api/clients/${encodeURIComponent(id)}/inventory`)).inventory);
    } catch (e) {
      inv[id] = { error: e.message };
    }
    if (panel.shows('seats', id)) D.panelAgain();
  };
  const row = (c, p) => `<div class="mrow"><span class="pic small">${svg('printer')}</span>`
    + `<div class="two"><b>${esc(p.name)}</b><small>${esc([where(p), p.driver].filter(Boolean).join(' · '))}</small></div>`
    + `<span class="acc-chips">${p.kind === 'universal' ? chip('Universal Print', 'muted') : ''}${p.offline ? chip('Offline', 'off', 'power') : ''}${p.shared ? chip('Shared', 'muted') : ''}</span>`
    + (can() && p.kind !== 'virtual' ? `<button type="button" class="linkish pr-test" data-pr-test="${esc(p.name)}">Test page</button>` : '')
    + (can() && p.kind !== 'virtual' && p.kind !== 'universal' ? `<button type="button" class="at-more dw-x" data-pr-remove="${esc(p.name)}" data-tip="Remove" aria-label="Remove ${esc(p.name)}">${svg('x')}</button>` : '')
    + '</div>';
  const pane = (c) => {
    const p = inv[c.id];
    if (!p) {
      loadInv(c.id);
      return '<div data-pr-skel></div>';
    }
    if (p.error) return section('Printers', none(p.error));
    const add = can() && c.approved ? `<button type="button" class="small" data-pr-add>${svg('plus')}Add printer</button>` : '';
    if (!p.printers) {
      return section('Printers', none(!p.sent ? 'This computer has not reported its inventory yet.' : below(c.version, NEEDS)
        ? `Printers appear with client ${NEEDS} or later. The client updates automatically within a few minutes.` : 'Windows did not report any printers.'), add);
    }
    const real = p.printers.filter((x) => x.kind !== 'virtual');
    const built = p.printers.filter((x) => x.kind === 'virtual');
    return section(`Printers <small>${real.length}</small>`, (real.length ? `<div class="dw-list">${real.map((x) => row(c, x)).join('')}</div>` : none('No printers.'))
      + '<p class="dw-how pr-note">Printers are added for all users of this computer. Changes run as jobs, and this list updates after each job.</p>', add)
      + (built.length ? section(`Virtual printers <small>${built.length}</small>`, `<div class="dw-list">${built.map((x) => row(c, x)).join('')}</div>`) : '');
  };
  const H = D.hooks;
  const tabs0 = H.seatTabs;
  H.seatTabs = (c) => [...(tabs0?.(c) || []), ...(!c.server && c.approved ? [['printers', 'Printers', inv[c.id]?.printers ? inv[c.id].printers.filter((x) => x.kind !== 'virtual').length : '']] : [])];
  const pane0 = H.seatPane;
  H.seatPane = (c, tab) => {
    if (tab !== 'printers') return pane0 ? pane0(c, tab) : null;
    const html = pane(c);
    queueMicrotask(() => {
      const skel = panel.body.querySelector('[data-pr-skel]');
      if (skel && !skel.firstChild) R.skeleton(skel, 'rows', 3);
    });
    return html;
  };
  const opened0 = H.seatOpened;
  H.seatOpened = (c) => {
    delete inv[c.id]; // read again for each opening
    opened0?.(c);
  };
  // The inventory the panel reads as it opens: the tab's count shows before the tab is opened.
  const inv0 = H.seatInventory;
  H.seatInventory = (c, got) => {
    keepInv(c.id, got?.inventory);
    inv0?.(c, got);
  };
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('seats')) return;
    const c = D.rows().find((x) => x.id === panel.key);
    if (!c) return;
    const t = e.target.closest('[data-pr-test]');
    const r = e.target.closest('[data-pr-remove]');
    if (e.target.closest('[data-pr-add]')) openAdd([c]);
    else if (t) runPrinter([c], { action: 'testpage', name: t.dataset.prTest }).then(() => toast('Test page sent.', `To ${t.dataset.prTest} on ${c.computer}.`)).catch(oops);
    else if (r) {
      const name = r.dataset.prRemove;
      sure(`Remove ${name}?`, `The printer is removed from ${c.computer} for all users.`, 'Remove', () => runPrinter([c], { action: 'remove', name })
        .then(() => toast('Printer removal requested.', `${name} on ${c.computer}. View progress under Jobs.`)).catch(oops));
    }
  });

  // --- Add a printer -------------------------------------------------------------------------------------------
  let adding = null; // { on, way } while the window is open
  const drawAdd = () => {
    const way = adding.way;
    $('pr-way').querySelectorAll('[data-way]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.way === way)));
    $('pr-dialog').querySelectorAll('[data-pr]').forEach((el) => { el.hidden = !el.dataset.pr.split(' ').includes(way); });
    $('pr-driver-typed-row').hidden = !(['tcpip', 'lpr'].includes(way) && $('pr-driver').value === '');
    const all = adding.on.length;
    $('pr-note').textContent = way === 'share' ? 'Added for all users at their next sign-in. The driver is installed from the print server.'
      : way === 'ipp' ? 'Uses the built-in Microsoft IPP Class Driver, so no download is needed. Most network printers from the last ten years support IPP.'
      : way === 'url' ? 'For printers with a Web Services for Devices (WSD) address. Windows selects the driver.'
      : 'Uses the selected driver. If the computer does not have it, the driver is installed from the driver file, if one is given.';
    $('pr-note').textContent += ` Runs as a SYSTEM job${all > 1 ? ` on ${many(all)}` : ''}.`;
  };
  const openAdd = async (on) => {
    const list = on.filter((c) => c.approved && !c.server);
    if (!list.length) {
      toast('No computers selected.', 'Select approved computers.', { tone: 'warn' });
      return true;
    }
    adding = { on: list, way: 'ipp' };
    for (const id of ['pr-name', 'pr-address', 'pr-ipp', 'pr-queue', 'pr-share', 'pr-url', 'pr-driver-typed', 'pr-inf', 'pr-count']) $(id).value = '';
    $('pr-port').value = '9100';
    $('pr-lead').textContent = `On ${names(list)}, for all users.`;
    $('pr-count-row').hidden = list.length <= BRAKE;
    $('pr-count-label').textContent = `This affects ${list.length} computers. Type ${list.length} to confirm`;
    // Drivers the computers reported, Windows' own first.
    let drivers = [];
    try {
      const all = await get('/api/devices/printers');
      drivers = [...new Set(all.computers.filter((x) => list.some((c) => c.id === x.id)).flatMap((x) => x.drivers))];
    } catch {
      // Windows' own are still there to pick.
    }
    const known = [...new Set([...drivers, ...CLASS])].filter((d) => !/OneNote|Print To PDF|Universal Print|Virtual Print|Point and Print/i.test(d));
    $('pr-driver').innerHTML = known.map((d) => `<option>${esc(d)}</option>`).join('') + '<option value="">Other driver…</option>';
    say('pr-problem', '');
    drawAdd();
    $('pr-dialog').showModal();
    $('pr-address').focus();
    return true;
  };
  $('pr-way').addEventListener('click', (e) => {
    const b = e.target.closest('[data-way]');
    if (!b || !adding) return;
    adding.way = b.dataset.way;
    drawAdd();
  });
  $('pr-driver').addEventListener('change', drawAdd);
  $('pr-cancel').addEventListener('click', () => $('pr-dialog').close());
  $('pr-go').addEventListener('click', async () => {
    const way = adding.way;
    const v = (id) => $(id).value.trim();
    const printer = { action: 'add', way, name: v('pr-name') || v('pr-address'), address: v('pr-address'), url: way === 'ipp' ? v('pr-ipp') : v('pr-url'),
      port: Number(v('pr-port')) || 9100, queue: v('pr-queue'), share: v('pr-share'), driver: v('pr-driver') || v('pr-driver-typed'), inf: v('pr-inf') };
    if (way === 'ipp' && !printer.name && printer.url) printer.name = printer.url.replace(/^\w+:\/\//, '').split(/[:/]/)[0];
    $('pr-go').disabled = true;
    try {
      await runPrinter(adding.on, printer, adding.on.length > BRAKE ? Number(v('pr-count')) || 0 : 0);
      $('pr-dialog').close();
      toast('Printer installation requested.', `${way === 'share' ? printer.share : printer.name} on ${names(adding.on)}. View progress under Jobs.`);
    } catch (e) {
      if (!e.cancelled) say('pr-problem', e.message);
    }
    $('pr-go').disabled = false;
  });

  // In Actions, for the ticked computers.
  const items0 = H.actItems;
  H.actItems = (item) => [...(items0?.(item) || []), ...(can() ? [item('printer', 'Add printer…')] : [])];
  const act0 = H.act;
  H.act = (act, on) => (act === 'printer' ? (openAdd(on), true) : act0?.(act, on) || false);

  // --- Printers: every printer the domain's servers share, and every printer on the computers ------------------
  let all = null; // /api/devices/printers
  let adp = null; // /api/ad/printers: what the servers share (asked one by one, PrintServers.cs) and what's in Active Directory
  let dev = null; // /api/devices/printers/supplies: what each printer says about itself over SNMP (PrinterDevices.cs)
  let rows = []; // the two together: a printer a server shares and computers have is one row
  let tab = 'printers'; // or servers: the print servers found, and the servers that couldn't be asked
  let again = false; // Refresh was pressed: the servers are asked again
  let followTimer = 0; // reads again shortly while servers are still answering
  const servers = () => adp?.servers || [];
  // \\print01.amw.com\Front and \\PRINT01\Front are the same place.
  const unc = (text) => {
    const m = /^\\\\([^\\]+)\\(.+)$/.exec(text || '');
    return m ? `${m[1].split('.')[0]}\\${m[2]}`.toLowerCase() : '';
  };
  // The printer's own address, as the server worked it out from a computer's port or the printer's location.
  const direct = (p) => p.ip || ((p.kind === 'tcpip' || p.kind === 'lpr') && p.address ? p.address : '');
  const LOW = 10; // a supply at this many percent or less is nearly out
  // Which ink a supply is, from its name, for its bar's colour: k, c, m, y, or nothing for a drum or a waste box.
  const ink = (name) => (/black|\bbk\b|\bk\b/i.test(name) ? 'k' : /cyan/i.test(name) ? 'c' : /magenta/i.test(name) ? 'm' : /yellow/i.test(name) ? 'y' : '');
  // The supply with the least left, among the ones the printer gives a level for.
  const lowest = (p) => (p.device?.supplies || []).filter((s) => s.percent != null).sort((a, b) => a.percent - b.percent)[0];
  // Toner, in a word: the lowest supply, OK when the printer answers without levels, nothing when it didn't answer.
  const toner = (p) => {
    const s = lowest(p);
    return s ? `${s.name} ${s.percent}%` : p.device?.answered && p.device.supplies.length ? 'OK' : '';
  };
  // One row per printer, the way a person counts them. What a server shares is the row, and the computers connected to it
  // (by its share, or straight to its address) go on it. What only computers have gets a row of its own. Each computer
  // keeps the name it has the printer under (as), which is what Remove needs.
  const merge = () => {
    rows = (adp?.printers || []).map((a) => ({ name: a.name, kind: 'share', address: a.share || '', drivers: a.driver ? [a.driver] : [], computers: [],
      server: a.server, location: a.location, share: a.share, port: a.port, description: a.description, ip: a.ip || '', status: a.status || '', jobs: a.jobs,
      published: a.published, found: a.found, listed: true, problems: [...(a.problems || [])] }));
    const byShare = new Map();
    const byIp = new Map();
    for (const r of rows) {
      const short = (r.server || '').split('.')[0];
      for (const k of [unc(r.share), `${short}\\${r.name}`.toLowerCase()]) if (k && !byShare.has(k)) byShare.set(k, r);
      if (r.ip && !byIp.has(r.ip)) byIp.set(r.ip, r);
    }
    const fold = (row, p) => {
      for (const c of p.computers) if (!row.computers.some((x) => x.id === c.id)) row.computers.push({ ...c, as: p.name });
      for (const d of p.drivers) if (!row.drivers.includes(d)) row.drivers.push(d);
    };
    const managed = servers().some((s) => s.printers > 0);
    for (const p of (all?.printers || []).filter((x) => x.kind !== 'virtual')) {
      // A connection to a server's printer is named \\server\Printer, or "Printer on server".
      const on = / on ([^\s\\]+)$/i.exec(p.name);
      const shared = byShare.get(unc(p.name)) || byShare.get(unc(p.address)) || (on && byShare.get(`${on[1].split('.')[0]}\\${p.name.slice(0, on.index)}`.toLowerCase()));
      if (shared) {
        fold(shared, p);
        continue;
      }
      const queue = direct(p) && byIp.get(direct(p));
      if (queue) {
        fold(queue, p);
        const around = 'Some computers print directly to the printer IP address, bypassing the server queue.';
        if (!queue.problems.includes(around)) queue.problems.push(around);
        continue;
      }
      const row = { ...p, computers: p.computers.map((c) => ({ ...c, as: p.name })), drivers: [...p.drivers], ip: direct(p), problems: [] };
      const from = unc(p.name).split('\\')[0];
      // Its server answered, shares printers, and this isn't one of them any more.
      if (from && servers().some((s) => s.server.toLowerCase() === from && !s.problem)) row.problems.push(`Computers connect to it on ${from.toUpperCase()}, but that server no longer shares it.`);
      else if (managed && row.ip) row.problems.push('Added manually on computers. It is not on a print server, so it is not centrally managed.');
      rows.push(row);
    }
    // What each printer says about itself, onto every row at its address; a supply that's nearly out is a problem.
    const said = new Map((dev?.devices || []).map((d) => [d.ip, d]));
    for (const r of rows) {
      r.device = r.ip ? said.get(r.ip) : null;
      for (const s of r.device?.supplies || []) if (s.percent != null && s.percent <= LOW) r.problems.push(`${s.name} is low (${s.percent}%).`);
    }
  };
  // What shows under Address: the printer's own address, never the share (that has its own column).
  const addr = (p) => p.ip || (p.listed ? '' : p.address) || '';
  const probs = (p) => (p.problems?.length > 1 ? `${p.problems.length} problems` : p.problems?.[0] || '');
  const onText = (n) => (n ? many(n) : 'None');
  let printersG = null;
  const printersGrid = () => (printersG ||= R.grid({
    el: $('dv-printers'), key: 'printers', sort: { key: 'name', dir: 1 },
    cols: [
      { key: 'name', label: 'Printer', w: 240, fixed: true, get: (p) => p.name, cell: (p) => `<div class="who"><span class="pic">${svg('printer')}</span><b class="ell">${esc(p.name)}</b></div>` },
      { key: 'server', label: 'Print server', w: 130, on: true, get: (p) => p.server || '', cell: (p) => `<span class="ell">${esc(p.server || '')}</span>` },
      { key: 'address', label: 'Address', w: 125, on: true, get: addr, cell: (p) => `<span class="mono muted ell">${esc(addr(p))}</span>` },
      // A queue its server listed with nothing wrong is Ready; one only computers have says nothing.
      { key: 'status', label: 'Status', w: 110, on: true, get: (p) => p.status || (p.found ? 'Ready' : ''), cell: (p) => `<span class="ell ${p.status ? 'pr-bad' : 'muted'}">${esc(p.status || (p.found ? 'Ready' : ''))}</span>` },
      // The supply with the least left, from the printer itself: sorts by how much that is, the ones that didn't say last.
      { key: 'toner', label: 'Toner', w: 200, on: true, get: (p) => lowest(p)?.percent ?? (toner(p) ? 100 : 101), text: toner,
        cell: (p) => `<span class="ell ${lowest(p)?.percent <= LOW ? 'pr-bad' : ''}">${esc(toner(p))}</span>` },
      { key: 'problems', label: 'Problems', w: 250, on: true, get: (p) => p.problems?.length || 0, text: (p) => (p.problems || []).join(' '), cell: (p) => `<span class="ell pr-bad">${esc(probs(p))}</span>` },
      { key: 'on', label: 'Computers', w: 120, on: true, get: (p) => p.computers.length, text: (p) => onText(p.computers.length), cell: (p) => esc(onText(p.computers.length)) },
      // What the printer calls itself, which the driver's name often isn't.
      { key: 'model', label: 'Model', w: 220, on: true, get: (p) => p.device?.model || '', cell: (p) => `<span class="ell">${esc(p.device?.model || '')}</span>` },
      { key: 'serial', label: 'Serial number', w: 140, get: (p) => p.device?.serial || '', cell: (p) => `<span class="mono muted ell">${esc(p.device?.serial || '')}</span>` },
      { key: 'pages', label: 'Pages printed', w: 120, get: (p) => p.device?.pages ?? '', cell: (p) => esc(p.device?.pages?.toLocaleString() ?? '') },
      { key: 'driver', label: 'Driver', w: 210, on: true, get: (p) => p.drivers.join(', '), cell: (p) => `<span class="ell">${esc(p.drivers.length > 1 ? `${p.drivers.length} drivers` : p.drivers[0] || '')}</span>` },
      { key: 'location', label: 'Location', w: 140, on: true, get: (p) => p.location || '', cell: (p) => `<span class="ell">${esc(p.location || '')}</span>` },
      { key: 'kind', label: 'Type', w: 130, on: true, get: (p) => KIND[p.kind] || p.kind },
      { key: 'jobs', label: 'Jobs in queue', w: 110, get: (p) => p.jobs ?? '', cell: (p) => esc(p.jobs ?? '') },
      { key: 'share', label: 'Share', w: 240, get: (p) => p.share || '', cell: (p) => `<span class="mono muted ell">${esc(p.share || '')}</span>` },
      { key: 'port', label: 'Port', w: 150, get: (p) => p.port || '', cell: (p) => `<span class="mono muted ell">${esc(p.port || '')}</span>` },
      { key: 'description', label: 'Comment', w: 180, get: (p) => p.description || '', cell: (p) => `<span class="ell">${esc(p.description || '')}</span>` },
      { key: 'published', label: 'Listed in AD', w: 80, get: (p) => (p.published ? 'Yes' : 'No') },
    ],
    row: (p) => ` data-pr-row="${esc(`${p.name}\n${p.address || ''}`)}"`,
    empty: () => D.more.printers.empty(), loading: () => !all && !D.failed(),
    redraw: () => D.draw(),
  }));
  // The servers that share printers, and the ones that couldn't be asked.
  let serversG = null;
  const serversGrid = () => (serversG ||= R.grid({
    el: $('dv-pservers'), key: 'pservers', sort: { key: 'printers', dir: -1 },
    cols: [
      { key: 'server', label: 'Server', w: 220, fixed: true, get: (s) => s.server, cell: (s) => `<div class="who"><span class="pic">${svg('server')}</span><b class="ell">${esc(s.server)}</b></div>` },
      { key: 'host', label: 'Full name', w: 230, on: true, get: (s) => s.host, cell: (s) => `<span class="mono muted ell">${esc(s.host)}</span>` },
      { key: 'printers', label: 'Printers', w: 100, on: true, get: (s) => s.printers },
      { key: 'status', label: 'Status', w: 380, on: true, get: (s) => s.problem || 'OK', cell: (s) => `<span class="ell${s.problem ? '' : ' muted'}">${esc(s.problem || 'OK')}</span>` },
      { key: 'at', label: 'Last checked', w: 170, on: true, get: (s) => new Date(s.at).getTime(), text: (s) => s.at, cell: (s) => `<span class="muted">${esc(R.when(s.at))}</span>` },
    ],
    row: (s) => ` data-ps-row="${esc(s.server)}"`,
    empty: () => D.more.printers.empty(), loading: () => !all && !D.failed(),
    redraw: () => D.draw(),
  }));
  const shown = () => rows;
  const PRINTER_FIELDS = [['Printer', [{ key: 'name', label: 'Name', type: 'text', get: (p) => p.name }, { key: 'kind', label: 'Type', type: 'choice', get: (p) => KIND[p.kind] || p.kind },
    { key: 'on', label: 'Computers', type: 'number', get: (p) => p.computers.length }, { key: 'server', label: 'Print server', type: 'choice', get: (p) => p.server || '' },
    { key: 'location', label: 'Location', type: 'text', get: (p) => p.location || '' }, { key: 'published', label: 'Listed in AD', type: 'choice', get: (p) => (p.published ? 'Yes' : 'No') },
    { key: 'status', label: 'Status', type: 'choice', get: (p) => p.status || (p.found ? 'Ready' : '') }, { key: 'problems', label: 'Has problems', type: 'choice', get: (p) => (p.problems?.length ? 'Yes' : 'No') },
    { key: 'jobs', label: 'Jobs in queue', type: 'number', get: (p) => p.jobs ?? 0 }, { key: 'toner', label: 'Lowest supply (%)', type: 'number', get: (p) => lowest(p)?.percent },
    { key: 'model', label: 'Model', type: 'choice', get: (p) => p.device?.model || '' }, { key: 'pages', label: 'Pages printed', type: 'number', get: (p) => p.device?.pages }]]];
  // What the printers say about themselves, read after the list so the list never waits on a printer. Their cells
  // fill in when it comes; printers still answering are read again shortly, for a couple of minutes at most.
  let supplyTimer = 0;
  let supplyTries = 0;
  const supplies = async (ask = false) => {
    clearTimeout(supplyTimer);
    const was = JSON.stringify(dev);
    try {
      dev = await get(`/api/devices/printers/supplies${ask ? '?again=true' : ''}`);
    } catch {
      return; // the list stands without them
    }
    merge();
    if (D.at() !== 'printers') return;
    if (JSON.stringify(dev) !== was) D.draw(); // drawn again only when a printer said something new
    if (dev.asking && supplyTries++ < 20) supplyTimer = setTimeout(() => supplies(), 5000);
  };
  D.lists.printers = ['Printers', 'printer', 'All printers on your print servers and computers.'];
  D.more.printers = {
    get table() { return tab === 'servers' ? 'dv-pservers' : tab === 'jobs' ? 'dv-pjobs' : 'dv-printers'; },
    get noun() { return tab === 'servers' ? 'print servers' : tab === 'jobs' ? 'print jobs' : 'printers'; },
    // Print jobs has ticks of its own; the other two lists have none.
    get acts() { return tab === 'jobs' ? jobsActs : undefined; },
    grid: () => (tab === 'servers' ? serversGrid() : tab === 'jobs' ? jobsGrid() : printersGrid()),
    rows: () => (tab === 'servers' ? servers() : tab === 'jobs' ? jobs || [] : shown()),
    count: () => (tab === 'jobs' ? jobs?.length ?? '' : all ? shown().length : ''), can: () => has('seats.view'),
    every: 60000, // read again a minute apart while it's open: Active Directory and the printers are asked too
    load: async () => {
      // The computers' printers, and the servers' when this person may see Active Directory (none when it isn't set up).
      // The servers are asked at most every six hours; Refresh asks them again.
      const ask = again;
      again = false;
      [all, adp] = await Promise.all([get('/api/devices/printers'), has('ad.view') ? get(`/api/ad/printers${ask ? '?again=true' : ''}`).catch(() => null) : null]);
      if (tab === 'jobs') await loadJobs();
      merge();
      supplyTries = 0;
      supplies(ask);
      // Servers still answering: read again shortly. The server calls a search over after 90 seconds, so this ends.
      clearTimeout(followTimer);
      if (adp?.scanning && D.at() === 'printers') {
        followTimer = setTimeout(async () => {
          if (D.at() !== 'printers') return;
          await D.more.printers.load().catch(() => {});
          D.draw();
        }, 6000);
      }
    },
    empty: () => {
      if (!all) return 'Could not load the list.';
      if (tab === 'servers') return !adp ? 'Active Directory is not configured, so print servers cannot be found.' : adp.scanning ? 'Querying print servers…' : 'No print servers found in Active Directory.';
      if (tab === 'jobs') return jobs ? 'No documents are waiting in any print queue.' : 'Could not load the print jobs.';
      return all.computers.some((c) => c.read) ? 'No printers found on print servers or computers.' : `No printers found on print servers, and no computer has reported its printers yet. Clients ${NEEDS} and later report printers within 30 minutes.`;
    },
    fields: () => (tab === 'servers' ? null : tab === 'jobs' ? JOB_FIELDS : PRINTER_FIELDS),
  };
  const markRow = () => document.querySelectorAll('#dv-printers [data-pr-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('printers', el.dataset.prRow)));
  panel.el.addEventListener('close', markRow);
  $('dv-printers').addEventListener('click', (e) => {
    const el = e.target.closest('[data-pr-row]');
    if (!el) return;
    const key = el.dataset.prRow;
    if (panel.shows('printers', key)) {
      panel.close();
      return;
    }
    const p = shown().find((x) => `${x.name}\n${x.address || ''}` === key);
    if (!p) return;
    openPrinter(p, key);
  });
  // A printer in the panel: Summary, and for a queue a print server shares, its Queue and History (print queues below).
  let pv = null; // { p, key, tab } while one is open
  const openPrinter = (p, key, tab = 'summary') => {
    reset(p);
    pv = { p, key, tab };
    drawPrinter();
    panel.open('printers', key);
    markRow();
    pollQueue();
  };
  const drawPrinter = () => {
    const { p } = pv;
    const pcs = p.computers.map((x) => D.rows().find((c) => c.id === x.id)).filter(Boolean);
    const line = (label, value, mono = false) => (value === '' || value == null ? '' : `<li><b>${label}</b><span${mono ? ' class="mono"' : ''}>${esc(value)}</span></li>`);
    const queued = p.listed && p.share && p.found;
    const tabs = queued ? `<div class="tabs2 dw-tabs" role="tablist">${[['summary', 'Summary'], ['queue', 'Queue', q.read?.share === p.share ? q.read.jobs.length : p.jobs], ['history', 'History']]
      .map(([t, label, n]) => `<a role="tab" data-pq-tab="${t}"${pv.tab === t ? ' aria-current="true"' : ''}>${esc(label)}${n == null || n === '' ? '' : `<small>${esc(String(n))}</small>`}</a>`).join('')}</div>` : '';
    const bar = [can() && p.kind !== 'universal' && p.computers.length ? `<button type="button" class="dw-qa" data-prl-remove>${svg('trash')}<span>Remove from all computers</span></button>` : '',
      queued && has('seats.printers') ? `<button type="button" class="dw-qa" data-pq-printer="${p.status === 'Paused' ? 'resume' : 'pause'}">${svg(p.status === 'Paused' ? 'play' : 'pause')}<span>${p.status === 'Paused' ? 'Resume printing' : 'Pause printing'}</span></button>`
        + `<button type="button" class="dw-qa" data-pq-printer="purge">${svg('x')}<span>Cancel all jobs</span></button>` : ''].join('');
    const head = `<div class="dw-head"><span class="pic dw-pic">${svg('printer')}</span><div class="dw-name"><h2 id="dw-title">${esc(p.name)}</h2><p>${esc([KIND[p.kind] || p.kind, addr(p) || p.server].filter(Boolean).join(' · '))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>${bar ? `<div class="dw-bar">${bar}</div>` : ''}${tabs}`;
    if (queued && pv.tab !== 'summary') {
      panel.body.innerHTML = head + (pv.tab === 'queue' ? queuePane(p) : historyPane(p));
      afterPane(p);
      wireRemove(p, pcs);
      return;
    }
    panel.body.innerHTML = head
      + (p.problems?.length ? section(`Problems <small>${p.problems.length}</small>`, `<ul class="pr-probs">${p.problems.map((t) => `<li>${svg('alert')}<span>${esc(t)}</span></li>`).join('')}</ul>`) : '')
      // What the printer itself said over SNMP: its supplies as bars in their own colours, then who it is.
      + (!p.device ? '' : !p.device.answered ? section('Printer status (SNMP)', none(`No SNMP response from ${p.ip}. The printer may be off, on a network this server cannot reach, or have SNMP disabled.`))
        : section('Printer status (SNMP)', (p.device.supplies.length ? `<div class="pr-sups">${p.device.supplies.map((s) => `<div class="pr-sup ${ink(s.name)}${s.percent != null && s.percent <= LOW ? ' low' : ''}"><span>${esc(s.name)}</span>`
          + `${s.percent != null ? `<progress max="100" value="${s.percent}"></progress><b>${s.percent}%</b>` : `<i></i><b>${s.some ? 'OK' : ''}</b>`}</div>`).join('')}</div>` : '')
          + `<ul class="does wide">${line('Model', p.device.model)}${line('Serial number', p.device.serial, true)}${line('Pages printed', p.device.pages?.toLocaleString())}${line('Last checked', R.when(p.device.at))}</ul>`))
      + (p.listed ? section('Print server', `<ul class="does wide">${line('Print server', p.server)}${line('Share', p.share, true)}${line('Address', p.ip, true)}${line('Port', p.port, true)}`
        + `${line('Location', p.location || 'Not set')}${line('Comment', p.description)}${line('Status', p.found ? p.status || 'Ready' : '')}${line('Jobs in queue', p.found ? p.jobs ?? '' : '')}`
        + `${line('In Active Directory', p.published ? 'Listed' : 'Not listed')}</ul>`) : '')
      + section(`Computers <small>${p.computers.length}</small>`, p.computers.length ? `<div class="dw-list">${p.computers.map((x) => `<div class="mrow acc-row" data-prl-pc="${esc(x.id)}"><span class="pic small">${svg('monitor')}</span>`
        + `<div class="two"><b>${esc(x.computer)}</b><small>${esc([x.driver, x.port].filter(Boolean).join(' · '))}</small></div>${x.offline ? chip('Offline', 'off', 'power') : ''}${svg('right', 'acc-go')}</div>`).join('')}</div>` : none('Not installed on any computer. Add it from a computer panel, or select computers and use Actions.'));
    // Each computer has it under its own name (a connection is \\server\Printer there): one job for each name.
    wireRemove(p, pcs);
  };
  const wireRemove = (p, pcs) => {
    const named = new Map();
    for (const x of p.computers) {
      const pc = pcs.find((c) => c.id === x.id);
      if (pc) named.set(x.as || p.name, [...(named.get(x.as || p.name) || []), pc]);
    }
    panel.body.querySelector('[data-prl-remove]')?.addEventListener('click', () => sure(`Remove ${p.name} from ${many(pcs.length)}?`, `${names(pcs)}. The printer is removed for all users on each computer.`, 'Remove', async () => {
      try {
        for (const [name, on] of named) await runPrinter(on, { action: 'remove', name }, on.length > BRAKE ? on.length : 0);
        toast('Printer removal requested.', `${p.name} on ${names(pcs)}. View progress under Jobs.`);
      } catch (e) {
        oops(e);
      }
    }));
  };
  // A print server in the panel: what it shares, or why it couldn't be asked.
  const markServer = () => document.querySelectorAll('#dv-pservers [data-ps-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('pservers', el.dataset.psRow)));
  panel.el.addEventListener('close', markServer);
  $('dv-pservers').addEventListener('click', (e) => {
    const el = e.target.closest('[data-ps-row]');
    if (!el) return;
    const key = el.dataset.psRow;
    if (panel.shows('pservers', key)) {
      panel.close();
      return;
    }
    const s = servers().find((x) => x.server === key);
    if (!s) return;
    const its = rows.filter((p) => p.found && (p.server || '').toLowerCase() === s.server.toLowerCase());
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('server')}</span><div class="dw-name"><h2 id="dw-title">${esc(s.server)}</h2><p>${esc(s.host)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (s.problem ? section('Unreachable', `<ul class="pr-probs"><li>${svg('alert')}<span>${esc(s.problem)}</span></li></ul>`
        + `<p class="muted dw-empty">${esc(R.app())} connects over File and Printer Sharing (port 445) using this server's computer account. Last checked ${esc(R.when(s.at))}.</p>`) : '')
      + (s.problem ? '' : section(`Printers <small>${its.length}</small>`, its.length ? `<div class="dw-list">${its.map((p) => `<div class="mrow acc-row" data-ps-printer="${esc(`${p.name}\n${p.address || ''}`)}"><span class="pic small">${svg('printer')}</span>`
        + `<div class="two"><b>${esc(p.name)}</b><small>${esc([p.drivers[0], p.ip || p.port].filter(Boolean).join(' · '))}</small></div>${p.status ? chip(p.status, 'off') : ''}${svg('right', 'acc-go')}</div>`).join('')}</div>` : none('No shared printers.')));
    panel.open('pservers', key);
    markServer();
  });
  // A printer in a server's panel: over to Printers, with that printer open.
  panel.el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-ps-printer]');
    if (!b || !panel.is('pservers')) return;
    const key = b.dataset.psPrinter;
    tab = 'printers';
    panel.close();
    D.draw();
    [...document.querySelectorAll('#dv-printers [data-pr-row]')].find((el) => el.dataset.prRow === key)?.click();
  });
  panel.el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-prl-pc]');
    if (b && panel.is('printers')) D.open(b.dataset.prlPc, 'printers');
  });
  // --- Print queues (PrintQueues.cs): a queue's jobs, read every 5 seconds while its panel shows them, what finished,
  // and Print jobs, every queue at once. Changing a job takes Manage print queues and runs as the service account. ---
  const q = { read: null, history: null, picked: new Set(), timer: 0 };
  const manage = () => has('seats.printers');
  const VERB = { pause: ['Pausing', 'Paused'], resume: ['Resuming', 'Resumed'], restart: ['Restarting', 'Restarted'], cancel: ['Cancelling', 'Cancelled'] };
  const STATE = { Error: ['alert', 'bad'], Offline: ['alert', 'bad'], 'Out of paper': ['alert', 'bad'], Paused: ['pause', 'warn'], Printing: ['printer', 'on'], Printed: ['check', 'ok'], Cancelling: ['x', ''] };
  const stateCell = (j) => { const [icon, cls] = STATE[j.state] || ['clock', '']; return `<span class="pq-state ${cls}">${svg(icon)}${esc(j.state)}</span>`; };
  const pagesCell = (j) => `<span class="pq-pages"><progress max="${Math.max(j.pages, 1)}" value="${Math.min(j.printed, Math.max(j.pages, 1))}"></progress>${j.printed} of ${j.pages}</span>`;
  const clockOf = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '');
  const dayOf = (iso) => (iso ? new Date(iso).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }) : '');
  const jobBtn = (act, j, icon, tip) => `<button type="button" class="icon" data-pq-job="${act}:${j.id}" data-pq-share="${esc(j.share)}" data-tip="${tip}" aria-label="${tip} ${esc(j.document)}">${svg(icon)}</button>`;
  const rowActs = (j) => (manage() ? `<span class="pq-acts">${j.state === 'Paused' ? jobBtn('resume', j, 'play', 'Resume') : jobBtn('pause', j, 'pause', 'Pause')}${jobBtn('restart', j, 'restart', 'Restart')}${jobBtn('cancel', j, 'x', 'Cancel')}</span>` : '');
  const tickBox = (attr, key, label, on) => `<label class="tk"><input type="checkbox" ${attr}="${esc(key)}" aria-label="Select ${esc(label)}"${on ? ' checked' : ''}></label>`;
  const JOB_COLS = (withPrinter) => [
    { key: 'document', label: 'Document', w: 200, fixed: true, get: (j) => j.document, cell: (j) => `<div class="who"><span class="pic">${svg('printer')}</span><b class="ell">${esc(j.document)}</b></div>` },
    ...(withPrinter ? [{ key: 'printer', label: 'Printer', w: 150, on: true, get: (j) => j.printer, cell: (j) => `<span class="ell">${esc(j.printer)}</span>` }] : []),
    { key: 'state', label: 'Status', w: 125, on: true, get: (j) => j.state, cell: stateCell },
    { key: 'pages', label: 'Pages', w: 140, on: true, get: (j) => j.pages, text: (j) => `${j.printed} of ${j.pages}`, cell: pagesCell },
    { key: 'owner', label: 'Owner', w: 130, on: true, get: (j) => j.owner || '' },
    { key: 'computer', label: 'Computer', w: 120, on: true, get: (j) => j.computer || '' },
    { key: 'submitted', label: 'Submitted', w: 110, on: true, get: (j) => (j.submitted ? new Date(j.submitted).getTime() : 0), text: (j) => clockOf(j.submitted), cell: (j) => `<span class="muted">${esc(clockOf(j.submitted))}</span>` },
    ...(withPrinter ? [{ key: 'server', label: 'Print server', w: 120, get: (j) => j.server }] : []),
    { key: 'position', label: 'Position', w: 80, get: (j) => j.position },
    { key: 'id', label: 'Job ID', w: 80, get: (j) => j.id },
  ];
  const jobsOf = (r) => (r?.jobs || []).map((j) => ({ ...j, printer: r.printer, server: r.server, share: r.share }));

  // The open printer's queue and history in its panel.
  let qG = null;
  const qGrid = () => (qG ||= R.grid({
    el: () => $('pq-jobs'), key: 'pqueue', sort: { key: 'position', dir: 1 }, cols: JOB_COLS(false),
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-pq-tick-all aria-label="Select all"></label>', cell: (j) => tickBox('data-pq-tick', j.id, j.document, q.picked.has(j.id)) },
    tail: { w: 100, cell: rowActs }, row: (j) => ` data-pq-row="${j.id}"`, empty: () => 'No jobs in the queue.', loading: () => !q.read, redraw: () => drawPrinter(),
  }));
  let hG = null;
  const hGrid = () => (hG ||= R.grid({
    el: () => $('pq-hist'), key: 'phistory', sort: { key: 'at', dir: -1 },
    cols: [
      { key: 'at', label: 'Finished', w: 140, fixed: true, get: (h) => new Date(h.at).getTime(), text: (h) => dayOf(h.at), cell: (h) => `<span class="muted">${esc(dayOf(h.at))}</span>` },
      { key: 'document', label: 'Document', w: 200, on: true, get: (h) => h.document, cell: (h) => `<b class="ell">${esc(h.document)}</b>` },
      { key: 'owner', label: 'Owner', w: 120, on: true, get: (h) => h.owner || '' },
      { key: 'computer', label: 'Computer', w: 110, on: true, get: (h) => h.computer || '' },
      { key: 'pages', label: 'Pages', w: 70, on: true, get: (h) => h.pages },
      { key: 'result', label: 'Result', w: 160, on: true, get: (h) => h.result, cell: (h) => `<span class="pq-state ${h.result === 'Printed' ? 'ok' : ''}">${svg(h.result === 'Printed' ? 'check' : 'x')}${esc(h.result)}</span>` },
    ],
    empty: () => 'No finished jobs yet.', loading: () => !q.history, redraw: () => drawPrinter(),
  }));
  const queuePane = (p) => {
    const r = q.read?.share === p.share ? q.read : null;
    const acts = manage() ? `<button type="button" class="small" data-pq-acts${q.picked.size ? '' : ' disabled'}>Actions${q.picked.size ? ` <small>${q.picked.size}</small>` : ''}${svg('chevron-down')}</button>` : '';
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Queue${r ? ` <small>${r.jobs.length}</small>` : ''}</h3>${acts}</div>${r?.problem ? `<p class="hint problem">${esc(r.problem)}</p>` : ''}`
      + `<div class="table adt pq-table" id="pq-jobs"></div><p class="hint">Refreshed from ${esc(p.server)} every 5 seconds.</p></section>`;
  };
  const historyPane = (p) => {
    const h = q.history?.share === p.share ? q.history : null;
    const off = (h?.servers || []).filter((s) => s.problem).map((s) => `<p class="hint warn pq-why">${svg('alert')}<span>${esc(s.problem)}</span></p>`).join('');
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>History${h ? ` <small>${h.rows.length}</small>` : ''}</h3></div>${h?.error ? `<p class="hint problem">${esc(h.error)}</p>` : ''}${off}`
      + `<div class="table adt pq-table" id="pq-hist"></div><p class="hint">Jobs that finished in the last ${h?.keep || 30} days, read from the PrintService log on ${esc(p.server)} every few minutes. How long they are kept is in Settings, Devices.</p></section>`;
  };
  const afterPane = (p) => {
    if (pv.tab === 'queue') {
      qGrid().draw(q.read?.share === p.share ? jobsOf(q.read) : []);
      const all = panel.body.querySelector('[data-pq-tick-all]');
      if (all && q.read) { all.checked = q.read.jobs.length > 0 && q.picked.size === q.read.jobs.length; all.indeterminate = q.picked.size > 0 && !all.checked; }
    } else {
      if (q.history?.share !== p.share) loadPrinted(p);
      hGrid().draw(q.history?.share === p.share ? q.history.rows : []);
    }
  };
  const loadQueue = async () => {
    const p = pv?.p;
    if (!p?.share) return;
    try {
      const r = await get(`/api/devices/printers/queue?share=${encodeURIComponent(p.share)}`);
      const was = JSON.stringify(q.read);
      q.read = r;
      q.picked = new Set([...q.picked].filter((id) => r.jobs.some((j) => j.id === id)));
      if (pv?.p === p && panel.shows('printers', pv.key) && JSON.stringify(r) !== was) drawPrinter();
    } catch {
      // What was read stays; the next look tries again.
    }
  };
  const loadPrinted = async (p) => {
    q.history = null;
    try {
      q.history = { ...(await get(`/api/devices/printers/history?share=${encodeURIComponent(p.share)}`)), share: p.share };
    } catch (e) {
      q.history = { rows: [], servers: [], share: p.share, error: e.message };
    }
    if (pv?.p === p && pv.tab === 'history' && panel.shows('printers', pv.key)) drawPrinter();
  };
  const pollQueue = () => {
    clearTimeout(q.timer);
    if (!pv || !panel.shows('printers', pv.key) || !(pv.p.listed && pv.p.share && pv.p.found)) return;
    if (document.visibilityState === 'visible') loadQueue();
    q.timer = setTimeout(pollQueue, 5000);
  };
  // A different printer starts with nothing read.
  const reset = (p) => { if (q.read?.share !== p.share) { q.read = null; q.picked.clear(); } q.history = null; };

  // Jobs changed: one share at a time, cancelling after a confirm; the queue and Print jobs read again after.
  const changeJobs = async (list, act, host) => {
    const go = async () => {
      const shares = [...new Set(list.map((j) => j.share))];
      let done = 0;
      const problems = [];
      if (host) R.busy(host, VERB[act][0], `${list.length} print ${list.length === 1 ? 'job' : 'jobs'}`);
      try {
        for (const share of shares) {
          const a = await send('/api/devices/printers/jobs', { share, ids: list.filter((j) => j.share === share).map((j) => j.id), action: act });
          done += a.done;
          problems.push(...a.problems);
        }
        if (problems.length) toast(done ? `Done for ${done} of ${list.length}.` : 'The print jobs could not be changed.', problems[0], { tone: 'warn' });
        else toast(`${VERB[act][1]} ${done} print ${done === 1 ? 'job' : 'jobs'}.`, list.length === 1 ? list[0].document : '');
        q.picked.clear();
        jp.clear();
      } catch (e) {
        oops(e);
      } finally {
        if (host) R.busy(host, '');
      }
      await loadQueue();
      if (tab === 'jobs') await loadJobs(true);
    };
    if (act === 'cancel') sure(list.length === 1 ? 'Cancel this print job?' : `Cancel ${list.length} print jobs?`, 'The documents are removed from the queue and do not print.', 'Cancel jobs', go);
    else await go();
  };
  const changePrinter = (p, act) => {
    const go = async () => {
      R.busy(panel.body, act === 'purge' ? 'Cancelling all jobs' : act === 'pause' ? 'Pausing printing' : 'Resuming printing', p.name);
      try {
        await send('/api/devices/printers/queue', { share: p.share, action: act });
        toast(act === 'purge' ? 'All print jobs cancelled.' : act === 'pause' ? 'Printing paused.' : 'Printing resumed.', `${p.name} on ${p.server}.`);
        if (act !== 'purge') p.status = act === 'pause' ? 'Paused' : '';
      } catch (e) {
        oops(e);
      } finally {
        R.busy(panel.body, '');
      }
      await loadQueue();
      if (pv?.p === p) drawPrinter();
    };
    if (act === 'purge') sure(`Cancel all jobs on ${p.name}?`, 'Every document waiting in this queue is removed and does not print.', 'Cancel all jobs', go);
    else go();
  };
  const jobActs = (button, list, host) => R.actions.open(button, {
    items: [{ act: 'pause', label: 'Pause' }, { act: 'resume', label: 'Resume' }, { act: 'restart', label: 'Restart' }, '-', { act: 'cancel', label: 'Cancel…', hint: String(list().length), danger: true }],
    pick: (act) => changeJobs(list(), act, host),
  });
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('printers') && !panel.is('pjobs')) return;
    let b;
    if ((b = e.target.closest('[data-pq-tab]')) && pv) { pv.tab = b.dataset.pqTab; drawPrinter(); return; }
    if ((b = e.target.closest('[data-pq-printer]')) && pv) { changePrinter(pv.p, b.dataset.pqPrinter); return; }
    if ((b = e.target.closest('[data-pq-acts]')) && q.read) { jobActs(b, () => jobsOf(q.read).filter((j) => q.picked.has(j.id)), panel.body); return; }
    if ((b = e.target.closest('[data-pq-job]'))) {
      const [act, id] = b.dataset.pqJob.split(':');
      const j = [...jobsOf(q.read), ...(jobs || [])].find((x) => x.id === Number(id) && x.share === b.dataset.pqShare);
      if (j) changeJobs([j], act, panel.body);
    }
  });
  panel.el.addEventListener('change', (e) => {
    if (!panel.is('printers') || !q.read) return;
    const one = e.target.closest('[data-pq-tick]');
    const all = e.target.closest('[data-pq-tick-all]');
    if (one) { const id = Number(one.dataset.pqTick); if (one.checked) q.picked.add(id); else q.picked.delete(id); } else if (all) q.picked = new Set(all.checked ? q.read.jobs.map((j) => j.id) : []);
    else return;
    drawPrinter();
  });

  // Print jobs: every queue the print servers share, read every 5 seconds while it's open.
  let jobs = null;
  const jp = new Set(); // ticked, as share|id
  const jkey = (j) => `${j.share}|${j.id}`;
  let jobsTimer = 0;
  const loadJobs = async (draw = false) => {
    try {
      const reads = await get('/api/devices/printers/jobs');
      const was = JSON.stringify(jobs);
      jobs = reads.flatMap(jobsOf);
      [...jp].forEach((k) => { if (!jobs.some((j) => jkey(j) === k)) jp.delete(k); });
      if ((draw || JSON.stringify(jobs) !== was) && D.at() === 'printers' && tab === 'jobs') D.draw();
    } catch {
      if (!jobs) jobs = [];
    }
  };
  const pollJobs = () => {
    clearTimeout(jobsTimer);
    if (D.at() !== 'printers' || tab !== 'jobs') return;
    if (document.visibilityState === 'visible') loadJobs();
    jobsTimer = setTimeout(pollJobs, 5000);
  };
  let jobsG = null;
  const jobsGrid = () => (jobsG ||= R.grid({
    el: $('dv-pjobs'), key: 'pjobs', sort: { key: 'submitted', dir: 1 }, cols: JOB_COLS(true),
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-pj-tick-all aria-label="Select all"></label>', cell: (j) => tickBox('data-pj-tick', jkey(j), j.document, jp.has(jkey(j))) },
    tail: { w: 100, cell: rowActs }, row: (j) => ` data-pj-row="${esc(jkey(j))}"`,
    empty: () => D.more.printers.empty(), loading: () => !jobs && !D.failed(), redraw: () => D.draw(),
  }));
  const JOB_FIELDS = [['Print job', [{ key: 'printer', label: 'Printer', type: 'choice', get: (j) => j.printer }, { key: 'owner', label: 'Owner', type: 'choice', get: (j) => j.owner || '' },
    { key: 'state', label: 'Status', type: 'choice', get: (j) => j.state }, { key: 'computer', label: 'Computer', type: 'choice', get: (j) => j.computer || '' },
    { key: 'document', label: 'Document', type: 'text', get: (j) => j.document }, { key: 'pages', label: 'Pages', type: 'number', get: (j) => j.pages },
    { key: 'server', label: 'Print server', type: 'choice', get: (j) => j.server }]]];
  const pickedJobs = () => (jobs || []).filter((j) => jp.has(jkey(j)));
  const jobsActs = { can: manage, count: () => jp.size, open: (button) => jobActs(button, pickedJobs, null) };
  const drawJobTicks = () => {
    const boxes = [...$('dv-pjobs').querySelectorAll('[data-pj-tick]')];
    boxes.forEach((b) => { b.checked = jp.has(b.dataset.pjTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const all = $('dv-pjobs').querySelector('[data-pj-tick-all]');
    if (all) { const on = boxes.filter((b) => b.checked).length; all.checked = !!boxes.length && on === boxes.length; all.indeterminate = on > 0 && on < boxes.length; }
    D.drawPicked?.();
  };
  $('dv-pjobs').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return;
      if (box.dataset.pjTickAll !== undefined) [...$('dv-pjobs').querySelectorAll('[data-pj-tick]')].forEach((b) => (box.checked ? jp.add(b.dataset.pjTick) : jp.delete(b.dataset.pjTick)));
      else if (box.checked) jp.add(box.dataset.pjTick);
      else jp.delete(box.dataset.pjTick);
      drawJobTicks();
      return;
    }
    if (e.target.closest('[data-pq-job]')) {
      const b = e.target.closest('[data-pq-job]');
      const [act, id] = b.dataset.pqJob.split(':');
      const j = (jobs || []).find((x) => x.id === Number(id) && x.share === b.dataset.pqShare);
      if (j) changeJobs([j], act, null);
      return;
    }
    const row = e.target.closest('[data-pj-row]');
    if (!row) return;
    if (panel.shows('pjobs', row.dataset.pjRow)) { panel.close(); return; }
    const j = (jobs || []).find((x) => jkey(x) === row.dataset.pjRow);
    if (j) openJob(j);
  });
  // A job in the panel: what it is and where, and what can be done to it.
  const openJob = (j) => {
    const fact = (label, value) => `<div><span class="l">${label}</span><span class="v">${value}</span></div>`;
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('printer')}</span><div class="dw-name"><h2 id="dw-title">${esc(j.document)}</h2><p>${esc(`${j.printer} · ${j.server}`)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (manage() ? `<div class="dw-bar">${j.state === 'Paused' ? `<button type="button" class="dw-qa" data-pq-job="resume:${j.id}" data-pq-share="${esc(j.share)}">${svg('play')}<span>Resume</span></button>`
        : `<button type="button" class="dw-qa" data-pq-job="pause:${j.id}" data-pq-share="${esc(j.share)}">${svg('pause')}<span>Pause</span></button>`}`
        + `<button type="button" class="dw-qa" data-pq-job="restart:${j.id}" data-pq-share="${esc(j.share)}">${svg('restart')}<span>Restart</span></button>`
        + `<button type="button" class="dw-qa" data-pq-job="cancel:${j.id}" data-pq-share="${esc(j.share)}">${svg('x')}<span>Cancel</span></button></div>` : '')
      + `<div class="dw-strip">${fact('Status', stateCell(j))}${fact('Pages', esc(`${j.printed} of ${j.pages}`))}${fact('Owner', esc(j.owner || 'Unknown'))}${fact('Computer', esc(j.computer || 'Unknown'))}</div>`
      + section('Details', `<ul class="does wide"><li><b>Submitted</b><span>${esc(dayOf(j.submitted))}</span></li><li><b>Position in queue</b><span>${j.position}</span></li>`
        + `<li><b>Job ID</b><span>${j.id}</span></li><li><b>Printer</b><span>${esc(j.printer)}</span></li><li><b>Share</b><span class="mono">${esc(j.share)}</span></li></ul>`);
    panel.open('pjobs', jkey(j));
  };

  // Settings, Devices: how long finished jobs are kept.
  const drawKeep = (keep, choices) => { $('ph-keep').innerHTML = choices.map((d) => `<button type="button" data-ph-keep="${d}" aria-pressed="${d === keep}">${d} days</button>`).join(''); };
  document.addEventListener('rabbidlab:me', () => {
    if (has('seats.settings')) get('/api/devices/printers/history?count=0').then((h) => drawKeep(h.keep, h.choices)).catch(() => {});
  });
  $('ph-keep').addEventListener('click', async (e) => {
    const b = e.target.closest('[data-ph-keep]');
    if (!b) return;
    try {
      const a = await send('/api/devices/printers/history/keep', { days: Number(b.dataset.phKeep) });
      drawKeep(a.keep, [5, 15, 30, 60, 90]);
      toast('Print job history setting saved.', `Finished print jobs are kept for ${a.keep} days.`);
    } catch (err) {
      oops(err);
    }
  });

  const drawn0 = H.drawn;
  H.drawn = () => {
    drawn0?.();
    const on = D.at() === 'printers';
    $('dv-printers').hidden = !(on && tab === 'printers');
    $('dv-pservers').hidden = !(on && tab === 'servers');
    $('dv-pjobs').hidden = !(on && tab === 'jobs');
    if (!on) {
      if (panel.is('printers') || panel.is('pservers') || panel.is('pjobs')) panel.close();
      clearTimeout(jobsTimer);
      return;
    }
    if (tab === 'jobs') drawJobTicks();
    // Two lists here: the printers, and the servers that share them (shown once Active Directory could be read).
    $('dv-tabs').innerHTML = `<a href="#seats/printers" data-pr-tab="printers"${tab === 'printers' ? ' aria-current="true"' : ''}>${svg('printer')}Printers <small>${all ? rows.length : ''}</small></a>`
      + (adp ? `<a href="#seats/printers" data-pr-tab="jobs"${tab === 'jobs' ? ' aria-current="true"' : ''}>${svg('list')}Print jobs <small>${jobs ? jobs.length : ''}</small></a>` : '')
      + (adp ? `<a href="#seats/printers" data-pr-tab="servers"${tab === 'servers' ? ' aria-current="true"' : ''}>${svg('server')}Print servers <small>${servers().filter((s) => s.printers > 0).length}</small></a>` : '');
    markRow();
    markServer();
  };
  $('dv-tabs').addEventListener('click', (e) => {
    const a = e.target.closest('[data-pr-tab]');
    if (!a) return;
    e.preventDefault();
    if (tab === a.dataset.prTab) return;
    tab = a.dataset.prTab;
    if (panel.is('printers') || panel.is('pservers') || panel.is('pjobs')) panel.close();
    D.draw();
    if (tab === 'jobs') { if (!jobs) loadJobs(true); pollJobs(); }
  });
  // Refresh on this list also asks the servers again (the server allows that once a minute). Heard first, so the read
  // Refresh starts carries it.
  $('dv-refresh').addEventListener('click', () => { if (D.at() === 'printers') again = true; }, true);
})();
