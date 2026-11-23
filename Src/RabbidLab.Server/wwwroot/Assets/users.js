// Active Directory (#ad/<users|groups|computers>/<view>), changed as Rabbid Lab's own AD account, and the New account and
// password windows a computer's Accounts tab on Devices uses (accounts.js). console.js shares its helpers as window.RabbidLab.
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
  // A moment on one line: Today 12:04 PM, Yesterday 4:34 PM, Nov 12 1:34 PM. dayOf is its day.
  const dayOf = (iso) => {
    const d = new Date(iso);
    const back = Math.round((new Date().setHours(0, 0, 0, 0) - new Date(iso).setHours(0, 0, 0, 0)) / DAY);
    return back === 0 ? 'Today' : back === 1 ? 'Yesterday'
      : d.toLocaleDateString([], { month: 'short', day: 'numeric', ...(d.getFullYear() === new Date().getFullYear() ? {} : { year: 'numeric' }) });
  };
  const clock = (iso) => new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const stamp = (iso) => `${dayOf(iso)} ${clock(iso)}`;
  const kindWord = (kind) => (kind === 'ou' ? 'OU' : kind ? kind.charAt(0).toUpperCase() + kind.slice(1) : '');

  // --- Active Directory: what each page lists, its views down the left, its columns and its CSV -------------------

  let ad = null; // /api/ad: the domain, and Rabbid Lab's account once it's set up
  let adPage = 'users';
  let adView = 'all';
  const lists = {}; // what each page loaded, until the next show
  const more = {};
  let rows = [];
  let places = null;

  // What's ticked on Users, Groups, Computers and the AD audit (by each page's key), kept while the list is read again.
  const picked = { users: new Set(), groups: new Set(), computers: new Set(), audit: new Set() };
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
    $('dir-act').dataset.tip = sel.size ? `${count(sel.size, ...AD[adPage].noun)} selected` : `Select ${AD[adPage].noun[1]} first`;
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
  const TONE = { good: badge('Passed', 'green'), warn: badge('Warning', 'amber'), bad: badge('Failed', 'red'), info: badge('Info', 'grey'), unknown: badge('Unknown', 'grey') };
  // Domain health's areas, in order, each with its own icon.
  const AREA_ICON = { 'Passwords and lockout': 'lock', 'Privileged accounts': 'crown', 'Kerberos and delegation': 'ticket', 'Stale objects': 'hourglass',
    'Domain and forest': 'tree', 'Domain controllers': 'server', 'Certificates (AD CS)': 'certificate', Trusts: 'link', DNS: 'signpost' };
  const WHAT = { made: 'green', changed: 'grey', moved: 'grey', deleted: 'red', restored: 'amber' };
  // Each kind of change in the words Event Viewer uses.
  const WHAT_WORD = { made: 'Created', changed: 'Modified', moved: 'Moved', deleted: 'Deleted', restored: 'Restored' };
  const KIND_ICON = { user: 'user', group: 'users', computer: 'laptop', ou: 'folder', container: 'folder', other: 'folder' };

  // --- AD audit and History: values, the operation in a sentence, and new changes easing in -----------------------
  // An audit value on one line: missing when no Security log recorded it (or it wasn't read), - when it was empty.
  const auditVals = (vs, missing) => (vs == null ? `<span class="muted">${missing}</span>` : vs.length ? `<span class="ell">${esc(vs.join('; '))}</span>` : '<span class="muted">-</span>');
  const auditPic = (c, cls = '') => (c.kind === 'user' ? `<span class="pic user${cls}">${esc(initials(c.name))}</span>` : `<span class="pic${cls}">${svg(KIND_ICON[c.kind] || 'folder')}</span>`);
  // An operation (one LDAP request, its rows xs) as a sentence: Priya Nair modified 4 attributes on Greg Sutton.
  const opSay = (xs) => {
    const x = xs[0];
    const who = x.by ? `<b>${esc(x.by)}</b>` : '<b class="dir-op-nobody">Unknown user</b>';
    const obj = `<b>${esc(x.name)}</b>`;
    const kind = esc(x.kind === 'ou' ? 'OU' : x.kind);
    const first = (vs) => `${esc(vs[0])}${vs.length > 1 ? ` and ${vs.length - 1} more` : ''}`;
    const one = xs.length === 1 ? x : null;
    const what = ['made', 'deleted', 'restored', 'moved'].find((w) => xs.some((r) => r.what === w));
    if (what === 'made') return `${who} created ${kind} ${obj}`;
    if (what === 'deleted') return `${who} deleted ${kind} ${obj}`;
    if (what === 'restored') return `${who} restored ${kind} ${obj}`;
    if (what === 'moved') return `${who} ${xs.find((r) => r.what === 'moved').label === 'Name' ? 'renamed' : 'moved'} ${obj}`;
    if (one?.label === 'Member added' && one.new?.length) return `${who} added ${first(one.new)} to ${obj}`;
    if (one?.label === 'Member removed' && one.old?.length) return `${who} removed ${first(one.old)} from ${obj}`;
    return `${who} modified ${one ? esc(one.label) : `${xs.length} attributes`} on ${obj}`;
  };
  const plainSay = (xs) => opSay(xs).replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'");
  // Each operation's rows, for the list read last.
  let opsFor = { list: null, ops: new Map() };
  const opRows = (c) => {
    const list = lists.audit || [];
    if (opsFor.list !== list) {
      const ops = new Map();
      list.forEach((r) => { if (!ops.has(r.op)) ops.set(r.op, []); ops.get(r.op).push(r); });
      opsFor = { list, ops };
    }
    return opsFor.ops.get(c.op) || [c];
  };
  // A row that arrived while the page was open eases in once: when it was first seen is kept, so a redraw carries the
  // animation on (a negative delay) instead of starting it again.
  const IN_MS = 2400;
  const born = new Map();
  let auditSeen = null; // the ids in the last list read; null before the first
  const noteAudit = (list) => {
    const ids = new Set(list.map((c) => c.id));
    if (auditSeen) ids.forEach((id) => { if (!auditSeen.has(id)) born.set(id, Date.now()); });
    auditSeen = ids;
    for (const [id, t] of born) if (Date.now() - t > IN_MS) born.delete(id);
  };
  const ageOf = (c) => (born.has(c.id) ? Date.now() - born.get(c.id) : IN_MS);
  const freshRow = (c) => ageOf(c) < IN_MS;
  let recycle = null; // /api/ad/recycle: whether it's on, and what's in it

  const AD = {
    users: {
      title: 'Users', noun: ['user', 'users'], add: 'New user', key: (u) => u.username,
      lead: 'All user accounts in the domain. Use the views to find accounts that need attention.',
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
      lead: 'Security and distribution groups and their members.',
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
      lead: 'All computer accounts in the domain, including servers, workstations and inactive computers.',
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
      title: 'Domain health', noun: ['check', 'checks'], add: null, key: (s) => s.key,
      lead: 'Checks against Microsoft, PingCastle and Defender for Identity recommendations.',
      load: () => R.get('/api/ad/domain'),
      find: (s) => [s.name, s.area, s.now, s.about, s.source, ...(s.found || []).map((f) => f.name)],
      views: [
        ['View', [
          { key: 'all', label: 'All checks' },
          { key: 'off', label: 'Failed', tone: 'red', where: (s) => s.tone === 'bad' },
          { key: 'look', label: 'Warning', tone: 'amber', where: (s) => s.tone === 'warn' },
          { key: 'notes', label: 'Info', quiet: true, where: (s) => s.tone === 'info' },
          { key: 'unread', label: 'Unknown', quiet: true, where: (s) => s.tone === 'unknown' },
        ]],
        ['Area', Object.keys(AREA_ICON).map((a) => ({ key: a.toLowerCase().replace(/[^a-z]+/g, '-').replace(/-$/, ''), label: a, where: (s) => s.area === a }))],
      ],
      csv: [['Area', (s) => s.area], ['Check', (s) => s.name], ['Current', (s) => s.now], ['Recommended', (s) => s.recommended], ['Result', (s) => s.tone], ['Details', (s) => s.about],
        ['Found', (s) => (s.found || []).map((f) => f.name).join('; ')], ['Source', (s) => s.source]],
    },
    // Domain health's second tab, On each computer: security settings read from each DC, server and PC (dc-settings.js).
    // Its views are the kinds of computer; each counts its computers, and the list counts only those.
    dcs: {
      title: 'Domain health', noun: ['setting', 'settings'], add: null, key: (s) => s.key,
      lead: 'Security settings read from each domain controller, server and PC, against what Microsoft recommends.',
      load: (fresh, only) => R.dcSettings.load(fresh, only),
      find: (s) => [s.name, s.recommended, ...R.dcSettings.of(s, hk()).off, ...Object.values(s.values || {}).map((v) => v.value)],
      views: [
        ['Computers', [['all', 'All computers'], ['dc', 'Domain controllers'], ['server', 'Servers'], ['pc', 'PCs']].map(([key, label]) => ({
          key, label, where: key === 'all' ? undefined : (s) => s.kinds.includes(key), n: () => R.dcSettings.computersOf(lists.dcs, key).length,
        }))],
      ],
      csv: [['Setting', (s) => s.name], ['Severity', (s) => s.severity], ['Current', (s) => R.dcSettings.current(s, hk())], ['Recommended', (s) => s.recommended],
        ['Compliant on', (s) => `${R.dcSettings.of(s, hk()).good.length} of ${R.dcSettings.of(s, hk()).total}`], ['Not compliant on', (s) => R.dcSettings.of(s, hk()).off.join('; ')],
        ['Could not be read on', (s) => R.dcSettings.of(s, hk()).unread.join('; ')], ['Read from', (s) => s.readFrom], ['Policy setting', (s) => s.policy], ['Value', (s) => s.setTo]],
    },
    // One row per attribute change (AdAudit.cs), grouped by the operation it was part of. The DCs' state and how long
    // changes are kept come with the rows, as rows.info. Refresh reads the Security logs first (fresh).
    audit: {
      title: 'AD audit', noun: ['change', 'changes'], add: null, key: (c) => c.id,
      lead: 'Every attribute change with its old and new value, who made it, and the domain controller it was made on.',
      load: async (fresh) => {
        const j = await R.get(`/api/ad/audit${fresh ? '?again=true' : ''}`);
        more.audit = j.more;
        j.rows.info = { dcs: j.dcs, keep: j.keep, windows: j.windows, account: j.account };
        noteAudit(j.rows);
        return j.rows;
      },
      find: (c) => [c.name, c.where, c.label, c.attribute, c.by, c.account, c.dc, ...(c.old || []), ...(c.new || [])],
      group: { on: true, keys: ['op'] },
      views: [
        ['When', [
          { key: 'all', label: 'All changes' },
          { key: 'week', label: 'Last 7 days', where: (c) => Date.now() - new Date(c.at) < 7 * DAY },
          { key: 'day', label: 'Last 24 hours', where: (c) => Date.now() - new Date(c.at) < DAY },
        ]],
        ['Change', [
          { key: 'made', label: 'Created', where: (c) => c.what === 'made' },
          { key: 'changed', label: 'Modified', where: (c) => c.what === 'changed' },
          { key: 'moved', label: 'Moved or renamed', quiet: true, where: (c) => c.what === 'moved' },
          { key: 'deleted', label: 'Deleted', tone: 'red', where: (c) => c.what === 'deleted' },
          { key: 'restored', label: 'Restored', quiet: true, where: (c) => c.what === 'restored' },
        ]],
        ['Type', [
          { key: 'users', label: 'Users', where: (c) => c.kind === 'user' },
          { key: 'groups', label: 'Groups', where: (c) => c.kind === 'group' },
          { key: 'computers', label: 'Computers', where: (c) => c.kind === 'computer' },
          { key: 'ous', label: 'OUs', quiet: true, where: (c) => c.kind === 'ou' },
        ]],
        ['Recorded', [{ key: 'unrecorded', label: 'Not in a Security log', tone: 'amber', quiet: true, where: (c) => !c.event }]],
      ],
      csv: [['When', (c) => c.at], ['Modified by', (c) => c.by], ['Account', (c) => c.account], ['Object', (c) => c.name], ['Type', (c) => c.kind], ['Location', (c) => c.where],
        ['Change', (c) => c.what], ['Attribute', (c) => c.label], ['LDAP name', (c) => c.attribute], ['Old value', (c) => (c.old == null ? 'Not recorded' : c.old.join('; '))],
        ['New value', (c) => (c.new == null ? 'Not read' : c.new.join('; '))], ['Originating DC', (c) => c.dc], ['Event ID', (c) => c.event], ['Operation', (c) => c.op],
        ['Distinguished name', (c) => c.dn]],
    },
    recycle: {
      title: 'Recycle Bin', noun: ['deleted object', 'deleted objects'], add: null, key: (d) => d.dn,
      lead: 'Deleted users, groups, computers and OUs. Restore an object to bring it back with its attributes.',
      load: async () => {
        recycle = await R.get('/api/ad/recycle');
        more.recycle = recycle.more;
        return recycle.items;
      },
      find: (d) => [d.name, d.wasIn, d.username],
      views: [
        ['View', [
          { key: 'all', label: 'All objects' },
          { key: 'users', label: 'Users', where: (d) => d.kind === 'user' },
          { key: 'groups', label: 'Groups', where: (d) => d.kind === 'group' },
          { key: 'computers', label: 'Computers', where: (d) => d.kind === 'computer' },
          { key: 'ous', label: 'OUs', quiet: true, where: (d) => d.kind === 'ou' },
        ]],
      ],
      csv: [['Name', (d) => d.name], ['Type', (d) => d.kind], ['Last known location', (d) => d.wasIn], ['Deleted', (d) => d.deleted], ['Distinguished name', (d) => d.dn]],
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

  const dateCell = (iso) => (iso ? esc(day(iso)) : '<span class="muted">Never</span>');
  const endOf = (iso) => (iso ? esc(day(new Date(new Date(iso) - 1))) : '<span class="muted">Never</span>');
  const time = (iso) => (iso ? new Date(iso).getTime() : 0);
  // key, heading, width, the value (for sorting, and the cell unless it has its own), and options: on (shown by
  // default), fixed (always shown), cell. A width in pixels, or the default one for that column.
  // Sized so the columns that show at first fit the page without scrolling sideways (Role and Privileged since 1.1.205).
  const DEFAULT_PX = { name: 180, username: 165, upn: 230, email: 210, department: 140, title: 150, company: 140, office: 120, phone: 120, manager: 160,
    where: 145, description: 240, lastSignIn: 105, passwordSet: 130, passwordExpires: 130, accountExpires: 130, created: 110, status: 100, type: 110, scope: 120,
    members: 90, notes: 200, os: 175, host: 200, location: 140, role: 120, privileged: 105 };
  const minOf = (w) => Number((String(w).match(/(\d+)px/) || [0, 80])[1]);
  const col = (key, label, w, get, o = {}) => ({ key, label, w: typeof w === 'number' ? w : DEFAULT_PX[key] || minOf(w), get, ...o });
  // A yes-or-no column: Yes, or a grey No; it filters as yes or no.
  const yesCol = (key, label, get) => col(key, label, `${Math.max(110, label.length * 7 + 30)}px`, (x) => !!get(x),
    { cell: (x) => (get(x) ? 'Yes' : '<span class="muted">No</span>'), text: (x) => (get(x) ? 'Yes' : 'No') });
  const mono = (v) => (v ? `<span class="mono ell">${esc(v)}</span>` : '');
  // How many groups it's directly in, with their names in the hint.
  const groupsCell = (gs) => (gs?.length ? `<span class="num" data-tip="${esc(gs.map((g) => g.name).join(', '))}">${gs.length}</span>` : '<span class="muted">0</span>');
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
    col('status', 'Status', '150px', (u) => (u.locked ? 0 : !u.enabled ? 2 : 1), { on: true, filter: true, cell: (u) => userStatus(u) }),
    // One thing per column: what kind of account it is, apart from its status.
    col('role', 'Role', '150px', (u) => (u.service ? 0 : u.protected ? 1 : 2), { on: true,
      cell: (u) => (u.service ? chip('Service account', 'ember') : u.protected ? badge('Privileged', 'amber') : '<span class="muted">-</span>') }),
    // Where each person's offboarding is (offboard.js): the next stage and its day, failed, or complete.
    col('offboarding', 'Offboarding', '160px', (u) => R.offboard?.sortOf(u.username) ?? 0, { on: true,
      cell: (u) => R.offboard?.cell(u.username) || '<span class="muted">-</span>', text: (u) => R.offboard?.status(u.username) || '' }),
    // The rest of the account, as Active Directory Users and Computers has it: hidden until picked under Columns.
    col('first', 'First name', 'minmax(110px, 1fr)', (u) => u.first),
    col('last', 'Last name', 'minmax(110px, 1fr)', (u) => u.last),
    col('initials', 'Initials', '90px', (u) => u.more?.initials),
    col('mobile', 'Mobile', 'minmax(110px, .8fr)', (u) => u.more?.mobile),
    col('ipPhone', 'IP phone', 'minmax(110px, .8fr)', (u) => u.more?.ipPhone),
    col('fax', 'Fax', 'minmax(110px, .8fr)', (u) => u.more?.fax),
    col('street', 'Street', 'minmax(160px, 1.2fr)', (u) => u.more?.street),
    col('city', 'City', 'minmax(110px, 1fr)', (u) => u.more?.city),
    col('state', 'State or province', 'minmax(120px, 1fr)', (u) => u.more?.state),
    col('postalCode', 'ZIP or postal code', '130px', (u) => u.more?.postalCode),
    col('country', 'Country', 'minmax(110px, 1fr)', (u) => u.more?.country),
    col('employeeId', 'Employee ID', '120px', (u) => u.more?.employeeId),
    col('employeeNumber', 'Employee number', '140px', (u) => u.more?.employeeNumber),
    col('employeeType', 'Employee type', '130px', (u) => u.more?.employeeType),
    col('homeFolder', 'Home folder', 'minmax(170px, 1.2fr)', (u) => u.more?.homeFolder, { cell: (u) => mono(u.more?.homeFolder) }),
    col('homeDrive', 'Home drive', '100px', (u) => u.more?.homeDrive),
    col('profilePath', 'Profile path', 'minmax(170px, 1.2fr)', (u) => u.more?.profilePath, { cell: (u) => mono(u.more?.profilePath) }),
    col('logonScript', 'Logon script', 'minmax(130px, 1fr)', (u) => u.more?.logonScript),
    col('webPage', 'Web page', 'minmax(140px, 1fr)', (u) => u.more?.webPage),
    col('notes', 'Notes', 'minmax(160px, 1.2fr)', (u) => u.more?.notes),
    col('aliases', 'Email aliases', 'minmax(180px, 1.2fr)', (u) => (u.more?.aliases || []).join(', ')),
    col('memberOf', 'Member of', '110px', (u) => (u.groups || []).length, { cell: (u) => groupsCell(u.groups) }),
    col('logons', 'Logon count', '115px', (u) => u.more?.logons ?? ''),
    col('badPasswords', 'Bad password count', '150px', (u) => u.more?.badPasswords ?? ''),
    col('lastBadPassword', 'Last bad password', '145px', (u) => time(u.more?.lastBadPassword), { cell: (u) => dateCell(u.more?.lastBadPassword) }),
    col('changed', 'Changed', '110px', (u) => time(u.more?.changed), { cell: (u) => dateCell(u.more?.changed) }),
    col('sid', 'SID', 'minmax(230px, 1.2fr)', (u) => u.more?.sid, { cell: (u) => mono(u.more?.sid) }),
    col('dn', 'Distinguished name', 'minmax(260px, 1.6fr)', (u) => u.dn, { cell: (u) => mono(u.dn) }),
    yesCol('enabled', 'Enabled', (u) => u.enabled),
    yesCol('locked', 'Locked out', (u) => u.locked),
    yesCol('neverExpires', 'Password never expires', (u) => u.neverExpires),
    yesCol('mustChange', 'Must change password', (u) => u.mustChange),
    yesCol('smartcard', 'Smart card required', (u) => u.smartcard),
    yesCol('sensitive', 'Account is sensitive and cannot be delegated', (u) => u.sensitive),
    yesCol('delegation', 'Trusted for delegation', (u) => u.more?.delegation),
    yesCol('noPassword', 'Password not required', (u) => u.more?.noPasswordNeeded),
    yesCol('reversible', 'Reversible encryption', (u) => u.reversible),
    yesCol('desOnly', 'DES encryption only', (u) => u.desOnly),
    yesCol('noPreauth', 'No Kerberos preauthentication', (u) => u.noPreauth),
    ...Array.from({ length: 15 }, (_, i) => col(`ext${i + 1}`, `Custom attribute ${i + 1}`, 'minmax(130px, 1fr)', (u) => u.more?.custom?.[i + 1])),
  ];
  AD.groups.cols = [
    col('name', 'Group', 'minmax(220px, 1.8fr)', (g) => g.name, { fixed: true, cell: (g) => `<div class="who"><span class="pic">${svg('users')}</span><b class="ell">${esc(g.name)}</b></div>` }),
    col('type', 'Group type', '150px', (g) => (g.security ? 'Security' : 'Distribution'), { on: true,
      cell: (g) => (g.security ? 'Security' : 'Distribution') }),
    col('privileged', 'Privileged', '110px', (g) => (g.protected ? 0 : 1), { on: true, cell: (g) => (g.protected ? badge('Privileged', 'amber') : '<span class="muted">-</span>') }),
    col('scope', 'Group scope', '120px', (g) => g.scope, { on: true }),
    col('members', 'Members', '90px', (g) => g.members, { on: true, cell: (g) => `<span class="num">${g.moreMembers ? '1,500+' : g.members.toLocaleString()}</span>` }),
    col('where', 'Organizational unit', 'minmax(140px, 1fr)', (g) => g.where || ad.state.domain, { on: true }),
    col('description', 'Description', 'minmax(160px, 1.4fr)', (g) => g.description),
    col('email', 'Email', 'minmax(160px, 1fr)', (g) => g.email),
    col('notes', 'Notes', 'minmax(160px, 1.2fr)', (g) => g.notes),
    col('username', 'Pre-Windows 2000 name', 'minmax(170px, 1fr)', (g) => g.username, { cell: (g) => mono(g.username) }),
    col('managedBy', 'Managed by', 'minmax(140px, 1fr)', (g) => g.managedBy),
    col('memberOf', 'Member of', '110px', (g) => (g.groups || []).length, { cell: (g) => groupsCell(g.groups) }),
    col('created', 'Created', '110px', (g) => time(g.created), { cell: (g) => dateCell(g.created) }),
    col('changed', 'Changed', '110px', (g) => time(g.changed), { cell: (g) => dateCell(g.changed) }),
    col('sid', 'SID', 'minmax(230px, 1.2fr)', (g) => g.sid, { cell: (g) => mono(g.sid) }),
    col('dn', 'Distinguished name', 'minmax(260px, 1.6fr)', (g) => g.dn, { cell: (g) => mono(g.dn) }),
    yesCol('system', 'Built-in', (g) => g.system),
  ];
  AD.computers.cols = [
    col('name', 'Computer', 'minmax(200px, 1.4fr)', (c) => c.name, { fixed: true, cell: (c) => `<div class="who"><span class="pic">${svg(server(c) ? 'server' : 'laptop')}</span><b class="ell">${esc(c.name)}</b></div>` }),
    col('os', 'Operating system', 'minmax(170px, 1.2fr)', (c) => c.os, { on: true }),
    col('osVersion', 'OS version', '120px', (c) => c.osVersion),
    col('host', 'DNS host name', 'minmax(170px, 1fr)', (c) => c.host),
    col('where', 'Organizational unit', 'minmax(140px, 1fr)', (c) => c.where || ad.state.domain, { on: true }),
    col('description', 'Description', 'minmax(160px, 1.2fr)', (c) => c.description),
    col('location', 'Location', 'minmax(120px, 1fr)', (c) => c.location),
    col('lastSignIn', 'Last sign-in', '110px', (c) => time(c.lastSignIn), { on: true, cell: (c) => esc(ago(c.lastSignIn)) }),
    col('created', 'Created', '110px', (c) => time(c.created), { cell: (c) => dateCell(c.created) }),
    col('status', 'Status', '150px', (c) => (!c.enabled ? 2 : stale(c.lastSignIn) ? 1 : 0), { on: true, filter: true, cell: (c) => pcStatus(c) }),
    // One thing per column: what the computer is, apart from its status.
    col('role', 'Role', '150px', (c) => (c.controller ? 0 : server(c) ? 1 : 2), { on: true, cell: (c) => esc(c.controller ? 'Domain controller' : server(c) ? 'Server' : 'Workstation') }),
    col('username', 'Pre-Windows 2000 name', 'minmax(170px, 1fr)', (c) => c.username, { cell: (c) => mono(c.username) }),
    col('servicePack', 'Service pack', '120px', (c) => c.servicePack),
    col('managedBy', 'Managed by', 'minmax(140px, 1fr)', (c) => c.managedBy),
    col('memberOf', 'Member of', '110px', (c) => (c.groups || []).length, { cell: (c) => groupsCell(c.groups) }),
    col('passwordSet', 'Password last set', '140px', (c) => time(c.passwordSet), { cell: (c) => dateCell(c.passwordSet) }),
    col('changed', 'Changed', '110px', (c) => time(c.changed), { cell: (c) => dateCell(c.changed) }),
    col('lapsExpires', 'LAPS password expires', '170px', (c) => time(c.lapsExpires), { cell: (c) => (c.lapsExpires ? dateCell(c.lapsExpires) : '<span class="muted">No LAPS password</span>') }),
    col('spns', 'Service principal names', '180px', (c) => c.spns ?? 0),
    col('encryption', 'Kerberos encryption', 'minmax(150px, 1fr)', (c) => c.encryption || ''),
    col('sid', 'SID', 'minmax(230px, 1.2fr)', (c) => c.sid, { cell: (c) => mono(c.sid) }),
    col('dn', 'Distinguished name', 'minmax(260px, 1.6fr)', (c) => c.dn, { cell: (c) => mono(c.dn) }),
    yesCol('enabled', 'Enabled', (c) => c.enabled),
    yesCol('controller', 'Domain controller', (c) => c.controller),
    yesCol('delegation', 'Trusted for delegation', (c) => c.delegation),
  ];

  AD.domain.cols = [
    col('name', 'Check', 300, (s) => s.name, { fixed: true, cell: (s) => `<div class="who"><span class="pic">${svg(AREA_ICON[s.area] || 'shield')}</span><b class="ell">${esc(s.name)}</b></div>` }),
    col('area', 'Area', 170, (s) => Object.keys(AREA_ICON).indexOf(s.area), { cell: (s) => esc(s.area) }),
    col('now', 'Current', 200, (s) => s.now, { on: true }),
    col('recommended', 'Recommended', 210, (s) => s.recommended, { on: true, cell: (s) => `<span class="muted ell">${esc(s.recommended)}</span>` }),
    col('result', 'Result', 170, (s) => ({ bad: 0, warn: 1, info: 2, unknown: 3, good: 4 })[s.tone] ?? 5, { on: true, filter: true, cell: (s) => TONE[s.tone] || '' }),
    col('about', 'Details', 320, (s) => s.about),
    col('source', 'Source', 240, (s) => s.source || '', { cell: (s) => `<span class="muted ell">${esc(s.source || '')}</span>` }),
  ];
  // One row per setting, as wide with 900 computers as with 2: how many are compliant and the first of those that are not,
  // counting the kind of computer whose view is open.
  const dcsOf = (s) => R.dcSettings.of(s, hk());
  AD.dcs.cols = [
    col('name', 'Setting', 240, (s) => s.name, { fixed: true, cell: (s) => `<div class="who"><span class="pic">${svg('shield')}</span><b class="ell">${esc(s.name)}</b></div>` }),
    col('severity', 'Severity', 90, (s) => R.dcSettings.sevRank(s) * 1000 - dcsOf(s).off.length, { on: true, filter: true, cell: (s) => R.dcSettings.sevCell(s), text: (s) => s.severity }),
    col('current', 'Current', 172, (s) => R.dcSettings.current(s, hk()), { on: true, cell: (s) => R.dcSettings.currentCell(s, hk()) }),
    col('recommended', 'Recommended', 124, (s) => s.recommended, { on: true, cell: (s) => `<span class="muted ell">${esc(s.recommended)}</span>` }),
    col('good', 'Compliant on', 116, (s) => (dcsOf(s).total ? dcsOf(s).good.length / dcsOf(s).total : 2), { on: true, cell: (s) => R.dcSettings.countCell(s, hk()),
      text: (s) => `${dcsOf(s).good.length} of ${dcsOf(s).total}` }),
    col('off', 'Not compliant on', 190, (s) => -dcsOf(s).off.length, { on: true, cell: (s) => R.dcSettings.few(dcsOf(s).off), text: (s) => dcsOf(s).off.join(', ') }),
    col('unread', 'Could not be read on', 220, (s) => -dcsOf(s).unread.length, { cell: (s) => R.dcSettings.few(dcsOf(s).unread), text: (s) => dcsOf(s).unread.join(', ') }),
    col('readFrom', 'Read from', 260, (s) => s.readFrom, { cell: (s) => `<span class="muted ell">${esc(s.readFrom)}</span>` }),
    col('policy', 'Policy setting', 320, (s) => s.policy),
  ];
  const oldText = (c) => (c.old == null ? 'Not recorded' : c.old.join('; '));
  const newText = (c) => (c.new == null ? 'Not read' : c.new.join('; '));
  AD.audit.cols = [
    // Sized so the columns that show at first fit the page without scrolling sideways.
    col('when', 'When', 112, (c) => time(c.at), { on: true, cell: (c) => esc(stamp(c.at)) }),
    col('by', 'Modified by', 104, (c) => c.by || '', { on: true, filter: true, cell: (c) => (c.by ? `<span class="ell">${esc(c.by)}</span>` : '<span class="muted">Unknown</span>') }),
    col('name', 'Object', 132, (c) => c.name, { fixed: true, filter: true, cell: (c) => `<div class="who">${auditPic(c)}<b class="ell">${esc(c.name)}</b></div>` }),
    col('label', 'Attribute', 104, (c) => c.label, { on: true, filter: true }),
    col('old', 'Old value', 104, oldText, { on: true, nofilter: true, cell: (c) => `<span class="dir-old">${auditVals(c.old, 'Not recorded')}</span>`, text: oldText }),
    col('new', 'New value', 104, newText, { on: true, nofilter: true, cell: (c) => auditVals(c.new, 'Not read'), text: newText }),
    col('dc', 'Originating DC', 112, (c) => c.dc || '', { on: true, filter: true }),
    col('what', 'Change', 110, (c) => c.what, { cell: (c) => badge(WHAT_WORD[c.what] || c.what, WHAT[c.what] || 'grey') }),
    col('kind', 'Type', 100, (c) => c.kind, { cell: (c) => esc(kindWord(c.kind)) }),
    col('where', 'Location', 180, (c) => c.where || ad.state.domain),
    col('account', 'Account', 170, (c) => c.account || ''),
    col('attribute', 'LDAP name', 170, (c) => c.attribute, { cell: (c) => mono(c.attribute) }),
    col('event', 'Event ID', 100, (c) => c.event || 0, { cell: (c) => (c.event ? esc(String(c.event)) : '<span class="muted">None</span>'), text: (c) => c.event || '' }),
    // Group by Operation (on by default) puts each operation's rows under a sentence that says what was done.
    col('op', 'Operation', 280, (c) => time(c.at), { nofilter: true, nosort: true, cell: (c) => `<span class="ell">${esc(plainSay(opRows(c)))}</span>`, text: (c) => plainSay(opRows(c)),
      group: {
        key: (c) => c.op,
        rank: (c) => -time(c.at),
        head: (xs) => `${xs[0].by ? `<span class="pic user small">${esc(initials(xs[0].by))}</span>` : `<span class="pic small">${svg('server')}</span>`}<span class="ell dir-op-say">${opSay(xs)}</span>`,
        side: (xs) => `<span class="dir-op-side"><span>${esc(stamp(xs[0].at))}</span>${xs[0].dc ? `<span class="dir-op-dc">${esc(xs[0].dc)}</span>` : ''}</span>`,
        cls: (xs) => `dir-op${xs.some(freshRow) ? ' dir-audit-in' : ''}`,
      } }),
    col('dn', 'Distinguished name', 320, (c) => c.dn, { cell: (c) => mono(c.dn) }),
  ];
  AD.recycle.cols = [
    col('name', 'Name', 240, (d) => d.name, { fixed: true, cell: (d) => `<div class="who"><span class="pic">${svg(KIND_ICON[d.kind] || 'folder')}</span><b class="ell">${esc(d.name)}</b></div>` }),
    col('username', 'Logon name', 160, (d) => d.username),
    col('kind', 'Type', 110, (d) => d.kind, { on: true, cell: (d) => esc(kindWord(d.kind)) }),
    col('wasIn', 'Last known location', 220, (d) => d.wasIn || ad.state.domain, { on: true }),
    col('deleted', 'Deleted', 130, (d) => time(d.deleted), { on: true, cell: (d) => esc(d.deleted ? ago(d.deleted) : '-') }),
    col('dn', 'Distinguished name', 320, (d) => d.dn),
  ];
  // How each page sorts until a heading is clicked.
  ['users', 'groups', 'computers'].forEach((k) => { AD[k].sort = { key: 'name', dir: 1 }; });
  AD.audit.sort = { key: 'when', dir: -1 };
  AD.dcs.sort = { key: 'severity', dir: 1 };
  AD.recycle.sort = { key: 'deleted', dir: -1 };

  // Every AD list is a data grid (grid.js): columns, widths and sort kept per page in this browser. Users, Groups,
  // Computers and the AD audit add a tick on the left and ⋯ on the right.
  let emptyNow = '';
  let adFailed = ''; // why the open page couldn't be read, until it's read again
  let orgFailed = false; // the Org chart's people couldn't be counted, for the banner
  // Role (Users, Computers) and Privileged (Groups) show by default since 1.1.205, out of Status and Group type:
  // put once into a column list someone already chose, keeping the rest of it.
  try {
    for (const [list, key] of [['users', 'role'], ['computers', 'role'], ['groups', 'privileged'], ['dcs', 'current']]) {
      const k = `rabbidlab.ad.cols.${list}`;
      const chosen = JSON.parse(localStorage.getItem(k) || 'null');
      if (Array.isArray(chosen) && !chosen.includes(key) && !localStorage.getItem(`${k}.${key}`)) localStorage.setItem(k, JSON.stringify([...chosen, key]));
      localStorage.setItem(`${k}.${key}`, '1');
    }
  } catch {
    // Not kept in this browser: the defaults show them.
  }
  const adGrid = R.grid({
    el: $('dir-table'),
    store: 'rabbidlab.ad',
    key: () => adPage,
    cols: () => page().cols,
    sort: () => page().sort || null,
    group: () => page().group || null,
    rowClass: (x) => (adPage === 'audit' && freshRow(x) ? 'dir-audit-in' : ''),
    lead: () => (picked[adPage] ? { w: 18, head: '<label class="tk"><input type="checkbox" data-tick-all aria-label="Select all"></label>',
      cell: (x) => `<label class="tk"><input type="checkbox" data-tick="${esc(keyOf(x))}" aria-label="Select ${esc(x.name || x.username)}"></label>` } : null),
    tail: () => (picked[adPage] ? { w: 28, cell: (x, i) => `<div class="tr-more"><button type="button" class="at-more" data-row-menu="${i}" data-tip="Actions" aria-label="Actions for ${esc(x.name || x.username)}">${svg('dots')}</button></div>` } : null),
    row: (x, i) => ` tabindex="0" data-i="${i}"`,
    empty: () => emptyNow, loading: () => !lists[adPage] && !adFailed && can('ad.view'),
    redraw: () => drawAd(),
  });
  const PAGE_ICON = { users: 'user', groups: 'users', computers: 'laptop', domain: 'shield', dcs: 'server', audit: 'history', recycle: 'trash' };
  // Domain health's two tabs are two pages: its checks, and its settings on each DC. Both sit under its entry down the left.
  const HEALTH = ['domain', 'dcs'];
  const healthAsked = {};
  let dwKind = 'all'; // the kind an open On each computer setting was drawn for
  // On each computer: the kind of computer whose view is open (all, dc, server or pc).
  function hk() { return adPage === 'dcs' && ['dc', 'server', 'pc'].includes(adView) ? adView : 'all'; }
  const railOf = (k) => (k === 'dcs' ? 'domain' : k);
  const total = {}; // how many each page had when it was last read, for the list down the left

  // The filter's fields: what each can be asked, by kind (filter.js has the tests).
  const fld = (key, label, type, get, o = {}) => ({ key, label, type, get, ...o });
  const OU = (x) => x.where || '';
  const IN = (x) => (x.groups || []).map((g) => g.name);
  AD.users.fields = [
    ['Account', [fld('enabled', 'Enabled', 'yes', (u) => u.enabled), fld('locked', 'Locked out', 'yes', (u) => u.locked), fld('protected', 'Privileged', 'yes', (u) => u.protected),
      fld('neverExpires', 'Password never expires', 'yes', (u) => u.neverExpires), fld('mustChange', 'User must change password at next sign-in', 'yes', (u) => u.mustChange),
      fld('lastSignIn', 'Last sign-in', 'days', (u) => u.lastSignIn, { never: true }), fld('passwordSet', 'Password last set', 'days', (u) => u.passwordSet),
      fld('passwordExpires', 'Password expires', 'until', (u) => (u.neverExpires ? null : u.passwordExpires)), fld('accountExpires', 'Account expires', 'until', (u) => u.accountExpires),
      fld('created', 'Created', 'days', (u) => u.created), fld('changed', 'Changed', 'days', (u) => u.more?.changed),
      fld('lastBadPassword', 'Last bad password', 'days', (u) => u.more?.lastBadPassword), fld('badPasswords', 'Bad password count', 'number', (u) => u.more?.badPasswords),
      fld('logons', 'Logon count', 'number', (u) => u.more?.logons)]],
    ['Security', [fld('smartcard', 'Smart card required', 'yes', (u) => u.smartcard), fld('sensitive', 'Account is sensitive and cannot be delegated', 'yes', (u) => u.sensitive),
      fld('delegation', 'Trusted for delegation', 'yes', (u) => u.more?.delegation), fld('noPassword', 'Password not required', 'yes', (u) => u.more?.noPasswordNeeded),
      fld('reversible', 'Reversible encryption', 'yes', (u) => u.reversible), fld('desOnly', 'DES encryption only', 'yes', (u) => u.desOnly),
      fld('noPreauth', 'No Kerberos preauthentication', 'yes', (u) => u.noPreauth)]],
    ['Identity', [fld('name', 'Name', 'text', (u) => u.name), fld('first', 'First name', 'text', (u) => u.first), fld('last', 'Last name', 'text', (u) => u.last),
      fld('initials', 'Initials', 'text', (u) => u.more?.initials), fld('username', 'Pre-Windows 2000 logon name', 'text', (u) => u.username), fld('upn', 'User principal name', 'text', (u) => u.upn),
      fld('email', 'Email', 'text', (u) => u.email), fld('aliases', 'Email aliases', 'text', (u) => (u.more?.aliases || []).join(', ')), fld('description', 'Description', 'text', (u) => u.description),
      fld('notes', 'Notes', 'text', (u) => u.more?.notes), fld('sid', 'SID', 'text', (u) => u.more?.sid), fld('dn', 'Distinguished name', 'text', (u) => u.dn)]],
    ['Organization', [fld('department', 'Department', 'choice', (u) => u.department), fld('title', 'Job title', 'choice', (u) => u.title), fld('company', 'Company', 'choice', (u) => u.company),
      fld('office', 'Office', 'choice', (u) => u.office), fld('manager', 'Manager', 'choice', (u) => u.manager), fld('where', 'Organizational unit', 'ou', OU),
      fld('employeeId', 'Employee ID', 'text', (u) => u.more?.employeeId), fld('employeeNumber', 'Employee number', 'text', (u) => u.more?.employeeNumber),
      fld('employeeType', 'Employee type', 'choice', (u) => u.more?.employeeType)]],
    ['Contact and address', [fld('phone', 'Phone', 'text', (u) => u.phone), fld('mobile', 'Mobile', 'text', (u) => u.more?.mobile), fld('ipPhone', 'IP phone', 'text', (u) => u.more?.ipPhone),
      fld('fax', 'Fax', 'text', (u) => u.more?.fax), fld('street', 'Street', 'text', (u) => u.more?.street), fld('city', 'City', 'choice', (u) => u.more?.city),
      fld('state', 'State or province', 'choice', (u) => u.more?.state), fld('postalCode', 'ZIP or postal code', 'text', (u) => u.more?.postalCode),
      fld('country', 'Country', 'choice', (u) => u.more?.country), fld('webPage', 'Web page', 'text', (u) => u.more?.webPage)]],
    ['Profile', [fld('homeFolder', 'Home folder', 'text', (u) => u.more?.homeFolder), fld('homeDrive', 'Home drive', 'choice', (u) => u.more?.homeDrive),
      fld('profilePath', 'Profile path', 'text', (u) => u.more?.profilePath), fld('logonScript', 'Logon script', 'choice', (u) => u.more?.logonScript)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN), fld('memberOf', 'Number of groups', 'number', (u) => (u.groups || []).length)]],
    ['Custom attributes', Array.from({ length: 15 }, (_, i) => fld(`ext${i + 1}`, `Custom attribute ${i + 1}`, 'text', (u) => u.more?.custom?.[i + 1]))],
  ];
  AD.groups.fields = [
    ['Group', [fld('name', 'Name', 'text', (g) => g.name), fld('description', 'Description', 'text', (g) => g.description), fld('type', 'Group type', 'choice', (g) => (g.security ? 'Security' : 'Distribution')),
      fld('scope', 'Group scope', 'choice', (g) => g.scope), fld('members', 'Members', 'number', (g) => g.members), fld('protected', 'Privileged', 'yes', (g) => g.protected),
      fld('email', 'Email', 'text', (g) => g.email), fld('where', 'Organizational unit', 'ou', OU), fld('username', 'Pre-Windows 2000 name', 'text', (g) => g.username),
      fld('managedBy', 'Managed by', 'choice', (g) => g.managedBy), fld('notes', 'Notes', 'text', (g) => g.notes), fld('system', 'Built-in', 'yes', (g) => g.system),
      fld('created', 'Created', 'days', (g) => g.created), fld('changed', 'Changed', 'days', (g) => g.changed), fld('sid', 'SID', 'text', (g) => g.sid),
      fld('dn', 'Distinguished name', 'text', (g) => g.dn)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN), fld('memberOf', 'Number of groups', 'number', (g) => (g.groups || []).length)]],
  ];
  AD.computers.fields = [
    ['Computer', [fld('name', 'Name', 'text', (c) => c.name), fld('host', 'DNS host name', 'text', (c) => c.host), fld('description', 'Description', 'text', (c) => c.description),
      fld('os', 'Operating system', 'choice', (c) => c.os), fld('osVersion', 'OS version', 'choice', (c) => c.osVersion), fld('servicePack', 'Service pack', 'choice', (c) => c.servicePack),
      fld('location', 'Location', 'choice', (c) => c.location), fld('where', 'Organizational unit', 'ou', OU), fld('username', 'Pre-Windows 2000 name', 'text', (c) => c.username),
      fld('managedBy', 'Managed by', 'choice', (c) => c.managedBy), fld('sid', 'SID', 'text', (c) => c.sid), fld('dn', 'Distinguished name', 'text', (c) => c.dn)]],
    ['Account', [fld('enabled', 'Enabled', 'yes', (c) => c.enabled), fld('controller', 'Domain controller', 'yes', (c) => c.controller),
      fld('lastSignIn', 'Last sign-in', 'days', (c) => c.lastSignIn, { never: true }), fld('passwordSet', 'Password last set', 'days', (c) => c.passwordSet),
      fld('created', 'Created', 'days', (c) => c.created), fld('changed', 'Changed', 'days', (c) => c.changed),
      fld('lapsExpires', 'LAPS password expires', 'until', (c) => c.lapsExpires)]],
    ['Security', [fld('delegation', 'Trusted for delegation', 'yes', (c) => c.delegation), fld('spns', 'Service principal names', 'number', (c) => c.spns ?? 0),
      fld('encryption', 'Kerberos encryption', 'choice', (c) => c.encryption)]],
    ['Membership', [fld('groups', 'Member of group', 'member', IN), fld('memberOf', 'Number of groups', 'number', (c) => (c.groups || []).length)]],
  ];
  // The filter bar over the list (filter.js, the same for every list): a search, a chip for each column worth filtering
  // by, saved filters, and the conditions window with the fields above. What's picked stays per page until it's cleared
  // or the console is opened again.
  const adBar = R.filter.bar({ el: $('dir-fbar'), store: 'rabbidlab.ad', key: () => adPage, grid: () => adGrid, noun: () => page().noun[1],
    rows: () => (lists[adPage] || []).filter((x) => inView(viewOf(adView), x)), find: (x) => page().find(x), fields: () => AD[adPage].fields || null, redraw: () => drawAd() });

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
  // The open page's views, tucked under its entry in the list down the left. No counts until its list is read.
  const drawViews = () => {
    const count = counts();
    $('dir-views').innerHTML = page().views.map(([, items]) => items.map((v) => {
      const n = v.n && lists[adPage] ? v.n() : count[v.key]; // a view can count something other than its rows (On each computer: its computers)
      if (v.quiet && !n && v.key !== adView) return '';
      return `<a href="#ad/${adPage}/${v.key}"${v.key === adView ? ' aria-current="page"' : ''}><span class="nm">${esc(v.label)}</span>`
        + `<small class="${n && v.tone ? `tone ${v.tone}` : ''}">${lists[adPage] ? n.toLocaleString() : ''}</small></a>`;
    }).join('')).filter(Boolean).join('<i></i>');
    document.querySelector(`#dir-rail a[data-adpage="${railOf(adPage)}"]`)?.after($('dir-views'));
    document.querySelectorAll('#dir-rail [data-n]').forEach((el) => { el.textContent = total[el.dataset.n] === undefined ? '' : total[el.dataset.n].toLocaleString(); });
  };
  // The tabs, or the page's own name where it has none; their counts once the list is read.
  const drawTabs = () => {
    const p = page();
    const list = lists[adPage];
    // Domain health: Checks and On each DC, each its own page, with the count of what's read.
    if (HEALTH.includes(adPage)) {
      // The other tab's count, read once in the background when it isn't known.
      const other = adPage === 'dcs' ? 'domain' : 'dcs';
      if (!lists[other] && ad?.state && Date.now() - (healthAsked[other] || 0) > 60000) {
        healthAsked[other] = Date.now(); // at most once a minute, so a read that fails isn't asked for on every draw
        AD[other].load().then((l) => { lists[other] ??= l; if (HEALTH.includes(adPage)) drawTabs(); }).catch(() => {});
      }
      $('dir-tabs').innerHTML = [['domain', 'shield', 'Checks'], ['dcs', 'server', 'On each computer']].map(([k, icon, label]) => `<a href="#ad/${k}" data-tab="${k}"${k === adPage ? ' aria-current="true"' : ''}>`
        + `${svg(icon)}${esc(label)} <small>${lists[k] ? lists[k].length.toLocaleString() : ''}</small></a>`).join('');
      return;
    }
    const openTab = tab();
    $('dir-tabs').innerHTML = (p.tabs || [{ key: '', label: p.title, icon: PAGE_ICON[adPage], where: () => true }])
      .map((t) => `<a data-tab="${t.key}"${!openTab || t.key === openTab.key ? ' aria-current="true"' : ''}>${svg(t.icon)}${esc(t.label)} <small>${list ? list.filter(t.where).length.toLocaleString() : ''}</small></a>`).join('');
  };
  const drawAd = () => {
    const p = page();
    const s = ad?.state;
    drawHero();
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
      $('rail-domain-dot').hidden = true;
      $('dir-broken').hidden = true;
      return;
    }
    $('dir-acct').textContent = s.problem ? `Read-only (${s.account} sign-in failed)` : s.readOnly ? 'Read-only (no service account)' : `Service account: ${s.account}`;
    // Its sign-in is failing: lists are read as this server, and nothing can be changed until it's fixed.
    $('dir-broken').hidden = !s.problem;
    $('dir-broken-text').textContent = s.problem ? `Read-only: ${s.problemText} Lists and searches still work, read with this server's computer account. Changes are unavailable until this is fixed.` : '';
    const v = viewOf(adView);
    const list = lists[adPage] || [];
    if (lists[adPage]) total[adPage] = (p.tabs ? list.filter(p.tabs[0].where) : list).length;
    // The open view, then what the filter bar narrows it to.
    const test = adBar.test();
    rows = list.filter((x) => inView(v, x) && test(x));
    drawTabs();
    // Actions is for the pages that list users, groups, computers and changes. The filter bar and the grid's part of the
    // list's bar (Group by, the count, Compact or Relaxed) draw themselves.
    const picks = !!picked[adPage];
    $('dir-act').hidden = !picks;
    adBar.draw();
    adGrid.tools($('dir-tools'), $('dir-rows'));
    if (picks && lists[adPage]) [...picked[adPage]].forEach((k) => { if (!byKey(k)) picked[adPage].delete(k); });
    $('dir-table').className = `table adt ${adPage} drows${picks ? ' picks' : ''}`;
    emptyNow = list.length ? (adBar.any() ? 'No items match the filter.' : 'No items in this view.') : lists[adPage] || !adFailed ? 'No items found.' : esc(adFailed);
    rows = adGrid.draw(rows);
    // A new audit row drawn again mid-way through easing in carries on from where it was.
    if (adPage === 'audit') $('dir-table').querySelectorAll('.tr.dir-audit-in[data-i]').forEach((r) => r.style.setProperty('animation-delay', `-${ageOf(rows[Number(r.dataset.i)])}ms`));
    $('dir-more').hidden = !more[adPage];
    $('dir-more').textContent = adPage === 'audit' ? 'Only the newest 2,000 changes are shown.' : 'Only the first 1,000 results are shown.';
    drawPicked();
    drawViews();
    // The side panel belongs to the page it was opened on.
    if (R.panel.is('ad') && dw.kind !== adPage) R.panel.close();
    // Something asked to open in the panel once this page's list was there (Open Domain Admins).
    if (dwNext && dwNext.k === adPage && lists[adPage]) {
      const x = lists[adPage].find(dwNext.find);
      dwNext = null;
      if (x) openDrawer(adPage, x);
    }
    markOpenRow();
    // On each computer: an open setting counts the kind of computer whose view is open now.
    if (adPage === 'dcs' && R.panel.is('ad') && dw.kind === 'dcs' && dwKind !== hk()) {
      dwKind = hk();
      drawDrawer();
    }
    // The dots in the rail keep what the last read said, while another page is open.
    if (lists.users) $('dir-locked-dot').hidden = !lists.users.some((u) => u.locked && !u.system);
    if (lists.domain) $('dir-domain-dot').hidden = !lists.domain.some((s) => s.tone === 'bad');
    // The main rail's one entry carries a dot when either of those does.
    $('rail-dir-dot').hidden = $('dir-locked-dot').hidden;
    $('rail-domain-dot').hidden = $('dir-domain-dot').hidden;
    drawBanner();
  };

  // The banner over the directory pages (index.html, #dir-hero): what's open, its numbers, and on Domain health a square
  // for each check. Org chart and the Attribute browser get their name, what they show, and the counts read so far.
  const HERO_STATS = { users: ['all', 'locked', 'expired', 'inactive'], groups: ['all', 'security', 'privileged', 'empty'], computers: ['all', 'servers', 'workstations', 'stale'],
    domain: ['all', 'off', 'look', 'notes'], dcs: ['all', 'off', 'ok', 'unread'], audit: ['all', 'week', 'day', 'deleted'], recycle: ['all', 'users', 'groups', 'computers'] };
  const STAT_ICON = { ok: 'check', unread: 'eye-off', locked: 'lock', expired: 'key', inactive: 'hourglass', stale: 'hourglass', security: 'shield', privileged: 'crown', empty: 'users', servers: 'server',
    workstations: 'laptop', off: 'alert', look: 'eye', notes: 'list', week: 'calendar', day: 'clock', deleted: 'trash', users: 'user', groups: 'users', computers: 'laptop' };
  const stat = (n, icon, label) => `<div><b>${n === null ? '<i class="sk sk-num"></i>' : esc(n.toLocaleString())}</b><span>${svg(icon)}${esc(label)}</span></div>`;
  // What New makes: the open list's kind, and a user on the pages that list none of these.
  const MAKES = [['users', 'user-plus', 'New user'], ['groups', 'users', 'New group'], ['computers', 'laptop', 'New computer']];
  const makeKind = () => (R.where()[0] === 'ad' && MAKES.some(([k]) => k === adPage) ? adPage : 'users');
  const drawHero = (other = '') => {
    const at = R.where()[0];
    if (!other && at !== 'ad') other = at === 'org' || at === 'attrs' ? at : ''; // a list read again while another page is open
    if (!other && at !== 'ad') return;
    // The bar under it, once the account is known: New, which makes the open list's kind; read-only, nothing.
    $('dir-cmd').hidden = !!ad && !ad.state;
    $('dir-new-split').hidden = !!ad?.state?.readOnly;
    $('dir-new-split').querySelectorAll('button').forEach((b) => { b.disabled = !ad?.state; });
    const [, makeIcon, makeText] = MAKES.find(([k]) => k === makeKind());
    R.newLabel($('dir-new'), makeIcon, makeText);
    const hero = $('dir-hero');
    $('dir-eyebrow').textContent = ad?.state ? `${ad.state.domain} · Directory` : 'Directory';
    if (other) {
      const pg = $(`page-${other}`);
      $('dir-state').textContent = `${pg.dataset.title}.`;
      $('dir-lead').textContent = pg.dataset.lead || '';
      // The counts read so far. The Org chart counts its own people, so Users waits for that (a dash if it failed).
      const own = other === 'org' && !(ad && !ad.state);
      $('dir-stats').innerHTML = ['users', 'groups', 'computers'].filter((k) => total[k] !== undefined || (k === 'users' && own))
        .map((k) => stat(total[k] ?? (orgFailed ? '—' : null), PAGE_ICON[k], AD[k].title)).join('');
      return;
    }
    const p = page();
    const list = lists[adPage];
    const n = list ? counts() : {};
    const bad = list && adPage === 'domain' ? list.filter((s) => s.tone === 'bad').length : 0;
    const warn = list && adPage === 'domain' ? list.filter((s) => s.tone === 'warn').length : 0;
    // Domain health says how it stands once its checks are read: a placeholder line until then.
    if (HEALTH.includes(adPage) && !list) $('dir-state').innerHTML = adFailed ? `${esc(p.title)}.` : '<i class="sk sk-text"></i>';
    else if (adPage === 'dcs') $('dir-state').textContent = R.dcSettings.state(list, hk());
    else $('dir-state').textContent = adPage !== 'domain' ? `${p.title}.` : bad || warn
      ? `${bad ? `Failed: ${bad}` : 'No failed checks'}${warn ? `. Warnings: ${warn}` : ''}.` : 'All checks passed.';
    $('dir-lead').textContent = p.lead;
    // On each computer counts the kind of computer whose view is open.
    if (adPage === 'dcs') {
      $('dir-stats').innerHTML = ad && !ad.state ? '' : R.dcSettings.stats(list, hk()).map(([v, icon, label]) => stat(v ?? (adFailed ? '—' : null), icon, label)).join('');
      return;
    }
    // Placeholder numbers while it's read (ad not known yet, or the list not read), dashes if that failed; none when there's no account.
    $('dir-stats').innerHTML = ad && !ad.state ? '' : HERO_STATS[adPage].map((key) => {
      const v = allViews().find((x) => x.key === key);
      const label = key === 'all' ? p.noun[1].charAt(0).toUpperCase() + p.noun[1].slice(1) : v?.label || key;
      return stat(list ? n[key] ?? 0 : adFailed ? '—' : null, key === 'all' ? PAGE_ICON[adPage] : STAT_ICON[key] || PAGE_ICON[adPage], label);
    }).join('');
  };
  document.addEventListener('rabbidlab:show', async (e) => {
    const at = e.detail[0];
    if (at !== 'org' && at !== 'attrs') return;
    orgFailed = false;
    drawHero(at);
    if (ad && (at !== 'org' || total.users !== undefined)) return;
    try {
      if (!ad) {
        ad = await R.adInfo(); // the domain's name for the banner, when the page was opened first
        if (R.where()[0] === at) drawHero(at);
      }
      // The Org chart's read of the people (directory.js) is the Users list's too: counted the same way for the banner.
      if (at === 'org' && ad.state && total.users === undefined) total.users = (await R.keep('/api/ad/users', 60000)).users.filter((u) => !u.system).length;
    } catch {
      orgFailed = true;
    }
    if (R.where()[0] === at) drawHero(at);
  });

  // A line above the table: the Recycle Bin's state and what the audit can say. Domain health's says it in the banner.
  const drawBanner = () => {
    const b = $('dir-banner');
    let html = '';
    if (adPage === 'recycle' && recycle) {
      html = recycle.on
        ? `${svg('check')}<span><b>The AD Recycle Bin is enabled.</b> Deleted users, groups and computers can be restored with all their attributes for ${recycle.lifetime} days.</span>`
        : `${svg('alert')}<span><b>The AD Recycle Bin is not enabled.</b> Restored objects lose their group memberships and most attributes.</span>`
          + (can('ad.connect') ? '<button type="button" class="small primary" data-banner="enable">Enable</button>' : '');
      if (recycle.problem) html += `<span class="warn">${esc(recycle.problem)}</span>`;
    } else if (adPage === 'audit' && lists.audit?.info) {
      html = auditBanner(lists.audit.info);
    } else if (adPage === 'dcs' && lists.dcs?.info) {
      html = R.dcSettings.banner(lists.dcs, hk());
    }
    b.innerHTML = html;
    b.hidden = !html;
  };
  // The audit's line: which DCs don't record changes and how to fix that, or whose logs it reads; and how long changes are kept.
  const KEEP = [[30, '30 days'], [90, '90 days'], [365, '1 year']];
  const keepWords = (d) => KEEP.find(([n]) => n === d)?.[1] || `${d} days`;
  const listOf = (names) => (names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`);
  const auditBanner = ({ dcs = [], keep, windows, account }) => {
    const off = dcs.filter((d) => d.state === 'silent' || d.state === 'denied').map((d) => d.name);
    const down = dcs.filter((d) => d.state === 'unreachable' || d.state === 'error');
    const reading = dcs.filter((d) => d.state === 'recording').map((d) => d.name);
    const logs = (n) => (n === 1 ? 'log' : 'logs');
    const pick = can('ad.connect') ? `<label class="dir-keep"><span>Keep changes for</span><select data-banner="keep" aria-label="Keep changes for">`
      + `${KEEP.map(([d, w]) => `<option value="${d}"${d === keep ? ' selected' : ''}>${w}</option>`).join('')}</select></label>` : '';
    const kept = pick ? '' : ` Changes are kept for ${keepWords(keep)}.`;
    let line;
    if (!windows) {
      line = `${svg('alert')}<span><b>Security logs are read only when ${esc(R.app())} runs on Windows.</b> Changes show the new value read from Active Directory, without Modified by or the old value.${kept}</span>`;
    } else if (!account) {
      line = `${svg('alert')}<span><b>Security logs are read as the service account.</b> Until it is set up, changes show the new value read from Active Directory, without Modified by or the old value.${kept}</span>`;
    } else if (off.length) {
      line = `${svg('alert')}<span><b>${esc(listOf(off))} ${off.length === 1 ? 'does' : 'do'} not record directory changes in ${off.length === 1 ? 'its' : 'their'} Security log.</b> `
        + `To fix this, turn on Audit Directory Service Changes (Success) in the Default Domain Controllers Policy and add ${esc(ad.state.account)} to the Event Log Readers group.</span>`;
    } else if (reading.length) {
      line = `${svg('check')}<span><b>Changes are read from the Security ${logs(reading.length)} of ${esc(listOf(reading))}.</b>${kept}</span>`;
    } else if (down.length) {
      line = `${svg('alert')}<span><b>The Security ${logs(down.length)} of ${esc(listOf(down.map((d) => d.name)))} could not be read.</b>${kept}</span>`;
    } else {
      line = `${svg('history')}<span><b>Reading the domain controllers' Security logs.</b> Changes with their old values appear here within a few minutes.${kept}</span>`;
    }
    return line + pick + down.map((d) => `<span class="warn">${esc(d.problem || `The Security log of ${d.name} could not be read.`)}</span>`).join('');
  };
  // Keep changes for: a shorter time removes the older changes for good, so it asks first.
  $('dir-banner').addEventListener('change', (e) => {
    const box = e.target.closest('[data-banner="keep"]');
    if (!box) return;
    const days = Number(box.value);
    const was = lists.audit?.info?.keep || 90;
    const go = async () => {
      try {
        const { keep } = await R.send('/api/ad/audit/keep', { days });
        lists.audit = await AD.audit.load();
        R.toast(`Changes are now kept for ${keepWords(keep)}.`, keep < was ? `Changes older than ${keepWords(keep)} were removed.` : '');
      } catch (err) {
        R.toast('Could not change how long changes are kept.', err.message, { tone: 'warn' });
      }
      if (adPage === 'audit') drawAd();
    };
    if (days >= was) {
      go();
      return;
    }
    sure(`Keep changes for ${keepWords(days)}?`, `Changes older than ${keepWords(days)} are removed from the AD audit. This cannot be undone.`, `Keep for ${keepWords(days)}`, go);
    $('sure-dialog').addEventListener('close', () => { if (adPage === 'audit') drawBanner(); }, { once: true });
  });
  $('dir-banner').addEventListener('click', (e) => {
    if (e.target.closest('[data-banner="dcs-paths"]') && lists.dcs) {
      R.dcSettings.copyPaths(lists.dcs);
      return;
    }
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
      say('dir-note', 'AD Recycle Bin enabled. Objects deleted from now on can be restored with all their attributes.');
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
        if (adPage === 'users') R.offboard?.load();
        lists[adPage] = await page().load();
        say('dir-problem', '');
      } finally {
        R.skeleton($('dir-table'), '');
      }
    }
  };

  // The tabs: each draws the list again from what's loaded.
  $('dir-tabs').addEventListener('click', (e) => {
    const t = e.target.closest('[data-tab]');
    if (!t || !tabs().length || t.dataset.tab === tab().key) return;
    adTab = t.dataset.tab;
    drawAd();
  });
  // Actions: the menu for what's ticked, the same one as a row's ⋯. It opens under the button, from its left edge.
  $('dir-act').addEventListener('click', () => {
    const r = $('dir-act').getBoundingClientRect();
    openCmd(selectedObjs(), r.left, r.bottom + 6);
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
  // Refresh: the open page's list read again from Active Directory (the AD audit reads the Security logs first), with the
  // flask while it reads.
  $('dir-refresh').addEventListener('click', () => R.refresh($('dir-refresh'), $('dir-lib'), adPage === 'audit' ? 'Reading Security logs…' : adPage === 'dcs' ? 'Reading each domain controller…' : 'Refreshing Active Directory…', async () => {
    const k = adPage;
    try {
      lists[k] = await AD[k].load(true);
      say('dir-problem', '');
    } catch (err) {
      say('dir-problem', err.message);
    }
    if (k !== adPage) return;
    drawAd();
    if (R.panel.is('ad')) drawDrawer();
  }));
  // The open list read again as Refresh does, without the flask; slower for the lists that cost more LDAP searches.
  const EVERY = { users: 30000, computers: 30000, groups: 60000, audit: 60000, recycle: 60000, domain: 300000, dcs: 300000 };
  // Re-check on On each DC: those DCs read again (every DC when none are named), with the flask, then the list and panel.
  const recheckDcs = (names) => R.refresh($('dir-refresh'), $('dir-lib'), names ? `Reading ${names.length === 1 ? names[0] : `${names.length} domain controllers`}…` : 'Reading each domain controller…', async () => {
    try {
      lists.dcs = await AD.dcs.load(true, names);
      say('dir-problem', '');
    } catch (err) {
      say('dir-problem', err.message);
    }
    if (adPage === 'dcs') drawAd();
    if (R.panel.is('ad') && dw.kind === 'dcs') drawDrawer();
  });
  const adLive = R.live({
    page: () => R.where()[0] === 'ad' && can('ad.view'),
    hold: () => (!!ad && !ad.state) || (!lists[adPage] && !adFailed), // not set up, or its own first read is under way
    // On each DC looks again every few seconds while the server reads the DCs.
    every: () => (adPage === 'dcs' && lists.dcs?.info?.reading ? 5000 : EVERY[adPage] || 60000),
    read: async () => {
      const k = adPage;
      if (!ad) ad = await R.adInfo(); // its first read failed before the domain was known
      if (!ad.state) throw new Error('Not set up.');
      if (k === 'users') R.offboard?.load(); // the Offboarding column, which draws again itself when it changed
      const list = await AD[k].load();
      return { k, list, failed: '', info: list.info || null }; // info: the audit's DCs and how long it keeps changes
    },
    now: () => ({ k: adPage, list: lists[adPage], failed: adFailed, info: lists[adPage]?.info || null }),
    draw: ({ k, list }) => {
      const open = R.panel.is('ad') ? JSON.stringify(dwObj()) : '';
      lists[k] = list;
      if (k !== adPage) return;
      if (adFailed) {
        adFailed = '';
        say('dir-problem', '');
      }
      drawAd();
      if (R.panel.is('ad') && JSON.stringify(dwObj()) !== open) drawDrawer(); // only when the one it shows changed
    },
    areas: () => [$('dir-table')],
  });
  // Offboarding: the column and the open panel draw again when someone's stages change, and a stage that ran reads the list again.
  R.offboard?.onChange(() => {
    if (adPage === 'users' && lists.users) drawAd();
    if (R.panel.is('ad') && dw.kind === 'users') drawDrawer();
  });
  document.addEventListener('rabbidlab:offboarded', () => {
    Object.keys(lists).filter((k) => k !== adPage).forEach((k) => delete lists[k]);
    dw.membersFor = '';
    if (hist.for) loadHistory({ dn: hist.for });
    adLive.now();
  });
  // Alerts added or changed (alerts-page.js): the open user's Alerts tab shows it.
  document.addEventListener('rabbidlab:alerts', () => { if (R.panel.is('ad')) drawDrawer(); });
  // A change undone anywhere: the open list is read again now, and the other pages, a group's members and the attributes when shown.
  document.addEventListener('rabbidlab:undone', () => {
    Object.keys(lists).filter((k) => k !== adPage).forEach((k) => delete lists[k]);
    dw.membersFor = '';
    dw.attrs.for = '';
    if (hist.for) loadHistory({ dn: hist.for }); // its Undo turns into Undone
    adLive.now();
  });
  // Domain health's dot from the start: its checks are read in the background once signed in, not only when it's opened.
  document.addEventListener('rabbidlab:me', () => {
    if (!can('ad.view') || lists.domain) return;
    R.get('/api/ad/domain').then((list) => {
      lists.domain ??= list;
      $('dir-domain-dot').hidden = !lists.domain.some((s) => s.tone === 'bad');
      $('rail-domain-dot').hidden = $('dir-domain-dot').hidden;
    }).catch(() => {}); // not set up, or a role that covers part of the domain: no dot
  });

  // --- The side panel's helpers ---------------------------------------------------------------------------------------

  // Local accounts close their panel when the list changes under it.
  const KIND_PIC = { user: 'user', group: 'users', computer: 'laptop', other: 'user' };

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
  // Every row opens in the side panel (R.panel).
  // Clicking the row that's open closes it; another row opens in its place.
  const openRow = (i) => (R.panel.shows('ad', `${adPage}:${keyOf(rows[i])}`) ? R.panel.close() : openDrawer(adPage, rows[i]));
  $('dir-table').addEventListener('keydown', (e) => {
    const row = e.target.closest('[data-i]');
    if (row && e.target === row && (e.key === 'Enter' || e.key === ' ')) {
      e.preventDefault();
      openRow(Number(row.dataset.i), row);
    }
  });

  // --- Users, groups and computers: ticked ones, and one menu of what can be done to them ---------------------------
  // A row's ⋯ or a right-click opens it for that row, or for every selected row when it's one of them; Actions over the
  // list opens it for the selected ones. Its categories open to the side. Changes go one at a time, and what this can't
  // change is left out: protected and built-in accounts, built-in groups and domain controllers.

  const CMD_W = 250;
  const nameOf = (x) => x.name || x.username;
  // Everything goes to Active Directory, which refuses what the service account may not do. A few things are never sent:
  // Rabbid Lab's own account keeps its password and sign-in, domain controllers stay put, built-in groups aren't deleted.
  const mayChange = () => true;
  const SIGN_IN = ['enable', 'disable', 'password', 'flag-on', 'flag-off', 'expiry', 'expiry-clear', 'delete'];
  const blocked = (k, x) => new Set(k === 'users' && x.service ? SIGN_IN : k === 'computers' && x.controller ? ['enable', 'disable', 'move', 'delete']
    : k === 'groups' && (x.system || x.protected) ? ['delete'] : []);
  let menuHide = new Set(); // what the open menu leaves out, for the ones it's for
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
  // What each change takes: resetting passwords, turning accounts off and on, or group membership. Anything else is
  // editing (ad.change); looking, copying and exporting take nothing more, and Delete is checked where it's listed.
  const NEEDS = { password: 'ad.password', unlock: 'ad.password', 'flag-on:mustchange': 'ad.password', 'flag-off:mustchange': 'ad.password',
    enable: 'ad.enable', disable: 'ad.enable',
    'groups-add': 'ad.groups', 'groups-remove': 'ad.groups', 'groups-remove-all': 'ad.groups', 'groups-copy': 'ad.groups', 'groups-copy-to': 'ad.groups',
    'members-add': 'ad.groups', 'members-remove': 'ad.groups', 'members-copy': 'ad.groups', 'members-copy-to': 'ad.groups' };
  const FREE = new Set(['open', 'openobj', 'clear-sel', 'attrs', 'export', 'delete', 'copy']);
  const may = (cmd) => FREE.has(cmd.split(':')[0]) || can(NEEDS[cmd] || 'ad.change');
  const anyChange = () => ['ad.change', 'ad.password', 'ad.enable', 'ad.groups'].some(can);
  const mi = (cmd, icon, label, { off = false, hint = '', danger = false } = {}) => (off || !may(cmd) || menuHide.has(cmd) || menuHide.has(cmd.split(':')[0]) ? ''
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
    mustchange: ['mustChange', (w) => `${w} must change password at next sign-in.`, (w) => `Password change requirement cleared for ${w}.`],
    noexpiry: ['neverExpires', (w) => `Password set to never expire for ${w}.`, (w) => `Password set to expire per policy for ${w}.`],
    cannotchange: [null, (w) => `Prevented ${w} from changing the password.`, (w) => `Allowed ${w} to change the password.`],
    sensitive: ['sensitive', (w) => `Marked ${w} as sensitive (cannot be delegated).`, (w) => `Allowed delegation for ${w}.`],
    smartcard: ['smartcard', (w) => `Smart card required for ${w}.`, (w) => `Smart card no longer required for ${w}.`],
    reversible: ['reversible', (w) => `Reversible password encryption enabled for ${w}.`, (w) => `Reversible password encryption disabled for ${w}.`],
    des: ['desOnly', (w) => `Set ${w} to use only Kerberos DES encryption types.`, (w) => `Kerberos DES-only setting cleared for ${w}.`],
    preauth: ['noPreauth', (w) => `Kerberos preauthentication disabled for ${w}.`, (w) => `Kerberos preauthentication enabled for ${w}.`],
  };
  const menuHtml = (k, objs) => {
    const [one, many] = AD[k].noun;
    const all = objs.length;
    // AD audit rows: the change, the object it was made to, and a CSV of the ones ticked.
    if (k === 'audit') {
      const x = all === 1 ? objs[0] : null;
      return (x ? part(mi('open', 'history', 'Properties')) : `<p class="cmd-head">${esc(count(all, one, many))} selected<button type="button" class="linkish cmd-clear" data-cmd="clear-sel">Clear</button></p>`)
        + (x && PAGE_OF[x.kind] && x.what !== 'deleted' ? part(mi('openobj', KIND_ICON[x.kind], `Open ${x.name}`)) : '')
        + part(mi('export', 'csv', all === 1 ? 'Export to CSV' : `Export ${count(all, one, many)} to CSV`));
    }
    const xs = objs.filter((x) => mayChange(k, x));
    const n = xs.length;
    const has = (f) => xs.filter(f).length;
    const change = anyChange() && n > 0;
    const toggle = (flag, icon, onLabel, offLabel) => {
      const on = has((u) => u[FLAGS[flag][0]]);
      return some(n - on, n, `flag-on:${flag}`, icon, onLabel) + some(on, n, `flag-off:${flag}`, icon, offLabel);
    };
    const top = all === 1 ? part(mi('open', PIC[k], 'Properties'))
      : `<p class="cmd-head">${esc(count(all, one, many))} selected<button type="button" class="linkish cmd-clear" data-cmd="clear-sel">Clear</button></p>`;
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
      // Offboarding first: what most often happens to a person here, in stages (offboard.js).
      const leaving = R.offboard && can('ad.enable') ? part(mi('offboard', 'user-minus', n === 1 ? 'Offboard…' : `Offboard ${n} users…`),
        ...(R.flows && n === 1 ? [mi('runflow', 'org', 'Run workflow…')] : [])) : '';
      changes = leaving + part(
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
          part(toggle('sensitive', 'shield', 'Mark as sensitive (cannot be delegated)', 'Allow delegation'),
            toggle('smartcard', 'key', 'Require smart card for interactive sign-in', 'Do not require smart card')),
          part(toggle('reversible', 'lock', 'Store password using reversible encryption', 'Do not store password using reversible encryption'),
            toggle('des', 'lock', 'Use only Kerberos DES encryption types', 'Clear Kerberos DES-only setting'),
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
    const deletable = xs.filter((x) => !blocked(k, x).has('delete')).length;
    const gone = can('ad.delete') && deletable ? part(mi('delete', 'trash', deletable === 1 ? 'Delete…' : `Delete ${count(deletable, one, many)}…`, { danger: true })) : '';
    // Alerts about one user (alerts-page.js), for whoever manages alerts.
    const watch = k === 'users' && all === 1 && R.alerts?.can() ? part(mi('alertme', 'bell', 'Alert me about this user…')) : '';
    return top + changes + watch + more + gone;
  };

  let menuFor = { kind: 'users', objs: [] };
  const openCmd = (objs, x, y, kind = adPage, { panel = false } = {}) => {
    if (!objs.length) return;
    // Left out: what's never sent for any of these, and Properties when the panel is already open.
    menuHide = new Set([...SIGN_IN, 'move'].filter((c) => objs.every((o) => blocked(kind, o).has(c))));
    if (panel) menuHide.add('open');
    const m = $('cmd-menu');
    if (m.matches(':popover-open')) m.hidePopover();
    // Over the side panel the menu has to be inside it, or the panel makes it unclickable.
    const host = R.panel.el.open ? R.panel.el : document.body;
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
    if (!xs.length) {
      if (R.panel.is('ad')) drawDrawer(); // a switch flipped for nothing goes back
      return [];
    }
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
    if (R.panel.is('ad')) { dw.membersFor = ''; drawDrawer(); }
    const ok = xs.length - failed.length;
    // What worked and can be undone gets Undo in the toast; a password reset or a create can't be.
    const ids = answers.map(([, a]) => a?.change).filter((c) => c?.undo).map((c) => c.id);
    const action = ids.length ? { label: 'Undo', run: () => R.undo(ids, { host: $('dir-lib') }), changes: ids } : null;
    if (!failed.length) R.toast(done, warnings[0] || '', { tone: warnings.length ? 'warn' : '', action });
    else R.toast(ok ? `Completed for ${ok} of ${xs.length}.` : 'The operation failed.', failed[0], { tone: 'warn', action });
    if (failed.length > 1) say('dir-problem', failed.join(' '));
    return answers;
  };

  // A group's members, read when they're needed.
  const membersNow = async (g) => (await R.get(`/api/ad/groups/members?dn=${encodeURIComponent(g.dn)}`)).map((m) => ({ dn: m.dn, name: m.name, kind: m.kind, sub: m.username || m.where }));
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
      case 'offboard': R.offboard?.open(xs.map((u) => u.username)); break;
      case 'runflow': R.flows?.run(xs[0]); break;
      case 'alertme': R.alerts?.me(xs[0]); break;
      case 'openobj': openChanged(objs[0]); break;
      case 'enable':
      case 'disable': {
        const on = what === 'enable';
        const to = only((x) => x.enabled !== on);
        each(k, to, on ? 'Enabling' : 'Disabling', `${whoOf(to, k)} ${on ? 'enabled' : 'disabled'}.`, (x) => enableOf(k, x, on));
        break;
      }
      case 'unlock': {
        const to = only((u) => u.locked);
        each(k, to, 'Unlocking', `${whoOf(to, k)} unlocked.`, (u) => post(u, '/unlock'));
        break;
      }
      case 'expiry': openExpiry(xs); break;
      case 'expiry-clear': {
        const to = only((u) => u.accountExpires);
        each(k, to, 'Removing account expiration', `Account expiration removed for ${whoOf(to, k)}.`, (u) => post(u, '/expiry', { date: null }));
        break;
      }
      case 'password':
        if (xs.length === 1) reset(who, `${userPath(xs[0])}/password`, 'This also unlocks the account if it is locked out.');
        else resetMany(xs);
        break;
      case 'flag-on':
      case 'flag-off': {
        const on = what === 'flag-on';
        const [field, onSaid, offSaid] = FLAGS[arg];
        const to = !field ? xs : only((u) => !!u[field] !== on && !(arg === 'mustchange' && on && u.neverExpires));
        if (arg === 'mustchange' && on && !to.length && xs.some((u) => u.neverExpires)) {
          R.toast('Password never expires.', 'Set the password to expire per policy before requiring a change at next sign-in.', { tone: 'warn' });
        }
        each(k, to, 'Updating account options', (on ? onSaid : offSaid)(whoOf(to, k)), (u) => post(u, '/flag', { flag: arg, on }));
        break;
      }
      case 'groups-add':
        // One of them: the groups it's already in are left out.
        openPicker({ title: 'Add to groups', lead: `Select the groups to add ${who} to.`, button: 'Add to groups', noun: ['group', 'groups'], none: `${who} ${xs.length === 1 ? 'is' : 'are'} already in every group`,
          rows: rowsOf(['groups'], [...xs.map((x) => x.dn), ...(xs.length === 1 ? (xs[0].groups || []).map((g) => g.dn) : [])]),
          go: (gs) => each(k, xs, 'Adding to groups', `Added ${who} to ${gs.length === 1 ? gs[0].name : count(gs.length, 'group', 'groups')}.`,
            (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), [])) });
        break;
      case 'groups-remove': {
        const to = only((x) => x.groups?.length);
        const seen = new Map();
        to.forEach((x) => x.groups.forEach((g) => seen.set(g.dn.toLowerCase(), { dn: g.dn, name: g.name, kind: 'group', n: (seen.get(g.dn.toLowerCase())?.n || 0) + 1 })));
        openPicker({ title: 'Remove from groups', lead: `Select the groups to remove ${whoOf(to, k)} from.`, button: 'Remove from groups', noun: ['group', 'groups'],
          none: `${whoOf(xs, k)} ${xs.length === 1 ? 'isn\'t' : 'aren\'t'} in any groups.`,
          rows: [...seen.values()].sort((a, b) => a.name.localeCompare(b.name)).map((g) => ({ ...g, sub: to.length > 1 ? `${g.n} of ${to.length}` : '' })),
          go: (gs) => each(k, to, 'Removing from groups', `Removed ${whoOf(to, k)} from ${gs.length === 1 ? gs[0].name : count(gs.length, 'group', 'groups')}.`,
            (x) => memberOf(k, x, [], gs.filter((g) => x.groups.some((h) => same(h.dn, g.dn))).map((g) => g.dn))) });
        break;
      }
      case 'groups-remove-all': {
        const to = only((x) => x.groups?.length);
        const stays = { users: ' The primary group (usually Domain Users) is not removed.', computers: ' The primary group (usually Domain Computers) is not removed.', groups: '' }[k];
        sure(`Remove ${whoOf(to, k)} from all groups?`, `All group memberships are removed.${stays}`, 'Remove from all groups',
          () => each(k, to, 'Removing from groups', `Removed ${whoOf(to, k)} from all groups.`, (x) => memberOf(k, x, [], x.groups.map((g) => g.dn))));
        break;
      }
      case 'groups-copy':
        openPicker({ title: 'Copy membership', lead: `Select the ${one} whose groups ${who} ${xs.length === 1 ? 'is added to' : 'are added to'}.`, button: 'Copy', one: true, noun: [one, many],
          rows: rowsOf([k], xs.map((x) => x.dn)),
          go: ([from]) => {
            const gs = from.x.groups || [];
            if (!gs.length) R.toast(`${from.name} is not a member of any groups.`, '', { tone: 'warn' });
            else each(k, xs, 'Copying membership', `Copied group membership from ${from.name} to ${who}.`, (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), []));
          } });
        break;
      case 'groups-copy-to': {
        const from = xs[0];
        const gs = from.groups || [];
        if (!gs.length) {
          R.toast(`${nameOf(from)} is not a member of any groups.`, '', { tone: 'warn' });
          break;
        }
        openPicker({ title: 'Copy membership', lead: `Select the ${many} to add to the ${count(gs.length, 'group', 'groups')} that ${nameOf(from)} is a member of.`, button: 'Copy', noun: [one, many],
          rows: rowsOf([k], [from.dn]),
          go: (picks) => each(k, picks.map((p) => p.x).filter((x) => mayChange(k, x)), 'Copying membership', `Copied group membership from ${nameOf(from)} to ${whoOf(picks.map((p) => p.x), k)}.`,
            (x) => memberOf(k, x, notIn(x, gs.map((g) => g.dn)), [])) });
        break;
      }
      case 'members-add':
        // One group: who's already in it is left out.
        openPicker({ title: 'Add members', lead: `Select the users, groups and computers to add to ${who}.`, button: 'Add members', noun: ['member', 'members'], tags: true, none: 'All users, groups and computers are already members',
          rows: (async () => rowsOf(['users', 'groups', 'computers'], [...xs.map((g) => g.dn), ...(xs.length === 1 ? (await membersNow(xs[0]).catch(() => [])).map((m) => m.dn) : [])]))(),
          go: (ms) => each(k, xs, 'Adding members', `Added ${ms.length === 1 ? ms[0].name : count(ms.length, 'member', 'members')} to ${who}.`, (g) => membersOf(g, ms.map((m) => m.dn), [])) });
        break;
      case 'members-remove': {
        const g = xs[0];
        openPicker({ title: 'Remove members', lead: `Select the members to remove from ${g.name}.`, button: 'Remove members', noun: ['member', 'members'], tags: true,
          none: `${g.name} has no members.`, rows: membersNow(g).then((ms) => ms.sort((a, b) => a.name.localeCompare(b.name))),
          go: (out) => each(k, [g], 'Removing members', `Removed ${out.length === 1 ? out[0].name : count(out.length, 'member', 'members')} from ${g.name}.`, (x) => membersOf(x, [], out.map((m) => m.dn))) });
        break;
      }
      case 'members-copy':
        openPicker({ title: 'Copy members', lead: `Select the group whose members are added to ${who}.`, button: 'Copy', one: true, noun: ['group', 'groups'],
          rows: rowsOf(['groups'], xs.map((g) => g.dn)),
          go: async ([from]) => {
            const ms = await membersNow(from).catch(() => []);
            if (!ms.length) R.toast(`${from.name} has no members to copy.`, '', { tone: 'warn' });
            else each(k, xs, 'Copying members', `Copied the members of ${from.name} to ${who}.`, (g) => membersOf(g, ms.map((m) => m.dn).filter((d) => !same(d, g.dn)), []));
          } });
        break;
      case 'members-copy-to': {
        const from = xs[0];
        openPicker({ title: 'Copy members', lead: `Select the groups to add the members of ${from.name} to.`, button: 'Copy', noun: ['group', 'groups'],
          rows: rowsOf(['groups'], [from.dn]),
          go: async (to) => {
            const ms = await membersNow(from).catch(() => []);
            if (!ms.length) R.toast(`${from.name} has no members to copy.`, '', { tone: 'warn' });
            else each(k, to, 'Copying members', `Copied the members of ${from.name} to ${to.length === 1 ? to[0].name : count(to.length, 'group', 'groups')}.`, (g) => membersOf(g, ms.map((m) => m.dn).filter((d) => !same(d, g.dn)), []));
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
        const to = xs.filter((x) => !blocked(k, x).has('delete'));
        const w = whoOf(to, k);
        const why = {
          users: `${to.length === 1 ? `${to[0].username} is` : `${count(to.length, 'account is', 'accounts are')}`} permanently deleted from ${ad.state.domain}, along with any objects in ${to.length === 1 ? 'it' : 'them'}. Disabling can be undone. Deleting cannot.`,
          groups: `${to.length === 1 ? 'The group is' : 'The groups are'} permanently deleted from ${ad.state.domain}. The members are not deleted, only removed from ${to.length === 1 ? 'it' : 'them'}.`,
          computers: `${to.length === 1 ? 'The computer account is' : 'The computer accounts are'} deleted from ${ad.state.domain}. Users cannot sign in to ${to.length === 1 ? 'it' : 'them'} with domain accounts until ${to.length === 1 ? 'it is' : 'they are'} joined to the domain again. Disabling can be undone. Deleting cannot.`,
        }[k];
        sure(`Delete ${w}?`, why, to.length === 1 ? `Delete ${nameOf(to[0])}` : `Delete ${count(to.length, one, many)}`,
          () => each(k, to, 'Deleting', `${w} deleted.`, (x) => deleteOf(k, x), { gone: true }));
        break;
      }
    }
  };

  const copyField = async (k, objs, key) => {
    const [, one, many, get] = COPY[k].find(([c]) => c === key);
    const values = objs.map(get).filter(Boolean);
    if (!values.length) {
      R.toast(`No ${many} to copy.`, '', { tone: 'warn' });
      return;
    }
    try {
      await navigator.clipboard.writeText(values.join('\r\n'));
      R.toast(values.length === 1 ? `Copied the ${one}.` : `Copied ${values.length} ${many}.`);
    } catch {
      R.toast('Could not copy to the clipboard.', 'The browser blocked clipboard access.', { tone: 'warn' });
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
      say('exp-problem', 'Select the last day the account can be used.');
      return;
    }
    $('exp-dialog').close();
    each('users', expFor, 'Setting account expiration', `${whoOf(expFor, 'users')} ${expFor.length === 1 ? 'expires' : 'expire'} at the end of ${day(`${date}T12:00`)}.`, (u) => post(u, '/expiry', { date }));
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
    $('ued-lead').textContent = one ? (k === 'users' ? `${one.username}, in ${one.where || ad.state.domain}.` : `In ${one.where || ad.state.domain}.`) : `Only the fields you fill in are changed. Blank fields are left unchanged.`;
    UED.forEach(([id, field, , kinds]) => {
      const box = $(`ued-${id}`);
      box.closest('div').hidden = !kinds.split(' ').includes(k) || (!one && box.closest('[data-one]') !== null);
      box.value = one ? one[field] || '' : '';
      box.placeholder = one ? '' : 'Leave unchanged';
    });
    $('ued-boss-row').hidden = k !== 'users';
    $('ued-manager').value = one?.manager || '';
    $('ued-manager').placeholder = one ? 'Search users' : 'Leave unchanged';
    say('ued-problem', '');
    $('ued-dialog').showModal();
    $('ued-fields').querySelector('div:not([hidden]) input')?.focus();
  };
  R.dropdown($('ued-boss'), {
    none: 'No matching users.',
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
    each(k, xs, 'Saving', `Saved changes to ${whoOf(xs, k)}.`, (x) => editOf(k, x, form));
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
      say('ren-problem', 'Enter the new name.');
      return;
    }
    $('ren-dialog').close();
    const u = renaming;
    each('users', [u], 'Renaming', `Renamed ${nameOf(u)} to ${name}.`, (x) => post(x, '/rename', { name }));
  });
  $('ren-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('ren-save').click(); });

  // Move…: the OU picker, then each one that isn't there already.
  let moving = { kind: 'users', objs: [] };
  const openMove = async (k, xs) => {
    moving = { kind: k, objs: xs };
    // It opens on where they are when that's one place, else where new users go.
    const at = xs.map((x) => dnParent(x.dn).toLowerCase());
    await placesInto('mv-ou', at.every((p) => p === at[0]) ? dnParent(xs[0].dn) : ad.state.newUsers, 'dir-problem');
    PICK_LEADS['mv-ou'] = `Select the OU ${whoOf(xs, k)} ${xs.length === 1 ? 'moves' : 'move'} to.`;
    openPick($('mv-ou'));
  };
  $('mv-ou').addEventListener('change', () => {
    const ou = $('mv-ou').value;
    const { kind: k, objs } = moving;
    const to = objs.filter((x) => !same(dnParent(x.dn), ou));
    const where = (places || []).find((p) => same(p.dn, ou))?.path || ad.state.domain;
    each(k, to, 'Moving', `Moved ${whoOf(to, k)} to ${where}.`, (x) => moveOf(k, x, ou));
  });

  // One window for picking, as NorthStack AD has it: every one there is in a list with a tick each, a box to find in
  // it, Tick all shown, and how many are ticked. One: ticking one clears the others.
  const KIND_OF = { users: 'user', groups: 'group', computers: 'computer' };
  const PK_ICON = { user: 'user', group: 'users', computer: 'laptop', other: 'user' };
  const PK_TAG = { user: 'User', group: 'Group', computer: 'Computer' };
  const pk = { rows: [], one: false, chosen: new Set(), go: null, noun: ['item', 'items'], tags: false, none: '' };
  const ensure = async (k) => lists[k] || (lists[k] = await AD[k].load());
  // Everyone of these kinds, A to Z, less the ones in not (themselves, and what they're already in or have).
  const rowsOf = async (kinds, not = []) => {
    const skip = new Set(not.map((d) => String(d).toLowerCase()));
    const sub = (k, x) => (k === 'users' ? [x.username, x.where] : k === 'groups' ? [x.security ? 'Security' : 'Distribution', x.where] : [x.host, x.where]).filter(Boolean).join(' · ');
    const all = await Promise.all(kinds.map(async (k) => (await ensure(k)).map((x) => ({ dn: x.dn, name: nameOf(x), kind: KIND_OF[k], sub: sub(k, x), x }))));
    return all.flat().filter((r) => !skip.has(String(r.dn).toLowerCase())).sort((a, b) => a.name.localeCompare(b.name));
  };
  const pkCount = () => {
    const n = pk.chosen.size;
    $('gp-count').textContent = n ? `${count(n, pk.noun[0], pk.noun[1])} selected` : '';
    $('gp-save').disabled = !n;
  };
  const pkShown = () => {
    const q = $('gp-find').value.trim().toLowerCase();
    return pk.rows.filter((r) => !q || `${r.name} ${r.sub}`.toLowerCase().includes(q));
  };
  const drawPk = () => {
    const shown = pkShown();
    $('gp-list').innerHTML = shown.length ? shown.map((r) => `<label class="pk-row"><input type="checkbox" data-pk="${r.i}"${pk.chosen.has(r.i) ? ' checked' : ''}>`
      + `<span class="pic">${svg(PK_ICON[r.kind] || 'user')}</span><span class="pk-text"><b>${esc(r.name)}</b>${r.sub ? `<small>${esc(r.sub)}</small>` : ''}</span>`
      + `${pk.tags && PK_TAG[r.kind] ? badge(PK_TAG[r.kind], 'grey') : ''}</label>`).join('')
      : pkEmpty();
    $('gp-all').hidden = pk.one;
    $('gp-all').disabled = !shown.length;
    pkCount();
  };
  // Nothing to show: what the find missed (with a way back to everything), or why there's nothing at all.
  const pkEmpty = () => {
    const q = $('gp-find').value.trim();
    const n = pk.rows.length;
    const [title, text] = n ? [`No ${pk.noun[1]} match “${q}”`, `Check the spelling, or clear the search to show all ${count(n, pk.noun[0], pk.noun[1])}.`]
      : [pk.none || `No ${pk.noun[1]} available`, ''];
    const dots = Array.from({ length: 6 }, (_, i) => `<i class="pk-bit b${i}"></i>`).join('');
    return `<div class="pk-empty"><div class="pk-art"><span class="big-ic ember">${svg(n ? 'search' : 'users')}</span><span class="pk-ghosts"><i></i><i></i><i></i></span>`
      + `<span class="pk-glass">${dots}<span class="pk-lens">${svg(n ? 'search' : 'users')}</span></span></div>`
      + `<b>${esc(title)}</b>${text ? `<small>${esc(text)}</small>` : ''}${n ? '<button type="button" class="small" data-pk-clear>Clear search</button>' : ''}</div>`;
  };
  $('gp-list').addEventListener('click', (e) => {
    if (!e.target.closest('[data-pk-clear]')) return;
    $('gp-find').value = '';
    drawPk();
    $('gp-find').focus();
  });
  // rows: the list, or a promise of it (it shows loading until it's read).
  const openPicker = async ({ title, lead, button, rows, one = false, noun = ['item', 'items'], tags = false, none = '', go }) => {
    Object.assign(pk, { rows: [], one, noun, tags, none, go, chosen: new Set() });
    $('gp-title').textContent = title;
    $('gp-lead').textContent = lead;
    $('gp-save').textContent = button;
    $('gp-find').value = '';
    $('gp-all').hidden = true;
    say('gp-problem', '');
    pkCount();
    R.skeleton($('gp-list'), 'rows', 6);
    $('gp-dialog').showModal();
    $('gp-find').focus();
    try {
      pk.rows = (await rows).map((r, i) => ({ ...r, i }));
    } catch (err) {
      say('gp-problem', err.message);
    } finally {
      R.skeleton($('gp-list'), '');
    }
    drawPk();
  };
  let pkTimer = null;
  $('gp-find').addEventListener('input', () => {
    clearTimeout(pkTimer);
    pkTimer = setTimeout(drawPk, 80);
  });
  $('gp-list').addEventListener('change', (e) => {
    if (e.target.dataset.pk === undefined) return;
    const i = Number(e.target.dataset.pk);
    if (pk.one) {
      pk.chosen = new Set(e.target.checked ? [i] : []);
      $('gp-list').querySelectorAll('[data-pk]').forEach((b) => { b.checked = Number(b.dataset.pk) === i && e.target.checked; });
    } else if (e.target.checked) pk.chosen.add(i);
    else pk.chosen.delete(i);
    pkCount();
  });
  $('gp-all').addEventListener('click', () => {
    pkShown().forEach((r) => pk.chosen.add(r.i));
    drawPk();
  });
  $('gp-cancel').addEventListener('click', () => $('gp-dialog').close());
  $('gp-save').addEventListener('click', () => {
    const chosen = pk.rows.filter((r) => pk.chosen.has(r.i));
    $('gp-dialog').close();
    if (chosen.length) pk.go(chosen);
  });

  // --- The panel from the right: one user, group or computer -------------------------------------------------------
  // Clicking a row opens it beside the list, as the Microsoft 365 and Entra admin centers do: who or what it is, its
  // status, the everyday actions (More has the rest), and tabs for its details and its groups (a group's members too).
  // Each section has its own actions, and settings are switches. The list stays usable: another row opens in it.

  const dw = { kind: '', key: '', tab: 'overview', members: null, membersFor: '', picks: new Set(), attrs: { for: '', obj: null, find: '', all: false } };
  const dwObj = () => (lists[dw.kind] || []).find((x) => String(AD[dw.kind].key(x)) === dw.key);
  const dwFact = (label, value, wide = false) => `<div class="dw-fact${wide ? ' wide' : ''}"><span class="l">${esc(label)}</span><span class="v">${value || '<span class="muted">Not set</span>'}</span></div>`;
  // A section: its heading with its actions (only those this may do), then its facts.
  let dwNot = new Set(); // what's never sent for the one shown
  const dwLink = (cmd, icon, label) => (!may(cmd) || dwNot.has(cmd.split(':')[0]) ? '' : `<button type="button" class="dw-link" data-dw-cmd="${cmd}">${svg(icon)}${esc(label)}</button>`);
  const dwFacts = (title, items, links = '') => {
    const shown = items.filter(Boolean).join('');
    return shown ? `<section class="dw-sec"><div class="dw-sec-head"><h3>${esc(title)}</h3>${links ? `<div class="dw-links">${links}</div>` : ''}</div><div class="dw-facts">${shown}</div></section>` : '';
  };
  // A setting as a switch: on or off, and flipping it makes the change.
  const dwSwitch = (label, on, onCmd, offCmd, help = '') => {
    const off = !may(onCmd) || dwNot.has(onCmd.split(':')[0]);
    return `<label class="dw-fact dw-sw"><span>${esc(label)}${help ? `<small>${esc(help)}</small>` : ''}</span>`
      + `<input type="checkbox" class="switch" data-dw-on="${onCmd}" data-dw-off="${offCmd}"${on ? ' checked' : ''}${off ? ' disabled' : ''}></label>`;
  };
  const e1 = (v) => (v ? esc(v) : '');
  // A command as a button; look is its class in the panel's heading (small, beside Edit), or a quick action.
  const qa = (cmd, icon, label, look = 'dw-qa') => (dwNot.has(cmd) || !may(cmd) ? '' : `<button type="button" class="${look}" data-dw-cmd="${cmd}">${svg(icon)}<span>${esc(label)}</span></button>`);
  // Why a few things aren't there, for the ones that have them.
  const dwWhy = (k, x) => (k === 'users' && x.service ? `${ad.state.account} is the ${R.app()} service account. Its password and sign-in settings cannot be changed here.`
    : k === 'computers' && x.controller ? 'Domain controllers cannot be disabled, moved or deleted here.' : '');

  const dwHead = (k, x) => {
    const pic = k === 'users' ? `<span class="pic user dw-pic">${esc(initials(nameOf(x)))}</span>` : `<span class="pic dw-pic">${svg(k === 'groups' ? 'users' : server(x) ? 'server' : 'laptop')}</span>`;
    const sub = k === 'users' ? [x.username, x.upn].filter(Boolean).join(' · ') : k === 'computers' ? x.host || '' : x.description || '';
    // What else it is, beside its status in the strip below.
    const status = k === 'users' ? (x.protected ? badge('Privileged', 'amber') : '') + (x.service ? chip(R.app(), 'ember') : '')
      : k === 'computers' ? (x.controller ? chip('Domain controller', 'muted') : '') + (server(x) ? chip('Server', 'muted') : '') : x.protected ? badge('Privileged', 'amber') : '';
    // Edit first (grey with why when this role can't), then the thing done most beside it (unlocking, when it's locked
    // out); Actions opens the whole menu.
    const what = k === 'users' ? 'user' : k === 'groups' ? 'group' : 'computer';
    const edit = dwNot.has('edit') ? '' : R.editButton('data-dw-cmd="edit"', may('edit') ? '' : `Your role can't edit this ${what}`);
    const main = !anyChange() ? '' : k === 'users' ? (x.locked ? qa('unlock', 'unlock', 'Unlock', 'small') : qa('password', 'key', 'Reset password', 'small'))
      : k === 'groups' ? qa('members-add', 'user-plus', 'Add members', 'small') : '';
    // The facts looked at first, in a row under the name.
    const password = x.mustChange ? 'Must change at next sign-in' : x.neverExpires ? 'Never expires' : x.passwordExpires ? `Expires ${day(x.passwordExpires)}` : '';
    const strip = (k === 'users' ? [['Status', userStatus(x)], ['Logon name', `<span class="mono">${esc(x.username)}</span>`], ['Password', esc(password)], ['Last sign-in', esc(ago(x.lastSignIn))], ['Created', x.created ? esc(day(x.created)) : '']]
      : k === 'computers' ? [['Status', pcStatus(x)], ['Operating system', esc(x.os || '')], ['Last sign-in', esc(ago(x.lastSignIn))], ['Created', x.created ? esc(day(x.created)) : '']]
        : [['Group type', x.security ? 'Security' : 'Distribution'], ['Group scope', esc(x.scope)], ['Members', x.moreMembers ? '1,500 or more' : esc(x.members.toLocaleString())]])
      .filter(([, v]) => v).map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${v}</span></div>`).join('');
    const why = dwWhy(k, x);
    return `<div class="dw-head">${pic}<div class="dw-name"><h2 id="dw-title">${esc(nameOf(x))}</h2>${sub ? `<p>${esc(sub)}</p>` : ''}</div>`
      + `<div class="dw-do">${edit}${main}<button type="button" class="small" data-dw-more aria-haspopup="menu">Actions${svg('chevron-down', 'chev')}</button></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<p class="dw-path">${esc([ad.state.domain, x.where].filter(Boolean).join(' / '))}</p>`
      + `<div class="dw-strip">${strip}</div>`
      + `<div class="dw-status">${status}</div>`
      + (why && anyChange() ? `<p class="hint dw-note">${esc(why)}</p>` : '');
  };

  const dwOverview = (k, x) => {
    const where = dwFact('Organizational unit', esc(x.where || ad.state.domain), true);
    if (k === 'users') {
      const password = x.mustChange ? 'Must change at next sign-in' : x.neverExpires ? 'Never expires' : x.passwordExpires ? `Expires ${day(x.passwordExpires)}` : '';
      return dwFacts('Account', [dwFact('Pre-Windows 2000 logon name', `<span class="mono">${esc(x.username)}</span>`), dwFact('User principal name', e1(x.upn)),
        dwFact('Last sign-in', esc(ago(x.lastSignIn))), dwFact('Created', x.created ? esc(day(x.created)) : ''),
        dwFact('Account expires', x.accountExpires ? `End of ${esc(day(new Date(new Date(x.accountExpires) - 1)))}` : 'Never'), dwFact('Locked out', x.locked ? 'Yes' : 'No'), where],
        dwLink('expiry', 'calendar', x.accountExpires ? 'Change expiration' : 'Set expiration') + (x.accountExpires ? dwLink('expiry-clear', 'calendar', 'Remove expiration') : '')
          + dwLink('move', 'move', 'Move') + dwLink('rename', 'edit', 'Rename'))
        + dwFacts('Password', [dwFact('Password', esc(password)), dwFact('Last set', x.passwordSet ? esc(day(x.passwordSet)) : ''),
          dwSwitch('Must change at next sign-in', !!x.mustChange, 'flag-on:mustchange', 'flag-off:mustchange'),
          dwSwitch('Never expires', !!x.neverExpires, 'flag-on:noexpiry', 'flag-off:noexpiry')],
          dwLink('password', 'key', 'Reset password') + dwLink('flag-on:cannotchange', 'lock', 'Prevent password change') + dwLink('flag-off:cannotchange', 'unlock', 'Allow password change'))
        + dwFacts('Contact and organization', [dwFact('Display name', e1(x.name)), dwFact('Email', e1(x.email)), dwFact('Job title', e1(x.title)), dwFact('Department', e1(x.department)),
          dwFact('Company', e1(x.company)), dwFact('Office', e1(x.office)), dwFact('Phone', e1(x.phone)), dwFact('Manager', e1(x.manager)), dwFact('Description', e1(x.description), true)],
          dwLink('edit', 'edit', 'Edit'))
        + dwFacts('Security', [dwSwitch('Account is sensitive and cannot be delegated', !!x.sensitive, 'flag-on:sensitive', 'flag-off:sensitive'),
          dwSwitch('Smart card required', !!x.smartcard, 'flag-on:smartcard', 'flag-off:smartcard'),
          dwSwitch('Reversible encryption', !!x.reversible, 'flag-on:reversible', 'flag-off:reversible'),
          dwSwitch('Kerberos DES only', !!x.desOnly, 'flag-on:des', 'flag-off:des'),
          dwSwitch('Kerberos preauthentication required', !x.noPreauth, 'flag-off:preauth', 'flag-on:preauth')])
        + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
    }
    if (k === 'groups') {
      return dwFacts('Group', [dwFact('Group type', x.security ? 'Security' : 'Distribution'), dwFact('Group scope', esc(x.scope)),
        dwFact('Members', x.moreMembers ? '1,500 or more' : esc(x.members.toLocaleString())), dwFact('Email', e1(x.email)),
        where, dwFact('Description', e1(x.description), true), dwFact('Notes', e1(x.notes), true)], dwLink('edit', 'edit', 'Edit') + dwLink('move', 'move', 'Move'))
        + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
    }
    return dwFacts('Computer', [dwFact('DNS host name', e1(x.host)), dwFact('Operating system', esc([x.os, x.osVersion].filter(Boolean).join(' '))),
      dwFact('Last sign-in', esc(ago(x.lastSignIn))), dwFact('Created', x.created ? esc(day(x.created)) : ''), dwFact('Location', e1(x.location)),
      where, dwFact('Description', e1(x.description), true)], dwLink('edit', 'edit', 'Edit') + dwLink('move', 'move', 'Move'))
      + `<p class="hint dw-dn">${esc(x.dn)}</p>`;
  };

  // Ticks on the panel's Member of and Members lists, like every list: tick some (or all), then Remove with the count.
  const dwPicked = (dn) => [...dw.picks].some((d) => same(d, dn));
  const dwTick = (dn, name) => `<label class="tk"><input type="checkbox" data-dw-pick="${esc(dn)}"${dwPicked(dn) ? ' checked' : ''} aria-label="Select ${esc(name)}"></label>`;
  const dwTickAll = (dns) => {
    [...dw.picks].forEach((d) => { if (!dns.some((x) => same(x, d))) dw.picks.delete(d); });
    const n = dw.picks.size;
    return `<div class="mrow dw-pick-head"><label class="tk"><input type="checkbox" data-dw-pick-all${n && n === dns.length ? ' checked' : ''}${n && n < dns.length ? ' data-mixed' : ''} aria-label="Select all"></label>`
      + `<span>${n ? `${n} selected` : 'Select all'}</span></div>`;
  };
  const dwRemove = () => `<button type="button" class="small" data-dw-remove${dw.picks.size ? '' : ' disabled'}>${svg('user-minus')}Remove${dw.picks.size ? ` (${dw.picks.size})` : ''}</button>`;

  // Its groups, each with a tick and a way out, and Add to groups.
  const dwMemberOf = (k, x) => {
    const change = can('ad.groups') && mayChange(k, x);
    const gs = x.groups || [];
    const ticks = change && gs.length;
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Member of <small>${gs.length}</small></h3>`
      + `${change ? `<span class="dw-sec-do">${ticks ? dwRemove() : ''}<button type="button" class="small" data-dw-cmd="groups-add">${svg('user-plus')}Add to groups</button></span>` : ''}</div>`
      + (gs.length ? `<div class="dw-list">${ticks ? dwTickAll(gs.map((g) => g.dn)) : ''}${gs.map((g) => `<div class="mrow">${ticks ? dwTick(g.dn, g.name) : ''}<span class="pic small">${svg('users')}</span><div class="two"><b>${esc(g.name)}</b><small>${esc(pathOf(g.dn))}</small></div>`
        + `${change ? `<button type="button" class="at-more dw-x" data-dw-leave="${esc(g.dn)}" data-tip="Remove" aria-label="Remove from ${esc(g.name)}">${svg('x')}</button>` : ''}</div>`).join('')}</div>`
        : `<p class="muted dw-empty">${k === 'users' ? 'Domain Users (primary group) only.' : k === 'computers' ? 'Domain Computers (primary group) only.' : 'Not a member of any groups.'}</p>`)
      + '</section>';
  };
  // Attributes: everything the account has in Active Directory, as ADSI Edit shows it (/api/ad/object), read as the
  // panel opens. Find, Show empty, Export, and each row's menu (directory.js: Copy, Edit…, Clear) as in the Attribute browser.
  const loadAttrs = (x) => {
    const dn = x.dn;
    dw.attrs = { ...dw.attrs, for: dn, obj: null };
    R.get(`/api/ad/object?dn=${encodeURIComponent(dn)}`)
      .then((o) => { if (dw.attrs.for === dn) dw.attrs.obj = o; })
      .catch((err) => { if (dw.attrs.for === dn) dw.attrs.obj = { problem: err.message }; })
      .finally(() => { if (dw.attrs.for === dn && R.panel.is('ad')) drawDrawer(); });
  };
  const attrsSet = (x) => (dw.attrs.for === x.dn && dw.attrs.obj?.attributes ? dw.attrs.obj.attributes.filter((a) => a.set).length : null);
  const dwAttrs = (x) => {
    const o = dw.attrs.for === x.dn ? dw.attrs.obj : null;
    const head = (right = '') => `<div class="dw-sec-head"><h3>Attributes${o?.attributes ? ` <small>${attrsSet(x)} of ${o.attributes.length} set</small>` : ''}</h3>${right}</div>`;
    if (!o) return `<section class="dw-sec">${head()}<div data-dwa-skel></div></section>`;
    if (o.problem) return `<section class="dw-sec">${head()}<p class="hint problem">${esc(o.problem)}</p></section>`;
    return `<section class="dw-sec dwa">${head(`<span class="dw-sec-do"><button type="button" class="small" data-dwa-export>${svg('download')}Export</button>`
      + `<button type="button" class="small" data-dwa-open>${svg('list')}Attribute browser</button></span>`)}`
      + (R.attrs.locked(o) ? `<p class="dw-how">Read-only: ${esc(R.attrs.locked(o))}</p>` : '')
      + `<div class="dwa-bar"><label class="side-find"><svg><use href="#i-search"/></svg><input type="search" id="dwa-find" placeholder="Search attributes and values" aria-label="Search attributes and values" autocomplete="off" spellcheck="false" value="${esc(dw.attrs.find)}"></label>`
      + `<label class="dwa-all"><input type="checkbox" id="dwa-all"${dw.attrs.all ? ' checked' : ''}>Show empty</label></div><div class="table attrs" id="dwa-table"></div></section>`;
  };
  const drawAttrGrid = () => {
    const el = $('dw-body').querySelector('#dwa-table');
    const o = dw.attrs.obj;
    if (!el || !o?.attributes) return;
    const q = dw.attrs.find.trim().toLowerCase();
    const rows = o.attributes.filter((a) => (dw.attrs.all || a.set) && (!q || a.name.toLowerCase().includes(q) || a.values.some((v) => v.toLowerCase().includes(q))));
    R.grid({
      el, key: 'dw-attrs', headButton: true, sort: { key: 'name', dir: 1 }, redraw: drawAttrGrid, empty: q ? 'No matching attributes.' : 'No attributes have a value.',
      cols: [
        { key: 'name', label: 'Attribute', w: 190, fixed: true, get: (a) => a.name, cell: (a) => `<span class="mono ell">${esc(a.name)}</span>` },
        { key: 'value', label: 'Value', w: 300, on: true, get: (a) => a.values.join(' '),
          cell: (a) => (a.set ? `<span class="ell${a.hidden ? ' muted' : ''}" title="${esc(a.values.join('\n'))}">${esc(a.values.join(', '))}</span>` : '<span class="muted">Not set</span>') },
        { key: 'type', label: 'Type', w: 130, get: (a) => a.type, cell: (a) => `<span class="ty ell">${esc(a.type)}</span>` },
      ],
      tail: { w: 30, cell: (a) => `<button type="button" class="at-more dwa-more" data-dwa-menu="${esc(a.name)}" data-tip="More" aria-label="More for ${esc(a.name)}">${svg('dots')}</button>` },
      row: (a) => ` data-dwa="${esc(a.name)}"`,
      rowClass: (a) => (a.set ? '' : 'unset'),
    }).draw(rows);
  };
  // The menu's Edit… and Clear save through directory.js, then the account is read again so every tab shows the change.
  const attrTarget = (k, x) => ({
    get: () => dw.attrs.obj,
    set: (o) => { dw.attrs.obj = o; },
    draw: async () => {
      drawAttrGrid();
      try {
        if (k === 'users' && lists.users?.includes(x)) {
          lists.users[lists.users.indexOf(x)] = await R.get(userPath(x));
          counted.list = null;
        } else if (k === adPage) await loadAd(true);
      } catch { /* the grid already shows it */ }
      drawAd();
      if (R.panel.is('ad')) drawDrawer();
    },
  });
  const pathOf = (dn) => dn.split(/(?<!\\),/).slice(1).filter((p) => !/^DC=/i.test(p)).map((p) => p.slice(p.indexOf('=') + 1)).reverse().join(' / ') || ad.state.domain;

  // A group's members, read when the tab opens.
  const dwMembers = (g) => {
    const change = can('ad.groups') && mayChange('groups', g);
    if (dw.membersFor !== g.dn) {
      dw.membersFor = g.dn;
      dw.members = null;
      membersNow(g).then((ms) => { if (dw.membersFor === g.dn) { dw.members = ms; drawDrawer(); } })
        .catch((err) => { if (dw.membersFor === g.dn) { dw.members = { problem: err.message }; drawDrawer(); } });
    }
    const ms = dw.members;
    const ticks = change && Array.isArray(ms) && ms.length;
    const body = ms === null ? `<div class="op-items">${'<i class="sk op-sk"></i>'.repeat(4)}</div>`
      : ms.problem ? `<p class="hint problem">${esc(ms.problem)}</p>`
        : ms.length ? `<div class="dw-list">${ticks ? dwTickAll(ms.map((m) => m.dn)) : ''}${ms.map((m) => `<div class="mrow">${ticks ? dwTick(m.dn, m.name) : ''}<span class="pic small${m.kind === 'user' ? ' user' : ''}">${m.kind === 'user' ? esc(initials(m.name)) : svg(KIND_PIC[m.kind] || 'user')}</span>`
          + `<div class="two"><b>${esc(m.name)}</b><small>${esc(m.sub || '')}</small></div>`
          + `${change ? `<button type="button" class="at-more dw-x" data-dw-out="${esc(m.dn)}" data-tip="Remove" aria-label="Remove ${esc(m.name)}">${svg('x')}</button>` : ''}</div>`).join('')}</div>`
          : '<p class="muted dw-empty">No members.</p>';
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Members${Array.isArray(ms) ? ` <small>${ms.length}</small>` : ''}</h3>`
      + `${change ? `<span class="dw-sec-do">${ticks ? dwRemove() : ''}<button type="button" class="small" data-dw-cmd="members-add">${svg('user-plus')}Add members</button></span>` : ''}</div>${body}</section>`;
  };

  // A domain setting: how it is, what's recommended and why, and changing it here (signed in as a domain admin for it,
  // not saved), or how it's changed when that's done somewhere else.
  let dwsUser = '';
  const DWS_POLICY = ['length', 'complex', 'reversible', 'history', 'maxage', 'minage', 'lockout', 'duration'];
  const dwsLink = (key) => (key === 'recycle' ? `<a class="dw-link" href="#ad/recycle">${svg('trash')}Recycle Bin</a>`
    : key === 'admins' ? `<button type="button" class="dw-link" data-dws-admins>${svg('users')}Open Domain Admins</button>` : '');
  const dwSetting = (s) => {
    const change = s.edit && can('ad.connect')
      ? '<section class="dw-sec"><div class="dw-sec-head"><h3>Change setting</h3></div><div class="form dws-form">'
        // On and off: two choices to pick, then Save (not a switch, which would look like it changes straight away).
        + (s.edit === 'switch' ? `<label>New value</label><div class="seg dws-seg" id="dws-value" role="radiogroup" aria-label="New value">${[[1, 'Enabled'], [0, 'Disabled']]
          .map(([v, label]) => `<button type="button" role="radio" data-dws-v="${v}" aria-pressed="${Number(s.value) === v}" aria-checked="${Number(s.value) === v}">${label}</button>`).join('')}</div>`
          : `<label for="dws-value">New value</label><span class="dws-num">${R.stepper(`id="dws-value" min="${s.min}" max="${s.max}" value="${s.value ?? ''}"`)}<span>${esc(s.unit || '')}</span></span>`
            + `<small class="hint">Between ${s.min} and ${s.max}.</small>`)
        + `<label for="dws-user">Domain admin</label><input id="dws-user" autocomplete="off" spellcheck="false" placeholder="AMW\\admin" value="${esc(dwsUser)}">`
        + '<label for="dws-pass">Password</label><input type="password" id="dws-pass" autocomplete="new-password">'
        + `<small class="hint">${DWS_POLICY.includes(s.key) ? 'Saved in the Default Domain Policy and applied to the domain immediately. ' : ''}`
        + 'These credentials are used once and are not stored.</small></div>'
        + '<p class="hint problem" id="dws-problem" hidden></p><div class="dws-foot"><button type="button" class="primary" data-dws-save>Save</button></div></section>'
      : s.how ? `<section class="dw-sec"><div class="dw-sec-head"><h3>How to change it</h3><div class="dw-links">${dwsLink(s.key)}</div></div><p class="dw-how">${esc(s.how)}</p></section>` : '';
    return `<div class="dw-head"><span class="pic dw-pic">${svg(AREA_ICON[s.area] || 'shield')}</span><div class="dw-name"><h2 id="dw-title">${esc(s.name)}</h2><p>${esc(s.area)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<div class="dw-status">${TONE[s.tone] || ''}</div>`
      + dwFacts('Check', [dwFact('Current', esc(s.now)), dwFact('Recommended', esc(s.recommended)), dwFact('Details', esc(s.about), true)])
      + dwFound(s)
      + (s.fix ? `<section class="dw-sec"><div class="dw-sec-head"><h3>How to fix it</h3></div><p class="dw-how">${esc(s.fix)}</p></section>` : '')
      + change
      + (s.source ? `<p class="dw-src">Source: ${esc(s.source)}</p>` : '');
  };
  // What a check found: each one opens in its own page, and the check's fix runs on the ticked ones.
  const FOUND_ICON = { ...KIND_ICON, cert: 'certificate', trust: 'link', zone: 'signpost' };
  const flagOff = (flag, on = false) => (f) => [`${userPath(f)}/flag`, { flag, on }];
  const ACTS = {
    'notreq-off': { label: 'Require a password', doing: 'Requiring a password', icon: 'lock', kinds: ['user'], run: flagOff('notreq') },
    'noexpiry-off': { label: 'Set passwords to expire', doing: 'Setting passwords to expire', icon: 'calendar', kinds: ['user'], run: flagOff('noexpiry') },
    'reversible-off': { label: 'Disable reversible encryption', doing: 'Disabling reversible encryption', icon: 'lock', kinds: ['user'], run: flagOff('reversible') },
    'sensitive-on': { label: 'Mark as sensitive', doing: 'Marking as sensitive', icon: 'shield', kinds: ['user'], run: flagOff('sensitive', true) },
    'preauth-on': { label: 'Require preauthentication', doing: 'Requiring preauthentication', icon: 'ticket', kinds: ['user'], run: flagOff('preauth') },
    'des-off': { label: 'Disable DES', doing: 'Disabling DES', icon: 'ticket', kinds: ['user'], run: flagOff('des') },
    disable: { label: 'Disable', doing: 'Disabling', icon: 'user-off', kinds: ['user', 'computer'], ask: true,
      run: (f) => (f.kind === 'computer' ? [`${pcPath({ name: f.username || f.name })}/enable`, { on: false }] : [`${userPath(f)}/enable`, { on: false }]) },
  };
  const actOf = (s) => (s.act && ACTS[s.act] && can(s.act === 'disable' ? 'ad.enable' : 'ad.change') ? ACTS[s.act] : null);
  const dwFound = (s) => {
    const fs = s.found || [];
    if (!fs.length) return '';
    const a = actOf(s);
    const can1 = (f) => a && a.kinds.includes(f.kind);
    const any = fs.some(can1);
    return `<section class="dw-sec"><div class="dw-sec-head"><h3>Affected objects <small>${fs.length}</small></h3>`
      + (any ? `<button type="button" class="small" data-dwf-act>${svg(a.icon)}${esc(a.label)}</button>` : '') + '</div>'
      + `<div class="dw-list">${fs.map((f, i) => `<div class="mrow">`
        + (any ? (can1(f) ? `<label class="tk"><input type="checkbox" data-dwf-tick="${i}" checked aria-label="Select ${esc(f.name)}"></label>` : '<span class="tk"></span>') : '')
        + `<span class="pic small${f.kind === 'user' ? ' user' : ''}">${f.kind === 'user' ? esc(initials(f.name)) : svg(FOUND_ICON[f.kind] || 'folder')}</span>`
        + `<div class="two"><b>${esc(f.name)}</b><small>${esc(f.where || '')}</small></div>`
        + (PAGE_OF[f.kind] ? `<button type="button" class="at-more dwf-open" data-dwf-open="${i}" data-tip="Open" aria-label="Open ${esc(f.name)}">${svg('right')}</button>` : '')
        + '</div>').join('')}</div>`
      + (fs.length >= 50 ? '<p class="hint">Showing the first 50. The CSV export contains the same 50.</p>' : '')
      + '<p class="hint problem" id="dwf-problem" hidden></p></section>';
  };
  // Runs the check's fix on each ticked one, then reads the checks again.
  const actFound = (s) => {
    const a = actOf(s);
    const fs = [...$('dw-body').querySelectorAll('[data-dwf-tick]:checked')].map((t) => s.found[Number(t.dataset.dwfTick)]);
    if (!a || !fs.length) return;
    const go = async () => {
      const failed = [];
      const ids = []; // the fixes that can be undone
      for (const [i, f] of fs.entries()) {
        R.busy($('dw-body'), `${a.doing}…`, fs.length > 1 ? `${i + 1} of ${fs.length}: ${f.name}` : f.name);
        try {
          const got = await R.send(...a.run(f));
          if (got?.change?.undo) ids.push(got.change.id);
        } catch (err) {
          failed.push(`${f.name}: ${err.message}`);
        }
      }
      delete lists.users;
      delete lists.computers;
      try {
        lists.domain = await R.get('/api/ad/domain');
      } catch { /* the list stays as it was */ }
      R.busy($('dw-body'), '');
      drawAd();
      drawDrawer();
      const done = fs.length - failed.length;
      if (done) R.toast(`Fixed ${count(done, 'account', 'accounts')}.`, s.name, ids.length ? { action: { label: 'Undo', run: () => R.undo(ids), changes: ids } } : {});
      if (failed.length) say($('dwf-problem') ? 'dwf-problem' : 'dir-problem', failed.join(' '));
    };
    if (a.ask) sure(`${a.label} ${count(fs.length, 'account', 'accounts')}?`, `${fs.map((f) => f.name).slice(0, 5).join(', ')}${fs.length > 5 ? ` and ${fs.length - 5} more` : ''}. They can be enabled again later.`, a.label, go);
    else go();
  };
  const saveSetting = async (s) => {
    const el = $('dws-value');
    const value = s.edit === 'switch' ? String(el.querySelector('[aria-pressed="true"]')?.dataset.dwsV === '1') : el.value.trim();
    dwsUser = $('dws-user').value.trim();
    const password = $('dws-pass').value;
    say('dws-problem', '');
    if (!dwsUser || !password) {
      say('dws-problem', 'Enter domain admin credentials to change this setting.');
      return;
    }
    R.busy($('dw-body'), `Changing ${s.name}…`);
    try {
      lists.domain = await R.send(`/api/ad/domain/${encodeURIComponent(s.key)}`, { value, username: dwsUser, password });
      R.busy($('dw-body'), '');
      drawAd();
      drawDrawer();
      R.toast(`${s.name} changed.`, lists.domain.find((x) => x.key === s.key)?.now || '');
    } catch (err) {
      R.busy($('dw-body'), '');
      say('dws-problem', err.message);
    }
  };
  // AD audit and Recycle Bin rows: what changed (and Open it), or what was deleted (and Restore).
  const dwTop = (icon, name, sub, status) => `<div class="dw-head"><span class="pic dw-pic">${svg(icon)}</span><div class="dw-name"><h2 id="dw-title">${esc(name)}</h2>`
    + `${sub ? `<p>${esc(sub)}</p>` : ''}</div><button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div><div class="dw-status">${status}</div>`;
  const PAGE_OF = { user: 'users', group: 'groups', computer: 'computers' };
  // An AD audit row: the old value over the new one in full, every value on its own line, the operation's other changes,
  // and the Security event that recorded it (or why there is none).
  const EVENT_SAYS = { 5136: 'A directory service object was modified.', 5137: 'A directory service object was created.', 5138: 'A directory service object was undeleted.',
    5139: 'A directory service object was moved.', 5141: 'A directory service object was deleted.' };
  const dwSide = (label, vs, missing, cls) => `<div class="dir-ba ${cls}"><span class="l">${label}</span><div class="v">${vs == null ? `<span class="muted">${esc(missing)}</span>`
    : vs.length ? vs.map((v) => `<span>${esc(v)}</span>`).join('') : '<span class="muted">Not set</span>'}</div></div>`;
  const dwChange = (c) => {
    const same = opRows(c);
    const by = c.by ? esc(c.by) : '<span class="muted">Unknown</span>';
    return `<div class="dw-head">${auditPic(c, ' dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(c.name)}</h2>`
      + `<p>${esc(`${c.label}: ${(WHAT_WORD[c.what] || c.what).toLowerCase()}${c.by ? ` by ${c.by}` : ''}`)}</p></div>`
      + `<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
      + `<p class="dw-path">${esc([ad.state.domain, c.where].filter(Boolean).join(' / '))}</p>`
      + `<div class="dw-strip"><div><span class="l">When</span><span class="v">${esc(stamp(c.at))}</span></div><div><span class="l">Modified by</span><span class="v">${by}</span></div>`
      + `<div><span class="l">Originating DC</span><span class="v">${c.dc ? esc(c.dc) : '<span class="muted">Unknown</span>'}</span></div>`
      + `<div><span class="l">Event ID</span><span class="v">${c.event ? esc(String(c.event)) : '<span class="muted">None</span>'}</span></div></div>`
      + (PAGE_OF[c.kind] && c.what !== 'deleted' ? `<div class="dw-bar"><button type="button" class="dw-qa" data-dwa-open>${svg('right')}<span>Open ${esc(c.name)}</span></button></div>` : '')
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>${esc(c.label)}</h3></div><div class="dir-bas">${dwSide('Old value', c.old, 'Not recorded', 'old')}`
      + `${dwSide('New value', c.new, 'Not read. Open the object to see its value now.', 'new')}</div></section>`
      + (same.length > 1 ? `<section class="dw-sec"><div class="dw-sec-head"><h3>Same operation <small>${same.length}</small></h3></div><p class="dw-how">${opSay(same)}.</p>`
        + `<div class="dir-mini"><div class="dir-mini-h"><span>Attribute</span><span>Old value</span><span>New value</span></div>${same.map((x) => `<div class="dir-mini-r${x.id === c.id ? ' on' : ''}" data-dwa-row="${esc(x.id)}">`
          + `<span class="ell">${esc(x.label)}</span><span class="dir-old">${auditVals(x.old, 'Not recorded')}</span><span>${auditVals(x.new, 'Not read')}</span></div>`).join('')}</div></section>` : '')
      + `<section class="dw-sec"><div class="dw-sec-head"><h3>Security event</h3></div>`
      + (c.event ? `<div class="dw-facts">${dwFact('Event', esc(`${c.event}: ${EVENT_SAYS[c.event] || ''}`), true)}${dwFact('Log', esc(`Security on ${c.dc}`))}`
        + `${dwFact('Account', c.account ? esc(c.account) : '')}${c.app ? dwFact('Made in', esc(`${R.app()}, by ${c.by}`), true) : ''}${dwFact('Correlation ID', `<span class="mono">${esc(c.op)}</span>`, true)}</div>`
        : `<p class="dw-how">${esc(unrecorded(c))}</p>`)
      + `</section><p class="dw-src">${esc(c.dn)}</p>`;
  };
  // Why a change has no event, and what that leaves out.
  const unrecorded = (c) => {
    const unknown = [c.old == null ? 'the old value' : '', c.by ? '' : 'who made it'].filter(Boolean);
    return `${c.dc ? `${c.dc} did not record this change in its Security log` : 'No Security log recorded this change'}`
      + `${unknown.length ? `, so ${unknown.join(' and ')} ${unknown.length > 1 ? 'are' : 'is'} not known` : ''}.${c.new != null ? ' The new value was read from Active Directory.' : ''}`;
  };
  const dwDeleted = (d) => dwTop(KIND_ICON[d.kind] || 'folder', d.name, d.wasIn || ad.state.domain, badge('Deleted', 'red'))
    + dwFacts('Deleted', [dwFact('Logon name', e1(d.username)), dwFact('Last known location', esc(d.wasIn || ad.state.domain)), dwFact('Deleted', d.deleted ? esc(day(d.deleted)) : '')])
    + (can('ad.change') ? '<section class="dw-sec"><div class="dw-sec-head"><h3>Restore</h3></div><p class="dw-how">The object is restored to its last known location with its group memberships and attributes.</p>'
      + `<div class="dws-foot"><button type="button" class="primary" data-dwr-restore>${svg('history')}Restore</button></div></section>` : '');
  // Open it: the user, group or computer, in its own page's panel.
  const openChanged = (c) => {
    dwNext = { k: PAGE_OF[c.kind], find: (x) => same(x.dn, c.dn) };
    location.hash = `#ad/${PAGE_OF[c.kind]}`;
  };
  const restoreDeleted = async (d) => {
    R.busy($('dw-body'), `Restoring ${d.name}…`);
    try {
      await R.send('/api/ad/recycle/restore', { dn: d.dn });
      R.panel.close();
      R.toast(`${d.name} restored.`, `Restored to ${d.wasIn || ad.state.domain}.`);
      await loadAd(true);
      drawAd();
    } catch (err) {
      say('dir-problem', err.message);
    } finally {
      R.busy($('dw-body'), '');
    }
  };
  // --- History: one user's, group's or computer's changes (/api/ad/history), a timeline of its operations -----------
  // Read as the panel opens, so the tab says how many, and again when the panel is drawn a minute or more later.
  const hist = { for: '', rows: null, error: '', at: 0, keep: 90 };
  const loadHistory = (x) => {
    const dn = x.dn;
    if (hist.for !== dn) Object.assign(hist, { for: dn, rows: null, error: '' });
    hist.at = Date.now();
    R.get(`/api/ad/history?dn=${encodeURIComponent(dn)}`).then((j) => {
      if (hist.for !== dn || JSON.stringify([j.rows, j.changes]) === JSON.stringify([hist.rows, hist.changes])) return;
      Object.assign(hist, { rows: j.rows, keep: j.keep, changes: j.changes || {}, error: '' });
      if (R.panel.is('ad')) drawDrawer();
    }).catch((err) => {
      if (hist.for !== dn || hist.rows) return; // what was read stays
      hist.error = err.message;
      if (R.panel.is('ad')) drawDrawer();
    });
  };
  // The rows by operation, newest first.
  const histOps = () => {
    const ops = new Map();
    (hist.rows || []).forEach((r) => { if (!ops.has(r.op)) ops.set(r.op, []); ops.get(r.op).push(r); });
    return [...ops.values()];
  };
  const histCount = (x) => (hist.for === x.dn && hist.rows ? histOps().length : null);
  const dwHistory = (x) => {
    const ops = hist.rows ? histOps() : [];
    const head = `<div class="dw-sec-head"><h3>History${hist.rows ? ` <small>${ops.length.toLocaleString()}</small>` : ''}</h3></div>`;
    if (hist.error) return `<section class="dw-sec">${head}<p class="hint problem">${esc(hist.error)}</p></section>`;
    if (!hist.rows) return `<section class="dw-sec">${head}<div data-dwh-skel></div></section>`;
    if (!ops.length) return `<section class="dw-sec">${head}<p class="muted dw-empty">No changes to ${esc(nameOf(x))} in the last ${keepWords(hist.keep)}.</p></section>`;
    let lastDay = '';
    const items = ops.map((xs) => {
      const c = xs[0];
      const d = dayOf(c.at);
      const heading = d !== lastDay ? `<p class="dir-tl-day">${esc(d)}</p>` : '';
      lastDay = d;
      const what = ['made', 'deleted', 'restored', 'moved'].find((w) => xs.some((r) => r.what === w)) || 'changed';
      // Undo for a change the Undo journal kept, when this person may undo it now; Undone once it was.
      const undo = R.undo ? [...new Set(xs.map((r) => r.change).filter(Boolean))].map((id) => {
        const b = hist.changes?.[id];
        if (b?.undone) return `<span class="muted">Undone by ${esc(b.undone.by)}</span>`;
        return b?.undo ? `<button type="button" class="dw-link" data-dwh-undo="${esc(id)}">${svg('restart')}Undo</button>` : '';
      }).join('') : '';
      return `${heading}<div class="dir-tl-ev${c.event ? '' : ' unknown'}"><div class="dir-tl-head"><b>${esc(clock(c.at))}</b>`
        + `<span class="ell">${c.by ? esc(c.by) : '<span class="muted">Unknown</span>'}</span>${c.dc ? `<span class="muted">${esc(c.dc)}</span>` : ''}<span class="grow"></span>${badge(WHAT_WORD[what], WHAT[what])}</div>`
        + `<div class="dir-tl-rows">${xs.map((r) => `<span class="a ell">${esc(r.label)}</span><span class="o">${auditVals(r.old, 'Not recorded')}</span>${svg('arrow')}<span class="n">${auditVals(r.new, 'Not read')}</span>`).join('')}</div>`
        + (c.event ? '' : `<p class="dir-tl-why">${esc(unrecorded(c))}</p>`)
        + (undo ? `<div class="dir-tl-do">${undo}</div>` : '')
        + '</div>';
    }).join('');
    return `<section class="dw-sec dir-hist">${head}<p class="hint dir-tl-lead">Changes to ${esc(nameOf(x))} in the last ${keepWords(hist.keep)}, read from the Security logs of the domain controllers.</p>`
      + `<div class="dir-tl">${items}</div></section>`;
  };

  // Domain Admins in Groups, opened in the panel once its list is there.
  let dwNext = null;
  const openAdmins = () => {
    dwNext = { k: 'groups', find: (g) => /^Domain Admins$/i.test(g.name) };
    location.hash = '#ad/groups';
  };

  const drawDrawer = () => {
    const x = dwObj();
    if (!x) {
      if (R.panel.is('ad')) R.panel.close();
      return;
    }
    const k = dw.kind;
    if (k === 'domain' || k === 'audit' || k === 'recycle') {
      $('dw-body').innerHTML = { domain: dwSetting, audit: dwChange, recycle: dwDeleted }[k](x);
      return;
    }
    // A setting on each DC keeps its place and its Find when it's drawn again after a Re-check.
    if (k === 'dcs') {
      const top = $('dw-body').scrollTop;
      const typing = document.activeElement?.id === 'dcs-find' ? [document.activeElement.selectionStart, document.activeElement.selectionEnd] : null;
      dwKind = hk();
      $('dw-body').innerHTML = R.dcSettings.panel(x, lists.dcs, dwKind);
      $('dw-body').scrollTop = top;
      const find = typing && $('dcs-find');
      if (find) {
        find.focus();
        find.setSelectionRange(...typing);
      }
      return;
    }
    dwNot = blocked(k, x);
    // Each tab that lists things says how many.
    const inGroups = (x.groups || []).length;
    const tabsFor = k === 'groups' ? [['overview', 'Overview'], ['members', 'Members', x.moreMembers ? '1,500+' : x.members], ['memberof', 'Member of', inGroups]] : [['overview', 'Overview'], ['memberof', 'Member of', inGroups]];
    // Its changes, read as the panel opens so the tab says how many, and again after a minute.
    if (x.dn) {
      if (hist.for !== x.dn || Date.now() - hist.at > 60000) loadHistory(x);
      tabsFor.push(['history', 'History', histCount(x)]);
    }
    // Every attribute it has, read as the panel opens so the tab says how many are set.
    if (x.dn && R.attrs) {
      if (dw.attrs.for !== x.dn) loadAttrs(x);
      tabsFor.push(['attrs', 'Attributes', attrsSet(x)]);
    }
    // Notes (notes.js) go by its SID, which a move or rename doesn't change.
    const sid = x.sid || x.more?.sid;
    const notesOn = sid && R.notesTab ? `ad:${sid}` : '';
    if (notesOn) tabsFor.push(['notes', 'Notes', R.notesTab.count(notesOn, () => { if (R.panel.is('ad')) drawDrawer(); }) || null]);
    if (dw.tab === 'notes' && !notesOn) dw.tab = 'overview';
    // Offboarding (offboard.js), once someone has one.
    const leaving = k === 'users' && R.offboard?.has(x.username);
    if (leaving) tabsFor.splice(1, 0, ['offboarding', 'Offboarding']);
    if (dw.tab === 'offboarding' && !leaving) dw.tab = 'overview';
    // Alerts about this user (alerts-page.js), for whoever manages alerts.
    const watched = k === 'users' && R.alerts?.can();
    if (watched) tabsFor.push(['alerts', 'Alerts', R.alerts.about(x).length || null]);
    if (dw.tab === 'alerts' && !watched) dw.tab = 'overview';
    const body = dw.tab === 'alerts' ? R.alerts.pane(x) : dw.tab === 'offboarding' ? R.offboard.pane(x) : dw.tab === 'notes' ? R.notesTab.pane(notesOn, x.name) : dw.tab === 'attrs' ? dwAttrs(x) : dw.tab === 'history' ? dwHistory(x) : dw.tab === 'members' ? dwMembers(x)
      : dw.tab === 'memberof' ? dwMemberOf(k, x) : dwOverview(k, x);
    const scroll = $('dw-body').scrollTop;
    // Typing in Find keeps its place when the panel is drawn again.
    const typing = document.activeElement?.id === 'dwa-find' ? [document.activeElement.selectionStart, document.activeElement.selectionEnd] : null;
    $('dw-body').innerHTML = dwHead(k, x)
      + `<div class="tabs2 dw-tabs" role="tablist">${tabsFor.map(([t, label, n]) => `<a role="tab" data-dw-tab="${t}"${dw.tab === t ? ' aria-current="true"' : ''}>${esc(label)}${n == null ? '' : `<small>${esc(n.toLocaleString())}</small>`}</a>`).join('')}</div>`
      + `<div class="dw-panel">${body}</div>`;
    $('dw-body').querySelectorAll('[data-mixed]').forEach((b) => { b.indeterminate = true; });
    R.skeleton.scan($('dw-body'));
    const histSkel = dw.tab === 'history' && $('dw-body').querySelector('[data-dwh-skel]');
    if (histSkel) R.skeleton(histSkel, 'rows', 4);
    if (dw.tab === 'attrs') {
      const skel = $('dw-body').querySelector('[data-dwa-skel]');
      if (skel) R.skeleton(skel, 'rows', 6);
      drawAttrGrid();
      const find = typing && $('dw-body').querySelector('#dwa-find');
      if (find) {
        find.focus();
        find.setSelectionRange(...typing);
      }
    }
    $('dw-body').scrollTop = scroll;
  };
  const openDrawer = (k, x) => {
    const key = String(AD[k].key(x));
    // Someone being offboarded opens on where that is.
    if (dw.kind !== k || dw.key !== key) Object.assign(dw, { tab: k === 'users' && R.offboard?.active(x.username) ? 'offboarding' : 'overview', members: null, membersFor: '', picks: new Set() });
    dw.kind = k;
    dw.key = key;
    drawDrawer();
    // Not modal: the list beside it stays usable, and another row opens in the same panel.
    R.panel.open('ad', `${k}:${key}`);
    markOpenRow();
  };
  // The row whose panel is open is marked in the list.
  const markOpenRow = () => {
    const on = R.panel.is('ad') && dw.kind === adPage ? dw.key : null;
    $('dir-table').querySelectorAll('.tr[data-i]').forEach((row) => row.classList.toggle('dw-on', on !== null && keyOf(rows[Number(row.dataset.i)]) === on));
  };
  // The side panel (R.panel, console.js) closes itself on Esc, its × and leaving the page; this marks the row.
  R.panel.el.addEventListener('close', markOpenRow);
  // Attributes: Find and Show empty narrow the grid only, so the box keeps its focus; a right-click on a row opens its menu.
  // On each DC: Find and Group by site redraw only the list of DCs, so the box keeps its focus.
  R.panel.el.addEventListener('input', (e) => {
    const x = R.panel.is('ad') && dw.kind === 'dcs' && dwObj();
    if (x) R.dcSettings.input(e, x, lists.dcs, hk());
  });
  R.panel.el.addEventListener('input', (e) => {
    if (e.target.id !== 'dwa-find' || !R.panel.is('ad')) return;
    dw.attrs.find = e.target.value;
    drawAttrGrid();
  });
  R.panel.el.addEventListener('change', (e) => {
    if (e.target.id !== 'dwa-all' || !R.panel.is('ad')) return;
    dw.attrs.all = e.target.checked;
    drawAttrGrid();
  });
  R.panel.el.addEventListener('contextmenu', (e) => {
    const row = R.panel.is('ad') && e.target.closest('.tr[data-dwa]');
    const x = row && dwObj();
    if (!x) return;
    e.preventDefault();
    R.attrs.menu(row.dataset.dwa, e.clientX, e.clientY, attrTarget(dw.kind, x));
  });
  // A tick on Member of or Members, or Select all.
  R.panel.el.addEventListener('change', (e) => {
    const one = e.target.closest('[data-dw-pick]');
    const all = e.target.closest('[data-dw-pick-all]');
    const x = R.panel.is('ad') && dwObj();
    if ((!one && !all) || !x) return;
    if (one) {
      const had = [...dw.picks].find((d) => same(d, one.dataset.dwPick));
      if (had) dw.picks.delete(had);
      else dw.picks.add(one.dataset.dwPick);
    } else {
      const dns = dw.tab === 'members' ? (Array.isArray(dw.members) ? dw.members.map((m) => m.dn) : []) : (x.groups || []).map((g) => g.dn);
      dw.picks = all.checked ? new Set(dns) : new Set();
    }
    drawDrawer();
  });
  // A switch makes its change straight away; the panel shows how it came out.
  R.panel.el.addEventListener('change', (e) => {
    const sw = e.target.closest('[data-dw-on]');
    const x = R.panel.is('ad') && dwObj();
    if (!sw || !x) return;
    runCmd(sw.checked ? sw.dataset.dwOn : sw.dataset.dwOff, dw.kind, [x]);
  });
  R.panel.el.addEventListener('click', (e) => {
    if (!R.panel.is('ad') || e.target.closest('[data-dw-close]')) return;
    const x = dwObj();
    if (!x) return;
    const k = dw.kind;
    if (k === 'audit') {
      const other = e.target.closest('[data-dwa-row]');
      if (e.target.closest('[data-dwa-open]')) openChanged(x);
      else if (other && other.dataset.dwaRow !== x.id) {
        const y = (lists.audit || []).find((r) => r.id === other.dataset.dwaRow);
        if (y) openDrawer('audit', y);
      }
      return;
    }
    if (k === 'recycle') {
      if (e.target.closest('[data-dwr-restore]')) restoreDeleted(x);
      return;
    }
    if (k === 'dcs') {
      R.dcSettings.click(e, x, lists.dcs, recheckDcs, hk());
      return;
    }
    if (k === 'domain') {
      const pick = e.target.closest('[data-dws-v]');
      if (pick) pick.parentElement.querySelectorAll('[data-dws-v]').forEach((b) => { b.setAttribute('aria-pressed', String(b === pick)); b.setAttribute('aria-checked', String(b === pick)); });
      else if (e.target.closest('[data-dws-save]')) saveSetting(x);
      else if (e.target.closest('[data-dws-admins]')) openAdmins();
      else if (e.target.closest('[data-dwf-act]')) actFound(x);
      else if (e.target.closest('[data-dwf-open]')) openChanged(x.found[Number(e.target.closest('[data-dwf-open]').dataset.dwfOpen)]);
      return;
    }
    const t = e.target.closest('[data-dw-tab]');
    if (t) { dw.tab = t.dataset.dwTab; dw.picks.clear(); drawDrawer(); return; }
    // History: Undo, which the Undo journal does (R.undo) for a change it kept.
    const undo = e.target.closest('[data-dwh-undo]');
    if (undo) { R.undo?.(undo.dataset.dwhUndo); return; }
    // Attributes: a row's menu, Export, and the same object in the Attribute browser.
    const am = e.target.closest('[data-dwa-menu]');
    if (am) {
      const r = am.getBoundingClientRect();
      R.attrs.menu(am.dataset.dwaMenu, r.right - 220, r.bottom + 6, attrTarget(k, x));
      return;
    }
    if (e.target.closest('[data-dwa-export]') && dw.attrs.obj?.attributes) { R.attrs.export(dw.attrs.obj); return; }
    if (e.target.closest('[data-dwa-open]')) { location.hash = R.attrs.href(x.dn); return; }
    // Remove what's ticked: its groups on Member of, or a group's members on Members.
    if (e.target.closest('[data-dw-remove]') && dw.picks.size) {
      const dns = [...dw.picks];
      const members = dw.tab === 'members';
      const names = dns.map((d) => (members ? (dw.members || []).find((m) => same(m.dn, d))?.name : (x.groups || []).find((g) => same(g.dn, d))?.name) || d);
      const what = members ? `${count(dns.length, 'member', 'members')} from ${x.name}` : `${nameOf(x)} from ${dns.length === 1 ? names[0] : count(dns.length, 'group', 'groups')}`;
      const go = () => {
        dw.picks.clear();
        if (members) {
          dw.membersFor = ''; // read again after
          each(k, [x], 'Removing members', `Removed ${what}.`, (g) => membersOf(g, [], dns));
        } else each(k, [x], 'Removing from groups', `Removed ${what}.`, (y) => memberOf(k, y, [], dns));
      };
      if (dns.length === 1) go();
      else sure(members ? `Remove ${what}?` : `Remove ${what}?`, names.join(', '), members ? 'Remove members' : 'Remove from groups', go);
      return;
    }
    const cmd = e.target.closest('[data-dw-cmd]');
    if (cmd) { runCmd(cmd.dataset.dwCmd, k, [x]); return; }
    const more = e.target.closest('[data-dw-more]');
    if (more) {
      const r = more.getBoundingClientRect();
      openCmd([x], r.right - CMD_W, r.bottom + 6, k, { panel: true });
      return;
    }
    const leave = e.target.closest('[data-dw-leave]');
    if (leave) {
      const g = (x.groups || []).find((h) => same(h.dn, leave.dataset.dwLeave));
      each(k, [x], 'Removing from group', `Removed ${nameOf(x)} from ${g?.name || 'the group'}.`, (y) => memberOf(k, y, [], [leave.dataset.dwLeave]));
      return;
    }
    const out = e.target.closest('[data-dw-out]');
    if (out) {
      const m = (dw.members || []).find((h) => same(h.dn, out.dataset.dwOut));
      dw.membersFor = ''; // read again after
      each(k, [x], 'Removing member', `Removed ${m?.name || 'the member'} from ${x.name}.`, (g) => membersOf(g, [], [out.dataset.dwOut]));
    }
  });

  // --- Windows accounts on a computer: the New account and password windows, for its Accounts tab on Devices (accounts.js) --

  let pcTarget = null; // { id, name, server, groups, done } of the computer a new account is for

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
    $('secret-title').textContent = many ? 'New passwords' : `Password for ${who}`;
    $('secret-lead').textContent = `${many ? 'Give each user their password.' : `Give this password to ${who}.`}${mustChange ? ' A new password must be set at first sign-in.' : ''}`;
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

  // Active Directory, read again after a change in a dialog.
  const reload = () => loadAd(true).then(drawAd).catch(() => {});

  let resetting = null;
  // done: what to read again afterwards, for a Windows account on Devices; else Active Directory.
  const reset = (who, path, lead, done = null) => {
    resetting = { who, path, users: null, done };
    $('reset-title').textContent = `Reset password for ${who}`;
    $('reset-lead').textContent = lead;
    resetMode('reset-pwmode', 'reset-password');
    $('reset-change').checked = true;
    say('reset-problem', '');
    $('reset-dialog').showModal();
  };
  // Several users: each gets one of its own, unless one is typed for all of them.
  const resetMany = (us) => {
    reset(whoOf(us, 'users'), '', 'Each user gets a unique password unless you enter one for all of them. This also unlocks locked-out accounts.');
    resetting.users = us;
    $('reset-title').textContent = `Reset passwords for ${us.length} users`;
  };
  $('reset-cancel').addEventListener('click', () => $('reset-dialog').close());
  $('reset-go').addEventListener('click', async () => {
    const password = typed('reset-pwmode', 'reset-password');
    if (resetting.users) {
      const mustChange = $('reset-change').checked;
      $('reset-dialog').close();
      const got = await each('users', resetting.users, 'Resetting passwords', `Passwords reset for ${resetting.who}.`, (u) => post(u, '/password', { password, mustChange }));
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
      else if (resetting.done) R.toast(`Password reset for ${resetting.who}.`);
      if (resetting.done) resetting.done();
      else reload();
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
    $('new-dir-groups').innerHTML = joining.map((g, i) => `<span class="gchip">${esc(g.name)}<button type="button" class="chip-x" data-unjoin="${i}" aria-label="Remove ${esc(g.name)}">${svg('x')}</button></span>`).join('')
      || '<span class="muted">Domain Users only</span>';
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
      $('new-lead').textContent = `Created in ${ad.state.domain} as ${ad.state.account}.`;
      drawJoining();
      await placesInto('new-ou', ad.state.newUsers, 'new-problem');
    } else {
      const c = pcTarget;
      $('acct-title').textContent = `New local account on ${c.name}`;
      $('new-lead').textContent = c.server ? 'Created on this server.' : `The client on ${c.name} creates the account immediately.`;
      const common = [ADMINS, 'S-1-5-32-545', 'S-1-5-32-555'];
      const groups = [...c.groups].sort((a, b) => (common.indexOf(b.sid) >= 0) - (common.indexOf(a.sid) >= 0));
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
    none: 'No matching groups.',
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
    say('new-problem', kind === 'pc' && !pcTarget.server ? `Contacting ${pcTarget.name}…` : '');
    try {
      const answer = await R.send(kind === 'ad' ? '/api/ad/users' : `/api/windows/${encodeURIComponent(pcTarget.id)}/accounts`, body);
      $('new-dialog').close();
      if (answer.password) secret(who, answer.password, answer.warning, body.mustChange);
      else if (kind === 'ad') say('dir-note', answer.warning || `${who} created.`);
      else R.toast(`${who} created.`, answer.warning || '', answer.warning ? { tone: 'warn' } : {});
      if (kind === 'ad') {
        delete lists.users; // read again when it's next open, if it was made from another list
        reload();
      } else pcTarget.done?.();
    } catch (err) {
      say('new-problem', err.message);
    } finally {
      $('new-go').disabled = false;
    }
  });
  // For a computer's Accounts tab on Devices (accounts.js): a new account on it, and a new password for one of its accounts.
  R.windowsAccounts = {
    create: (target) => {
      pcTarget = target;
      openNew('pc');
    },
    password: (who, path, lead, done) => reset(who, path, lead, done),
  };

  // A new user, group or computer account, from the bar under the banner.
  const makeNew = async (k) => {
    if (k === 'users') {
      openNew('ad');
      return;
    }
    if (k === 'computers') {
      $('pcnew-name').value = '';
      $('pcnew-about').value = '';
      $('pcnew-lead').textContent = `Created in ${ad.state.domain} as ${ad.state.account}. A computer with this name can then join the domain.`;
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
    $('group-lead').textContent = `Created in ${ad.state.domain} as ${ad.state.account}.`;
    say('grp-problem', '');
    await placesInto('grp-ou', ad.state.newGroups || ad.state.newUsers, 'grp-problem');
    $('group-dialog').showModal();
    $('grp-name').focus();
  };
  const mayMake = () => ad?.state && !ad.state.readOnly;
  $('dir-new').addEventListener('click', () => { if (mayMake()) makeNew(makeKind()); });
  $('dir-new-more').addEventListener('click', () => {
    if (mayMake()) R.picker.menu($('dir-new-split'), MAKES.map(([k, icon, label]) => ({ label, icon, run: () => makeNew(k) })), { key: 'dir.new' });
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
      R.toast(`Computer account ${c.name} created.`, `A computer named ${c.name} can now join the domain using this account.`);
      delete lists.computers;
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
      say('dir-note', `${g.name} created. Open the group to add members.`);
      delete lists.groups;
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
  const PICK_LEADS = { 'dset-scope': 'The service account gets Full Control over users, groups and computers in this OU.', 'new-ou': 'Select the OU for the new user.', 'grp-ou': 'Select the OU for the new group.',
    'adset-ou': 'Default OU for new users.', 'adset-gou': 'Default OU for new groups.', 'adset-cou': 'Default OU for new computers.', 'pcnew-ou': 'Select the OU for the new computer account.',
    'role-scope-add': 'An OU in the role scope. Child OUs are included.' };
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
      html = `<p class="rail-label">${hits.length ? `${hits.length} found` : 'No OUs found'}</p>`
        + hits.map((n) => ouRow(n, svg('folder'), n.name, n.path.split(' / ').slice(0, -1).join(' / '))).join('');
    } else {
      const whole = pick.nodes.find((n) => n.domain);
      html = `<p class="rail-label">${esc(pick.domain)}</p>` + (whole ? ouRow(whole, svg('globe'), 'Entire domain', '') : '')
        + topOus().map((n) => railRow(n, true)).join('');
    }
    $('oupick-list').innerHTML = pick.nodes.length ? html : '<p class="ds-empty oupick-none">No OUs available.</p>';
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
    const said = counts.length > 1 ? `${counts.slice(0, -1).join(', ')} and ${counts[counts.length - 1]}` : counts[0] || 'Empty';
    return `<p class="op-count">${esc(said)}</p>`
      + (got.items.length ? `<div class="op-items">${got.items.map((i) => `<div class="op-item" title="${esc(i.dn)}">${svg(PICK_ICON[i.kind] || 'folder')}<b>${esc(i.name)}</b>`
        + `${i.description ? `<small>${esc(i.description)}</small>` : ''}</div>`).join('')}</div>` : '');
  };
  const drawPickPage = () => {
    const n = pick.nodes.find((x) => same(x.dn, pick.sel));
    $('oupick-use').disabled = !n || !!n.fixed;
    if (!n) {
      $('oupick-page').innerHTML = '<header class="sub-head"><h2>Select an OU</h2><p>Select an OU on the left, or search by name.</p></header>';
      return;
    }
    $('oupick-page').innerHTML = `<header class="sub-head"><h2>${esc(n.domain ? pick.domain : n.name)}</h2><p>${esc(n.domain ? 'Entire domain' : `${pick.domain} / ${n.path}`)}</p></header>`
      + (n.fixed ? `<p class="op-fixed">${svg('lock')}<span>${esc(n.fixed)}</span></p>` : '')
      + `<h3 class="op-h">Contents</h3>${insideHtml(n.dn)}`
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
    pick.nodes = [...select.options].filter((o) => o.value).map((o) => ({ dn: o.value, path: o.textContent, name: dnName(o.value), parent: dnParent(o.value), domain: isDomain(o.value), fixed: o.dataset.fixed || '' }));
    inPlace.clear(); // read afresh each time it opens
    const any = pick.nodes[0]?.dn || '';
    pick.domain = any.split(/(?<!\\),/).filter(isDomain).map((p) => p.slice(3)).join('.');
    $('oupick-lead').textContent = PICK_LEADS[select.id] || '';
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

  // --- Setting up Rabbid Lab's AD account: one window, two kinds ------------------------------------------------------
  // Delegated: svc-rabbidlab, with rights only in the OUs picked. Domain admin: svc-rabbidlab-admin, which Rabbid Lab
  // makes and whose password it changes itself, or a domain admin that's there already. The questions under the kinds
  // answer for the one picked. A domain admin's sign-in makes the account and isn't kept. Change… opens it on the kind
  // in use, to switch to the other.

  let look = null; // what the sign-in read: the domain, its base DN and where accounts go
  const sa = { mode: 'setup', kind: 'delegated', how: 'make', open: new Set(['what']), test: null };
  const SA_KINDS = { delegated: 'Delegated', admin: 'Domain admin' };
  const SA_NAMES = { delegated: 'svc-rabbidlab', admin: 'svc-rabbidlab-admin' };
  const saHave = () => sa.kind === 'admin' && sa.how === 'have';
  const saName = () => (saHave() ? '' : $('dset-account').value.trim());
  const saDomain = () => $('dset-domain').value.trim() || ad?.domain || 'the domain';
  const saSign = () => ({ domain: $('dset-domain').value.trim(), username: $('dset-user').value.trim(), password: $('dset-password').value });
  // The account that's set up, even while its sign-in fails; not the read-only stand-in (Not now).
  const saState = () => (ad?.state && !(ad.state.readOnly && !ad.state.problem) ? ad.state : null);
  // The questions people ask before handing over an account. The answers follow the kind picked.
  const saFacts = () => {
    const d = saDomain();
    const app = R.app();
    const admin = sa.kind === 'admin';
    return [
      ['what', 'info', `What is a service account, and why does ${app} need one?`, ['An Active Directory account used by a program, not a person.',
        `${app} signs in to ${d} with this account to make the changes requested in the console.`,
        'Until one is set up, Active Directory can only be read, with this server\'s computer account, and nothing can be changed.']],
      ['does', 'check', `What does ${app} do with the account?`, ['Reads users, groups, computers, OUs and GPOs.',
        'Makes the changes requested by users with the right role: create, edit, move, disable, delete, reset passwords and change group membership.',
        'Checks every change against the allowed OUs before sending it, and records the change in the activity log with the requesting user.']],
      ['never', 'x', `What does ${app} never do with the account?`, ['The account is never used to sign in to Windows or the console, and its password is never shown.',
        'The domain admin credentials used for setup are used once and not saved.',
        `${app} does not change the account's own permissions, and the console cannot edit, disable or delete the account.`]],
      ['rights', 'shield', 'What permissions does the account need?', admin
        ? [`All Domain Admins permissions in ${d}.`, 'Group Policy changes and push installs work without another sign-in.',
          `If this server is compromised, the attacker gains control of all of ${d}. This is the trade-off.`]
        : ['Full Control over user, group and computer objects in the OUs you select, the same permissions the Delegation of Control Wizard grants.',
          'Group Policy changes and push installs need more permissions, so they ask for domain admin credentials each time. Those credentials are not saved.',
          'If this server is compromised, the attacker gains control of the selected OUs only.']],
      ['password', 'lock', 'Where is the password stored?', [
        saHave() ? `You manage the password. ${app} stores it and does not change it.`
          : admin ? `64 random characters generated by ${app} and never shown. ${app} changes the password every 30 days.`
            : `64 random characters generated by ${app} and never shown. Set to never expire.`,
        'Stored encrypted in the data folder on this server. On Windows the key is protected by DPAPI for the account the service runs as, so a copy of the folder cannot be used on another computer.',
        'The password is never sent to a browser or written to a log.']],
      ['broken', 'alert', 'What happens if the password is rejected, or the account is locked out, disabled or expired?', [
        'Active Directory pages show that the service account sign-in failed and switch to read-only. No data is lost.',
        'An alert is sent, and Settings > Active Directory shows the problem.',
        'Fix the problem there: Repair, Unlock and test, or Enable and test. Each one asks for domain admin credentials once and does not save them.']],
      ['later', 'refresh', 'How do I change or disconnect the account later?', [
        'Settings > Active Directory: change where new users, groups and computers go, switch between Delegated and Domain admin, or disconnect the account.',
        'Disconnect deletes the stored password and stops using the account. Type the account name to confirm.',
        'The account and its permissions stay in Active Directory until you delete them there.']],
    ];
  };
  // What Test sign-in found, or what the sign-in is for until it's run.
  const saResult = () => {
    const t = sa.test;
    const el = $('dset-result');
    el.className = `dset-result${t?.error ? ' bad' : t?.account ? (t.domainAdmin ? ' ok' : ' warn') : ''}`;
    el.innerHTML = t === 'busy' ? `${FLASK}<span>${esc(`Testing sign-in to ${saDomain()}…`)}</span>`
      : t?.error ? `${svg('x')}<span>${esc(`Sign-in failed: ${t.error}`)}</span>`
        : t?.account ? `${svg(t.domainAdmin ? 'check' : 'alert')}<span>${esc(`Signed in to ${saDomain()} as ${t.account}, ${t.domainAdmin ? 'a member of Domain Admins' : 'which is not a member of Domain Admins'}. ${t.ous} ${t.ous === 1 ? 'OU' : 'OUs'} read. No changes made.`)}</span>`
          : `<span>${esc(saHave() ? `${R.app()} uses this account. The password is stored encrypted on this server.` : 'Used once to create the account. Not saved.')}</span>`;
    $('dset-try').disabled = t === 'busy';
  };
  const saDraw = () => {
    const have = saHave();
    const admin = sa.kind === 'admin';
    const cur = sa.mode === 'change' ? saState() : null;
    $('dset-title').textContent = cur ? 'Change the service account' : 'Set up the service account';
    $('dset-sub').textContent = cur ? `In use: ${cur.account}, ${SA_KINDS[cur.kind] || 'an existing account'}. Select a type, then open any question.`
      : 'Select a type, then open any question. The answers match the selected type.';
    $('dset-kinds').querySelectorAll('[data-kind]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.kind === sa.kind)));
    $('dset-qa').innerHTML = saFacts().map(([id, icon, q, a]) => `<div class="dset-q${sa.open.has(id) ? ' open' : ''}"><button type="button" data-q="${id}" aria-expanded="${sa.open.has(id)}">${svg(icon)}<b>${esc(q)}</b>${svg('chevron-down')}</button>`
      + `${sa.open.has(id) ? `<ul>${a.map((l) => `<li>${esc(l)}</li>`).join('')}</ul>` : ''}</div>`).join('');
    $('dset-risk').hidden = !admin;
    $('dset-risk-text').textContent = `Anyone who compromises this server, or signs in to the console as an administrator, can change anything in ${saDomain()}.`;
    $('dset-how').querySelectorAll('[data-how]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.how === sa.how)));
    $('dset-risk-password').textContent = have ? 'An existing member of Domain Admins. The password is stored encrypted on this server. You manage password changes.'
      : `64 random characters generated by ${R.app()} and never shown. ${R.app()} changes the password every 30 days.`;
    $('dset-scope-row').hidden = admin;
    $('dset-account-row').hidden = have;
    $('dset-user-label').textContent = have ? 'Domain admin account to use' : 'Domain admin';
    $('dset-browse').hidden = !have;
    if (!have) $('dset-drop').hidden = true;
    saResult();
    // Change… has nothing to do until another kind is picked.
    const inUse = !!cur && cur.kind === sa.kind && (!admin || (cur.made ? 'make' : 'have') === sa.how);
    const unticked = admin && !$('dset-tick').checked;
    $('dset-foot-note').textContent = inUse ? 'This type is in use. Select the other type to switch.' : unticked ? 'Select the check box above to continue.' : 'No changes are made in Active Directory until you continue.';
    $('dset-go').disabled = inUse || unticked;
    $('dset-go').innerHTML = `${svg('check')}${esc(cur && !inUse && cur.kind !== sa.kind ? `Switch to ${SA_KINDS[sa.kind]}` : have ? 'Use this domain admin account' : `Create ${saName() || 'the account'}`)}`;
  };
  // The OUs a delegated account can be given. This server reads them before anyone signs in, where it can; the sign-in reads them otherwise.
  const saScopes = (list, baseDn, domain) => {
    const was = $('dset-scope').value;
    $('dset-scope').innerHTML = `<option value="${esc(baseDn)}">Entire domain (${esc(domain)})</option>`
      + list.filter((p) => !/^CN=/i.test(p.dn)).map((p) => `<option value="${esc(p.dn)}"${p.dn === was ? ' selected' : ''}>${esc(p.path || p.name)}</option>`).join('');
  };
  let scopeAsk = 0;
  const saReadScopes = async () => {
    const n = ++scopeAsk;
    const domain = $('dset-domain').value.trim();
    if (!domain) return;
    let list = [];
    try {
      list = await R.get(`/api/ad/look/places?domain=${encodeURIComponent(domain)}`);
    } catch {
      // Not readable as this server (Docker, or outside the domain): the sign-in reads them.
    }
    if (n === scopeAsk && !look) saScopes(list, domain.split('.').map((p) => `DC=${p}`).join(','), domain);
  };
  // Set it up, from any page that needs Rabbid Lab's AD account; or change it (Settings), which opens on the kind in use.
  const openSetup = (domain, mode = 'setup') => {
    const cur = mode === 'change' ? saState() : null;
    Object.assign(sa, { mode: cur ? 'change' : 'setup', kind: cur?.kind === 'admin' ? 'admin' : 'delegated', how: cur?.kind === 'admin' && !cur.made ? 'have' : 'make', open: new Set(['what']), test: null });
    $('dset-domain').value = domain || ad?.domain || '';
    const me = R.me();
    $('dset-user').value = me?.source === 'ad' && !saHave() ? me.username : '';
    $('dset-password').value = '';
    $('dset-account').value = SA_NAMES[sa.kind];
    $('dset-tick').checked = false;
    $('dset-scope').innerHTML = '';
    ['dset-user', 'dset-password'].forEach((id) => $(id).classList.remove('bad'));
    look = null;
    say('dset-problem', '');
    saDraw();
    $('dset-dialog').showModal();
    $('dset-dialog').scrollTop = 0;
    saReadScopes();
  };
  // The card each Active Directory page shows until the service account is set up (directory.js draws it too).
  // No service account yet: Active Directory's pages wait under a cover, with what to do in the middle of it.
  const setupCard = (el, info) => {
    const allowed = can('ad.connect');
    const card = (title, text, acts) => `<div class="dgate-card"><span class="big-ic ember">${svg('key')}</span><div class="ustatus-text"><b>${esc(title)}</b>`
      + `<span>${esc(text)}</span></div><div class="dgate-acts">${acts}</div></div>`;
    if (!info?.domain) {
      // No domain here: these pages have nothing to manage until one is connected.
      el.innerHTML = card('This server is not joined to a domain', allowed ? 'Connect a domain to manage it here.' : 'No domain is connected.',
        allowed ? '<button type="button" class="primary" data-setup>Connect a domain</button>' : '');
    } else {
      // Not now: read-only as this server for the whole server, which only people who can set up the account may choose and see.
      el.innerHTML = card('Set up the service account', allowed ? `${R.app()} makes all changes in ${info.domain} as this account.` : 'An admin with permission to set up the service account must do this first.',
        (allowed ? '<button type="button" class="primary" data-setup>Set up</button>' : '') + (allowed && info.canRead ? '<button type="button" data-not-now>Not now</button>' : ''));
    }
    el.hidden = false;
    el.querySelector('[data-setup]')?.addEventListener('click', () => openSetup(info?.domain));
    el.querySelector('[data-not-now]')?.addEventListener('click', () => readOnly(true));
  };
  R.adSetupCard = setupCard;
  // Another page asks for a user, group or computer (the org chart, Ctrl+K search): its list, with it open. page is users,
  // groups or computers; key is what that list keys rows by (username, DN or name).
  let wanted = null;
  const openWantedAd = () => {
    if (!wanted || adPage !== wanted.page || !lists[adPage]) return;
    const x = lists[adPage].find((v) => same(AD[adPage].key(v), wanted.key));
    wanted = null;
    if (x) openDrawer(adPage, x);
  };
  R.adOpen = (page, key) => {
    wanted = { page, key };
    if (R.where()[0] === 'ad' && adPage === page && lists[page]) openWantedAd(); // already there: the address doesn't change
    else location.hash = `#ad/${page}`;
  };
  R.adOpenUser = (username) => R.adOpen('users', username);
  // Turns read-only on (confirming it's you first) or off, then every Active Directory page reads again.
  const readOnly = async (on) => {
    try {
      await R.devices.confirmed(() => R.send('/api/ad/read-only', { on }));
    } catch (err) {
      if (!err.cancelled) R.toast('Could not change read-only mode.', err.message, { tone: 'warn' });
      return;
    }
    R.forget();
    ad = null;
    Object.keys(lists).forEach((k) => delete lists[k]);
    if (R.where()[0] === 'ad') refreshAd(true);
    if (R.where()[0] === 'settings' && R.where()[1] === 'ad') drawAdSettings(true);
    document.dispatchEvent(new CustomEvent('rabbidlab:ad'));
  };
  // Read-only is the server's to say now: forget what an earlier version kept in this browser.
  try {
    localStorage.removeItem('rabbidlab.ad.notNow');
  } catch {
    // Nothing kept.
  }
  R.adInfo = async () => {
    const info = await R.get('/api/ad');
    return !info.state && info.reading ? { ...info, state: info.reading } : info;
  };
  // Picking a kind: its questions, its fields and its default name.
  $('dset-kinds').addEventListener('click', (e) => {
    const b = e.target.closest('[data-kind]');
    if (!b || b.dataset.kind === sa.kind) return;
    // The name follows the kind unless someone typed their own.
    if (Object.values(SA_NAMES).includes($('dset-account').value.trim()) || !$('dset-account').value.trim()) $('dset-account').value = SA_NAMES[b.dataset.kind];
    Object.assign(sa, { kind: b.dataset.kind, test: null });
    $('dset-tick').checked = false;
    say('dset-problem', '');
    saDraw();
  });
  // A domain admin: one Rabbid Lab makes, or one that's there already.
  $('dset-how').addEventListener('click', (e) => {
    const b = e.target.closest('[data-how]');
    if (!b || b.dataset.how === sa.how) return;
    Object.assign(sa, { how: b.dataset.how, test: null });
    // Your own name fills the domain admin box; the account to use is someone else.
    const mine = R.me()?.source === 'ad' ? R.me().username : '';
    if (saHave() && $('dset-user').value === mine) $('dset-user').value = '';
    if (!saHave() && !$('dset-user').value) $('dset-user').value = mine;
    say('dset-problem', '');
    saDraw();
  });
  $('dset-qa').addEventListener('click', (e) => {
    const b = e.target.closest('[data-q]');
    if (!b) return;
    if (!sa.open.delete(b.dataset.q)) sa.open.add(b.dataset.q);
    saDraw();
  });
  $('dset-tick').addEventListener('change', saDraw);
  $('dset-account').addEventListener('input', saDraw);
  // A changed sign-in is read and tested again.
  ['dset-domain', 'dset-user', 'dset-password'].forEach((id) => $(id).addEventListener('input', () => {
    ['dset-user', 'dset-password'].forEach((x) => $(x).classList.remove('bad'));
    look = null;
    if (sa.test) {
      sa.test = null;
      saResult();
    }
  }));
  $('dset-domain').addEventListener('change', saReadScopes);
  $('dset-try').addEventListener('click', async () => {
    sa.test = 'busy';
    saResult();
    try {
      sa.test = await R.send('/api/ad/test', saSign());
    } catch (err) {
      sa.test = { error: err.message };
    }
    saResult();
  });
  // Use an existing account: type it, or pick it from AD as you type. AD is read as this server's own computer account,
  // so no login is needed for the list; with nothing typed it shows the ones that look like service accounts.
  let acctTimer = 0;
  let acctAsk = 0;
  let acctHits = [];
  const acctFind = () => {
    if (!saHave()) return;
    clearTimeout(acctTimer);
    const n = ++acctAsk;
    const q = $('dset-user').value.trim();
    acctTimer = setTimeout(async () => {
      let html;
      try {
        acctHits = await R.get(`/api/ad/look/accounts?domain=${encodeURIComponent($('dset-domain').value.trim())}&q=${encodeURIComponent(q)}`);
        if (n !== acctAsk) return;
        html = (q ? '' : '<p class="drop-note">Service accounts in Active Directory</p>')
          + (acctHits.map((h, i) => R.option(` data-acct="${i}"`, `<span class="pic">${svg('user')}</span>`, h.enabled ? h.username : `${h.username} (disabled)`,
            [h.name, h.description, h.where].filter(Boolean).join(' · '))).join('') || `<p class="drop-note">${q ? 'No matching accounts.' : 'No service accounts found. Type a name to search.'}</p>`);
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
    const head = q ? `<header class="sub-head"><h2>Search results</h2><p>Accounts matching “${esc(q)}” in ${esc(apDomain())}</p></header>`
      : `<header class="sub-head"><h2>${esc(place?.name || apDomain())}</h2><p>${esc(place ? `${apDomain()} / ${place.path}` : apDomain())}</p></header>`;
    const body = got.loading ? `<div class="op-items ap-items">${'<i class="sk op-sk"></i>'.repeat(6)}</div>`
      : got.problem ? `<p class="hint problem">${esc(got.problem)}</p>`
      : got.items.length ? `<p class="op-count">${got.items.length} account${got.items.length === 1 ? '' : 's'}</p><div class="op-items ap-items">${got.items.map((h) => `<button type="button" class="op-item ap-acct${same(ap.sel, h.username) ? ' on' : ''}" data-ap-acct="${esc(h.username)}"${h.enabled ? '' : ' data-off'}>`
        + `${svg('user')}<b>${esc(h.username)}${h.enabled ? '' : ' (disabled)'}</b><small>${esc([h.name, h.description, q ? h.where : ''].filter(Boolean).join(' · '))}</small></button>`).join('')}</div>`
      : `<p class="op-count">${q ? 'No matching accounts.' : 'No user accounts in this OU. Select another OU on the left, or search by name.'}</p>`;
    $('acctpick-page').innerHTML = head + body;
    $('acctpick-use').disabled = !ap.sel;
  };
  const apOpen = async () => {
    if (!apDomain()) {
      say('dset-problem', 'Enter the domain first.');
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
    if (e.key === 'Enter' && e.target.matches('.dset-in input')) $('dset-try').click();
  });
  ['dset-cancel', 'dset-x'].forEach((id) => $(id).addEventListener('click', () => {
    $('dset-password').value = '';
    $('dset-dialog').close();
  }));
  $('dset-go').addEventListener('click', async () => {
    const have = saHave();
    const sign = saSign();
    const was = sa.mode === 'change' ? saState() : null;
    say('dset-problem', '');
    if (!have && !saName()) {
      say('dset-problem', 'Enter a name for the service account.');
      $('dset-account').focus();
      return;
    }
    $('dset-go').disabled = true;
    R.busy($('dset-dialog'), have ? 'Checking the account…' : `Creating ${saName()}…`);
    try {
      if (!have && !look) {
        // The sign-in reads where accounts go, and the OUs when this server couldn't read them itself.
        const listed = $('dset-scope').options.length > 1;
        look = await R.send('/api/ad/look', sign);
        saScopes(look.places, look.baseDn, look.domain);
        if (sa.kind === 'delegated' && !listed && $('dset-scope').options.length > 1) {
          say('dset-problem', 'Select the OUs to manage, then continue.');
          return;
        }
      }
      const answer = await R.send('/api/ad/connect', { ...sign, kind: sa.kind, existing: have, account: saName(), ou: look?.accountOu, scope: sa.kind === 'admin' ? '' : $('dset-scope').value });
      const s = answer.state;
      $('dset-password').value = '';
      $('dset-dialog').close();
      const left = was && was.account !== s.account ? ` ${was.account} stays in Active Directory until you delete it.` : '';
      if (answer.warning) R.toast('Service account set up.', answer.warning, { tone: 'warn' });
      else R.toast(was ? `Switched to ${SA_KINDS[s.kind] || s.account}.` : have ? 'Service account set up.' : 'Service account created.', `${R.app()} now makes Active Directory changes with ${s.account}.${left}`);
      places = null;
      R.forget();
      refreshAd(true);
      document.dispatchEvent(new CustomEvent('rabbidlab:ad')); // the Org chart and Attribute browser read again
    } catch (err) {
      // A wrong sign-in: its two fields outlined, and the password ready to type again.
      const wrong = /username or password/i.test(err.message);
      say('dset-problem', err.message);
      if (wrong) {
        ['dset-user', 'dset-password'].forEach((id) => $(id).classList.add('bad'));
        $('dset-password').select();
      }
    } finally {
      R.busy($('dset-dialog'), '');
      saDraw();
    }
  });

  // Settings, Active Directory: the service account, how it's doing, where new accounts go, and disconnecting it. The gear opens it.
  $('dir-settings').addEventListener('click', () => { location.hash = '#settings/ad'; });
  // How its sign-in can fail, and the button that fixes each.
  const BROKEN = {
    rejected: { badge: 'Sign-in failed', fix: 'Repair', how: 'Resets the password to a new random value and tests sign-in.', done: 'Service account repaired.' },
    locked: { badge: 'Locked out', fix: 'Unlock and test', how: 'Unlocks the account and tests sign-in.', done: 'Service account unlocked.' },
    disabled: { badge: 'Disabled', fix: 'Enable and test', how: 'Enables the account, clears its expiration date and tests sign-in.', done: 'Service account enabled.' },
  };
  // An account Rabbid Lab was given has a password its owner changes: the new one fixes a rejected sign-in.
  const saOwn = (s) => s.problem === 'rejected' && !s.made;
  const drawAdSettings = async (fresh) => {
    say('adset-problem', '');
    try {
      if (fresh || !ad) ad = await R.adInfo();
    } catch (err) {
      say('adset-problem', err.message);
      // The placeholders give way to a plain heading when nothing was read before.
      if ($('as-account').querySelector('.sk')) $('as-account').textContent = 'Service account';
      if ($('as-about').querySelector('.sk')) $('as-about').textContent = '';
      return;
    }
    const s = saState();
    // Read-only (Not now) is no account at all here.
    const reading = !s && !!ad?.state?.readOnly;
    const B = s?.problem ? BROKEN[s.problem] : null;
    $('as-account').textContent = s ? s.account : 'No service account';
    $('as-state').hidden = !s;
    $('as-state').className = `ubadge ${B ? 'red' : 'green'}`;
    $('as-state').textContent = B ? B.badge : 'Connected';
    $('as-about').textContent = s
      ? `${s.kind === 'admin' ? `Domain admin in ${s.domain}.` : `In ${s.domain}, ${s.made ? 'with delegated permissions on' : 'scoped to'} ${s.scopeName}.`} ${s.made ? 'Created' : 'Connected'} by ${s.by}${s.at ? ` on ${R.when(s.at)}` : ''}. The password is stored encrypted on this server.`
      : reading ? `Read-only: ${ad.domain} is read with this server's computer account. Only admins who can set up the service account, or whose role allows read-only access, can see it. Set up a service account to make changes.`
      : ad?.domain ? `${R.app()} makes changes in ${ad.domain} as a service account. Set up a service account to use the Active Directory pages.` : 'This server is not joined to a domain. Connect a domain to manage it here.';
    $('as-setup').hidden = !!s;
    $('as-ro-off').hidden = !reading;
    $('as-setup').textContent = ad?.domain ? 'Set up' : 'Connect a domain';
    $('as-test').hidden = !s;
    $('as-change').hidden = !s;
    say('as-broken', B ? `${s.problemText} Active Directory pages are read-only until this is fixed.` : '');
    $('as-fix-row').hidden = !B;
    if (B) {
      const own = saOwn(s);
      $('as-fix-title').textContent = own ? 'Update password' : B.fix;
      $('as-fix-text').textContent = own ? `Enter the new password for ${s.account}. ${R.app()} tests it and stores it encrypted.` : `${B.how} Requires domain admin credentials, which are not saved.`;
      $('as-fix').textContent = own ? 'Update password' : B.fix;
    }
    ['as-password-row', 'as-alert-row', 'as-ou-row', 'as-gou-row', 'as-cou-row'].forEach((id) => { $(id).hidden = !s; });
    $('as-danger').classList.toggle('gone', !s);
    if (!s) return;
    $('as-password-text').textContent = s.rotates ? `Changed by ${R.app()} every 30 days. Last changed ${R.when(s.passwordSet || s.at)}. Next change ${R.when(s.passwordNext)}.`
      : s.made ? 'A long random password. Set to never expire.' : `Managed by you. ${R.app()} stores the password encrypted and does not change it.`;
    $('as-password-value').textContent = s.rotates ? '' : s.made ? 'Never expires' : 'Managed by you';
    $('as-rotate').hidden = !s.rotates;
    $('adset-stop-title').textContent = 'Disconnect service account';
    $('adset-stop-text').textContent = `${s.account}: ${R.app()} deletes the stored password and stops using the account.${s.made ? ' The account and its permissions remain in AD until you delete them.' : ''}`;
    // Where new things go can't be read or saved while its sign-in fails.
    const ous = ['adset-ou', 'adset-gou', 'adset-cou'];
    ous.forEach((id) => { $(id).disabled = true; });
    if (B) return;
    await placesInto('adset-ou', s.newUsers, 'adset-problem');
    $('adset-gou').innerHTML = $('adset-ou').innerHTML;
    $('adset-cou').innerHTML = $('adset-ou').innerHTML;
    $('adset-gou').value = s.newGroups || s.newUsers;
    $('adset-cou').value = s.newComputers || '';
    ous.forEach((id) => { $(id).disabled = false; });
  };
  // After the account changes hands or state: every Active Directory page reads again.
  const saChanged = (info) => {
    if (info?.state !== undefined && ad) ad = { ...ad, state: info.state };
    places = null;
    R.forget();
    Object.keys(lists).forEach((k) => delete lists[k]);
    drawAdSettings(true);
    document.dispatchEvent(new CustomEvent('rabbidlab:ad'));
  };
  $('as-setup').addEventListener('click', () => openSetup(ad?.domain));
  $('as-change').addEventListener('click', () => openSetup(ad?.domain, 'change'));
  $('as-ro-off').addEventListener('click', () => readOnly(false));
  $('as-test').addEventListener('click', async () => {
    const s = saState();
    $('as-test').disabled = true;
    R.toast('Testing sign-in', `${s.domain}…`, { stay: true });
    try {
      const answer = await R.send('/api/ad/service/test', {});
      const now = answer.state;
      if (now?.problem) R.toast('Sign-in test failed.', now.problemText, { tone: 'warn' });
      else R.toast('Sign-in test passed.', `${s.account} signed in to ${s.domain}.`);
      if ((now?.problem || '') !== (s.problem || '')) saChanged(answer);
    } catch (err) {
      R.toast('Could not test sign-in.', err.message, { tone: 'warn' });
    } finally {
      $('as-test').disabled = false;
    }
  });
  $('as-rotate').addEventListener('click', async () => {
    $('as-rotate').disabled = true;
    try {
      const answer = await R.devices.confirmed(() => R.send('/api/ad/service/password', {}));
      R.toast('Password changed.', `Next change on ${R.when(answer.next)}.`);
      saChanged(answer);
    } catch (err) {
      if (!err.cancelled) R.toast('Could not change the password.', err.message, { tone: 'warn' });
    } finally {
      $('as-rotate').disabled = false;
    }
  });
  // The fix: a domain admin's sign-in, used once, or the account's own new password.
  $('as-fix').addEventListener('click', () => {
    const s = saState();
    if (!s?.problem) return;
    const own = saOwn(s);
    const B = BROKEN[s.problem];
    $('svcfix-title').textContent = own ? 'Update password' : B.fix;
    $('svcfix-lead').textContent = own ? `Enter the new password for ${s.account}. It is tested, then stored encrypted on this server.` : `${B.how} Enter domain admin credentials. They are used once and not saved.`;
    $('svcfix-admin').hidden = own;
    $('svcfix-own').hidden = !own;
    $('svcfix-own-label').textContent = `New password for ${s.account}`;
    $('svcfix-user').value = R.me()?.source === 'ad' ? R.me().username : '';
    $('svcfix-password').value = '';
    $('svcfix-account-password').value = '';
    $('svcfix-go').textContent = own ? 'Update password' : B.fix;
    say('svcfix-problem', '');
    $('svcfix-dialog').showModal();
    $(own ? 'svcfix-account-password' : $('svcfix-user').value ? 'svcfix-password' : 'svcfix-user').focus();
  });
  const fixClose = () => {
    ['svcfix-password', 'svcfix-account-password'].forEach((id) => { $(id).value = ''; });
    $('svcfix-dialog').close();
  };
  $('svcfix-cancel').addEventListener('click', fixClose);
  $('svcfix-dialog').addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.matches('input')) $('svcfix-go').click(); });
  $('svcfix-go').addEventListener('click', async () => {
    const was = saState();
    say('svcfix-problem', '');
    $('svcfix-go').disabled = true;
    R.busy($('svcfix-dialog'), `${$('svcfix-go').textContent}…`);
    try {
      const answer = await R.devices.confirmed(() => R.send('/api/ad/service/repair', { username: $('svcfix-user').value.trim(), password: $('svcfix-password').value, accountPassword: $('svcfix-account-password').value }));
      fixClose();
      const now = answer.state;
      if (now?.problem) R.toast(`${was.account} still cannot sign in.`, now.problemText, { tone: 'warn' });
      else R.toast(saOwn(was) ? 'Password updated.' : BROKEN[was.problem]?.done || 'Service account repaired.', 'Sign-in test passed. Active Directory pages are available again.');
      saChanged(answer);
    } catch (err) {
      if (!err.cancelled) say('svcfix-problem', err.message);
    } finally {
      R.busy($('svcfix-dialog'), '');
      $('svcfix-go').disabled = false;
    }
  });
  $('dset-dialog').addEventListener('close', () => { if (R.where()[0] === 'settings' && R.where()[1] === 'ad') drawAdSettings(true); });
  // Each default OU saves as it changes: users, groups and computers.
  [['adset-ou', 'newUsers'], ['adset-gou', 'newGroups'], ['adset-cou', 'newComputers']].forEach(([id, field]) => $(id).addEventListener('change', async () => {
    say('as-done', '');
    try {
      await R.send('/api/ad/settings', { [field]: $(id).value });
      ad = await R.adInfo();
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
    sure(`Disconnect ${s.account}?`, `${R.app()} cannot make AD changes until a service account is set up again.${s.made ? ` ${s.account} remains in AD until you delete it.` : ''}`,
      'Disconnect', async () => {
        try {
          await R.send('/api/ad/disconnect', {});
          say('dir-note', s.made ? `${s.account} disconnected. If you no longer need the account, delete it in Active Directory Users and Computers.` : `${s.account} disconnected.`);
          say('as-done', s.made ? `${s.account} disconnected. If you no longer need the account, delete it in Active Directory Users and Computers.` : `${s.account} disconnected.`);
        } catch (err) {
          say('dir-problem', err.message);
        }
        places = null;
        R.forget();
        await refreshAd(true);
        drawAdSettings();
        document.dispatchEvent(new CustomEvent('rabbidlab:ad'));
      }, { type: s.account });
  });

  // --- Showing the pages -------------------------------------------------------------------------------------------

  const refreshAd = async (fresh) => {
    const [, , rest] = R.where();
    adPage = AD[rest[0]] ? rest[0] : 'users';
    adView = viewOf(rest[1]).key;
    adFailed = '';
    if (fresh) delete lists[adPage]; // read again below, so it's loading until then
    // The placeholder rows straight away, every time a list has to be read, while the domain is looked at too. The
    // setup card takes their place when there's no account. This page's tabs, views and bars, with no counts yet,
    // not the last page's.
    if (fresh || !ad || !lists[adPage]) {
      $('dir-lib').hidden = false;
      R.skeleton($('dir-table'), 'rows', 8);
      drawHero();
      drawTabs();
      drawViews();
      adBar.draw();
      adGrid.tools($('dir-tools'), $('dir-rows'));
    }
    try {
      if (fresh || !ad) {
        ad = await R.adInfo();
        Object.keys(lists).forEach((k) => delete lists[k]);
      }
      await loadAd(false);
    } catch (err) {
      adFailed = err.message;
      say('dir-problem', err.message);
    }
    drawAd();
    // Asked for from another page (the org chart, search): that one, open in the panel, once the list is in.
    openWantedAd();
  };
  let lastAdPage = null;
  document.addEventListener('rabbidlab:show', (e) => {
    const [pageName, , rest] = e.detail;
    if (pageName === 'ad') {
      // A new page of Active Directory is read again; another view of the same page draws from what's loaded.
      const fresh = lastAdPage !== rest[0];
      lastAdPage = rest[0];
      say('dir-note', '');
      if (fresh) {
        // Another page starts on its first tab with Find cleared; its columns, sort and filter stay as they were.
        adBar.forget();
        adTab = '';
      }
      refreshAd(fresh);
    } else {
      lastAdPage = null;
    }
  });
})();
