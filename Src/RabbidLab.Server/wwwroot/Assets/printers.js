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
  H.actItems = (item) => (items0?.(item) || '') + (can() ? item('printer', 'printer', 'Add a printer…') : '');
  const act0 = H.act;
  H.act = (act, on) => (act === 'printer' ? (openAdd(on), true) : act0?.(act, on) || false);

  // --- Printers: one list across the computers -----------------------------------------------------------------
  let all = null; // /api/devices/printers
  let adp = null; // /api/ad/printers: the ones print servers published in Active Directory
  let rows = []; // the two together: a printer computers have and AD lists is one row
  const merge = () => {
    rows = (all?.printers || []).filter((p) => p.kind !== 'virtual').map((p) => ({ ...p }));
    for (const a of adp?.printers || []) {
      const short = (a.server || '').split('.')[0].toLowerCase();
      const same = (p) => (p.address && a.share && p.address.toLowerCase() === a.share.toLowerCase()) || p.name.toLowerCase().startsWith(`${a.name} on ${short}`.toLowerCase());
      const match = rows.find(same);
      if (match) Object.assign(match, { server: a.server, location: a.location, share: a.share, published: true });
      else rows.push({ name: a.name, kind: 'share', address: a.share || '', drivers: a.driver ? [a.driver] : [], computers: [], server: a.server, location: a.location, share: a.share, published: true });
    }
  };
  const onText = (n) => (n ? many(n) : 'No computers yet');
  let printersG = null;
  const printersGrid = () => (printersG ||= R.grid({
    el: $('dv-printers'), key: 'printers', sort: { key: 'name', dir: 1 },
    cols: [
      { key: 'name', label: 'Printer', w: 260, fixed: true, get: (p) => p.name, cell: (p) => `<div class="who"><span class="pic">${svg('printer')}</span><b class="ell">${esc(p.name)}</b></div>` },
      { key: 'kind', label: 'Kind', w: 150, on: true, get: (p) => KIND[p.kind] || p.kind },
      { key: 'address', label: 'Address', w: 170, on: true, get: (p) => p.address || '', cell: (p) => `<span class="mono muted ell">${esc(p.address || '')}</span>` },
      { key: 'driver', label: 'Driver', w: 220, on: true, get: (p) => p.drivers.join(', '), cell: (p) => `<span class="ell">${esc(p.drivers.length > 1 ? `${p.drivers.length} drivers` : p.drivers[0] || '')}</span>` },
      { key: 'server', label: 'Print server', w: 150, on: true, get: (p) => p.server || '', cell: (p) => `<span class="ell">${esc(p.server || '')}</span>` },
      { key: 'location', label: 'Location', w: 140, on: true, get: (p) => p.location || '', cell: (p) => `<span class="ell">${esc(p.location || '')}</span>` },
      { key: 'published', label: 'In AD', w: 80, get: (p) => (p.published ? 'Yes' : 'No') },
      { key: 'on', label: 'On', w: 120, on: true, get: (p) => p.computers.length, text: (p) => onText(p.computers.length), cell: (p) => esc(onText(p.computers.length)) },
    ],
    row: (p) => ` data-pr-row="${esc(`${p.name}\n${p.address || ''}`)}"`,
    empty: () => D.more.printers.empty(),
    redraw: () => D.draw(),
  }));
  const shown = () => rows;
  D.lists.printers = ['Printers', 'printer', 'Every printer on the computers, and the ones your print servers publish in Active Directory. Add one from a computer, or tick computers and use Actions.'];
  D.more.printers = {
    table: 'dv-printers', grid: printersGrid, rows: shown, count: () => (all ? shown().length : ''), can: () => has('seats.view'), noun: 'printers',
    load: async () => {
      // The computers' printers, and AD's when this person may see Active Directory (none when it isn't set up).
      [all, adp] = await Promise.all([get('/api/devices/printers'), has('ad.view') ? get('/api/ad/printers').catch(() => null) : null]);
      merge();
    },
    empty: () => (!all ? 'Reading the list.' : all.computers.some((c) => c.read) ? 'No printers on the computers yet.' : `No computer has sent its printers yet. Clients send them from ${NEEDS} on, within half an hour.`),
    fields: () => [['Printer', [{ key: 'name', label: 'Name', type: 'text', get: (p) => p.name }, { key: 'kind', label: 'Kind', type: 'choice', get: (p) => KIND[p.kind] || p.kind },
      { key: 'on', label: 'On (computers)', type: 'number', get: (p) => p.computers.length }, { key: 'server', label: 'Print server', type: 'text', get: (p) => p.server || '' },
      { key: 'location', label: 'Location', type: 'text', get: (p) => p.location || '' }, { key: 'published', label: 'In AD', type: 'choice', get: (p) => (p.published ? 'Yes' : 'No') }]]],
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
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('printer')}</span><div class="dw-name"><h2 id="dw-title">${esc(p.name)}</h2><p>${esc(where(p))}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + (can() && p.kind !== 'universal' && p.computers.length ? `<div class="dw-bar"><button type="button" class="dw-qa" data-prl-remove>${svg('trash')}<span>Remove from these computers</span></button></div>` : '')
      + (p.published ? section('In Active Directory', `<ul class="does"><li><b>Print server</b><span>${esc(p.server || '')}</span></li><li><b>Location</b><span>${esc(p.location || 'Not set')}</span></li>`
        + `<li><b>Share</b><span class="mono">${esc(p.share || p.address || '')}</span></li></ul>`) : '')
      + section(`On <small>${p.computers.length}</small>`, p.computers.length ? `<div class="dw-list">${p.computers.map((x) => `<div class="mrow acc-row" data-prl-pc="${esc(x.id)}"><span class="pic small">${svg('monitor')}</span>`
        + `<div class="two"><b>${esc(x.computer)}</b><small>${esc([x.driver, x.port].filter(Boolean).join(' · '))}</small></div>${x.offline ? chip('Offline', 'off', 'power') : ''}${svg('right', 'acc-go')}</div>`).join('')}</div>` : none('No computer has it yet. Add it from a computer, or tick computers and use Actions.'));
    panel.open('printers', key);
    markRow();
    panel.body.querySelector('[data-prl-remove]')?.addEventListener('click', () => sure(`Remove ${p.name} from ${many(pcs.length)}?`, `${names(pcs)}. It's taken off each one for everyone who uses it.`, 'Remove',
      () => runPrinter(pcs, { action: 'remove', name: p.name }, pcs.length > BRAKE ? pcs.length : 0).then(() => toast('Remove sent.', `${p.name} on ${names(pcs)}. It's under Jobs.`)).catch(oops)));
  });
  panel.el.addEventListener('click', (e) => {
    const b = e.target.closest('[data-prl-pc]');
    if (b && panel.is('printers')) D.open(b.dataset.prlPc, 'printers');
  });
  const drawn0 = H.drawn;
  H.drawn = () => {
    drawn0?.();
    if (D.at() !== 'printers' && panel.is('printers')) panel.close();
    if (D.at() === 'printers') markRow();
  };
})();
