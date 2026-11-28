// Connected apps (ConnectedApps.cs), a part of the sidebar of their own: the apps, and the access rules that give their
// groups and roles to the members of Active Directory groups. An app opens in the side panel: Access (what each rule
// gives there), Accounts (how each matches an AD user, and the fix when one doesn't), Provisioning and Activity. A rule
// gives one or several groups and roles in each app. Review changes lists every difference before anything is changed.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, panel, toast, sure, get, send, count, listed: list, none, oops, closeX } = R;
  const some = (xs, more = 2) => (xs.length <= more ? list(xs) : `${xs.slice(0, more).join(', ')} and ${xs.length - more} more`);
  const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right ? `<span class="dw-sec-do">${right}</span>` : ''}</div>${body}</section>`;
  const onPage = () => R.where()[0] === 'apps';
  const MATCH = { email: 'Email address', username: 'Username', hand: 'By hand' };
  const MARK = { create: '+', add: '+', drift: '−', blocked: '!' };

  let data = null; // /api/apps
  let tab = 'apps';
  let pane = 'access'; // the open app's tab in the panel
  let show = 'all'; // which accounts the Accounts tab lists
  const picked = new Set();
  const may = () => !!data?.canManage;
  const appOf = (id) => data?.apps.find((a) => a.id === id);
  const ruleOf = (id) => data?.rules.find((r) => r.id === id);
  const groupOf = (a, id) => a?.groups.find((g) => g.id === id);
  const n = (a, state) => a.accounts.filter((x) => x.state === state).length;
  const stateOf = (a) => (a.problem ? ['Sync failed', 'red'] : a.paused ? ['Paused', 'grey'] : n(a, 'torn') ? ['Needs attention', 'amber'] : ['Connected', 'green']);
  const rulesFor = (a) => data.rules.filter((r) => r.to.some((t) => t.app === a.id));
  const planFor = (test) => data.plan.filter(test);
  // What a rule gives in each app, as words: Help desk: Agents and Editors.
  const gives = (r) => data.apps.map((a) => [a, r.to.filter((t) => t.app === a.id).map((t) => groupOf(a, t.group)?.name).filter(Boolean)]).filter(([, names]) => names.length);
  const tile = (a, cls = '') => `<span class="pic ${cls}">${esc(R.initials(a.name))}</span>`;

  const load = async () => {
    try {
      const was = JSON.stringify(data);
      data = await get('/api/apps');
      R.say('ap-problem', '');
      if (JSON.stringify(data) !== was) draw();
    } catch (e) {
      R.say('ap-problem', e.message);
      if (!data) { data = { apps: [], rules: [], plan: [], kinds: [], canManage: false }; draw(); }
    }
  };

  // --- The lists -------------------------------------------------------------------------------------------------
  let GA = null;
  let GR = null;
  const tick = (id, name) => `<label class="tk"><input type="checkbox" data-ap-tick="${esc(id)}" aria-label="Select ${esc(name)}"${picked.has(id) ? ' checked' : ''}></label>`;
  const lead = { w: 18, head: '<label class="tk"><input type="checkbox" data-ap-tick-all aria-label="Select all"></label>' };
  const appsGrid = () => (GA ||= R.grid({
    el: $('ap-list'), key: 'apps', sort: { key: 'name', dir: 1 }, lead: { ...lead, cell: (a) => tick(a.id, a.name) },
    cols: [
      { key: 'name', label: 'Name', w: 220, fixed: true, get: (a) => a.name, cell: (a) => `<div class="who">${tile(a)}<b class="ell">${esc(a.name)}</b></div>` },
      { key: 'kind', label: 'Connected through', w: 170, on: true, get: (a) => a.kindName },
      { key: 'state', label: 'Status', w: 140, on: true, get: (a) => stateOf(a)[0], cell: (a) => `<span class="ubadge ${stateOf(a)[1]}">${stateOf(a)[0]}</span>` },
      { key: 'matched', label: 'Accounts matched', w: 150, on: true, get: (a) => n(a, 'matched'), text: (a) => `${n(a, 'matched')} of ${a.accounts.length - n(a, 'np')}`,
        cell: (a) => `<span class="num">${n(a, 'matched')} of ${a.accounts.length - n(a, 'np')}</span>` },
      { key: 'groups', label: 'Groups and roles', w: 140, on: true, get: (a) => a.groups.length, cell: (a) => `<span class="num">${a.groups.length}</span>` },
      { key: 'rules', label: 'Access rules', w: 120, on: true, get: (a) => rulesFor(a).length, cell: (a) => `<span class="num">${rulesFor(a).length}</span>` },
      { key: 'sync', label: 'Last sync', w: 150, on: true, get: (a) => (a.lastSync ? new Date(a.lastSync).getTime() : 0), text: (a) => (a.lastSync ? R.when(a.lastSync) : 'Never'),
        cell: (a) => `<span class="muted">${esc(a.lastSync ? R.when(a.lastSync) : 'Never')}</span>` },
    ],
    row: (a) => ` data-ap-row="${esc(a.id)}"`, empty: () => 'No apps are connected yet. Select Connect an app to add one.', loading: () => !data, redraw: () => draw(),
  }));
  const waiting = (r, kinds) => planFor((i) => i.rule === r.id && kinds.includes(i.kind)).length;
  const rulesGrid = () => (GR ||= R.grid({
    el: $('ap-rules'), key: 'apprules', sort: { key: 'ad', dir: 1 }, lead: { ...lead, cell: (r) => tick(r.id, r.adName) },
    cols: [
      { key: 'ad', label: 'Members of', w: 200, fixed: true, get: (r) => r.adName, cell: (r) => `<div class="who"><span class="pic">${svg('users')}</span><b class="ell">${esc(r.adName)}</b></div>` },
      { key: 'gives', label: 'Get', w: 360, on: true, get: (r) => gives(r).map(([a, names]) => `${a.name}: ${names.join(', ')}`).join(' · '),
        cell: (r) => `<span class="ell">${esc(gives(r).map(([a, names]) => `${a.name}: ${list(names)}`).join(' · '))}</span>` },
      { key: 'mode', label: 'Given', w: 140, on: true, get: (r) => (r.mode === 'auto' ? 'Automatically' : 'After approval') },
      { key: 'people', label: 'People', w: 90, on: true, get: (r) => r.people, cell: (r) => `<span class="num">${r.people}</span>` },
      { key: 'wait', label: 'Waiting', w: 100, on: true, get: (r) => waiting(r, ['add', 'create']), cell: (r) => (waiting(r, ['add', 'create']) ? `<span class="ubadge amber">${waiting(r, ['add', 'create'])}</span>` : '<span class="muted">None</span>') },
      { key: 'hand', label: 'Given by hand', w: 130, on: true, get: (r) => waiting(r, ['drift']), cell: (r) => (waiting(r, ['drift']) ? `<span class="ubadge amber">${waiting(r, ['drift'])}</span>` : '<span class="muted">None</span>') },
    ],
    row: (r) => ` data-ap-row="${esc(r.id)}"`, empty: () => 'No access rules yet. Select Add a rule, or Give access on an app.', loading: () => !data, redraw: () => draw(),
  }));

  const drawHero = () => {
    if (!data) return;
    const bad = data.apps.filter((a) => stateOf(a)[1] === 'red' || stateOf(a)[1] === 'amber').length;
    const people = new Set(data.apps.flatMap((a) => a.accounts.map((x) => x.user).filter(Boolean))).size;
    $('ap-state').textContent = !data.apps.length ? 'No apps are connected.' : bad ? `${count(bad, 'app needs', 'apps need')} attention.` : `${count(data.apps.length, 'app is', 'apps are')} connected.`;
    $('ap-n-apps').textContent = data.apps.length;
    $('ap-n-people').textContent = people;
    $('ap-n-diff').textContent = data.plan.length;
    $('ap-n-bad').textContent = bad;
    $('rail-apps').textContent = data.apps.length || '';
  };
  const draw = () => {
    drawHero();
    if (!onPage() || !data) return;
    const rules = tab === 'rules';
    $('ap-tabs').querySelectorAll('[data-ap-tab]').forEach((a) => (a.dataset.apTab === tab ? a.setAttribute('aria-current', 'true') : a.removeAttribute('aria-current')));
    $('ap-t-apps').textContent = data.apps.length;
    $('ap-t-rules').textContent = data.rules.length;
    $('ap-list').hidden = rules;
    $('ap-rules').hidden = !rules;
    $('ap-new').hidden = !may() || rules;
    $('ap-rule').hidden = !may() || !rules || !data.apps.length;
    $('ap-acts').hidden = !may();
    const todo = planFor((i) => i.kind !== 'blocked').length;
    $('ap-review').hidden = !data.plan.length;
    $('ap-review').querySelector('.cnt').hidden = !todo;
    $('ap-review').querySelector('.cnt').textContent = todo;
    if (rules) rulesGrid().draw(data.rules); else appsGrid().draw(data.apps);
    $('ap-count').textContent = rules ? count(data.rules.length, 'rule', 'rules') : count(data.apps.length, 'app', 'apps');
    drawTicks();
    if (panel.is('app')) { const a = appOf(panel.key); if (a) openApp(a, true); else panel.close(); }
    if (panel.is('aprule')) { const r = ruleOf(panel.key); if (r) openRule(r, true); else panel.close(); }
  };
  const shown = () => (tab === 'rules' ? data?.rules : data?.apps) || [];
  const drawTicks = () => {
    const el = $(tab === 'rules' ? 'ap-rules' : 'ap-list');
    [...el.querySelectorAll('[data-ap-tick]')].forEach((b) => { b.checked = picked.has(b.dataset.apTick); b.closest('.tr')?.classList.toggle('picked', b.checked); });
    const k = shown().filter((x) => picked.has(x.id)).length;
    $('ap-acts').disabled = !k;
    $('ap-acts').querySelector('.cnt').hidden = !k;
    $('ap-acts').querySelector('.cnt').textContent = k;
  };
  const rowClick = (e) => {
    const tk = e.target.closest('.tk');
    if (tk) {
      const box = tk.querySelector('input');
      if (e.target !== box) return;
      if (box.dataset.apTickAll !== undefined) shown().forEach((x) => (box.checked ? picked.add(x.id) : picked.delete(x.id)));
      else if (box.checked) picked.add(box.dataset.apTick);
      else picked.delete(box.dataset.apTick);
      drawTicks();
      return;
    }
    const row = e.target.closest('[data-ap-row]');
    if (!row) return;
    const kind = tab === 'rules' ? 'aprule' : 'app';
    if (panel.shows(kind, row.dataset.apRow)) panel.close();
    else if (tab === 'rules') openRule(ruleOf(row.dataset.apRow));
    else { pane = 'access'; openApp(appOf(row.dataset.apRow)); }
  };
  $('ap-list').addEventListener('click', rowClick);
  $('ap-rules').addEventListener('click', rowClick);
  $('ap-tabs').addEventListener('click', (e) => {
    const a = e.target.closest('[data-ap-tab]');
    if (!a || a.dataset.apTab === tab) return;
    tab = a.dataset.apTab;
    picked.clear();
    if (panel.is('app') || panel.is('aprule')) panel.close();
    draw();
  });
  $('ap-acts').addEventListener('click', () => {
    const xs = shown().filter((x) => picked.has(x.id));
    R.actions.open($('ap-acts'), tab === 'rules'
      ? { items: [{ act: 'delete', label: 'Delete…', hint: String(xs.length), danger: true }], pick: () => removeRules(xs) }
      : { items: [{ act: 'sync', label: 'Sync now' }, { act: 'pause', label: 'Pause sync' }, { act: 'resume', label: 'Resume sync' }, '-', { act: 'remove', label: 'Remove…', hint: String(xs.length), danger: true }],
        pick: (act) => (act === 'sync' ? sync(xs) : act === 'remove' ? removeApps(xs) : pause(xs, act === 'pause')) });
  });

  // --- What an app does -------------------------------------------------------------------------------------------
  const sync = async (xs) => {
    toast(xs.length === 1 ? `Syncing ${xs[0].name}` : `Syncing ${xs.length} apps`, 'Reading accounts, groups and roles…', { stay: true });
    const failed = [];
    for (const a of xs) {
      try {
        const r = await send(`/api/apps/${a.id}/sync`, {});
        if (!r.ok) failed.push(`${a.name}: ${r.problem}`);
      } catch (e) {
        failed.push(`${a.name}: ${e.message}`);
      }
    }
    await load();
    if (failed.length) toast('Sync failed.', failed.join(' '), { tone: 'warn' });
    else toast('Sync completed.', xs.length === 1 ? `${xs[0].name} is up to date.` : `${xs.length} apps are up to date.`);
  };
  const pause = async (xs, on) => {
    try {
      for (const a of xs) await send(`/api/apps/${a.id}/pause`, { on });
      toast(on ? 'Sync paused.' : 'Sync resumed.', list(xs.map((a) => a.name)));
    } catch (e) {
      oops(e);
    }
    await load();
  };
  const removeApps = (xs) => sure(xs.length === 1 ? `Remove ${xs[0].name}?` : `Remove ${xs.length} apps?`,
    `${R.app()} stops reading ${xs.length === 1 ? 'the app' : 'these apps'} and deletes the saved secret. Nothing is changed in the app. Access rules stop giving its groups and roles.`, 'Remove', async () => {
      try {
        for (const a of xs) await send(`/api/apps/${a.id}/delete`, {});
        toast(xs.length === 1 ? 'App removed.' : `${xs.length} apps removed.`, list(xs.map((a) => a.name)));
        picked.clear();
        if (panel.is('app')) panel.close();
      } catch (e) {
        oops(e);
      }
      await load();
    });
  const removeRules = (xs) => sure(xs.length === 1 ? `Delete the rule for ${xs[0].adName}?` : `Delete ${xs.length} rules?`,
    'Nobody loses access now. The groups and roles stop following the Active Directory group.', 'Delete', async () => {
      try {
        for (const r of xs) await send(`/api/apps/rules/${r.id}/delete`, {});
        toast(xs.length === 1 ? 'Rule deleted.' : `${xs.length} rules deleted.`, 'Nobody lost access in any app.');
        picked.clear();
        if (panel.is('aprule')) panel.close();
      } catch (e) {
        oops(e);
      }
      await load();
    });

  // --- An app in the side panel -------------------------------------------------------------------------------
  const ACC = { matched: ['Matched', 'green'], none: ['Not matched', 'amber'], torn: ['Two accounts', 'red'], np: ['Not a person', 'grey'] };
  const accessPane = (a) => {
    const row = (g) => {
      const from = g.rules.length ? `Given to members of ${list(g.rules)}` : `Not given by a rule`;
      return `<div class="mrow"><span class="pic small">${svg('users')}</span><div class="two"><b>${esc(g.name)}</b><small>${esc(`${g.type} · ${count(g.members.length, a.account, `${a.account}s`)} · ${from}`)}</small></div>`
        + `${may() && !g.rules.length ? `<button type="button" class="small" data-ap-give="${esc(g.id)}">${svg('link')}Give by a rule</button>` : ''}</div>`;
    };
    const given = a.groups.filter((g) => g.rules.length);
    const rest = a.groups.filter((g) => !g.rules.length);
    const diff = planFor((i) => i.app === a.id);
    return (diff.length ? `<p class="hint ap-diff">${esc(`${count(diff.length, 'difference', 'differences')} from the rules in ${a.name}.`)}<button type="button" class="dw-link" data-ap-review-app>Review changes</button></p>` : '')
      + section(`Given by access rules <small>${given.length}</small>`, given.length ? `<div class="dw-list">${given.map(row).join('')}</div>` : none(`No rule gives a ${a.group} in ${a.name} yet.`),
        may() ? `<button type="button" class="small" data-ap-give="">${svg('plus')}Give access</button>` : '')
      + section(`Not given by a rule <small>${rest.length}</small>`, rest.length ? `<div class="dw-list">${rest.map(row).join('')}</div>` : none(`Every ${a.group} is given by a rule.`));
  };
  const accountsPane = (a) => {
    const kinds = [['all', 'All', a.accounts.length], ['matched', 'Matched', n(a, 'matched')], ['none', 'Not matched', n(a, 'none')], ['torn', 'Two accounts', n(a, 'torn')], ['np', 'Not people', n(a, 'np')]];
    const rows = a.accounts.filter((x) => show === 'all' || x.state === show);
    const fix = (x) => {
      if (!may()) return '';
      if (x.state === 'none') return `<button type="button" class="small" data-ap-match="${esc(x.id)}">Match to…</button>`;
      if (x.state === 'torn') return `<button type="button" class="small" data-ap-use="${esc(x.id)}">${svg('check')}Use this one</button>`;
      if (x.state === 'np' || x.byHand) return `<button type="button" class="dw-link" data-ap-auto="${esc(x.id)}">Match automatically</button>`;
      return '';
    };
    return `<div class="seg ap-show" role="group" aria-label="Show">${kinds.map(([k, l, c]) => `<button type="button" data-ap-show="${k}" aria-pressed="${show === k}">${l}<small>${c}</small></button>`).join('')}</div>`
      + section(`${a.account[0].toUpperCase()}${a.account.slice(1)}s <small>${rows.length}</small>`, rows.length ? `<div class="dw-list">${rows.map((x) => `<div class="mrow${x.active ? '' : ' ap-off'}"><span class="pic small">${svg(x.user ? 'user' : 'key')}</span>`
        + `<div class="two"><b>${esc(x.userName && x.userName !== x.name ? `${x.userName} (${x.name || x.id})` : x.name || x.id)}</b><small>${esc([x.email || x.login || x.id, x.active ? '' : 'Disabled in the app', x.byHand ? 'Matched by hand' : ''].filter(Boolean).join(' · '))}</small></div>`
        + `<span class="ubadge ${ACC[x.state][1]}">${ACC[x.state][0]}</span>${fix(x)}</div>`).join('')}</div>` : none('No accounts here.'));
  };
  const provisionPane = (a) => section('Matching', `<div class="ap-set"><span>Match accounts to Active Directory users by</span><div class="seg" role="group" aria-label="Match accounts by">`
      + `${Object.entries(MATCH).map(([k, l]) => `<button type="button" data-ap-matchby="${k}" aria-pressed="${a.match === k}"${may() ? '' : ' disabled'}>${l}</button>`).join('')}</div></div>`
      + `<p class="hint">${esc(a.match === 'hand' ? 'Nothing is matched until you match each account under Accounts.' : `An account whose ${a.match === 'email' ? 'email address' : 'username'} is the same as an Active Directory user's is that user's. Two accounts for one user wait until you pick one.`)}</p>`)
    + section('Accounts', `<label class="ap-set"><span>Make ${esc(a.account === 'account' ? 'an' : 'a')} ${esc(a.account)} for someone a rule gives access who has none</span>`
      + `<input type="checkbox" class="switch" role="switch" data-ap-create${a.create ? ' checked' : ''}${may() && a.canCreate ? '' : ' disabled'}></label>`
      + `<p class="hint">${esc(a.create ? 'Without one, the person is listed as blocked and gets nothing.' : `People without ${a.account === 'account' ? 'an' : 'a'} ${a.account} are listed as blocked until one is made in the app.`)}</p>`)
    + section('When access ends', `<ul class="does wide"><li><b>An account left in no ${esc(a.group)}s</b><span>Disabled in ${esc(a.name)}</span></li><li><b>A ${esc(a.group)} given by hand in the app</b><span>Reported under Review changes. Nothing is taken away until you apply it.</span></li></ul>`);
  const activityPane = (a) => section(`Activity <small>${a.log.length}</small>`, a.log.length ? `<div class="dw-list">${a.log.map((l) => `<div class="mrow"><span class="pic small">${svg(l.result === 'failed' ? 'alert' : 'check')}</span>`
    + `<div class="two"><b class="ap-wrap">${esc(l.text)}</b><small>${esc(`${R.when(l.at)} · ${l.by}`)}</small></div>${l.result === 'failed' ? '<span class="ubadge red">Failed</span>' : ''}</div>`).join('')}</div>` : none('Nothing yet.'));
  const openApp = (a, again = false) => {
    const [state, tone] = stateOf(a);
    const tabs = [['access', 'Access', a.groups.length], ['accounts', 'Accounts', a.accounts.length], ['provision', 'Provisioning', null], ['activity', 'Activity', a.log.length]];
    const body = { access: accessPane, accounts: accountsPane, provision: provisionPane, activity: activityPane }[pane](a);
    const where = a.settings.url || a.settings.domain || a.settings.folder || '';
    const scroll = panel.body.scrollTop;
    panel.body.innerHTML = `<div class="dw-head">${tile(a, 'dw-pic')}<div class="dw-name"><h2 id="dw-title">${esc(a.name)}</h2><p>${esc([a.kindName, where].filter(Boolean).join(' · '))}</p></div>`
      + `${closeX}</div>`
      + `<div class="dw-bar">${may() ? R.editButton('data-ap-edit', '') : ''}<button type="button" class="dw-qa" data-ap-sync${may() ? '' : ' disabled'}>${svg('refresh')}<span>Sync now</span></button>`
      + (may() ? `<button type="button" class="dw-qa" data-ap-pause>${svg(a.paused ? 'play' : 'pause')}<span>${a.paused ? 'Resume sync' : 'Pause sync'}</span></button><button type="button" class="dw-qa" data-ap-remove>${svg('trash')}<span>Remove</span></button>` : '') + '</div>'
      + (a.problem ? `<p class="hint problem" role="alert">${esc(`The last sync failed: ${a.problem}`)}</p>` : '')
      + `<div class="dw-strip"><div><span class="l">Status</span><span class="v"><span class="ubadge ${tone}">${state}</span></span></div><div><span class="l">Last sync</span><span class="v">${esc(a.lastSync ? R.when(a.lastSync) : 'Never')}</span></div>`
      + `<div><span class="l">Accounts matched</span><span class="v">${n(a, 'matched')} of ${a.accounts.length - n(a, 'np')}</span></div><div><span class="l">Matched by</span><span class="v">${MATCH[a.match]}</span></div></div>`
      + `<div class="tabs2 dw-tabs" role="tablist">${tabs.map(([k, l, c]) => `<a role="tab" data-ap-pane="${k}"${pane === k ? ' aria-current="true"' : ''}>${l}${c == null ? '' : `<small>${c}</small>`}</a>`).join('')}</div>`
      + `<div class="dw-panel">${body}</div>`;
    if (again) panel.body.scrollTop = scroll;
    else panel.open('app', a.id);
  };
  // Match to…: the people in Active Directory to pick from, or not a person.
  const matchTo = async (anchor, a, id) => {
    let users = [];
    try {
      users = ((await R.keep('/api/ad/users', 60000))?.users || []).filter((u) => !u.system && !u.service).sort((x, y) => (x.name || x.username).localeCompare(y.name || y.username));
    } catch {
      // Only Not a person, then.
    }
    R.picker.menu(anchor, [{ label: 'Not a person', icon: 'x', run: () => pick(a, id, '-') }, '-',
      ...users.map((u) => ({ label: u.name ? `${u.name} (${u.username})` : u.username, run: () => pick(a, id, u.username) }))], { key: `ap-match:${id}`, right: true });
  };
  const pick = async (a, account, user) => {
    try {
      await send(`/api/apps/${a.id}/pick`, { account, user });
    } catch (e) {
      oops(e);
    }
    await load();
  };
  panel.el.addEventListener('click', async (e) => {
    let b;
    if (panel.is('app')) {
      const a = appOf(panel.key);
      if (!a) return;
      if ((b = e.target.closest('[data-ap-pane]'))) { pane = b.dataset.apPane; openApp(a, true); }
      else if ((b = e.target.closest('[data-ap-show]'))) { show = b.dataset.apShow; openApp(a, true); }
      else if (e.target.closest('[data-ap-edit]')) openConnect(a);
      else if (e.target.closest('[data-ap-sync]')) sync([a]);
      else if (e.target.closest('[data-ap-pause]')) pause([a], !a.paused);
      else if (e.target.closest('[data-ap-remove]')) removeApps([a]);
      else if ((b = e.target.closest('[data-ap-give]'))) openGive({ app: a.id, group: b.dataset.apGive });
      else if (e.target.closest('[data-ap-review-app]')) openReview((i) => i.app === a.id, a.name);
      else if ((b = e.target.closest('[data-ap-match]'))) matchTo(b, a, b.dataset.apMatch);
      else if ((b = e.target.closest('[data-ap-use]'))) {
        // This one is theirs; the other account that claims them is nobody's until it's matched to someone else.
        const mine = a.accounts.find((x) => x.id === b.dataset.apUse);
        const key = (x) => (a.match === 'username' ? x.login : x.email).toLowerCase();
        const user = ((await R.keep('/api/ad/users', 60000))?.users || []).find((u) => (a.match === 'username' ? u.username : u.email || '').toLowerCase() === key(mine));
        if (!user) { toast('Could not match the account.', 'The user it belongs to was not found in Active Directory.', { tone: 'warn' }); return; }
        for (const other of a.accounts.filter((x) => x.state === 'torn' && x.id !== mine.id && key(x) === key(mine))) await send(`/api/apps/${a.id}/pick`, { account: other.id, user: '-' });
        pick(a, mine.id, user.username);
      } else if ((b = e.target.closest('[data-ap-auto]'))) pick(a, b.dataset.apAuto, null);
      else if ((b = e.target.closest('[data-ap-matchby]')) && b.dataset.apMatchby !== a.match) setApp(a, { match: b.dataset.apMatchby });
    } else if (panel.is('aprule')) {
      const r = ruleOf(panel.key);
      if (!r) return;
      if (e.target.closest('[data-ap-redit]')) openGive({ rule: r });
      else if (e.target.closest('[data-ap-rdel]')) removeRules([r]);
      else if (e.target.closest('[data-ap-rreview]')) openReview((i) => i.rule === r.id, r.adName);
      else if ((b = e.target.closest('[data-ap-keep]'))) {
        const i = data.plan.find((x) => x.key === b.dataset.apKeep);
        try {
          await send('/api/apps/keep', { rule: i.rule, app: i.app, group: i.group, account: i.account });
          toast('Kept.', `${i.userName} keeps ${i.groupName} in ${i.appName}.`);
        } catch (err) {
          oops(err);
        }
        await load();
      }
    }
  });
  panel.el.addEventListener('change', (e) => {
    if (panel.is('app') && e.target.matches('[data-ap-create]')) setApp(appOf(panel.key), { create: e.target.checked });
  });
  const setApp = async (a, change) => {
    try {
      await send(`/api/apps/${a.id}/settings`, change);
    } catch (e) {
      oops(e);
    }
    await load();
  };

  // --- A rule in the side panel -----------------------------------------------------------------------------------
  const planRow = (i, act = '') => `<div class="mrow"><span class="ap-mark ${i.kind}">${MARK[i.kind]}</span><div class="two"><b class="ap-wrap">${esc(i.text)}</b><small>${esc(`${i.appName} · ${i.why}`)}</small></div>${act}</div>`;
  const openRule = (r, again = false) => {
    const mine = planFor((i) => i.rule === r.id);
    const wait = mine.filter((i) => i.kind === 'add' || i.kind === 'create');
    const hand = mine.filter((i) => i.kind === 'drift');
    const blocked = mine.filter((i) => i.kind === 'blocked');
    const scroll = panel.body.scrollTop;
    panel.body.innerHTML = `<div class="dw-head"><span class="pic dw-pic">${svg('link')}</span><div class="dw-name"><h2 id="dw-title">${esc(`Members of ${r.adName}`)}</h2><p>${esc(`Access rule · ${count(r.people, 'person', 'people')} · given ${r.mode === 'auto' ? 'automatically' : 'after approval'}`)}</p></div>`
      + `${closeX}</div>`
      + (may() ? `<div class="dw-bar">${R.editButton('data-ap-redit', '')}<button type="button" class="dw-qa" data-ap-rdel>${svg('trash')}<span>Delete</span></button></div>` : '')
      + section(`What they get <small>${r.to.length}</small>`, `<div class="dw-list">${gives(r).map(([a, names]) => `<div class="mrow">${tile(a, 'small')}<div class="two"><b>${esc(list(names))}</b><small>${esc(`${a.name} · ${count(names.length, a.group, `${a.group}s`)}`)}</small></div></div>`).join('')}</div>`
        + '<p class="hint">Someone who leaves the Active Directory group keeps these until you review and apply the change.</p>')
      + section(`Waiting to be given <small>${wait.length}</small>`, wait.length ? `<div class="dw-list">${wait.map((i) => planRow(i)).join('')}</div>` : none('Everyone in the group has what the rule gives.'),
        may() && wait.length ? `<button type="button" class="small" data-ap-rreview>${svg('list')}Review changes</button>` : '')
      + (hand.length ? section(`Given by hand <small>${hand.length}</small>`, `<div class="dw-list">${hand.map((i) => planRow(i, may() ? `<button type="button" class="dw-link" data-ap-keep="${esc(i.key)}">Keep</button>` : '')).join('')}</div>`,
        may() ? `<button type="button" class="small" data-ap-rreview>${svg('list')}Review changes</button>` : '') : '')
      + (blocked.length ? section(`Blocked <small>${blocked.length}</small>`, `<div class="dw-list">${blocked.map((i) => planRow(i)).join('')}</div>`) : '');
    if (again) panel.body.scrollTop = scroll;
    else panel.open('aprule', r.id);
  };

  // --- Connect an app, or change its connection --------------------------------------------------------------------
  let draft = null; // { id, kind, name, match, create }
  const drawConnect = () => {
    const edit = !!draft.id;
    const kind = data.kinds.find((k) => k.kind === draft.kind);
    $('ap-connect-title').textContent = edit ? `Edit ${draft.name}` : 'Connect an app';
    $('ap-connect-lead').textContent = edit ? 'The app is read once with these settings before they are saved. Leave a secret empty to keep the one saved.'
      : `${R.app()} reads the accounts, groups and roles in the app and matches each account to an Active Directory user. Nothing changes in the app until you give access with a rule.`;
    const field = (x) => {
      const v = draft.settings[x.key] || '';
      const input = x.long ? `<textarea data-ap-f="${esc(x.key)}" rows="4" spellcheck="false" placeholder="${esc(edit && x.secret ? 'Saved. Paste a new one to replace it.' : x.hint)}"></textarea>`
        : `<input data-ap-f="${esc(x.key)}" type="${x.secret ? 'password' : 'text'}" value="${esc(x.secret ? '' : v)}" placeholder="${esc(edit && x.secret ? 'Saved. Enter a new one to replace it.' : x.hint)}" autocomplete="off" spellcheck="false">`;
      return `<label class="pf-v${x.long ? ' wide' : ''}"><span>${esc(x.label)}</span>${input}</label>`;
    };
    $('ap-connect-body').innerHTML = (edit ? '' : `<div class="ap-kinds">${data.kinds.map((k) => `<button type="button" class="dset-card" data-ap-kind="${esc(k.kind)}" aria-pressed="${k.kind === draft.kind}"><span><b>${esc(k.name)}</b><span>${esc(k.about)}</span></span></button>`).join('')}</div>`)
      + (kind ? `<label class="pf-v"><span>Name</span><input data-ap-name value="${esc(draft.name)}" maxlength="60" placeholder="What people call the app" autocomplete="off"></label>${kind.fields.map(field).join('')}`
        + `<div class="pf-when"><span>Match accounts by</span><div class="seg" role="group" aria-label="Match accounts by">${Object.entries(MATCH).map(([k, l]) => `<button type="button" data-ap-m="${k}" aria-pressed="${draft.match === k}">${l}</button>`).join('')}</div></div>` : '');
    $('ap-connect-go').disabled = !kind;
    $('ap-connect-go').textContent = edit ? 'Save' : 'Connect';
  };
  const keepTyped = () => {
    const name = $('ap-connect-body').querySelector('[data-ap-name]');
    if (name) draft.name = name.value;
    $('ap-connect-body').querySelectorAll('[data-ap-f]').forEach((el) => { draft.settings[el.dataset.apF] = el.value; });
  };
  const openConnect = (a = null) => {
    draft = a ? { id: a.id, kind: a.kind, name: a.name, match: a.match, settings: { ...a.settings } } : { id: '', kind: '', name: '', match: 'email', settings: {} };
    R.say('ap-connect-problem', '');
    drawConnect();
    $('ap-connect').showModal();
  };
  $('ap-connect').addEventListener('click', (e) => {
    const k = e.target.closest('[data-ap-kind]');
    const m = e.target.closest('[data-ap-m]');
    if (!k && !m) return;
    keepTyped();
    if (k) draft.kind = k.dataset.apKind;
    if (m) draft.match = m.dataset.apM;
    drawConnect();
  });
  $('ap-connect-cancel').addEventListener('click', () => $('ap-connect').close());
  $('ap-connect-go').addEventListener('click', async () => {
    keepTyped();
    const kind = data.kinds.find((k) => k.kind === draft.kind);
    const secretKey = kind.fields.find((x) => x.secret)?.key;
    const settings = Object.fromEntries(kind.fields.filter((x) => !x.secret).map((x) => [x.key, draft.settings[x.key] || '']));
    R.say('ap-connect-problem', '');
    R.busy($('ap-connect'), draft.id ? 'Saving' : `Connecting ${draft.name || kind.name}`, 'Reading accounts, groups and roles…');
    try {
      const saved = await send('/api/apps', { id: draft.id || null, kind: draft.kind, name: draft.name, settings, secret: secretKey ? draft.settings[secretKey] || null : null, match: draft.match });
      $('ap-connect').close();
      await load();
      const a = appOf(saved.id);
      toast(draft.id ? 'Connection saved.' : 'App connected.', a ? `${a.name}: ${count(a.accounts.length, a.account, `${a.account}s`)} and ${count(a.groups.length, a.group, `${a.group}s`)} read.` : '');
      if (a && !draft.id) { pane = 'accounts'; openApp(a); }
    } catch (e) {
      if (!e.cancelled) R.say('ap-connect-problem', e.message);
    } finally {
      R.busy($('ap-connect'), '');
    }
  });

  // --- Give access: an AD group, and one or several groups and roles in each app -----------------------------------
  let giving = null; // { id, ad, adName, mode, to: Map(app id, Set(group id)), plan, asked }
  let adGroups = [];
  const givingTo = () => [...giving.to].flatMap(([app, set]) => [...set].map((group) => ({ app, group })));
  const drawGive = () => {
    const picks = data.apps.map((a) => {
      const set = giving.to.get(a.id) || new Set();
      const names = a.groups.filter((g) => set.has(g.id)).map((g) => g.name);
      return `<div class="pf-v"><span>${esc(a.name)}</span><button type="button" class="ap-pickb${names.length ? '' : ' none'}" data-ap-roles="${esc(a.id)}"><span class="ell">${esc(names.length ? some(names) : 'Nothing')}</span>${names.length > 1 ? `<small>${names.length}</small>` : ''}${svg('chevron-down')}</button></div>`;
    }).join('');
    const p = giving.plan;
    const k = (kind) => (p || []).filter((i) => i.kind === kind).length;
    const sum = !p ? '' : [k('create') && count(k('create'), 'account to make', 'accounts to make'), k('add') && `${k('add')} to give`, k('blocked') && `${k('blocked')} blocked`].filter(Boolean).join(' · ') || 'Nothing changes now';
    $('ap-give-body').innerHTML = `<label class="pf-v"><span>Members of this Active Directory group</span><select data-ap-ad${giving.id ? ' disabled' : ''}><option value="">Select a group</option>`
      + `${adGroups.map((g) => `<option value="${esc(g.dn)}"${g.dn.toLowerCase() === giving.ad.toLowerCase() ? ' selected' : ''}>${esc(g.name)}</option>`).join('')}</select></label>`
      + `<h3>Get these groups and roles</h3>${picks}`
      + `<div class="pf-when"><span>Members get them</span><div class="seg" role="group" aria-label="Members get them"><button type="button" data-ap-mode="auto" aria-pressed="${giving.mode === 'auto'}">Automatically</button><button type="button" data-ap-mode="hand" aria-pressed="${giving.mode === 'hand'}">After I approve each one</button></div></div>`
      + `<h3>What happens <small>${esc(sum)}</small></h3><div class="ap-plan">${!giving.ad || !givingTo().length ? '<p class="muted">Select a group, and at least one group or role to give.</p>'
        : !p ? '<div data-skel="rows:2"></div>' : p.length ? p.map((i) => planRow(i)).join('') : '<p class="muted">Everyone in the group already has these. New members get them when they join.</p>'}</div>`;
    R.skeleton.scan($('ap-give-body'));
    $('ap-give-go').disabled = !giving.ad || !givingTo().length;
  };
  // What saving would do, asked again each time the group or what it gets changes.
  const preview = async () => {
    const asked = ++giving.asked;
    giving.plan = null;
    drawGive();
    if (!giving.ad || !givingTo().length) return;
    try {
      const r = await send('/api/apps/rules/preview', { id: giving.id || null, ad: giving.ad, adName: giving.adName, to: givingTo(), mode: giving.mode });
      if (giving && asked === giving.asked) { giving.plan = r.plan; drawGive(); }
    } catch (e) {
      if (giving && asked === giving.asked) { giving.plan = []; drawGive(); R.say('ap-give-problem', e.message); }
    }
  };
  // from: { rule } to edit one, or { app, group } to start from a group or role in an app.
  const openGive = async (from = {}) => {
    const r = from.rule;
    giving = { id: r?.id || '', ad: r?.ad || '', adName: r?.adName || '', mode: r?.mode || 'auto', to: new Map(), plan: null, asked: 0 };
    (r?.to || []).forEach((t) => { if (!giving.to.has(t.app)) giving.to.set(t.app, new Set()); giving.to.get(t.app).add(t.group); });
    if (from.group) giving.to.set(from.app, new Set([from.group]));
    $('ap-give-title').textContent = r ? `Edit the rule for ${r.adName}` : 'Give access';
    R.say('ap-give-problem', '');
    $('ap-give').showModal();
    R.skeleton($('ap-give-body'), 'form', 4);
    try {
      adGroups = [...(await R.keep('/api/ad/groups', 60000))].sort((x, y) => x.name.localeCompare(y.name));
    } catch (e) {
      adGroups = r ? [{ dn: r.ad, name: r.adName }] : [];
      R.say('ap-give-problem', e.message);
    }
    preview();
  };
  $('ap-give').addEventListener('change', (e) => {
    if (!e.target.matches('[data-ap-ad]')) return;
    giving.ad = e.target.value;
    giving.adName = adGroups.find((g) => g.dn === giving.ad)?.name || '';
    // A group that has a rule already: this edits it, starting from what it gives.
    const had = data.rules.find((x) => x.ad.toLowerCase() === giving.ad.toLowerCase());
    if (had) had.to.forEach((t) => { if (!giving.to.has(t.app)) giving.to.set(t.app, new Set()); giving.to.get(t.app).add(t.group); });
    preview();
  });
  $('ap-give').addEventListener('click', (e) => {
    const m = e.target.closest('[data-ap-mode]');
    const b = e.target.closest('[data-ap-roles]');
    if (m) { giving.mode = m.dataset.apMode; drawGive(); }
    if (!b) return;
    const a = appOf(b.dataset.apRoles);
    if (!giving.to.has(a.id)) giving.to.set(a.id, new Set());
    // The console's own tick list: several can be ticked, and it stays open while you tick.
    R.picker.open(b, { key: `ap-roles:${a.id}`, stars: false, items: a.groups.map((g) => ({ key: g.id, label: a.groups.some((x) => x.type !== g.type) ? `${g.name} (${g.type.toLowerCase()})` : g.name })),
      picked: giving.to.get(a.id), change: () => preview() });
  });
  $('ap-give-cancel').addEventListener('click', () => $('ap-give').close());
  $('ap-give').addEventListener('close', () => { R.picker.close(); giving = null; });
  $('ap-give-go').addEventListener('click', async () => {
    const g = giving;
    R.say('ap-give-problem', '');
    R.busy($('ap-give'), `Giving ${g.adName} access`, g.mode === 'auto' ? 'Making accounts and giving groups and roles…' : 'Saving the rule…');
    try {
      const r = await send('/api/apps/rules', { id: g.id || null, ad: g.ad, adName: g.adName, to: givingTo(), mode: g.mode });
      $('ap-give').close();
      await load();
      const names = gives(ruleOf(r.id) || { to: [] }).flatMap(([, x]) => x);
      if (r.failed) toast(`${g.adName}: ${count(r.failed, 'change', 'changes')} failed.`, `${r.done} made. Each app's Activity says why the rest failed.`, { tone: 'warn' });
      else toast(`${g.adName} gets ${some(names, 3)}.`, g.mode === 'auto' ? (r.done ? `${count(r.done, 'change', 'changes')} made in the apps.` : 'Everyone already had them.') : 'The changes wait under Review changes.');
    } catch (e) {
      if (!e.cancelled) R.say('ap-give-problem', e.message);
    } finally {
      R.busy($('ap-give'), '');
    }
  });

  // --- Review changes: every difference, by app, ticked for what happens now ---------------------------------------
  let reviewing = null; // { items, ticked }
  const drawReview = () => {
    const { items, ticked } = reviewing;
    const apps = [...new Set(items.map((i) => i.app))];
    $('ap-review-body').innerHTML = apps.map((id) => {
      const mine = items.filter((i) => i.app === id);
      return `<section class="ap-rapp"><h3>${esc(mine[0].appName)} <small>${esc([['create', 'to make'], ['add', 'to give'], ['drift', 'given by hand'], ['blocked', 'blocked']].map(([k, w]) => { const c = mine.filter((i) => i.kind === k).length; return c ? `${c} ${w}` : ''; }).filter(Boolean).join(' · '))}</small></h3>`
        + mine.map((i) => `<label class="ap-ritem">${i.kind === 'blocked' ? '<span></span>' : `<input type="checkbox" data-ap-key="${esc(i.key)}"${ticked.has(i.key) ? ' checked' : ''}>`}<span class="ap-mark ${i.kind}">${MARK[i.kind]}</span>`
          + `<span class="two"><b>${esc(i.text)}</b><small>${esc(i.why)}</small></span></label>`).join('') + '</section>';
    }).join('') || '<p class="muted">Everyone has what the rules give, and nobody else has it.</p>';
    const k = ticked.size;
    $('ap-review-sum').textContent = k ? `${count(k, 'change', 'changes')} selected` : 'Nothing selected';
    $('ap-review-go').disabled = !k || !may();
    $('ap-review-go').textContent = k ? `Apply ${count(k, 'change', 'changes')}` : 'Apply';
  };
  const openReview = (test = () => true, of = '') => {
    const items = planFor(test);
    // Ticked to start with: what the rules owe. Taking something away is always a choice.
    reviewing = { items, ticked: new Set(items.filter((i) => i.kind === 'create' || i.kind === 'add').map((i) => i.key)) };
    $('ap-review-title').textContent = of ? `Changes waiting for ${of}` : 'Changes waiting';
    R.say('ap-review-problem', '');
    drawReview();
    $('ap-review-d').showModal();
  };
  $('ap-review-body').addEventListener('change', (e) => {
    const k = e.target.dataset.apKey;
    if (!k) return;
    if (e.target.checked) reviewing.ticked.add(k); else reviewing.ticked.delete(k);
    drawReview();
  });
  $('ap-review-cancel').addEventListener('click', () => $('ap-review-d').close());
  $('ap-review-go').addEventListener('click', async () => {
    const keys = [...reviewing.ticked];
    R.busy($('ap-review-d'), `Applying ${count(keys.length, 'change', 'changes')}`, 'Accounts first, then groups and roles…');
    try {
      const r = await send('/api/apps/apply', { keys });
      $('ap-review-d').close();
      await load();
      if (r.failed) toast(`${count(r.failed, 'change', 'changes')} failed.`, `${r.done} made. Each app's Activity says why the rest failed.`, { tone: 'warn' });
      else toast('Changes applied.', `${count(r.done, 'change', 'changes')} made in the apps.`);
    } catch (e) {
      if (!e.cancelled) R.say('ap-review-problem', e.message);
    } finally {
      R.busy($('ap-review-d'), '');
    }
  });

  $('ap-new').addEventListener('click', () => openConnect());
  $('ap-rule').addEventListener('click', () => openGive());
  $('ap-review').addEventListener('click', () => openReview());
  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'apps') return;
    if (!data) R.skeleton($('ap-list'), 'grid', 4, { cols: 6 });
    else draw();
    load();
  });
  // While it's open: every 30 seconds, so a sync that just ran shows without a refresh.
  R.live({ page: onPage, every: 30000, read: () => get('/api/apps'), now: () => data, draw: (got) => { data = got; draw(); }, areas: () => [$('ap-list'), $('ap-rules')] });
  // The count in the sidebar from the start.
  document.addEventListener('rabbidlab:me', () => { if (has('apps.view') && !data) load(); });
})();
