// Domain health › On each DC (DcSettings.cs): security settings read from each domain controller. users.js lists them as
// its dcs page, the second tab of Domain health beside Checks: one row per setting with how many DCs are compliant and
// which are not. This reads them, draws a setting's side panel with every DC's value, and the line over the list.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg } = R;
  const count = (n, one, many) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  const list = (xs) => (xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`);
  const badge = (text, tone, tip = '') => `<span class="ubadge ${tone}"${tip ? ` data-tip="${esc(tip)}"` : ''}>${esc(text)}</span>`;
  const muted = (t) => `<span class="muted">${esc(t)}</span>`;
  const GPO_ID = '{6AC1786C-016F-11D2-945F-00C04FB984F9}'; // Default Domain Controllers Policy
  const SEV = { High: 0, Medium: 1, Low: 2 };
  const SECURITY_OPTIONS = 'Network access: Remotely accessible registry paths and subpaths';
  const time = (iso) => (iso ? new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : '');
  const today = (iso) => iso && new Date(iso).toDateString() === new Date().toDateString();
  const at = (iso) => (!iso ? 'Never' : today(iso) ? time(iso) : R.when(iso));

  // Each setting as a row: the DCs where it's compliant, not compliant, and couldn't be read. rows.info carries the DCs.
  const rows = (j) => {
    const out = (j.settings || []).map((s) => {
      const vs = Object.entries(s.values || {});
      const names = (pick) => vs.filter(([, v]) => pick(v)).map(([n]) => n).sort((a, b) => a.localeCompare(b));
      return { ...s, good: names((v) => v.ok === true), off: names((v) => v.ok === false), unread: names((v) => v.ok == null), total: vs.length };
    });
    out.info = { dcs: j.dcs || [], reading: !!j.reading, account: !!j.account, user: j.user || 'The service account', windows: !!j.windows, paths: j.paths || [] };
    return out;
  };
  const load = async (fresh = false, only = null) => rows(await R.get(`/api/ad/dcs${fresh ? `?again=true${only?.length ? `&dc=${encodeURIComponent(only.join(','))}` : ''}` : ''}`));

  // --- The list's cells -------------------------------------------------------------------------------------------------
  // A mark and the words: green with a tick, red with !, grey when nothing could be read.
  const mark = (tone, text) => `<span class="dcs-v"><span class="ubadge ${tone}"><span class="dcs-t">${esc(text)}</span></span></span>`;
  const countCell = (s) => (!s.total ? muted('Not read yet') : mark(s.off.length ? 'red' : s.good.length ? 'green' : 'grey', `${s.good.length} of ${s.total}`));
  // A few DC names, then how many more: "WEST-DC09, HQ-DC01 +5 more".
  const few = (names, max = 2) => {
    if (!names.length) return muted('None');
    const cut = names.length > max + 1;
    return `<span class="ell">${esc((cut ? names.slice(0, max) : names).join(', '))}${cut ? ` <span class="muted">+${names.length - max} more</span>` : ''}</span>`;
  };
  const sevCell = (s) => `<span class="dcs-sev ${esc(String(s.severity).toLowerCase())}">${esc(s.severity)}</span>`;
  const sevRank = (s) => SEV[s.severity] ?? 3;

  // --- The line over the list and the banner's state ---------------------------------------------------------------------
  const offCount = (rs) => rs.filter((s) => s.off.length).length;
  const state = (rs) => (!rs.info.dcs.length ? 'On each DC.' : offCount(rs) ? `Not compliant: ${offCount(rs)} of ${rs.length} settings.` : 'Compliant on every domain controller.');
  // Why values couldn't be read, by message, with the DCs each was about. Access denied comes with what to do.
  const problems = (rs) => {
    const by = new Map();
    rs.forEach((s) => Object.entries(s.values || {}).forEach(([dc, v]) => {
      if (v.ok != null || !v.problem) return;
      const key = v.problem.startsWith('Access denied') ? 'denied' : v.problem;
      if (!by.has(key)) by.set(key, new Set());
      by.get(key).add(dc);
    }));
    return by;
  };
  const banner = (rs) => {
    const i = rs.info;
    if (!i.windows) return `${svg('alert')}<span><b>Settings are read from each domain controller only when ${esc(R.app())} runs on Windows.</b></span>`;
    if (!i.account) return `${svg('alert')}<span><b>Settings are read from each domain controller as the service account.</b> Set up the service account to read them.</span>`;
    if (!i.dcs.length) return `${svg('refresh', 'dcs-spin')}<span><b>Reading security settings from each domain controller.</b> They show here as soon as they are read.</span>`;
    const off = offCount(rs);
    const last = i.dcs.reduce((m, d) => (!m || d.at > m ? d.at : m), '');
    let html = `${svg('server')}<span><b>${off ? `${off} of ${rs.length} settings are not compliant on one or more domain controllers.` : `All ${rs.length} settings are compliant on every domain controller read.`}</b> `
      + `${i.reading ? 'Reading the domain controllers again.' : `${count(i.dcs.length, 'domain controller', 'domain controllers')} read, last at ${esc(at(last))}.`}</span>`;
    const why = problems(rs);
    const denied = why.get('denied');
    if (denied) {
      html += `<span class="warn">${esc(i.user)} cannot read the registry on ${esc(list([...denied].sort()))}. In the Default Domain Controllers Policy, add the registry paths to ${esc(SECURITY_OPTIONS)}, or read as an account in the Administrators group.</span>`
        + '<button type="button" class="small" data-banner="dcs-paths">Copy the paths</button>';
    }
    [...why.keys()].filter((k) => k !== 'denied').slice(0, 3).forEach((p) => { html += `<span class="warn">${esc(p)}</span>`; });
    return html;
  };
  const copyPaths = async (rs) => {
    try {
      await navigator.clipboard.writeText(rs.info.paths.join('\r\n'));
      R.toast('Copied the registry paths.', `Paste them into ${SECURITY_OPTIONS}, one per line.`);
    } catch {
      R.toast('Could not copy the registry paths.', rs.info.paths.join('; '), { tone: 'warn' });
    }
  };

  // --- A setting's side panel: every DC's value, not compliant first, then why it matters and how to fix it ---------------
  const P = { for: '', q: '', site: false };
  const status = (v) => (v.ok === true ? badge('Compliant', 'green') : v.ok === false ? badge('Not compliant', 'red') : badge('Could not be read', 'grey', v.problem || ''));
  const rank = (v) => (v.ok === false ? 0 : v.ok == null ? 1 : 2);
  const dcRows = (s, rs) => {
    const q = P.q.trim().toLowerCase();
    const dcs = rs.info.dcs.filter((d) => s.values[d.name]);
    const hit = (d) => !q || `${d.name} ${d.site || ''} ${s.values[d.name].value}`.toLowerCase().includes(q);
    const order = (ds) => [...ds].sort((a, b) => rank(s.values[a.name]) - rank(s.values[b.name]) || a.name.localeCompare(b.name));
    const row = (d) => {
      const v = s.values[d.name];
      return `<div class="dcs-mini-r"><span>${svg('server')}${esc(d.name)}</span><span>${esc(d.site || '-')}</span><span${v.problem ? ` data-tip="${esc(v.problem)}"` : ''}>${esc(v.value)}</span>`
        + `<span>${status(v)}</span><span>${esc(at(d.at))}</span></div>`;
    };
    let body;
    if (P.site) {
      const sites = [...new Set(dcs.map((d) => d.site || 'No site'))].sort();
      body = sites.map((site) => {
        const all = dcs.filter((d) => (d.site || 'No site') === site);
        const shown = order(all.filter(hit));
        if (q && !shown.length) return '';
        const ok = all.filter((d) => s.values[d.name].ok === true).length;
        return `<div class="dcs-mini-g">${svg('globe')}<b>${esc(site)}</b>${mark(ok === all.length ? 'green' : 'red', `${ok} of ${all.length} compliant`)}</div>${shown.map(row).join('')}`;
      }).join('');
    } else body = order(dcs.filter(hit)).map(row).join('');
    return `<div class="dcs-mini"><div class="dcs-mini-h"><span>Domain controller</span><span>Site</span><span>Value</span><span>Status</span><span>Last read</span></div>`
      + `${body || '<p class="dcs-none">No domain controller matches the search.</p>'}</div>`;
  };
  const panel = (s, rs) => {
    if (P.for !== s.key) Object.assign(P, { for: s.key, q: '' });
    const n = s.total;
    const top = !n ? badge('Not read yet', 'grey')
      : s.off.length ? badge(`Not compliant on ${s.off.length <= 3 ? list(s.off) : `${s.off.length} of ${n} DCs`}`, 'red')
        : s.unread.length ? badge(`Could not be read on ${s.unread.length <= 3 ? list(s.unread) : `${s.unread.length} of ${n} DCs`}`, 'grey')
          : badge(n === 1 ? 'Compliant' : n === 2 ? 'Compliant on both DCs' : `Compliant on all ${n} DCs`, 'green');
    const gpo = R.has('gpo.view') ? `<button type="button" class="dw-qa" data-dcs-gpo>${svg('policy')}<span>Open GPO</span></button>` : '';
    const tools = n > 6 ? `<div class="dwa-bar"><label class="side-find">${svg('search')}<input type="search" id="dcs-find" placeholder="Find a domain controller, site or value" aria-label="Find a domain controller, site or value"`
      + ` autocomplete="off" spellcheck="false" value="${esc(P.q)}"></label><label class="dwa-all"><input type="checkbox" id="dcs-site"${P.site ? ' checked' : ''}>Group by site</label></div>` : '';
    const fix = s.off.length ? '<section class="dw-sec"><div class="dw-sec-head"><h3>How to fix it</h3></div>'
      + `<div class="dcs-fix"><div><span class="l">GPO</span><span class="v">${esc(s.gpo)}</span></div><div><span class="l">Path</span><span class="v">${esc(s.path)}</span></div>`
      + `<div><span class="l">Policy setting</span><span class="v">${esc(s.policy)}</span></div><div><span class="l">Value</span><span class="v"><b>${esc(s.setTo)}</b></span></div></div>`
      + '<p class="dw-how">Domain controllers apply the change at the next Group Policy refresh (within 5 minutes), or run gpupdate /force on each one. Then select Re-check.</p></section>' : '';
    return `<div class="dw-head"><span class="pic dw-pic">${svg('shield')}</span><div class="dw-name"><h2 id="dw-title">${esc(s.name)}</h2><p>Domain controllers · Severity: ${esc(s.severity)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-status">${top}</div>`
      + `<div class="dw-bar"><button type="button" class="dw-qa" data-dcs-recheck>${svg('refresh')}<span>Re-check</span></button>${gpo}`
      + `<button type="button" class="dw-qa" data-dcs-copy>${svg('copy')}<span>Copy path</span></button></div>`
      + `<div class="dw-strip">${[['Recommended', s.recommended], ['Severity', s.severity], ['Compliant on', n ? `${s.good.length} of ${n}` : '-'], ['Read from', s.readFrom.split(':')[0]]]
        .map(([l, v]) => `<div><span class="l">${esc(l)}</span><span class="v">${esc(v)}</span></div>`).join('')}</div>`
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>On each DC <small>${n}</small></h3></div>${tools}<div data-dcs-rows>${dcRows(s, rs)}</div>`
      + `<p class="dw-src">Read from: ${esc(s.readFrom)}</p></section>`
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>Why it matters</h3></div><p class="dw-how">${esc(s.about)}</p></section>`
      + fix;
  };
  // The panel's own controls. again(names) reads those DCs again (users.js, which draws the list and the panel after).
  const click = (e, s, rs, again) => {
    if (e.target.closest('[data-dcs-recheck]')) {
      const names = [...s.off, ...s.unread];
      again(names.length && names.length < rs.info.dcs.length ? names : null);
    } else if (e.target.closest('[data-dcs-gpo]')) R.openGpo?.(GPO_ID);
    else if (e.target.closest('[data-dcs-copy]')) {
      navigator.clipboard.writeText(`${s.gpo} › ${s.path} › ${s.policy}: ${s.setTo}`).then(() => R.toast('Copied the GPO path and setting.'), () => R.toast('Could not copy.', '', { tone: 'warn' }));
    }
  };
  const input = (e, s, rs) => {
    if (e.target.id === 'dcs-find') P.q = e.target.value;
    else if (e.target.id === 'dcs-site') P.site = e.target.checked;
    else return;
    const box = $('dw-body').querySelector('[data-dcs-rows]');
    if (box) box.innerHTML = dcRows(s, rs);
  };

  R.dcSettings = { load, rows, countCell, few, sevCell, sevRank, state, banner, copyPaths, panel, click, input };
})();
