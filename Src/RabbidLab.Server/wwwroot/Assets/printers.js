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
  const oops = (e) => { if (!e.cancelled) toast("That didn't work.", e.message, { tone: 'warn' }); };
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
  const loadInv = async (id) => {
    try {
      const got = (await get(`/api/clients/${encodeURIComponent(id)}/inventory`)).inventory;
      inv[id] = { printers: got?.printers ?? null, drivers: got?.printerDrivers || [], sent: !!got };
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
    const add = can() && c.approved ? `<button type="button" class="small" data-pr-add>${svg('plus')}Add a printer</button>` : '';
    if (!p.printers) {
      return section('Printers', none(!p.sent ? "This computer hasn't sent its inventory yet." : below(c.version, NEEDS)
        ? `Its printers show once its client is ${NEEDS} or later. It updates itself within a few minutes.` : 'Windows didn’t say which printers it has.'), add);
    }
    const real = p.printers.filter((x) => x.kind !== 'virtual');
    const built = p.printers.filter((x) => x.kind === 'virtual');
    return section(`Printers <small>${real.length}</small>`, (real.length ? `<div class="dw-list">${real.map((x) => row(c, x)).join('')}</div>` : none('No printers yet.'))
      + '<p class="dw-how pr-note">Added for everyone who uses the computer. Changes go out as jobs, and its list updates after each one.</p>', add)
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
      sure(`Remove ${name}?`, `It's taken off ${c.computer} for everyone who uses it.`, 'Remove', () => runPrinter([c], { action: 'remove', name })
        .then(() => toast('Remove sent.', `${name} on ${c.computer}. It's under Jobs.`)).catch(oops));
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
    $('pr-note').textContent = way === 'share' ? 'Everyone who signs in to the computer gets it, the next time they sign in. Its driver comes from the print server.'
      : way === 'ipp' ? 'Uses Windows’ own IPP driver, so nothing has to be downloaded. Most network printers from the last ten years work this way.'
      : way === 'url' ? 'For a printer that gives a web services (WSD) address. Windows picks its driver.'
      : 'Uses the driver picked. A driver the computer doesn’t have comes from the driver file, when there is one.';
    $('pr-note').textContent += ` It goes out as a job${all > 1 ? ` to ${many(all)}` : ''} and runs as SYSTEM.`;
  };
  const openAdd = async (on) => {
    const list = on.filter((c) => c.approved && !c.server);
    if (!list.length) {
      toast('Nothing to add it to.', 'Pick computers that are approved.', { tone: 'warn' });
      return true;
    }
    adding = { on: list, way: 'ipp' };
    for (const id of ['pr-name', 'pr-address', 'pr-ipp', 'pr-queue', 'pr-share', 'pr-url', 'pr-driver-typed', 'pr-inf', 'pr-count']) $(id).value = '';
    $('pr-port').value = '9100';
    $('pr-lead').textContent = `On ${names(list)}, for everyone who uses ${list.length === 1 ? 'it' : 'them'}.`;
    $('pr-count-row').hidden = list.length <= BRAKE;
    $('pr-count-label').textContent = `That's ${list.length} computers. Type ${list.length} to go ahead`;
    // Drivers the computers reported, Windows' own first.
    let drivers = [];
    try {
      const all = await get('/api/devices/printers');
      drivers = [...new Set(all.computers.filter((x) => list.some((c) => c.id === x.id)).flatMap((x) => x.drivers))];
    } catch {
      // Windows' own are still there to pick.
    }
    const known = [...new Set([...drivers, ...CLASS])].filter((d) => !/OneNote|Print To PDF|Universal Print|Virtual Print|Point and Print/i.test(d));
    $('pr-driver').innerHTML = known.map((d) => `<option>${esc(d)}</option>`).join('') + '<option value="">Another driver…</option>';
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
      toast('Printer sent.', `${way === 'share' ? printer.share : printer.name} to ${names(adding.on)}. It's under Jobs.`);
    } catch (e) {
      if (!e.cancelled) say('pr-problem', e.message);
    }
    $('pr-go').disabled = false;
  });

  // In Actions, for the ticked computers.
  const items0 = H.actItems;
  H.actItems = (item) => [...(items0?.(item) || []), ...(can() ? [item('printer', 'Add a printer…')] : [])];
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
        const around = 'Some computers print straight to its address, around the server queue.';
        if (!queue.problems.includes(around)) queue.problems.push(around);
        continue;
      }
      const row = { ...p, computers: p.computers.map((c) => ({ ...c, as: p.name })), drivers: [...p.drivers], ip: direct(p), problems: [] };
      const from = unc(p.name).split('\\')[0];
      // Its server answered, shares printers, and this isn't one of them any more.
      if (from && servers().some((s) => s.server.toLowerCase() === from && !s.problem)) row.problems.push(`Computers connect to it on ${from.toUpperCase()}, but that server doesn't share it any more.`);
      else if (managed && row.ip) row.problems.push("Added by hand on computers. It's on no print server, so nobody manages it.");
      rows.push(row);
    }
    // What each printer says about itself, onto every row at its address; a supply that's nearly out is a problem.
    const said = new Map((dev?.devices || []).map((d) => [d.ip, d]));
    for (const r of rows) {
      r.device = r.ip ? said.get(r.ip) : null;
      for (const s of r.device?.supplies || []) if (s.percent != null && s.percent <= LOW) r.problems.push(`${s.name} is at ${s.percent}%: nearly out.`);
    }
  };
  // What shows under Address: the printer's own address, never the share (that has its own column).
  const addr = (p) => p.ip || (p.listed ? '' : p.address) || '';
  const probs = (p) => (p.problems?.length > 1 ? `${p.problems.length} problems` : p.problems?.[0] || '');
  const onText = (n) => (n ? many(n) : 'No computers yet');
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
      { key: 'on', label: 'On', w: 120, on: true, get: (p) => p.computers.length, text: (p) => onText(p.computers.length), cell: (p) => esc(onText(p.computers.length)) },
      // What the printer calls itself, which the driver's name often isn't.
      { key: 'model', label: 'Model', w: 220, on: true, get: (p) => p.device?.model || '', cell: (p) => `<span class="ell">${esc(p.device?.model || '')}</span>` },
      { key: 'serial', label: 'Serial number', w: 140, get: (p) => p.device?.serial || '', cell: (p) => `<span class="mono muted ell">${esc(p.device?.serial || '')}</span>` },
      { key: 'pages', label: 'Pages printed', w: 120, get: (p) => p.device?.pages ?? '', cell: (p) => esc(p.device?.pages?.toLocaleString() ?? '') },
      { key: 'driver', label: 'Driver', w: 210, on: true, get: (p) => p.drivers.join(', '), cell: (p) => `<span class="ell">${esc(p.drivers.length > 1 ? `${p.drivers.length} drivers` : p.drivers[0] || '')}</span>` },
      { key: 'location', label: 'Location', w: 140, on: true, get: (p) => p.location || '', cell: (p) => `<span class="ell">${esc(p.location || '')}</span>` },
      { key: 'kind', label: 'Kind', w: 130, on: true, get: (p) => KIND[p.kind] || p.kind },
      { key: 'jobs', label: 'Jobs waiting', w: 110, get: (p) => p.jobs ?? '', cell: (p) => esc(p.jobs ?? '') },
      { key: 'share', label: 'Share', w: 240, get: (p) => p.share || '', cell: (p) => `<span class="mono muted ell">${esc(p.share || '')}</span>` },
      { key: 'port', label: 'Port', w: 150, get: (p) => p.port || '', cell: (p) => `<span class="mono muted ell">${esc(p.port || '')}</span>` },
      { key: 'description', label: 'Comment', w: 180, get: (p) => p.description || '', cell: (p) => `<span class="ell">${esc(p.description || '')}</span>` },
      { key: 'published', label: 'In AD', w: 80, get: (p) => (p.published ? 'Yes' : 'No') },
    ],
    row: (p) => ` data-pr-row="${esc(`${p.name}\n${p.address || ''}`)}"`,
    empty: () => D.more.printers.empty(), loading: () => !all,
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
      { key: 'status', label: 'Status', w: 380, on: true, get: (s) => s.problem || 'Answered', cell: (s) => `<span class="ell${s.problem ? '' : ' muted'}">${esc(s.problem || 'Answered')}</span>` },
      { key: 'at', label: 'Asked', w: 170, on: true, get: (s) => new Date(s.at).getTime(), text: (s) => s.at, cell: (s) => `<span class="muted">${esc(R.when(s.at))}</span>` },
    ],
    row: (s) => ` data-ps-row="${esc(s.server)}"`,
    empty: () => D.more.printers.empty(), loading: () => !all,
    redraw: () => D.draw(),
  }));
  const shown = () => rows;
  const PRINTER_FIELDS = [['Printer', [{ key: 'name', label: 'Name', type: 'text', get: (p) => p.name }, { key: 'kind', label: 'Kind', type: 'choice', get: (p) => KIND[p.kind] || p.kind },
    { key: 'on', label: 'On (computers)', type: 'number', get: (p) => p.computers.length }, { key: 'server', label: 'Print server', type: 'choice', get: (p) => p.server || '' },
    { key: 'location', label: 'Location', type: 'text', get: (p) => p.location || '' }, { key: 'published', label: 'In AD', type: 'choice', get: (p) => (p.published ? 'Yes' : 'No') },
    { key: 'status', label: 'Status', type: 'choice', get: (p) => p.status || (p.found ? 'Ready' : '') }, { key: 'problems', label: 'Has problems', type: 'choice', get: (p) => (p.problems?.length ? 'Yes' : 'No') },
    { key: 'jobs', label: 'Jobs waiting', type: 'number', get: (p) => p.jobs ?? 0 }, { key: 'toner', label: 'Lowest supply (%)', type: 'number', get: (p) => lowest(p)?.percent },
    { key: 'model', label: 'Model', type: 'choice', get: (p) => p.device?.model || '' }, { key: 'pages', label: 'Pages printed', type: 'number', get: (p) => p.device?.pages }]]];
  // What the printers say about themselves, read after the list so the list never waits on a printer. Their cells
  // fill in when it comes; printers still answering are read again shortly, for a couple of minutes at most.
  let supplyTimer = 0;
  let supplyTries = 0;
  const supplies = async (ask = false) => {
    clearTimeout(supplyTimer);
    try {
      dev = await get(`/api/devices/printers/supplies${ask ? '?again=true' : ''}`);
    } catch {
      return; // the list stands without them
    }
    merge();
    if (D.at() !== 'printers') return;
    D.draw();
    if (dev.asking && supplyTries++ < 20) supplyTimer = setTimeout(() => supplies(), 5000);
  };
  D.lists.printers = ['Printers', 'printer', 'Every printer on your print servers and computers.'];
  D.more.printers = {
    get table() { return tab === 'servers' ? 'dv-pservers' : 'dv-printers'; },
    get noun() { return tab === 'servers' ? 'print servers' : 'printers'; },
    grid: () => (tab === 'servers' ? serversGrid() : printersGrid()),
    rows: () => (tab === 'servers' ? servers() : shown()),
    count: () => (all ? shown().length : ''), can: () => has('seats.view'),
    load: async () => {
      // The computers' printers, and the servers' when this person may see Active Directory (none when it isn't set up).
      // The servers are asked at most every six hours; Refresh asks them again.
      const ask = again;
      again = false;
      [all, adp] = await Promise.all([get('/api/devices/printers'), has('ad.view') ? get(`/api/ad/printers${ask ? '?again=true' : ''}`).catch(() => null) : null]);
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
      if (!all) return 'Reading the list.';
      if (tab === 'servers') return !adp ? "Active Directory isn't set up, so there's nowhere to look for print servers." : adp.scanning ? 'Still asking the servers.' : 'No server in Active Directory shares a printer.';
      return all.computers.some((c) => c.read) ? 'No printers on the servers or the computers yet.' : `No printers on the servers, and no computer has sent its printers yet. Clients send them from ${NEEDS} on, within half an hour.`;
    },
    fields: () => (tab === 'servers' ? null : PRINTER_FIELDS),
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
    const pcs = p.computers.map((x) => D.rows().find((c) => c.id === x.id)).filter(Boolean);
    const line = (label, value, mono = false) => (value === '' || value == null ? '' : `<li><b>${label}</b><span${mono ? ' class="mono"' : ''}>${esc(value)}</span></li>`);
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('printer')}</span><div class="dw-name"><h2 id="dw-title">${esc(p.name)}</h2><p>${esc([KIND[p.kind] || p.kind, addr(p) || p.server].filter(Boolean).join(' · '))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (can() && p.kind !== 'universal' && p.computers.length ? `<div class="dw-bar"><button type="button" class="dw-qa" data-prl-remove>${svg('trash')}<span>Remove from these computers</span></button></div>` : '')
      + (p.problems?.length ? section(`Problems <small>${p.problems.length}</small>`, `<ul class="pr-probs">${p.problems.map((t) => `<li>${svg('alert')}<span>${esc(t)}</span></li>`).join('')}</ul>`) : '')
      // What the printer itself said over SNMP: its supplies as bars in their own colours, then who it is.
      + (!p.device ? '' : !p.device.answered ? section('From the printer', none(`It didn't answer over SNMP at ${p.ip}. It may be off, on a network this server can't reach, or have SNMP turned off.`))
        : section('From the printer', (p.device.supplies.length ? `<div class="pr-sups">${p.device.supplies.map((s) => `<div class="pr-sup ${ink(s.name)}${s.percent != null && s.percent <= LOW ? ' low' : ''}"><span>${esc(s.name)}</span>`
          + `${s.percent != null ? `<progress max="100" value="${s.percent}"></progress><b>${s.percent}%</b>` : `<i></i><b>${s.some ? 'OK' : ''}</b>`}</div>`).join('')}</div>` : '')
          + `<ul class="does wide">${line('Model', p.device.model)}${line('Serial number', p.device.serial, true)}${line('Pages printed', p.device.pages?.toLocaleString())}${line('Asked', R.when(p.device.at))}</ul>`))
      + (p.listed ? section('On the print server', `<ul class="does wide">${line('Print server', p.server)}${line('Share', p.share, true)}${line('Address', p.ip, true)}${line('Port', p.port, true)}`
        + `${line('Location', p.location || 'Not set')}${line('Comment', p.description)}${line('Status', p.found ? p.status || 'Ready' : '')}${line('Jobs waiting', p.found ? p.jobs ?? '' : '')}`
        + `${line('In Active Directory', p.published ? 'Listed' : 'Not listed')}</ul>`) : '')
      + section(`On <small>${p.computers.length}</small>`, p.computers.length ? `<div class="dw-list">${p.computers.map((x) => `<div class="mrow acc-row" data-prl-pc="${esc(x.id)}"><span class="pic small">${svg('monitor')}</span>`
        + `<div class="two"><b>${esc(x.computer)}</b><small>${esc([x.driver, x.port].filter(Boolean).join(' · '))}</small></div>${x.offline ? chip('Offline', 'off', 'power') : ''}${svg('right', 'acc-go')}</div>`).join('')}</div>` : none('No computer has it yet. Add it from a computer, or tick computers and use Actions.'));
    panel.open('printers', key);
    markRow();
    // Each computer has it under its own name (a connection is \\server\Printer there): one job for each name.
    const named = new Map();
    for (const x of p.computers) {
      const pc = pcs.find((c) => c.id === x.id);
      if (pc) named.set(x.as || p.name, [...(named.get(x.as || p.name) || []), pc]);
    }
    panel.body.querySelector('[data-prl-remove]')?.addEventListener('click', () => sure(`Remove ${p.name} from ${many(pcs.length)}?`, `${names(pcs)}. It's taken off each one for everyone who uses it.`, 'Remove', async () => {
      try {
        for (const [name, on] of named) await runPrinter(on, { action: 'remove', name }, on.length > BRAKE ? on.length : 0);
        toast('Remove sent.', `${p.name} on ${names(pcs)}. It's under Jobs.`);
      } catch (e) {
        oops(e);
      }
    }));
  });
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
      + (s.problem ? section('Couldn\'t be asked', `<ul class="pr-probs"><li>${svg('alert')}<span>${esc(s.problem)}</span></li></ul>`
        + `<p class="muted dw-empty">${esc(R.app())} asks over file and printer sharing (port 445), as this server's own computer account. Asked ${esc(R.when(s.at))}.</p>`) : '')
      + (s.problem ? '' : section(`Printers <small>${its.length}</small>`, its.length ? `<div class="dw-list">${its.map((p) => `<div class="mrow acc-row" data-ps-printer="${esc(`${p.name}\n${p.address || ''}`)}"><span class="pic small">${svg('printer')}</span>`
        + `<div class="two"><b>${esc(p.name)}</b><small>${esc([p.drivers[0], p.ip || p.port].filter(Boolean).join(' · '))}</small></div>${p.status ? chip(p.status, 'off') : ''}${svg('right', 'acc-go')}</div>`).join('')}</div>` : none('It shares none right now.')));
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
  const drawn0 = H.drawn;
  H.drawn = () => {
    drawn0?.();
    const on = D.at() === 'printers';
    $('dv-printers').hidden = !(on && tab === 'printers');
    $('dv-pservers').hidden = !(on && tab === 'servers');
    if (!on) {
      if (panel.is('printers') || panel.is('pservers')) panel.close();
      return;
    }
    // Two lists here: the printers, and the servers that share them (shown once Active Directory could be read).
    $('dv-tabs').innerHTML = `<a href="#seats/printers" data-pr-tab="printers"${tab === 'printers' ? ' aria-current="true"' : ''}>${svg('printer')}Printers <small>${all ? rows.length : ''}</small></a>`
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
    if (panel.is('printers') || panel.is('pservers')) panel.close();
    D.draw();
  });
  // Refresh on this list also asks the servers again (the server allows that once a minute). Heard first, so the read
  // Refresh starts carries it.
  $('dv-refresh').addEventListener('click', () => { if (D.at() === 'printers') again = true; }, true);
})();
