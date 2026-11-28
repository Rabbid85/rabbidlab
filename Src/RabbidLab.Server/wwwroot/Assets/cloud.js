// Microsoft cloud: Entra ID, Exchange, Microsoft 365 and Azure (/api/cloud, CloudApi.cs), each a page laid out like
// Active Directory: a banner, its lists down the left with their views, the open list's grid, and a row's side panel.
// Until the tenant is connected every page waits under a cover with Connect: Microsoft's device sign-in, then each
// step as the server does it. Read-only for now.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, get, send, toast, sure, panel, head, day, stamp, badge, muted, facts, section, none, count: plural, stat, since } = R;

  const time = (iso) => (iso ? new Date(iso).getTime() : 0);
  const mono = (text) => `<span class="mono muted ell">${esc(text || '')}</span>`;
  const who = (icon, name) => `<div class="who"><span class="pic">${svg(icon)}</span><b class="ell">${esc(name || '')}</b></div>`;
  const ENTRA = 'https://entra.microsoft.com/#view';
  const EXO = 'https://admin.exchange.microsoft.com/#';

  // --- What each page lists ------------------------------------------------------------------------------------------
  const on = (yes) => (yes ? badge('Enabled', 'green') : badge('Disabled', 'grey'));
  const users = (extra = []) => [
    { key: 'name', label: 'Name', w: 220, fixed: true, get: (u) => u.name || '', cell: (u) => who('user', u.name) },
    { key: 'upn', label: 'User principal name', w: 260, on: true, get: (u) => u.upn || '' },
    { key: 'enabled', label: 'Account', w: 110, on: true, get: (u) => (u.enabled ? 'Enabled' : 'Disabled'), cell: (u) => on(u.enabled) },
    ...extra,
    { key: 'source', label: 'Source', w: 140, on: true, get: (u) => (u.synced ? 'Synced from AD' : 'Cloud only') },
    { key: 'type', label: 'User type', w: 100, on: true, get: (u) => (u.guest ? 'Guest' : 'Member') },
    { key: 'title', label: 'Job title', w: 160, get: (u) => u.title || '' },
    { key: 'department', label: 'Department', w: 150, get: (u) => u.department || '' },
    { key: 'mail', label: 'Email', w: 230, get: (u) => u.mail || '' },
    { key: 'created', label: 'Created', w: 120, get: (u) => time(u.created), text: (u) => u.created || '', cell: (u) => esc(day(u.created)) },
    { key: 'id', label: 'Object ID', w: 290, get: (u) => u.id, cell: (u) => mono(u.id) },
  ];
  const licences = { key: 'licenses', label: 'Licences', w: 100, on: true, get: (u) => u.licenses, cell: (u) => (u.licenses ? esc(String(u.licenses)) : badge('None', 'amber')) };
  const userViews = [['all', 'All users', () => true], ['enabled', 'Enabled', (u) => u.enabled], ['disabled', 'Disabled', (u) => !u.enabled, 'grey'],
    ['guests', 'Guests', (u) => u.guest], ['synced', 'Synced from AD', (u) => u.synced], ['cloud', 'Cloud only', (u) => !u.synced],
    ['unlicensed', 'No licence', (u) => !u.licenses && !u.guest, 'amber']];

  const SERVICES = {
    entra: {
      name: 'Entra ID', icon: 'user-check',
      lists: {
        users: {
          name: 'Users', one: 'User', icon: 'user', path: '/api/cloud/entra/users', lead: 'Every user in the tenant, synced from Active Directory or made in the cloud.', noun: 'users',
          sort: { key: 'name', dir: 1 }, views: userViews, cols: users([licences]), open: (u) => `${ENTRA}/Microsoft_AAD_UsersAndTenants/UserProfileMenuBlade/~/overview/userId/${u.id}`,
        },
        groups: {
          name: 'Groups', one: 'Group', icon: 'users', path: '/api/cloud/entra/groups', lead: 'Security groups, Microsoft 365 groups and distribution lists.', noun: 'groups',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All groups', () => true], ['m365', 'Microsoft 365', (g) => g.kind === 'Microsoft 365'], ['security', 'Security', (g) => g.kind === 'Security'],
            ['mailsec', 'Mail-enabled security', (g) => g.kind === 'Mail-enabled security'], ['dl', 'Distribution lists', (g) => g.kind === 'Distribution list'],
            ['dynamic', 'Dynamic membership', (g) => g.dynamic], ['synced', 'Synced from AD', (g) => g.synced]],
          cols: [
            { key: 'name', label: 'Name', w: 230, fixed: true, get: (g) => g.name || '', cell: (g) => who('users', g.name) },
            { key: 'kind', label: 'Group type', w: 170, on: true, get: (g) => g.kind },
            { key: 'mail', label: 'Email', w: 230, on: true, get: (g) => g.mail || '', cell: (g) => (g.mail ? esc(g.mail) : muted('None')) },
            { key: 'members', label: 'Membership', w: 120, on: true, get: (g) => (g.dynamic ? 'Dynamic' : 'Assigned') },
            { key: 'source', label: 'Source', w: 140, on: true, get: (g) => (g.synced ? 'Synced from AD' : 'Cloud only') },
            { key: 'description', label: 'Description', w: 260, get: (g) => g.description || '' },
            { key: 'created', label: 'Created', w: 120, get: (g) => time(g.created), text: (g) => g.created || '', cell: (g) => esc(day(g.created)) },
            { key: 'id', label: 'Object ID', w: 290, get: (g) => g.id, cell: (g) => mono(g.id) },
          ],
          open: (g) => `${ENTRA}/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${g.id}`,
        },
        devices: {
          name: 'Devices', one: 'Device', icon: 'laptop', path: '/api/cloud/entra/devices', lead: 'Computers and phones joined to or registered with Microsoft Entra ID.', noun: 'devices',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All devices', () => true], ['joined', 'Microsoft Entra joined', (d) => d.join === 'Microsoft Entra joined'], ['hybrid', 'Hybrid joined', (d) => d.join === 'Hybrid joined'],
            ['registered', 'Registered', (d) => d.join === 'Registered'], ['noncompliant', 'Not compliant', (d) => d.compliant === false, 'amber'],
            ['stale', 'No sign-in for 90 days', (d) => !d.seen || Date.now() - time(d.seen) > 90 * 86400000, 'amber']],
          cols: [
            { key: 'name', label: 'Name', w: 200, fixed: true, get: (d) => d.name || '', cell: (d) => who('laptop', d.name) },
            { key: 'os', label: 'Operating system', w: 190, on: true, get: (d) => d.os || '' },
            { key: 'join', label: 'Join type', w: 170, on: true, get: (d) => d.join || '' },
            { key: 'compliant', label: 'Compliant', w: 110, on: true, get: (d) => (d.compliant == null ? 'Not evaluated' : d.compliant ? 'Yes' : 'No'),
              cell: (d) => (d.compliant == null ? muted('Not evaluated') : d.compliant ? badge('Yes', 'green') : badge('No', 'amber')) },
            { key: 'seen', label: 'Last sign-in', w: 130, on: true, get: (d) => time(d.seen), text: (d) => d.seen || '', cell: (d) => (d.seen ? esc(since(d.seen)) : muted('Never')) },
            { key: 'enabled', label: 'Account', w: 110, on: true, get: (d) => (d.enabled ? 'Enabled' : 'Disabled'), cell: (d) => on(d.enabled) },
            { key: 'managed', label: 'Managed', w: 100, get: (d) => (d.managed ? 'Yes' : 'No') },
            { key: 'registered', label: 'Registered', w: 120, get: (d) => time(d.registered), text: (d) => d.registered || '', cell: (d) => esc(day(d.registered)) },
            { key: 'id', label: 'Object ID', w: 290, get: (d) => d.id, cell: (d) => mono(d.id) },
          ],
          open: (d) => `${ENTRA}/Microsoft_AAD_Devices/DeviceDetailsMenuBlade/~/Properties/objectId/${d.id}`,
        },
        roles: {
          name: 'Admin roles', one: 'Admin role', icon: 'crown', path: '/api/cloud/entra/roles', lead: 'The Microsoft Entra roles in use and who holds each one.', noun: 'roles',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All roles', () => true], ['global', 'Global Administrator', (r) => r.name === 'Global Administrator', 'amber']],
          cols: [
            { key: 'name', label: 'Role', w: 240, fixed: true, get: (r) => r.name || '', cell: (r) => who('crown', r.name) },
            { key: 'count', label: 'Members', w: 100, on: true, get: (r) => (r.members || []).length },
            { key: 'members', label: 'Assigned to', w: 320, on: true, get: (r) => (r.members || []).map((m) => m.name).join(', ') },
            { key: 'description', label: 'Description', w: 360, get: (r) => r.description || '' },
            { key: 'id', label: 'Object ID', w: 290, get: (r) => r.id, cell: (r) => mono(r.id) },
          ],
          tabs: (r) => [['members', 'Members', (r.members || []).length, () => ((r.members || []).length
            ? facts(r.members.map((m) => [m.name || m.id, esc(m.upn || (m.kind === 'servicePrincipal' ? 'App' : m.kind || ''))])) : none('No one holds this role.'))]],
          open: () => `${ENTRA}/Microsoft_AAD_IAM/RolesManagementMenuBlade/~/AllRoles`,
        },
      },
    },
    exchange: {
      name: 'Exchange', icon: 'mail',
      note: () => (cloud && !cloud.exchange ? 'Rabbid Lab’s app does not have the Exchange Administrator role, so Exchange Online refuses it. Give the role to the app in the Microsoft Entra admin center, under Roles and administrators, or disconnect and connect again.' : ''),
      lists: {
        mailboxes: {
          name: 'Mailboxes', one: 'Mailbox', icon: 'mail', path: '/api/cloud/exchange/mailboxes', lead: 'User, shared, room and equipment mailboxes.', noun: 'mailboxes',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All mailboxes', () => true], ['user', 'User', (m) => m.kind === 'User'], ['shared', 'Shared', (m) => m.kind === 'Shared'], ['room', 'Room', (m) => m.kind === 'Room'],
            ['equipment', 'Equipment', (m) => m.kind === 'Equipment'], ['forwarding', 'Forwarding', (m) => !!m.forwarding, 'amber'], ['hidden', 'Hidden from address lists', (m) => m.hidden]],
          cols: [
            { key: 'name', label: 'Display name', w: 220, fixed: true, get: (m) => m.name || '', cell: (m) => who('mail', m.name) },
            { key: 'address', label: 'Email address', w: 250, on: true, get: (m) => m.address || '' },
            { key: 'kind', label: 'Mailbox type', w: 120, on: true, get: (m) => m.kind || '' },
            { key: 'forwarding', label: 'Forwarding', w: 200, on: true, get: (m) => m.forwarding || '', cell: (m) => (m.forwarding ? badge(m.forwarding.replace(/^smtp:/i, ''), 'amber') : muted('None')) },
            { key: 'hidden', label: 'Address lists', w: 120, on: true, get: (m) => (m.hidden ? 'Hidden' : 'Shown'), cell: (m) => (m.hidden ? badge('Hidden', 'grey') : muted('Shown')) },
            { key: 'archive', label: 'Archive', w: 100, get: (m) => (m.archive === 'Active' ? 'Active' : 'None') },
            { key: 'litigation', label: 'Litigation hold', w: 120, get: (m) => (m.litigation ? 'On' : 'Off') },
            { key: 'alias', label: 'Alias', w: 140, get: (m) => m.alias || '' },
            { key: 'created', label: 'Created', w: 120, get: (m) => time(m.created), text: (m) => m.created || '', cell: (m) => esc(day(m.created)) },
          ],
          tabs: (m) => [['addresses', 'Email addresses', (m.addresses || []).length, () => ((m.addresses || []).length
            ? facts(m.addresses.map((a) => [a, a.toLowerCase() === String(m.address).toLowerCase() ? badge('Primary', 'green') : muted('Alias')])) : none('No other addresses.'))]],
          open: () => `${EXO}/mailboxes`,
        },
        groups: {
          name: 'Distribution lists', one: 'Distribution list', icon: 'users', path: '/api/cloud/exchange/groups', lead: 'Distribution lists and mail-enabled security groups.', noun: 'lists',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All', () => true], ['dl', 'Distribution lists', (g) => g.kind === 'Distribution list'], ['security', 'Mail-enabled security', (g) => g.kind !== 'Distribution list'],
            ['outside', 'Open to outside senders', (g) => g.outside, 'amber']],
          cols: [
            { key: 'name', label: 'Display name', w: 230, fixed: true, get: (g) => g.name || '', cell: (g) => who('users', g.name) },
            { key: 'address', label: 'Email address', w: 250, on: true, get: (g) => g.address || '' },
            { key: 'kind', label: 'Group type', w: 170, on: true, get: (g) => g.kind },
            { key: 'outside', label: 'Outside senders', w: 140, on: true, get: (g) => (g.outside ? 'Allowed' : 'Blocked'), cell: (g) => (g.outside ? badge('Allowed', 'amber') : muted('Blocked')) },
            { key: 'owners', label: 'Owners', w: 220, on: true, get: (g) => (g.owners || []).join(', ') },
          ],
          open: () => `${EXO}/groups`,
        },
        contacts: {
          name: 'Contacts', one: 'Contact', icon: 'user', path: '/api/cloud/exchange/contacts', lead: 'People outside the organisation in the address book.', noun: 'contacts',
          sort: { key: 'name', dir: 1 },
          cols: [
            { key: 'name', label: 'Display name', w: 230, fixed: true, get: (c) => c.name || '', cell: (c) => who('user', c.name) },
            { key: 'address', label: 'Email address', w: 260, on: true, get: (c) => c.address || '' },
            { key: 'hidden', label: 'Address lists', w: 120, on: true, get: (c) => (c.hidden ? 'Hidden' : 'Shown') },
          ],
          open: () => `${EXO}/contacts`,
        },
        rules: {
          name: 'Mail flow rules', one: 'Mail flow rule', icon: 'signpost', path: '/api/cloud/exchange/rules', lead: 'Transport rules applied to mail as it passes through Exchange Online, in priority order.', noun: 'rules',
          sort: { key: 'priority', dir: 1 },
          views: [['all', 'All rules', () => true], ['enabled', 'Enabled', (r) => r.state === 'Enabled'], ['disabled', 'Disabled', (r) => r.state !== 'Enabled', 'grey']],
          cols: [
            { key: 'name', label: 'Rule', w: 260, fixed: true, get: (r) => r.name || '', cell: (r) => who('signpost', r.name) },
            { key: 'state', label: 'Status', w: 110, on: true, get: (r) => r.state || '', cell: (r) => on(r.state === 'Enabled') },
            { key: 'priority', label: 'Priority', w: 90, on: true, get: (r) => r.priority ?? 0 },
            { key: 'mode', label: 'Mode', w: 150, on: true, get: (r) => ({ Enforce: 'Enforce', Audit: 'Test', AuditAndNotify: 'Test with Policy Tips' }[r.mode] || r.mode || '') },
            { key: 'description', label: 'What it does', w: 420, on: true, get: (r) => (r.description || '').replace(/\s+/g, ' ').trim() },
          ],
          open: () => `${EXO}/transportrules`,
        },
        quarantine: {
          name: 'Quarantine', one: 'Quarantined message', icon: 'lock', path: '/api/cloud/exchange/quarantine', lead: 'Messages held as spam, phishing or malware. The newest 500 are shown.', noun: 'messages',
          sort: { key: 'received', dir: -1 },
          views: [['all', 'All messages', () => true], ['held', 'Not released', (q) => !q.released, 'amber'], ['released', 'Released', (q) => q.released]],
          cols: [
            { key: 'subject', label: 'Subject', w: 280, fixed: true, get: (q) => q.subject || '', cell: (q) => who('lock', q.subject || '(No subject)') },
            { key: 'sender', label: 'Sender', w: 220, on: true, get: (q) => q.sender || '' },
            { key: 'to', label: 'Recipient', w: 220, on: true, get: (q) => (q.to || []).join(', ') },
            { key: 'why', label: 'Reason', w: 120, on: true, get: (q) => q.why || '' },
            { key: 'received', label: 'Received', w: 160, on: true, get: (q) => time(q.received), text: (q) => q.received || '', cell: (q) => esc(stamp(q.received)) },
            { key: 'released', label: 'Released', w: 100, on: true, get: (q) => (q.released ? 'Yes' : 'No'), cell: (q) => (q.released ? badge('Yes', 'green') : muted('No')) },
            { key: 'expires', label: 'Deleted on', w: 130, get: (q) => time(q.expires), text: (q) => q.expires || '', cell: (q) => esc(day(q.expires)) },
          ],
          key: (q) => q.id,
          open: () => 'https://security.microsoft.com/quarantine',
        },
      },
    },
    m365: {
      name: 'Microsoft 365', icon: 'license',
      lists: {
        licences: {
          name: 'Licences', one: 'Licence', icon: 'license', path: '/api/cloud/m365/licenses', lead: 'The subscriptions the tenant has, and how many of each are assigned.', noun: 'licences',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All licences', () => true], ['low', 'Running out', (s) => s.total > 0 && s.used >= s.total * 0.9, 'amber'], ['unused', 'None assigned', (s) => !s.used]],
          cols: [
            { key: 'name', label: 'Licence', w: 280, fixed: true, get: (s) => s.name || s.part || '', cell: (s) => who('license', s.name || s.part) },
            { key: 'used', label: 'Assigned', w: 100, on: true, get: (s) => s.used },
            { key: 'total', label: 'Total', w: 90, on: true, get: (s) => s.total },
            { key: 'free', label: 'Available', w: 110, on: true, get: (s) => s.total - s.used,
              cell: (s) => (s.total - s.used <= 0 ? badge('None', 'amber') : esc((s.total - s.used).toLocaleString())) },
            { key: 'state', label: 'Status', w: 120, on: true, get: (s) => s.state || '', cell: (s) => (s.state === 'Enabled' ? badge('Active', 'green') : badge(s.state || 'Unknown', 'amber')) },
            { key: 'part', label: 'Product ID', w: 230, get: (s) => s.part || '', cell: (s) => mono(s.part) },
          ],
          open: () => 'https://admin.microsoft.com/#/licenses',
        },
        users: {
          name: 'Users and licences', one: 'User', icon: 'user', path: '/api/cloud/entra/users', lead: 'Every user and how many licences each has.', noun: 'users',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All users', () => true], ['licensed', 'Licensed', (u) => u.licenses > 0], ['unlicensed', 'No licence', (u) => !u.licenses && !u.guest, 'amber'],
            ['disabled', 'Disabled with a licence', (u) => !u.enabled && u.licenses > 0, 'amber']],
          cols: users([licences]),
          open: (u) => `https://admin.microsoft.com/#/users/:/UserDetails/${u.id}/LicensesAndApps`,
        },
      },
    },
    azure: {
      name: 'Azure', icon: 'cloud',
      note: () => (cloud && !cloud.subscriptions ? 'Rabbid Lab’s app has Reader on no Azure subscription, so these lists are empty. Give the app Reader on a subscription in the Azure portal, under Access control (IAM).' : ''),
      lists: {
        machines: {
          name: 'Virtual machines', one: 'Virtual machine', icon: 'server', path: '/api/cloud/azure/machines', lead: 'Virtual machines in every subscription Rabbid Lab can read, and whether each is running.', noun: 'machines',
          sort: { key: 'name', dir: 1 },
          views: [['all', 'All machines', () => true], ['running', 'Running', (v) => v.power === 'Running'], ['stopped', 'Stopped', (v) => v.power === 'Stopped', 'grey'],
            ['billed', 'Stopped, still billed', (v) => v.power === 'Stopped, still billed', 'amber']],
          cols: [
            { key: 'name', label: 'Name', w: 200, fixed: true, get: (v) => v.name || '', cell: (v) => who('server', v.name) },
            { key: 'power', label: 'Status', w: 170, on: true, get: (v) => v.power || '',
              cell: (v) => badge(v.power || 'Unknown', v.power === 'Running' ? 'green' : v.power === 'Stopped, still billed' ? 'amber' : 'grey') },
            { key: 'size', label: 'Size', w: 150, on: true, get: (v) => v.size || '' },
            { key: 'os', label: 'Operating system', w: 130, on: true, get: (v) => v.os || '' },
            { key: 'group', label: 'Resource group', w: 170, on: true, get: (v) => v.group || '' },
            { key: 'location', label: 'Location', w: 120, on: true, get: (v) => v.location || '' },
            { key: 'subscription', label: 'Subscription', w: 200, on: true, get: (v) => v.subscription || '' },
          ],
          open: (v) => `https://portal.azure.com/#@${cloud?.tenantId || ''}/resource${v.id}/overview`,
        },
        subscriptions: {
          name: 'Subscriptions', one: 'Subscription', icon: 'key', path: '/api/cloud/azure/subscriptions', lead: 'The Azure subscriptions Rabbid Lab has Reader on.', noun: 'subscriptions',
          sort: { key: 'name', dir: 1 },
          cols: [
            { key: 'name', label: 'Subscription', w: 260, fixed: true, get: (s) => s.name || '', cell: (s) => who('key', s.name) },
            { key: 'state', label: 'Status', w: 110, on: true, get: (s) => s.state || '', cell: (s) => (s.state === 'Enabled' ? badge('Enabled', 'green') : badge(s.state || 'Unknown', 'amber')) },
            { key: 'id', label: 'Subscription ID', w: 290, on: true, get: (s) => s.id, cell: (s) => mono(s.id) },
          ],
          open: (s) => `https://portal.azure.com/#@${cloud?.tenantId || ''}/resource/subscriptions/${s.id}/overview`,
        },
      },
    },
  };
  const keyOf = (l, x) => (l.key ? l.key(x) : x.id) || '';

  // --- What's known --------------------------------------------------------------------------------------------------
  let cloud = null; // /api/cloud: connected, tenant, domain, connecting, ...
  let cloudProblem = '';
  const data = {}; // list path -> { rows, at } | { error }
  const reading = {}; // list path -> the read under way
  const st = Object.fromEntries(Object.keys(SERVICES).map((k) => [k, { list: Object.keys(SERVICES[k].lists)[0], view: 'all', picked: new Set() }]));
  const showing = () => (SERVICES[R.where()[0]] ? R.where()[0] : '');
  const canSee = () => has('cloud.view');

  const status = async () => {
    try {
      cloud = await get('/api/cloud');
      cloudProblem = '';
    } catch (e) {
      cloudProblem = e.message;
    }
  };
  const read = (path) => (reading[path] ||= get(path).then((rows) => { data[path] = { rows, at: Date.now() }; }, (e) => { data[path] = { error: e.message }; })
    .finally(() => {
      delete reading[path];
      drawOpen();
    }));
  // A page opened: what it lists, each read once until Refresh or the next live read.
  const load = (k) => {
    if (!cloud?.connected || !canSee()) return;
    Object.values(SERVICES[k].lists).forEach((l) => { if (!data[l.path] && !reading[l.path]) read(l.path); });
  };
  const forget = () => Object.keys(data).forEach((p) => delete data[p]);

  // --- The pages, built once from SERVICES ---------------------------------------------------------------------------
  const SKEL = '<div class="dnav-sk" aria-hidden="true"><i class="sk sk-head"></i>' + '<span><i class="sk sq"></i><i class="sk"></i></span>'.repeat(4) + '</div>';
  const shell = (k, s) => `<header class="hero" id="cl-${k}-hero"><h1 class="hero-title">${esc(s.name)}</h1>`
    + `<p class="hero-lead"><b id="cl-${k}-state"></b> <span id="cl-${k}-lead"></span></p><div class="hero-row"><div class="hero-stats" id="cl-${k}-stats"></div></div></header>`
    + `<div class="dir-area"><div class="dgate" id="cl-${k}-gate" hidden></div>`
    + `<nav class="dnav lists" id="cl-${k}-rail" aria-label="${esc(s.name)}">${SKEL}<p class="rail-label">Browse</p>`
    + Object.entries(s.lists).map(([l, x]) => `<a href="#${k}/${l}" data-cl-list="${l}">${svg(x.icon)}<span>${esc(x.name)}</span><small data-cl-n="${l}"></small></a><div class="dkids" data-cl-views="${l}"></div>`).join('')
    + `<div class="dir-acct" data-cl-acct hidden>${svg('cloud')}<span data-cl-tenant></span><button type="button" class="icon" data-cl-conn data-tip="Connection" aria-label="Connection">${svg('gear')}</button></div></nav>`
    + `<div class="dmain"><div class="lib" id="cl-${k}-lib"><div class="lib-body">`
    + `<div class="dbar"><div class="tabs2" id="cl-${k}-tabs"></div><span class="grow"></span><div class="cmd-side"><span class="cmd-l">View</span><span id="cl-${k}-rows"></span>`
    + `<div class="gdock"><button type="button" data-cl-refresh data-tip="Refresh" aria-label="Refresh">${svg('refresh')}</button>`
    + `<button type="button" class="gexp" data-cl-export data-tip="Export" aria-label="Export">${svg('download')}</button></div></div></div>`
    + `<div id="cl-${k}-fbar"></div><p class="hint note" id="cl-${k}-note" hidden></p>`
    + `<div class="gcard"><div class="gbar"><button type="button" class="small" id="cl-${k}-act" aria-haspopup="menu" disabled>Actions<b class="cnt" hidden></b>${svg('chevron-down', 'chev')}</button>`
    + `<div id="cl-${k}-tools"></div></div><div class="table adt drows" id="cl-${k}-table"></div></div>`
    + `<p class="hint problem" id="cl-${k}-problem" hidden></p></div></div></div></div>`;

  const L = (k) => SERVICES[k].lists[st[k].list];
  const all = (l) => data[l.path]?.rows || [];
  const viewOf = (k) => (L(k).views || []).find((v) => v[0] === st[k].view) || null;
  const rowsOf = (k) => all(L(k)).filter(viewOf(k)?.[2] || (() => true));
  Object.entries(SERVICES).forEach(([k, s]) => {
    const page = $(`page-${k}`);
    if (!page) return;
    page.innerHTML = shell(k, s);
    s.grid = R.grid({
      el: $(`cl-${k}-table`), store: `rabbidlab.cloud.${k}`, key: () => st[k].list, cols: () => L(k).cols, sort: () => L(k).sort,
      lead: () => ({ w: 18, head: '<label class="tk"><input type="checkbox" data-cl-tick-all aria-label="Select all"></label>',
        cell: (x) => `<label class="tk"><input type="checkbox" data-cl-tick="${esc(keyOf(L(k), x))}" aria-label="Select"></label>` }),
      row: (x) => ` data-cl-row="${esc(keyOf(L(k), x))}"`,
      empty: () => (data[L(k).path]?.error ? `Could not read ${L(k).name.toLowerCase()}.` : `No ${L(k).noun}.`),
      loading: () => !!cloud?.connected && !data[L(k).path], redraw: () => draw(k),
    });
    s.bar = R.filter.bar({ el: $(`cl-${k}-fbar`), store: `rabbidlab.cloud.${k}`, key: () => st[k].list, grid: () => s.grid, noun: () => L(k).noun, rows: () => rowsOf(k), redraw: () => draw(k) });
    wire(k, page);
  });

  const shown = (k) => rowsOf(k).filter(SERVICES[k].bar.test());
  const draw = (k) => {
    const s = SERVICES[k];
    const t = st[k];
    const l = L(k);
    if (!s.grid) return;
    drawGate(k);
    const up = !!cloud?.connected;
    $(`cl-${k}-state`).textContent = `${l.name}.`;
    $(`cl-${k}-lead`).textContent = l.lead;
    // A count for each list: placeholders while it's read, a dash when it couldn't be.
    const count = (x) => (data[x.path]?.rows ? data[x.path].rows.length.toLocaleString() : data[x.path]?.error || !up ? '—' : null);
    $(`cl-${k}-stats`).innerHTML = Object.values(s.lists).map((x) => stat(count(x), x.name)).join('');
    const page = $(`page-${k}`);
    page.querySelectorAll('a[data-cl-list]').forEach((a) => (a.dataset.clList === t.list ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    page.querySelectorAll('[data-cl-n]').forEach((n) => { n.textContent = data[s.lists[n.dataset.clN].path]?.rows?.length.toLocaleString() || ''; });
    page.querySelectorAll('[data-cl-views]').forEach((v) => {
      const x = s.lists[v.dataset.clViews];
      v.innerHTML = v.dataset.clViews !== t.list || !x.views ? '' : x.views.map((w) => {
        const n = all(x).filter(w[2]).length;
        return `<a href="#${k}/${t.list}/${w[0]}"${w[0] === t.view ? ' aria-current="page"' : ''}><span class="nm">${esc(w[1])}</span><small class="${n && w[3] ? `tone ${w[3]}` : ''}">${data[x.path]?.rows ? n.toLocaleString() : ''}</small></a>`;
      }).join('');
    });
    const acct = page.querySelector('[data-cl-acct]');
    acct.hidden = !up;
    acct.querySelector('[data-cl-tenant]').textContent = cloud?.domain || cloud?.tenant || '';
    const rows = shown(k);
    $(`cl-${k}-tabs`).innerHTML = `<a aria-current="true">${svg(l.icon)}${esc(viewOf(k) && t.view !== 'all' ? viewOf(k)[1] : l.name)} <small>${data[l.path]?.rows ? rows.length.toLocaleString() : ''}</small></a>`;
    s.bar.draw();
    s.grid.tools($(`cl-${k}-tools`), $(`cl-${k}-rows`));
    const problem = data[l.path]?.error || '';
    $(`cl-${k}-problem`).hidden = !problem;
    $(`cl-${k}-problem`).textContent = problem;
    const note = s.note?.() || '';
    $(`cl-${k}-note`).hidden = !note || !up;
    $(`cl-${k}-note`).textContent = note;
    if (!up) R.skeleton($(`cl-${k}-table`), 'rows', 6);
    else if (data[l.path]) s.grid.draw(rows);
    else R.skeleton($(`cl-${k}-table`), 'rows', 6);
    drawTicks(k);
    page.querySelectorAll('[data-cl-row]').forEach((el) => el.classList.toggle('dw-on', panel.shows('cloud', `${k}:${t.list}:${el.dataset.clRow}`)));
  };
  const drawOpen = () => {
    const k = showing();
    if (k) draw(k);
    if (panel.is('cloud')) drawPanel();
  };

  // --- Not connected: the cover, with Connect ------------------------------------------------------------------------
  const gateCard = (title, text, body, acts) => `<div class="dgate-card cl-gate"><span class="big-ic cl-ms">${svg('ms')}</span><div class="ustatus-text"><b>${esc(title)}</b>`
    + `<span>${esc(text)}</span></div>${body}<div class="dgate-acts">${acts}</div></div>`;
  const drawGate = (k) => {
    const gate = $(`cl-${k}-gate`);
    const c = cloud?.connecting;
    let html = '';
    if (cloudProblem) html = gateCard('Could not read the Microsoft cloud connection', cloudProblem, '', '<button type="button" class="primary" data-cl-retry>Try again</button>');
    else if (!cloud || cloud.connected) html = '';
    else if (c && !c.finished) {
      const steps = `<ol class="cl-steps">${c.done.map((d) => `<li class="done">${svg('check')}<span>${esc(d)}</span></li>`).join('')}<li class="now"><i class="cl-spin"></i><span>${esc(c.step)}</span></li></ol>`;
      // Before the sign-in: the code and where to enter it. After it: only the steps, as the server does them.
      const until = new Date(c.expires).toLocaleTimeString([], { timeStyle: 'short' });
      html = !c.done.length ? gateCard('Sign in at Microsoft', 'Enter this code at Microsoft and sign in with a Global Administrator account. Rabbid Lab does the rest.',
        `<div class="cl-code"><b>${esc(c.code)}</b><button type="button" class="icon" data-cl-copy="${esc(c.code)}" data-tip="Copy code" aria-label="Copy code">${svg('copy')}</button></div>`
        + `<p class="cl-how">The code works until ${esc(until)}. Your sign-in is used once, to set up Rabbid Lab’s own app registration, and is never kept.</p>${steps}`,
        `<button type="button" class="primary" data-cl-signin="${esc(c.url)}">Open Microsoft sign-in</button>`)
        : gateCard('Setting up Microsoft cloud', 'You are signed in. Rabbid Lab is setting up its app registration in your tenant, which takes about a minute.', steps, '');
    } else if (c?.problem) {
      html = gateCard('Microsoft cloud did not connect', c.problem,
        c.done.length ? `<ol class="cl-steps">${c.done.map((d) => `<li class="done">${svg('check')}<span>${esc(d)}</span></li>`).join('')}</ol>` : '',
        has('cloud.connect') ? '<button type="button" class="primary" data-cl-connect>Try again</button>' : '');
    } else if (has('cloud.connect')) {
      html = gateCard('Connect Microsoft cloud', 'Sign in once with a Global Administrator account. Rabbid Lab makes its own app registration in your tenant with a certificate and signs in as that app from then on. Entra ID, Exchange, Microsoft 365 and Azure all use the one connection.',
        `<ul class="cl-gets">${['Read users, groups, devices and admin roles', 'Read mailboxes, distribution lists, mail flow rules and quarantine', 'Read licences and who has them', 'Reader on your Azure subscriptions']
          .map((x) => `<li>${svg('check')}<span>${esc(x)}</span></li>`).join('')}</ul>`,
        '<button type="button" class="primary" data-cl-connect>Connect</button>');
    } else {
      html = gateCard('Microsoft cloud is not connected', 'Someone with the Connect Microsoft cloud permission connects it once, for everyone.', '', '');
    }
    gate.hidden = !html;
    if (gate.dataset.drawn !== html) {
      gate.innerHTML = html;
      gate.dataset.drawn = html;
    }
  };
  // While someone is signing in at Microsoft: the connection is read every two seconds until it finishes.
  let polling = 0;
  const poll = () => {
    clearTimeout(polling);
    if (!cloud?.connecting || cloud.connecting.finished) return;
    polling = setTimeout(async () => {
      const was = !!cloud?.connected;
      await status();
      if (cloud?.connected && !was) {
        forget();
        toast('Microsoft cloud is connected.', cloud.tenant || '');
        if (cloud.notes?.length) toast('Some of it needs a look.', cloud.notes.join(' '), { tone: 'warn', stay: true });
      }
      const k = showing();
      if (k) load(k);
      drawOpen();
      poll();
    }, 2000);
  };
  const connect = async (button) => {
    button.disabled = true;
    try {
      await send('/api/cloud/connect');
      await status();
      poll();
    } catch (e) {
      if (!e.cancelled) toast('Could not start connecting.', e.message, { tone: 'warn' });
    }
    button.disabled = false;
    drawOpen();
  };
  const copy = async (b) => {
    let ok = false;
    try {
      await navigator.clipboard.writeText(b.dataset.clCopy);
      ok = true;
    } catch {
      const t = document.createElement('textarea');
      t.value = b.dataset.clCopy;
      document.body.append(t);
      t.select();
      ok = document.execCommand('copy');
      t.remove();
    }
    b.innerHTML = svg(ok ? 'check' : 'copy');
    if (!ok) toast('Could not copy to the clipboard.', 'Select the code and press Ctrl+C.', { tone: 'warn' });
    setTimeout(() => { b.innerHTML = svg('copy'); }, 1500);
  };

  // --- Ticks, and what Actions does with them ------------------------------------------------------------------------
  const ticked = (k) => all(L(k)).filter((x) => st[k].picked.has(keyOf(L(k), x)));
  const drawTicks = (k) => {
    const table = $(`cl-${k}-table`);
    R.grid.ticks(table, 'clTick', st[k].picked);
    const n = ticked(k).length;
    const act = $(`cl-${k}-act`);
    act.hidden = !cloud?.connected;
    act.disabled = !n;
    act.querySelector('.cnt').hidden = !n;
    act.querySelector('.cnt').textContent = n.toLocaleString();
    act.dataset.tip = n ? `${n.toLocaleString()} selected` : `Select ${L(k).noun} first`;
  };

  function wire(k, page) {
    page.addEventListener('click', (e) => {
      if (e.target.closest('[data-cl-connect]')) connect(e.target.closest('[data-cl-connect]'));
      else if (e.target.closest('[data-cl-copy]')) copy(e.target.closest('[data-cl-copy]'));
      else if (e.target.closest('[data-cl-signin]')) window.open(e.target.closest('[data-cl-signin]').dataset.clSignin, '_blank', 'noopener');
      else if (e.target.closest('[data-cl-retry]')) status().then(() => { load(k); draw(k); });
      else if (e.target.closest('[data-cl-conn]')) openConnection();
      else if (e.target.closest('[data-cl-export]')) SERVICES[k].grid.csv(SERVICES[k].grid.sorted(shown(k)), `${k}-${st[k].list}`);
      else if (e.target.closest('[data-cl-refresh]')) {
        R.refresh(e.target.closest('[data-cl-refresh]'), $(`cl-${k}-lib`), `Refreshing ${SERVICES[k].name}…`, async () => {
          await status();
          Object.values(SERVICES[k].lists).forEach((l) => delete data[l.path]);
          load(k);
          await Promise.all(Object.values(reading));
        });
      }
    });
    $(`cl-${k}-table`).addEventListener('click', (e) => {
      const t = st[k];
      const ticked = R.grid.tick(e, $(`cl-${k}-table`), 'clTick', t.picked);
      if (ticked !== null) {
        if (ticked) drawTicks(k);
        return;
      }
      const row = e.target.closest('[data-cl-row]');
      if (row && !e.target.closest('.th')) openRow(k, t.list, row.dataset.clRow);
    });
    $(`cl-${k}-act`).addEventListener('click', (e) => {
      const rows = ticked(k);
      R.actions.open(e.currentTarget, {
        items: [{ act: 'export', label: 'Export selected', hint: String(rows.length) }, { act: 'clear', label: 'Clear selection' }],
        pick: (act) => {
          if (act === 'export') SERVICES[k].grid.csv(SERVICES[k].grid.sorted(rows), `${k}-${st[k].list}-selected`);
          else {
            st[k].picked.clear();
            drawTicks(k);
          }
        },
      });
    });
  }

  // --- A row's side panel, and the connection's ----------------------------------------------------------------------
  let open = null; // { k, list, id } or { connection: true }
  let tab = 'details';
  const tabsOf = (list) => `<div class="tabs2 dw-tabs" role="tablist">${list.map(([key, label, n = '']) => `<a role="tab" data-cl-tab="${key}"${tab === key ? ' aria-current="true"' : ''}>${esc(label)}${n === '' || n === 0 ? '' : ` <small>${n}</small>`}</a>`).join('')}</div>`;
  const value = (c, x) => (c.cell ? c.cell(x) : esc(String(c.get(x) ?? '')));
  const openRow = (k, list, id) => {
    const pk = `${k}:${list}:${id}`;
    if (panel.shows('cloud', pk)) {
      panel.close();
      return;
    }
    open = { k, list, id };
    tab = 'details';
    if (!drawPanel()) return;
    panel.open('cloud', pk);
    draw(k);
  };
  const openConnection = () => {
    if (panel.shows('cloud', 'connection')) {
      panel.close();
      return;
    }
    open = { connection: true };
    tab = 'details';
    if (!drawPanel()) return;
    panel.open('cloud', 'connection');
  };
  const drawPanel = () => {
    if (open?.connection) return drawConnection();
    if (!open) return false;
    const s = SERVICES[open.k];
    const l = s.lists[open.list];
    const x = all(l).find((y) => keyOf(l, y) === open.id);
    if (!x) {
      if (panel.is('cloud')) panel.close();
      return false;
    }
    const name = l.cols[0].get(x) || '(No name)';
    const more = l.tabs?.(x) || [];
    const strip = `<div class="dw-strip">${l.cols.slice(1).filter((c) => c.on).slice(0, 4).map((c) => `<div><span class="l">${esc(c.label)}</span><span class="v">${value(c, x)}</span></div>`).join('')}</div>`;
    const extra = more.find((m) => m[0] === tab);
    const pane = extra ? section(esc(extra[1]), extra[3]())
      : section('Details', facts(l.cols.map((c, i) => [c.label, (i ? value(c, x) : esc(name)) || muted('None')])));
    panel.body.innerHTML = head(l.icon, name, `${l.one} · ${s.name}`)
      + `<div class="dw-bar"><button type="button" class="small dw-acts" data-cl-menu aria-haspopup="menu">${svg('menu')}Actions${svg('chevron-down', 'chev')}</button></div>`
      + strip + tabsOf([['details', 'Details'], ...more.map((m) => [m[0], m[1], m[2]])]) + pane
      + `<p class="dw-how">Read from Microsoft ${esc(since(new Date(data[l.path]?.at || Date.now()).toISOString()).toLowerCase())}. Changes come in a later version; until then, make them in Microsoft’s admin center.</p>`;
    return true;
  };
  const drawConnection = () => {
    const c = cloud;
    if (!c?.connected) {
      if (panel.is('cloud')) panel.close();
      return false;
    }
    const strip = `<div class="dw-strip">${[['Tenant', esc(c.tenant || '')], ['Exchange', c.exchange ? badge('Connected', 'green') : badge('No role', 'amber')],
      ['Azure', c.subscriptions ? esc(plural(c.subscriptions, 'subscription')) : badge('None', 'amber')], ['Connected', esc(since(c.since))]]
      .map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${v}</span></div>`).join('')}</div>`;
    panel.body.innerHTML = head('cloud', c.tenant || 'Microsoft cloud', c.domain || '') + strip
      + section('Connection', facts([['Tenant', esc(c.tenant || '')], ['Default domain', esc(c.domain || '')], ['Tenant ID', mono(c.tenantId)], ['App registration', mono(c.app)],
        ['Connected', esc(`${stamp(c.since)} by ${c.by || 'unknown'}`)], ['Exchange Administrator role', c.exchange ? badge('Given', 'green') : badge('Not given', 'amber')],
        ['Azure Reader', c.subscriptions ? esc(plural(c.subscriptions, 'subscription')) : muted('None')]]))
      + (c.notes?.length ? section('Needs a look', `<ul class="cl-notes">${c.notes.map((n) => `<li>${svg('alert')}<span>${esc(n)}</span></li>`).join('')}</ul>`) : '')
      + section(`Microsoft Graph permissions <small>${c.permissions.length}</small>`, `<p class="dw-how">What Rabbid Lab’s app registration is allowed in the tenant. What each person can do here is set by their role, under Settings › Roles and people.</p>`
        + `<ul class="cl-perms">${c.permissions.map((p) => `<li class="mono">${esc(p)}</li>`).join('')}</ul>`)
      + (has('cloud.connect') ? section('Disconnect', `<p class="dw-how">Deletes Rabbid Lab’s app registration in ${esc(c.tenant || 'the tenant')}, which removes its permissions and roles there. The Microsoft cloud pages stay empty until someone connects again.</p>`
        + '<button type="button" class="danger" data-cl-disconnect>Disconnect…</button>') : '');
    return true;
  };
  panel.el.addEventListener('close', () => {
    const k = showing();
    if (k) draw(k);
  });
  panel.el.addEventListener('click', (e) => {
    if (!panel.is('cloud')) return;
    const t = e.target.closest('[data-cl-tab]');
    if (t) {
      tab = t.dataset.clTab;
      drawPanel();
      return;
    }
    const m = e.target.closest('[data-cl-menu]');
    if (m && open && !open.connection) {
      const l = SERVICES[open.k].lists[open.list];
      const x = all(l).find((y) => keyOf(l, y) === open.id);
      if (!x) return;
      const where = open.k === 'azure' ? 'the Azure portal' : open.k === 'exchange' ? (open.list === 'quarantine' ? 'Microsoft Defender' : 'the Exchange admin center') : open.k === 'm365' ? 'the Microsoft 365 admin center' : 'the Microsoft Entra admin center';
      R.actions.open(m, {
        items: [{ act: 'open', label: `Open in ${where}` }, ...(x.id ? [{ act: 'copy', label: 'Copy ID' }] : [])],
        pick: (act) => {
          if (act === 'open') window.open(l.open(x), '_blank', 'noopener');
          else navigator.clipboard?.writeText(x.id).then(() => toast('Copied.', x.id), () => toast('Could not copy to the clipboard.', x.id, { tone: 'warn' }));
        },
      });
      return;
    }
    if (e.target.closest('[data-cl-disconnect]')) {
      sure('Disconnect Microsoft cloud?', `Rabbid Lab deletes its app registration in ${cloud?.tenant || 'the tenant'}. Entra ID, Exchange, Microsoft 365 and Azure stay empty until someone connects again.`, 'Disconnect', async () => {
        try {
          const r = await send('/api/cloud/disconnect');
          panel.close();
          forget();
          await status();
          drawOpen();
          if (r.problem) toast('Microsoft cloud is disconnected, with a problem.', r.problem, { tone: 'warn', stay: true });
          else toast('Microsoft cloud is disconnected.', 'Its app registration was deleted.');
        } catch (err) {
          if (!err.cancelled) toast('Could not disconnect.', err.message, { tone: 'warn' });
        }
      });
    }
  });

  // --- Going to a page -----------------------------------------------------------------------------------------------
  document.addEventListener('rabbidlab:show', async (e) => {
    const k = e.detail[0];
    if (!SERVICES[k] || !$(`page-${k}`)) return;
    const [l, v] = e.detail[2] || [];
    const t = st[k];
    const next = SERVICES[k].lists[l] ? l : Object.keys(SERVICES[k].lists)[0];
    if (next !== t.list) {
      if (panel.is('cloud')) panel.close();
      t.picked.clear();
    }
    t.list = next;
    t.view = (L(k).views || []).some((w) => w[0] === v) ? v : 'all';
    draw(k);
    if (!cloud && !cloudProblem) await status();
    load(k);
    draw(k);
    poll();
  });
  // While a page is open: its lists read again every two minutes, drawn only when something changed.
  R.live({
    page: () => !!showing() && !!cloud?.connected && canSee(),
    hold: () => Object.keys(reading).length > 0,
    every: 120000,
    // Each list on its own, so one Microsoft refuses (Exchange without its role) still shows why and the others still update.
    read: async () => {
      const k = showing();
      const got = {};
      await Promise.all(Object.values(SERVICES[k].lists).map(async (l) => { got[l.path] = await get(l.path).then((rows) => ({ rows }), (e) => ({ error: e.message })); }));
      return { k, got };
    },
    now: () => {
      const k = showing();
      const was = (d) => (d?.rows ? { rows: d.rows } : d ? { error: d.error } : null);
      return { k, got: Object.fromEntries(Object.values(SERVICES[k]?.lists || {}).map((l) => [l.path, was(data[l.path])])) };
    },
    draw: ({ got }) => {
      Object.entries(got).forEach(([p, d]) => { data[p] = { ...d, at: Date.now() }; });
      drawOpen();
    },
    areas: () => [showing() ? $(`cl-${showing()}-table`) : null],
  });
})();
