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

  // What's ticked on Users, Groups and Computers (by each page's key), kept while the list is read again.
  const picked = { users: new Set(), groups: new Set(), computers: new Set() };
  const pickedHere = () => picked[adPage] || new Set();
  const keyOf = (x) => String(AD[adPage].key(x));
  const byKey = (key) => (lists[adPage] || []).find((x) => keyOf(x) === key);
  const count = (n, one, many) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  // How many are ticked on the Actions button (greyed until there are some), the tick in the heading and each row's
  // highlight, without drawing the list again.
  const drawPicked = () => {
    const sel = pickedHere();
    $('dir-act').disabled = !sel.size;
    $('dir-act').querySelector('.cnt').hidden = !sel.size;
    $('dir-act').querySelector('.cnt').textContent = sel.size.toLocaleString();
    $('dir-act').dataset.tip = sel.size ? `${count(sel.size, ...AD[adPage].noun)} selected` : `Tick ${AD[adPage].noun[1]} first`;
    $('dir-table').querySelectorAll('[data-tick]').forEach((box) => {
      box.checked = sel.has(box.dataset.tick);
      box.closest('.tr')?.classList.toggle('picked', box.checked);
    });
    const all = $('dir-table').querySelector('[data-tick-all]');
    if (all) {
      const shown = rows.filter((x) => sel.has(keyOf(x))).length;
      all.checked = !!rows.length && shown === rows.length;
      all.indeterminate = shown > 0 && shown < rows.length;
    }
  };

  const userStatus = (u) => (u.locked ? badge('Locked out', 'red') : !u.enabled ? badge('Disabled', 'grey')
    : u.passwordExpires && days(u.passwordExpires) < 0 ? badge('Password expired', 'red') : badge('Enabled', 'green'));
  const pcStatus = (c) => (!c.enabled ? badge('Disabled', 'grey') : stale(c.lastSignIn) ? badge('Inactive', 'amber') : badge('Enabled', 'green'));
  const server = (c) => /server/i.test(c.os || '');
  const TONE = { good: badge('As recommended', 'green'), warn: badge('Worth a look', 'amber'), bad: badge('Not as recommended', 'red'), unknown: badge("Couldn't be read", 'grey') };
  const WHAT = { made: 'green', changed: 'grey', deleted: 'red', restored: 'amber' };
  const KIND_ICON = { user: 'user', group: 'users', computer: 'laptop', ou: 'folder', container: 'folder', other: 'folder' };
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
      // People, and the accounts Windows and Active Directory made for themselves, on a tab each.
      tabs: [{ key: 'people', label: 'People', icon: 'user', where: (u) => !u.system }, { key: 'system', label: 'System accounts', icon: 'key', where: (u) => u.system }],
      find: (u) => [u.name, u.username, u.upn, u.email, u.title, u.department, u.where],
      views: [
        ['View', [
          { key: 'all', label: 'All users' },
          { key: 'enabled', label: 'Enabled', where: (u) => u.enabled },
          { key: 'disabled', label: 'Disabled', where: (u) => !u.enabled },
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
      csv: [['Group', (g) => g.name], ['Description', (g) => g.description], ['Group type', (g) => (g.security ? 'Security' : 'Distribution')], ['Group scope', (g) => g.scope],
        ['Members', (g) => g.members], ['Privileged', (g) => g.protected], ['Organizational unit', (g) => g.where], ['Distinguished name', (g) => g.dn]],
    },
    computers: {
      title: 'Computers', noun: ['computer', 'computers'], add: 'New computer', key: (c) => c.name,
      lead: 'Every computer in the domain: servers and workstations, and the ones nobody has used in a while.',
      load: async () => {
        const j = await R.get('/api/ad/computers');
        more.computers = j.more;
        return j.computers;
      },
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
      csv: [['Computer', (c) => c.name], ['DNS host name', (c) => c.host], ['Operating system', (c) => c.os], ['Version', (c) => c.osVersion], ['Description', (c) => c.description],
        ['Organizational unit', (c) => c.where], ['Last sign-in', (c) => c.lastSignIn], ['Created', (c) => c.created], ['Enabled', (c) => c.enabled], ['Domain controller', (c) => c.controller],
        ['Distinguished name', (c) => c.dn]],
    },
    domain: {
      title: 'Domain settings', noun: ['setting', 'settings'], add: null, key: (s) => s.key,
      lead: 'How the domain is set, beside what is recommended.',
      load: () => R.get('/api/ad/domain'),
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
        `<div class="who"><span class="pic">${svg(KIND_ICON[c.kind] || 'folder')}</span>${small(c.name, c.kind === 'ou' ? 'OU' : c.kind)}</div>`,
        esc(c.where || ad.state.domain),
        small(c.attributes.slice(0, 4).join(', ') + (c.attributes.length > 4 ? ` and ${c.attributes.length - 4} more` : ''), c.by ? `by ${c.by}, in ${R.app()}` : ''),
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
      sorts: [['Deleted', (d) => -new Date(d.deleted || 0)], ['Name', (d) => d.name.toLowerCase()]],
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
        `<div class="who"><span class="pic">${svg(KIND_ICON[d.kind] || 'folder')}</span>${small(d.name, d.username)}</div>`,
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
  // A page can split what it lists over tabs (people, system accounts); its views count what's on the open one.
  let adTab = '';
  const tabs = () => page().tabs || [];
  const tab = () => tabs().find((t) => t.key === adTab) || tabs()[0];
  const inView = (v, x) => (!tab() || tab().where(x)) && (!v.where || v.where(x));

  // --- Columns, sorting and the filter, on Users, Groups and Computers --------------------------------------------
  // Each page lists every column it can show; Columns picks which (kept in this browser), the heading sorts by one,
  // and Filter keeps the rows that match its conditions (field, test, value), all of them or any.

  const text = (v) => (v ? `<span class="ell">${esc(v)}</span>` : '<span class="muted">-</span>');
  const dateCell = (iso) => (iso ? esc(day(iso)) : '<span class="muted">Never</span>');
  const endOf = (iso) => (iso ? esc(day(new Date(new Date(iso) - 1))) : '<span class="muted">Never</span>');
  const time = (iso) => (iso ? new Date(iso).getTime() : 0);
  // key, heading, grid width, the value (for sorting, and the cell unless it has its own), and options: on (shown by
  // default), fixed (always shown), cell, sort.
  const col = (key, label, w, get, o = {}) => ({ key, label, w, get, ...o });
  AD.users.cols = [
    col('name', 'Name', 'minmax(200px, 1.5fr)', (u) => u.name || u.username, { fixed: true,
      cell: (u) => `<div class="who"><span class="pic user">${esc(initials(u.name || u.username))}</span><b class="ell">${esc(u.name || u.username)}</b></div>` }),
    col('username', 'Pre-Windows 2000 logon name', 'minmax(215px, 1fr)', (u) => u.username, { on: true, cell: (u) => `<span class="mono">${esc(u.username)}</span>` }),
    col('upn', 'User principal name', 'minmax(200px, 1.2fr)', (u) => u.upn),
    col('email', 'Email', 'minmax(180px, 1.2fr)', (u) => u.email),
    col('department', 'Department', 'minmax(120px, 1fr)', (u) => u.department, { on: true }),
    col('title', 'Job title', 'minmax(120px, 1fr)', (u) => u.title),
    col('company', 'Company', 'minmax(120px, 1fr)', (u) => u.company),
    col('office', 'Office', 'minmax(110px, 1fr)', (u) => u.office),
    col('phone', 'Phone', 'minmax(110px, .8fr)', (u) => u.phone),
    col('manager', 'Manager', 'minmax(130px, 1fr)', (u) => u.manager),
    col('where', 'Organizational unit', 'minmax(140px, 1fr)', (u) => u.where || ad.state.domain),
    col('description', 'Description', 'minmax(160px, 1.4fr)', (u) => u.description),
    col('lastSignIn', 'Last sign-in', '110px', (u) => time(u.lastSignIn), { on: true, cell: (u) => esc(ago(u.lastSignIn)) }),
    col('passwordSet', 'Password last set', '130px', (u) => time(u.passwordSet), { cell: (u) => dateCell(u.passwordSet) }),
    col('passwordExpires', 'Password expires', '130px', (u) => time(u.passwordExpires), { cell: (u) => (u.neverExpires ? '<span class="muted">Never</span>' : dateCell(u.passwordExpires)) }),
    col('accountExpires', 'Account expires', '130px', (u) => time(u.accountExpires), { cell: (u) => endOf(u.accountExpires) }),
    col('created', 'Created', '110px', (u) => time(u.created), { cell: (u) => dateCell(u.created) }),
    col('status', 'Status', '150px', (u) => (u.locked ? 0 : !u.enabled ? 2 : 1), { on: true, cell: (u) => userStatus(u) + (u.service ? chip(R.app(), 'ember') : '') }),
  ];
  AD.groups.cols = [
    col('name', 'Group', 'minmax(220px, 1.8fr)', (g) => g.name, { fixed: true, cell: (g) => `<div class="who"><span class="pic">${svg('users')}</span><b class="ell">${esc(g.name)}</b></div>` }),
    col('type', 'Group type', '150px', (g) => (g.security ? 'Security' : 'Distribution'), { on: true,
      cell: (g) => `${g.security ? 'Security' : 'Distribution'}${g.protected ? ` ${badge('Privileged', 'amber')}` : ''}` }),
    col('scope', 'Group scope', '120px', (g) => g.scope, { on: true }),
    col('members', 'Members', '90px', (g) => g.members, { on: true, cell: (g) => `<span class="num">${g.moreMembers ? '1,500+' : g.members.toLocaleString()}</span>` }),
    col('where', 'Organizational unit', 'minmax(140px, 1fr)', (g) => g.where || ad.state.domain, { on: true }),
    col('description', 'Description', 'minmax(160px, 1.4fr)', (g) => g.description),
    col('email', 'Email', 'minmax(160px, 1fr)', (g) => g.email),
    col('notes', 'Notes', 'minmax(160px, 1.2fr)', (g) => g.notes),
  ];
  AD.computers.cols = [
    col('name', 'Computer', 'minmax(200px, 1.4fr)', (c) => c.name, { fixed: true, cell: (c) => `<div class="who"><span class="pic">${svg(server(c) ? 'server' : 'laptop')}</span><b class="ell">${esc(c.name)}</b></div>` }),
    col('os', 'Operating system', 'minmax(170px, 1.2fr)', (c) => c.os, { on: true, cell: (c) => (c.os ? small(c.os, c.osVersion) : '<span class="muted">-</span>') }),
    col('host', 'DNS host name', 'minmax(170px, 1fr)', (c) => c.host),
    col('where', 'Organizational unit', 'minmax(140px, 1fr)', (c) => c.where || ad.state.domain, { on: true }),
    col('description', 'Description', 'minmax(160px, 1.2fr)', (c) => c.description),
    col('location', 'Location', 'minmax(120px, 1fr)', (c) => c.location),
    col('lastSignIn', 'Last sign-in', '110px', (c) => time(c.lastSignIn), { on: true, cell: (c) => esc(ago(c.lastSignIn)) }),
    col('created', 'Created', '110px', (c) => time(c.created), { cell: (c) => dateCell(c.created) }),
    col('status', 'Status', '150px', (c) => (!c.enabled ? 2 : stale(c.lastSignIn) ? 1 : 0), { on: true, cell: (c) => pcStatus(c) + (c.controller ? chip('Domain controller', 'muted') : '') }),
  ];

  // Which columns each page shows, in the page's own order, and how it's sorted; both kept in this browser.
  const remember = (name, value) => { try { localStorage.setItem(`rabbidlab.ad.${name}`, JSON.stringify(value)); } catch { /* not kept, then */ } };
  const recall = (name) => { try { return JSON.parse(localStorage.getItem(`rabbidlab.ad.${name}`) || 'null'); } catch { return null; } };
  const shownCols = (p = page()) => {
    const keys = recall(`cols.${adPage}`);
    return p.cols.filter((c) => c.fixed || (Array.isArray(keys) ? keys.includes(c.key) : c.on));
  };
  const colSort = () => {
    const s = recall(`sort.${adPage}`);
    return s && page().cols?.some((c) => c.key === s.key) ? s : { key: 'name', dir: 1 };
  };
  const sorted = (list) => {
    const p = page();
    if (!p.cols) return p.sorts ? [...list].sort((a, b) => (p.sorts[0][1](a) < p.sorts[0][1](b) ? -1 : 1)) : list;
    const s = colSort();
    const c = p.cols.find((x) => x.key === s.key);
    const val = (x) => { const v = c.get(x); return typeof v === 'number' ? v : String(v ?? '').toLowerCase(); };
    // Blanks go last either way.
    return [...list].sort((a, b) => {
      const x = val(a);
      const y = val(b);
      if (x === '' || y === '') return x === y ? 0 : x === '' ? 1 : -1;
      return (x < y ? -1 : x > y ? 1 : 0) * s.dir;
    });
  };
  const PAGE_ICON = { users: 'user', groups: 'users', computers: 'laptop', domain: 'globe', audit: 'history', recycle: 'trash' };
  const total = {}; // how many each page had when it was last read, for the list down the left

  // The filter's fields: what each can be asked, by kind. Ops in NO_VALUE need nothing filled in.
  const fld = (key, label, type, get, o = {}) => ({ key, label, type, get, ...o });
  const OU = (x) => x.where || '';
  const IN = (x) => (x.groups || []).map((g) => g.name);
  AD.users.fields = [
    ['Account', [fld('enabled', 'Enabled', 'yes', (u) => u.enabled), fld('locked', 'Locked out', 'yes', (u) => u.locked), fld('protected', 'Privileged', 'yes', (u) => u.protected),
      fld('neverExpires', 'Password never expires', 'yes', (u) => u.neverExpires), fld('mustChange', 'Must change password at next sign-in', 'yes', (u) => u.mustChange),
      fld('lastSignIn', 'Last sign-in', 'days', (u) => u.lastSignIn, { never: true }), fld('passwordSet', 'Password last set', 'days', (u) => u.passwordSet),
      fld('passwordExpires', 'Password expires', 'until', (u) => (u.neverExpires ? null : u.passwordExpires)), fld('accountExpires', 'Account expires', 'until', (u) => u.accountExpires),
      fld('created', 'Created', 'days', (u) => u.created)]],
    ['Identity', [fld('name', 'Name', 'text', (u) => u.name), fld('username', 'Pre-Windows 2000 logon name', 'text', (u) => u.username), fld('upn', 'User principal name', 'text', (u) => u.upn),
      fld('email', 'Email', 'text', (u) => u.email), fld('description', 'Description', 'text', (u) => u.description)]],
    ['Organization', [fld('department', 'Department', 'choice', (u) => u.department), fld('title', 'Job title', 'choice', (u) => u.title), fld('company', 'Company', 'choice', (u) => u.company),
      fld('office', 'Office', 'choice', (u) => u.office), fld('manager', 'Manager', 'choice', (u) => u.manager), fld('where', 'Organizational unit', 'ou', OU)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN)]],
  ];
  AD.groups.fields = [
    ['Group', [fld('name', 'Name', 'text', (g) => g.name), fld('description', 'Description', 'text', (g) => g.description), fld('type', 'Group type', 'choice', (g) => (g.security ? 'Security' : 'Distribution')),
      fld('scope', 'Group scope', 'choice', (g) => g.scope), fld('members', 'Members', 'number', (g) => g.members), fld('protected', 'Privileged', 'yes', (g) => g.protected),
      fld('email', 'Email', 'text', (g) => g.email), fld('where', 'Organizational unit', 'ou', OU)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN)]],
  ];
  AD.computers.fields = [
    ['Computer', [fld('name', 'Name', 'text', (c) => c.name), fld('host', 'DNS host name', 'text', (c) => c.host), fld('description', 'Description', 'text', (c) => c.description),
      fld('os', 'Operating system', 'choice', (c) => c.os), fld('location', 'Location', 'choice', (c) => c.location), fld('where', 'Organizational unit', 'ou', OU)]],
    ['Account', [fld('enabled', 'Enabled', 'yes', (c) => c.enabled), fld('controller', 'Domain controller', 'yes', (c) => c.controller),
      fld('lastSignIn', 'Last sign-in', 'days', (c) => c.lastSignIn, { never: true }), fld('created', 'Created', 'days', (c) => c.created)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN)]],
  ];
  const OPS = {
    text: [['contains', 'contains'], ['notContains', 'does not contain'], ['is', 'is'], ['isNot', 'is not'], ['starts', 'starts with'], ['empty', 'is empty'], ['set', 'is not empty']],
    choice: [['in', 'is any of'], ['notIn', 'is none of'], ['empty', 'is empty'], ['set', 'is not empty']],
    member: [['in', 'is any of'], ['notIn', 'is none of']],
    ou: [['under', 'is in or under'], ['in', 'is directly in']],
    days: [['olderThan', 'more than'], ['within', 'in the last'], ['empty', 'never'], ['set', 'is set']],
    until: [['next', 'in the next'], ['past', 'has passed'], ['empty', 'never'], ['set', 'is set']],
    number: [['more', 'more than'], ['fewer', 'fewer than'], ['eq', 'exactly']],
    yes: [['is', 'is']],
  };
  const NO_VALUE = ['empty', 'set', 'past'];
  const fieldOf = (pageKey, key) => (AD[pageKey].fields || []).flatMap(([, items]) => items).find((f) => f.key === key);
  const listOf = (v) => (Array.isArray(v) ? v : v === undefined || v === null || v === '' ? [] : [v]);
  const lc = (s) => String(s ?? '').toLowerCase();
  const dayDiff = (iso) => (Date.now() - new Date(iso)) / DAY; // days ago (negative: to come)

  // What's filtered on each page, until it's cleared or the page is read again in a new tab.
  const filterOn = {};
  // A test for one row from a condition; one not filled in yet lets everything through.
  const ruleTest = (pageKey, r) => {
    const f = fieldOf(pageKey, r.field);
    if (!f) return () => true;
    const values = listOf(r.value);
    const one = values[0];
    if (!NO_VALUE.includes(r.op) && f.type !== 'yes' && (one === undefined || one === '')) return () => true;
    const n = Number(one);
    switch (f.type) {
      case 'text': {
        const want = lc(one);
        return { empty: (x) => !f.get(x), set: (x) => !!f.get(x), is: (x) => lc(f.get(x)) === want, isNot: (x) => lc(f.get(x)) !== want,
          starts: (x) => lc(f.get(x)).startsWith(want), notContains: (x) => !lc(f.get(x)).includes(want) }[r.op] || ((x) => lc(f.get(x)).includes(want));
      }
      case 'choice': {
        const want = new Set(values.map(lc));
        return { empty: (x) => !f.get(x), set: (x) => !!f.get(x), notIn: (x) => !want.has(lc(f.get(x))) }[r.op] || ((x) => want.has(lc(f.get(x))));
      }
      case 'member': {
        const want = new Set(values.map(lc));
        const has = (x) => f.get(x).some((g) => want.has(lc(g)));
        return r.op === 'notIn' ? (x) => !has(x) : has;
      }
      case 'ou': return r.op === 'in' ? (x) => values.some((p) => same(f.get(x), p)) : (x) => values.some((p) => same(f.get(x), p) || lc(f.get(x)).startsWith(`${lc(p)} / `));
      case 'days':
        if (r.op === 'empty') return (x) => !f.get(x);
        if (r.op === 'set') return (x) => !!f.get(x);
        if (r.op === 'within') return (x) => !!f.get(x) && dayDiff(f.get(x)) <= n;
        return (x) => (f.get(x) ? dayDiff(f.get(x)) > n : !!f.never);
      case 'until':
        if (r.op === 'empty') return (x) => !f.get(x);
        if (r.op === 'set') return (x) => !!f.get(x);
        if (r.op === 'past') return (x) => !!f.get(x) && dayDiff(f.get(x)) > 0;
        return (x) => !!f.get(x) && dayDiff(f.get(x)) <= 0 && -dayDiff(f.get(x)) <= n;
      case 'number': return r.op === 'more' ? (x) => f.get(x) > n : r.op === 'fewer' ? (x) => f.get(x) < n : (x) => f.get(x) === n;
      case 'yes': return (x) => !!f.get(x) === (one !== false && one !== 'false');
      default: return () => true;
    }
  };
  const ruleFilled = (pageKey, r) => { const f = fieldOf(pageKey, r.field); return !!f && (f.type === 'yes' || NO_VALUE.includes(r.op) || listOf(r.value).some((v) => v !== '')); };
  const filterCount = (pageKey = adPage) => (filterOn[pageKey]?.rules || []).filter((r) => ruleFilled(pageKey, r)).length;
  const filterTest = (pageKey, flt) => {
    const tests = (flt?.rules || []).filter((r) => ruleFilled(pageKey, r)).map((r) => ruleTest(pageKey, r));
    if (!tests.length) return () => true;
    return flt.join === 'any' ? (x) => tests.some((t) => t(x)) : (x) => tests.every((t) => t(x));
  };
  const filtering = () => filterCount() > 0;
  // A filter in words, for the line over the list.
  const filterWords = (pageKey, flt) => (flt?.rules || []).filter((r) => ruleFilled(pageKey, r)).map((r) => {
    const f = fieldOf(pageKey, r.field);
    const op = (OPS[f.type].find(([k]) => k === r.op) || ['', ''])[1];
    const v = listOf(r.value);
    if (f.type === 'yes') return `${f.label}: ${v[0] === false || v[0] === 'false' ? 'no' : 'yes'}`;
    if (NO_VALUE.includes(r.op)) return `${f.label} ${op}`;
    if (f.type === 'days') return `${f.label} ${op} ${v[0]} days${r.op === 'olderThan' ? ' ago' : ''}`;
    if (f.type === 'until') return `${f.label} ${op} ${v[0]} days`;
    return `${f.label} ${op} ${v.join(', ')}`;
  }).join(flt.join === 'any' ? ' or ' : '; ');

  // How many are in each view, counted again only when the list or the tab changes.
  let counted = { list: null, page: '', tab: '', n: {} };
  const counts = () => {
    const list = lists[adPage] || [];
    if (counted.list !== list || counted.page !== adPage || counted.tab !== adTab) {
      const n = {};
      allViews().forEach((v) => { n[v.key] = list.filter((x) => inView(v, x)).length; });
      counted = { list, page: adPage, tab: adTab, n };
    }
    return counted.n;
  };
  // The open page's views, tucked under its entry in the list down the left.
  const drawViews = () => {
    const count = counts();
    $('dir-views').innerHTML = page().views.map(([, items]) => items.map((v) => {
      const n = count[v.key];
      if (v.quiet && !n && v.key !== adView) return '';
      return `<a href="#ad/${adPage}/${v.key}"${v.key === adView ? ' aria-current="page"' : ''}><span class="nm">${esc(v.label)}</span>`
        + `<small class="${n && v.tone ? `tone ${v.tone}` : ''}">${n.toLocaleString()}</small></a>`;
    }).join('')).filter(Boolean).join('<i></i>');
    document.querySelector(`#dir-rail a[data-adpage="${adPage}"]`)?.after($('dir-views'));
    document.querySelectorAll('#dir-rail [data-n]').forEach((el) => { el.textContent = total[el.dataset.n] === undefined ? '' : total[el.dataset.n].toLocaleString(); });
  };
  const drawAd = () => {
    const p = page();
    const s = ad?.state;
    $('dir-lead').textContent = p.lead;
    $('dir-setup').hidden = !!s;
    $('dir-lib').hidden = !s;
    // Find, the views and the filters show in the list down the left while a page that lists things is open.
    $('dir-rail').classList.toggle('lists', !!s);
    $('dir-acct-row').hidden = !s;
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
    if (lists[adPage]) total[adPage] = (p.tabs ? list.filter(p.tabs[0].where) : list).length;
    const test = filterTest(adPage, filterOn[adPage]);
    rows = sorted(list.filter((x) => inView(v, x) && test(x) && (!find || p.find(x).some((t) => String(t || '').toLowerCase().includes(find)))));
    // The tabs, or the page's own name where it has none.
    const openTab = tab();
    $('dir-tabs').innerHTML = (p.tabs || [{ key: '', label: p.title, icon: PAGE_ICON[adPage], where: () => true }])
      .map((t) => `<a data-tab="${t.key}"${!openTab || t.key === openTab.key ? ' aria-current="true"' : ''}>${svg(t.icon)}${esc(t.label)} <small>${list.filter(t.where).length.toLocaleString()}</small></a>`).join('');
    // Filter, Columns and Actions are for the pages that list users, groups and computers.
    const picks = !!picked[adPage];
    ['dir-filter', 'dir-cols', 'dir-act'].forEach((id) => { $(id).hidden = !picks; });
    const nf = filterCount();
    $('dir-filter').classList.toggle('on', nf > 0);
    $('dir-filter').querySelector('.cnt').hidden = !nf;
    $('dir-filter').querySelector('.cnt').textContent = String(nf);
    // What's open says so over the list once it's narrower than everything.
    $('dir-now').hidden = !(adView !== allViews()[0].key || find || filtering());
    $('dir-view-title').textContent = v.label;
    $('dir-count').textContent = `${rows.length.toLocaleString()} ${p.noun[rows.length === 1 ? 0 : 1]}`;
    $('dir-filtered').hidden = !filtering();
    $('dir-filtered').innerHTML = filtering() ? `${svg('filter')}<span>${esc(filterWords(adPage, filterOn[adPage]))}</span><button type="button" class="linkish" data-filter-clear>Clear filter</button>` : '';
    if (picks && lists[adPage]) [...picked[adPage]].forEach((k) => { if (!byKey(k)) picked[adPage].delete(k); });
    $('dir-table').className = `table adt ${adPage} drows${picks ? ` picks ${density()}` : ''}`;
    const none = `<div class="empty">${list.length ? (filtering() ? 'Nothing matches the filter.' : 'Nothing here right now.') : 'Nothing yet.'}</div>`;
    if (!picks) {
      $('dir-table').innerHTML = !rows.length ? none : `<div class="tr th">${p.head.map((h) => `<span>${esc(h)}</span>`).join('')}</div>`
        + rows.map((x, i) => `<div class="tr" tabindex="0" data-i="${i}">${p.cells(x).map((c) => `<div>${c}</div>`).join('')}</div>`).join('');
    } else {
      // The chosen columns, each heading sorts by itself and its right edge drags to resize it.
      const cols = shownCols(p);
      const s = colSort();
      setTracks(cols);
      $('dir-table').innerHTML = `<div class="tr th"><label class="tk"><input type="checkbox" data-tick-all aria-label="Select all"></label>`
        + cols.map((c) => `<div class="th-cell"><button type="button" class="th-sort${c.key === s.key ? ' on' : ''}" data-sort="${c.key}" title="${esc(c.label)}"><span class="lbl">${esc(c.label)}</span>`
          + `${c.key === s.key ? svg(s.dir > 0 ? 'chevron-up' : 'chevron-down', 'arr') : ''}</button><i class="th-grip" data-grip="${c.key}" title="Drag to resize; double-click for the default width"></i></div>`).join('')
        + '<span></span></div>'
        + (!rows.length ? none : rows.map((x, i) => `<div class="tr" tabindex="0" data-i="${i}">`
          + `<label class="tk"><input type="checkbox" data-tick="${esc(keyOf(x))}" aria-label="Select ${esc(x.name || x.username)}"></label>`
          + cols.map((c) => `<div>${c.cell ? c.cell(x) : text(c.get(x))}</div>`).join('')
          + `<div class="tr-more"><button type="button" class="at-more" data-row-menu="${i}" data-tip="Actions" aria-label="Actions for ${esc(x.name || x.username)}">${svg('dots')}</button></div>`
          + '</div>').join(''));
    }
    $('dir-more').hidden = !more[adPage];
    drawPicked();
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
      html = `${svg('history')}<span>Who made a change shows when it was made in ${esc(R.app())}. For changes made elsewhere, the domain controllers' security logs say who.</span>`;
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
      R.skeleton($('dir-table'), 'rows', 8); // until the list is read
      try {
        lists[adPage] = await page().load();
        say('dir-problem', '');
      } finally {
        R.skeleton($('dir-table'), '');
      }
    }
  };

  // Find draws once typing pauses: the list can be 1,000 long.
  let adFindTimer = null;
  $('dir-find').addEventListener('input', () => {
    clearTimeout(adFindTimer);
    adFindTimer = setTimeout(drawAd, 150);
  });
  // The tabs, the sort, rows or tiles, and the filters: each draws the list again from what's loaded.
  $('dir-tabs').addEventListener('click', (e) => {
    const t = e.target.closest('[data-tab]');
    if (!t || !tabs().length || t.dataset.tab === tab().key) return;
    closeCard();
    adTab = t.dataset.tab;
    drawAd();
  });
  // The grid's tracks: the tick, each column at a width in pixels (as data grids do: its own default, or what it was
  // dragged to), and ⋯ taking what's left. Text that doesn't fit ends in …; a wider list scrolls sideways.
  const widths = () => recall(`widths.${adPage}`) || {};
  const minOf = (w) => Number((String(w).match(/(\d+)px/) || [0, 80])[1]);
  const DEFAULT_PX = { name: 210, username: 190, upn: 230, email: 210, department: 150, title: 150, company: 140, office: 120, phone: 120, manager: 160,
    where: 180, description: 240, lastSignIn: 110, passwordSet: 130, passwordExpires: 130, accountExpires: 130, created: 110, status: 160, type: 160, scope: 120,
    members: 90, notes: 200, os: 210, host: 200, location: 140 };
  const widthOf = (c, wd = widths()) => wd[c.key] || DEFAULT_PX[c.key] || minOf(c.w);
  const setTracks = (cols) => {
    const wd = widths();
    const min = 18 + 28 + cols.reduce((sum, c) => sum + widthOf(c, wd), 0) + 12 * (cols.length + 1) + 32;
    $('dir-table').style.setProperty('--cols', `18px ${cols.map((c) => `${widthOf(c, wd)}px`).join(' ')} minmax(28px, 1fr)`);
    $('dir-table').style.setProperty('--minw', `${min}px`);
  };
  // Compact rows (the default) or relaxed ones, kept in this browser for every list.
  const density = () => (recall('density') === 'relaxed' ? 'relaxed' : 'compact');

  // A heading sorts by its column; again turns it round.
  $('dir-table').addEventListener('click', (e) => {
    const b = e.target.closest('[data-sort]');
    if (!b) return;
    const s = colSort();
    remember(`sort.${adPage}`, { key: b.dataset.sort, dir: s.key === b.dataset.sort ? -s.dir : 1 });
    closeCard();
    drawAd();
  });
  // A heading's right edge drags to make its column wider or narrower; a double-click puts it back.
  $('dir-table').addEventListener('pointerdown', (e) => {
    const grip = e.target.closest('[data-grip]');
    if (!grip) return;
    e.preventDefault();
    const key = grip.dataset.grip;
    const cell = grip.closest('.th-cell');
    const start = e.clientX;
    const from = cell.getBoundingClientRect().width;
    const cols = shownCols();
    const wd = widths();
    document.body.classList.add('col-dragging');
    const move = (m) => {
      wd[key] = Math.max(60, Math.round(from + m.clientX - start));
      remember(`widths.${adPage}`, wd);
      setTracks(cols);
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      document.body.classList.remove('col-dragging');
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, { once: true });
  });
  // A double-click on the edge fits the column to what's in it: each row is measured at its natural width.
  $('dir-table').addEventListener('dblclick', (e) => {
    const grip = e.target.closest('[data-grip]');
    if (!grip) return;
    const cols = shownCols();
    const at = cols.findIndex((c) => c.key === grip.dataset.grip);
    if (at < 0) return;
    const t = $('dir-table');
    const wd = widths();
    const tracks = cols.map((c, i) => (i === at ? 'max-content' : `${widthOf(c, wd)}px`));
    t.classList.add('measuring');
    t.style.setProperty('--cols', `18px ${tracks.join(' ')} minmax(28px, 1fr)`);
    const widest = Math.max(...[...t.querySelectorAll(`.tr > :nth-child(${at + 2})`)].map((cell) => cell.getBoundingClientRect().width));
    t.classList.remove('measuring');
    wd[grip.dataset.grip] = Math.min(600, Math.max(60, Math.ceil(widest) + 4));
    remember(`widths.${adPage}`, wd);
    setTracks(cols);
  });

  // Columns: tick the ones to show; Reset puts back the page's own columns, widths and order.
  const placeUnder = (menu, button) => {
    const r = button.getBoundingClientRect();
    menu.style.setProperty('left', '0px');
    menu.style.setProperty('top', '0px');
    menu.showPopover();
    menu.style.setProperty('left', `${Math.max(8, Math.min(r.right - menu.offsetWidth, innerWidth - menu.offsetWidth - 8))}px`);
    menu.style.setProperty('top', `${r.bottom + 6}px`);
  };
  const drawColMenu = () => {
    const shown = shownCols().map((c) => c.key);
    $('col-menu').innerHTML = `<div class="col-dens"><span>Rows</span><div class="seg">${[['compact', 'Compact'], ['relaxed', 'Relaxed']].map(([d, label]) => `<button type="button" data-density="${d}" aria-pressed="${density() === d}">${label}</button>`).join('')}</div></div>`
      + `<p class="cmd-head">Columns</p><div class="am-links col-list">${page().cols.filter((c) => !c.fixed)
      .map((c) => `<label class="am-link col-check"><input type="checkbox" data-col="${c.key}"${shown.includes(c.key) ? ' checked' : ''}><span>${esc(c.label)}</span></label>`).join('')}</div>`
      + `<div class="am-links"><button type="button" class="am-link" data-col-reset>${svg('refresh')}<span>Reset columns, widths and sort</span></button></div>`;
  };
  $('dir-cols').addEventListener('click', () => {
    if ($('col-menu').matches(':popover-open')) return;
    drawColMenu();
    placeUnder($('col-menu'), $('dir-cols'));
  });
  $('col-menu').addEventListener('change', (e) => {
    const box = e.target.closest('[data-col]');
    if (!box) return;
    const keys = shownCols().filter((c) => !c.fixed).map((c) => c.key).filter((k) => k !== box.dataset.col);
    if (box.checked) keys.push(box.dataset.col);
    remember(`cols.${adPage}`, keys);
    drawAd();
  });
  $('col-menu').addEventListener('click', (e) => {
    const d = e.target.closest('[data-density]');
    if (d) {
      remember('density', d.dataset.density);
      drawColMenu();
      drawAd();
      return;
    }
    if (!e.target.closest('[data-col-reset]')) return;
    ['cols', 'widths', 'sort'].forEach((n) => { try { localStorage.removeItem(`rabbidlab.ad.${n}.${adPage}`); } catch { /* nothing kept */ } });
    drawColMenu();
    drawAd();
  });

  // Actions: the menu for what's ticked, the same one as a row's ⋯.
  $('dir-act').addEventListener('click', () => {
    const r = $('dir-act').getBoundingClientRect();
    openCmd(selectedObjs(), r.right - CMD_W, r.bottom + 6);
  });

  // --- Filter: conditions, each a field, a test and a value; all of them or any ----------------------------------
  let draft = null; // { page, join, rules } while the window is open
  const firstField = (pageKey) => AD[pageKey].fields[0][1][0];
  const blankRule = (pageKey) => { const f = firstField(pageKey); return { field: f.key, op: OPS[f.type][0][0], value: f.type === 'yes' ? true : '' }; };
  // What a choice can be: the values the list holds, for the box's suggestions.
  const choicesFor = (pageKey, f) => {
    const all = (lists[pageKey] || []).flatMap((x) => listOf(f.get(x))).filter(Boolean).map(String);
    return [...new Set(all)].sort((a, b) => a.localeCompare(b));
  };
  const valueHtml = (r, f, i) => {
    const v = listOf(r.value);
    if (f.type === 'yes') return `<select data-r-val="${i}" aria-label="Value"><option value="true"${v[0] === false || v[0] === 'false' ? '' : ' selected'}>Yes</option><option value="false"${v[0] === false || v[0] === 'false' ? ' selected' : ''}>No</option></select>`;
    if (NO_VALUE.includes(r.op)) return '<span class="flt-none"></span>';
    if (f.type === 'days' || f.type === 'until' || f.type === 'number') {
      const after = f.type === 'number' ? '' : f.type === 'days' && r.op === 'olderThan' ? `days ago${f.never ? ', or never' : ''}` : 'days';
      return `<span class="flt-num"><input type="number" min="0" max="3650" data-r-val="${i}" value="${esc(v[0] ?? '')}" aria-label="Number">${after ? `<span>${after}</span>` : ''}</span>`;
    }
    if (f.type === 'text') return `<input type="text" data-r-val="${i}" value="${esc(v[0] ?? '')}" aria-label="Value" placeholder="Text" autocomplete="off" spellcheck="false">`;
    // Choices, OUs and groups: picked values as chips, and a box to add more from what the list holds.
    return `<span class="flt-tokens">${v.map((x, j) => `<span class="gchip">${esc(x)}<button type="button" class="chip-x" data-r-unpick="${i}" data-j="${j}" aria-label="Remove ${esc(x)}">${svg('x')}</button></span>`).join('')}`
      + `<input type="text" list="flt-dl-${i}" data-r-pick="${i}" placeholder="${v.length ? 'Add another' : 'Type or pick'}" autocomplete="off" spellcheck="false" aria-label="Add a value">`
      + `<datalist id="flt-dl-${i}">${choicesFor(draft.page, f).filter((c) => !v.includes(c)).map((c) => `<option value="${esc(c)}">`).join('')}</datalist></span>`;
  };
  const drawDraft = () => {
    const p = AD[draft.page];
    $('flt-join').value = draft.join;
    $('flt-rules').innerHTML = draft.rules.map((r, i) => {
      const f = fieldOf(draft.page, r.field) || firstField(draft.page);
      return `<div class="flt-row"><select data-r-field="${i}" aria-label="Field">${p.fields.map(([group, items]) => `<optgroup label="${esc(group)}">`
          + `${items.map((x) => `<option value="${x.key}"${x.key === f.key ? ' selected' : ''}>${esc(x.label)}</option>`).join('')}</optgroup>`).join('')}</select>`
        + `<select data-r-op="${i}" aria-label="Test"${OPS[f.type].length < 2 ? ' disabled' : ''}>${OPS[f.type].map(([k, label]) => `<option value="${k}"${k === r.op ? ' selected' : ''}>${esc(label)}</option>`).join('')}</select>`
        + `<div class="flt-val">${valueHtml(r, f, i)}</div>`
        + `<button type="button" class="at-more flt-x" data-r-del="${i}" data-tip="Remove" aria-label="Remove this condition">${svg('x')}</button></div>`;
    }).join('') || '<p class="muted flt-empty">No conditions. Add one to narrow the list.</p>';
    drawDraftCount();
  };
  // How many match as the conditions stand, updated as they're typed.
  const drawDraftCount = () => {
    const p = AD[draft.page];
    const v = viewOf(adView);
    const base = (lists[draft.page] || []).filter((x) => inView(v, x));
    const n = base.filter(filterTest(draft.page, draft)).length;
    $('flt-count').textContent = `${n.toLocaleString()} of ${base.length.toLocaleString()} ${p.noun[1]} match`;
  };
  $('dir-filter').addEventListener('click', () => {
    const now = filterOn[adPage];
    draft = { page: adPage, join: now?.join || 'all', rules: now?.rules?.length ? JSON.parse(JSON.stringify(now.rules)) : [blankRule(adPage)] };
    $('flt-title').textContent = `Filter ${AD[adPage].noun[1]}`;
    drawDraft();
    $('flt-dialog').showModal();
    $('flt-rules').querySelector('[data-r-val], [data-r-pick]')?.focus();
  });
  const addPick = (i, value) => {
    const r = draft.rules[i];
    const v = String(value || '').trim();
    if (!v || listOf(r.value).some((x) => same(x, v))) return;
    r.value = [...listOf(r.value), v];
    drawDraft();
    $('flt-rules').querySelector(`[data-r-pick="${i}"]`)?.focus();
  };
  $('flt-rules').addEventListener('change', (e) => {
    const t = e.target;
    if (t.dataset.rField !== undefined) {
      const f = fieldOf(draft.page, t.value);
      draft.rules[Number(t.dataset.rField)] = { field: f.key, op: OPS[f.type][0][0], value: f.type === 'yes' ? true : '' };
      drawDraft();
    } else if (t.dataset.rOp !== undefined) {
      const r = draft.rules[Number(t.dataset.rOp)];
      r.op = t.value;
      drawDraft();
    } else if (t.dataset.rVal !== undefined) {
      draft.rules[Number(t.dataset.rVal)].value = t.tagName === 'SELECT' ? t.value === 'true' : t.value;
      drawDraftCount();
    } else if (t.dataset.rPick !== undefined) {
      addPick(Number(t.dataset.rPick), t.value);
    }
  });
  $('flt-rules').addEventListener('input', (e) => {
    const t = e.target;
    if (t.dataset.rVal !== undefined && t.tagName === 'INPUT') {
      draft.rules[Number(t.dataset.rVal)].value = t.value;
      drawDraftCount();
    }
    // A suggestion picked from the list adds itself straight away.
    if (t.dataset.rPick !== undefined && [...(t.list?.options || [])].some((o) => o.value === t.value)) addPick(Number(t.dataset.rPick), t.value);
  });
  $('flt-rules').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    const t = e.target;
    e.preventDefault();
    if (t.dataset.rPick !== undefined) addPick(Number(t.dataset.rPick), t.value);
    else $('flt-apply').click();
  });
  $('flt-rules').addEventListener('click', (e) => {
    const del = e.target.closest('[data-r-del]');
    if (del) {
      draft.rules.splice(Number(del.dataset.rDel), 1);
      drawDraft();
      return;
    }
    const un = e.target.closest('[data-r-unpick]');
    if (un) {
      const r = draft.rules[Number(un.dataset.rUnpick)];
      r.value = listOf(r.value).filter((_, j) => j !== Number(un.dataset.j));
      drawDraft();
    }
  });
  $('flt-join').addEventListener('change', () => { draft.join = $('flt-join').value; drawDraftCount(); });
  $('flt-add').addEventListener('click', () => {
    draft.rules.push(blankRule(draft.page));
    drawDraft();
    $('flt-rules').querySelectorAll('[data-r-field]')[draft.rules.length - 1]?.focus();
  });
  $('flt-cancel').addEventListener('click', () => $('flt-dialog').close());
  $('flt-clear').addEventListener('click', () => {
    draft.rules = [];
    drawDraft();
  });
  $('flt-apply').addEventListener('click', () => {
    const kept = draft.rules.filter((r) => ruleFilled(draft.page, r));
    if (kept.length) filterOn[draft.page] = { join: draft.join, rules: kept };
    else delete filterOn[draft.page];
    $('flt-dialog').close();
    closeCard();
    drawAd();
  });
  // Clear filter, on the line over the list.
  $('dir-now').addEventListener('click', (e) => {
    if (!e.target.closest('[data-filter-clear]')) return;
    delete filterOn[adPage];
    drawAd();
  });
  // The page's columns for each row given, as a CSV download.
  const csvOf = (list, name) => {
    const p = page();
    const cell = (v) => {
      // A leading quote keeps Excel from running a value that starts like a formula.
      const s = (v === null || v === undefined ? '' : String(v)).replace(/^[=+\-@\t\r]/, "'$&");
      return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
    };
    const text = [p.csv.map(([h]) => cell(h)).join(','), ...list.map((x) => p.csv.map(([, f]) => cell(f(x))).join(','))].join('\r\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([`﻿${text}`], { type: 'text/csv' }));
    a.download = `${ad.state.domain}-${name}.csv`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  };
  $('dir-export').addEventListener('click', () => csvOf(rows, `${adPage}-${adView}`));

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
    const fixed = u.service ? `That's ${ad.state.account}, ${R.app()}'s own account. It looks after it itself.`
      : u.protected ? `Active Directory protects the accounts of admins, so ${R.app()}'s account can't change this one. Use Active Directory Users and Computers for it.`
      : u.system ? 'A built-in account. Change it in Active Directory Users and Computers.' : '';
    const change = can('ad.change') && !u.service && !u.protected && !u.system;
    return head(`<span class="pic user">${esc(initials(u.name || u.username))}</span>`, u.name || u.username, userStatus(u))
      + facts([['Logon name', u.username], ['Principal name', u.upn], ['Where', u.where || ad.state.domain], ['Email', u.email], ['Department', u.department], ['Job title', u.title],
        ['Manager', u.manager], ['Description', u.description], ['Last sign-in', ago(u.lastSignIn)],
        ['Password', u.mustChange ? 'Changes at the next sign-in' : u.neverExpires ? 'Never expires' : u.passwordExpires ? `Expires ${day(u.passwordExpires)}` : null],
        ['Account expires', u.accountExpires ? `End of ${day(new Date(new Date(u.accountExpires) - 1))}` : null], ['Made', u.created ? day(u.created) : null]])
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

  const changeCard = (c) => head(`<span class="pic">${svg(KIND_ICON[c.kind] || 'folder')}</span>`, c.name, badge(c.what.charAt(0).toUpperCase() + c.what.slice(1), WHAT[c.what]))
    + facts([['When', new Date(c.at).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })], ['Kind', c.kind === 'ou' ? 'OU' : c.kind], ['Where', c.where || ad.state.domain],
      ['By', c.by ? `${c.by}, in ${R.app()}` : null]])
    + (c.attributes.length ? `<h4>What changed</h4><div class="chips-in">${c.attributes.map((a) => `<span class="gchip">${esc(a)}</span>`).join('')}</div>` : '')
    + `<p class="hint">${esc(c.dn)}</p>`;

  const deletedCard = (d) => head(`<span class="pic">${svg(KIND_ICON[d.kind] || 'folder')}</span>`, d.name, badge('Deleted', 'red'))
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
    if (e.target.closest('.tk')) {
      tickRange(e);
      return;
    }
    const dots = e.target.closest('[data-row-menu]');
    if (dots) {
      const r = dots.getBoundingClientRect();
      openCmd(menuObjs(rows[Number(dots.dataset.rowMenu)]), r.right - CMD_W, r.bottom + 6);
      return;
    }
    const row = e.target.closest('[data-i]');
    if (row) openRow(Number(row.dataset.i), row);
  });
  // Users, groups and computers open in the side panel; the other pages' rows in their card.
  const openRow = (i, el) => (picked[adPage] ? openDrawer(adPage, rows[i]) : openAd(i, el));
  $('dir-table').addEventListener('keydown', (e) => {
    const row = e.target.closest('[data-i]');
    if (row && e.target === row && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      openRow(Number(row.dataset.i), row);
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

  // --- Users, groups and computers: ticked ones, and one menu of what can be done to them ---------------------------
  // A row's ⋯ or a right-click opens it for that row, or for every selected row when it's one of them; Actions over the
  // list opens it for the selected ones. Its categories open to the side. Changes go one at a time, and what this can't
  // change is left out: protected and built-in accounts, built-in groups and domain controllers.

  const CMD_W = 250;
  const nameOf = (x) => x.name || x.username;
  const mayChange = (k, x) => (k === 'users' ? !x.service && !x.protected && !x.system : k === 'groups' ? !x.system : !x.controller);
  const whoOf = (xs, k = adPage) => (xs.length === 1 ? nameOf(xs[0]) : count(xs.length, ...AD[k].noun));
  const userPath = (u) => `/api/ad/users/${encodeURIComponent(u.username)}`;
  const selectedObjs = () => [...pickedHere()].map(byKey).filter(Boolean);
  const menuObjs = (x) => (x && pickedHere().has(keyOf(x)) && pickedHere().size > 1 ? selectedObjs() : [x].filter(Boolean));

  // Where each change goes, for each kind.
  const pcPath = (x) => `/api/ad/computers/${encodeURIComponent(x.name)}`;
  const enableOf = (k, x, on) => [k === 'users' ? `${userPath(x)}/enable` : `${pcPath(x)}/enable`, { on }];
  const memberOf = (k, x, add, remove) => (k === 'users' ? [`${userPath(x)}/groups`, { add, remove }] : ['/api/ad/object/groups', { dn: x.dn, add, remove }]);
  const moveOf = (k, x, ou) => (k === 'users' ? [`${userPath(x)}/move`, { ou }] : ['/api/ad/object/move', { dn: x.dn, ou }]);
  const editOf = (k, x, form) => (k === 'users' ? [`${userPath(x)}/edit`, form] : ['/api/ad/object/edit', { dn: x.dn, ...form }]);
  const deleteOf = (k, x) => (k === 'users' ? [`${userPath(x)}/delete`, {}] : k === 'groups' ? ['/api/ad/groups/delete', { dn: x.dn }] : [`${pcPath(x)}/delete`, {}]);
  const membersOf = (g, add, remove) => ['/api/ad/groups/members', { dn: g.dn, add, remove }];
  const post = (u, tail, body = {}) => [`${userPath(u)}${tail}`, body];

  // Shift-click ticks every row between the last one ticked and this one.
  let lastTick = null;
  const tickRange = (e) => {
    const box = e.target.closest('.tk')?.querySelector('input');
    if (!box || e.target !== box) return; // the label's own click comes through the box
    const sel = pickedHere();
    if (box.dataset.tickAll !== undefined) {
      rows.forEach((x) => (box.checked ? sel.add(keyOf(x)) : sel.delete(keyOf(x))));
    } else {
      const i = rows.findIndex((x) => keyOf(x) === box.dataset.tick);
      const from = e.shiftKey && lastTick !== null ? Math.min(lastTick, i) : i;
      const to = e.shiftKey && lastTick !== null ? Math.max(lastTick, i) : i;
      rows.slice(from, to + 1).forEach((x) => (box.checked ? sel.add(keyOf(x)) : sel.delete(keyOf(x))));
      lastTick = i;
    }
    drawPicked();
  };
  $('dir-table').addEventListener('contextmenu', (e) => {
    const row = picked[adPage] && e.target.closest('[data-i]');
    if (!row) return;
    e.preventDefault();
    const r = row.getBoundingClientRect();
    // From the keyboard there's no pointer: under the row's name.
    openCmd(menuObjs(rows[Number(row.dataset.i)]), e.clientX || r.left + 40, e.clientY || r.bottom);
  });

  // The menu: Properties, then a category for each kind of change, More, and Delete. Only what applies is listed; for
  // several, an item says how many of them it applies to.
  const mi = (cmd, icon, label, { off = false, hint = '', danger = false } = {}) => (off ? ''
    : `<button type="button" role="menuitem" class="am-link${danger ? ' out' : ''}" data-cmd="${cmd}">${svg(icon)}<span>${esc(label)}</span>${hint ? `<small>${esc(hint)}</small>` : ''}</button>`);
  const some = (k, n, cmd, icon, label) => mi(cmd, icon, label, { off: !k, hint: n > 1 && k < n ? `${k} of ${n}` : '' });
  const part = (...items) => (items.join('') ? `<div class="am-links">${items.join('')}</div>` : '');
  // A category opens to the side. One with a single item is just that item; an empty one isn't there.
  const fly = (icon, label, ...parts) => {
    const inner = parts.join('');
    const items = (inner.match(/data-cmd=/g) || []).length;
    if (items < 2) return inner.replace(/<div class="am-links">|<\/div>/g, '');
    return `<div class="cmd-sub"><button type="button" role="menuitem" class="am-link" aria-haspopup="menu" aria-expanded="false">`
      + `${svg(icon)}<span>${esc(label)}</span>${svg('right', 'chev')}</button><div class="gear-menu cmd-fly" role="menu">${inner}</div></div>`;
  };
  const COPY = {
    users: [['name', 'name', 'names', nameOf], ['username', 'logon name', 'logon names', (u) => u.username],
      ['upn', 'user principal name', 'user principal names', (u) => u.upn], ['email', 'email address', 'email addresses', (u) => u.email],
      ['dn', 'distinguished name', 'distinguished names', (u) => u.dn]],
    groups: [['name', 'name', 'names', (g) => g.name], ['email', 'email address', 'email addresses', (g) => g.email], ['dn', 'distinguished name', 'distinguished names', (g) => g.dn]],
    computers: [['name', 'name', 'names', (c) => c.name], ['host', 'DNS host name', 'DNS host names', (c) => c.host], ['dn', 'distinguished name', 'distinguished names', (c) => c.dn]],
  };
  const PIC = { users: 'user', groups: 'users', computers: 'laptop' };
  // What's left out on each page, one and several, for the line under the menu's heading.
  const LEFT_OUT = {
    users: ['protected or built-in account', 'protected or built-in accounts'],
    groups: ['built-in group', 'built-in groups'],
    computers: ['domain controller', 'domain controllers'],
  };
  // An account option's field on the user, and what turning it on or off says afterwards.
  const FLAGS = {
    mustchange: ['mustChange', (w) => `${w} must change the password at the next sign-in`, (w) => `Cleared the password change requirement for ${w}`],
    noexpiry: ['neverExpires', (w) => `Set the password to never expire for ${w}`, (w) => `Set the password to expire per policy for ${w}`],
    cannotchange: [null, (w) => `Stopped ${w} from changing the password`, (w) => `Let ${w} change the password`],
    sensitive: ['sensitive', (w) => `Marked ${w} as sensitive and cannot be delegated`, (w) => `Allowed delegation for ${w}`],
    smartcard: ['smartcard', (w) => `Required a smart card for ${w}`, (w) => `Stopped requiring a smart card for ${w}`],
    reversible: ['reversible', (w) => `Turned on reversible password encryption for ${w}`, (w) => `Turned off reversible password encryption for ${w}`],
    des: ['desOnly', (w) => `Set ${w} to use only Kerberos DES encryption types`, (w) => `Stopped ${w} using only Kerberos DES encryption types`],
    preauth: ['noPreauth', (w) => `Turned off Kerberos preauthentication for ${w}`, (w) => `Turned on Kerberos preauthentication for ${w}`],
  };
  const menuHtml = (k, objs) => {
    const [one, many] = AD[k].noun;
    const all = objs.length;
    const xs = objs.filter((x) => mayChange(k, x));
    const n = xs.length;
    const has = (f) => xs.filter(f).length;
    const change = can('ad.change') && n > 0;
    const toggle = (flag, icon, onLabel, offLabel) => {
      const on = has((u) => u[FLAGS[flag][0]]);
      return some(n - on, n, `flag-on:${flag}`, icon, onLabel) + some(on, n, `flag-off:${flag}`, icon, offLabel);
    };
    const out = can('ad.change') ? all - n : 0;
    const left = LEFT_OUT[k];
    const top = all === 1
      ? part(mi('open', PIC[k], 'Properties')) + (out ? `<p class="cmd-note">${esc(left[0].charAt(0).toUpperCase() + left[0].slice(1))}: change it in Active Directory Users and Computers.</p>` : '')
      : `<p class="cmd-head">${esc(count(all, one, many))} selected<button type="button" class="linkish cmd-clear" data-cmd="clear-sel">Clear</button>`
        + `${out ? `<small>${esc(count(out, ...left))} left out</small>` : ''}</p>`;
    const onOff = (icon) => part(some(has((x) => !x.enabled), n, 'enable', 'user-check', n === 1 ? 'Enable account' : 'Enable accounts'),
      some(has((x) => x.enabled), n, 'disable', 'user-off', n === 1 ? 'Disable account' : 'Disable accounts'),
      ...(icon ? [some(has((u) => u.locked), n, 'unlock', 'unlock', n === 1 ? 'Unlock account' : 'Unlock accounts')] : []));
    const membership = [
      part(mi('groups-add', 'user-plus', 'Add to groups…'), some(has((x) => x.groups?.length), n, 'groups-remove', 'user-minus', 'Remove from groups…'),
        some(has((x) => x.groups?.length), n, 'groups-remove-all', 'user-minus', 'Remove from all groups…')),
      part(mi('groups-copy', 'copy', `Copy membership from another ${one}…`), mi('groups-copy-to', 'copy', `Copy membership to other ${many}…`, { off: n !== 1 })),
    ];
    const organization = fly('folder', 'Organization',
      part(mi('edit', 'edit', n === 1 ? 'Edit properties…' : `Edit properties of ${count(n, one, many)}…`),
        ...(k === 'users' ? [mi('rename', 'edit', 'Rename…', { off: n !== 1 })] : []), mi('move', 'move', 'Move…')));
    let changes = '';
    if (change && k === 'users') {
      changes = part(
        fly('user', 'Account', onOff(true),
          part(mi('expiry', 'calendar', n === 1 && xs[0].accountExpires ? 'Change account expiration…' : 'Set account expiration…'),
            some(has((u) => u.accountExpires), n, 'expiry-clear', 'calendar', 'Remove account expiration'))),
        fly('key', 'Password',
          part(mi('password', 'key', n === 1 ? 'Reset password…' : `Reset password for ${n} users…`)),
          part(some(has((u) => !u.mustChange && !u.neverExpires), n, 'flag-on:mustchange', 'refresh', 'Require password change at next sign-in'),
            some(has((u) => u.mustChange), n, 'flag-off:mustchange', 'refresh', 'Clear password change requirement')),
          part(toggle('noexpiry', 'clock', 'Set password to never expire', 'Set password to expire per policy')),
          part(mi('flag-on:cannotchange', 'lock', 'Prevent user from changing password'), mi('flag-off:cannotchange', 'unlock', 'Allow user to change password'))),
        fly('users', 'Groups', ...membership),
        fly('shield', 'Security',
          part(toggle('sensitive', 'shield', 'Mark as sensitive and cannot be delegated', 'Allow delegation'),
            toggle('smartcard', 'key', 'Require smart card for interactive sign-in', 'Stop requiring a smart card')),
          part(toggle('reversible', 'lock', 'Store password using reversible encryption', 'Stop storing password using reversible encryption'),
            toggle('des', 'lock', 'Use only Kerberos DES encryption types', 'Stop using only Kerberos DES encryption types'),
            toggle('preauth', 'alert', 'Do not require Kerberos preauthentication', 'Require Kerberos preauthentication'))),
        organization);
    } else if (change && k === 'groups') {
      changes = part(
        fly('users', 'Members',
          part(mi('members-add', 'user-plus', n === 1 ? 'Add members…' : `Add members to ${n} groups…`), mi('members-remove', 'user-minus', 'Remove members…', { off: n !== 1 })),
          part(mi('members-copy', 'copy', 'Copy members from another group…'), mi('members-copy-to', 'copy', 'Copy members to other groups…', { off: n !== 1 }))),
        fly('org', 'Member of', ...membership),
        organization);
    } else if (change) {
      changes = part(fly('laptop', 'Account', onOff(false)), fly('users', 'Groups', ...membership), organization);
    }
    const more = part(fly('dots', 'More',
      part(mi('attrs', 'list', 'View in attribute browser', { off: all !== 1 })),
      part(...COPY[k].map(([key, o, m]) => mi(`copy:${key}`, 'copy', `Copy ${all === 1 ? o : m}`))),
      part(mi('export', 'csv', all === 1 ? 'Export to CSV' : `Export ${count(all, one, many)} to CSV`))));
    const deletable = xs.filter((x) => k !== 'groups' || !x.protected).length;
    const gone = can('ad.delete') && deletable ? part(mi('delete', 'trash', deletable === 1 ? 'Delete…' : `Delete ${count(deletable, one, many)}…`, { danger: true })) : '';
    return top + changes + more + gone;
  };

  let menuFor = { kind: 'users', objs: [] };
  const openCmd = (objs, x, y, kind = adPage) => {
    if (!objs.length) return;
    const m = $('cmd-menu');
    if (m.matches(':popover-open')) m.hidePopover();
    // Over the side panel the menu has to be inside it, or the panel makes it unclickable.
    const host = $('ad-drawer').open ? $('ad-drawer') : document.body;
    if (m.parentElement !== host) host.append(m);
    menuFor = { kind, objs };
    m.innerHTML = menuHtml(kind, objs);
    m.style.setProperty('left', '0px');
    m.style.setProperty('top', '0px');
    m.showPopover();
    const h = m.offsetHeight;
    m.style.setProperty('left', `${Math.max(8, Math.min(x, innerWidth - m.offsetWidth - 8))}px`);
    m.style.setProperty('top', `${Math.max(8, y + h > innerHeight - 8 ? innerHeight - h - 8 : y)}px`);
    m.querySelector('.am-link:not(:disabled)')?.focus({ preventScroll: true });
  };
  // A category's items open beside it: to the right, or to the left where the window ends, and up where it's short.
  const openFly = (sub) => {
    $('cmd-menu').querySelectorAll('.cmd-sub.open').forEach((s) => {
      if (s === sub) return;
      s.classList.remove('open', 'left');
      s.firstElementChild.setAttribute('aria-expanded', 'false');
    });
    if (!sub || sub.classList.contains('open')) return;
    sub.classList.add('open');
    sub.firstElementChild.setAttribute('aria-expanded', 'true');
    const f = sub.querySelector('.cmd-fly');
    f.style.removeProperty('top');
    sub.classList.toggle('left', sub.getBoundingClientRect().right + f.offsetWidth + 12 > innerWidth);
    const low = f.getBoundingClientRect().bottom - (innerHeight - 8);
    if (low > 0) f.style.setProperty('top', `${-7 - low}px`);
  };
  $('cmd-menu').addEventListener('mouseover', (e) => {
    const sub = e.target.closest('.cmd-sub');
    if (sub && !e.target.closest('.cmd-fly')) openFly(sub);
    else if (!sub && e.target.closest('.am-link')) openFly(null);
  });
  $('cmd-menu').addEventListener('toggle', (e) => { if (e.newState === 'closed') openFly(null); });
  $('cmd-menu').addEventListener('click', (e) => {
    const head = e.target.closest('.cmd-sub > .am-link');
    if (head) {
      openFly(head.parentElement);
      head.parentElement.querySelector('.cmd-fly .am-link:not(:disabled)')?.focus();
      return;
    }
    const b = e.target.closest('[data-cmd]');
    if (!b || b.disabled) return;
    $('cmd-menu').hidePopover();
    runCmd(b.dataset.cmd, menuFor.kind, menuFor.objs);
  });
  // Up and down within the list that has focus; right opens a category, left goes back to it.
  $('cmd-menu').addEventListener('keydown', (e) => {
    const m = $('cmd-menu');
    const now = document.activeElement;
    const scope = now.closest('.cmd-fly') || m;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const items = [...scope.querySelectorAll('.am-link:not(:disabled)')].filter((b) => (b.parentElement.closest('.cmd-fly') || m) === scope);
      const at = items.indexOf(now);
      items[(at + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length]?.focus();
    } else if (e.key === 'ArrowRight' && now.matches('.cmd-sub > .am-link')) {
      e.preventDefault();
      openFly(now.parentElement);
      now.parentElement.querySelector('.cmd-fly .am-link:not(:disabled)')?.focus();
    } else if (e.key === 'ArrowLeft' && scope !== m) {
      e.preventDefault();
      openFly(null);
      scope.parentElement.firstElementChild.focus();
    }
  });

  // Each one in turn under the flask; then the list is read again and a toast says how it went. Gives back what each
  // that worked answered.
  const each = async (k, xs, doing, done, work, { gone = false } = {}) => {
    if (!xs.length) return [];
    closeCard();
    say('dir-problem', '');
    say('dir-note', '');
    const answers = [];
    const failed = [];
    const warnings = [];
    for (const [i, x] of xs.entries()) {
      R.busy($('dir-lib'), doing, xs.length > 1 ? `${i + 1} of ${xs.length}: ${nameOf(x)}` : nameOf(x));
      try {
        const a = await R.send(...work(x));
        if (a?.warning) warnings.push(a.warning);
        answers.push([x, a]);
        if (gone) picked[k]?.delete(String(AD[k].key(x)));
      } catch (err) {
        failed.push(`${nameOf(x)}: ${err.message}`);
      }
    }
    try {
      if (k === 'users' && xs.length === 1 && !gone && lists.users?.includes(xs[0])) {
        lists.users[lists.users.indexOf(xs[0])] = await R.get(userPath(xs[0]));
        counted.list = null;
      } else if (k === adPage) {
        await loadAd(true);
      } else {
        delete lists[k];
      }
    } catch (err) {
      say('dir-problem', err.message);
    }
    R.busy($('dir-lib'), '');
    drawAd();
    // The side panel shows the change too (or closes, for something deleted).
    if ($('ad-drawer').open) { dw.membersFor = ''; drawDrawer(); }
    const ok = xs.length - failed.length;
    if (!failed.length) R.toast(done, warnings[0] || '', { tone: warnings.length ? 'warn' : '' });
    else R.toast(ok ? `Done for ${ok} of ${xs.length}` : 'That didn\'t work', failed[0], { tone: 'warn' });
    if (failed.length > 1) say('dir-problem', failed.join(' '));
    return answers;
  };

  // A group's members, read when they're needed.
  const membersNow = async (g) => (await R.get(`/api/ad/groups/members?dn=${encodeURIComponent(g.dn)}`)).map((m) => ({ dn: m.dn, name: m.name, kind: m.kind, sub: m.username || m.where }));
  const groupsFind = async (q) => (await R.get(`/api/ad/groups?q=${encodeURIComponent(q)}`)).filter((g) => !g.system).map((g) => ({ dn: g.dn, name: g.name, kind: 'group', sub: g.description || g.where }));
  const anyFind = async (q) => (await R.get(`/api/ad/find?q=${encodeURIComponent(q)}`)).map((m) => ({ dn: m.dn, name: m.name, kind: m.kind, sub: m.username || m.where }));
  // The same kind, from the list already read: for copying membership from one to another.
  const sameFind = (k, not) => async (q) => {
    const t = q.toLowerCase();
    return (lists[k] || []).filter((x) => !not.includes(x) && [x.name, x.username].some((v) => String(v || '').toLowerCase().includes(t)))
      .slice(0, 30).map((x) => ({ dn: x.dn, name: nameOf(x), kind: { users: 'user', groups: 'group', computers: 'computer' }[k], sub: x.username || x.where, x }));
  };
  const notIn = (x, dns) => dns.filter((d) => !(x.groups || []).some((g) => same(g.dn, d)) && !same(x.dn, d));

  const runCmd = (cmd, k, objs) => {
    const xs = objs.filter((x) => mayChange(k, x));
    const [what, arg] = cmd.split(':');
    const only = (f) => xs.filter(f);
    const [one, many] = AD[k].noun;
    const who = whoOf(xs, k);
    switch (what) {
      case 'clear-sel':
        picked[k]?.clear();
        drawPicked();
        break;
      case 'open': openDrawer(k, objs[0]); break;
      case 'enable':
      case 'disable': {
        const on = what === 'enable';
        const to = only((x) => x.enabled !== on);
        each(k, to, on ? 'Enabling' : 'Disabling', `${on ? 'Enabled' : 'Disabled'} ${whoOf(to, k)}`, (x) => enableOf(k, x, on));
        break;
      }
      case 'unlock': {
        const to = only((u) => u.locked);
        each(k, to, 'Unlocking', `Unlocked ${whoOf(to, k)}`, (u) => post(u, '/unlock'));
        break;
      }
      case 'expiry': openExpiry(xs); break;
      case 'expiry-clear': {
        const to = only((u) => u.accountExpires);
        each(k, to, 'Removing the expiration', `Removed the account expiration for ${whoOf(to, k)}`, (u) => post(u, '/expiry', { date: null }));
        break;
      }
      case 'password':
        if (xs.length === 1) reset(who, `${userPath(xs[0])}/password`, 'It also unlocks the account if it\'s locked out.');
        else resetMany(xs);
        break;
      case 'flag-on':
      case 'flag-off': {
        const on = what === 'flag-on';
        const [field, onSaid, offSaid] = FLAGS[arg];
        const to = !field ? xs : only((u) => !!u[field] !== on && !(arg === 'mustchange' && on && u.neverExpires));
        each(k, to, 'Changing the account options', (on ? onSaid : offSaid)(whoOf(to, k)), (u) => post(u, '/flag', { flag: arg, on }));
        break;
      }
      case 'groups-add':
        openPicker({ title: 'Add to groups', lead: `Find the groups to add ${who} to.`, button: 'Add', find: groupsFind, not: xs.map((x) => x.dn),
          go: (gs) => each(k, xs, 'Adding to groups', `Added ${who} to ${gs.length === 1 ? gs[0].name : count(gs.length, 'group', 'groups')}`,
            (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), [])) });
        break;
      case 'groups-remove': {
        const to = only((x) => x.groups?.length);
        const seen = new Map();
        to.forEach((x) => x.groups.forEach((g) => seen.set(g.dn.toLowerCase(), { dn: g.dn, name: g.name, kind: 'group', n: (seen.get(g.dn.toLowerCase())?.n || 0) + 1 })));
        openPicker({ title: 'Remove from groups', lead: `Tick the groups to take ${whoOf(to, k)} out of.`, button: 'Remove',
          ticks: [...seen.values()].sort((a, b) => a.name.localeCompare(b.name)).map((g) => ({ ...g, sub: to.length > 1 ? `${g.n} of ${to.length}` : '' })),
          go: (gs) => each(k, to, 'Removing from groups', `Removed ${whoOf(to, k)} from ${gs.length === 1 ? gs[0].name : count(gs.length, 'group', 'groups')}`,
            (x) => memberOf(k, x, [], gs.filter((g) => x.groups.some((h) => same(h.dn, g.dn))).map((g) => g.dn))) });
        break;
      }
      case 'groups-remove-all': {
        const to = only((x) => x.groups?.length);
        const stays = { users: ' The primary group, usually Domain Users, stays.', computers: ' The primary group, usually Domain Computers, stays.', groups: '' }[k];
        sure(`Remove ${whoOf(to, k)} from all groups?`, `Every group membership is removed.${stays}`, 'Remove from all groups',
          () => each(k, to, 'Removing from groups', `Removed ${whoOf(to, k)} from all groups`, (x) => memberOf(k, x, [], x.groups.map((g) => g.dn))));
        break;
      }
      case 'groups-copy':
        openPicker({ title: 'Copy membership', lead: `Pick the ${one} whose groups ${who} ${xs.length === 1 ? 'joins' : 'join'}.`, button: 'Copy', one: true, find: sameFind(k, xs),
          go: ([from]) => {
            const gs = from.x.groups || [];
            if (!gs.length) R.toast(`${from.name} isn't in any groups`, '', { tone: 'warn' });
            else each(k, xs, 'Copying membership', `Added ${who} to the groups ${from.name} is in`, (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), []));
          } });
        break;
      case 'groups-copy-to': {
        const from = xs[0];
        const gs = from.groups || [];
        if (!gs.length) {
          R.toast(`${nameOf(from)} isn't in any groups`, '', { tone: 'warn' });
          break;
        }
        openPicker({ title: 'Copy membership', lead: `Pick the ${many} to add to the ${count(gs.length, 'group', 'groups')} ${nameOf(from)} is in.`, button: 'Copy', find: sameFind(k, xs),
          go: (picks) => each(k, picks.map((p) => p.x).filter((x) => mayChange(k, x)), 'Copying membership', `Added ${whoOf(picks.map((p) => p.x), k)} to the groups ${nameOf(from)} is in`,
            (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), [])) });
        break;
      }
      case 'members-add':
        openPicker({ title: 'Add members', lead: `Find the users, groups and computers to add to ${who}.`, button: 'Add', find: anyFind, not: xs.map((g) => g.dn),
          go: (ms) => each(k, xs, 'Adding members', `Added ${ms.length === 1 ? ms[0].name : count(ms.length, 'member', 'members')} to ${who}`, (g) => membersOf(g, ms.map((m) => m.dn), [])) });
        break;
      case 'members-remove': {
        const g = xs[0];
        membersNow(g).then((ms) => openPicker({ title: 'Remove members', lead: ms.length ? `Tick who to take out of ${g.name}.` : `${g.name} has no members.`, button: 'Remove', ticks: ms,
          go: (out) => each(k, [g], 'Removing members', `Took ${out.length === 1 ? out[0].name : count(out.length, 'member', 'members')} out of ${g.name}`, (x) => membersOf(x, [], out.map((m) => m.dn))) }))
          .catch((err) => R.toast('Couldn\'t read the members', err.message, { tone: 'warn' }));
        break;
      }
      case 'members-copy':
        openPicker({ title: 'Copy members', lead: `Pick the group whose members go into ${who}.`, button: 'Copy', one: true, find: groupsFind, not: xs.map((g) => g.dn),
          go: async ([from]) => {
            const ms = await membersNow(from).catch(() => []);
            if (!ms.length) R.toast(`${from.name} has no members to copy`, '', { tone: 'warn' });
            else each(k, xs, 'Copying members', `Added the members of ${from.name} to ${who}`, (g) => membersOf(g, ms.map((m) => m.dn).filter((d) => !same(d, g.dn)), []));
          } });
        break;
      case 'members-copy-to': {
        const from = xs[0];
        openPicker({ title: 'Copy members', lead: `Pick the groups that get the members of ${from.name}.`, button: 'Copy', find: groupsFind, not: [from.dn],
          go: async (to) => {
            const ms = await membersNow(from).catch(() => []);
            if (!ms.length) R.toast(`${from.name} has no members to copy`, '', { tone: 'warn' });
            else each(k, to, 'Copying members', `Added the members of ${from.name} to ${to.length === 1 ? to[0].name : count(to.length, 'group', 'groups')}`, (g) => membersOf(g, ms.map((m) => m.dn).filter((d) => !same(d, g.dn)), []));
          } });
        break;
      }
      case 'edit': openEdit(k, xs); break;
      case 'rename': openRename(xs[0]); break;
      case 'move': openMove(k, xs); break;
      case 'attrs': location.hash = `#attrs/${encodeURIComponent(dnParent(objs[0].dn))}/${encodeURIComponent(objs[0].dn)}`; break;
      case 'copy': copyField(k, objs, arg); break;
      case 'export': csvOf(objs, objs.length === 1 ? nameOf(objs[0]) : `${objs.length}-${many}`); break;
      case 'delete': {
        const to = xs.filter((x) => k !== 'groups' || !x.protected);
        const w = whoOf(to, k);
        const why = {
          users: `${to.length === 1 ? `${to[0].username} is` : `${count(to.length, 'account is', 'accounts are')}`} deleted from ${ad.state.domain} for good, with anything in ${to.length === 1 ? 'it' : 'them'}. Disabling can be undone; this can't.`,
          groups: `${to.length === 1 ? 'The group is' : 'The groups are'} deleted from ${ad.state.domain} for good. Members stay; they're just not in ${to.length === 1 ? 'it' : 'them'} any more.`,
          computers: `${to.length === 1 ? 'Its account is' : 'Their accounts are'} deleted from ${ad.state.domain}. ${to.length === 1 ? 'It' : 'They'} can't sign anyone in with domain accounts until ${to.length === 1 ? 'it joins' : 'they join'} the domain again. Disabling can be undone; this can't.`,
        }[k];
        sure(`Delete ${w}?`, why, to.length === 1 ? `Delete ${nameOf(to[0])}` : `Delete ${count(to.length, one, many)}`,
          () => each(k, to, 'Deleting', `Deleted ${w}`, (x) => deleteOf(k, x), { gone: true }));
        break;
      }
    }
  };

  const copyField = async (k, objs, key) => {
    const [, one, many, get] = COPY[k].find(([c]) => c === key);
    const values = objs.map(get).filter(Boolean);
    if (!values.length) {
      R.toast(`No ${many} to copy`, '', { tone: 'warn' });
      return;
    }
    try {
      await navigator.clipboard.writeText(values.join('\r\n'));
      R.toast(values.length === 1 ? `Copied the ${one}` : `Copied ${values.length} ${many}`);
    } catch {
      R.toast('Couldn\'t copy', 'The browser didn\'t allow it.', { tone: 'warn' });
    }
  };

  // Set account expiration: the last day it works, as ADUC's End of.
  let expFor = [];
  const ymd = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const openExpiry = (us) => {
    expFor = us;
    const one = us.length === 1 ? us[0] : null;
    $('exp-title').textContent = one?.accountExpires ? 'Change account expiration' : 'Set account expiration';
    $('exp-lead').textContent = `For ${whoOf(us, 'users')}.`;
    $('exp-date').value = one?.accountExpires ? ymd(new Date(new Date(one.accountExpires) - 1)) : '';
    say('exp-problem', '');
    $('exp-dialog').showModal();
    $('exp-date').focus();
  };
  $('exp-cancel').addEventListener('click', () => $('exp-dialog').close());
  $('exp-save').addEventListener('click', () => {
    const date = $('exp-date').value;
    if (!date) {
      say('exp-problem', 'Pick the last day the account works.');
      return;
    }
    $('exp-dialog').close();
    each('users', expFor, 'Setting the expiration', `${whoOf(expFor, 'users')} ${expFor.length === 1 ? 'expires' : 'expire'} at the end of ${day(`${date}T12:00`)}`, (u) => post(u, '/expiry', { date }));
  });

  // Edit properties: one's details, or for several only what's typed (blank leaves each as it is). Each field is for
  // the kinds listed beside it; data-one fields are for one at a time.
  const UED = [['first', 'first', 'first', 'users'], ['last', 'last', 'last', 'users'], ['display', 'name', 'display', 'users'],
    ['description', 'description', 'description', 'users groups computers'], ['jobtitle', 'title', 'title', 'users'], ['department', 'department', 'department', 'users'],
    ['company', 'company', 'company', 'users'], ['office', 'office', 'office', 'users'], ['email', 'email', 'email', 'users groups'], ['phone', 'phone', 'phone', 'users'],
    ['notes', 'notes', 'notes', 'groups'], ['location', 'location', 'location', 'computers']];
  const ued = { kind: 'users', objs: [], boss: undefined }; // boss: undefined leaves it, '' clears it, else the manager's DN
  const openEdit = (k, xs) => {
    ued.kind = k;
    ued.objs = xs;
    ued.boss = undefined;
    const one = xs.length === 1 ? xs[0] : null;
    $('ued-title').textContent = one ? `Edit ${nameOf(one)}` : `Edit ${count(xs.length, ...AD[k].noun)}`;
    $('ued-lead').textContent = one ? (k === 'users' ? `${one.username}, in ${one.where || ad.state.domain}.` : `In ${one.where || ad.state.domain}.`) : `Only what you fill in changes. Blank boxes leave each one as it is.`;
    UED.forEach(([id, field, , kinds]) => {
      const box = $(`ued-${id}`);
      box.closest('div').hidden = !kinds.split(' ').includes(k) || (!one && box.closest('[data-one]') !== null);
      box.value = one ? one[field] || '' : '';
      box.placeholder = one ? '' : 'Leave as it is';
    });
    $('ued-boss-row').hidden = k !== 'users';
    $('ued-manager').value = one?.manager || '';
    $('ued-manager').placeholder = one ? 'Find a person' : 'Leave as it is';
    say('ued-problem', '');
    $('ued-dialog').showModal();
    $('ued-fields').querySelector('div:not([hidden]) input')?.focus();
  };
  R.dropdown($('ued-boss'), {
    none: 'Nobody matches.',
    find: async (q) => {
      const t = q.toLowerCase();
      return (lists.users || []).filter((u) => !u.system && !ued.objs.includes(u) && [u.name, u.username].some((v) => String(v || '').toLowerCase().includes(t))).slice(0, 30);
    },
    draw: (u, i) => R.option(` data-pick="${i}"`, `<span class="pic user">${esc(initials(nameOf(u)))}</span>`, nameOf(u), u.title || u.username),
    pick: (u) => {
      ued.boss = u.dn;
      $('ued-manager').value = nameOf(u);
    },
  });
  $('ued-manager-x').addEventListener('click', () => {
    ued.boss = '';
    $('ued-manager').value = '';
  });
  $('ued-cancel').addEventListener('click', () => $('ued-dialog').close());
  $('ued-save').addEventListener('click', () => {
    const { kind: k, objs: xs } = ued;
    const one = xs.length === 1 ? xs[0] : null;
    const form = {};
    UED.forEach(([id, field, key]) => {
      const box = $(`ued-${id}`);
      if (box.closest('div').hidden) return;
      const v = box.value.trim();
      if (one ? v !== (one[field] || '') : v) form[key] = v;
    });
    if (k === 'users' && ued.boss !== undefined) form.manager = ued.boss;
    $('ued-dialog').close();
    if (!Object.keys(form).length) return;
    each(k, xs, 'Saving', `Saved ${whoOf(xs, k)}`, (x) => editOf(k, x, form));
  });

  // Rename: a user's name in Active Directory, and its display name with it.
  let renaming = null;
  const openRename = (u) => {
    renaming = u;
    $('ren-lead').textContent = `${u.username}, in ${u.where || ad.state.domain}.`;
    $('ren-name').value = nameOf(u);
    say('ren-problem', '');
    $('ren-dialog').showModal();
    $('ren-name').select();
  };
  $('ren-cancel').addEventListener('click', () => $('ren-dialog').close());
  $('ren-save').addEventListener('click', () => {
    const name = $('ren-name').value.trim();
    if (!name) {
      say('ren-problem', 'Type the new name.');
      return;
    }
    $('ren-dialog').close();
    const u = renaming;
    each('users', [u], 'Renaming', `Renamed ${nameOf(u)} to ${name}`, (x) => post(x, '/rename', { name }));
  });
  $('ren-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('ren-save').click(); });

  // Move…: the OU picker, then each one that isn't there already.
  let moving = { kind: 'users', objs: [] };
  const openMove = async (k, xs) => {
    moving = { kind: k, objs: xs };
    // It opens on where they are when that's one place, else where new users go.
    const at = xs.map((x) => dnParent(x.dn).toLowerCase());
    await placesInto('mv-ou', at.every((p) => p === at[0]) ? dnParent(xs[0].dn) : ad.state.newUsers, 'dir-problem');
    PICK_LEADS['mv-ou'] = `Where ${whoOf(xs, k)} ${xs.length === 1 ? 'goes' : 'go'}.`;
    openPick($('mv-ou'));
  };
  $('mv-ou').addEventListener('change', () => {
    const ou = $('mv-ou').value;
    const { kind: k, objs } = moving;
    const to = objs.filter((x) => !same(dnParent(x.dn), ou));
    const where = (places || []).find((p) => same(p.dn, ou))?.path || ad.state.domain;
    each(k, to, 'Moving', `Moved ${whoOf(to, k)} to ${where}`, (x) => moveOf(k, x, ou));
  });

  // One window for picking: find and pick (shown as chips), or tick from a list. One: picking replaces what's picked.
  const pk = { find: null, ticks: null, one: false, not: [], chosen: [], go: null };
  const PK_ICON = { user: 'user', group: 'users', computer: 'laptop', other: 'user' };
  const drawPk = () => {
    $('gp-list').innerHTML = pk.ticks
      ? `<div class="ticks">${pk.ticks.map((t, i) => `<label class="tick"><input type="checkbox" data-gp="${i}"${pk.chosen.includes(t) ? ' checked' : ''}>${svg(PK_ICON[t.kind] || 'user')}<span>${esc(t.name)}</span>`
        + `${t.sub ? `<small>${esc(t.sub)}</small>` : ''}</label>`).join('')}</div>`
      : `<div class="chips-in">${pk.chosen.map((c, i) => `<span class="gchip">${esc(c.name)}<button type="button" class="chip-x" data-gp-x="${i}" aria-label="Not ${esc(c.name)}">${svg('x')}</button></span>`).join('')
        || '<span class="muted">Nothing picked yet.</span>'}</div>`;
    $('gp-save').disabled = !pk.chosen.length;
  };
  const openPicker = ({ title, lead, button, find = null, ticks = null, one = false, not = [], go }) => {
    Object.assign(pk, { find, ticks, one, not, go, chosen: [] });
    $('gp-title').textContent = title;
    $('gp-lead').textContent = lead;
    $('gp-adder').hidden = !find;
    $('gp-save').textContent = button;
    $('gp-adder').querySelector('[data-find]').value = '';
    $('gp-adder').querySelector('[data-drop]').hidden = true;
    say('gp-problem', '');
    drawPk();
    $('gp-dialog').showModal();
    if (find) $('gp-adder').querySelector('[data-find]').focus();
  };
  R.dropdown($('gp-adder'), {
    none: 'Nothing matches.',
    find: async (q) => (pk.find ? (await pk.find(q)).filter((x) => !pk.chosen.some((c) => same(c.dn, x.dn)) && !pk.not.some((d) => same(d, x.dn))).slice(0, 30) : []),
    draw: (x, i) => R.option(` data-pick="${i}"`, `<span class="pic">${svg(PK_ICON[x.kind] || 'user')}</span>`, x.name, x.sub || ''),
    pick: (x) => {
      pk.chosen = pk.one ? [x] : [...pk.chosen, x];
      drawPk();
      $('gp-adder').querySelector('[data-find]').focus();
    },
  });
  $('gp-list').addEventListener('click', (e) => {
    const x = e.target.closest('[data-gp-x]');
    if (!x) return;
    pk.chosen.splice(Number(x.dataset.gpX), 1);
    drawPk();
  });
  $('gp-list').addEventListener('change', (e) => {
    if (e.target.dataset.gp === undefined) return;
    const t = pk.ticks[Number(e.target.dataset.gp)];
    pk.chosen = e.target.checked ? [...pk.chosen, t] : pk.chosen.filter((c) => c !== t);
    $('gp-save').disabled = !pk.chosen.length;
  });
  $('gp-cancel').addEventListener('click', () => $('gp-dialog').close());
  $('gp-save').addEventListener('click', () => {
    const { chosen, go } = pk;
    $('gp-dialog').close();
    if (chosen.length) go(chosen);
  });

  // --- The panel from the right: one user, group or computer -------------------------------------------------------
  // Clicking a row opens it over the list, as the Microsoft 365 and Entra admin centers do: who or what it is, its
  // status, the everyday actions (⋯ has the rest), and tabs for its details and its groups (a group's members too).

  const dw = { kind: '', key: '', tab: 'overview', members: null, membersFor: '' };
  const dwObj = () => (lists[dw.kind] || []).find((x) => String(AD[dw.kind].key(x)) === dw.key);
  const dwFact = (label, value, wide = false) => `<div class="dw-fact${wide ? ' wide' : ''}"><span class="l">${esc(label)}</span><span class="v">${value || '<span class="muted">Not set</span>'}</span></div>`;
  const dwFacts = (title, items) => {
    const shown = items.filter(Boolean).join('');
    return shown ? `<section class="dw-sec"><h3>${esc(title)}</h3><div class="dw-facts">${shown}</div></section>` : '';
  };
  const e1 = (v) => (v ? esc(v) : '');
  const qa = (cmd, icon, label) => `<button type="button" class="dw-qa" data-dw-cmd="${cmd}">${svg(icon)}<span>${esc(label)}</span></button>`;
  const LEFT_WHY = {
    users: (u) => (u.service ? `That's ${ad.state.account}, ${R.app()}'s own account. It looks after it itself.`
      : u.protected ? `Active Directory protects the accounts of admins, so ${R.app()}'s account can't change this one. Use Active Directory Users and Computers for it.`
        : 'A built-in account. Change it in Active Directory Users and Computers.'),
    groups: () => 'A built-in group. Change it in Active Directory Users and Computers.',
    computers: () => 'A domain controller. Change it in Active Directory itself.',
  };

  const dwHead = (k, x) => {
    const pic = k === 'users' ? `<span class="pic user dw-pic">${esc(initials(nameOf(x)))}</span>` : `<span class="pic dw-pic">${svg(k === 'groups' ? 'users' : server(x) ? 'server' : 'laptop')}</span>`;
    const sub = k === 'users' ? [x.username, x.upn].filter(Boolean).join(' · ') : k === 'computers' ? x.host || '' : x.description || '';
    const status = k === 'users' ? userStatus(x) + (x.mustChange ? badge('Must change password', 'amber') : '') + (x.protected ? badge('Privileged', 'amber') : '')
      + (x.service ? chip(R.app(), 'ember') : '')
      : k === 'computers' ? pcStatus(x) + (x.controller ? chip('Domain controller', 'muted') : '') + (server(x) ? chip('Server', 'muted') : '')
        : badge(x.security ? 'Security group' : 'Distribution group', 'grey') + badge(x.scope, 'grey') + (x.protected ? badge('Privileged', 'amber') : '');
    // The everyday actions as buttons; ⋯ opens the whole menu.
    const change = can('ad.change') && mayChange(k, x);
    const quick = !change ? '' : k === 'users'
      ? qa('password', 'key', 'Reset password') + (x.locked ? qa('unlock', 'unlock', 'Unlock') : '') + qa(x.enabled ? 'disable' : 'enable', x.enabled ? 'user-off' : 'user-check', x.enabled ? 'Disable' : 'Enable')
        + qa('edit', 'edit', 'Edit') + qa('groups-add', 'user-plus', 'Add to groups')
      : k === 'groups' ? qa('members-add', 'user-plus', 'Add members') + qa('groups-add', 'org', 'Add to groups') + qa('edit', 'edit', 'Edit')
        : qa(x.enabled ? 'disable' : 'enable', x.enabled ? 'user-off' : 'user-check', x.enabled ? 'Disable' : 'Enable') + qa('groups-add', 'user-plus', 'Add to groups') + qa('edit', 'edit', 'Edit');
    return `<div class="dw-head">${pic}<div class="dw-name"><h2 id="dw-title">${esc(nameOf(x))}</h2>${sub ? `<p>${esc(sub)}</p>` : ''}</div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-status">${status}</div>`
      + `<div class="dw-bar">${quick}<button type="button" class="dw-qa dw-more" data-dw-more aria-haspopup="menu">${svg('dots')}<span>More</span></button></div>`
      + (!change && can('ad.change') ? `<p class="hint dw-note">${esc(LEFT_WHY[k](x))}</p>` : '');
  };

  const dwOverview = (k, x) => {
    if (k === 'users') {
      const password = x.mustChange ? 'Must change at the next sign-in' : x.neverExpires ? 'Never expires' : x.passwordExpires ? `Expires ${day(x.passwordExpires)}` : '';
      return dwFacts('Account', [dwFact('Pre-Windows 2000 logon name', `<span class="mono">${esc(x.username)}</span>`), dwFact('User principal name', e1(x.upn)),
        dwFact('Last sign-in', esc(ago(x.lastSignIn))), dwFact('Password', esc(password)), dwFact('Password last set', x.passwordSet ? esc(day(x.passwordSet)) : ''),
        dwFact('Account expires', x.accountExpires ? `End of ${esc(day(new Date(new Date(x.accountExpires) - 1)))}` : 'Never'), dwFact('Created', x.created ? esc(day(x.created)) : ''),
        dwFact('Organizational unit', esc(x.where || ad.state.domain), true)])
        + dwFacts('Contact and organization', [dwFact('Display name', e1(x.name)), dwFact('Email', e1(x.email)), dwFact('Job title', e1(x.title)), dwFact('Department', e1(x.department)),
          dwFact('Company', e1(x.company)), dwFact('Office', e1(x.office)), dwFact('Phone', e1(x.phone)), dwFact('Manager', e1(x.manager)), dwFact('Description', e1(x.description), true)])
        + dwFacts('Security', [dwFact('Sensitive and cannot be delegated', x.sensitive ? 'Yes' : 'No'), dwFact('Smart card required', x.smartcard ? 'Yes' : 'No'),
          dwFact('Reversible encryption', x.reversible ? 'Yes' : 'No'), dwFact('Kerberos preauthentication', x.noPreauth ? 'Not required' : 'Required')])
        + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
    }
    if (k === 'groups') {
      return dwFacts('Group', [dwFact('Group type', x.security ? 'Security' : 'Distribution'), dwFact('Group scope', esc(x.scope)),
        dwFact('Members', x.moreMembers ? '1,500 or more' : esc(x.members.toLocaleString())), dwFact('Email', e1(x.email)),
        dwFact('Organizational unit', esc(x.where || ad.state.domain), true), dwFact('Description', e1(x.description), true), dwFact('Notes', e1(x.notes), true)])
        + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
    }
    return dwFacts('Computer', [dwFact('DNS host name', e1(x.host)), dwFact('Operating system', esc([x.os, x.osVersion].filter(Boolean).join(' '))),
      dwFact('Last sign-in', esc(ago(x.lastSignIn))), dwFact('Created', x.created ? esc(day(x.created)) : ''), dwFact('Location', e1(x.location)),
      dwFact('Organizational unit', esc(x.where || ad.state.domain), true), dwFact('Description', e1(x.description), true)])
      + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
  };

  // Its groups, each with a way out, and Add to groups.
  const dwMemberOf = (k, x) => {
    const change = can('ad.change') && mayChange(k, x);
    const gs = x.groups || [];
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Member of <small>${gs.length}</small></h3>${change ? `<button type="button" class="small" data-dw-cmd="groups-add">${svg('user-plus')}Add to groups</button>` : ''}</div>`
      + (gs.length ? `<div class="dw-list">${gs.map((g) => `<div class="mrow"><span class="pic small">${svg('users')}</span><div class="two"><b>${esc(g.name)}</b><small>${esc(pathOf(g.dn))}</small></div>`
        + `${change ? `<button type="button" class="at-more dw-x" data-dw-leave="${esc(g.dn)}" data-tip="Remove" aria-label="Remove from ${esc(g.name)}">${svg('x')}</button>` : ''}</div>`).join('')}</div>`
        : `<p class="muted dw-empty">${k === 'users' ? 'Only Domain Users, the primary group.' : k === 'computers' ? 'Only Domain Computers, the primary group.' : 'Not in any groups.'}</p>`)
      + '</section>';
  };
  const pathOf = (dn) => dn.split(/(?<!\\),/).slice(1).filter((p) => !/^DC=/i.test(p)).map((p) => p.slice(p.indexOf('=') + 1)).reverse().join(' / ') || ad.state.domain;

  // A group's members, read when the tab opens.
  const dwMembers = (g) => {
    const change = can('ad.change') && mayChange('groups', g);
    if (dw.membersFor !== g.dn) {
      dw.membersFor = g.dn;
      dw.members = null;
      membersNow(g).then((ms) => { if (dw.membersFor === g.dn) { dw.members = ms; drawDrawer(); } })
        .catch((err) => { if (dw.membersFor === g.dn) { dw.members = { problem: err.message }; drawDrawer(); } });
    }
    const ms = dw.members;
    const body = ms === null ? `<div class="op-items">${'<i class="sk op-sk"></i>'.repeat(4)}</div>`
      : ms.problem ? `<p class="hint problem">${esc(ms.problem)}</p>`
        : ms.length ? `<div class="dw-list">${ms.map((m) => `<div class="mrow"><span class="pic small${m.kind === 'user' ? ' user' : ''}">${m.kind === 'user' ? esc(initials(m.name)) : svg(KIND_PIC[m.kind] || 'user')}</span>`
          + `<div class="two"><b>${esc(m.name)}</b><small>${esc(m.sub || '')}</small></div>`
          + `${change ? `<button type="button" class="at-more dw-x" data-dw-out="${esc(m.dn)}" data-tip="Remove" aria-label="Take ${esc(m.name)} out">${svg('x')}</button>` : ''}</div>`).join('')}</div>`
          : '<p class="muted dw-empty">Nobody yet.</p>';
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Members${Array.isArray(ms) ? ` <small>${ms.length}</small>` : ''}</h3>`
      + `${change ? `<button type="button" class="small" data-dw-cmd="members-add">${svg('user-plus')}Add members</button>` : ''}</div>${body}</section>`;
  };

  const drawDrawer = () => {
    const x = dwObj();
    if (!x) {
      if ($('ad-drawer').open) $('ad-drawer').close();
      return;
    }
    const k = dw.kind;
    const tabsFor = k === 'groups' ? [['overview', 'Overview'], ['members', 'Members'], ['memberof', 'Member of']] : [['overview', 'Overview'], ['memberof', 'Member of']];
    const body = dw.tab === 'members' ? dwMembers(x) : dw.tab === 'memberof' ? dwMemberOf(k, x) : dwOverview(k, x);
    const scroll = $('dw-body').scrollTop;
    $('dw-body').innerHTML = dwHead(k, x)
      + `<div class="tabs2 dw-tabs" role="tablist">${tabsFor.map(([t, label]) => `<a role="tab" data-dw-tab="${t}"${dw.tab === t ? ' aria-current="true"' : ''}>${esc(label)}</a>`).join('')}</div>`
      + `<div class="dw-panel">${body}</div>`;
    $('dw-body').scrollTop = scroll;
  };
  const openDrawer = (k, x) => {
    const key = String(AD[k].key(x));
    if (dw.kind !== k || dw.key !== key) Object.assign(dw, { tab: 'overview', members: null, membersFor: '' });
    dw.kind = k;
    dw.key = key;
    closeCard();
    drawDrawer();
    if (!$('ad-drawer').open) $('ad-drawer').showModal();
    $('ad-drawer').querySelector('.dw-close')?.focus({ preventScroll: true });
  };
  $('ad-drawer').addEventListener('click', (e) => {
    const d = $('ad-drawer');
    // A click on the dimmed page beside the panel closes it.
    if (e.target === d || e.target.closest('[data-dw-close]')) { d.close(); return; }
    const x = dwObj();
    if (!x) return;
    const k = dw.kind;
    const t = e.target.closest('[data-dw-tab]');
    if (t) { dw.tab = t.dataset.dwTab; drawDrawer(); return; }
    const cmd = e.target.closest('[data-dw-cmd]');
    if (cmd) { runCmd(cmd.dataset.dwCmd, k, [x]); return; }
    const more = e.target.closest('[data-dw-more]');
    if (more) {
      const r = more.getBoundingClientRect();
      openCmd([x], r.right - CMD_W, r.bottom + 6, k);
      return;
    }
    const leave = e.target.closest('[data-dw-leave]');
    if (leave) {
      const g = (x.groups || []).find((h) => same(h.dn, leave.dataset.dwLeave));
      each(k, [x], 'Removing from the group', `Removed ${nameOf(x)} from ${g?.name || 'the group'}`, (y) => memberOf(k, y, [], [leave.dataset.dwLeave]));
      return;
    }
    const out = e.target.closest('[data-dw-out]');
    if (out) {
      const m = (dw.members || []).find((h) => same(h.dn, out.dataset.dwOut));
      dw.membersFor = ''; // read again after
      each(k, [x], 'Removing a member', `Took ${m?.name || 'them'} out of ${x.name}`, (g) => membersOf(g, [], [out.dataset.dwOut]));
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
    R.skeleton($('us-pc-rows'), 'rows', 6); // until Windows, or the computer's client, has answered
    try {
      const got = await R.get(`/api/windows/${encodeURIComponent(c.id)}/accounts`);
      pc = { id: c.id, accounts: got.accounts, groups: got.groups };
    } finally {
      R.skeleton($('us-pc-rows'), '');
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
    try {
      const answer = await R.send(path, body);
      if (answer.warning) say('us-note', answer.warning);
    } catch (err) {
      say('us-problem', err.message);
    } finally {
      localBusy = false;
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

  const secret = (who, password, warning, mustChange, many = false) => {
    $('secret-title').textContent = many ? 'The new passwords' : `${who}'s password`;
    $('secret-lead').textContent = `${many ? 'Give each person theirs.' : `Give it to ${who}.`}${mustChange ? ' They pick their own when they first sign in.' : ''}`;
    $('secret-text').textContent = password;
    $('secret-text').classList.toggle('many', many);
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
    resetting = { who, path, users: null };
    $('reset-title').textContent = `A new password for ${who}`;
    $('reset-lead').textContent = lead;
    resetMode('reset-pwmode', 'reset-password');
    $('reset-change').checked = true;
    say('reset-problem', '');
    $('reset-dialog').showModal();
  };
  // Several users: each gets one of its own, unless one is typed for all of them.
  const resetMany = (us) => {
    reset(whoOf(us, 'users'), '', 'Each gets a password of its own, unless you type one for all of them. It also unlocks accounts that are locked out.');
    resetting.users = us;
    $('reset-title').textContent = `New passwords for ${us.length} users`;
  };
  $('reset-cancel').addEventListener('click', () => $('reset-dialog').close());
  $('reset-go').addEventListener('click', async () => {
    const password = typed('reset-pwmode', 'reset-password');
    if (resetting.users) {
      const mustChange = $('reset-change').checked;
      $('reset-dialog').close();
      const got = await each('users', resetting.users, 'Setting passwords', `Set new passwords for ${resetting.who}`, (u) => post(u, '/password', { password, mustChange }));
      const made = got.filter(([, a]) => a?.password);
      const wide = Math.max(...made.map(([u]) => u.username.length), 0);
      if (made.length) secret('', made.map(([u, a]) => `${u.username.padEnd(wide + 3)}${a.password}`).join('\n'), null, mustChange, true);
      return;
    }
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
    // Builtin, Domain Controllers and the like are listed, as in ADUC, but can't be picked: data-fixed says why.
    $(select).innerHTML = places.map((p) => `<option value="${esc(p.dn)}"${p.dn === selected ? ' selected' : ''}${p.fixed ? ` disabled data-fixed="${esc(p.fixed)}"` : ''}>${esc(p.path || p.name)}</option>`).join('');
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

  // A new user, group or computer account.
  $('dir-new').addEventListener('click', async () => {
    if (adPage === 'users') {
      openNew('ad');
      return;
    }
    if (adPage === 'computers') {
      $('pcnew-name').value = '';
      $('pcnew-about').value = '';
      $('pcnew-lead').textContent = `In ${ad.state.domain}, made by ${ad.state.account}, ready for a computer of that name to join.`;
      say('pcnew-problem', '');
      await placesInto('pcnew-ou', ad.state.newComputers, 'pcnew-problem');
      $('pcnew-dialog').showModal();
      $('pcnew-name').focus();
      return;
    }
    $('grp-name').value = '';
    $('grp-about').value = '';
    $('grp-scope').value = 'global';
    $('grp-type').querySelectorAll('[data-type]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.type === 'security')));
    $('group-lead').textContent = `In ${ad.state.domain}, made by ${ad.state.account}.`;
    say('grp-problem', '');
    await placesInto('grp-ou', ad.state.newGroups || ad.state.newUsers, 'grp-problem');
    $('group-dialog').showModal();
    $('grp-name').focus();
  });
  $('grp-type').addEventListener('click', (e) => {
    const b = e.target.closest('[data-type]');
    if (b) $('grp-type').querySelectorAll('[data-type]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
  });
  $('grp-cancel').addEventListener('click', () => $('group-dialog').close());
  $('pcnew-cancel').addEventListener('click', () => $('pcnew-dialog').close());
  $('pcnew-go').addEventListener('click', async () => {
    $('pcnew-go').disabled = true;
    try {
      const c = await R.send('/api/ad/computers', { name: $('pcnew-name').value, description: $('pcnew-about').value, ou: $('pcnew-ou').value });
      $('pcnew-dialog').close();
      R.toast(`Made the computer account ${c.name}`, `A computer called ${c.name} can join the domain into it.`);
      reload();
    } catch (err) {
      say('pcnew-problem', err.message);
    } finally {
      $('pcnew-go').disabled = false;
    }
  });
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

  // --- Picking an OU: Browse… beside an OU list ----------------------------------------------------------------------
  // The list's own options are the OUs, shown as a rail like Settings: an OU with OUs inside has the flask and opens
  // under itself when picked. The one picked is described beside it, and Use this OU puts it back in the list.

  const FLASK = '<svg class="all-flask" viewBox="0 0 24 24" aria-hidden="true"><path class="liquid" d="M6.6 15.2h10.8l2 3.2A1.7 1.7 0 0 1 17.9 21H6.1a1.7 1.7 0 0 1-1.5-2.6z"/><path d="M9 3h6M10 3v6.2L4.6 18.4A1.7 1.7 0 0 0 6.1 21h11.8a1.7 1.7 0 0 0 1.5-2.6L14 9.2V3"/></svg>';
  const dnParent = (dn) => { const m = String(dn).match(/^(?:[^,\\]|\\.)*,(.*)$/); return m ? m[1] : ''; };
  const dnName = (dn) => { const first = String(dn).match(/^(?:[^,\\]|\\.)*/)[0]; return first.slice(first.indexOf('=') + 1).replace(/\\(.)/g, '$1'); };
  const isDomain = (dn) => /^DC=/i.test(dn);
  const same = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();
  const pick = { select: null, sel: '', find: '', nodes: [], domain: '' };
  const PICK_LEADS = { 'dset-ou': 'Where the service account is created.', 'new-ou': 'Where the new user goes.', 'grp-ou': 'Where the new group goes.',
    'adset-ou': 'Where New user puts them.', 'adset-gou': 'Where New group puts them.', 'adset-cou': 'Where New computer puts them.', 'pcnew-ou': 'Where the new computer account goes.' };
  const ouNode = (dn) => pick.nodes.find((n) => !n.domain && same(n.dn, dn));
  const kidsOf = (dn) => pick.nodes.filter((n) => !n.domain && same(n.parent, dn));
  const topOus = () => pick.nodes.filter((n) => !n.domain && !ouNode(n.parent));
  const inside = (dn) => same(pick.sel, dn) || pick.sel.toLowerCase().endsWith(`,${dn.toLowerCase()}`);
  const ouRow = (n, icon, text, small) => `<a role="button" tabindex="0" data-ou="${esc(n.dn)}" title="${esc(n.dn)}"${n.fixed ? ' class="fixed"' : ''}${same(pick.sel, n.dn) ? ' aria-current="true"' : ''}>`
    + `${icon}<span class="nm">${esc(text)}</span>${small ? `<small>${esc(small)}</small>` : ''}</a>`;
  const railRow = (n, top) => {
    const ks = kidsOf(n.dn);
    return ouRow(n, top ? (ks.length ? FLASK : svg('folder')) : '', n.name, ks.length ? String(ks.length) : '')
      + (ks.length ? `<div class="dkids${inside(n.dn) ? ' open' : ''}">${ks.map((k) => railRow(k, false)).join('')}</div>` : '');
  };
  const drawPickList = () => {
    const q = pick.find.trim().toLowerCase();
    let html;
    if (q) {
      const hits = pick.nodes.filter((n) => !n.domain && n.path.toLowerCase().includes(q));
      html = `<p class="rail-label">${hits.length ? `Found ${hits.length}` : 'Nothing found'}</p>`
        + hits.map((n) => ouRow(n, svg('folder'), n.name, n.path.split(' / ').slice(0, -1).join(' / '))).join('');
    } else {
      const whole = pick.nodes.find((n) => n.domain);
      html = `<p class="rail-label">${esc(pick.domain)}</p>` + (whole ? ouRow(whole, svg('globe'), 'The whole domain', '') : '')
        + topOus().map((n) => railRow(n, true)).join('');
    }
    $('oupick-list').innerHTML = pick.nodes.length ? html : '<p class="ds-empty oupick-none">No OUs to pick from.</p>';
  };
  // What's directly in a place (the Attribute browser's read), kept while the window is open.
  const inPlace = new Map();
  const KINDS = [['user', 'user', 'user', 'users'], ['group', 'users', 'group', 'groups'], ['computer', 'laptop', 'computer', 'computers'],
    ['ou', 'folder', 'OU', 'OUs'], ['container', 'folder', 'container', 'containers'], ['other', 'folder', 'other object', 'other objects']];
  const PICK_ICON = Object.fromEntries(KINDS.map(([k, icon]) => [k, icon]));
  const lookInside = async (dn) => {
    inPlace.set(dn.toLowerCase(), { loading: true });
    try {
      // While the service account is being set up there isn't one to read with yet: the login typed there reads it (not saved).
      const r = pick.select?.id?.startsWith('dset-')
        ? await R.send('/api/ad/look/browse', { domain: $('dset-domain').value.trim(), username: $('dset-user').value.trim(), password: $('dset-password').value, dn })
        : await R.get(`/api/ad/browse?dn=${encodeURIComponent(dn)}`);
      inPlace.set(dn.toLowerCase(), { items: r.items, more: r.more });
    } catch (err) {
      inPlace.set(dn.toLowerCase(), { problem: err.message });
    }
    if (same(pick.sel, dn) && $('oupick-dialog').open) drawPickPage();
  };
  const insideHtml = (dn) => {
    const got = inPlace.get(dn.toLowerCase());
    if (!got) {
      lookInside(dn);
      return insideHtml(dn);
    }
    if (got.loading) return `<div class="op-items">${'<i class="sk op-sk"></i>'.repeat(5)}</div>`;
    if (got.problem) return `<p class="hint problem">${esc(got.problem)}</p>`;
    const counts = KINDS.map(([k, , one, many]) => {
      const n = got.items.filter((i) => i.kind === k).length;
      return n ? `${n}${got.more ? '+' : ''} ${n === 1 ? one : many}` : '';
    }).filter(Boolean);
    const said = counts.length > 1 ? `${counts.slice(0, -1).join(', ')} and ${counts[counts.length - 1]}` : counts[0] || 'Nothing in it yet';
    return `<p class="op-count">${esc(said)}</p>`
      + (got.items.length ? `<div class="op-items">${got.items.map((i) => `<div class="op-item" title="${esc(i.dn)}">${svg(PICK_ICON[i.kind] || 'folder')}<b>${esc(i.name)}</b>`
        + `${i.description ? `<small>${esc(i.description)}</small>` : ''}</div>`).join('')}</div>` : '');
  };
  const drawPickPage = () => {
    const n = pick.nodes.find((x) => same(x.dn, pick.sel));
    $('oupick-use').disabled = !n || !!n.fixed;
    if (!n) {
      $('oupick-page').innerHTML = '<header class="sub-head"><h2>Pick an OU</h2><p>Pick one on the left, or find it by name.</p></header>';
      return;
    }
    $('oupick-page').innerHTML = `<header class="sub-head"><h2>${esc(n.domain ? pick.domain : n.name)}</h2><p>${esc(n.domain ? 'The whole domain' : `${pick.domain} / ${n.path}`)}</p></header>`
      + (n.fixed ? `<p class="op-fixed">${svg('lock')}<span>${esc(n.fixed)}</span></p>` : '')
      + `<h3 class="op-h">Inside it</h3>${insideHtml(n.dn)}`
      + `<div class="ds-row op-dn"><div class="ds-l"><b>Distinguished name</b><span class="ds-code">${esc(n.dn)}</span></div></div>`;
  };
  const drawPick = () => {
    drawPickList();
    drawPickPage();
  };
  const openPick = (select) => {
    pick.select = select;
    pick.sel = select.value;
    pick.find = '';
    pick.nodes = [...select.options].map((o) => ({ dn: o.value, path: o.textContent, name: dnName(o.value), parent: dnParent(o.value), domain: isDomain(o.value), fixed: o.dataset.fixed || '' }));
    inPlace.clear(); // read afresh each time it opens
    const any = pick.nodes[0]?.dn || '';
    pick.domain = any.split(/(?<!\\),/).filter(isDomain).map((p) => p.slice(3)).join('.');
    $('oupick-lead').textContent = select.id === 'dset-scope'
      ? (ds.tab === 'make' ? 'Where it gets Full Control over users, groups and computers.' : `Where ${R.app()} makes its changes. The account must already have permission there.`)
      : PICK_LEADS[select.id] || '';
    $('oupick-find').value = '';
    drawPick();
    $('oupick-dialog').showModal();
    $('oupick-list').querySelector('[aria-current]')?.scrollIntoView({ block: 'nearest' });
    $('oupick-find').focus();
  };
  const usePick = () => {
    const s = pick.select;
    const o = s && [...s.options].find((x) => same(x.value, pick.sel));
    if (!o) return;
    $('oupick-dialog').close();
    if (s.value !== o.value || s.dataset.always !== undefined) {
      s.value = o.value;
      s.dispatchEvent(new Event('change', { bubbles: true }));
    }
  };
  document.addEventListener('click', (e) => {
    const b = e.target.closest('[data-browse-ou]');
    if (b) openPick($(b.dataset.browseOu));
  });
  const pickRow = (e) => {
    const a = e.target.closest('[data-ou]');
    if (!a) return false;
    pick.sel = a.dataset.ou;
    pick.find = '';
    $('oupick-find').value = '';
    drawPick();
    $('oupick-list').querySelector('[aria-current]')?.focus();
    return true;
  };
  $('oupick-list').addEventListener('click', pickRow);
  $('oupick-list').addEventListener('dblclick', (e) => { if (pickRow(e)) usePick(); });
  $('oupick-list').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return;
    e.preventDefault();
    const a = e.target.closest('[data-ou]');
    if (a && same(a.dataset.ou, pick.sel) && e.key === 'Enter') usePick();
    else pickRow(e);
  });
  $('oupick-find').addEventListener('input', () => {
    pick.find = $('oupick-find').value;
    drawPickList();
  });
  $('oupick-use').addEventListener('click', usePick);
  ['oupick-cancel', 'oupick-x'].forEach((id) => $(id).addEventListener('click', () => $('oupick-dialog').close()));

  // --- Setting up Rabbid Lab's AD account: its steps down the side ----------------------------------------------------
  // Create a new one: sign in as a domain admin (not saved), name it and pick its OU, then check and create it. Use one
  // I have: sign in as it, then check and use it. What it may manage starts as the whole domain; Review can change it.

  let look = null; // what the sign-in read: the domain, its OUs and where accounts go
  const ds = { tab: 'make', step: 0, reached: 0 };
  // First, which: a new one Rabbid Lab creates, or one that's already there. The steps after it follow the choice.
  const DCHOOSE = { key: 'choose', name: 'Choose', sub: 'New, or one you have', icon: 'shield', title: 'Which service account?', text: 'Rabbid Lab makes every Active Directory change as it.' };
  const DSTEPS = {
    make: [
      DCHOOSE,
      { key: 'signin', name: 'Sign in', sub: 'A domain admin login', icon: 'user', title: 'Sign in as a domain admin', text: 'Used once to read your OUs and create the account. Not saved.' },
      { key: 'account', name: 'Account', sub: 'Its name and OU', icon: 'user-plus', title: 'Name it, and pick where it goes', text: 'Pick an OU from the list, or Browse… to look through them.' },
      { key: 'review', name: 'Review', sub: 'Check, then create', icon: 'check', title: 'Check it, then create it', text: 'Nothing changes in Active Directory until you click Create service account.' },
    ],
    existing: [
      DCHOOSE,
      { key: 'signin', name: 'Sign in', sub: 'The service account', icon: 'user', title: 'Sign in as the service account', text: 'Rabbid Lab stores its password encrypted and makes every AD change as it.' },
      { key: 'review', name: 'Review', sub: 'Check, then use it', icon: 'check', title: 'Check it, then use it', text: 'Nothing changes in Active Directory. Rabbid Lab makes its changes as this account from then on.' },
    ],
  };
  const dsReview = () => {
    if (!look) return;
    const make = ds.tab === 'make';
    const user = $('dset-user').value.trim();
    const ou = $('dset-ou').selectedOptions[0];
    const scope = $('dset-scope').selectedOptions[0];
    const where = scope ? (isDomain(scope.value) ? `All of ${look.domain}` : scope.textContent) : '';
    const row = (icon, label, value, sub, act) => `<div class="dset-rv">${svg(icon)}<span>${label}</span><b>${esc(value)}<small>${esc(sub)}</small></b>${act}</div>`;
    const change = (i) => `<button type="button" class="dset-change" data-dstep="${i}">Change</button>`;
    const browse = '<button type="button" class="dset-change" data-browse-ou="dset-scope">Change</button>';
    $('dset-review').innerHTML = make
      ? row('user', 'Signed in as', user, `${look.domain} · not saved`, change(1))
        + row('user-plus', 'Service account', $('dset-account').value.trim(), `Password made by ${R.app()}, stored encrypted`, change(2))
        + row('folder', 'Created in', ou ? ou.textContent : '', ou ? ou.value : '', change(2))
        + row('shield', 'Can manage', where, 'Users, groups and computers', browse)
      : row('user', 'Service account', user, `${look.domain} · password stored encrypted`, change(1))
        + row('shield', 'Works in', where, 'Users, groups and computers', browse);
    $('dset-note2').textContent = make
      ? 'It gets Full Control over user, group and computer objects there, the same as the Delegation of Control wizard. Admin accounts protected by AdminSDHolder aren\'t affected.'
      : `${R.app()} only makes changes there. The account must already have permission to create and manage users, groups and computers there. ${R.app()} doesn't change its permissions.`;
  };
  const dsDraw = () => {
    const make = ds.tab === 'make';
    const steps = DSTEPS[ds.tab];
    const st = steps[ds.step];
    $('dset-choose').querySelectorAll('[data-adtab]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.adtab === ds.tab)));
    $('dset-steps').innerHTML = steps.map((s, i) => {
      const done = i !== ds.step && i < Math.max(ds.step, ds.reached);
      return `<li class="${done ? 'done' : i === ds.step ? 'now' : ''}${i !== ds.step && i <= ds.reached ? ' go' : ''}" data-dstep="${i}">`
        + `<i>${done ? svg('check') : i + 1}</i><b>${s.name}</b><small>${s.sub}</small></li>`;
    }).join('');
    $('dset-icon').setAttribute('href', `#i-${st.icon}`);
    $('dset-step-title').textContent = st.title;
    $('dset-step-text').textContent = st.text.replace(/Rabbid Lab/g, R.app()); // the steps are written before the lab's name is known
    $('dset-dialog').querySelectorAll('[data-pane]').forEach((p) => { p.hidden = p.dataset.pane !== st.key; });
    $('dset-user-label').textContent = make ? 'Domain admin' : 'Service account';
    $('dset-user').placeholder = make ? '' : 'Type its name, or pick it from AD';
    $('dset-browse').hidden = make;
    if (make) $('dset-drop').hidden = true;
    else if (document.activeElement === $('dset-user')) acctFind(); // switched to Use one I have while in the box
    $('dset-note').textContent = make ? 'These credentials are only used to create the service account. They are never saved.' : 'Its password is stored encrypted on this server.';
    $('dset-back').hidden = ds.step === 0;
    $('dset-go').innerHTML = st.key === 'review' ? `${svg('check')}${make ? 'Create service account' : 'Use this account'}` : `Next${svg('right')}`;
    if (st.key === 'review') dsReview();
  };
  const dsGo = (i) => {
    ds.step = i;
    ds.reached = Math.max(ds.reached, i);
    say('dset-problem', '');
    dsDraw();
  };
  // Set it up, from any page that needs Rabbid Lab's AD account.
  const openSetup = (domain) => {
    $('dset-domain').value = domain || ad?.domain || '';
    const me = R.me();
    $('dset-user').value = me?.source === 'ad' ? me.username : '';
    $('dset-password').value = '';
    $('dset-account').value = 'svc-rabbidlab';
    ['dset-user', 'dset-password'].forEach((id) => $(id).classList.remove('bad'));
    look = null;
    Object.assign(ds, { tab: 'make', step: 0, reached: 0 });
    say('dset-problem', '');
    dsDraw();
    $('dset-dialog').showModal();
    $('dset-choose').querySelector('[aria-pressed="true"]').focus();
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
          ? `${R.app()} makes its Active Directory changes as a dedicated service account (svc-rabbidlab). Create it with a domain admin login for ${info.domain}: ${R.app()} creates the account, delegates it permissions on users, groups and computers, and stores its password encrypted. The admin login is only used for this and isn't saved.${local ? noAccount : ''}`
          : 'Someone whose roles allow setting up the service account has to do this first.')
        + `</span></div><div class="dgate-acts">${allowed ? '<button type="button" class="primary" data-setup>Set up</button>' : ''}</div></div>`;
    }
    el.hidden = false;
    el.querySelector('[data-setup]')?.addEventListener('click', () => openSetup(info?.domain));
  };
  R.adSetupCard = setupCard;
  $('dset-local').addEventListener('click', () => $('dset-dialog').close());
  // Picking a card picks the way; a double-click goes straight on to signing in.
  $('dset-choose').addEventListener('click', (e) => {
    const b = e.target.closest('[data-adtab]');
    if (!b || b.dataset.adtab === ds.tab) return;
    Object.assign(ds, { tab: b.dataset.adtab, step: 0, reached: 0 });
    // Your own name fills the domain admin box; the service account is someone else.
    const mine = R.me()?.source === 'ad' ? R.me().username : '';
    if (ds.tab === 'existing' && $('dset-user').value === mine) $('dset-user').value = '';
    if (ds.tab === 'make' && !$('dset-user').value) $('dset-user').value = mine;
    say('dset-problem', '');
    dsDraw();
  });
  $('dset-choose').addEventListener('dblclick', (e) => { if (e.target.closest('[data-adtab]')) $('dset-go').click(); });
  // A step that's been reached opens again from the side, or from Change on Review.
  $('dset-dialog').addEventListener('click', (e) => {
    const s = e.target.closest('[data-dstep]');
    if (s && Number(s.dataset.dstep) <= ds.reached && Number(s.dataset.dstep) !== ds.step) dsGo(Number(s.dataset.dstep));
  });
  $('dset-scope').addEventListener('change', dsReview);
  $('dset-back').addEventListener('click', () => dsGo(ds.step - 1));
  // A changed sign-in has to be read again before the steps after it (Choose stays done).
  ['dset-domain', 'dset-user', 'dset-password'].forEach((id) => $(id).addEventListener('input', () => {
    ['dset-user', 'dset-password'].forEach((x) => $(x).classList.remove('bad'));
    if (ds.reached > 1) {
      ds.reached = 1;
      dsDraw();
    }
  }));
  // Use one I have: type the account, or pick it from AD as you type. AD is read as this server's own computer account,
  // so no login is needed for the list; with nothing typed it shows the ones that look like service accounts.
  let acctTimer = 0;
  let acctAsk = 0;
  let acctHits = [];
  const acctFind = () => {
    if (ds.tab !== 'existing') return;
    clearTimeout(acctTimer);
    const n = ++acctAsk;
    const q = $('dset-user').value.trim();
    acctTimer = setTimeout(async () => {
      let html;
      try {
        acctHits = await R.get(`/api/ad/look/accounts?domain=${encodeURIComponent($('dset-domain').value.trim())}&q=${encodeURIComponent(q)}`);
        if (n !== acctAsk) return;
        html = (q ? '' : '<p class="drop-note">Service accounts in Active Directory</p>')
          + (acctHits.map((h, i) => R.option(` data-acct="${i}"`, `<span class="pic">${svg('user')}</span>`, h.enabled ? h.username : `${h.username} (turned off)`,
            [h.name, h.description, h.where].filter(Boolean).join(' · '))).join('') || `<p class="drop-note">${q ? 'No account like that.' : 'None that look like service accounts. Type to find one.'}</p>`);
      } catch (err) {
        if (n !== acctAsk) return;
        html = `<p class="drop-note">${esc(err.message)}</p>`;
      }
      $('dset-drop').innerHTML = html;
      $('dset-drop').hidden = document.activeElement !== $('dset-user');
    }, q ? 250 : 0);
  };
  $('dset-user').addEventListener('input', acctFind);
  $('dset-user').addEventListener('focus', acctFind);
  $('dset-user').addEventListener('click', () => { if ($('dset-drop').hidden) acctFind(); }); // already in the box: a click shows them again
  $('dset-user').addEventListener('blur', () => setTimeout(() => { $('dset-drop').hidden = true; }, 150));
  $('dset-drop').addEventListener('mousedown', (e) => e.preventDefault()); // keeps the box focused while picking
  $('dset-drop').addEventListener('click', (e) => {
    const b = e.target.closest('[data-acct]');
    if (!b) return;
    $('dset-user').value = acctHits[Number(b.dataset.acct)].username;
    $('dset-drop').hidden = true;
    $('dset-user').dispatchEvent(new Event('input')); // a changed sign-in is read again
    clearTimeout(acctTimer); // without looking it up again
    $('dset-password').focus();
  });

  // Browse… beside the box: the OUs down the left, as Pick an OU shows them, and the accounts in the one picked on
  // the right; Find an account looks across the domain. Read as this server, like the list under the box.
  const ap = { places: [], nodes: [], at: '', sel: '', find: '', got: new Map() };
  const apDomain = () => $('dset-domain').value.trim();
  const apKids = (dn) => ap.nodes.filter((n) => same(n.parent, dn));
  const apTop = () => ap.nodes.filter((n) => !ap.nodes.some((m) => same(m.dn, n.parent)));
  const apOpenUnder = (dn) => same(ap.at, dn) || ap.at.toLowerCase().endsWith(`,${dn.toLowerCase()}`);
  const apRow = (n, top) => {
    const ks = apKids(n.dn);
    return `<a role="button" tabindex="0" data-ap-ou="${esc(n.dn)}" title="${esc(n.dn)}"${same(ap.at, n.dn) ? ' aria-current="true"' : ''}>`
      + `${top ? (ks.length ? FLASK : svg('folder')) : ''}<span class="nm">${esc(n.name)}</span>${ks.length ? `<small>${ks.length}</small>` : ''}</a>`
      + (ks.length ? `<div class="dkids${apOpenUnder(n.dn) ? ' open' : ''}">${ks.map((k) => apRow(k, false)).join('')}</div>` : '');
  };
  const apLoad = async (key, q) => {
    ap.got.set(key, { loading: true });
    try {
      const items = await R.get(`/api/ad/look/accounts?domain=${encodeURIComponent(apDomain())}${q ? `&q=${encodeURIComponent(q)}` : `&place=${encodeURIComponent(ap.at)}`}`);
      ap.got.set(key, { items });
    } catch (err) {
      ap.got.set(key, { problem: err.message });
    }
    if ($('acctpick-dialog').open) apDraw();
  };
  const apDraw = () => {
    $('acctpick-list').innerHTML = `<p class="rail-label">${esc(apDomain())}</p>` + apTop().map((n) => apRow(n, true)).join('');
    const q = ap.find.trim();
    const key = (q ? `?${q}` : ap.at).toLowerCase();
    if (!ap.got.has(key)) apLoad(key, q);
    const got = ap.got.get(key);
    const place = ap.places.find((p) => same(p.dn, ap.at));
    const head = q ? `<header class="sub-head"><h2>Found</h2><p>Accounts like “${esc(q)}” anywhere in ${esc(apDomain())}</p></header>`
      : `<header class="sub-head"><h2>${esc(place?.name || apDomain())}</h2><p>${esc(place ? `${apDomain()} / ${place.path}` : apDomain())}</p></header>`;
    const body = got.loading ? `<div class="op-items ap-items">${'<i class="sk op-sk"></i>'.repeat(6)}</div>`
      : got.problem ? `<p class="hint problem">${esc(got.problem)}</p>`
      : got.items.length ? `<p class="op-count">${got.items.length} account${got.items.length === 1 ? '' : 's'}</p><div class="op-items ap-items">${got.items.map((h) => `<button type="button" class="op-item ap-acct${same(ap.sel, h.username) ? ' on' : ''}" data-ap-acct="${esc(h.username)}"${h.enabled ? '' : ' data-off'}>`
        + `${svg('user')}<b>${esc(h.username)}${h.enabled ? '' : ' (turned off)'}</b><small>${esc([h.name, h.description, q ? h.where : ''].filter(Boolean).join(' · '))}</small></button>`).join('')}</div>`
      : `<p class="op-count">${q ? 'No account like that.' : 'No user accounts directly in here. Pick another OU on the left, or find it by name.'}</p>`;
    $('acctpick-page').innerHTML = head + body;
    $('acctpick-use').disabled = !ap.sel;
  };
  const apOpen = async () => {
    if (!apDomain()) {
      say('dset-problem', 'Type the domain first.');
      $('dset-domain').focus();
      return;
    }
    Object.assign(ap, { find: '', sel: $('dset-user').value.trim(), places: [], nodes: [] });
    ap.got.clear();
    $('acctpick-find').value = '';
    $('acctpick-list').innerHTML = '<i class="sk op-sk"></i>'.repeat(6);
    $('acctpick-page').innerHTML = '';
    $('acctpick-use').disabled = true;
    $('acctpick-dialog').showModal();
    try {
      ap.places = await R.get(`/api/ad/look/places?domain=${encodeURIComponent(apDomain())}`);
    } catch (err) {
      $('acctpick-list').innerHTML = '';
      $('acctpick-page').innerHTML = `<p class="hint problem">${esc(err.message)}</p>`;
      return;
    }
    ap.nodes = ap.places.map((p) => ({ dn: p.dn, name: dnName(p.dn), parent: dnParent(p.dn) }));
    // It opens where service accounts usually are: an OU called Service Accounts, else Users.
    ap.at = (ap.places.find((p) => /^service accounts?$/i.test(p.name)) || ap.places.find((p) => /^CN=Users,/i.test(p.dn)) || ap.places[0])?.dn || '';
    apDraw();
    $('acctpick-find').focus();
  };
  const apUse = () => {
    if (!ap.sel) return;
    $('dset-user').value = ap.sel;
    $('acctpick-dialog').close();
    $('dset-user').dispatchEvent(new Event('input')); // a changed sign-in is read again
    clearTimeout(acctTimer);
    $('dset-drop').hidden = true;
    $('dset-password').focus();
  };
  $('dset-browse').addEventListener('click', apOpen);
  $('acctpick-list').addEventListener('click', (e) => {
    const a = e.target.closest('[data-ap-ou]');
    if (!a) return;
    ap.at = a.dataset.apOu;
    ap.find = '';
    $('acctpick-find').value = '';
    apDraw();
  });
  let apTimer = 0;
  $('acctpick-find').addEventListener('input', () => {
    clearTimeout(apTimer);
    apTimer = setTimeout(() => {
      ap.find = $('acctpick-find').value;
      apDraw();
    }, 250);
  });
  $('acctpick-page').addEventListener('click', (e) => {
    const b = e.target.closest('[data-ap-acct]');
    if (!b) return;
    ap.sel = b.dataset.apAcct;
    apDraw();
  });
  $('acctpick-page').addEventListener('dblclick', (e) => { if (e.target.closest('[data-ap-acct]')) apUse(); });
  $('acctpick-use').addEventListener('click', apUse);
  ['acctpick-cancel', 'acctpick-x'].forEach((id) => $(id).addEventListener('click', () => $('acctpick-dialog').close()));
  $('dset-dialog').addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.matches('.dset-in input')) $('dset-go').click();
  });
  $('dset-cancel').addEventListener('click', () => {
    $('dset-password').value = '';
    $('dset-dialog').close();
  });
  $('dset-go').addEventListener('click', async () => {
    const make = ds.tab === 'make';
    const key = DSTEPS[ds.tab][ds.step].key;
    say('dset-problem', '');
    if (key === 'choose') {
      dsGo(1);
      $(make && R.me()?.source === 'ad' && $('dset-user').value ? 'dset-password' : 'dset-user').focus();
      return;
    }
    if (key === 'account') {
      if (!$('dset-account').value.trim()) {
        say('dset-problem', 'Name the service account.');
        $('dset-account').focus();
        return;
      }
      dsGo(ds.step + 1);
      return;
    }
    const sign = { domain: $('dset-domain').value.trim(), username: $('dset-user').value.trim(), password: $('dset-password').value };
    $('dset-go').disabled = true;
    R.busy($('dset-dialog'), key === 'signin' ? `Signing in to ${sign.domain || 'the domain'}…` : make ? 'Creating the service account…' : 'Checking the service account…');
    try {
      if (key === 'signin') {
        look = await R.send('/api/ad/look', sign);
        const opt = (p, selected) => `<option value="${esc(p.dn)}"${p.dn === selected ? ' selected' : ''}>${esc(p.path || p.name)}</option>`;
        const was = $('dset-scope').value;
        $('dset-ou').innerHTML = look.places.map((p) => opt(p, look.accountOu)).join('');
        $('dset-scope').innerHTML = `<option value="${esc(look.baseDn)}">All of ${esc(look.domain)}</option>` + look.places.filter((p) => !/^CN=/i.test(p.dn)).map((p) => opt(p, was)).join('');
        dsGo(ds.step + 1);
      } else {
        const answer = await R.send('/api/ad/connect', { ...sign, existing: !make, account: $('dset-account').value.trim(), ou: $('dset-ou').value, scope: $('dset-scope').value });
        $('dset-password').value = '';
        $('dset-dialog').close();
        say('dir-note', answer.warning || (make ? `Created the service account ${answer.state.account}. ${R.app()} makes its AD changes as it from now on.` : `${R.app()} makes its AD changes as ${answer.state.account} from now on.`));
        places = null;
        R.forget();
        refreshAd(true);
        document.dispatchEvent(new CustomEvent('rabbidlab:ad')); // the Org chart and Attribute browser read again
      }
    } catch (err) {
      // A wrong sign-in: back on the sign-in step, its two fields outlined, and the password ready to type again.
      const wrong = /username or password/i.test(err.message);
      if (wrong) {
        ds.reached = 1;
        dsGo(1);
      }
      say('dset-problem', err.message);
      if (wrong) {
        ['dset-user', 'dset-password'].forEach((id) => $(id).classList.add('bad'));
        $('dset-password').select();
      }
    } finally {
      R.busy($('dset-dialog'), '');
      $('dset-go').disabled = false;
    }
  });

  // Settings, Active Directory: the service account, where new accounts go, and disconnecting it. The gear opens it.
  $('dir-settings').addEventListener('click', () => { location.hash = '#settings/ad'; });
  const drawAdSettings = async (fresh) => {
    say('adset-problem', '');
    try {
      if (fresh || !ad) ad = await R.get('/api/ad');
    } catch (err) {
      say('adset-problem', err.message);
      return;
    }
    const s = ad?.state;
    $('as-account').textContent = s ? s.account : 'No service account';
    $('as-about').textContent = s
      ? `In ${s.domain}, ${s.made ? 'with delegated permissions on' : 'managing'} ${s.scopeName}. ${s.made ? 'Created' : 'Connected'} by ${s.by}${s.at ? ` on ${R.when(s.at)}` : ''}. Its password is stored encrypted on this server.`
      : ad?.domain ? `${R.app()} makes its changes in ${ad.domain} as a service account. Set one up to use the Active Directory pages.` : "This server isn't on a domain. Connect one to manage it from here.";
    $('as-setup').hidden = !!s;
    $('as-setup').textContent = ad?.domain ? 'Set up' : 'Connect a domain';
    ['as-ou-row', 'as-gou-row', 'as-cou-row'].forEach((id) => { $(id).hidden = !s; });
    $('as-danger').classList.toggle('gone', !s);
    if (!s) return;
    $('adset-stop-title').textContent = `Disconnect ${s.account}`;
    $('adset-stop-text').textContent = `${R.app()} deletes the stored password and stops using ${s.account}.${s.made ? ' The account and its delegated permissions stay in AD until you delete them.' : ''}`;
    await placesInto('adset-ou', s.newUsers, 'adset-problem');
    $('adset-gou').innerHTML = $('adset-ou').innerHTML;
    $('adset-cou').innerHTML = $('adset-ou').innerHTML;
    $('adset-gou').value = s.newGroups || s.newUsers;
    $('adset-cou').value = s.newComputers || '';
  };
  $('as-setup').addEventListener('click', () => openSetup(ad?.domain));
  $('dset-dialog').addEventListener('close', () => { if (R.where()[0] === 'settings' && R.where()[1] === 'ad') drawAdSettings(true); });
  // Each default OU saves as it changes: users, groups and computers.
  [['adset-ou', 'newUsers'], ['adset-gou', 'newGroups'], ['adset-cou', 'newComputers']].forEach(([id, field]) => $(id).addEventListener('change', async () => {
    say('as-done', '');
    try {
      await R.send('/api/ad/settings', { [field]: $(id).value });
      ad = await R.get('/api/ad');
      say('adset-problem', '');
    } catch (err) {
      say('adset-problem', err.message);
    }
  }));
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] === 'settings' && e.detail[1] === 'ad' && can('ad.connect')) drawAdSettings(true);
  });
  $('adset-stop').addEventListener('click', () => {
    const s = ad.state;
    sure(`Disconnect ${s.account}?`, `${R.app()} can't make any AD changes until a service account is set up again.${s.made ? ` ${s.account} stays in AD until you delete it.` : ''}`,
      'Disconnect', async () => {
        try {
          await R.send('/api/ad/disconnect', {});
          say('dir-note', s.made ? `Disconnected ${s.account}. Delete it in Active Directory Users and Computers if you won't use it again.` : `Disconnected ${s.account}.`);
          say('as-done', s.made ? `Disconnected ${s.account}. Delete it in Active Directory Users and Computers if you won't use it again.` : `Disconnected ${s.account}.`);
        } catch (err) {
          say('dir-problem', err.message);
        }
        places = null;
        R.forget();
        await refreshAd(true);
        drawAdSettings();
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
      if (fresh) {
        // Another page starts on its first tab with Find cleared; its columns, sort and filter stay as they were.
        $('dir-find').value = '';
        adTab = '';
      }
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
