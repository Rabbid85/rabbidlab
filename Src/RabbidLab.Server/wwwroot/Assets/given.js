// What Rabbid Lab gave a person (Given.cs): the Given tab on a user's panel. Three lists: what Rabbid Lab gave and they
// still have, what they have in Active Directory that it has no record of giving, and what was taken back. Offboarding
// takes away the first two.
(() => {
  const R = window.RabbidLab;
  const { esc, svg } = R;
  const got = new Map(); // by username: { at, entries, error }
  const lower = (s) => String(s).toLowerCase();
  const ICON = { account: 'user', group: 'users' };
  const SYSTEM = { ad: 'Active Directory' };

  // Read as the panel opens, and again when it's drawn after a few seconds: a change made in the panel shows straight away.
  const load = (x, again) => {
    const k = lower(x.username);
    const had = got.get(k);
    if (had && Date.now() - had.at < 4000) return;
    got.set(k, { ...had, at: Date.now() });
    R.get(`/api/ad/users/${encodeURIComponent(x.username)}/given`).then((j) => {
      const same = JSON.stringify(j.entries) === JSON.stringify(got.get(k)?.entries);
      got.set(k, { at: Date.now(), entries: j.entries });
      if (!same) again();
    }).catch((err) => {
      if (had?.entries) return; // what was read stays
      got.set(k, { at: Date.now(), error: err.message });
      again();
    });
  };
  // How many things they have from Rabbid Lab, for the tab.
  const count = (x, again) => {
    load(x, again);
    const entries = got.get(lower(x.username))?.entries;
    return entries ? entries.filter((e) => !e.tookAt).length : null;
  };
  const head = (title, n, right = '') => `<div class="dw-sec-head"><h3>${esc(title)}${n == null ? '' : ` <small>${n}</small>`}</h3>${right}</div>`;
  const row = (icon, bold, small, cls = '') => `<div class="mrow${cls}"><span class="pic small">${svg(icon)}</span><div class="two"><b>${esc(bold)}</b><small>${esc(small)}</small></div></div>`;
  const pane = (x) => {
    const g = got.get(lower(x.username));
    const title = `Given by ${R.app()}`;
    if (g?.error) return `<section class="dw-sec">${head(title)}<p class="hint problem">${esc(g.error)}</p></section>`;
    if (!g?.entries) return `<section class="dw-sec">${head(title)}<div data-skel="rows:3"></div></section>`;
    const has = g.entries.filter((e) => !e.tookAt);
    const took = g.entries.filter((e) => e.tookAt);
    // Groups they are in that no change made here put them in: added in another tool, or before Rabbid Lab.
    const found = (x.groups || []).filter((gr) => !has.some((e) => e.kind === 'group' && lower(e.key) === lower(gr.dn)));
    const offboard = R.offboard && R.has('ad.enable') && x.enabled && !x.system && !R.offboard.active(x.username)
      ? `<span class="dw-sec-do"><button type="button" class="small" data-given-offboard="${esc(x.username)}">${svg('user-off')}Offboard…</button></span>` : '';
    return `<section class="dw-sec">${head(title, has.length, offboard)}`
      + (has.length ? `<div class="dw-list">${has.map((e) => row(ICON[e.kind] || 'key', e.what, `${SYSTEM[e.system] || e.system} · ${e.by}, ${R.when(e.at)} · ${e.how}`)).join('')}</div>`
        : `<p class="muted dw-empty">${esc(`${R.app()} has not given ${x.name || x.username} anything yet. What it creates and the groups it adds are recorded here.`)}</p>`)
      + '</section>'
      + (found.length ? `<section class="dw-sec">${head(`Found, not given by ${R.app()}`, found.length)}<p class="hint given-lead">${esc(`${x.name || x.username} is in these groups, and ${R.app()} has no record of adding them. Offboarding removes these too.`)}</p>`
        + `<div class="dw-list">${found.map((gr) => row('users', `Group ${gr.name}`, 'Active Directory · Added in another tool, or before this was recorded')).join('')}</div></section>` : '')
      + (took.length ? `<section class="dw-sec">${head('Taken back', took.length)}<div class="dw-list">${took.map((e) => row(ICON[e.kind] || 'key', e.what, `Taken back ${R.when(e.tookAt)} by ${e.tookBy} · ${e.tookHow}`, ' given-gone')).join('')}</div></section>` : '');
  };
  document.addEventListener('click', (e) => {
    const b = e.target.closest('[data-given-offboard]');
    if (b) R.offboard.open([b.dataset.givenOffboard]);
  });

  R.given = { count, pane };
})();
