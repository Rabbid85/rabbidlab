// Rabbid Lab's page: this machine (/api/status), where updating stands (/api/update, with Check for updates, Install now
// and the automatic switch), what's new, and the activity log (/api/activity). Changes carry the X-Rabbid-Lab header
// (Access.cs). Setup's What's new reads RELEASE_NOTES from this file. Each version's notes are here and, word for word,
// in CHANGELOG.md (ChangelogTests checks): newest first, each note one sentence first.
const RELEASE_NOTES = [
  ['1.1.13', [
    'Pages no longer shift sideways when going between a long page and a short one: room for the scrollbar is always kept.']],
  ['1.1.12', [
    'Settings has every setting in one place, under You, Server and Advanced, with Find a setting on top: Appearance, Updates, Notifications, Links, Active Directory, Activity log, Developer in Dev builds, and About.',
    'Appearance picks the layout, Centered or Full width, and the sidebar, names and icons or icons only. Both are kept in this browser.',
    'Notifications shows the mail server and the alert settings, and sends a test email. The Email settings button on Sensors opens it.',
    'Links settings and the Active Directory service account moved into Settings. The gears on Links and Active Directory open their page there.',
    'Pages sit in a column in the middle unless Full width is picked. Active Directory and Settings list their pages down the left with brighter text and a bar down the open one.']],
  ['1.1.11', [
    'Settings is laid out like Active Directory: a big title across the top, its sections listed down the left under Server and Options headings, and the open one marked with an orange bar. Developer is under Options.',
    'In a narrow window, the section headings in Active Directory and Settings stay as words instead of turning into lines.']],
  ['1.1.10', [
    'Settings has Options in place of Developer. Developer tools are under its Developer heading, and Options is where new settings will go.']],
  ['1.1.9', [
    'The Developer tools test buttons moved into Add a link and Add a sensor, so the whole add can be watched: the address goes in, the site is read under the flask, the tile fills in, and then you add it.',
    'Add a link has a test link that answers and one that does not, and lists of 12 and 200 addresses with a few that do not answer and one already on Links. Add a sensor fills in a ping sensor that answers or goes down.',
    'Anything added from a test address goes away after 30 seconds. Test sensors are checked every 5 seconds and never email.']],
  ['1.1.8', [
    'Rabbid Lab Dev has Developer tools under Settings, Developer. Turned on, Links gets test buttons for a link, a link that is down and a list of 12, and Sensors for a sensor that answers and one that goes down. Each test goes away after 30 seconds, and test sensors never email.']],
  ['1.1.7', [
    'Add a link has a new layout: the form beside the tile as it will look on the page, filled in from the site as you paste the address, with the icon picked in a strip under the tile.',
    'Adding a list reads every address first, then shows what each one is in a table: found, no answer, or on Links already. Tick the ones to add; the table scrolls, so a list of 200 fits.']],
  ['1.1.6', [
    'The README is down to the features and how to install on Windows.']],
  ['1.1.5', [
    'Windows service and Disk space sensors check a computer in Seats through its Rabbid Lab client, which runs as SYSTEM, so they need no credentials. The client needs this version for them; it updates itself from the server.']],
  ['1.1.4', [
    'Alert rules became saved alert settings, kept in Email settings: name them, add your own, delete them, and star one as the default for new sensors. The Alert rules button is gone.',
    'A sensor picks saved alert settings by name, or Custom for times of its own, which can be saved by name for other sensors.']],
  ['1.1.3', [
    'SNMP sensors read switches, UPS units and printers over v2c or v3 (SHA authentication, AES privacy): uptime, a UPS battery and power, whether an interface is up, or any OID.',
    'Saved credentials can be a username and password, an API key, an SNMP community or an SNMPv3 user, and each sensor offers only the kind it takes.']],
  ['1.1.2', [
    'VMware sensors check vCenter or a standalone ESXi host: hosts connected, datastore free space and triggered alarms. A user with the Read-only role is enough.',
    'Alert rules has the same layout as Add a sensor: the four rules down the left with their colour and how many sensors use each, and the open rule beside them.']],
  ['1.1.1', [
    'Storage sensors check a TrueNAS (with an API key) or a Synology (with a DSM account): pool or volume health, disks, and free space, with a warning below 5, 10 or 20 percent free.']],
  ['1.1.0', [
    'Sensors can check server hardware over Redfish (iDRAC, iLO, Supermicro): overall health, power supplies, fans, temperature and disks. Pick which of them count.',
    'Credentials for sensors are saved once and picked for any sensor. Passwords are stored encrypted, never shown again, and deleted when no sensor uses them.',
    'For a device with a self-signed certificate, Rabbid Lab trusts the certificate seen at Check now and only sends credentials to that one.',
    'The Links page title says Links.']],
  ['1.0.9', [
    'Add a sensor has a new layout: the kinds of check are listed down the left, and each one shows only its own fields. Check now shows what it found in a card.',
    'Web page sensors can count as up only for a status from 200 to 399, or only when the page has some text on it.',
    'DNS lookup sensors can check that a name resolves to the address it should.',
    'Certificate sensors can warn at 14, 21, 30 or 60 days left.']],
  ['1.0.8', [
    'Links, Sensors and Active Directory switch between rows and tiles with two icons in one box, in place of the Cards and Grid buttons.']],
  ['1.0.7', [
    'The Active Directory setup card sits in the middle of the window and is sharp: no blur behind it and no shadow around it.']],
  ['1.0.6', [
    'Links recognises common homelab products, such as TrueNAS, iDRAC, ESXi, vCenter and UniFi, and writes a description for each. Links already there get theirs once, including ones that only had the description copied from the site. Descriptions typed by hand are not changed.',
    'Links settings has a checkbox to turn this off. Turning it off removes the descriptions Rabbid Lab wrote.',
    'A link that is down has a red border and a red tint on its tile.',
    'Fill in on Add a link shows the flask and the site it is reading, without the line of text under them.']],
  ['1.0.5', [
    'Active Directory has a new layout. A big title, then its pages down the left with Find over them and the views of the open page tucked under its entry. The open entry is marked with a bar and brighter words.',
    'Users, Groups and Computers can be narrowed from the left: by enabled or disabled, by when they last signed in, and by OU.',
    'Across the top of each list: its tabs (People and System accounts on Users), how it is sorted, rows or tiles, Export and New.',
    'What needs a look shows as cards over the list, such as Locked out or Expiring passwords, each with how many and who. Click one to see them.',
    'Each row of a list is a card of its own, and the list can be shown as tiles.']],
  ['1.0.4', [
    'With no service account set up yet, Active Directory shows that once, in a card over the whole section, in place of a card at the top of each of its pages.']],
  ['1.0.3', [
    'The pages of Active Directory are listed down the left of the page, under its title, and look like the rail beside them: Find, then Directory, Tools and the views of the page that is open. It was a boxed panel of its own.',
    'Dropdowns have the console arrow, set in from the edge, in place of the one the browser draws.']],
  ['1.0.2', [
    'Active Directory is one entry in the rail. It opens a rail of its own beside the page, with Users, Groups, Computers, Domain settings and the directory tools, and the views of the page that is open under them.']],
  ['1.0.1', [
    'Adding a list of links shows only how far along it is, such as Reading 3 of 14, with nothing under it.']],
  ['1.0.0', [
    'The first release. Rabbid Lab is a console for a homelab or a small office: it runs as a Windows service or in Docker, and you use it in a browser.',
    'Links: the sites in your lab as cards or a grid, in groups you can drag around, each with its own icon and a dot for whether it answers. Paste a list of addresses to add them in one go, open a whole group at once, and export or import the lot.',
    'Sensors: ping, port, web page, DNS and certificate checks on a timer, with email when something stays down.',
    'Active Directory: users, groups and computers with views for what needs a look, domain settings, an org chart, an attribute browser, an audit and the Recycle Bin.',
    'Seats and local accounts: a Windows client for every computer, installed by hand, with Group Policy or with Intune, and the Windows accounts on the server and on each seat.',
    'Roles and people: sign in with a local account or Active Directory, with roles that allow exactly the parts you pick and an activity log of every change.',
    'Updates come from GitHub, are checked against a release signature, and install from the console or by themselves.']],
];

(() => {
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const when = (iso) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : 'never');
  const mb = (n) => (n / 1048576).toFixed(1) + ' MB';
  const svg = (id, cls = '') => `<svg${cls ? ` class="${cls}"` : ''}><use href="#i-${id}"/></svg>`;
  // Shows a line of text in an element, or hides it when there's nothing to say.
  const say = (id, text) => {
    $(id).hidden = !text;
    $(id).textContent = text || '';
  };
  const initials = (name) => {
    const w = String(name || '?').split(/[\s.@\\_-]+/).filter(Boolean);
    return ((w[0]?.[0] || '?') + (w.length > 1 ? w[w.length - 1][0] : '')).toUpperCase();
  };
  // How long ago in days: Today, Yesterday, 12 days ago, then the date.
  const daysAgo = (iso, never = 'Never') => {
    if (!iso) return never;
    const d = Math.floor((Date.now() - new Date(iso)) / 86400000);
    return d < 1 ? 'Today' : d === 1 ? 'Yesterday' : d < 60 ? `${d} days ago` : new Date(iso).toLocaleDateString([], { dateStyle: 'medium' });
  };
  const headers = { 'X-Rabbid-Lab': '1', 'Content-Type': 'application/json', Accept: 'application/json' };
  // A session that ended (signed out elsewhere, or a password changed) goes back to the sign-in page.
  const plainFetch = window.fetch.bind(window);
  let pending = 0; // requests still out, so starting can wait for the page's first reads
  const fetch = async (...args) => {
    pending++;
    let r;
    try {
      r = await plainFetch(...args);
    } finally {
      pending--;
    }
    if (r.status === 401) {
      location.replace('/signin.html');
      throw new Error('Signed out');
    }
    return r;
  };
  const WORDS = { current: 'Up to date', available: 'Update available', waiting: 'Waiting', downloading: 'Downloading', installing: 'Installing', failed: 'Failed' };
  let last = null;
  let timer = null;
  let product = 'Rabbid Lab';

  // What the signed-in person's roles allow (can), and what the page shows: the same, or a role's while seeing Rabbid Lab
  // as it. Anything marked data-can="a b" shows when either is allowed. Until sign-in is known, nothing marked shows.
  let can = new Set();
  let shown = null;
  let meInfo = null;
  let me = '';
  const allows = (list) => !!shown && list.split(/\s+/).some((p) => shown.has(p));
  const has = (p) => can.has(p);
  const PAGES = { overview: 'overview.view', links: 'links.view', sensors: 'sensors.view', seats: 'seats.view sessions.view', people: 'access.view', ad: 'ad.view ad.connect', local: 'windows.view',
    org: 'ad.view', attrs: 'ad.view' };
  // Settings' pages and what each needs; Appearance and About are for everyone, Developer for Rabbid Lab Dev only.
  const SUBS = { appearance: '', updates: 'updates.view', notifications: 'sensors.alerts', links: 'links.edit', ad: 'ad.connect', activity: 'activity.view',
    developer: 'updates.auto', about: '' };
  const subOk = (s) => s in SUBS && (!SUBS[s] || !shown || allows(SUBS[s])) && (s !== 'developer' || document.body.classList.contains('is-dev'));
  const pageOk = (key) => !shown || !PAGES[key] || allows(PAGES[key]);
  const home = () => ['overview', 'links', 'sensors', 'ad', 'seats', 'people', 'local'].find(pageOk) || 'wiki';
  const firstSetting = () => Object.keys(SUBS).find(subOk) || 'appearance';

  // Which page shows comes from the address: #settings/<page> is a page of Settings (#updates, #activity and #developer
  // still work), and #people and #role/<id> are Roles and people (people.js). The rails mark them.
  const where = () => {
    const [h, ...rest] = location.hash.slice(1).split('/');
    if (['updates', 'activity', 'developer'].includes(h)) return ['settings', h, rest];
    if (h === 'settings') return ['settings', rest[0] in SUBS ? rest[0] : firstSetting(), rest.slice(1)];
    if (h === 'role') return ['people', 'role', rest];
    return [h || home(), h, rest];
  };
  const page = () => {
    const key = where()[0];
    const el = document.querySelector(`.page[data-page="${CSS.escape(key)}"]`);
    return el && pageOk(key) ? el : $(`page-${home()}`);
  };
  const section = () => {
    const s = where()[1];
    return document.querySelector(`.sub[data-sub="${subOk(s) ? s : firstSetting()}"]`);
  };
  const title = () => {
    const p = page();
    document.title = `${p.id === 'page-settings' ? section().dataset.title : p.dataset.title} · ${product}`;
  };
  const mark = (links, key, value) => links.forEach((a) => {
    if (a.dataset[key] === value) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
  const show = () => {
    const now = page();
    const sub = section();
    document.querySelectorAll('.page').forEach((p) => { p.hidden = p !== now; });
    document.querySelectorAll('.sub').forEach((s) => { s.hidden = s !== sub; });
    mark(document.querySelectorAll('.rail a[data-page]'), 'page', now.dataset.page);
    mark(document.querySelectorAll('.srail a[data-sub]'), 'sub', sub.dataset.sub);
    // Active Directory's pages are listed down the left of whichever of them is open, with that page's views (users.js).
    const dir = now.classList.contains('dir-area');
    if (now.dataset.page !== 'ad') $('dir-rail').classList.remove('lists'); // Find, views and filters are for the pages that list things
    if (dir) {
      now.querySelector('.page-head').after($('dir-rail'));
      $('rail-dir').setAttribute('aria-current', 'page');
      mark(document.querySelectorAll('#dir-rail a[data-adpage]'), 'adpage', now.dataset.page === 'ad' ? where()[2][0] || 'users' : now.dataset.page);
    }
    title();
    document.dispatchEvent(new CustomEvent('rabbidlab:show', { detail: where() }));
  };
  // Hides what the roles don't allow, then shows the page again in case it was one of those.
  const apply = () => {
    document.querySelectorAll('[data-can]').forEach((el) => el.classList.toggle('cant', !allows(el.dataset.can)));
    show();
  };
  // Role colours come from data-color: the page's rules allow no style attributes.
  const paint = (root) => root.querySelectorAll('[data-color]').forEach((el) => el.style.setProperty('--c', el.dataset.color));
  const chip = (r) => `<span class="rchip" data-color="${esc(r.color)}"><i class="dot"></i>${esc(r.name)}</span>`;
  // Seeing Rabbid Lab as a role: what it allows (with User's), until Stop.
  const viewAs = (role, permissions) => {
    shown = role ? new Set(permissions) : can;
    $('viewas').hidden = !role;
    $('viewas-name').textContent = role ? role.name : '';
    apply();
  };

  // The rail shrinks to icons when asked (remembered in this browser) or when the window is narrow.
  const narrow = matchMedia('(max-width: 760px)');
  const rail = () => {
    const small = narrow.matches || localStorage.getItem('rabbidlab.rail') === 'small';
    document.body.classList.toggle('rail-small', small);
    $('rail-collapse').setAttribute('aria-expanded', String(!small));
    $('rail-collapse-text').textContent = small ? 'Expand' : 'Collapse';
    $('rail-collapse').title = small ? 'Expand' : 'Collapse';
    $('set-sidebar').querySelectorAll('[data-rail]').forEach((b) => b.setAttribute('aria-pressed', String((b.dataset.rail === 'small') === (localStorage.getItem('rabbidlab.rail') === 'small'))));
    $('set-narrow').hidden = !narrow.matches;
  };
  // Appearance, Layout: pages in a column in the middle, or the whole window.
  const layout = () => {
    let wide = false;
    try { wide = localStorage.getItem('rabbidlab.layout') === 'wide'; } catch { /* centered */ }
    document.body.classList.toggle('layout-wide', wide);
    $('set-layout').querySelectorAll('[data-layout]').forEach((b) => b.setAttribute('aria-pressed', String((b.dataset.layout === 'wide') === wide)));
  };
  $('set-layout').addEventListener('click', (e) => {
    const b = e.target.closest('[data-layout]');
    if (!b) return;
    try { localStorage.setItem('rabbidlab.layout', b.dataset.layout); } catch { /* this visit only */ }
    layout();
  });
  $('set-sidebar').addEventListener('click', (e) => {
    const b = e.target.closest('[data-rail]');
    if (!b) return;
    localStorage.setItem('rabbidlab.rail', b.dataset.rail);
    rail();
  });
  layout();
  rail();
  // Find a setting: the pages whose name or settings have the words, and their groups' headings.
  $('set-find').addEventListener('input', () => {
    const q = $('set-find').value.trim().toLowerCase();
    const nav = $('set-find').closest('nav');
    let any = false;
    nav.querySelectorAll('a[data-sub]').forEach((a) => {
      const page = document.querySelector(`.sub[data-sub="${a.dataset.sub}"]`);
      a.hidden = !!q && !`${a.textContent} ${page ? page.textContent : ''}`.toLowerCase().includes(q);
      if (!a.hidden && a.offsetParent) any = true;
    });
    nav.querySelectorAll('.rail-label').forEach((p) => {
      let el = p.nextElementSibling;
      let show = false;
      for (; el && el.tagName === 'A'; el = el.nextElementSibling) if (!el.hidden && !el.classList.contains('cant')) show = true;
      p.hidden = !show;
    });
    $('set-none').hidden = any || !q;
  });
  $('set-find').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    const a = [...$('set-find').closest('nav').querySelectorAll('a[data-sub]')].find((x) => !x.hidden && x.offsetParent);
    if (a) location.hash = a.getAttribute('href');
  });
  apply();

  // Rabbid Lab Dev's Developer tools: the switch in Settings, Developer, and the DEV buttons it shows in Add a link and Add a sensor.
  const devTools = async (on) => {
    try {
      const d = on === undefined ? await get('/api/devtools') : await send('/api/devtools', { on });
      document.body.classList.toggle('dev-tools', !!d.on);
      $('dev-tools').checked = !!d.on;
      $('dev-tools-label').textContent = d.on ? 'On' : 'Off';
      say('dev-problem', '');
    } catch (err) {
      say('dev-problem', err.message);
    }
  };
  $('dev-tools').addEventListener('change', (e) => devTools(e.target.checked));
  const status = async () => {
    const r = await fetch('/api/status', { headers: { Accept: 'application/json' } });
    if (!r.ok) throw new Error(r.statusText);
    const s = await r.json();
    product = s.product;
    title();
    $('brand-name').textContent = s.product;
    $('rail-version').textContent = s.version;
    $('rail-repo').href = s.repository;
    $('foot-version').textContent = `${s.product} ${s.version}`;
    $('foot-repo').href = s.repository;
    document.body.classList.toggle('is-dev', !!s.dev);
    if (s.dev) devTools();
    $('hero-title').textContent = s.product;
    $('hero-version').textContent = s.version;
    $('hero-where').textContent = `Running on ${s.server} since ${when(s.startedAt)}.`;
    $('ab-version').textContent = `${s.product} ${s.version}`;
    $('ab-where').textContent = `Running on ${s.server} since ${when(s.startedAt)}.`;
    $('ab-repo').href = s.repository;
    $('ab-repo-text').textContent = s.repository.replace(/^https?:\/\//, '');
    $('hero-repo').href = s.repository;
    started = s.startedAt;
    uptime();
  };

  // How long Rabbid Lab has been running, in the hero.
  let started = null;
  const uptime = () => {
    if (!started) return;
    const m = Math.floor((Date.now() - new Date(started)) / 60000);
    const say = (n, unit) => `${n} ${unit}${n === 1 ? '' : 's'}`;
    $('hero-uptime').textContent = m < 1 ? 'Just started' : m < 60 ? say(m, 'minute') : m < 2880 ? say(Math.floor(m / 60), 'hour') : say(Math.floor(m / 1440), 'day');
  };

  // The Updates status card: an icon, where it stands, and a line about it.
  const card = (icon, tone, heading, line) => {
    $('upd-icon-use').setAttribute('href', `#i-${icon}`);
    $('upd-icon').className = `big-ic ${tone}`;
    $('upd-title').textContent = heading;
    $('upd-line').textContent = line;
  };
  const checked = (iso) => {
    if (!iso) return 'not checked yet';
    const d = new Date(iso);
    return d.toDateString() === new Date().toDateString() ? `checked today at ${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}` : `checked ${when(iso)}`;
  };

  // An update under way takes the status card's place, and shows in a dialog mid screen: download, install, back on the
  // new version. Phase null puts the status card back.
  let since = 0; // when installing began
  let tick = null;
  let hid = false; // the update dialog was put away with Hide
  const bar = (phase, pct, version, line) => {
    const on = phase != null;
    $('upd-installing').hidden = !on;
    $('upd-status').hidden = on;
    if (!on) {
      clearInterval(tick);
      tick = null;
      since = 0;
      hid = false;
      if ($('update-dialog').open) $('update-dialog').close();
      return;
    }
    // Mid screen too: the flask, and the same steps. Hide puts it away until the next update.
    $('updlg-flask').innerHTML ||= FLASK;
    $('updlg-title').textContent = phase === 'done' ? `Updated to Rabbid Lab ${version}` : `Updating to Rabbid Lab ${version}`;
    $('updlg-now').textContent = phase === 'done' ? 'Done. Opening the new version.' : phase === 'install' ? 'Installing the update. The server is offline for about a minute.'
      : `Downloading the update${/ of /.test(line) ? `, ${line.split(' · ').pop()}` : ''}.`;
    $('updlg-say').textContent = phase === 'done' ? '' : 'This page reloads by itself when the update is in.';
    $('updlg-hide').hidden = phase === 'done';
    if (!hid && !$('update-dialog').open) $('update-dialog').showModal();
    const p = `${Math.min(100, Math.round(pct))}%`;
    $('inst-fill').style.width = p;
    $('updlg-fill').style.width = p;
    $('inst-pct').textContent = p;
    $('inst-title').textContent = phase === 'done' ? `Updated to Rabbid Lab ${version}` : `Updating to Rabbid Lab ${version}`;
    $('inst-line').textContent = line;
    const steps = { download: ['now', '', ''], install: ['done', 'now', ''], done: ['done', 'done', 'done'] }[phase];
    for (const at of ['step', 'updlg']) {
      ['download', 'install', 'back'].forEach((id, i) => { $(`${at}-${id}`).className = steps[i]; });
      $(`${at}-download`).textContent = phase === 'download' ? 'Downloading' : 'Downloaded';
      $(`${at}-install`).textContent = phase === 'done' ? 'Installed' : 'Installing';
      $(`${at}-back`).textContent = `Back on ${version}`;
    }
    $('inst-say').textContent = phase === 'done' ? 'Done. This page opens the new version in a moment.'
      : 'The server is offline for about a minute while the update installs. This page reloads when it is done.';
  };
  // The download fills two thirds of the bar; while Rabbid Lab is stopped to install, it moves on the minute that takes.
  const installing = (version) => {
    since ||= Date.now();
    bar('install', 66 + 32 * (1 - Math.exp(-(Date.now() - since) / 30000)), version, last ? `From ${last.version}` : '');
    tick ||= setInterval(() => installing(version), 1000);
  };

  // The hero says where updating stands in a few words; Settings > Updates has the rest.
  const headline = (u) => ({
    available: `Version ${u.available} is ready to install.`,
    waiting: `Version ${u.available} installs in a moment.`,
    downloading: `Downloading version ${u.available}.`,
    installing: `Installing version ${u.available}.`,
    failed: `Version ${u.available} didn't install.`,
  })[u.status] || 'Rabbid Lab is up to date.';

  const draw = (u) => {
    // Back as a new version: a full bar and the version for a moment, then the new page.
    if (last && u.version !== last.version) {
      clearInterval(tick);
      bar('done', 100, u.version, `From ${last.version}`);
      setTimeout(() => location.reload(), 1500);
      return;
    }
    last = u;
    // A dot on Settings, and on Updates in it, while there's something to see there.
    for (const id of ['rail-dot', 'srail-dot']) {
      $(id).hidden = !['available', 'downloading', 'installing', 'failed'].includes(u.status);
      $(id).classList.toggle('bad', u.status === 'failed');
    }
    const busy = u.status === 'downloading' || u.status === 'installing';
    // While the update dialog is up it does the talking: the words behind it stay as they were.
    if (!busy || hid) {
      $('hero-state').textContent = headline(u);
      $('hero-updates').textContent = WORDS[u.status] || u.status;
    }
    $('hero-check').disabled = busy;
    // An update ready: Install now leads, and Check for updates steps back.
    $('hero-install').hidden = !(u.status === 'available' || u.status === 'failed') || !u.canInstall;
    $('hero-check').classList.toggle('primary', $('hero-install').hidden);
    if (u.status === 'installing') installing(u.available);
    else if (u.status === 'downloading') {
      bar('download', 4 + 62 * (u.total ? (u.downloaded || 0) / u.total : 0), u.available,
        `From ${u.version}${u.total ? ` · ${mb(u.downloaded || 0)} of ${mb(u.total)}` : ''}`);
    } else bar(null);
    if (u.status === 'available') {
      card('download', 'ember', `Rabbid Lab ${u.available} is ready`, u.automatic
        ? `You have ${u.version}. The update installs automatically when nobody is using Rabbid Lab, or click Install now.`
        : `You have ${u.version}. Installing takes about a minute, and the server is offline while it does.`);
    } else if (u.status === 'waiting') card('clock', 'ember', `Version ${u.available} installs in a moment`, u.message);
    else if (u.status === 'failed') card('alert', 'bad', `Version ${u.available} didn't install`, u.message);
    else card('check', 'ok', "You're up to date", `Rabbid Lab ${u.version} · ${checked(u.checkedAt)}`);
    $('upd-problem').hidden = !u.problem;
    $('upd-problem').textContent = u.problem || '';
    $('upd-install').hidden = !(u.status === 'available' || u.status === 'failed') || !u.canInstall;
    $('upd-install').disabled = busy;
    $('upd-check').disabled = busy;
    $('upd-auto').checked = !!u.automatic;
    $('upd-auto-label').textContent = u.automatic ? 'On' : 'Off';
    $('upd-history').textContent = u.updatedFrom ? `${u.updatedFrom} to ${u.version}. Every update is in the activity log.` : 'Every update is in the activity log.';
    $('upd-source').href = u.source;
    schedule(busy ? 2000 : document.hidden ? 60000 : 15000);
  };

  const update = async () => {
    try {
      const r = await fetch('/api/update', { headers: { Accept: 'application/json' } });
      if (!r.ok) throw new Error(r.statusText);
      draw(await r.json());
    } catch {
      // Setup stops Rabbid Lab to install, sometimes before this page has seen the download finish.
      if (last && (last.status === 'installing' || last.status === 'downloading')) {
        $('rail-dot').hidden = false;
        installing(last.available);
        schedule(3000);
      } else {
        card('alert', 'bad', "Can't reach the server", 'Retrying automatically.');
        $('hero-updates').textContent = 'Unreachable';
        schedule(5000);
      }
    }
  };

  const schedule = (ms) => {
    clearTimeout(timer);
    timer = setTimeout(update, ms);
  };

  const post = async (path, body) => {
    $('upd-check').disabled = true;
    $('upd-install').disabled = true;
    try {
      const r = await fetch(path, { method: 'POST', headers, body: JSON.stringify(body || {}) });
      const j = await r.json();
      if (!r.ok) {
        $('upd-problem').hidden = false;
        $('upd-problem').textContent = j.error || r.statusText || `The server answered ${r.status}.`;
        $('upd-check').disabled = false;
        $('upd-install').disabled = false;
        return;
      }
      draw(j);
      if (can.has('activity.view')) activity();
    } catch (e) {
      $('upd-problem').hidden = false;
      $('upd-problem').textContent = `Couldn't reach Rabbid Lab (${e.message}).`;
      $('upd-check').disabled = false;
      $('upd-install').disabled = false;
    }
  };

  // Check for updates says Checking while it asks; when nothing is newer, a short line beside it says so.
  const check = async (button, note) => {
    button.disabled = true;
    button.textContent = 'Checking…';
    note.hidden = true;
    await post('/api/update/check');
    button.disabled = !!last && ['downloading', 'installing'].includes(last.status);
    button.textContent = 'Check for updates';
    if (!last || last.status !== 'current') return;
    note.textContent = '✓ You have the newest version';
    note.hidden = false;
    clearTimeout(note.timer);
    note.timer = setTimeout(() => { note.hidden = true; }, 6000);
  };

  const activity = async () => {
    try {
      const r = await fetch('/api/activity?count=25', { headers: { Accept: 'application/json' } });
      if (r.status === 403) {
        $('activity-list').innerHTML = '<li class="muted">Your roles don\'t let you see the activity log.</li>';
        return;
      }
      if (!r.ok) throw new Error(r.statusText);
      const list = await r.json();
      $('activity-list').innerHTML = list.length
        ? list.map((a) => `<li><span class="status status-${a.result === 'ok' ? 'ok' : a.result === 'warning' ? 'amber' : 'red'}"><i aria-hidden="true"></i></span>`
          + `<span class="act-title">${esc(a.title)}</span><span class="act-by">${esc(a.by)}</span><span class="act-when">${esc(when(a.at))}</span>`
          + (a.detail ? `<span class="act-detail">${esc(a.detail)}</span>` : '') + '</li>').join('')
        : '<li class="muted">Nothing yet.</li>';
    } catch {
      // The page still shows everything else.
    }
  };

  // A change, as the page asks for it: the answer, or the error it gave.
  const send = async (path, body) => {
    const r = await fetch(path, { method: 'POST', headers, body: JSON.stringify(body || {}) });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(j.error || r.statusText || `The server answered ${r.status}.`);
    return j;
  };
  const get = async (path) => {
    const r = await fetch(path, { headers: { Accept: 'application/json' } });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(j.error || r.statusText || `The server answered ${r.status}.`);
    // A plain list that Active Directory cut short says so in a header.
    if (Array.isArray(j)) j.more = r.headers.get('X-More') === 'true';
    return j;
  };
  // A read two pages share: asked again unless the last answer is newer than ms.
  const kept = new Map();
  const keep = (path, ms = 0) => {
    const k = kept.get(path);
    if (k && Date.now() - k.at < ms) return k.answer;
    const answer = get(path);
    kept.set(path, { at: Date.now(), answer });
    answer.catch(() => { if (kept.get(path)?.answer === answer) kept.delete(path); });
    return answer;
  };
  const forget = () => kept.clear();

  // --- What the pages share: popup cards, search lists, Are you sure, and Copy ------------------------------------

  // Puts a popup card under the row it's for, inside its page.
  const place = (box, near, pageEl, dx = 0) => {
    const pg = pageEl.getBoundingClientRect();
    const r = near.getBoundingClientRect();
    box.hidden = false;
    box.style.top = `${r.bottom - pg.top + 6}px`;
    box.style.left = `${Math.max(12, Math.min(r.left - pg.left + dx, pg.width - box.offsetWidth - 16))}px`;
  };
  // Closes popup cards on Escape and on a click outside them; inside is a selector for what doesn't close them.
  const popups = (inside, close) => {
    document.addEventListener('click', (e) => { if (!e.target.closest(`${inside}, dialog`)) close(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !document.querySelector('dialog[open]')) close(); });
  };
  // A filter button with a menu under it, to show one group, on Links and Sensors: id-filter holds id-filter-open, id-filter-name and id-groups.
  // rows() gives each choice as [value, name, count, colour]. Past a handful the menu has its own search.
  const groupFilter = (id, { rows, picked, pick }) => {
    const menu = $(`${id}-groups`);
    const button = $(`${id}-filter-open`);
    const close = () => {
      menu.hidden = true;
      button.setAttribute('aria-expanded', 'false');
    };
    const open = () => {
      const list = rows();
      menu.innerHTML = (list.length > 9 ? `<label class="side-find">${svg('search')}<input type="search" id="${id}-filter-find" placeholder="Find a group" aria-label="Find a group" autocomplete="off" spellcheck="false"></label>` : '')
        + list.map(([g, name, n, colour]) => `<button type="button" class="opt" data-group="${esc(g)}" aria-pressed="${g === picked()}">${colour ? `<i class="sq" data-color="${esc(colour)}"></i>` : ''}<span class="t"><b>${esc(name)}</b></span><small>${n}</small></button>`).join('');
      paint(menu);
      menu.hidden = false;
      button.setAttribute('aria-expanded', 'true');
      menu.querySelector('input')?.focus();
    };
    button.addEventListener('click', () => (menu.hidden ? open() : close()));
    menu.addEventListener('input', (e) => {
      const text = e.target.value.trim().toLowerCase();
      menu.querySelectorAll('.opt').forEach((b) => { b.hidden = !!text && !b.textContent.toLowerCase().includes(text); });
    });
    menu.addEventListener('click', (e) => {
      const b = e.target.closest('[data-group]');
      if (!b) return;
      close();
      pick(b.dataset.group);
    });
    popups(`#${id}-filter`, close);
    // Shown only when picking one would leave something out; the button says what is picked.
    return (narrows, name) => {
      $(`${id}-filter`).hidden = !narrows;
      if (!narrows) close();
      $(`${id}-filter-name`).textContent = name || 'All groups';
      button.classList.toggle('on', !!name);
    };
  };
  // One line of a search list: a picture, a name and a detail.
  const option = (attrs, pic, name, detail) => `<button type="button" class="opt"${attrs}>${pic}<span class="t"><b>${esc(name)}</b><span>${esc(detail || '')}</span></span></button>`;
  // A search box ([data-find]) over a list ([data-drop]) in host: find(text) gets the matches, draw(item, i) a line with data-pick, pick(item) the choice.
  const dropdown = (host, { find, draw, pick, none = 'Nothing found.' }) => {
    let timer = null;
    let found = [];
    let asked = 0;
    host.addEventListener('input', (e) => {
      if (!e.target.matches('[data-find]')) return;
      clearTimeout(timer);
      const n = ++asked;
      timer = setTimeout(async () => {
        const text = e.target.value.trim();
        if (!text) {
          const drop = host.querySelector('[data-drop]');
          if (drop) drop.hidden = true;
          return;
        }
        let html;
        try {
          const got = await find(text);
          if (n !== asked) return;
          found = Array.isArray(got) ? got : got.items;
          html = (got.note ? `<p class="drop-note">${esc(got.note)}</p>` : '') + (found.map(draw).join('') || `<p class="drop-note">${esc(none)}</p>`);
        } catch (err) {
          if (n !== asked) return;
          html = `<p class="drop-note">${esc(err.message)}</p>`;
        }
        // Looked up again here: the card may have been drawn again while asking.
        const drop = host.querySelector('[data-drop]');
        if (!drop) return;
        drop.innerHTML = html;
        drop.hidden = false;
      }, 250);
    });
    host.addEventListener('click', (e) => {
      const b = e.target.closest('[data-pick]');
      if (!b || !host.contains(b)) return;
      const item = found[Number(b.dataset.pick)];
      const box = host.querySelector('[data-find]');
      const drop = host.querySelector('[data-drop]');
      if (box) box.value = '';
      if (drop) drop.hidden = true;
      asked++;
      if (item) pick(item);
    });
  };
  // The flask that foams while something loads.
  const FLASK = '<svg class="flask" viewBox="0 0 64 64" aria-hidden="true"><defs>'
    + '<linearGradient id="flask-ember" gradientUnits="userSpaceOnUse" x1="0" y1="21" x2="0" y2="55"><stop offset="0" stop-color="#FF8A3D"/><stop offset="1" stop-color="#E8542F"/></linearGradient>'
    + '<clipPath id="flask-glass"><path d="M26.5 21V30L12.5 48.5Q9 55 16 55H48Q55 55 51.5 48.5L37.5 30V21Z"/></clipPath></defs>'
    + '<path fill="#F2F3FA" d="M26.5 21V30L12.5 48.5Q9 55 16 55H48Q55 55 51.5 48.5L37.5 30V21Z"/><rect x="24" y="17" width="16" height="5" rx="2.5" fill="#F2F3FA"/>'
    + '<g clip-path="url(#flask-glass)"><path class="flask-wave" fill="url(#flask-ember)" d="M0 41Q8 37 16 41T32 41T48 41T64 41T80 41T96 41V64H0Z"/>'
    + '<g class="flask-fizz" fill="#F2BE6B"><circle cx="25" cy="48" r="2.4"/><circle cx="37" cy="46" r="1.7"/><circle cx="31" cy="50" r="1.3"/></g></g>'
    + '<g class="flask-foam" fill="#F2BE6B"><circle cx="28.5" cy="15.5" r="5.5"/><circle cx="34.5" cy="12.5" r="6.2"/><circle cx="40.5" cy="16.5" r="4.5"/></g>'
    + '<g class="flask-pop" fill="#F2BE6B"><circle cx="46" cy="9.5" r="2.2"/><circle cx="21.5" cy="10" r="1.7"/></g></svg>';
  // Covers a part of the page with the flask, a line of text and smaller lines under it; no text takes the cover away.
  const busy = (host, text, detail) => {
    let cover = host.querySelector(':scope > .busy-cover');
    if (!text) {
      cover?.remove();
      host.classList.remove('busy-host');
      return;
    }
    if (!cover) {
      cover = document.createElement('span');
      cover.className = 'busy-cover';
      cover.setAttribute('role', 'status');
      cover.innerHTML = `${FLASK}<b></b><small></small>`;
      host.append(cover);
      host.classList.add('busy-host');
    }
    cover.querySelector('b').textContent = text;
    cover.querySelector('small').textContent = detail || ''; // what it's doing just now, a line for each thing
  };
  // Are you sure: a title, what happens, the button's words, and what to do on yes.
  let sureDo = null;
  const sure = (title, lead, button, then) => {
    $('sure-title').textContent = title;
    $('sure-lead').textContent = lead;
    $('sure-go').textContent = button;
    sureDo = then;
    $('sure-dialog').showModal();
  };
  // Copies an element's text. Over plain HTTP browsers have no clipboard, so it selects the text to copy by hand.
  const copy = async (el, button) => {
    try {
      await navigator.clipboard.writeText(el.textContent);
      button.textContent = 'Copied';
    } catch {
      getSelection().selectAllChildren(el);
      button.textContent = 'Press Ctrl+C';
    }
  };

  // Seats: the computers with the client and everyone signed in, asked again every 30 seconds while it's open.
  let seatFilter = 'all';
  let computers = [];
  let seatTimer = null;
  const ago = (iso) => {
    const s = Math.round((Date.now() - new Date(iso)) / 1000);
    if (s < 60) return 'Now';
    if (s < 3600) return `${Math.floor(s / 60)} min ago`;
    const d = new Date(iso);
    return d.toDateString() === new Date().toDateString() ? d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : when(iso);
  };
  const stateOf = (c) => (!c.approved ? 'waiting' : c.online ? 'online' : 'offline');
  const STATE = { online: 'Online', offline: 'Offline', waiting: 'Waiting' };
  const drawSeats = () => {
    const n = { all: computers.length, behind: computers.filter((c) => c.behind).length };
    for (const st of ['online', 'offline', 'waiting']) n[st] = computers.filter((c) => stateOf(c) === st).length;
    for (const k of ['online', 'offline', 'waiting', 'behind']) $(`n-${k}`).textContent = n[k];
    for (const k of Object.keys(n)) $(`f-${k}`).textContent = n[k];
    $('rail-seats').textContent = n.online || '';
    $('seats-dot').hidden = !n.waiting;
    const shown = computers.filter((c) => seatFilter === 'all' || (seatFilter === 'behind' ? c.behind : stateOf(c) === seatFilter));
    $('seats-table').innerHTML = '<div class="tr th"><span>State</span><span>Computer</span><span>Signed in to Windows</span><span>Client</span><span>Address</span><span>Last seen</span><span></span></div>'
      + (shown.length ? shown.map((c) => {
        const st = stateOf(c);
        return `<div class="tr" data-client="${esc(c.id)}"><span class="state ${st}">${STATE[st]}</span>`
          + `<div><b>${esc(c.computer)}</b><small>${esc(c.uninstalling ? 'Uninstalling its client' : c.removalUntil ? `Uninstall allowed until ${new Date(c.removalUntil).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}` : c.os || '')}</small></div>`
          + (c.user ? `<span>${esc(c.user)}</span>` : '<span class="nobody">Nobody</span>')
          + `<span class="${c.behind ? 'old' : ''}">${esc(c.version || '?')}</span>`
          + `<span class="mono muted">${esc(c.address || '')}</span><span class="muted">${esc(ago(c.lastSeen))}</span>`
          + `<span class="acts">${st === 'waiting' && can.has('seats.approve') ? '<button type="button" class="small primary" data-approve>Let in</button>' : ''}`
          + `${st !== 'waiting' && can.has('seats.remove') ? '<button type="button" class="small quiet" data-uninstall>Uninstall</button>' : ''}`
          + `${can.has('seats.remove') ? '<button type="button" class="small quiet" data-forget>Remove</button>' : ''}</span></div>`;
      }).join('') : `<div class="empty">${computers.length ? 'None of the computers are like that right now.' : 'No computers yet. Add computers to give people the Rabbid Lab client.'}</div>`);
  };
  const drawSessions = (list) => {
    // Each person's roles, highest first, without User when there's more: everyone has that.
    const roles = (s) => (s.roles.length > 1 ? s.roles.filter((r) => r.id !== 'user') : s.roles);
    $('sessions-table').innerHTML = '<div class="tr th sess"><span>Person</span><span>Roles</span><span>Using</span><span>Address</span><span>Signed in</span><span>Last seen</span><span></span></div>'
      + list.map((s) => `<div class="tr sess" data-session="${esc(s.id)}"><div><b>${esc(s.name || s.username)}</b><small>${esc(s.username)}</small></div>`
        + `<span class="chips-in">${roles(s).map(chip).join('')}</span>`
        + `<div><span>${esc(s.device)}</span><small>${s.remembered ? 'Kept signed in' : 'Until the page closes'}</small></div>`
        + `<span class="mono muted">${esc(s.address || '')}</span><span class="muted">${esc(when(s.started))}</span><span class="muted">${esc(ago(s.lastSeen))}</span>`
        + `<span class="acts">${s.current ? '<span class="you">You, here</span>'
          : s.username === me || can.has('sessions.signout') ? '<button type="button" class="small quiet" data-end>Sign out</button>' : ''}</span></div>`).join('');
    paint($('sessions-table'));
  };
  const seats = async () => {
    clearTimeout(seatTimer);
    try {
      const none = new Response('[]', { status: 403 });
      const [c, s] = await Promise.all([can.has('seats.view') ? fetch('/api/clients', { headers: { Accept: 'application/json' } }) : none,
        fetch('/api/sessions', { headers: { Accept: 'application/json' } })]);
      if (c.ok) {
        computers = await c.json();
        drawSeats();
      }
      if (s.ok) drawSessions(await s.json());
    } catch {
      // Asked again shortly.
    }
    if (where()[0] === 'seats') seatTimer = setTimeout(seats, 30000);
  };
  const seatProblem = (text) => say('seats-problem', text);
  // Add computers: the quiet install line with this server's address and its certificate fingerprint in it.
  let joining = null;
  const drawJoining = () => {
    if (!joining) return;
    const parts = [`msiexec /i RabbidLab-Client-${joining.version}.msi /qn SERVER=${location.origin}`];
    if (joining.fingerprint) parts.push(`FINGERPRINT=${joining.fingerprint}`);
    $('install-line').textContent = parts.join(' ');
    $('msi-link').hidden = !joining.msi;
    $('get-client').textContent = joining.msi ? 'Download it and run it, or install it quietly from a command line or Intune:'
      : 'Get the client from the Rabbid Lab release on GitHub, then install it quietly from a command line or Intune:';
    $('policy-link').hidden = !joining.policy;
    $('fingerprint-row').hidden = !joining.fingerprint;
    $('fingerprint').textContent = joining.fingerprint || '';
    $('gpo-fingerprint').hidden = !joining.fingerprint;
  };

  const notes = () => {
    $('notes').innerHTML = RELEASE_NOTES.map(([v, items]) => `<li><b>${esc(v)}</b><ul>${items.map((n) => `<li>${esc(n)}</li>`).join('')}</ul></li>`).join('');
    // The hero's New card and the news cards: the newest versions, each with its first note.
    const [newest, first] = RELEASE_NOTES[0];
    $('new-title').textContent = `Rabbid Lab ${newest}`;
    $('new-text').textContent = first[0];
    $('upd-new-title').textContent = `What's new in ${newest}`;
    $('upd-new-text').textContent = first[0];
    $('news').innerHTML = RELEASE_NOTES.slice(0, 4).map(([v, items]) => `<a class="post" href="#whats-new"><span class="meta"><span class="tag">Update</span>`
      + `${items.length} change${items.length === 1 ? '' : 's'}</span><b>Rabbid Lab ${esc(v)}</b><span class="text">${esc(items[0])}</span></a>`).join('');
  };

  // This computer, when it isn't a seat yet: the client to download, then a rabbidlab://join link that hands the client
  // this server's address. Windows only, and not for a month after Not now.
  const LATER = 'rabbidlab.here.later';
  let hereTimer = null;
  let hereInfo = null;
  const joinLink = () => {
    const q = new URLSearchParams({ server: hereInfo.server });
    if (hereInfo.fingerprint) q.set('fingerprint', hereInfo.fingerprint);
    return `rabbidlab://join?${q}`;
  };
  const joined = (seat) => {
    clearInterval(hereTimer);
    $('here-text').textContent = seat.approved ? `${seat.computer} is connected.` : `${seat.computer} joined. Let it in under Seats.`;
    $('here-download').hidden = true;
    $('here-connect').hidden = true;
    setTimeout(() => { $('herebar').hidden = true; }, 12000);
  };
  const here = async () => {
    if (!/Windows/.test(navigator.userAgent) || Number(localStorage.getItem(LATER) || 0) > Date.now()) return;
    try {
      hereInfo = await get('/api/clients/here');
    } catch {
      return;
    }
    if (hereInfo.seat || hereInfo.thisServer) return;
    $('here-connect').href = joinLink();
    if (!hereInfo.msi) {
      $('here-download').href = 'https://github.com/Rabbid85/rabbidlab/releases/latest';
      $('here-download').removeAttribute('download');
      $('here-download').target = '_blank';
    }
    $('herebar').hidden = false;
  };
  // After Connect, watch for this computer to show up.
  const watchHere = () => {
    clearInterval(hereTimer);
    let tries = 0;
    hereTimer = setInterval(async () => {
      if (++tries > 36) clearInterval(hereTimer);
      try {
        const h = await get('/api/clients/here');
        if (h.seat) joined(h.seat);
      } catch {
        // Asked again in a moment.
      }
    }, 5000);
  };

  // Still tiles over the right of the hero, placed the same way every time.
  const scatter = () => {
    const COLOURS = window.RabbidLabColours.tiles;
    let seed = 20261109;
    const r = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < 22; i++) {
      const t = document.createElement('i');
      const roll = r();
      const kind = roll < 0.18 ? 'dot' : roll < 0.42 ? 'outline' : 'fill';
      const size = `${kind === 'dot' ? 4 + r() * 5 : 10 + r() * 26}px`;
      const colour = COLOURS[Math.floor(r() * COLOURS.length)];
      t.className = `tile ${kind}`;
      Object.assign(t.style, { left: `${58 + r() * 38}%`, top: `${4 + r() * 86}%`, width: size, height: size,
        transform: `rotate(${r() * 90}deg)`, opacity: String(0.35 + r() * 0.5) });
      if (kind === 'outline') t.style.border = `1.5px solid ${colour}`;
      else t.style.background = colour;
      $('hero').prepend(t);
    }
  };

  // Where a page's squares go: rows over the right of its hero, more and smaller as there are more, clear of the card in the corner.
  const places = (n) => {
    const [cols, rows, size] = n <= 30 ? [8, 6, 34] : n <= 60 ? [11, 8, 24] : n <= 120 ? [15, 11, 16] : [20, 14, 11];
    const slots = [];
    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        const x = 56 + col * (42 / cols) + (row % 2) * (21 / cols);
        const y = 7 + row * (80 / rows);
        if (!(x > 75 && y < 58)) slots.push([x, y]);
      }
    }
    // Mixed the same way every time, so a square stays where it was.
    let seed = 20261110;
    const next = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = slots.length - 1; i > 0; i--) {
      const j = Math.floor(next() * (i + 1));
      [slots[i], slots[j]] = [slots[j], slots[i]];
    }
    return { slots, size };
  };

  document.addEventListener('DOMContentLoaded', () => {
    notes();
    scatter();
    setInterval(uptime, 60000);
    // Starting: the rail and the page stay hidden (body.booting) until sign-in has answered and the page's first reads are back, so a
    // reload never shows the console half built. The flask comes up if that takes a moment; after a second and a half the page shows as it is.
    const main = document.querySelector('.main');
    const bootFlask = setTimeout(() => busy(main, 'Loading…'), 400);
    const started = () => {
      clearTimeout(bootFlask);
      busy(main, '');
      document.body.classList.remove('booting');
    };
    const startWhenQuiet = () => {
      const until = Date.now() + 1500;
      let quiet = 0;
      const look = () => {
        quiet = pending ? 0 : quiet + 1;
        if (quiet >= 2 || Date.now() > until) started();
        else setTimeout(look, 25);
      };
      look();
    };
    // The account chip: initials and name, with a menu saying how this session signed in.
    const signedIn = (s) => {
      const u = s.user;
      if (!u) return location.replace('/signin.html');
      const name = u.name || u.username;
      const letters = initials(name);
      const domain = u.domain || s.domain;
      const dc = u.server && u.server !== domain ? u.server : null;
      const rows = u.source === 'ad' ? [['Signed in with', 'Active Directory'], ['Domain', domain], ['Domain controller', dc ? dc.split('.')[0].toUpperCase() : 'Automatic', dc]]
        : u.source === 'windows' ? [['Signed in with', 'Local account'], ['Computer', u.domain]] : [['Signed in with', 'Local account']];
      // What their roles allow decides what shows; each part loads only when it's allowed.
      can = new Set(u.can || []);
      shown = can;
      meInfo = u;
      me = u.username;
      const top = (u.roles || []).filter((r) => r.id !== 'user');
      rows.push([top.length > 1 ? 'Roles' : 'Role', (top.length ? top : u.roles || []).map((r) => r.name).join(', ')]);
      apply();
      if (can.has('updates.view')) update();
      else $('hero-state').textContent = `Welcome, ${name}.`;
      if (can.has('activity.view')) activity();
      if (can.has('seats.view') || can.has('sessions.view')) seats(); // the count and the waiting dot in the rail, and the page itself when it's open
      document.dispatchEvent(new CustomEvent('rabbidlab:me', { detail: u }));
      here();
      $('account-name').textContent = name;
      $('menu-name').textContent = name;
      $('menu-user').textContent = u.source === 'ad' ? `${u.username}@${domain}` : u.source === 'windows' ? u.username : `${s.product} account`;
      $('avatar').textContent = letters;
      $('avatar-big').textContent = letters;
      $('account-facts').replaceChildren(...rows.flatMap(([label, value, full]) => {
        const dt = document.createElement('dt');
        dt.textContent = label;
        const dd = document.createElement('dd');
        dd.textContent = value;
        if (full) dd.title = full;
        return [dt, dd];
      }));
      $('account').hidden = false;
      if (u.source === 'ad') $('hero-eyebrow').textContent = `${domain} · Homelab console`;
      startWhenQuiet();
    };
    // Asked again every 5 seconds until Rabbid Lab answers, so a failed first try doesn't leave the page empty.
    const start = async () => {
      let s;
      try {
        const r = await fetch('/api/signin', { headers: { Accept: 'application/json' } });
        if (!r.ok) throw new Error(r.statusText);
        s = await r.json();
      } catch {
        $('hero-state').textContent = "Can't reach Rabbid Lab. Trying again.";
        started();
        setTimeout(start, 5000);
        return;
      }
      signedIn(s);
    };
    start();
    const menu = (open) => {
      $('account-menu').hidden = !open;
      $('account-chip').setAttribute('aria-expanded', String(open));
    };
    $('account-chip').addEventListener('click', () => menu($('account-menu').hidden));
    document.addEventListener('click', (e) => { if (!$('account').contains(e.target)) menu(false); });
    document.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape' || $('account-menu').hidden) return;
      menu(false);
      $('account-chip').focus();
    });
    $('signout').addEventListener('click', async () => {
      await plainFetch('/api/signout', { method: 'POST', headers }).catch(() => {});
      location.replace('/signin.html');
    });
    status().catch(() => { $('st-facts').textContent = "Couldn't be read."; });
    $('viewas-stop').addEventListener('click', () => viewAs(null));
    $('here-connect').addEventListener('click', watchHere);
    $('here-later').addEventListener('click', () => {
      localStorage.setItem(LATER, String(Date.now() + 30 * 86400000));
      $('herebar').hidden = true;
    });
    // The update dialog goes away with Hide or Escape, and stays away until the update is over.
    $('updlg-hide').addEventListener('click', () => $('update-dialog').close());
    $('update-dialog').addEventListener('close', () => { if (!$('upd-installing').hidden) hid = true; });
    $('upd-check').addEventListener('click', (e) => check(e.currentTarget, $('upd-note')));
    $('hero-check').addEventListener('click', (e) => check(e.currentTarget, $('hero-note')));
    $('hero-install').addEventListener('click', () => post('/api/update/install'));
    $('upd-install').addEventListener('click', () => post('/api/update/install'));
    $('upd-auto').addEventListener('change', (e) => post('/api/update/settings', { automatic: e.target.checked }));
    // Seats: the filters, Let in and Remove on a computer, Sign out on a session, and the Add computers dialog.
    $('seats-filter').addEventListener('click', (e) => {
      const chip = e.target.closest('[data-filter]');
      if (!chip) return;
      seatFilter = chip.dataset.filter;
      $('seats-filter').querySelectorAll('[data-filter]').forEach((b) => b.setAttribute('aria-selected', String(b === chip)));
      drawSeats();
    });
    $('seats-table').addEventListener('click', (e) => {
      const row = e.target.closest('[data-client]');
      const c = row && computers.find((x) => x.id === row.dataset.client);
      if (!c) return;
      const act = async (what) => {
        try {
          await send(`/api/clients/${encodeURIComponent(c.id)}/${what}`);
          seatProblem('');
        } catch (err) {
          seatProblem(err.message);
        }
        seats();
      };
      if (e.target.matches('[data-approve]')) act('approve');
      else if (e.target.matches('[data-forget]')) sure(`Remove ${c.computer}?`, 'Its client will ask to join again and wait for approval.', 'Remove', () => act('remove'));
      else if (e.target.matches('[data-uninstall]')) openSeat(c);
    });
    // Uninstalling a seat's client: locked on the computer until it's allowed here, for an hour, or ordered from here.
    let seat = null;
    const drawSeat = () => {
      $('seat-title').textContent = `Uninstall the client on ${seat.computer}`;
      $('seat-allow-title').textContent = seat.removalUntil ? `Allowed until ${new Date(seat.removalUntil).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}` : 'Allow uninstalling for an hour';
      $('seat-allow').textContent = seat.removalUntil ? 'Lock again' : 'Allow';
      $('seat-now').disabled = !!seat.uninstalling;
      $('seat-now').textContent = seat.uninstalling ? 'Ordered' : 'Uninstall';
    };
    const openSeat = (c) => {
      seat = c;
      say('seat-problem', '');
      drawSeat();
      $('seat-dialog').showModal();
    };
    const removal = async (body) => {
      try {
        Object.assign(seat, await send(`/api/clients/${encodeURIComponent(seat.id)}/removal`, body));
        say('seat-problem', '');
      } catch (err) {
        say('seat-problem', err.message);
      }
      drawSeat();
      drawSeats();
    };
    $('seat-now').addEventListener('click', () => removal({ allow: true, now: true }));
    $('seat-allow').addEventListener('click', () => removal({ allow: !seat.removalUntil }));
    $('seat-close').addEventListener('click', () => $('seat-dialog').close());
    $('sessions-table').addEventListener('click', async (e) => {
      const row = e.target.closest('[data-session]');
      if (!row || !e.target.matches('[data-end]')) return;
      try {
        await send(`/api/sessions/${encodeURIComponent(row.dataset.session)}/signout`);
        seatProblem('');
      } catch (err) {
        seatProblem(err.message);
      }
      seats();
    });
    $('seats-add').addEventListener('click', async () => {
      $('add-dialog').showModal();
      try {
        joining = await get('/api/clients/settings');
      } catch (err) {
        $('get-client').textContent = `The install line couldn't be read (${err.message}). Close this and try again.`;
        return;
      }
      drawJoining();
      $('join-approve').checked = joining.approve;
    });
    $('line-copy').addEventListener('click', async () => {
      await copy($('install-line'), $('line-copy'));
      setTimeout(() => { $('line-copy').textContent = 'Copy'; }, 2000);
    });
    $('add-close').addEventListener('click', () => $('add-dialog').close());
    $('join-approve').addEventListener('change', (e) => send('/api/clients/settings', { approve: e.target.checked }).catch(() => {}));
    $('sure-cancel').addEventListener('click', () => $('sure-dialog').close());
    $('sure-go').addEventListener('click', async () => {
      $('sure-dialog').close();
      if (sureDo) await sureDo();
    });
    let lastPage = where()[0];
    addEventListener('hashchange', () => {
      const was = lastPage;
      lastPage = where()[0];
      show();
      // The Attribute browser keeps its place while you click through it.
      if (was !== 'attrs' || lastPage !== 'attrs') scrollTo(0, 0);
      if (lastPage === 'seats' && (can.has('seats.view') || can.has('sessions.view'))) seats();
      if (where()[1] === 'activity' && can.has('activity.view')) activity();
    });
    narrow.addEventListener('change', rail);
    $('rail-collapse').addEventListener('click', () => {
      localStorage.setItem('rabbidlab.rail', document.body.classList.contains('rail-small') ? 'wide' : 'small');
      rail();
    });
    document.addEventListener('visibilitychange', () => { if (!document.hidden && can.has('updates.view')) update(); });
  });

  // Groups under headings, for Links and Sensors: a group is renamed where it stands, dragged by its handle to another
  // place, or deleted. The page gives the list, the server's address for groups, and how to take an answer and draw.
  let makeGroup = null; // what New group's dialog does with the name it's given
  const groupTools = (list, o) => {
    let renaming = null; // the group whose name is being typed: its name and the text so far
    let dragging = null; // the group being dragged to another place
    let moving = null; // the link or sensor being dragged to another group: its id, and the group it came from
    const change = async (path, body) => {
      try {
        o.take(await send(path, body));
        say(o.problem, '');
        return true;
      } catch (err) {
        say(o.problem, err.message);
        return false;
      } finally {
        o.draw();
      }
    };
    // A group's heading: its name and count, and for people who may change it the handle, pencil and bin.
    const heading = (g, n) => {
      if (renaming?.name === g) {
        return `<div class="lhead"><input class="lrename" maxlength="40" value="${esc(renaming.text)}" aria-label="Name of the group">`
          + '<button type="button" class="small primary" data-rename-save>Save</button><button type="button" class="link" data-rename-cancel>Cancel</button></div>';
      }
      const tools = g && o.can()
        ? `<span class="ltools"><span class="lgrip" draggable="true" title="Drag to reorder">${svg('grip')}</span>`
          + `<button type="button" class="icon" data-rename="${esc(g)}" title="Rename" aria-label="Rename ${esc(g)}">${svg('edit')}</button>`
          + `<button type="button" class="icon" data-gdelete="${esc(g)}" title="Delete" aria-label="Delete ${esc(g)}">${svg('trash')}</button></span>` : '';
      return `<div class="lhead"><h2>${esc(g || 'No group')}<small>${n}</small></h2>${o.also?.(g, n) || ''}${tools}</div>`;
    };
    const rename = async () => {
      const { name, text } = renaming;
      renaming = null;
      if (!text.trim() || text.trim() === name) {
        o.draw();
        return;
      }
      if (await change(`${o.api}/rename`, { name, to: text })) o.renamed?.(name, text.trim());
    };
    list.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b) return;
      if (b.dataset.rename) {
        renaming = { name: b.dataset.rename, text: b.dataset.rename };
        o.draw();
      } else if ('renameSave' in b.dataset) {
        rename();
      } else if ('renameCancel' in b.dataset) {
        renaming = null;
        o.draw();
      } else if (b.dataset.gdelete) {
        const name = b.dataset.gdelete;
        const n = o.count(name);
        sure(`Delete the group ${name}?`, n ? `Its ${n === 1 ? `${o.thing} is` : `${n} ${o.thing}s are`} kept, with no group.` : `It has no ${o.thing}s.`, 'Delete group',
          () => change(`${o.api}/delete`, { name }));
      }
    });
    list.addEventListener('input', (e) => { if (e.target.classList.contains('lrename') && renaming) renaming.text = e.target.value; });
    list.addEventListener('keydown', (e) => {
      if (!e.target.classList.contains('lrename')) return;
      if (e.key === 'Enter') rename();
      if (e.key === 'Escape') {
        renaming = null;
        o.draw();
      }
    });
    // Dragging a group by its handle: a line shows where it lands, above or below the group under the pointer.
    const unmark = () => list.querySelectorAll('.drop-above, .drop-below').forEach((el) => el.classList.remove('drop-above', 'drop-below'));
    list.addEventListener('dragstart', (e) => {
      // A link or a sensor, dragged by its tile into another group or out of them all.
      const item = o.move && e.target.closest?.('[data-drag]');
      if (item) {
        moving = { id: item.dataset.drag, from: item.closest('.lgroup')?.dataset.name ?? '' };
        e.dataTransfer.effectAllowed = 'move';
        // No address goes with it: a link dragged to the edge of the window makes Chrome offer its split view.
        e.dataTransfer.clearData();
        e.dataTransfer.setData('application/x-rabbidlab-tile', moving.id);
        e.dataTransfer.setDragImage(item, 24, 24);
        item.classList.add('dragged');
        list.classList.add('moving');
        return;
      }
      const from = e.target.closest?.('.lgrip')?.closest('.lgroup');
      if (!from) return;
      dragging = from.dataset.name;
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', dragging);
      e.dataTransfer.setDragImage(from.querySelector('.lhead'), 0, 12);
      from.classList.add('dragged');
    });
    list.addEventListener('dragover', (e) => {
      if (moving) {
        const into = e.target.closest('.lgroup');
        if (!into) return;
        e.preventDefault();
        list.querySelectorAll('.drop-into, .drop-before, .drop-after').forEach((el) => el.classList.remove('drop-into', 'drop-before', 'drop-after'));
        // Over another tile, a line shows which side of it this one lands on (where the page keeps an order). Elsewhere in a group, it joins the end.
        const tile = o.places && e.target.closest('[data-drag]');
        if (tile && tile.dataset.drag !== moving.id) {
          const box = tile.getBoundingClientRect();
          const across = getComputedStyle(tile).display !== 'flex'; // tiles sit side by side, rows one over the other
          tile.classList.add((across ? e.clientX < box.left + box.width / 2 : e.clientY < box.top + box.height / 2) ? 'drop-before' : 'drop-after');
        } else if (into.dataset.name !== moving.from) into.classList.add('drop-into');
        return;
      }
      const over = e.target.closest('.lgroup');
      if (!dragging || !over?.dataset.name) return;
      e.preventDefault();
      unmark();
      if (over.dataset.name === dragging) return;
      const box = over.getBoundingClientRect();
      over.classList.add(e.clientY < box.top + box.height / 2 ? 'drop-above' : 'drop-below');
    });
    list.addEventListener('drop', (e) => {
      if (moving) {
        const into = e.target.closest('.lgroup');
        if (!into) return;
        e.preventDefault();
        const { id, from } = moving;
        moving = null;
        list.classList.remove('moving');
        // The group's tiles as they'll stand: this one beside the tile it was dropped on, or at the end.
        const ids = [...into.querySelectorAll('[data-drag]')].map((t) => t.dataset.drag).filter((x) => x !== id);
        const tile = o.places && e.target.closest('[data-drag]');
        if (tile && tile.dataset.drag !== id) ids.splice(ids.indexOf(tile.dataset.drag) + (tile.classList.contains('drop-after') ? 1 : 0), 0, id);
        else if (into.dataset.name === from) return o.draw(); // dropped where it came from, nothing changes
        else ids.push(id);
        o.move(id, into.dataset.name, ids);
        return;
      }
      const over = e.target.closest('.lgroup');
      if (!dragging || !over?.dataset.name || over.dataset.name === dragging) return;
      e.preventDefault();
      const names = o.names().filter((g) => g !== dragging);
      names.splice(names.indexOf(over.dataset.name) + (over.classList.contains('drop-below') ? 1 : 0), 0, dragging);
      dragging = null;
      // Shown in the new order straight away; the server's answer is the same order.
      o.order(names);
      change(`${o.api}/order`, { names });
    });
    list.addEventListener('dragend', () => {
      if (moving) {
        moving = null;
        list.classList.remove('moving');
        o.draw();
        return;
      }
      if (!dragging) return;
      dragging = null;
      o.draw();
    });
    return {
      heading,
      // Somewhere to drop a tile to take it out of its group, when the page shows nothing with no group. Seen only during a drag.
      out: (shown) => (o.move && o.can() && o.names().length && !shown
        ? `<section class="lgroup lout" data-name=""><div class="lhead"><h2>No group</h2></div><p class="hint lnone">Drop it here to take it out of its group.</p></section>` : ''),
      // A drag or a name being typed is under way: the page leaves the list alone until it's done.
      busy: () => !!(renaming || dragging || moving),
      // After a draw, the name being typed gets the cursor back.
      settle: () => {
        const box = renaming && list.querySelector('.lrename');
        if (!box) return;
        box.focus();
        box.setSelectionRange(box.value.length, box.value.length);
      },
      // New group: the dialog asks for the name, and says so itself when the server won't have it.
      create: () => {
        makeGroup = async (name) => {
          o.take(await send(o.api, { name }));
          o.draw();
        };
        $('lg-name').value = '';
        say('lg-problem', '');
        $('lgroup-dialog').showModal();
        $('lg-name').focus();
      },
    };
  };
  const saveGroup = async () => {
    try {
      await makeGroup?.($('lg-name').value);
      $('lgroup-dialog').close();
    } catch (err) {
      say('lg-problem', err.message);
    }
  };
  $('lg-save').addEventListener('click', saveGroup);
  $('lg-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') saveGroup(); });
  $('lg-cancel').addEventListener('click', () => $('lgroup-dialog').close());

  // What there is to know about this Rabbid Lab, as label and value: This machine on the Overview shows it and Support
  // info gives it as text to copy. Only what this person's roles let them see goes in, and nothing secret: no
  // passwords, addresses of sensors or names of people.
  const facts = async () => {
    const ask = async (path, allowed = true) => {
      if (!allowed) return null;
      try {
        return await get(path);
      } catch {
        return null;
      }
    };
    const [s, who, upd, dir, links, sensors, seats] = await Promise.all([ask('/api/status'), ask('/api/signin'), ask('/api/update/watch'), ask('/api/ad', has('ad.view') || has('ad.connect')),
      ask('/api/links', has('links.view')), ask('/api/sensors', has('sensors.view')), ask('/api/clients', has('seats.view'))]);
    const list = [];
    const add = (label, value) => { if (value != null && value !== '') list.push([label, value]); };
    const since = s ? Math.max(0, Date.now() - new Date(s.startedAt).getTime()) / 3600000 : 0;
    add('Product', s && `${s.product} ${s.version}${s.dev ? ' (dev build)' : ''}`);
    add('Machine', s?.server);
    add('Installed as', s?.platform);
    add('Windows / OS', s?.os);
    add('.NET', s?.dotNet);
    add('Running since', s && `${when(s.startedAt)} (${since < 1 ? `${Math.round(since * 60)} minutes` : since < 48 ? `${Math.round(since)} hours` : `${Math.round(since / 24)} days`})`);
    add('Data folder', s?.dataFolder);
    add('Sign-in', who && (who.domain ? `Active Directory, ${who.domain}${who.server ? ` (domain controller ${who.server})` : ''}` : 'Local accounts only'));
    add('Service account', dir && (dir.state ? `${dir.state.account} in ${dir.state.domain}, ${dir.state.made ? 'created by Rabbid Lab' : 'an existing account'}, scope ${dir.state.scopeName}` : 'Not set up'));
    add('Updates', upd && `${{ current: 'up to date', available: 'update available', downloading: 'downloading', installing: 'installing', failed: 'last update failed' }[upd.status] || upd.status}`
      + `${upd.available ? ` (${upd.available})` : ''}, checked ${upd.checkedAt ? when(upd.checkedAt) : 'never'}`);
    add('Seats', Array.isArray(seats) ? `${seats.length} computer${seats.length === 1 ? '' : 's'}, ${seats.filter((c) => c.online).length} online` : null);
    add('Links', links && `${links.links.length} in ${links.groups?.length || 0} group${links.groups?.length === 1 ? '' : 's'}`);
    add('Sensors', sensors && `${sensors.sensors.length}, ${sensors.sensors.filter((x) => x.status === 'down').length} down, email ${sensors.mail ? 'set up' : 'not set up'}`);
    add('Signed in as', meInfo && `${meInfo.source === 'ad' ? 'an Active Directory account' : meInfo.source === 'windows' ? 'a Windows account' : 'a local account'}, roles: ${(meInfo.roles || []).map((r) => r.name || r).join(', ') || 'none'}`);
    return list;
  };
  // Support info: the same, with where it was copied from, in a code block a chat keeps in columns.
  const about = async () => {
    const list = [...await facts(), ['Page', location.origin], ['Browser', navigator.userAgent.replace(/^Mozilla\/5\.0 /, '')], ['Copied', new Date().toString().replace(/ \(.*\)$/, '')]];
    return `\`\`\`\n${list.map(([label, value]) => `${`${label}:`.padEnd(17)}${value}`).join('\n')}\n\`\`\``;
  };
  // This machine, on the Overview: in two columns, filled each time the page is shown.
  const machine = async () => {
    const list = await facts();
    if (!list.length) return;
    const half = Math.ceil(list.length / 2);
    const column = (rows) => `<dl class="facts">${rows.map(([label, value]) => `<dt>${esc(label)}</dt><dd${label === 'Data folder' ? ' class="mono"' : ''}>${esc(value)}</dd>`).join('')}</dl>`;
    $('st-facts').innerHTML = column(list.slice(0, half)) + column(list.slice(half));
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'overview') machine(); });
  document.addEventListener('rabbidlab:me', () => { if (where()[0] === 'overview') machine(); });
  $('about-open').addEventListener('click', async () => {
    $('about-copy').textContent = 'Copy';
    $('about-text').textContent = 'Gathering…';
    $('about-dialog').showModal();
    $('about-text').textContent = await about();
  });
  $('about-copy').addEventListener('click', () => copy($('about-text'), $('about-copy')));
  $('about-close').addEventListener('click', () => $('about-dialog').close());
  // Settings, About: the same text, straight to the clipboard; where that isn't allowed, the dialog to copy it from.
  $('ab-copy').addEventListener('click', async () => {
    const b = $('ab-copy');
    b.disabled = true;
    try {
      await navigator.clipboard.writeText(await about());
      b.textContent = 'Copied';
      setTimeout(() => { b.textContent = 'Copy'; }, 2500);
    } catch {
      $('about-open').click();
    }
    b.disabled = false;
  });

  // What the other scripts use.
  window.RabbidLab = { $, esc, svg, say, initials, when, daysAgo, send, get, keep, forget, has, paint, where, viewAs, place, popups, option, dropdown, sure, copy, busy, groupTools, groupFilter, places,
    palette: window.RabbidLabColours.roles, me: () => meInfo };
})();
