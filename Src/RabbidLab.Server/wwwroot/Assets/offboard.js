// Offboarding (Offboarding.cs): the window that sets it up in three stages counted from the last day, the Offboarding tab
// in a user's side panel, and what the Users list's Offboarding column shows. Users (users.js) opens the window from
// Actions › Offboard…, asks for the tab and the column, and reads its list again on rabbidlab:offboarded.
(() => {
  const R = window.RabbidLab;
  const { $, esc, svg, count, listed: list } = R;
  const lower = (u) => String(u || '').toLowerCase();

  // --- What the Users list shows: each person's offboarding in brief --------------------------------------------------
  const briefs = new Map(); // username → { status, state, next }
  const views = new Map(); // username → { plan, at } for the tab
  let redraw = () => {};
  const load = async () => {
    try {
      const got = await R.get('/api/offboarding');
      const next = new Map(got.map((b) => [lower(b.username), b]));
      if (JSON.stringify([...next]) === JSON.stringify([...briefs])) return;
      briefs.clear();
      next.forEach((b, k) => briefs.set(k, b));
      views.forEach((v) => { v.at = 0; }); // the open tab reads its plan again
      redraw();
    } catch {
      // The column keeps what it showed.
    }
  };
  const brief = (u) => briefs.get(lower(u));
  const active = (u) => ['scheduled', 'running', 'failed'].includes(brief(u)?.state);
  const cell = (u) => {
    const b = brief(u);
    if (!b) return '<span class="muted">-</span>';
    return `<span class="ob-due ${b.state}">${svg(b.state === 'failed' ? 'alert' : b.state === 'done' ? 'check' : 'clock')}<span class="ell">${esc(b.status)}</span></span>`;
  };
  const sortOf = (u) => { const b = brief(u); return b?.next ? new Date(b.next).getTime() : b ? Number.MAX_SAFE_INTEGER - 1 : Number.MAX_SAFE_INTEGER; };

  // --- The tab ---------------------------------------------------------------------------------------------------------
  const readView = async (u) => {
    const v = views.get(lower(u)) || { plan: undefined, at: 0 };
    views.set(lower(u), v);
    v.at = Date.now();
    try {
      const { plan } = await R.get(`/api/offboarding/${encodeURIComponent(u)}`);
      if (JSON.stringify(plan) === JSON.stringify(v.plan)) return;
      v.plan = plan;
    } catch (err) {
      if (v.plan !== undefined) return; // what was read stays
      v.plan = { error: err.message };
    }
    redraw();
  };
  const STAGE_WORD = { scheduled: ['Scheduled', 'amber'], running: ['Running', 'amber'], done: ['Done', 'green'], failed: ['Failed', 'red'], cancelled: ['Cancelled', 'grey'] };
  const RESULT_ICON = { done: 'check', skipped: 'dots', failed: 'alert' };
  const pane = (x) => {
    const u = x.username;
    const v = views.get(lower(u));
    if (!v || Date.now() - v.at > 30000) readView(u);
    const head = (links = '') => `<div class="dw-sec-head"><h3>Offboarding</h3><div class="dw-links">${links}</div></div>`;
    if (!v || v.plan === undefined) return `<section class="dw-sec">${head()}<div data-skel="timeline:3"></div></section>`;
    const p = v.plan;
    if (!p) return `<section class="dw-sec">${head()}<p class="muted dw-empty">${esc(x.name || u)} has no offboarding.</p></section>`;
    if (p.error) return `<section class="dw-sec">${head()}<p class="hint problem">${esc(p.error)}</p></section>`;
    const going = ['scheduled', 'running', 'failed'].includes(p.state);
    const links = p.canChange && going ? `<button type="button" class="dw-link" data-ob-edit>${svg('edit')}Edit</button><button type="button" class="dw-link" data-ob-cancel>${svg('x')}Cancel the rest</button>` : '';
    const next = p.stages.find((s) => ['scheduled', 'running', 'failed'].includes(s.state));
    const items = p.stages.map((s) => {
      const [word, tone] = STAGE_WORD[s.state] || ['', 'grey'];
      const when = s.ran ? `Ran ${s.ranWhen}${s.ranBy ? ` by ${s.ranBy}` : ''}` : s.state === 'cancelled' ? `Was to run ${s.when}` : `Runs ${s.when}`;
      const steps = s.steps.map((st) => {
        const r = st.result;
        return `<li class="${r ? r.state : ''}">${r ? svg(RESULT_ICON[r.state] || 'dots') : '<i class="ob-dot"></i>'}<span><b>${esc(st.line)}</b>${r?.detail ? `<small>${esc(r.detail)}</small>` : ''}</span></li>`;
      }).join('');
      const act = p.canChange && s === next && s.state !== 'running'
        ? `<button type="button" class="small" data-ob-run>${svg(s.state === 'failed' ? 'restart' : 'play')}${s.state === 'failed' ? `Retry stage ${s.n}` : `Run stage ${s.n} now`}</button>` : '';
      return `<li class="ob-st ${s.state}${s === next ? ' next' : ''}"><i class="ob-n">${s.state === 'done' ? svg('check') : s.n}</i><div><p><b>Stage ${s.n}: ${esc(s.name)}</b><span class="ubadge ${tone}">${word}</span></p>`
        + `<small class="muted">${esc(when)}</small><ul class="ob-steps">${steps}</ul>${act}</div></li>`;
    }).join('');
    return `<section class="dw-sec" data-ob-user="${esc(u)}">${head(links)}<p class="hint">Saved by ${esc(p.by)}. Each stage runs with ${esc(p.by)}’s permissions at the time it runs.</p>`
      + `<ol class="ob-tl">${items}</ol></section>`;
  };
  const plan = (u) => views.get(lower(u))?.plan;
  const changedNow = (users) => {
    users.forEach((u) => views.delete(lower(u)));
    load();
    document.dispatchEvent(new CustomEvent('rabbidlab:offboarded', { detail: { users } }));
  };
  R.panel.el.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-ob-edit], [data-ob-cancel], [data-ob-run]');
    if (!b) return;
    const p = plan(b.closest('[data-ob-user]')?.dataset.obUser);
    const u = p?.username;
    if (!u || p.error) return;
    if (b.matches('[data-ob-edit]')) { open([u], { edit: true }); return; }
    if (b.matches('[data-ob-cancel]')) {
      const left = p.stages.filter((s) => ['scheduled', 'failed'].includes(s.state)).length;
      R.sure('Cancel the rest of the offboarding?', `${count(left, 'stage', 'stages')} for ${p.name} will not run. Stages that already ran are not undone.`, 'Cancel stages', async () => {
        try {
          await R.send(`/api/offboarding/${encodeURIComponent(u)}/cancel`);
          R.toast('Offboarding stages canceled.', `${count(left, 'stage was', 'stages were')} canceled for ${p.name}.`);
          changedNow([u]);
        } catch (err) {
          R.toast('The stages could not be canceled.', err.message, { tone: 'warn' });
        }
      });
      return;
    }
    const next = p.stages.find((s) => ['scheduled', 'failed'].includes(s.state));
    R.busy($('dw-body'), next?.state === 'failed' ? `Retrying stage ${next.n}` : `Running stage ${next?.n}`, p.name);
    try {
      const { plan: after } = await R.send(`/api/offboarding/${encodeURIComponent(u)}/run`);
      const st = after.stages.find((s) => s.n === next?.n);
      const bad = st?.steps.filter((x) => x.result?.state === 'failed') || [];
      if (bad.length) R.toast(`Stage ${st.n} did not finish.`, `${bad[0].label}: ${bad[0].result.detail}`, { tone: 'warn' });
      else R.toast(`Stage ${st?.n} complete.`, after.state === 'done' ? `${p.name} is fully offboarded.` : after.status);
      changedNow([u]);
    } catch (err) {
      R.toast('The stage could not run.', err.message, { tone: 'warn' });
    } finally {
      R.busy($('dw-body'), '');
    }
  });

  // --- The window ------------------------------------------------------------------------------------------------------
  const DAY = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  const MON = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  // Times are the server's (its own clock and time zone): a Date here only carries the numbers.
  const parse = (s) => { const [d, t] = s.split('T'); const [y, m, dd] = d.split('-').map(Number); const [h, mi] = t.split(':').map(Number); return new Date(y, m - 1, dd, h, mi); };
  const fmt = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  const clock = (d) => `${d.getHours() % 12 || 12}:${String(d.getMinutes()).padStart(2, '0')} ${d.getHours() < 12 ? 'AM' : 'PM'}`;
  const sameDay = (a, b) => !!a && !!b && a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  const plusDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, d.getHours(), d.getMinutes());
  const at5 = (y, m, d) => new Date(y, m, d, 17, 0);

  const STEPS = [
    ['disable', 1, 'Disable the account'], ['password', 1, 'Reset the password to a random value'], ['signOut', 1, 'Sign out of every computer'],
    ['hide', 1, 'Hide from address lists'], ['manager', 1, 'Clear the manager and reassign direct reports'], ['contact', 1, 'Remove the phone number and office'],
    ['note', 1, 'Add an offboarding note'], ['email', 1, 'Email the manager when it is done'],
    ['groups', 2, 'Remove from groups'], ['move', 2, 'Move to another OU'], ['computers', 2, 'Disable their computers'], ['archive', 2, 'Move the home folder to the archive share'],
    ['purge', 3, 'Delete the archived home folder'], ['delete', 3, 'Delete the account'],
  ];
  const W = { open: false, people: [], look: null, p: null, i: 0, seen: new Set(), edit: false, lock: new Set(), places: [], users: [] };

  const quickPicks = () => {
    const now = parse(W.look.now);
    const y = now.getFullYear();
    const m = now.getMonth();
    const today = at5(y, m, now.getDate());
    const friday = at5(y, m, now.getDate() + ((5 - now.getDay() + 7) % 7 || (today > now ? 0 : 7)));
    const lastWork = (yy, mm) => { const d = new Date(yy, mm + 1, 0); while (d.getDay() % 6 === 0) d.setDate(d.getDate() - 1); return at5(yy, mm, d.getDate()); };
    const month = lastWork(y, m) > now ? lastWork(y, m) : lastWork(y, m + 1);
    return [['now', 'Now', null], ['today', 'End of today', today > now ? today : null], ['friday', `${sameDay(friday, now) ? 'Today' : 'Friday'} at 5:00 PM`, friday],
      ['month', 'Last working day of the month', month], ['pick', 'Pick a date and time', null]];
  };
  const stageAt = (n) => { const p = W.p; const base = p.quick === 'now' ? parse(W.look.now) : p.at; return base && (n === 1 ? base : plusDays(base, n === 2 ? p.days2 : p.days3)); };
  const whenWords = (d) => (sameDay(d, parse(W.look.now)) ? `today at ${clock(d)}` : `${DAY[d.getDay()]}, ${MON[d.getMonth()]} ${d.getDate()} at ${clock(d)}`);
  const stageWhen = (n) => (n === 1 && W.p.quick === 'now' && !W.lock.has(1) ? 'Now' : stageAt(n) ? whenWords(stageAt(n)) : 'Pick a time');
  const stageName = (n) => (n === 1 ? 'Last day' : `${n === 2 ? W.p.days2 : W.p.days3} days later`);
  const on = (key) => !!W.p.o[key];
  const stageKeys = (n) => STEPS.filter(([k, s]) => s === n && on(k)).map(([k]) => k);
  const one = () => (W.people.length === 1 ? W.people[0] : null);
  const who = () => (one() ? one().name : `${W.people.length} users`);

  // What a step does for these people, as the review and the timeline say it.
  const line = (k) => {
    const o = W.p.o;
    if (k === 'manager') return o.reportsTo ? `Clear the manager and move direct reports to ${o.reportsToName}` : 'Clear the manager';
    if (k === 'note') return `Add the note “${o.noteText || defaultNote()}” to the description and the Notes tab`;
    if (k === 'groups') return W.p.keep.size ? `Remove from groups, keeping ${list(groups().filter((g) => W.p.keep.has(g.dn)).map((g) => g.name))}` : 'Remove from every group except the primary group';
    if (k === 'move') return `Move to ${o.moveToName || 'another OU'}`;
    if (k === 'archive') return `Move the home folder to ${o.archiveTo || 'the archive share'}`;
    return STEPS.find(([x]) => x === k)[2];
  };
  const defaultNote = () => { const d = stageAt(1) || parse(W.look.now); return `Offboarded ${d.getDate()} ${MON[d.getMonth()].slice(0, 3)} ${d.getFullYear()} by ${R.me?.()?.name || R.me?.()?.username || 'me'}.`; };
  const groups = () => {
    const seen = new Map();
    W.people.forEach((u) => (u.groups || []).forEach((g) => { if (!seen.has(lower(g.dn))) seen.set(lower(g.dn), g); }));
    return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name));
  };

  // One switch row: the step, what it does for these people, and anything it needs under it.
  const help = (k) => {
    const u = one();
    const reports = W.people.flatMap((x) => x.reports || []);
    switch (k) {
      case 'disable': return 'Blocks sign-in to Windows, email and everything that uses Active Directory.';
      case 'password': return 'The new password is not shown or kept. This step cannot be undone.';
      case 'signOut': {
        const pcs = W.people.flatMap((x) => x.signedIn || []);
        return pcs.length ? `Signed in now to ${list(pcs)}. Computers they are signed in to when the stage runs are signed out through the ${R.app?.()?.title || 'Rabbid Lab'} client.`
          : 'Not signed in to any computer in Devices right now. Computers they are signed in to when the stage runs are signed out.';
      }
      case 'hide': return W.look.exchange ? 'Sets msExchHideFromAddressLists, so they no longer show in the Global Address List.' : 'This domain has no Exchange attributes, so this step will be skipped.';
      case 'manager': return `${u ? (u.manager ? `${u.name} reports to ${u.manager}.` : `${u.name} has no manager.`) : 'Clears the manager of each user.'} ${reports.length ? `${list(reports.map((r) => r.name))} ${reports.length === 1 ? 'reports' : 'report'} to ${u ? u.name : 'them'}.` : 'There are no direct reports to move.'}`;
      case 'contact': return 'Clears the telephone number, mobile number and office.';
      case 'note': return 'Added to the start of the description and to the Notes tab.';
      case 'email': return u ? (u.manager ? `Sends ${u.manager} an email that lists what changed.` : `${u.name} has no manager, so no email is sent.`) : 'Sends each manager an email that lists what changed.';
      case 'groups': return 'Clear a group to keep that membership. The primary group, such as Domain Users, is not removed.';
      case 'move': return u ? `Now in ${u.where || 'the domain'}.` : 'Each user is moved out of their current OU.';
      case 'computers': {
        const pcs = W.people.flatMap((x) => x.computers || []).map((c) => c.name);
        return pcs.length ? `Disables ${list(pcs)}, which ${pcs.length === 1 ? 'has' : 'have'} ${u ? u.name : 'them'} as Managed by.` : `No computers have ${u ? u.name : 'them'} as Managed by, so this step will be skipped.`;
      }
      case 'archive': {
        const homes = W.people.map((x) => x.home).filter(Boolean);
        return homes.length ? `Home folder: ${list(homes)}.` : `${u ? u.name : 'They'} ${u ? 'has' : 'have'} no home folder set, so this step will be skipped.`;
      }
      case 'purge': return 'Deletes the folder from the archive share. This cannot be undone.';
      case 'delete': return 'The account goes to the Recycle Bin and can be restored from there.';
      default: return '';
    }
  };
  const under = (k) => {
    const o = W.p.o;
    const dim = on(k) ? '' : ' ob-dim';
    if (k === 'manager' && W.people.some((x) => (x.reports || []).length)) {
      const out = new Set(W.people.map((x) => lower(x.username)));
      const opts = W.users.filter((x) => x.enabled && !out.has(lower(x.username)));
      return `<label class="ob-sub${dim}"><span>Direct reports move to</span><select data-ob-field="reportsTo"><option value="">Nobody: only clear their manager</option>`
        + `${opts.map((x) => `<option value="${esc(x.dn)}"${x.dn === o.reportsTo ? ' selected' : ''}>${esc(x.name || x.username)}</option>`).join('')}</select></label>`;
    }
    if (k === 'note') return `<label class="ob-sub${dim}"><span>Note</span><input data-ob-field="noteText" maxlength="512" value="${esc(o.noteText || '')}" placeholder="${esc(defaultNote())}" autocomplete="off"></label>`;
    if (k === 'groups') {
      const gs = groups();
      if (!gs.length) return `<p class="ob-sub muted${dim}">Not in any groups other than the primary group.</p>`;
      return `<div class="ob-groups${dim}">${gs.map((g) => `<label class="ob-g"><input type="checkbox" data-ob-keep="${esc(g.dn)}"${W.p.keep.has(g.dn) ? '' : ' checked'}${on('groups') ? '' : ' disabled'}>`
        + `<span class="ell">${esc(g.name)}</span><small>${W.p.keep.has(g.dn) ? 'Keep' : 'Remove'}</small></label>`).join('')}</div>`;
    }
    if (k === 'move') {
      return `<label class="ob-sub${dim}"><span>Move to</span><select data-ob-field="moveTo"><option value="">Select an OU</option>`
        + `${W.places.map((pl) => `<option value="${esc(pl.dn)}"${pl.dn === o.moveTo ? ' selected' : ''}>${esc(pl.path || pl.name)}</option>`).join('')}</select></label>`;
    }
    if (k === 'archive') return `<label class="ob-sub${dim}"><span>Archive share</span><input data-ob-field="archiveTo" value="${esc(o.archiveTo || '')}" placeholder="\\\\FS01\\Archive$" autocomplete="off" spellcheck="false"></label>`;
    return '';
  };
  const row = ([k, , label]) => `<div class="ob-row"><label class="ob-sw"><span><b>${esc(label)}</b><small>${esc(help(k))}</small></span>`
    + `<input type="checkbox" class="switch" role="switch" data-ob-step="${k}"${on(k) ? ' checked' : ''} aria-label="${esc(label)}"></label>${under(k)}</div>`;
  const seg = (field, opts, label) => `<div class="ob-seg"><span><b>${esc(label)}</b><small>This stage runs ${esc(stageWhen(field === 'days2' ? 2 : 3))}.</small></span>`
    + `<div class="seg" role="group" aria-label="${esc(label)}">${opts.map((d) => `<button type="button" data-ob-days="${field}:${d}" aria-pressed="${W.p[field] === d}">${d} days</button>`).join('')}</div></div>`;

  const calendar = () => {
    const p = W.p;
    const { y, m } = p.cal;
    const now = parse(W.look.now);
    const first = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const lead = new Date(y, m, 1).getDay();
    const days = new Date(y, m + 1, 0).getDate();
    const cells = Array.from({ length: lead }, () => '<span></span>');
    for (let d = 1; d <= days; d++) {
      const day = new Date(y, m, d);
      const cls = [sameDay(day, p.at) && 'on', sameDay(day, now) && 'today', day.getDay() % 6 === 0 && 'wknd'].filter(Boolean).join(' ');
      cells.push(`<button type="button"${cls ? ` class="${cls}"` : ''} data-ob-day="${d}"${day < first ? ' disabled' : ''}>${d}</button>`);
    }
    const cur = p.at ? `${p.at.getHours()}:${p.at.getMinutes()}` : '17:0';
    const times = [];
    for (let h = 0; h < 24; h++) [0, 30].forEach((mm) => times.push(`${h}:${mm}`));
    if (!times.includes(cur)) times.push(cur);
    const opts = times.map((t) => { const [h, mm] = t.split(':').map(Number); return `<option value="${t}"${t === cur ? ' selected' : ''}>${clock(new Date(2000, 0, 1, h, mm))}</option>`; }).join('');
    const atFirst = y === now.getFullYear() && m === now.getMonth();
    return `<div class="ob-cal${p.quick === 'now' ? ' idle' : ''}"><div class="ob-cal-head"><button type="button" class="ob-mon" data-ob-cal="-1"${atFirst ? ' disabled' : ''} data-tip="Previous month" aria-label="Previous month">${svg('right', 'flip')}</button>`
      + `<b>${MON[m]} ${y}</b><button type="button" class="ob-mon" data-ob-cal="1" data-tip="Next month" aria-label="Next month">${svg('right')}</button></div>`
      + `<div class="ob-days">${['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((d) => `<i>${d}</i>`).join('')}${cells.join('')}</div>`
      + `<label class="ob-time"><span>Time</span><select data-ob-time${p.quick === 'now' ? ' disabled' : ''}>${opts}</select></label></div>`;
  };
  const mini = () => `<ol class="ob-mini">${[1, 2, 3].map((n) => { const ks = stageKeys(n); return `<li${ks.length ? '' : ' class="off"'}><i>${n}</i><span><b>Stage ${n}: ${esc(stageName(n))}</b><small>${esc(stageWhen(n))}</small></span><em>${ks.length ? count(ks.length, 'change', 'changes') : 'Nothing selected'}</em></li>`; }).join('')}</ol>`;
  const valid = () => W.lock.has(1) || W.p.quick === 'now' || (!!W.p.at && W.p.at > parse(W.look.now));

  const body = () => {
    const p = W.p;
    if (W.i === 0) {
      if (W.lock.has(1)) return `<h3>Last day</h3><p class="ob-lead">Stage 1 already ran, so the last day stays ${esc(whenWords(p.at))}. The later stages are counted from it.</p>${mini()}`;
      return `<h3>When is the last day?</h3><p class="ob-lead">Stage 1 runs at this time, and the later stages are counted from it. Times are in the server’s time zone (${esc(W.look.zone)}).</p>`
        + `<div class="seg ob-quick" role="radiogroup" aria-label="When">${quickPicks().map(([k, label, d]) => `<button type="button" data-ob-quick="${k}" aria-pressed="${p.quick === k}"${k !== 'now' && k !== 'pick' && !d ? ' disabled' : ''}>${esc(label)}</button>`).join('')}</div>`
        + `<div class="ob-when">${calendar()}<div>${mini()}${valid() ? `<p class="ob-whenline">${svg('clock')}<span>${esc(p.quick === 'now' ? `${who()} ${W.people.length === 1 ? 'is' : 'are'} offboarded as soon as you select Start offboarding.` : `Stage 1 runs ${whenWords(p.at)}. Nothing changes before then.`)}</span></p>`
          : `<p class="ob-whenline bad">${svg('alert')}<span>Pick a time later than now.</span></p>`}</div></div>`;
    }
    if (W.i === 4) {
      return `<h3>Review</h3><p class="ob-lead">${esc(who())}. Nothing changes until you select ${W.p.quick === 'now' && !W.lock.has(1) ? 'Start offboarding' : W.edit ? 'Save changes' : 'Schedule'}.</p>`
        + `<ol class="ob-tl review">${[1, 2, 3].map((n) => { const ks = stageKeys(n); const done = W.lock.has(n); return `<li class="ob-st${done ? ' done' : ks.length ? '' : ' off'}"><i class="ob-n">${done ? svg('check') : n}</i><div><p><b>Stage ${n}: ${esc(stageName(n))}</b>${done ? '<span class="ubadge green">Done</span>' : ''}</p>`
          + `<small class="muted">${esc(done ? 'Already ran' : stageWhen(n))}</small><ul class="ob-steps">${ks.length ? ks.map((k) => `<li><i class="ob-dot"></i><span><b>${esc(line(k))}</b></span></li>`).join('') : '<li class="muted">Nothing selected.</li>'}</ul></div></li>`; }).join('')}</ol>`;
    }
    const n = W.i;
    const head = `<div class="ob-stagehead"><h3>Stage ${n}: ${esc(stageName(n))}</h3><span>${svg('clock')}${esc(stageWhen(n))}</span></div>`;
    if (W.lock.has(n)) return `${head}<p class="ob-lead">This stage already ran, so it cannot be changed.</p>`;
    const days = n === 2 ? seg('days2', W.look.days2, 'How long after the last day') : n === 3 ? seg('days3', W.look.days3, 'How long after the last day') : '';
    return head + days + STEPS.filter(([, s]) => s === n).map(row).join('');
  };
  const ruler = () => {
    const p = W.p;
    const short = (d) => (d ? `${MON[d.getMonth()].slice(0, 3)} ${d.getDate()}` : 'Pick a time');
    const nodes = [{ label: 'When', sub: p.quick === 'now' && !W.lock.has(1) ? 'Now' : short(p.at), icon: 'calendar' },
      ...[1, 2, 3].map((n) => ({ label: stageName(n), sub: W.lock.has(n) ? 'Done' : !stageKeys(n).length ? 'Nothing selected' : n === 1 && p.quick === 'now' && !W.lock.has(1) ? 'Now' : short(stageAt(n)), n, empty: !stageKeys(n).length, done: W.lock.has(n) })),
      { label: 'Review', sub: W.edit ? 'Then save' : 'Then schedule', icon: 'check' }];
    return nodes.map((x, k) => `<button type="button" data-ob-go="${k}" class="${[k === W.i && 'on', x.empty && 'empty', x.done && 'locked', W.seen.has(k) && k !== W.i && 'past'].filter(Boolean).join(' ')}">`
      + `<i>${x.done ? svg('check') : x.icon ? svg(x.icon) : x.n}</i><b>${esc(x.label)}</b><small>${esc(x.sub)}</small></button>`).join('');
  };
  // Why it can't be saved yet, or ''.
  const problem = () => {
    const o = W.p.o;
    if (!valid()) return 'Pick a time later than now.';
    if (![1, 2, 3].some((n) => !W.lock.has(n) && stageKeys(n).length)) return 'Select at least one change to make.';
    if (on('move') && !o.moveTo && !W.lock.has(2)) return 'Select the OU to move to, or turn off Move to another OU.';
    if (on('archive') && !/^\\\\/.test(o.archiveTo || '') && !W.lock.has(2)) return 'Enter the archive share, such as \\\\FS01\\Archive$, or turn off the home folder step.';
    return '';
  };
  const draw = (keepScroll = false) => {
    const d = $('ob-dialog');
    const box = $('ob-body');
    const top = box.scrollTop;
    $('ob-ruler').innerHTML = ruler();
    box.innerHTML = body();
    box.scrollTop = keepScroll ? top : 0;
    const last = W.i === 4;
    const now = W.p.quick === 'now' && !W.lock.has(1);
    const why = last ? problem() : W.i === 0 && !valid() ? 'Pick a time later than now.' : '';
    $('ob-problem').textContent = last ? why : '';
    $('ob-problem').hidden = !last || !why;
    $('ob-back').disabled = W.i === 0;
    const next = $('ob-next');
    next.textContent = !last ? 'Next' : now ? 'Start offboarding' : W.edit ? 'Save changes' : 'Schedule';
    next.className = last && now ? 'danger solid' : 'primary';
    next.disabled = !!why;
    d.classList.toggle('ob-wide', W.i === 0);
  };
  const go = (n) => { W.i = n; W.seen.add(n); draw(); };

  const open = async (usernames, { edit = false } = {}) => {
    const d = $('ob-dialog');
    Object.assign(W, { open: true, people: [], look: null, i: 0, seen: new Set([0]), edit, lock: new Set() });
    $('ob-title').textContent = usernames.length === 1 ? 'Offboard' : `Offboard ${usernames.length} users`;
    $('ob-lead').textContent = 'Offboarding runs in three stages, counted from the last day. Nothing changes until you finish the last step.';
    $('ob-ruler').innerHTML = '';
    $('ob-problem').hidden = true;
    $('ob-next').disabled = true;
    R.skeleton($('ob-body'), 'form', 5);
    if (!d.open) d.showModal();
    try {
      const [look, places, users] = await Promise.all([
        R.send('/api/offboarding/look', { users: usernames }),
        R.keep('/api/ad/places', 60000),
        R.keep('/api/ad/users', 60000).then((j) => j.users || []).catch(() => []),
      ]);
      if (!W.open) return;
      W.look = look;
      W.people = look.people;
      W.places = (places || []).filter((pl) => !pl.fixed);
      W.users = [...users].sort((a, b) => (a.name || a.username).localeCompare(b.name || b.username));
    } catch (err) {
      R.skeleton($('ob-body'), '');
      $('ob-body').innerHTML = `<p class="hint problem">${esc(err.message)}</p>`;
      return;
    }
    const u = one();
    const old = W.people.length === 1 && u.plan && ['scheduled', 'running', 'failed'].includes(u.plan.state) ? u.plan : null;
    W.edit = !!old;
    $('ob-title').textContent = old ? `Change the offboarding of ${u.name}` : u ? `Offboard ${u.name}` : `Offboard ${W.people.length} users`;
    const disabledOu = W.places.find((pl) => /disabled|former|leav/i.test(pl.name));
    const home = W.people.some((x) => x.home);
    const o = old ? { ...old.options } : {
      disable: true, password: true, signOut: true, hide: true, manager: true, reportsTo: u?.managerDn || '', reportsToName: u?.manager || '', contact: true, note: true, noteText: '',
      email: true, groups: true, move: true, moveTo: W.look.moveTo || disabledOu?.dn || '', moveToName: W.look.moveToName || disabledOu?.path || '', computers: true,
      archive: home, archiveTo: W.look.archiveTo || '', purge: home, delete: true,
    };
    const at = old ? parse(old.lastDayLocal) : null;
    W.p = { o, keep: new Set(old?.options.keep || []), days2: old?.days2 || 7, days3: old?.days3 || 90, quick: old ? 'pick' : 'now', at };
    const base = at || parse(W.look.now);
    W.p.cal = { y: base.getFullYear(), m: base.getMonth() };
    if (old) old.stages.filter((s) => s.state === 'done').forEach((s) => W.lock.add(s.n));
    // Start where something can still change.
    W.i = W.lock.has(1) ? ([2, 3].find((n) => !W.lock.has(n)) || 4) : 0;
    W.seen = new Set([W.i]);
    draw();
  };
  const close = () => { W.open = false; $('ob-dialog').close(); };

  const submit = async () => {
    const p = W.p;
    const users = W.people.map((x) => x.username);
    const now = p.quick === 'now' && !W.lock.has(1);
    const form = { users, when: now ? 'now' : fmt(p.at), days2: p.days2, days3: p.days3, options: { ...p.o, keep: [...p.keep] } };
    R.busy($('ob-dialog'), now ? 'Offboarding' : W.edit ? 'Saving the offboarding' : 'Scheduling the offboarding', who());
    try {
      const { plans } = await R.send('/api/offboarding', form);
      close();
      const failed = plans.flatMap((pl) => pl.stages.filter((s) => s.state === 'failed').map((s) => [pl, s]));
      if (failed.length) {
        const [pl, s] = failed[0];
        const bad = s.steps.find((x) => x.result?.state === 'failed');
        R.toast(`Stage ${s.n} did not finish for ${pl.name}.`, bad ? `${bad.label}: ${bad.result.detail}` : '', { tone: 'warn' });
      } else if (now) {
        R.toast('Offboarding started.', `${who()} ${users.length === 1 ? 'was' : 'were'} offboarded for stage 1. ${plans[0]?.status ? `Next: ${plans[0].status}.` : ''}`.trim());
      } else {
        R.toast(W.edit ? 'Offboarding updated.' : 'Offboarding scheduled.', `${who()} will be offboarded ${whenWords(p.at)}.`);
      }
      changedNow(users);
    } catch (err) {
      $('ob-problem').textContent = err.message;
      $('ob-problem').hidden = false;
    } finally {
      R.busy($('ob-dialog'), '');
    }
  };

  // The window's clicks and changes, wired once.
  const setQuick = (k) => {
    const p = W.p;
    p.quick = k;
    const d = quickPicks().find(([x]) => x === k)?.[2];
    if (k === 'now') p.at = null;
    else if (d) p.at = d;
    else if (!p.at) { const n = parse(W.look.now); p.at = at5(n.getFullYear(), n.getMonth(), n.getDate() + 1); }
    if (p.at) p.cal = { y: p.at.getFullYear(), m: p.at.getMonth() };
  };
  const quickOf = () => quickPicks().find(([, , d]) => d && W.p.at && d.getTime() === W.p.at.getTime())?.[0] || 'pick';
  $('ob-dialog').addEventListener('click', (e) => {
    if (!W.p) { if (e.target.closest('#ob-cancel')) close(); return; }
    let b;
    if ((b = e.target.closest('[data-ob-quick]'))) { setQuick(b.dataset.obQuick); draw(true); return; }
    if ((b = e.target.closest('[data-ob-day]'))) {
      const p = W.p;
      const [h, m] = p.at ? [p.at.getHours(), p.at.getMinutes()] : [17, 0];
      p.at = new Date(p.cal.y, p.cal.m, Number(b.dataset.obDay), h, m);
      p.quick = quickOf();
      draw(true);
      return;
    }
    if ((b = e.target.closest('[data-ob-cal]'))) { const m = W.p.cal.m + Number(b.dataset.obCal); W.p.cal = { y: W.p.cal.y + Math.floor(m / 12), m: ((m % 12) + 12) % 12 }; draw(true); return; }
    if ((b = e.target.closest('[data-ob-days]'))) { const [f, v] = b.dataset.obDays.split(':'); W.p[f] = Number(v); draw(true); return; }
    if ((b = e.target.closest('[data-ob-go]'))) { go(Number(b.dataset.obGo)); return; }
    if (e.target.closest('#ob-cancel')) { close(); return; }
    if (e.target.closest('#ob-back')) { go(Math.max(0, W.i - 1)); return; }
    if (e.target.closest('#ob-next')) { if (W.i < 4) go(W.i + 1); else submit(); }
  });
  $('ob-dialog').addEventListener('change', (e) => {
    if (!W.p) return;
    const t = e.target;
    const o = W.p.o;
    if (t.dataset.obStep) { o[t.dataset.obStep] = t.checked; draw(true); return; }
    if (t.dataset.obKeep) { if (t.checked) W.p.keep.delete(t.dataset.obKeep); else W.p.keep.add(t.dataset.obKeep); draw(true); return; }
    if (t.matches('[data-ob-time]')) {
      const [h, m] = t.value.split(':').map(Number);
      const b = W.p.at || parse(W.look.now);
      W.p.at = new Date(b.getFullYear(), b.getMonth(), b.getDate(), h, m);
      W.p.quick = quickOf();
      draw(true);
      return;
    }
    const f = t.dataset.obField;
    if (f === 'reportsTo') { o.reportsTo = t.value; o.reportsToName = t.value ? t.selectedOptions[0].textContent : ''; draw(true); }
    if (f === 'moveTo') { o.moveTo = t.value; o.moveToName = t.value ? t.selectedOptions[0].textContent : ''; draw(true); }
  });
  $('ob-dialog').addEventListener('input', (e) => {
    const f = W.p && e.target.dataset.obField;
    if (f === 'noteText' || f === 'archiveTo') {
      W.p.o[f] = e.target.value;
      $('ob-ruler').innerHTML = ruler();
    }
  });
  $('ob-dialog').addEventListener('close', () => { W.open = false; });

  R.offboard = { open, load, cell, sortOf, status: (u) => brief(u)?.status || '', has: (u) => !!brief(u), active, pane, plan, onChange: (f) => { redraw = f; } };
})();
