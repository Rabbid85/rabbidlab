// Rabbid Lab's page: this machine (/api/status), where updating stands (/api/update, with Check for updates, Install now
// and the automatic switch), what's new, and the activity log (/api/activity). Changes carry the X-Rabbid-Lab header
// (Access.cs). Setup's What's new reads RELEASE_NOTES from this file. Each version's notes are here and, word for word,
// in CHANGELOG.md (ChangelogTests checks): newest first, each note one sentence first.
const RELEASE_NOTES = [
  ['1.1.40', [
    'One shortcut in the Start menu, named for the product, in place of the folder with Start, Stop and Restart. It checks Rabbid Lab is answering, starts the service if it is not (Windows asks for permission), then opens the console in the browser.',
    'The desktop shortcut does the same. The old Start menu folder and the old desktop shortcut are removed by the update.']],
  ['1.1.39', [
    'While a page loads, grey placeholders the shape of what is coming pulse where the content will be: tiles on Links and Sensors, rows on Seats, Roles and people, Active Directory, Local accounts, the Activity log and the lists in Settings, and blocks for the numbers in the banners.',
    'The lines that said Reading and Asking on Active Directory and Local accounts are gone; the placeholders say it. The flask still shows for things you start, like signing in, saving, importing and updating.']],
  ['1.1.38', [
    'Rabbid Lab Dev shows as Rabbid Lab with the DEV tag in the top bar and on the sign-in page, not Rabbid Lab Dev and the tag.']],
  ['1.1.37', [
    'The bar over the Activity log lines up: the find box, the person list, the day buttons and Export are all the same height and sit on one line. A long name no longer stretches the person list.']],
  ['1.1.36', [
    'The accent colour is on from the first moment the page shows, so a reload or an update no longer starts in orange. The look is kept in the browser as well as on the server.',
    'The accent now colours everything: the flask and its foam, the dot in the logo, the browser tab icon, the glow behind the banners, and the borders and tints that were still orange.',
    'The sign-in page wears the lab name, icon and accent too.',
    'Tidied up: the colour code is in one place for the console and the sign-in page, rules written twice in the stylesheet are merged, and unused styles are gone. On a phone, the banner sides are back to their narrow padding.']],
  ['1.1.35', [
    'Setting up the service account shows the flask while it signs in and while it creates the account. A wrong username or password outlines both fields in red and selects the password to type again.',
    'Problems everywhere show as a red box with a warning sign. They were a line of grey text.',
    'The service account window says first how creating one works: sign in once with a domain admin login, Rabbid Lab uses it to create the account, and the credentials are not saved.']],
  ['1.1.34', [
    'The Developer tools switch in Settings, Developer just says it turns on a few developer-only options.']],
  ['1.1.33', [
    'Banner squares moved from Settings, Appearance to Settings, Links, as Icon layout, with its own entry in the Links menu. It sets how the link icons sit in the banner on Links; Sensors keeps them evenly spread.']],
  ['1.1.32', [
    'A page you jump to from another one has a Back link over its title, naming where you came from: the gear on Links opens Settings with Back to Links, a card on Overview opens What is new with Back to Overview. Going somewhere with the sidebar has no Back link, and going back takes it away.']],
  ['1.1.31', [
    'The version in the top bar looks like the shortcuts beside it: a tag icon and plain grey text, with no box around it.']],
  ['1.1.30', [
    'The flask that shows while the page loads is bigger, and the top bar shortcuts wait until the page has loaded instead of filling in one by one.',
    'The footer with the version and the GitHub link is gone. The version is at the start of the shortcuts in the top bar.']],
  ['1.1.29', [
    'GitHub in the top bar is plain purple text, the same purple as the links on the page, with no box around it.',
    'The update window no longer has the line about the page reloading, and it stays the same size from start to finish.']],
  ['1.1.28', [
    'Updates, Activity, What is new and GitHub moved from the Overview banner to the top bar, on every page. GitHub is purple so it stands out. In a narrow window they show as icons.',
    'The Overview banner is the same size as the ones on Links and Sensors.']],
  ['1.1.27', [
    'Describe known products in Settings, Links is now Auto-fill descriptions, with plainer help: Rabbid Lab recognises common gear like TrueNAS, iDRAC, ESXi and UniFi and fills in what it is.']],
  ['1.1.26', [
    'Overview is back the way it was in 1.1.24: its tiles sit where they did and its banner is its old height. Banner squares and the shorter banner are for Links and Sensors only.']],
  ['1.1.25', [
    'The banner at the top of Overview, Links and Sensors is shorter, 300 pixels.',
    'Banner squares in Settings, Appearance picks how the squares sit in those banners: Scatter, Even spread, Cluster, Tidy rows, Sweep, Wave, Ring, Arc, Steps, Stack or Spiral. Even spread is the default, and the pick is kept in this browser.',
    'Over 40 links or sensors, the squares line up in a straight grid, every one of them, and a down one still glows red where it sits. The squares keep clear of the Down now card.',
    'Old code the banners no longer used is gone.']],
  ['1.1.24', [
    'The row of Updates, Activity, What is new and GitHub buttons is back on the Overview banner. The New card stays gone.']],
  ['1.1.23', [
    'Overview no longer has the row of Updates, Activity, What is new and GitHub buttons, or the New card on the right of its banner. The news cards below still show what is new.']],
  ['1.1.22', [
    'The banner at the top of Overview, Links and Sensors is tighter: the line under the title, the buttons and the numbers sit closer together, and the banner is shorter. Nothing in it changed size.']],
  ['1.1.21', [
    'The Red accent is a true red now, not a pink one. A lab that picked the old red gets the new one by itself.']],
  ['1.1.20', [
    'The Activity log is built for a big lab: grouped by day, the same change made over and over folded into one row that opens, and 50 at a time with Show 50 more.',
    'Its sub-menu shows one kind: sign-ins, people and accounts, links and sensors, seats, or updates and settings. The bar finds a person, computer or change, picks a person, narrows to today, 7 days, 30 days or all, and to problems only.',
    'Export saves what the log shows as a CSV file for Excel.']],
  ['1.1.19', [
    'The update window shows how far along the update is as a percentage, the same as Settings, Updates behind it.']],
  ['1.1.18', [
    'Appearance no longer has a Sidebar section. The Collapse button at the foot of the sidebar still shrinks it to icons.']],
  ['1.1.17', [
    'Support info and About are under their own Help heading in Settings. Advanced has only Developer and shows in Rabbid Lab Dev only, without the extra Dev label.']],
  ['1.1.16', [
    'The footer with the version lines up with the page above it when pages are centered, instead of sitting off to one side.']],
  ['1.1.15', [
    'Settings has headings with their pages under them. Updates, Links, Sensors and Seats show a flask and have a sub-menu: the page shows all of its sections, and a sub-entry shows only that one.',
    'Settings pages are laid out like Discord: a big title, then each section with its settings as rows, the value, switch or button on the right.',
    'New pages: Account, See it as a role, Support info and About. Alert settings and saved credentials moved to Settings, Sensors, and New computers and the client install line to Settings, Seats.',
    'Appearance sets the lab name, its icon and the accent colour for everyone, with Layout and Sidebar just for you. A new permission, Change the lab name, icon and accent colour, decides who can change them.',
    'The full stop in the Rabbid Lab logo is square now, in the favicon, the sign-in page and the Windows icons too.']],
  ['1.1.14', [
    'Layout moved into the list down the left of Settings, under its own heading like filters: Pages, Centered or Full width, and Sidebar, names and icons or icons only. The Appearance page is gone.']],
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
  // Settings' pages and what each needs; About is for everyone, Developer for Rabbid Lab Dev only.
  const SUBS = { account: '', appearance: '', viewas: 'access.view', updates: 'updates.view', notifications: 'sensors.alerts', activity: 'activity.view',
    links: 'links.edit', sensors: 'sensors.alerts sensors.edit', seats: 'seats.settings seats.add', ad: 'ad.connect', developer: 'updates.auto', support: '', about: '' };
  const subOk = (s) => s in SUBS && (!SUBS[s] || !shown || allows(SUBS[s])) && (s !== 'developer' || document.body.classList.contains('is-dev'));
  const pageOk = (key) => !shown || !PAGES[key] || allows(PAGES[key]);
  const home = () => ['overview', 'links', 'sensors', 'ad', 'seats', 'people', 'local'].find(pageOk) || 'wiki';
  const firstSetting = () => Object.keys(SUBS).find(subOk) || 'account';

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
  // What the page showing is called: a Settings page by its own name.
  const pageName = () => (page().id === 'page-settings' ? section().dataset.title : page().dataset.title);
  const title = () => { document.title = `${pageName()} · ${product}`; };
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
    const [, , rest] = where();
    const one = now.id === 'page-settings' && rest[0] && (sub.querySelector(`.ds-sec[data-sec="${CSS.escape(rest[0])}"]:not(.cant)`) || (sub.dataset.filters || '').split(' ').includes(rest[0])) ? rest[0] : '';
    document.querySelectorAll('.srail .dkids').forEach((k) => k.classList.toggle('open', k.dataset.kids === sub.dataset.sub));
    document.querySelectorAll('.srail a[data-sub]').forEach((a) => {
      if (a.dataset.sub === sub.dataset.sub && (a.dataset.sec || '') === one) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
    sub.querySelectorAll('.ds-sec[data-sec]').forEach((s) => { s.hidden = !!one && s.dataset.sec !== one; });
    sub.classList.toggle('one-sec', !!one);
    // Active Directory's pages are listed down the left of whichever of them is open, with that page's views (users.js).
    const dir = now.classList.contains('dir-area');
    if (now.dataset.page !== 'ad') $('dir-rail').classList.remove('lists'); // Find, views and filters are for the pages that list things
    if (dir) {
      now.querySelector('.page-head').after($('dir-rail'));
      $('rail-dir').setAttribute('aria-current', 'page');
      mark(document.querySelectorAll('#dir-rail a[data-adpage]'), 'adpage', now.dataset.page === 'ad' ? where()[2][0] || 'users' : now.dataset.page);
    }
    // Back to where you jumped from, at the top of the page: in its banner, or over its title.
    const backlink = $('backlink');
    backlink.hidden = !back;
    if (back) {
      backlink.href = back.hash;
      $('backlink-text').textContent = `Back to ${back.name}`;
      (now.querySelector(':scope > .hero, :scope > .page-head') || now).prepend(backlink);
    }
    title();
    document.dispatchEvent(new CustomEvent('rabbidlab:show', { detail: where() }));
  };
  // Where you jumped here from: another page, by a link in it (a gear, a card, a shortcut in the top bar). Going there by
  // the sidebar or the logo is moving across, not in, so it has no way back; and going back clears it.
  let back = null;
  let fromSidebar = false;
  document.addEventListener('click', (e) => { fromSidebar = !!e.target.closest('.rail a, .brand'); }, true);
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
  };
  // Layout, in the Settings list: pages in a column in the middle, or the whole window.
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
  layout();
  rail();
  // Find a setting: the pages whose name or settings have the words, and their groups' headings.
  $('set-find').addEventListener('input', () => {
    const q = $('set-find').value.trim().toLowerCase();
    const nav = $('set-find').closest('nav');
    let any = false;
    nav.querySelectorAll(':scope > a[data-sub]').forEach((a) => {
      const page = document.querySelector(`.sub[data-sub="${a.dataset.sub}"]`);
      a.hidden = !!q && !`${a.textContent} ${page ? page.textContent : ''}`.toLowerCase().includes(q);
      if (!a.hidden && a.offsetParent) any = true;
    });
    nav.classList.toggle('finding', !!q);
    nav.querySelectorAll(':scope > .rail-label').forEach((p) => {
      let el = p.nextElementSibling;
      let show = false;
      for (; el && !el.classList.contains('rail-label') && el.tagName !== 'P'; el = el.nextElementSibling) if (el.tagName === 'A' && !el.hidden && !el.classList.contains('cant')) show = true;
      p.hidden = !show;
    });
    $('set-none').hidden = any || !q;
  });
  $('set-find').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    const a = [...$('set-find').closest('nav').querySelectorAll(':scope > a[data-sub]')].find((x) => !x.hidden && x.offsetParent);
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
  // The lab's own look, the same for everyone (Settings, Appearance): its name, its icon, and the accent colour. Nothing set:
  // Rabbid Lab's own. brand.js keeps the colours and the tab's icon; here are the name, the mark in the top bar, and Appearance.
  const Brand = window.RabbidLabBrand;
  let brand = Brand.now; // the look last seen in this browser, until the server's is read
  let productName = 'Rabbid Lab';
  const drawBrand = () => {
    product = brand.name || productName;
    $('brand-name').textContent = product.replace(/ Dev$/, ''); // the DEV tag beside it says the rest
    $('hero-title').textContent = product;
    title();
    $('brand-img').hidden = !brand.icon;
    document.querySelector('.brand svg.mark').classList.toggle('gone', !!brand.icon);
    if (brand.icon) $('brand-img').src = brand.icon;
    // Settings, Appearance.
    if (document.activeElement !== $('br-name')) $('br-name').value = brand.name || '';
    $('br-icon').innerHTML = brand.icon ? '<img alt="">' : document.querySelector('.brand svg.mark').outerHTML.replace(' gone', '');
    if (brand.icon) $('br-icon').querySelector('img').src = brand.icon;
    const may = has('appearance.brand');
    const at = Brand.swatch(brand.accent);
    $('br-swatches').innerHTML = Brand.SWATCHES.map(([nm, , m], i) => `<button type="button" class="ds-sw" data-accent="${i}" data-color="${m}" title="${nm}" aria-pressed="${at === i}"${may ? '' : ' disabled'}></button>`).join('')
      + `<label class="ds-sw custom${may ? '' : ' off'}" title="Any colour" aria-pressed="${at === -1}"><input type="color" id="br-custom" value="${Brand.shades(brand.accent)[1] || Brand.SWATCHES[0][2]}"${may ? '' : ' disabled'}></label>`;
    paint($('br-swatches'));
    $('br-name').disabled = !may;
    document.querySelectorAll('.br-edit').forEach((b) => b.classList.toggle('cant', !may));
    $('br-cant').hidden = may;
  };
  const saveBrand = async (change) => {
    try {
      brand = Brand.apply(await send('/api/brand', change));
      say('br-problem', '');
    } catch (err) {
      say('br-problem', err.message);
    }
    drawBrand();
  };
  $('br-name').addEventListener('change', () => saveBrand({ name: $('br-name').value }));
  $('br-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('br-name').blur(); });
  $('br-swatches').addEventListener('click', (e) => {
    const b = e.target.closest('[data-accent]');
    if (b) saveBrand({ accent: b.dataset.accent === '0' ? '' : Brand.SWATCHES[b.dataset.accent][2] });
  });
  $('br-swatches').addEventListener('change', (e) => { if (e.target.id === 'br-custom') saveBrand({ accent: e.target.value }); });
  $('br-file').addEventListener('change', () => {
    const f = $('br-file').files[0];
    $('br-file').value = '';
    if (!f) return;
    if (f.size > 256 * 1024) {
      say('br-problem', 'Keep the icon under 256 KB.');
      return;
    }
    const read = new FileReader();
    read.onload = () => saveBrand({ icon: read.result });
    read.readAsDataURL(f);
  });
  $('br-reset').addEventListener('click', () => saveBrand({ icon: '' }));
  document.addEventListener('rabbidlab:me', () => drawBrand());
  const loadBrand = async () => {
    try {
      brand = Brand.apply(await get('/api/brand'));
    } catch {
      // The look last seen in this browser stays.
    }
    drawBrand();
  };
  const status = async () => {
    const r = await fetch('/api/status', { headers: { Accept: 'application/json' } });
    if (!r.ok) throw new Error(r.statusText);
    const s = await r.json();
    productName = s.product;
    drawBrand();
    $('rail-version').textContent = s.version;
    $('rail-repo').href = s.repository;
    $('top-version').textContent = s.version;
    document.body.classList.toggle('is-dev', !!s.dev);
    if (s.dev) devTools();
    loadBrand();
    $('hero-version').textContent = s.version;
    $('hero-where').textContent = `Running on ${s.server} since ${when(s.startedAt)}.`;
    $('ab-version').textContent = `${s.product} ${s.version}`;
    $('ab-server').textContent = s.server;
    $('ab-since').textContent = when(s.startedAt);
    $('ab-repo').href = s.repository;
    $('ab-repo-text').textContent = s.repository.replace(/^https?:\/\//, '');
    $('top-repo').href = s.repository;
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
    $('updlg-hide').style.visibility = phase === 'done' ? 'hidden' : ''; // keeps its room, so the window doesn't shrink when it's done
    if (!hid && !$('update-dialog').open) $('update-dialog').showModal();
    const p = `${Math.min(100, Math.round(pct))}%`;
    $('inst-fill').style.width = p;
    $('updlg-fill').style.width = p;
    $('inst-pct').textContent = p;
    $('updlg-pct').textContent = p;
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

  // Settings, Activity log: found on the server, newest first, by day, and a run of the same change by the same person
  // folded into one row that opens. The sub-menu picks a kind; the bar narrows by words, person, time and problems.
  const AL_KINDS = { signins: ['Sign-ins', 'user', '#6FA8FF'], accounts: ['People and accounts', 'users', '#B07CE8'], lab: ['Links and sensors', 'link', '#3CC7C7'],
    seats: ['Seats', 'monitor', '#F2BE6B'], admin: ['Updates and settings', 'updates', '#FF8A3D'] };
  const AREA_KIND = { 'sign-in': 'signins', access: 'accounts', users: 'accounts', links: 'lab', sensors: 'lab', clients: 'seats', admin: 'admin' };
  const al = { q: '', who: '', days: 7, problems: false, count: 50, open: new Set(), page: null };
  const alKind = () => (where()[0] === 'settings' && where()[1] === 'activity' && AL_KINDS[where()[2][0]] ? where()[2][0] : '');
  const alQuery = (count) => {
    const p = new URLSearchParams();
    if (alKind()) p.set('kind', alKind());
    if (al.who) p.set('by', al.who);
    if (al.q) p.set('q', al.q);
    if (al.days) {
      const d = new Date();
      if (al.days === 1) d.setHours(0, 0, 0, 0);
      else d.setDate(d.getDate() - al.days);
      p.set('since', d.toISOString());
    }
    if (al.problems) p.set('problems', 'true');
    if (count) p.set('count', count);
    return p.toString();
  };
  const dayOf = (iso) => {
    const d = new Date(iso);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const ago = Math.round((today - new Date(iso).setHours(0, 0, 0, 0)) / 86400000);
    return ago === 0 ? 'Today' : ago === 1 ? 'Yesterday' : d.toLocaleDateString([], { weekday: 'long', month: 'short', day: 'numeric', year: d.getFullYear() === today.getFullYear() ? undefined : 'numeric' });
  };
  const alRow = (g, kid) => {
    const k = AREA_KIND[g.e.area] || 'admin';
    const [label, icon, colour] = AL_KINDS[k];
    const n = g.items ? g.items.length : 1;
    const key = `${g.e.at}|${g.e.title}`;
    const sub = [g.e.by, g.e.area === 'sign-in' && g.e.from ? `from ${g.e.from}` : label, g.e.detail].filter(Boolean).join(' · ');
    return `<div class="al-row${kid ? ' kid' : ''}"><span class="al-ic" data-color="${colour}">${svg(icon)}</span><span class="al-t"><b>${esc(g.e.title)}</b><span>${esc(sub)}</span></span>`
      + `<span class="al-r">${g.e.result !== 'ok' ? `<span class="al-bad">${g.e.result === 'warning' ? 'Warning' : 'Failed'}</span>` : ''}`
      + `${n > 1 ? `<button type="button" class="al-x" data-al-open="${esc(key)}" aria-expanded="${al.open.has(key)}" title="The same ${n} times in a row">×${n}</button>` : ''}`
      + `<span title="${esc(when(g.e.at))}">${new Date(g.e.at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</span></span></div>`
      + (n > 1 && al.open.has(key) ? g.items.map((x) => alRow({ e: x }, true)).join('') : '');
  };
  const alDraw = () => {
    const page = al.page;
    if (!page) return;
    const kind = alKind();
    const range = { 1: 'today', 7: 'in the last 7 days', 30: 'in the last 30 days', 0: 'in all' }[al.days];
    $('al-count').innerHTML = `<b>${page.total}</b> ${range}${kind ? ` · ${AL_KINDS[kind][0]}` : ''}${page.problems ? ` · <b>${page.problems}</b> ${page.problems === 1 ? 'problem' : 'problems'}` : ''}`;
    const groups = page.entries.reduce((out, e) => {
      const last = out[out.length - 1];
      if (last && last.e.title === e.title && last.e.by === e.by && new Date(last.items[last.items.length - 1].at) - new Date(e.at) < 600000) last.items.push(e);
      else out.push({ e, items: [e] });
      return out;
    }, []);
    let day = '';
    $('activity-list').innerHTML = groups.length ? groups.map((g) => {
      const d = dayOf(g.e.at);
      const head = d !== day ? `<p class="al-day">${esc(d)}</p>` : '';
      day = d;
      return head + alRow(g);
    }).join('') : '<p class="ds-empty">Nothing matches.</p>';
    paint($('activity-list'));
    const left = page.total - page.entries.length;
    $('al-more').innerHTML = left > 0 ? `<button type="button" class="ds-btn" data-al-more>Show 50 more</button><span>${left} older</span>` : '';
    document.querySelectorAll('#al-range [data-days]').forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.days) === al.days)));
    document.querySelectorAll('#al-res [data-problems]').forEach((b) => b.setAttribute('aria-pressed', String(!!b.dataset.problems === al.problems)));
  };
  const activity = async () => {
    try {
      al.page = await get(`/api/activity/find?${alQuery(al.count)}`);
    } catch (err) {
      $('activity-list').innerHTML = `<p class="ds-empty">${esc(err.message)}</p>`;
      return;
    }
    $('al-who').innerHTML = `<option value="">Anyone</option>${al.page.people.map((p) => `<option${p === al.who ? ' selected' : ''}>${esc(p)}</option>`).join('')}`;
    $('al-export').href = `/api/activity/export?${alQuery(0)}`;
    alDraw();
  };
  const alAgain = () => {
    al.count = 50;
    activity();
  };
  let alTyping = null;
  $('al-q').addEventListener('input', () => {
    clearTimeout(alTyping);
    alTyping = setTimeout(() => { al.q = $('al-q').value.trim(); alAgain(); }, 250);
  });
  $('al-who').addEventListener('change', () => { al.who = $('al-who').value; alAgain(); });
  $('al-range').addEventListener('click', (e) => { const b = e.target.closest('[data-days]'); if (b) { al.days = Number(b.dataset.days); alAgain(); } });
  $('al-res').addEventListener('click', (e) => { const b = e.target.closest('[data-problems]'); if (b) { al.problems = !!b.dataset.problems; alAgain(); } });
  $('al-more').addEventListener('click', (e) => { if (e.target.closest('[data-al-more]')) { al.count += 50; activity(); } });
  $('activity-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-al-open]');
    if (!b) return;
    if (al.open.has(b.dataset.alOpen)) al.open.delete(b.dataset.alOpen);
    else al.open.add(b.dataset.alOpen);
    alDraw();
  });

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
    + '<linearGradient id="flask-ember" gradientUnits="userSpaceOnUse" x1="0" y1="21" x2="0" y2="55"><stop offset="0" class="flask-a"/><stop offset="1" class="flask-b"/></linearGradient>'
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
  // Grey placeholders the shape of what's loading, pulsing, until a page's first read is back: tiles for Links and Sensors,
  // rows for lists. What's loaded takes their place when it's drawn; no kind takes them away if they're still there.
  const SKELETON = {
    tiles: (i) => `<div class="sk-tile"><i class="sk sq"></i><span class="sk-lines"><i class="sk w${[45, 60, 30, 75][i % 4]}"></i><i class="sk w${[75, 45, 60, 45][i % 4]}"></i><i class="sk w30"></i></span></div>`,
    rows: (i) => `<div class="sk-row"><i class="sk sq"></i><span class="sk-lines"><i class="sk w${[45, 60, 30, 75][i % 4]}"></i><i class="sk w${[75, 45, 60, 45][i % 4]}"></i></span><i class="sk end"></i></div>`,
  };
  const skeleton = (el, kind = 'rows', count = 6) => {
    if (!kind) {
      if (el.firstElementChild?.classList.contains('sk-wrap')) el.replaceChildren();
      return;
    }
    const items = Array.from({ length: count }, (_, i) => SKELETON[kind](i)).join('');
    el.innerHTML = `<div class="sk-wrap" aria-hidden="true">${kind === 'tiles' ? `<i class="sk sk-head"></i><div class="sk-tiles">${items}</div>` : items}</div>`;
  };
  // A page's lists start as placeholders: data-skel="rows:6" on the list says what and how many.
  document.querySelectorAll('[data-skel]').forEach((el) => {
    const [kind, count] = el.dataset.skel.split(':');
    skeleton(el, kind, Number(count) || 6);
  });
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
    // Settings, Updates, and the news cards: the newest versions, each with its first note.
    const [newest, first] = RELEASE_NOTES[0];
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

  // --- Banner squares: a square for each link and each sensor, over the right of the hero on Links and Sensors -------------------
  // On Links they sit as each person picks (Settings, Links, Icon layout, kept in this browser); on Sensors, evenly. Over 40, a grid.
  const SQUARES = { scatter: 'Scatter', even: 'Even spread', cluster: 'Cluster', rows: 'Tidy rows', sweep: 'Sweep', wave: 'Wave', ring: 'Ring',
    arc: 'Arc', steps: 'Steps', stack: 'Stack', spiral: 'Spiral' };
  const squaresPicked = () => {
    try {
      const s = localStorage.getItem('rabbidlab.squares');
      return s in SQUARES ? s : 'even';
    } catch {
      return 'even';
    }
  };
  // A number from 0 to 1 for some text, the same every time.
  const mix = (text) => {
    let h = [...String(text)].reduce((n, c) => Math.imul(n ^ c.charCodeAt(0), 16777619), 2166136261);
    h = Math.imul(h ^ (h >>> 16), 0x85ebca6b);
    h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967295;
  };
  // Where n squares about size px wide go in a box w by h: the middle of each, in px from the box's top left.
  const arrange = (style, n, w, h, size) => {
    const at = [];
    const pad = size * 0.6;
    const jig = (i, k) => mix(`${style}${i}${k}`) - 0.5;
    if (style === 'scatter') {
      // Spare spots in loose rows, mixed the same way every time, so nothing lands on top of anything else.
      const cols = Math.max(1, Math.ceil(Math.sqrt((n * 1.7 * w) / h)));
      const rows = Math.max(1, Math.ceil((n * 1.7) / cols));
      const spots = [];
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) spots.push([((c + 0.5 + jig(r * cols + c, 'x') * 0.5) / cols) * w, ((r + 0.5 + jig(r * cols + c, 'y') * 0.5) / rows) * h]);
      }
      spots.sort((a, b) => mix(a.join()) - mix(b.join()));
      at.push(...spots.slice(0, n));
    } else if (style === 'even') {
      const rows = Math.max(1, Math.round(Math.sqrt(n / (w / h))));
      const cell = w / Math.ceil(n / rows);
      let i = 0;
      for (let r = 0; r < rows; r++) {
        const count = Math.round((n - i) / (rows - r));
        const start = (w - count * cell) / 2 + (r % 2 ? cell : -cell) * 0.25;
        for (let c = 0; c < count; c++, i++) at.push([start + (c + 0.5 + jig(i, 'x') * 0.3) * cell, ((r + 0.5 + jig(i, 'y') * 0.3) / rows) * h]);
      }
    } else if (style === 'cluster') {
      const ry = h / 2 - pad;
      for (let i = 0; i < n; i++) {
        const r = Math.sqrt((i + 0.5) / n);
        at.push([w / 2 + Math.cos(i * 2.39996) * r * Math.min(ry * 1.35, w / 2 - pad), h / 2 + Math.sin(i * 2.39996) * r * ry]);
      }
    } else if (style === 'rows') {
      const cols = Math.max(1, Math.min(n, Math.floor(w / (size * 1.9))));
      const rows = Math.ceil(n / cols);
      const cell = (w - pad) / cols;
      for (let i = 0; i < n; i++) {
        const r = Math.floor(i / cols);
        at.push([w - pad / 2 - (cols - (i % cols) - 0.5 - (r % 2) * 0.5) * cell, ((r + 0.5) / Math.max(rows, 3)) * h]);
      }
    } else if (style === 'sweep') {
      for (let i = 0; i < n; i++) {
        const t = n === 1 ? 0.5 : i / (n - 1);
        const y = h - pad - t * (h - 2 * pad) - Math.sin(t * Math.PI) * h * 0.14 + (i % 2 ? 1 : -1) * h * 0.07;
        at.push([pad + t * (w - 2 * pad), Math.min(h - pad, Math.max(pad, y))]);
      }
    } else if (style === 'wave') {
      const rows = n > 20 ? 2 : 1;
      let i = 0;
      for (let r = 0; r < rows; r++) {
        const count = Math.round((n - i) / (rows - r));
        for (let c = 0; c < count; c++, i++) {
          const t = count === 1 ? 0.5 : c / (count - 1);
          at.push([pad + t * (w - 2 * pad), (h * (r + 0.5)) / rows + Math.sin(t * Math.PI * 2 + r * Math.PI) * h * (rows === 1 ? 0.28 : 0.14)]);
        }
      }
    } else if (style === 'ring') {
      const outer = n > 18 ? Math.ceil(n * 0.62) : n;
      const ry = h / 2 - pad;
      const rx = Math.min(w / 2 - pad, ry * 2.2);
      [[outer, 1, 0], [n - outer, 0.55, 0.5]].forEach(([count, f, shift]) => {
        for (let k = 0; k < count; k++) {
          const a = -Math.PI / 2 + ((k + shift) / count) * Math.PI * 2;
          at.push([w / 2 + Math.cos(a) * rx * f, h / 2 + Math.sin(a) * ry * f]);
        }
      });
    } else if (style === 'arc') {
      const outer = n > 16 ? Math.ceil(n * 0.58) : n;
      const ry = h - 2 * pad;
      const rx = Math.min(w / 2 - pad, ry * 1.9);
      [[outer, 1], [n - outer, 0.58]].forEach(([count, f]) => {
        for (let k = 0; k < count; k++) {
          const a = Math.PI - ((k + 0.5) / count) * Math.PI;
          at.push([w / 2 + Math.cos(a) * rx * f, h - pad - Math.sin(a) * ry * f]);
        }
      });
    } else if (style === 'steps') {
      // Columns rising from left to right, from the bottom up.
      const most = Math.max(1, Math.floor(h / (size * 1.35)));
      const tall = (c, cols) => 1 + Math.floor((c * most) / cols);
      let cols = 1;
      while (Array.from({ length: cols }, (_, c) => tall(c, cols)).reduce((a, b) => a + b, 0) < n) cols++;
      for (let c = 0, i = 0; c < cols && i < n; c++) {
        for (let k = 0; k < tall(c, cols) && i < n; k++, i++) at.push([((c + 0.5) * w) / cols, h - ((k + 0.5) * h) / most]);
      }
    } else if (style === 'stack') {
      // A pyramid: rows one wider each, the bottom one short of any it doesn't need.
      const rows = Math.max(1, Math.min(Math.floor(h / (size * 1.25)), Math.ceil(Math.sqrt(2 * n))));
      const top = Math.max(1, Math.ceil((n - (rows * (rows - 1)) / 2) / rows));
      const widths = Array.from({ length: rows }, (_, r) => top + r);
      widths[rows - 1] -= widths.reduce((a, b) => a + b, 0) - n;
      const cy = h / rows;
      const cx = Math.min(w / Math.max(...widths), cy * 1.2);
      widths.forEach((count, r) => { for (let k = 0; k < count; k++) at.push([w / 2 + (k - (count - 1) / 2) * cx, (r + 0.5) * cy]); });
    } else if (style === 'spiral') {
      const ry = h / 2 - pad;
      const rx = Math.min(w / 2 - pad, ry * 1.7);
      const most = 2.2 * Math.PI * 2;
      for (let i = 0; i < n; i++) {
        const t = most * Math.sqrt((i + 0.6) / n);
        at.push([w / 2 + Math.cos(t) * (t / most) * rx, h / 2 + Math.sin(t) * (t / most) * ry]);
      }
    }
    return at;
  };
  // How big a square is for this many on a page; over 40 the grid sizes its own.
  const squareSize = (n) => (n <= 20 ? 34 : n <= 30 ? 30 : 26);
  // A hero's squares ([{ el }], in a steady order) and the card in its corner they keep clear of, put in place again whenever it
  // changes size: when its page shows, when the window does.
  const laid = new Map();
  const layOut = (hero) => {
    const now = laid.get(hero);
    const W = hero.clientWidth;
    const H = hero.clientHeight;
    if (!now || !now.items.length || !W || !H) return;
    const { items, card, picked } = now;
    const n = items.length;
    const left = W * 0.56;
    const right = card && !card.hidden && card.offsetWidth ? card.offsetLeft - 28 : W * 0.95;
    const top = H * 0.12;
    const aw = right - left;
    const ah = H * 0.86 - top;
    if (aw < 40 || ah < 40) return;
    const put = (el, x, y) => {
      el.style.left = `${((x / W) * 100).toFixed(2)}%`;
      el.style.top = `${((y / H) * 100).toFixed(2)}%`;
    };
    if (n > 40) {
      let cols = Math.ceil(Math.sqrt((n * aw) / ah));
      let rows = Math.ceil(n / cols);
      while (rows * (aw / cols) > ah) rows = Math.ceil(n / ++cols);
      const cell = Math.min(aw / cols, ah / rows);
      const side = Math.max(4, Math.round(cell * 0.78));
      const x0 = left + (aw - cols * cell) / 2 + (cell - side) / 2;
      const y0 = top + (ah - rows * cell) / 2 + (cell - side) / 2;
      items.forEach(({ el }, i) => {
        el.classList.add('gridded');
        el.classList.toggle('tiny', side < 20);
        Object.assign(el.style, { width: `${side}px`, height: `${side}px`, rotate: '0deg' });
        put(el, x0 + (i % cols) * cell, y0 + Math.floor(i / cols) * cell);
      });
      return;
    }
    const sides = items.map(({ el }) => parseFloat(el.style.width) || el.offsetWidth);
    const spots = arrange(picked ? squaresPicked() : 'even', n, aw, ah, Math.max(...sides));
    items.forEach(({ el }, i) => {
      el.classList.remove('gridded');
      put(el, left + spots[i][0] - sides[i] / 2, top + spots[i][1] - sides[i] / 2);
    });
  };
  const resized = new ResizeObserver((seen) => seen.forEach((e) => layOut(e.target)));
  // picked: they sit as the person chose in Icon layout, rather than evenly.
  const squares = (hero, items, card = null, picked = false) => {
    if (!laid.has(hero)) resized.observe(hero);
    laid.set(hero, { items, card, picked });
    layOut(hero);
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

  // Settings, Links, Icon layout: each choice drawn with 13 dots where it puts them.
  const drawSquaresPicker = () => {
    const COLOURS = window.RabbidLabColours.tiles;
    const picked = squaresPicked();
    $('set-squares').replaceChildren(...Object.entries(SQUARES).map(([key, name]) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'sq-pick';
      b.dataset.squares = key;
      b.setAttribute('aria-pressed', String(key === picked));
      const pic = document.createElement('span');
      pic.className = 'sq-pic';
      const box = document.createElement('span');
      arrange(key, 13, 200, 84, 14).forEach(([x, y], i) => {
        const dot = document.createElement('i');
        dot.dataset.color = COLOURS[i % COLOURS.length];
        Object.assign(dot.style, { left: `${x / 2}%`, top: `${(y / 84) * 100}%`, rotate: `${Math.round(mix(`${key}${i}`) * 20 - 10)}deg` });
        box.append(dot);
      });
      pic.append(box);
      const label = document.createElement('b');
      label.textContent = name;
      b.append(pic, label);
      return b;
    }));
    paint($('set-squares'));
  };
  $('set-squares').addEventListener('click', (e) => {
    const b = e.target.closest('[data-squares]');
    if (!b) return;
    try { localStorage.setItem('rabbidlab.squares', b.dataset.squares); } catch { /* this visit only */ }
    $('set-squares').querySelectorAll('[data-squares]').forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    laid.forEach((_, hero) => layOut(hero));
  });

  document.addEventListener('DOMContentLoaded', () => {
    notes();
    drawBrand(); // the lab's name and icon as this browser last saw them, before the server answers
    scatter();
    drawSquaresPicker();
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
      $('acc-name').textContent = name;
      $('acc-user').textContent = u.username;
      $('acc-how').textContent = u.source === 'ad' ? `Active Directory · ${u.username}@${domain}` : u.source === 'windows' ? `Windows · ${u.username}` : `${s.product} account`;
      $('acc-roles').textContent = (u.roles || []).map((r) => r.name).join(', ');
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
    let lastHash = location.hash || `#${lastPage}`;
    let lastName = pageName();
    addEventListener('hashchange', () => {
      const was = lastPage;
      lastPage = where()[0];
      if (fromSidebar || (back && back.hash === location.hash)) back = null;
      else if (lastPage !== was) back = { hash: lastHash, name: lastName };
      fromSidebar = false;
      show();
      lastHash = location.hash;
      lastName = pageName();
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
  $('sup-copy').addEventListener('click', async () => {
    await copy($('sup-text'), $('sup-copy'));
    setTimeout(() => { $('sup-copy').innerHTML = `${svg('csv')}Copy`; }, 2500);
  });
  $('acc-signout').addEventListener('click', () => $('signout').click());
  let viewRoles = [];
  $('va-start').addEventListener('click', () => {
    const r = viewRoles.find((x) => x.id === $('va-role').value);
    if (!r) return;
    const user = viewRoles.find((x) => x.id === 'user');
    viewAs({ name: r.name }, r.id === 'admin' ? r.permissions : [...new Set([...(user ? user.permissions : []), ...r.permissions])]);
    location.hash = '#overview';
  });
  $('seat-approve').addEventListener('change', async (e) => {
    $('seat-approve-label').textContent = e.target.checked ? 'On' : 'Off';
    try {
      await send('/api/clients/settings', { approve: e.target.checked });
      say('seat-set-problem', '');
    } catch (err) {
      say('seat-set-problem', err.message);
    }
  });
  $('seat-line-copy').addEventListener('click', async () => {
    await copy($('seat-line'), $('seat-line-copy'));
    setTimeout(() => { $('seat-line-copy').textContent = 'Copy'; }, 2000);
  });
  $('seat-fp-copy').addEventListener('click', async () => {
    await copy($('seat-fp'), $('seat-fp-copy'));
    setTimeout(() => { $('seat-fp-copy').textContent = 'Copy'; }, 2000);
  });
  document.addEventListener('rabbidlab:show', async (e) => {
    const [pg, sub] = e.detail;
    if (pg !== 'settings') return;
    if (sub === 'support') $('sup-text').textContent = await about();
    if (sub === 'viewas' && can.has('access.view')) {
      try {
        viewRoles = (await get('/api/roles')).roles || [];
        $('va-role').innerHTML = viewRoles.filter((r) => r.id !== 'user').map((r) => `<option value="${esc(r.id)}">${esc(r.name)}</option>`).join('');
        say('va-problem', '');
      } catch (err) {
        say('va-problem', err.message);
      }
    }
    if (sub === 'seats') {
      try {
        joining = await get('/api/clients/settings');
        say('seat-set-problem', '');
      } catch (err) {
        say('seat-set-problem', err.message);
        return;
      }
      $('seat-approve').checked = !!joining.approve;
      $('seat-approve-label').textContent = joining.approve ? 'On' : 'Off';
      const line = [`msiexec /i RabbidLab-Client-${joining.version}.msi /qn SERVER=${location.origin}`];
      if (joining.fingerprint) line.push(`FINGERPRINT=${joining.fingerprint}`);
      $('seat-line').textContent = line.join(' ');
      $('seat-msi-row').hidden = !joining.msi;
      $('seat-policy-row').hidden = !joining.policy;
      $('seat-fp-row').hidden = !joining.fingerprint;
      $('seat-fp').textContent = joining.fingerprint || '';
    }
    if (sub === 'updates' && can.has('activity.view')) {
      try {
        const list = (await get('/api/activity?count=1000')).filter((a) => a.action === 'update.install').slice(0, 12);
        $('up-history').innerHTML = list.length ? list.map((a) => `<div class="ds-row"><div class="ds-l"><b>${esc(a.title.replace(/^[^:]*: /, ''))}</b>${a.result === 'ok' ? '' : `<span>${esc(a.detail || "Didn't install.")}</span>`}</div>`
          + `<div class="ds-r"><span class="ds-v">${esc(a.by)} · ${esc(when(a.at))}</span></div></div>`).join('') : '<p class="ds-empty">No updates installed yet.</p>';
      } catch {
        skeleton($('up-history'), ''); // the line above still says where they're kept
      }
    }
  });

  // What the other scripts use.
  window.RabbidLab = { $, esc, svg, say, initials, when, daysAgo, send, get, keep, forget, has, paint, where, viewAs, place, popups, option, dropdown, sure, copy, busy, groupTools, groupFilter, squares, squareSize, skeleton,
    palette: window.RabbidLabColours.roles, me: () => meInfo };
})();
