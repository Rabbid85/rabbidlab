// Active Directory (#ad/<users|groups|computers>/<view>), changed as Rabbid Lab's own AD account, and Local accounts
// (#local/<computer id>): the Windows accounts on this server ("server") or a computer in Seats, whose client makes the
// change. console.js shares its helpers as window.RabbidLab.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, say, initials, sure, has: can, daysAgo: ago } = R;
  const ADMINS = 'S-1-5-32-544';
  const DAY = 86400000;
  const day = (iso) => new Date(iso).toLocaleDateString([], { dateStyle: 'medium' });
  const days = (iso) => Math.floor((new Date(iso) - Date.now()) / DAY);
  const stale = (iso) => !iso || Date.now() - new Date(iso) > 90 * DAY;
  const chip = (text, cls = '', icon = '') => `<span class="uchip ${cls}">${icon ? svg(icon) : ''}${esc(text)}</span>`;
  const badge = (text, tone) => `<span class="ubadge ${tone}">${esc(text)}</span>`;
  const small = (a, b) => `<div class="two"><b>${esc(a)}</b>${b ? `<small>${esc(b)}</small>` : ''}</div>`;

  // --- Active Directory: what each page lists, its views down the left, its columns and its CSV -------------------

  let ad = null; // /api/ad: the domain, and Rabbid Lab's account once it's set up
  let adPage = 'users';
  let adView = 'all';
  const lists = {}; // what each page loaded, until the next show
  const more = {};
  let rows = [];
  let places = null;

  const userStatus = (u) => (u.locked ? badge('Locked out', 'red') : !u.enabled ? badge('Disabled', 'grey')
    : u.passwordExpires && days(u.passwordExpires) < 0 ? badge('Password expired', 'red') : badge('Enabled', 'green'));
  const pcStatus = (c) => (!c.enabled ? badge('Disabled', 'grey') : stale(c.lastSignIn) ? badge('Inactive', 'amber') : badge('Enabled', 'green'));
  const server = (c) => /server/i.test(c.os || '');
  const TONE = { good: badge('As recommended', 'green'), warn: badge('Worth a look', 'amber'), bad: badge('Not as recommended', 'red'), unknown: badge("Couldn't be read", 'grey') };
  const WHAT = { made: 'green', changed: 'grey', deleted: 'red', restored: 'amber' };
  const KIND_ICON = { user: 'user', group: 'users', computer: 'laptop', ou: 'list', container: 'list', other: 'list' };
  let recycle = null; // /api/ad/recycle: whether it's on, and what's in it

  const AD = {
    users: {
      title: 'Users', noun: ['user', 'users'], add: 'New user', key: (u) => u.username,
      lead: 'Everyone in the domain, with views for what needs looking at.',
      load: async () => {
        const j = await R.keep('/api/ad/users'); // kept for the Org chart
        more.users = j.more;
        return j.users;
      },
      aside: (u) => u.system,
      find: (u) => [u.name, u.username, u.upn, u.email, u.title, u.department, u.where],
      views: [
        ['View', [
          { key: 'all', label: 'All users' },
          { key: 'enabled', label: 'Enabled', where: (u) => u.enabled },
          { key: 'disabled', label: 'Disabled', where: (u) => !u.enabled },
          { key: 'system', label: 'System accounts', where: (u) => u.system, quiet: true, aside: true },
        ]],
        ['Needs attention', [
          { key: 'locked', label: 'Locked out', tone: 'red', quiet: true, where: (u) => u.locked },
          { key: 'expired', label: 'Expired passwords', tone: 'red', quiet: true, where: (u) => u.enabled && u.passwordExpires && days(u.passwordExpires) < 0 },
          { key: 'expiring', label: 'Expiring passwords', tone: 'amber', quiet: true, where: (u) => u.enabled && u.passwordExpires && days(u.passwordExpires) >= 0 && days(u.passwordExpires) <= 14 },
          { key: 'inactive', label: 'Inactive (90+ days)', tone: 'amber', quiet: true, where: (u) => u.enabled && stale(u.lastSignIn) },
        ]],
        ['Review', [
          { key: 'privileged', label: 'Privileged accounts', quiet: true, where: (u) => u.protected },
          { key: 'noexpiry', label: 'Password never expires', quiet: true, where: (u) => u.neverExpires },
          { key: 'mustchange', label: 'Password change required', quiet: true, where: (u) => u.mustChange },
          { key: 'never', label: 'Never signed in', quiet: true, where: (u) => !u.lastSignIn },
          { key: 'ending', label: 'Accounts with an expiration date', quiet: true, where: (u) => !!u.accountExpires },
        ]],
      ],
      head: ['Name', 'Pre-Windows 2000 logon name', 'Department', 'Last sign-in', 'Status'],
      cells: (u) => [
        `<div class="who"><span class="pic user">${esc(initials(u.name || u.username))}</span>${small(u.name || u.username, u.upn)}</div>`,
        `<span class="mono">${esc(u.username)}</span>`,
        u.department || u.title ? small(u.department || u.title, u.department ? u.title : '') : '<span class="muted">-</span>',
        esc(ago(u.lastSignIn)),
        userStatus(u) + (u.service ? chip('Rabbid Lab', 'ember') : ''),
      ],
      csv: [['Name', (u) => u.name], ['Logon name', (u) => u.username], ['User principal name', (u) => u.upn], ['Email', (u) => u.email], ['Department', (u) => u.department],
        ['Job title', (u) => u.title], ['Manager', (u) => u.manager], ['Organizational unit', (u) => u.where], ['Last sign-in', (u) => u.lastSignIn], ['Password last set', (u) => u.passwordSet],
        ['Password expires', (u) => u.passwordExpires], ['Password never expires', (u) => u.neverExpires], ['Must change password', (u) => u.mustChange],
        ['Account expires', (u) => u.accountExpires], ['Enabled', (u) => u.enabled], ['Locked out', (u) => u.locked], ['Privileged', (u) => u.protected], ['Created', (u) => u.created],
        ['Distinguished name', (u) => u.dn]],
    },
    groups: {
      title: 'Groups', noun: ['group', 'groups'], add: 'New group', key: (g) => g.dn,
      lead: 'Security and distribution groups, and who is in each.',
      load: async () => { const l = await R.get('/api/ad/groups'); more.groups = l.more; return l; },
      aside: () => false,
      find: (g) => [g.name, g.description, g.where],
      views: [
        ['Type', [
          { key: 'all', label: 'All groups' },
          { key: 'security', label: 'Security', where: (g) => g.security },
          { key: 'distribution', label: 'Distribution', where: (g) => !g.security },
          { key: 'privileged', label: 'Privileged', where: (g) => g.protected },
        ]],
        // Domain Users and the like count their members another way (the primary group), so they never look empty.
        ['Cleanup', [{ key: 'empty', label: 'Empty groups', tone: 'amber', where: (g) => g.members === 0 && !g.moreMembers && !g.system }]],
      ],
      head: ['Group', 'Group type', 'Group scope', 'Members', 'Organizational unit'],
      cells: (g) => [
        `<div class="who"><span class="pic">${svg('users')}</span>${small(g.name, g.description)}</div>`,
        `${g.security ? 'Security' : 'Distribution'}${g.protected ? ` ${badge('Privileged', 'amber')}` : ''}`,
        esc(g.scope),
        `<span class="num">${g.moreMembers ? '1,500+' : g.members.toLocaleString()}</span>`,
        esc(g.where || ad.state.domain),
      ],
      csv: [['Group', (g) => g.name], ['Description', (g) => g.description], ['Group type', (g) => (g.security ? 'Security' : 'Distribution')], ['Group scope', (g) => g.scope],
        ['Members', (g) => g.members], ['Privileged', (g) => g.protected], ['Organizational unit', (g) => g.where], ['Distinguished name', (g) => g.dn]],
    },
    computers: {
      title: 'Computers', noun: ['computer', 'computers'], add: null, key: (c) => c.name,
      lead: 'Every computer in the domain: servers and workstations, and the ones nobody has used in a while.',
      load: async () => {
        const j = await R.get('/api/ad/computers');
        more.computers = j.more;
        return j.computers;
      },
      aside: () => false,
      find: (c) => [c.name, c.host, c.os, c.description, c.where],
      views: [
        ['View', [
          { key: 'all', label: 'All computers' },
          { key: 'servers', label: 'Servers', where: server },
          { key: 'workstations', label: 'Workstations', where: (c) => !server(c) },
        ]],
        ['Cleanup', [
          { key: 'stale', label: 'Inactive (90+ days)', tone: 'amber', where: (c) => c.enabled && stale(c.lastSignIn) },
          { key: 'disabled', label: 'Disabled', where: (c) => !c.enabled },
        ]],
      ],
      head: ['Computer', 'Operating system', 'Organizational unit', 'Last sign-in', 'Status'],
      cells: (c) => [
        `<div class="who"><span class="pic">${svg(server(c) ? 'server' : 'laptop')}</span>${small(c.name, c.host)}</div>`,
        c.os ? small(c.os, c.osVersion) : '<span class="muted">-</span>',
        esc(c.where || ad.state.domain),
        esc(ago(c.lastSignIn)),
        pcStatus(c) + (c.controller ? chip('Domain controller', 'muted') : ''),
      ],
      csv: [['Computer', (c) => c.name], ['DNS host name', (c) => c.host], ['Operating system', (c) => c.os], ['Version', (c) => c.osVersion], ['Description', (c) => c.description],
        ['Organizational unit', (c) => c.where], ['Last sign-in', (c) => c.lastSignIn], ['Created', (c) => c.created], ['Enabled', (c) => c.enabled], ['Domain controller', (c) => c.controller],
        ['Distinguished name', (c) => c.dn]],
    },
    domain: {
      title: 'Domain settings', noun: ['setting', 'settings'], add: null, key: (s) => s.key,
      lead: 'How the domain is set, beside what is recommended.',
      load: () => R.get('/api/ad/domain'),
      aside: () => false,
      find: (s) => [s.name, s.area, s.now, s.about],
      views: [
        ['View', [
          { key: 'all', label: 'All settings' },
          { key: 'off', label: 'Not as recommended', tone: 'red', where: (s) => s.tone === 'bad' },
          { key: 'look', label: 'Worth a look', tone: 'amber', where: (s) => s.tone === 'warn' },
          { key: 'unread', label: 'Couldn\'t be read', quiet: true, where: (s) => s.tone === 'unknown' },
        ]],
        ['Area', ['Passwords', 'Lockout', 'Accounts', 'Domain', 'Domain controllers'].map((a) => ({ key: a.toLowerCase().replace(/\s+/g, '-'), label: a, where: (s) => s.area === a }))],
      ],
      head: ['Setting', 'Now', 'Recommended', 'Result'],
      cells: (s) => [small(s.name, s.area), esc(s.now), `<span class="muted">${esc(s.recommended)}</span>`, TONE[s.tone] || ''],
      csv: [['Area', (s) => s.area], ['Setting', (s) => s.name], ['Now', (s) => s.now], ['Recommended', (s) => s.recommended], ['Result', (s) => s.tone], ['Why', (s) => s.about]],
    },
    audit: {
      title: 'AD audit', noun: ['change', 'changes'], add: null, key: (c) => `${c.dn}|${c.at}`,
      lead: 'What changed in Active Directory in the last 30 days, made anywhere: here, Active Directory Users and Computers, PowerShell. Sign-ins and housekeeping are left out.',
      load: async () => { const l = await R.get('/api/ad/audit?days=30'); more.audit = l.more; return l; },
      aside: () => false,
      find: (c) => [c.name, c.where, c.attributes.join(' '), c.by],
      views: [
        ['When', [
          { key: 'all', label: 'Last 30 days' },
          { key: 'week', label: 'Last 7 days', where: (c) => Date.now() - new Date(c.at) < 7 * DAY },
          { key: 'day', label: 'Last 24 hours', where: (c) => Date.now() - new Date(c.at) < DAY },
        ]],
        ['What', [
          { key: 'made', label: 'Made', where: (c) => c.what === 'made' },
          { key: 'changed', label: 'Changed', where: (c) => c.what === 'changed' },
          { key: 'deleted', label: 'Deleted', tone: 'red', where: (c) => c.what === 'deleted' },
          { key: 'restored', label: 'Restored', quiet: true, where: (c) => c.what === 'restored' },
        ]],
        ['Kind', [
          { key: 'users', label: 'Users', where: (c) => c.kind === 'user' },
          { key: 'groups', label: 'Groups', where: (c) => c.kind === 'group' },
          { key: 'computers', label: 'Computers', where: (c) => c.kind === 'computer' },
          { key: 'ous', label: 'OUs', quiet: true, where: (c) => c.kind === 'ou' },
        ]],
      ],
      head: ['When', 'What', 'Object', 'Where', 'Changed'],
      cells: (c) => [
        small(ago(c.at), new Date(c.at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })),
        badge(c.what.charAt(0).toUpperCase() + c.what.slice(1), WHAT[c.what]),
        `<div class="who"><span class="pic">${svg(KIND_ICON[c.kind] || 'list')}</span>${small(c.name, c.kind === 'ou' ? 'OU' : c.kind)}</div>`,
        esc(c.where || ad.state.domain),
        small(c.attributes.slice(0, 4).join(', ') + (c.attributes.length > 4 ? ` and ${c.attributes.length - 4} more` : ''), c.by ? `by ${c.by}, in Rabbid Lab` : ''),
      ],
      csv: [['When', (c) => c.at], ['What', (c) => c.what], ['Kind', (c) => c.kind], ['Name', (c) => c.name], ['Where', (c) => c.where], ['Changed', (c) => c.attributes.join(' ')],
        ['By', (c) => c.by], ['Distinguished name', (c) => c.dn]],
    },
    recycle: {
      title: 'Recycle Bin', noun: ['deleted object', 'deleted objects'], add: null, key: (d) => d.dn,
      lead: 'Users, groups, computers and OUs that were deleted, which can come back as they were.',
      load: async () => {
        recycle = await R.get('/api/ad/recycle');
        more.recycle = recycle.more;
        return recycle.items;
      },
      aside: () => false,
      find: (d) => [d.name, d.wasIn, d.username],
      views: [
        ['View', [
          { key: 'all', label: 'Everything' },
          { key: 'users', label: 'Users', where: (d) => d.kind === 'user' },
          { key: 'groups', label: 'Groups', where: (d) => d.kind === 'group' },
          { key: 'computers', label: 'Computers', where: (d) => d.kind === 'computer' },
          { key: 'ous', label: 'OUs', quiet: true, where: (d) => d.kind === 'ou' },
        ]],
      ],
      head: ['Name', 'Type', 'Was in', 'Deleted'],
      cells: (d) => [
        `<div class="who"><span class="pic">${svg(KIND_ICON[d.kind] || 'list')}</span>${small(d.name, d.username)}</div>`,
        esc(d.kind === 'ou' ? 'OU' : d.kind.charAt(0).toUpperCase() + d.kind.slice(1)),
        esc(d.wasIn || ad.state.domain),
        esc(d.deleted ? ago(d.deleted) : '-'),
      ],
      csv: [['Name', (d) => d.name], ['Type', (d) => d.kind], ['Was in', (d) => d.wasIn], ['Deleted', (d) => d.deleted], ['Distinguished name', (d) => d.dn]],
    },
  };
  const page = () => AD[adPage];
  const allViews = () => page().views.flatMap(([, items]) => items);
  const viewOf = (key) => allViews().find((v) => v.key === key) || allViews()[0];
  const inView = (v, x) => (v.aside || !page().aside(x)) && (!v.where || v.where(x));

  // How many are in each view, counted again only when the list changes.
  let counted = { list: null, page: '', n: {} };
  const counts = () => {
    const list = lists[adPage] || [];
    if (counted.list !== list || counted.page !== adPage) {
      const n = {};
      allViews().forEach((v) => { n[v.key] = list.filter((x) => inView(v, x)).length; });
      counted = { list, page: adPage, n };
    }
    return counted.n;
  };
  const drawViews = () => {
    const count = counts();
    $('dir-views').innerHTML = page().views.map(([label, items]) => {
      const links = items.map((v) => {
        const n = count[v.key];
        if (v.quiet && !n && v.key !== adView) return '';
        return `<a href="#ad/${adPage}/${v.key}"${v.key === adView ? ' aria-current="page"' : ''}><span class="nm">${esc(v.label)}</span>`
          + `<small class="${n && v.tone ? `tone ${v.tone}` : ''}">${n.toLocaleString()}</small></a>`;
      }).join('');
      return links ? `<p class="side-label">${esc(label)}</p>${links}` : '';
    }).join('');
  };

  const drawAd = () => {
    const p = page();
    const s = ad?.state;
    $('dir-title').textContent = p.title;
    $('dir-lead').textContent = p.lead;
    $('dir-setup').hidden = !!s;
    $('dir-lib').hidden = !s;
    $('dir-side').hidden = !s; // the views, in Active Directory's rail
    if (!s) {
      setupCard($('dir-setup'), ad);
      $('dir-locked-dot').hidden = true;
      $('dir-domain-dot').hidden = true;
      $('rail-dir-dot').hidden = true;
      return;
    }
    $('dir-acct').textContent = `Service account: ${s.account}`;
    $('dir-new').hidden = !p.add;
    $('dir-new-text').textContent = p.add || '';
    const v = viewOf(adView);
    const find = $('dir-find').value.trim().toLowerCase();
    const list = lists[adPage] || [];
    rows = list.filter((x) => inView(v, x) && (!find || p.find(x).some((t) => String(t || '').toLowerCase().includes(find))));
    $('dir-view-title').textContent = v.label;
    $('dir-count').textContent = `${rows.length.toLocaleString()} ${p.noun[rows.length === 1 ? 0 : 1]}`;
    $('dir-table').className = `table adt ${adPage}`;
    $('dir-table').innerHTML = `<div class="tr th">${p.head.map((h) => `<span>${esc(h)}</span>`).join('')}</div>`
      + (rows.length ? rows.map((x, i) => `<div class="tr" tabindex="0" data-i="${i}">${p.cells(x).map((c) => `<div>${c}</div>`).join('')}</div>`).join('')
        : `<div class="empty">${list.length ? 'Nothing here right now.' : 'Nothing yet.'}</div>`);
    $('dir-more').hidden = !more[adPage];
    drawViews();
    // The dots in the rail keep what the last read said, while another page is open.
    if (lists.users) $('dir-locked-dot').hidden = !lists.users.some((u) => u.locked && !u.system);
    if (lists.domain) $('dir-domain-dot').hidden = !lists.domain.some((s) => s.tone === 'bad');
    // The main rail's one entry carries a dot when either of those does.
    $('rail-dir-dot').hidden = $('dir-locked-dot').hidden && $('dir-domain-dot').hidden;
    drawBanner();
  };

  // A line above the table: the Recycle Bin's state, how Domain settings stands, and what the audit can say.
  const drawBanner = () => {
    const b = $('dir-banner');
    let html = '';
    if (adPage === 'recycle' && recycle) {
      html = recycle.on
        ? `${svg('check')}<span><b>The Recycle Bin is on.</b> Deleted users, groups and computers can come back as they were for ${recycle.lifetime} days.</span>`
        : `${svg('alert')}<span><b>The Recycle Bin is off,</b> so anything deleted comes back without its groups and most of its details, if at all.</span>`
          + (can('ad.connect') ? '<button type="button" class="small primary" data-banner="enable">Turn it on</button>' : '');
      if (recycle.problem) html += `<span class="warn">${esc(recycle.problem)}</span>`;
    } else if (adPage === 'domain' && lists.domain) {
      const bad = lists.domain.filter((s) => s.tone === 'bad').length;
      const warn = lists.domain.filter((s) => s.tone === 'warn').length;
      html = `${svg(bad ? 'alert' : 'check')}<span><b>${bad ? `${bad} not as recommended` : 'Nothing is off'}${warn ? `, ${warn} worth a look` : ''}.</b> Click a setting to see why it matters and where it's set.</span>`;
    } else if (adPage === 'audit') {
      html = `${svg('history')}<span>Who made a change shows when it was made in Rabbid Lab. For changes made elsewhere, the domain controllers' security logs say who.</span>`;
    }
    b.innerHTML = html;
    b.hidden = !html;
  };
  $('dir-banner').addEventListener('click', (e) => {
    if (!e.target.closest('[data-banner="enable"]')) return;
    $('rb-user').value = R.me()?.source === 'ad' ? R.me().username : '';
    $('rb-password').value = '';
    say('rb-problem', '');
    $('recycle-dialog').showModal();
    $(R.me()?.source === 'ad' ? 'rb-password' : 'rb-user').focus();
  });
  $('rb-cancel').addEventListener('click', () => {
    $('rb-password').value = '';
    $('recycle-dialog').close();
  });
  $('rb-go').addEventListener('click', async () => {
    $('rb-go').disabled = true;
    try {
      await R.send('/api/ad/recycle/enable', { username: $('rb-user').value.trim(), password: $('rb-password').value });
      $('rb-password').value = '';
      $('recycle-dialog').close();
      say('dir-note', 'The Recycle Bin is on. Anything deleted from now on can come back as it was.');
      await loadAd(true);
      drawAd();
    } catch (err) {
      say('rb-problem', err.message);
    } finally {
      $('rb-go').disabled = false;
    }
  });

  // Everything Rabbid Lab's account can see on the page shown, read once each time the page shows.
  const loadAd = async (fresh) => {
    if (!ad?.state || !can('ad.view')) return;
    if (fresh || !lists[adPage]) {
      say('dir-loading', `Reading ${ad.state.domain}…`);
      try {
        lists[adPage] = await page().load();
        say('dir-problem', '');
      } finally {
        say('dir-loading', '');
      }
    }
  };

  // Find draws once typing pauses: the list can be 1,000 long.
  let adFindTimer = null;
  $('dir-find').addEventListener('input', () => {
    clearTimeout(adFindTimer);
    adFindTimer = setTimeout(drawAd, 150);
  });
  $('dir-export').addEventListener('click', () => {
    const p = page();
    const cell = (v) => {
      // A leading quote keeps Excel from running a value that starts like a formula.
      const s = (v === null || v === undefined ? '' : String(v)).replace(/^[=+\-@\t\r]/, "'$&");
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const text = [p.csv.map(([h]) => cell(h)).join(','), ...rows.map((x) => p.csv.map(([, f]) => cell(f(x))).join(','))].join('\r\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([`﻿${text}`], { type: 'text/csv' }));
    a.download = `${ad.state.domain}-${adPage}-${adView}.csv`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  });

  // --- One user, group or computer -----------------------------------------------------------------------------------

  let open = null; // the row its card is for
  const closeCard = () => {
    $('adcard').hidden = true;
    $('ucard').hidden = true;
    open = null;
  };
  const place = (box, near, pageEl) => R.place(box, near, pageEl, 160);
  const facts = (list) => `<dl class="ufacts">${list.filter(([, v]) => v || v === 0).map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('')}</dl>`;
  const head = (picture, name, chips) => `<div class="pcard-head">${picture}<div><b>${esc(name)}</b><span>${chips}</span></div>`
    + `<button type="button" class="icon" data-act="close" aria-label="Close">${svg('x')}</button></div>`;
  const adder = (placeholder) => `<div class="adder"><label class="find">${svg('search')}<input type="search" data-find placeholder="${esc(placeholder)}" aria-label="${esc(placeholder)}" autocomplete="off" spellcheck="false"></label><div class="drop" data-drop hidden></div></div>`;

  const userCard = (u) => {
    const fixed = u.service ? `That's ${ad.state.account}, Rabbid Lab's own account. It looks after it itself.`
      : u.protected ? 'Active Directory protects the accounts of admins, so Rabbid Lab\'s account can\'t change this one. Use Active Directory Users and Computers for it.'
      : u.system ? 'A built-in account. Change it in Active Directory Users and Computers.' : '';
    const change = can('ad.change') && !u.service && !u.protected && !u.system;
    return head(`<span class="pic user">${esc(initials(u.name || u.username))}</span>`, u.name || u.username, userStatus(u))
      + facts([['Logon name', u.username], ['Principal name', u.upn], ['Where', u.where || ad.state.domain], ['Email', u.email], ['Department', u.department], ['Job title', u.title],
        ['Manager', u.manager], ['Description', u.description], ['Last sign-in', ago(u.lastSignIn)],
        ['Password', u.mustChange ? 'Changes at the next sign-in' : u.neverExpires ? 'Never expires' : u.passwordExpires ? `Expires ${day(u.passwordExpires)}` : null],
        ['Account expires', u.accountExpires ? day(u.accountExpires) : null], ['Made', u.created ? day(u.created) : null]])
      + (fixed ? `<p class="hint">${esc(fixed)}</p>` : '')
      + `<h4>Groups</h4><div class="chips-in">${u.groups.map((g, i) => `<span class="gchip">${esc(g.name)}${change ? `<button type="button" class="chip-x" data-act="ungroup" data-g="${i}" aria-label="Take out of ${esc(g.name)}">${svg('x')}</button>` : ''}</span>`).join('') || '<span class="muted">Only Domain Users</span>'}</div>`
      + (change ? adder('Add to a group') : '')
      + `<div class="pcard-acts">${change ? `<button type="button" class="small" data-act="password">${svg('key')}Set a password</button>` : ''}`
      + `${change && u.locked ? `<button type="button" class="small" data-act="unlock">${svg('unlock')}Unlock</button>` : ''}`
      + `${change ? `<button type="button" class="small" data-act="power">${svg('power')}${u.enabled ? 'Turn off' : 'Turn on'}</button>` : ''}`
      + `${can('ad.delete') && !u.service && !u.protected && !u.system ? `<button type="button" class="small danger" data-act="delete">${svg('trash')}Delete</button>` : ''}</div>`;
  };

  const KIND_PIC = { user: 'user', group: 'users', computer: 'laptop', other: 'user' };
  // Who's in the group, drawn on its own so the rest of the card (and what's typed in it) stays put.
  const memberRows = (g, members) => (members === null ? '<p class="muted">Reading…</p>'
    : members.map((m, i) => `<div class="mrow"><span class="pic small${m.kind === 'user' ? ' user' : ''}">${m.kind === 'user' ? esc(initials(m.name)) : svg(KIND_PIC[m.kind])}</span>`
      + `<div class="two"><b>${esc(m.name)}</b><small>${esc([m.username, m.where].filter(Boolean).join(' · '))}</small></div>`
      + `${can('ad.change') && !g.system ? `<button type="button" class="icon" data-act="unmember" data-m="${i}" aria-label="Take ${esc(m.name)} out">${svg('x')}</button>` : ''}</div>`).join('')
      || '<p class="muted">Nobody yet.</p>');
  const groupCard = (g, members) => {
    const change = can('ad.change') && !g.system;
    return head(`<span class="pic">${svg('users')}</span>`, g.name, `${chip(g.security ? 'Security' : 'Distribution', 'muted')}${chip(g.scope, 'muted')}${g.protected ? chip('Privileged', 'amber') : ''}`)
      + facts([['Description', g.description], ['Where', g.where || ad.state.domain], ['Members', g.moreMembers ? '1,500 or more' : String(g.members)]])
      + `<h4>Members</h4><div class="mlist" data-members>${memberRows(g, members)}</div>`
      + (change ? adder('Add a user, group or computer') : '')
      + (can('ad.delete') && !g.system && !g.protected ? `<div class="pcard-acts"><button type="button" class="small danger" data-act="delete">${svg('trash')}Delete group</button></div>` : '');
  };

  const settingCard = (s) => head(`<span class="pic">${svg('globe')}</span>`, s.name, TONE[s.tone] || '')
    + facts([['Area', s.area], ['Now', s.now], ['Recommended', s.recommended]])
    + `<p class="hint">${esc(s.about)}</p>`;

  const changeCard = (c) => head(`<span class="pic">${svg(KIND_ICON[c.kind] || 'list')}</span>`, c.name, badge(c.what.charAt(0).toUpperCase() + c.what.slice(1), WHAT[c.what]))
    + facts([['When', new Date(c.at).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })], ['Kind', c.kind === 'ou' ? 'OU' : c.kind], ['Where', c.where || ad.state.domain],
      ['By', c.by ? `${c.by}, in Rabbid Lab` : null]])
    + (c.attributes.length ? `<h4>What changed</h4><div class="chips-in">${c.attributes.map((a) => `<span class="gchip">${esc(a)}</span>`).join('')}</div>` : '')
    + `<p class="hint">${esc(c.dn)}</p>`;

  const deletedCard = (d) => head(`<span class="pic">${svg(KIND_ICON[d.kind] || 'list')}</span>`, d.name, badge('Deleted', 'red'))
    + facts([['Logon name', d.username], ['Was in', d.wasIn || ad.state.domain], ['Deleted', d.deleted ? day(d.deleted) : null]])
    + (can('ad.change') ? `<div class="pcard-acts"><button type="button" class="small primary" data-act="restore">${svg('history')}Restore</button></div>` : '')
    + '<p class="hint">It comes back where it was, with its groups and details.</p>';

  const computerCard = (c) => {
    const change = can('ad.change') && !c.controller;
    return head(`<span class="pic">${svg(server(c) ? 'server' : 'laptop')}</span>`, c.name, pcStatus(c) + (c.controller ? chip('Domain controller', 'muted') : ''))
      + facts([['DNS name', c.host], ['Windows', [c.os, c.osVersion].filter(Boolean).join(' ')], ['Where', c.where || ad.state.domain], ['Description', c.description],
        ['Last sign-in', ago(c.lastSignIn)], ['Made', c.created ? day(c.created) : null]])
      + (c.controller ? '<p class="hint">Domain controllers are changed in Active Directory itself.</p>' : '')
      + `<div class="pcard-acts">${change ? `<button type="button" class="small" data-act="power">${svg('power')}${c.enabled ? 'Turn off' : 'Turn on'}</button>` : ''}`
      + `${can('ad.delete') && !c.controller ? `<button type="button" class="small danger" data-act="delete">${svg('trash')}Delete</button>` : ''}</div>`;
  };

  let members = null;
  const openAd = async (i, el) => {
    open = rows[i];
    members = null;
    const card = { users: userCard, groups: (x) => groupCard(x, null), computers: computerCard, domain: settingCard, audit: changeCard, recycle: deletedCard }[adPage];
    $('adcard').innerHTML = card(open);
    place($('adcard'), el, $('page-ad'));
    if (adPage !== 'groups') return;
    const g = open;
    let got = [];
    try {
      got = await R.get(`/api/ad/groups/members?dn=${encodeURIComponent(g.dn)}`);
    } catch (err) {
      if (open === g) say('dir-problem', err.message);
    }
    // A slower answer for a group whose card has closed is dropped.
    if (open !== g) return;
    members = got;
    const list = $('adcard').querySelector('[data-members]');
    if (list) list.innerHTML = memberRows(g, members);
  };
  const adBase = () => (adPage === 'users' ? `/api/ad/users/${encodeURIComponent(open.username)}` : `/api/ad/computers/${encodeURIComponent(open.name)}`);

  // Does it, then reads what changed again and shows the card again (unless it was deleted).
  let busy = false;
  const act = async (path, body, { gone = false, done = null } = {}) => {
    if (busy || !open) return;
    busy = true;
    const was = open;
    const keep = page().key(open);
    say('dir-note', '');
    say('dir-problem', '');
    try {
      const answer = await R.send(path, body);
      if (answer.warning) say('dir-note', answer.warning);
      if (done) done(answer);
    } catch (err) {
      say('dir-problem', err.message);
    } finally {
      busy = false;
    }
    // One user: just that one is read again. Anything else reads the page again.
    let one = false;
    if (adPage === 'users' && !gone && lists.users?.includes(was)) {
      try {
        const u = await R.get(`/api/ad/users/${encodeURIComponent(was.username)}`);
        const at = (lists.users || []).indexOf(was);
        if (at >= 0) {
          lists.users[at] = u;
          counted.list = null;
          one = true;
        }
      } catch {
        // The whole list is read instead.
      }
    }
    if (!one) await loadAd(true).catch((err) => say('dir-problem', err.message));
    drawAd();
    const again = rows.findIndex((x) => page().key(x) === keep);
    const el = document.querySelector(`#dir-table [data-i="${again}"]`);
    if (!gone && again >= 0 && el) openAd(again, el);
    else closeCard();
  };

  $('dir-table').addEventListener('click', (e) => {
    const row = e.target.closest('[data-i]');
    if (row) openAd(Number(row.dataset.i), row);
  });
  $('dir-table').addEventListener('keydown', (e) => {
    const row = e.target.closest('[data-i]');
    if (row && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      openAd(Number(row.dataset.i), row);
    }
  });

  // The card's search box: groups to put a user in, or who to put in a group.
  R.dropdown($('adcard'), {
    none: 'Nothing matches.',
    find: async (q) => {
      const x = open;
      if (!x) return [];
      const found = adPage === 'users'
        ? (await R.get(`/api/ad/groups?q=${encodeURIComponent(q)}`)).filter((g) => !x.groups.some((h) => h.dn === g.dn)).map((g) => ({ dn: g.dn, name: g.name, kind: 'group', where: g.description || g.where }))
        : (await R.get(`/api/ad/find?q=${encodeURIComponent(q)}`)).filter((m) => m.dn !== x.dn && !(members || []).some((h) => h.dn === m.dn));
      return found.slice(0, 30);
    },
    draw: (m, i) => R.option(` data-pick="${i}"`, `<span class="pic">${svg(KIND_PIC[m.kind])}</span>`, m.name, m.username || m.where),
    pick: (m) => {
      if (!open) return;
      if (adPage === 'groups') act('/api/ad/groups/members', { dn: open.dn, add: [m.dn] });
      else act(`${adBase()}/groups`, { add: [m.dn] });
    },
  });
  $('adcard').addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]');
    if (!b || !open) return;
    const x = open;
    const isGroup = adPage === 'groups';
    switch (b.dataset.act) {
      case 'close': closeCard(); break;
      case 'ungroup': act(`${adBase()}/groups`, { remove: [x.groups[Number(b.dataset.g)].dn] }); break;
      case 'unmember': act('/api/ad/groups/members', { dn: x.dn, remove: [members[Number(b.dataset.m)].dn] }); break;
      case 'unlock': act(`${adBase()}/unlock`, {}); break;
      case 'restore': act('/api/ad/recycle/restore', { dn: x.dn }, { gone: true, done: () => say('dir-note', `Restored ${x.name}. It's back in ${x.wasIn || ad.state.domain}.`) }); break;
      case 'power': act(`${adBase()}/enable`, { on: !x.enabled }); break;
      case 'password': reset(x.name || x.username, `${adBase()}/password`, 'It also unlocks the account if it\'s locked out.'); break;
      case 'delete':
        if (isGroup) sure(`Delete ${x.name}?`, `The group is deleted from ${ad.state.domain} for good. Its members stay; they're just not in it any more.`, `Delete ${x.name}`, () => act('/api/ad/groups/delete', { dn: x.dn }, { gone: true }));
        else if (adPage === 'computers') sure(`Delete ${x.name}?`, `Its account is deleted from ${ad.state.domain}. The computer can't sign anyone in with domain accounts until it joins the domain again. Turning it off can be undone; this can't.`, `Delete ${x.name}`, () => act(`${adBase()}/delete`, {}, { gone: true }));
        else sure(`Delete ${x.name || x.username}?`, `${x.username} is deleted from ${ad.state.domain} for good, with anything in it. Turning it off can be undone; this can't.`, `Delete ${x.username}`, () => act(`${adBase()}/delete`, {}, { gone: true }));
        break;
    }
  });

  // --- Local accounts: one computer's Windows accounts --------------------------------------------------------------

  let computers = [];
  let pc = null; // { id, accounts, groups }
  let pcId = null;
  let local = [];
  let openLocal = null;
  const computer = () => computers.find((c) => c.id === pcId);
  const adminsName = () => pc?.groups.find((g) => g.sid === ADMINS)?.name || 'Administrators';
  const isAdmin = (a) => (a.groups || []).includes(adminsName());
  const pcChips = (a) => [
    isAdmin(a) && chip('Administrator', 'ember', 'shield'),
    !a.enabled && chip('Turned off', 'off', 'power'),
    a.locked && chip('Locked out', 'amber', 'lock'),
    a.builtIn && chip('Built in', 'muted'),
    a.neverExpires && chip('Never expires', 'muted'),
  ].filter(Boolean).join('');
  const localBase = (a) => `/api/windows/${encodeURIComponent(pcId)}/accounts/${encodeURIComponent(a.name)}`;

  const drawLocal = () => {
    $('us-computers').innerHTML = computers.map((c) => `<a href="#local/${encodeURIComponent(c.id)}" class="pc"${c.id === pcId ? ' aria-current="page"' : ''}>`
      + `${svg(c.server ? 'server' : 'monitor')}<span class="nm">${esc(c.name)}</span>${c.here ? '<small>This one</small>' : ''}`
      + `<i class="pcdot ${c.ready ? 'on' : c.online ? 'warn' : 'off'}" title="${esc(c.ready ? 'Ready' : c.why || 'Offline')}"></i></a>`).join('')
      || '<p class="side-sub">No computers in Seats yet.</p>';
    const c = computer();
    $('us-pc-title').textContent = c ? c.name : computers.length ? 'Not found' : 'No computers yet';
    $('us-pc-line').textContent = c ? [c.server ? 'This server' : c.os, c.user ? `${c.user} is on it` : ''].filter(Boolean).join(' · ') : '';
    say('us-pc-why', c && !c.ready ? c.why : '');
    $('us-pc-new').disabled = !c?.ready || !pc;
    const find = $('us-find').value.trim().toLowerCase();
    local = pc ? pc.accounts.filter((a) => ($('us-off').checked || a.enabled) && ($('us-system').checked || !a.builtIn)
      && (!find || [a.name, a.fullName, a.description].some((v) => String(v || '').toLowerCase().includes(find)))) : [];
    $('us-pc-rows').innerHTML = !pc ? '' : local.map((a, i) => `<div class="prow urow" tabindex="0" data-i="${i}">`
      + `<div class="who"><span class="pic local">${svg('key')}</span><div><b>${esc(a.name)}</b><span>${esc(a.fullName || a.description || '')}</span></div></div>`
      + `<div class="chips-in">${pcChips(a)}</div><div class="seen">${a.lastSignIn ? `Signed in ${esc(ago(a.lastSignIn).toLowerCase())}` : 'Never signed in'}</div></div>`).join('')
      || '<p class="hint">No accounts to show.</p>';
  };

  const loadLocal = async () => {
    const c = computer();
    pc = null;
    if (!c || !c.ready) return;
    say('us-loading', c.server ? 'Asking Windows…' : `Asking ${c.name}…`);
    try {
      const got = await R.get(`/api/windows/${encodeURIComponent(c.id)}/accounts`);
      pc = { id: c.id, accounts: got.accounts, groups: got.groups };
    } finally {
      say('us-loading', '');
    }
  };

  const localCard = (a) => {
    const change = can('windows.change');
    const others = pc.groups.filter((g) => !(a.groups || []).includes(g.name));
    return head(`<span class="pic local">${svg('key')}</span>`, a.name, pcChips(a) || chip('On', 'ok'))
      + facts([['Full name', a.fullName], ['Description', a.description], ['Last sign-in', ago(a.lastSignIn)]])
      + `<h4>Groups</h4><div class="chips-in">${(a.groups || []).map((g) => `<span class="gchip">${esc(g)}${change ? `<button type="button" class="chip-x" data-act="ungroup" data-g="${esc(g)}" aria-label="Take out of ${esc(g)}">${svg('x')}</button>` : ''}</span>`).join('') || '<span class="muted">None</span>'}`
      + `${change && others.length ? `<select class="gpick" data-act="group" aria-label="Add to a group"><option value="">Add to…</option>${others.map((g) => `<option>${esc(g.name)}</option>`).join('')}</select>` : ''}</div>`
      + `<div class="pcard-acts">${change ? `<button type="button" class="small" data-act="password">${svg('key')}Set a password</button><button type="button" class="small" data-act="power">${svg('power')}${a.enabled ? 'Turn off' : 'Turn on'}</button>` : ''}`
      + `${can('windows.delete') && !a.builtIn ? `<button type="button" class="small danger" data-act="delete">${svg('trash')}Delete</button>` : ''}</div>`;
  };
  const openLocalCard = (i, el) => {
    openLocal = local[i];
    $('ucard').innerHTML = localCard(openLocal);
    place($('ucard'), el, $('page-local'));
  };

  let localBusy = false;
  const actLocal = async (path, body, gone = false) => {
    if (localBusy || !openLocal) return;
    localBusy = true;
    const keep = openLocal.name;
    say('us-note', '');
    say('us-problem', '');
    if (!computer()?.server) say('us-loading', `Asking ${computer()?.name}…`);
    try {
      const answer = await R.send(path, body);
      if (answer.warning) say('us-note', answer.warning);
    } catch (err) {
      say('us-problem', err.message);
    } finally {
      localBusy = false;
      say('us-loading', '');
    }
    await loadLocal().catch((err) => say('us-problem', err.message));
    drawLocal();
    const again = local.findIndex((a) => a.name === keep);
    const el = document.querySelector(`#us-pc-rows [data-i="${again}"]`);
    if (!gone && again >= 0 && el) openLocalCard(again, el);
    else closeCard();
  };

  $('us-pc-rows').addEventListener('click', (e) => {
    const row = e.target.closest('[data-i]');
    if (row) openLocalCard(Number(row.dataset.i), row);
  });
  $('us-pc-rows').addEventListener('keydown', (e) => {
    const row = e.target.closest('[data-i]');
    if (row && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      openLocalCard(Number(row.dataset.i), row);
    }
  });
  $('ucard').addEventListener('change', (e) => {
    if (e.target.dataset.act === 'group' && e.target.value) actLocal(`${localBase(openLocal)}/groups`, { add: [e.target.value] });
  });
  $('ucard').addEventListener('click', (e) => {
    const b = e.target.closest('[data-act]');
    if (!b || !openLocal || b.tagName === 'SELECT') return;
    const a = openLocal;
    switch (b.dataset.act) {
      case 'close': closeCard(); break;
      case 'ungroup': actLocal(`${localBase(a)}/groups`, { remove: [b.dataset.g] }); break;
      case 'power': actLocal(`${localBase(a)}/enable`, { on: !a.enabled }); break;
      case 'password': reset(a.name, `${localBase(a)}/password`, `On ${computer()?.name}. It also unlocks the account if it's locked out.`); break;
      case 'delete': sure(`Delete ${a.name}?`, `${a.name} is deleted from ${computer()?.name} for good. Its files stay in its profile folder.`, `Delete ${a.name}`, () => actLocal(`${localBase(a)}/delete`, {}, true)); break;
    }
  });
  ['us-off', 'us-system'].forEach((id) => $(id).addEventListener('change', () => { closeCard(); drawLocal(); }));
  $('us-find').addEventListener('input', () => { closeCard(); drawLocal(); });

  R.popups('.ucard, [data-i]', closeCard);

  // --- Dialogs: a password, and showing a password once ---------------------------------------------------------------

  const pwMode = (seg, input) => {
    $(seg).addEventListener('click', (e) => {
      const b = e.target.closest('[data-pw]');
      if (!b) return;
      $(seg).querySelectorAll('[data-pw]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
      $(input).hidden = b.dataset.pw !== 'type';
      if (!$(input).hidden) $(input).focus();
    });
  };
  const typed = (seg, input) => ($(seg).querySelector('[aria-pressed="true"]').dataset.pw === 'type' ? $(input).value : '');
  const resetMode = (seg, input) => {
    $(seg).querySelectorAll('[data-pw]').forEach((x) => x.setAttribute('aria-pressed', String(x.dataset.pw === 'make')));
    $(input).hidden = true;
    $(input).value = '';
  };
  pwMode('new-pwmode', 'new-password');
  pwMode('reset-pwmode', 'reset-password');

  const secret = (who, password, warning, mustChange) => {
    $('secret-title').textContent = `${who}'s password`;
    $('secret-lead').textContent = `Give it to ${who}.${mustChange ? ' They pick their own when they first sign in.' : ''}`;
    $('secret-text').textContent = password;
    say('secret-warning', warning || '');
    $('secret-copy').textContent = 'Copy';
    $('secret-dialog').showModal();
  };
  $('secret-copy').addEventListener('click', () => R.copy($('secret-text'), $('secret-copy')));
  $('secret-done').addEventListener('click', () => {
    $('secret-text').textContent = '';
    $('secret-dialog').close();
  });

  // Where the page is now, to load again after a change in a dialog.
  const reload = () => (R.where()[0] === 'local' ? loadLocal().then(drawLocal) : loadAd(true).then(drawAd)).catch(() => {});

  let resetting = null;
  const reset = (who, path, lead) => {
    resetting = { who, path };
    $('reset-title').textContent = `A new password for ${who}`;
    $('reset-lead').textContent = lead;
    resetMode('reset-pwmode', 'reset-password');
    $('reset-change').checked = true;
    say('reset-problem', '');
    $('reset-dialog').showModal();
  };
  $('reset-cancel').addEventListener('click', () => $('reset-dialog').close());
  $('reset-go').addEventListener('click', async () => {
    const password = typed('reset-pwmode', 'reset-password');
    $('reset-go').disabled = true;
    try {
      const answer = await R.send(resetting.path, { password, mustChange: $('reset-change').checked });
      $('reset-dialog').close();
      if (answer.password) secret(resetting.who, answer.password, null, $('reset-change').checked);
      reload();
    } catch (err) {
      say('reset-problem', err.message);
    } finally {
      $('reset-go').disabled = false;
    }
  });

  // --- A new user (AD) or account (one computer) ----------------------------------------------------------------------

  let joining = [];
  let edited = {};
  const newKind = () => $('new-dialog').dataset.kind;
  const drawJoining = () => {
    $('new-dir-groups').innerHTML = joining.map((g, i) => `<span class="gchip">${esc(g.name)}<button type="button" class="chip-x" data-unjoin="${i}" aria-label="Not ${esc(g.name)}">${svg('x')}</button></span>`).join('')
      || '<span class="muted">Only Domain Users</span>';
  };
  const placesInto = async (select, selected, problem) => {
    try {
      places ??= await R.get('/api/ad/places');
    } catch (err) {
      places = [];
      say(problem, err.message);
    }
    $(select).innerHTML = places.map((p) => `<option value="${esc(p.dn)}"${p.dn === selected ? ' selected' : ''}>${esc(p.path || p.name)}</option>`).join('');
  };
  const openNew = async (kind) => {
    const d = $('new-dialog');
    d.dataset.kind = kind;
    ['new-first', 'new-last', 'new-display', 'new-user', 'new-full', 'new-mail', 'new-job', 'new-about', 'new-group-find'].forEach((id) => { $(id).value = ''; });
    edited = {};
    joining = [];
    resetMode('new-pwmode', 'new-password');
    $('new-change').checked = true;
    $('new-never').checked = false;
    $('new-on').checked = true;
    say('new-problem', '');
    d.querySelectorAll('.dir-only').forEach((el) => { el.hidden = kind !== 'ad'; });
    d.querySelectorAll('.pc-only').forEach((el) => { el.hidden = kind !== 'pc'; });
    if (kind === 'ad') {
      $('acct-title').textContent = 'New user';
      $('new-lead').textContent = `In ${ad.state.domain}, made by ${ad.state.account}.`;
      drawJoining();
      await placesInto('new-ou', ad.state.newUsers, 'new-problem');
    } else {
      const c = computer();
      $('acct-title').textContent = `New Windows account on ${c.name}`;
      $('new-lead').textContent = c.server ? 'On this server.' : `${c.name}'s client makes it, straight away.`;
      const common = ['S-1-5-32-544', 'S-1-5-32-545', 'S-1-5-32-555'];
      const groups = [...pc.groups].sort((a, b) => (common.indexOf(b.sid) >= 0) - (common.indexOf(a.sid) >= 0));
      $('new-pc-groups').innerHTML = groups.map((g) => `<label class="role-pick"><input type="checkbox" value="${esc(g.name)}"${g.sid === 'S-1-5-32-545' ? ' checked' : ''}>${esc(g.name)}</label>`).join('');
    }
    d.showModal();
    $(kind === 'ad' ? 'new-first' : 'new-user').focus();
  };
  // Display name and username follow the first and last names, until someone types their own.
  ['new-display', 'new-user'].forEach((id) => $(id).addEventListener('input', () => { edited[id] = true; }));
  ['new-first', 'new-last'].forEach((id) => $(id).addEventListener('input', () => {
    const first = $('new-first').value.trim();
    const last = $('new-last').value.trim();
    if (!edited['new-display']) $('new-display').value = [first, last].filter(Boolean).join(' ');
    if (!edited['new-user']) $('new-user').value = ((first[0] || '') + last).toLowerCase().normalize('NFD').replace(/[^a-z0-9]/g, '').slice(0, 20);
  }));
  $('new-never').addEventListener('change', () => { if ($('new-never').checked) $('new-change').checked = false; });
  $('new-change').addEventListener('change', () => { if ($('new-change').checked) $('new-never').checked = false; });

  // Groups for the new user to join.
  R.dropdown($('new-group-find').closest('.adder'), {
    none: 'No groups match.',
    find: async (q) => (await R.get(`/api/ad/groups?q=${encodeURIComponent(q)}`)).filter((g) => !joining.some((x) => x.dn === g.dn)).slice(0, 30),
    draw: (g, i) => R.option(` data-pick="${i}"`, `<span class="pic">${svg('users')}</span>`, g.name, g.description || g.where),
    pick: (g) => {
      joining.push(g);
      drawJoining();
    },
  });
  $('new-dir-groups').addEventListener('click', (e) => {
    const b = e.target.closest('[data-unjoin]');
    if (!b) return;
    joining.splice(Number(b.dataset.unjoin), 1);
    drawJoining();
  });
  $('new-cancel').addEventListener('click', () => $('new-dialog').close());
  $('new-go').addEventListener('click', async () => {
    const kind = newKind();
    const password = typed('new-pwmode', 'new-password');
    const body = kind === 'ad'
      ? { first: $('new-first').value, last: $('new-last').value, display: $('new-display').value, username: $('new-user').value.trim(), ou: $('new-ou').value,
        email: $('new-mail').value, title: $('new-job').value, description: $('new-about').value, password, mustChange: $('new-change').checked,
        neverExpires: $('new-never').checked, enabled: $('new-on').checked, groups: joining.map((g) => g.dn) }
      : { name: $('new-user').value.trim(), fullName: $('new-full').value, description: $('new-about').value, password, mustChange: $('new-change').checked,
        neverExpires: $('new-never').checked, groups: [...$('new-pc-groups').querySelectorAll('input:checked')].map((c) => c.value) };
    const who = kind === 'ad' ? body.username : body.name;
    $('new-go').disabled = true;
    say('new-problem', kind === 'pc' && !computer()?.server ? `Asking ${computer()?.name}…` : '');
    try {
      const answer = await R.send(kind === 'ad' ? '/api/ad/users' : `/api/windows/${encodeURIComponent(pcId)}/accounts`, body);
      $('new-dialog').close();
      if (answer.password) secret(who, answer.password, answer.warning, body.mustChange);
      else say(kind === 'ad' ? 'dir-note' : 'us-note', answer.warning || `Made ${who}.`);
      reload();
    } catch (err) {
      say('new-problem', err.message);
    } finally {
      $('new-go').disabled = false;
    }
  });
  $('us-pc-new').addEventListener('click', () => openNew('pc'));

  // A new group.
  $('dir-new').addEventListener('click', async () => {
    if (adPage === 'users') {
      openNew('ad');
      return;
    }
    $('grp-name').value = '';
    $('grp-about').value = '';
    $('grp-scope').value = 'global';
    $('grp-type').querySelectorAll('[data-type]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.type === 'security')));
    $('group-lead').textContent = `In ${ad.state.domain}, made by ${ad.state.account}.`;
    say('grp-problem', '');
    await placesInto('grp-ou', ad.state.newUsers, 'grp-problem');
    $('group-dialog').showModal();
    $('grp-name').focus();
  });
  $('grp-type').addEventListener('click', (e) => {
    const b = e.target.closest('[data-type]');
    if (b) $('grp-type').querySelectorAll('[data-type]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
  });
  $('grp-cancel').addEventListener('click', () => $('group-dialog').close());
  $('grp-go').addEventListener('click', async () => {
    $('grp-go').disabled = true;
    try {
      const g = await R.send('/api/ad/groups', { name: $('grp-name').value, description: $('grp-about').value, ou: $('grp-ou').value, scope: $('grp-scope').value,
        security: $('grp-type').querySelector('[aria-pressed="true"]').dataset.type === 'security' });
      $('group-dialog').close();
      say('dir-note', `Made ${g.name}. Open it to add members.`);
      reload();
    } catch (err) {
      say('grp-problem', err.message);
    } finally {
      $('grp-go').disabled = false;
    }
  });

  // --- Setting up Rabbid Lab's AD account --------------------------------------------------------------------------

  let look = null;
  const adsTab = (tab) => {
    $('dset-tabs').querySelectorAll('[data-adtab]').forEach((a) => (a.dataset.adtab === tab ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    $('dset-dialog').dataset.tab = tab;
    const make = tab === 'make';
    $('dset-user-label').textContent = make ? 'Admin username' : 'Service account username';
    $('dset-password-label').textContent = make ? 'Admin password' : 'Service account password';
    $('dset-note').textContent = make
      ? 'A domain admin (or any account allowed to create users and change permissions). These credentials are only used to create the service account and are not saved.'
      : 'Rabbid Lab stores this password encrypted and makes every AD change as this account. It needs permission to create and manage users, groups and computers in the scope you pick next.';
    adsStep(1);
  };
  const adsStep = (n) => {
    $('dset-step1').hidden = n !== 1;
    $('dset-step2').hidden = n !== 2;
    $('dset-back').hidden = n !== 2;
    const make = $('dset-dialog').dataset.tab === 'make';
    document.querySelectorAll('#dset-dialog .dset-make').forEach((el) => { el.hidden = !make; });
    // Nothing is delegated to an account that already exists: the scope is only where Rabbid Lab works.
    document.querySelector('label[for="dset-scope"]').textContent = make ? 'Delegate permissions on' : 'Manage users, groups and computers in';
    $('dset-go').textContent = n === 1 ? 'Next' : make ? 'Create service account' : 'Use this account';
    $('dset-dialog').dataset.step = n;
    say('dset-problem', '');
  };
  // Set it up, from any page that needs Rabbid Lab's AD account.
  const openSetup = (domain) => {
    $('dset-domain').value = domain || ad?.domain || '';
    const me = R.me();
    $('dset-user').value = me?.source === 'ad' ? me.username : '';
    $('dset-password').value = '';
    $('dset-account').value = 'svc-rabbidlab';
    adsTab('make');
    $('dset-dialog').showModal();
    $(me?.source === 'ad' ? 'dset-password' : 'dset-user').focus();
  };
  // The card each Active Directory page shows until the service account is set up (directory.js draws it too).
  // No service account yet: Active Directory's pages wait under a cover, with what to do in the middle of it.
  const setupCard = (el, info) => {
    const allowed = can('ad.connect');
    const local = can('windows.view');
    // Local accounts are made by this server and by each seat's client, as SYSTEM: only Active Directory needs an account.
    const noAccount = ' Windows accounts on this server and on seats need no service account: those are under Local accounts.';
    if (!info?.domain) {
      // No domain here: these pages have nothing to manage until one is connected.
      el.innerHTML = `<div class="dgate-card"><span class="big-ic ember">${svg('key')}</span><div class="ustatus-text"><b>This server isn't on a domain</b><span>`
        + esc(`The Active Directory pages need a domain.${noAccount}${allowed ? ' To manage a domain from here anyway, connect one.' : ''}`)
        + `</span></div><div class="dgate-acts">${local ? '<a class="button primary" href="#local">Local accounts</a>' : ''}${allowed ? '<button type="button" data-setup>Connect a domain</button>' : ''}</div></div>`;
    } else {
      el.innerHTML = `<div class="dgate-card"><span class="big-ic ember">${svg('key')}</span><div class="ustatus-text"><b>Set up the Active Directory service account</b><span>`
        + esc(allowed
          ? `Rabbid Lab makes its Active Directory changes as a dedicated service account (svc-rabbidlab). Create it with a domain admin login for ${info.domain}: Rabbid Lab creates the account, delegates it permissions on users, groups and computers, and stores its password encrypted. The admin login is only used for this and isn't saved.${local ? noAccount : ''}`
          : 'Someone whose roles allow setting up the service account has to do this first.')
        + `</span></div><div class="dgate-acts">${allowed ? '<button type="button" class="primary" data-setup>Set up</button>' : ''}</div></div>`;
    }
    el.hidden = false;
    el.querySelector('[data-setup]')?.addEventListener('click', () => openSetup(info?.domain));
  };
  R.adSetupCard = setupCard;
  $('dset-local').addEventListener('click', () => $('dset-dialog').close());
  $('dset-tabs').addEventListener('click', (e) => {
    const a = e.target.closest('[data-adtab]');
    if (a) adsTab(a.dataset.adtab);
  });
  $('dset-back').addEventListener('click', () => adsStep(1));
  $('dset-cancel').addEventListener('click', () => {
    $('dset-password').value = '';
    $('dset-dialog').close();
  });
  $('dset-go').addEventListener('click', async () => {
    const make = $('dset-dialog').dataset.tab === 'make';
    const sign = { domain: $('dset-domain').value.trim(), username: $('dset-user').value.trim(), password: $('dset-password').value };
    $('dset-go').disabled = true;
    try {
      if ($('dset-dialog').dataset.step === '1') {
        look = await R.send('/api/ad/look', sign);
        const opt = (p, selected) => `<option value="${esc(p.dn)}"${p.dn === selected ? ' selected' : ''}>${esc(p.path || p.name)}</option>`;
        $('dset-ou').innerHTML = look.places.map((p) => opt(p, look.accountOu)).join('');
        $('dset-scope').innerHTML = `<option value="${esc(look.baseDn)}">All of ${esc(look.domain)}</option>` + look.places.filter((p) => !/^CN=/i.test(p.dn)).map((p) => opt(p, '')).join('');
        $('dset-note2').textContent = make
          ? 'The service account gets Full Control over user, group and computer objects in this scope, the same as the Delegation of Control wizard. Admin accounts protected by AdminSDHolder are not affected.'
          : 'Rabbid Lab only makes changes inside this domain or OU. The service account must already have permission to create and manage users, groups and computers there. Rabbid Lab does not change its permissions.';
        adsStep(2);
      } else {
        const answer = await R.send('/api/ad/connect', { ...sign, existing: !make, account: $('dset-account').value.trim(), ou: $('dset-ou').value, scope: $('dset-scope').value });
        $('dset-password').value = '';
        $('dset-dialog').close();
        say('dir-note', answer.warning || (make ? `Created the service account ${answer.state.account}. Rabbid Lab makes its AD changes as it from now on.` : `Rabbid Lab makes its AD changes as ${answer.state.account} from now on.`));
        places = null;
        R.forget();
        refreshAd(true);
        document.dispatchEvent(new CustomEvent('rabbidlab:ad')); // the Org chart and Attribute browser read again
      }
    } catch (err) {
      say('dset-problem', err.message);
    } finally {
      $('dset-go').disabled = false;
    }
  });

  $('dir-settings').addEventListener('click', async () => {
    const s = ad.state;
    $('adset-lead').textContent = `${s.account} in ${s.domain}, ${s.made ? 'with delegated permissions on' : 'managing'} ${s.scopeName}. ${s.made ? 'Created' : 'Connected'} by ${s.by}${s.at ? ` on ${R.when(s.at)}` : ''}.`;
    $('adset-stop-title').textContent = `Disconnect ${s.account}`;
    $('adset-stop-text').textContent = `Rabbid Lab deletes the stored password and stops using ${s.account}.${s.made ? ' The account and its delegated permissions stay in AD until you delete them.' : ''}`;
    say('adset-problem', '');
    await placesInto('adset-ou', s.newUsers, 'adset-problem');
    $('adset-dialog').showModal();
  });
  $('adset-cancel').addEventListener('click', () => $('adset-dialog').close());
  $('adset-save').addEventListener('click', async () => {
    try {
      await R.send('/api/ad/settings', { newUsers: $('adset-ou').value });
      $('adset-dialog').close();
      refreshAd(true);
    } catch (err) {
      say('adset-problem', err.message);
    }
  });
  $('adset-stop').addEventListener('click', () => {
    const s = ad.state;
    $('adset-dialog').close();
    sure(`Disconnect ${s.account}?`, `Rabbid Lab can't make any AD changes until a service account is set up again.${s.made ? ` ${s.account} stays in AD until you delete it.` : ''}`,
      'Disconnect', async () => {
        try {
          await R.send('/api/ad/disconnect', {});
          say('dir-note', s.made ? `Disconnected ${s.account}. Delete it in Active Directory Users and Computers if you won't use it again.` : `Disconnected ${s.account}.`);
        } catch (err) {
          say('dir-problem', err.message);
        }
        places = null;
        R.forget();
        refreshAd(true);
        document.dispatchEvent(new CustomEvent('rabbidlab:ad'));
      });
  });

  // --- Showing the pages -------------------------------------------------------------------------------------------

  const refreshAd = async (fresh) => {
    const [, , rest] = R.where();
    adPage = AD[rest[0]] ? rest[0] : 'users';
    adView = viewOf(rest[1]).key;
    try {
      if (fresh || !ad) {
        ad = await R.get('/api/ad');
        Object.keys(lists).forEach((k) => delete lists[k]);
      }
      await loadAd(false);
    } catch (err) {
      say('dir-problem', err.message);
    }
    drawAd();
  };
  const refreshLocal = async () => {
    const [, , rest] = R.where();
    try {
      computers = await R.get('/api/windows/computers');
      pcId = decodeURIComponent(rest[0] || '') || computers.find((c) => c.here)?.id || computers[0]?.id;
      await loadLocal();
      say('us-problem', '');
    } catch (err) {
      say('us-problem', err.message);
    }
    drawLocal();
  };

  let lastAdPage = null;
  document.addEventListener('rabbidlab:show', (e) => {
    closeCard();
    const [pageName, , rest] = e.detail;
    if (pageName === 'ad') {
      // A new page of Active Directory is read again; another view of the same page draws from what's loaded.
      const fresh = lastAdPage !== rest[0];
      lastAdPage = rest[0];
      say('dir-note', '');
      if (fresh) $('dir-find').value = '';
      refreshAd(fresh);
    } else {
      lastAdPage = null;
      if (pageName === 'local') {
        say('us-note', '');
        refreshLocal();
      }
    }
  });
})();
