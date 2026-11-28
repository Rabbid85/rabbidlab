// Lifecycle (Lifecycle.cs), under Automation: what is dated, a card each, soonest first. A workflow run saved for a day
// (a new user set up for their start date) with its steps, Run now and Cancel; an offboarding with its stages, which
// opens the person on their Offboarding tab; and an account about to expire in Active Directory, with Offboard….
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, has, toast, sure, get, send, count, oops } = R;
  const DAY = 86400000;
  const lower = (s) => String(s || '').toLowerCase();
  const onPage = () => R.where()[0] === 'lifecycle';
  const dayOf = (d) => new Date(d).toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' });
  const timeOf = (d) => new Date(d).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

  let data = null; // /api/lifecycle
  let users = []; // the Users list other pages keep, for accounts about to expire

  const STEP = { done: ['Done', 'green'], failed: ['Failed', 'red'], skipped: ['Not run', 'grey'] };
  const RUN = { scheduled: ['Scheduled', 'grey'], running: ['Running', 'amber'], done: ['Done', 'green'], failed: ['Failed', 'red'], cancelled: ['Cancelled', 'grey'] };
  const going = (state) => ['scheduled', 'running', 'failed'].includes(state);
  // Enabled people whose account expires within the days shown, and who aren't being offboarded already.
  const expiring = () => {
    if (!data) return [];
    const leaving = new Set(data.offboarding.map((o) => lower(o.username)));
    const now = Date.now();
    return users.filter((u) => u.enabled && !u.system && u.accountExpires && !leaving.has(lower(u.username))
      && new Date(u.accountExpires) - now < data.days * DAY && new Date(u.accountExpires) - now > -DAY);
  };
  const cards = () => [
    ...data.planned.map((p) => ({ at: new Date(p.when), key: `run:${p.id}`, html: planned(p) })),
    ...data.offboarding.map((o) => ({ at: new Date(o.lastDay), key: `off:${o.username}`, html: leaving(o) })),
    ...expiring().map((u) => ({ at: new Date(u.accountExpires), key: `exp:${u.username}`, html: expires(u) })),
  ].sort((a, b) => a.at - b.at);

  const head = (name, line, from, badge, acts = '', open = '') => `<header${open ? ` data-lc-open="${esc(open)}" tabindex="0"` : ''}><span class="pic user">${esc(R.initials(name))}</span>`
    + `<span class="two"><b>${esc(name)}</b><small>${esc(line)}</small><small class="lc-from">${svg('calendar')}${esc(from)}</small></span>`
    + `<span class="ubadge ${badge[1]}">${badge[0]}</span>${acts ? `<span class="lc-acts">${acts}</span>` : ''}</header>`;
  const task = (cls, when, bold, small, badge) => `<li class="${cls}"><i></i><time>${esc(when)}</time><span class="two"><b>${esc(bold)}</b>${small ? `<small>${esc(small)}</small>` : ''}</span>`
    + `${badge ? `<span class="ubadge ${badge[1]}">${badge[0]}</span>` : '<span></span>'}</li>`;

  // A workflow run saved for a day: its steps as they'll run, or as they went.
  const planned = (p) => {
    const acts = p.canChange && ['scheduled', 'failed'].includes(p.state)
      ? `<button type="button" class="small" data-lc-run="${esc(p.id)}">${svg(p.state === 'failed' ? 'restart' : 'play')}${p.state === 'failed' ? 'Run again' : 'Run now'}</button>`
        + `<button type="button" class="small" data-lc-cancel="${esc(p.id)}">${svg('x')}Cancel</button>` : '';
    const when = p.ran ? `Ran ${dayOf(p.ran)} at ${timeOf(p.ran)}` : `${p.creates ? 'Starts' : 'Runs'} ${dayOf(p.when)} at ${timeOf(p.when)}`;
    const steps = p.steps.map((s) => task(s.state === 'done' ? 'done' : s.state === 'failed' ? 'failed' : '', s.label, s.text, '', s.state ? STEP[s.state] : p.state === 'cancelled' ? RUN.cancelled : RUN[p.state === 'running' ? 'running' : 'scheduled'])).join('');
    return `<div class="lc-card" data-lc-card="run:${esc(p.id)}">${head(p.name, `${when} · ${p.flow}`, `Scheduled by ${p.by} on ${R.when(p.made)}. It runs with ${p.by}’s permissions at that time.`,
      going(p.state) ? [p.creates ? 'New user' : 'Workflow', p.state === 'failed' ? 'red' : 'green'] : RUN[p.state], acts, p.user && p.state === 'done' ? p.user : '')}`
      + `<ol class="lc-tl">${steps || task('', '', 'The workflow was deleted.', '', null)}</ol></div>`;
  };
  // An offboarding: its three stages, each with what it does. The person's panel has Run stage now, Edit and Cancel.
  const leaving = (o) => {
    const stages = o.stages.map((s) => task(s.state === 'done' ? 'done' : s.state === 'failed' ? 'failed' : '', s.ran ? `Ran ${s.ranWhen}` : s.when, `Stage ${s.n}: ${s.name}`, s.steps.map((x) => x.line).join(' · '),
      RUN[s.state] || RUN.scheduled)).join('');
    return `<div class="lc-card" data-lc-card="off:${esc(o.username)}">${head(o.name || o.username, `Last day ${dayOf(o.lastDay)} · ${o.status}`, `Offboarding saved by ${o.by} on ${R.when(o.saved)}.`,
      ['Offboarding', o.state === 'failed' ? 'red' : going(o.state) ? 'amber' : 'grey'], '', o.username)}<ol class="lc-tl">${stages}</ol></div>`;
  };
  // An account with an expiry date and nothing planned for it.
  const expires = (u) => `<div class="lc-card" data-lc-card="exp:${esc(u.username)}">${head(u.name || u.username, `Account expires ${dayOf(u.accountExpires)} · ${u.where || u.username}`, 'Account expires in Active Directory. No offboarding is scheduled.',
    ['Expires', 'amber'], R.offboard && has('ad.enable') ? `<button type="button" class="small" data-lc-offboard="${esc(u.username)}">${svg('user-off')}Offboard…</button>` : '', u.username)}</div>`;

  const drawHero = () => {
    if (!data) return;
    const runs = data.planned.filter((p) => going(p.state));
    const offs = data.offboarding.filter((o) => going(o.state));
    const exp = expiring().length;
    const tasks = runs.reduce((n, p) => n + p.steps.length, 0) + offs.reduce((n, o) => n + o.stages.filter((s) => s.state === 'scheduled').reduce((m, s) => m + s.steps.length, 0), 0);
    const all = runs.length + offs.length + exp;
    $('lc-state').textContent = all ? `${count(all, 'person has', 'people have')} something scheduled or expiring.` : 'Nothing is scheduled.';
    $('lc-n-new').textContent = runs.filter((p) => p.creates).length;
    $('lc-n-off').textContent = offs.length;
    $('lc-n-exp').textContent = exp;
    $('lc-n-tasks').textContent = tasks;
    $('rail-lifecycle').textContent = all || '';
  };
  const draw = () => {
    drawHero();
    if (!onPage() || !data) return;
    const list = cards();
    $('lc-count').textContent = count(list.length, 'entry', 'entries');
    $('lc-list').innerHTML = list.map((c) => c.html).join('')
      || `<p class="muted lc-none">Nothing is scheduled, and no account expires in the next ${data.days} days. Run a workflow on a date, or schedule an offboarding, and it is listed here.</p>`;
  };
  const read = async () => {
    const [d, u] = await Promise.all([get('/api/lifecycle'), has('ad.view') ? R.keep('/api/ad/users', 60000).catch(() => null) : null]);
    return { d, users: (u?.users || []).filter((x) => !x.service) };
  };
  const take = (got) => {
    data = got.d;
    users = got.users;
    R.say('lc-problem', '');
    draw();
  };
  const load = async () => {
    try {
      take(await read());
    } catch (e) {
      R.say('lc-problem', e.message);
      if (!data) R.skeleton($('lc-list'), '');
    }
  };

  $('lc-list').addEventListener('click', async (e) => {
    const run = e.target.closest('[data-lc-run]');
    const cancel = e.target.closest('[data-lc-cancel]');
    const off = e.target.closest('[data-lc-offboard]');
    const open = e.target.closest('[data-lc-open]');
    if (run) {
      const p = data.planned.find((x) => x.id === run.dataset.lcRun);
      run.disabled = true;
      toast(`Running ${p.flow}`, p.name, { stay: true });
      try {
        const done = await send(`/api/lifecycle/planned/${p.id}/run`, {});
        const bad = done.steps.find((s) => s.state === 'failed');
        if (bad) toast(`${p.flow} stopped at ${bad.label}.`, bad.detail, { tone: 'warn' });
        else toast(`${p.flow} completed.`, `${count(done.steps.length, 'step', 'steps')}${done.userName ? ` for ${done.userName}` : ''}.`);
        document.dispatchEvent(new CustomEvent('rabbidlab:undone')); // the Users list reads again
      } catch (err) {
        oops(err);
      }
      R.forget();
      await load();
    } else if (cancel) {
      const p = data.planned.find((x) => x.id === cancel.dataset.lcCancel);
      sure(`Cancel ${p.flow} for ${p.name}?`, 'The run is not made. What an earlier run already changed stays as it is.', 'Cancel run', async () => {
        try {
          await send(`/api/lifecycle/planned/${p.id}/cancel`, {});
          toast('Run cancelled.', `${p.flow} for ${p.name}.`);
        } catch (err) {
          oops(err);
        }
        await load();
      });
    } else if (off) {
      R.offboard.open([off.dataset.lcOffboard]);
    } else if (open) {
      R.adOpenUser(open.dataset.lcOpen);
    }
  });
  $('lc-list').addEventListener('keydown', (e) => { if (e.key === 'Enter' && e.target.matches('[data-lc-open]')) e.target.click(); });
  // New user: a workflow that makes one, run now or on their start date. Offboard…: pick who, then the Offboard window.
  $('lc-new').addEventListener('click', () => R.flows.create());
  $('lc-off').addEventListener('click', () => {
    const list = users.filter((u) => u.enabled && !u.system && !R.offboard.active(u.username)).sort((a, b) => (a.name || a.username).localeCompare(b.name || b.username));
    $('lc-pick-user').innerHTML = list.map((u) => `<option value="${esc(u.username)}">${esc(u.name ? `${u.name} (${u.username})` : u.username)}</option>`).join('');
    $('lc-pick-go').disabled = !list.length;
    $('lc-pick').showModal();
  });
  $('lc-pick-cancel').addEventListener('click', () => $('lc-pick').close());
  $('lc-pick-go').addEventListener('click', () => {
    const u = $('lc-pick-user').value;
    $('lc-pick').close();
    if (u) R.offboard.open([u]);
  });

  document.addEventListener('rabbidlab:show', (e) => {
    if (e.detail[0] !== 'lifecycle') return;
    if (!data) R.skeleton($('lc-list'), 'timeline', 4);
    else draw();
    load();
  });
  // While it's open: every 20 seconds, so a stage or run that just ran shows without a refresh.
  R.live({ page: onPage, every: 20000, read, now: () => ({ d: data, users }), draw: take, areas: () => [$('lc-list')] });
  // Something was scheduled or changed elsewhere: the count beside its name, and the page when it's open.
  ['rabbidlab:lifecycle', 'rabbidlab:undone'].forEach((name) => document.addEventListener(name, () => { if (has('ad.view')) load(); }));
  document.addEventListener('rabbidlab:me', () => { if (has('ad.view') && !data) load(); });
})();
