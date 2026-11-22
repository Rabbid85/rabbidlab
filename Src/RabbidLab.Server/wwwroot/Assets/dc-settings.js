// Domain health › On each computer (DcSettings.cs): security settings read from each domain controller, server and PC.
// users.js lists them as its dcs page, the second tab of Domain health beside Checks, with a view for each kind of computer
// (All computers, Domain controllers, Servers, PCs): one row per setting with its current value, how many computers are
// compliant and which are not. This reads them, draws a setting's side panel with every computer's value, and the line
// over the list. kind is the view open: all, dc, server or pc.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg } = R;
  const count = (n, one, many) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const list = (xs) => (xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`);
  // A few names, then how many more, in words: "PC01, PC02, PC03 and 12 more".
  const some = (xs, max = 3) => (xs.length <= max + 1 ? list(xs) : `${xs.slice(0, max).join(', ')} and ${xs.length - max} more`);
  const badge = (text, tone, tip = '') => `<span class="ubadge ${tone}"${tip ? ` data-tip="${esc(tip)}"` : ''}>${esc(text)}</span>`;
  const muted = (t) => `<span class="muted">${esc(t)}</span>`;
  const DC_GPO_ID = '{6AC1786C-016F-11D2-945F-00C04FB984F9}'; // Default Domain Controllers Policy
  const DC_GPO = 'Default Domain Controllers Policy';
  const MEMBER_GPO = 'A GPO linked to the servers’ and PCs’ OUs';
  const SEV = { High: 0, Medium: 1, Low: 2 };
  const KINDS = ['all', 'dc', 'server', 'pc'];
  const WORDS = { all: ['computer', 'computers'], dc: ['domain controller', 'domain controllers'], server: ['server', 'servers'], pc: ['PC', 'PCs'] };
  const TYPE = { dc: 'Domain controller', server: 'Server', pc: 'PC' };
  const REMOTE_PATHS = 'Network access: Remotely accessible registry paths and subpaths';
  const time = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '');
  const today = (iso) => iso && new Date(iso).toDateString() === new Date().toDateString();
  const at = (iso) => (!iso ? 'Never' : today(iso) ? time(iso) : R.when(iso));
  const words = (n, kind) => count(n, ...WORDS[kind]);

  // Each setting as a row, with, for each kind of computer, the ones where it's compliant, not compliant and couldn't be read.
  const rows = (j) => {
    const computers = j.computers || [];
    const kindOf = Object.fromEntries(computers.map((c) => [c.name, c.kind]));
    const out = (j.settings || []).map((s) => {
      const vs = Object.entries(s.values || {});
      const by = {};
      KINDS.forEach((k) => {
        const mine = vs.filter(([n]) => k === 'all' || kindOf[n] === k);
        const names = (pick) => mine.filter(([, v]) => pick(v)).map(([n]) => n).sort((a, b) => a.localeCompare(b));
        by[k] = { good: names((v) => v.ok === true), off: names((v) => v.ok === false), unread: names((v) => v.ok == null), total: mine.length };
      });
      return { ...s, kinds: s.kinds || ['dc'], by, kindOf, ...by.all };
    });
    out.info = { computers, reading: !!j.reading, account: !!j.account, user: j.user || 'The service account', windows: !!j.windows, paths: j.paths || [] };
    return out;
  };
  const load = async (fresh = false, only = null) => rows(await R.get(`/api/ad/dcs${fresh ? `?again=true${only?.length ? `&dc=${encodeURIComponent(only.join(','))}` : ''}` : ''}`));
  const of = (s, kind) => s.by[kind] || s.by.all;
  const applies = (s, kind) => kind === 'all' || s.kinds.includes(kind);
  const computersOf = (rs, kind) => (rs?.info.computers || []).filter((c) => kind === 'all' || c.kind === kind);

  // --- The list's cells -------------------------------------------------------------------------------------------------
  // A mark and the words: green with a tick, red with !, grey when nothing could be read.
  const mark = (tone, text) => `<span class="dcs-v"><span class="ubadge ${tone}"><span class="dcs-t">${esc(text)}</span></span></span>`;
  const countCell = (s, kind) => {
    const t = of(s, kind);
    return !t.total ? muted('Not read yet') : mark(t.off.length ? 'red' : t.good.length ? 'green' : 'grey', `${t.good.length} of ${t.total}`);
  };
  // A few names, then how many more: "WEST-DC09, HQ-DC01 +5 more".
  const few = (names, max = 2) => {
    if (!names.length) return muted('None');
    const cut = names.length > max + 1;
    return `<span class="ell">${esc((cut ? names.slice(0, max) : names).join(', '))}${cut ? ` <span class="muted">+${names.length - max} more</span>` : ''}</span>`;
  };
  // What it's set to now: the one value when every computer says the same, else each value and on how many, the ones to fix first.
  const current = (s, kind = 'all') => {
    const by = new Map();
    Object.entries(s.values || {}).forEach(([n, v]) => {
      if (v.ok == null || (kind !== 'all' && s.kindOf[n] !== kind)) return;
      const e = by.get(v.value) || { n: 0, ok: v.ok };
      e.n += 1;
      by.set(v.value, e);
    });
    if (!by.size) return of(s, kind).total ? 'Could not be read' : '';
    const vs = [...by].sort((a, b) => Number(a[1].ok) - Number(b[1].ok) || b[1].n - a[1].n);
    return vs.length === 1 ? vs[0][0] : vs.map(([v, e]) => `${v} on ${e.n}`).join(', ');
  };
  const currentCell = (s, kind) => { const t = current(s, kind); return t ? `<span class="ell" data-tip="${esc(t)}">${esc(t)}</span>` : muted('Not read yet'); };
  const sevCell = (s) => `<span class="dcs-sev ${esc(String(s.severity).toLowerCase())}">${esc(s.severity)}</span>`;
  const sevRank = (s) => SEV[s.severity] ?? 3;

  // --- The banner's state and numbers, and the line over the list ------------------------------------------------------
  const shown = (rs, kind) => rs.filter((s) => applies(s, kind));
  const offCount = (rs, kind) => shown(rs, kind).filter((s) => of(s, kind).off.length).length;
  const state = (rs, kind) => (!computersOf(rs, kind).length ? 'On each computer.' : offCount(rs, kind)
    ? `Not compliant: ${offCount(rs, kind)} of ${shown(rs, kind).length} settings.` : `Compliant on every ${WORDS[kind][0]}.`);
  // [number, icon, label] for the banner; null numbers while it's read.
  const stats = (rs, kind) => {
    const unread = rs ? computersOf(rs, kind).filter((c) => rs.some((s) => s.values?.[c.name] && s.values[c.name].ok == null)).length : null;
    const plural = WORDS[kind][1];
    return [[rs ? shown(rs, kind).length : null, 'shield', 'Settings'], [rs ? offCount(rs, kind) : null, 'alert', 'Not compliant'],
      [rs ? computersOf(rs, kind).length : null, kind === 'pc' ? 'laptop' : 'server', plural.charAt(0).toUpperCase() + plural.slice(1)], [unread, 'eye-off', 'Could not be read']];
  };
  // Why values couldn't be read, by kind of reason, with the computers each was about.
  const problems = (rs, kind) => {
    const by = new Map();
    shown(rs, kind).forEach((s) => Object.entries(s.values || {}).forEach(([n, v]) => {
      if (v.ok != null || !v.problem || (kind !== 'all' && s.kindOf[n] !== kind)) return;
      const key = v.why && v.why !== 'error' ? v.why : v.problem;
      if (!by.has(key)) by.set(key, new Set());
      by.get(key).add(n);
    }));
    return by;
  };
  const banner = (rs, kind) => {
    const i = rs.info;
    if (!i.windows) return `${svg('alert')}<span><b>Settings are read from each computer only when ${esc(R.app())} runs on Windows.</b></span>`;
    if (!i.account) return `${svg('alert')}<span><b>Settings are read from each computer as the service account.</b> Set up the service account to read them.</span>`;
    const cs = computersOf(rs, kind);
    if (!cs.length) {
      return i.reading && kind !== 'server' && kind !== 'pc' ? `${svg('refresh', 'dcs-spin')}<span><b>Reading security settings from each domain controller.</b> They show here as soon as they are read.</span>`
        : `${svg('server')}<span><b>No ${esc(WORDS[kind][1])} yet.</b> Servers and PCs show here once the ${esc(R.app())} client is installed and approved on them. It sends their settings with its inventory.</span>`;
    }
    const n = shown(rs, kind).length;
    const off = offCount(rs, kind);
    const last = cs.reduce((m, c) => (!m || c.at > m ? c.at : m), '');
    const how = kind === 'dc' ? '' : kind === 'all' ? ' Domain controllers are read over the network; servers and PCs send theirs through the client.' : ' Each one’s client sends them with its inventory.';
    let html = `${svg('server')}<span><b>${off ? `${off} of ${n} settings are not compliant on one or more ${esc(WORDS[kind][1])}.` : `All ${n} settings are compliant on every ${esc(WORDS[kind][0])} read.`}</b> `
      + `${i.reading && kind !== 'server' && kind !== 'pc' ? 'Reading the domain controllers again.' : `${esc(words(cs.length, kind))}, last read at ${esc(at(last))}.`}${how}</span>`;
    const why = problems(rs, kind);
    const names = (k) => some([...why.get(k)].sort());
    if (why.has('denied')) {
      html += `<span class="warn">${esc(i.user)} cannot read the registry on ${esc(names('denied'))}. Add the registry paths to ${esc(REMOTE_PATHS)} in their GPO, or read as an account in their Administrators group.</span>`
        + '<button type="button" class="small" data-banner="dcs-paths">Copy the paths</button>';
    }
    if (why.has('registry')) html += `<span class="warn">The Remote Registry service is not running on ${esc(names('registry'))}. Set it to Automatic with a GPO, under System Services.</span>`;
    if (why.has('unreachable')) html += `<span class="warn">${esc(names('unreachable'))} could not be reached on port 445. Check that they are on, and that the firewall allows File and Printer Sharing.</span>`;
    if (why.has('client')) html += `<span class="warn">The client on ${esc(names('client'))} has not sent its security settings yet. It sends them with its next inventory, once it has updated to this version.</span>`;
    [...why.keys()].filter((k) => !['denied', 'registry', 'unreachable', 'client'].includes(k)).slice(0, 2).forEach((p) => { html += `<span class="warn">${esc(p)}</span>`; });
    return html;
  };
  const copyPaths = async (rs) => {
    try {
      await navigator.clipboard.writeText(rs.info.paths.join('\r\n'));
      R.toast('Copied the registry paths.', `Paste them into ${REMOTE_PATHS}, one per line.`);
    } catch {
      R.toast('Could not copy the registry paths.', rs.info.paths.join('; '), { tone: 'warn' });
    }
  };

  // --- A setting's side panel: every computer's value, not compliant first, then why it matters and how to fix it ---------
  const P = { for: '', q: '', group: false };
  const status = (v) => (v.ok === true ? badge('Compliant', 'green') : v.ok === false ? badge('Not compliant', 'red') : badge('Could not be read', 'grey', v.problem || ''));
  const rank = (v) => (v.ok === false ? 0 : v.ok == null ? 1 : 2);
  // The second column and what Group by gathers under: the AD site for DCs, the kind of computer for All computers.
  const place = (c, kind) => (kind === 'dc' ? c.site || 'No site' : TYPE[c.kind] || c.kind);
  const groupable = (kind) => kind === 'dc' || kind === 'all';
  const rowsHtml = (s, rs, kind) => {
    const q = P.q.trim().toLowerCase();
    const cs = computersOf(rs, kind).filter((c) => s.values[c.name]);
    const hit = (c) => !q || `${c.name} ${place(c, kind)} ${s.values[c.name].value}`.toLowerCase().includes(q);
    const order = (xs) => [...xs].sort((a, b) => rank(s.values[a.name]) - rank(s.values[b.name]) || a.name.localeCompare(b.name));
    const row = (c) => {
      const v = s.values[c.name];
      return `<div class="dcs-mini-r"><span>${svg(c.kind === 'pc' ? 'laptop' : 'server')}${esc(c.name)}</span><span>${esc(place(c, kind))}</span><span${v.problem ? ` data-tip="${esc(v.problem)}"` : ''}>${esc(v.value)}</span>`
        + `<span>${status(v)}</span><span>${esc(at(c.at))}</span></div>`;
    };
    let body;
    if (P.group && groupable(kind)) {
      const groups = [...new Set(cs.map((c) => place(c, kind)))].sort();
      body = groups.map((g) => {
        const all = cs.filter((c) => place(c, kind) === g);
        const hits = order(all.filter(hit));
        if (q && !hits.length) return '';
        const ok = all.filter((c) => s.values[c.name].ok === true).length;
        return `<div class="dcs-mini-g">${svg(kind === 'dc' ? 'globe' : 'server')}<b>${esc(g)}</b>${mark(ok === all.length ? 'green' : 'red', `${ok} of ${all.length} compliant`)}</div>${hits.map(row).join('')}`;
      }).join('');
    } else body = order(cs.filter(hit)).map(row).join('');
    return `<div class="dcs-mini"><div class="dcs-mini-h"><span>Computer</span><span>${kind === 'dc' ? 'Site' : 'Type'}</span><span>Value</span><span>Status</span><span>Last read</span></div>`
      + `${body || '<p class="dcs-none">No computer matches the search.</p>'}</div>`;
  };
  // Where the fix goes: the DCs' policy for DC settings, a GPO on the members' OUs for theirs, both for settings everywhere.
  const gpoOf = (s, kind) => s.gpo || (kind === 'dc' ? DC_GPO : kind === 'all' ? `${DC_GPO} for the DCs, and a GPO linked to the servers’ and PCs’ OUs` : MEMBER_GPO);
  const dcGpo = (s, kind) => s.gpo === DC_GPO || (!s.gpo && kind === 'dc');
  // Domain controllers are read over the network; servers and PCs by their own client.
  const readFrom = (s, kind, short) => {
    const net = short ? s.readFrom.split(':')[0] : s.readFrom;
    if (kind === 'server' || kind === 'pc' || !s.kinds.includes('dc')) return `The ${R.app()} client`;
    return kind === 'dc' || s.kinds.length === 1 ? net : short ? `${net} and the client` : `${net} on domain controllers, and the ${R.app()} client on servers and PCs`;
  };
  const panel = (s, rs, kind) => {
    if (P.for !== s.key) Object.assign(P, { for: s.key, q: '' });
    const t = of(s, kind);
    const n = t.total;
    const who = (xs) => (xs.length <= 3 ? list(xs) : `${xs.length} of ${n} ${WORDS[kind][1]}`);
    const top = !n ? badge('Not read yet', 'grey')
      : t.off.length ? badge(`Not compliant on ${who(t.off)}`, 'red')
        : t.unread.length ? badge(`Could not be read on ${who(t.unread)}`, 'grey')
          : badge(n === 1 ? 'Compliant' : n === 2 ? `Compliant on both ${WORDS[kind][1]}` : `Compliant on all ${n} ${WORDS[kind][1]}`, 'green');
    const gpo = R.has('gpo.view') ? `<button type="button" class="dw-qa" data-dcs-gpo>${svg('policy')}<span>${dcGpo(s, kind) ? 'Open GPO' : 'Open Group Policy'}</span></button>` : '';
    const tools = n > 6 ? `<div class="dwa-bar"><label class="side-find">${svg('search')}<input type="search" id="dcs-find" placeholder="Find a computer or value" aria-label="Find a computer or value"`
      + ` autocomplete="off" spellcheck="false" value="${esc(P.q)}"></label>${groupable(kind) ? `<label class="dwa-all"><input type="checkbox" id="dcs-group"${P.group ? ' checked' : ''}>${kind === 'dc' ? 'Group by site' : 'Group by type'}</label>` : ''}</div>` : '';
    const fix = t.off.length ? '<section class="dw-sec"><div class="dw-sec-head"><h3>How to fix it</h3></div>'
      + `<div class="dcs-fix"><div><span class="l">GPO</span><span class="v">${esc(gpoOf(s, kind))}</span></div><div><span class="l">Path</span><span class="v">${esc(s.path)}</span></div>`
      + `<div><span class="l">Policy setting</span><span class="v">${esc(s.policy)}</span></div><div><span class="l">Value</span><span class="v"><b>${esc(s.setTo)}</b></span></div></div>`
      + '<p class="dw-how">Computers apply the change at their next Group Policy refresh (within 5 minutes on DCs, 90 minutes on others), or run gpupdate /force on each one. Then select Re-check.</p></section>' : '';
    const forWhom = s.kinds.length === 3 ? 'Every computer' : s.kinds.map((k) => WORDS[k][1].charAt(0).toUpperCase() + WORDS[k][1].slice(1)).join(' and ');
    return `<div class="dw-head"><span class="pic dw-pic">${svg('shield')}</span><div class="dw-name"><h2 id="dw-title">${esc(s.name)}</h2><p>${esc(forWhom)} · Severity: ${esc(s.severity)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-status">${top}</div>`
      + `<div class="dw-bar"><button type="button" class="dw-qa" data-dcs-recheck>${svg('refresh')}<span>Re-check</span></button>${gpo}`
      + `<button type="button" class="dw-qa" data-dcs-copy>${svg('copy')}<span>Copy path</span></button></div>`
      + `<div class="dw-strip">${[['Current', current(s, kind) || '-'], ['Recommended', s.recommended], ['Compliant on', n ? `${t.good.length} of ${n}` : '-'], ['Read from', readFrom(s, kind, true)]]
        .map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${esc(v)}</span></div>`).join('')}</div>`
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>On each ${esc(WORDS[kind][0])} <small>${n}</small></h3></div>${tools}<div data-dcs-rows>${rowsHtml(s, rs, kind)}</div>`
      + `<p class="dw-src">Read from: ${esc(readFrom(s, kind, false))}</p></section>`
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>Why it matters</h3></div><p class="dw-how">${esc(s.about)}</p></section>`
      + fix;
  };
  // The panel's own controls. again(names) reads those computers again (users.js, which draws the list and the panel after).
  const click = (e, s, rs, again, kind) => {
    if (e.target.closest('[data-dcs-recheck]')) {
      // DCs are read again here; servers and PCs send their own with their next inventory.
      const t = of(s, kind);
      const dc = (n) => s.kindOf[n] === 'dc';
      const names = [...t.off, ...t.unread].filter(dc);
      const all = computersOf(rs, kind).filter((c) => c.kind === 'dc' && s.values[c.name]).map((c) => c.name);
      if (kind !== 'server' && kind !== 'pc' && all.length) again(names.length ? names : all);
      if (kind !== 'dc' && computersOf(rs, kind).some((c) => c.kind !== 'dc')) {
        R.toast('Servers and PCs send their settings through the client.', 'With their inventory: within 30 minutes of a change, and at least every 6 hours.');
      }
    } else if (e.target.closest('[data-dcs-gpo]')) {
      if (dcGpo(s, kind)) R.openGpo?.(DC_GPO_ID);
      else location.hash = '#gpo';
    } else if (e.target.closest('[data-dcs-copy]')) {
      navigator.clipboard.writeText(`${s.path} › ${s.policy}: ${s.setTo}`).then(() => R.toast('Copied the policy path and setting.'), () => R.toast('Could not copy.', '', { tone: 'warn' }));
    }
  };
  const input = (e, s, rs, kind) => {
    if (e.target.id === 'dcs-find') P.q = e.target.value;
    else if (e.target.id === 'dcs-group') P.group = e.target.checked;
    else return;
    const box = $('dw-body').querySelector('[data-dcs-rows]');
    if (box) box.innerHTML = rowsHtml(s, rs, kind);
  };

  R.dcSettings = { KINDS, load, rows, of, applies, computersOf, countCell, few, current, currentCell, sevCell, sevRank, state, stats, banner, copyPaths, panel, click, input };
})();
