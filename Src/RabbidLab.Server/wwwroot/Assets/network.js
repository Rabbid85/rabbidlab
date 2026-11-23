// Network (NetworkScan.cs): everything that answers on the network ranges. Every address is pinged, as a network scanner
// does, and whatever answers is identified by its ports, names, MAC vendor, SNMP and the TTL of its reply. A small card
// above the list has Scan now and each range as one thin strip of its addresses, coloured by what answered, which the ping
// sweeps along. Under it the list, or Map: every address of every range, numbered (List or Map on the list's bar). A
// device opens the side panel, and what a scan finds comes in while the page is open. Settings › Lab › Network has the
// ranges, the schedule and the SNMP community.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, panel, toast, sure, get, send } = R;

  const KIND = { router: ['Router', 'router'], switch: ['Switch', 'switch'], ap: ['Access point', 'wifi'], server: ['Server', 'server'], pc: ['PC', 'laptop'],
    printer: ['Printer', 'printer'], camera: ['Camera', 'camera'], phone: ['Phone or tablet', 'phone'], nas: ['NAS', 'disk'], iot: ['Smart device', 'chip'], unknown: ['Unknown', 'unknown'] };
  // A type's colour comes from network.css by data-net-kind: the console's CSP refuses style attributes.
  const tone = (k) => ` data-net-kind="${KIND[k] ? k : 'unknown'}"`;
  const PORT = { 21: 'FTP', 22: 'SSH', 23: 'Telnet', 53: 'DNS', 80: 'HTTP', 88: 'Kerberos', 135: 'RPC endpoint mapper', 139: 'NetBIOS session', 389: 'LDAP', 443: 'HTTPS',
    445: 'SMB', 515: 'LPD printing', 554: 'RTSP video', 631: 'IPP printing', 902: 'VMware ESXi', 3389: 'Remote Desktop', 5000: 'Web (Synology DSM and others)',
    5001: 'Web, secure (Synology DSM and others)', 5985: 'WinRM', 8000: 'Web (alternate)', 8080: 'Web (alternate)', 9100: 'Raw printing (JetDirect)', 62078: 'Apple device sync' };
  const WEB = { 80: 'http', 443: 'https', 5000: 'http', 5001: 'https', 8000: 'http', 8080: 'http' };
  const read = (k) => { try { return localStorage.getItem(k); } catch { return null; } };
  const keep = (k, v) => { try { localStorage.setItem(k, v); } catch { /* not kept, then */ } };

  let data = null; // /api/network
  let problem = '';
  let acting = 0;
  let view = read('rabbidlab.network.view') === 'map' ? 'map' : 'list';
  let hl = ''; // a type picked out on the map
  const picked = new Set(); // the ticked devices, by id
  const came = new Map(); // id -> when it came in, for devices found while the page was open

  const may = () => has('network.scan');
  const count = (n, one, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const kindOf = (d) => KIND[d.kind] || KIND.unknown;
  const nameOf = (d) => d.name || d.ip;
  const rangeOf = (d) => data?.ranges.find((r) => r.id === d.range);
  const devOf = (id) => data?.devices.find((d) => d.id === id);
  const ipNum = (ip) => String(ip).split('.').reduce((n, p) => n * 256 + Number(p), 0);
  const ipOf = (n) => [16777216, 65536, 256, 1].map((m) => Math.floor(n / m) % 256).join('.');
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);
  const ago = (iso) => {
    if (!iso) return 'Never';
    const s = Math.round((Date.now() - ms(iso)) / 1000);
    if (s < 60) return 'Just now';
    if (s < 3600) return `${Math.floor(s / 60)} min ago`;
    if (s < 86400) return `${count(Math.floor(s / 3600), 'hour')} ago`;
    if (s < 30 * 86400) return `${count(Math.floor(s / 86400), 'day')} ago`;
    return new Date(iso).toLocaleDateString([], { dateStyle: 'medium' });
  };
  const day = (iso) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : 'Never');
  const pingText = (d) => (!d.online ? 'No reply' : d.ping == null ? 'Blocks ping' : `${d.ping} ms`);
  const ttlWords = (t) => (t > 128 ? 'network gear or a printer' : t > 64 ? 'Windows' : 'Linux, macOS, iOS or Android');
  // Windows, from what Devices knows of it, or its ports and TTL: a computer the client can go on.
  const windows = (d) => /windows/i.test(d.os || '') || (['pc', 'server'].includes(d.kind) && (d.ports.some((p) => [135, 445, 3389].includes(p)) || (d.ttl > 64 && d.ttl <= 128)));
  const noClient = (d) => windows(d) && !d.managed;
  const statusOf = (d) => (d.isNew ? 'New' : d.online ? 'Online' : 'Offline');

  // --- The list ------------------------------------------------------------------------------------------------------
  const mono = (v) => (v ? `<span class="mono muted ell">${esc(v)}</span>` : '<span class="muted">-</span>');
  const pic = (d) => `<span class="pic net-pic"${tone(d.kind)}>${svg(kindOf(d)[1])}</span>`;
  const COLS = [
    { key: 'name', label: 'Name', w: 190, fixed: true, get: (d) => nameOf(d).toLowerCase(), text: nameOf, cell: (d) => `<div class="who">${pic(d)}<b class="ell">${esc(nameOf(d))}</b></div>` },
    { key: 'ip', label: 'IP address', w: 118, on: true, nofilter: true, get: (d) => ipNum(d.ip), text: (d) => d.ip, cell: (d) => mono(d.ip) },
    { key: 'ping', label: 'Ping', w: 86, on: true, nofilter: true, get: (d) => (!d.online ? 99999 : d.ping ?? 99998), text: pingText,
      cell: (d) => (d.online && d.ping != null ? `<span class="mono">${d.ping} ms</span>` : `<span class="muted">${pingText(d)}</span>`) },
    { key: 'range', label: 'Range', w: 110, on: true, filter: true, get: (d) => rangeOf(d)?.name || '' },
    { key: 'kind', label: 'Type', w: 124, on: true, filter: true, get: (d) => kindOf(d)[0] },
    { key: 'vendor', label: 'Vendor', w: 140, on: true, filter: true, get: (d) => d.vendor || '' },
    { key: 'mac', label: 'MAC address', w: 140, on: true, nofilter: true, get: (d) => d.mac || '', cell: (d) => mono(d.mac) },
    { key: 'status', label: 'Status', w: 92, on: true, filter: true, get: (d) => ['New', 'Online', 'Offline'].indexOf(statusOf(d)), text: statusOf,
      cell: (d) => (d.isNew ? '<span class="ubadge net-new">New</span>' : d.online ? '<span class="state">Online</span>' : '<span class="state offline">Offline</span>') },
    { key: 'managed', label: 'Management', w: 124, on: true, filter: true, get: (d) => (d.managed ? 0 : noClient(d) ? 1 : 2), text: (d) => (d.managed ? 'Managed' : 'Unmanaged'),
      cell: (d) => (d.managed ? '<span class="state">Managed</span>' : noClient(d) ? '<span class="state waiting" data-tip="Windows, without the Rabbid Lab client">Unmanaged</span>' : '<span class="muted">Unmanaged</span>') },
    { key: 'first', label: 'First seen', w: 120, on: true, get: (d) => ms(d.first), text: (d) => d.first, cell: (d) => `<span class="muted ell">${esc(ago(d.first))}</span>` },
    { key: 'last', label: 'Last seen', w: 120, on: true, get: (d) => -ms(d.last), text: (d) => d.last, cell: (d) => `<span class="muted ell">${esc(ago(d.last))}</span>` },
    { key: 'os', label: 'Operating system', w: 170, get: (d) => d.os || '' },
    { key: 'ports', label: 'Open ports', w: 170, nofilter: true, get: (d) => d.ports.length, text: (d) => d.ports.join(', '), cell: (d) => (d.ports.length ? `<span class="mono muted ell">${esc(d.ports.join(', '))}</span>` : '<span class="muted">None</span>') },
    { key: 'from', label: 'Name from', w: 100, get: (d) => (d.custom && d.name !== d.reported ? 'Edited' : d.nameFrom || '') },
    { key: 'ttl', label: 'TTL', w: 64, nofilter: true, get: (d) => d.ttl ?? '' },
    { key: 'snmp', label: 'SNMP description', w: 240, nofilter: true, get: (d) => d.snmp || '' },
  ];
  const grid = R.grid({
    el: $('net-table'), store: 'rabbidlab.network', key: 'devices', cols: COLS, sort: { key: 'ip', dir: 1 },
    lead: { w: 18, head: '<label class="tk"><input type="checkbox" data-net-tick-all aria-label="Select all"></label>',
      cell: (d) => `<label class="tk"><input type="checkbox" data-net-tick="${esc(d.id)}" aria-label="Select ${esc(nameOf(d))}"${picked.has(d.id) ? ' checked' : ''}></label>` },
    tail: { w: 30, cell: (d) => `<button type="button" class="at-more" data-net-more="${esc(d.id)}" data-tip="Actions" aria-label="${esc(`Actions for ${nameOf(d)}`)}">${svg('dots')}</button>` },
    row: (d) => ` data-net-row="${esc(d.id)}"`, rowClass: (d) => `${d.online ? '' : 'off'}${picked.has(d.id) ? ' picked' : ''}`,
    empty: () => (problem ? 'Could not read the network list.' : !data?.ranges.some((r) => r.on) ? 'No range is on. Add or turn on a range in Settings, Network.'
      : !data.lastScan && !data.scan ? 'Nothing found yet. Select Scan now.' : data.devices.length ? 'No devices match this filter.' : 'Nothing answered on the ranges.'),
    loading: () => !data && !problem, redraw: () => draw(), picked: () => picked.size,
  });
  // The filter bar (filter.js): a search, a chip for Range, Type, Vendor, Status and Management, and saved filters.
  const bar = R.filter.bar({ el: $('net-fbar'), store: 'rabbidlab.network', key: 'devices', grid: () => grid, noun: 'devices', rows: () => data?.devices || [], redraw: () => draw() });
  const shown = () => (data?.devices || []).filter(bar.test());

  // --- The banner, the bar and the map --------------------------------------------------------------------------------
  R.scatter?.($('net-hero'), 20261123);
  const nums = () => {
    const list = data?.devices || [];
    return { all: list.length, new: list.filter((d) => d.isNew).length, unmanaged: list.filter((d) => !d.managed).length, noclient: list.filter(noClient).length };
  };
  const drawHero = () => {
    const n = nums();
    $('net-state').textContent = !data ? '' : data.scan ? 'Scanning the network.' : n.all ? `${count(n.all, 'device')} discovered.` : 'Nothing discovered yet.';
    for (const [k, id] of [['all', 'net-n-all'], ['new', 'net-n-new'], ['unmanaged', 'net-n-unmanaged'], ['noclient', 'net-n-noclient']]) {
      const el = $(id);
      const now = data ? String(n[k]) : problem ? '—' : null;
      if (now === null) continue;
      const was = el.textContent;
      if (was === now) continue;
      el.textContent = now;
      if (was !== '' && Number(now) > Number(was)) R.bump(el);
    }
    drawRail();
  };
  const drawRail = () => {
    const n = data ? nums() : null;
    $('rail-network').textContent = n?.all ? n.all.toLocaleString() : '';
    $('network-dot').hidden = !n?.new;
  };
  const scanText = () => {
    const sc = data.scan;
    if (sc) {
      const on = data.ranges.filter((r) => sc.ranges.includes(r.id));
      const total = on.reduce((t, r) => t + r.size, 0);
      const done = on.reduce((t, r) => t + (r.done || 0), 0);
      return sc.identifying ? `${stillAsking()} · ${sc.asked} of ${count(sc.found, 'device')} identified`
        : `Scanning: ${done.toLocaleString()} of ${total.toLocaleString()} addresses${sc.found ? ` · ${sc.asked} of ${count(sc.found, 'device')} identified` : ''}`;
    }
    const t = data.scheduleText || '';
    return `${data.lastScan ? `Last scan: ${ago(data.lastScan).replace(/^J/, 'j')}.` : 'Not scanned yet.'} Scans ${t.charAt(0).toLowerCase()}${t.slice(1)}.`;
  };
  const drawBar = () => {
    const b = $('net-scan');
    const going = !!data?.scan;
    b.disabled = !data || going || acting > 0;
    b.classList.toggle('turning', going);
    b.querySelector('span').textContent = going ? 'Scanning' : 'Scan now';
    b.dataset.tip = !data?.ranges.some((r) => r.on) ? 'Add or turn on a range in Settings, Network.' : '';
    $('net-when').textContent = data ? scanText() : '';
    document.querySelectorAll('[data-net-view]').forEach((x) => x.setAttribute('aria-pressed', String(x.dataset.netView === view)));
  };

  // The strips: each range that's on as one thin strip of its addresses in order, a tick each, coloured by the type of
  // what answered there, dark where nothing did. A range bigger than 1,022 addresses shares each tick between a few. Point
  // at a tick for what it is, click it to open it; ↻ at the row's end scans that range alone. A filter on the list dims
  // what it leaves out.
  const MOST = 1022;
  const spanOf = (r) => {
    const [ip, b] = r.cidr.split('/');
    const size = 2 ** (32 - Number(b));
    const n = Math.max(1, size - 2);
    return { first: Math.floor(ipNum(ip) / size) * size + 1, n, per: Math.ceil(n / MOST) };
  };
  const tick = (s, i, byIp, inList) => {
    const a = s.first + i * s.per;
    const b = Math.min(s.first + s.n - 1, a + s.per - 1);
    let d = null;
    for (let x = a; x <= b && !d; x += 1) d = byIp.get(x) || null;
    if (!d) return `<i class="net-u free" data-n="${a}" data-tip="${esc(`${a === b ? ipOf(a) : `${ipOf(a)} to .${b % 256}`} · No reply`)}"></i>`;
    const cls = `${d.online ? '' : ' off'}${panel.shows('network', d.id) ? ' on' : ''}${inList.has(d.id) ? '' : ' dim'}`;
    return `<button type="button" class="net-u${cls}" data-n="${a}" data-net-cell="${esc(d.id)}"${tone(d.kind)}`
      + ` data-tip="${esc(`${d.ip} · ${nameOf(d)} · ${kindOf(d)[0]} · ${pingText(d)}`)}" aria-label="${esc(`${d.ip}, ${nameOf(d)}`)}"></button>`;
  };
  let stripsDrawn = '';
  let keyDrawn = '';
  const drawStrips = (rows) => {
    const box = $('net-strips');
    if (!data) {
      if (!problem && !stripsDrawn) R.skeleton(box, 'rows', 3);
      return;
    }
    const kinds = Object.keys(KIND).filter((k) => data.devices.some((d) => d.kind === k));
    const key = kinds.map((k) => `<span${tone(k)}><i></i>${esc(KIND[k][0])}</span>`).join('');
    if (key !== keyDrawn) $('net-key').innerHTML = keyDrawn = key;
    const inList = new Set((bar.any() ? rows : data.devices).map((d) => d.id));
    const byIp = new Map(data.devices.map((d) => [ipNum(d.ip), d]));
    const ranges = data.ranges.filter((r) => r.on);
    const html = !ranges.length ? '<p class="net-none">No range is on. Add or turn on a range in Settings, Network.</p>' : ranges.map((r) => {
      const s = spanOf(r);
      const on = data.devices.filter((d) => d.range === r.id);
      const ticks = Math.ceil(s.n / s.per);
      return `<div class="net-srow" data-net-range="${esc(r.id)}"><div class="net-slabel"><b>${esc(r.name)}</b><span class="mono">${esc(r.cidr)}</span></div>`
        + `<div class="net-strip${ticks > 254 ? ' fine' : ''}" data-ticks="${ticks}">${Array.from({ length: ticks }, (_, i) => tick(s, i, byIp, inList)).join('')}<i class="net-head" hidden></i></div>`
        + `<div class="net-sside"><small>${on.filter((d) => d.online).length.toLocaleString()} answering · ${Math.max(0, r.size - on.length).toLocaleString()} free</small><span class="net-sprog"></span></div>`
        + (may() ? `<button type="button" class="gcols" data-net-scanone="${esc(r.id)}" data-tip="Scan ${esc(r.name)}" aria-label="Scan ${esc(r.name)}">${svg('refresh')}</button>` : '<span></span>')
        + '</div>';
    }).join('');
    // Drawn again only when something on it changed, so the sweep and a new device's glow run on.
    if (html !== stripsDrawn) {
      R.skeleton(box, '');
      box.innerHTML = stripsDrawn = html;
      box.querySelectorAll('[data-ticks]').forEach((el) => el.style.setProperty('--n', el.dataset.ticks));
      box.querySelectorAll('[data-net-cell]').forEach((c) => {
        const t = came.get(c.dataset.netCell);
        if (t && Date.now() - t < 1900) { c.style.setProperty('--arrive-at', `${t - Date.now()}ms`); c.classList.add('arrive'); }
      });
    }
    sweep(ranges);
  };
  // How a scan shows on the strips and the map, calmly. While a range is pinged, what the ping hasn't reached yet is
  // dimmed, and on a strip a line glides to where it has got to. While devices are identified, those not done yet stay
  // dimmed, the ones being asked just now (scan.busy) breathe slowly, out of step, and each brightens once as it's done.
  const busyNow = () => new Set((data?.scan?.busy || []).map(ipNum));
  const waitingStrips = new Set();
  const waitingMap = new Set();
  // was: the devices still waiting at the last look, so one done since brightens even when its tick was drawn again.
  const stage = (sec, r, s, per, busy, was) => {
    const sc = data.scan;
    const mine = !!sc && sc.ranges.includes(r.id);
    const pinging = mine && !sc.identifying;
    const naming = mine && sc.identifying;
    const done = Math.min(r.done || 0, s.n);
    const since = sc ? ms(sc.started) : 0;
    sec.querySelectorAll('[data-n]').forEach((c, i) => {
      const a = Number(c.dataset.n);
      const ahead = pinging && a - s.first >= Math.ceil(done / per) * per;
      c.classList.toggle('ahead', ahead);
      // Each device is identified as soon as the ping finds it, so behind the line it waits only until it's asked.
      const d = c.dataset.netCell ? devOf(c.dataset.netCell) : null;
      const waits = mine && !ahead && !!d && d.online && ms(d.last) < since;
      if (d && !waits && was.has(d.id)) {
        c.classList.remove('settle');
        void c.offsetWidth;
        c.classList.add('settle');
      }
      if (d) { if (waits) was.add(d.id); else was.delete(d.id); }
      c.classList.toggle('waiting', waits);
      // Being asked just now: a device, or in the last few (found only in the ARP table) an address with none yet. An
      // address being pinged doesn't count, or a hundred would breathe at once.
      let asked = false;
      for (let x = a; x < a + per && (d || naming) && !ahead && !asked; x += 1) asked = busy.has(x);
      c.classList.toggle('asking', asked);
      if (asked) c.style.setProperty('--d', `${-((i * 0.37) % 2.4).toFixed(2)}s`);
    });
    const head = sec.querySelector('.net-head');
    if (head) {
      head.hidden = !pinging || !done || done >= s.n; // only on the range being pinged now
      head.style.setProperty('--p', `${((done / s.n) * 100).toFixed(2)}%`);
    }
  };
  // Once the ping is done, what's still being asked, by name: one or two of them, or how many (s: a range, or all).
  const stillAsking = (s = null) => {
    const ips = (data.scan?.busy || []).filter((ip) => !s || (ipNum(ip) >= s.first && ipNum(ip) < s.first + s.n));
    const named = ips.map((ip) => { const d = data.devices.find((x) => x.ip === ip); return d?.name ? `${d.name} (${ip})` : ip; });
    if (!named.length) return s ? '' : 'Finishing up'; // a range with nothing left says nothing
    return named.length <= 2 ? `Identifying ${named.join(' and ')}` : `Identifying ${named.length} devices`;
  };
  // Where a range has got to: the last address pinged, or what's still being identified.
  const progress = (r, s) => {
    const sc = data.scan;
    if (!sc || !sc.ranges.includes(r.id)) return '';
    const done = Math.min(r.done || 0, s.n);
    return sc.identifying ? stillAsking(s) : !done ? 'Queued' : done >= s.n ? 'Pinged' : ipOf(s.first + done - 1);
  };
  const sweep = (ranges) => {
    const busy = busyNow();
    for (const r of ranges) {
      const row = $('net-strips').querySelector(`[data-net-range="${CSS.escape(r.id)}"]`);
      if (!row) continue;
      const s = spanOf(r);
      row.querySelector('.net-sprog').textContent = progress(r, s);
      stage(row, r, s, s.per, busy, waitingStrips);
    }
    document.querySelectorAll('[data-net-scanone]').forEach((b) => { b.disabled = !!data.scan || acting > 0; });
  };

  // Map, in the list's place: every address of every range that's on, numbered by its last part. A device is filled in
  // its type's colour, no reply is an outline, New has a dot; a free address is only its faint number. A range bigger
  // than a /24 shows the /24 blocks where something answered. The key, with how many of each, picks out a type.
  const blocksOf = (r) => {
    const s = spanOf(r);
    const last = s.first + s.n - 1;
    const out = [];
    for (let b = Math.floor(s.first / 256) * 256; b <= last; b += 256) out.push([Math.max(s.first, b), Math.min(last, b + 255)]);
    return out;
  };
  const mapKey = (rows) => {
    const kinds = Object.keys(KIND).filter((k) => rows.some((d) => d.kind === k));
    return `<div class="net-mkey">${kinds.map((k) => `<button type="button" class="net-k${hl === k ? ' on' : ''}${hl && hl !== k ? ' dim' : ''}" data-net-hl="${k}"${tone(k)} aria-pressed="${hl === k}">`
      + `<i></i>${esc(KIND[k][0])}<small>${rows.filter((d) => d.kind === k).length}</small></button>`).join('')}`
      + '<span class="grow"></span><span class="net-k2"><i class="off"></i>No reply</span><span class="net-k2"><i class="new"></i>New</span></div>';
  };
  const cell = (n, d, inList) => {
    const o = n % 256;
    if (!d) return `<i class="net-n free" data-n="${n}" data-tip="${esc(`${ipOf(n)} · No reply`)}">${o}</i>`;
    const cls = `${d.online ? '' : ' off'}${d.isNew ? ' new' : ''}${panel.shows('network', d.id) ? ' on' : ''}${!inList || (hl && hl !== d.kind) ? ' dim' : ''}`;
    return `<button type="button" class="net-n${cls}" data-n="${n}" data-net-cell="${esc(d.id)}"${tone(d.kind)}`
      + ` data-tip="${esc(`${d.ip} · ${nameOf(d)} · ${kindOf(d)[0]} · ${pingText(d)}`)}" aria-label="${esc(`${d.ip}, ${nameOf(d)}`)}">${o}</button>`;
  };
  let mapDrawn = '';
  const drawMap = (rows) => {
    const box = $('net-maps');
    if (!data) return;
    const inList = new Set(rows.map((d) => d.id));
    const byIp = new Map(data.devices.map((d) => [ipNum(d.ip), d]));
    const ranges = data.ranges.filter((r) => r.on);
    const html = !ranges.length ? '<p class="net-none">No range is on. Add or turn on a range in Settings, Network.</p>'
      : mapKey(rows) + ranges.map((r) => {
        const blocks = blocksOf(r);
        const show = blocks.length === 1 ? blocks : blocks.filter(([a, b]) => [...byIp.keys()].some((n) => n >= a && n <= b));
        const on = data.devices.filter((d) => d.range === r.id);
        return `<div class="net-range" data-net-range="${esc(r.id)}"><div class="net-rhead"><b>${esc(r.name)}</b><span class="mono">${esc(r.cidr)}</span>`
          + `<small>${on.filter((d) => d.online).length.toLocaleString()} answering · ${Math.max(0, r.size - on.length).toLocaleString()} free</small>`
          + `${blocks.length > show.length ? `<small>${count(blocks.length - show.length, 'block')} with nothing that answers not shown</small>` : ''}<span class="net-rprog"></span></div>`
          + show.map(([a, b]) => `${blocks.length > 1 ? `<p class="net-block mono">${esc(ipOf(a).replace(/\.\d+$/, '.x'))}</p>` : ''}`
            + `<div class="net-grid">${Array.from({ length: b - a + 1 }, (_, i) => cell(a + i, byIp.get(a + i), inList.has(byIp.get(a + i)?.id))).join('')}</div>`).join('')
          + '</div>';
      }).join('');
    // Drawn again only when something on it changed, so the sweep and a new device's glow run on.
    if (html !== mapDrawn) {
      box.innerHTML = mapDrawn = html;
      box.querySelectorAll('[data-net-cell]').forEach((c) => {
        const t = came.get(c.dataset.netCell);
        if (t && Date.now() - t < 1900) { c.style.setProperty('--arrive-at', `${t - Date.now()}ms`); c.classList.add('arrive'); }
      });
    }
    const busy = busyNow();
    for (const r of ranges) {
      const sec = box.querySelector(`[data-net-range="${CSS.escape(r.id)}"]`);
      if (!sec) continue;
      const s = spanOf(r);
      sec.querySelector('.net-rprog').textContent = progress(r, s);
      stage(sec, r, s, 1, busy, waitingMap);
    }
  };

  // --- Drawing the page ----------------------------------------------------------------------------------------------
  const done = new WeakSet(); // rows already brought in
  const draw = () => {
    if (R.where()[0] !== 'network') return;
    drawHero();
    bar.draw();
    grid.tools($('net-tools'), $('net-rows'));
    drawBar();
    $('net-card').classList.toggle('map', view === 'map');
    $('net-table').hidden = view === 'map';
    $('net-maps').hidden = view !== 'map';
    $('net-problem').hidden = !problem;
    $('net-problem').textContent = problem;
    const rows = shown();
    [...picked].forEach((id) => { if (!devOf(id)) picked.delete(id); });
    if (data || problem) grid.draw(rows);
    else R.skeleton($('net-table'), 'rows', 6);
    drawStrips(rows);
    if (view === 'map') drawMap(rows);
    // Devices found while the page is open come in as rows do everywhere (R.arrive).
    came.forEach((t, id) => {
      if (Date.now() - t > 2800) { came.delete(id); return; }
      const row = $('net-table').querySelector(`[data-net-row="${CSS.escape(id)}"]`);
      if (row && !done.has(row)) { done.add(row); R.arrive(row, t); }
    });
    drawTicks();
    mark();
  };
  const mark = () => {
    document.querySelectorAll('#net-table [data-net-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('network', el.dataset.netRow)));
    document.querySelectorAll('#net-strips [data-net-cell], #net-maps [data-net-cell]').forEach((el) => el.classList.toggle('on', panel.shows('network', el.dataset.netCell)));
  };
  const drawTicks = () => {
    const boxes = [...$('net-table').querySelectorAll('[data-net-tick]')];
    boxes.forEach((b) => { b.checked = picked.has(b.dataset.netTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const all = $('net-table').querySelector('[data-net-tick-all]');
    if (all) {
      const on = boxes.filter((b) => b.checked).length;
      all.checked = !!boxes.length && on === boxes.length;
      all.indeterminate = on > 0 && on < boxes.length;
    }
    const n = picked.size;
    $('net-act').disabled = !n;
    $('net-act').dataset.tip = n ? '' : 'Select devices first';
    $('net-act').querySelector('.cnt').hidden = !n;
    $('net-act').querySelector('.cnt').textContent = n;
    grid.recount();
  };

  // --- Reading -------------------------------------------------------------------------------------------------------
  // What a new answer brings: devices that weren't there before come in (and say so, unless it's the first scan), and a
  // scan that has just finished says how it went.
  let wasScan = null;
  const took = (next) => {
    const before = data ? new Set(data.devices.map((d) => d.id)) : null;
    const scanBefore = data?.scan || null;
    data = next;
    problem = '';
    if (before) {
      const fresh = data.devices.filter((d) => !before.has(d.id));
      fresh.forEach((d) => came.set(d.id, Date.now()));
      const told = fresh.filter((d) => d.isNew);
      if (told.length === 1) toast('New device on the network:', `${nameOf(told[0])} (${told[0].ip}).`, { stay: true, action: { label: 'Open', run: () => openDevice(devOf(told[0].id)) } });
      else if (told.length > 1) toast(`${told.length} new devices on the network.`, told.slice(0, 4).map((d) => `${nameOf(d)} (${d.ip})`).join(', ') + (told.length > 4 ? ', and more.' : '.'));
    }
    if ((scanBefore || wasScan) && !data.scan) finished(scanBefore || wasScan);
    wasScan = data.scan;
  };
  const finished = (sc) => {
    const since = ms(sc.started);
    const on = data.devices.filter((d) => d.online && sc.ranges.includes(d.range)).length;
    const fresh = data.devices.filter((d) => d.isNew && ms(d.first) >= since).length;
    toast('Scan complete.', `${count(on, 'device')} answered. ${fresh ? `${count(fresh, 'new device')} found.` : 'No new devices found.'}`);
  };
  const load = async () => {
    try {
      took(await get('/api/network'));
    } catch (e) {
      if (!data) problem = e.message;
    }
    draw();
    drawSettings();
    if (data?.scan) soon();
    if (wanted && devOf(wanted)) openDevice(devOf(wanted));
    wanted = '';
  };
  // Another page asks for a device (Ctrl+K search): this page, with it open.
  let wanted = '';
  R.openNetwork = (id) => {
    if (R.where()[0] === 'network' && devOf(id)) openDevice(devOf(id));
    else { wanted = id; location.hash = '#network'; }
  };
  R.networkNow = () => data;
  // Every second while a scan runs, so the sweep and each device show as they happen; every 15 seconds otherwise.
  // soon(): a scan has just started (here or found running), so the next look is in a second, not at the slow pace.
  const soon = () => setTimeout(() => live.now(), 900);
  const live = R.live({
    page: () => R.where()[0] === 'network' && has('network.view'),
    hold: () => acting > 0,
    every: () => (data?.scan ? 1000 : 15000),
    read: () => get('/api/network'),
    now: () => data,
    draw: (got) => {
      took(got);
      draw();
      if (panel.is('network')) { const d = devOf(panel.key); if (d) openDevice(d, true); else panel.close(); }
    },
    areas: () => [$('net-table'), $('net-strips'), $('net-maps')],
  });
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'network' && has('network.view')) {
      draw();
      load();
    } else if (e.detail[0] === 'settings' && e.detail[1] === 'network' && has('network.settings')) loadSettings();
  });
  // The rail's count and dot, as soon as the console is up, for whoever can see Network.
  document.addEventListener('rabbidlab:me', () => { if (has('network.view') && R.where()[0] !== 'network') get('/api/network').then((v) => { if (!data) { data = v; wasScan = v.scan; drawRail(); } }).catch(() => {}); });

  // --- Doing things --------------------------------------------------------------------------------------------------
  const oops = (e) => { if (!e.cancelled) toast('The operation failed.', e.message, { tone: 'warn' }); };
  const act = async (work) => {
    acting += 1;
    drawBar();
    try {
      took(await work());
      return true;
    } catch (e) {
      oops(e);
      return false;
    } finally {
      acting -= 1;
      draw();
      if (panel.is('network')) { const d = devOf(panel.key); if (d) openDevice(d, true); else panel.close(); }
    }
  };
  // Every range that's on, or one range (↻ on its strip).
  const scan = async (range = '') => {
    const on = (data?.ranges || []).filter((r) => r.on && (!range || r.id === range));
    if (await act(() => send('/api/network/scan', range ? { range } : {}))) {
      wasScan = data.scan;
      soon();
      toast('Scan started.', `${count(on.reduce((t, r) => t + r.size, 0), 'address', 'addresses')} in ${on.map((r) => r.name).join(', ')}.`);
    }
  };
  const again = async (list) => {
    const one = list.length === 1;
    toast(one ? `Scanning ${list[0].ip} again.` : `Scanning ${count(list.length, 'device')} again.`, 'Ping, open ports, names and SNMP.', { stay: true, tone: 'work' });
    if (!await act(() => send('/api/network/rescan', { ids: list.map((d) => d.id) }))) return;
    const now = list.map((d) => devOf(d.id)).filter(Boolean);
    const up = now.filter((d) => d.online).length;
    if (one && now[0]) toast(now[0].online ? 'Device online.' : 'Device offline.', now[0].online ? `${nameOf(now[0])} (${now[0].ip}). ${count(now[0].ports.length, 'open port')}.` : `${nameOf(now[0])} (${now[0].ip}) did not answer.`, { tone: now[0].online ? '' : 'warn' });
    else toast('Scan complete.', `${up} online, ${now.length - up} offline.`);
  };
  const known = async (list) => {
    const fresh = list.filter((d) => d.isNew);
    if (!fresh.length) return;
    if (await act(() => send('/api/network/known', { ids: fresh.map((d) => d.id) }))) toast(fresh.length === 1 ? 'Marked as known.' : `${fresh.length} devices marked as known.`, fresh.length === 1 ? `${nameOf(fresh[0])} (${fresh[0].ip}).` : '');
  };
  const forget = (list) => {
    if (!list.length) return;
    const one = list.length === 1;
    sure(one ? `Remove ${nameOf(list[0])}?` : `Remove ${list.length} devices?`, `${one ? 'It is' : 'They are'} removed from Network. Nothing on the ${one ? 'device' : 'devices'} changes. ${one ? 'It comes' : 'They come'} back as new if the next scan finds ${one ? 'it' : 'them'}.`, 'Remove', async () => {
      if (await act(() => send('/api/network/forget', { ids: list.map((d) => d.id) }))) {
        list.forEach((d) => picked.delete(d.id));
        if (panel.is('network') && list.some((d) => d.id === panel.key)) panel.close();
        drawTicks();
        toast(one ? 'Device removed.' : `${list.length} devices removed.`, one ? `${nameOf(list[0])} (${list[0].ip}).` : '');
      }
    });
  };
  // Open in Devices: once Devices has read its computers, the computer's own panel.
  const openPc = (id) => {
    location.hash = '#seats';
    const started = Date.now();
    const tryOpen = () => {
      if (R.devices.computers().some((c) => c.id === id)) R.devices.open(id);
      else if (Date.now() - started < 8000) setTimeout(tryOpen, 200);
    };
    setTimeout(tryOpen, 50);
  };

  // --- A device in the side panel ------------------------------------------------------------------------------------
  const section = (title, body) => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3></div>${body}</section>`;
  const qa = (attr, icon, label) => `<button type="button" class="dw-qa" ${attr}>${svg(icon)}<span>${esc(label)}</span></button>`;
  const facts = (rows) => `<ul class="does">${rows.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${v}</span></li>`).join('')}</ul>`;
  const strip = (pairs) => `<div class="dw-strip">${pairs.map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const openDevice = (d, again2 = false) => {
    if (!d) return;
    const k = kindOf(d);
    const r = rangeOf(d);
    const managed = d.managed ? '<span class="state">Managed</span>' : noClient(d) ? '<span class="state waiting">Unmanaged</span>' : 'Unmanaged';
    const web = d.online ? d.ports.filter((p) => WEB[p]) : [];
    const ports = d.ports.length ? `<div class="dw-list">${d.ports.map((p) => `<div class="mrow net-prow"><span class="mono net-pnum">${p}</span><span class="net-pname">${esc(PORT[p] || 'Unknown service')}</span>`
      + `${WEB[p] && d.online ? `<a class="linkish" href="${WEB[p]}://${esc(d.ip)}${[80, 443].includes(p) ? '' : `:${p}`}" target="_blank" rel="noopener noreferrer">Open</a>` : ''}</div>`).join('')}</div>`
      : `<p class="muted dw-empty">${d.online ? 'None of the common ports checked are open.' : 'Not checked: the device did not answer the last scan.'}</p>`;
    const why = [d.why, d.custom && d.kind !== d.detected ? `The scan detected ${(KIND[d.detected] || KIND.unknown)[0].toLowerCase()}; the type was changed with Edit.` : ''].filter(Boolean).join(' ');
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic net-pic"${tone(d.kind)}>${svg(k[1])}</span><div class="dw-name"><h2 id="dw-title">${esc(nameOf(d))}</h2>`
      + `<p>${esc([k[0], d.vendor].filter(Boolean).join(' · '))}</p></div><button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-bar">${R.editButton('data-net-edit', may() ? '' : 'Your role does not include changing Network.')}`
      + (d.client && has('seats.view') ? qa('data-net-pc', 'monitor', 'Open in Devices') : '')
      + (noClient(d) && has('seats.add') ? qa('data-net-client', 'download', 'Install the client') : '')
      + (web.length ? qa(`data-net-web="${web[0]}"`, 'browser', 'Open web page') : '')
      + (d.isNew && may() ? qa('data-net-known', 'check', 'Mark as known') : '')
      + (may() ? qa('data-net-again', 'refresh', 'Scan again') : '')
      + (may() ? qa('data-net-forget', 'trash', 'Remove from list') : '') + '</div>'
      + strip([['IP address', `<span class="mono">${esc(d.ip)}</span>`], ['Type', esc(k[0])], ['Ping', esc(pingText(d))],
        ['Status', d.isNew ? '<span class="ubadge net-new">New</span>' : esc(statusOf(d))], ['Management', managed], ['Last seen', esc(ago(d.last))]])
      + (noClient(d) ? section('Management', `<p class="dw-how">${esc(d.os || 'A Windows computer')}, without the <span data-app>Rabbid Lab</span> client. Install the client to see it under Devices with its hardware, software and updates.</p>`) : '')
      + section('Details', facts([['Name', esc(nameOf(d))], d.custom && d.reported && d.reported !== d.name ? ['Reported name', esc(d.reported)] : null, ['Name from', esc(d.custom && d.reported !== d.name ? 'Edit' : d.nameFrom || 'No name found')],
        ['IP address', `<span class="mono">${esc(d.ip)}</span>`], ['MAC address', d.mac ? `<span class="mono">${esc(d.mac)}</span>` : 'Not found'], ['Vendor', esc(d.vendor || 'Unknown')], ['Type', esc(k[0])],
        ['Ping', esc(pingText(d))], d.ttl ? ['TTL', `${d.ttl} <span class="muted">(${esc(ttlWords(d.ttl))})</span>`] : null, d.os ? ['Operating system', esc(d.os)] : null,
        d.snmp ? ['SNMP', esc(d.snmp)] : null, ['Range', r ? `${esc(r.name)} <span class="muted mono">${esc(r.cidr)}</span>` : 'Removed'], ['First seen', esc(day(d.first))], ['Last seen', esc(day(d.last))]])
        + `<p class="dw-how">${esc(why)} The vendor comes from the first half of the MAC address.</p>`)
      + section(`Open ports <small>${d.ports.length}</small>`, ports);
    if (!again2) panel.open('network', d.id);
    mark();
  };
  const rowClick = (id) => {
    const d = devOf(id);
    if (!d) return;
    if (panel.shows('network', id)) { panel.close(); mark(); } else openDevice(d);
  };
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('network')) return;
    const d = devOf(panel.key);
    const on = (attr) => e.target.closest(`[${attr}]`);
    if (!d) return;
    if (on('data-net-edit') && may()) openEdit(d);
    else if (on('data-net-known')) known([d]);
    else if (on('data-net-again')) again([d]);
    else if (on('data-net-forget')) forget([d]);
    else if (on('data-net-pc')) openPc(d.client);
    else if (on('data-net-client')) location.hash = '#seats/deploy';
    else if (on('data-net-web')) { const p = Number(on('data-net-web').dataset.netWeb); window.open(`${WEB[p]}://${d.ip}${[80, 443].includes(p) ? '' : `:${p}`}`, '_blank', 'noopener'); }
  });
  panel.el.addEventListener('close', () => mark());

  // --- Edit: a name and type of our own ------------------------------------------------------------------------------
  let editing = null;
  const openEdit = (d) => {
    editing = d.id;
    $('net-edit-title').textContent = `Edit ${nameOf(d)}`;
    $('net-edit-name').value = d.name || '';
    $('net-edit-name').placeholder = d.reported || d.ip;
    $('net-edit-kind').innerHTML = Object.entries(KIND).map(([key, [label]]) => `<option value="${key}"${key === d.kind ? ' selected' : ''}>${esc(label)}${key === d.detected ? ' (detected)' : ''}</option>`).join('');
    R.say('net-edit-problem', '');
    $('net-edit').showModal();
    $('net-edit-name').select();
  };
  $('net-edit-cancel').addEventListener('click', () => $('net-edit').close());
  $('net-edit-save').addEventListener('click', async () => {
    const d = devOf(editing);
    if (!d) { $('net-edit').close(); return; }
    $('net-edit-save').disabled = true;
    try {
      took(await send(`/api/network/devices/${encodeURIComponent(d.id)}`, { name: $('net-edit-name').value.trim(), kind: $('net-edit-kind').value }));
      $('net-edit').close();
      const now = devOf(d.id);
      toast('Device saved.', now ? `${nameOf(now)}: ${kindOf(now)[0]}.` : '');
      draw();
      if (now && panel.shows('network', now.id)) openDevice(now, true);
    } catch (e) {
      if (!e.cancelled) R.say('net-edit-problem', e.message);
    }
    $('net-edit-save').disabled = false;
  });
  $('net-edit-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('net-edit-save').click(); });

  // --- The list's clicks: ticks, rows, ⋯, Actions, the map, List or Map ----------------------------------------------
  let lastTick = null;
  $('net-table').addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return; // the label's own click comes through the box
      const boxes = [...$('net-table').querySelectorAll('[data-net-tick]')];
      const set = (b) => (box.checked ? picked.add(b.dataset.netTick) : picked.delete(b.dataset.netTick));
      if (box.dataset.netTickAll !== undefined) boxes.forEach(set);
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
    const m = e.target.closest('[data-net-more]');
    if (m) { rowMenu(m, devOf(m.dataset.netMore)); return; }
    if (e.target.closest('input, button, a')) return;
    const row = e.target.closest('[data-net-row]');
    if (row) rowClick(row.dataset.netRow);
  });
  const rowMenu = (button, d) => {
    if (!d) return;
    R.actions.open(button, {
      items: [{ act: 'open', label: 'Open' }, ...(may() ? [{ act: 'edit', label: 'Edit…' }, { act: 'known', label: 'Mark as known', disabled: !d.isNew }, { act: 'again', label: 'Scan again' }, '-',
        { act: 'forget', label: 'Remove from list…', danger: true }] : [])],
      pick: (a) => (a === 'open' ? openDevice(d) : a === 'edit' ? openEdit(d) : a === 'known' ? known([d]) : a === 'again' ? again([d]) : forget([d])),
    });
  };
  const pickedRows = () => (data?.devices || []).filter((d) => picked.has(d.id));
  $('net-act').addEventListener('click', () => {
    const rows = pickedRows();
    const fresh = rows.filter((d) => d.isNew).length;
    R.actions.open($('net-act'), {
      items: [...(may() ? [{ act: 'known', label: 'Mark as known', hint: fresh ? String(fresh) : '', disabled: !fresh }, { act: 'again', label: 'Scan again', hint: String(rows.length) }] : []),
        { act: 'export', label: 'Export selected' }, { act: 'clear', label: 'Clear selection' },
        ...(may() ? ['-', { act: 'forget', label: 'Remove from list…', hint: String(rows.length), danger: true }] : [])],
      pick: (a) => {
        const now = pickedRows();
        if (a === 'known') known(now);
        else if (a === 'again') again(now);
        else if (a === 'export') grid.csv(grid.sorted(now), 'network-selected');
        else if (a === 'clear') { picked.clear(); drawTicks(); }
        else if (a === 'forget') forget(now);
      },
    });
  });
  $('net-scan').addEventListener('click', () => { if (!$('net-scan').disabled) scan(); });
  $('net-strips').addEventListener('click', (e) => {
    const one = e.target.closest('[data-net-scanone]');
    if (one) { if (!one.disabled) scan(one.dataset.netScanone); return; }
    const c = e.target.closest('[data-net-cell]');
    if (c) rowClick(c.dataset.netCell);
  });
  $('net-card').addEventListener('click', (e) => {
    const v = e.target.closest('[data-net-view]');
    if (!v || v.dataset.netView === view) return;
    view = v.dataset.netView;
    keep('rabbidlab.network.view', view);
    mapDrawn = '';
    draw();
  });
  $('net-maps').addEventListener('click', (e) => {
    const k = e.target.closest('[data-net-hl]');
    if (k) {
      hl = hl === k.dataset.netHl ? '' : k.dataset.netHl;
      draw();
      return;
    }
    const c = e.target.closest('[data-net-cell]');
    if (c) rowClick(c.dataset.netCell);
  });

  // --- Settings › Lab › Network ---------------------------------------------------------------------------------------
  const FROM = { local: 'This server\'s network', added: 'Added' };
  const drawSettings = () => {
    if (R.where()[0] !== 'settings' || R.where()[1] !== 'network' || !data) return;
    R.skeleton($('ns-ranges'), '');
    $('ns-ranges').innerHTML = data.ranges.length ? data.ranges.map((r) => `<div class="ds-row" data-ns-range="${esc(r.id)}"><div class="ds-l"><b class="mono">${esc(r.cidr)}</b>`
      + `<span>${esc(`${r.size.toLocaleString()} addresses · ${FROM[r.from] || 'Added'}`)}</span></div><div class="ds-r">`
      + `<input class="ds-input ns-name" data-ns-name value="${esc(r.name)}" maxlength="40" autocomplete="off" spellcheck="false" aria-label="Name for ${esc(r.cidr)}">`
      + `<span class="sw-label">${r.on ? 'On' : 'Off'}</span><input type="checkbox" class="switch" data-ns-on role="switch" aria-label="Scan ${esc(r.cidr)}"${r.on ? ' checked' : ''}>`
      + `<button type="button" class="gcols" data-ns-del data-tip="Remove range" aria-label="Remove ${esc(r.cidr)}">${svg('trash')}</button></div></div>`).join('')
      : '<p class="muted ns-none">No ranges. Add the networks to scan below.</p>';
    $('ns-schedule').innerHTML = Object.entries(SCHEDULES).map(([k, label]) => `<option value="${k}"${k === data.schedule ? ' selected' : ''}>${esc(label)}</option>`).join('');
    $('ns-next').textContent = `How often every range that is on is scanned without anyone selecting Scan now.${data.next ? ` Next scan: ${day(data.next)}.` : ''}`;
    if (document.activeElement !== $('ns-community')) $('ns-community').value = data.community || 'public';
  };
  const SCHEDULES = { '10m': 'Every 10 minutes', '30m': 'Every 30 minutes', '1h': 'Every hour', '2h': 'Every 2 hours', '6h': 'Every 6 hours', day: 'Every day at 6:00 AM', off: 'Only when someone selects Scan now' };
  const loadSettings = async () => {
    if (!data) R.skeleton($('ns-ranges'), 'rows', 2);
    try {
      took(await get('/api/network'));
      R.say('ns-problem', '');
    } catch (e) {
      R.say('ns-problem', e.message);
    }
    drawSettings();
  };
  // Every change saves at once: the ranges as they stand, the schedule and the community.
  const save = async (change, said) => {
    const form = { ranges: data.ranges.map((r) => ({ id: r.id, name: r.name, cidr: r.cidr, on: r.on })), schedule: data.schedule, community: data.community, ...change(data) };
    try {
      took(await send('/api/network/settings', form));
      R.say('ns-problem', '');
      if (said) toast(said[0], said[1] || '');
      drawSettings();
      return true;
    } catch (e) {
      if (!e.cancelled) R.say('ns-problem', e.message);
      drawSettings();
      return false;
    }
  };
  const rangesWith = (fn) => () => ({ ranges: fn(data.ranges.map((r) => ({ id: r.id, name: r.name, cidr: r.cidr, on: r.on }))) });
  $('ns-ranges').addEventListener('change', (e) => {
    const row = e.target.closest('[data-ns-range]');
    if (!row) return;
    const id = row.dataset.nsRange;
    if (e.target.matches('[data-ns-on]')) {
      const on = e.target.checked;
      save(rangesWith((rs) => rs.map((r) => (r.id === id ? { ...r, on } : r))), [on ? 'Range on.' : 'Range off.', data.ranges.find((r) => r.id === id)?.cidr]);
    } else if (e.target.matches('[data-ns-name]')) {
      const name = e.target.value.trim();
      save(rangesWith((rs) => rs.map((r) => (r.id === id ? { ...r, name } : r))), ['Range renamed.', name]);
    }
  });
  $('ns-ranges').addEventListener('click', (e) => {
    const del = e.target.closest('[data-ns-del]');
    if (!del) return;
    const r = data.ranges.find((x) => x.id === del.closest('[data-ns-range]').dataset.nsRange);
    if (!r) return;
    sure(`Remove ${r.name}?`, `${r.cidr} is no longer scanned. Devices already found on it stay on Network until you remove them.`, 'Remove',
      () => save(rangesWith((rs) => rs.filter((x) => x.id !== r.id)), ['Range removed.', r.cidr]));
  });
  $('ns-add').addEventListener('click', async () => {
    const cidr = $('ns-new-cidr').value.trim();
    const name = $('ns-new-name').value.trim();
    if (!cidr) { R.say('ns-problem', 'Enter a range in CIDR form, such as 10.10.40.0/24.'); $('ns-new-cidr').focus(); return; }
    if (await save(rangesWith((rs) => [...rs, { id: '', name: name || cidr, cidr, on: true }]), ['Range added.', `${name || cidr}. Select Scan now on Network to scan it.`])) {
      $('ns-new-cidr').value = '';
      $('ns-new-name').value = '';
    }
  });
  $('ns-new-cidr').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('ns-add').click(); });
  $('ns-schedule').addEventListener('change', () => {
    const schedule = $('ns-schedule').value;
    save(() => ({ schedule }), ['Schedule saved.', SCHEDULES[schedule]]);
  });
  $('ns-community').addEventListener('change', () => {
    const community = $('ns-community').value.trim() || 'public';
    save(() => ({ community }), ['SNMP community saved.', '']);
  });
})();
